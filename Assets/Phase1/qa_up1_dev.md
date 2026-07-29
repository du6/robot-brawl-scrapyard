# ROUND-UP1 IMPLEMENTATION ENGINEER (round 1 of 3)

## Brief
Fix what the round-1 critic found, verify by MEASUREMENT (before/after),
and prove every builder claim through the POINTER path, not LoadSnapshot.

## Code read before changing anything
- `BuilderManager.StartTest()` ~1488 vs `StartFight()` ~1847
- `BuilderManager.UpdateGhost()` floor rule, line 1211
- `BuilderManager.SpawnBot()` ~1662 (minY / spawnY re-basing)
- `BuilderManager.UpdateBuild()` ~585 (R cycle) and ~613-633 (mouse buttons)
- `BuilderManager.PlacedPart.Half()` ~42-77 (yaw mapping)
- `Actuator.GroundBlocked()` ~1087, `GROUND_CLEAR` ~483, drive loop ~875-930
- `Phase0Input` (whole file) - the debug seam and BOTH backend branches

## Measured facts established before touching anything
- Console: 0 errors at start.
- `ProjectSettings/ProjectSettings.asset` -> `activeInputHandler: 1`
  = **Input System Package (New)**. So the live branch of `Phase0Input` is
  `#elif ENABLE_INPUT_SYSTEM`, NOT the legacy one.

## BEFORE run (unpatched code) - Assets/Phase1/qa_up1_dev_before.txt
All three critic findings reproduced independently:
- A  TEST maxRate **0.00** rad/s, travel 0.000, **0 cycles**, playerControlled 0/1
    FIGHT (control) maxRate **14.00** rad/s, travel 2.618, **14 cycles**, 1/1
- B  pointer at core underside: target=core normal=(0,-1,0) **valid=True** reason=""
    ghost bottom **0.350** vs deck **0.520**
    baseline drive **10.11 m** / 6 s  ->  with the belly bracket **0.00 m**
- C  raised-body positive control: a bracket at bottom 0.550 is legal (good),
    a second one at bottom 0.350 was ALSO accepted (the bug)
- D  beam / beamlong / plate: 4 distinct yaws but only **3** distinct meshes
    chassis: 4 and 4 (unaffected)

## Patch (ONE batch, one reload) - backups *.up1.bak
- FIX A  BuilderManager.StartTest: set act.playerControlled, as StartFight does
- FIX B  BuilderManager: new FloorPlane(isWheel) - the floor rule is now the
         build's own wheel-bottom plane, not build-space y=0.05
- FIX C  Phase0Input (new-input branch): MouseDown(2) could never be true.
         Middle-click REPAINT has been dead in the shipping build.
- FIX D  Phase0Input: DebugClick(int button) - the seam now covers all 3 buttons
- FIX E  BuilderManager: R skips geometrically identical yaw states

## AFTER run - Assets/Phase1/qa_up1_dev_after.txt (12 checks)
- A  TEST **14.00 rad/s**, travel **2.618** (= the full 150 deg arc), **14 cycles**,
     playerControlled 1/1 - identical to FIGHT. Fixed.
- B  pointer at core underside now: valid=**False** reason="Too low - would clip
     the floor". Fixed.
- C  positive control both ways: bracket at bottom 0.550 still ACCEPTED,
     second bracket at bottom 0.350 REFUSED. Not over-strict.
- E  middle-click REPAINT Aluminum -> Titanium: works. Right-click REMOVE
     9 -> 8 parts: works. Right-click on the CORE: 8 -> 8, still protected.

## Regression guard - Assets/Phase1/qa_up1_sweep_afterfix.txt
Re-ran the critic's own UP1Sweep: **135 checks, 5 failures**, ALL FIVE in the
R-cycle section and all traceable to FIX E:
- beam / beamlong / plate / engine "CYCLE-DOES-NOT-WRAP" - the sweep asserts
  state[4]==state[0]; those cycles are now 3 long by design. Expected.
- bracket "ONLY-1-STATES" - a REAL regression I introduced: a bracket is a
  0.20 m cube, so no yaw differs and my loop left the yaw alone. Fixed with a
  fallback to the plain +90 when nothing differs.
Everything else - all 18 parts x 5 faces of a beam, the 4-deep stack, Esc,
panel clicks, empty-space clicks, invalid-ghost clicks, and the full
pointer-built robot - still passes unchanged.

## Pass 2 - Assets/Phase1/qa_up1_dev2.txt
R with the RIGHT metric (per-press, does the DRAWN mesh change, normalised
start): beam / beamlong / plate / chassis / engine **0 dead presses out of 6**.
bracket and battery are the same shape at every yaw and are reported as such
rather than failed.

## Part FUNCTION, measured in TEST DRIVE (only possible at all after FIX A)
| part | number |
|---|---|
| pivot | SWINGS: maxRate **14.00 rad/s** (= its computed ceiling), travel reaches the full **2.618 rad / 150 deg** arc every cycle, **19 cycles in 12 s** |
| spindle | SPINS: maxRate **27.17 rad/s**, **322.6 rad = 51.3 revolutions in 12 s**, stays in Driving while held |
| ram | EXTENDS to **0.450 m = 100%** of RAM_STROKE_M, on all 16 cycles, at BOTH 0.030 m and 0.230 m belly clearance |
| wheel (drive) | **10.11 m in 6 s**, peak **10.40 m/s** |
| wheel (steer) | hull yaw **0.0 -> 22.9 -> 99.9 deg**, angVelY **0.267 -> 3.599 rad/s** (sampled at timeScale 0.15) |
| gyro | RIGHTS a flip: **1.70 s** with no gyro vs **0.30 s** with one = **5.7x** |
| engine | peakKW **8.0 / 22.0 / 36.0** and MotorKW **1.00 / 2.50 / 4.00** for 0 / 1 / 2 engines |
| battery | capacityKJ **300 -> 540** for 1 -> 2 batteries; peakKW 22.0 -> 30.0 |
| spinner | **94.25 rad/s (900 RPM cap)** for 2.9 kJ stored |
| spinnerSaw | same **94.25 rad/s** cap for **5.9 kJ = 2.0x** the spinner |

Two of these disagree with the round-1 critic and the disagreement is the point:
- **RAM.** The critic reported 0.280 / 0.450 = 62% at core mount height. I could
  NOT reproduce it: ram+spike on the aft chassis at core height (belly
  clearance 0.030 m, spike bottom 0.070 m clear) reached the full 0.450 m on
  every one of 16 cycles, and so did the same rig raised to 0.230 m clearance.
  So the truncation is NOT "the ram only ever reaches 62%" - it is
  configuration-specific, and it is GroundBlocked doing exactly its job on one
  particular low-slung limb. See the decline below.
- **STEER.** My first attempt measured start-vs-end yaw over 6 s and reported
  "0 deg, NO YAW RESPONSE". That was my metric, not the game: this machine
  peaks at 10.4 m/s in a 14 m box, so it spent most of those 6 s against the
  walls and happened to end near its starting heading. Re-measured in slow
  motion it steers hard. Recording the wrong call and its correction here on
  purpose - it is exactly the failure mode the brief warns about.

## Fight-path regression guard - Assets/Phase1/qa_up1_matches.txt
6 matches with the pointer-built hammer vs MAULER: **W3 / L2 / D1**, all ran to
the 90 s judges' decision, no errors. None of this round's fixes touch fight
code; this is a guard, not a balance sweep. Self-reported NOISY at n=6.

## DECLINED, with reasons
1. **Ram 62% stroke (critic major).** Declined: I could not reproduce it (above),
   so I have no controlled before/after to verify a fix against, and the code
   it would change - GroundBlocked / GROUND_CLEAR - carries the hard-won fix
   that stopped an arm posing through the floor and levering the player's own
   machine over. Loosening it on one unreproduced data point is exactly the
   trade this project has been burned by. Needs the critic's exact fixture.
2. **Blade damage (critic moderate).** Declined: the critic's own sweep
   self-reported NOISY and it says "re-run at n>=10 first". Balance tuning on
   n=4 is guessing.
3. **Flat tier ladder (critic moderate).** Declined: this is roster/content
   design (Champion recipes need more structure, power and wheels), not a
   defect, and it is too big to do properly alongside two criticals.

## NEW finding for round 2 - the match sweeps never fire the player's weapon
`RepeatHarness` has no fire policy: it sets aiThrottle/aiSteer per policy and
NEVER touches `Phase0Input.debugFire`. Its own header line prints
`debugFire False`. MEASURED consequence in all 6 of my matches:
`limb 0/0 (max hit 0.0)` - every point of the player's 41-575 damage came from
ramming, none from the hammer that build exists to swing. Combined with FIX A
(weapons were dead in test drive too), the player's weapon has been
systematically absent from this project's automated evidence. Not fixed here:
adding a fire policy changes what every historical sweep number means, and that
should be a deliberate decision with its own re-baseline, not a drive-by.

## Editor restored
mode=Build, parts=16, Validate()=null, timeScale=1, debugFire=False,
debugPointer=False, selected=-1, **0 console errors**, all scene renderers
re-enabled, every harness GameObject destroyed.
`qa_owen_build_SAFE.txt` untouched: 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b.
Backups: BuilderManager.cs.up1.bak, BuilderManager.cs.up1b.bak,
UP1Dev.cs.up1.bak, ../Phase0/Scripts/Phase0Input.cs.up1.bak
