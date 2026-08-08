#!/bin/zsh
# push_m0_main.zsh - commit the Multiplayer v3 Phase M0 work and push it to
# GitHub main. Mirrors push_main.zsh (2026-08-08 morning); never prints the
# token.
#
# WHY NOT `git push origin main`: there is no `origin` remote in .git/config.
# Every push in this repo goes by full URL with an inline token read from
# .gh_token.local, so the remote name simply does not exist.
#
# WHAT IT PUSHES: HEAD is on session/2026-08-05-rotor-pool-shop, and
# merge_all.zsh put that lineage onto GitHub main this morning, so HEAD:main
# is a fast-forward. Same shape as push_main.zsh.
set -e
cd "/Users/leondu/Setup Guide In-Editor Tutorial"

git check-ignore -q .gh_token.local || { echo "ABORT: .gh_token.local is NOT gitignored"; exit 1; }
git check-ignore -q _to_delete      || { echo "ABORT: _to_delete/ is NOT gitignored"; exit 1; }

echo "--- about to commit:"
git status --short | head -40
echo "--- ($(git status --porcelain | wc -l | tr -d ' ') paths total)"
echo ""

git add -A
git commit -F - <<'MSG_EOF' || echo "(nothing new to commit)"
Multiplayer v3 Phase M0: snapshot envelope, replay, MatchRunner

New (Assets/Phase1/Scripts/):
- RobotSnapshot.cs   snapshot envelope {payload, clientVersion, sha256} over
                     the game's OWN serialization - build is BuilderManager
                     SnapshotString text, program is RobotProgram.ToJson, so
                     the worker parses the same bytes the builder writes
                     (design doc 5.2, one implementation, zero drift). Adds
                     ProgramHash and SnapshotMeta (mass, size box, parts
                     manifest, legality) - no program identity existed before.
- ReplayRecorder.cs  gzip JSON-lines replay format + recorder + reader.
                     10 Hz of SIM time, root + 7 floats per part, damage
                     events off the existing DamageResolver.OnHit hook.
                     Carries each build, never a program (1.3), and the bench
                     asserts that against the raw decompressed bytes.
                     Measured 388 kB for a 90 s two-robot bout.
- ReplayPlayer.cs    kinematic playback: spawns through the same SpawnBot path
                     a fight uses, freezes physics, writes recorded transforms.
                     A replay is a recording, not a re-sim (5.4).
- MatchRunner.cs     snapshot vs snapshot, seeded, best-of-3, career-isolated.
                     First path where BOTH sides are programs; first seeded
                     spawns; isolates Career because FightManager.End calls
                     Progression.OnMatchEnd unconditionally.
- ReplayBench.cs     M0 acceptance: snapshot -> MatchRunner -> replay file ->
                     playback, verdict consistency and frame coverage.
- LadderSweepBench.cs  measurement bench: every unordered pairing of the five
                     shipped presets over two seeds, 30 bouts, reporting
                     contact, dead air and weapon survival per bout.

Seams (small on purpose - a parallel spawn path is how you get a robot that
fights differently from the one the builder drew):
- BuilderManager.SpawnBot made public
- BuilderManager.DriveDir accessor (never rebuild forward from transform)
- BuilderManager.EnterMatchArena(float) - StartFight's preamble, no combatants
- VerbBench.ARMED made public, so the verified rig is the shared fixture

Speed is Time.timeScale, not stepped Physics.Simulate - deliberate, measured:
Physics.Simulate/autoSimulation/simulationMode appear nowhere in the project's
206 scripts. Stepping belongs with the headless worker in M1. Seam is
MatchRunner.speed.

ReplayBench 57/57, VerbBench 32/32, ProgramBench 40/40, AutonomyBench 24/24.
Career save byte-identical (md5 18614d0e).

Also sweeps 42 stale _claude_*/critic_* scratch files that earlier sessions
could not delete over the desktop bridge.

Finding, recorded in Ladder_Sweep_Weapon_Trade_2026-08-08.md and measured over
30 bouts: the weapon is the fragile thing, not the robot. 26/30 bouts end with
a weapon destroyed, 15/30 with both sides disarmed, and all 11 bouts with an
asymmetric weapon count were won by the side that kept one - the loser losing
18-19 of 19 parts. Mirror matches are deterministic mutual disarm. Mean dead
air 53.0 s of a 66.8 s bout (79.3%). Ladder-design question, not a runner bug.
MSG_EOF

TOK=$(tr -d '\r\n' < .gh_token.local)
git push "https://x-access-token:${TOK}@github.com/du6/robot-brawl.git" HEAD:main 2>&1 | sed "s/${TOK}/***REDACTED***/g"
echo ""
echo "Pushed $(git rev-parse --short HEAD) to main on du6/robot-brawl."
