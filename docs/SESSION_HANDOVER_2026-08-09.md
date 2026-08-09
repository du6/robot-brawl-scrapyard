# SESSION HANDOVER — 2026-08-09 → next session

**Read this first.** Robot Brawl: Bolt & Blade — owen's physics robot
construction/combat game, built live in his Unity editor via the Unity
MCP bridge. CURRENT handover; supersedes `SESSION_HANDOVER_2026-08-08.md`
and everything before it.

**Two headlines.** M1's server half went from *never compiled* to
compiled, booted and passing end-to-end. And a bench that was merely
"flaky" turned out to be reporting a real bug in the tutorial's first
fight.

## Where things stand

Three bodies of work. Read the record before touching the code.

1. **`M1_API_First_Compile_2026-08-09.md`** — the API compiles, boots and
   passes; how it was compiled without NuGet; four bugs and one
   retraction.
2. **`Opening_Disarm_Tutorial_Floor_2026-08-09.md`** — the opening
   exchange disarms *and inverts* the player in ~⅓ of League 1 bouts.
   **The most important finding of the day.**
3. Carried forward: `M1_API_And_Schema_2026-08-08.md`,
   `M0_Snapshot_Replay_MatchRunner_2026-08-08.md`,
   `Weapon_Trade_Fix_Shipped_2026-08-08.md`,
   `Ladder_Sweep_Weapon_Trade_2026-08-08.md`,
   `Multiplayer_V3_Design_Doc.md`.

### M1 server half — DONE and PROVEN

| | |
|---|---|
| compiles | real Npgsql + JwtBearer, 0 errors 0 warnings |
| boots | `applying migration 001_init`, then listening on :5000 |
| schema | `sql_bench.sh` **34/34** vs PostgreSQL 16 |
| endpoints | `api_smoke.sh` **35/35**, 0 skipped, **no exceptions logged** |

Four bugs fixed: the `Dockerfile`'s `useradd app` (the .NET 8 base image
already ships that user); validate-result **activating before
superseding** (a plain partial unique index is enforced per statement, so
every robot's *second* upload died on 23505); **`MapInboundClaims`**
defaulting true, which rewrote `sub` and turned every authenticated
endpoint into a bare 500; and sha256 hex case. One claim **retracted** —
the `aabb` ternary is not CS0173.

New: **`server/tests/api_smoke.sh`** (35 checks) and
**`server/tests/run_local.sh`** (one command: reset DB, build, boot, run
smoke, stop, leave logs on disk).

### Unity — MatrixBench diagnosed, one fix shipped

`MatrixBench` gained **per-bout verdict logging** and
**`MatrixBench.RunFloor()`** (10 bouts, ~1 min, writes
`qa_matrix_floor.txt`). `FightManager.Judge` gained
**`bool bothDisarmed`**, which suppresses the aggression tie-break when a
bout ended because both machines were disarmed.

## ⚠ Things that are NOT done, stated plainly

**1. The FLOOR check cannot discriminate. Do not treat a FLOOR failure as
a regression.** It ran 6/10, 8/10 and 7/10 today with no relevant code
change. Brawler wins **14/14 bouts in which a fight actually happens**;
FLOOR measures how often the opening exchange disarms, not whether the
preset is good. `>= 7` sits at the true rate, so it fails ~half the time
at **any** sample size. Promise and alarm cannot be the same number.

**2. The opening disarm is unfixed.** The aggression change made the
verdict *honest*, not different: the disarmed bouts now read "decided on
control: you spent the match on your back" and are **still losses**,
because the opening exchange **flips Brawler as well as disarming it**.
The cure is the presets' approach/steering — do not meet nose-to-nose at
full closing speed. `WEAPON_VS_WEAPON = 0.25` never had a chance at this;
the trade happens in the first two seconds and the constant governs only
what follows.

**3. The Docker image has never been built.** owen has no Docker. The
`useradd` fix is correct per Microsoft's own breaking-change page but
**unexercised**.

**4. `/v1/auth/register` is not transactional.** The user INSERT and the
500-scrap `SIGNING_BONUS` ledger INSERT share a connection with no
transaction, so a failure between them leaves an account that cannot
afford a challenge — contradicting the endpoint's own comment. Held back
before the first compile; **safe to do now**, and there is a bench to
re-run.

**5. Secret Manager** — `deploy_api.zsh` still expects `rb-jwt-secret`,
`rb-worker-key`, `rb-pg-conn`. They do not exist.

**6. `BuilderManager.OnGUI()` line 6075** reads `Career.Data.doneContests`
with no null guard. Any bench that restores `Career.Data` on teardown
throws two NREs per play-mode transition. Editor-only, cosmetic, real.

## ⚠ Git — TWO COMMITS LANDED, AND THE REPO IS WRONG

owen committed twice: `24ff379`, then **`a456ac0`** (current HEAD) on
`session/2026-08-05-rotor-pool-shop`. `main` is still the initial commit
`8320343` and is far behind.

**Found by reading `.git/index` directly, after the commits:**

- **`server/RobotBrawl.Api/RobotBrawl.Api.csproj` IS NOT TRACKED.**
  `.gitignore` ignores `*.csproj` because *Unity* regenerates its project
  files — but the server's csproj is hand-written and is the only thing
  that knows the API's package references. **The repo contains every
  source file, every build artifact, and nothing that can build them. A
  fresh clone cannot compile the API.**
- **2.8 MB of .NET build output IS tracked** — twelve DLLs (Npgsql, the
  whole IdentityModel stack), `apphost`, the compiled assembly, MSBuild
  caches, and `project.assets.json`, which hard-codes `/Users/leondu`
  paths. `/[Oo]bj/` and `/[Bb]uild/` are **root-anchored**, so they never
  covered `server/**/obj/`, and there was no `bin/` rule at all.

**`.gitignore` is already fixed** (backup:
`_claude_backups/gitignore__before_server_rules`) with `server/**/bin/`,
`server/**/obj/`, `!server/**/*.csproj` and the run log. **The index
still needs correcting — owen runs this from Terminal:**

```
cd "/Users/leondu/Setup Guide In-Editor Tutorial"
git rm -r --cached server/RobotBrawl.Api/bin server/RobotBrawl.Api/obj
git add .gitignore
git add -f server/RobotBrawl.Api/RobotBrawl.Api.csproj
git commit -m "Untrack .NET build output; track the hand-written server csproj"
```

⚠ Still true: **no `origin` remote** — every push goes by full URL with
the token from `.gh_token.local`. **Never force-push.** And **never run
`git` over the bridge mount** — it cannot unlink its own lock files.

## Environment & protocols — SEVERAL CHANGES

### ⚠ The Unity namespace trap is `RobotBrawl.Phase0.`

Every previous handover named "the RunCommand namespace trap" without
saying what it was. **It cost four calls to rediscover, so it is written
down now.** Game types are in namespace `RobotBrawl.Phase0`:

- `BuilderManager` → CS0246.
- `global::BuilderManager` → CS0400 "missing an assembly reference",
  which is misleading — the assembly is loaded, the type is namespaced.
- **`RobotBrawl.Phase0.BuilderManager` → works.**

Other RunCommand facts learned today:

- **`result.Log` only substitutes `{0}` and `{1}`.** A `{2}` prints
  literally, format specifiers and all. Pre-format anything longer.
- The namespace allowlist rejects the **literal text**
  `System.Reflection.BindingFlags`, but `System.AppDomain.CurrentDomain.
  GetAssemblies()` and single-argument `GetField`/`GetMethod` are fine.
  Reflection is available as long as you never type that namespace.
- The tool wraps your class in `namespace Unity.AI.Assistant.Agent.
  Dynamic.Extension.Editor`, which is why unqualified game types fail.

### ⚠ The Unity MCP bridge needs a FULL desktop restart

Unity's *Project Settings → Unity MCP Server → Integrations* showed
"Claude Desktop — Configured" while this session saw **no Unity tools at
all**. Claude Desktop spawns MCP servers **at app launch**, so a config
Unity wrote afterwards is never started. **Cmd-Q the desktop app (not
just close the window) with Unity already running, reopen, start a fresh
session.** After that the tools appear as
`mcp__remote-devices__unity-mcp__Unity_*`.

The bridge still **drops constantly** — three times today. Wait 60–70 s,
verify state, retry the same command. Unchanged.

### ⚠ `device_bash` is NOT owen's Mac — confirmed empirically

A separate Linux VM (aarch64) with only the project folder mounted. **No
`dotnet`, no `psql`, no Homebrew, no network at all, and no route to the
Mac's `localhost:5000`** — loopback, `host.docker.internal` and the
default gateway were all tried. `git` exists there and must still never
be used.

**Consequence: every command touching the macOS shell must come from
owen.** For a session that is mostly server/toolchain work rather than
file editing, **Claude Code CLI on the Mac is the better tool.**

### The file-handoff pattern (use this)

owen does not want to paste terminal output. `run_local.sh` redirects
everything into `server/qa_api_server.log` and `server/qa_api_smoke.txt`,
which the session **can read**. owen runs one command, says "done", the
session reads the result. Same convention as `Assets/Phase1/qa_*.txt`.
**Apply it to anything new that must run on his machine.**

### owen's Mac now has

`dotnet@8` (8.0.129) and `postgresql@16` via brew, **both keg-only** —
`run_local.sh` puts them on PATH and sets `DOTNET_ROOT`. Local database
`rb`/`rb`. **Still no Docker**, and it is not blocking anything yet.

### Compiling C# in the cloud sandbox without NuGet

NuGet is unreachable, but the **Ubuntu archive carries `dotnet-sdk-8.0`**
and **ASP.NET Core ships inside the SDK**, so only Npgsql and JwtBearer
are missing — a surface small enough to stub. ~140 lines of
signature-faithful stand-ins, `PackageReference` swapped for
`ProjectReference`, `nuget.config` with `<clear />`. **Every shim
signature matched reality.** Proves everything that is not an Npgsql or
JWT *signature*; proves nothing about their *behaviour* —
`MapInboundClaims` is exactly the class of bug it cannot see. Sandbox
proxy allows the Ubuntu archive, pypi and npm; blocks nuget.org,
`dotnetcli.azureedge.net`, `download.docker.com`.

### The folder grant works, immediately

Second session running, no Telegram ping needed. Treat it as reliable.

### Career save

md5 **`18614d0e6603869f28fb65992b7d1484`**, mtime still 08-05 21:57 —
**verified against the 08-08 handover's fingerprint and unchanged.**
Backed up to `career_backups/career_2026-08-09_0914_m1api.json` and the
matching profile. Live save lives at
`~/Library/Application Support/owen/Robot Brawl_ Bolt & Blade/`.

## Bench inventory

**Run today, green:** `sql_bench.sh` (34) · `api_smoke.sh` (35) ·
MatrixBench (9/9 on run 2; 8/9 on run 1 — FLOOR only).

**NOT run today — stale-green, verify before trusting:** VerbBench (32) ·
ProgramBench (40) · AutonomyBench (24) · ReplayBench (57) ·
LadderSweepBench (30 bouts) · CareerSmoke (128) · CanvasDragBench (31) ·
TestDebugBench (30) · TouchSmoke · HazardBench · SensorProbe ·
CareerBench.

- **`MatrixBench.RunFloor()`** — FLOOR alone, ~1 min. Use it for any
  approach/steering work; the per-bout lines say whether bouts stopped
  being *disarmed* or merely stopped being *lost*.
- **`api_smoke.sh`** — needs the API running; `run_local.sh` does both.
- **`LadderSweepBench`** — a MEASUREMENT bench. Any weapon-trade change
  goes through it before and after.
- ⚠ **CareerSmoke is not isolated.** Run it first or in its own play
  session, or it reports 115/128.

## Next direction

1. **Fix the git index** (the four commands above). The API is currently
   unbuildable from a clone.
2. **The opening disarm** — approach/steering so the presets do not meet
   nose-to-nose at full closing speed. This is now *one* problem wearing
   three hats: the mirror lock, the tutorial floor, and the weapon trade.
   Measure with `RunFloor()` and `LadderSweepBench`.
3. **The sim worker** — the last piece before an end-to-end cloud fight,
   and much better defined now there is a live, proven API to point it
   at. Linux Dedicated Server Build Support is installed. Start on Mono.
4. **`/v1/auth/register` transaction** — small, safe, benched.
5. **Re-run the stale-green benches**, CareerSmoke first.
6. **The chassis sweep** (needs a second and third fixture rig) — now
   doubly motivated, since weapon geometry and mounting is exactly what
   decides whether the opening exchange shears both weapons.
7. Carried over, still open: `WallShy`'s 1.5 s retreat burst wants a
   balance eye · mismatched wheel roll axes make a legal build behave
   backwards · braking overshoot 62% not zero · `SIDE TO BACK TO` lands
   ~18° out against its own 8° tolerance · critic loop 7's open list.
8. **Roblox port scoping** — wants a fresh session.

## First moves

1. Health RunCommand (`isPlaying`, `isCompiling`) → probe owen before
   taking the editor. **Owner state is sacred.**
2. Timestamped career backup; verify md5 vs **`18614d0e`**.
3. Fix the git index.
4. `zsh server/tests/run_local.sh` to confirm the API still passes 35/35.
5. Then the disarm.

## Method notes worth keeping

**When a check fails, ask whether the CHECK is wrong before changing the
product — but do not stop there.** Today both were wrong, in order: the
FLOOR check could not discriminate, *and* underneath the noise sat a real
bug it was never designed to see. "It's just noise, the bench is flaky"
would have been correct about the number and would have missed the
disarm. **The flakiness was the signal** — a bimodal outcome is what a
flaky pass/fail looks like from outside.

**State the prediction before the re-run.** Predicting "the disarmed
losses become draws" is what made the verification informative: they
stayed losses *for a different reason*, which is how the flip was found.
A fix that lands for the wrong reason looks identical to one that works,
unless the expectation was written down first.

**A bench that passes on nothing is worse than one that fails.**
`api_smoke.sh`'s first run reported three PASSes that had compared two
empty strings. It now has a `same()` that refuses to pass on empty, and
failed prerequisites SKIP their dependants instead of failing them — one
500 had produced seventeen failures and buried its own cause.

**Fix the shared cause, not the loud symptom.** `MapInboundClaims` broke
the ownership check *loudly* (500) and the rate-limiter partition
*silently* (fell back to per-IP). Patching `UserId()` would have fixed
the visible one and left the invisible one for months.
