# Robot Brawl: Scrapyard — working notes for Claude

⚠ **THIS IS THE FORK, NOT ROBOT BRAWL.** `du6/robot-brawl-scrapyard`, forked
from `du6/robot-brawl` at `1dc752e` (feature/scraplands, 2026-09-09) as a
**separate game**: owen's "start with a rookie robot, drive a map, find
crates, challenge parked robots, points on a board, log in only to
challenge". The design — the whole game, the fork, what is deleted, what is
shared, the milestones and the stop rule — is
**`docs/Scrapyard_Design_2026-09-09.md`. READ IT FIRST.**

What is different from the repo this was forked from, as of the fork:

| | Robot Brawl: Bolt & Blade | **Scrapyard (this repo)** |
|---|---|---|
| local folder | `~/Setup Guide In-Editor Tutorial` | `~/robot-brawl-scrapyard` |
| product / bundle | Robot Brawl: Bolt & Blade / `club.cyberduck.robotbrawl` | Robot Brawl: Scrapyard / `club.cyberduck.scrapyard`, version 0.1.0 build 1 |
| save file | `…/owen/Robot Brawl_ Bolt & Blade/robotbrawl_career.json` | `…/owen/Robot Brawl_ Scrapyard/scrapyard_save.json` (+ `scrapyard_profile.json`) — a different folder AND a different name |
| web | `cyberduck.club/play/rb/` | `cyberduck.club/play/scrapyard/`, beacon `/v1/beacon/scrapyard-play` (template still to be made — the WebGL template lives in `~/rb-webgl-spike`, not here) |
| server | shared: one API, one DB, one worker | **the same server**, with `GET /v1/pool`, the `yard` ruleset and the `game` tag (deployed 2026-09-09). Server code lives in Robot Brawl's repo (`server/**`); this repo's copy is a snapshot and must not be deployed from |
| git | side branches, never main | this repo's `main` IS the game; push by URL with the token in `.gh_token.local` (gitignored; never print it) |

**The fight core is kept BYTE-IDENTICAL to Robot Brawl's** — `CompoundRobot`,
`MatDB`, `RaycastWheelDrive`, `Actuator`, `DamageResolver`, `ProgramRunner`,
`SensorBus`, `RobotProgram`, `RobotSnapshot`, `RobotCategory`, `MatchRunner`,
`RobotWorker`, `FightWorkerLoop` — because the shared referee is built from
Robot Brawl's repo and fights this game's robots. **Backport fixes, never
features, in both directions, by commit reference.** A change to any file in
that list is not done until it is in both repos with both suites green.

**Owner state is sacred here too**, and it is a DIFFERENT file: Robot Brawl's
career save must never be touched from this repo, and this game's save must
survive every bench byte-identical. The rules below about `Career.Data`
isolation and `Career.SuspendAutosave()` apply unchanged.

**Day-one deletions** (design §2.2), one commit per system, benches green
after each — status is tracked in the design doc's progress log: title
screen / `ModeSelect` boot button · the five leagues, contests, medals ·
`RookieGuide` and the ghost hand · the ARENA tab's sign-in wall · MANUAL
FIGHT and TEST DRIVE as modes · the `RB_PORTAL` gate.

---

Everything below this line was inherited from Robot Brawl's `CLAUDE.md` at
the fork and is still true of the shared code, the benches and the traps.
Where it describes a system this game deletes, the deletion commit is the
correction. Paths like `~/Setup Guide In-Editor Tutorial` refer to the
OTHER repo.

---

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
0. **`docs/HANDOVER_2026-08-10.md`** — **START HERE.** The ladder is live and
   autonomous; its §5 is the career-save incident and the rule that prevents
   it, and should be read before running any bench.
0. **`docs/HANDOVER_2026-08-09_late_night.md`** — the previous handover. It supersedes `HANDOVER_2026-08-09_night.md` entirely
   (that document's §6 traps are still true), and supersedes (1) on all
   status; (1) is still live on the hard rules and editor discipline (its §2
   and §3).
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
| `docs/Scrapyard_Design_2026-09-09.md` | **ROBOT BRAWL: SCRAPYARD — a NEW GAME forked from this repo at `9d41135` (owen; design only, nothing built; all five calls made).** Drive the rookie round a seeded yard, crates are `qbox`es, parked robots (yard bots + one anonymous pool of real snapshots from both games, programs never shipped) are Quick bouts under an auto-brain picked by the build; rewards local, POINTS only from the referee; login only to send a challenge. Shares THIS server (`GET /v1/pool`, the `yard` ruleset on `matches.arena`, a `game` tag); fight core kept byte-identical, fixes backported by reference. Repo `du6/robot-brawl-scrapyard`, bundle `club.cyberduck.scrapyard`, `/play/scrapyard/`, `scrapyard_save.json` |
| `docs/CATS_Gap_Analysis_Plan_2026-09-07.md` | **the plan against C.A.T.S.** — CATS is a better *loop*, not a better fight: 30-s bouts, Quick Fight (3 wins = box, 5 = crown, daily cap instead of timers), real players' snapshots as guest opponents, drops + fusing on the material ladder, one-tap share, hard currency last. Six steps with the funnel metric each must move; owen's four calls listed |
| `docs/QA_Round1_Fixes_2026-09-05.md` | **two play-test agents (web + iOS simulator), three fix rounds, builds 19-22.** 2.2.0 (build 21) is ON SALE with a debrief that can strand a phone after a fight; **2.2.1 (build 22) in review is the fix.** The debrief's short-screen layout, deferred reward grants (`pendingRewards`), the fight autosaving the build, the runner's DPI artifact and its control leg |
| `docs/Build18_iOS_Port_2026-09-04.md` | **build 18 / 2.2.0 — the web warm-up ported to iOS, sign-in wall kept.** Cut, archived, exported, NOT uploaded (owen's three commands are in `server/scripts/release_ios_build18.zsh`). Also: **headless TouchSmoke overwrote the career save** (rookie rewards call Save; TouchSmoke now holds autosave) — restore command in §3; and every headless raycast bench was racing physics at 3-4k fps until `BatchSmoke` capped the loop |
| `docs/Gusset_x4_2026-08-18.md` | the gusset holds **×4**, measured with its control leg — and the finding that fell out of it: **`Main.unity` has no `BuilderManager`**, so every fight-running bench is unrunnable without a human clicking Start. Also: the ladder is at ×4 while build 9 in review is at ×1.5 |
| `docs/HANDOVER_iOS_Launch_2026-08-16.md` | **THE LAUNCH OPERATING MANUAL** — 1.0 submitted (build 8 then, **build 10** now); production topology, the four alerts and the regex trap that silences them, the 5-min health check, symptom→subsystem playbook, the build-9 ritual, the in-review build-swap procedure, open items in bite order |
| `docs/Launch_Check_2026-08-19.md` | **READ BEFORE RELEASING** — the DB accepts ~25 connections while the API fleet can open 400; unsubscribe is blockable by signup traffic (proven); and five documented facts are now false, incl. PITR being ON |
| `docs/Promo_Video_2026-08-18.md` | **the 21s promo** — real footage driven frame-by-frame (rAF is suspended, so MediaRecorder cannot work); this ffmpeg has NO drawtext; a still input is one frame at t=0, which silently killed every caption |
| `docs/Mailing_List_2026-08-18.md` | **the site's only working CTA** — how to fetch the list and send by hand; the CSV *is* the send list, BCC discipline, and the domain has NO SPF/DKIM/DMARC yet |
| `docs/Server_Economy_Design_2026-08-13.md` | scrap goes ON SALE eventually (owen) — wallet+inventory server-side. SERVER HALF BUILT + DEPLOYED same day (010/011, claims/purchase/wallet, 255/255) and the client's first-win rule with it; the boot gate + client cache is the remaining piece, tracked with hazards |
| `docs/Flagship_Hardening_2026-08-12.md` | CEILING L4 92%→50% in two measured steps; the L5 reversal; the FLOOR needle at 61%; where the rest of the gap lives |
| `docs/Gusset_Shipped_2026-08-12.md` | the seam-reinforcement part: fmt4 snapshots, the LOCKSTEP worker rule, what is measured and what is owed |
| `docs/League_Purse_Halving_2026-08-12.md` | the league pays HALF (owen) — what moved, what deliberately did not, and the dpi-sensitive legibility check |
| `docs/Catalog_Cuts_2026-08-12.md` | the saw and the bracket are GONE (hard deletes); the spike was reprieved — read before resurrecting a part id |
| `docs/Seasons_Live_2026-08-12.md` | seasons wired end-to-end; the board double-listing the first rollover would have detonated; deploy is TWO commands IN ORDER, owen's go |
| `docs/LAUNCH_CHECKLIST_2026-08-10.md` | **START HERE before shipping** — what is ready, what needs a phone, what needs owen |
| `docs/CareerBench_Sample_Size_2026-08-10.md` | three of eleven balance verdicts were coin flips |
| `docs/Disarm_Lever_Sweep_2026-08-10.md` | which lever moves the 43% — and the one that sounds right does nothing |
| `docs/ARENA_Judged_2026-08-10.md` | the ARENA photographed — and it has no entry point |
| `docs/Mutual_Disarm_Root_Cause_2026-08-09.md` | the 43% disarm: the limb fails, not the weapon |
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
| `dotnet`, `psql`, `curl localhost:5099` | **no** | yes |
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
- **WHEN YOU RENUMBER TABS, GREP FOR THE INDEX, NOT THE NAME.** The PARTS
  removal (08-05) said it had covered "every index-keyed site" and missed two:
  `TabHint()`'s cases 4 and 5 described the wrong tabs for five days, and
  `MedalDev` photographed the PROGRAM tab and filed it as `trophies.png`.
  Neither failed anything — a wrong sentence is not an exception. Both found
  and fixed 2026-08-10.
- ⚠ **EVERY DOCK PANEL IS GATED ON `dockOpen`.** `TestShowTab(i)` against a
  COLLAPSED dock switches the tab and shows nothing; the tab BUTTON opens the
  dock, a direct call does not. A harness that skips `SetDockOpen(true)`
  photographs an empty screen under a correct tab strip.
- ⚠ **A CONTROL'S BEHAVIOUR CAN LIVE IN ITS `onClick`, NOT IN THE METHOD THE
  SEAM EXPOSES — so a harness that calls the seam measures the half that was
  never broken.** The worked example, 2026-08-10: `SetDockOpen(false)` lives
  in the WATCH button's `onClick` (`MobileBuilderUI.cs`, `inboxwatch_*`).
  `ArenaScreen.WatchNow()` deliberately does NOT own dock state, because
  `ArenaScreen.Open()` renders this screen with no dock at all. A QA pass
  drove `WatchNow()` directly, faithfully reproduced the OLD behaviour, and
  photographed it as proof the fix had failed. **The two images are committed
  side by side in `c886459` — `docs/shots/17` and `18` — precisely because
  they look like a pass and a fail of one fix and are a fix and a bypassed
  fix.** 18 carries its own tell: the status line reads `replay t=6.4s` while
  the dock sits over it, and a working WATCH cannot produce that frame.
  **To check any UI behaviour, fire `b.onClick.Invoke()` on the named button;
  never the seam.**
  ⚠ **And the gap this leaves is REAL AND STILL OPEN.** A click-synthesising
  harness would have caught this one and will miss the next, because our
  `Test*` seams are drawn BELOW the layer where UI behaviour actually lives.
  This class of defect is invisible to every bench in this project. Naming it
  is worth more than a bench that reaches through the seam and proves nothing
  — do not "close" it with one.
- ⚠ **`Main.unity` CONTAINS NO `BuilderManager`, AND ONLY A CLICK EVER CREATES
  ONE.** The scene holds a camera, a light and the URP light data — nothing
  else. `StartCareer()` is the sole creator and it hangs off a title-screen
  button, so **every fight-running bench (`DisarmBench`, `MatrixBench`,
  `LadderSweepBench`, `OpeningBench`, `CareerBench`) fails on a freshly opened
  scene** unless a human clicked Start first. Measured 2026-08-18: DisarmBench
  returned `0 pass, 1 fail — FAIL BuilderManager in scene` in under a second,
  then **32/32** once one was created headlessly. The SAME missing object
  crashed the worker container the same day (`WorkerBootstrap` now creates one;
  see `docs/Gusset_x4_2026-08-18.md` §3). Create it before you run anything:
  `new GameObject("BuilderManager").AddComponent<BuilderManager>()`.
- ⚠ **NO BENCH IN THIS PROJECT FIGHTS A GUSSETED ROBOT.** Every fight fixture
  descends from `VerbBench.ARMED`, which carries **zero `|G:` marks**, so
  `GUSSET_SEAM_MULT` is never read in `DisarmBench`/`MatrixBench`/`OpeningBench`
  /`LadderSweepBench`. Their numbers are INSENSITIVE to it at any value — an
  unchanged 43% after the ×4 change is not a null result, it is no result.
  Only `CareerBench`'s HARDENED enemies are gusseted at all.
- ⚠ **A `#` AT THE START OF A LINE INSIDE A VERBATIM STRING BREAKS THE PLAYER
  BUILD AND NOT THE EDITOR.** In a DISABLED `#if` region the C# lexer does not
  recognise string literals at all — it only scans for directives — so
  `@"#fmt3-disc…"` inside `#if UNITY_EDITOR || DEVELOPMENT_BUILD` is read as a
  preprocessor directive and fails **CS1024**. The editor compiles it fine
  (region enabled, the `#` is just a character); the *release player* does not.
  Cost a build on 2026-08-20 (`ChallengeBench.CHAMPION_BUILD`, a snapshot whose
  first two lines are `#fmt3-disc` / `#fmt4-gusset`). **Snapshot text belongs in
  escaped concatenated literals** (`"#fmt3-disc\n" + …`), which is the form the
  original constant used and now we know why. Every bench in this project lives
  in such a region, so any fixture pasted as `@"…"` is a build waiting to fail —
  and no editor-side check can see it.
- **A UI PATH ONLY A HUMAN CAN DRIVE IS A UI PATH NOTHING CHECKS.** ENLIST
  shipped with its network half 21/21 and the button itself never once
  pressed; the first run of `EnlistUiBench` found the confirmation message
  being destroyed by the `Refresh()` that followed it. Use the `Test*` seam
  precedent (`ProgramCanvas`, `ArenaScreen`) — the bridge refuses reflection.
  ⚠ And a draw seam must be serviced from a real `OnGUI`: `GUILayout` called
  anywhere else throws, which reads as a broken panel and is a broken harness.
- **A GREEN ENDPOINT IS NOT A REACHABLE FEATURE.** `POST /v1/robots` and
  `POST /v1/snapshots` were benched, deployed, alerted and worked in
  production for two days with **no caller anywhere in `Assets/`** — the
  ladder was unreachable from inside the game and every bench was green,
  because `api_smoke` calls the endpoints with curl. Twice now the fault has
  been a missing CALLER, not broken code (the other is
  `HANDOVER_2026-08-10` §3). When something "works", ask which line of
  *product* code invokes it, and grep `Assets/` for the route.
- **Read `docs/…` before touching `ProgramRunner` or `SensorBus`.** Both
  have a long history of fixes that regressed each other.

## Bench notes

- **`EnlistLiveBench` needs a live API and WRITES TO IT** — accounts, robots,
  snapshots, matches. Point it at a dev database, never production. Play mode,
  21/21, skips rather than fails with no server. It is the only cover for the
  worker's real HTTP transport and for the enlist path.
  ⚠ **It drains the queue to reach its own match**, and it must: a single
  claim takes whatever is at the head, which on a dev DB is usually a leftover
  `api_smoke` job with a synthetic payload. A bench that assumes the first
  claim is its own reports a red against a worker that did exactly right.
  ⚠ **Its fixture has to be a REAL robot.** Validation checks the build (a
  lone core fails "needs at least 1 wheel") AND the program against the build
  (`Brawler` fails "needs a Compass tracker" on a chassis without one). Use
  `FirstSteps`, the sensor-free preset.
- **`CategoryBench` is pure data** — no scene, no play mode, no career
  state. `RobotBrawl.Phase0.CategoryBench.RunPure()`, under a second,
  44/44. The cheapest green in the project; run it after any edit near
  the ladder.
- **`server/tests/run_local.sh`** — one command: reset the dev DB, build,
  boot, `sql_bench.sh`, `api_smoke.sh`, stop, leave the logs on disk.
  `sql_bench` is the ONLY cover for `server/RobotBrawl.Api/Sql/*.sql`.
- ⚠ **LOCAL DEV IS PORT 5099, NOT 5000 — changed 2026-08-10.** macOS ships
  AirPlay Receiver **listening on 5000**: `curl localhost:5000` answers `403
  … Server: AirTunes`, and `bind(127.0.0.1:5000)` is `EADDRINUSE`. The dev
  API cannot start there. `LadderClient.LOCAL_DEV`, `run_local.sh` (via
  `RB_PORT`) and `api_smoke.sh` now all say 5099; older docs saying 5000 are
  stale on this point. **This is why it mattered:** every live Unity bench
  SKIPS when no server answers, so `EnlistLiveBench` read **0 pass / 0 fail /
  1 skip** — and a summary saying "failed 0" reads green. Zero failures and
  zero coverage look identical in a total. On 5099 the same bench is 21/21.
- ⚠ **A "green" bench with `passed == 0` is not a pass.** `Report()` has
  always printed "NOTHING RAN — this is not a pass" and nobody read it. Look
  at the pass COUNT, not just the fail count.
- ⚠ **CareerSmoke is not isolated.** Run it first or in its own play
  session, or it reports 115/128.
- ✅ **HEADLESS RUNS: `Assets/Editor/BatchSmoke.cs`** (`BatchSmoke.Career` /
  `.Touch`, `-batchmode -nographics`, no `-quit`) — the way to bench when the
  live editor is busy or has the Device Simulator open (a bench run under the
  simulator is NOT comparable: 119/24 on 2026-09-04 for geometry reasons). Run
  it from a **git worktree**, never a second instance on owen's project.
  ⚠ **`-batchmode` runs at 3,000-4,000 fps and `targetFrameRate` is ignored**,
  so "yield two frames" passes inside one physics step and every tap that
  raycasts (REMOVE, the gusset applique) is silently eaten — TouchSmoke read
  39/14 on BOTH legs of a control for that reason alone. `BatchSmoke.Tick`
  sleeps 16 ms a frame; with it, both legs are 53/53 and CareerSmoke is 141/2.
  The headless floor is 2 fails (DRAFT banner, the 640×480 label sweep) when
  the run reports `Screen.dpi = 0`, and **7** when it reports a real DPI
  (measured 266 at 640×480 on 2026-09-05: `TouchRow()`'s 110-unit clamp is
  then 38.9 pt and five size checks fail). Which you get is an environment
  property, not code — run the control leg before believing either number.
- ⚠ **A BENCH THAT SWAPS `Career.Data` MUST HOLD `Career.SuspendAutosave()`.**
  "Nothing here calls Save()" stops being true whenever product code grows a
  save — the rookie checklist did (`RookieTaskBolt` → `Save()`), and on
  2026-09-04 headless TouchSmoke overwrote owen's career with a 10-scrap
  bench career; the 2026-08-20 state is unrecoverable byte-exact. TouchSmoke
  holds it now. `docs/Build18_iOS_Port_2026-09-04.md` §3 has the restore.
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

✅ **The acceptance test is DONE, 2026-08-10, and it passed.** A fresh clone
builds the API (0 warnings), ships all six `Sql/*.sql` and all nine
migrations to the build output, boots, and runs `sql_bench` 53/53 +
`api_smoke` 198/198 + `restore_drill`. The two tracked/untracked complaints
that used to sit here are also resolved: `qa_api_server.log` is untracked and
`qa_sql_bench.txt` is tracked.

⚠ **It found a check that had never once run.** `run_local.sh` set
`TRUST_PROXY` on the API PROCESS's environment while `api_smoke.sh` reads the
same name out of ITS OWN shell — so section P skipped on every one-command run
and passed only when a human had exported it by hand. It covers
`X-Forwarded-For` spoof resistance. **`skipped` counts CALLS, not checks**
(section L's one skip stands for fourteen), so "skipped 1" beside "failed 0"
read as a pass. Skips are now replayed at the end under **NOT COVERED BY THIS
RUN**. Read a skip line as missing cover, never as a pass.

## Current state, briefly

**Green, all re-measured 2026-08-10 (counts updated 2026-08-12 where a bench
grew):** CategoryBench 44/44 · ReplayBench 60/60
· ProgramBench 40/40 · MatrixBench 10/10 FLOOR + 3/3 SWEEP · `sql_bench.sh`
**53/53** · `api_smoke.sh` **208/208** (196/196 against docker compose was
measured at the old count) · `restore_drill.sh` 10/10 · WorkerBench **47/47** · FuzzBench
25/25 · FightWorkerBench 21/21 play + 26/26 pure · LadderClientBench **37/37**
· LadderLiveBench 11/11 · DisarmBench 32/32 · **CareerSmoke 128/128** ·
**VerbBench 32/32** · **AutonomyBench 24/24** · **CanvasDragBench 31/31** ·
**TestDebugBench 30/30** · **TouchSmoke 29/29** · **HazardBench 23/23** ·
**EnlistLiveBench 21/21** · **EnlistUiBench 17/17** ·
**ReturningPlayerBench 22/22**.

⚠ **EnlistLiveBench's 21/21 is NEW, and its old "green" was 0/0/1-skip.** It
had never once executed: `LOCAL_DEV` pointed at port 5000, which macOS
AirPlay owns. See the port note under Bench notes. Treat any live bench you
have not personally seen a PASS COUNT from as unmeasured.

✅ **`ReturningPlayerBench` is new, 2026-08-10, and it closes a hole in the
SHAPE of the suite.** **Not one bench had ever called `LadderClient.Login`** —
every one registers a fresh account, does its errand in a single session, and
exits, so the only player ever exercised was a player in their first thirty
seconds, on a ladder whose whole premise is leaving and coming back. That is
the direct reason "the ARENA board never refetched" survived everything: a
bench that never comes back cannot see a screen that never updates. The new
bench signs out, signs back IN, and proves the dock re-asks the server after
a real absence and stays quiet on a quick flick. **Run it after any change to
`ShowTab`, `RefreshArena` or `ArenaScreen.Refresh`.**

✅ **The palette touch-floor regression is FIXED, 2026-08-10.** It was a UNIT
BUG: `FitPaletteRows` floored the cell at `44f` — 44 CANVAS UNITS — while the
dock's touch floor is a PHYSICAL 44 pt, which is what `TouchRow()` converts to
(`(44/163)*dpi/sf`). Clamping between a canvas-unit low bound and a physical
high bound measured 36.9 pt on owen's phone. The cell is now one touch row
always, and the vertical scroll that method already had absorbs the overflow.
Found by re-running CareerSmoke after the ARENA tab change; it arrived with
`2dec48f` and nothing re-ran the bench that covered it.
**Re-measured after the fix: CareerSmoke 128/128, TouchSmoke 29/29** — the
latter including "no scroller clips its content across a FIXED axis", the
invariant that caught the unreachable sensor rows in the first place.

**The stale-green list is gone — every one of them was run.** Two notes:
`CareerBench` is a BALANCE harness, and **at its shipped N=6-8 THREE OF
ELEVEN band verdicts are coin flips** — measured 2026-08-10 by re-running at
3x sample, ~500 fights: `docs/CareerBench_Sample_Size_2026-08-10.md`. Two runs
of an UNCHANGED build gave 12/5 and 10/7. Use `CareerBench.SampleMul` (default
1) before believing any single band. What survives the bigger sample: **CEILING
L4 at 22/24 = 92%** (a robot two classes below the flagship wins nearly always
— the worst thing in the harness), **STRETCH failing L3/L4/L5 in the same
direction** (systematic, not noise), and FLOOR L2/L3 low. **FLOOR L4 and
CEILING L5 were false alarms and should stop being counted.** Which to tune is
owen's.** `CareerSmoke`, `TouchSmoke`,
`HazardBench` and `CareerBench` each drive the builder, the arena and
`Career.Data`, and `BuildArena()` is not idempotent. Started together they
clobber each other and report nonsense — measured: HazardBench 7/7 fail
concurrently, **23/23 green alone**; TouchSmoke 17/12 concurrently, **29/29
alone**. Worse, on 2026-08-10 a concurrent run **overwrote owen's career
save**. It is recoverable (`career_backups/`, baseline md5 `18614d0e`) and the
mechanism is now closed — `Career.SuspendAutosave()` is a COUNTED hold, so
autosave returns only when the last holder releases — but the rule stands.

⚠ **THE APP DID NOT KNOW THE SERVER EXISTED until 2026-08-10.**
`LadderClient.BaseUrl` was `http://localhost:5000` and **nothing in the game
ever set it** — the production URL was in the deploy scripts and five docs and
in zero lines of game code, so a shipped iOS build would have reached for
localhost ON THE PHONE and rendered an empty ladder. Every bench pointed at
localhost too, which is exactly why none of them caught it.
Now: **the EDITOR defaults to `LOCAL_DEV` and a BUILD defaults to
`PRODUCTION`**, and that asymmetry is load-bearing — `EnlistLiveBench`,
`EnlistUiBench` and `ArenaShots` REGISTER ACCOUNTS, and they now **refuse to
run when `LadderClient.IsProduction`** (proven: pointed at production on
purpose, it stopped before any request). Override with
`LadderClient.BaseUrl = LadderClient.PRODUCTION`.
⚠ The BUILD side of that default is **verified by inspection only** — no iOS
build has ever been made from this code. Confirm it on the first one.
✅ **First supporting observation, 2026-08-12:** a Simulator-SDK player —
which takes the same compile-time `#if` branch TestFlight takes — rendered
the PRODUCTION URL in the ARENA status line. Device confirmation still owed.
⚠ **THE SIMULATOR CANNOT TESTIFY ABOUT RUNTIME BUILD FLAGS — measured,
2026-08-12.** Unity ships ONE simulator runtime per arch (no dev/release
variant on disk), so `Debug.isDebugBuild` is TRUE on a `BuildOptions.None`
sim build with a clean boot.config. Two consequences, both observed on a
verified-release sim build: the on-screen Development Console appears ON
ERRORS regardless of flags, and the `[dev]` status-line badge prints beside
whatever URL is live. Compile-time symbols (`#if UNITY_EDITOR`) ARE honest
there; runtime flags are not. Build identity on the simulator comes from
ARTIFACTS — bundle path, boot.config, process lineage — never from anything
on screen. Two instrument rules failed the same way before this was learned.
⚠ And the `[dev]` badge (MobileBuilderUI ~:3161) encodes BUILD-flavour but
reads as SERVER-environment — on the sim it sat beside the production URL
and nearly cost two testers a production account each. Whether it appears
on a real TestFlight build is a QUESTION for the first device build, not a
derivation: `isDebugBuild`-on-device claims failed twice tonight.

✅ **#15 RESOLVED, 2026-08-12: PLACEMENT WAS NEVER DEAD — the commit chain
measured healthy in a release sim player, BOTH verbs.** A temporary trace at
every link (release edge → `DebugClick` → `TakeClick` → `UpdateBuild` gates →
commit) showed place AND remove complete end-to-end (`ADD done`, `REM
hit=True`) under finger-speed input. "Dead placement" decomposed into TWO
mechanisms, one per instrument leg:
1. **Sub-frame synthetic taps (instrument artifact, already catalogued).** A
   synthetic click's down+up fits inside ONE frame and `Pointers()` samples
   `isPressed` as a LEVEL — a quick tap produced ZERO trace lines: it never
   existed as input. A real finger holds 50 ms+ and is safe; a 150 ms
   synthetic press placed every time. "Dev places fine" was frame rate, not
   code path.
2. **The REMOVE-armed geometry trap (product defect, real, FIXED).** Arming
   REMOVE writes a message; the message bar deepens the top band; with the
   dock OPEN the robot's last visible sliver vanishes under UI — "tap a part
   on the robot" had ZERO tappable pixels and every tap was silently eaten by
   `OverUI` ("eight taps, nothing deleted", reproduced exactly, trace shows
   `overUI=True` at coordinates that placed parts a minute earlier). Fix: the
   REMOVE button's onClick now collapses the dock (the WATCH precedent —
   behaviour lives in the onClick). TouchSmoke asserts the new contract,
   31/31.
The `[ray]` matrix-desync hypothesis is dead — picking was never wrong (the
readout was already deleted in `dfa60f9`). Trace method note: three
independent writers (UI queue, input seam, builder gates) whose frame numbers
must line up, each logging INPUTS at the decision — that is what let one
missing line name the fault.

**The ladder API is LIVE**: `https://rb-api-902243335343.us-central1.run.app`
on Cloud Run, against Cloud SQL over a unix socket, with blobs in GCS.
`server/scripts/deploy_api.zsh` now runs end to end — it was written from the
docs and had never executed until 08-09. Alerting is
`server/scripts/gcp_monitoring.zsh`; both are idempotent.

⚠ **Anything that assumes "this process is the whole world" breaks in the
cloud, and breaks GREEN.** Four such defects landed on 08-09 —
`docs/Cloud_Only_Defects_2026-08-09.md`. Read it before touching blob
storage, `Request.Scheme`, or anything that partitions on a client address.

**The ladder runs unattended.** A Cloud Run JOB (`rb-worker`) drains the queue
and exits; Cloud Scheduler starts it every 5 minutes, so it bills only while
there is work. `server/scripts/build_worker.zsh` builds the Linux player from
a CLONE (never owen's copy — building switches the active target, and this
project's is iOS); `deploy_worker.zsh` packages, deploys and schedules it.
Proven end to end: a robot uploaded to the live API was validated by the
container, and two accounts have fought a real cloud match with replays in GCS.

Things explicitly NOT done:

1. ⚠ ~~**Point-in-time recovery is off** on `rb-db`.~~ **FALSE, corrected
   2026-08-19 against the live instance: PITR is ON.** Daily backups (09:00
   UTC, 7 retained) are on and the restore drill passes. This entry said PITR
   had been deliberately declined to save WAL storage against a $25/mo
   budget, and three documents repeated it. Nobody has priced the WAL — if
   the bill does not fall as expected, look here first.
2. ✅ **ARENA STYLING — DONE 2026-08-10.** All five surfaces (board,
   scouting card + challenge, MY FIGHTS + replay launcher, ENLIST, sign-in)
   are UGUI **in the dock**, so they inherit the safe-area insets, the 44 pt
   touch floor and the benches that measure both — `TouchSmoke`'s fixed-axis
   invariant now covers all four ARENA scrollers and could see none of them as
   IMGUI.
   ⚠ **CORRECTION, 2026-08-12: the sentence above overstates the benches.**
   The 44 pt floor check lives in `CareerSmoke:~898` (not TouchSmoke), and it
   runs over a NAMED LIST of six BUILD-tab controls — the ARENA surfaces, TEST
   DRIVE, the tip strip and four whole tabs were never in its scope. "Inherit
   the benches" was inference, this file's own prose, and it was quoted as
   fact twice before anyone opened the bench. Two more limits, measured:
   `TapTargetPt` returns −1 for a missing name and the check SKIPS it (a stale
   name passes silently), and no bench anywhere asks whether one control is
   DRAWN ON TOP of another — which is how TEST DRIVE shipped with its bottom
   55% eaten by a transparent viewport (fixed `3a61d72`) while every check
   stayed green. The floor is also met with ZERO margin by design
   (`TouchRow()` = 44.0 pt exactly), so any occlusion puts a control under it. `ArenaScreen` keeps its OnGUI only for the standalone
   `ArenaScreen.Open()` path and cannot drift, because both renderers share
   one model and one set of flows.
   Pictures: `docs/shots/07..16`, re-shoot with `ArenaShots.RunMobileTab(dir)`.
   ⚠ **The password is PUSHED, never pulled** — `SetPassword` has no getter and
   the panel's rebuild key is not derived from it. Keep it that way; this dock
   is screenshotted on purpose.
   ⚠ **CONTRAST: THE ARENA FIELDS ARE FIXED, THE SHIPPED ONES ARE NOT.**
   Measured 2026-08-10, off the live hierarchy rather than computed: the ARENA
   fields were **1.02:1** against their row (floor is 3:1). The obvious fix —
   lift the fill to `MkInput`'s `#21242E` — reaches only **1.23:1** and CANNOT
   reach 3:1, because any fill dark enough to read white text on is too dark to
   separate from a dark dock. So the boundary moved to a 2px border `#6B7080`:
   **3.89:1** vs the dock, **3.15:1** vs the fill. Do not "simplify" that back
   into a fill change; it was tried and measured.
   **`MkInput` still carries the 1.23:1 defect** — the ROBOTS name field and
   every other dock input built through it, i.e. shipped single-player. It is a
   one-line change now that the border helper exists, and it is **owen's call**
   because it touches the shipped game's look, not a forgotten task.
3. ~~**The worker fight path has no BENCH.**~~ **CLOSED 2026-08-10** —
   `EnlistLiveBench` 21/21 drives the real `HttpWorkerTransport` against a
   live API for both halves: validate, then a real best-of-3 fought and posted
   by `FightWorkerLoop`, with the replay URL read back from the inbox.
4. **The worker latency/cost trade is a dial.** 5 minutes was chosen because
   an always-on worker service costs ~$35/mo against a $25 budget. If owen
   wants instant fights, that is a scheduler change and a bill.
5. **The ladder's 43% mutual disarm — root cause found 08-09, LEVERS NOW
   SWEPT 08-10.** `docs/Disarm_Lever_Sweep_2026-08-10.md` is the table, 105
   real fights, nothing changed. In short: **`WEAPON_VS_STRUCT` does nothing**
   (candidate 3 is dead, measured); **`BREAK_K` ×1.5 takes 40% → 27% and ×2.0
   buys nothing more**; and **a SHORTER WEAPON ARM fixes the mechanism but
   makes the outcome worse** — weapons stop being shed (67% → 38% leave with
   HP intact) and start being destroyed instead, so mutual disarm rises to
   53%. Together, short limb + `BREAK_K` ×1.5 is the best row at 20%.
   ⚠ 15 bouts/arm: trust the MECHANISM columns, not 1–2 bout differences in
   the rate. **Which lever to pull, if any, is still owen's** — and the sweep
   raises a question nobody has asked: is 43% actually wrong?

6. **Mirror lock is unsolved** — two identical robots still meet
   nose-to-nose and mutually disarm. A behaviour problem, not a damage
   constant; 0.10 was tested and rejected.

**Superseded — do not act on these, they appear in older handovers:** the
VALIDATE worker loop and the FIGHT half of the server contract were both
listed as not-done. Both exist and are benched (`FightWorkerLoop.cs`,
match-create/result/settlement, replay upload). `RobotCategory.cs` and
`CategoryBench.cs` were listed as untracked; both are committed.
