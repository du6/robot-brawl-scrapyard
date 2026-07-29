# QA Critic Round 2 - Robot Brawl: Bolt & Blade
Date: 2026-07-26
Mandate: author >=5 genuinely different robots (varying weapon system AND material),
run real matches vs varied opponents/tiers, evaluate visuals via Unity capture,
and re-measure Round 1's claims.

## Log

## A. Six builds authored, all validated in the builder (2026-07-26)
Files: Assets/Phase1/qa_r2_b{1..6}_*.txt . All 6 return bm.Validate()==OK,
4 wheels each, and EVERY actuated build reports blockedBy=null with parts>0
(so no silently-welded limb in this set).

| build | weapon system | materials | parts | mass kg | cost | limb parts | I | tipR | rateMax | E(J) | kJ/swing |
|---|---|---|---|---|---|---|---|---|---|---|---|
| b1_hammer_ti  | pivot overhead hammer (tower+arm+blade), vertical arc | Steel frame / Titanium arm+blade | 13 | 1924.0 | 1924 | 2 | 29.34 | 1.025 | 12.20 | 2181 | 6.23 |
| b2_rotor_cf   | spindle rotor built from beamlong + 2 blades (NOT the spinner part) | Aluminum frame / CarbonFiber rotor | 13 | 705.1 | 889 | 3 | 6.88 | 0.810 | 15.43 | 820 | 2.34 |
| b3_lance_w    | ram spear (ram + beam + spike) | Steel frame / Tungsten spike | 12 | 1994.2 | 2322 | 2 | 328.52 | 0.300 | 3.50 | 2012 | 5.75 |
| b4_flipper_abs| pivot flipper, arm sweeps UPWARD, crossbar + wedge | all-ABS | 13 | 449.7 | 305 | 3 | 53.84 | 1.375 | 9.09 | 2225 | 6.36 |
| b5_discus_al  | CONTROL: no Phase 4 parts. spinnerSaw + ram spike | Aluminum frame / Steel saw+spike | 11 | 706.7 | 663 | - | - | - | - | - | - |
| b6_hammer_w   | b1 geometry, arm re-materialled Tungsten (A/B against b1) | Steel frame / Tungsten arm+blade | 13 | 2500.8 | 5912 | 2 | 125.54 | 1.025 | 12.20 | 9335 | 26.67 |

Immediate read: b1 vs b6 is a clean single-variable material A/B (identical geometry,
only the 3 limb parts change material). Tungsten multiplies limb inertia 4.3x
(29.3 -> 125.5) and stored energy 4.3x (2181 J -> 9335 J) at IDENTICAL rateMax
(12.20) -- because RateCeiling only depends on tipRadius, not on inertia or motor.
Cost 1924 -> 5912 (3.07x). Whole-machine mass 1924 -> 2501 kg.

## C-0. Capture tooling (re-measuring R1's declined item)
- device_stage_files STILL returns HTTP 403 "untrusted_device". Confirmed 2026-07-26.
  PNGs written to Assets/Phase1 CANNOT be pulled into the agent container.
- Unity_Camera_Capture WITHOUT a camera id returns a STALE CACHED FRAME. Three
  consecutive calls, with a SceneView.pivot/rotation/size change and a LoadSnapshot
  between them, returned byte-identical 499033-byte images of an edge-on horizon.
  R1's report of this is CONFIRMED.
- Unity_Camera_Capture WITH cameraInstanceID is unusable: Camera.GetEntityId() now
  returns a 64-bit id (QACam = 568105589205431198, Main Camera = 568105589213655476)
  and the MCP JSON layer rounds it through a double, so the id arrives as
  ...431200 and the tool answers "Failed to render scene preview".
- WORKAROUND THAT WORKS, and the one every image below uses:
  Unity_SceneView_CaptureMultiAngleSceneView renders FRESH. Its FRONT/TOP/RIGHT
  panes auto-frame the visible renderers, so disabling every renderer under
  build_room that is not part of the robot gives a large, legible robot.
  (Its ISO pane does NOT reframe - the robot stays a speck. ISO is unusable.)

## B. Matches. Harness = RepeatHarness (committed code), timeScale 3, 90 s matches.
policy "charge" = scripted driver that chases + can reverse; "afk" = parked, throttle 0.
fire=True means Phase0Input.debugFire held for the whole match (player holds SPACE).
Raw files: Assets/Phase1/qa_r2_sweep_NN.txt

### SWEEP 01  b1_hammer_ti (Titanium arm, 1924 kg) vs mauler/Veteran, charge, fire=ON, n=4
W2/L2/D0  flips=2  len 26.1-90.3s spread=3.46x  [NOISY]  worst single-step piece loss: player 1 ENEMY 5
 run0 Loss 90.3s dealt 295 taken 364 - judges on damage
 run1 Win  90.0s dealt 419 taken 429 - damage inside draw band, decided on STRUCTURE
 run2 Win  26.1s dealt 381 taken  51 - KO, enemy core destroyed
 run3 Loss 75.9s dealt 156 taken 281 - COUNTED OUT, out of power, 300 kJ gone in 66 s

### SWEEP 02  b6_hammer_w (SAME GEOMETRY AS b1, Tungsten arm, 2500.8 kg) vs mauler/Veteran, charge, fire=ON, n=4
W0/L4/D0  flips=0  spread=1.54x  [CONVERGED]  -- i.e. this is NOT noise, it loses every time.
 run0 Loss 55.8s dealt 46 taken 221 - COUNTED OUT out of power (300 kJ in 46 s), flipped 12.0 s
 run1 Loss 42.9s dealt 12 taken  98 - COUNTED OUT out of power (300 kJ in 33 s)
 run2 Loss 44.7s dealt 38 taken 169 - COUNTED OUT out of power (300 kJ in 35 s)
 run3 Loss 36.3s dealt 21 taken 162 - COUNTED OUT, battery torn off, remainder dry after 26 s
*** SINGLE-VARIABLE MATERIAL A/B (only the 3 limb parts change material) ***
 Titanium arm: W2/L2, damage dealt 295/419/381/156 mean 313, 1 of 4 power count-outs
 Tungsten arm: W0/L4, damage dealt  46/ 12/ 38/ 21 mean  29, 4 of 4 power count-outs
The build screen quotes Tungsten as 4.3x the stored energy (26.67 kJ/swing vs 6.23) and
3.07x the cost (5912 vs 1924). It performs at 9% of the damage and 0% of the wins.

### SWEEP 03  b2_rotor_cf (CarbonFiber self-built spindle rotor, 705 kg) vs scout/Veteran, charge, fire=ON, n=4
W2/L2/D0  flips=2  spread=1.14x  [NOISY]  worst single-step piece loss: PLAYER 9  enemy 1
 run0 Loss 78.9s dealt 135 taken 281 - KO, player core destroyed, pieces 0/13 (whole machine came apart)
 run1 Loss 90.3s dealt  91 taken 215 - judges on damage
 run2 Win  90.3s dealt 330 taken 198 - judges on damage
 run3 Win  90.3s dealt 213 taken 197 - inside draw band, decided on structure
NOTE: 9 attached pieces left one body in ONE FixedUpdate. The whole 13-part robot
went to 0 pieces. This is the old "structural teardown" finding and it is not fixed.
No power problems at 705 kg (all four matches ran to time or to a KO).

### SWEEP 04  b3_lance_w (ram spear, Tungsten spike, 1994 kg) vs bulwark/Veteran, charge, fire=ON, n=4
W2/L2/D0  spread=1.42x  [NOISY]
 run0 Loss 58.5s dealt 221 taken 486 - PLAYER counted out, out of power (49 s)
 run1 Win  74.4s dealt 875 taken 352 - ENEMY counted out, out of power (64 s)
 run2 Win  64.5s dealt 341 taken 238 - ENEMY counted out, battery torn off, dry at 55 s
 run3 Loss 52.5s dealt 281 taken 350 - PLAYER counted out, out of power (43 s)
*** 4 of 4 matches decided by a POWER COUNT-OUT. None reached the 90 s judges'
decision and none was a KO. At ~2 tonne vs ~2 tonne this matchup is a battery
race, not a fight. Running total of power count-outs: sweep01 1/4, sweep02 4/4,
sweep03 0/4, sweep04 4/4 => 9 of 16 matches so far (56%). ***

### SWEEP 05  b4_flipper_abs (all-ABS pivot flipper, 450 kg) vs tipper/Veteran, charge, fire=ON, n=4
W0/L4/D0  spread=3.41x
 run0 Loss 84.6s dealt 327 taken 319 - counted out, ALL 4 WHEELS TORN OFF, pieces 2/13, flipped 21.0 s
 run1 Loss 26.4s dealt  99 taken 347 - counted out, BEACHED (0 of 2 wheels on ground), pieces 6/13
 run2 Loss 90.0s dealt 245 taken 310 - judges on damage, pieces 9/13
 run3 Loss 27.9s dealt 152 taken 621 - counted out, ALL 4 WHEELS TORN OFF, pieces 3/13, flipped 10.8 s
NO power problem at all here (450 kg). The all-ABS machine dies STRUCTURALLY:
it retains 2/13, 6/13, 9/13, 3/13 pieces. Damage dealt (mean 206) is respectable.
Combined with R1's finding that its 2125 kg Tungsten-ish flipper died of POWER, the
two ends of the material table fail for two DIFFERENT reasons and neither is the wedge.
Toppling still not observed - needs dedicated instrumentation (below).

### SWEEP 06  b5_discus_al (CONTROL, no Phase 4 parts, 707 kg) vs widowmaker/Veteran, charge, n=4
W1/L3/D0  spread=2.95x
 run0 Win  90.3s dealt 1052 taken 481 - judges on damage
 run1 Loss 90.3s dealt  497 taken 722 - judges on damage, player flipped 17.1 s
 run2 Loss 30.6s dealt  683 taken 819 - KO, player core destroyed, pieces 0/11
 run3 Loss 90.3s dealt  426 taken 758 - judges on damage, player flipped 10.2 s
Zero power count-outs at 707 kg. Damage per match is 3-5x anything the Phase 4
builds managed (mean dealt 665 vs 313 for the Titanium hammer and 206 for the flipper).

### SWEEP 07  b5_discus_al (CONTROL) vs mauler/Veteran, charge, n=4   <-- SAME CELL AS SWEEPS 01 AND 02
W4/L0/D0
 run0 Win 25.5s dealt 707 taken   0 - KO enemy core destroyed
 run1 Win 90.3s dealt 299 taken 230 - judges on damage
 run2 Win 11.7s dealt 632 taken   0 - KO enemy core destroyed
 run3 Win 90.3s dealt 548 taken 159 - judges on damage
*** SAME OPPONENT, SAME TIER, SAME POLICY, THREE PLAYER BUILDS ***
 b5 discus (NO Phase 4 parts, 707 kg, cost 663):  W4/L0  dealt mean 546  taken mean  97
 b1 pivot hammer (Phase 4, 1924 kg, cost 1924):   W2/L2  dealt mean 313  taken mean 281
 b6 pivot hammer (Phase 4, 2501 kg, cost 5912):   W0/L4  dealt mean  29  taken mean 163
The legacy pre-made spinner disc beats the whole Phase 4 base-component kit on the
same fight, for a third of the cost. Mass is a confound - controlled for below (b8).

### SWEEP 08  b5_discus_al vs mauler/Veteran, policy=AFK (PARKED, throttle 0), n=4
W3/L1/D0
 run0 Win 42.6s dealt 719 taken  71 - KO enemy core destroyed  (player never moved)
 run1 Win 90.0s dealt 462 taken 407 - judges on damage
 run2 Loss 90.3s dealt   0 taken 262 - judges on damage (enemy never came into the disc)
 run3 Win 90.3s dealt 499 taken   9 - judges on damage, 98.2% margin
*** PARKED vs DRIVING, identical build/opponent/tier/fire ***
 charge (drive at the enemy): W4/L0  dealt mean 546  taken mean  97
 afk    (do not move at all): W3/L1  dealt mean 420  taken mean 187
Standing still and holding the disc wins 3 of 4 against a Veteran. Driving buys
+1 win in 4 and +30% damage. "Parking beats fighting" is NOT closed by Phase 4:
it is still very close to a free win, and Phase 4 did not change the calculus.

### SWEEP 09  b8_hammer_al (b1 geometry, ALL-ALUMINIUM, 769.9 kg, cost 747) vs mauler/Veteran, charge, fire=ON, n=4
W1/L3/D0  spread=1.00x [CONVERGED]  every match ran the full 90 s, zero power count-outs
 run0 Win  dealt 459 taken  18  pieces 12/13  flipped  8.4 s
 run1 Loss dealt 254 taken 322  pieces  5/13  flipped 15.6 s
 run2 Loss dealt 229 taken 351  pieces  5/13  flipped  5.1 s
 run3 Loss dealt 167 taken 338  pieces  9/13  flipped  1.2 s
*** MASS-CONTROLLED PHASE-4-vs-LEGACY (770 kg hammer vs 707 kg disc, same cell) ***
 b5 discus (spinnerSaw + spike, cost 663): W4/L0  dealt mean 546  taken mean  97
 b8 hammer (pivot+beam+blade, cost 747):   W1/L3  dealt mean 277  taken mean 257
So the Phase 4 kit is not merely heavier - at matched mass and matched cost it deals
51% of the damage and takes 2.6x as much. It also spends 1.2-15.6 s a match on its back
(the tower raises the CoM), which the disc build almost never does.

### SWEEP 10  b8_hammer_al vs mauler/Veteran, charge, fire=OFF (actuator never fired), n=4
W0/L4/D0  dealt 115/198/261/313 mean 222, taken mean 373
*** DOES THE PIVOT WEAPON DO ANYTHING? (identical build/opponent/policy, n=4 each) ***
 fire=OFF: W0/L4  dealt mean 222  taken mean 373
 fire=ON : W1/L3  dealt mean 277  taken mean 257
Firing the hammer for the entire match is worth +25% damage dealt and -31% taken.
The other 80% of the build's output is just ramming. Note the fire=OFF bot still
deals 222 - i.e. driving a 770 kg box into the enemy is 80% as good as the weapon.

## C. VISUAL EVALUATION (Unity multi-angle scene capture, room renderers hidden)
Build-screen gallery, one capture per build (b1,b6,b2,b3,b4,b5) plus live-fight
captures of b8 mid-swing and b8 facing bulwark at 1.7 m.

1. MATERIAL IS LEGIBLE ON WEAPON PARTS - R1's fix VERIFIED BY EYE. b1 and b6 are the
   same geometry; b1's Titanium arm/blade render light warm grey, b6's Tungsten arm
   renders near-black purple-brown. The difference is obvious at a glance in FRONT
   and RIGHT. ABS (b4) renders matte cream and reads unmistakably as plastic.
2. THE BLADE DOES NOT READ AS A BLADE. On every hammer the blade bar at the arm tip
   is a flat featureless slab that reads as a shelf or a spoiler. Compare b5's
   spinnerSaw, which has visible rim teeth and an unmistakable disc silhouette.
   The part the player pays the most for is the least legible thing on the machine.
3. THE SELF-BUILT SPINDLE ROTOR (b2) IS ONE UNDIFFERENTIATED DARK BAR. The CarbonFiber
   beamlong and the two CarbonFiber blades at its tips are the same colour and nearly
   the same thickness, so the assembly reads as a radio antenna, not a weapon. You
   cannot see where its cutting edges are.
4. NO MOTION CUE ON A SWINGING LIMB - still true, R1 declined this and it is the most
   visible gap. Captured b8 mid-swing at travel=1.152 rad: the arm is unambiguously
   rotated, but there is no blur, trail, arc ghost or spin-up tell anywhere. A still
   frame cannot distinguish "swinging at 12 rad/s" from "parked at 66 degrees".
5. NO OWNERSHIP CUE. Capture of b8 (player) 1.7 m from bulwark (enemy): both machines
   are grey boxes with the SAME yellow accent bars and the SAME green engine face.
   In the TOP and ISO panes I could not tell which robot was mine. There is no team
   colour, outline, or marker of any kind.
6. NOTHING LOOKED BROKEN in the geometry sense: no floating parts, no interpenetration,
   no parts left behind by the swing. Joint bosses (orange/yellow on pivot, cyan on
   spindle) are bright and do read as "this rotates".
7. The multi-angle ISO pane never reframes and is useless; FRONT/TOP/RIGHT are fine.

### SWEEP 11  b10_hammer_front (SAME kit as b8 but the pivot moved to the NOSE so the
blade reaches 0.775 m past the front bumper; 777.7 kg, cost 752) vs mauler/Veteran, charge, fire=ON, n=4
W2/L2/D0  dealt 210/289/424/385 mean 327, taken mean 208
 run2 was a KO against the player with pieces 0/13 and 8 pieces lost in ONE step.
*** WHY b10 EXISTS: b8's pivot sits at the REAR (build z=-0.40) with tipRadius 1.025,
so at the top of its arc the blade is at z=+0.625 and the machine's own nose is at
z=+0.65 - b8's hammer CANNOT REACH PAST ITS OWN BUMPER. That is a build error of mine
and it was confounding the Phase-4-vs-disc comparison, so I rebuilt it correctly. ***
THREE-WAY, all ~700-780 kg, all cost 663-752, all vs mauler/Veteran, charge, n=4 each:
 b5  disc  (legacy parts)         W4/L0  dealt 546  taken  97
 b10 hammer (Phase 4, good reach) W2/L2  dealt 327  taken 208
 b8  hammer (Phase 4, bad reach)  W1/L3  dealt 277  taken 257
Correcting the reach is worth +18% damage and +1 win, and the legacy disc still wins
twice as often and deals 67% more damage for less money.

### SWEEP 12  b9_flipper_al (Aluminium frame, Titanium crossbar+wedge, 928.8 kg, cost 1361)
vs tipper/Veteran, charge, fire=ON, n=4
W0/L4/D0
 run0 Loss 12.6s dealt   1 taken 446 - KO player core destroyed, pieces 0/13
 run1 Loss 90.0s dealt 201 taken 480 - judges on damage
 run2 Loss 70.2s dealt  77 taken 183 - counted out, out of power (60 s), flipped 11.4 s
 run3 Loss 90.3s dealt 266 taken 417 - judges on damage
*** THE FLIPPER IS 0W/12L THIS ROUND ACROSS THREE MASS/MATERIAL REGIMES ***
 450 kg all-ABS      : 0W/4L, dies structurally (wheels torn off, 2-9 of 13 pieces left)
 929 kg Al+Ti        : 0W/4L, dies to damage and once to power
 2125 kg (R1's)      : 0W/8L in round 1, died to power
Plus round 1's 0W/8L. Sixteen consecutive losses for the wedge across two rounds.
MEASURED REASON THIS ROUND, and it is geometric, not a balance number:
 - at spawn the b9 wedge's centre sits at world y = 0.600 m, so its ramp underside is
   at y = 0.54 m. The opponent's CORE centre is at y = 0.200 m and a chassis top is
   about y = 0.50 m. THE RAMP IS ABOVE THE THING IT IS SUPPOSED TO GET UNDER.
 - this is forced by the kit: a pivot's axis doubles as its MOUNT NORMAL, so a vertical
   arc needs the pivot bolted on a +-X face, and the 0.30 housing plus "the limb must
   not touch the frame anywhere else" pushes the whole arm up onto the roof line.
   There is no legal way I could find to put a pivoted wedge at floor level.
 - enemy upDot stayed 0.994-0.999 and enemy flippedTime 0.00 s in every live poll.
TOPPLING AS A WIN PATH IS STILL NOT DEMONSTRATED. Second round running.

## ROUND 2 CONTINUATION (second critic session, 2026-07-26)
Prior session's notes read end to end. Editor state on entry VERIFIED, not assumed:
isPlaying=True, timeScale=1, BuilderManager present with 16 parts (owen's build),
Phase0Input.debugFire=False. qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b
(790 bytes) == qa_build_prewheelfix.txt. Untouched.
Mission this session: resolve the parked-vs-charging contradiction, spot-check
artefact-prone claims, fill genuine gaps, save named PNGs.

### RE-MEASURE 1 (this session, personally run): PARKED vs CHARGING, n=6+6
Same build (qa_r2_b5_discus_al, 707 kg, no Phase 4 parts), same opponent
(mauler/Veteran), same fire state (no actuator on this build, so fire is not a
confound), same harness, timeScale 3. This is the pair the brief asked me to resolve.

SWEEP A  b5_discus_al vs mauler/Veteran, policy=CHARGE, n=6
W6/L0/D0  flips=0  len 77.4-90.3s  spread=1.17x  [CONVERGED]
 dealt 708/649/632/833/522/374  mean 620
 taken  50/ 27/156/203/ 49/228  mean 119
 endings: 5 judges-on-damage, 1 enemy counted out (beached)
 raw: Assets/Phase1/qa_r2c_A_disc_charge.txt
This REPRODUCES the prior session's sweep 07 (W4/L0, dealt 546, taken 97) at n=6
and it CONVERGES by the harness's own criterion. Charging is not noise.

SWEEP B  b5_discus_al vs mauler/Veteran, policy=AFK (parked, throttle 0), n=6
W2/L4/D0  flips=2  len 90.0-90.3s  spread=1.00x  [NOISY by flips, but see below]
 dealt 0/424/0/385/0/0   mean 135
 taken 76/ 58/124/248/165/209  mean 147
 endings: 6 of 6 judges-on-damage. ZERO KOs, zero count-outs.
 raw: Assets/Phase1/qa_r2c_B_disc_afk.txt

*** CONTRADICTION RESOLVED. PARKING LOSES. ***
 CHARGE n=6: W6/L0  dealt mean 620  taken mean 119
 AFK    n=6: W2/L4  dealt mean 135  taken mean 147
Pooling my n=6 with the prior session's n=4 in the identical cell:
 charge n=10: W10/L0   afk n=10: W5/L5
The prior session's sweep 08 (afk W3/L1) was a small-n fluke. Its prose conclusion
"standing still and holding the disc wins 3 of 4 ... still very close to a free win"
DOES NOT HOLD and was not supported by its own paired numbers even at n=4
(charge W4/L0 dealt 546 vs afk W3/L1 dealt 420). I am marking the old
"parking beats fighting" finding CLOSED as of this build. Driving is strictly
better on wins (10/10 vs 5/10), on damage dealt (4.6x) and on damage taken.

*** BUT THE PARKED DATA EXPOSES A WORSE BUG THAN THE ONE IT CLOSES ***
In 4 of the 6 parked matches the player dealt EXACTLY 0.0 damage over a full 90 s
and lost only 76-209. Against a stationary target the Veteran MAULER never
committed to contact for 90 straight seconds, four times out of six. That is not
a balance number, it is an AI engagement failure, and it is what actually
produced the prior session's "afk looks fine" reading: the parked bot is not
winning, it is being IGNORED.

### RE-MEASURE 2 (this session): THE FLIPPER RAMP HEIGHT. CONFIRMED EXACTLY.
Loaded qa_r2_b9_flipper_al, StartFight vs tipper/Rookie, read collider bounds on
the SPAWN FRAME (t=0.00) so nothing is contaminated by tilt:
  PLAYER wedgeZP_8   minY=0.541  maxY=0.661   <-- the ramp's underside
  PLAYER pivotXN_5   minY=0.451  maxY=0.751
  PLAYER beam_6      minY=0.501  maxY=0.701
  PLAYER beamlong_7  minY=0.501  maxY=0.701
  ENEMY  core_0      minY=0.050  maxY=0.350
  ENEMY  chassis_1/2 minY=0.050  maxY=0.350
  ENEMY  spinnerZP_7 minY=0.030  maxY=0.370
The prior session's 0.54 m figure is reproduced to the millimetre (0.541).
STRENGTHENING IT: the ramp underside is not merely above the enemy's core centre,
it is 0.19 m above the TOP of the enemy's entire hull (0.541 vs 0.350). The whole
limb -- pivot, both arm segments and the wedge -- lives in the 0.45-0.75 m band,
i.e. on the roof line. A ramp cannot get under a hull it is standing over.
Also confirmed the limb itself is fine: LimbReport parts=3 blockedBy=null
I=221.18 tipR=1.375 E=5484 J. The mechanism works; it is aimed at the sky.

### RE-MEASURE 3 (this session): "THERE IS NO LEGAL WAY TO PUT A PIVOTED WEDGE AT
### FLOOR LEVEL" -- THIS CLAIM IS FALSE. I built one in one attempt.
New build qa_r2c_c1_lowflip_ti.txt (13 parts, Aluminium frame, Titanium arm+wedge):
  core/2 chassis/4 wheels/battery/engine  (standard skeleton)
  pivot   0.300,0.700,0.000  axis +X   <-- mounted on the CORE's +X face, at hull
                                           height, BETWEEN the two wheel stations
  bracket 0.550,0.700,0.000            <-- jogs the arm outboard of the wheels
  beam    0.550,0.700,0.400            <-- runs forward at x 0.45-0.65, clear of
                                           the wheels (wheel xMax 0.34)
  wedge   0.550,0.640,0.875  axis +Z
bm.Validate() == OK. LimbReport: limbs=1 parts=3 blockedBy=null I=58.06 tipR=1.127
E=4999 J kJ/swing=14.28. So the limb is correctly classified as a driven limb.
SPAWN-FRAME COLLIDER BOUNDS:
  wedgeZP_8 minY=0.080 maxY=0.200   (b9's was 0.541-0.661)
  and its leading edge is zMax -2.950 against the machine's own bumper at -3.350,
  i.e. it protrudes 0.40 m past the nose.
The ramp now sits INSIDE the enemy hull band (enemy hull 0.050-0.350) instead of
0.19 m above it. The kit is not the blocker; the previous build was.
THE REAL CONSTRAINT, stated precisely: a pivot's axis is its mount-face normal, so
a vertical arc forces a +-X mount; the +-X faces at wheel z-stations are occupied
by the wheels; therefore the ONLY low mounting point is the core's side face at
z=0, and the arm must then be jogged outboard of the wheel envelope before it can
run forward. Nothing in the builder tells the player that. That is a discoverability
problem, not a geometric impossibility.

### RE-MEASURE 4 (this session): TOPPLING AS A PLAYER WIN PATH -- NOW DEMONSTRATED
Live poll during sweep C run 0 (c1_lowflip_ti vs tipper/Rookie, charge, fire ON):
  t=0.0   enemyFlipped=0.00s  upDot=1.000  dist=7.87
  t=18.9  enemyFlipped=1.80s  playerFlipped=11.10s  upDot=0.513  dealt=32  dist=8.45
The ENEMY spent 1.80 s on its back inside the first 19 s. Three prior critic rounds
recorded ZERO player-caused topples in ~150 matches with the wedge mounted high.
The topple physics that R1's dev added WORKS; what was missing was a build that
could put the ramp under the target. Note the player also spent 11.10 s flipped,
which is its own problem (below).

### SWEEP C  c1_lowflip_ti (floor-level Titanium wedge, 13 parts) vs tipper/Rookie,
### charge, fire=ON, n=4     raw: Assets/Phase1/qa_r2c_C_lowflip.txt
W1/L3/D0  flips=1  len 90.0-90.3s  spread=1.00x  [CONVERGED]
 dealt 148/263/227/15   taken 230/316/210/146
 pieces 13/13 in ALL FOUR (the player never lost a single part)
 4 of 4 ended on the judges' card.
*** THE HEADLINE NUMBER IS PLAYER flippedTime: 54.6 / 46.5 / 41.1 / 42.6 SECONDS
*** OF A 90 SECOND MATCH. The player spends 46-61% of every match on its back. ***
So putting the ramp where it can work costs the machine its own stability. Whether
that is the actuator's reaction torque or the asymmetric side-mounted arm is the
next question -- fire=OFF control below.
Also: the enemy was toppled but it never mattered. Enemy righted itself every time
(gyro), INCAP_GRACE is 2 s, and no match was decided by a topple. Toppling is now
POSSIBLE and still not a WIN PATH.

## VISUAL EVALUATION (this session, my own captures)
PNG saved for a human: Assets/Phase1/qa_r2_shot2_lowflip_build.png
Method: multi-angle scene capture with all 272 non-robot renderers disabled
(the prior session's method; Unity_Camera_Capture without an id still returns a
stale cached frame, CONFIRMED again).
V1. MATERIAL LEGIBILITY IS ONLY HALF-FIXED, and the prior session over-credited it.
    Its check was Titanium (light) vs Tungsten (near-black) -- the easiest pair in
    the table. On c1_lowflip the frame is ALUMINIUM and the entire limb (bracket,
    beam, wedge) is TITANIUM, and in FRONT, TOP and RIGHT they are the same pale
    grey-cream. I could not tell the Titanium arm from the Aluminium chassis by eye.
    Adjacent entries in the material table are not distinguishable; only the
    extremes are.
V2. THE WEDGE RENDERS AS A STEPPED ZIGGURAT, not a ramp. In TOP the wedge reads as
    four stacked slabs of increasing width with hard edges between them. A wedge is
    the one part whose whole function is a continuous slope, and its silhouette
    does not communicate one.
V3. A FLOOR-LEVEL WEAPON IS INVISIBLE FROM THE FRONT. In FRONT the entire flipper
    is a single small grey cube (the pivot housing) peeking past the right wheel
    plus a cream sliver. In RIGHT the arm and wedge are a nub at ground level,
    almost entirely occluded by the near wheel. A player looking at this machine
    head-on cannot tell it has a weapon.
V4. ISO pane is still a useless speck (prior session's item 7 CONFIRMED).

### SWEEP D  c1_lowflip_ti, IDENTICAL build/opponent/tier/policy, fire=OFF, n=4
### raw: Assets/Phase1/qa_r2c_D_lowflip_fireoff.txt
W0/L4/D0  dealt 69/185/83/254  taken 220/371/1123/690
 player flippedTime 0.0 / 0.0 / 0.0 / 6.6 s
 pieces 12/13, 13/13, 5/13, 9/13. run2 was counted out with ALL FOUR WHEELS TORN OFF.

*** FIRING YOUR OWN FLIPPER IS WHAT PUTS YOU ON YOUR BACK. n=4 vs n=4, single variable. ***
 fire=ON : player flipped 54.6/46.5/41.1/42.6  mean 46.2 s of a 90 s match
 fire=OFF: player flipped  0.0/ 0.0/ 0.0/ 6.6  mean  1.7 s
 That is a 28x difference and the ONLY thing that changed is Phase0Input.debugFire.
 Mechanism candidate (not yet isolated): the wedge's underside is at y=0.080, so the
 swinging edge contacts the ARENA FLOOR, and the lift/topple term levers the player's
 own chassis over. The actuator's reaction torque on the parent body is the other
 candidate. Either way the player's own weapon is its primary source of being flipped.

*** AND AN INCENTIVE INVERSION FALLS OUT OF THE SAME PAIR ***
 fire=ON  (46 s on its back): taken mean 226, kept 13/13 pieces in all 4 matches
 fire=OFF (1.7 s on its back): taken mean 601, lost down to 5/13 and 9/13, one
   count-out with all four wheels gone
Lying on its back cut damage taken by 62% and stopped structural loss dead. Being
toppled is currently a DEFENSIVE state. INCAP_GRACE is 2 s and gyros right you, so
there is no penalty that outweighs the protection.

### THE SELF-FLIP, CAUGHT IN ISOLATION AND ON CAMERA
Single manual match, c1_lowflip_ti vs tipper/Rookie, fire held ON, Time.timeScale 0.25:
  t=1.20 s  upDot(player) = -0.055  flippedTime 0.23 s  angVel 1.20 rad/s
  t=7.49 s  upDot(player) = -0.845  flippedTime 3.89 s  dealt 61
The player is on its side 1.2 SECONDS INTO THE MATCH, before it has closed with the
enemy at all. There is no opponent contact in that window. The robot flips ITSELF
purely by firing its own actuator with a floor-level edge.
PNG: Assets/Phase1/qa_r2_shot4_selfflip.png (also Assets/Phase1/qa_r2_shot3_live_fight.png)
V5. THE SELF-FLIP IS PERFECTLY LEGIBLE ON SCREEN, which is the one good thing about
    it: in FRONT and RIGHT the machine is unmistakably on its flank with its wheels
    in the air and the wedge arm pointing at the sky. Nothing was interpenetrating,
    nothing floated, no part was left behind by the swing. Geometry is clean.
V6. NO MOTION CUE ON THE SWINGING LIMB -- CONFIRMED AGAIN, MY OWN EYE. At timeScale
    0.25 with the actuator actively firing, the arm has no blur, no trail, no arc
    ghost, no wind-up tell. A still frame cannot distinguish a firing actuator from
    a parked one; the only reason I know it fired is the telemetry.
V7. NO OWNERSHIP CUE -- CONFIRMED, MY OWN EYE. In the TOP pane of the live capture
    both machines are the same pale grey boxes with the same yellow accent bars and
    the same dark-green engine face. I could only tell them apart by remembering
    which one had a tower.
V8. THE WEDGE READS AS A RIBBED GRILLE. Seen clearly in the FRONT pane with the arm
    in the air, the wedge is four parallel raised ribs on a slab. It looks like a
    heatsink or a radiator, not a ramp.
V9. NO DAMAGE FEEDBACK ON THE MACHINES. By t=7.5 s 61 damage had been dealt and the
    enemy showed no decal, no dent, no scorch, no debris. Damage exists only in the
    HUD number.

### RE-MEASURE 5, AND IT IS THE BIGGEST ONE: THE PRIOR SESSION'S ENTIRE BUILD TABLE
### WAS MEASURED AGAINST A STALE ACTUATOR. Its Phase-4 numbers do not describe this build.
I reloaded the prior session's own snapshot FILES, unmodified, and re-read LimbReport.
No .cs was edited this round. Deterministic: 3 consecutive reloads of b1 and b6 gave
byte-identical quotes, and a StartFight/BackToBuild round trip did not perturb them.

TODAY (rateMax * tipRadius == the material tip-speed cap, as R1's dev specified):
  b1_hammer_ti   tipR 1.025  rateMax 14.00  cap 14.79 (Titanium)   E  2875 J
  b2_rotor_cf    tipR 0.810  rateMax 16.19  cap 13.11 (Carbon)     E   902 J
  b4_flipper_abs tipR 1.375  rateMax  6.55  cap  9.00 (ABS)        E  1153 J
  b6_hammer_w    tipR 1.025  rateMax 13.36  cap 13.69 (Tungsten)   E 11202 J
  b8_hammer_al   tipR 1.025  rateMax  9.45  cap  9.68 (Aluminium)  E   784 J
PRIOR SESSION recorded, for THE SAME FILES:
  b1 rateMax 12.20 x 1.025 = 12.50
  b2 rateMax 15.43 x 0.810 = 12.50
  b4 rateMax  9.09 x 1.375 = 12.50   <-- an ALL-ABS build, which must be 9.00
  b6 rateMax 12.20 x 1.025 = 12.50
FOUR OF FOUR ROTARY LIMBS AT EXACTLY 12.50 m/s REGARDLESS OF MATERIAL. 12.50 is the
legacy universal clamp R1's dev removed. The energies line up on rate^2 exactly:
  b1 2181 -> 2875 = (14.00/12.20)^2 = 1.318 (measured 1.318)
  b2  820 ->  902 = (16.19/15.43)^2 = 1.101 (measured 1.100)
  b4 2225 -> 1153 = ( 6.55/ 9.09)^2 = 0.519 (measured 0.518)
  b6 9335 -> 11202 = (13.36/12.20)^2 = 1.199 (measured 1.200)
CONCLUSION: at the time the prior session ran, R1's material tip-speed cap was NOT
live in the editor. Its whole build table, and therefore its ~48 matches of Phase-4
sweeps, were produced against the pre-patch actuator. In particular:
 - the Titanium-vs-Tungsten A/B is framed on "IDENTICAL rateMax 12.20" -- that
   identity was the artefact, not a finding. Under the live code the two differ
   (14.00 vs 13.36) and the energy gap is 3.9x, not 4.3x.
 - the all-ABS flipper was quoted 93% MORE energy than it actually has (2225 vs 1153).
 - the CF rotor was under-quoted by 10%.
I could not reproduce the stale reading on demand, so I cannot name the trigger. The
process finding stands on its own and is the actionable one: NOTHING IN THE GAME OR
THE HARNESS TELLS YOU WHICH BUILD OF THE CODE IS LIVE, and a full QA round was spent
measuring the wrong one. LimbReport should print a code-version/constants stamp
(e.g. TipSpeedCap(Steel) and MotorKW(1)) into every sweep file it writes.
WHAT SURVIVES: my sweeps A and B (the disc control) have no actuator and are
unaffected. Sweeps C and D (the low flipper) were run today under the live code.

## ROUND 2 CONTINUATION - CLOSE-OUT
Editor state VERIFIED, not assumed: play mode cycled (stop/start) to undo the ~400
renderer disables I made for capture -- disabledRenderers now 0. ModeSelect destroyed,
BuilderManager present. owen's build reloaded from qa_owen_build_SAFE.txt: 790 bytes,
16 parts, 4 wheels, Validate()==OK. mode=Build, Time.timeScale=1,
Phase0Input.debugFire=False. RepeatHarness destroyed. Console: 0 errors, 0 warnings.
qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, unchanged, never written.
NEW FILES THIS SESSION (all new names, nothing overwritten):
  Assets/Phase1/qa_r2c_A_disc_charge.txt      (n=6 charge)
  Assets/Phase1/qa_r2c_B_disc_afk.txt         (n=6 parked)
  Assets/Phase1/qa_r2c_C_lowflip.txt          (n=4 fire ON)
  Assets/Phase1/qa_r2c_D_lowflip_fireoff.txt  (n=4 fire OFF)
  Assets/Phase1/qa_r2c_c1_lowflip_ti.txt      (new build: floor-level flipper)
  Assets/Phase1/qa_r2_shot2_lowflip_build.png
  Assets/Phase1/qa_r2_shot3_live_fight.png
  Assets/Phase1/qa_r2_shot4_selfflip.png
Matches run this session: 20 (6+6+4+4). Prior session: ~48. Round-2 total ~68.

## ROUND 2 CONTINUATION - SESSION 3 (third critic session, 2026-07-27)
Read the whole file end to end before touching Unity. Sessions 1 and 2 between them
authored 8 builds, ran ~68 matches, and session 2 already RESOLVED the
parked-vs-charging contradiction (charge W10/L0 pooled vs afk W5/L5) and found that
session 1's ENTIRE Phase-4 build table was measured against a STALE ACTUATOR
(universal 12.50 m/s tip clamp, i.e. pre-R1-patch code).
=> Session 1's ~48 Phase-4 matches are therefore of UNKNOWN validity. That is the
biggest open hole in round 2 and it is what I am spending this session on:
re-running the load-bearing Phase-4 sweeps under CONFIRMED-LIVE code, plus my own
camera pass. Everything below is personally run by me unless it says otherwise.

### S3-0. WHICH CODE IS LIVE, AND A REPEAT OF THE EXACT TRAP SESSION 2 FOUND
Actuator.ConstantsStamp() read live by me, 2026-07-27:
  tipSpeedCap m/s: ABS=9.00 Aluminum=9.68 Steel=12.50 Titanium=14.79
                   CarbonFiber=13.11 Tungsten=13.69
  MotorKW 0/1/2/3 eng 1.0/2.5/4.0/5.5 | DRIVE_KW_PER_TONNE 1.25 | LIFT_EFFICIENCY 0.85
  REACTION_FRAC 0.60 | GROUND_CLEAR 0.020 | BITE_RATE_FRAC 0.45 | EXPOSED_MULT 1.75
LimbReport re-read of the SAME snapshot files, live now (matches session 2 exactly,
so the actuator energy regime has not moved since session 2's re-measure):
  b1_hammer_ti  I=29.34 tipR=1.025 E=2875 J   (session 1 quoted 2181 -> stale)
  b6_hammer_w   I=125.54 tipR=1.025 E=11202 J (session 1 quoted 9335 -> stale)
  b2_rotor_cf   I=6.88  tipR=0.810 E=902 J
  b3_lance_w    I=328.52 tipR=0.300 E=2012 J
  b4_flipper_abs I=53.84 tipR=1.375 E=1153 J  (session 1 quoted 2225 -> stale)
  b8_hammer_al  I=17.56 E=784 J | b10_hammer_front I=22.36 E=998 J
  c1_lowflip_ti I=58.06 tipR=1.127 E=4999 J   all blockedBy=null, parts>0
BUT THE SAME TRAP HAS RECURRED ONE LEVEL UP, and I am reporting it as the headline
process finding. Source mtimes on the user's disk:
  session 2's sweeps A-D ran 21:42-21:58 on 2026-07-26
  Actuator/FightManager/AIController/DamageResolver/RepeatHarness were then PATCHED
  at 22:19-22:21 (Actuator 230 changed lines vs Actuator.cs.r2.bak, FightManager 121,
  AIController 57, DamageResolver 51) and AGAIN at 00:07-00:09 on 2026-07-27.
  The live AIController contains a fix explicitly labelled
  "ROUND-2-CRITIC FIX (MAJOR 5: the enemy AI does not engage a stationary target)".
=> EVERY match number in this file above this line was produced by code that is no
longer running. Sessions 1 and 2's ~68 matches are of unknown validity against the
build a player would run today. Session 2 asked for a constants stamp; one now exists
(RepeatHarness writes Actuator.ConstantsStamp() into every sweep header) but it stamps
only the ACTUATOR. AI, judging and damage all changed under it and are NOT stamped.

### S3-1. b6_hammer_w, THE HEADLINE MATERIAL A/B OF ROUND 2, IS NOW AN ILLEGAL BUILD
bm.Validate() on session 1's own file, live:
  qa_r2_b6_hammer_w -> "Over budget: 5912 cr of 4000. Cheaper materials, or fewer parts."
So round 2's flagship Titanium-vs-Tungsten comparison is between a legal build and one
the builder now refuses. It cannot be reproduced and no player can make it. Replacement
legal single-variable A/B authored this session (below): qa_r2s3_b11_hammer_front_ti.

### S3-2. THE AI ENGAGEMENT FIX LANDED, AND IT REOPENS "PARKING BEATS FIGHTING"
SWEEP S3-A  b5_discus_al (707 kg, no Phase 4 parts) vs mauler/Veteran, policy=AFK
(parked, throttle 0), n=6, live code.  raw: Assets/Phase1/qa_r2s3_A_disc_afk.txt
W5/L1/D0  [CONVERGED, spread 1.05x]  dealt 894/481/1051/0/537/804 mean 628
                                     taken  98/235/ 255/193/191/ 84 mean 176
Compare session 2's SAME CELL under the pre-fix AI: W2/L4, dealt mean 135.
- The dev's CIRCLE_MAX_S/COMMIT_S fix is REAL: matches where the parked player dealt
  EXACTLY 0.0 went from 4 of 6 to 1 of 6. The AI now commits. VERIFIED.
- But the consequence is that a PARKED robot now wins 5 of 6 against a Veteran and
  deals 628 damage a match while taking 176. Session 2 closed "parking beats fighting"
  on its parked numbers; those numbers were an artefact of the AI not turning up.
  Under the live AI the finding is REOPENED and is worse than it ever was.

### S3-3. THE DAMAGE CARD'S "disc" COLUMN IS DEAD CODE - SPINNER DAMAGE IS BOOKED AS RAM
Read from source, then confirmed in every run of my sweep S3-A.
  DamageResolver.cs:104  public const int SRC_RAM = 0, SRC_LIMB = 1, SRC_DISC = 2;
  grep of the whole Phase1/Scripts tree for SRC_DISC / SRC_RAM: the ONLY caller that
  passes a src tag anywhere in the game is Actuator.cs:1084 (SRC_LIMB).
  SpinnerWeapon.cs:212 calls ApplyHit WITHOUT a tag, so it defaults to src=0=SRC_RAM.
Consequence measured: in all 6 runs of S3-A the card reads "disc 0/0" while the
spinnerSaw build deals 481-1051 damage, all of it booked under "ram". A parked robot
that never moves cannot ram anything; the 894 damage in run 0 is disc damage wearing
the ram label. Any conclusion drawn from this split - including round 1's "the card is
~2/3 chassis ram" - is measuring the wrong thing for every disc build in the project.
FIX DIRECTION: pass DamageResolver.SRC_DISC at SpinnerWeapon.cs:212.

### S3-4. SESSION 2'S SWEEP FILES CARRY NO CONSTANTS STAMP - CONFIRMED FROM CONTENT
head -3 of Assets/Phase1/qa_r2c_A_disc_charge.txt goes straight from the build line to
"run 0", i.e. it was written by the pre-patch harness. My files carry the stamp. So the
"sessions 1 and 2 measured code that is no longer live" claim rests on file CONTENT,
not only on mtimes.

### S3-5. PARKED vs CHARGING, RE-RUN UNDER LIVE CODE, n=6 + n=6, SAME CELL
b5_discus_al (707 kg, no Phase 4 parts) vs mauler/Veteran. Only bm-policy changes.
raw: qa_r2s3_A_disc_afk.txt / qa_r2s3_B_disc_charge.txt (both carry the constants stamp)
 CHARGE n=6: W5/L0/D1  dealt 550/234/536/630/830/755 mean 589  taken mean  99
             lengths 9.9-90.3 s, two KOs at 9.9 s and 14.4 s, two enemy count-outs
 AFK    n=6: W5/L1/D0  dealt 894/481/1051/0/537/804 mean 628  taken mean 176
             lengths 85.8-90.3 s, one KO at 85.8 s, no count-outs
*** THE ANSWER CHANGES AGAIN, AND THIS IS THE THIRD DIFFERENT ANSWER IN THREE
*** MEASUREMENTS: under the code that is live today, PARKING IS AT PARITY.
 round 1     : parking beats fighting
 round 2 s2  : charging strictly better (charge W10/L0 pooled vs afk W5/L5)
 round 2 s3  : charge 5W-0L-1D vs afk 5W-1L; parked deals MORE damage (628 vs 589)
The only thing driving is now worth is SPEED of the win (mean length 58.7 s charging
vs 89.5 s parked) and damage taken (99 vs 176). Neither shows up on the scoreboard.
A player who does nothing at all beats a Veteran five times in six. n=6 per cell,
same build, same opponent, same tier, same harness, live code.

### S3-6. SWEEP C: b10_hammer_front (Aluminium limb, 777.7 kg, 752 cr, limb E=998 J)
vs mauler/Veteran, charge, fire ON, n=4, live code. raw qa_r2s3_C_hammer_al.txt
W2/L1/D1  dealt 522/304/120/199 mean 286  taken mean 191
 limb column: 276/0/73/110 mean 115 (40% of output), limb hits 14/0/3/4, max hit 43.0
 RUN 1 LANDED ZERO LIMB HITS in a full 90 s match with the actuator held down the
 whole time, and still scored 304 damage by ramming. The weapon is optional.

### S3-7. LEGAL SINGLE-VARIABLE MATERIAL A/B UNDER LIVE CODE (replaces the illegal b6)
New build qa_r2s3_b11_hammer_front_ti.txt = qa_r2_b10_hammer_front.txt with EXACTLY the
two limb parts (arm beam, blade) re-materialled Aluminium/Steel -> Titanium. Nothing
else differs. Both Validate()==OK and both inside the 4000 cr budget.
  b10 AL : 777.7 kg  752 cr  I=22.36  tipR=1.025  E=  998 J
  b11 TI : 816.1 kg 1023 cr  I=29.34  tipR=1.025  E= 2875 J   (2.88x energy, +36% cost)
SWEEP D raw qa_r2s3_D_hammer_ti.txt, same cell as sweep C (mauler/Veteran, charge, fire ON, n=4)
  b10 AL: W2/L1/D1  dealt mean 286  taken 191  limb 459 over 21 hits = 21.9/hit  max hit 43.0
  b11 TI: W2/L1/D1  dealt mean 348  taken 262  limb 555 over 19 hits = 29.2/hit  max hit 55.5
*** 2.88x STORED LIMB ENERGY BUYS 1.21x DAMAGE AND ZERO WINS (n=4 vs n=4). ***
Per-hit it buys 1.33x, i.e. damage scales as roughly E^0.30. Round 1's CRITICAL 2
("stored limb energy does not convert into damage") was measured on the stale build and
is REPRODUCED HERE ON THE LIVE ONE with a budget-legal pair. Round 1 declined to fix it.
COST OF THE UPGRADE, which nothing in the game tells you: the heavier Titanium limb put
the player on its back 24.6/2.4/16.8/9.0 s (mean 13.2 s) vs 2.1/4.2/0.0/7.5 (mean 3.5 s)
for the Aluminium one. Paying 36% more for the better material makes you 3.8x more
likely to be lying on your back.

### S3-8. SWEEP E: b2_rotor_cf (self-built SPINDLE rotor, beamlong + 2 CarbonFiber blades,
705 kg, 889 cr, E=902 J) vs mauler/Veteran, charge, fire ON, n=4, live code.
raw qa_r2s3_E_rotor_cf.txt
W3/L1/D0 [CONVERGED]  dealt 399/634/375/414 mean 456  taken mean 328
 limb column 120/231/0/79 = 430 total over EIGHT bites in four 90 s matches.
 ram column 335/403/375/334 = 1447 over 106 collisions.
*** A CONTINUOUSLY SPINNING ROTOR LANDS 2 BITES PER 90-SECOND MATCH. ***
 HIT_COOLDOWN is 0.5 s, so the ceiling is 180 bites a match; it registers 1.1% of that,
 and one whole match (run 2) registered ZERO bites while the rotor span the entire time
 and the machine still dealt 375 by ramming. Bites are big when they land (max 82.2,
 mean 54) - the problem is not the per-hit number, it is that contact almost never
 counts. Same shape as sweep C run 1 (hammer, zero limb hits in 90 s).

### S3-9. SWEEP F: b3_lance_w (RAM spear, Tungsten spike, 1994 kg, 2322 cr, E=2012 J)
vs mauler/Veteran, charge, fire ON, n=4, live code. raw qa_r2s3_F_lance_w.txt
W2/L2/D0  dealt 510/235/289/405 mean 360  taken mean 492
 limb column 43/0/54/0 = 97 damage over NINE ram strokes; MAX SINGLE HIT 12.1.
*** THE RAM IS THE WORST WEAPON IN THE GAME BY A LARGE MARGIN. ***
Per-bite damage, all measured in the SAME cell under the SAME live code this session:
  spindle rotor  902 J -> 53.8 dmg/bite (max 82.2)
  pivot hammer Ti 2875 J -> 29.2 (max 55.5)
  pivot hammer Al  998 J -> 21.9 (max 43.0)
  ram lance     2012 J -> 10.8 (max 12.1)
Twice the stored energy of the rotor, one fifth of the damage. Stored energy is not
what decides a hit; actuator KIND is, and nothing in the build screen says so.
Run 1 also produced the only structural collapse of my session: 3/12 pieces left,
949 damage taken, "counted out - flipped onto its back - the emergency struts could
not roll a hull this shape back over".

### S3-10. THE SINGLE-CELL WEAPON TABLE, ALL LIVE CODE, ALL vs mauler/Veteran, charge
| build | weapon | mass | cost | W/L/D | dealt | taken | limb dmg | bites |
|---|---|---|---|---|---|---|---|---|
| b5_discus_al  | legacy spinnerSaw disc (no Phase 4) | 707 kg | 663 | 5/0/1 | 589 | 99 | n/a | n/a |
| b5 PARKED     | same, throttle 0                    | 707 kg | 663 | 5/1/0 | 628 | 176 | n/a | n/a |
| b2_rotor_cf   | self-built SPINDLE rotor            | 705 kg | 889 | 3/1/0 | 456 | 328 | 430 | 8 |
| b3_lance_w    | RAM spear                           |1994 kg |2322 | 2/2/0 | 360 | 492 |  97 | 9 |
| b11_hammer_ti | PIVOT hammer, Titanium limb         | 816 kg |1023 | 2/1/1 | 348 | 262 | 555 | 19 |
| b10_hammer_al | PIVOT hammer, Aluminium limb        | 778 kg | 752 | 2/1/1 | 286 | 191 | 459 | 21 |
n=6/6/4/4/4/4. The legacy pre-made disc beats every Phase 4 weapon system on wins, on
damage dealt and on damage taken, for the lowest price - and so does DOING NOTHING with
it. Session 1 found this against stale code; it reproduces exactly against live code.

### S3-11. SESSION 2'S BIGGEST CLAIM DOES NOT HOLD UNDER LIVE CODE, AND THE OPPOSITE
### IS NOW TRUE: THE FLIPPER TOPPLES THE ENEMY AND NO LONGER TOPPLES ITSELF.
SWEEP G: qa_r2c_c1_lowflip_ti (session 2's own floor-level Titanium wedge, unmodified
file) vs tipper/Rookie, charge, fire ON, n=3.  raw qa_r2s3_G_lowflip_fireon.txt
 player flippedTime  0.0 / 0.0 / 0.0 s     (session 2 measured 54.6/46.5/41.1/42.6,
                                            mean 46.2 s of a 90 s match, fire ON)
 enemy  flippedTime 16.8 / 14.1 / 33.6 s   (session 2's enemy: toppled but never decisive)
 W2/L1/D0, dealt 206/417/354
 RUN 0 ENDED AT 24.0 s: "Enemy counted out - flipped onto its back". THE FIRST MATCH IN
 THIS PROJECT'S RECORDED HISTORY WON BY A TOPPLE. Toppling is now a win path.
So: session 2's "firing your own flipper is what puts you on your back (28x)" and its
companion "being toppled is a DEFENSIVE state" are both measured against superseded
code. I re-ran its exact snapshot file and got 0.0 s of self-flip three times out of
three. Whoever fixed it, fixed it. Both findings should be marked closed, not carried
forward, and this is exactly why the stamp finding (S3-0) matters.

### S3-12. WHY LIMB BITES ALMOST NEVER LAND - arithmetic from the LIVE constants
PIVOT_ARC_DEG 150 (2.618 rad), PIVOT_RETURN_DPS 200 (3.49 rad/s), RECOVER_S 0.35,
BITE_RATE_FRAC 0.45. A pivot's cycle is: spin up, ~0.2 s above 45% of max rate,
then a 0.75 s spring return, then 0.35 s dead. So even with the button held down the
limb is in a state where a contact COUNTS for roughly 0.2 s in every ~1.3 s, i.e. a
~15% duty cycle - and the enemy must be inside the arc during that 15%. Measured
consequence, four builds, live code: 8 bites (rotor), 9 (ram), 19-21 (pivot) per FOUR
90-second matches, against 100+ ram collisions in the same matches. Two of my sixteen
Phase-4 matches registered ZERO bites over a full 90 s with fire held the whole time.

## VISUAL EVALUATION - SESSION 3, MY OWN EYES, MY OWN CAPTURES
Method: multi-angle scene capture (renders FRESH; Unity_Camera_Capture without an id
still returns a stale cached frame, so I did not use it), with every non-robot renderer
temporarily disabled so the auto-framing gives a large robot. PNGs also written from a
purpose-built camera so a human can look at the same frames:
  Assets/Phase1/qa_r2_shot5_b11_hammer_ti_build.png   (build screen, Ti hammer)
  Assets/Phase1/qa_r2_shot6_live_swing.png            (live fight, arm raised)
  Assets/Phase1/qa_r2_shot7_topple.png                (live fight, enemy toppled, eUp=0.50)
S3-V1. MATERIAL: on b11 (Aluminium frame, Titanium arm+blade) the Titanium limb reads
  near-WHITE and the Aluminium hull reads pale BLUE-grey. In FRONT and RIGHT they are
  just distinguishable; in TOP both read white and I could not tell them apart.
  Session 2's "adjacent materials are indistinguishable" is HALF right: this pair is
  separable from two of three angles, but only because of shading, not hue.
S3-V2. THE WEAPON IS THE LEAST LEGIBLE PART OF THE MACHINE, CONFIRMED, THIRD SESSION
  RUNNING. The blade at the hammer's tip is a plain flat slab - in TOP it reads exactly
  like a roof spoiler, and in FRONT it is a white "T" crossbar. Nothing about it says
  edge, mass or direction of travel. The pivot housing's yellow disc is the ONLY thing
  on the machine that announces "this rotates", and it is a better weapon cue than the
  weapon.
S3-V3. NO OWNERSHIP CUE - CONFIRMED. In qa_r2_shot6, player and mauler are both blue-grey
  hulls with the same yellow accent bar, the same black wheels and the same dark-green
  engine face with the same two green marks. I identified mine only by "it has an arm".
  In the TOP pane of the topple shot I had to check the telemetry (pUp/eUp) to know
  which robot was which. This is a 10-minute fix (tint one team) and it is the single
  clearest visual failure in the game.
S3-V4. NO MOTION CUE ON A SWINGING LIMB - CONFIRMED. Captured with fire held and the
  actuator live; the arm is a static-looking bar. Corroborated numerically: at the
  moment of qa_r2_shot6 the actuator read rate=0.00 travel=0.073 rad and the image
  looks IDENTICAL in character to a frame where it is swinging at 14 rad/s. The player
  cannot see wind-up, release or recovery, which is exactly the 1.3 s cycle they would
  need to see to aim a bite (S3-12).
S3-V5. A TOPPLE IS READABLE - THE ONE THING THAT READS WELL. In qa_r2_shot7 the toppled
  tipper is unmistakably up on its edge at ~60 deg with its wheels off the floor, in
  both FRONT and RIGHT. What is NOT readable is that it is IN TROUBLE: there is no
  struggle animation, no dust, no marker, and the count-out that eventually ends the
  match has no representation in the world at all.
S3-V6. NO DAMAGE FEEDBACK, CONFIRMED. At the capture the scoreboard read dealt 207 /
  taken 294 and both machines were visually pristine - no dents, no scorch, no bent
  parts, no debris on the floor. 500 points of damage leaves no mark on the world.
S3-V7. GEOMETRY IS CLEAN. Across three captures and two builds: nothing floated, nothing
  interpenetrated, no part lagged behind the body, wheels sat on the ground correctly,
  and the tall hammer tower is visibly top-heavy in a way that honestly predicts its
  13.2 s/match of lying on its back.
S3-V8. The ISO pane never reframes (robot is a 40-px speck at 1024 px). Third session
  confirming this; FRONT/TOP/RIGHT are fine.

## SESSION 3 CLOSE-OUT
Editor state VERIFIED, not assumed: play mode cycled to undo my capture-time renderer
disables (disabledRenderers now 0). BuilderManager present, owen's build reloaded from
qa_owen_build_SAFE.txt: 790 bytes, 16 parts, 4 wheels, Validate()==OK, 750 cr.
mode=Build, Time.timeScale=1, Phase0Input.debugFire=False, RepeatHarness objects 0,
console 0 errors / 0 warnings. qa_owen_build_SAFE.txt NEVER written this session.
Matches run this session: 31 (6 afk + 6 charge + 4 + 4 + 4 + 4 + 3). Round-2 total ~99.
NEW FILES: qa_r2s3_limbstamp.txt, qa_r2s3_A_disc_afk.txt, qa_r2s3_B_disc_charge.txt,
qa_r2s3_C_hammer_al.txt, qa_r2s3_D_hammer_ti.txt, qa_r2s3_E_rotor_cf.txt,
qa_r2s3_F_lance_w.txt, qa_r2s3_G_lowflip_fireon.txt, qa_r2s3_H_visual.txt,
qa_r2s3_b11_hammer_front_ti.txt (new build), qa_r2s3_hidden.txt,
qa_r2_shot5_b11_hammer_ti_build.png, qa_r2_shot6_live_swing.png, qa_r2_shot7_topple.png
