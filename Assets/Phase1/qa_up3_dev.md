# ROUND-3 IMPLEMENTATION - stream

## Reading first (rule 1)

Critic's CRITICAL says "Collider box and drawn mesh disagree for wedge/hook/pivot/spindle/ram".
Read the code before believing the list. def sizes (Phase1Parts.cs):

  pivot   0.30 x 0.30 x 0.30   CUBE
  spindle 0.30 x 0.30 x 0.30   CUBE
  ram     0.30 x 0.30 x 0.30   CUBE
  blade   0.50 x 0.05 x 0.12
  wedge   0.50 x 0.12 x 0.35
  hook    0.16 x 0.30 x 0.20

PartVisualFactory:
  WedgeVis / HookVis  -> f.transform.localRotation = FromToRotation(FORWARD, axis)  ROTATED FRAME
  ActuatorVis         -> f.transform.localRotation = FromToRotation(UP, axis)       ROTATED FRAME
  BladeVis            -> NO frame rotation, builds straight out of `size`

So the axis/AABB disagreement can only bite a part that is (a) drawn in a rotated
frame and (b) NOT cube-shaped. That is wedge and hook, and ONLY wedge and hook.
pivot/spindle/ram are 0.30 cubes: rotating their AABB is the identity, so their
mesh-vs-box gap is something else (the piston rod / socket pips drawn proud of
the case), not this bug. blade is already consistent by construction.
To be measured, not asserted - see section geom below.

## The fix shape

CompoundRobot.Build line 561/565 passes PartSpec.size to BOTH `box.size` AND
`PartVisualFactory.BuildPart`. The builder does the same. So there is exactly
ONE number and two consumers that interpret it differently: the collider treats
it as an AABB, the rotated-frame visuals treat it as a shape in their own local
frame. Fixing it at the call sites would mean plumbing two sizes through both
the builder and the arena. Fixing it inside the visual is one shared helper and
zero call-site changes, and it is what BladeVis already does.

  1. PlacedPart.Half() gains the axis branch (shared helper, as the critic asked)
     so the BOX turns with the mount face.
  2. WedgeVis/HookVis/ActuatorVis un-rotate the incoming AABB into their own
     frame before shaping from it, so the MESH lands back on that same AABB.
  Net: mesh AABB == collider AABB by construction, in the builder AND the arena.

## MAJOR - click seam

BuilderManager.cs:665  `if (!overPanel && clicksLive && Phase0Input.MouseDown(0) ...)`
C# short-circuits, so MouseDown is never reached when the pointer is over the
panel and TakeClick never consumes. Confirmed by reading Phase0Input:56
`TakeClick` is the only consumer. Fix: read the buttons FIRST, gate after.


---------------------------------------------------------------------------
# RESULTS

## FIX A - collider box vs drawn mesh (critic CRITICAL)

Instrument: UP3Dev section `geom`. Every placement made by POINTER onto a bare
core, hit-verified (TestGhostTarget + TestGhostNormal) before the click.
Same harness, same fixture, before and after.

BEFORE  42 checks, 26 ORIENT failures
AFTER   42 checks,  0 ORIENT failures

               BEFORE box            BEFORE mesh          AFTER box             AFTER mesh
 wedge +Y  (0.500,0.120,0.350)  (0.500,0.413,0.149)  (0.500,0.350,0.120)  (0.500,0.413,0.149)
 wedge +X  (0.500,0.120,0.350)  (0.413,0.149,0.500)  (0.350,0.120,0.500)  (0.413,0.149,0.500)
 hook  +Y  (0.160,0.300,0.200)  (0.112,0.185,0.309)  (0.160,0.200,0.300)  (0.112,0.185,0.309)

worst mesh-outside-collider, in metres, over all six faces:
 wedge  0.147 -> 0.032   and now the SAME 0.032 on every face (it was 0.032 on
                         the default face all along - the number stops depending
                         on which way you bolt it, which is the whole point)
 hook   0.054 -> 0.004   likewise uniform
 blade  0.005 -> 0.005   CONTROL, byte-identical
 spike  0.002 -> 0.002   CONTROL, byte-identical

I REFUTED 3 of the 5 parts in the critic's title. pivot/spindle/ram are 0.30 m
CUBES, so rotating their AABB is the identity: their box measured
(0.300,0.300,0.300) on all six faces BEFORE and AFTER, unchanged. Their
mesh-outside-box (0.048 / 0.048 / 0.092) is the axle boss and the piston rod,
drawn deliberately proud of the case, identical on every face in both runs.
That is an affordance, not an orientation bug, and growing the collider to
swallow it would be a physics retune.

MY OWN METRIC WAS WRONG FIRST. Its initial version compared argmax/argmin of
box and mesh, which is undefined for a cube, and reported all 18 actuator
probes as "!!MISMATCH". Rewritten to compare only axis PAIRS the box actually
distinguishes (>0.02 m apart); a cube now scores "cube-n/a". The reasoning is
kept in the source.

ARENA SIDE (CompoundRobot.Build passes the same number to BoxCollider.size AND
to the visual). Four wedges on four different faces, spawned into TEST:
  wedgeXP  collider (0.350,0.120,0.500)  mesh (0.414,0.158,0.504)  OVER 0.032
  wedgeXN  collider (0.350,0.120,0.500)  mesh (0.414,0.158,0.504)  OVER 0.032
  wedgeYP  collider (0.500,0.350,0.120)  mesh (0.500,0.413,0.163)  OVER 0.032
  wedgeZP  collider (0.500,0.120,0.350)  mesh (0.500,0.162,0.414)  OVER 0.032
One-sided (after only) and labelled as such; the before number is the builder's,
because it is literally the same number.

## FIX B - debug click seam (critic MAJOR)

Instrument: UP3Dev section `leak`, the critic's exact sequence.
BEFORE  3 of 3 repeats LEAKED - click posted over the panel placed nothing
        (correct), then moving onto the model with NO new click placed a beam.
AFTER   0 of 3. CONTROL (a click actually on the face) still places, so this
        cannot be passed by swallowing clicks.
CROSS-MODE, measured after the fix: click posted in TEST, back to Build,
        pointer parked on a valid face - nothing placed. No cross-mode leak, so
        I did not add the speculative mode-transition clear I had drafted.

## REGRESSION

UserPathTest, the brief's canonical pointer test: 126 checks, 0 failures,
0 off-camera - identical to the round-1 and round-2 baselines.
Live 90 s fight vs the roster on owen's restored build: ran the clock to a
judges' decision, outcome PlayerLoss, 0 console errors.

## FUNCTION PROOF (requirement B) - my own run, after the patch

pivot    2.618 of 2.618 rad = 100% of the 150 deg arc, 19 cycles/12 s,
         returns to 0.000, phases Winding>Driving>Returning. Idle 0.000.
spindle  peakRate 27.17 rad/s = MaxRate 27.17, 325.3 rad accumulated, never
         leaves Driving. Idle 0.000.
ram      maxTravel 0.450 m = RAM_STROKE_M 0.450 exactly, 17 cycles. Idle 0.000.
wheel    4 wheels: 10.08 m in 4 s, 9.49 m/s peak, 107 deg of steer,
         idleDrift 0.000. 2 wheels at one end: 0.03 m fwd, 0 deg steer.
gyro     165 deg roll to up.y>0.7: 1.86/1.72 s with 0 gyros, 0.42 s with 1,
         0.34 s with 2. 4.4x faster, reproducible.
engine   capacityKJ 240->300->360 (+60 each), peakKW 8->22->36 (+14 each),
         Actuator.MotorKW 1.00/2.50/4.00/5.50, cap 12.
battery  capacityKJ 300->540 (+240), peakKW +8. Nothing went flat in 40 s at
         full throttle in any rig (minFrac 0.83-0.91) - runtime still not
         demonstrable from driving alone.
spinner  94.2 rad/s (E 2912 J, I 0.6556), at MaxOmega from 0.5 s.
spinnerSaw 72.6/76.9/84.7/91.6/94.2 rad/s over 5 s (E 5854 J, I 1.3181) -
         2x the inertia, ramps slower, as the power-limited motor intends.
         These match the round-3 critic's saw ramp to 0.1 rad/s across all five
         samples, which is the evidence that spinner physics is untouched.
arms     RAM pass, dealt: hook 224.4 > blade 139.1, wedge 144.5, spike 143.6 -
         differentiated. Roof-pivot pass: only the wedge connected (77.3 dealt,
         lift 0.065). hook pull 0.022 = blade pull 0.022: STILL DOES NOT PULL.

DISCREPANCY I am flagging rather than smoothing: the spinner BITE sub-test
dealt 0.0 in my run where the round-3 critic measured 345.9 in one ram, and
arm self-damage was 0.0 for all four where the critic saw wedge 126 / hook 72.
Those are single contact events in a rig that re-parks the dummy; the spin-up
numbers agree to 0.1 rad/s, so the physics is the same and the contact is the
variance. n=1 either way. Nobody should tune on either number.

## DECLINED, with reasons

1. "pivot/spindle/ram" half of the critic's CRITICAL. REFUTED by measurement,
   not declined on judgement: their box is a 0.300 cube on all six faces before
   and after. See above.

2. Hook does not PULL (critic MODERATE, third round running). CONFIRMED again
   by my own run - hook pull 0.022 m, blade pull 0.022 m - and still declined.
   It needs a decision that is not a bug fix: either the physics gains a
   toward-attacker impulse or HookVis's authored claim ("this part pulls rather
   than pushes") is withdrawn. Those are different games. What HAS changed is
   that the hook is now demonstrably distinct (224.4 dealt vs blade 139.1), so
   the round-2 critic's "mechanically identical" is dead; what remains is a
   content decision, and dropping an impulse term into a weapon on the last
   round of three, alongside a collider change to that same weapon, is how you
   get a headline a re-test reverses.

3. Validate() passes machines that cannot move (critic MODERATE). Reproduced:
   the 2-wheels-at-one-end rig returns Validate()=null, cost 552, FIGHT
   enabled, and drives 0.03 m in 4 s at full throttle. Declined as filed.
   Reading RefreshOverlay: for that rig the grounded-wheel polygon has ZERO
   depth (both wheels at z=+0.40), so supportMargin = min(..., com.z-0.40,
   0.40-com.z) is negative for any CoM, and the panel already refuses to say
   "Ready to fight" - it says "UNSTABLE - will tip over" instead. So the
   builder does warn; what the critic wants is a BLOCK. Blocking on
   supportMargin<=0 would also refuse a two-wheel-plus-skid machine, which is a
   legitimate design, and it would refuse it at the FIGHT button with no way
   to override. That is a design call for the roster/UX owner, not a bug fix.
   Honest caveat: supportMargin is private, so the warning half of that is a
   code-read plus arithmetic, not a measurement. It would need an accessor to
   measure, which is a change I did not want to make to observe something.

4. Ghost is 0.011 m thinner than the placed part (critic MINOR). Real
   (BuildCollar, which AddPart adds and the ghost does not) and cosmetic. Not
   worth a third domain reload against the two fixes above.

5. No reach limit on a build (critic MINOR) and ISO pane never reframes
   (critic INFO). Design call and tool limitation respectively. I confirm the
   ISO observation for the seventh time: my capture had ISO wide and tiny while
   FRONT/TOP/RIGHT framed correctly.

6. RepeatHarness still never touches Phase0Input.debugFire. Third round this
   has been noted and not fixed, by three different agents including me. I
   agree with the two previous declines - flipping it silently rewrites what
   every historical sweep number means - but it should stop being deferred:
   it is now the largest known hole in this project's evidence base, and my
   FIX A has just changed wedge and hook colliders in the arena, so the
   re-baseline has to happen anyway.
