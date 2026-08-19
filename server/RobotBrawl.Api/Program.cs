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
using Microsoft.AspNetCore.HttpOverrides;
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
// 30 days, was 12 hours. A 12h token expired between almost every play
// session, and the client TRUSTED a stored-but-expired token at boot —
// so a returning player saw an empty board over a signed-in-looking dock
// and read it as "the app update wiped my fights" (records were safe
// server-side; the session was just dead). The client now retires a
// 401'd token and re-prompts (LadderClient.Send/SessionExpired); this
// makes the common case — come back a day later — not need a re-auth at
// all. Overridable by Jwt:Hours config for a shorter-lived deployment.
var tokens = new Tokens(jwtSecret, TimeSpan.FromHours(cfg.GetValue("Jwt:Hours", 720)));

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(tokens);
builder.Services.AddSingleton(new JobQueue(db, sqlDir));
// Start the worker when work arrives instead of every five minutes forever.
// Unset RB_WORKER_JOB and this is a no-op, which is what local runs and the
// bench get. See WorkerTrigger for the 126-seconds-to-do-nothing measurement.
builder.Services.AddSingleton(new WorkerTrigger(
    db,
    cfg["Worker:Job"] ?? Environment.GetEnvironmentVariable("RB_WORKER_JOB"),
    int.TryParse(Environment.GetEnvironmentVariable("RB_WORKER_NUDGE_DEBOUNCE_S"), out var _nd) ? _nd : 30));
// Object storage when it is configured, the local filesystem otherwise.
// The fallback is not laziness: run_local.sh and every bench must work with
// no network and no credentials, and a test suite that needs a cloud account
// is a test suite that stops being run.
//
// ⚠ FileBlobStore ON CLOUD RUN LOSES DATA. That filesystem is ephemeral and
// per-instance, so with min-instances=0 an uploaded payload vanishes when the
// service scales down and its VALIDATE job can never be worked - while the
// upload answers 200 the whole time. If BLOB_S3_BUCKET is unset in a
// deployment, that deployment is broken in a way nothing will report.
var s3Bucket = Environment.GetEnvironmentVariable("BLOB_S3_BUCKET");
var s3Endpoint = Environment.GetEnvironmentVariable("BLOB_S3_ENDPOINT")
                 ?? "https://storage.googleapis.com";
var s3Key = Environment.GetEnvironmentVariable("BLOB_S3_KEY");
var s3Secret = Environment.GetEnvironmentVariable("BLOB_S3_SECRET");
if (!string.IsNullOrEmpty(s3Bucket) && !string.IsNullOrEmpty(s3Key) && !string.IsNullOrEmpty(s3Secret))
    builder.Services.AddSingleton<IBlobStore>(new S3BlobStore(s3Endpoint, s3Bucket!, s3Key!, s3Secret!));
else
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
    // Economy traffic is BURSTY BY DESIGN and the numbers come from real
    // flows, not from a bench: settling one fight posts up to 2 claims
    // (entry + purse/consolation), and a shop teardown-and-rebuild session
    // legitimately sells and re-buys a dozen parts inside a minute of taps.
    // 20/min (the wallet bucket, sized for deposits) throttles that player
    // out of their own shop. 60/min is one op per second sustained — far
    // above any human tap rate's steady state, and the rate limit is DoS
    // protection here, NOT the economic defense: idempotency keys and the
    // first-win uniqueness are what guard the money.
    o.AddPolicy("economy", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue("sub") ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
    // The mailing list gets its own bucket for the reason stated above the
    // wallet one, and here it cuts BOTH ways: sharing "auth" would let a flood
    // of signups from one address lock real players out of signing IN, and a
    // player who just registered would find the form refusing them.
    //
    // ⚠ 20, NOT THE 5 THIS SHIPPED WITH FOR AN HOUR. "A human subscribes once"
    // was the reasoning, and it is wrong twice over. THE BUCKET IS PER IP, NOT
    // PER PERSON: a household, an office or a school behind one NAT share it,
    // so five is five PEOPLE, and the sixth reader of the same link is refused
    // with no way to tell why. And unsubscribe sits in this bucket too — a
    // mail client that prefetches links can spend the budget before the human
    // clicks anything. Five was also tighter than `auth` at 10, which is
    // backwards: this endpoint cannot be used to guess a password.
    // Found by the bench, which needs eight calls to check the round trip and
    // got 429s on six of them; the limit was the thing that was wrong.
    o.AddPolicy("subscribe", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    // ⚠ UNSUBSCRIBE GETS ITS OWN BUCKET, AND SHARING ONE WAS A REAL DEFECT —
    // measured on production during the 2026-08-19 launch check, by accident:
    // 24 rapid signups exhausted the shared budget, and all 24 unsubscribe
    // calls that followed returned 429. Nobody was unsubscribed.
    //
    // The bucket is PER IP, so that is not hypothetical: a burst of signups
    // from one office or household NAT stops everyone behind it leaving the
    // list. Leaving is the one action that must always work — a broken
    // unsubscribe is what gets a sending domain blocklisted, and the site's
    // own copy promises "one-step unsubscribe".
    //
    // 60/min because unsubscribing is not an abuse vector worth defending
    // against: the worst a flood achieves is removing addresses the sender
    // then does not mail, and this endpoint deliberately answers identically
    // whether or not the address was on the list, so it cannot be used to
    // enumerate anyone. The limit exists only to bound a DoS.
    o.AddPolicy("unsubscribe", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// ⚠ CORS EXISTS FOR EXACTLY ONE ROUTE, AND THE ALLOW-LIST IS THE POINT.
// Until now this API sent no CORS headers at all, which is why the website
// BAKES its data with a Python job instead of fetching live (see
// bake_showcase.py's header). That stays true: opening the ladder up to
// browser reads is a separate decision with its own abuse profile, and the
// baked file is faster and survives the API being cold or down.
//
// The mailing-list form is different — it is a WRITE from a page on another
// origin, so it cannot work without this. Named origins only: a wildcard here
// would let any site on the internet post signups through this endpoint using
// a visitor's browser. Localhost is listed so the form can be exercised
// against a dev API without editing this file, which is how a "temporary"
// wildcard normally gets added and left in.
const string SitePolicy = "site";
builder.Services.AddCors(o => o.AddPolicy(SitePolicy, p => p
    .WithOrigins("https://cyberduck.club", "https://www.cyberduck.club",
                 "http://localhost:8811")
    .WithMethods("POST", "GET")
    .WithHeaders("Content-Type")));

var app = builder.Build();

// Behind Cloud Run the app speaks plain HTTP to a proxy that terminated TLS,
// and every request arrives from that proxy's address. Two things break, and
// BOTH are invisible on a local run where the connection really is the client:
//
//   - Request.Scheme is "http", so Fetchable() hands workers an http:// blob
//     URL. Cloud Run answers that with a 302, and a redirect drops the
//     X-Worker-Key header. Measured against the live service: 302 on http,
//     200 on the same URL forced to https.
//   - RemoteIpAddress is the proxy for EVERY caller, so the 10/min anonymous
//     auth limiter becomes one global bucket. Ten registrations a minute
//     across all players, worldwide.
//
// ForwardLimit 1 with the known-proxy lists cleared takes the RIGHTMOST entry
// of each header. That is deliberate and is the spoof-proof choice: Cloud Run
// APPENDS the real client address to whatever X-Forwarded-For the caller sent,
// so the rightmost value is the platform's, not the caller's. Reading the
// leftmost would let anyone forge an IP and evade the limiter entirely.
//
// Gated, because trusting these headers with no proxy in front is exactly that
// forgery hole. K_SERVICE is set by Cloud Run itself; TRUST_PROXY forces it on
// for any other proxied deployment, and for the smoke test.
var trustProxy = Environment.GetEnvironmentVariable("TRUST_PROXY") is { Length: > 0 } tp
    ? tp is not ("0" or "false" or "False")
    : Environment.GetEnvironmentVariable("K_SERVICE") is { Length: > 0 };
if (trustProxy)
{
    var fwd = new ForwardedHeadersOptions {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        ForwardLimit = 1,
    };
    fwd.KnownNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
    app.Logger.LogInformation("Trusting X-Forwarded-For/Proto from the immediate proxy.");
}

// Before auth: a CORS preflight is an unauthenticated OPTIONS and must be
// answered as one.
app.UseCors(SitePolicy);
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


// A stored blob URI is not a fetchable URL, and deliberately so: s3:// says
// where the bytes live, and WHO may read them is decided per request. This
// turns one into the other, pointing at this service's own /v1/blobs.
//
// file:// rows are returned unchanged - they predate object storage and are
// only reachable on a single-machine deployment anyway, which is exactly
// where they still work.
string Fetchable(HttpContext ctx, string? storedUri)
{
    if (!BlobUri.IsObjectStore(storedUri)) return storedUri ?? "";
    var key = BlobUri.KeyOf(storedUri);
    if (key == null) return storedUri ?? "";
    var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}";
    return $"{baseUrl}/v1/blobs/{key}";
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
        // displayName comes back, and it is not decoration — see the note on
        // /v1/auth/login below. LadderClient.Auth has always read this field
        // out of BOTH replies and neither one has ever sent it.
        return Results.Ok(new { token = tokens.Issue(id, req.Email!), userId = id, displayName = name });
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        // ⚠ THERE ARE TWO UNIQUE INDEXES ON `users` NOW, and this handler used
        // to answer "that email is already registered" to whichever one fired.
        // Since 015 added `users_display_name_lower_key`, that would tell a
        // player their email was taken when the real problem was their name —
        // and they would change the one thing that was fine, over and over.
        // The client shows this string verbatim (LadderClient.Auth reads the
        // "error" field), so the wrong string here is the whole user
        // experience of the failure.
        //
        // Match on the CONSTRAINT NAME, not on the message text: the message
        // is Postgres's to reword, the index name is ours.
        var which = ex.ConstraintName ?? "";
        if (which == "users_display_name_lower_key")
            return Results.Conflict(new { error = "that display name is already taken — pick another" });
        if (which == "users_email_lower_key")
            return Results.Conflict(new { error = "that email is already registered" });
        // A 23505 from neither index is a schema change nobody told this
        // handler about. Say that, rather than blaming the email again.
        app.Logger.LogWarning("register: unexpected unique violation on {Constraint}", which);
        return Results.Conflict(new { error = "that account could not be created — please try different details" });
    }
}).RequireRateLimiting("auth");

app.MapPost("/v1/auth/login", async (LoginReq req) =>
{
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT id, pw_hash, email, display_name FROM users WHERE email_lower = lower($1);", c);
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
    var displayName = r.IsDBNull(3) ? "" : r.GetString(3);
    if (!Passwords.Verify(req.Password ?? "", hash)) return Results.Unauthorized();
    // ⚠ displayName IS PART OF THE REPLY — found 2026-08-10 by the first bench
    // that ever signed a player back IN rather than registering a new one.
    //
    // LadderClient.Auth reads "displayName" out of this body and ArenaScreen
    // does `who = string.IsNullOrEmpty(name) ? email : name`. Neither auth
    // endpoint had ever sent the field, so that fallback fired EVERY TIME and
    // the ARENA greeted every player, on every path, by their EMAIL ADDRESS —
    // on the status line and across the face of the SIGN OUT button. On a
    // screen this project screenshots on purpose (docs/ARENA_Judged and
    // ArenaShots both photograph it) that is a player's email in the
    // repository, and the client had been asking for the right thing all
    // along. It read as correct because the only sessions anyone ever
    // exercised were brand-new accounts nobody knew the name of.
    //
    // Note this is returned only AFTER the password verifies — moving it above
    // the check would turn a login attempt into an account-enumeration oracle,
    // which is the exact thing the decoy-hash branch above exists to prevent.
    return Results.Ok(new { token = tokens.Issue(id, email), userId = id, displayName });
}).RequireRateLimiting("auth");

// OPERATOR password reset (2026-08-13). The login gate made a forgotten
// password a soft-brick — no account, no game — and there is no mail
// infrastructure on this budget, so self-serve email reset would be theater.
// The honest v1: the player emails support (the gate says so), the operator
// resets here. Worker-key gated like every operator action; enumeration is a
// non-issue for the operator. Replace with a real email flow before scale.
app.MapPost("/v1/admin/reset-password", async (HttpContext ctx, ResetPwReq req) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if ((req.NewPassword ?? "").Length < 10) return Bad("password must be at least 10 characters");
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "UPDATE users SET pw_hash = $1 WHERE email_lower = lower($2) RETURNING id;", c);
    cmd.Parameters.AddWithValue(Passwords.Hash(req.NewPassword!));
    cmd.Parameters.AddWithValue(req.Email ?? "");
    var got = await cmd.ExecuteScalarAsync();
    if (got is null) return Results.NotFound(new { error = "no account with that email" });
    return Results.Ok(new { reset = true });
    // No player rate bucket: this is worker-key-gated OPERATOR traffic, like
    // every other /v1/admin/* route — parking it in the shared "auth" bucket
    // starved real logins of their 10/min (measured: the bench's own login
    // checks 429'd).
});

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
    // ⚠ THE ACTIVE JOIN AND THE LATEST JOIN ARE TWO DIFFERENT QUESTIONS, and
    // conflating them would break challenges. `s` answers "which snapshot may
    // fight" and MUST stay ACTIVE-only, because POST /v1/challenges resolves a
    // challenger through activeSnapshotId; widening it would silently offer a
    // PENDING or REJECTED build as a challenger. `ls` answers "what happened to
    // the last thing I uploaded", which is a question the ACTIVE join cannot
    // answer even in principle: a rejected snapshot is not ACTIVE, so the row
    // came back with activeSnapshotId = NULL and no status — byte-identical to
    // a robot still being checked. The client therefore had no way to tell
    // REJECTED from PENDING and said "waiting to be checked" forever. That was
    // a missing COLUMN, not a missing label.
    //
    // Owner-only by construction: this endpoint filters WHERE r.user_id = $1,
    // so every row is the caller's own robot. That is what makes it safe to
    // return fail_reasons here when GET /v1/snapshots/{id} guards them behind
    // `mine` — handing a scout the validator's reasons is handing them the
    // build (see that endpoint's comment). Do not reuse this projection on any
    // route that can return another player's robot.
    //
    // LATERAL + LIMIT 1 rides snapshots_robot_idx (robot_id, uploaded_at DESC),
    // so it is an index hit per robot, not a sort of the history.
    await using var cmd = new NpgsqlCommand(@"
        SELECT r.id, r.name, r.created_at, r.retired, s.id, s.category,
               ls.status, ls.fail_reasons
          FROM robots r
          LEFT JOIN snapshots s ON s.robot_id = r.id AND s.status = 'ACTIVE'
          LEFT JOIN LATERAL (
              SELECT status, fail_reasons
                FROM snapshots
               WHERE robot_id = r.id
               ORDER BY uploaded_at DESC
               LIMIT 1
          ) ls ON TRUE
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
            // NULL when the robot has never been enlisted — distinct from
            // "PENDING", and the client must not render either as a reason.
            snapshotStatus = r.IsDBNull(6) ? null : r.GetString(6),
            failReasons = r.IsDBNull(7) ? null : r.GetFieldValue<string[]>(7),
        });
    return Results.Ok(list);
}).RequireAuthorization();

// ------------------------------------------------------------- snapshots
app.MapPost("/v1/snapshots", async (SnapshotReq req, ClaimsPrincipal user, IBlobStore blobs, WorkerTrigger nudge) =>
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
    // ⚠ AFTER THE COMMIT, NEVER BEFORE. A worker started while the transaction
    // is still open either finds nothing (and pays a Unity boot for it) or
    // races the row it was sent to collect; and a rolled-back transaction that
    // had already started a worker is a boot bought for work that never
    // existed.
    await nudge.NudgeAsync(app.Logger);
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

    // ⚠ EVERY FIELD IS READ OUT OF THE READER BEFORE ANY OTHER QUERY RUNS.
    // Npgsql allows one command per connection at a time, so issuing the badge
    // query below while this reader is still open throws
    // NpgsqlOperationInProgressException and the whole card 500s. That is
    // exactly what the first version of this did — 17 checks went red, all of
    // them downstream of a scouting card that had started failing.
    var cardId       = r.GetGuid(0);
    var cardStatus   = r.GetString(1);
    var cardMass     = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
    var cardAabb     = r.IsDBNull(3) ? null : new { x = r.GetFloat(3), y = r.GetFloat(4), z = r.GetFloat(5) };
    var cardCategory = r.IsDBNull(6) ? null : r.GetString(6);
    var cardParts    = r.IsDBNull(7) ? null : JsonSerializer.Deserialize<string[]>(r.GetFieldValue<string>(7));
    var cardHash     = r.IsDBNull(8) ? null : r.GetString(8);
    var cardFails    = mine && !r.IsDBNull(9) ? r.GetFieldValue<string[]>(9) : null;
    var cardUploaded = r.GetDateTime(10);
    var cardName     = r.GetString(12);
    await r.CloseAsync();

    // §M3's "season history on robot cards". A badge is public: it is a past
    // PLACING, the most public fact about a ladder there is, and it is what
    // makes a scouting card mean something two seasons on — "1st in FEATHER,
    // at 1574" says more about an opponent than a current provisional rating.
    var badges = new List<object>();
    await using (var b = new NpgsqlCommand(@"
        SELECT b.season_id, b.category, b.place, b.final_rating
          FROM season_badges b
          JOIN snapshots s ON s.robot_id = b.robot_id
         WHERE s.id = $1
         ORDER BY b.season_id DESC, b.place ASC;", c))
    {
        b.Parameters.AddWithValue(id);
        await using var br = await b.ExecuteReaderAsync();
        while (await br.ReadAsync())
            badges.Add(new { season = br.GetInt32(0), category = br.GetString(1),
                             place = br.GetInt32(2), rating = Math.Round(br.GetDouble(3), 1) });
    }

    // §1.3: this is the scouting card. Everything here is public by design —
    // mass, size box, category, part manifest, record. The program is not
    // here, and neither is the payload URL, for the owner OR anyone else:
    // the only thing that ever reads a payload is a worker.
    return Results.Ok(new
    {
        id = cardId,
        status = cardStatus,
        robotName = cardName,
        massKg = cardMass,
        aabb = cardAabb,
        category = cardCategory,
        // A REAL JSON array, not a quoted string. parts_manifest is jsonb, and
        // handing it back as GetFieldValue<string> emitted
        // "partsManifest": "[\"beam\",\"wheel\"]" — legal JSON that every
        // client has to unescape and parse a second time. The scouting card
        // read it as zero parts, which is how this was found.
        partsManifest = cardParts,
        programHash = cardHash,
        hasProgram = cardHash != null && cardHash.Length > 0,
        // Only the owner is told WHY their own upload was rejected. Handing a
        // scout the validator's reasons is handing them the build.
        failReasons = cardFails,
        uploadedAt = cardUploaded,
        seasonHistory = badges,
        mine,
    });
}).AllowAnonymous();

// §M4's monitoring surface. Behind the worker key, not anonymous: queue depth
// and failure counts tell an attacker when the ladder is struggling, which is
// exactly when a flood is cheapest. It is also what an uptime check can hit
// to prove the DB is reachable, which /healthz deliberately does not do —
// /healthz answers from the process alone, so it stays green through a total
// database outage. Two different questions, two different endpoints.
app.MapGet("/v1/admin/metrics", async (HttpContext ctx, JobQueue q) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    return Results.Ok(await q.StatsAsync());
}).AllowAnonymous();

// ----------------------------------------------------------- worker path
// §5.5: worker→API calls are authenticated with a worker key. Ownership of a
// job is checked in SQL (claimed_by), so a stolen key still cannot steal
// another worker's job result.
app.MapPost("/v1/worker/jobs/claim", async (HttpContext ctx, JobQueue q, ClaimReq req) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(req.WorkerId)) return Bad("workerId is required");
    // Optional. A specialised worker passes the one kind it can run so it does
    // not claim — and burn an attempt on — work it will only put back. Refused
    // rather than ignored: a typo'd kind that silently claimed everything would
    // reintroduce exactly the bug this prevents.
    var kind = string.IsNullOrWhiteSpace(req.Kind) ? null : req.Kind!.Trim().ToUpperInvariant();
    if (kind != null && kind != "VALIDATE" && kind != "FIGHT")
        return Bad("kind must be VALIDATE, FIGHT, or omitted");
    var job = await q.ClaimAsync(req.WorkerId!, kind);
    if (job is null) return Results.NoContent();

    // The stored URIs are s3://; a worker fetches over HTTP. Rewriting here
    // rather than at write time means the link is minted for THIS request and
    // nothing stale is ever persisted. Field names must match ClaimedJob's
    // exactly — RobotWorker.ParseClaim reads them by name.
    return Results.Ok(new
    {
        id = job.Id, kind = job.Kind, matchId = job.MatchId, snapshotId = job.SnapshotId,
        attempts = job.Attempts,
        payloadUrl = Fetchable(ctx, job.PayloadUrl), payloadSha256 = job.PayloadSha256,
        challengerUrl = Fetchable(ctx, job.ChallengerUrl), challengerSha256 = job.ChallengerSha256,
        defenderUrl = Fetchable(ctx, job.DefenderUrl), defenderSha256 = job.DefenderSha256,
        arena = job.Arena, seeds = job.Seeds,
    });
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

    // A robot that passes validation JOINS THE LADDER, and it has to happen
    // here or the ladder cannot start at all.
    //
    // Ratings rows used to be created only by the first FIGHT ("first fight in
    // this category: create the placement row"). But the leaderboard reads
    // `ratings`, and the leaderboard is how one player discovers another. So a
    // freshly validated robot was ACTIVE, correctly categorised, visible to its
    // OWNER via /v1/robots — and invisible to everybody else. Two players could
    // both enlist and neither could see the other, so neither could issue the
    // first challenge, so no rating row was ever created. League nights could
    // not break the tie either: they draw from the top 8 of `ratings`, which
    // was empty. The ladder had no way to begin.
    //
    // Found by working the first real job in the cloud and looking at the
    // board afterwards: ACTIVE robot, FEATHER, count 0.
    //
    // ON CONFLICT DO NOTHING is load-bearing, not defensive: re-uploading a
    // robot revalidates it, and this must never reset a rating that has been
    // earned. A build that changes weight class gets a placement row in the new
    // category and keeps its old one, which is what per-category rating means.
    if (res.Legal && !string.IsNullOrEmpty(res.Category))
    {
        await using var place = new NpgsqlCommand(@"
            INSERT INTO ratings (robot_id, category, season_id)
            SELECT s.robot_id, $2, (SELECT value::int FROM ladder_config WHERE key = 'current_season')
              FROM snapshots s WHERE s.id = $1
            ON CONFLICT DO NOTHING;", c, tx);
        place.Parameters.AddWithValue(res.SnapshotId);
        place.Parameters.AddWithValue(res.Category!);
        await place.ExecuteNonQueryAsync();
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
app.MapPost("/v1/challenges", async (ChallengeReq req, ClaimsPrincipal user, WorkerTrigger nudge) =>
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

    // ONE bout (owen, 2026-08-14 — best-of-3 retired for challenges: the
    // client now plays the fight LIVE and a spectator sits through every
    // bout, so the match is one decisive fight). The seed is still
    // server-chosen — a client-chosen seed is a client choosing its own
    // fight — and it is the SAME seed the worker referees with, which is
    // what makes the on-device fight and the settlement fight one fight.
    var seeds = new[] { Random.Shared.Next(1, int.MaxValue) };

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
    await nudge.NudgeAsync(app.Logger);

    return Results.Ok(new { matchId, status = "QUEUED", stake, gap, category = df.Category, seeds });
}).RequireAuthorization().RequireRateLimiting("upload");

// The LIVE-FIGHT feed (owen, 2026-08-14): a challenge plays out ON the
// challenger's device with the same seed the worker referees with, so the
// client needs both snapshot envelopes. Participant-gated — snapshot blobs
// are otherwise worker-only, and this endpoint is exactly as wide as "your
// own match": build AND program of both sides go to a participant's device,
// which is inherent to simulating the fight locally and to nobody else.
app.MapGet("/v1/matches/{matchId:guid}/envelopes", async (Guid matchId, ClaimsPrincipal user, IBlobStore blobs) =>
{
    var me = UserId(user);
    await using var c = await db.OpenAsync();
    string? chUri = null, dfUri = null; Guid chOwner = Guid.Empty, dfOwner = Guid.Empty;
    await using (var cmd = new NpgsqlCommand(@"
        SELECT sc.storage_url, sd.storage_url, rc.user_id, rd.user_id
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
         WHERE m.id = $1 AND (rc.user_id = $2 OR rd.user_id = $2);", c))
    {
        cmd.Parameters.AddWithValue(matchId);
        cmd.Parameters.AddWithValue(me);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return Results.NotFound(new { error = "no such match, or it is not yours" });
        chUri = r.GetString(0); dfUri = r.GetString(1); chOwner = r.GetGuid(2); dfOwner = r.GetGuid(3);
    }
    async Task<byte[]?> ReadSnapshot(string? uri)
    {
        if (BlobUri.IsObjectStore(uri)) return await blobs.GetAsync(BlobUri.KeyOf(uri)!);
        if (uri != null && uri.StartsWith("file://", StringComparison.Ordinal))
        {
            var path = new Uri(uri).LocalPath;
            return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
        }
        return uri == null ? null : await blobs.GetAsync(uri);
    }
    var chBytes = await ReadSnapshot(chUri);
    var dfBytes = await ReadSnapshot(dfUri);
    if (chBytes is null || dfBytes is null)
        return Results.NotFound(new { error = "a snapshot blob is missing" });

    // ⚠ SECURITY (launch audit 2026-08-14): a program is secret IP, and
    // "participant" is self-service — anyone can challenge anyone. So this
    // endpoint returns the caller's OWN side in full (their program is not a
    // secret to them) and only the OPPONENT'S BUILD — never the opponent's
    // program. The on-device fight is an EXHIBITION preview: it renders the
    // real opponent chassis under generic AI while the cloud referee settles
    // the real match with the real programs. Extract name+build from the
    // opponent payload; the API otherwise treats payloads as opaque, and this
    // is the one place it must read two fields, so it reads exactly two.
    string mineEnvelope = System.Text.Encoding.UTF8.GetString(me == chOwner ? chBytes : dfBytes);
    byte[] oppBytes = me == chOwner ? dfBytes : chBytes;
    string oppName = "", oppBuild = "";
    try
    {
        using var oppDoc = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(oppBytes));
        var payloadStr = oppDoc.RootElement.GetProperty("payload").GetString() ?? "";
        using var payDoc = JsonDocument.Parse(payloadStr);
        if (payDoc.RootElement.TryGetProperty("robotName", out var n)) oppName = n.GetString() ?? "";
        if (payDoc.RootElement.TryGetProperty("build", out var b)) oppBuild = b.GetString() ?? "";
    }
    catch { return Results.Problem("opponent snapshot is unreadable"); }

    return Results.Ok(new
    {
        // "you" carries the caller's full envelope (build + program); the
        // opponent is build-only. The live client always calls this as the
        // challenger, but keying on ownership keeps it correct either way.
        you = mineEnvelope,
        opponentName = oppName,
        opponentBuild = oppBuild,
    });
}).RequireAuthorization();

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
    // ⚠ THE SEASON FILTER IS LOAD-BEARING — found 2026-08-12, the day the
    // rollover got a scheduler. ratings is keyed (robot, category, SEASON)
    // and the rollover CARRIES rows into the new season while keeping the
    // old season's rows for the record — so a board with no season filter
    // lists every robot once per season it has existed in. Nobody had seen
    // it because no rollover had ever run outside api_smoke's dev DB, i.e.
    // the bug was armed by the same missing caller that hid the feature.
    var cfg = await LadderConfig(c);
    int curSeason = cfg.TryGetValue("current_season", out var cs) ? (int)cs : 1;
    DateTime? seasonEndsAt = null;
    await using (var se = new NpgsqlCommand(
        "SELECT ends_at FROM seasons WHERE id = $1;", c))
    {
        se.Parameters.AddWithValue(curSeason);
        var v = await se.ExecuteScalarAsync();
        if (v is DateTime dt) seasonEndsAt = dt;
    }
    // Rank by rating, but surface deviation too: a 1400 at RD 350 has not
    // earned the same claim as a 1400 at RD 60, and a board that hides that
    // is a board that lies about its own confidence.
    await using var cmd = new NpgsqlCommand(@"
        SELECT ra.category, ra.rating, ra.deviation, ra.volatility, ra.updated_at,
               r.id, r.name, u.display_name,
               (SELECT s.id FROM snapshots s
                 WHERE s.robot_id = r.id AND s.status = 'ACTIVE' LIMIT 1),
               ct.name, cp.name
          FROM ratings ra
          JOIN robots r ON r.id = ra.robot_id
          JOIN users  u ON u.id = r.user_id
          LEFT JOIN cosmetics ct ON ct.id = r.title_id
          LEFT JOIN cosmetics cp ON cp.id = r.plate_id
         WHERE ra.season_id = $3
           AND ($1::text IS NULL OR ra.category = $1)
         ORDER BY ra.rating DESC, ra.deviation ASC
         LIMIT $2;", c);
    cmd.Parameters.AddWithValue((object?)category ?? DBNull.Value);
    cmd.Parameters.AddWithValue(take);
    cmd.Parameters.AddWithValue(curSeason);

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
            // Cosmetic only, and visibly so — a title on the board is what
            // makes the scrap sink worth spending into.
            title = r.IsDBNull(9) ? null : r.GetString(9),
            plate = r.IsDBNull(10) ? null : r.GetString(10),
            updatedAt = r.GetDateTime(4),
        });
    // The season identity travels WITH the board: a board that says "Season 2
    // ends Friday" is a re-entry point (§2.4's whole purpose); a bare list of
    // ratings is not. seasonEndsAt is null until the first scheduler tick
    // starts season 1's clock — render the season number alone in that case.
    return Results.Ok(new { category = category ?? "ALL", count = rows.Count,
                            season = curSeason, seasonEndsAt, entries = rows });
}).AllowAnonymous();

// A single match, public. M2's acceptance requires that "a third account can
// scout both and watch the replay but cannot fetch either program payload" —
// so the replay URL is here for everyone, and the payload URL is nowhere.
app.MapGet("/v1/matches/{id:guid}", async (Guid id, HttpContext ctx) =>
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
        replayUrls = r.IsDBNull(6) ? Array.Empty<string>()
                   : Array.ConvertAll(r.GetFieldValue<string[]>(6), u => Fetchable(ctx, u)),
        // The fight report §2.2 asks to be shown honestly: tapered, floor
        // reached, defender unrated on a punch-up.
        ratingDeltas = r.IsDBNull(7) ? null : r.GetFieldValue<string>(7),
        challenger = new { snapshotId = r.GetGuid(10), robotName = r.GetString(12), owner = r.GetString(14) },
        defender   = new { snapshotId = r.GetGuid(11), robotName = r.GetString(13), owner = r.GetString(15) },
        createdAt = r.GetDateTime(8),
        completedAt = r.IsDBNull(9) ? (DateTime?)null : r.GetDateTime(9),
    });
}).AllowAnonymous();

// A ROBOT'S COMPLETED FIGHTS — public, like the board and the match card it
// is assembled from (2026-08-17, for cyberduck.club's daily showcase: the
// site needs to find the top robot's best win before it can render it).
//
// Public is the right default here and not a loosening: every field below
// already leaves the building through /v1/matches/{id}, which is anonymous —
// this endpoint only saves a caller from having to know a match id first.
// The replay a caller follows to is likewise already public and, by §1.3,
// carries builds but never programs.
//
// ⚠ NOT the inbox. /v1/inbox is per-account and scoped on purpose ("an inbox
// that shows everyone's fights is a feed, not an inbox", line ~1473); this is
// per-ROBOT and shows only what a scouting card would.
app.MapGet("/v1/robots/{robotId:guid}/matches", async (Guid robotId, HttpContext ctx, int? limit) =>
{
    int take = Math.Clamp(limit ?? 10, 1, 50);
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(@"
        SELECT m.id, m.verdict, m.category, m.gap, m.arena, m.replay_urls,
               m.rating_deltas, m.completed_at, (rc.id = $1) AS was_challenger,
               rc.name, rd.name, uc.display_name, ud.display_name
          FROM matches m
          JOIN snapshots sc ON sc.id = m.challenger_snapshot_id JOIN robots rc ON rc.id = sc.robot_id
          JOIN snapshots sd ON sd.id = m.defender_snapshot_id   JOIN robots rd ON rd.id = sd.robot_id
          JOIN users uc ON uc.id = rc.user_id JOIN users ud ON ud.id = rd.user_id
         WHERE m.status = 'COMPLETE' AND (rc.id = $1 OR rd.id = $1)
         ORDER BY m.completed_at DESC NULLS LAST
         LIMIT $2;", c);
    cmd.Parameters.AddWithValue(robotId);
    cmd.Parameters.AddWithValue(take);
    await using var r = await cmd.ExecuteReaderAsync();
    var rows = new List<object>();
    while (await r.ReadAsync())
    {
        string verdict = r.IsDBNull(1) ? "" : r.GetString(1);
        bool wasChallenger = r.GetBoolean(8);
        // Derived HERE from the stored side, never re-inferred downstream:
        // line ~1501 records what re-deriving "was I the challenger" cost.
        string outcome = verdict == "DRAW" ? "DRAW"
                       : (verdict == "CHALLENGER") == wasChallenger ? "WON" : "LOST";
        rows.Add(new
        {
            id = r.GetGuid(0),
            outcome,
            verdict,
            category = r.GetString(2),
            gap = r.GetInt32(3),
            arena = r.GetString(4),
            replayUrls = r.IsDBNull(5) ? Array.Empty<string>()
                       : Array.ConvertAll(r.GetFieldValue<string[]>(5), u => Fetchable(ctx, u)),
            ratingDeltas = r.IsDBNull(6) ? null : r.GetFieldValue<string>(6),
            completedAt = r.IsDBNull(7) ? (DateTime?)null : r.GetDateTime(7),
            robotName = wasChallenger ? r.GetString(9) : r.GetString(10),
            opponentName = wasChallenger ? r.GetString(10) : r.GetString(9),
            opponentOwner = wasChallenger ? r.GetString(12) : r.GetString(11),
        });
    }
    return Results.Ok(new { robotId, count = rows.Count, matches = rows });
}).AllowAnonymous();

// §5.3 step 4: "Both players' fight inboxes show the result; replay is live."
// Authenticated, and scoped to matches this account was actually in — an
// inbox that shows everyone's fights is a feed, not an inbox.
app.MapGet("/v1/inbox", async (ClaimsPrincipal user, HttpContext ctx, int? limit) =>
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
            replayUrls = r.IsDBNull(5) ? Array.Empty<string>()
                       : Array.ConvertAll(r.GetFieldValue<string[]>(5), u => Fetchable(ctx, u)),
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
    // The client's cache refill (docs/Server_Economy_Design_2026-08-13.md):
    // ownership is server-truth and the local save is a cache, so the wallet
    // answers with everything the cache needs in one round trip. Additive
    // field — pre-economy clients read balance/recent and ignore it.
    var inv = new List<object>();
    await using (var iq = new NpgsqlCommand(
        "SELECT part_id, mat, count FROM inventory WHERE user_id=$1 AND count > 0 ORDER BY part_id, mat;", c))
    {
        iq.Parameters.AddWithValue(me);
        await using var r = await iq.ExecuteReaderAsync();
        while (await r.ReadAsync())
            inv.Add(new { partId = r.GetString(0), mat = r.GetString(1), count = r.GetInt32(2) });
    }
    return Results.Ok(new { balance, recent = rows, inventory = inv });
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

// ================================================================= economy
// docs/Server_Economy_Design_2026-08-13.md: scrap will eventually be sold for
// real money, and you cannot sell for money what the client can mint for
// free. The career's earn/spend paths become ledger rows here. Two rules
// carry everything:
//
//   * A LEAGUE PURSE PAYS ON THE FIRST WIN ONLY (owen). The idem_key is
//     SERVER-constructed from (user, contest), so the database refuses a
//     second payment — total league credit per account is hard-capped at
//     the purse-table ceiling (13,850; see the design doc's arithmetic).
//   * Every amount is computed HERE from server-known tables and clamped
//     constants. The client reports inputs (damage dealt, the underdog
//     ratio); it never names its own price.
//
// The constants mirror Career.cs and must move with it — the client remains
// the DISPLAY copy of this arithmetic, this is what pays. api_smoke pins the
// ceiling arithmetic so drift fails a bench instead of paying wrong money.
const double ECON_UNDERDOG_CAP = 1.6;      // Career.UNDERDOG_CAP
const double ECON_WIN_DMG_K   = 0.1667;    // Career.WIN_DMG_K (one-third cut, 2026-08-15)
const double ECON_WIN_DMG_CAP = 400;       // Career.WIN_DMG_CAP
const int    ECON_FIRST_WIN_BONUS = 50;    // Career.FIRST_WIN_BONUS (one-third cut, 2026-08-15)
// The ECON_LOSS_* constants and ECON_CONSOLATION_MAX lived here for a few
// hours on 2026-08-13 and are GONE (owen: "remove loss payment in leagues" —
// register #6 resolved by deletion). With fees also gone, a repeatable loss
// payment was the league's last unbounded faucet. The league pays WINS ONLY,
// once each; the whole exposure is the purse-table ceiling.

app.MapPost("/v1/economy/claims", async (EconClaimReq req, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
    var contest = (req.ContestId ?? "").Trim();
    if (contest.Length is < 1 or > 16) return Bad("a contestId is required");

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    int purse;
    await using (var q = new NpgsqlCommand(
        "SELECT purse FROM league_contests WHERE id = $1;", c, tx))
    {
        q.Parameters.AddWithValue(contest);
        var got = await q.ExecuteScalarAsync();
        if (got is null) return Results.NotFound(new { error = "no such contest" });
        purse = (int)got;
    }

    // One writer per (user, contest) at a time: the consolation bound and the
    // won-already checks below are COUNT/EXISTS reads, and two concurrent
    // claims must not both pass them. A transaction-scoped advisory lock
    // serializes exactly this pair and nothing else.
    await using (var l = new NpgsqlCommand(
        "SELECT pg_advisory_xact_lock(hashtextextended($1, 0));", c, tx))
    {
        l.Parameters.AddWithValue(me.ToString() + ":" + contest);
        await l.ExecuteNonQueryAsync();
    }

    bool won;
    await using (var w = new NpgsqlCommand(
        "SELECT EXISTS(SELECT 1 FROM ledger WHERE user_id=$1 AND reason='LEAGUE_PURSE' AND ref=$2);", c, tx))
    {
        w.Parameters.AddWithValue(me); w.Parameters.AddWithValue(contest);
        won = (bool)(await w.ExecuteScalarAsync())!;
    }

    if (kind == "purse")
    {
        if (won)
        {
            // The first-win rule, told straight: re-entry is practice.
            await using var prev = new NpgsqlCommand(
                "SELECT delta, created_at FROM ledger WHERE user_id=$1 AND reason='LEAGUE_PURSE' AND ref=$2;", c, tx);
            prev.Parameters.AddWithValue(me); prev.Parameters.AddWithValue(contest);
            await using var pr = await prev.ExecuteReaderAsync();
            object? already = await pr.ReadAsync()
                ? new { paid = pr.GetInt32(0), at = pr.GetDateTime(1) } : null;
            return Results.Conflict(new
            {
                error = "this contest already paid its first win — re-entry is a practice bout",
                alreadyPaid = already,
            });
        }
        // The client reports inputs; the server names the price. mult is the
        // underdog ratio the client observed, clamped to Career's cap; dealt
        // is damage, clamped to Career's cap. Worst case per contest is
        // purse*1.6 + 100 + 75 by construction.
        var mult = Math.Clamp(double.IsFinite(req.Mult) ? req.Mult : 1.0, 1.0, ECON_UNDERDOG_CAP);
        var dealt = Math.Clamp(double.IsFinite(req.Dealt) ? req.Dealt : 0.0, 0.0, ECON_WIN_DMG_CAP);
        int pay = (int)Math.Round(purse * mult + dealt * ECON_WIN_DMG_K) + ECON_FIRST_WIN_BONUS;

        await using var ins = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, ref, idem_key) VALUES ($1,$2,'LEAGUE_PURSE',$3,$4);", c, tx);
        ins.Parameters.AddWithValue(me);
        ins.Parameters.AddWithValue(pay);
        ins.Parameters.AddWithValue(contest);
        ins.Parameters.AddWithValue("lpurse:" + me + ":" + contest);
        await ins.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return Results.Ok(new { kind = "purse", contest, paid = pay, firstWin = true });
    }

    // The "consolation" and "entry" kinds each lived here for a few hours on
    // 2026-08-13 and are GONE (owen, same day: "remove League's entry fee
    // completely", then "remove loss payment in leagues" — with fees gone, a
    // repeatable loss payment was the last unbounded faucet). Their ledger
    // reasons stay legal — the ledger is append-only and history is not an
    // error — but nothing can write another row of either. The league pays
    // WINS ONLY, once each.
    return Bad("kind must be purse");
}).RequireAuthorization().RequireRateLimiting("economy");

app.MapPost("/v1/economy/purchase", async (PurchaseReq req, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    var op = (req.Op ?? "").Trim().ToLowerInvariant();
    var partId = (req.PartId ?? "").Trim();
    var mat = (req.Mat ?? "").Trim();
    if (partId.Length is < 1 or > 32 || mat.Length is < 1 or > 32) return Bad("partId and mat are required");
    if (string.IsNullOrWhiteSpace(req.IdemKey))
        return Bad("an idempotency key is required — without one a dropped response cannot be retried safely");
    if (op != "buy" && op != "sell") return Bad("op must be buy or sell");

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    // The price comes from the GENERATED seed (011) — the server's copy of the
    // editor's real defs. An id/mat pair the shop never sold is a 404, which
    // is also what retires a part: delete its rows and the server stops
    // selling it even if a stale client still shows a tile.
    int price;
    await using (var p = new NpgsqlCommand(
        "SELECT price FROM part_prices WHERE part_id = $1 AND mat = $2;", c, tx))
    {
        p.Parameters.AddWithValue(partId); p.Parameters.AddWithValue(mat);
        var got = await p.ExecuteScalarAsync();
        if (got is null) return Results.NotFound(new { error = "the shop does not sell that part/material" });
        price = (int)got;
    }
    var refv = partId + ":" + mat;

    if (op == "buy")
    {
        long balance;
        await using (var b = new NpgsqlCommand(
            "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id = $1;", c, tx))
        {
            b.Parameters.AddWithValue(me);
            balance = Convert.ToInt64(await b.ExecuteScalarAsync());
        }
        if (balance < price) return Bad($"that part is {price} scrap and your wallet holds {balance}");

        try
        {
            await using var l = new NpgsqlCommand(
                "INSERT INTO ledger (user_id, delta, reason, ref, idem_key) VALUES ($1,$2,'SHOP_BUY',$3,$4);", c, tx);
            l.Parameters.AddWithValue(me);
            l.Parameters.AddWithValue(-price);
            l.Parameters.AddWithValue(refv);
            l.Parameters.AddWithValue("shop:" + me + ":" + req.IdemKey!.Trim());
            await l.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            return Results.Conflict(new { error = "this purchase was already applied — the idempotency key has been used" });
        }
        await using (var inv = new NpgsqlCommand(@"
            INSERT INTO inventory (user_id, part_id, mat, count) VALUES ($1,$2,$3,1)
            ON CONFLICT (user_id, part_id, mat)
            DO UPDATE SET count = inventory.count + 1, updated_at = now();", c, tx))
        {
            inv.Parameters.AddWithValue(me); inv.Parameters.AddWithValue(partId); inv.Parameters.AddWithValue(mat);
            await inv.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return Results.Ok(new { op, partId, mat, paid = price, balance = balance - price });
    }

    // sell — half back, floor enforced by the ledger's credit guard. The
    // decrement's WHERE count > 0 is the ownership check; the CHECK
    // constraint is the backstop, not the mechanism.
    int refund = (int)Math.Round(price * 0.5);
    await using (var dec = new NpgsqlCommand(@"
        UPDATE inventory SET count = count - 1, updated_at = now()
         WHERE user_id = $1 AND part_id = $2 AND mat = $3 AND count > 0;", c, tx))
    {
        dec.Parameters.AddWithValue(me); dec.Parameters.AddWithValue(partId); dec.Parameters.AddWithValue(mat);
        if (await dec.ExecuteNonQueryAsync() == 0)
            return Bad("you do not own that part in that material");
    }
    try
    {
        await using var l = new NpgsqlCommand(
            "INSERT INTO ledger (user_id, delta, reason, ref, idem_key) VALUES ($1,$2,'SHOP_SELL',$3,$4);", c, tx);
        l.Parameters.AddWithValue(me);
        l.Parameters.AddWithValue(refund);
        l.Parameters.AddWithValue(refv);
        l.Parameters.AddWithValue("shop:" + me + ":" + req.IdemKey!.Trim());
        await l.ExecuteNonQueryAsync();
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        return Results.Conflict(new { error = "this sale was already applied — the idempotency key has been used" });
    }
    await tx.CommitAsync();
    return Results.Ok(new { op, partId, mat, refunded = refund });
}).RequireAuthorization().RequireRateLimiting("economy");

// Reserved so the client can ship against a stable path. Returning 501 --
// not a fake 200 -- is deliberate: crediting scrap on an UNVERIFIED receipt
// would be the exact free-scrap faucet this design exists to close, and a
// verification stub that "works" in dev is how that ships by accident.
app.MapPost("/v1/iap/verify", (ClaimsPrincipal user) =>
    Results.Json(new
    {
        error = "IAP verification is not configured on this deployment — the App Store Server API keys do not exist yet",
    }, statusCode: 501)
).RequireAuthorization().RequireRateLimiting("economy");

// ========================================================= mailing list
// The website's only call to action. Anonymous BY NECESSITY — the whole point
// is the visitor who does not have the game yet — and therefore the only
// endpoint here a stranger can write to without an account, which is why it
// is the most conservative one in the file.
//
// ⚠ IT MUST NOT BE AN ADDRESS ORACLE. "Already subscribed" and "subscribed"
// return the SAME 200 with the same body. Distinguishing them would let
// anyone test whether a given person is on the list, which is a privacy leak
// dressed up as a helpful error message. The upsert below is what makes one
// answer honest for both cases.
app.MapPost("/v1/subscribers", async (SubscribeReq req) =>
{
    var email = (req.Email ?? "").Trim();
    // Deliberately not a regex. Address grammar is famously not one, and every
    // clever pattern this could use rejects somebody's real address; the send
    // path is what finds out whether an address works. Length is bounded
    // because the column is unbounded text.
    if (email.Length is < 3 or > 254 || !email.Contains('@') || email.Contains(' '))
        return Bad("a real email address is required");
    var source = (req.Source ?? "web").Trim();
    if (source.Length > 40) source = source.Substring(0, 40);
    bool updates = req.Updates ?? true, seasons = req.Seasons ?? true;
    if (!updates && !seasons)
        return Bad("choose at least one thing to hear about");

    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(@"
        INSERT INTO subscribers (email, wants_updates, wants_seasons, consent_source, unsub_token)
        VALUES ($1,$2,$3,$4,$5)
        ON CONFLICT (lower(email)) DO UPDATE SET
            wants_updates   = EXCLUDED.wants_updates,
            wants_seasons   = EXCLUDED.wants_seasons,
            -- Coming back through the form is a fresh, explicit opt-in, so it
            -- clears the tombstone and re-dates the consent. Anything else
            -- would leave someone unable to resubscribe after one mis-click.
            unsubscribed_at = NULL,
            consent_at      = now(),
            consent_source  = EXCLUDED.consent_source
        ;", c);
    cmd.Parameters.AddWithValue(email.ToLowerInvariant());
    cmd.Parameters.AddWithValue(updates);
    cmd.Parameters.AddWithValue(seasons);
    cmd.Parameters.AddWithValue(source);
    cmd.Parameters.AddWithValue(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
    await cmd.ExecuteNonQueryAsync();

    // No id, no token, no "you were already on it". Nothing here tells the
    // caller anything about who else is on the list.
    return Results.Ok(new
    {
        ok = true,
        message = "You're on the list. We'll email you when Robot Brawl launches.",
    });
}).AllowAnonymous().RequireRateLimiting("subscribe").RequireCors(SitePolicy);

// ⚠ THE ONLY WAY THE LIST COMES BACK OUT. rb-db has no route from a laptop —
// its authorised-networks list refuses one, correctly — so without this
// endpoint the signups accumulate somewhere owen cannot read, and a mailing
// list nobody can open is not a mailing list. That is the whole reason this
// exists: updates are sent BY HAND for now, which means the addresses have to
// be fetchable by hand.
//
// Behind the worker key, like /v1/admin/metrics. This is the single most
// sensitive read in the API — it is every address anyone ever gave us — so it
// is never anonymous and never behind a mere user token.
app.MapGet("/v1/admin/subscribers", async (HttpContext ctx, string? format, bool? all) =>
{
    if (!WorkerAuthed(ctx)) return Results.Unauthorized();
    // ⚠ UNSUBSCRIBED ROWS ARE EXCLUDED BY DEFAULT, and that default is the
    // point of the tombstone. An export that quietly included them is exactly
    // how a hand-sent update reaches someone who asked to be left alone —
    // there is no send pipeline here to filter them out later, the CSV IS the
    // send list. `all=true` exists for auditing a request, not for mailing.
    var rows = new List<object>();
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "SELECT email, wants_updates, wants_seasons, consent_source, consent_at, "
        + "verified_at IS NOT NULL, unsubscribed_at IS NULL "
        + "FROM subscribers " + ((all == true) ? "" : "WHERE unsubscribed_at IS NULL ")
        + "ORDER BY consent_at;", c);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
        rows.Add(new
        {
            email = r.GetString(0),
            updates = r.GetBoolean(1),
            seasons = r.GetBoolean(2),
            source = r.GetString(3),
            consentAt = r.GetDateTime(4).ToString("o"),
            verified = r.GetBoolean(5),
            subscribed = r.GetBoolean(6),
        });

    if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new { count = rows.Count, subscribers = rows });

    var sb = new System.Text.StringBuilder("email,updates,seasons,source,consent_at,verified,subscribed\n");
    foreach (dynamic x in rows)
        // Addresses cannot contain a comma unquoted, but source is free text
        // that arrives from a query string, so every field is quoted and every
        // quote doubled. A CSV that breaks on one row breaks the whole export.
        sb.Append('"').Append(((string)x.email).Replace("\"", "\"\"")).Append("\",")
          .Append(x.updates).Append(',').Append(x.seasons).Append(",\"")
          .Append(((string)x.source).Replace("\"", "\"\"")).Append("\",")
          .Append(x.consentAt).Append(',').Append(x.verified).Append(',')
          .Append(x.subscribed).Append('\n');
    return Results.Text(sb.ToString(), "text/csv");
}).AllowAnonymous();

// ⚠ UNSUBSCRIBE BY ADDRESS, BECAUSE A HAND-SENT EMAIL CANNOT CARRY A
// PER-RECIPIENT LINK. One message BCC'd to everybody is one body with one
// link in it, so the token flow below — which is correct, and stays for when
// something sends per-recipient mail — cannot be what a manual update uses.
// Without this the form's promise of an unsubscribe would be a promise we
// could not keep.
//
// The trade, stated rather than hidden: anyone who knows an address can
// unsubscribe it. For a game newsletter that is a nuisance and it is
// reversible in one form submission, whereas the alternative — no working
// unsubscribe at all — is the thing that gets a domain blocklisted. It
// answers identically whether or not the address was on the list, so it is
// still not an oracle.
app.MapPost("/v1/subscribers/unsubscribe", async (SubscribeReq req) =>
{
    var email = (req.Email ?? "").Trim();
    if (email.Length is > 0 and <= 254)
    {
        await using var c = await db.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE subscribers SET unsubscribed_at = now() "
            + "WHERE lower(email) = lower($1) AND unsubscribed_at IS NULL;", c);
        cmd.Parameters.AddWithValue(email);
        await cmd.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { ok = true, message = "That address has been removed from the list." });
}).AllowAnonymous().RequireRateLimiting("unsubscribe").RequireCors(SitePolicy);

// One-click unsubscribe. GET on purpose: it is what a mail client can put
// behind a link, and the token IS the authorisation, so there is nothing to
// sign in to. Unknown tokens answer exactly like known ones — see above.
app.MapGet("/v1/subscribers/unsubscribe", async (string? token) =>
{
    if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
        return Results.Ok(new { ok = true, message = "You have been unsubscribed." });
    await using var c = await db.OpenAsync();
    await using var cmd = new NpgsqlCommand(
        "UPDATE subscribers SET unsubscribed_at = now() WHERE unsub_token = $1 AND unsubscribed_at IS NULL;", c);
    cmd.Parameters.AddWithValue(token);
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { ok = true, message = "You have been unsubscribed." });
}).AllowAnonymous().RequireRateLimiting("unsubscribe").RequireCors(SitePolicy);

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
app.MapPost("/v1/admin/league-night/{category}", async (string category, HttpContext ctx, int? top, WorkerTrigger nudge) =>
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
    // One nudge for the whole batch: the worker drains the queue, so N jobs
    // still only need one Unity boot.
    await nudge.NudgeAsync(app.Logger);

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

    // THE TIMING GUARD, 2026-08-12 — what makes this endpoint schedulable.
    // Until now nothing in production ever called this (api_smoke's curl was
    // its only caller, the missing-caller pattern for the third time), and it
    // rolled the season THE MOMENT it was asked. A dumb daily scheduler needs
    // the endpoint itself to know whether the season is actually over:
    //   - no seasons row yet (season 1 has none until something writes it):
    //     start the clock NOW and refuse — the first scheduled tick after
    //     deploy begins season 1 rather than instantly closing it.
    //   - now < ends_at: refuse, 200 not 4xx, because "not due yet" is the
    //     scheduler's NORMAL daily outcome and Cloud Scheduler retries and
    //     alerts on non-2xx — a 409 here would page somebody every midnight.
    //   - ?force=1 skips the clock (api_smoke's staging rollover, an operator
    //     ending a season early). The duplicate-season check below still
    //     applies to forced calls: force skips the CLOCK, never the ledger
    //     protections.
    bool force = ctx.Request.Query.TryGetValue("force", out var fv)
                 && fv.ToString() is "1" or "true";
    if (!force)
    {
        // 003_ratings seeded season 1 ending at a 2099 SENTINEL, explicitly
        // because "§2.4's season rollover does not exist yet". It exists now,
        // so the first unforced tick RETIRES the sentinel — clock starts at
        // that tick, the roll comes season_weeks later. The WHERE keeps this
        // from ever touching a season whose clock is real: a legitimate
        // ends_at is rewritten by nothing but the rollover itself.
        await using (var boot = new NpgsqlCommand(@"
            INSERT INTO seasons (id, starts_at, ends_at)
            VALUES ($1, now(), now() + make_interval(weeks => $2))
            ON CONFLICT (id) DO UPDATE
               SET starts_at = now(),
                   ends_at   = now() + make_interval(weeks => $2)
             WHERE seasons.ends_at = TIMESTAMPTZ '2099-01-01 00:00:00+00';", c))
        {
            boot.Parameters.AddWithValue(from);
            boot.Parameters.AddWithValue(weeks);
            await boot.ExecuteNonQueryAsync();
        }
        await using var due = new NpgsqlCommand(
            "SELECT ends_at, now() >= ends_at FROM seasons WHERE id = $1;", c);
        due.Parameters.AddWithValue(from);
        await using var dr = await due.ExecuteReaderAsync();
        if (await dr.ReadAsync() && !dr.GetBoolean(1))
            return Results.Ok(new { rolled = false, season = from,
                                    endsAt = dr.GetDateTime(0) });
    }

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

    // The season being CLOSED must exist as a row before anything can be
    // recorded against it. `seasons` only ever got rows for NEW seasons, so
    // season 1 — the one ladder_config starts on — never had one, and the
    // first badge insert failed its foreign key and took the whole rollover
    // down with it. A season that is being closed is by definition a season
    // that happened; this makes the table say so.
    //
    // starts_at is unknown for a season nobody recorded, so it is derived from
    // the length that was configured rather than invented: it ran for
    // season_weeks up to now. ON CONFLICT DO NOTHING because every season
    // after this one is inserted properly below.
    await using (var back = new NpgsqlCommand(@"
        INSERT INTO seasons (id, starts_at, ends_at)
        VALUES ($1, now() - make_interval(weeks => $2), now())
        ON CONFLICT (id) DO NOTHING;", c, tx))
    {
        back.Parameters.AddWithValue(from);
        back.Parameters.AddWithValue(weeks);
        await back.ExecuteNonQueryAsync();
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
            // The badge is the PERMANENT half of a season result — the ledger
            // row pays the scrap and is never looked at again, the badge is
            // what a scouting card shows two seasons later. Written inside the
            // same transaction as the payout so a podium can never be paid
            // without being recorded, or recorded without being paid.
            //
            // ON CONFLICT DO NOTHING for the same reason the payout has an
            // idempotency key: (robot, season, category) is the primary key,
            // and a rollover that somehow ran twice must not raise here and
            // roll back a payout that was already correct.
            await using (var badge = new NpgsqlCommand(@"
                INSERT INTO season_badges (robot_id, season_id, category, place, final_rating)
                VALUES ($1,$2,$3,$4,$5) ON CONFLICT DO NOTHING;", c, tx))
            {
                badge.Parameters.AddWithValue(row.Robot);
                badge.Parameters.AddWithValue(from);
                badge.Parameters.AddWithValue(row.Cat);
                badge.Parameters.AddWithValue((int)row.Place);
                badge.Parameters.AddWithValue(row.Rating);
                await badge.ExecuteNonQueryAsync();
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
    return Results.Ok(new { rolled = true, fromSeason = from, toSeason = to,
                            ratingsCarried = carried,
                            compressedToPct = keep * 100, awards });
}).AllowAnonymous();

// ============================================================== cosmetics
// §M3's scrap sink. Every faucet in this economy is live and until now the
// only way scrap could leave a wallet was a stake you usually got back or a
// one-way deposit into the career save.

app.MapGet("/v1/cosmetics", async (ClaimsPrincipal user) =>
{
    await using var c = await db.OpenAsync();
    var owned = new HashSet<string>();
    if (user.Identity?.IsAuthenticated == true)
    {
        await using var o = new NpgsqlCommand(
            "SELECT cosmetic_id FROM user_cosmetics WHERE user_id = $1;", c);
        o.Parameters.AddWithValue(UserId(user));
        await using var r0 = await o.ExecuteReaderAsync();
        while (await r0.ReadAsync()) owned.Add(r0.GetString(0));
    }
    var list = new List<object>();
    await using (var q = new NpgsqlCommand("SELECT id, kind, name, price FROM cosmetics ORDER BY price;", c))
    await using (var r = await q.ExecuteReaderAsync())
        while (await r.ReadAsync())
            list.Add(new
            {
                id = r.GetString(0), kind = r.GetString(1),
                name = r.GetString(2), price = r.GetInt32(3),
                owned = owned.Contains(r.GetString(0)),
            });
    return Results.Ok(new { count = list.Count, cosmetics = list });
}).AllowAnonymous();

app.MapPost("/v1/cosmetics/{id}/buy", async (string id, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    int price;
    await using (var p = new NpgsqlCommand("SELECT price FROM cosmetics WHERE id = $1;", c, tx))
    {
        p.Parameters.AddWithValue(id);
        var got = await p.ExecuteScalarAsync();
        if (got is null) return Results.NotFound(new { error = "no such cosmetic" });
        price = (int)got;
    }

    // The balance IS the ledger (§2.3), read inside the transaction so two
    // concurrent purchases cannot both see the same scrap.
    long balance;
    await using (var b = new NpgsqlCommand(
        "SELECT COALESCE(SUM(delta),0) FROM ledger WHERE user_id = $1;", c, tx))
    {
        b.Parameters.AddWithValue(me);
        balance = Convert.ToInt64(await b.ExecuteScalarAsync());
    }
    if (balance < price)
        return Bad($"{price} scrap and your wallet holds {balance}");

    try
    {
        await using (var own = new NpgsqlCommand(
            "INSERT INTO user_cosmetics (user_id, cosmetic_id) VALUES ($1,$2);", c, tx))
        {
            own.Parameters.AddWithValue(me);
            own.Parameters.AddWithValue(id);
            await own.ExecuteNonQueryAsync();
        }
    }
    catch (PostgresException ex) when (ex.SqlState == "23505")
    {
        // The primary key refuses it, not a code path that could be skipped.
        // Charging twice for a thing you already own is the failure that
        // actually costs a player something.
        return Results.Conflict(new { error = "you already own that" });
    }

    await using (var l = new NpgsqlCommand(
        "INSERT INTO ledger (user_id, delta, reason) VALUES ($1,$2,'COSMETIC');", c, tx))
    {
        l.Parameters.AddWithValue(me);
        l.Parameters.AddWithValue(-price);
        await l.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();
    return Results.Ok(new { bought = id, paid = price, balance = balance - price });
}).RequireAuthorization().RequireRateLimiting("wallet");

app.MapPost("/v1/robots/{robotId:guid}/equip", async (Guid robotId, EquipReq req, ClaimsPrincipal user) =>
{
    var me = UserId(user);
    await using var c = await db.OpenAsync();

    await using (var own = new NpgsqlCommand("SELECT user_id FROM robots WHERE id = $1;", c))
    {
        own.Parameters.AddWithValue(robotId);
        var o = await own.ExecuteScalarAsync();
        if (o is null) return Results.NotFound(new { error = "no such robot" });
        if ((Guid)o != me) return Results.Forbid();
    }

    // You may only wear what you bought. Checked here AND reachable only for
    // your own robot above — a cosmetic you do not own is not a display bug,
    // it is a free item.
    foreach (var (which, val) in new[] { ("plate", req.PlateId), ("title", req.TitleId) })
    {
        if (string.IsNullOrEmpty(val)) continue;
        await using var q = new NpgsqlCommand(
            "SELECT 1 FROM user_cosmetics WHERE user_id = $1 AND cosmetic_id = $2;", c);
        q.Parameters.AddWithValue(me);
        q.Parameters.AddWithValue(val!);
        if (await q.ExecuteScalarAsync() is null)
            return Bad($"you do not own the {which} '{val}'");
    }

    await using (var up = new NpgsqlCommand(@"
        UPDATE robots SET plate_id = COALESCE($2, plate_id), title_id = COALESCE($3, title_id)
         WHERE id = $1;", c))
    {
        up.Parameters.AddWithValue(robotId);
        up.Parameters.AddWithValue((object?)req.PlateId ?? DBNull.Value);
        up.Parameters.AddWithValue((object?)req.TitleId ?? DBNull.Value);
        await up.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { robotId, plateId = req.PlateId, titleId = req.TitleId });
}).RequireAuthorization();

// ====================================================== account deletion
// §M4 lists "privacy policy + account deletion endpoint (store requirement
// even pre-revenue)". Apple and Google both require an in-app path to delete
// an account, and "email us" does not satisfy it.
//
// THE HARD PART IS NOT DELETING, IT IS WHAT MUST SURVIVE. §2.3 makes the
// ledger the audit trail a real-money phase will need, and matches are other
// players' history — a fight you lost does not disappear because your
// opponent left. So this is a REDACTION, not a DELETE:
//
//   gone      email, password hash, display name — everything that identifies
//             a person. The row remains so foreign keys hold.
//   kept      ledger rows and matches, now attached to an anonymous id.
//
// That is the standard shape for "delete my account" against an append-only
// financial record, and it is worth being explicit that it is a choice: a
// hard DELETE would cascade through robots and snapshots and take other
// players' match history with it.
// [FromBody] is required: MapDelete refuses to INFER a body, and the
// confirmation must not move to the query string where it would be
// logged by every proxy between here and the client.
app.MapDelete("/v1/account", async (ClaimsPrincipal user,
                                    [Microsoft.AspNetCore.Mvc.FromBody] DeleteReq req) =>
{
    var me = UserId(user);
    // Deleting an account is irreversible and one fat-fingered client should
    // not do it, so it takes an explicit confirmation string rather than an
    // empty DELETE.
    if (req?.Confirm != "DELETE MY ACCOUNT")
        return Bad("send {\"confirm\":\"DELETE MY ACCOUNT\"} to confirm — this cannot be undone");

    await using var c = await db.OpenAsync();
    await using var tx = await c.BeginTransactionAsync();

    // Retire the robots so nothing of theirs can be challenged or fought
    // after the person has gone, and stand their snapshots down off the
    // ladder. Their PAST matches stay, because they are also somebody else's.
    await using (var r = new NpgsqlCommand(
        "UPDATE robots SET retired = true, name = 'deleted robot' WHERE user_id = $1;", c, tx))
    { r.Parameters.AddWithValue(me); await r.ExecuteNonQueryAsync(); }

    await using (var s = new NpgsqlCommand(@"
        UPDATE snapshots SET status = 'SUPERSEDED'
         WHERE status = 'ACTIVE'
           AND robot_id IN (SELECT id FROM robots WHERE user_id = $1);", c, tx))
    { s.Parameters.AddWithValue(me); await s.ExecuteNonQueryAsync(); }

    // Ratings are per robot and would keep a retired robot on the board.
    await using (var ra = new NpgsqlCommand(
        "DELETE FROM ratings WHERE robot_id IN (SELECT id FROM robots WHERE user_id = $1);", c, tx))
    { ra.Parameters.AddWithValue(me); await ra.ExecuteNonQueryAsync(); }

    // The redaction itself. email_lower is generated from email, so writing a
    // unique placeholder keeps the unique index satisfied without keeping
    // anything that identifies anyone.
    //
    // ⚠ display_name GETS THE SAME TREATMENT AND IT IS NOT COSMETIC. It used
    // to be the constant 'deleted player', which was fine while nothing
    // enforced uniqueness on it. Since 015 added users_display_name_lower_key,
    // a constant here means THE SECOND ACCOUNT DELETION EVER ATTEMPTED fails
    // on a 23505 and rolls back the whole transaction — a player who asks to
    // be forgotten is told no, because someone else already was. The suffix is
    // the first 8 of the account's own uuid, which is already in the row, so
    // it identifies nobody new. 'deleted ' + 8 = 16 chars, inside the 2-24
    // CHECK.
    await using (var u = new NpgsqlCommand(@"
        UPDATE users SET email = 'deleted+' || id::text || '@deleted.invalid',
                         pw_hash = 'deleted',
                         display_name = 'deleted ' || left(replace(id::text, '-', ''), 8)
         WHERE id = $1;", c, tx))
    { u.Parameters.AddWithValue(me); await u.ExecuteNonQueryAsync(); }

    await tx.CommitAsync();
    return Results.Ok(new
    {
        deleted = true,
        kept = "ledger rows and match history, now anonymous — the ledger is an audit trail (§2.3) and a match is also the other player's record",
    });
}).RequireAuthorization().RequireRateLimiting("wallet");

// ================================================================== blobs
// The read side of object storage. Stored URIs are s3://, which nothing can
// fetch; this is where the bytes actually come out.
//
// §1.3 DECIDES THE AUTHORISATION, and it splits by prefix:
//
//   replays/     PUBLIC. M2's acceptance is explicit that "a third account
//                can scout both and WATCH THE REPLAY" — and it is tested
//                anonymously, so this cannot require a token.
//   everything   WORKER KEY. snapshots/ holds robot payloads. "It never
//   else         leaks a program to anyone but its owner" is the rule the
//                whole API is shaped around, and the only thing that ever
//                legitimately reads a payload is a worker.
//
// Serving bytes through the API rather than handing out signed URLs is a
// deliberate §7 rule-4 choice: signed URLs are storage-vendor specific, and
// this keeps the critical path portable. At prototype traffic the bandwidth
// is irrelevant; if that stops being true, signed URLs go HERE, behind the
// same authorisation, and no caller changes.
app.MapGet("/v1/blobs/{**key}", async (string key, HttpContext ctx, IBlobStore blobs) =>
{
    if (string.IsNullOrEmpty(key)) return Results.NotFound();
    // A key is server-generated, but this one arrives in a URL. Refuse
    // traversal before it reaches a store that might resolve it.
    if (key.Contains("..", StringComparison.Ordinal)) return Results.NotFound();

    bool isReplay = key.StartsWith("replays/", StringComparison.Ordinal);
    if (!isReplay && !WorkerAuthed(ctx)) return Results.Unauthorized();

    var bytes = await blobs.GetAsync(key);
    if (bytes is null) return Results.NotFound();

    // Replays are immutable once written, so they cache hard. Payloads are
    // private and must not sit in an intermediary.
    ctx.Response.Headers.CacheControl = isReplay
        ? "public, max-age=31536000, immutable"
        : "no-store";
    return Results.File(bytes, key.EndsWith(".gz", StringComparison.Ordinal)
        ? "application/gzip" : "application/octet-stream");
}).AllowAnonymous();

app.Run();

// ------------------------------------------------------------------ dtos
public record SubscribeReq(string? Email, bool? Updates, bool? Seasons, string? Source);
public record RegisterReq(string? Email, string? Password, string? DisplayName);
public record LoginReq(string? Email, string? Password);
public record RobotReq(string? Name);
public record SnapshotReq(Guid RobotId, string? Envelope);
public record ClaimReq(string? WorkerId, string? Kind);
public record ChallengeReq(Guid ChallengerSnapshotId, Guid DefenderSnapshotId);
public record ReplayReq(string? Replay);
public record DepositReq(int Amount, string? IdemKey);
public record EconClaimReq(string? Kind, string? ContestId, double Dealt, double Mult, string? AttemptId);
public record PurchaseReq(string? Op, string? PartId, string? Mat, string? IdemKey);
public record EquipReq(string? PlateId, string? TitleId);
public record DeleteReq(string? Confirm);
public record ResetPwReq(string? Email, string? NewPassword);
public record FightResult(Guid MatchId, string? WorkerId, string? Verdict,
                          string[]? ReplayUrls, string[]? Bouts);
public record ValidateResult(
    Guid SnapshotId, string? WorkerId, bool Legal, int MassKg,
    float AabbX, float AabbY, float AabbZ, string? Category,
    List<string>? PartsManifest, string? ProgramHash, string[]? FailReasons);
