# QA Critic Round 1 — Robot Brawl: Bolt & Blade
Date: 2026-07-26
Mandate: 5+ genuinely different robots (pivot hammer, spindle rotor, ram spear, wedge flipper, control disc/spike),
span materials, real matches vs varied opponents/tiers, visual evaluation via camera capture,
re-measure prior-round claims.

## Log

### Static re-measure of known open items (before any match)
- **Item 1 (AI cannot fire) — HALF FIXED, HALF STILL BROKEN.**
  `grep -rn aiFire Phase1/Scripts ../Phase0/Scripts` returns exactly 3 hits:
  Actuator.cs:424 (the field decl), Actuator.cs:425 (`Fire()` reads it),
  BuilderManager.cs:1736 (a *comment* saying "AIController writing aiFire").
  NOTHING ASSIGNS IT. AI actuators can never fire. STILL BROKEN.
  BUT the threatDir half IS fixed: AIController.cs:290-304 now sums
  `tp.spec.edgeHardness > 1.01` over all live parts, with a forward-vector
  fallback. So the AI *avoids* Phase-4 edges correctly; it just cannot *use* them.
- **Item 3 (CoM does not follow limb pose) — STILL BROKEN.**
  RecomputeMass callers: BuilderManager:1653, CompoundRobot:394/785/800/841,
  RaycastWheelDrive:219. Zero calls from Actuator.cs. RecomputeMass reads
  `p.go.transform.localPosition`, so it WOULD pick up a swung limb — it is
  simply never called during a swing.
- **Item 5**: CompoundRobot.cs:387 `rb.maxDepenetrationVelocity = 4f` confirmed present.

### Six builds authored, validated, LimbReport confirmed (all blockedBy=null, parts>0)
| file | archetype | material | parts | mass kg | cost | limb parts | I | tipR | rateMax rad/s | E J | kJ/swing |
|---|---|---|---|---|---|---|---|---|---|---|---|
| qa_r1_b1_maul_ti  | pivot hammer   | Titanium frame / Tungsten blade | 12 | 1135.2 | 3278 | 2 | 54.68 | 1.25 | 10.00 | 2734.0 | 7.81 |
| qa_r1_b2_vortex   | spindle rotor (beams+blades, self-built) | Aluminum / Steel blades | 14 | 789.4 | 761 | 4 | 54.13 | 1.25 | 10.00 | 2706.4 | 7.73 |
| qa_r1_b3_lance    | ram spear      | Carbon frame / Tungsten spike | 12 | 841.2 | 1519 | 2 | 204.92 | 0.30 | 3.50 | 1255.1 | 3.59 |
| qa_r1_b4_plough   | pivot wedge flipper (low, front) | Steel | 13 | 2124.9 | 1698 | 2 | 115.97 | 1.18 | 10.64 | 6562.4 | 18.75 |
| qa_r1_b5_discus   | CONTROL: spinnerSaw + spike, no Phase 4 | Steel / Tungsten spike | 11 | 1749.6 | 2126 | - | - | - | - | - | - |
| qa_r1_b6_maul_abs | pivot hammer, IDENTICAL geometry to b1 | all ABS | 12 | 398.2 | 279 | 2 | 7.46 | 1.25 | 10.00 | 372.9 | 1.07 |

Material A/B is clean: b1 vs b6 are the same 12-part geometry. Ti/W = 1135 kg / 3278cr / 2734 J per swing.
ABS = 398 kg / 279cr / 373 J per swing. 7.3x the energy for 11.7x the cost and 2.9x the mass.

### FINDING (static, from Actuator.RateCeiling): SPINDLE_MAX_RPM = 900 is unreachable at ANY buildable geometry.
RateCeiling: `safe = STEP_DISP_CAP / (tipRadius * dt)` = 0.25 / (tipR * 0.02) = 12.5/tipR.
Design speed only binds when design < safe. Spindle design = 900 rpm = 94.25 rad/s, so it binds only at
tipR < 0.133 m. The spindle part is itself a 0.15 half-cube, so the SMALLEST possible limb (a bracket
flush on its face) already has tipR = 0.15+0.20+... >= 0.35 m -> safe = 35.7 rad/s (341 rpm).
My 1.25 m rotor: safe = 10.00 rad/s = 95 RPM.
Consequence measured: b1 (PIVOT, tipR 1.25) and b2 (SPINDLE, tipR 1.25) get the *identical*
rateMax of 10.00 rad/s. The two actuator kinds are indistinguishable in top speed for any real build.

## SWEEP A — 24 matches, all vs mauler/Veteran, policy=charge, debugFire held ON
Raw: Assets/Phase1/qa_r1_matches.csv

| build | W/L | dealt (range) | taken (range) | went flat? | how it ended |
|---|---|---|---|---|---|
| b1 maul_ti (Ti/W hammer, 1135 kg, 3278cr) | 1W/3L | 310-567 | 104-329 | 3/4 FLAT | 3x "out of power" count-out, 1 judges' win |
| b2 vortex (Al spindle rotor, 789 kg, 761cr) | **4W/0L** | 453-747 | 39-114 | 0/4 | 4x enemy lost all 4 wheels, 34.9-40.5 s |
| b3 lance (Carbon ram spear, 841 kg, 1519cr) | 0W/4L | 77-345 | 149-474 | 2/4 | 4x judges' decision, lost on damage every time |
| b4 plough (Steel wedge flipper, 2125 kg, 1698cr) | 0W/4L | **0,11,11,59** | 73-284 | 4/4 | 4x "out of power" AFTER the battery was torn off |
| b5 discus (CONTROL saw+spike, 1750 kg, 2126cr) | 3W/1L | 534-780 | 124-220 | 3/4 | 2x enemy wheels off, 1x KO, 1x own power count-out |
| b6 maul_abs (ABS hammer, SAME GEOMETRY as b1, 398 kg, 279cr) | 3W/1L | 272-402 | 94-456 | 0/4 | 2x enemy count-out, 1x judges' win, 1x judges' loss |

### A-1 CRITICAL: "out of power" count-out takes the match away from the side that is WINNING on damage. n=6/24.
b1 run0: dealt 310, took 104 — LOSS, "all 300 kJ spent in 57 s".
b1 run2: dealt 451, took 154 — LOSS. b1 run3: dealt 539, took 329 — LOSS.
b5 run0: dealt 661, took 188 — LOSS, "all 300 kJ spent in 61 s".
6 of 24 matches (25%) ended with the player counted out for empty batteries; in 5 of those 6 the
player was ahead on damage, twice by more than 3:1. Item 2 is not just still open, it got worse:
holding the fire button is what empties the pack.

### A-2 CRITICAL: the wedge is not a weapon and toppling is not a win path. n=4.
b4 plough total damage over 4 matches: 0, 11, 11, 59. Its own flipped time was 4.1-8.4 s;
the ENEMY's structFrac never moved off 0.91 (it lost exactly one wheel, all match, every match).
Root cause is visible in the palette dump: wedge edgeHardness = **1.0**, and AIController:296
skips `edgeHardness <= 1.01` as "structure is not a weapon". So the wedge is literally classified
as structure by the AI, and Actuator.Bite prices it as structure. liftBias 0.75 is real
(CompoundRobot.cs:577) but a lift that deals no damage and does not flip anything is a no-op verb.

### A-3 CRITICAL: item 6 (battery on one seam) reproduces at 100% on a heavy build.
b4 plough lost its battery in **4 of 4** matches. capKJ went 300 -> 60 every time, and the cause
line was identical each time: "240 kJ of its 300 kJ pack was torn off the chassis". Structurally
the plough lost exactly ONE part out of 13 (structFrac 0.92) and that one part decided the match.
Also seen on b6 run3 (cap 240) and on the ENEMY in b6 run2 and b3 run2 - it is not player-specific.

### A-4 MAJOR: paying more makes you worse. Controlled A/B, identical 12-part geometry, n=4 each.
b1 = Titanium frame + Tungsten blade: 1135 kg, 3278 credits, 2734 J/swing -> **1W/3L**.
b6 = all ABS:                          398 kg,  279 credits,  373 J/swing -> **3W/1L**.
The heavy hammer never reached its rate ceiling (live maxRate 7.16 of a quoted 10.00) because
MOTOR_KW = 2.5 kW needs 1.09 s to wind 2734 J, and it drained the pack doing it (flat 3/4).
The ABS hammer reached 14.00 rad/s (the pivot design ceiling) and finished with 90-146 kJ spare.
Spending 11.7x the credits and 2.9x the mass buys you a strictly worse robot.

### A-5 MAJOR: the ram spear is the weakest verb in the game. n=4, 0W/4L.
b3 lance fired 82-91 times per match (drives column) and was pinned at maxRate 3.50 rad/s every
single time - RateCeiling for Ram is a hard `Mathf.Min(3.5, ...)` regardless of what you build.
Damage 77/149/219/345 against 274/272/404/474 taken. Every loss was a judges' decision on damage.
A ram cannot miss its ceiling and cannot exceed it, so there is no build decision to make.

### A-6 MAJOR: builder-quoted rateMax does not match the fight. 
LimbReport quoted rateMax 10.00 for b1/b2/b6 (tipRadius 1.25). Live maxRate measured 14.00 (b6),
16.67 (b2, 3 of 4 runs), 7.16 (b1). Actuator.Recompute() re-derives tipRadius from LIVE parts, so
shearing one blade off a 1.25 m rotor drops tipRadius to 0.75 and RAISES the safe rate from
10.00 to 16.67 rad/s. Losing half your weapon makes the other half swing 67% faster. The comment
at Actuator.cs:236 claims "the number on the build screen is the number the fight uses". It is not.

### A-7 MAJOR: b2 vortex is dominant and it is the CHEAPEST armed build. 4W/0L.
761 credits, 789 kg, 4 wins in 34.9-40.5 s, dealt 453-747 while taking 39-114, never went flat.
Every win was the same script: shear all four of the mauler's wheels off. The self-built rotor
(spindle + 2 beams + 2 blades) is 2.8x cheaper than the control disc bot and beats it.

## SWEEP B — 34 more matches (16 matched afk/charge, 18 across the roster). Raw: qa_r1_matches_b.csv
### AGGREGATE OVER ALL 58 MATCHES
How matches ended:
  judges' decision (90 s expiry)                19  32.8%
  PACK TORN OFF -> power count-out              19  32.8%
  all 4 wheels off count-out                     9  15.5%
  own pack simply ran dry -> power count-out     6  10.3%
  KO, core destroyed                             4   6.9%
  flipped onto its back count-out                1   1.7%
=> 25 of 58 matches (43.1%) are decided by the ENERGY system, not by the fight.
=> 19 of 58 (32.8%) are decided by ONE PART, the battery, coming off ONE seam.
=> exactly 1 of 58 (1.7%) was decided by a flip, and it was a rotor doing it, not a wedge.

Win/loss by build and policy:
  b1 maul_ti   afk    n=4  W1/L3  dealt 211 taken 318
  b1 maul_ti   charge n=4  W1/L3  dealt 467 taken 227
  b2 vortex    afk    n=4  W4/L0  dealt 382 taken  31
  b2 vortex    charge n=10 W9/L1  dealt 517 taken 186
  b3 lance     charge n=4  W0/L4  dealt 198 taken 356
  b4 plough    charge n=10 W0/L10 dealt  49 taken 138
  b5 discus    afk    n=4  W2/L2  dealt 171 taken 299
  b5 discus    charge n=10 W7/L3  dealt 607 taken 182
  b6 maul_abs  afk    n=4  W0/L4  dealt  60 taken 261
  b6 maul_abs  charge n=4  W3/L1  dealt 337 taken 233

### B-1 ITEM 9 IS REFUTED. Parking no longer beats fighting. Matched test, n=16 each.
Same four builds (b1,b2,b5,b6), same opponent (mauler/Veteran), only the drive policy differs:
  charge: 11W / 5L = 69% win rate, mean damage dealt 509
  afk:     7W / 9L = 44% win rate, mean damage dealt 206
Per build: b1 tie (1W3L both), b2 tie (4W0L both), b5 charge 3W1L vs afk 2W2L,
b6 charge 3W1L vs **afk 0W4L**. The ABS hammer is the clearest case - parking it is a
guaranteed loss. Phase 4 DID change this; the standing "afk 3W/2L, charge 0W/5L" no longer holds.
Report this as fixed, with the caveat that it is fixed by adding a verb, not by fixing driving.

### B-2 CRITICAL: 7 of 31 losses (23%) were suffered by the side that was AHEAD ON DAMAGE.
All 7 were power count-outs. The worst two:
  b5 discus vs scout/Champion: dealt 446, took 38 (11.7:1) -> LOSS, "all 300 kJ spent in 47 s"
  b5 discus vs scout/Champion: dealt 579, took 22 (26.3:1) -> LOSS, "all 300 kJ spent in 53 s"
This is not "energy is a constraint", it is "the scoreboard and the win condition disagree".

### B-3 MAJOR: roster balance has MOVED, item 7 is partly stale.
  bulwark/Champion   0W/3L (still cannot beat the player - item 7 holds)
  tipper/Rookie      0W/3L  but tipper/Veteran 3W/0L  <- tier, not the bot, was the problem
  scout/Rookie       3W/0L  and scout/Champion 2W/1L  <- scout is now the strongest opponent
  widowmaker/Champion 1W/2L, and its one win was a 14.9 s KO of the core (dealt 509, took 0)
  mauler/Veteran     22W/18L over n=40
Note scout/ROOKIE beat the player 3/3 while tipper/ROOKIE lost 0/3 - the tiers are not
monotonic across the roster.

## VISUAL PASS (Unity_SceneView_CaptureMultiAngleSceneView, build_room props hidden so the robot fills frame)
Note: Unity_Camera_Capture with a custom camera is UNUSABLE this round - Camera.GetEntityId()
returns 568105589205777590, which exceeds JSON's 2^53 integer precision, so the id that reaches
the tool is 568105589205777600 and it answers "Failed to render scene preview". Same problem
blocks focusObjectIds. Unity_Camera_Capture with no id returned a byte-identical cached frame
twice in a row after I moved the SceneView, so it is not reliable either. The multi-angle tool
is the only one that rendered live state.

### V-1 CRITICAL: material is INVISIBLE on every part that is not Structural.
Measured by reading renderer.sharedMaterial.color on every part of two builds with IDENTICAL
geometry, one all-Titanium/Tungsten and one all-ABS:
  part      b1 (Ti/W)                 b6 (ABS)                  same?
  core      (0.95,0.74,0.10)          (0.95,0.74,0.10)          IDENTICAL
  pivot     15 renderers, first 6:    15 renderers, first 6:    IDENTICAL, all 15
            (.28,.29,.33)(.66,.67,.72)(1.00,.62,.12)...          same list
  blade     (0.28,0.29,0.33)(0.66,0.67,0.72)   SAME TWO COLOURS  IDENTICAL
  engine    (0.19,0.19,0.22)          (0.19,0.19,0.22)          IDENTICAL
  battery   (0.12,0.17,0.14)          (0.12,0.17,0.14)          IDENTICAL
  wheel     (0.06,0.06,0.07)          (0.06,0.06,0.07)          IDENTICAL
  chassis   (0.62,0.60,0.56)          (0.92,0.90,0.85)          <- the ONLY part that differs
A TUNGSTEN blade and an ABS blade are pixel-identical. So are the actuators. The player pays
6.0 cr/kg for tungsten vs 0.5 for ABS and gets zero visual feedback on the weapon - the one part
they care most about. MatDB has proper per-material colour+metallic+smoothness for all seven
materials and PartVisualFactory only applies it to P1Category.Structural.
Shots: qa_r1_shot_build_1_qa_r1_b1_maul_ti.png vs qa_r1_shot_build_6_qa_r1_b6_maul_abs.png
(both under Assets/Phase1/, rendered by my own camera at 1400x900).

### V-2 MAJOR: the fix already exists and is OFF by default.
bm.MatViewOn() == false on entry. bm.SetMatView(true) switches to MatDef.auditColor, which IS a
proper Okabe-Ito set (ABS yellow .94/.89/.26, Aluminum sky .34/.71/.91, Steel dark blue 0/.45/.70,
Titanium orange .90/.62/0, CarbonFiber pink .80/.47/.65, Tungsten teal 0/.62/.45, Rubber grey).
With it ON I could immediately read the tungsten blade (teal) and the aluminium battery (blue) in
the capture. With it OFF I could not tell a plastic robot from a titanium one at a glance.

### V-3 MAJOR: an unknown material key silently becomes Aluminum, and Validate() says OK.
MatDB.cs:104 `return mats["Aluminum"];  // unknown key (old snapshot, typo) degrades safely`.
The carbon-fibre key is **"CarbonFiber"**, but the project's own snapshot documentation calls it
"Carbon". I wrote b3_lance with `Carbon` and it loaded as ALUMINIUM, Validate() returned null,
LimbReport quoted numbers, the fight ran, and nothing anywhere said a word. My "carbon spear"
result above is therefore an ALUMINIUM spear result - I am flagging my own data. A player who
hand-edits a snapshot, or any doc/tool that says "Carbon", silently builds a different robot.

### V-4 CRITICAL (re-measure of open item 3): CoM does NOT move when a limb swings. DELTA = 0.000000 m.
Raw: Assets/Phase1/qa_r1_com.txt. b1 Titanium hammer, live fight, 900 fixed frames at timeScale 0.2
(~9 complete 150-deg swings, travel sampled from 0.000 to 2.618 rad):
  rb.centerOfMass = (0.06826, 0.10558, -0.01168) at EVERY sample, to five decimals, with
  travel at 0.000 / 0.166 / 0.441 / 0.791 / 1.203 / 1.668 / 2.181 / 2.618 rad. rb.mass = 1135 throughout.
  CoM at travel~0 minus CoM at travel 2.618 = 0.000000 m.
How much it SHOULD move: the limb is beam 108.2 kg at r=0.45 plus blade 29.0 kg at r=1.00, so the
limb's own CoM sits 0.566 m from the pivot. Sweeping 150 deg moves that point 2*0.566*sin(75) = 1.093 m.
On a 1135 kg body carrying a 137.2 kg limb that is a 0.132 m (13 cm) shift of the whole robot's CoM -
against a wheelbase half-track of 0.27 m. A raised hammer genuinely should make you tippy and it does
not, at all. Item 3 confirmed open, and now quantified.

### V-5 MAJOR: every limb part's VISUAL is bigger than the thing that actually hits.
Measured mesh bounds in the part's own local frame vs its colliders (b1, live fight):
  part     spec/solid collider     visual mesh              LimbEdge trigger
  pivot    0.300 cube              0.390 x 0.396 x 0.390    -            (+30% visual)
  beam     0.600 x 0.200 x 0.200   0.740 x 0.218 x 0.564    0.700x0.300x0.300
  blade    0.500 x 0.050 x 0.120   0.573 x 0.053 x 0.110, centre offset +0.036 on x
           -> blade visual spans -0.2505..+0.3225 on its long axis
           -> blade TRIGGER spans -0.300..+0.300
So along the swing the blade's painted tip reaches 2.3 cm PAST the volume that can bite, while in
cross-section the bite volume is 0.150 tall and 0.220 wide against a blade that is drawn 0.053 tall
and 0.110 wide - the hit box is 2.8x the blade's visible thickness and 2.0x its visible width.
A hit can register with a 3.5 cm visible air gap, and a tip hit can visually connect and do nothing.
On a 5 cm blade both errors are larger than the blade.

### V-6 what the swing actually looks like (shot: multi-angle capture, b1 vs mauler, t=2.7 s, timeScale 0.04)
GOOD: the arm and blade are rigidly posed by transform (verified numerically - beam centre stays at
|r| = 0.4502 and blade at |r| = 0.9997 from the pivot through the whole arc, so the limb does NOT
stretch, teleport or come apart). travel advances smoothly 0.000 -> 2.618 rad across frames.
BAD, and this is the legibility problem: there is NO motion cue of any kind. No trail, no blur, no
arc ghost, no wind-up telegraph, no impact spark in the capture. At 10 rad/s and 50 fps the tip
moves 25 cm per rendered frame - a quarter of the arm's length between frames. The only reason I
could tell the hammer was swinging at all was by reading Actuator.travel in a log. A player watching
at timeScale 1 sees a stick that is in a different place each frame.
Contrast: the disc weapon (b5) has SpinnerWeapon and reads as spinning; the actuated limbs do not.

### V-7 what else the captures showed (b2 vortex vs tipper, live fight, ~8 s in)
- The self-built rotor reads clearly as a WEAPON in TOP view: a 4-arm cross with a lit cyan hub.
  Silhouette legibility of the spindle archetype is genuinely good.
- At rate = 10.00 rad/s (its cap, confirmed live) there is still no blur/trail/disc-ghost. The
  rotor renders as a crisp static X. Compared to the disc weapon, which reads as spinning.
- Floating damage numbers DO render and are readable ("23" red, "69" yellow) - that part works.
- Detached parts spawn real debris shards with rigidbodies. NOT A BUG: the six shards I found at
  y = -28..-33 m falling at 24 m/s had fallen through the floor because MY capture rig had
  SetActive(false) on the arena root. Retracting that as an observation; it is my artifact.
- One genuine oddity worth a look: CompoundRobot keeps detached parts in `parts` with a live
  GameObject in one case and a null one in another in the same body
  ("bladeXP_8 goNull=False" next to "bladeXN_9 goNull=True"). Inconsistent teardown.

## FINAL STATE VERIFICATION
mode=Build parts=16 wheels=4 validate=OK(null) timeScale=1 debugFire=False matView=False
inactiveObjs=0 qa_owen_build_SAFE.txt=790 bytes (untouched) opponent=mauler/Veteran
Console: 2 errors total, BOTH mine (a camera instance id that overflowed JSON int precision, and
a pixelsPerUnit out of range). ZERO game-code errors across 58 matches and ~40 minutes of play.

================================================================================
# SECOND CRITIC PASS (post-dev-patch) — 2026-07-26, later session
A prior critic pass + a dev implementation pass already wrote to this file / qa_r1_dev.md.
This pass RE-MEASURES the dev's round-1 claims and runs its own build/material/visual sweep.
Findings below are independent measurements, not a rehash.

## R3-A. Three NEW builds authored (weapons/materials neither prior round tested)
| file | weapon system | material | parts | mass kg | cost | limb parts | blockedBy | I | tipR | rateMax | kJ/swing |
|---|---|---|---|---|---|---|---|---|---|---|---|
| qa_r3_b1_hook_w    | pivot arm ending in a HOOK (never tested before) | all-Tungsten frame+arm+hook | 12 | 3856.4 | 22101 | 2 | null | 172.06 | 0.980 | 12.76 | 39.99 |
| qa_r3_b2_whisk_cf  | spindle "eggbeater": 2 brackets + 2 TANGENTIAL blades, short arms | CarbonFiber frame / Steel blades | 14 | 508.9 | 1327 | 4 | null | 6.13 | 0.660 | 18.94 | 3.14 |
| qa_r3_b3_plow_steel| STATIC wedge bolted to the chassis nose, NO actuator | Steel / Steel wedge | 10 | 1642.1 | 1311 | - | - | - | - | - | - |
Reused for comparison: qa_r2_b5_discus_al (control, 706.7 kg, 663cr),
qa_r2_b1_hammer_ti (1924.0 kg, 1924cr), qa_r2_b4_flipper_abs (449.7 kg, 305cr).

RATE-CEILING LAW CONFIRMED ON A THIRD DATA POINT: rateMax = 12.5 / tipRadius exactly.
  b2_whisk tipR 0.660 -> 18.94 (12.5/0.660 = 18.94)   b1_hook tipR 0.980 -> 12.76 (12.5/0.980 = 12.76)
So a SPINDLE's advertised 900 rpm (94.2 rad/s) needs tipR < 0.133 m, which no legal build reaches.
Even my deliberately-stubby 0.66 m eggbeater tops out at 181 rpm - 20% of the quoted spec.

## R3-B. THE MASS/POWER SOLVENCY LINE — derived, then confirmed against every sweep on disk
Constants read from source: PowerPlant.DRIVE_KW_PER_TONNE = 3.5; core energyKJ 60 / powerKW 14;
battery energyKJ 240 / powerKW 8. Default pack therefore = 300 kJ, 22 kW.
Driving at full throttle costs 0.0035*mass kW, so a full 90 s match at throttle 1 costs
  E_drive = 0.315 * mass_kg  kJ.
Set that equal to the 300 kJ default pack:  **the heaviest robot that can DRIVE for a whole
match on the stock pack is 952 kg.** Above that the machine is arithmetically bankrupt before
it ever touches the enemy, and nothing in the builder says so.

Predicted time-to-flat  t = 300 / (0.0035*m)  vs every measurement on disk:
  2501 kg (r2 b6_hammer_w)  predicted 34 s   measured 33 / 35 / 46 s
  1994 kg (r2 b3_lance_w)   predicted 43 s   measured 43 / 49 / 55 / 64 s
  1924 kg (r2 b1_hammer_ti) predicted 45 s   measured 66 s
  1642 kg (my b3_plow)      predicted 52 s   measured 61 / 66 / 68 s
  <=952 kg (b5 707, b8 770, b4 450, b2 509)  predicted NEVER   measured never flat, any sweep
The model is a tight lower bound and never once wrong about which side of the line a build is on.

MATERIAL CONSEQUENCE: for the SAME ~10-14 part chassis, whole-machine mass by material is
  ABS 450 | CarbonFiber 509 | Aluminum 707-770 | Steel 1642-1924 | Tungsten 2501-3856
The 952 kg line falls between Aluminum and Steel. **4 of the 7 materials in the catalog
(Steel, Titanium, Tungsten, and any mix of them) cannot finish a match.** The material picker
is a menu where the top half of the list is a trap.

## R3-SWEEP 01  qa_r3_b3_plow_steel (STATIC floor-level wedge, no actuator, 1642 kg) vs tipper/Veteran, charge, n=4
W1/L3/D0  [CONVERGED]  raw: Assets/Phase1/qa_r3_sw01.txt
 run0 Loss 76.2s dealt 320 taken 488  pieces 10/10  - COUNTED OUT, 300 kJ gone in 66 s
 run1 Win  69.3s dealt 304 taken 197  pieces 10/10  - ENEMY counted out (its battery torn off, dry at 59 s)
 run2 Loss 70.8s dealt  93 taken 279  pieces 10/10  - COUNTED OUT, 300 kJ gone in 61 s
 run3 Loss 78.0s dealt  47 taken 511  pieces 10/10  - COUNTED OUT, 300 kJ gone in 68 s
*** 4 of 4 decided by a power count-out. ZERO of 4 decided by damage or a KO. ***
*** The plow lost ZERO pieces in all four matches (10/10 every time) - it is the most
    structurally durable build tested in three rounds - and it still went 1W/3L, because
    being made of steel is what bankrupts it. Armour and solvency are the same axis. ***
*** TOPPLING: enemy never flipped in any of the 4. Confirming r1+r2: the wedge has now
    failed to topple anything as a 2125 kg pivot flipper, a 450 kg ABS pivot flipper, a
    929 kg Al/Ti pivot flipper, and now as a STATIC floor-level plow whose ramp underside
    genuinely IS below the enemy chassis. Geometry was not the only problem. n=24 across 3 rounds, 0 topples.

## R3-SWEEP 02  SAME plow, SAME opponent, ONLY the drive policy differs. PARKED, n=4. raw qa_r3_sw02.txt
 run0 Loss 90.0s dealt 0 taken 115 - judges on damage
 run1 Loss 90.3s dealt 0 taken 210 - judges on damage
 run2 Win  40.5s dealt 0 taken  32 - ENEMY beached itself (0 of 4 wheels on the ground)
 run3 Loss 90.3s dealt 0 taken  28 - judges on damage
*** MECHANISM FOR OPEN ITEM 9, MEASURED. Identical build/opponent/tier, n=4 each: ***
   charge : W1/L3  dealt 320/304/93/47 (mean 191)  |  3 of 4 lost to a POWER count-out
   afk    : W1/L3  dealt   0/  0/ 0/ 0 (mean   0)  |  0 of 4 power count-outs, ever
 Driving buys 191 damage a match and costs you the pack. Parking buys nothing and costs
 nothing. THE RECORD IS IDENTICAL. Parking is not "beating" fighting because parking is
 good - it is because DRIVING IS THE SINGLE BIGGEST LINE ITEM IN THE ENERGY BUDGET and the
 count-out rule then takes the match off the player who spent it. Items 2 and 9 are one bug.

## R3-SWEEP 03  qa_r3_b1_hook_w — the HOOK weapon, untested in rounds 1 and 2.
All-Tungsten, 3856.4 kg, **cost 22101 credits**, limb I=172, 39.99 kJ/swing.
vs mauler/Veteran, charge, fire=ON, n=4.  W2/L2  raw qa_r3_sw03.txt
 run0 Win  90.3s dealt 423 taken 257 - judges on damage
 run1 Loss 33.3s dealt 165 taken 210 - COUNTED OUT, **300 kJ gone in 23 s** (model predicted 22 s)
 run2 Win  90.3s dealt 408 taken 338 - judges on damage
 run3 Loss 87.9s dealt 367 taken 377 - COUNTED OUT, 300 kJ gone in 78 s
COST-EFFECTIVENESS, same opponent/tier/policy, cross-round:
  qa_r2_b5_discus_al   706 kg,    663 cr, ZERO Phase-4 parts  ->  W4/L0  dealt mean 546
  qa_r3_b1_hook_w     3856 kg, 22101 cr, Phase-4 hook limb    ->  W2/L2  dealt mean 341
**33x the price and 5.5x the mass buys half the wins and 62% of the damage.**
The hook's stated identity ("drags them toward you and off their line") never showed up:
enemy never flipped, never beached, and pieces lost tracked ordinary ram/bite damage.

## R3-SWEEP 04/05  qa_r3_b2_whisk_cf — CarbonFiber short-arm eggbeater, 508.9 kg, 1327 cr
vs mauler/Veteran, fire=ON, n=4 each. raw qa_r3_sw04.txt / qa_r3_sw05.txt
  charge : **W4/L0**  dealt 360/529/302/526 (mean 429)  taken mean 108  pieces 14/14 EVERY match
  afk    :   W3/L1    dealt 330/ 80/158/264 (mean 208)  taken mean 201  pieces 14/14 every match
Enemy was reduced to 1-6 of 11 pieces while the whisk lost NOTHING, in 8 of 8 matches.

### R3-1 THE PARKING PARADOX RESOLVED. Driving is a losing move IF AND ONLY IF you are over the solvency line.
Two builds, same instrument, same opponent tier, n=4 per cell:
  1642 kg steel plow (OVER the 952 kg line):  charge W1/L3 (3 power count-outs) | afk W1/L3 (0 count-outs)
   509 kg CF whisk  (UNDER the line):         charge **W4/L0**                  | afk W3/L1
Under the line, driving is worth +1 win, +106% damage dealt and -46% damage taken.
Over the line, driving is worth +191 damage and it hands you the loss anyway.
r1's critic said item 9 was REFUTED; r2's critic said parking is still nearly a free win.
BOTH ARE RIGHT ON THEIR OWN DATA and neither is the rule. The rule is mass.

### R3-2 CRITICAL: every rotary weapon in the game is capped at EXACTLY 12.5 m/s tip speed, so a long arm is strictly worse than a short one.
Actuator.RateCeiling: safe = STEP_DISP_CAP / (tipRadius * dt), STEP_DISP_CAP = 0.25, dt = 0.02.
Therefore tipSpeed = rate * tipRadius = 0.25/0.02 = **12.5 m/s for EVERY rotary limb, always,
independent of geometry, material, motor or inertia.** Confirmed on 3 builds this round:
  tipR 0.660 -> 18.94 rad/s | tipR 0.980 -> 12.76 | tipR 1.025 -> 12.20  (all = 12.5/tipR)
Consequence: lengthening an arm buys ZERO tip speed and costs inertia, mass, credits and
wind-up time. Actuator's own header sells the opposite ("A long heavy arm stores more and
winds up slower. That tradeoff IS the feature."). The tradeoff is one-sided: the long arm's
only remaining upside is Energy = 0.5*I*rate^2 feeding `drain`, and that upside is eaten by
the 2.5 kW motor (windUp = E / 2500 W):
  whisk  1099 J -> 0.44 s to full rate  -> effectively continuous
  hook  13997 J -> **5.60 s** to full rate -> at most ~5 charged swings in a 90 s match
MEASURED END RESULT, same opponent/tier/policy: 1327 cr / 3.14 kJ-per-swing whisk = **W4/L0**;
22101 cr / 39.99 kJ-per-swing hook = W2/L2. The expensive weapon loses to the cheap one.

### R3-3 CRITICAL: toppling is not merely undemonstrated, it is ~3x to 11x short of the physics required. Arithmetic, from source.
Actuator.Bite: shove = min(drain*0.3, DamageResolver.SHOVE_CAP=500); topple impulse =
shove * liftBias(0.75) * TIP_ARM_M(0.45) => **max 168.75 N.m.s, ever, at the cap.**
To roll a 707 kg machine over its 0.27 m half-track with CoM at ~0.25 m: the CoM must rise
0.118 m => 818 J; inertia about the tipping edge >= m*r^2 = 707*0.368^2 = 96 kg.m^2 (body
inertia makes it more, ~150). Required angular momentum = I*sqrt(2E/I) ~ **495 N.m.s.**
So the BEST case in the game is 3x short, and a typical case is far worse - my whisk's
drain is 0.4*1099 = 440 J, so shove = 132 and the topple impulse would be 44.5 N.m.s, **11x short**.
This is why 0 topples have been observed in ~80 matches across three rounds and four
different wedge builds. It is a magnitude bug, not a geometry bug, and no build can fix it.

## R3-SWEEP 06  qa_r2_b5_discus_al (control) vs **RIPPER/Champion** — the new actuated roster bot
W3/L1  raw qa_r3_sw06.txt.  RIPPER dealt 268/268/411/362 — it is a real opponent, not a punchbag.
 run2 Loss 61.5s — KO, player core destroyed, player pieces **0/11** with a worst single-step
 loss of 5. The whole-machine teardown r2 saw on b2_rotor_cf reproduces on a different build
 against a different opponent. Still open.

### R3-4 VERIFIED FIXED: the AI can now fire a Phase 4 weapon (open item 1).
Live poll of RIPPER's pivot during sweep 06:
  t=33.3 dist=1.83 dot=0.81 state=charge **aiFire=True phase=Returning travel=2.059**
Caveat worth logging so the next tester does not repeat my mistake: three earlier polls read
aiFire=False, because the gate is narrow (FIRE_RANGE 3.0 m AND dot > FIRE_ARC_DOT 0.35) and
because I computed the facing with Vector3.forward instead of AIController.forwardLocal.
The flag is real and the limb cycles. Item 1 CLOSED.

### R3-5 VERIFIED FIXED: CoM now tracks the limb (open item 3). Independent re-measure.
qa_r2_b1_hammer_ti, 1924.0 kg, live fight vs bulwark/Rookie, timeScale 0.08:
  travel 0.0000 (Ready)      com = (0.02286, 0.17717, -0.01148)
  travel 0.1531 (Driving)    com = (0.02286, 0.17684, -0.00720)
  travel 1.6406 (Returning)  com = (0.02286, 0.14717, +0.01650)
  |com(1.64) - com(rest)| = **0.0410 m** (was 0.000000 m before the fix).
Item 3 CLOSED. Note the magnitude is modest on this build (limb ~130 kg of 1924) - 4 cm against
a 0.27 m half-track. It is correct physics, not yet a strong tactical lever.

### R3-6 A spindle at full rate is nearly FREE; driving is what costs.
Live, whisk parked with the rotor pinned at rate = MaxRate = 18.94 rad/s:
  t=15.6 s, storedKJ = **295 of 300**. Fifteen seconds at full weapon speed cost 5 kJ.
Compare the drive: 0.0035 * mass kW, i.e. 1.8 kW for this 509 kg bot, 5.7 kW for the 1642 kg plow.
The energy economy prices the WEAPON at nothing and MOVEMENT at everything. That is backwards
for a fighting game and it is the same root as R3-1.
Also confirms the tip-speed cap in the live sim: rate * tipRadius = 18.94 * 0.660 = **12.50 m/s exactly**.

## R3-C. VISUAL PASS. Tool that works: Unity_SceneView_CaptureMultiAngleSceneView with
## build_room/sandbox renderers disabled. Unity_Camera_Capture with an instance id is still
## broken (the 64-bit id loses precision in JSON: console shows "No GameObject found with
## Instance ID 568105589205431200"). ISO pane never reframes and is useless. Confirms r2 C-0.

V1. **The HOOK is not modelled.** hook_11 has THREE renderers, against 37 for the beam it is
    bolted to and 15 for the pivot. In FRONT it is a plain rectangular block on the end of a
    bar. Nothing curls, nothing hooks, there is no claw. Its whole stated identity ("curls back
    on itself... drags them toward you") is invisible. A player cannot tell a hook build from a
    blade build by looking at it.
V2. **Material reads in the build room and stops reading in the arena.** Build-room FRONT:
    all-Tungsten hook bot renders dark purple-brown, CarbonFiber whisk renders dark green-black,
    Steel blades on it render light blue-grey - all three clearly distinguishable, so r1's
    weapon-material fix is real. But in the ARENA capture my CarbonFiber whisk and the ALUMINIUM
    mauler are both black boxes with an identical yellow accent bar and an identical green
    engine face. Two different materials, two different robots, same read.
V3. **The TOP pane renders the same parts 4-5 shades lighter than FRONT.** Tungsten reads
    near-black in FRONT and pale PINK in TOP; CarbonFiber reads dark green-black in FRONT and
    blown-out WHITE in TOP. The material cue is not stable across camera angle, so "what is it
    made of" changes depending on where the camera happens to be.
V4. **No motion cue on a rotor at its cap.** Two consecutive live captures of the whisk with
    rate = MaxRate = 18.94 rad/s (tip 12.50 m/s, confirmed numerically in the same frame):
    the blades are a crisp, hard-edged static bar at a different arbitrary angle in each frame.
    No blur, no trail, no arc ghost, no disc-of-revolution. The ONLY cue that the weapon is
    live is the cyan glow on the spindle hub - which is on whether it is spinning or not.
    At timeScale 1 and 60 fps the tip covers 0.21 m per frame against a 0.50 m blade, so the
    blade strobes ~40% of its own length between frames. This is now the third round in a row
    this has been reported and it is the single most visible gap in the game.
V5. **Floating damage numbers collide.** Two "40" pops landing within a few frames render
    overlapping and read as garbage ("420" in the RIGHT pane). The numbers work; they do not
    de-conflict.
V6. **No ownership cue.** In the TOP pane of a live fight I could not identify my own robot
    except by finding the rotor. Same grey/black bodywork, same yellow accent, same green
    engine face on both machines. Third round this has been raised.
V7. NOT A BUG, retracted before reporting: the "floating" cube near the robots is the single
    detached debris chunk (MAULER_debris, 57 kg) asleep on the floor at y = 0.110 - it only
    looked airborne because I had disabled the arena floor renderer for framing. Checked
    numerically: 1 non-robot rigidbody in the scene, velocity 0.00, IsSleeping true.
    No interpenetration, no stretched limbs, no parts left behind by a swing were found.

## R3 FINAL STATE
mode=Build parts=16 wheels=4 validate=OK  timeScale=1  debugFire=False
owen's build restored from qa_owen_build_SAFE.txt; file untouched at 790 bytes,
md5 f4e052c2081911eeadc5502e86a63a4b == qa_build_prewheelfix.txt.
QASweep harness object destroyed, all renderers re-enabled (0 left disabled).
Console: 1 error total, and it is NOT from this run - it is the Unity_Camera_Capture
instance-id overflow stamped [12:31:15], hours before this session. ZERO game-code errors
across 24 harness matches plus 2 instrumented live fights.
Matches this round: 24 (sweeps 01-06, n=4 each) + 2 instrumented partial fights.

## R3-SWEEP 07  PREDICTIVE TEST OF THE SOLVENCY THESIS — and it holds.
I built qa_r3_b4_lance_al: the SAME ram-spear archetype r2 tested (ram + beam + spike, same
positions), re-materialled to Aluminium/Steel so it lands UNDER the 952 kg line.
  758.1 kg, cost 724, LimbReport parts=2 blockedBy=null I=121.79 tipR=0.300 rate=3.50 (the Ram
  hard cap) 2.13 kJ/swing. Same opponent as r2 sweep 04: bulwark/Veteran, charge, fire=ON, n=4.
  r2's 1994 kg Steel/Tungsten lance : W2/L2, PLAYER counted out on power twice (43 s, 49 s)
  my  758 kg Aluminium/Steel lance  : **W4/L0**, player NEVER went flat
  ...and all four of my wins were "**Enemy counted out - out of power**" (57/63/66/69 s).
Halving the mass of the identical weapon turned 2W/2L into 4W/0L, and the win condition it
turned into was "outlast the other machine's battery". Prediction made from arithmetic before
the sweep, confirmed by the sweep, n=4, [CONVERGED].

## R3-D. AGGREGATE OVER MY 28 MATCHES THIS ROUND
How they ended:
  judges' decision at 90 s ................ 15  53.6%
  power count-out (either side) ........... 10  35.7%
  KO, core destroyed ....................... 1   3.6%
  all wheels torn off ...................... 1   3.6%
  beached ................................... 1   3.6%
  **topple / flip ........................... 0   0.0%**
=> 89% of matches are decided by a clock or a battery. ONE match in 28 ended by destroying
   the opponent. Zero ended by turning one over, on top of the ~80 in rounds 1 and 2.
