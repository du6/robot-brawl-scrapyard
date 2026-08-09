# The opening disarm — found, fixed, and NOT what it looked like — 2026-08-09

Continues `Opening_Disarm_Tutorial_Floor_2026-08-09.md`, which established
that the L1 floor's wobble was one physical event — the opening exchange
shears both weapons and inverts the heavier robot — and named the cure:

> **B. Stop the disarm happening.** The preset approach/steering fix the
> handover identified — do not meet nose-to-nose at full closing speed.

That sentence names **two** things. Only the second half was load bearing.

## Headline

**`RobotProgram.Brawler()`'s IN RANGE hat commanded 100% throttle inside
2.2 m. That number, alone, was the whole bug.** It is now **45%**.

| | before | after |
|---|---|---|
| FLOOR (MatrixBench) | 6–8/10, run to run | **10/10, every bout a KO** |
| L1 opening disarm rate | **42%** | **0%** |
| player damage in the first 7 s | 107 | **358** |
| FLOOR bout length | 6–90 s, bimodal at 14 s | 6–21 s, one mode |

Backup: `_claude_backups/openram__RobotProgram.cs`.
`WallShy` derives from `Brawler` and inherits the fix for free.

## The bench that made it findable

**`Assets/Phase1/Scripts/OpeningBench.cs` (new).** `MatrixBench.RunFloor()`
could not have found this and could not have measured the fix. It is a
90 s-per-bout **win-rate** bench at N=10 against a true disarm rate near
1/3, so its output swings 6/10–8/10 with no code change: the measurement
noise is larger than any plausible effect. You cannot tune against it.

`OpeningBench` changes three things:

- **It measures the disarm RATE directly** — live edges still bolted on,
  by the same `DamageResolver.IsEdge` test `FightManager`'s own
  "both machines disarmed" call uses, so the two agree by construction.
- **It stops each bout at 7 s.** The event is over by t≈2 s. RunFloor
  spends 83 of every 90 seconds on a question this bench is not asking.
- **Arms run INTERLEAVED**, bout *k* of every arm back to back. Run A of
  the floor bench scored bouts 4 and 5 of a cell byte-identical
  (19 dealt / 10 taken / 14 s), so something latches across bouts inside
  one play session; a before/after run as two batches would confound that
  drift with the change. Same discipline as `AIController.avoidOverride`.

It **does not pass or fail.** It prints rates. A rate is what a bimodal
process has; a threshold on one is the mistake the floor check already
made once.

## Run 1 — four arms, N=12 each, interleaved

`Assets/Phase1/qa_opening_run1_arms.txt`

| arm | disarmed | flipped | dealt@7s |
|---|---|---|---|
| A shipped (ram 100%) | **42%** | 17% | 54 |
| B no-ram (ram 45%) | **0%** | 8% | 378 |
| C angled (ram 100%) | **42%** | 17% | 77 |
| D both (angled + 45%) | **0%** | 0% | 389 |

**Arm C is the finding.** It is a deliberately non-pursuit approach: inside
5 m it alternates half a second of homing with a third of a second of hard
lock, so it arrives on a line that is off the enemy's centre — exactly the
"do not meet nose-to-nose" half of the prescription — with the ram held at
the shipped 100%. It moved **nothing**: 42% and 42%, the same 5 of 12.

Arm C was worth building precisely because it came back null. Without it
the fix would have shipped as "approach geometry and closing speed", half
of it cargo, and the next person to touch the approach would have believed
the geometry was carrying weight it never carried.

**Why the geometry could not have been the cause, in hindsight:** two
robots that each steer to null their bearing on the other converge to a
head-on meeting *whatever* their starting geometry. Pure pursuit is
self-correcting toward nose-to-nose. Offsetting the spawn — which the
`BuilderManager` spawn does make tempting, since the two machines are
placed at `-axis*4` and `+axis*4` facing each other, a perfect mirror —
would have been an even more expensive null result.

## Run 2 — the sweep, because 45% was a guess

`Assets/Phase1/qa_opening_run2_sweep.txt`. A guess that works is still a
guess. Closing throttle 100/70/55/45/30, N=12 each, interleaved:

| ram % | disarmed | flipped | dealt@7s |
|---|---|---|---|
| 100 | **42%** (5/12) | 8% | 107 |
| 70 | 0% | 17% | 248 |
| 55 | 0% | 8% | 357 |
| **45** | **0%** | **0%** | **358** |
| 30 | 0% | 0% | 382 |

The 100% arm scored **5/12 in both runs, independently** — the effect
replicates exactly, which is more than the floor bench ever managed.

The cliff is between 100 and 70; the damage plateau is 45–30. **45 is the
fastest closing speed that scored 0% on both failure modes**, i.e. the
most closing authority the fix can keep. 30% deals 7% more damage in the
opening and was not chosen for it, because closing authority is what the
upper leagues need and the opening is not where it is spent.

## Why the ram was wrong

A spinner does its damage with the **disc**. A full-speed hull ram adds
its own impulse to the disc's contact impulse **at the weapon mount**. The
preset was paying for its own disarm — and the tutorial hands that preset
to a new player as their first fight.

Two failure modes, both from the same cause, and the second only visible
because the bench logged parts rather than outcomes:

- the disc **shears off** (10 of 48 rows in run 1 read `weap 0`); and
- the disc **survives but stops producing damage** — `weap 1`, dealt
  frozen at 19–20 for the rest of the bout. The spindle that spins it is
  a separate part, and `WeaponsAlive` counts edges, not motors.

The second mode is invisible to any bench that reads only the verdict.

## Verification — with the predictions written down first

**Prediction 1: FLOOR goes 8/10 → 9 or 10/10.** Reason: the disarm was the
only L1 loss mechanism (Brawler was 14/14 in bouts where a fight actually
happened) and the disarm rate is now 0/12, twice.
**Measured: 10/10. Right.** Every bout an `enemy core destroyed` KO in
6–21 s. Mean dealt 390→572 (scout) and 411→719 (tipper).

**Prediction 2: Brawler wins MORE upper cells but does not sweep.**
**Measured: it did not sweep — but it did not win more cells either.**
1/4 before, 1/4 after; fights 3/12 → 4/12. And **Matador, whose code was
not touched, went 4/12 → 7/12 in the same run.** UPPER is N=3 per cell and
cannot resolve a change of this size. The prediction was wrong in a way
that indicted the instrument, not the fix — and the untouched preset in
the same batch is what proved it. All three SWEEP claims still pass.

**Prediction 3: the fix generalises to the ladder.** `LadderSweepBench`
runs the real presets, so Brawler and WallShy appear in 18 of its 30
bouts. If the ram is the shear mechanism, both-disarmed should fall from
13/30 toward 5–8/30.
**Measured: WRONG, and this is the most useful of the three.**

| | 08-08 baseline | after |
|---|---|---|
| both sides disarmed | 13/30 (43.3%) | **13/30 (43.3%)** |
| either side disarmed | 26/30 (86.7%) | **26/30 (86.7%)** |
| mean hits per bout | 14.9 | 14.1 |
| mean LAST hit at | 13.9 s | **11.4 s** |
| dead air | 41.5% | **54.0%** |

The disarm counts did not move by a single bout, while the dynamics
plainly did — fights go quiet **sooner** and dead air is up 12 points.
So the bouts genuinely changed and the disarm outcome genuinely did not.

**Conclusion: the fix is LOCAL.** The ram explains the disarm in the
tutorial's head-on opening against a Rookie AI. It does **not** explain
the ladder's 43% mutual disarm — that is a different mechanism, still
open, and still the thing the 08-08 handover called "the one that decides
whether the ladder can rank anything". Two problems that looked like one.

## The trade this leaves on the table — OWNER DECISION

Nothing failed: every MatrixBench claim passes and `LadderSweepBench` is a
measurement bench with no thresholds. But the ladder got **less lively**:
dead air 41.5% → 54.0%, last hit 13.9 s → 11.4 s. A robot that closes at
45% is slower to re-engage after being knocked back, and that cost is real
even though no check names it.

Options, not a unilateral tune:

1. **Keep 45%.** The on-ramp is the thing that was broken, and it is now
   fixed with margin. Ladder liveliness is a measurement, not a promise.
2. **55%.** Also 0% disarmed (8% flipped, dealt 357 vs 358). Might buy
   back some of the pressure; needs a `LadderSweepBench` run to say.
3. **Make the ram conditional** — ram hard only when the opponent has no
   live weapon, which is when the mount is not at risk. The program
   language has no "enemy weapon alive" sensor today, so this is a
   language change, not a tune.

## Also true, and deliberately not done

- **`Matador`'s STRIKE has the same bug**: `MoveRel(Enemy, 100%, 1.5 s)`
  inside 2.2 m. It was left at 100% **on purpose** this session so it
  could not confound the measurement — and it earned its keep, because
  being the untouched arm is what exposed UPPER's N=3 noise. It is the
  obvious next single-variable change.
- **`AIController` rams too.** `OPENING_THROTTLE = 0.55` covers only the
  first `OPENING_TIME = 3 s` after the bell; after that `thr = 1f`
  unconditionally, every approach, for the rest of the match. If the ram
  shears mounts on the player's side it shears them on the AI's side, and
  that is a live hypothesis for the ladder's 43% — but the ladder is
  preset-vs-preset, so it is **not** an AI-side explanation there. Worth
  a bench of its own.

## Benches run

Green: **MatrixBench 10/10 FLOOR + 3/3 SWEEP, all checks pass** ·
**ProgramBench 40/40** · **OpeningBench** two runs (no pass/fail by
design) · **LadderSweepBench** 30 bouts, measurement only.

Artifacts: `qa_opening_run1_arms.txt`, `qa_opening_run2_sweep.txt`,
`qa_matrix_bench_after_ram45.txt`, `qa_ladder_sweep_after_ram45.txt`,
`qa_program_bench.txt`.

Career save `18614d0e6603869f28fb65992b7d1484`, mtime 08-05 21:57 —
verified unchanged after every bench. Backups
`career_backups/career_2026-08-09_1113_sessionstart.json` and profile.

## Method notes

**Build the instrument before you tune.** The temptation was to change the
preset and re-run `RunFloor()`. With a true rate near 1/3 and N=10 that
would have produced a number, and the number would have meant nothing —
the same instrument had already printed 6, 8 and 7 for identical code. An
hour spent on a bench that measures the *event* rather than the *outcome*
made the effect unmissable: 42% → 0%, replicated, in two minutes a run.

**Ship the null arm.** Arm C cost one arm's worth of bouts and retired
half of the prescription the previous session had written down. A fix that
lands with a passenger looks exactly like a fix that works.

**Keep an untouched arm in the batch.** Matador's code did not change and
its upper-league fights went 4/12 → 7/12. That single fact is what turned
"my prediction about UPPER was wrong" into "UPPER cannot resolve this",
which is a statement about the instrument and survives the session.

**A wrong prediction that indicts the instrument is worth more than a
right one.** Two of three predictions here were wrong. Prediction 2 found
that the SWEEP sample is too small to steer by; prediction 3 found that
the mirror lock and the tutorial disarm are two problems, not one. Neither
would have surfaced if the expectations had not been written down first.
