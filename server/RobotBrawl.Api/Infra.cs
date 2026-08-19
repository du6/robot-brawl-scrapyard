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
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

/// <summary>Object storage over the S3 API — which is GCS, S3, R2 or a local
/// Minio without a line changing. §7 rule 3 asks for exactly this ("the
/// S3-compatible interoperability API so the exit door stays open"), and the
/// interface above was written anticipating it.
///
/// WHY THIS EXISTS, 2026-08-09: FileBlobStore was the only implementation and
/// the API had just gone live on Cloud Run, where the container filesystem is
/// EPHEMERAL AND PER-INSTANCE. With min-instances=0 an uploaded payload
/// disappeared the moment the service scaled down and its VALIDATE job could
/// never be worked. The upload answered 200 throughout, which is exactly what
/// made it dangerous.
///
/// Against GCS this needs an HMAC key, because the S3 interop API predates
/// workload identity. That is a real trade — a long-lived secret instead of
/// an ambient one — accepted because §7 rule 3 puts portability first and the
/// key lives in Secret Manager with everything else.</summary>
public sealed class S3BlobStore : IBlobStore
{
    readonly Amazon.S3.IAmazonS3 _s3;
    readonly string _bucket;

    public S3BlobStore(string endpoint, string bucket, string accessKey, string secretKey)
    {
        _bucket = bucket;
        _s3 = new Amazon.S3.AmazonS3Client(accessKey, secretKey, new Amazon.S3.AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,   // GCS interop is path-style only; S3 tolerates it
        });
    }

    /// <summary>Returns an s3:// URI, NOT a fetchable URL. What a caller may
    /// fetch is decided at READ time by whoever is asking — see /v1/blobs.
    /// Storing a fetchable URL is how expired links end up baked into
    /// database rows.</summary>
    public async Task<string> PutAsync(string key, byte[] bytes, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(bytes);
        await _s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = ms,
            // Without this the SDK streams with a chunked signature GCS
            // rejects, and the failure is a 501 that explains nothing.
            UseChunkEncoding = false,
        }, ct);
        return $"s3://{_bucket}/{key}";
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var r = await _s3.GetObjectAsync(_bucket, key, ct);
            using var ms = new MemoryStream();
            await r.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (Amazon.S3.AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;   // a missing blob is an answer, not an exception
        }
    }
}

/// <summary>Turns a stored blob URI back into the key its store understands.
/// Both forms exist in the wild: file:// rows written before object storage,
/// s3:// after. A reader has to cope with both.</summary>
public static class BlobUri
{
    public static bool IsObjectStore(string? uri) =>
        uri != null && uri.StartsWith("s3://", StringComparison.Ordinal);

    public static string? KeyOf(string? uri)
    {
        if (!IsObjectStore(uri)) return null;
        var rest = uri!.Substring(5);
        int slash = rest.IndexOf('/');
        return slash < 0 ? null : rest.Substring(slash + 1);
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

/// <summary>Queue health, read on a timer by the reaper and served by
/// GET /v1/admin/metrics. The two *AgeS fields are the ones worth alerting
/// on; the counts are context for whoever the alert wakes.</summary>
public sealed record QueueStats(
    int Ready, int Claimed, int Failed, int Done,
    int OldestReadyAgeS, int OldestHeartbeatAgeS,
    int PendingSnapshots, int OldestPendingSnapshotAgeS);

/// <summary>The Postgres-as-queue half of §5.1. Every statement is loaded
/// from Sql/*.sql rather than inlined, because those files are what
/// tests/sql_bench.sh proves — inlining the text here would create a second
/// copy that can drift from the one under test.</summary>
public sealed class JobQueue
{
    readonly Db _db;
    readonly string _claim, _heartbeat, _complete, _fail, _reap, _stats;

    public JobQueue(Db db, string sqlDir)
    {
        _db = db;
        _claim     = File.ReadAllText(Path.Combine(sqlDir, "claim_job.sql"));
        _heartbeat = File.ReadAllText(Path.Combine(sqlDir, "heartbeat_job.sql"));
        _complete  = File.ReadAllText(Path.Combine(sqlDir, "complete_job.sql"));
        _fail      = File.ReadAllText(Path.Combine(sqlDir, "fail_job.sql"));
        _reap      = File.ReadAllText(Path.Combine(sqlDir, "reap_stale_jobs.sql"));
        _stats     = File.ReadAllText(Path.Combine(sqlDir, "queue_stats.sql"));
    }

    /// <summary>The numbers §M4 alerts on. See queue_stats.sql for why these
    /// four and not the obvious counts.</summary>
    public async Task<QueueStats> StatsAsync(CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(_stats, c);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return new QueueStats(0, 0, 0, 0, 0, 0, 0, 0);
        return new QueueStats(
            (int)r.GetInt64(0), (int)r.GetInt64(1), (int)r.GetInt64(2), (int)r.GetInt64(3),
            r.GetInt32(4), r.GetInt32(5), (int)r.GetInt64(6), r.GetInt32(7));
    }

    /// <summary>Claim one job. <paramref name="kind"/> null claims any kind,
    /// which is the old behaviour; passing 'VALIDATE' or 'FIGHT' lets a
    /// specialised worker avoid claiming — and thereby burning an attempt on —
    /// work it cannot do. See claim_job.sql for why that matters.</summary>
    public async Task<ClaimedJob?> ClaimAsync(string workerId, string? kind = null,
                                              CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(_claim, c);
        cmd.Parameters.AddWithValue(workerId);
        cmd.Parameters.AddWithValue((object?)kind ?? DBNull.Value);
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

    /// <summary>Retires a job the worker cannot succeed at. Unlike the reaper's
    /// timeout path this does NOT return the job to the queue: the payload is
    /// deterministic, so a retry produces the same refusal. Returns false if
    /// the job was already reclaimed, exactly like CompleteAsync.</summary>
    public async Task<bool> FailAsync(long jobId, string workerId, string reason,
                                      CancellationToken ct = default)
    {
        await using var c = await _db.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(_fail, c);
        cmd.Parameters.AddWithValue(jobId);
        cmd.Parameters.AddWithValue(workerId);
        cmd.Parameters.AddWithValue(reason);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct);
    }

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

                // §M4's queue depth / failure rate / heartbeat age, emitted on
                // the pass we are already making rather than on a second
                // timer. The prefix is fixed and the shape is key=value
                // because Cloud Logging extracts these with a regex; changing
                // either breaks the log-based metrics silently, so it is
                // covered by api_smoke rather than left to trust.
                //
                // HONEST LIMITATION: this only runs while an instance is
                // alive, and min-instances=0 means that is "while there is
                // traffic". A queue that backs up with nothing polling emits
                // nothing at all, and the alert policies treat missing data
                // as INACTIVE — firing on absence would page for an idle
                // ladder every night. What covers the gap is the uptime
                // check in scripts/gcp_monitoring.zsh: its probe wakes an
                // instance every 5 minutes, which runs a pass, which emits
                // this line. The metrics exist because something is polling.
                var s = await _q.StatsAsync(ct);
                _log.LogInformation(
                    "rbmetrics ready={Ready} claimed={Claimed} failed={Failed} done={Done} " +
                    "oldest_ready_s={OldestReady} oldest_heartbeat_s={OldestHeartbeat} " +
                    "pending_snapshots={Pending} oldest_pending_s={OldestPending}",
                    s.Ready, s.Claimed, s.Failed, s.Done,
                    s.OldestReadyAgeS, s.OldestHeartbeatAgeS,
                    s.PendingSnapshots, s.OldestPendingSnapshotAgeS);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogError(ex, "reaper pass failed");
            }
            try { await Task.Delay(every, ct); } catch (OperationCanceledException) { }
        }
    }
}

// ===========================================================================
// WorkerTrigger — start the fight/validate worker the moment there is work.
//
// ⚠ WHY THIS EXISTS: THE WORKER COST TWICE THE PROJECT'S ENTIRE BUDGET TO DO
// NOTHING. Cloud Scheduler started the rb-worker Cloud Run job every five
// minutes, and the job is a HEADLESS UNITY PLAYER — the real game, because
// only the game can referee a fight (§5.2). Measured on a live execution:
//
//     15:02:25  [WorkerHost] up.
//     15:02:36  queue empty for 3 consecutive polls; exiting
//     15:02:46  Container called exit(0).
//        total billed: 126 s at 2 vCPU / 2 GiB
//
// The worker loop ran ELEVEN SECONDS and claimed nothing. The other ~115 s was
// Unity booting and shutting down — physics, input, App UI — purely to find
// out the queue was empty. At 288 ticks a day that is ~2.1M vCPU-seconds a
// month, about $51, against a $25 budget. deploy_worker.zsh's comment that the
// worker "costs nothing while idle" was wrong in the most expensive way: it
// exits promptly, but only AFTER paying a cold boot to learn there is nothing
// to do.
//
// So the queue tells the worker, instead of the worker asking the queue. This
// starts the job when a job is actually enqueued, and never otherwise.
//
// ⚠ IT IS AWAITED, NOT FIRE-AND-FORGET. Cloud Run throttles CPU outside
// request processing unless the instance is configured always-allocated, so
// work started in a discarded Task after the response may simply not run. A
// bounded await costs the caller a few hundred milliseconds on the rare
// request that enqueues something, and actually happens.
//
// Disabled unless RB_WORKER_JOB is set, so local runs, the bench and any
// deployment that has not opted in are unaffected and pay nothing.
// ===========================================================================
public sealed class WorkerTrigger
{
    readonly Db _db;
    readonly string? _job;          // projects/P/locations/L/jobs/rb-worker
    readonly int _debounceSeconds;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public bool Enabled => !string.IsNullOrWhiteSpace(_job);

    public WorkerTrigger(Db db, string? job, int debounceSeconds = 30)
    {
        _db = db;
        _job = string.IsNullOrWhiteSpace(job) ? null : job.Trim();
        _debounceSeconds = Math.Max(0, debounceSeconds);
    }

    /// <summary>Start the worker if nobody has started it very recently.
    /// NEVER THROWS: a missed nudge must not fail the request that enqueued
    /// the work. The job stays in the queue either way, and the safety-net
    /// schedule picks up anything a lost nudge left behind.</summary>
    public async Task NudgeAsync(ILogger log, CancellationToken ct = default)
    {
        if (!Enabled) return;
        try
        {
            if (!await ClaimNudgeAsync(ct)) return;   // someone else just did it
            var token = await MetadataTokenAsync(ct);
            if (token is null) { log.LogWarning("[nudge] no metadata token; worker not started"); return; }

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://run.googleapis.com/v2/{_job}:run");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            var res = await _http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) log.LogInformation("[nudge] worker started");
            else log.LogWarning("[nudge] run.googleapis.com said {Code}: {Body}",
                                (int)res.StatusCode, await res.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[nudge] could not start the worker; the job stays queued");
        }
    }

    /// <summary>⚠ THE DEBOUNCE IS IN THE DATABASE, NOT IN THIS PROCESS. The API
    /// runs up to four Cloud Run instances, so an in-memory timestamp would let
    /// four of them start four Unity players for one burst of enlists — the
    /// exact cost this class exists to remove. One conditional UPSERT decides
    /// it for all of them: whoever updates the row wins, everyone else is told
    /// no. The worker drains the WHOLE queue, so one execution covers a burst
    /// anyway.</summary>
    async Task<bool> ClaimNudgeAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var c = await _db.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO ladder_config (key, value, note)
            VALUES ('worker_nudge_at', $1, 'unix time the worker was last started on demand — WorkerTrigger')
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now()
            WHERE ladder_config.value <= $2
            RETURNING value;", c);
        cmd.Parameters.AddWithValue((int)now);
        cmd.Parameters.AddWithValue((int)(now - _debounceSeconds));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>The instance's own service-account token, from the metadata
    /// server. No key file anywhere: the API's identity IS the credential, and
    /// it needs run.jobs.run on the worker job and nothing else.</summary>
    async Task<string?> MetadataTokenAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/token");
        req.Headers.Add("Metadata-Flavor", "Google");
        var res = await _http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }
}
