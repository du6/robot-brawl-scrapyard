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
    await using var cmd = new NpgsqlCommand(
        "INSERT INTO users (email, pw_hash, display_name) VALUES ($1,$2,$3) RETURNING id;", c);
    cmd.Parameters.AddWithValue(req.Email.Trim());
    cmd.Parameters.AddWithValue(Passwords.Hash(req.Password!));
    cmd.Parameters.AddWithValue(name);
    try
    {
        var id = (Guid)(await cmd.ExecuteScalarAsync())!;
        // §2.3: a fresh wallet gets the signing bonus, in the same breath as
        // the account, so "registered" and "can afford a challenge" are the
        // same state.
        await using var l = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, idem_key) VALUES ($1,$2,'SIGNING_BONUS',$3);", c);
        l.Parameters.AddWithValue(id);
        l.Parameters.AddWithValue(500);
        l.Parameters.AddWithValue("signup:" + id);
        await l.ExecuteNonQueryAsync();
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

app.Run();

// ------------------------------------------------------------------ dtos
public record RegisterReq(string? Email, string? Password, string? DisplayName);
public record LoginReq(string? Email, string? Password);
public record RobotReq(string? Name);
public record SnapshotReq(Guid RobotId, string? Envelope);
public record ClaimReq(string? WorkerId);
public record ValidateResult(
    Guid SnapshotId, string? WorkerId, bool Legal, int MassKg,
    float AabbX, float AabbY, float AabbZ, string? Category,
    List<string>? PartsManifest, string? ProgramHash, string[]? FailReasons);
