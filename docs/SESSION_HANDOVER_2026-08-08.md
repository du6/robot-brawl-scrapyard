<!-- Mirrored from the claude.ai Project (path: claude/SESSION_HANDOVER_2026-08-08.md) on 2026-08-08.
     The Project copy is the source of truth; edit there and re-mirror. -->

# SESSION HANDOVER — 2026-08-08 → next session

**Read this first.** Robot Brawl: Bolt & Blade — owen's physics robot
construction/combat game, built live in his Unity editor via the Unity
MCP bridge. CURRENT handover; supersedes `SESSION_HANDOVER_2026-08-07.md`
and everything before it. A long session: **Multiplayer v3 went from a
design doc to M0 shipped, a measured balance finding fixed, and M1's
server half written.**

## Where things stand

Four bodies of work, in order. Each has its own record — read the record
before touching the code it describes.

1. **`M0_Snapshot_Replay_MatchRunner_2026-08-08.md`** — Phase M0 SHIPPED.
   Snapshot envelope, replay recorder + player, MatchRunner, ReplayBench.
2. **`Ladder_Sweep_Weapon_Trade_2026-08-08.md`** — the 30-bout sweep that
   found the weapon trade decides every fight.
3. **`Weapon_Trade_Fix_Shipped_2026-08-08.md`** — options 1, 2 and 3
   shipped and measured before/after.
4. **`M1_API_And_Schema_2026-08-08.md`** — the ladder API, schema and
   queue, under `server/`.

Design doc: `Multiplayer_V3_Design_Doc.md` (§10 all resolved).

### New code

**Unity** (`Assets/Phase1/Scripts/`): `RobotSnapshot.cs` ·
`ReplayRecorder.cs` · `ReplayPlayer.cs` · `MatchRunner.cs` ·
`ReplayBench.cs` · `LadderSweepBench.cs`.

**Seams cut into existing files** — small on purpose; a parallel spawn
path is how you get a robot that fights differently from the one the
builder drew:

- `BuilderManager.SpawnBot` → public
- `BuilderManager.DriveDir` — accessor for the private `driveDir`
- `BuilderManager.EnterMatchArena(float)` — StartFight's preamble, no combatants
- `VerbBench.ARMED` → public (the shared fixture rig)
- `FightManager.DrawBand` → public (one definition of "is this a margin")
- `FightManager.TickStalemate()` + `Judge(string earlyNote)`
- `DamageResolver.WEAPON_VS_WEAPON = 0.25f`

**Server** (`server/`, new): `RobotBrawl.Api/` (Program.cs, Infra.cs,
Migrations/001_init.sql, Sql/×4), `tests/sql_bench.sh`, `Dockerfile`,
`docker-compose.yml`, `scripts/gcp_bootstrap.zsh`,
`scripts/deploy_api.zsh`. Plus `push_m0_main.zsh` in the project root.

### Green at close

**Unity (iPad sim):** ReplayBench **57/57** · LadderSweepBench **31/31**
· VerbBench **32/32** · ProgramBench **40/40** · AutonomyBench **24/24**
· MatrixBench **9/9** · CareerSmoke **128/128**.

**Server:** `tests/sql_bench.sh` **31/31** against real PostgreSQL 16.

NOT run this session — **stale-green, verify before trusting**:
CanvasDragBench, TouchSmoke, TestDebugBench, HazardBench.

**Career save:** md5 **18614d0e**, mtime unchanged (08-05 21:57),
verified three times.

**Editor left:** NOT in play mode, compile clean, builder in Build mode.

## ⚠ Three things that are NOT done, stated plainly

**1. The C# API has never been compiled.** NuGet is unreachable from the
cloud sandbox, so Npgsql and the JWT package could not be restored. The
SQL is tested; the C# is a careful draft. **First real compile is
`docker compose up` in `server/` on owen's Mac** — expect to fix errors
there. Do not report the API as working until it has built.

**2. Mirror lock is unsolved.** Brawler v Brawler, WallShy v WallShy and
Rusher v Rusher still meet nose-to-nose, mutually disarm and Draw. The
weapon multiplier softened it (4 hits over 5 s became 10 over 15 s) but
did not change the kind of outcome — and **0.10 was tested and rejected**
because it only stretched the grind to 41 s with the same result. The
cause is the nose-to-nose lock: two programs that both charge and hold.
The fix belongs in the presets' approach/steering, not in a damage
constant. Measurable now via `LadderSweepBench`.

**3. MatrixBench's FLOOR passed at exactly its threshold** — Brawler
clears the L1 sample 7/10 against a `>= 7` requirement. Green with no
margin, and the weapon change is the kind that moves it. Re-run it early
and watch that number.

Smaller: one bout still runs the full clock (Rusher v Statue seed 202,
85.8 s dead air) because **both sides kept their weapons** — the
stalemate rule deliberately does not fire while more damage is
physically possible. Correct by the rule, still a gap.

## Git — UNCOMMITTED

Everything from this session is uncommitted. Last check-in `1bc61a0`
2026-08-08 08:24 on `session/2026-08-05-rotor-pool-shop`.

**`push_m0_main.zsh` is written, syntax-checked, and carries a full
commit message.** owen runs it **from Terminal on the Mac**:

```
cd "/Users/leondu/Setup Guide In-Editor Tutorial" && zsh push_m0_main.zsh
```

⚠ **There is no `origin` remote in `.git/config`** — every push in this
repo goes by full URL with the token from `.gh_token.local`, so
`git push origin main` cannot work. If the push is rejected as
non-fast-forward, work out the merge; **never force-push** — the local
history and the curated GitHub history have different roots.

⚠ The script does NOT yet mention `server/`. It was last edited before
the API landed — **check its `git status` preview covers the new tree**
before running, or just let `git add -A` take it (the message is the
only thing that would be stale).

Backups in `_claude_backups/`: `m0__0808_1204__*` (career + profile),
`m0seam__BuilderManager.cs`, `m0seam__VerbBench.cs`,
`sweep__MatchRunner.cs`, `wpn__{DamageResolver,FightManager,MatchRunner}.cs`,
`qa_ladder_sweep_BEFORE.txt`, `qa_ladder_sweep_AFTER_w025.txt`,
`qa_ladder_sweep_AFTER_w010.txt`.

## Housekeeping

- **42 stale `_claude_*` / `critic_*` scratch files from previous
  sessions are swept** into `_to_delete/`, along with this session's
  staging, replays and screenshots, and a pile of stale git lock files.
  `_to_delete/` is gitignored. **Ask owen to delete it** — `mv` works
  over the bridge, `rm` does not.
- `_claude_backups/` left alone.

## Next direction

1. **`docker compose up` in `server/`** — the first real compile. Then
   `bash tests/sql_bench.sh` against the composed database to confirm the
   schema also applies through the boot-time migrator.
2. **The sim worker** — unblocked: owen installed **Linux Dedicated
   Server Build Support** (confirmed: `StandaloneLinux64` now reports
   supported). Linux server build of the game + a `MatchRunner` boot
   scene in Docker, running the validate-job and fight-job loops against
   the API. **Start on Mono**, measure with `MatchRunner.simSeconds`, and
   only reach for IL2CPP if managed time turns out to be material — the
   worker is CPU-bound on PhysX either way, and the IL2CPP path adds a
   2 GB sysroot and a C++ cross-compile to every build.
3. **Secret Manager**: `deploy_api.zsh` expects `rb-jwt-secret`,
   `rb-worker-key`, `rb-pg-conn`. They do not exist yet. Deliberately not
   in the bootstrap script — secrets should not be created by something
   that gets re-run.
4. **Client login + ENLIST** once there is a URL to point it at.
5. **The mirror-lock behaviour problem** (item 2 above). This is the one
   that decides whether the ladder can rank anything.
6. **The next sweep axis is CHASSIS.** The 30-bout sweep held the rig
   constant so the program was the only variable; it does not establish
   how the weapon trade behaves across different weapon geometries and
   mounting positions. Needs a second and third fixture rig.
7. Carried over from 2026-08-07, still open: `WallShy`'s retreat burst is
   now 1.5 s and **changes a shipped preset — wants a balance eye** ·
   **mismatched wheel roll axes make a legal build behave backwards** (a
   builder problem needing owen's design call) · braking cut timed-step
   overshoot 62%, not to zero · `SIDE TO BACK TO` lands ~18° out against
   its own 8° tolerance · critic loop 7's open list.
8. **Roblox port scoping** — wants a fresh session.

## Environment & protocols — CHANGED THIS SESSION

**⚠ The folder grant now WORKS.** Every previous handover says it fails
with "requires approval"; on 2026-08-08 owen approved
`device_request_folder_access` for
`/Users/leondu/Setup Guide In-Editor Tutorial` and it granted. This
changes the working method substantially:

- **`device_bash` runs on owen's Mac with the project mounted
  read-write.** Use `python3` heredocs with content-based anchors and
  `assert`s for edits — far safer than the Unity `RunCommand`
  File.ReadAllLines dance — and `cat > file <<'EOF'` for whole new files.
  It reads `Logs/Editor.log` directly, which makes compile-error checking
  a one-liner.
- **`rm` is NOT permitted** on the mount. `mv` into `_to_delete/` is the
  delete.
- **`device_bash` has NO network access.** No `gcloud`, no NuGet, no
  `git fetch` from there.
- The grant is **per session** — the next session must request it again.
  If owen is away the dialog times out; **send a Telegram ping first**
  (chat `7028111085`, one line, under 300 chars, no markdown) and then
  raise the dialog.
- Container→Mac transfer still works via base64 chunks — either through
  `Unity_RunCommand` (no grant needed) or, faster, `printf '%s' '<b64>'
  >> /tmp/x.b64` through `device_bash` then `base64 -d | tar xz`. **Use a
  subagent for it** so it does not eat the main context; that is how both
  the five M0 files and the whole `server/` tree landed.

⚠ **NEVER run `git` over the bridge mount.** Git cannot unlink its own
lock files there, so every `git status` leaves a stale
`.git/index.lock` that wedges the repo. One was cleared this session;
`_to_delete/` holds a pile from earlier sessions. If you must, `mv` the
lock away afterwards.

### Unity gotchas found this session

- ⚠ **`BuilderManager.BuildArena()` is not idempotent.** It news a fresh
  `sandboxRoot` with no teardown; two calls without `BackToBuild()`
  between them leave two overlapping arenas. `EnterMatchArena` inherits
  this — always pair it with `BackToBuild()`.
- ⚠ **`FightManager.End()` calls `Progression.OnMatchEnd`
  unconditionally**, outside any "a contest is active" guard. Anything
  that runs a fight headlessly MUST isolate career state or it will pay
  owen's career.
- ⚠ **`CompressionLevel` is ambiguous** between `UnityEngine` and
  `System.IO.Compression` — fully qualify it.
- `Object.GetInstanceID()` is obsolete in Unity 6.5; use `GetEntityId()`.
- **`SpawnBot` reads only `PlacedPart` DATA, never its build-space
  GameObject** (verified against BuilderManager.cs 3846–4025). That is
  what lets one arena hold two independently-uploaded robots: parse both
  builds into detached `PlacedPart` copies first, then spawn.
- **`PartSpec.edgeHardness` / `DamageResolver.IsEdge` is the codebase's
  definition of "is this a weapon".** Use it. Do not match on part-id
  string prefixes — the weapon visual id gets an index suffix at spawn.
- **Contact metrics belong to the MATCH, not to the replay.**
  `MatchRunner` subscribes its own `DamageResolver.OnHit` counter, so a
  sweep that writes no replay files can still answer "was this a fight".
- ⚠ **CareerSmoke is not isolated from other benches.** Run after
  MatrixBench + AutonomyBench + ReplayBench it reports **115/128**; alone
  in a fresh play session it is **128/128**. One of those three does not
  fully restore what it mutates. **Run CareerSmoke first, or in its own
  play session.**

### Still true, from 2026-08-07 — all of it worth re-reading

The RunCommand namespace trap · no `System.Reflection` / no
`System.Diagnostics.Process` (not available) / no `File.Delete` /
literals < 7000 chars · the ~60 s RunCommand ceiling and the
coroutine-plus-scratch-file pattern · **the bridge drops constantly**
(wait 50–60 s, verify state, retry the same command) · the ~6 min compile
round trip, so batch every edit you are confident in into one compile ·
anchor on ASCII · locate by content, not by remembered line number ·
descending line order for multi-edits · direction is the SIGN OF THE
POWER ARGUMENT · `SensorBus.forwardLocal` is the BUILD's drive axis, not
`transform.forward` · the centre-of-mass `wallDist` trap · always run the
control leg · do not loosen a threshold without saying why in the file ·
owner-state sacred, probe and ASK before taking the editor.

**And the one that earned its keep three separate times today: when a
check fails, ask whether the CHECK is wrong before changing the
product.** It was the check every time — a coverage metric that counted
both sides and read 199%; a bash heredoc that ate `$1` and invented a
double claim; and CareerSmoke's 13 "failures" that were cross-bench
pollution.

**Subagents drive the Unity bridge and the file channel well.** Five ran
today — three read-only source mappers (which found things the main loop
had assumed wrong) and two file-transfer jobs. Brief them with: the
namespace trap, the drop-and-retry rule, the ~60 s rule, and an explicit
**"never exit play, never recompile, never edit a .cs"** so they cannot
collide with the main loop.

## Bench inventory

TouchSmoke · CareerSmoke (128) · CanvasDragBench (31) · TestDebugBench
(30) · ProgramBench (40) · AutonomyBench (24) · VerbBench (32) ·
**ReplayBench (57)** · **LadderSweepBench (30 bouts)** · MatrixBench (9)
· SensorProbe · CareerBench · HazardBench · **`server/tests/sql_bench.sh`
(31)**.

- **VerbBench** — extend for behaviour work; it asserts what the robot
  physically DOES.
- **ReplayBench** — the multiplayer end-to-end bench; already asserts the
  §1.3 privacy boundary against the raw replay bytes.
- **LadderSweepBench** — a MEASUREMENT bench, not an assertion bench. It
  fails only on things broken regardless of balance; its value is the
  table at `Assets/Phase1/qa_ladder_sweep.txt`. **Any balance change to
  the weapon trade goes through it before and after.**
- **`sql_bench.sh`** — runs the same query text the API ships, so a
  passing bench cannot drift from a broken server.

## Doc map

- `M1_API_And_Schema_2026-08-08.md` — schema, queue, API v0, and what is
  and is not tested.
- `Weapon_Trade_Fix_Shipped_2026-08-08.md` — options 1–3, measured.
- `Ladder_Sweep_Weapon_Trade_2026-08-08.md` — the 30-bout table.
- `M0_Snapshot_Replay_MatchRunner_2026-08-08.md` — snapshot and replay
  formats, MatchRunner, and the deliberate deviations from the design doc.
- `Multiplayer_V3_Design_Doc.md` — the v3 plan; §10 all resolved.
- `WallEscapeField_2026-08-07.md` · `BehaviourLoop_R1_R3_2026-08-07.md` —
  **read before touching ProgramRunner or SensorBus.**
- `Spinner_Sweep_Legality_2026-08-05.md` — where further weapon-damage
  work starts.
- `CriticLoop6_Legibility_2026-08-04.md` — the house rule every loop is
  held to: *where a quality is measurable, measure it over EVERYTHING
  rather than over a named list.*
- `Programmable_Robots_V2_Design_Doc.md` · `Career_Mode_Design_Doc.md`.

## First moves

1. Health RunCommand (isPlaying, compile state) → probe owen. Request the
   folder grant (Telegram ping first if he is away).
2. Timestamped career backup; verify md5 vs **18614d0e**.
3. **Get this session committed** — `zsh push_m0_main.zsh` from Terminal.
   This is three phases of tested work sitting uncommitted.
4. Ask owen to delete `_to_delete/`.
5. Re-run the stale-green benches. **MatrixBench first** — and look at
   the FLOOR number, not just the pass.
6. `docker compose up` in `server/` and fix the first compile.
7. Then the sim worker, which is the last piece before an end-to-end
   cloud fight (M1's acceptance: upload two robots from the running game,
   fight on GCP, watch the replay in-client).
