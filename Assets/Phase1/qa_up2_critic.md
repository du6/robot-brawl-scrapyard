# QA Round 2 (User-Path Critic) - 2026-07-27
Status: STARTED. Streaming as I go.

## Plan
1. Pointer sweep beyond round 1: part-onto-part, deep stacks, R at every yaw, Esc, misclicks, panel clicks, full robot by mouse alone.
2. Prove FUNCTION of every part with a number.
3. Fight test.
4. Visual capture.

## Harnesses written (round 2)
- Assets/Phase1/Scripts/UP2Path.cs  - pointer sweep on NEW ground: actuator mount axis
  per face, R full cycle -> placed yaw, credit budget in the ghost, Esc/panel/empty/
  right-click/middle-click, floor-rule edges, complete weaponised robot by pointer only.
- Assets/Phase1/Scripts/UP2Func.cs  - part FUNCTION with numbers: actuator rate/travel/
  cycles, weapon bite vs a parked ram dummy (lift + pull measured separately), wheel
  drive/steer, gyro righting with & without, engine peakKW, battery capacity + endurance.

## Code read before measuring (findings to verify)
- BuilderManager.AddPart and UpdateGhost contain NO credit-budget check. Validate() is
  the only gate and it only runs at Test/Fight. Suspect: a player can click parts in
  past 4000 cr with no ghost refusal. TO MEASURE in UP2Path section C.
- `oriented = NeedsAxis(def)` (round-1 fix) is now true for blade/wedge/hook/pivot/
  spindle/ram, so they record a mount normal. But PlacedPart.Half() only orients by
  wheelAxis for spike / Mobility / spinner*. Blade/wedge/hook are sized by YAW only,
  while SpawnBot bakes their mount normal into the arena part id (AxisCode). Suspect a
  builder-vs-arena orientation mismatch. TO MEASURE.

## PASS 1 RESULTS (Assets/Phase1/qa_up2_path.txt, qa_up2_func.txt)
Pointer sweep: 25 checks, 3 failures. HEADLINE PASS: a complete 11-part weaponised
robot (core, 2 chassis, 4 wheels, battery, engine, pivot, blade) was built START TO
FINISH BY POINTER ALONE from a bare core - Validate()=null, cost 714 cr, limb parts=1
blockedBy=null tipR=0.250 rateMax=14.00. Saved to Assets/Phase1/qa_up2_pointerbot.txt.
Also passing by pointer: Esc clears ghost+selection; a click in the palette panel
places nothing; a click on empty space places nothing; a pointer-placed part takes the
active material (Tungsten); the underside of a part IS reachable (round-1 camera fix
holds); a front wedge places at bottom y=0.640 while a plate hung under the aft chassis
is refused with "Too low - would clip the floor" at ghostBottom 0.490 vs deck 0.520.
Two of the three failures were MY fixture's fault (Deselect() before a right/middle
click stops the ghost raycast, so the hit could not be verified) - re-running verified.
Function pass 1: wheel DRIVE 10.11 m in 5 s peak 10.40 m/s; STEER 234.1 deg cumulative,
peak yaw rate 4.748 rad/s; GYRO rights a flip in 0.58 s with vs 1.78 s without = 3.1x;
engines 0/1/2 -> peakKW 8.00 / 22.00 / 36.00 and capacityKJ 240 / 300 / 360;
Actuator.MotorKW(0/1/2/3) = 1.00 / 2.50 / 4.00 / 5.50 kW; ram EXTENDS 0.450/0.450 m =
100% of stroke on 13 cycles in 10 s (I therefore CONFIRM round-1's inability to
reproduce the earlier 62%-stroke claim).
Six function fixtures were refused as "Structure has floating parts" - my hand-authored
snapshot coordinates, off by 3-12 cm. Re-assembling those rigs BY POINTER instead.

## CONFIRMED FINDINGS

### C1 (CRITICAL) All six Phase-4 parts are DRAWN IN THE WRONG ORIENTATION in the builder
BuilderManager.AddPart calls `PartVisualFactory.BuildPart(def.id, ...)` with the BARE id.
Every Phase-4 visual resolves its orientation with `ParseAxis(id, "<prefix>", fallback)`
(PartVisualFactory.cs:125-130), which needs an axis code appended to the id. With a bare
id ParseAxis always returns the FALLBACK, so the build screen draws
  pivot & spindle hinged about +X, ram along +Z, blade/wedge/hook pointing +Z
no matter which face you bolted them to. SpawnBot DOES append the code
(`sid = p.def.id + AxisCode(p.wheelAxis)`, BuilderManager.cs:1777), so the ARENA draws
them correctly - the two disagree. The ghost has the same defect: UpdateGhost sets
`ghost.transform.rotation = Quaternion.identity` for everything except wheels, spinners
and spikes.
MEASURED, builder's own rendered bounds, same part on two different mount faces:
  hook  on -Z: box (0.160,0.200,0.300)  drawn (0.112,0.246,0.236)
  hook  on +Y: box (0.160,0.200,0.300)  drawn (0.112,0.246,0.236)   <- IDENTICAL drawing
  spike on -Z: box (0.220,0.220,0.300)  drawn (0.220,0.220,0.303)
  spike on +Y: box (0.220,0.300,0.220)  drawn (0.220,0.303,0.220)   <- correctly rotates
  wedge on -Z: box (0.500,0.350,0.120)  drawn (0.500,0.149,0.142)   <- box and mesh differ
spike is in the isSpike branch and rotates; the six Phase-4 parts are not, and do not.
This is the SAME root cause as round 1's headline blade bug, one layer up: round 1 fixed
the ghost's placement axis and left the ghost's and the placed part's VISUAL id alone.
FIX DIRECTION: pass the axis-coded id to BuildPart in AddPart (and to the ghost builder),
reusing SpawnBot's AxisCode so the builder and the arena derive orientation from one
place.

### C2 (MAJOR) Two of the armour plate's four R orientations can never be placed anywhere
MEASURED by pointer, plate on a bare core, stepping R:
  yaw   0  half (0.250,0.041,0.250)  valid=True
  yaw 180  half (0.250,0.250,0.042)  valid=False  "No socket on this face"
  yaw 270  half (0.042,0.250,0.250)  valid=False  "No socket on this face"
  yaw   0  ...back to flat
A stood-up plate always presents a 0.06 m tangent, and SocketOffsets() is empty below
SOCKET_PITCH 0.15, so a vertical plate is refused on EVERY face of EVERY part - the
rejection is geometric, not fixture-specific. The 4-state R cycle for Structural parts
exists (per the round-1 FIX E comment) precisely to offer "VERTICAL (stood-up) states:
vertical beam, WALL PLATE". You cannot build a wall of armour. Same failure class as the
unplaceable blade: R advertises an orientation the socket rule then refuses everywhere,
with no message beyond a generic one.

### C3 (MAJOR) The credit budget is not enforced anywhere in the pointer path
MEASURED: starting from a bare core I clicked in 12 Tungsten armour plates. Cost went to
21,014 cr against CREDIT_BUDGET 4,000 - 5.25x - with the ghost valid on every single
click and no refusal at any point. The build first went over budget at plate 3 and the
player got no signal for the next 10 clicks. Neither AddPart nor UpdateGhost consults
BuildCost(); Validate() is the only gate and it runs at T/F, where its budget test is
LAST, so in this build the message was "Needs at least 1 wheel" and the overspend was
never mentioned at all.

### C4 (MAJOR) The builder promises a full-power hammer that the arena delivers at 21%
MEASURED, pivot+blade assembled BY POINTER on the aft chassis' rear face, trigger held
10 s in test drive:
  BUILDER SAYS: tipR=0.250  rateMax=14.00  kJ/swing=0.068  blockedBy=null
  ARENA GIVES:  peak rate 14.00 rad/s but arc 0.560 rad of 2.618 = 21% of PIVOT_ARC_DEG,
                and 36 cycles in 10 s (it is slamming into the floor and re-cycling).
A pivot on a VERTICAL face hinges about a horizontal axis, so its arm sweeps down into
the ground: pivot centre y 0.70, tip radius 0.25 -> tip reaches y 0.45 against a deck at
0.52. The same pivot on a roof face reaches the full 2.618 rad (round-1's number, which
I reproduce). LimbReport() models inertia, tip radius, rate and energy but not ground
clearance, so the build screen cannot tell the two apart. This is very likely what the
earlier "ram reaches only 62% of stroke" report was: a mount-height artefact that
round-1 could not reproduce because it never varied the mount FACE.

### C5 (MODERATE) ghostYaw leaks across palette selections
MEASURED: stepping R on a beam left ghostYaw at 90; selecting a long beam next started
its cycle at 180, and an engine's first observed yaw was 270. Every weapon in my WYSIWYG
sweep was placed at a yaw of 180 I never asked for. Selecting a different part should
start it at yaw 0.

## CONFIRMED WORKING (numbers)
- Actuator mount axis by pointer: 15/15 reachable faces of an isolated chassis recorded
  the correct outward normal (pivot/spindle/ram x +Y/+Z/-Z/+X). Round-1's fix holds.
- Right-click removes a leaf wheel (9->8) and is refused on the core (9->9); middle-click
  repaints Aluminum -> Titanium. All three hit-verified. (My pass-1 "failures" here were
  my own fixture calling Deselect() first, which stops the ghost raycast.)
- Esc clears selection and ghost; palette-panel click and empty-space click place nothing.
- A pointer-placed part takes the active material (Tungsten).
- Undersides are reachable (round-1 camera fix holds); a plate hung under the aft chassis
  is refused at ghostBottom 0.490 vs deck 0.520; a front wedge places at bottom 0.640.
- COMPLETE 11-part weaponised robot built start-to-finish by pointer alone from a bare
  core: Validate()=null, 714 cr, limb parts=1 blockedBy=null tipR=0.250 rateMax=14.00.
  Saved as Assets/Phase1/qa_up2_pointerbot.txt.

### C6 (MAJOR) hook is mechanically identical to the blade and does not PULL
MEASURED, identical rig (pivot on the fore chassis front face, arm on the pivot, both
assembled BY POINTER), ram dummy re-parked 1.15 m ahead every 2 s, trigger held 12 s:
  weapon        dealt   lift    push    pull   builder tipR
  blade  /pivot   77.3  0.101   0.085  0.000   0.250
  hook   /pivot   77.3  0.026   0.046  0.039   0.150     <- SAME damage as blade, to 4 s.f.
  spike  /pivot  102.8  0.106   0.220  0.040   0.150
  wedge  /pivot  164.2  0.249   0.047  0.399   0.250
  blade  /spindle 292.0 0.068   0.200  0.069   0.250
So the weapons ARE differentiated overall (77 -> 292, a 3.8x spread) and the WEDGE
demonstrably LIFTS (0.249 m, 2.5-10x every other arm) and drags the target in (pull
0.399 m, 10x every other arm). But the HOOK is the odd one out: identical damage to the
blade and a pull of 0.039 m, i.e. it does not pull. Its authored identity - "a claw that
curls back toward the robot... this part pulls rather than pushes"
(PartVisualFactory.HookVis) - is not implemented in the physics.
CAVEAT, stated rather than hidden: the four "direct" (no-actuator) rows all read
dealt=0.0 because a weapon bolted flat to the chassis reaches ~0.75 m from the robot
centre and my dummy was parked at 1.15 m, so it never made contact. Those rows are
INCONCLUSIVE, not evidence that a direct-mount weapon cannot bite. The spinner DOES
spin: omega 94.2 rad/s = 900 RPM, exactly SpinnerWeapon.MAX_RPM.

## FIGHT PASS (Assets/Phase1/qa_up2_fight.txt) - the pointer-built robot, 12 matches
Roster, trigger HELD, 1 match each:
  scout(Rookie)      WIN  90s  dealt  81 taken 162   <- won on structure while taking 2x
  tipper(Rookie)     WIN  90s  dealt 203 taken  77
  mauler(Veteran)    DRAW 90s  dealt  94 taken  81
  ripper(Veteran)    LOSS 66s  dealt 200 taken 560   <- counted out, all 4 wheels torn off
  bulwark(Champion)  WIN  90s  dealt 385 taken 109   (71.8% margin)
  widowmaker(Champ)  WIN  90s  dealt 182 taken 206
### F1 (MODERATE) the tier ladder is still inverted, with fresh numbers
An 11-part 714-credit machine I clicked together in the builder beat BOTH Champions and
lost only to a Veteran. RIPPER - the one actuated opponent - is a huge outlier: it dealt
560 where no other bot exceeded 206, and ended the match by tearing off all four wheels
in 66 s. This is the same finding round 1 declined as content design; it is now the
single clearest gap in the ladder and RIPPER specifically looks mis-tiered.
### F2 (MODERATE) the judges' damage margin is unsigned, so the readout misleads
vs SCOUT the cause line reads "damage 81 vs 162 (50.1% margin) ... decided on structure
destroyed" and the player WON. vs TIPPER it reads "damage 203 vs 77 (62.0% margin)" and
the player also won. The same phrasing is used whether the player dealt double or took
double; nothing in the line says which side the margin favours.
### F3 (MODERATE, NOISY at n=4/3) holding the trigger made no measurable difference
A/B vs MAULER on the same build: fire=True dealt 94 / 176 / 84 / 107 (mean 115);
fire=False dealt 108 / 132 / 369 (mean 203). The no-fire runs dealt MORE. This is
consistent with C4 - on this build the pivot is roof-mounted so it sweeps a horizontal
arc at 1.175 m, above a low opponent, and every point of damage comes from ramming.
Round 1 reported that RepeatHarness never sets Phase0Input.debugFire and declined to
change it; I confirm the code, and add that on this archetype it would not have changed
the numbers anyway. Needs n>=10 before anyone tunes on it.

## VISUAL CHECK (Unity_SceneView_CaptureMultiAngleSceneView, non-robot renderers off)
1. Orientation fixture - one core, wedges on +X, -X and +Z, hook on +Y. In the TOP pane
   ALL THREE WEDGES TAPER THE SAME WORLD DIRECTION regardless of which face they are
   bolted to, and the two side wedges are drawn lying across their mount instead of
   projecting out of it. This is C1, visible to the naked eye.
2. Owen's restored 16-part build renders correctly from all four angles: materials
   legible, wheels the lowest feature, nothing floating or interpenetrating.
3. The ISO pane still never reframes - confirmed for the fourth time on this project.

## EDITOR RESTORED
Play mode, mode=Build, owen's 16 parts loaded from qa_owen_build_SAFE.txt, Validate()=null,
selected=-1, timeScale=1, debugFire=false, debugPointer=false, debugThrottle/Steer=0,
all 38 temporarily disabled renderers re-enabled (0 left off), 0 harness GameObjects,
0 console errors. qa_owen_build_SAFE.txt untouched: 790 bytes, md5
f4e052c2081911eeadc5502e86a63a4b.
Harness sources left on disk (nothing deleted): UP2Path.cs, UP2Path2.cs, UP2Func.cs,
UP2Func2.cs, UP2Func3.cs, UP2Fight.cs. Data: qa_up2_path.txt, qa_up2_path2.txt,
qa_up2_func.txt, qa_up2_func2.txt, qa_up2_func3.txt, qa_up2_fight.txt,
qa_up2_pointerbot.txt.

## SCORE 6.5/10
The builder now genuinely works through the mouse - I built a complete, valid, weaponised
robot from a bare core by pointer alone and then fought it six times. The round-1 fixes
hold under independent test (mount axis correct on 15/15 faces, undersides reachable,
floor rule two-sided, remove/repaint working). What pulls the score down is that the
SAME class of bug round 1 was created to kill is still present one layer up: the builder
draws all six Phase-4 parts in a fallback orientation that ignores the mount face (C1),
two of the armour plate's four R states can never be placed anywhere (C2), and the build
screen promises a hammer arc the arena delivers at 11-21% (C4).
