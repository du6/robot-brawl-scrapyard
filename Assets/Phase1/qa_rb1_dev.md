# ROUND 1 DEV (implementation agent) - 2026-07-27
Responding to qa_rb1_critic.md (score 3.5). Appended continuously.

## Plan / priority
1. CRITICAL "no weapon beats an empty nose; 80% of armed runs land ZERO weapon
   hits". The critic gave a code-supported HYPOTHESIS for the mechanism (the
   shared PART_IMMUNITY window swallowing limb bites) but explicitly did not
   isolate it. FIRST JOB: try to REFUTE that mechanism with a direct funnel
   measurement, and find the real one.
2. MODERATE undriven-disc warning (BuilderManager:3006) - cheap, certain,
   both-sides checkable.
3. MAJOR spinner-vs-spinnerSaw domination - only if the CRITICAL leaves budget.
   Not going to rearrange weapon stats before knowing whether weapons land.

## Instrument added (MEASUREMENT ONLY, no behaviour change)
- Actuator.cs: static funnel counters in Bite() - fnCall (trigger callbacks),
  fnTarget (callback is on a live enemy part), then the three gates that can
  reject it: fnGate (tip speed), fnEnergy (MIN_BITE_E), fnCool (per-collider
  HIT_COOLDOWN), fnApply (reached DamageResolver.ApplyHit). Plus fnGateWorstFrac
  = best rate*arm / gateV ratio seen, so a near-miss on the speed gate is
  distinguishable from never spinning.
- DamageResolver.cs: immEatLimb/immEatRam/immPassLimb/immPassRam around the
  PART_IMMUNITY early-return, split by src. This is the exact counter that
  decides the critic's hypothesis.
- RB1Probe.cs: reset both before each run, print both after.
Backups: *.rb1dev.bak in Assets/Phase1/Scripts.

## Editor state on arrival
NOT clean, again: the same BuilderManager.OnGUI:2754 NRE flood (15/15 console
entries errors, all timestamp 09:05:21) from a BuilderManager that survived a
domain reload. Standard recovery applied: destroyed 1 BM, 0 ModeSelect, created
one fresh BM, reset timeScale/debugFire/debugPointer. This is the THIRD handover
in a row where "0 console errors" was claimed and was not true - it is the
reload, not the previous agent.

## M1. THE CRITIC'S MECHANISM IS REFUTED (qa_rb1d_funnel.txt, 15 runs / 360 s)
SOURCE REGIME 89d20372. Same fixtures, same opponent (mauler), trigger held.

The critic's MAJOR said: a limb bite is swallowed by the shared 0.5 s
PART_IMMUNITY that the ram stamps first, so it reads 0/0 while still paying 40%
of the limb's energy. The counter that decides this is `immEatLimb`.

  build    runs   Bite() callbacks   reached a live enemy part   immEatLimb
  NOWEAP     3            0                    0                      0
  DISC_Z     3        8619/9239/8411           0  0  0                0 0 0
  SAW_Z      3       12315/11760/11824         0  0  0                0 0 0
  COAX_V     3        3160/3065/3080           0  0  0                0 0 0
  HAMMER     3         869/425/2822           35  0  0                1 0 0

immEatLimb IS ZERO ON EVERY DISC RUN. Not one disc bite was ever swallowed by
the immunity window, because not one disc bite ever got as far as the immunity
window. The mechanism is real but it is not the cause: across 15 runs the
window ate exactly ONE limb hit (HAMMER r0, 1 eaten vs 2 passed) against 76 ram
hits it passed. REFUTED as the explanation for "80% of armed runs land zero
weapon hits".

## M2. THE REAL SHAPE OF IT - the disc's hit volume never touches the enemy
Read the two columns again. A DISC_Z run fires the bite path 8619 TIMES in 22 s
and NOT ONE of those callbacks is on an enemy part. Every one is rejected in the
first six lines of Bite() - own body, world, debris. The disc trigger is
overlapping something continuously (8619/1100 fixed steps = ~8 colliders every
step) and that something is its OWN machine.

Meanwhile the SAME runs land ram hits on the enemy (7, 10, 4). So the chassis
reaches the enemy and the disc bolted to its nose does not.

That is not a damage-model problem, a gate problem or a cooldown problem. It is
GEOMETRY. Next: measure the forward extent of the disc's trigger box against the
forward extent of the chassis' own solid colliders. If the chassis stands proud
of the disc, an upright enemy can never be touched by it and no tuning of
DRAIN_FRAC, PART_IMMUNITY or BITE_TIP_SPEED_MS can ever change that.

## M3. THE FUNNEL, FULLY BROKEN OUT (qa_rb1d_funnel2.txt, 12 runs / 288 s, n=4)
Finer counters: every early return in Bite() now has its own tally.

  run          calls  noRb   notRobot  onTarget  gate  cool  APPLY  limb dmg
  DISC_Z r0    22547  10547     0          0      0     0     0        0
  DISC_Z r1     9884   9866     0         18      0    17     1       40/1
  DISC_Z r2    10640  10577    63          0      0     0     0        0
  DISC_Z r3    10257  10257     0          0      0     0     0        0
  SAW_Z  r0    12517  12517     0          0      0     0     0        0
  SAW_Z  r1    12904  12904     0          0      0     0     0        0
  SAW_Z  r2    15737  15679    48         10      0     8     2      102/2
  SAW_Z  r3    12987  12987     0          0      0     0     0        0
  HAMMER r0     1425   1425     0          0      0     0     0        0
  HAMMER r1      580    580     0          0      0     0     0        0
  HAMMER r2     1937   1845    72         20      8     9     3      125/2
  HAMMER r3     2516   2415    90         11      0     8     3      178/3

READ THE `onTarget` COLUMN. A 22 s run is ~1100 physics steps. `onTarget` is the
number of (step x enemy-collider) pairs in which the weapon's hit volume was
overlapping the enemy AT ALL. In 8 of 12 runs it is ZERO - the weapon did not
touch the opponent once in 1100 steps. In the other 4 it is 10-20, i.e. the
weapon was in contact for 0.2-0.4 s out of 22.

AND WHEN IT DOES TOUCH, IT WORKS. Those same 4 runs are the 4 highest limb-damage
runs: 40, 102, 125, 178 dmg at 40-62 PER BITE against the chassis ram's 15.5.
The weapon is not weak. It is not gated out (gate rejects 8 of 59 on-target
callbacks, and 0 of 48 for the discs). It is not starved (energy rejects 0). It
is not eaten by a cooldown that matters (`cool` is just OnTriggerStay firing 50x
a second during one continuous 0.36 s contact - correctly one bite per contact).

THE BOTTLENECK IS CONTACT OPPORTUNITY AND NOTHING ELSE.
The chassis rams the enemy 2-8 times in the very same runs where the weapon
touches zero times. Ramming is 72% of damage because the hull is ~9 solid boxes
covering the whole machine and the weapon is one box on the nose. That is the
CRITICAL, restated as a mechanism instead of a symptom.

`noRb` (~10-15k per run) is the weapon trigger overlapping WORLD geometry every
single step. Chased it: the disc's trigger sits 0.051 m BELOW the arena floor
(see M4), and the scene holds 6 builder_floor + 2 arena_floor colliders. I
suspected a per-fight leak; DISPROVED - the count was 6+2 before a 12-fight
sweep and 6+2 after. It is one pair per BuilderManager, i.e. per recovery, not
per fight. Not a gameplay bug. Not filed.

## M4. NEW: THE SHIPPED SPINDLE RECIPE GRINDS ITS ROTOR THROUGH THE FLOOR
Actuator.GroundBlocked exempts spindles, and says why:
   "a rotor built to sweep through the deck is a build error the arc picker
    cannot fix either, and NO MEASURED BUILD DOES IT."
That last clause is now false. Measured (qa_rb1d_floor.txt, qa_rb1d_ybands.txt):
  DISC_Z, at rest, arena:  disc SOLID underside y = 0.000, floor top y = 0.000
                           disc TRIGGER underside y = -0.051  (inside the floor)
  every chassis part:      0.040 - 0.100 m of clearance
  GROUND_CLEAR band:       0.020
The disc is the machine's lowest point and it is a ground-bearing skid.

Swept through a full turn (qa_rb1d_dipcheck.txt), the arena's own solid collider
reaches 0.064 m BELOW the clearance band. DISC_Z is byte-identical to owen's
qa_owen_build_SPINDLE and is the tipper/widowmaker roster recipe.
The build screen said nothing, and structurally COULD not: arcFrac is the only
clearance number the panel has and a spindle is exempt from it, so every rotor
reports a clean 1.0 forever.

## FIX 1 (critic MODERATE) - undriven-disc warning counts ANY actuator
BuilderManager.cs. Was: `li.act.def.id.StartsWith("spindle")`. Now: any limb
whose `blockedBy == null` and whose members contain the disc. A disc on a PIVOT
is driven; a disc WELDED to the frame (blockedBy != null) is still undriven, and
already has its own louder seam-naming warning.
Text also changed: "N disc(s) with no spindle" -> "N disc(s) bolted straight to
the frame ... Put it past a spindle to spin it, or a pivot to swing it, or keep
it as a fixed edge."

BEFORE / AFTER over all 13 critic fixtures + owen's two builds
(qa_rb1d_fixcheck.txt). Column = number of discs the panel calls undriven:
  build         warnOLD  warnNEW
  DISCPIVOT        1  ->   0     <- the critic's build. False positive GONE.
  FIXDISC          1  ->   1     <- disc bolted to frame. TRUE positive KEPT.
  FLAIL_V          1  ->   1     <- disc welded to the frame. KEPT (correct).
  OWEN_SAFE        1  ->   1     <- owen's own fixed-edge disc. KEPT.
  DISC_Z / SAW_Z / COAX_V / SAW_V / OWEN_SPINDLE   0 -> 0   unchanged
  the 6 disc-less builds                           0 -> 0   unchanged
EXACTLY ONE BUILD OF 15 CHANGED, AND IT IS THE ONE THAT WAS FILED. No collateral.

THE OTHER SIDE (this project's signature failure): the fix is builder-only, so
the question is whether the ARENA agrees a pivot really drives a disc. It does,
and the critic already measured it - DISCPIVOT wires as pivotYP kind=Pivot
limbN=2 members=[beam spinnerZP], I=44.5, tipR=0.97, and it bit for 187 dmg /
3 hits. The builder was the side that was wrong. Nothing in Actuator changed.

## FIX 2 (new, from M4) - the builder now predicts a rotor that grinds the deck
`LimbInfo.rotorDip` + `RotorFloorDip()`: sweeps a spindle's members through 360
deg (72 steps = 5 deg) about the mount axis, using the SAME deck rule and the
SAME exact VertExtent support of the rotated box that ArcClearance already uses -
not a bounding sphere, per that code's own warning. Warns above 5 mm.

PREDICTION ONLY. Actuator.GroundBlocked is untouched, deliberately: round 1
declined to loosen it and was right, and adding a spindle guard would stall the
weapon outright, which the source says in as many words.

BUILDER (new) vs ARENA (measured by sweeping the real collider, qa_rb1d_dipcheck):
  DISC_Z        builder 0.080 m   arena 0.064 m   both > 0, builder conservative
  SAW_Z         builder 0.165 m   (bigger disc, hangs lower - consistent)
  OWEN_SPINDLE  builder 0.080 m   (same recipe as DISC_Z - consistent)
  COAX_V, SAW_V builder 0.000 m   VERTICAL axle: a rotor about Y never changes
                                  height as it turns. Correctly silent.
  every non-spindle build         0.000 m         silent
The 16 mm gap is ride height: the builder's deck is wheel-centre minus wheel
radius, the arena's raycast suspension settles slightly higher. The builder errs
EARLY, which is the right direction for a warning.
BOX ORIENTATION CHECKED, because this is exactly where this project breaks:
PlacedPart.Half() permutes a spinner's size onto its mount normal (returns
(0.17,0.17,0.05) for a +Z axle) and the arena's solid collider is (0.34,0.34,0.10).
Builder and arena agree on the box. Verified before the number was believed.

## M5. REGRESSION CHECK - and an accidental A/A test of the noise floor
Rule 5: re-run what I did NOT change. My whole diff outside the builder panel is
static counter increments, so the ARENA is bit-identical. Same fixtures, same
opponent, same n=3, before (qa_rb1d_funnel.txt) vs after (qa_rb1d_regress.txt):

  build    BEFORE                        AFTER
  NOWEAP   ram 208/8  limb   0/0  m 69.3 | ram 114/9  limb   0/0  m  38.0
  DISC_Z   ram 136/21 limb   0/0  m 44.0 | ram 278/22 limb   0/0  m  92.7
  SAW_Z    ram 204/16 limb   0/0  m 68.0 | ram 244/36 limb 195/3  m 146.3
  COAX_V   ram 124/10 limb   0/0  m 41.3 | ram 350/23 limb   0/0  m 116.7
  HAMMER   ram 150/11 limb  95/2  m 81.7 | ram  88/6  limb 606/24 m 231.3
  TOTAL    ram 12.5 per bite             | ram 11.2 per bite

NO REGRESSION: the per-bite rate, which is the stable statistic, moved 12.5 ->
11.2 over 66 and 96 hits. Nothing I touched can reach the arena and nothing did.

But look at the MATCH TOTALS on identical code: HAMMER 81.7 -> 231.3 is 2.8x and
DISC_Z 44.0 -> 92.7 is 2.1x. This is an unintentional but clean A/A TEST - two
n=3 samples of bit-identical combat - and it independently re-derives, and
slightly WIDENS, this project's 2.4x noise floor. Treat any n=3 match-total
comparison on this game as meaningless. The per-bite figures were stable to 10%
across the same pair. The project's own rule is correct and now has fresh
evidence behind it.

Also worth carrying forward: HAMMER landed 24 limb bites in 3 runs here, against
2 in the previous 3. Weapon engagement is not merely rare, it is BIMODAL - the
critic reached the same conclusion (its M4) after being forced to retract a
"never bites" claim. Any future weapon sweep needs n>=9 and should report the
bite COUNT distribution, not a mean.

## WHAT I DECLINED, AND WHY
1. CRITIC'S MAJOR "PART_IMMUNITY swallows limb bites". REFUTED by direct
   measurement (M1), not declined - immEatLimb is 0 across every disc run and 1
   across 15 runs total. The code path the critic describes is real and I left it
   alone; it is not what is costing weapons their damage. Filing a fix for it
   would have been a fix to a mechanism that fires once per 15 matches.
2. THE CRITICAL ITSELF. I isolated it (M3: contact opportunity, not damage
   math) but did NOT ship a combat change. The candidate fix - make a rotary
   limb's HIT_COOLDOWN its rotor period instead of a flat 0.5 s, so a disc in
   contact for 0.36 s at 11.7 rev/s strikes more than once - is coupled to
   PART_IMMUNITY: extra bites on the SAME victim part would be swallowed by the
   0.5 s window while still paying DRAIN_FRAC, which is precisely the critic's
   mechanism, currently harmless, made harmful. That is two coupled changes to
   the damage subsystem, and the brief forbids shipping two risky changes to one
   subsystem. It also would not address the 8-of-12 runs with ZERO contact.
   Handing this to round 2 with the funnel instrument in place and the mechanism
   named is worth more than a rushed knob.
3. SPINNER-VS-SPINNERSAW (critic MAJOR). Not touched. It is a catalogue-balance
   change, and re-pricing weapons before they demonstrably land is the mistake
   the previous five rounds made. Note for round 2: the catalogue text is
   already self-contradictory - the spinner promises "heavy rim, lots of stored
   energy in a small package" and the saw "less rim mass", but measured volumes
   are spinner 0.0116 and saw 0.0127, so the saw is simply the bigger, heavier,
   longer-reach part on every axis. The intended trade is not implemented. That
   is a Phase1Parts data fix, not a tuning pass.
4. STANDING FINDINGS 1-9. No new evidence gathered, none re-filed.
   Finding 9 (ISO pane) not tested for an eighth time.

## FILES CHANGED
  Assets/Phase1/Scripts/BuilderManager.cs   FIX 1 (undriven-disc gate + text),
                                            FIX 2 (LimbInfo.rotorDip,
                                            RotorFloorDip(), ROTOR_STEPS, warning)
  Assets/Phase1/Scripts/Actuator.cs         funnel counters in Bite() - PURE
                                            instrument, every increment sits on
                                            an existing control-flow path
  Assets/Phase1/Scripts/DamageResolver.cs   immEat/immPass counters around the
                                            PART_IMMUNITY return - PURE
  Assets/Phase1/Scripts/RB1Probe.cs         prints the funnel line per run
Backups: *.rb1dev.bak beside each.
NOT touched: Actuator.GroundBlocked, PART_IMMUNITY, HIT_COOLDOWN, DRAIN_FRAC,
BITE_TIP_SPEED_MS, Phase1Parts, EnemyRoster, AIController.

## DATA LEFT ON DISK
  qa_rb1d_funnel.txt    15 runs, first funnel (refutes the critic's mechanism)
  qa_rb1d_funnel2.txt   12 runs, full rejection breakdown (isolates the cause)
  qa_rb1d_geom.txt      DISCARDED - measured along the spawn->enemy axis, which
                        was 148 deg off the machine's own forward. Kept as the
                        record of a wrong turn, superseded by geom2.
  qa_rb1d_geom2.txt     extents along the machine's own drive forward
  qa_rb1d_ybands.txt    vertical bands, weapon vs enemy vs floor
  qa_rb1d_floor.txt     per-part ground clearance; the disc at 0.000
  qa_rb1d_fixcheck.txt  FIX 1 + FIX 2 before/after over 15 builds
  qa_rb1d_dipcheck.txt  arena-side swept rotor dip (the other side of FIX 2)
  qa_rb1d_regress.txt   post-fix regression / A-A noise test

## MY OWN ERRORS
1. My first extent probe measured along the player->enemy direction at spawn and
   concluded the disc was 1.23 m behind the front of the machine - i.e. mounted
   backwards. The spawn pose was 148 deg off the drive forward. I checked the
   axis before believing the number and the real answer is the opposite: the
   disc leads the chassis by 0.40 m. Exactly the fixture error the brief warns
   about, caught by the check the brief prescribes.
2. I hypothesised the 6 builder_floor + 2 arena_floor colliders were a per-fight
   leak. Counted before and after a 12-fight sweep: 6+2 both times. Disproved my
   own hypothesis before filing it.
3. I expected the weapon to be blocked by chassis geometry or by height. Both
   wrong: the weapon leads by 0.40 m and overlaps the enemy's vertical band by
   0.40 m. The cause is contact opportunity, which I only found by counting.

## EDITOR LEFT
1 BuilderManager, 0 probe objects, Build mode, qa_owen_build_SPINDLE loaded
(17 parts, Validate OK), timeScale 1, debugFire false, debugPointer false,
funnel counters reset. Console: 0 errors, 0 warnings, checked 25 s after the new
warning had been drawing every OnGUI frame on a build that trips it.
qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes - UNCHANGED.
