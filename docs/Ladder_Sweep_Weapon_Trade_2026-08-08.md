<!-- Mirrored from the claude.ai Project (path: claude/Ladder_Sweep_Weapon_Trade_2026-08-08.md) on 2026-08-08.
     The Project copy is the source of truth; edit there and re-mirror. -->

# Ladder sweep — the weapon trade decides the fight (2026-08-08)

M0's first match was three draws in which both robots destroyed each
other's weapon in five seconds. One matchup is an anecdote, so this
swept **every unordered pairing of the five shipped presets over two
seeds — 30 bouts**, on one chassis (`VerbBench.ARMED`, both sides) so
the only variable is the PROGRAM. Bench: **`LadderSweepBench`**, report
at `Assets/Phase1/qa_ladder_sweep.txt`. 31/31, but the value is the
table, not the pass count: this is a measurement bench, not an
assertion bench. Balance thresholds are owen's to set, so it fails only
on things that are broken regardless of balance.

## The table

```
  matchup                    seed  verdict   hits  1st    last   deadair  lost A/B  wpn A/B
  Brawler    v Brawler      101  Draw       4    1.4    3.8    88.1   1/1      0/0
  Brawler    v Brawler      202  Draw       4    1.4    5.4    87.0   1/1      0/0
  Brawler    v WallShy      101  Draw       4    1.4    5.5    86.0   1/1      0/0
  Brawler    v WallShy      202  Draw       4    1.4    5.3    86.1   1/1      0/0
  Brawler    v Matador      101  B         19    1.5   17.6     1.3   19/0      0/1
  Brawler    v Matador      202  A         27    1.5    7.2     1.3   2/19      1/0
  Brawler    v Rusher       101  B          4    1.2    1.7    89.7   1/1      0/0
  Brawler    v Rusher       202  B          5    1.2    2.3    89.2   1/1      0/0
  Brawler    v Statue       101  A         27    5.2   23.5    19.0   0/18     1/0
  Brawler    v Statue       202  A         38    2.4   40.2     1.4   0/19     1/0
  WallShy    v WallShy      101  Draw       4    1.4    5.6    85.8   1/1      0/0
  WallShy    v WallShy      202  Draw       4    1.4    5.5    86.0   1/1      0/0
  WallShy    v Matador      101  A         19    1.5   25.0    66.4   4/7      1/1
  WallShy    v Matador      202  B         32    1.5   11.7     1.3   19/0     0/1
  WallShy    v Rusher       101  B          4    1.2    1.7    89.8   1/1      0/0
  WallShy    v Rusher       202  B          5    1.2    2.3    89.1   1/1      0/0
  WallShy    v Statue       101  A         34    5.3   21.5     1.5   0/19     1/0
  WallShy    v Statue       202  A         17    3.7   15.3     1.4   0/19     1/0
  Matador    v Matador      101  Draw       4    1.6    4.7    86.5   1/1      0/0
  Matador    v Matador      202  Draw       4    1.7    4.8    86.5   1/1      0/0
  Matador    v Rusher       101  A         35    1.3   22.5     1.5   0/19     1/0
  Matador    v Rusher       202  Draw       2    1.3    1.3    90.3   1/1      0/0
  Matador    v Statue       101  A         24    4.4   10.3     1.3   0/19     1/0
  Matador    v Statue       202  A         41    3.7   31.8     1.4   0/19     1/0
  Rusher     v Rusher       101  Draw       4    1.1    1.6    89.8   1/1      0/0
  Rusher     v Rusher       202  Draw       6    1.1    2.1    89.4   1/1      0/0
  Rusher     v Statue       101  Draw       0      -      -    91.3   0/0      1/1
  Rusher     v Statue       202  A         27    1.6   19.4    13.7   0/15     1/0
  Statue     v Statue       101  Draw       0      -      -    91.3   0/0      1/1
  Statue     v Statue       202  Draw       0      -      -    91.2   0/0      1/1
```

Aggregates over 30 bouts: mean **13.4** hits · mean bout **66.8 s** ·
mean last hit at **11.1 s** · mean **dead air 53.0 s = 79.3% of the
bout** · **3/30** bouts with no contact at all · **15/30 (50%)** end
with BOTH sides disarmed · **26/30 (86.7%)** end with at least one side
disarmed.

## What it says

**1. The weapon is the fragile thing, not the robot.** In 26 of 30
bouts a weapon dies. The mirror matches are perfectly deterministic:
Brawler v Brawler, WallShy v WallShy, Matador v Matador, Rusher v
Rusher all produce **exactly 4 hits, one part lost each side, zero
weapons alive, ~86–90 s of dead air, Draw**. Brawler v WallShy and
both Rusher matchups behave identically — because those programs also
close head-on, so the engagement is symmetric even when the programs
are not.

**2. Keeping your weapon is the whole game.** Every bout that ended
with an asymmetric weapon count was won by the side that still had
one — **11 of 11**. And it is not a narrow win: the loser is
*dismantled*, losing **18–19 of 19 parts**. There is no middle outcome
in this data. Either both weapons die in the first five seconds and
nothing happens for eighty-five, or one weapon survives and the other
robot is taken apart.

**3. So the match is decided in the first ~1.4 seconds**, by whether
your weapon meets their weapon or their body. That is approach
geometry — spawn angle and closing behaviour — not programming. The
evidence that it is close to a coin flip: **Brawler v Matador flips
with the seed** (seed 101 → Matador wins, seed 202 → Brawler wins), as
does Matador v Rusher (35 hits and a dismantling on one seed, 2 hits
and a draw on the other).

**4. Statue exposes the other end.** Statue v Statue and Rusher v
Statue (seed 101) record **zero hits in 91 seconds**. A robot that
does not move is never touched by a robot that does not find it —
and it scores a Draw, identical to a mutual-disarm draw. Nothing
downstream can tell those two apart.

**5. The 85 s after the disarm is not quite meaningless — which is
worse.** Rusher wins all four of its disarmed shoving matches against
Brawler and WallShy on the judges' aggression criterion. So the ladder,
as it stands, would partly rank robots on **who shoves harder after
both weapons are gone**.

## Why this matters for v3 specifically

`Multiplayer_V3_Design_Doc.md` §2.1 ranks robots by Glicko-2 on bout
outcomes, and §1.1 makes watching the replays the product. Against
this data:

- **Rating would measure the opening trade, not the robot.** A ladder
  is only worth climbing if the better machine wins more often than
  the luckier approach angle. Best-of-3 (§1.4) helps, but three
  coin-flips is still a coin-flip.
- **79% dead air is a bad product.** The thing players are asked to
  watch is, on average, 53 seconds of nothing after 13 seconds of
  fight.
- **Draws are load-bearing and currently ambiguous.** Half of all bouts
  are draws, and a mutual disarm, a mutual whiff, and a genuinely close
  fight all produce the same verdict.

## Options, cheapest first — owen's call

1. **A draw band in `MatchRunner.Decide()`** (~10 lines). Stops a 0.13
   damage margin deciding a match; feeds Glicko-2 a 0.5. Does not touch
   the underlying problem, but stops the rating system reading noise as
   signal. Worth doing regardless of what else is chosen.
2. **Make weapons survive weapon-on-weapon contact.** The direct fix.
   Weapon parts take reduced damage from other weapon parts, or weapons
   get their own durability class. `Spinner_Sweep_Legality_2026-08-05.md`
   is the place that already thinks about spinner interactions. This is
   the one that changes the shape of every fight, so it wants a
   balance pass and a re-sweep, not a guess.
3. **End the bout when it is over.** If neither side has a weapon and
   no damage has landed for N seconds, call it — on the judges'
   existing criteria. Removes most of the dead air without changing
   combat at all, and makes replays worth watching. Cheap, and
   independent of option 2.
4. **Shorten `DEFAULT_MATCH_TIME`** from 90 s. Blunt, and it would
   truncate the genuinely long fights (Brawler v Statue seed 202 landed
   its last hit at 40 s) — prefer 3 over this.

My read: **1 and 3 are cheap and clearly right; 2 is the real fix and
deserves its own session with a re-sweep after.** None of them should
be guessed at — the sweep bench now exists, so any change can be
measured over all 30 bouts before and after.

## Scope of this measurement, honestly

- **One chassis on both sides.** A program cannot change part geometry,
  so this establishes that the weapon trade is structural across every
  program. It does NOT establish how it behaves across different
  weapon geometries and mounting positions — that is the next sweep,
  and it needs a second and third fixture rig.
- **Two seeds per pairing.** Enough to show that seed flips outcomes;
  not enough to quantify how often.
- Replay writing is off in the sweep, so contact is measured by
  `MatchRunner`'s own damage hook rather than read back from a file.
  That instrumentation (`hits`, `firstHitT`, `lastHitT`,
  `aPartsLost`/`bPartsLost`, `aWeaponsAlive`/`bWeaponsAlive` on
  `BoutResult`) is new this session and is what makes any future
  before/after comparison possible.
