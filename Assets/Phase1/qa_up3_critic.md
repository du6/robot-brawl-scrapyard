# QA Round 3 - CRITIC report (user-path + function proof)
Started 2026-07-27. Agent: critic r3 of 3.
Rules: pointer path for builder claims; measured numbers for function claims.

## Log

## Setup
Wrote two section-dispatched harnesses, one compile for the whole round:
- Assets/Phase1/Scripts/UP3Path.cs  sections: robot | onto | stack | ryaw | misc
  Everything goes through Phase0Input.debugMousePos -> UpdateBuild -> ghost ->
  DebugClick. Nothing calls AddPart. Hit-verified (target id + face normal + the
  snap landing on the correct SIDE of the target, pos within 1.6 m).
- Assets/Phase1/Scripts/UP3Func.cs  sections: power | wheel | act | spin | arm | gyro
  Fixtures by snapshot (fast, permitted); every claim is a measured number.
Reused UserPathTest's + UP2Func3's rules and measurement shape so r2/r3 compare.
Waiting on domain reload.

## 1. POINTER PATH

### 1.1 A complete robot, start to finish, by mouse alone  [PASS]
From a bare core, 10 pointer placements (orbit -> hit-verify -> DebugClick):
2 chassis, 4 wheels, battery, engine, spinner. parts=10 wheels=4 cost=851
Validate()=OK. Snapshot gated on parts>=5 && wheels>=2 && Validate()==null and
written to Assets/Phase1/qa_up3_pointerbot.txt.
One step failed - gyro onto core +Y - reported off-camera because the battery
placed two steps earlier already occupies the core roof. My fixture, not a bug.
NOTE for later: LimbReport entries=0 on this bot (no actuator), as expected.

### 1.2 Part ONTO part - 162 pointer probes, 0 failures, 0 off-camera  [PASS]
Every sweep on this project until now attached to a CORE. This attaches all 18
placeable parts onto a CHASSIS (+X/+Y/+Z/-Y), a BEAM (+X/+Y/+Z) and a PLATE
(+Y/+X). No "ghost green, click did nothing" anywhere. All rejections are
correct rules: wheels side-only, spinners not underneath, plate side faces have
no socket. The -Y (underside) face is now genuinely reachable and accepts parts
- the round-2 camera-elevation fix holds up on non-core hosts too.
FIX C2 (round 2) is visible working: the plate reports "no-socket-self" when IT
is the part being placed edge-on, and "no-socket-target" when its 0.06 m edge is
the face being aimed at. Two different messages, correct one each time.

CAVEAT, my error not the game's: my BEAM fixture put the beam at z=0.400 with
half-z 0.300, i.e. 0.05 m INSIDE the core (flush would be z=0.450). So the
"beam/beamlong onto beam +X/+Y = blocked" cells are the core rejecting an
overlap, which is the rule working. Beam-onto-beam is covered properly by the
stack section below, which builds the chain through the ghost's own snap.

### 1.3 Multi-part stacks  [PASS]
Five beams end-to-end, each placed onto the PREVIOUS beam through the pointer:
z = 0.450 / 1.050 / 1.650 / 2.250 / 2.850, gap-to-host 0.0000 every time. The
snap does not drift with depth. Mixed chain core->chassis->beam->bracket->blade
also builds clean by pointer - note the BLADE placing onto a BRACKET, so the
round-1 blade fix is not core-specific.
Observation (design, not a defect): nothing limits reach. Six beams = 235 cr
builds a 2.85 m spar out of a machine that fights in a 14 m arena, and Validate()
only objects to the missing wheels. Budget is the only brake on extent.

### 1.4 R at every yaw, ghost vs placed  [PASS, after I disproved my own result]
Sweep result (20 probes) looked like the R cycle was stuck: beam 0,90,270,270 and
plate 0,180,0,0 for 0..3 presses. I did NOT file that. Only one code path writes
ghostYaw (BuilderManager.cs:600-642, one caller of RotateDown), so I re-measured
ONE PRESS PER EDITOR TICK, seconds apart, where nothing can merge inputs:
  press0 yaw=0   mesh 0.218 x 0.218 x 0.618   (flat along Z)
  press1 yaw=90  mesh 0.618 x 0.218 x 0.218   (flat along X)
  press2 yaw=180 mesh 0.218 x 0.618 x 0.218   (STOOD UP, valid=True)
  press3 yaw=270 mesh 0.218 x 0.218 x 0.618   (flat along Z again)
That is the documented cycle, every press changes the drawn shape, round-2 FIX E
holds. From 270 the next press goes to 90 (Half(0)==Half(270), so 0 is skipped) -
a clean 3-shape cycle. The sweep's contradicting numbers are an artifact of my
own harness firing R presses two frames apart; the game is right and I was wrong.
GHOST vs PLACED: yaw, position and mesh agree on all 20, with one systematic
difference - the placed part is exactly 0.011 m THICKER on the mount axis than
the ghost was (beam 0.218->0.229, plate 0.083->0.094, chassis 0.318->0.329).
That is BuildCollar, the seam collar, which AddPart adds and the ghost does not.
Cosmetic, consistent, worth knowing.

### 1.5 Esc / panel / empty space / all three mouse buttons
ESC while holding a beam: shown=True -> selected=-1, ghost hidden. ok
Click OVER the palette panel: ghost hidden, nothing placed. ok
Click on EMPTY SKY: ghostValid=False, target=none, "Aim at the robot to attach",
  nothing placed, no exception. ok
Click with a RED ghost (wheel on the roof): refused "Wheels attach to side faces
  only", nothing placed. ok
Two clicks two frames apart: 2 parts, no overlapping boxes. ok
MIDDLE CLICK repaint, by pointer, verified across editor ticks:
  core Steel -> Titanium after one DebugClick(2). ok
RIGHT CLICK removal, by pointer:
  on the CORE with a beam attached -> refused, parts stayed 2 (no orphaning). ok
  on the BEAM -> parts 2 -> 1, [core]. ok
(The sweep reported these three as off-camera: my Reach() verifies the aim via
the GHOST, and the ghost does not exist when nothing is selected, so it could
never confirm a hit for the two deselected verbs. Harness limitation; redone by
hand above.)

### 1.6 FINDING - the debug click seam is not faithful, exactly where it matters
UpdateBuild reads `!overPanel && clicksLive && Phase0Input.MouseDown(0) && ...`.
C# short-circuits, so when the pointer is over the panel - or during the
click-grace window after a mode change - MouseDown is NEVER CALLED, and
TakeClick therefore never consumes the pending debug click. MEASURED: clicked
over the panel (nothing placed, correct), then moved the pointer back onto the
model and issued NO further click; a part was placed anyway on a later frame.
A human is unaffected (Input.GetMouseButtonDown is frame-scoped). But this
project's whole evidence base now rests on this seam, and it means any harness
that clicks over the panel, or clicks within the grace window, can silently
place a part one frame later at a location it never clicked - a false PASS or a
phantom part in a fixture, with no error anywhere.

## 2. FINDING (CRITICAL) - round-2 FIX A turned the VISUALS but not the BOX
PlacedPart.Half() has axis-aware branches for exactly three cases: spike,
Mobility (wheels) and ids starting with "spinner". Everything else falls through
to the yaw-only branch. Round-2 FIX A made PartVisualFactory orient
pivot/spindle/ram/wedge/hook by the MOUNT NORMAL. So for those parts the drawn
mesh now turns with the face and the box does NOT.
This is not cosmetic. BuilderManager.SpawnBot line ~1985:
    Vector3 size = p.Half() * 2f;   ->  PartSpec  ->  the arena BoxCollider
while the same loop passes VisualId(def, wheelAxis) as the id, which is what the
arena visual factory orients by. Collider from the box, model from the axis.
MEASURED, box (= collider + overlap test + snap) vs rendered mesh, same part,
three mount faces off a bare core:
  wedge +X  box 0.500 x 0.120 x 0.350   mesh 0.413 x 0.149 x 0.500
  wedge +Y  box 0.500 x 0.120 x 0.350   mesh 0.500 x 0.413 x 0.149
  wedge +Z  box 0.500 x 0.120 x 0.350   mesh 0.500 x 0.149 x 0.413
  hook  +Y  box 0.160 x 0.300 x 0.200   mesh 0.112 x 0.185 x 0.309
  hook  +Z  box 0.160 x 0.300 x 0.200   mesh 0.112 x 0.309 x 0.185
  pivot +X  box 0.300 cube              mesh 0.396 x 0.318 x 0.318
  ram   +X  box 0.300 cube              mesh 0.483 x 0.318 x 0.318
Worst case, a roof-mounted wedge: drawn 0.413 m tall, collides as 0.120 m tall.
The top 0.29 m of the flipper the player can see is not there, and 0.20 m of
collider sticks out where nothing is drawn. A roof wedge is also drawn sinking
0.147 m THROUGH the part it is bolted to.
The blade is NOT affected and that is informative: BladeVis re-decorates within
the incoming AABB (it only moves the sharp lip to the mount face) instead of
re-orienting it, so blade mesh ~ box on all three faces (0.51/0.495 x 0.045 x
0.12). WedgeVis/HookVis/ActuatorVis do re-orient. So the fix is to give Half()
the same axis awareness the visuals already have - or to make the five visuals
respect the AABB the way BladeVis does. Either way the two must be derived from
one place, which is the exact lesson round 2 wrote up for FIX A.

## 3. FUNCTION PROOF (requirement B) - measured numbers

### 3.1 ACTUATORS  [ALL THREE WORK]
Rig: roof-mounted actuator on the front chassis (hinge vertical, arm horizontal,
so the floor cannot eat the arc), blade as the limb. LimbReport confirmed
parts=1 and blockedBy=null before trusting any of it. Control first: 3 s with
the trigger OFF gives peakRate 0.000 and peakTravel 0.000 on all three, so the
numbers below are the trigger and not drift.
  pivot    tipR 0.460  peakRate 14.00 rad/s (= MaxRate 14.00)
           maxTravel 2.618 rad of 2.618 = 100% of the 150 deg arc
           19 cycles in 12 s, returns to travel 0.000 between swings
           phases Winding>Driving>Returning>Recovering, repeating. SWINGS.
  spindle  tipR 0.460  peakRate 27.17 rad/s (= MaxRate 27.17, tip-speed capped)
           travel 325.3 rad accumulated, never leaves Driving. SPINS.
  ram      tipR 0.250  peakRate 3.50 (= MaxRate)
           maxTravel 0.450 m of RAM_STROKE_M 0.450 = 100%, 17 cycles,
           returns to 0.000. EXTENDS.
Note the earlier failed run is the useful control for the brief's warning: with
a floating limb the builder still reported blockedBy=null and rateMax=14.00 -
only parts=0 and tipR=0.001 gave it away. blockedBy alone is not a safety check.

### 3.2 ENGINE  [WORKS]  and  BATTERY  [WORKS on capacity; runtime not observable]
Live PowerPlant on the spawned robot, same chassis, engines/batteries varied:
  1 bat, 0 eng   capacityKJ 240.0   peakKW  8.00
  1 bat, 1 eng   capacityKJ 300.0   peakKW 22.00
  1 bat, 2 eng   capacityKJ 360.0   peakKW 36.00
  2 bat, 1 eng   capacityKJ 540.0   peakKW 30.00
So an engine is +60 kJ and +14 kW; a battery is +240 kJ and +8 kW. Distinct
roles, both real. Actuator.MotorKW(n): 1.00 / 2.50 / 4.00 / 5.50 ... 12.00 at
the cap, i.e. +1.5 kW per engine. ENGINE RAISES BOTH, as specified.
HONEST GAP on the battery's second half ("does the bot run longer"): I could not
make it matter. At FULL THROTTLE for 40 s the drive draw is only 0.79-1.56 kW
against 240-540 kJ, so minFrac never fell below 0.83 and nothing went flat in
any of the four rigs. Driving alone cannot flatten a battery inside a 90 s
match (240 kJ / 1.2 kW = 200 s). The battery's runtime value is only reachable
under weapon load, which is the case the automated sweeps still do not exercise
(see the round-2 dev's own note that RepeatHarness never sets debugFire).

### 3.3 WHEEL  [DRIVES and STEERS]
  4 wheels (0.27 m track, front and rear)  idleDrift 0.000 m
     forward 4 s: 10.08 m, peak 9.49 m/s      reverse 3 s: 12.19 m
     steer 4 s at throttle 0.3: 121 deg of heading
     (forward distance is wall-limited - the arena is 14 m and it spawns at
      z=-4 - so read the 9.49 m/s peak, not the 10.08 m.)
  2 wheels, BOTH ON THE FRONT chassis   forward 4 s: 0.03 m, peak 0.02 m/s
     reverse 3 s: 2.12 m      steer 4 s: 0 deg
     i.e. it cannot drive forward and cannot turn at all, only reverse.
  CONTROL, 2 wheels on the CORE's own side faces (mid-wheelbase): drives 10.6 m
     to the far wall. So this is wheelbase geometry - the rear chassis grounding
     out - and NOT a defect in the wheel. WHEEL WORKS.
  Worth the dev's attention anyway: Validate() only requires wheels >= 1, so the
  immobile front-only build returns OK and the FIGHT button stays lit. The
  builder does carry the right warnings for this family ("UNSTABLE - will tip
  over", "N wheel(s) can't reach the ground", "Not ready - see wheel warnings")
  but they are advisory. I could not read which one fired - the flags are
  private and reflection is blocked - so I am not claiming one did.

### 3.4 SPINNER and SPINNER SAW  [SPIN, and they BITE HARD]
Omega with the trigger held, 0.5/1/2/3/5/8 s:
  spinner     94.2 / 94.2 / 94.2 / 94.2 / 94.2 / 94.2 rad/s   (MaxOmega 94.2,
              E 2912 J, I 0.6556)
  spinnerSaw  72.6 / 76.9 / 84.7 / 91.6 / 94.2 / 94.2 rad/s   (E 5854 J,
              I 1.3181 - the heavier rim takes ~5 s to reach the same omega,
              which is the power-limited motor working as designed)
The trigger is IRRELEVANT to a disc and that is deliberate, not a bug: read
SpinnerWeapon.FixedUpdate - it gates on `robot.combatEnabled`, never on
FireHeld, with the comment "no pre-bell spin-up". Both discs were already at or
near max omega before I ever set debugFire.
BITE: my first measurement said dealt=0.0 for both and I did not file it,
because the fixture parked the dummy 1.10 m ahead where the disc reaches 0.87 m
and the dummy's near face is at 0.85 m - a 0.02 m overlap with zero closing
speed. Re-run properly, driving INTO the dummy at full throttle:
  spinner, one ram: dummy HP 377.3 -> 31.4, DEALT 345.9, and the spinner part
  SHEARED OFF the player's own machine in the process (the SpinnerWeapon
  component was gone afterwards - which is also what caused the one NRE in my
  probe, since I had not null-guarded it).
So discs work. What I cannot say from this is whether a disc biting a
STATIONARY target with no closing speed does anything - my fixture never made
real contact, so that case is untested, not disproved.

### 3.5 BLADE / WEDGE / HOOK / SPIKE  [all four differ; the hook still does not PULL]
FIRST ATTEMPT WAS INVALID AND I AM NOT FILING IT. Arms on a roof pivot, dummy
parked 1.05 m ahead: blade 0.0, wedge 77.3, hook 0.0, spike 0.0. That is a reach
artifact, not a damage result - I offset each arm by its own half-depth, so the
wedge's swept tip reaches 0.81 m and the blade's only 0.73 m against a dummy
face at 0.80 m. Three of the four never touched anything. Same fixture trap as
the spinner, twice in one round; I am writing it down rather than hiding it.
The FAIR comparison is the ram pass - arm rigid on the nose, driven into the
dummy at full throttle, so reach differences are erased by closing the distance
(n=1 per arm, deterministic - the pivot pass repeated identically 3/3):
  blade  dealt 145.8   self   0.0   lift 0.009   pull 0.022
  wedge  dealt 142.2   self 126.0   lift 0.050   pull 0.032
  hook   dealt 231.0   self  72.0   lift 0.000   pull 0.022
  spike  dealt 144.5   self   0.0   lift 0.005   pull 0.022
THE DAMAGE IS DIFFERENT, as required: the hook hits 1.6x harder than the blade
and pays 72 HP of self-damage for it; the wedge pays 126. blade and spike are
the clean, no-recoil options. This REFUTES the round-2 critic's "hook is
mechanically identical to the blade" - 231.0 vs 145.8 dealt and 72.0 vs 0.0
self is not identical, and the round-2 dev was right to decline a re-tune on
n=1 evidence.
WEDGE LIFTS: 0.050 m on the ram and 0.065 m on the pivot swing, the largest
lift of the four. Real, but 5-6 cm is a nudge, not an overturn.
HOOK DOES NOT PULL: 0.022 m, identical to the blade's 0.022 and LESS than the
wedge's 0.032. PartVisualFactory.HookVis documents "this part pulls rather than
pushes"; nothing in the physics implements a toward-attacker term. The authored
identity and the behaviour still disagree - now with a measurement that says
which part of the round-2 critic's claim was right (the pull) and which was
wrong (the damage).

### 3.6 GYRO  [WORKS - 4.3x faster self-right]
Bot rolled to 165 deg and dropped, timed until transform.up.y > 0.7. Two reps
each, identical both times (deterministic):
  0 gyros   1.80 s, 1.72 s   (liveGyros 0)
  1 gyro    0.42 s, 0.42 s   (liveGyros 1)
  2 gyros   0.34 s, 0.34 s   (liveGyros 2)
So one gyro is a 4.3x improvement and the second adds a further 19%. Note the
0-gyro machine still rights itself in ~1.8 s, so a gyro accelerates recovery
rather than enabling it - worth knowing when pricing the part.

## 4. FIGHT TEST
  POINTER-BUILT BOT (10 parts, 851 cr, spinner) vs SCOUT (Rookie):
     PlayerLoss. dealt 0, taken 196, player 80% HP, enemy 100% HP, enemy 10/11.
  OWEN'S 16-PART BUILD vs MAULER (Veteran):
     PlayerWin. dealt 350, taken 145, player 85% HP / 15 of 16 parts,
     enemy 78% HP / 7 of 11 parts. The player's spinner sheared off during the
     match (SpinnerWeapon gone by mid-fight) and it still won.
CAVEAT I want on the record: I held throttle and trigger but never STEERED, so
the pointer bot's dealt=0 is at least partly "it drove past". This is not a
verdict on the ladder. It is, though, the first fight numbers on this project
recorded with debugFire actually held - the round-2 dev flagged that every
historical sweep ran with the player's trigger off and recommended a re-baseline
as round 3's first act. That re-baseline still has not happened.

## 5. VISUAL CHECK (requirement C)
CaptureMultiAngleSceneView with non-robot renderers disabled (isolated by placed
-part transform), twice: mid-fight, and on a purpose-built wedge fixture.
- Mid-fight: both machines render as coherent robots, wheels on the ground,
  correct materials, no floating or interpenetrating parts.
- Wedge fixture (chassis roof wedge + side wedge + rear ram): the roof wedge is
  drawn as a TALL STEPPED RAMP rising well above the chassis, exactly the
  0.413 m the renderer measurement predicted, on a part the physics treats as a
  0.120 m slab. You can see the divergence from the FRONT and RIGHT panes.
- The ISO pane still never reframes - sixth independent confirmation on this
  project. Front/Top/Right do reframe and are the usable ones.

## 6. WHAT I CHECKED AND FOUND NOTHING WRONG WITH
- 162 part-onto-part pointer probes on chassis/beam/plate hosts: 0 failures.
- 5-deep and mixed pointer stacks: flush to 0.0000 m at every depth.
- R cycle and ghost-vs-placed agreement across 20 placements.
- Esc, panel clicks, sky clicks, red-ghost clicks, double clicks.
- Right-click removal incl. the orphan refusal; middle-click repaint.
- All three actuators at 100% of their rated travel; all four power configs;
  wheels; gyros; both discs.

## 7. HONEST LIST OF MY OWN ERRORS THIS ROUND
1. Beam fixture overlapped the core by 0.05 m -> false "blocked" cells.
2. Spinner bite fixture left 0.02 m of overlap with no closing speed -> a false
   "dealt=0.0" I nearly filed. Re-ran driving in: 345.9 damage.
3. Arm fixture offset each arm by its own half-depth, so three of four never
   reached the dummy -> a false "blade/hook/spike deal no damage".
4. Rapid-fire R presses in the sweep produced a yaw sequence the code cannot
   produce; single-press-per-tick disproved it. Harness artifact, not a bug.
5. Reach() verifies aim via the ghost, which needs a selection, so it could not
   verify the two deselected verbs (right/middle click). Redone by hand.
Three of these five would have been filed as game bugs by a less paranoid pass.

## 8. FINDINGS, RANKED

### F1 CRITICAL - collider box and drawn mesh disagree for wedge/hook/pivot/spindle/ram
See section 2 for the full table. PlacedPart.Half() (BuilderManager.cs:42-76)
is axis-aware only for spike, Mobility and "spinner*"; round-2 FIX A made
PartVisualFactory orient five MORE parts by the mount normal. SpawnBot then
feeds `p.Half()*2` to PartSpec (the collider) and VisualId(def, wheelAxis) to
the model, so the two disagree on every non-default face. Worst measured case:
roof wedge drawn 0.413 m tall, collides as 0.120 m. Fix by giving Half() the
same axis branch the visuals use, from one shared helper - or by making
WedgeVis/HookVis/ActuatorVis respect the incoming AABB the way BladeVis already
does. Do not fix only one side again.

### F2 MAJOR - the debug click seam is not faithful, and the project's evidence rests on it
UpdateBuild:663 `!overPanel && clicksLive && Phase0Input.MouseDown(0) && ...`
short-circuits, so a pending debug click is not consumed when the pointer is
over the panel or during click-grace, and fires on a later frame at a place the
test never clicked. MEASURED (section 1.6). Humans unaffected. Every future
harness IS affected. Suggest evaluating MouseDown FIRST and gating after, or an
explicit Phase0Input.ConsumeClick() on the ignored paths.

### F3 MODERATE - the hook still does not PULL
blade pull 0.022, hook pull 0.022, wedge pull 0.032 (m, ram pass). HookVis
documents "this part pulls rather than pushes". The round-2 critic's version of
this claim ("hook is mechanically identical to the blade") is REFUTED - hook
deals 231.0 vs blade 145.8 and takes 72.0 self-damage vs 0.0 - so the part IS
distinct, just not in the way it is documented to be. Either add a
toward-attacker impulse term or drop the authored claim.

### F4 MODERATE - Validate() passes machines that cannot move
Wheels >= 1 is the only mobility gate. A build with both wheels at one end
drives 0.03 m in 4 s at full throttle and turns 0 deg, and Validate() returns
null so FIGHT is enabled. The advisory warnings for this family exist
("UNSTABLE - will tip over", "N wheel(s) can't reach the ground"). Consider
promoting the support-polygon test from advisory to blocking, or at least
telling the player the machine cannot drive.

### F5 MINOR - the ghost is 0.011 m thinner than the part it promises
The placed part gains BuildCollar on the mount axis; the ghost does not.
Consistent across all 20 R-cycle placements (beam 0.218 -> 0.229 etc).

### F6 MINOR - no reach limit on a build
Six beams (235 cr) build a 2.85 m spar on a machine that fights in a 14 m arena
and Validate() objects only to the missing wheels. Budget is the only brake.

### F7 INFO - battery runtime is unreachable from driving alone
240 kJ against a 0.79-1.56 kW drive draw is ~200 s; a match is 90 s. Nothing
went flat in any of four rigs at full throttle for 40 s. The battery's second
advertised benefit only exists under weapon load, which the automated sweeps
still do not exercise.

### F8 INFO - the ISO capture pane never reframes (6th confirmation)

## 9. EDITOR STATE ON EXIT
playing=True, mode=Build, 16 parts, 4 wheels, cost 750, Validate=OK,
timeScale=1, debugFire=False, debugPointer=False, debugThrottle=0, debugSteer=0,
selected=-1, activeMat=Aluminum, harness GameObjects=0, disabled renderers=0,
console errors=0.
qa_owen_build_SAFE.txt NEVER WRITTEN: 790 bytes, md5
f4e052c2081911eeadc5502e86a63a4b - unchanged.
New files left for the dev to re-run: Assets/Phase1/Scripts/UP3Path.cs,
UP3Func.cs (+ UP3Func.cs.geom.bak), and the pointer-built machine at
Assets/Phase1/qa_up3_pointerbot.txt. Raw logs: qa_up3_path.txt, qa_up3_func.txt.
