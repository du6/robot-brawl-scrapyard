<!-- Mirrored from the claude.ai Project (path: claude/M0_Snapshot_Replay_MatchRunner_2026-08-08.md) on 2026-08-08.
     The Project copy is the source of truth; edit there and re-mirror. -->

# Multiplayer v3 — Phase M0 SHIPPED (2026-08-08)

**All of M0 in one push, owen's call.** Snapshot envelope · replay
recorder + player · MatchRunner best-of-3 · ReplayBench. Everything runs
in the editor with no cloud account, which is the whole point of M0:
`Multiplayer_V3_Design_Doc.md` §9 says any divergence between the
headless build and the editor is a bug to find BEFORE the server exists,
and you cannot find it without an editor-side runner to compare against.

**ReplayBench 57/57.** Green set re-run after: VerbBench 32/32 ·
ProgramBench 40/40 · AutonomyBench 24/24. Career save byte-identical
(md5 **18614d0e**, mtime unchanged).

## What shipped

Five new files in `Assets/Phase1/Scripts/`:

| file | what |
|---|---|
| `RobotSnapshot.cs` | `SnapshotPayload` / `SnapshotEnvelope` / `SnapshotMeta`, sha256, export/open/import, `Describe()` |
| `ReplayRecorder.cs` | the replay FORMAT + the recorder + `ReplayFile` reader |
| `ReplayPlayer.cs` | kinematic playback |
| `MatchRunner.cs` | snapshot vs snapshot, seeded, best-of-3, career-isolated |
| `ReplayBench.cs` | the acceptance bench |

Three seams cut into existing files (deliberately tiny — the alternative
was a parallel spawn path, which is how you get two robots that fight
differently from the one the builder drew):

- `BuilderManager.SpawnBot` → **public**.
- `BuilderManager.DriveDir` — read-only accessor for the private
  `driveDir`. MatchRunner needs it to face two independently-uploaded
  robots at each other. Rebuilding a forward axis from `transform`
  instead is exactly the mistake that put the wall limit cycle back on
  X-drive builds on 2026-08-07.
- `BuilderManager.EnterMatchArena(float)` — StartFight's preamble with
  the spawns, the roster opponent, the AI, the camera and the touch dock
  removed. ⚠ `BuildArena()` is **not idempotent** (it news a fresh
  `sandboxRoot` with no teardown); every `EnterMatchArena` must be
  paired with `BackToBuild()`.
- `VerbBench.ARMED` → **public**, so the verified 19-part armed rig is
  the fixture for the new bench too rather than a second rig that drifts.

## Design decisions, and why

**The build format is not reinvented.** A snapshot's build field is
exactly `BuilderManager.SnapshotString()` text — the stamped
pipe-delimited format the game already saves garages and career robots
in, which already carries the per-part material as its last field. The
program is exactly `RobotProgram.ToJson()`. §5.2's "one implementation,
zero drift" only holds if the worker parses the SAME bytes the builder
writes.

**The hash covers the payload STRING, not a re-serialized object.**
Hashing a re-serialization would let a formatting change silently
invalidate every stored snapshot. The envelope stores the payload JSON
verbatim and hashes those bytes.

**The hash is integrity, not authentication.** It catches truncation and
tampering-by-accident. Anti-forgery is §5.2 (the worker runs the real
`Validate()`) and §5.5 (worker key). Nothing in the code pretends
otherwise.

**No program identity existed in this project before today.** Programs
were identified only by `SavedProgram.name` or by being the literal JSON
on `CareerRobot.program`. `RobotSnapshot.ProgramHash` is new, and "" for
no program on purpose, so "no program" is not a collidable constant.

**The replay is a recording, not a re-sim** (§5.4). It stores part
transforms and damage events. Replay therefore always matches result by
construction, and cross-machine physics determinism is never needed.

**Speed is `Time.timeScale`, NOT stepped `Physics.Simulate` — and this
is a deliberate deviation from §3.2 of the design doc.** Measured
reason: `Physics.Simulate`, `Physics.autoSimulation` and
`simulationMode` appear **nowhere** in the project's 206 scripts. Every
bench rides the automatic FixedUpdate loop and bumps `Time.timeScale`
(MatrixBench uses 10×). `Time.fixedDeltaTime = 0.02f` is set in exactly
one place, `Phase0Manager.cs:32`. Swapping the whole game to
script-driven stepping is new infrastructure that touches every
dt-integrating system (`RaycastWheelDrive`, `Actuator` ×5 sites,
`PowerPlant`, `ProgramRunner`, `SensorBus`, `SpinnerWeapon`) — it
belongs with the headless Linux worker in M1, not smuggled into the
phase whose job is to prove the fight loop and the replay. **The seam is
in `MatchRunner`: it is one field (`speed`) and one place that sets it.**

## The replay format

gzip'd JSON-lines, one record per line, first char is the kind:

```
H {header}   exactly one, first — matchId, bout, seed, arenaHalf, hz,
             both names, both BUILD texts, both part-id lists, duration
F {frame}    10 Hz of SIM time, one line per side per sample:
             {t, s, f[]} where f = root(7) + 7 floats per part
             (pos3 + quat4, pos rounded 3 dp, quat 4 dp)
E {event}    {t,"hit",side,partIdx,amount,x,y,z,destroyed} and the verdict
```

Sampling runs on FixedUpdate against `Time.time`, so a bench at
timeScale 10 records the same 10 Hz of **fight** seconds as a real-time
match. Rounding before write is worth ~5× on file size and is far below
what any eye sees on playback.

**Measured: 388 kB for a 90 s two-robot bout** — inside §5.1's "kB–low-MB,
egress negligible".

**§1.3 is enforced structurally, and asserted.** The replay carries each
robot's build (playback has to spawn something) and never its program,
not even a hash. ReplayBench decompresses the file and searches the raw
bytes for each side's program JSON and for `"hats"`. There is no field a
modified client could read a program out of.

Damage events need no change to `DamageResolver`: the existing
`OnHit(Vector3, float, bool, object victimPart)` hook (line 117) hands
back the victim `Part` object, and `DestroyPart` never re-indexes
`parts`, so a reference-keyed dictionary built at the bell is a stable
part identity for the whole bout.

## MatchRunner

Three things it does that no existing path does:

1. **Both sides are programs.** `StartFight` always spawns a roster AI
   opponent; `StartCareerFight` then hands only the PLAYER to a
   `ProgramRunner`. Two programmed robots pushing each other had never
   been run in this project (2026-08-07 handover, next-direction item 3).
   Here it is the default. A side with no program falls back to an
   `AIController`, so the runner also covers career replays.
2. **Seeded spawns.** Nothing in the project seeded anything before
   today (`UnityEngine.Random` was used only for a cosmetic build-order
   shuffle and for picking the fight music). `SpawnPoses(seed)` rotates
   the shared engagement axis, varies separation 3.6–4.4 m, and applies
   the SAME magnitude of aim jitter to both sides in opposite senses —
   a seed changes the fight, never the odds.
3. **Career isolation.** `FightManager.End` calls
   `Progression.OnMatchEnd` **unconditionally**, so a ladder bout could
   otherwise pay owen's career. The runner swaps in a scratch
   `CareerData`, clears `Career.active`/`autosave`/`activeContest` and
   `Progression.activeRungIndex`, and restores all of it in a `finally`.
   It also saves and restores the builder's working build.

Verdict: majority of bouts; ties broken on total damage; still tied →
Draw. Early-exit at 2 wins.

## What the first real match measured — READ THIS

The bench ran the armed rig with **Brawler** against the same rig with
**WallShy**, three seeds. Result: **three draws**, 121 vs 122 damage
every time.

Pulling the replays apart:

| | bout 0 (seed 11) | bout 1 (seed 22) | bout 2 (seed 33) |
|---|---|---|---|
| hit exchanges | 4 | 4 | 4 |
| last hit at | 3.75 s | 5.16 s | 5.00 s |
| **dead air** | **86.3 s of 90.0** | **84.9 s of 90.0** | **85.0 s of 90.2** |
| min separation | 2.09 m | 1.19 m | 2.10 m |
| path travelled A / B | 20.7 / 20.6 m | 93.4 / 74.1 m | 20.8 / 21.0 m |

Every hit lands at world ≈ (0, 0.3, 0) — the arena centre — in the first
five seconds. Both robots charge, meet nose-to-nose, and **each side's
part 8 (the spinner) destroys the other's on the first exchange**, at
60.75 damage a side, twice. Pieces 18/19 both sides: one part lost each,
the weapon. After that there is no weapon left on the field and the
match runs **85 more seconds of two disarmed robots shoving**, which the
judges correctly score "too close to call".

The by-hand visual check (house rule) confirms it frame by frame:
playback is correct, camera tracks, ownership rings are right, and at
t=6 s both machines are visibly nose-to-nose with their front weapons
gone.

**This is not a MatchRunner bug — the plumbing is doing exactly what it
should. It is a ladder-design problem, and M0 is where it was always
going to surface**, because M0 is the first time two programmed robots
have ever met. Three consequences worth owen's attention before M2
builds a rating system on top of this:

1. **Mirror matches mutually disarm.** Two identical weapon geometries
   meeting head-on trade weapon-for-weapon on contact one. On a ladder
   where the best build is copied, that is the default fight.
2. **A 90 s match with 5 s of fighting is bad replay content**, and §1.1
   makes watching the fights the product. The count-out machinery does
   not fire because the robots are still moving.
3. **The tie-break is deciding matches on noise.** The match verdict
   came back "B, by damage" off a **0.13 total damage** margin across
   three draws. §2.1 needs a win/loss, but a margin that small should
   read as a Draw and feed Glicko-2 a 0.5, the way `FightManager`'s own
   `DrawBand` already thinks about it. Currently `MatchRunner.Decide()`
   splits on any margin at all.

## Two defects the bench caught in my own work

- **`ReplayPlayer.Coverage` counted both sides**, so a perfect playback
  read **199.87%** and the 95% floor was unfalsifiable — a metric that
  is wrong when everything works cannot catch anything. Fixed to count
  side A only; now reads 99.87%, and the check is a 95–105% **band**,
  not a floor.
- `CompressionLevel` is ambiguous between `UnityEngine` and
  `System.IO.Compression` in this project — fully qualified.

## Honest limits of v1, stated here rather than discovered later

- **Wheels are not recorded.** Wheels are not entries in
  `CompoundRobot.parts` — they are `RaycastWheelDrive` channels whose
  visuals hang off the root. They ride the recorded root transform, so
  an intact robot looks right; after a break-up a shed island's wheels
  lag its body. Fixing it means recording drive channels too.
- A part destroyed mid-bout is **hidden** at its destruction event, not
  shattered: the shard VFX is a physics effect and physics is off.
- `SnapshotMeta` computes mass, size box, parts manifest, program hash
  and legality, but **not category** — category assignment is
  server-side by §1.2 and belongs in M1.
- Best-of-3 early-exits at 2 wins, so a decided match records 2 replays,
  not 3.

## Next

Design-doc order says M1: GCP project + billing alert, API v0, worker
image, client login + ENLIST. Before that, items 1–3 of "what the first
real match measured" are worth a decision — M2's whole rating design
sits on top of what a bout verdict means.
