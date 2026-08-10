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

// A recorded best-of-3 measured ~200 KB per bout on a real fight, so 8 MB is
// generous by an order of magnitude and still refuses an upload that could
// fill a disk. Bounded on purpose: the worker key is shared, so "trusted"
// only means "not anonymous".
const int MaxReplayBytes = 8 * 1024 * 1024;

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
    // The wallet gets its own bucket rather than sharing "upload". They are
    // different actions with different abuse profiles, and sharing one bucket
    // means a player who has been challenging hard is throttled out of
    // looking at their own winnings. Note this does NOT raise the upload
    // limit — it stops three unrelated operations competing for one budget.
    o.AddPolicy("wallet", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
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
// value is NUMERIC as of migration 003: glicko_tau is 0.5, and storing it as
// an integer number of thousandths would be a unit trap for whoever tunes it.
// Callers that want whole scrap use (int) on the way out.
async Task<Dictionary<string,double>> LadderConfig(NpgsqlConnection c, NpgsqlTransaction? tx = null)
{
    var d = new Dictionary<string,double>();
    await using var cmd = new NpgsqlCommand("SELECT key, value FROM ladder_config;", c, tx);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) d[r.GetString(0)] = (double)r.GetDecimal(1);
    return d;
}

// §2.1: "New snapshot in a category → placement rating 1200, high deviation."
// The placement values are the COLUMN DEFAULTS on `ratings`, not constants
// here — one source of truth, and an INSERT that names no values gets them.
// create:false is for a robot we are about to deliberately NOT rate — the
// defender of a punch-up. Materialising its placement row would put it on the
// leaderboard of a class it never fought in, purely because somebody reached
// up at it, which is the farming surface §2.1 exists to close. "Untouched"
// has to mean no row appears, not just no number changes.
async Task<Rating> LoadRating(NpgsqlConnection c, NpgsqlTransaction tx,
                              Guid robot, string category, int season,
                              bool create = true)
{
    await using (var get = new NpgsqlCommand(
        "SELECT rating, deviation, volatility FROM ratings WHERE robot_id=$1 AND category=$2 AND season_id=$3;", c, tx))
    {
        get.Parameters.AddWithValue(robot);
        get.Parameters.AddWithValue(category);
        get.Parameters.AddWithValue(season);
        await using var r = await get.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return new Rating(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2));
    }
    // Not rating this robot, so do not put it on the board. The placement
    // values are still returned so the punch-up offset has something to
    // compute against — in memory only.
    if (!create) return new Rating(1200, 350, 0.06);

    // First fight in this category: create the placement row from the
    // defaults so the ladder has something to move.
    await using (var ins = new NpgsqlCommand(
        "INSERT INTO ratings (robot_id, category, season_id) VALUES ($1,$2,$3) "
      + "ON CONFLICT DO NOTHING RETURNING rating, deviation, volatility;", c, tx))
    {
        ins.Parameters.AddWithValue(robot);
        ins.Parameters.AddWithValue(category);
        ins.Parameters.AddWithValue(season);
        await using var r = await ins.ExecuteReaderAsync();
        if (await r.ReadAsync())
            return new Rating(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2));
    }
    return new Rating(1200, 350, 0.06);   // unreachable unless the row raced in
}

async Task SaveRating(NpgsqlConnection c, NpgsqlTransaction tx,
                      Guid robot, string category, int season, Rating v)
{
    await using var up = new NpgsqlCommand(@"
        INSERT INTO ratings (robot_id, category, season_id, rating, deviation, volatility, updated_at)
        VALUES ($1,$2,$3,$4,$5,$6, now())
        ON CONFLICT (robot_id, category, season_id) DO UPDATE
           SET rating = EXCLUDED.rating, deviation = EXCLUDED.deviation,
               volatility = EXCLUDED.volatility, updated_at = now();", c, tx);
    up.Parameters.AddWithValue(robot);
    up.Parameters.AddWithValue(category);
    up.Parameters.AddWithValue(season);
    up.Parameters.AddWithValue(v.R);
    up.Parameters.AddWithValue(v.Rd);
    up.Parameters.AddWithValue(v.Sigma);
    await up.ExecuteNonQueryAsync();
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
    // The ACTIVE snapshot comes with it. Without this a client cannot name
    // its own challenger: POST /v1/challenges wants a snapshot id, and there
    // was no endpoint that turned "my robot" into one. Adding it here rather
    // than a second round trip because the ladder browser needs the category
    // anyway, to know which fights are legal before offering them.
    await using var cmd = new NpgsqlCommand(@"
        SELECT r.id, r.name, r.created_at, r.retired, s.id, s.category
          FROM robots r
          LEFT JOIN snapshots s ON s.robot_id = r.id AND s.status = 'ACTIVE'
         WHERE r.user_id = $1 ORDER BY r.created_at;", c);
    cmd.Parameters.AddWithValue(UserId(user));
    var list = new List<object>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        list.Add(new
        {
            id = r.GetGuid(0), name = r.GetString(1),
            createdAt = r.GetDateTime(2), retired = r.GetBoolean(3),
            activeSnapshotId = r.IsDBNull(4) ? (Guid?)null : r.GetGuid(4),
            category = r.IsDBNull(5) ? null : r.GetString(5),
        });
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
        // A REAL JSON array, not a quoted string. parts_manifest is jsonb, and
        // handing it back as GetFieldValue<string> emitted
        // "partsManifest": "[\"beam\",\"wheel\"]" — legal JSON that every
        // client has to unescape and parse a second time. The scouting card
        // read it as zero parts, which is how this was found.
        partsManifest = r.IsDBNull(7)
            ? null
            : JsonSerializer.Deserialize<string[]>(r.GetFieldValue<string>(7)),
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
    int stake = (int)cfg["stake_base"] * (1 + gap);

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

    // §2.2: "each robot may INITIATE 10 challenges per day (tickets,
    // refreshed daily). Defenses are unlimited and free." Inside the
    // transaction, and the spend is the same statement as the check — a
    // separate read-then-write would let two concurrent challenges both see
    // the ninth ticket. The WHERE on the DO UPDATE is what makes the
    // exhausted case return no row rather than a negative balance.
    int ticketCap = (int)(cfg.TryGetValue("challenge_tickets_per_day", out var tc) ? tc : 10);
    bool gotTicket;
    await using (var tk = new NpgsqlCommand(@"
        INSERT INTO tickets (robot_id, day, used) VALUES ($1, CURRENT_DATE, 1)
        ON CONFLICT (robot_id, day) DO UPDATE SET used = tickets.used + 1
              WHERE tickets.used < $2
        RETURNING used;", c, tx))
    {
        tk.Parameters.AddWithValue(ch.RobotId);
        tk.Parameters.AddWithValue(ticketCap);
        gotTicket = await tk.ExecuteScalarAsync() is not null;
    }
    if (!gotTicket)
        return Results.Json(new { error = $"out of challenge tickets — {ticketCap} per robot per day, and defending is always free" },
                            statusCode: StatusCodes.Status429TooManyRequests);

    Guid matchId;
    await using (var ins = new NpgsqlCommand(@"
        INSERT INTO matches (challenger_snapshot_id, defender_snapshot_id, category, gap, arena, seeds, status, stake)
        VALUES ($1,$2,$3,$4,'league',$5,'QUEUED',$6) RETURNING id;", c, tx))
    {
        ins.Parameters.AddWithValue(req.ChallengerSnapshotId);
        ins.Parameters.AddWithValue(req.DefenderSnapshotId);
        ins.Parameters.AddWithValue(df.Category);   // you fight in THEIR class
        ins.Parameters.AddWithValue(gap);
        ins.Parameters.AddWithValue(seeds);
        // What is CHARGED is what gets refunded. See migration 006.
        ins.Parameters.AddWithValue(stake);
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
// Two content types, one endpoint, because there are genuinely two artifacts:
//
//   application/octet-stream  the RECORDING — a .rbr.gz ReplayRecorder wrote.
//                             This is the one a client can actually watch.
//   application/json          a text summary (verdict, bouts). Useful in a
//                             fight report, useless for playback.
//
// 2026-08-09: the worker originally uploaded ONLY the summary, so every
// "replay URL" on every match pointed at a scorecard. Nothing noticed,
// because nothing had ever tried to PLAY one — the gap only appears the
// moment you build the launcher. Binary is not base64'd into the JSON form:
// a real best-of-3 is ~200 KB per bout and base64 would add a third to every
// one of them for nothing.
app.MapPost("/v1/worker/matches/{matchId:guid}/replay",
    async (Guid matchId, HttpContext ctx, IBlobStore blobs) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();

    byte[] bytes; string ext;
    var contentType = ctx.Request.ContentType ?? "";
    if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
    {
        var req = await ctx.Request.ReadFromJsonAsync<ReplayReq>();
        if (string.IsNullOrEmpty(req?.Replay)) return Bad("empty replay");
        bytes = System.Text.Encoding.UTF8.GetBytes(req.Replay);
        ext = "json";
    }
    else
    {
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms);
        bytes = ms.ToArray();
        if (bytes.Length == 0) return Bad("empty replay");
        ext = "rbr.gz";
    }
    if (bytes.Length > MaxReplayBytes)
        return Bad($"replay is {bytes.Length} bytes; the cap is {MaxReplayBytes}");

    var url = await blobs.PutAsync($"replays/{matchId}/{Guid.NewGuid():N}.{ext}", bytes);
    return Results.Ok(new { url, bytes = bytes.Length, kind = ext });
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

    // Everything settlement needs, read under the same transaction. The ROBOT
    // ids come along too: scrap belongs to a user, but a rating belongs to a
    // robot (§2.1 is per-robot, not per-account).
    Guid chUser, dfUser, chRobot, dfRobot; int gap, stakePaid; string status, category;
    await using (var m = new NpgsqlCommand(@"
        SELECT m.status, m.gap, m.category, rc.user_id, rd.user_id, rc.id, rd.id, m.stake
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
         WHERE m.id = $1 FOR UPDATE OF m;", c, tx))
    {
        m.Parameters.AddWithValue(res.MatchId);
        await using var r = await m.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Results.NotFound(new { error = "no such match" });
        status = r.GetString(0); gap = r.GetInt32(1); category = r.GetString(2);
        chUser = r.GetGuid(3); dfUser = r.GetGuid(4);
        chRobot = r.GetGuid(5); dfRobot = r.GetGuid(6);
        stakePaid = r.GetInt32(7);
    }
    // Settling twice would mint scrap from nothing.
    if (status == "COMPLETE") return Results.Conflict(new { error = "this match is already settled" });

    var cfg = await LadderConfig(c, tx);
    // What was CHARGED, not what the config says today. A league night
    // charges nothing, and a tuned stake_base must not change a refund for a
    // challenge that was already priced. Migration 006.
    int stake = stakePaid;

    // §2.2 repeat-opponent taper: "rating and scrap gains vs the same opponent
    // taper to zero after 3 wins in a rolling 24 h (kills win-trading and
    // griefing in one rule)."
    //
    // READ AS: the first N wins in the window pay in full; the N+1th and
    // beyond pay nothing. The alternative reading is a graded taper
    // (full, 2/3, 1/3, 0) — "taper" invites it — but §2.2 says "to zero AFTER
    // 3 wins", which is a cliff, and a cliff is the version a player can
    // actually reason about. Recorded here because it is a judgement call.
    //
    // GAINS only: a loss still costs full price. Zeroing losses too would
    // make the 4th rematch a free roll.
    //
    // Counted from `matches`, not a counter column: a derived rule with its
    // own tally is a rule that can disagree with the history it derives from.
    int taperAfter = (int)(cfg.TryGetValue("repeat_win_taper_after", out var ta) ? ta : 3);
    int priorWins = 0;
    await using (var pw = new NpgsqlCommand(@"
        SELECT count(*) FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id
         WHERE m.status = 'COMPLETE' AND m.verdict = 'CHALLENGER'
           AND m.completed_at > now() - interval '24 hours'
           AND sc.robot_id = $1 AND sd.robot_id = $2;", c, tx))
    {
        pw.Parameters.AddWithValue(chRobot);
        pw.Parameters.AddWithValue(dfRobot);
        priorWins = Convert.ToInt32(await pw.ExecuteScalarAsync());
    }
    bool tapered = res.Verdict == "CHALLENGER" && priorWins >= taperAfter;

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
        // ledger_delta_nonzero: a zero-value row is not a transaction, it is
        // noise in an audit trail. The schema refuses one, and rightly.
        if (delta == 0) return;
        await using var l = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, match_id) VALUES ($1,$2,$3,$4);", c, tx);
        l.Parameters.AddWithValue(who); l.Parameters.AddWithValue(delta);
        l.Parameters.AddWithValue(reason); l.Parameters.AddWithValue(res.MatchId);
        await l.ExecuteNonQueryAsync();
    }
    // NO STAKE, NO SETTLEMENT. A league night is server-initiated: nobody
    // chose the fight and nobody paid for it, so there is nothing to return
    // and nothing to win. Paying a purse here would be a faucet attached to
    // content the server generates on a timer, which is exactly the
    // one-way door §2.3 warns about. The ladder still moves - rating is the
    // reward for a league night, and scrap can be added later if it needs it.
    bool escrowed = stakePaid > 0;
    if (!escrowed)
    {
        // nothing to settle
    }
    else if (res.Verdict == "CHALLENGER")
    {
        // The stake always comes back — it is escrow, not a fee, and §2.2
        // tapers GAINS. Confiscating the stake on a tapered win would be a
        // penalty the spec never asks for.
        await Credit(chUser, stake, "STAKE_REFUND");
        if (!tapered)
        {
            // (1 + 0.5*gap)^2 — punching up two categories pays 4x base.
            double mult = Math.Pow(1 + 0.5 * gap, 2);
            await Credit(chUser, (int)Math.Round(cfg["win_purse_base"] * mult), "PURSE");
        }
    }
    else if (res.Verdict == "DEFENDER")
    {
        await Credit(dfUser, (int)cfg["defense_purse"], "DEFENSE");   // challenger's stake is forfeit
    }
    else
    {
        await Credit(chUser, stake, "STAKE_REFUND");
    }

    // ---- §2.1: the ladder actually moves -------------------------------
    // In the same transaction as the money: a rating that survived a rollback
    // of its own match is a rank nobody can explain.
    //
    // §2.1 says "win = 1, loss = 0; no draws — judges decide", so a DRAW
    // applies no rating change at all. That also covers the worker's refusal
    // path, which posts DRAW when no fight happened — nobody should climb for
    // a match that never ran.
    if (res.Verdict != "DRAW")
    {
        double tau = cfg.TryGetValue("glicko_tau", out var t) ? t : 0.5;
        int offset = cfg.TryGetValue("cross_category_offset", out var o) ? (int)o : 150;
        int season = cfg.TryGetValue("current_season", out var s) ? (int)s : 1;

        var chR = await LoadRating(c, tx, chRobot, category, season);
        // The defender only gets a row when it is actually being rated, i.e.
        // a same-category fight. See LoadRating's note.
        var dfR = await LoadRating(c, tx, dfRobot, category, season, create: gap == 0);
        double chScore = res.Verdict == "CHALLENGER" ? 1.0 : 0.0;

        // The challenger is rated against a defender who is treated as
        // heavier the further up the challenger reached. Beating a heavy is
        // worth more and losing to one costs almost nothing — the
        // expected-score curve does that on its own once the offset is in.
        var oppForChallenger = dfR with { R = dfR.R + offset * gap };
        var chNew = Glicko2.UpdateOne(chR, oppForChallenger, chScore, tau);

        // §2.2 taper zeroes the challenger's GAIN, not its loss.
        if (tapered) chNew = chR;

        // §2.1: "The defender's rating is untouched by cross-category
        // fights." Heavies must not farm rating by squashing lightweights,
        // nor lose their rank to a swarm of speculative punch-ups. Only a
        // same-category fight moves the defender.
        Rating? dfNew = gap == 0
            ? Glicko2.UpdateOne(dfR, chR, 1.0 - chScore, tau)
            : null;

        // §2.2 defense loss floor: "a defender's rating cannot drop more than
        // 75 points per day from defenses. Excess challenges still pay out
        // scrap to winners but apply zero rating delta to the defender."
        //
        // Summed from what actually happened — each completed match this robot
        // DEFENDED today carries its own before/after in rating_deltas. A
        // separate daily counter would be a second source of truth for a
        // number the match history already holds.
        //
        // ⚠ The floor protects an EARNED rank, not a placement guess. See
        // migration 005: at placement deviation one loss is worth 162 points
        // against a 75/day allowance, so applying the floor there leaves a
        // new robot stranded above its true rating and misleads everyone who
        // challenges it. Deviation at or above the threshold means the rating
        // is provisional — the same threshold the leaderboard shows — and a
        // provisional rating takes its full natural movement.
        bool floorHit = false, floorProtected = false;
        double droppedToday = 0;
        double floorBelowRd = cfg.TryGetValue("floor_applies_below_deviation", out var fr) ? fr : 200;
        floorProtected = dfR.Rd < floorBelowRd;
        if (floorProtected && dfNew is Rating cand && cand.R < dfR.R)
        {
            double floor = cfg.TryGetValue("defense_daily_floor", out var fl) ? fl : 75;
            await using (var dd = new NpgsqlCommand(@"
                SELECT COALESCE(SUM(GREATEST(
                         (m.rating_deltas->'defender'->>'before')::float8
                       - (m.rating_deltas->'defender'->>'after')::float8, 0)), 0)
                  FROM matches m
                  JOIN snapshots sd ON sd.id = m.defender_snapshot_id
                 WHERE m.status = 'COMPLETE' AND sd.robot_id = $1
                   AND m.rating_deltas->'defender' IS NOT NULL
                   AND m.completed_at >= date_trunc('day', now());", c, tx))
            {
                dd.Parameters.AddWithValue(dfRobot);
                droppedToday = Convert.ToDouble(await dd.ExecuteScalarAsync());
            }
            double allowed = Math.Max(0, floor - droppedToday);
            double wanted = dfR.R - cand.R;
            if (wanted > allowed)
            {
                // Clamp rather than skip: "cannot drop more than 75 points per
                // day" is a bound on the total, and once the bound is spent
                // `allowed` is 0, which is the spec's "zero rating delta".
                // Deviation and volatility still move — the defender did play
                // a game, and freezing RD would make an actively-defended
                // robot look idle.
                dfNew = cand with { R = dfR.R - allowed };
                floorHit = true;
            }
        }

        await SaveRating(c, tx, chRobot, category, season, chNew);
        if (dfNew is Rating dn) await SaveRating(c, tx, dfRobot, category, season, dn);

        var deltas = JsonSerializer.Serialize(new
        {
            challenger = new { before = chR.R, after = chNew.R, rd = chNew.Rd, sigma = chNew.Sigma },
            defender = dfNew is Rating d2
                ? new { before = dfR.R, after = d2.R, rd = d2.Rd, sigma = d2.Sigma }
                : null,
            gap,
            defenderUnrated = gap > 0,
            // Shown honestly in the fight report, per §2.2 — a player whose
            // win paid nothing is owed the reason.
            tapered,
            priorWins,
            defenderFloorReached = floorHit,
            defenderDroppedToday = droppedToday,
            // A provisional defender is deliberately unprotected. Say so, or
            // a player watching their new robot fall 160 points will assume
            // the floor is broken.
            defenderFloorProtected = floorProtected,
            defenderProvisional = !floorProtected,
        });
        await using var rd = new NpgsqlCommand(
            "UPDATE matches SET rating_deltas = $2::jsonb WHERE id = $1;", c, tx);
        rd.Parameters.AddWithValue(res.MatchId);
        rd.Parameters.AddWithValue(deltas);
        await rd.ExecuteNonQueryAsync();
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

// ============================================================ the ladder
// M2's read side. Everything above WRITES the ladder; until now nothing let
// anyone SEE it — ratings were computed, protected and stored, and no client
// could ask for them.
//
// All three are anonymous by design. §1.3: "design public, code private" —
// a scout may see standings, cards and replays without an account, and the
// program payload is not reachable from any of them.

// Leaderboard within a category (§2.1: "ranked within category"). Omit the
// category for the global pound-for-pound board, which §2.1 marks display
// only, no mechanics.
app.MapGet("/v1/leaderboard/{category?}", async (string? category, int? limit) =>
{
    if (category != null && Array.IndexOf(Categories, category) < 0)
        return Bad($"'{category}' is not one of {string.Join(", ", Categories)}");
    int take = Math.Clamp(limit ?? 50, 1, 200);

    await using var c = await db.OpenAsync();
    // Rank by rating, but surface deviation too: a 1400 at RD 350 has not
    // earned the same claim as a 1400 at RD 60, and a board that hides that
    // is a board that lies about its own confidence.
    await using var cmd = new NpgsqlCommand(@"
        SELECT ra.category, ra.rating, ra.deviation, ra.volatility, ra.updated_at,
               r.id, r.name, u.display_name,
               (SELECT s.id FROM snapshots s
                 WHERE s.robot_id = r.id AND s.status = 'ACTIVE' LIMIT 1)
          FROM ratings ra
          JOIN robots r ON r.id = ra.robot_id
          JOIN users  u ON u.id = r.user_id
         WHERE ($1::text IS NULL OR ra.category = $1)
         ORDER BY ra.rating DESC, ra.deviation ASC
         LIMIT $2;", c);
    cmd.Parameters.AddWithValue((object?)category ?? DBNull.Value);
    cmd.Parameters.AddWithValue(take);

    var rows = new List<object>();
    await using var r = await cmd.ExecuteReaderAsync();
    int rank = 0;
    while (await r.ReadAsync())
        rows.Add(new
        {
            rank = ++rank,
            category = r.GetString(0),
            rating = Math.Round(r.GetDouble(1), 1),
            deviation = Math.Round(r.GetDouble(2), 1),
            // A robot that has not fought recently is soft, not wrong. Say so
            // rather than letting a stale number look authoritative.
            provisional = r.GetDouble(2) > 200,
            robotId = r.GetGuid(5),
            robotName = r.GetString(6),
            owner = r.GetString(7),
            activeSnapshotId = r.IsDBNull(8) ? (Guid?)null : r.GetGuid(8),
            updatedAt = r.GetDateTime(4),
        });
    return Results.Ok(new { category = category ?? "ALL", count = rows.Count, entries = rows });
}).AllowAnonymous();

// A single match, public. M2's acceptance requires that "a third account can
// scout both and watch the replay but cannot fetch either program payload" —
// so the replay URL is here for everyone, and the payload URL is nowhere.
app.MapGet("/v1/matches/{id:guid}", async (Guid id) =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(@"
        SELECT m.id, m.status, m.verdict, m.category, m.gap, m.arena, m.replay_urls,
               m.rating_deltas, m.created_at, m.completed_at,
               m.challenger_snapshot_id, m.defender_snapshot_id,
               rc.name, rd.name, uc.display_name, ud.display_name
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
          JOIN users uc ON uc.id = rc.user_id JOIN users ud ON ud.id = rd.user_id
         WHERE m.id = $1;", c);
    cmd.Parameters.AddWithValue(id);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NotFound();
    return Results.Ok(new
    {
        id = r.GetGuid(0),
        status = r.GetString(1),
        verdict = r.IsDBNull(2) ? null : r.GetString(2),
        category = r.GetString(3),
        gap = r.GetInt32(4),
        arena = r.GetString(5),
        replayUrls = r.IsDBNull(6) ? Array.Empty<string>() : r.GetFieldValue<string[]>(6),
        // The fight report §2.2 asks to be shown honestly: tapered, floor
        // reached, defender unrated on a punch-up.
        ratingDeltas = r.IsDBNull(7) ? null : r.GetFieldValue<string>(7),
        challenger = new { snapshotId = r.GetGuid(10), robotName = r.GetString(12), owner = r.GetString(14) },
        defender   = new { snapshotId = r.GetGuid(11), robotName = r.GetString(13), owner = r.GetString(15) },
        createdAt = r.GetDateTime(8),
        completedAt = r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9),
    });
}).AllowAnonymous();

// §5.3 step 4: "Both players' fight inboxes show the result; replay is live."
// Authenticated, and scoped to matches this account was actually in — an
// inbox that shows everyone's fights is a feed, not an inbox.
app.MapGet("/v1/inbox", async (ClaimsPrincipal user, int? limit) =>
{
    var me = UserId(user);
    int take = Math.Clamp(limit ?? 25, 1, 100);
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(@"
        SELECT m.id, m.status, m.verdict, m.category, m.gap, m.replay_urls, m.rating_deltas,
               m.created_at, m.completed_at,
               rc.user_id = $1 AS i_challenged,
               rc.name, rd.name
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
         WHERE rc.user_id = $1 OR rd.user_id = $1
         ORDER BY COALESCE(m.completed_at, m.created_at) DESC
         LIMIT $2;", c);
    cmd.Parameters.AddWithValue(me);
    cmd.Parameters.AddWithValue(take);

    var rows = new List<object>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        bool iChallenged = r.GetBoolean(9);
        string? verdict = r.IsDBNull(2) ? null : r.GetString(2);
        // Say who won from THIS account's point of view. Making every client
        // re-derive "was I the challenger" from a verdict enum is how two
        // clients end up disagreeing about who won.
        string outcome = verdict is null ? "PENDING"
            : verdict == "DRAW" ? "DRAW"
            : (verdict == "CHALLENGER") == iChallenged ? "WON" : "LOST";
        rows.Add(new
        {
            matchId = r.GetGuid(0),
            status = r.GetString(1),
            outcome,
            role = iChallenged ? "CHALLENGER" : "DEFENDER",
            opponent = iChallenged ? r.GetString(11) : r.GetString(10),
            myRobot = iChallenged ? r.GetString(10) : r.GetString(11),
            category = r.GetString(3),
            gap = r.GetInt32(4),
            replayUrls = r.IsDBNull(5) ? Array.Empty<string>() : r.GetFieldValue<string[]>(5),
            ratingDeltas = r.IsDBNull(6) ? null : r.GetFieldValue<string>(6),
            createdAt = r.GetDateTime(7),
            completedAt = r.IsDBNull(8) ? (DateTime?)null : r.GetDateTime(8),
        });
    }
    return Results.Ok(new { count = rows.Count, matches = rows });
}).RequireAuthorization();

// The wallet, so a client can show a balance before a stake confirm (§2.3:
// the balance IS the sum of the ledger; there is no cached column).
app.MapGet("/v1/wallet", async (ClaimsPrincipal user) =>
{
    var me = UserId(user);
    await using var c = await db.OpenAsync();
    long balance;
    await using (var bal = new NpgsqlCommand(
        "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id = $1;", c))
    {
        bal.Parameters.AddWithValue(me);
        balance = Convert.ToInt64(await bal.ExecuteScalarAsync());
    }
    var rows = new List<object>();
    await using (var led = new NpgsqlCommand(
        "SELECT delta, reason, match_id, created_at FROM ledger WHERE user_id=$1 ORDER BY id DESC LIMIT 50;", c))
    {
        led.Parameters.AddWithValue(me);
        await using var r = await led.ExecuteReaderAsync();
        while (await r.ReadAsync())
            rows.Add(new
            {
                delta = r.GetInt32(0),
                reason = r.GetString(1),
                matchId = r.IsDBNull(2) ? (Guid?)null : r.GetGuid(2),
                at = r.GetDateTime(3),
            });
    }
    return Results.Ok(new { balance, recent = rows });
}).RequireAuthorization();

// §2.3's one-way valve: wallet scrap may move INTO the local career save and
// can never come back. That direction is what makes a hacked save worthless
// online — local scrap can never enter the ladder.
//
// Two rules are enforced by the DATABASE, not here, and deliberately so:
//   * ledger_deposit_is_withdrawal — a DEPOSIT_TO_CAREER row can only be
//     negative, so no code path can turn this into a faucet.
//   * ledger_idem_key (unique, partial) — the replay is refused by Postgres
//     rather than by an application check somebody can skip.
// This endpoint's job is to not undermine either.
app.MapPost("/v1/wallet/deposit", async (DepositReq req, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    if (req.Amount <= 0) return Bad("a deposit must be a positive amount of scrap");
    if (string.IsNullOrWhiteSpace(req.IdemKey))
        return Bad("an idempotency key is required — without one a dropped response cannot be retried safely");

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    // The balance IS the ledger (§2.3). Read it inside the transaction so a
    // concurrent stake cannot be spent twice over.
    long balance;
    await using (var bal = new NpgsqlCommand(
        "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id = $1;", c, tx))
    {
        bal.Parameters.AddWithValue(me);
        balance = Convert.ToInt64(await bal.ExecuteScalarAsync());
    }
    if (balance < req.Amount)
        return Bad($"your wallet holds {balance} scrap and this deposit is {req.Amount}");

    try
    {
        await using var ins = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, idem_key) VALUES ($1,$2,'DEPOSIT_TO_CAREER',$3);", c, tx);
        ins.Parameters.AddWithValue(me);
        ins.Parameters.AddWithValue(-req.Amount);
        ins.Parameters.AddWithValue(req.IdemKey!);
        await ins.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        // §8/M3: "a deposited balance replayed from a tampered client is
        // rejected". Rejected, not silently re-accepted — but the original
        // row comes back with it, so an HONEST client that merely lost its
        // response can reconcile instead of guessing.
        await tx.RollbackAsync();
        await using var prev = new NpgsqlCommand(
            "SELECT -delta, created_at FROM ledger WHERE idem_key = $1;", c);
        prev.Parameters.AddWithValue(req.IdemKey!);
        await using var pr = await prev.ExecuteReaderAsync();
        object? already = await pr.ReadAsync()
            ? new { amount = pr.GetInt32(0), at = pr.GetDateTime(1) } : null;
        return Results.Conflict(new
        {
            error = "this deposit was already applied — the idempotency key has been used",
            alreadyDeposited = already,
        });
    }

    return Results.Ok(new { deposited = req.Amount, balance = balance - req.Amount });
}).RequireAuthorization().RequireRateLimiting("wallet");

// ========================================================= league nights
// §M3: "Scheduled league nights (server-initiated round-robin among top 8 per
// category, weekly) — passive content that makes the ladder move even when
// nobody challenges, and produces featured replays for the ARENA tab's front
// page."
//
// Server-initiated, so THREE of the challenge rules deliberately do not
// apply, and each omission is a decision rather than an oversight:
//
//   * No stake. Nobody chose this fight, so nobody pays for it. The match
//     records stake = 0 and settlement refunds exactly that, which is why
//     migration 006 had to land first — a recomputed stake would have minted
//     a refund against a debit that never happened.
//   * No tickets. §2.2 caps challenges a robot INITIATES; a robot did not
//     initiate this, the league did.
//   * No purse. §2.3 says the faucets start conservative and inflating career
//     progression from the ladder is a one-way door. Rating movement is the
//     content here; scrap can be added later if it turns out to need it.
//
// What DOES apply is everything about rating: same-category pairs, so gap 0,
// both sides updated, the defense floor and the repeat taper both live.
app.MapPost("/v1/admin/league-night/{category}", async (string category, HttpContext ctx, int? top) =>
{
    // Worker-key gated, not player-authenticated: this is an operator action.
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if (Array.IndexOf(Categories, category) < 0)
        return Bad($"'{category}' is not one of {string.Join(", ", Categories)}");
    int n = Math.Clamp(top ?? 8, 2, 16);

    await using var c = await db.OpenAsync();
    int season = 1;
    {
        var cfg0 = await LadderConfig(c);
        if (cfg0.TryGetValue("current_season", out var sv)) season = (int)sv;
    }

    // The top N by rating who still have an ACTIVE snapshot to fight with. A
    // rating with no active snapshot is a robot that has been superseded or
    // retired; inviting it would queue a match nothing can run.
    var field = new List<(Guid Robot, Guid Snap, double Rating)>();
    await using (var q = new NpgsqlCommand(@"
        SELECT ra.robot_id, s.id, ra.rating
          FROM ratings ra
          JOIN snapshots s ON s.robot_id = ra.robot_id AND s.status = 'ACTIVE'
         WHERE ra.category = $1 AND ra.season_id = $2
         ORDER BY ra.rating DESC
         LIMIT $3;", c))
    {
        q.Parameters.AddWithValue(category);
        q.Parameters.AddWithValue(season);
        q.Parameters.AddWithValue(n);
        await using var r = await q.ExecuteReaderAsync();
        while (await r.ReadAsync())
            field.Add((r.GetGuid(0), r.GetGuid(1), r.GetDouble(2)));
    }
    if (field.Count < 2)
        return Bad($"a league night needs at least 2 rated {category} robots with an active snapshot; found {field.Count}");

    // Every pair once: n*(n-1)/2 matches. Eight robots is 28, which is the
    // number §M3's acceptance names.
    var created = new List<Guid>();
    await using var tx = await c.BeginTransactionAsync();
    for (int i = 0; i < field.Count; i++)
        for (int j = i + 1; j < field.Count; j++)
        {
            // The LOWER-rated robot is the challenger. Arbitrary but not
            // random: the roles decide who the taper and the defense floor
            // apply to, so they have to be deterministic or two identical
            // league nights would settle differently.
            var (a, b) = field[i].Rating >= field[j].Rating ? (field[j], field[i]) : (field[i], field[j]);
            var seeds = new[] { Random.Shared.Next(1, int.MaxValue),
                                Random.Shared.Next(1, int.MaxValue),
                                Random.Shared.Next(1, int.MaxValue) };
            Guid id;
            await using (var ins = new NpgsqlCommand(@"
                INSERT INTO matches (challenger_snapshot_id, defender_snapshot_id, category, gap,
                                     arena, seeds, status, stake)
                VALUES ($1,$2,$3,0,'league_night',$4,'QUEUED',0) RETURNING id;", c, tx))
            {
                ins.Parameters.AddWithValue(a.Snap);
                ins.Parameters.AddWithValue(b.Snap);
                ins.Parameters.AddWithValue(category);
                ins.Parameters.AddWithValue(seeds);
                id = (Guid)(await ins.ExecuteScalarAsync())!;
            }
            await using (var job = new NpgsqlCommand(
                "INSERT INTO match_jobs (kind, match_id) VALUES ('FIGHT', $1);", c, tx))
            {
                job.Parameters.AddWithValue(id);
                await job.ExecuteNonQueryAsync();
            }
            created.Add(id);
        }
    await tx.CommitAsync();

    return Results.Ok(new { category, field = field.Count, matches = created.Count, matchIds = created });
}).AllowAnonymous();

// ======================================================== season rollover
// §2.4: "At rollover: ratings compress 50% toward 1200, deviation resets
// high, wallet scrap persists, season badges + payouts awarded. Seasons are
// what keep a solved ladder from fossilizing and give lapsed players a
// re-entry point."
//
// One transaction for the whole rollover. A season that half-happened - some
// categories paid, some ratings carried, the config pointing at a season that
// does not exist - is not a state anyone could unpick afterwards.
app.MapPost("/v1/admin/season/rollover", async (HttpContext ctx) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();

    await using var c = await db.OpenAsync();
    var cfg = await LadderConfig(c);
    int from = cfg.TryGetValue("current_season", out var cs) ? (int)cs : 1;
    int to = from + 1;
    double keep = (cfg.TryGetValue("season_compress_pct", out var cp) ? cp : 50) / 100.0;
    int payBase = (int)(cfg.TryGetValue("season_payout_base", out var pb) ? pb : 300);
    int places = (int)(cfg.TryGetValue("season_payout_places", out var pl) ? pl : 3);
    int weeks = (int)(cfg.TryGetValue("season_weeks", out var sw) ? sw : 4);

    await using var tx = await c.BeginTransactionAsync();

    // Refuse to roll into a season that already exists. Rolling over twice
    // would pay every podium a second time out of a faucet, and the ledger
    // has no way to tell the duplicate from a legitimate award.
    await using (var dup = new NpgsqlCommand("SELECT 1 FROM seasons WHERE id = $1;", c, tx))
    {
        dup.Parameters.AddWithValue(to);
        if (await dup.ExecuteScalarAsync() is not null)
            return Results.Conflict(new { error = $"season {to} already exists — this rollover has already run" });
    }

    // ---- payouts, before the ratings are compressed ---------------------
    // The standings being paid are the FINAL ones, so this has to read them
    // before the compression rewrites them.
    var awards = new List<object>();
    await using (var top = new NpgsqlCommand(@"
        SELECT category, robot_id, rating,
               ROW_NUMBER() OVER (PARTITION BY category ORDER BY rating DESC) AS place
          FROM ratings WHERE season_id = $1;", c, tx))
    {
        top.Parameters.AddWithValue(from);
        var rows = new List<(string Cat, Guid Robot, double Rating, long Place)>();
        await using (var r = await top.ExecuteReaderAsync())
            while (await r.ReadAsync())
                rows.Add((r.GetString(0), r.GetGuid(1), r.GetDouble(2), r.GetInt64(3)));

        foreach (var row in rows)
        {
            if (row.Place > places) continue;
            int amount = payBase / (int)row.Place;      // 300 / 150 / 100
            if (amount <= 0) continue;
            Guid owner;
            await using (var o = new NpgsqlCommand("SELECT user_id FROM robots WHERE id = $1;", c, tx))
            {
                o.Parameters.AddWithValue(row.Robot);
                owner = (Guid)(await o.ExecuteScalarAsync())!;
            }
            await using (var l = new NpgsqlCommand(
                "INSERT INTO ledger (user_id, delta, reason, idem_key) VALUES ($1,$2,'SEASON',$3);", c, tx))
            {
                l.Parameters.AddWithValue(owner);
                l.Parameters.AddWithValue(amount);
                // The idempotency key is the real guard: even if the season
                // check above were bypassed, the unique index refuses a
                // second payout for the same podium.
                //
                // The CATEGORY is part of the key because a robot can place
                // in more than one - §2.1 rates per robot PER CATEGORY, and a
                // featherweight that punched up is ranked in both. Keying on
                // robot alone collided the moment that happened, which is the
                // ledger's unique index doing exactly its job.
                l.Parameters.AddWithValue($"season:{from}:{row.Cat}:{row.Robot}");
                await l.ExecuteNonQueryAsync();
            }
            awards.Add(new { category = row.Cat, place = row.Place, robotId = row.Robot, scrap = amount });
        }
    }

    // ---- the new season, and the compressed carry-over -------------------
    await using (var ins = new NpgsqlCommand(
        "INSERT INTO seasons (id, starts_at, ends_at) VALUES ($1, now(), now() + make_interval(weeks => $2));", c, tx))
    {
        ins.Parameters.AddWithValue(to);
        ins.Parameters.AddWithValue(weeks);
        await ins.ExecuteNonQueryAsync();
    }

    int carried;
    await using (var carry = new NpgsqlCommand(@"
        INSERT INTO ratings (robot_id, category, season_id, rating, deviation, volatility)
        SELECT robot_id, category, $2,
               1200 + (rating - 1200) * $3,   -- compress toward the placement rating
               350,                            -- §2.4: deviation resets HIGH, so a
                                               -- new season is genuinely open again
               volatility
          FROM ratings WHERE season_id = $1
        RETURNING robot_id;", c, tx))
    {
        carry.Parameters.AddWithValue(from);
        carry.Parameters.AddWithValue(to);
        carry.Parameters.AddWithValue(keep);
        carried = 0;
        await using var r = await carry.ExecuteReaderAsync();
        while (await r.ReadAsync()) carried++;
    }

    await using (var bump = new NpgsqlCommand(
        "UPDATE ladder_config SET value = $1, updated_at = now() WHERE key = 'current_season';", c, tx))
    {
        bump.Parameters.AddWithValue(to);
        await bump.ExecuteNonQueryAsync();
    }

    await tx.CommitAsync();
    // Wallet scrap persisting needed no code: the ledger is append-only and
    // this never touched it.
    return Results.Ok(new { fromSeason = from, toSeason = to, ratingsCarried = carried,
                            compressedToPct = keep * 100, awards });
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
public record DepositReq(int Amount, string? IdemKey);
public record FightResult(Guid MatchId, string? WorkerId, string? Verdict,
                          string[]? ReplayUrls, string[]? Bouts);
public record ValidateResult(
    Guid SnapshotId, string? WorkerId, bool Legal, int MassKg,
    float AabbX, float AabbY, float AabbZ, string? Category,
    List<string>? PartsManifest, string? ProgramHash, string[]? FailReasons);
