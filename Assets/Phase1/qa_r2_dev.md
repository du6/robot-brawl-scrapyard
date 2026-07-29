# Round 2 Dev report — Robot Brawl: Bolt & Blade
Started 2026-07-27 (agent: implementation engineer, round 2 of 5)

## Plan (pre-work)
Critic's round-2 findings, ranked by (value x can-I-verify-it-this-round):
1. CRITICAL: legacy disc dominates all Phase-4 weapons  <- root cause is MAJOR "limb bites almost never register" (duty cycle)
2. MAJOR: limb bite duty cycle ~15%  <- THE lever. Fix rate, not damage.
3. MAJOR: SpinnerWeapon never tags SRC_DISC (one arg). Trivially fixable + verifiable.
4. MAJOR: stored energy does not convert (LIN_IMP_CAP flat 600 term)
5. MAJOR: ram is worst per hit by 5x
6. CRITICAL: no regime stamp on datasets

## BEFORE (my own control, same session, live pre-patch code)
Cell: mauler/Veteran, policy=charge, debugFire=true, timeScale 3, n=4. RepeatHarness.
File: Assets/Phase1/qa_r2d_before_b10.txt
b10_hammer_al (pivot hammer, Al arm, Steel blade, 13 parts):
  W4/L0/D0  dealt mean 501.5  taken mean 201.8
  LIMB 1280 dmg over 61 bites = 20.98 / bite   max limb hit 65.6
  RAM  726 dmg over 100 hits
  (critic measured 21.9/bite on the same file - per-bite reproduces exactly;
   its bite COUNT of 21 does not, mine is 61. Bite count is high-variance.)
Live tipSpeedCap: ABS 9.00 Al 9.68 Steel 12.50 Ti 14.79 CF 13.11 W 13.69

## MECHANISM I DERIVED FROM THE CODE (before touching it)
Actuator.FixedUpdate integrates the motor THROUGH the sweep:
  w(t)=sqrt(2Pt/I), theta(t)=(2/3)sqrt(2P/I) t^1.5
Eliminating t, the energy the limb carries WHEN IT REACHES ANGLE theta is
  E(theta) = 0.5 * (3*P*theta)^(2/3) * I^(1/3)
i.e. bite energy depends on I^(1/3) and on MOTOR POWER, and is INDEPENDENT of
the limb's stored-energy ceiling. Stored energy 0.5*I*MaxRate^2 never enters.
That is exactly why 2.88x stored energy bought only 1.21x damage:
  (I_Ti/I_Al)^(1/3) with Emax ratio 2.88 and cap ratio (14.79/9.68)^2=2.33
  gives I ratio 1.24 -> E-at-angle ratio 1.07. Measured 1.33 incl. the linear term.
The build screen's "J a hit" (= energyJ*DRAIN_FRAC) was therefore a fiction:
the limb only ever carried ~20% of it at contact.

b11_hammer_ti (SAME geometry as b10, limb re-materialled Titanium; E=2875 J):
  file Assets/Phase1/qa_r2d_before_b11.txt
  W2/L2/D0  dealt mean 241.0  taken mean 238.5
  LIMB 423 dmg over 16 bites = 26.44 / bite   max limb hit 52.1
  RAM  543 over 50 hits

>>> BEFORE A/B RESULT (reproduces the critic's headline exactly):
    stored energy   Ti/Al = 2875/998  = 2.88x
    damage per bite Ti/Al = 26.44/20.98 = 1.26x      (critic measured 1.33x)
    bite COUNT      Ti/Al = 16/61      = 0.26x   <-- the better limb lands 4x FEWER hits
    match damage    Ti/Al = 241/501    = 0.48x   <-- the better limb is WORSE
    => damage ~ E^0.26 per bite. The I^(1/3) law above predicts E^0.33-ish. Confirmed.

b3_lance_w (RAM actuator, Tungsten spike, E=2012 J), file qa_r2d_before_b3.txt:
  W2/L1/D1  dealt mean 360.5  taken mean 417.5
  LIMB 367 over 21 strokes = 17.48 / stroke   max limb hit 24.3
  RAM 1075 over 125 hits
  Confirms the critic: the highest-stored-energy weapon in the game is the
  weakest per hit. Mechanism from the formula above: a 0.45 m stroke has no
  room to accelerate, so contact happens at ~1.6 m/s (the old 0.45x3.5 gate).

b5_discus_al (legacy pre-made spinnerSaw, NO Phase 4 parts), qa_r2d_before_b5.txt:
  W4/L0/D0  dealt mean 720.3  taken mean 94.3   (2 KOs, 1 count-out)
  card reads "disc 0/0" in all 4 runs while dealing 496-1094 -> SRC_DISC bug
  reproduced in my own session.
THE BAR: the legacy disc deals 720/match. Best Phase 4 build (b10) deals 501.

## PATCH (one batch, one domain reload)

### PATCH 1 result: mechanism CONFIRMED, outcome REGRESSED. Diagnosed, not asserted.
b10 AFTER patch1 (qa_r2d_after_b10.txt): W1/L2/D1 dealt mean 320 (was 501.5)
  LIMB 685 over 20 bites = 34.25/bite  (was 20.98 -> per-bite x1.63 AS PREDICTED)
  bite COUNT 20 (was 61) -> x0.33.  self-flip mean 4.35 s (was 1.7 s)
Live probe mid-fight proved the latch does what it says:
  ACT phase=Winding rate=9.45 MaxRate=9.45 travel=0.000 E=998J  <- full charge, arm at rest
  PowerPlant storedKJ=228.7/300.0 peakKW=22.0 drawKW=6.8 supplyFrac=1.000
ROOT CAUSE of the regression, from those numbers:
  the old code only ever built ~0.2*Emax per cycle, so it only ever PAID for
  0.2*Emax. The latch builds 998 J every 1.077 s and the arc-end branch
  DELETES all of it (rate=0f on Returning) whether or not it hit anything.
  That is 926 W mechanical / EFFICIENCY 0.35 = 2.65 kW electrical, continuous,
  on a 1-engine machine - a 7x rise in the weapon's power bill. Pack capacity
  is 300 kJ; the match is 90 s. The pack goes flat, supplyFrac falls, wind time
  balloons, the DRIVE dies too (ram damage fell 726->594, taken rose 202->341,
  self-flip 1.7->4.4 s). Every secondary symptom follows from one line.

### PATCH 2: spring recuperation + motor stall-torque cap. b10 re-measured.
regime 96ce6370 (file qa_r2d_after2_b10.txt)
b10 BEFORE   W4/L0/D0  dealt 501.5  taken 201.8  limb 1280/61 = 20.98/bite  max 65.6  selfflip 1.7s  enemyflip 1.7s
b10 PATCH1   W1/L2/D1  dealt 320.0  taken 340.8  limb  685/20 = 34.25/bite  max 53.9  selfflip 4.4s  enemyflip 0.6s
b10 PATCH2   W3/L1/D0  dealt 336.5  taken 263.0  limb  725/21 = 34.52/bite  max 76.4  selfflip 1.9s  enemyflip 5.7s
  - stall cap fixed the self-flip regression outright (4.4 -> 1.9 s, BEFORE was 1.7 s)
  - per-bite damage holds at +64% over BEFORE; biggest limb hit in the project 76.4
  - enemy flipped time 1.7 -> 5.7 s/match: the heavier swing now topples people
  - bite COUNT is still 21 vs 61 and match damage is still short of BEFORE.
    n=4, sd of dealt ~180, so 501 vs 336 is 165 +/- ~128 - NOT a clean result
    either way. Reported as an open regression, not as a win.

### THE A/B THE CRITIC NAMED AS THE ACCEPTANCE TEST (b10 Al vs b11 Ti, identical geometry)
                    stored E   dealt   taken   limb dmg / bites  per-bite  max hit
 BEFORE  b10 Al       998 J   501.5   201.8    1280 / 61          20.98     65.6
 BEFORE  b11 Ti      2875 J   241.0   238.5     423 / 16          26.44     52.1
 AFTER2  b10 Al       998 J   336.5   263.0     725 / 21          34.52     76.4
 AFTER2  b11 Ti      2875 J   395.8   175.0    1214 / 36          33.70     82.4

 Ti/Al on MATCH damage   : BEFORE 0.48x  ->  AFTER 1.18x
 Ti/Al on LIMB damage    : BEFORE 0.33x  ->  AFTER 1.67x
 Ti/Al on bite count     : BEFORE 0.26x  ->  AFTER 1.71x
 Ti/Al on damage TAKEN   : BEFORE 1.18x  ->  AFTER 0.67x  (lower is better)
 => the 2.88x-energy limb went from LOSING on every axis to WINNING on every
    axis. That is the finding "stored limb energy does not convert" reversed.

 HONEST CAVEAT, stated up front: my own BEFORE sample of b10 was a high
 outlier. The critic measured the SAME FILE at dealt 286 / limb 459 over 21
 bites; I measured 501 / 1280 over 61. Pooling the two n=4 before-sets:
   b10 BEFORE(n=8) ~394 dealt  ->  AFTER 336   (flat to slightly down)
   b11 BEFORE(n=8) ~294 dealt  ->  AFTER 396   (up ~35%)
 So the ORDERING flip is robust across both before-sets; the absolute level of
 the Aluminium build is not clearly improved and I am not claiming it is.

### RAM ACTUATOR (critic MAJOR: "the worst weapon in the game by a factor of five per hit")
b3_lance_w, mauler/Veteran, charge, n=4, same instrument both sides.
                     dealt   taken   limb dmg / strokes   per stroke   max hit   record
 BEFORE (mine)       360.5   417.5     367 / 21             17.48       24.3     W2/L1/D1
 BEFORE (critic)     360       492       97 /  9             10.8       12.1     W2/L2
 AFTER2              484.8   288.0    1390 / 33             42.12       65.9     W3/L1 (one KO at 8.1 s)
  per stroke  x2.41 vs my before, x3.90 vs the critic's
  max hit     x2.71 vs my before, x5.45 vs the critic's
  damage TAKEN -31%, match damage +34%
  No ram-specific damage model was written. The 0.45 m stroke simply never had
  room to accelerate into a target that was already touching it; the latch
  means the stroke now STARTS at 3.5 m/s carrying its full 2012 J.
  The ram is now the hardest-hitting weapon per contact in the game
  (42.1/stroke vs 33.7-34.5 for the pivot hammers), which is the correct
  ordering for the highest-stored-energy weapon. The critic's finding that
  "actuator KIND, not stored energy, orders the weapons" is closed.

### DISC CONTROL + THE SRC_DISC FIX VERIFIED (b5_discus_al, NO Phase 4 parts)
BEFORE card: "disc 0/0" in all 4 runs while dealing 496-1094.
AFTER  card: "disc 243/2", "disc 49/1", "disc 49/1", "disc 49/1". Column is live.
UNPLANNED FINDING THIS FIX IMMEDIATELY PRODUCED, and it changes the CRITICAL:
  the legacy "discus" build lands only ONE OR TWO disc bites a match. Of its
  306 mean damage, 209 is chassis RAM and 98 is the disc. The finding
  "the legacy pre-made disc beats every Phase 4 weapon" is really
  "CHASSIS RAMMING beats every Phase 4 weapon" - the disc is a bumper.
  Nobody could see that before, because the disc's damage was filed as ram.

### NOISE FLOOR - the most important caveat in this report
mauler (my control opponent) is skeleton + engine + battery + gyro + spike:
verified from EnemyRoster.cs, it carries NO actuator. So none of my Actuator
edits could touch the enemy side of any sweep, and for b5 (which has no
actuator either) the ONLY change between my two n=4 samples was which COUNTER
the disc's damage is filed in - a change that cannot alter one damage number.
  b5 BEFORE n=4: dealt 720.3 taken  94.3  W4/L0
  b5 AFTER  n=4: dealt 306.3 taken 223.3  W3/L1
That is a 2.4x swing in mean damage across two samples of BIT-IDENTICAL
behaviour. n=4 in this harness cannot resolve anything smaller than about 2x.
Every match-damage comparison in this report has to be read against that, and
it is why I lean on PER-BITE numbers and on the ORDER of the builds, which are
much tighter, rather than on match totals.

### THE CRITIC'S SIX-ROW TABLE, RE-RUN (mauler/Veteran, charge, n=4, fire held)
                        dealt  taken  record   limb dmg/bites  per bite  max hit
 b3  ram lance          485    288    W3/L1     1390 / 33       42.1      65.9
 b11 pivot hammer Ti    396    175    W3/L1     1214 / 36       33.7      82.4
 b10 pivot hammer Al    337    263    W3/L1      725 / 21       34.5      76.4
 b5  legacy disc        306    223    W3/L1       98 /  4       24.5      n/a
Two Phase 4 builds now out-damage the legacy disc and all four builds have the
same record. Against the critic's own before-table (disc 589, best Phase 4
456) the gap is closed. Given the noise floor above I claim the ORDERING, not
the margins, and this table should be re-run at n>=8 before anyone banks it.

### EDITOR STATE ON EXIT
mode=Build, playing=True, timeScale=1, debugFire=False, detachLogOn=False,
0 RepeatHarness objects, 0 console errors.
owen restored from qa_owen_build_SAFE.txt: 16 parts, 842 kg, 750 cr, Validate OK.
qa_owen_build_SAFE.txt UNTOUCHED: 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b,
byte-identical to qa_build_prewheelfix.txt.
Backups: *.r2dev.bak (patch 1) and Actuator.cs.r2dev2.bak (patch 2). The .r2.bak
names were already taken by an earlier round-2 agent; nothing was overwritten or
deleted.
SOURCE REGIME of everything measured after the patch: 96ce6370 (22 scripts).

### ARITHMETIC PROOF THAT SWINGS NOW LAND AT FULL STORED ENERGY
The build screen quotes "J a hit" = energyJ * DRAIN_FRAC. For b10 that is
998 * 0.4 = 399 J. A bite's damage is (drain + linImp) * hardness * DMG_K,
blade hardness 1.7, DMG_K 0.045 -> factor 0.0765.
  Largest b10 limb hit measured AFTER: 76.4  ->  effImpulse 76.4/0.0765 = 999
  = drain 399 + linImp 600, and 600 is exactly LIN_IMP_CAP.
So the biggest hit is a full-charge bite plus a fully-capped closing term, to
within rounding. Before the patch the largest b10 hit was 65.6 -> effImpulse
857, of which the drain component could not have exceeded ~0.2*399 = 80.
The number on the build screen was a ~5x over-quote; it is now the truth.

### WHAT I DID NOT DO, AND WHY
- CRITICAL "doing nothing wins" (parked beats driving). NOT TOUCHED. It needs
  either a scoring term for initiative or an AI that only commits with an
  angle; either is a design decision, and its acceptance test is a paired
  AFK-vs-charge sweep in the exact cell I spent this round perturbing. Doing it
  now would have made both results unattributable. It is also the finding my
  noise-floor measurement bites hardest: the critic's AFK-vs-charge separation
  (628 vs 589 dealt) is well inside the +/-2x band I measured on b5.
- MAJOR "Tungsten is unbuildable at 4000 cr". Not touched - it is a pricing/
  budget decision, not a bug, and the critic already supplied a legal
  replacement A/B (b10 vs b11), which is what I used.
- MAJOR/MINOR visual work (team tint, motion cue off rate/MaxRate, damage
  marks). Not touched. Third round running these are declined by a dev who
  cannot see the frame; they need the round that pairs a dev with the camera.
  NOTE FOR THAT ROUND: Actuator.phase now includes Winding, which is exactly
  the wind-up tell the critic asked for - a charge meter or emissive ramp on
  rate/MaxRate during Winding, flashing at release, is now trivial to drive.
- LIN_IMP_CAP inertia scaling (the critic's own suggested lever). Deliberately
  left at 600 so the latch's effect could be attributed. It is now the LARGEST
  remaining diluter: at full charge it contributes 600 of a b10 bite's 999 and
  600 of a b11 bite's 1750, which is why per-bite Ti/Al is 0.98 while stored
  energy is 2.88. That is the single highest-value next constant to move.
