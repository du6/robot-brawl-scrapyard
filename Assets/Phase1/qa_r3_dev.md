# Round 3 dev log
Started. Plan: read source for the 4 CRITICALs, batch one patch, measure.

## BEFORE baselines (regime 96ce6370, unpatched)
b6 Steel hammer vs mauler/Veteran, charge, debugFire ON, timeScale 3, n=4
  -> W4/L0/D0, dealt mean 641 (758/411/599/795), taken mean 121, spread 5.68x NOISY
  -> file Assets/Phase1/qa_r3d_before_b6.txt
  -> SATURATION CONFIRMED: every one of the 4 runs opens with the IDENTICAL pair
     MAULER core_0<->chassis_1 "SHEARED at 1400" then core_0<->chassis_2 "at 1050",
     1.0-1.1 s apart (= BREAK_COOLDOWN). 21 of 26 shear lines in the file read
     exactly 1400 or exactly 1050.
  -> player's Steel blade (maxHp 18) HP-DESTROYED in run 2.

RAW IMPULSE TELEMETRY over those 4 matches (CompoundRobot.telemetry*):
  hits >= MIN_IMPULSE(40): 8372   sub-min: 19961   sum 2398456   mean 286.5 N.s
  MAX RAW J = 29251 N.s  (PlayerBuild vs Cube)  = 20.9x STRESS_J_CAP
  -> a naive "raise the cap" is not available; the cap is holding back a
     20x solver spike. Needs a compressor with a hard ceiling, not a bigger cap.

b6 AFK control (throttle 0, steer 0, debugFire OFF), timeScale 4, n=3
  -> W0/L3, player dealt 0.00 in all three. CONVERGED spread 1.00x.
  -> file Assets/Phase1/qa_r3d_before_afk.txt
  -> SPAWN SHED REPRODUCED 3/3: MAULER chassis_1<->spikeZP_6 "SHEARED at 1050
     vs eff 900" then spikeZP_6 STRUCTURAL-SHED at "hp 87/87 (100%)", ~1.2 s
     after the fight starts, with nobody having touched it. Enemy finishes
     10/11 in all three. 1050 = STRESS_J_CAP 1400 * STRESS_ENV_SCALE 0.75,
     i.e. the floor impact arrives ALREADY CLIPPED AT THE CAP.

## PARALLEL WORKSTREAM DETECTED MID-SESSION (must be recorded)
At 02:44:37Z, while I was measuring, a NEW file Assets/Phase1/Scripts/RobotVisuals.cs
(406 lines: TeamMark ground ring + floating diamond, MotionTrail smear, BattleScars)
appeared and BuilderManager.cs was edited at 02:44:59Z to call
RobotVisuals.Install(testRobot,true)/(aiRobot,false). Neither was written by me.
This forced the domain reload that made my BuilderManager palette null at 02:45.
It covers the critic's visual findings (ownership marker, motion, damage feedback).
CONSEQUENCE FOR ATTRIBUTION: my BEFORE sweeps above ran at regime 96ce6370, i.e.
BEFORE RobotVisuals landed. I therefore stay entirely out of Actuator.cs,
BuilderManager.cs, RobotVisuals.cs and MatDB.cs, and I re-baseline before claiming
any delta.

## PATCH (2 files, one reload)
Assets/Phase0/Scripts/CompoundRobot.cs  (.r3.bak kept)
  - STRESS_SOFT_EXP 0.5 / STRESS_J_HARD 4200 / static SeamLoad(J): identity below
    the 1400 knee, sqrt-compressed above, hard-ceilinged at 4200.
  - ENV_ARM_RAMP_S 0.75 + combatArmedAt/EnvArmScale(): ENVIRONMENT seam stress
    ramps 0->1 over 0.75 s after combat arms. Robot-vs-robot untouched.
  - telemetryOverKnee / telemetryMaxSeamLoad instruments.
Assets/Phase1/Scripts/DamageResolver.cs (.r3.bak kept)
  - EDGE_MIN_VOL 0.012 m3 floor on the effective volume of any part with
    edgeHardness > 1.01. Blade x4.0, hook x1.25, spinner x1.03, rest unchanged.

## AFTER: compile + constants live (0 console errors)
SeamLoad(1400)=1400  SeamLoad(2800)=1980  SeamLoad(29251)=4200  (curve exactly as designed)
ENV_ARM_RAMP_S=0.75  EDGE_MIN_VOL=0.012

## MEASURED: edge-part HP, before -> after (DamageResolver.HpOf, live)
  blade      vol 0.0030  ABS 6->25  Alu 11->43  Steel 18->72  Ti 25->101  CF 20->79  W 22->86
  hook       vol 0.0096  ABS 20->25 Alu 35->43  Steel 58->72  Ti 81->101  CF 63->79  W 69->86
  spinnerSaw vol 0.0127  UNCHANGED  ABS 27 Alu 46 Steel 76 Ti 107 CF 84 W 91
  spike      vol 0.0145  UNCHANGED  ABS 30 Alu 52 Steel 87 Ti 122 CF 96 W 105
  wedge      vol 0.0210  UNCHANGED  ABS 44 Alu 76 Steel 126 Ti 176 CF 139 W 151
  chassis    vol 0.0600  UNCHANGED  ABS 126 Alu 216 Steel 360 Ti 504 CF 396 W 432
The blade now sits with the legacy disc class instead of 4x below it, and it is
still the softest weapon on the machine. Material now orders blade durability.

## AFTER AFK n=3 (Assets/Phase1/qa_r3d_after_afk.txt) - regime a8303d5b (23 scripts)
NOTE: regime moved under me (the parallel visual workstream added a 23rd script),
and Time.timeScale was reset to 1 by something outside the harness partway through,
so run 2 hit the harness's 120 s wall-clock cap and reads "None at 36.7s". This
sweep is therefore NOT a clean before/after pair and I do not claim its outcomes.
What it DOES show unambiguously (structural, not statistical):
  BEFORE shear magnitudes, 3 AFK runs: 1050, 1400, 1050, 1050 / 1050, 1400, 1050,
        1050 / 1050, 1400, 1050, 1194, 1400   -> 12 of 13 are EXACTLY 1400 or 1050
  AFTER  shear magnitudes, 3 AFK runs: 2078, 1207, 2234, 2234 / 2093, 2093, 2639,
        2639, 2569, 2569, 1558 / 1687, 1687, 2238, 2293, 2293  -> 0 of 16 at the cap
  and the first shear moved from ~0.4 s after the bell to ~15 s into the match.
  Blade shed at "hp 32/72" - the new 72 hp floor is live in the arena.

## DECISION: attribution by CONSTANT FLIP, not by regime pair
Because another agent is editing the same tree, before/after FILE pairs cannot be
attributed. Both of my physics changes are single constants that can be switched
back to EXACTLY the old behaviour inside one session on one regime:
  STRESS_J_HARD = 1400  =>  SeamLoad(J) = Mathf.Min(J, 1400), i.e. bit-identical
                            to the shipped clamp.
  ENV_ARM_RAMP_S = 0    =>  EnvArmScale() returns 1, i.e. bit-identical.
All acceptance tests below are run as A/B pairs on ONE regime, minutes apart.

## TEST 1 (finding 3, spawn shed) - A/B by constant flip, one regime a8303d5b
Cell: b6 steel, mauler/Veteran, AFK (throttle 0, steer 0), debugFire OFF,
      FightManager.DEFAULT_MATCH_TIME = 5 s so the window is the spawn only,
      timeScale 1, n=6 each arm.
  arm A  ENV_ARM_RAMP_S = 0    (= shipped behaviour exactly)
         MAULER spikeZP_6 STRUCTURAL-SHED at hp 87/87 (100%) in 6 of 6.
         seam chassis_1<->spikeZP_6 sheared at 2237/2077/1925/1924/2075/1924 vs eff 900.
  arm B  ENV_ARM_RAMP_S = 0.75 (my fix)
         MAULER spikeZP_6 STRUCTURAL-SHED at hp 87/87 (100%) in 6 of 6.  NO CHANGE.
         seam sheared at 1923/2076/1924/2076/1924/1924 vs eff 900.
=> MY 0.75 s RAMP DOES NOT FIX IT. Reported as measured, not as intended.
   Two things this test DID establish, which the old saturated clamp hid:
   (a) the spawn impact is a 1.9-2.2 kN.s event on a 900 N.s seam - 2.1-2.5x
       over, not the "1050" the clamp was reporting. It was never marginal.
   (b) runs 2,4,5 of arm B had player taken 0.00 and dealt 0.00 - no robot
       contact of any kind - and still sheared at 1924. So it is not a ram.
   The impact lands ~1.2 s AFTER the bell, i.e. outside a 0.75 s window.
   Next: discriminate "settle too short" from "Bell() zeroes velocity and starts
   a fresh drop" by extending SETTLE_TIME with the ramp OFF.

## FINDING 3 ROOT CAUSE - it is NOT the spawn drop. LIVE PROBE, timeScale 0.15:
  t=822.08  bell at 821.34, elapsed 0.73, enemy v=3.46 m/s, EnvArmScale=0.96, no detach
  t=824.09  elapsed 2.75, detach ledger:
      MAULER|SEAM chassis_1<->spikeZP_6|SHEARED at 1945 vs eff 900|t 823.40
    telemetry at that instant: maxJ = 4806 N.s  [MAULER vs Cube]  maxSeamLoad 2594
    2594 * STRESS_ENV_SCALE 0.75 = 1945.5 -> the ledger's 1945 EXACTLY.
  "Cube" identified by enumerating the scene: the FOUR ARENA WALLS
    (BuilderManager.cs:1503, parent "sandbox", 14.5 x 1.5 x 0.5 at +-7 m,
     BoxCollider, no Rigidbody). They are the only GameObjects named "Cube"
     with a collider.
=> The MAULER drives into the ARENA WALL at ~3.5 m/s about 2.0 s after the bell
   and shears its own spike off, at 100% HP, in essentially every match. It is a
   WALL CRASH, not a spawn drop - which is why neither a longer settle (n=6, 6/6
   still shed) nor a 0.75 s environment ramp (n=6, 6/6 still shed) touched it.
   The critic's finding is real and its stated mechanism is wrong.

## RE-DERIVING STRESS_ENV_SCALE, which the compressor invalidated
Old clamp: EVERY env contact above 1400 raw produced the SAME 1050, so 0.75 was
calibrated against a single saturated value and could not distinguish a 3.5 m/s
bump from a 10.4 m/s crash. Measured raw env impulses now:
  routine 3.5 m/s wall bump   J ~ 4700-6400  -> SeamLoad 2565-2983
  full-speed 10.4 m/s crash   J ~ 14000      -> SeamLoad 4200 (ceiling)
Requirement, taken straight from the round-5 note this constant carries:
  a routine bump must NOT shear a x1 (900 N.s) seam, and a full-speed crash must
  still cost exactly one part.
  scale < 900/2983 = 0.302   and   scale > 900/4200 = 0.214
=> 0.28 sits inside that window with margin at both ends:
     worst routine bump 2983*0.28 = 835 (< 900, safe)
     full-speed crash   4200*0.28 = 1176 (> 900, still costs a part)
     ABS seams (525) still fail on the boards, which the original comment
     explicitly wants ("you can shake a badly-braced build apart on the boards").

## TEST 1 continued - bracketing STRESS_ENV_SCALE (all one regime, AFK, b6, mauler/Vet)
  0.75 (shipped), MATCH_TIME 5, n=6 : MAULER spike SHED at 100% HP  6/6, enemy 10/11 x6
  0.28,           MATCH_TIME 5, n=6 : spike SHED 0/6, enemy 11/11 x6, but the seam
                                      still SHEARED (971-1282) - part left hanging
  0.18,           MATCH_TIME 30, n=4: spike shed 3/4 - BUT at 976/999/1097, which at
                                      0.18 would need SeamLoad 5400-6100 and the
                                      ceiling is 4200 (max env load 756). So those
                                      three are ROBOT contacts, not environment:
                                      the runs that shed are exactly the runs with
                                      player "taken" 41-56 (the mauler ramming the
                                      parked player with its own spike). Run 3,
                                      taken 9, had NO shear at all and finished 11/11.
=> At 0.18-0.20 the ARENA no longer removes a healthy part from a x1 machine.
   What remains is the MAULER shearing its own spike mount by ramming, which is a
   real contact by a real opponent and a different finding.
SHIPPING 0.20: it is the largest round value satisfying the guarantee
   STRESS_J_HARD * scale < 900  (4200 * 0.20 = 840, 93% of the weakest x1 seam),
   so no environment contact of any magnitude can shear a x1 single-socket seam,
   while ABS (525) and any already-degraded seam still fail on the boards.

## TEST 2 (finding 1, seam saturation) - A/B by constant flip, ONE regime, 25 min apart
Cell: b6 steel hammer, mauler/Veteran, charge, debugFire HELD, timeScale 2,
      MATCH_TIME 45 s, n=4, STRESS_ENV_SCALE 0.20 in BOTH arms. Only
      STRESS_J_HARD differs (1400 => SeamLoad is bit-identical to the shipped
      Mathf.Min(J,1400) clamp; 4200 => the compressor).
  arm A  STRESS_J_HARD 1400   Assets/Phase1/qa_r3d_ab_clamp1400.txt
     11 of 13 shear lines read EXACTLY 1400. All four runs open with the
     IDENTICAL pair, in the identical order, 1.0 s apart:
       MAULER core_0<->chassis_1 @1400 then chassis_1<->spikeZP_6 @1400.
     The critic's finding reproduced exactly, inside my own session.
     W4/L0, dealt 583/437/406/563, len 15.4-45.2 s, enemy final 0/6/7/4 of 11,
     worst single-step piece loss player 0 enemy 2.
  arm B  STRESS_J_HARD 4200   Assets/Phase1/qa_r3d_ab_soft4200.txt
     0 of 18 shear lines at any constant. The recurring value is 2884-2885,
     which is not a constant in the code: SeamLoad(J)=2884 => J=5947 N.s, the
     measured opening mutual chassis ram, and BOTH bodies bill the same J in the
     same frame (the ledger shows the player's mirror seam at the same number).
     W4/L0, dealt 525/221/1087/562, len 10.0-45.0 s, enemy final 4/7/0/0 of 11,
     worst single-step piece loss player 0 enemy 5.

WHAT I AM WILLING TO CLAIM: seam load is no longer pinned to a constant, and
seams the clamp could NEVER fail now fail - the player's own core_0<->chassis_1
at eff 1500 and the mauler's battery seam at eff 1440 both shear under the
compressor and neither ever did under the clamp. That is the finding closed.

WHAT I MUST ALSO REPORT, because it is a cost:
 (a) Matches got shorter: mean length 35.2 s -> 22.6 s, and the damage range
     widened (406-583 -> 221-1087). Against finding 4 ("no difficulty curve, the
     strongest build is 14-0") this pushes the wrong way. Player piece count was
     12/12 in all 8 matches in both arms, so machines are not exploding; fights
     are just resolving faster.
 (b) I discovered while doing this that the detach ledger prints worst.peak, the
     seam's RUNNING MAXIMUM stress, not the stress that broke it. Under the old
     clamp every peak saturated at 1400, so "87% of failures occur at exactly
     STRESS_J_CAP" is partly an artifact of the instrument as well as a real
     saturation. Whoever measures next should read the breaking stress, not the peak.
 (c) THE DEEPER FINDING, which is bigger than this fix: the largest impulse in
     this game is not a weapon. A mutual chassis ram measures J ~ 5947 N.s; the
     hardest weapon contact is far below it. So seam load is dominated by driving
     into things, and no amplitude constant can separate "my hammer landed" from
     "we collided". Making load depend on the character of the STRIKING part (an
     edge concentrates, a flat chassis spreads) is the real fix and is a design
     change I could not validate in one round.

## SHIPPED-STATE VERIFICATION (regime 76113c28, 23 scripts, newest 03:18:17Z)
Assets/Phase1/qa_r3d_shipped_b6.txt - b6 steel, mauler/Veteran, charge, fire held,
timeScale 3, n=4, all constants at their shipped values.
  W4/L0/D0, dealt 543/681/930/777 (mean 733, before 641), taken 30/36/81/128
  player pieces 12/12 in ALL FOUR (before: 11/12 in one), worst single-step piece
  loss player 0 enemy 2 (the round-4 guardrail is intact - the 4200 A/B arm's
  enemy 5 did not reproduce), len 19.8-59.1 s.
  PLAYER'S STEEL BLADE SURVIVED ALL FOUR MATCHES. Before the edge floor it was
  HP-DESTROYED at 0/18 in one of four; at 72 hp it took 0 destructions in four.
  The kill mode changed: gyro, battery, engine and both chassis now die
  HP-DESTROYED rather than being sheared off, i.e. damage is deciding matches
  where the saturated seam system used to.
BLAST RADIUS, measured: over those 4 matches, 10969 contacts above MIN_IMPULSE
  and only 118 (1.1%) exceeded the compressor knee. 98.9% of all physics in this
  game is bit-identical to before my change. Max raw J 11118 (PlayerBuild vs an
  arena wall), max compressed seam load 3945 - the 4200 ceiling was approached
  but not reached.

## HONEST LIMIT ON FINDING 1 - I DID NOT FULLY CLOSE IT
The shipped ledger still shows the same four seams failing at 2883-2885 in every
one of the four matches. The number is no longer a code constant (it is
SeamLoad(5947), the measured opening mutual chassis ram) and seams the clamp
could NEVER fail now fail - the player's core_0<->chassis_1 at eff 1500 and the
mauler's battery seam at eff 1440 both shear, and neither ever did at 1400. But
the SEQUENCE is still near-identical run to run, because the dominant physical
event is still the opening ram and not anybody's weapon. Half the finding is
closed (load tracks the hit); half is not (the hit that dominates is a ram).
See TEST 2 note (c) for what I believe the real fix is.

## FINAL EDITOR STATE
mode=Build, playing=True, Time.timeScale=1, Phase0Input.debugFire=False,
detachLogOn=False, telemetryOn=False, 0 RepeatHarness objects,
SETTLE_TIME restored to 1.2, DEFAULT_MATCH_TIME restored to 90.
owen's build reloaded: 16 parts, Validate()=OK.
qa_owen_build_SAFE.txt UNTOUCHED - 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b,
byte-identical to qa_build_prewheelfix.txt, mtime still 2026-07-26 17:59:08Z.
Console: 0 errors, 0 warnings.
Backups kept: CompoundRobot.cs.r3.bak (pre-patch-1), CompoundRobot.cs.r3b.bak
(pre-patch-2), DamageResolver.cs.r3.bak. Nothing deleted.
