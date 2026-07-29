# QA User-Path Critic — Round 1 of 3
Date: 2026-07-27. Agent: critic (round 1). Unity 6000.5.4f1.
Mandate: drive the game as a PLAYER does (pointer+click), prove every part FUNCTIONS
(with a number), fight-test, and look at the result.

Status: STREAMING. Sections are appended as measured.

## 00. Session start
- Unity alive, isPlaying=True, dataPath=/Users/leondu/Setup Guide In-Editor Tutorial/Assets

## 01. Plan and instrumentation (written before any measurement)
Two new harnesses added in ONE domain reload:
- `Assets/Phase1/Scripts/UP1Sweep.cs`  — the pointer path BEYOND a bare core:
  part-onto-part, a 4-deep stack, the full R cycle at every yaw, Esc, mis-clicks,
  panel clicks, empty space, and a complete robot built start-to-finish by pointer.
- `Assets/Phase1/Scripts/FuncProbe.cs` — per-part FUNCTION with a number:
  actuator rate/travel/phase cycles, disc omega, wheel displacement + yaw,
  gyro flip-recovery time, PowerPlant capacity/peak/energy used.
Existing `UserPathTest.cs` (part x face onto a bare core) is left untouched as the
baseline it already is; this round adds the cases it does not reach.

## 02. USER PATH — coverage beyond a bare core  (Assets/Phase1/qa_up1_sweep.txt)
Harness: `UP1Sweep.cs`. Every placement below went pointer -> UpdateBuild -> ghost ->
click. Result: **135 checks, 0 hard failures.** The three fixes from last round hold up
under a much wider sweep than the one that validated them.

### A. Part ONTO PART (fixture: core + a beam already bolted to it; 18 parts x 5 free faces)
All 90 probes resolved. Every rejection is a correct rule:
- `plate` +X/+Z/-Z on the beam -> "No socket on this face" (a plate's 0.06 m edge is
  under SOCKET_PITCH — correct, and the same rule as on the core).
- `wheel` +Y/-Y -> "Wheels attach to side faces only".  `spinner`/`spinnerSaw` -Y ->
  "Spinner can't mount underneath".
- Everything else placed, including all six Phase-4 parts and the blade — i.e. the
  blade fix works on part-onto-part too, not only on a core.

### B. FOUR-DEEP STACK, every level by pointer
core -> chassis(+Z) -> bracket(+Z) -> beam(+Z) -> blade(+Z), 4 clicks, each landing
exactly where the ghost promised (positional delta < 1e-3 m), yaw preserved, and the
blade's mount axis recorded as (0,0,1) — the mouse-placed-actuator/normal fix holds at
depth 4, not just at depth 1.

### C. R THROUGH THE WHOLE CYCLE
Structural parts cycle 0->90->180->270->0 and wrap; everything else toggles 0->90->0
and wraps. No part had R do nothing. BUT see finding UP1-1 below: the cycle contains
geometric duplicates for three parts.

### D/E. Esc and mis-clicks
- Esc clears the selection AND destroys the ghost; a click afterwards places nothing.
- Pointer over the palette panel: ghost hidden, click places nothing.
- Pointer on empty space: ghost invalid, reason "Aim at the robot to attach", click
  places nothing.
- Click on an invalid ghost (wheel on the core roof): places nothing.
- Two rapid clicks on one face: two brackets, the second snapping onto the first —
  correct, not a double-fire.

### F. A COMPLETE ROBOT, BUILT START TO FINISH BY POINTER
11 pointer placements, 0 failures: 2 chassis, 4 wheels, battery, engine, pivot,
beam arm, blade tip.  `Validate() == null`.  `LimbReport()` -> one limb, 2 parts,
`blockedBy == null`, inertia 17.79, tipR 1.060, rateMax 9.13, 2.12 kJ/swing.
Snapshot saved to `Assets/Phase1/qa_up1_pointerbuilt.txt`. Every function measurement
below is run on THIS pointer-built machine or a controlled variant of it.

## 03. PART FUNCTION — measured, not asserted  (Assets/Phase1/qa_up1_func.txt)
Harness `FuncProbe.cs`. Fixture is the pointer-built skeleton (2 chassis, 4 wheels,
battery, engine) with one variable bolted on. Every number below is sampled once per
FixedUpdate (50 Hz) for the stated window, timeScale 1.

### FINDING UP1-A (CRITICAL): the player's weapons are DEAD in TEST-DRIVE mode
`BuilderManager.StartFight()` line ~1871 does
    `foreach (var act in testRobot.GetComponentsInChildren<Actuator>(true)) act.playerControlled = true;`
`BuilderManager.StartTest()` (line ~1526) has NO such loop. `Actuator.Fire()` is
    `return playerControlled ? Phase0Input.FireHeld() : aiFire;`
so in Test mode every player actuator reads `aiFire`, which nothing ever writes.

MEASURED, HAMMER build (pivot + beam + blade), SPACE held for 9 s in Test mode:
    maxRate 0.00  maxTravel 0.000  cycles 0  phase Ready  energy used 0.0 kJ
Live probe of the scene in Test mode: `actuators found 1, playerControlled was FALSE on 1/1`.
Same build, same mode, after setting the flag by hand:
    maxRate 9.13 rad/s  maxTravel 2.618 rad (= the full 150 deg limit)  15 cycles / 12 s
    energy used 10.0 kJ, chassis yawed -31 deg from reaction torque.
Test drive is the mode whose entire purpose is "does my build work before I fight".
A player who builds a hammer, presses T and holds SPACE sees a robot that does nothing,
with no message. Spinners are unaffected (SpinnerWeapon has no such gate).

### PIVOT — does it SWING?  YES
15 cycles in 12 s (~0.80 s/cycle). Phase ring is clean and repeats exactly:
Ready>Winding>Driving>Returning>Recovering>Ready. travel reaches the 2.618 rad limit
every cycle and spring-returns to 0. maxRate 9.13 rad/s == its computed ceiling
(tipSpeedCap 9.7 / tipRadius 1.060 = 9.15). Reaction torque is real: -31 deg of chassis yaw.

### SPINDLE — does it SPIN?  YES
Identical geometry, spindle instead of pivot. maxRate 9.13 rad/s (same ceiling),
travel 108.7 rad in 12 s = 17.3 revolutions, one entry into Driving and it stays there
while fire is held. Chassis yawed -52 deg (reaction). 4.0 kJ drawn.

### FINDING UP1-B (MAJOR): the RAM reaches 62% of its advertised stroke at any normal mount height
`ram` reads "Linear piston, 0.45 m stroke."  MEASURED, ram+spike bolted to the aft
chassis at core height (build y 0.700 — the height the project's own "proven skeleton"
uses for everything):
    maxTravel 0.280 m  vs  RAM_STROKE_M 0.450 m, on every one of 18 cycles.
Diagnostic: raising `Actuator.RAM_STROKE_M` to 1.0 at runtime left maxTravel at exactly
0.280 — so the stroke is NOT ending at the limit, it is being cut by
`Actuator.GroundBlocked()`.
Control: the same ram+spike raised 0.45 m on two brackets -> **maxTravel 0.450, the full
stroke**, 14 cycles.  Root cause measured below (UP1-C).
Side effect at the low mount: the scraping spike shoves the machine 0.92 m across the
floor in 12 s of firing (vs 0.16 m for the high mount) — a weapon that pushes you around.

### FINDING UP1-C (MAJOR): build-space Y is NOT height above the deck, and the builder's
### floor-clip rule is checked ~0.47 m below where the floor actually is
Measured mapping, test-mode spawn of the pointer-built skeleton:
    root=(0.00, 0.180, -3.87)   arena deck y = 0.00
    core       build y 0.700 -> world y 0.180
    battery    build y 0.975 -> world y 0.455
    a spike at build y 0.700 -> world y 0.145, i.e. its underside is 0.035 m off the floor
The spawn re-bases the build so the wheel bottoms (build y 0.700 - 0.18 = 0.520) sit on
the deck. The builder's only floor test is, in build space,
    `else if (pos.y - newHalf.y < 0.05f) ghostReason = "Too low — would clip the floor";`
Build y 0.05 maps to WORLD y 0.05 - 0.52 = **-0.47 m**. So the builder will happily accept
parts up to ~0.47 m below the deck and say nothing; they arrive in the arena buried or
scraping. This is newly reachable territory: the underside of a part only became
clickable last round, so nothing has ever built downward before.

### FINDING UP1-D (CRITICAL): one legal pointer click beaches the machine — wheels off the
### ground, cannot move, `Validate()` says OK
Driven entirely through the pointer path (camera orbited to pitch -50 to see the underside,
`Phase0Input.debugMousePos` projected through `bm.TestCam`, `DebugClick()`):
    aimed at the core underside (0.000, 0.547, 0.000)
    ghost: target=core normal=(0.00,-1.00,0.00) **valid=True reason="" ghostPos=(0.000,0.450,0.000)**
i.e. the builder shows a GREEN ghost and accepts a bracket under the core. `Validate()` -> OK.

In the arena the spawn re-bases on the LOWEST part, so the bracket becomes the foot:
    without the bracket: root y 0.180, core world y 0.180
    with the bracket:    root y 0.350, core world y 0.350, bracket world y 0.100 (bottom = 0.000)
The machine is now standing on a 0.20 m cube and all four wheels hang 0.17 m in the air.

MEASURED, identical build, identical throttle (full, 6 s):
    pointer-built machine            disp **10.08 m**, maxSpeed **10.17 m/s**
    + one bracket under the core     disp **0.00 m**,  maxSpeed **0.00 m/s**   (still drawing 6.5 kJ)
One click the builder calls legal turns a working robot into a statue, silently. The
existing "Too low — would clip the floor" check cannot catch it because it is measured
0.47 m below the real deck (UP1-C). The builder has no concept of "this part is below the
wheel line", which is the plane that actually matters.

### WHEEL — does it DRIVE?  YES (10.08 m in 6 s, peak 10.17 m/s) — see above.

### WHEEL — does it STEER?  YES
Full throttle + full right steer, 6 s: travelled 11.38 m while the hull turned 54 deg.
(Observation, not yet a finding: the signed turn came out -54 deg about world +Y with
`debugSteer = +1` ("D"/right). Whether that reads as a right turn on screen depends on the
build's driveDir vs the hull's local +Z; worth one explicit screen check.)

### GYRO — does it RIGHT a flip?  YES, and by a large margin
Same skeleton, put on its back (rotated 180 deg about Z, dropped from +0.45 m), then timed
until `dot(up, worldUp) > 0.7`:
    NO gyro   -> recovered at **1.72 s**  (liveGyros 0)
    ONE gyro  -> recovered at **0.44 s**  (liveGyros 1)     = 3.9x faster
The gyro build also stayed put while righting (0.32 m of scrabble vs 0.59 m, 10 deg of
yaw drift vs 44 deg).

### ENGINE — does it raise peakKW and Actuator.MotorKW?  YES, linearly
Same hammer build, engine count varied:
    0 engines -> Actuator.motorKW **1.00**, PowerPlant.peakKW **8.0**,  capacity 240 kJ
    1 engine  -> Actuator.motorKW **2.50**, PowerPlant.peakKW **22.0**, capacity 300 kJ
    2 engines -> Actuator.motorKW **4.00**, PowerPlant.peakKW **36.0**, capacity 360 kJ
Matches MOTOR_KW_BASE 1.0 + 1.5/engine and 8 kW battery + 14 kW/engine exactly.

### BATTERY — does it raise capacityKJ, and does the bot run longer?  YES
    with battery: capacity **300 kJ** (240 battery + 60 engine reserve)
    no battery:   capacity **60 kJ**  = 5.0x less
Full-throttle drain measured at 5.2-6.3 kJ per 6 s (~0.9-1.05 kW), so continuous driving
endurance is ~69 s without the battery against ~285-345 s with it. Top speed is unchanged
(10.17 m/s both ways) — the battery buys duration, not performance, which is the intent.

### SPINNER / SPINNERSAW — do they SPIN?  YES, both to the cap
    spinner    maxOmega **94.25 rad/s** (= MAX_RPM 900), 9.2 kJ to spin up
    spinnerSaw maxOmega **94.25 rad/s** (= the same cap),  18.8 kJ to spin up
The saw costs 2.0x the energy for the same omega (heavier/wider rim) and carries it at a
larger radius, so its tip speed is higher. Bite is measured in the fight section below.

## 04. FIGHT TESTS — do the edges deal DIFFERENT damage?  (Assets/Phase1/qa_up1_fight.txt)
Method: one pivot-hammer chassis (skeleton + pivot on the aft roof + beam arm), the ONLY
variable being the tip bolted to the arm's end. 4 matches each, "charge" policy, vs MAULER
(Veteran), timeScale 3, debugFire held. Regime stamp b8432015.

| tip    | edgeHardness | W/L/D | limb dmg per run          | mean | best single hit | enemy flipped (mean) |
|--------|--------------|-------|---------------------------|------|-----------------|----------------------|
| none   | –            | 4/0/0 | 241, 460, 492, 551        | 436  | 51.2            | 1.20 s               |
| blade  | 1.70         | 3/0/1 | 705, 428,  40, 341        | 379  | 91.5            | 0.08 s               |
| wedge  | 1.15         | 4/0/0 | 648, 254, 464, 306        | 418  | 125.9           | **2.03 s**           |
| hook   | 1.30         | 4/0/0 | 916, 606, 297, 164        | 496  | **140.8**       | 2.40 s               |
| spike  | 1.20         | 4/0/0 | 825, 302, 630, 576        | 583  | 89.0            | 0.53 s               |

WHAT WORKS: every tip deals damage; every build wins; the wedge's identity is real and
measurable — 2.03 s of enemy flipped time against the blade's 0.08 s, a 25x difference on
the verb the wedge exists for. The hook produced the single hardest hit in the whole
dataset (140.8) and 9.3 s of enemy flip time in one match.

### FINDING UP1-E (MODERATE): the BLADE, "the hardest edge in the catalog", is the worst tip
`blade` has the catalog's top edgeHardness (1.70) and its description says so. Measured, it
is the ONLY tip that failed to win all four (3W/1D), has the lowest mean limb damage of any
TIP (379), and is beaten on mean damage by a BARE BEAM ARM WITH NO TIP AT ALL (436).
Peak-hit ordering does not follow edgeHardness either:
    hook 1.30 -> 140.8  >  wedge 1.15 -> 125.9  >  blade 1.70 -> 91.5  >  spike 1.20 -> 89.0
The blade is 11.8 kg against the wedge's 82 kg and the spike's 57 kg, so limb inertia and
tip radius are dominating edgeHardness completely — a 1.7x edge multiplier on a limb that
carries a seventh of the energy is a losing trade. The player is told the opposite.
CAVEAT, stated honestly: n=4 per tip and the sweeps self-report NOISY (length spread
1.7-5.7x). The per-run damage ranges overlap. The ranking of MEANS is not yet significant;
what IS solid is (a) the blade never leads on any measure, and (b) the peak-hit order is
inverted against edgeHardness. Round 2 should re-run this at n>=10 before tuning.

### COVERAGE GAP: "hook should PULL" is not measurable with anything that exists
There is no instrumentation for relative approach velocity or for the enemy being dragged
toward the player, so the hook's stated identity cannot be confirmed or denied. It shows up
only as generic damage. The same is true of the wedge's lift except by the indirect
`flippedTime` proxy used above.

### FIGHT ACROSS OPPONENTS AND TIERS
| opponent | tier | policy | result | notes |
|----------|------|--------|--------|-------|
| MAULER   | Veteran  | charge | 19W / 0L / 1D over 20 matches (5 tip variants x 4) | |
| BULWARK  | Champion | charge | **3W / 0L / 0D**, CONVERGED, spread 1.36x | dealt 731-1027, took 129-219 |
| RIPPER   | Veteran (actuated) | hitrun | **3W / 0L / 0D** | the AI's own pivot did fire — player still took only 243-356 |

### FINDING UP1-F (MODERATE): the tier ladder is flat — an entry-level pointer build
### beats the Champion without losing a match
26 matches this round: **21 W, 1 D, 0 L**. The machine is the one UP1Sweep built by clicking
(2 chassis, 4 wheels, battery, engine, and a pivot+beam+blade hammer) — no material tuning,
no armour, the first thing a player would assemble. It beat the Rookie-through-Champion
range without a single loss, and against BULWARK (Champion) the sweep self-reported
CONVERGED, so that is not variance. The tiers `EnemyRoster.DecisionInterval /
SteerAggr / OpeningThrottle / Aggression` differentiate is not producing a difficulty curve
the player can feel.

## 05. VISUAL CHECK  (Unity_SceneView_CaptureMultiAngleSceneView, fresh render)
Two captures taken, non-robot renderers disabled first so the machines fill the panes.
1. BELLY build in the arena — FRONT and RIGHT panes show the bracket protruding below the
   chassis as the machine's lowest point, with all four wheels visibly clear of the floor.
   This is UP1-D seen rather than inferred.
2. Owen's restored build on the build turntable — reads correctly: legible parts, correct
   materials, drive-direction arrow present, nothing floating or interpenetrating.
Note on the tool: the ISO pane never reframes and renders the machine as a distant speck;
FRONT / TOP / RIGHT are the usable panes.

## 06. WHAT I COULD NOT TEST (hand these to round 2)
1. **Right-click (remove) and middle-click (repaint) have no synthetic seam.**
   `Phase0Input.DebugClick()` drives button 0 only. The two DESTRUCTIVE mouse verbs in the
   builder are still unreachable from any automated test — precisely the blind spot that
   hid the blade bug for five rounds. Add `DebugClick(int button)`.
2. **Test mode cannot fire actuators at all** (UP1-A), so nothing about weapon behaviour can
   be measured in the mode built for measuring it; every number above needed the flag
   patched by hand or a full fight.
3. **"hook should PULL"** — no instrumentation exists for it.
4. **Steering sign** — the hull turned 54 deg on full right steer, but whether that reads as
   RIGHT on screen was not confirmed.

## 07. SUMMARY
Pointer path: 135 checks, 0 failures. The builder itself is in good shape — part-onto-part,
4-deep stacks, R, Esc, mis-clicks and a complete robot built by clicking all behave.
The bugs this round are NOT in the ghost pipeline; they are in what happens to a legal
build afterwards: a whole mode where weapons are dead, a floor rule measured half a metre
below the floor, a legal click that beaches the machine, a ram that never reaches its
stroke, and a flagship edge that loses to no edge at all.

## 08. FINDING UP1-G (MINOR): R has a dead press on beam, beamlong and plate
Measured in section C (`TestGhostMeshSize()` over the full cycle):
    beam      yaw 0->90->180->270->0, 4 states, but only **3 distinct meshes**
    beamlong  4 states, **3 distinct meshes**
    plate     4 states, **3 distinct meshes**
    chassis   4 states, 4 distinct meshes  (correct)
Cause is in `PlacedPart.Half()`: yaw 270 maps (x,y,z)->(y,x,z), which for a beam
(0.20, 0.20, 0.60) returns (0.20, 0.20, 0.60) — identical to yaw 0. Same for the plate at
yaw 90, where (x,y,z)->(z,y,x) on (0.50,0.06,0.50) is a no-op. So on the catalog's two most
used structural parts, one press in four visibly does nothing, which reads as R being
broken. Only `chassis` uses all four states.
(Non-structural parts correctly toggle 0/90 and wrap; mount-oriented parts — wheels,
spinners, spike — are oriented by their mount face and R is legitimately inert on them.)

## 09. ARTEFACTS
- `Assets/Phase1/qa_up1_critic.md`        this report
- `Assets/Phase1/qa_up1_sweep.txt`        pointer-path sweep, 135 checks
- `Assets/Phase1/qa_up1_func.txt`         per-part function measurements
- `Assets/Phase1/qa_up1_fight.txt`        26 matches, 7 sweeps
- `Assets/Phase1/qa_up1_pointerbuilt.txt` the robot built entirely by pointer
- `Assets/Phase1/up1/*.txt`               the controlled build variants (BASE, HAMMER, DRUM,
                                          RAMBOT, RAM_HIGH, BELLY, T_*, NOENG/NOBAT/2ENG)
- `Assets/Phase1/Scripts/UP1Sweep.cs`     pointer-path harness (new)
- `Assets/Phase1/Scripts/FuncProbe.cs`    per-part function harness (new)

## 10. EDITOR LEFT
owen's build restored (16 parts, `Validate()` OK), Build mode, timeScale 1,
debugFire=false, debugPointer=false, debugThrottle/Steer 0, opponentId=mauler,
`Actuator.RAM_STROKE_M` restored to 0.45 after the diagnostic, all harness GameObjects
destroyed, room renderers re-enabled, **0 console errors**.
qa_owen_build_SAFE.txt untouched: 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b.
