// ===========================================================================
// Infra.cs — database, password hashing, JWT, blob storage, job queue.
//
// Deliberately small and dependency-light: Npgsql + the in-box JWT handler,
// raw SQL, no ORM. §7's portability rules mean the data layer has to stay
// plain Postgres, and raw SQL is the only way to be sure of that. It also
// keeps the SKIP LOCKED queue honest — the query in Sql/claim_job.sql is the
// query that runs, and it is the same text sql_bench.sh proves.
// ===========================================================================

using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace RobotBrawl.Api;

// --------------------------------------------------------------------- db
public sealed class Db
{
    readonly string _cs;
    public Db(string connectionString) { _cs = connectionString; }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var c = new NpgsqlConnection(_cs);
        await c.OpenAsync(ct);
        return c;
    }

    /// <summary>Apply any migration in Migrations/ we have not applied yet.
    /// Runs at boot: a container that starts is a container with the right
    /// schema, so there is no separate deploy step to forget.</summary>
    public async Task MigrateAsync(string dir, ILogger log)
    {
        await using var c = await OpenAsync();
        await using (var probe = new NpgsqlCommand(
            "CREATE TABLE IF NOT EXISTS schema_version (version INT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT now());", c))
            await probe.ExecuteNonQueryAsync();

        var applied = new HashSet<int>();
        await using (var cmd = new NpgsqlCommand("SELECT version FROM schema_version;", c))
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) applied.Add(r.GetInt32(0));

        foreach (var path in Directory.GetFiles(dir, "*.sql").OrderBy(p => p))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var num = int.Parse(name.Split('_')[0]);
            if (applied.Contains(num)) continue;
            log.LogInformation("applying migration {Name}", name);
            await using var m = new NpgsqlCommand(await File.ReadAllTextAsync(path), c);
            await m.ExecuteNonQueryAsync();
        }
    }
}

// --------------------------------------------------------------- passwords
public static class Passwords
{
    // PBKDF2-HMAC-SHA256. In-box, no dependency, and the iteration count is a
    // stored parameter so raising it later does not invalidate old hashes.
    const int Iterations = 210_000, SaltBytes = 16, KeyBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var p = stored.Split('$');
        if (p.Length != 4 || p[0] != "pbkdf2") return false;
        if (!int.TryParse(p[1], out var iters)) return false;
        var salt = Convert.FromBase64String(p[2]);
        var expect = Convert.FromBase64String(p[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iters, HashAlgorithmName.SHA256, expect.Length);
        // Constant time: a timing oracle on password compare is free to exploit.
        return CryptographicOperations.FixedTimeEquals(actual, expect);
    }
}

// -------------------------------------------------------------------- jwt
public sealed class Tokens
{
    readonly byte[] _key;
    readonly TimeSpan _life;
    public Tokens(string secret, TimeSpan life)
    {
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new ArgumentException("JWT secret must be at least 32 bytes");
        _key = Encoding.UTF8.GetBytes(secret);
        _life = life;
    }

    public byte[] Key => _key;
    public TimeSpan Life => _life;

    public string Issue(Guid userId, string email)
    {
        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(_key),
            Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: "robotbrawl",
            audience: "robotbrawl",
            claims: new[]
            {
                new System.Security.Claims.Claim("sub", userId.ToString()),
                new System.Security.Claims.Claim("email", email),
            },
            expires: DateTime.UtcNow.Add(_life),
            signingCredentials: creds);
        return handler.WriteToken(token);
    }
}

// ---------------------------------------------------------------- storage
/// <summary>§5.1 wants snapshot payloads and replays in object storage, and
/// §7 rule 3 wants that reached through the S3-compatible interoperability
/// API so the exit door stays open. This interface is that seam. The file
/// implementation is what runs locally and on a single VM; an S3 one drops
/// in without touching a caller.</summary>
public interface IBlobStore
{
    Task<string> PutAsync(string key, byte[] bytes, CancellationToken ct = default);
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);
}

public sealed class FileBlobStore : IBlobStore
{
    readonly string _root;
    public FileBlobStore(string root) { _root = root; Directory.CreateDirectory(root); }

    string PathFor(string key)
    {
        // Keys are server-generated (see SnapshotKey), but a store that can be
        // walked out of with "../" is a store that will be, so this is belt
        // and braces rather than trust.
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal))
            throw new ArgumentException("blob key escapes the store root");
        return full;
    }

    public async Task<string> PutAsync(string key, byte[] bytes, CancellationToken ct = default)
    {
        var p = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        await File.WriteAllBytesAsync(p, bytes, ct);
        return "file://" + p;
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        var p = PathFor(key);
        return File.Exists(p) ? await File.ReadAllBytesAsync(p, ct) : null;
    }
}

// ------------------------------------------------------------------ queue
/// <summary>What a worker is handed when it claims a job. The last eight
/// fields were added 2026-08-09: without them a worker received a snapshot
/// UUID and had no way to resolve it to bytes, so the queue could be claimed
/// from but never worked. See Sql/claim_job.sql for the full account.
///
/// Exactly one side is populated, enforced by the match_jobs CHECK: a
/// VALIDATE job fills Payload*, a FIGHT job fills Challenger*/Defender* plus
/// Arena and Seeds.</summary>
public sealed record ClaimedJob(
    long Id, string Kind, Guid? MatchId, Guid? SnapshotId, int Attempts,
    string? PayloadUrl, string? PayloadSha256,
    string? ChallengerUrl, string? ChallengerSha256,
    string? DefenderUrl, string? DefenderSha256,
    string? Arena, int[]? Seeds);

/// <summary>The Postgres-as-queue half of §5.1. Every statement is loaded
/// from Sql/*.sql rather than inlined, because those files are what
/// tests/sql_bench.sh proves — inlining the text here would create a second
/// copy that can drift from the one under test.</summary>
public sealed class JobQueue
{
    readonly Db _db;
    readonly string _claim, _heartbeat, _complete, _reap;

    public JobQueue(Db db, string sqlDir)
    {
        _db = db;
        _claim     = File.ReadAllText(Path.Combine(sqlDir, "claim_job.sql"));
        _heartbeat = File.ReadAllText(Path.Combine(sqlDir, "heartbeat_job.sql"));
        _complete  = File.ReadAllText(Path.Combine(sqlDir, "complete_job.sql"));
        _reap      = File.ReadAllText(Path.Combine(sqlDir, "reap_stale_jobs.sql"));
    }

    public async Task<ClaimedJob?> ClaimAsync(string workerId, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(_claim, c);
        cmd.Parameters.AddWithValue(workerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ClaimedJob(
            r.GetInt64(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetGuid(2),
            r.IsDBNull(3) ? null : r.GetGuid(3),
            r.GetInt32(4),
            r.IsDBNull(5)  ? null : r.GetString(5),
            r.IsDBNull(6)  ? null : r.GetString(6),
            r.IsDBNull(7)  ? null : r.GetString(7),
            r.IsDBNull(8)  ? null : r.GetString(8),
            r.IsDBNull(9)  ? null : r.GetString(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetFieldValue<int[]>(12));
    }

    public async Task<bool> HeartbeatAsync(long jobId, string workerId, CancellationToken ct = default)
        => await OneRow(_heartbeat, jobId, workerId, ct);

    public async Task<bool> CompleteAsync(long jobId, string workerId, CancellationToken ct = default)
        => await OneRow(_complete, jobId, workerId, ct);

    async Task<bool> OneRow(string sql, long jobId, string workerId, CancellationToken ct)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, c);
        cmd.Parameters.AddWithValue(jobId);
        cmd.Parameters.AddWithValue(workerId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct);
    }

    /// <summary>Returns jobs to the queue whose worker went quiet, and fails
    /// the ones that have used up their attempts.</summary>
    public async Task<int> ReapAsync(int staleSeconds, int maxAttempts, CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(_reap, c);
        cmd.Parameters.AddWithValue(staleSeconds);
        cmd.Parameters.AddWithValue(maxAttempts);
        int n = 0;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) n++;
        return n;
    }
}

/// <summary>§5.3's "a job unclaimed or unheartbeated for 5 min returns to the
/// queue". Runs in the API rather than as a separate cron container: it is
/// one query on a timer, and a second deployable to forget is worse than a
/// loop.</summary>
public sealed class ReaperService : BackgroundService
{
    readonly JobQueue _q; readonly ILogger<ReaperService> _log; readonly IConfiguration _cfg;
    public ReaperService(JobQueue q, ILogger<ReaperService> log, IConfiguration cfg)
    { _q = q; _log = log; _cfg = cfg; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        int stale = _cfg.GetValue("Queue:StaleSeconds", 300);
        int maxAtt = _cfg.GetValue("Queue:MaxAttempts", 3);
        var every = TimeSpan.FromSeconds(_cfg.GetValue("Queue:ReapEverySeconds", 30));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                int n = await _q.ReapAsync(stale, maxAtt, ct);
                if (n > 0) _log.LogWarning("reaped {Count} stale job(s)", n);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex, "reaper pass failed");
            }
            try { await Task.Delay(every, ct); } catch (OperationCanceledException) { }
        }
    }
}
