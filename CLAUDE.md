# Robot Brawl: Bolt & Blade — working notes for Claude

owen's physics robot construction/combat game. Unity 6.5 (6000.5.4f1),
macOS, project root is this directory. Single-player is shipped and
playable; **Multiplayer v3** (cloud ladder) is mid-build.

## Read this first

1. **`docs/SESSION_HANDOVER_2026-08-09_late.md`** — the current handover.
   It supersedes only Part 2 (multiplayer) of
   `docs/SESSION_HANDOVER_2026-08-09_evening.md`; that document's Part 1
   is still the live guidance for single player, so read both.
2. **`docs/CLI_SESSION_BRIEF_2026-08-09.md`** — read this if you are
   Claude Code on owen's Mac. It is scoped to what a real shell does
   better, and it explains how to get the Unity bridge so you are not
   limited to that.

The records, most recent first:

| doc | what |
|---|---|
| `docs/Category_Assignment_Shipped_2026-08-09.md` | the ladder's weight categories; why the size box is gone |
| `docs/M1_Worker_Contract_Gaps_2026-08-09.md` | the claim contract fix, and the two gaps that were left |
| `docs/Opening_Ram_Fix_2026-08-09.md` | the opening disarm: 42% → 0% |
| `docs/Matador_Ram_Retraction_2026-08-09.md` | a retraction — read before "fixing" Matador |
| `docs/Brawler_Disengage_Negative_2026-08-09.md` | a measured negative — read before adding a disengage |
| `docs/M1_API_First_Compile_2026-08-09.md` | first real build of `server/` |
| `docs/M1_API_And_Schema_2026-08-08.md` | the ladder API, schema and queue |
| `docs/M0_Snapshot_Replay_MatchRunner_2026-08-08.md` | snapshot envelope, replay format, MatchRunner |
| `docs/Ladder_Sweep_Weapon_Trade_2026-08-08.md` | the 30-bout sweep and what it found |
| `docs/Weapon_Trade_Fix_Shipped_2026-08-08.md` | the fix, measured before/after |
| `docs/Size_Box_On_Build_Bar_2026-08-03.md` | why the size box exists nowhere |
| `docs/Multiplayer_V3_Design_Doc.md` | the v3 spec (§1.2 carries an as-built amendment) |

⚠ **`docs/` is a MIRROR.** The source of truth is the claude.ai Project
attached to owen's Cowork sessions. If you change one, tell owen so the
Project copy is updated too — otherwise the next Cowork session works
from a stale doc. **The reverse also bites:** a design doc can describe
code that has since been deleted. §1.2's size box did exactly that and
cost a session; see `Category_Assignment_Shipped_2026-08-09.md`.

## Who can do what

Two kinds of session work on this project, and neither can do everything.

| | Cowork session | Claude Code on the Mac |
|---|---|---|
| edit files here | yes | yes |
| `git`, `rm` | **no** | yes |
| `dotnet`, `psql`, `curl localhost:5000` | **no** | yes |
| run a Unity bench | **yes**, via the MCP bridge | **probably not** |

A Cowork session's `bash` is a **separate Linux VM** with this folder
mounted and no network, no `dotnet`, no `psql`, no Homebrew and no route
to the Mac's `localhost:5000`. Confirmed empirically. It reaches the Mac
only through the mounted folder and the **Unity MCP bridge**, which
compiles and runs C# in owen's live editor — which is how every bench in
this project gets run.

**If you cannot run the benches, do not report Unity-side work as
verified.** Say what you changed and that it is unverified, and leave the
measurement to a session that can. A claim without a measurement is not a
result here.

Ownership when both are active: **CLI owns `server/**`, `.gitignore` and
git; Cowork owns `Assets/**` and the editor.**

### The two sessions talk through `_relay/`

There is no agent-to-agent channel — checked, not assumed. `_relay/` is a
**work queue** on disk that both sides can reach: Cowork writes requests
to `_relay/inbox/`, a CLI session claims them, works, and writes results
to `_relay/outbox/`. **`_relay/README.md` is the protocol and is written
for whoever picks it up — a session does not need owen to explain it.**

Two constraints shaped it and are worth knowing before you touch it: the
Cowork mount **refuses `rm`**, so every removal is expressed as a move and
belongs to the CLI side; and each file has **exactly one writer**, because
two writers on one file with no locking is how you get interleaved
garbage. `_relay/` is gitignored — anything in it worth keeping graduates
to `docs/` as a dated record.

## Layout

```
Assets/Phase0/Scripts/      CompoundRobot, MatDB, RaycastWheelDrive, Phase0Manager
Assets/Phase1/Scripts/      everything else: BuilderManager (~7k lines),
                            FightManager, ProgramRunner, SensorBus,
                            DamageResolver, Career, RobotSnapshot,
                            RobotCategory, and all the benches
Assets/Phase1/qa_*.txt      bench reports — tracked on purpose, quoted in docs
server/                     the multiplayer API (C#/ASP.NET Core + Postgres)
server/qa_*.txt|log         what run_local.sh leaves behind
docs/                       these records
_claude_backups/            pre-edit backups; leave alone
career_backups/             timestamped career saves
```

Ignore `Assets/Phase1/.bundle*/`, `.push_tmp/` and `.to_delete/` — stale
duplicate copies of the real scripts, plus a great many `*.cs.*.bak`
files beside the live ones. Editing one of those instead of the live file
has bitten before. **When you grep, expect the backups to outnumber the
hits that matter.**

## House rules, and they are not optional

1. **Where a quality is measurable, measure it over EVERYTHING rather
   than over a named list.** (`CriticLoop6_Legibility_2026-08-04`.) The
   benches exist because "it looks right" was wrong repeatedly.
2. **When a check fails, ask whether the CHECK is wrong before changing
   the product.** It was the check three separate times in one day.
3. **Do not loosen a threshold to make a check pass** — and if a
   threshold genuinely must be loose, say why IN THE FILE, with the
   measured numbers.
4. **Always run the control leg before believing a negative result.**
5. **Owner state is sacred.** The career save
   (`~/Library/Application Support/…/robotbrawl_career.json`, baseline
   md5 `18614d0e`) must come back byte-identical. Anything that runs a
   fight headlessly MUST isolate `Career.Data` — `FightManager.End()`
   calls `Progression.OnMatchEnd` unconditionally.
6. **Predict before you measure**, and write the prediction down. A test
   that can fail is worth running even when you expect to win.
7. **Back up before editing**: `_claude_backups/<topic>__<file>`.

## Code traps that have caused real bugs

- **`SensorBus.forwardLocal` is the BUILD's drive axis**, snapped to
  (0,0,1) or (1,0,0) — NOT `transform.forward`. Rebuild a direction from
  the transform and you are 90° wrong on X-drive builds.
- **Direction is the SIGN OF THE POWER ARGUMENT.** `MkMoveRel(Wall,-80f)`
  is AWAY, `+80f` is TOWARD. Caused three separate bugs.
- **`PartSpec.edgeHardness` / `DamageResolver.IsEdge` is the definition
  of "is this a weapon".** Do not match on part-id string prefixes.
- **`BuilderManager.BuildArena()` is not idempotent** — pair every
  `EnterMatchArena()` with `BackToBuild()`.
- **`wallDist` is measured from the CENTRE OF MASS**, not the nose.
- **There is no size box anywhere in this game.** It was removed on
  2026-08-03 and the removal was deliberate and complete. The ladder's
  categories are **mass only**. Read `RobotCategory.cs`'s header before
  you are tempted; the open question it leaves is a *bench* (does an ABS
  tower actually win?), not a rule to add back.
- **A robot with no weight category is a rejection, not "unrated".**
  `ratings.category` is NOT NULL. `SnapshotMeta.category` is `""` for
  none, but `snapshots.category` accepts NULL and **not** the empty
  string — a worker must map one to the other.
- **Read `docs/…` before touching `ProgramRunner` or `SensorBus`.** Both
  have a long history of fixes that regressed each other.

## Bench notes

- **`CategoryBench` is pure data** — no scene, no play mode, no career
  state. `RobotBrawl.Phase0.CategoryBench.RunPure()`, under a second,
  44/44. The cheapest green in the project; run it after any edit near
  the ladder.
- **`server/tests/run_local.sh`** — one command: reset the dev DB, build,
  boot, `sql_bench.sh`, `api_smoke.sh`, stop, leave the logs on disk.
  `sql_bench` is the ONLY cover for `server/RobotBrawl.Api/Sql/*.sql`.
- ⚠ **CareerSmoke is not isolated.** Run it first or in its own play
  session, or it reports 115/128.
- ⚠ **`LadderSweepBench` and `OpeningBench` are MEASUREMENT benches** —
  no pass/fail by design. `OpeningBench`'s `CUTOFF` is per-subject and its
  point estimate wanders ±~17 points at N=12; keep controls inside the
  batch.
- ⚠ **Do not treat a `MatrixBench.RunFloor()` failure as a regression on
  its own** — 6/10, 8/10 and 7/10 with no relevant code change.

## Git

⚠ **There is no `origin` remote.** Pushes go by full URL with a token
read from `.gh_token.local` (gitignored, never print it). See
`push_m0_main.zsh`, `checkin.zsh`, `push_main.zsh` in this directory.
`checkin.zsh` pushes to a dated side branch; `push_main.zsh` and
`push_m0_main.zsh` target `main`.

The local history and the curated GitHub history have **different roots**
— if a push is rejected as non-fast-forward, work out the merge.
**Never force-push.** **Never run `git` from a Cowork session** — that
mount cannot unlink its own lock files.

✅ **The index fix landed** — owen committed it at 11:10 on 2026-08-09
(`651d5670`, *"Untrack .NET build output; track the hand-written server
csproj"*). The hand-written `RobotBrawl.Api.csproj` is tracked and no
`bin/` or `obj/` entries remain. **Handovers written before that still
list it as outstanding — they are stale on this point.**

⚠ **What was never done is the acceptance test**: nobody has confirmed a
fresh clone actually builds the API, because no session had both `git`
and `dotnet`. `docs/CLI_SESSION_BRIEF_2026-08-09.md` §3 has the command.
Also still open: `server/qa_api_server.log` is tracked despite now being
in `.gitignore` (ignore rules do not untrack), and
`server/qa_sql_bench.txt` is untracked while its two siblings are.

## Current state, briefly

**Green at last measurement (2026-08-09):** CategoryBench 44/44 ·
ReplayBench 60/60 · ProgramBench 40/40 · MatrixBench 10/10 FLOOR + 3/3
SWEEP · `sql_bench.sh` 34/34 · `api_smoke.sh` 37/37.

**Stale-green, verify before trusting:** VerbBench (32) · AutonomyBench
(24) · CareerSmoke (128) · CanvasDragBench (31) · TestDebugBench (30) ·
TouchSmoke · HazardBench · SensorProbe · CareerBench.

**`RobotCategory.cs` and `CategoryBench.cs` are untracked** — new on
2026-08-09, proven 44/44, and existing in exactly one copy. Run
`git status` before assuming anything else about what is committed.

Things explicitly NOT done — the handover has the detail:

1. **The VALIDATE worker loop.** Every piece it needs now exists; it is
   the unblocked next step on the multiplayer side.
2. **The FIGHT half of the server contract does not exist** — no
   match-create, no match-result, no replay upload.
3. **The Docker image has never been built** — owen has no Docker, so
   `server/` runs via `dotnet run` against a brew-installed Postgres.
   `docker-compose.yml` and the Dockerfile are unproven.
4. **The ladder's 43% mutual disarm** — the biggest open single-player
   problem, with three cheap explanations already ruled out. It needs its
   own bench; `OpeningBench` is the template.
5. **Mirror lock is unsolved** — two identical robots still meet
   nose-to-nose and mutually disarm. A behaviour problem, not a damage
   constant; 0.10 was tested and rejected.
