// ===========================================================================
// Program.cs — Robot Brawl ladder API v0 (design doc §8, Phase M1).
//
// WHAT THIS SERVICE IS NOT ALLOWED TO DO, and why that shapes every endpoint:
//
//   * It never parses a build or a program (§5.2). Uploads are opaque bytes
//     with a hash. The Unity worker — running the ACTUAL game code — decides
//     whether a robot is legal, what it weighs and which category it belongs
//     in. One implementation, zero drift, and a hacked client cannot upload an
//     illegal robot because the same code that refuses it in the builder
//     refuses it here.
//   * It never computes a fight outcome (§5.5). Results arrive from a worker
//     that holds the job, or they do not arrive.
//   * It never leaks a program to anyone but its owner (§1.3). Scouting
//     metadata is public; the payload URL is not. That is an authz test, not
//     a comment.
// ===========================================================================

using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using RobotBrawl.Api;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// A legal snapshot is small — the v2 program caps (≤12 hats, ≤64 blocks) and
// the build format bound it. 256 KB is generous by ~10x and still refuses an
// upload that could tie up a worker.
const int MaxSnapshotBytes = 256 * 1024;

var connString = cfg.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("PG_CONN")
    ?? "Host=localhost;Username=rb;Password=rb;Database=rb";
var jwtSecret = cfg["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException("JWT_SECRET is required — refusing to boot with a default signing key");
var workerKey = cfg["Worker:Key"] ?? Environment.GetEnvironmentVariable("WORKER_KEY")
    ?? throw new InvalidOperationException("WORKER_KEY is required — refusing to boot with an open worker endpoint");

var sqlDir = Path.Combine(AppContext.BaseDirectory, "Sql");
var migrationsDir = Path.Combine(AppContext.BaseDirectory, "Migrations");

var db = new Db(connString);
var tokens = new Tokens(jwtSecret, TimeSpan.FromHours(cfg.GetValue("Jwt:Hours", 12)));

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(new JobQueue(db, sqlDir));
builder.Services.AddSingleton<IBlobStore>(
    new FileBlobStore(cfg["Storage:Root"] ?? Environment.GetEnvironmentVariable("BLOB_ROOT") ?? "/var/lib/robotbrawl/blobs"));
builder.Services.AddHostedService<ReaperService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // MapInboundClaims defaults to TRUE, which rewrites the token's own
        // claim names into the WS-Federation URIs — "sub" becomes
        // ".../identity/claims/nameidentifier". Every read in this file asks
        // for "sub" by its real name, so the default turns UserId() into
        // Guid.Parse(null) and every authenticated endpoint into a bare 500.
        // Off means the claims arrive named the way the token named them.
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,   ValidIssuer = "robotbrawl",
            ValidateAudience = true, ValidAudience = "robotbrawl",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(tokens.Key),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// §5.5: rate limits per user and per IP on the upload and challenge paths.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("upload", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

await db.MigrateAsync(migrationsDir, app.Logger);

// --------------------------------------------------------------- helpers
static Guid UserId(ClaimsPrincipal u) => Guid.Parse(u.FindFirstValue("sub")!);
static IResult Bad(string msg) => Results.BadRequest(new { error = msg });

bool WorkerAuthed(HttpContext ctx) =>
    ctx.Request.Headers.TryGetValue("X-Worker-Key", out var k) &&
    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        System.Text.Encoding.UTF8.GetBytes(k.ToString()),
        System.Text.Encoding.UTF8.GetBytes(workerKey));

// §2.3: "All constants live in one server-side table and are tunable without
// a client update." Read per request rather than cached at boot — the whole
// point is that owen can change a dial without a deploy, and a cache would
// quietly reinstate the deploy requirement. It is one small indexed table.
async Task<Dictionary<string,int>> LadderConfig(NpgsqlConnection c, NpgsqlTransaction? tx = null)
{
    var d = new Dictionary<string,int>();
    await using var cmd = new NpgsqlCommand("SELECT key, value FROM ladder_config;", c, tx);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) d[r.GetString(0)] = r.GetInt32(1);
    return d;
}

// The five weight classes, mirroring 001_init.sql's CHECK on
// snapshots.category and ratings.category. Kept here rather than inferred
// from a failed INSERT so the API can refuse a bad value with a readable
// reason instead of letting Postgres raise 23514 from inside an UPDATE.
// If this list and the migration ever disagree, sql_bench catches the
// migration and api_smoke catches this one.
string[] Categories = { "FEATHER", "LIGHT", "MIDDLE", "HEAVY", "SUPER" };

/// <summary>Null if the result is storable; otherwise the reason it is not,
/// phrased for a worker author reading a 400.</summary>
string? ValidateResultShape(ValidateResult r)
{
    // NULL is legal in the schema and means "no category". The empty string
    // is NOT — it is the JsonUtility null-string trap, and it is the whole
    // reason this check exists.
    if (r.Category != null && Array.IndexOf(Categories, r.Category) < 0)
        return r.Category.Length == 0
            ? "category was the empty string; send a bare JSON null for \"no category\" "
            + "(Unity's JsonUtility serialises a null string as \"\" — hand-build this field)"
            : $"category '{r.Category}' is not one of {string.Join(", ", Categories)}";

    // ratings.category is NOT NULL, so a legal snapshot with no category can
    // never be placed on a ladder — it would be ACTIVE and unrateable. Refuse
    // it here rather than storing a row nothing downstream can use.
    if (r.Legal && r.Category == null)
        return "a legal robot must carry a weight category; "
             + "ratings.category is NOT NULL, so a legal snapshot without one can never be rated";

    return null;
}

// ---------------------------------------------------------------- health
app.MapGet("/healthz", async () =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand("SELECT 1;", c);
    await cmd.ExecuteScalarAsync();
    return Results.Ok(new { ok = true, version = 1 });
});

// ------------------------------------------------------------------ auth
app.MapPost("/v1/auth/register", async (RegisterReq req) =>
{
    if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@')) return Bad("a real email is required");
    if ((req.Password ?? "").Length < 10) return Bad("password must be at least 10 characters");
    var name = (req.DisplayName ?? "").Trim();
    if (name.Length is < 2 or > 24) return Bad("display name must be 2-24 characters");

    await using var c = await db.OpenAsync();
    try
    {
        // The account and its wallet commit together or not at all. §2.3 makes
        // the balance SUM(ledger.delta) over an append-only table, so a torn
        // write here is not a transient glitch — it is a permanently wrong
        // balance whose only repair is another row.
        await using var tx = await c.BeginTransactionAsync();
        Guid id;
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO users (email, pw_hash, display_name) VALUES ($1,$2,$3) RETURNING id;", c, tx))
        {
            cmd.Parameters.AddWithValue(req.Email.Trim());
            cmd.Parameters.AddWithValue(Passwords.Hash(req.Password!));
            cmd.Parameters.AddWithValue(name);
            id = (Guid)(await cmd.ExecuteScalarAsync())!;
        }
        // §2.3: a fresh wallet gets the signing bonus, in the same breath as
        // the account, so "registered" and "can afford a challenge" are the
        // same state.
        await using (var l = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, idem_key) VALUES ($1,$2,'SIGNING_BONUS',$3);", c, tx))
        {
            l.Parameters.AddWithValue(id);
            l.Parameters.AddWithValue(500);
            l.Parameters.AddWithValue("signup:" + id);
            await l.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return Results.Ok(new { token = tokens.Issue(id, req.Email!), userId = id });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "that email is already registered" });
    }
}).RequireRateLimiting("auth");

app.MapPost("/v1/auth/login", async (LoginReq req) =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT id, pw_hash, email FROM users WHERE email_lower = lower($1);", c);
    cmd.Parameters.AddWithValue(req.Email ?? "");
    await using var r = await cmd.ExecuteReaderAsync();
    // Same response and roughly the same work whether the account exists or
    // the password is wrong — an endpoint that distinguishes them is an
    // account-enumeration oracle.
    if (!await r.ReadAsync())
    {
        Passwords.Verify(req.Password ?? "", Passwords.Hash("decoy-decoy"));
        return Results.Unauthorized();
    }
    var id = r.GetGuid(0); var hash = r.GetString(1); var email = r.GetString(2);
    if (!Passwords.Verify(req.Password ?? "", hash)) return Results.Unauthorized();
    return Results.Ok(new { token = tokens.Issue(id, email), userId = id });
}).RequireRateLimiting("auth");

// ---------------------------------------------------------------- robots
app.MapPost("/v1/robots", async (RobotReq req, ClaimsPrincipal user) =>
{
    var name = (req.Name ?? "").Trim();
    if (name.Length is < 1 or > 32) return Bad("robot name must be 1-32 characters");
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "INSERT INTO robots (user_id, name) VALUES ($1,$2) RETURNING id, created_at;", c);
    cmd.Parameters.AddWithValue(UserId(user));
    cmd.Parameters.AddWithValue(name);
    await using var r = await cmd.ExecuteReaderAsync();
    await r.ReadAsync();
    return Results.Ok(new { id = r.GetGuid(0), name, createdAt = r.GetDateTime(1) });
}).RequireAuthorization();

app.MapGet("/v1/robots", async (ClaimsPrincipal user) =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT id, name, created_at, retired FROM robots WHERE user_id = $1 ORDER BY created_at;", c);
    cmd.Parameters.AddWithValue(UserId(user));
    var list = new List<object>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        list.Add(new { id = r.GetGuid(0), name = r.GetString(1), createdAt = r.GetDateTime(2), retired = r.GetBoolean(3) });
    return Results.Ok(list);
}).RequireAuthorization();

// ------------------------------------------------------------- snapshots
app.MapPost("/v1/snapshots", async (SnapshotReq req, ClaimsPrincipal user, IBlobStore blobs) =>
{
    if (req.RobotId == Guid.Empty) return Bad("robotId is required");
    if (string.IsNullOrEmpty(req.Envelope)) return Bad("envelope is required");
    var bytes = System.Text.Encoding.UTF8.GetBytes(req.Envelope);
    if (bytes.Length > MaxSnapshotBytes)
        return Bad($"snapshot is {bytes.Length} bytes; the limit is {MaxSnapshotBytes}");

    // The API reads exactly three fields out of the envelope and understands
    // none of them: the hash it will store, the client version, and nothing
    // else. The payload stays opaque (§5.2).
    string sha, clientVersion;
    try
    {
        using var doc = JsonDocument.Parse(req.Envelope);
        sha = (doc.RootElement.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
        clientVersion = doc.RootElement.GetProperty("clientVersion").GetString() ?? "";
        var payload = doc.RootElement.GetProperty("payload").GetString() ?? "";
        // Cheap integrity gate so a truncated upload fails here rather than
        // wasting a worker. NOT a legality check — that is the worker's job.
        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        if (actual != sha) return Bad("sha256 does not match the payload — upload was altered or truncated");
    }
    catch (Exception) { return Bad("envelope is not a readable snapshot envelope"); }

    await using var c = await db.OpenAsync();
    await using (var own = new NpgsqlCommand("SELECT 1 FROM robots WHERE id=$1 AND user_id=$2;", c))
    {
        own.Parameters.AddWithValue(req.RobotId);
        own.Parameters.AddWithValue(UserId(user));
        if (await own.ExecuteScalarAsync() is null) return Results.NotFound(new { error = "no such robot" });
    }

    var key = $"snapshots/{req.RobotId}/{Guid.NewGuid():N}.json";
    var url = await blobs.PutAsync(key, bytes);

    await using var tx = await c.BeginTransactionAsync();
    Guid snapId;
    await using (var ins = new NpgsqlCommand(
        "INSERT INTO snapshots (robot_id, storage_url, sha256, client_version) VALUES ($1,$2,$3,$4) RETURNING id;", c, tx))
    {
        ins.Parameters.AddWithValue(req.RobotId);
        ins.Parameters.AddWithValue(url);
        ins.Parameters.AddWithValue(sha);
        ins.Parameters.AddWithValue(clientVersion);
        snapId = (Guid)(await ins.ExecuteScalarAsync())!;
    }
    // The validate job and the row it describes commit together: a snapshot
    // that exists is a snapshot something is going to look at.
    await using (var job = new NpgsqlCommand(
        "INSERT INTO match_jobs (kind, snapshot_id) VALUES ('VALIDATE', $1);", c, tx))
    {
        job.Parameters.AddWithValue(snapId);
        await job.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();
    return Results.Ok(new { id = snapId, status = "PENDING" });
}).RequireAuthorization().RequireRateLimiting("upload");

app.MapGet("/v1/snapshots/{id:guid}", async (Guid id, ClaimsPrincipal user) =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(@"
        SELECT s.id, s.status, s.mass_kg, s.aabb_x, s.aabb_y, s.aabb_z, s.category,
               s.parts_manifest, s.program_hash, s.fail_reasons, s.uploaded_at,
               r.user_id, r.name
          FROM snapshots s JOIN robots r ON r.id = s.robot_id
         WHERE s.id = $1;", c);
    cmd.Parameters.AddWithValue(id);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NotFound();

    var owner = r.GetGuid(11);
    var mine = user.Identity?.IsAuthenticated == true && UserId(user) == owner;

    // §1.3: this is the scouting card. Everything here is public by design —
    // mass, size box, category, part manifest, record. The program is not
    // here, and neither is the payload URL, for the owner OR anyone else:
    // the only thing that ever reads a payload is a worker.
    return Results.Ok(new
    {
        id = r.GetGuid(0),
        status = r.GetString(1),
        robotName = r.GetString(12),
        massKg = r.IsDBNull(2) ? (int?)null : r.GetInt32(2),
        aabb = r.IsDBNull(3) ? null : new { x = r.GetFloat(3), y = r.GetFloat(4), z = r.GetFloat(5) },
        category = r.IsDBNull(6) ? null : r.GetString(6),
        partsManifest = r.IsDBNull(7) ? null : r.GetFieldValue<string>(7),
        programHash = r.IsDBNull(8) ? null : r.GetString(8),
        hasProgram = !r.IsDBNull(8) && r.GetString(8).Length > 0,
        // Only the owner is told WHY their own upload was rejected. Handing a
        // scout the validator's reasons is handing them the build.
        failReasons = mine && !r.IsDBNull(9) ? r.GetFieldValue<string[]>(9) : null,
        uploadedAt = r.GetDateTime(10),
        mine,
    });
}).AllowAnonymous();

// ----------------------------------------------------------- worker path
// §5.5: worker→API calls are authenticated with a worker key. Ownership of a
// job is checked in SQL (claimed_by), so a stolen key still cannot steal
// another worker's job result.
app.MapPost("/v1/worker/jobs/claim", async (HttpContext ctx, JobQueue q, ClaimReq req) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.WorkerId)) return Bad("workerId is required");
    var job = await q.ClaimAsync(req.WorkerId!);
    return job is null ? Results.NoContent() : Results.Ok(job);
}).AllowAnonymous();

app.MapPost("/v1/worker/jobs/{id:long}/heartbeat", async (long id, HttpContext ctx, JobQueue q, ClaimReq req) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    return await q.HeartbeatAsync(id, req.WorkerId ?? "") ? Results.Ok() : Results.NotFound();
}).AllowAnonymous();

/// Validate-job result (§5.2): the worker ran the real game code against the
/// payload and is telling us what it found. The API stores it and does not
/// second-guess it.
app.MapPost("/v1/worker/jobs/{id:long}/validate-result",
    async (long id, HttpContext ctx, JobQueue q, ValidateResult res) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();

    // 2026-08-09. Check the result BEFORE writing it. snapshots.category is
    // TEXT CHECK (category IS NULL OR category IN (…)), and until now a value
    // the CHECK forbids reached the UPDATE, raised an unhandled 23514 and
    // returned 500 — WITHOUT completing the job, so the reaper handed it back
    // and the next worker failed identically, forever. A malformed result
    // livelocked a worker slot instead of failing loudly.
    //
    // "" is not hypothetical: Unity's JsonUtility serialises a null string as
    // "", so it is exactly what a worker written the obvious way emits. The
    // shipped worker hand-builds its JSON to send a bare null, but the API
    // must not depend on one client being careful.
    string? badResult = ValidateResultShape(res);
    if (badResult != null)
    {
        // Retire the job rather than leaving it to spin. Not READY: the
        // payload is deterministic, so a retry buys nothing. If the job was
        // already reclaimed this returns false and we say so instead —
        // a late malformed result must not knock over someone else's work.
        bool wasOurs = await q.FailAsync(id, res.WorkerId ?? "", badResult);
        if (!wasOurs)
            return Results.Conflict(new { error = "this job is no longer yours — it was reclaimed" });
        return Results.BadRequest(new { error = badResult, jobStatus = "FAILED" });
    }

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    // §1.2: one ACTIVE snapshot per robot. The old one is stood down BEFORE the
    // new one is raised: snapshots_one_active_per_robot is a plain partial
    // unique index, so it is checked per statement rather than at COMMIT, and
    // the other order raises 23505 on a robot's second upload.
    if (res.Legal)
    {
        await using var sup = new NpgsqlCommand(@"
            UPDATE snapshots SET status = 'SUPERSEDED'
             WHERE robot_id = (SELECT robot_id FROM snapshots WHERE id = $1)
               AND id <> $1 AND status = 'ACTIVE';", c, tx);
        sup.Parameters.AddWithValue(res.SnapshotId);
        await sup.ExecuteNonQueryAsync();
    }
    await using (var up = new NpgsqlCommand(@"
        UPDATE snapshots SET
            status = CASE WHEN $2 THEN 'ACTIVE' ELSE 'REJECTED' END,
            mass_kg = $3, aabb_x = $4, aabb_y = $5, aabb_z = $6,
            category = $7, parts_manifest = $8::jsonb, program_hash = $9,
            fail_reasons = $10, validated_at = now()
          WHERE id = $1;", c, tx))
    {
        up.Parameters.AddWithValue(res.SnapshotId);
        up.Parameters.AddWithValue(res.Legal);
        up.Parameters.AddWithValue(res.MassKg);
        up.Parameters.AddWithValue(res.AabbX);
        up.Parameters.AddWithValue(res.AabbY);
        up.Parameters.AddWithValue(res.AabbZ);
        up.Parameters.AddWithValue((object?)res.Category ?? DBNull.Value);
        up.Parameters.AddWithValue(JsonSerializer.Serialize(res.PartsManifest ?? new List<string>()));
        up.Parameters.AddWithValue((object?)res.ProgramHash ?? DBNull.Value);
        up.Parameters.AddWithValue(res.FailReasons ?? Array.Empty<string>());
        await up.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();

    return await q.CompleteAsync(id, res.WorkerId ?? "") ? Results.Ok() : Results.Conflict(
        new { error = "this job is no longer yours — it was reclaimed" });
}).AllowAnonymous();

// ================================================================== fights
// §5.3, the lifecycle the design doc calls "the one flow that must be
// airtight". Three endpoints: create the match, upload the replay, post the
// result. M1 scope — see docs/Fight_Contract_2026-08-09.md for what is
// deliberately deferred to M2 (Glicko-2 deltas, taper, tickets).

// §5.3 step 1. Every check, the escrow debit, the match row and the job are
// one transaction: a stake debited without a match is money destroyed, and a
// match without a stake is money created.
app.MapPost("/v1/challenges", async (ChallengeReq req, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    if (req.ChallengerSnapshotId == req.DefenderSnapshotId)
        return Bad("a robot cannot challenge itself");

    await using var c = await db.OpenAsync();

    // Both sides in one query so the two reads cannot straddle a change.
    var sides = new Dictionary<Guid, (string Status, string? Category, Guid RobotId, Guid UserId)>();
    await using (var look = new NpgsqlCommand(@"
        SELECT s.id, s.status, s.category, r.id, r.user_id
          FROM snapshots s JOIN robots r ON r.id = s.robot_id
         WHERE s.id = ANY($1);", c))
    {
        look.Parameters.AddWithValue(new[] { req.ChallengerSnapshotId, req.DefenderSnapshotId });
        await using var r = await look.ExecuteReaderAsync();
        while (await r.ReadAsync())
            sides[r.GetGuid(0)] = (r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                                   r.GetGuid(3), r.GetGuid(4));
    }
    if (!sides.TryGetValue(req.ChallengerSnapshotId, out var ch)) return Results.NotFound(new { error = "no such challenger snapshot" });
    if (!sides.TryGetValue(req.DefenderSnapshotId,   out var df)) return Results.NotFound(new { error = "no such defender snapshot" });

    if (ch.UserId != me)          return Results.Forbid();
    if (ch.RobotId == df.RobotId) return Bad("a robot cannot challenge itself");
    // §1.2: only an ACTIVE snapshot is on the ladder. PENDING has not been
    // judged and REJECTED was judged and failed.
    if (ch.Status != "ACTIVE")    return Bad($"your snapshot is {ch.Status}, not ACTIVE");
    if (df.Status != "ACTIVE")    return Bad($"their snapshot is {df.Status}, not ACTIVE");
    if (ch.Category == null || df.Category == null)
        return Bad("both robots need a weight category to meet on the ladder");

    // §1.2 category legality: you may punch UP, never down. gap is how many
    // classes up, and it prices both the stake and the purse.
    int ci = Array.IndexOf(Categories, ch.Category), di = Array.IndexOf(Categories, df.Category);
    if (ci < 0 || di < 0) return Bad("unknown weight category on one of the snapshots");
    int gap = di - ci;
    if (gap < 0) return Bad($"a {ch.Category} cannot punch down to a {df.Category}");

    var cfg = await LadderConfig(c);
    int stake = cfg["stake_base"] * (1 + gap);

    // §2.3: the balance IS the sum of the ledger. There is no cached column
    // to disagree with it.
    long balance;
    await using (var bal = new NpgsqlCommand(
        "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id = $1;", c))
    {
        bal.Parameters.AddWithValue(me);
        balance = Convert.ToInt64(await bal.ExecuteScalarAsync());
    }
    if (balance < stake)
        return Bad($"this challenge stakes {stake} scrap and your wallet holds {balance}");

    // Best-of-3 (§1.4). Seeds are server-chosen: a client-chosen seed is a
    // client choosing its own fight.
    var seeds = new[] { Random.Shared.Next(1, int.MaxValue),
                        Random.Shared.Next(1, int.MaxValue),
                        Random.Shared.Next(1, int.MaxValue) };

    await using var tx = await c.BeginTransactionAsync();
    Guid matchId;
    await using (var ins = new NpgsqlCommand(@"
        INSERT INTO matches (challenger_snapshot_id, defender_snapshot_id, category, gap, arena, seeds, status)
        VALUES ($1,$2,$3,$4,'league',$5,'QUEUED') RETURNING id;", c, tx))
    {
        ins.Parameters.AddWithValue(req.ChallengerSnapshotId);
        ins.Parameters.AddWithValue(req.DefenderSnapshotId);
        ins.Parameters.AddWithValue(df.Category);   // you fight in THEIR class
        ins.Parameters.AddWithValue(gap);
        ins.Parameters.AddWithValue(seeds);
        matchId = (Guid)(await ins.ExecuteScalarAsync())!;
    }
    await using (var deb = new NpgsqlCommand(
        "INSERT INTO ledger (user_id, delta, reason, match_id) VALUES ($1,$2,'STAKE',$3);", c, tx))
    {
        deb.Parameters.AddWithValue(me);
        deb.Parameters.AddWithValue(-stake);   // into escrow; settled in step 3
        deb.Parameters.AddWithValue(matchId);
        await deb.ExecuteNonQueryAsync();
    }
    await using (var job = new NpgsqlCommand(
        "INSERT INTO match_jobs (kind, match_id) VALUES ('FIGHT', $1);", c, tx))
    {
        job.Parameters.AddWithValue(matchId);
        await job.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();

    return Results.Ok(new { matchId, status = "QUEUED", stake, gap, category = df.Category });
}).RequireAuthorization().RequireRateLimiting("upload");

// §5.3 step 2's upload half. The replay is a RECORDING of the worker's run
// (§5.4), so it is opaque bytes to the API exactly like a snapshot payload.
app.MapPost("/v1/worker/matches/{matchId:guid}/replay",
    async (Guid matchId, HttpContext ctx, ReplayReq req, IBlobStore blobs) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if (string.IsNullOrEmpty(req.Replay)) return Bad("empty replay");
    var bytes = System.Text.Encoding.UTF8.GetBytes(req.Replay);
    var url = await blobs.PutAsync($"replays/{matchId}/{Guid.NewGuid():N}.json", bytes);
    return Results.Ok(new { url, bytes = bytes.Length });
}).AllowAnonymous();

// §5.3 step 3. Verify ownership -> write result -> settle escrow -> mark
// COMPLETE, in ONE transaction, including retiring the job: the validate
// livelock (docs/Validate_Result_Livelock_Fixed) was exactly the failure of
// leaving job completion outside the write.
app.MapPost("/v1/worker/jobs/{id:long}/fight-result",
    async (long id, HttpContext ctx, FightResult res) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if (res.Verdict is not ("CHALLENGER" or "DEFENDER" or "DRAW"))
        return Bad("verdict must be CHALLENGER, DEFENDER or DRAW");

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    // Job ownership, and the row is locked for the rest of the transaction so
    // two workers cannot settle the same match twice.
    Guid jobMatch;
    await using (var own = new NpgsqlCommand(@"
        SELECT match_id FROM match_jobs
         WHERE id = $1 AND claimed_by = $2 AND status = 'CLAIMED' AND kind = 'FIGHT'
           FOR UPDATE;", c, tx))
    {
        own.Parameters.AddWithValue(id);
        own.Parameters.AddWithValue(res.WorkerId ?? "");
        var got = await own.ExecuteScalarAsync();
        if (got is null)
            return Results.Conflict(new { error = "this job is no longer yours — it was reclaimed" });
        jobMatch = (Guid)got;
    }
    // The worker must be reporting on the match it was actually given.
    if (jobMatch != res.MatchId)
        return Bad("this result is for a different match than the job holds");

    // Everything settlement needs, read under the same transaction.
    Guid chUser, dfUser; int gap; string status;
    await using (var m = new NpgsqlCommand(@"
        SELECT m.status, m.gap, rc.user_id, rd.user_id
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
         WHERE m.id = $1 FOR UPDATE OF m;", c, tx))
    {
        m.Parameters.AddWithValue(res.MatchId);
        await using var r = await m.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Results.NotFound(new { error = "no such match" });
        status = r.GetString(0); gap = r.GetInt32(1); chUser = r.GetGuid(2); dfUser = r.GetGuid(3);
    }
    // Settling twice would mint scrap from nothing.
    if (status == "COMPLETE") return Results.Conflict(new { error = "this match is already settled" });

    var cfg = await LadderConfig(c, tx);
    int stake = cfg["stake_base"] * (1 + gap);

    await using (var up = new NpgsqlCommand(@"
        UPDATE matches SET status = 'COMPLETE', verdict = $2, replay_urls = $3, completed_at = now()
         WHERE id = $1;", c, tx))
    {
        up.Parameters.AddWithValue(res.MatchId);
        up.Parameters.AddWithValue(res.Verdict!);
        up.Parameters.AddWithValue((object?)res.ReplayUrls ?? Array.Empty<string>());
        await up.ExecuteNonQueryAsync();
    }

    // §2.3's table, and the sums must conserve scrap: the challenger's stake
    // already left the wallet in step 1, so a win returns it AND pays the
    // purse, a draw returns it, and a loss keeps it.
    async Task Credit(Guid who, int delta, string reason)
    {
        await using var l = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, match_id) VALUES ($1,$2,$3,$4);", c, tx);
        l.Parameters.AddWithValue(who); l.Parameters.AddWithValue(delta);
        l.Parameters.AddWithValue(reason); l.Parameters.AddWithValue(res.MatchId);
        await l.ExecuteNonQueryAsync();
    }
    if (res.Verdict == "CHALLENGER")
    {
        // (1 + 0.5*gap)^2 — punching up two categories pays 4x base.
        double mult = Math.Pow(1 + 0.5 * gap, 2);
        await Credit(chUser, stake, "STAKE_REFUND");
        await Credit(chUser, (int)Math.Round(cfg["win_purse_base"] * mult), "PURSE");
    }
    else if (res.Verdict == "DEFENDER")
    {
        await Credit(dfUser, cfg["defense_purse"], "DEFENSE");   // challenger's stake is forfeit
    }
    else
    {
        await Credit(chUser, stake, "STAKE_REFUND");
    }

    // Retire the job in the SAME transaction as the settlement.
    await using (var done = new NpgsqlCommand(@"
        UPDATE match_jobs SET status = 'DONE', claimed_by = NULL, claimed_at = NULL, heartbeat_at = NULL
         WHERE id = $1;", c, tx))
    {
        done.Parameters.AddWithValue(id);
        await done.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    return Results.Ok(new { matchId = res.MatchId, status = "COMPLETE", verdict = res.Verdict });
}).AllowAnonymous();

app.Run();

// ------------------------------------------------------------------ dtos
public record RegisterReq(string? Email, string? Password, string? DisplayName);
public record LoginReq(string? Email, string? Password);
public record RobotReq(string? Name);
public record SnapshotReq(Guid RobotId, string? Envelope);
public record ClaimReq(string? WorkerId);
public record ChallengeReq(Guid ChallengerSnapshotId, Guid DefenderSnapshotId);
public record ReplayReq(string? Replay);
public record FightResult(Guid MatchId, string? WorkerId, string? Verdict,
                          string[]? ReplayUrls, string[]? Bouts);
public record ValidateResult(
    Guid SnapshotId, string? WorkerId, bool Legal, int MassKg,
    float AabbX, float AabbY, float AabbZ, string? Category,
    List<string>? PartsManifest, string? ProgramHash, string[]? FailReasons);
