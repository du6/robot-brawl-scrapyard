# Robot Brawl: Bolt & Blade — working notes for Claude

owen's physics robot construction/combat game. Unity 6.5 (6000.5.4f1),
macOS, project root is this directory. Single-player is shipped and
playable; **Multiplayer v3** (cloud ladder) is mid-build.

## Read this first

0. **`docs/HANDOVER_2026-08-09_night.md`** — **START HERE.** End of
   2026-08-09: the API is LIVE on Cloud Run, 42 commits that day, and it
   names the one task that matters next (GcsBlobStore). It supersedes the
   status tables in (1) below; (1) is still the live guidance for the hard
   rules and the editor discipline.
0. **`docs/HANDOVER_2026-08-09_night.md`** — the newest handover, and the one
   to start from. It supersedes (1) on all status; (1) is still live on the
   hard rules and editor discipline (its §2 and §3). ⚠ Its §3 ("write
   GcsBlobStore") is **DONE** — see `docs/Cloud_Only_Defects_2026-08-09.md`.
1. **`docs/HANDOVER_TO_CLI.md`** — the takeover brief. As of 2026-08-09 this
   project is driven by a Claude Code session on owen's Mac. That document
   is the takeover brief: what to verify before trusting anything, the
   editor discipline, the hard rules, full M1 state, and the work queue in
   order.
2. **`docs/SESSION_HANDOVER_2026-08-09_late.md`** — the current handover.
   It supersedes only Part 2 (multiplayer) of
   `docs/SESSION_HANDOVER_2026-08-09_evening.md`; that document's Part 1
   is still the live guidance for single player, so read both.
3. `docs/CLI_SESSION_BRIEF_2026-08-09.md` — superseded by (1), kept for the
   record. It contains one claim now known to be false; see the note in it.

⚠ **The claude.ai Project that these `docs/` were mirrored from is
FROZEN.** `docs/` in git is the source of truth. A change to the code and a
change to its record can now land in the same commit — which is the point.

The records, most recent first:

| doc | what |
|---|---|
| `docs/Cloud_Only_Defects_2026-08-09.md` | four faults that only exist in the cloud, and all four looked green |
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
| run an edit-mode Unity bench | **yes** | **yes — measured 2026-08-09** |
| run a PLAY-MODE Unity bench | **yes** | **yes — measured 2026-08-09** |

A Cowork session's `bash` is a **separate Linux VM** with this folder
mounted and no network, no `dotnet`, no `psql`, no Homebrew and no route
to the Mac's `localhost:5000`. Confirmed empirically. It reaches the Mac
only through the mounted folder and the **Unity MCP bridge**, which
compiles and runs C# in owen's live editor — which is how every bench in
this project gets run.

⚠ **CORRECTION, 2026-08-09.** This file used to say a CLI session
"probably cannot" run a Unity bench. **That was never tested by anyone who
wrote it**, and it is false: `unity-mcp` is registered as a user MCP in
`/Users/leondu/.claude.json`, and a CLI session has now run
`CategoryBench.RunPure()` to **44 pass, 0 fail** through its own bridge.
The false sentence was inherited from a handover, repeated into two
documents, and shaped how the work was divided for a day. **Check the
artifact, not the note about the artifact.**

✅ **And play mode is now proven too, 2026-08-09.** A CLI session entered
play mode over the bridge, ran `WorkerBench.Run()`, polled the coroutine to
`finished`, got **39 pass, 0 fail**, and exited — with the career save
byte-identical **and its mtime unchanged**, meaning it was never written.
See `docs/Play_Mode_From_CLI_Proven_2026-08-09.md` for the method and the
traps. **A CLI session can run this project's whole bench suite.**

Still unmeasured: the benches that run real **fights** — `MatrixBench`,
`LadderSweepBench`, `OpeningBench` — and `CareerSmoke`, which is documented
as not isolated. `FightManager.End()` calls `Progression.OnMatchEnd`
unconditionally, so those are where isolation actually matters. Fingerprint
the career save either side, **mtime included**, every time.

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

**Green at last measurement (2026-08-09, late):** CategoryBench 44/44 ·
ReplayBench 60/60 · ProgramBench 40/40 · MatrixBench 10/10 FLOOR + 3/3
SWEEP · `sql_bench.sh` **48/48** · `api_smoke.sh` **185/185** ·
`restore_drill.sh` 10/10 · WorkerBench 39/39 · FuzzBench 25/25 ·
FightWorkerBench 21/21 play + 26/26 pure · LadderClientBench 18/18.

**Stale-green, verify before trusting:** VerbBench (32) · AutonomyBench
(24) · CareerSmoke (128) · CanvasDragBench (31) · TestDebugBench (30) ·
TouchSmoke · HazardBench · SensorProbe · CareerBench.

**The ladder API is LIVE**: `https://rb-api-902243335343.us-central1.run.app`
on Cloud Run, against Cloud SQL over a unix socket, with blobs in GCS.
`server/scripts/deploy_api.zsh` now runs end to end — it was written from the
docs and had never executed until 08-09. Alerting is
`server/scripts/gcp_monitoring.zsh`; both are idempotent.

⚠ **Anything that assumes "this process is the whole world" breaks in the
cloud, and breaks GREEN.** Four such defects landed on 08-09 —
`docs/Cloud_Only_Defects_2026-08-09.md`. Read it before touching blob
storage, `Request.Scheme`, or anything that partitions on a client address.

Things explicitly NOT done:

1. **The Docker image builds and deploys now** (colima + Cloud Build), but
   `docker-compose.yml` and its `db` service are still unproven —
   `docker compose` needs `brew install docker-compose`.
2. **Point-in-time recovery is off** on `rb-db`. Daily backups (09:00 UTC,
   7 retained) are on and the restore drill passes, but a restore loses up
   to 24h. It costs WAL storage against a $25/mo budget — **owen's call.**
3. **ARENA styling.** Four of five surfaces exist and work; the layout is
   OnGUI placeholder and largely unjudged. Screenshot with
   `RobotBrawl.Phase0.UiShot.Take(path)` in play mode — the MCP capture
   tools render from a camera and never see IMGUI.
4. **The end-to-end worker↔API runs are hand-run, not benches.** That path
   has no regression cover.
5. **The ladder's 43% mutual disarm** — the biggest open single-player
   problem, with three cheap explanations already ruled out. It needs its
   own bench; `OpeningBench` is the template.
6. **Mirror lock is unsolved** — two identical robots still meet
   nose-to-nose and mutually disarm. A behaviour problem, not a damage
   constant; 0.10 was tested and rejected.

**Superseded — do not act on these, they appear in older handovers:** the
VALIDATE worker loop and the FIGHT half of the server contract were both
listed as not-done. Both exist and are benched (`FightWorkerLoop.cs`,
match-create/result/settlement, replay upload). `RobotCategory.cs` and
`CategoryBench.cs` were listed as untracked; both are committed.
