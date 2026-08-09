# M1 — the API compiles, boots, and passes end to end — 2026-08-09

Companion to `M1_API_And_Schema_2026-08-08.md`, which ended with the API
described as "a careful draft" that had **never been compiled**.

**It now compiles, boots against real PostgreSQL, and passes a 35-check
end-to-end bench with a clean server log.** Four bugs were found getting
there, one claim was retracted, and the bench itself had to be fixed
before it could be believed.

## Where M1's server half stands

| | |
|---|---|
| compiles | yes — real Npgsql + JwtBearer, 0 errors 0 warnings |
| boots | yes — `applying migration 001_init`, then listening |
| schema | `sql_bench.sh` **34/34** vs PostgreSQL 16 |
| endpoints | `api_smoke.sh` **35/35**, 0 skipped, no exceptions logged |

Remaining before M1 acceptance: **the sim worker**. Everything it needs to
talk to now exists and is proven.

## The four bugs

### 1. The Dockerfile could not have built

`RUN useradd -u 10001 -m app` — .NET 8 Linux images **pre-create a
non-root `app` user**, and Microsoft documents the "user already exists"
failure as a breaking change. Now `RUN id -u app >/dev/null 2>&1 ||
useradd -u 10001 -m app`, written as a guard so it still works on an
older base.

*Not yet exercised* — owen has no Docker, so the local run is `dotnet
run`, not the image. The Dockerfile is fixed but unproven.

### 2. validate-result raised the new snapshot before standing down the old

Set the new snapshot `ACTIVE` and only then marked the previous one
`SUPERSEDED`, in one transaction, reasoning that "there is never a moment
with two or none."

Wrong. `snapshots_one_active_per_robot` is a **plain partial unique
index**, enforced **per statement, not at COMMIT**. The first UPDATE put
two `ACTIVE` rows on one robot and raised 23505. **Every robot's second
upload would have failed.**

Now supersede-then-activate. Guarded in **two** places: `sql_bench.sh`
section H proves the order in SQL, `api_smoke.sh` section H proves it
through the endpoint.

`sql_bench.sh` was 31/31 green straight across this bug, because it
proved the *index* while the bug was in the *endpoint's statement order*.
**A constraint being correct says nothing about whether callers order
their statements to satisfy it.**

### 3. `MapInboundClaims` turned every authenticated endpoint into a 500

Found by running the bench, not by reading. `POST /v1/robots` returned
**500** with a token and a correct **401** without one — so authentication
worked and something inside the handler threw.

`JwtBearerOptions.MapInboundClaims` **defaults to `true`**, which rewrites
the token's own claim names into WS-Federation URIs: `sub` becomes
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`.
Every read in `Program.cs` asks for `"sub"` by its real name, so
`UserId()` was `Guid.Parse(null)` — an unhandled `ArgumentNullException`,
hence a bare 500 with an empty body.

Fixed with `o.MapInboundClaims = false;` on the JWT handler.

**Fixed at the handler, not at the reads, on purpose.** `sub` is read in
two places: the ownership check, which failed loudly, and the `upload`
rate-limiter partition key, which failed **silently** — `FindFirstValue
("sub") ?? ctx.Connection.RemoteIpAddress` was quietly falling back to
per-IP partitioning. Patching `UserId()` would have fixed the 500 and left
the limiter partitioning by IP instead of by user indefinitely. When one
symptom is loud and another is silent, fix the shared cause.

### 4. sha256 hex case (defensive)

The column CHECKs `^[0-9a-f]{64}$` and the gate compares against a
lowercased hash, so an uppercase envelope would fail twice with a
confusing message. Now lowercased on read.

Cross-checked the seam: Unity's `RobotSnapshot.Sha256Hex` hashes
`Encoding.UTF8.GetBytes(payload)` and formats `x2` — lowercase; the API
hashes the same bytes and lowercases. **They agree.** Belt-and-braces,
not a repair. `api_smoke.sh` builds its envelope from first principles
rather than a fixture, so a future divergence in *what* is hashed fails
the bench instead of passing on a copied constant.

### Retracted: the `aabb` ternary is NOT CS0173

Claimed `aabb = r.IsDBNull(3) ? null : new { … }` was a compile error and
"fixed" it with an `(object)` cast. **Wrong** — `null` converts implicitly
to an anonymous *reference* type, so the conditional has a natural type.
CS0173 needs two operands where neither converts to the other. **Reverted**:
a change made on a false premise should not sit in the tree looking like a
fix. A careful read is not a compile.

## The bench had to be fixed before it could be believed

`api_smoke.sh`'s first run reported **17 passed, 17 failed**. One of those
seventeen failures was real (bug 3). The other sixteen, and — worse —
three of the *passes*, were the bench's fault.

**Vacuous passes.** `[ "$JSNAP" = "$SNAP1" ]` with both variables empty is
**true**. "…pointing at the snapshot just uploaded" reported PASS having
compared nothing, and both payload-leak greps passed against an empty
405 body. **A bench that passes on nothing is worse than one that fails,
because it gets believed.** There is now a `same()` helper that refuses to
pass on empty, an `is()` for single values, and the leak checks do not run
at all unless the card actually returned 200.

**Cascade.** One 500 produced sixteen downstream failures and buried its
own cause: an empty `$ROBOT` made `robotId` empty (400), so no job existed
to claim (204), so every job id was empty (404) — and `GET /v1/snapshots/`
with an empty id fell through to the `POST /v1/snapshots` route, giving
405s that looked like a routing bug. Failed prerequisites now **SKIP**
their dependants, counted and printed, never silent. The summary line now
says how many things are broken rather than how many are downstream of
one thing.

Both rules are written into the file's header, as rules rather than fixes.

Also: `UID` is a **readonly** variable in bash. Renamed.

## New files

- **`server/tests/api_smoke.sh`** (35 checks) — the end-to-end bench.
  Covers registration and the login enumeration-oracle property, robot
  ownership, opaque upload and the sha gate, the worker key gate and job
  ownership, the validate round trip, §1.2 supersede, and §1.3's privacy
  boundary asserted against a real anonymous HTTP response.
- **`server/tests/run_local.sh`** — one command: reset the dev database,
  build, boot, wait for `/healthz`, run the smoke, stop, and leave
  `qa_api_server.log` + `qa_api_smoke.txt` on disk. It resets because a job
  left `READY` by a failed run is the job the next run claims.

### Why run_local.sh writes files instead of printing

**This is the working pattern for a Cowork session on this project.** The
session cannot run anything on owen's Mac (see below), but it *can* read
files in the project folder. So every local run redirects into
`qa_*.txt`/`*.log` beside the code, owen runs one command and says "done",
and the session reads the result itself. No copying terminals into chat.
It matches what the Unity benches already do with `Assets/Phase1/qa_*.txt`.

## Building without NuGet (the sandbox trick)

NuGet is unreachable from the cloud sandbox — every nuget.org host and
`dotnetcli.azureedge.net` return nothing. But the **Ubuntu archive is
reachable and carries `dotnet-sdk-8.0`**, and **ASP.NET Core ships inside
the SDK**, so only `Npgsql` and `Microsoft.AspNetCore.Authentication.
JwtBearer` are missing.

That surface is small enough to stub: ~140 lines of signature-faithful
stand-ins, the API project's two `PackageReference`s swapped for a
`ProjectReference`, and a `nuget.config` with `<clear />` to keep restore
offline. **Parameter names matter**, not just types — `Tokens.Issue`
constructs `JwtSecurityToken` with named arguments, so the stub mirrors
the real signature including the `notBefore` it never passes.

**Every shim signature matched reality** — the first real restore on
owen's Mac produced zero errors. The one assumption worth naming was
`NpgsqlParameterCollection.AddWithValue(object)`, the single-argument
positional overload used at ~30 call sites; verified against the published
API reference before relying on it.

Useful again for any C# work in this sandbox. Proves everything that is
not an Npgsql or JWT *signature*; proves nothing about their *behaviour* —
`MapInboundClaims` is exactly the class of bug it cannot see.

## Still open

- **`/v1/auth/register` is not transactional.** The user INSERT and the
  500-scrap `SIGNING_BONUS` ledger INSERT share a connection with no
  transaction, so a failure between them leaves an account that cannot
  afford a challenge — contradicting the endpoint's own comment that
  "registered" and "can afford a challenge" are the same state. Held back
  before the first compile; **safe to do now**, and there is a bench to
  re-run.
- **The Docker image is unproven** (bug 1 fixed but unexercised).
- **Secret Manager** — `deploy_api.zsh` still expects `rb-jwt-secret`,
  `rb-worker-key`, `rb-pg-conn`; they do not exist.
- **The sim worker** — the last piece before an end-to-end cloud fight.

## Environment — 2026-08-09

- **The Unity MCP bridge was NOT available.** No `Unity_*` tools and no
  `mcp__remote-devices__unity__*` either; only `telegram-notify` proxied
  through. Unity was closed at session start. **All Unity-side work was
  blocked all session** — the mirror lock, MatrixBench's FLOOR margin, the
  chassis sweep. Probe for these tools first; their absence changes the
  whole shape of a session.
- **`device_bash` is NOT owen's Mac.** Confirmed empirically, not assumed:
  it is a separate Linux VM (aarch64) with only the project folder
  mounted. No `dotnet`, no `psql`, no Homebrew, **no network at all**, and
  **no route to the Mac's `localhost:5000`** (tried loopback,
  `host.docker.internal`, and the default gateway). `git` exists there but
  must still never be used — the lock-file trap is unchanged.
  **Consequence: every command touching the macOS shell must come from
  owen.** For a session that is mostly server/toolchain work rather than
  file editing, Claude Code CLI on the Mac is the better tool.
- **The folder grant worked immediately again**, no Telegram ping needed.
  Second session running; it looks reliable now.
- **owen's Mac now has `dotnet@8` (8.0.129) and `postgresql@16` via brew**,
  both keg-only — `run_local.sh` puts them on PATH and sets `DOTNET_ROOT`.
  Local database `rb`/`rb`. **Still no Docker.**
- **Sandbox proxy**: allows the Ubuntu archive, pypi, npm; blocks all of
  nuget.org, `dotnetcli.azureedge.net`, `download.docker.com`.
- **Git**: M0 landed as `54f8d0f` on `session/2026-08-05-rotor-pool-shop`.
  `main` is still the initial commit `8320343`, far behind. `_to_delete/`
  is gone. No stale `index.lock`. **Everything from today is uncommitted.**

## Backups

`_claude_backups/`: `m1c__Dockerfile`, `m1c__Program.cs`,
`m1c__sql_bench.sh`, `m1d__Program.cs` (pre-MapInboundClaims).
