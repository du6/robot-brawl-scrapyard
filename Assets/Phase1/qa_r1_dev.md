# Round 1 Implementation Dev Report
Started 2026-07-26.
Critic report: /sessions/.../mnt/Phase1/qa_r1_critic.md (score 4.5, 28 matches)

## Plan (to be revised after reading source)
Candidate targets, highest value first:
1. CRITICAL: 12.5 m/s universal tip-speed clamp (Actuator.RateCeiling) - kills the
   long-arm tradeoff the whole Phase 4 feature is sold on.
2. CRITICAL: 952 kg solvency line (PowerPlant.DRIVE_KW_PER_TONNE=3.5 vs 300 kJ pack)
   - makes 4 of 7 materials traps; also causes findings 4 and 5.
3. CRITICAL: topple impulse 3-11x short (SHOVE_CAP/TIP_ARM_M).
Deferred likely: hook model, motion blur, material legibility, damage number overlap.

## Log

### Source read (round trips 1-6)
- PowerPlant.DRIVE_KW_PER_TONNE = 3.5f (round-6 rebalance from 9). Battery 240 kJ/8 kW,
  engine 60 kJ/14 kW. Critic's 952 kg solvency line arithmetic CONFIRMED from source.
- Actuator.MaxRate / RateCeiling: safe = STEP_DISP_CAP(0.25)/(tipR*dt) => tipSpeed
  == 0.25/0.02 == 12.5 m/s for EVERY rotary limb. Critic CONFIRMED from source.
- ROOT CAUSE I found that the critic did not name: E = 0.5*I*w^2 with I ~ m*R^2 and
  w = v/R gives E = 0.5*m*v^2 -- energy depends on MASS and TIP SPEED only, never on R.
  And wind-up time = E / MOTOR_KW where MOTOR_KW is a FLAT 2.5 kW for every actuator on
  every robot. So energy-throughput (=DPS) is pinned at ~2.5 kW * EFFICIENCY for every
  weapon in the game, which is exactly why the 1099 J whisk and the 13997 J hook measured
  the same damage rate. The clamp is a symptom; the flat motor is the reason a big
  weapon cannot convert its size into anything.
- DamageResolver.ApplyHit: damage is LINEAR in energy. Confirms DPS-is-flat.
- GyroStabilizer: righting torque is skipped while |angularVelocity| > MAX_SPIN (5 rad/s),
  so a genuine fast flip is NOT opposed mid-roll. A topple only has to clear 5 rad/s.
- FightManager: flipped == dot(bot.up, up) < 0.2 (>78 deg). INCAP_GRACE 2 s.
- BuilderManager ALREADY shows "Runs flat in Ns of hard use" / "Endurance Ns". So the
  critic's fix_direction on finding 1 ("surface the number") is already done; the number
  itself is what is unaffordable. Not re-implementing that.

### Chosen fixes (3, one patch)
F1  PowerPlant.DRIVE_KW_PER_TONNE 3.5 -> 1.25  (findings 1, 4, 5)
F2  Actuator motor power becomes a BUILD DECISION (scales with fitted engines) +
    tip-speed cap scales with the limb's weakest material + swept trigger volume
    so the higher speeds cannot tunnel  (finding 2, and "expensive materials are worse")
F3  Energy-correct topple: L = sqrt(2*I_victim_about_axis*E_lift), shared by the
    actuated (Actuator.Bite) and static (CompoundRobot) wedge paths  (finding 3)
DECLINED this round: hook model, motion blur, arena material legibility, ownership cue,
damage-number overlap, cascading seam teardown. Reasons in the return value.

### PATCH APPLIED (one batch, 18 edits, 4 files) - backups *.r1dev.bak
PowerPlant.cs    1  DRIVE_KW_PER_TONNE 3.5 -> 1.25
Actuator.cs     11  MotorKW(engines) 1.5+3.0/engine cap 12 ; TipSpeedCap(strengthRel)
                    = clamp(12.5*sqrt(s), 9, 18) ; RateCeiling/CycleSeconds overloads ;
                    Recompute derives motorKW + tipSpeedCap from LIVE parts ;
                    UpdateSweptVolumes() swept trigger ; ApplyTopple()/InertiaAboutWorldAxis()
CompoundRobot.cs 1  static wedge topple -> ApplyTopple with E = J^2/(2*mu)
BuilderManager.cs 5 LimbInfo.tipSpeedCap ; quotes tip m/s, motor kW, s a swing ;
                    endurance uses fitted-engine motor
BASELINE (from critic, pre-patch, taken as given - deterministic from source):
  tipSpeed == 12.50 m/s for every rotary limb (tipR .660 -> 18.94, .980 -> 12.76,
  1.025 -> 12.20). MOTOR_KW flat 2.5. t_flat = 300/(0.0035*m); 952 kg line.
  Topple max 168.75 N.m.s ever, typical 44.5 ; 0 topples in 28 matches.
Now forcing the domain reload.

## MEASUREMENT 1 - compile + constants (static, no play mode)
Compiles: 0 compile errors. Console errors present are BuilderManager.OnGUI:2211
(palette null on a BuilderManager that survived the domain reload) - the brief's
known reload artefact, cleared by the recovery cycle. Not from this patch.
drive=1.25 (was 3.5)
motor by engines 0/1/2/3/4 = 1.5/4.5/7.5/10.5/12.0 kW   (was flat 2.5 for all)
tipSpeedCap ABS/Al/Steel/CF/W/Ti = 9.00/9.68/12.50/13.11/13.69/14.79 m/s  (was 12.50 for all)

## MEASUREMENT 2 - the 12.5 m/s universal clamp is gone
SPINDLE  tipR -> rate rad/s / TIP SPEED m/s
  0.300  ABS 30.00/9.00   Al 32.27/9.68   Steel 41.67/12.50  Ti 49.30/14.79
  0.660  ABS 13.64/9.00   Al 14.67/9.68   Steel 18.94/12.50  Ti 22.41/14.79
  0.980  ABS  9.18/9.00   Al  9.88/9.68   Steel 12.76/12.50  Ti 15.09/14.79
  1.025  ABS  8.78/9.00   Al  9.45/9.68   Steel 12.20/12.50  Ti 14.43/14.79
  1.375  ABS  6.55/9.00   Al  7.04/9.68   Steel  9.09/12.50  Ti 10.76/14.79
PIVOT (design ceiling 14 rad/s binds on short arms, so tip speed also varies with R there)
  0.300  all 14.00/4.20
  0.660  ABS 13.64/9.00  Al/Steel/Ti 14.00/9.24
  0.980  ABS 9.18/9.00  Al 9.88/9.68  Steel 12.76/12.50  Ti 14.00/13.72
  1.375  ABS 6.55/9.00  Al 7.04/9.68  Steel 9.09/12.50  Ti 10.76/14.79
=> Steel column reproduces the OLD numbers EXACTLY (18.94, 12.76, 12.20, 9.09), which
   is the intended invariant: Steel-grade == the old behaviour, so every prior
   measurement on a Steel edge still holds. Every other material now differs.
   Ti edge buys +18% tip speed over Steel == +40% stored energy for the same mass.

## MEASUREMENT 3 - the flat-DPS root cause (the real reason a big arm was worthless)
critic's whisk  spindle I=6.13   tipR 0.660 Steel : rate 18.94  E  1099 J (unchanged)
critic's hook   pivot   I=172.06 tipR 0.980 W     : rate 13.97  E 16796 J (was 12.76 / 13997)
Hook damage-energy throughput (E*DRAIN_FRAC / cycle seconds):
  0 engines  motor  1.5 kW : cycle 12.48 s ->  538 W
  1 engine   motor  4.5 kW : cycle  5.02 s -> 1338 W
  2 engines  motor  7.5 kW : cycle  3.53 s -> 1905 W
  3 engines  motor 10.5 kW : cycle  2.89 s -> 2327 W
BEFORE: motor was 2.5 kW for everyone, so throughput was 2.5 kW * DRAIN for EVERY
weapon in the game and the critic measured the 1099 J whisk and the 13997 J hook at
the same damage rate. AFTER: a whisk is still capped near 880 W by HIT_COOLDOWN
(440 J drain / 0.5 s), while the hook reaches 2327 W if you fit the engines to feed
it. 4.3x spread where there was 1.0x. The big arm is now buyable.

## MEASUREMENT 4 - solvency line, the critic's four exact builds reloaded
build                  mass kg  driveKW(was)   t_flat_drive s AFTER (BEFORE)
qa_r3_b2_whisk_cf        508.9  0.64 (1.78)     472  (168)
qa_r2_b4_flipper_abs     449.7  0.56 (1.57)     534  (191)
qa_r3_b3_plow_steel     1642.1  2.05 (5.75)     146  ( 52)   <- critic measured 61/66/68
qa_r3_b1_hook_w         3856.4  4.82 (13.50)     62  ( 22)   <- critic measured 23
The 952 kg solvency line is now 2667 kg. Steel (1642) and Tungsten frames (2501)
clear a 90 s match; the 3856 kg Tungsten-frame-AND-Tungsten-arm extreme still does
not, which is correct - that build should have to choose.
LimbReport after patch (tipCap / rate / tipSpeed / E):
  whisk_cf   spindle I 6.13   tipR 0.660 Steel blades cap 12.50 rate 18.94 tip 12.50 E  1099 J (unchanged)
  hook_w     pivot   I 172.06 tipR 0.980 Tungsten     cap 13.69 rate 13.97 tip 13.69 E 16796 J (was 13997)
  flipper_abs pivot  I 53.84  tipR 1.375 ABS          cap  9.00 rate  6.55 tip  9.00 E  1153 J (was 2226)
  plow_steel  no actuator (static wedge)
NOTE AGAINST MYSELF: the ABS flipper got WEAKER (E 2226 -> 1153 J) because an ABS
edge is now correctly capped at 9 m/s. That invalidates the sizing note I wrote in
LIFT_EFFICIENCY, which cited the ABS flipper's OLD 2544 J drain. Energy conservation
now says an ABS flipper CANNOT topple a 707 kg machine: drain 461 J * lift 0.75 = 346 J
available against 818 J needed, so it would need >100% efficiency. The correct
instrument for the topple test is a Steel or Titanium flipper. Comment to be corrected
in the second patch; measuring both flippers below rather than assuming.

## MEASUREMENT 5 - SWEEP S1, the critic's exact scenario re-run
qa_r3_b3_plow_steel (1642 kg Steel, static wedge) vs tipper/Veteran, charge, n=4, fire ON
AFTER:  W0/L4  len 80.8-90.4s spread 1.12x CONVERGED
  run0 Loss 90.0s dealt 289 taken 350  pieces 8/10 vs 8/12  PLAYER FLIPPED 4.8s   Judges
  run1 Loss 80.8s dealt  98 taken 437  pieces 8/10 vs 9/12  PLAYER FLIPPED 14.4s  count-out, but the
       cause line is "240 kJ of its 300 kJ pack was TORN OFF the chassis, and what was left ran
       dry after 71 s" -- i.e. structural loss of the battery, NOT the drive budget
  run2 Loss 90.4s dealt 158 taken 844  pieces 9/10 vs 9/12  PLAYER FLIPPED 0.4s   Judges
  run3 Loss 90.4s dealt  21 taken 404  pieces 9/10 vs 9/12  PLAYER FLIPPED 1.6s   Judges
  worst single-step piece loss: player 1, enemy 1 (critic MAJOR 6 did not reproduce here)
BEFORE (critic): W1/L3, 4 of 4 decided by a power count-out (3 against), dealt 320/304/93/47,
  0 flips anywhere in 28 matches.
READ:
 + F1 CONFIRMED. 0 of 4 matches decided by the drive budget. The one count-out is a battery
   being sheared off, which is a legitimate loss path the design wants.
 + F3 CONFIRMED FIRING. The player was flipped onto its back in ALL FOUR matches
   (4.8 / 14.4 / 0.4 / 1.6 s) by the tipper's wedge. Across three prior rounds and ~110
   matches the critics recorded ZERO topples. The verb exists now.
 - The plow's record went 1W/3L -> 0W/4L and damage TAKEN roughly doubled. Honest reading:
   the enemy no longer runs dry either, so a build whose entire plan was "outlast on power"
   lost its plan. That is the intended consequence of F1, but it is a real balance shift and
   the next round should watch it. Recording it rather than burying it.

## MEASUREMENT 6 - SWEEP S2, actuated Aluminium flipper (929 kg) vs mauler/Veteran, charge n=4
W1/L3 flips=1 len 64.4-90.0s spread 1.40x CONVERGED. worst single-step loss: player 0 enemy 2.
  run0 Loss 72.4 dealt  37 taken 138 PLAYERflip 10.8s  power count-out, all 300 kJ in 63 s
  run1 Loss 77.6 dealt  98 taken 193 PLAYERflip  7.2s  power count-out, all 300 kJ in 68 s
  run2 Loss 64.4 dealt 156 taken 361 PLAYERflip 17.5s  power count-out, all 300 kJ in 55 s
  run3 Win  90.0 dealt 459 taken 256 PLAYERflip 12.0s  Judges, enemy cut to 5/11 pieces
Live poll mid-run0: t=25.2 PLAYERflip 6.40 ENEMYflip 0.00, MAULER up=0.93 angV=3.85 rad/s.
EnemyRoster: MAULER is an "aluminium WEDGE-brawler" and TIPPER is the flipper - so the
player flips in S1 and S2 are those bots' wedges going through the SAME ApplyTopple I
added, i.e. the topple verb is firing, just from the receiving end.

## MEASUREMENT 7 - SWEEP S3, Steel flipper (2215 kg, liftE 2659 J) vs bulwark/Veteran
W1/L2/D1 flips=2 spread 1.42x NOISY. All four matches involved a power count-out; in
THREE of them the cause line is "240 kJ of its 300 kJ pack was TORN OFF the chassis".
Live poll t=50.0: player actuator rate=0.00/9.09 E=0 motor=4.5 tipCap=12.50 - flat.

## READ ON MEASUREMENTS 6-7: I OVERSHOT THE MOTOR. Correcting it, not defending it.
MOTOR_KW_BASE 1.5 + 3.0/engine puts the DEFAULT one-engine build at 4.5 kW, which is
1.8x the old flat 2.5 kW. Energy per swing is E/EFFICIENCY regardless of motor, so a
faster motor buys more swings and therefore spends MORE energy per match. Measured
consequence: the 929 kg flipper emptied a 300 kJ pack in 55/63/68 s where its DRIVE
draw alone would have taken 259 s. That is the "weapon, not locomotion, is the dominant
draw" rebalance the critic asked for - but it arrived as a straight endurance nerf to
every existing build, which is not what the fix was for.
Re-pitching to BASE 1.0 + 1.5/engine so that ONE ENGINE == EXACTLY 2.5 kW, the old
value: no existing build regresses on power at all, and engines 2/3/4 (4.0/5.5/7.0 kW)
become the upgrade path that lets a big limb convert its size into throughput.
Also raising LIFT_EFFICIENCY 0.6 -> 0.85: at 0.6 the 929 kg Al flipper offers 987 J
against the ~818 J needed to roll a 707 kg machine - a 1.2x margin that real contact
losses eat. 0.85 gives 1399 J (1.7x) and is still < 1.0, so it cannot manufacture energy.
Second patch now.

## MEASUREMENT 8 - THE TOPPLE BENCH. Controlled, same robot, same axis, same match.
Instrument: live fight, player = qa_r2_b9_flipper_al, enemy = MAULER (spawned m=637 kg).
Enemy velocities zeroed before each trial; tipAxis = cross(up, attacker->victim horizontal),
which is the exact axis Actuator.Bite and CompoundRobot use.
  enemy m = 637 kg, I about the tip axis (read from its own inertia tensor) = 80.3-87.9 kg.m2

CONTROL - the OLD formulation at its ABSOLUTE BEST, which the pre-patch code could never
exceed: shove capped at DamageResolver.SHOVE_CAP 500 N.s, L = 500 * 0.75 * 0.45 = 168.75 N.m.s
  -> w0 = 1.92 rad/s, rotational KE = 162 J
  -> dot(up, world up) 0.998 BEFORE, 0.996 four seconds AFTER. IT DID NOT MOVE.
     ENEMY flippedTime 0.00 s.
NEW - ApplyTopple with the Aluminium flipper's real bite energy
(E 5484 J * DRAIN_FRAC 0.4 = 2194 J drained, * liftBias 0.75 * LIFT_EFFICIENCY 0.85 = 1399 J)
  -> I = 80.3, L = sqrt(2*I*E) = 473.8 N.m.s, w0 = 5.90 rad/s
  -> ENEMY flippedTime 0.00 -> 0.60 s. The machine went past dot(up) < 0.2, i.e. onto its
     back, and its gyro then righted it (up back to 0.994, still rolling at 5.71 rad/s).
=> 0.00 s flipped -> 0.60 s flipped from ONE hit, on the same robot in the same match.
   Critic CRITICAL 3 is fixed and the fix is measured, not asserted. The 2 s INCAP_GRACE
   means a single flip is a tempo and scoring event rather than an instant win, which is
   the pre-existing design and I did not change it.
Also note the critic's arithmetic is now independently confirmed from the other direction:
the old term really was incapable of moving a 637 kg machine at all, even at its cap.

## MEASUREMENT 9 - SWEEP S4: S2 re-run IDENTICALLY after the motor recalibration
Same snapshot (qa_r2_b9_flipper_al 929 kg), same opponent (mauler/Veteran), same policy,
same n. ONLY change: MotorKW at 1 engine 4.5 -> 2.5 kW, LIFT_EFFICIENCY 0.6 -> 0.85.
                        S2 (motor 4.5)              S4 (motor 2.5)
  record                W1/L3                       W3/L1
  power count-outs      3 of 4, ALL against player  1 of 4, against the ENEMY, and its
                        ("all 300 kJ spent in       cause is structural ("300 kJ of its
                        55/63/68 s")                pack was TORN OFF the chassis")
  dealt                 37 / 98 / 156 / 459         470 / 199 / 276 / 263
  taken                 138 / 193 / 361 / 256       271 / 171 / 303 / 186
  player pieces         13/13 every match           13/13 every match
  enemy pieces          10/10/10/5 of 11            5 / 10 / 5 / 6 of 11
  spread / verdict      1.40x CONVERGED             1.44x CONVERGED
The endurance regression I introduced is gone and the build is stronger than it was under
either the old or my first constants.

## MEASUREMENT 10 - outcome mix, post-recalibration matches only (S1 + S4, n=8)
  judges' decision at 90 s .... 6  (75.0%)
  power count-out ............. 2  (25.0%)  BOTH caused by the pack being physically
                                            sheared off, not by the drive budget
  KO / all-wheels / beached ... 0
  topple as a WIN condition ... 0  (but see below)
Critic finding 4 ("89% decided by a clock or a battery", 35.7% power count-outs) is only
PARTLY addressed: the budget-driven count-out is gone, but decisions now dominate even
harder and I produced no KOs in 8. NOT claiming this one fixed.
Toppling now happens constantly as an EVENT even though it never ended a match: player
flipped 4.4 / 16.8 / 16.8 / 36.5 s of 90 s in S4 and 0.4-14.4 s in S1, against ZERO
topples in ~110 matches across the three prior rounds. FLAG FOR ROUND 2: a wedge bot now
puts its opponent on its back for up to 40% of a match. That may be too easy - the verb
went from impossible to routine in one step and the next critic should sweep it.

## FINAL EDITOR STATE (verified, not assumed)
Unity console: 0 errors, 0 warnings (Unity_GetConsoleLogs logTypes=Error, totalCount 0).
Compiles clean; both domain reloads recovered with the full stop/start/recreate cycle.
owen's build RESTORED and loaded: 790 bytes, 16 parts, 4 wheels, mass 842.5, Validate()==null.
  qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes - byte-identical to
  qa_build_prewheelfix.txt, i.e. untouched. Nothing was ever written over any snapshot file.
BuilderManager.mode = Build. Time.timeScale = 1. Phase0Input.debugFire = False.
RepeatHarness object destroyed.
Backups kept: Actuator.cs.r1dev.bak, Actuator.cs.r1dev2.bak, PowerPlant.cs.r1dev.bak,
BuilderManager.cs.r1dev.bak, CompoundRobot.cs.r1dev.bak (Phase0). Nothing deleted.
New data files written this round: qa_r4_s1_plow.txt, qa_r4_s2_flipper.txt,
qa_r4_s3_flipsteel.txt, qa_r4_s4_flipper_remeasure.txt.

## DECLINED THIS ROUND, WITH REASONS
- MAJOR "hook has no model" / MAJOR "no motion cue on a spinning limb" / MAJOR "material
  legibility dies in the arena" / MINOR "no ownership cue" / MINOR "damage numbers overlap":
  all five are art/renderer work in PartVisualFactory and the HUD. They are real and I
  believe the critic, but none of them can be VERIFIED by me - verification is a screenshot
  read by eye, and the critic's own tooling note says the only usable capture rig is
  Unity_SceneView_CaptureMultiAngleSceneView with renderers manually disabled. Bundling
  them with three physics fixes into one domain reload would also have made a regression
  impossible to attribute. They are a coherent single-round job for a visual pass.
- MAJOR "whole-machine structural teardown": did not reproduce in my 16 matches (worst
  single-step piece loss was player 1 / enemy 2, against the critic's 5 and 9). I will not
  rewrite the seam solver against a bug I cannot currently trigger; the right next step is
  a repro, not a fix.
- CRITICAL 4 "89% decided by a clock or a battery": partly addressed as a side effect (the
  budget count-out is gone) but I did not touch the judges' card, and I produced zero KOs
  in 8 post-fix matches. Explicitly NOT claimed.
# R1 DEV LOG (implementation engineer, round 1 of 5)
Started. Plan: read code first, pick 2-3 findings I can finish AND measure.

## R1-IMPL (round 1 of 5, 2026-07-27) — engineer log

### MEASUREMENT 0 — build costs (baseline for the budget finding)
Loaded every critic build through BuilderManager and summed P1Placed.Cost():
  qa_owen_build_SAFE   16 parts  842 kg    750 cr
  qa_c6_hammer_abs     12 parts  398 kg    279 cr
  qa_c5_discus_abs     11 parts  464 kg    344 cr
  qa_c4_flipper_al     13 parts  929 kg   1361 cr
  qa_hammer_test       12 parts 1673 kg   1355 cr
  qa_c2_rotor_cf       14 parts  560 kg   1533 cr
  qa_c1_hammer_ti      12 parts 1135 kg   3278 cr
  qa_c3_lance_w        12 parts 3904 kg  22386 cr   <-- 6.8x the next dearest
All Validate() == OK.

### PATCH 1 (one domain reload) — files touched
Pre-patch copies saved as *.r1dev2pre.bak (the *.r1.bak names were already taken
by an earlier round-1 dev; I did not overwrite them).
  Phase0/Scripts/CompoundRobot.cs   VITAL_SEAM_MUL 1.60 (HP-scaled) + detach ledger
                                     + per-source damage counters + VitalSeamLoad()
  Phase1/Scripts/DamageResolver.cs  ApplyHit gains a src tag (ram/limb/disc)
  Phase1/Scripts/Actuator.cs        limb bites tagged SRC_LIMB
  Phase1/Scripts/FightManager.cs    StructBand() part-count floor; pack-seam HUD warning
  Phase1/Scripts/BuilderManager.cs  CREDIT_BUDGET 4000 + BuildCost() + Validate gate
                                     + credits line + per-part cr + battery-seam advisory
  Phase1/Scripts/RepeatHarness.cs   per-run damage-source split, packSeam, detach ledger
Roster costs measured for the budget: scout 341, mauler 660, ripper 724,
tipper 743, bulwark 1516, widowmaker 1827.

### MEASUREMENT 1 — budget gate + judges' band (arithmetic, verified in-engine)
CREDIT_BUDGET=4000, VITAL_SEAM_MUL=1.60, MIN_STRUCT_PARTS=2.
BuilderManager.Validate() after the patch:
  qa_owen_build_SAFE  750 OK      qa_c6_hammer_abs   279 OK
  qa_c5_discus_abs    344 OK      qa_hammer_test    1355 OK
  qa_c4_flipper_al   1361 OK      qa_c2_rotor_cf    1533 OK
  qa_c1_hammer_ti    3278 OK
  qa_c3_lance_w    22386 REJECTED "Over budget: 22386 cr of 4000."
=> the W4/L0-no-parts-lost Tungsten lance can no longer reach StartFight; every
   other build in the project, including owen's, is untouched.
Judges' structure band, 11-part robots: 0.060 -> 0.136 (16 parts: 0.094).
  9/11 vs 10/11 (diff 0.091)  BEFORE structure decides -> AFTER falls to damage
  5/11 vs  7/11 (diff 0.182)  structure decides, before and after
  2/11 vs 10/11 (diff 0.727)  structure decides, before and after
=> reverses exactly the critic's "+31.1% damage lost on a ONE-PART difference"
   card, and leaves the two-part-and-worse cards decided on structure, which I
   think is correct: 2 pieces clear is a difference a spectator can point at.

### MEASUREMENT 2 — BEFORE sweep (fix disabled at runtime, instrument on)
qa_c1_hammer_ti vs MAULER/Veteran, charge, debugFire ON, n=8, timeScale 3.
VITAL_SEAM_MUL forced to 1.0 => byte-identical to the code the critic measured.
File: Assets/Phase1/qa_r1d_sw_before.txt
  W7/L1/D0, len 36.9-90.3 s, worst single-step piece loss player 1 / enemy 3.

MECHANISM CONFIRMED, and it is exactly what the critic described. The pack sits
on THREE 1-socket Aluminium seams (owen's build, all six critic builds and every
roster bot bar tipper are the same) whose effective break threshold is
  min(strengthRel) x BREAK_K x sockets x LoadFactor = 0.6 x 1500 x 1 x 1 = 900 N.s
while attributed stress is capped at STRESS_J_CAP = 1400. The ledger shows the
seams shearing ONE AT A TIME at 930-1400 N.s over the match, then the orphaned
pack sheds:
  PlayerBuild battery_3 STRUCTURAL-SHED hp 56/56 (100%)
  PlayerBuild battery_3 STRUCTURAL-SHED hp 53/56  (94%)
  MAULER      battery_4 STRUCTURAL-SHED hp 45/56  (80%)
  MAULER      battery_4 STRUCTURAL-SHED hp 31/56  (55%)
  MAULER      battery_4 STRUCTURAL-SHED hp 10/56  (18%)
PLAYER PACK LOST IN 2 OF 8 MATCHES (25%), the worse of the two at 100% HP.
So "multi-socket battery mounts" is NOT the missing piece - the pack already has
three mounts; each one is individually below the stress cap, so the machine just
loses them in sequence.

### SIDE FINDING (critic CRITICAL 2) — the damage card, split by source
New per-run instrument, same 8 matches, player side:
  run 0  ram 306/17 hits · limb  40/1  (max limb hit 40.4)
  run 1  ram 190/22       · limb 401/20 (35.2)
  run 2  ram 246/10       · limb 142/5  (36.8)
  run 3  ram 275/18       · limb  18/1  (17.7)
  run 4  ram 219/11       · limb  30/1  (30.3)
  run 5  ram 389/30       · limb 104/5  (24.5)
  run 6  ram  98/7        · limb 306/6  (68.7)
  run 7  ram 329/23       · limb 103/4  (32.9)
Limb damage is 36% of the mean card; ram is 64%. THE CRITIC'S A/B COMPARED
WHOLE-MATCH CARDS, which are 2/3 chassis ram - a quantity that does not depend
on limb energy at all - so the card could not have shown a 17x energy difference
even if the limb converted perfectly.
And the limb does NOT convert well: this hammer's advertised store is 3281 J, so
a full-energy bite is (0.4x3281 + linImp<=600) x 1.7 x 0.045 = 146 dmg. The
LARGEST limb hit observed in 43 limb hits across 8 matches was 68.7, and the
per-run maxima cluster at 24-40. Back-solving, most bites land at
drain+linImp ~ 300-900, i.e. the limb is biting at roughly the BITE_RATE_FRAC
gate (0.45 x MaxRate => E = 0.2025 x Emax) rather than anywhere near peak.
That is the real reason energy does not predict damage: energyJ/kjPerSwing are
quoted at MAX rate and the weapon almost never bites there. NOT FIXED THIS
ROUND - see the declined list.

### MEASUREMENT 3 — AFTER sweep (VITAL_SEAM_MUL 1.60, everything else identical)
File: Assets/Phase1/qa_r1d_sw_after.txt.  Same build, same opponent, same policy,
same n, same instrument; the ONLY difference is the runtime value of one static.

                                    BEFORE (1.0)      AFTER (1.60)
  player battery-seam shears           5 across 8         0
  player pack lost                     2 of 8 (25%)       0 of 8
  worst case                           shed at 56/56 HP   -
  packSeam load at the bell            0.00-0.84          0.97 every run
  record                               W7/L1              W8/L0
  mean damage dealt                    387                535
  worst single-step piece loss (P/E)   1 / 3              1 / 3

0.97 is the number that matters most: the seam is being loaded to 97% of its new
effective threshold (1400 attributed against 900x1.6 = 1440), so this is a
near-miss and not an over-correction. 1.60 is the smallest multiplier that clears
STRESS_J_CAP and it clears it by 3%.

COUNTERPLAY IS INTACT AND WAS OBSERVED, not asserted:
  MAULER|SEAM chassis_1<->battery_4|SHEARED at 1400 vs eff 1395
i.e. once the mauler's pack was down to ~92% HP its bay dropped under the cap and
sheared exactly as before. Three more mauler packs went HP-DESTROYED at 0/56.
So "shoot the pack out" now works and "jostle it off at full health" does not,
which is the trade the finding asked for.

HONEST CAVEAT: the record moving W7/L1 -> W8/L0 is ONE match at n=8 and I do not
claim it as a result. The mechanism numbers (5 shears -> 0, packSeam 0.97) are
the evidence. Mean damage rising 387 -> 535 is mechanistically coherent - two of
the eight BEFORE matches had the player flat and undriveable for their back half
- but it is also the change I would watch in round 2 for the fix being too kind.

### MEASUREMENT 4 — the controlled LIMB-ONLY energy A/B (critic CRITICAL 2)
Same opponent, policy, n and instrument; the two geometrically identical hammers.
Isolating the LIMB column, which is the only damage that can depend on limb energy:

                          qa_c1_hammer_ti     qa_c6_hammer_abs     ratio
  stored energy               3281 J               193 J           17.0x
  advertised kJ/swing          9.37                0.55            17.0x
  cost                        3278 cr             279 cr           11.7x
  limb damage per MATCH       139.6               144.9            0.96x
  limb damage per HIT          26.0                12.7            2.05x
  limb hits landed          43 in 8 matches     80 in 7 matches     0.54x
  largest single limb hit      64.9                45.2            1.44x
  theoretical full-energy bite 146                 51.8            2.82x

THE MECHANISM, stated properly. Per HIT the chain is not badly broken - 2.05x
measured against a 2.82x ceiling. What cancels it is HIT COUNT: the cheap light
limb lands 1.9x as many bites, because it winds up to BITE_RATE_FRAC faster and
recovers faster, and 2.05 / 1.9 = 1.08. Per match the two weapons are the same
weapon. Two compounding causes, both worth a round of their own:
  (a) neither hammer ever bites near its stored energy - Ti's ceiling is 146
      dmg/hit and its MEAN is 26 - because BITE_RATE_FRAC lets a bite land at
      0.45 x MaxRate, where E is 0.2025 x Emax;
  (b) LIN_IMP_CAP = 600 contributes a flat term worth ~46 dmg that is the SAME
      for both machines and is 92% of the ABS hammer's theoretical hit.
NOT FIXED THIS ROUND. Changing either without re-running both sweeps would be
exactly the kind of asserted headline this project has already been burned by.

Side note from the ABS sweep: VITAL_SEAM_MUL is material-sensitive rather than a
blanket immunity, which is right. The ABS build's pack bay is
min(ABS 0.35, Alu 0.6) x 1500 x 1.6 = 840 N.s, still under STRESS_J_CAP, and it
sheared in every match - a cheap frame still cannot hold a pack. It only ever
lost ONE of its three bays per match, so the pack itself survived except in the
run that ended in a core KO.

### FINAL STATE (verified in-engine)
mode=Build  playing=True  timeScale=1  debugFire=False  detachLogOn=False
VITAL_SEAM_MUL=1.60  CREDIT_BUDGET=4000  MIN_STRUCT_PARTS=2  VITAL_WARN_FRAC=0.70
owen's build reloaded from qa_owen_build_SAFE.txt: 16 parts, 844 kg, 750 cr, Validate OK.
Console errors: 0.
Build panel now reads "Parts: 16   Mass: 844 kg   Credits: 750 / 4000" and
"battery bolted on 3 seams - losing one will not cost you the pack".
NOTE ON UI VERIFICATION: the panel is IMGUI (OnGUI), which does not render into
Unity_Camera_Capture, so I could not screenshot it. What IS verified is that the
new OnGUI code runs every frame in Build mode with owen's build loaded and throws
nothing (0 console errors), and that the strings it builds are the ones above.
qa_owen_build_SAFE.txt untouched (checked byte-identical to qa_build_prewheelfix.txt).

### DECLINED THIS ROUND, with reasons
1. CRITICAL 2 (energy -> damage). MEASURED, not fixed. Measurement 4 says the
   per-hit chain is only 1.4x short of its ceiling and the real cancellation is
   hit COUNT (light limb lands 1.9x the bites). Fixing that means touching
   BITE_RATE_FRAC / RECOVER_S / LIN_IMP_CAP, each of which has a written,
   measured reason for its current value, and re-running both 8-match sweeps per
   candidate. I would rather hand round 2 a decisive measurement than an
   unverified constant. The two sweep files are the before-set for that work.
2. CRITICAL 4 (roster difficulty labels inverted). Re-tuning roster CHASSIS, as
   the critic correctly says it must be, means re-measuring all six bots against
   several player builds. It is a whole round's work and it would have made my
   own before/after unattributable, because the mauler is my control opponent.
3. MAJOR 5 (flipper self-topple). Note that my AFTER sweep reproduced the
   self-flip on a NON-flipper: qa_c1_hammer_ti run 3 spent 50.7 s (56% of the
   match) on its back and still won on damage. So this is not wedge-specific and
   the diagnosis in the critic's finding (heavy limb hung off ONE SIDE) is
   probably incomplete. Worth a dedicated round.
4. MAJOR 6a (six parts in one FixedUpdate). Partially reproduced: the ABS sweep
   shows worst single-step player loss of SEVEN. But that run ended "KO - your
   core was destroyed", i.e. it is CompoundRobot's deliberate wreck path
   (RebuildIslands: `bool wreck = dead || core detached` -> shed budget = all),
   not the shed cap failing. Capping the wreck teardown is a decision about how
   a KO should LOOK, not a structural-pacing bug, and I did not want to change
   it on a guess. The non-wreck cap is holding: worst non-wreck step loss was 1.
5. MAJOR 7 + MAJOR 8 (motion cue, ownership/collar). Rendering work in
   PartVisualFactory. Genuinely valuable and cheap in isolation, but it cannot
   be verified from this seat - OnGUI and per-frame trails are exactly what I
   just found I cannot screenshot - and I judged one measured structural fix
   worth more this round than three asserted visual ones.
6. MINOR 9 (SPINDLE_MAX_RPM 9.4x unreachable) and MINOR 10 (drive-budget
   count-out). Real but small; 9 is a one-line honesty fix that belongs with the
   build-screen work in 5.
