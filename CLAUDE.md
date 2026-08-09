# Robot Brawl: Bolt & Blade — working notes for Claude Code

owen's physics robot construction/combat game. Unity 6.5 (6000.5.4f1),
macOS, project root is this directory. Single-player is shipped and
playable; **Multiplayer v3** (cloud ladder) is mid-build.

## Read this first

**`docs/SESSION_HANDOVER_2026-08-08.md`** — the current handover. It has
where everything stands, what is green, what is deliberately NOT done,
and the hard-won gotchas. Everything below is a summary of it.

The other records, in the order they happened:

| doc | what |
|---|---|
| `docs/M0_Snapshot_Replay_MatchRunner_2026-08-08.md` | snapshot envelope, replay format, MatchRunner |
| `docs/Ladder_Sweep_Weapon_Trade_2026-08-08.md` | the 30-bout sweep and what it found |
| `docs/Weapon_Trade_Fix_Shipped_2026-08-08.md` | the fix, measured before/after |
| `docs/M1_API_And_Schema_2026-08-08.md` | the ladder API, schema and queue |

⚠ **These are MIRRORS.** The source of truth is the claude.ai Project
attached to owen's Cowork sessions. If you change one, tell owen so the
Project copy is updated too — otherwise the next Cowork session works
from a stale doc.

## What is different for you vs. a Cowork session

Most of the protocol notes in the handover were written for a Cowork
session driving this editor through the **Unity MCP bridge**, from a
cloud container. Running as Claude Code on owen's Mac, you have it much
easier, and some of those rules do not apply to you:

- You have **direct filesystem access**. No base64 chunking, no
  `RunCommand` File.ReadAllLines dance, no 7000-char literal limit.
- **`rm` works** and **`git` works.** The handover's "never run git over
  the mount" warning is about the Cowork device bridge, not about you.
- **You probably cannot drive the Unity editor.** The benches
  (VerbBench, ReplayBench, MatrixBench, CareerSmoke…) run *inside* play
  mode and are launched over the Unity MCP bridge. Unless that MCP server
  is configured for your session too, **you cannot run or verify any
  bench** — so do not report Unity-side work as verified. Say what you
  changed and that it is unverified, and let a Cowork session or owen run
  the benches.

## Layout

```
Assets/Phase0/Scripts/      CompoundRobot, MatDB, RaycastWheelDrive, Phase0Manager
Assets/Phase1/Scripts/      everything else: BuilderManager (~7k lines),
                            FightManager, ProgramRunner, SensorBus,
                            DamageResolver, Career, and all the benches
Assets/Phase1/qa_*.txt      bench reports — tracked on purpose, quoted in docs
server/                     the multiplayer API (C#/ASP.NET Core + Postgres)
docs/                       these records
_claude_backups/            pre-edit backups; leave alone
```

Ignore `Assets/Phase1/.bundle*/`, `.push_tmp/` and `.to_delete/` — stale
duplicate copies of the real scripts. Editing one of those instead of the
live file has bitten before.

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
- **Read `docs/…` before touching `ProgramRunner` or `SensorBus`.** Both
  have a long history of fixes that regressed each other.

## Git

⚠ **There is no `origin` remote.** Pushes go by full URL with a token
read from `.gh_token.local` (gitignored, never print it). See
`push_m0_main.zsh`, `checkin.zsh`, `push_main.zsh` in this directory.
`checkin.zsh` pushes to a dated side branch; `push_main.zsh` and
`push_m0_main.zsh` target `main`.

The local history and the curated GitHub history have **different roots**
— if a push is rejected as non-fast-forward, work out the merge.
**Never force-push.**

## Current state, briefly

Everything from 2026-08-08 is **uncommitted**. Green at close: ReplayBench
57/57, LadderSweepBench 31/31, VerbBench 32/32, ProgramBench 40/40,
AutonomyBench 24/24, MatrixBench 9/9, CareerSmoke 128/128,
`server/tests/sql_bench.sh` 31/31.

Three things explicitly NOT done — the handover has the detail:

1. **`server/`'s C# has never been compiled** (NuGet was unreachable
   where it was written). First real build is `docker compose up` in
   `server/`. The SQL *is* tested.
2. **Mirror lock is unsolved** — two identical robots still meet
   nose-to-nose and mutually disarm. It is a behaviour problem, not a
   damage constant; 0.10 was tested and rejected.
3. **MatrixBench's FLOOR passed at exactly its threshold** (7/10 against
   `>= 7`). Green with no margin. Watch that number.
