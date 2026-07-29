# ROUND 1 CRITIC (re-baseline + disc conversion review)
Date 2026-07-27. Focus: (a) re-baseline Phase 4 weapons with the trigger ACTUALLY HELD,
(b) first review of the disc conversion.

## Source-regime stamp
(filled below)

## Log

## Source-regime stamp (pre-RB1Probe)
SOURCE REGIME b3f9659c (41 scripts, newest 2026-07-27 15:12:21Z)
DMG_K 0.045 | HP_K 6000 | BITE_TIP_SPEED_MS 4.00 | DRAIN_FRAC 0.40 | LIN_IMP_CAP 600
MATCH_TIME 90 | CREDIT_BUDGET 4000 | MotorKW 0/1/2/3 eng = 1.0/2.5/4.0/5.5

## Editor state on arrival
NOT clean. Console was flooding with the documented post-reload
`BuilderManager.OnGUI:2754` NRE (30/30 entries errors). Applied the standard
recovery: destroyed 1 surviving BuilderManager, created a fresh one, reset
timeScale/debugFire/debugPointer. Recorded because the brief said "0 console
errors, verified before you started" and that was not true at handover.

## Instruments
- RepeatHarness (now fires) - W/L + src ledger, to KO.
- DiscProbe - rotor revs + seam peak.
- NEW: Assets/Phase1/Scripts/RB1Probe.cs, two sections:
  * "wire" - loads a fixture, prints the BUILDER's LimbReport quote AND the
    ARENA's Actuator wiring side by side, plus a 6 s live spin sample that
    measures whether the disc turns IN ITS OWN PLANE (axisAlignToOwnFace)
    rather than tumbling. Fixture verification before any damage number.
  * "bite" - FIXED-DURATION charge match, per-source damage ledger
    (ram N/hits, limb N/hits, per-bite). Fixed duration on purpose: the 2.4x
    noise floor is on match TOTALS; a per-bite rate is a much tighter number.

## Fixture set (Assets/Phase1/qa_rb1_fixtures.txt)
All 13 share ONE chassis: owen's own qa_owen_build_SAFE frame with its weapon
and its two ABS cosmetic beams removed (13 parts). Only the nose weapon differs,
so cost/mass deltas are the weapon's.

## M1. FIXTURE VERIFICATION (Assets/Phase1/qa_rb1_wire.txt) - before any damage number
Regime b3f9659c. Opponent mauler. Cost delta is vs NOWEAP = 688 cr / 746.7 kg.

build      cost  d(cr)  arenaMass  actuator quote                              tipSpd
NOWEAP      688     -     746.7    (none)
FLIPPER     730   +42     794.2    pivot  I=1.392  tipR=0.575 rate14.0 kJ0.39   8.05
FIXDISC     724   +36     792.1    (none - fixed edge)                          -
FIXSPIKE    734   +46     803.7    (none - fixed edge)                          -
DISC_Z      760   +72     828.5    spindle I=0.433 tipR=0.170 rate73.5 kJ3.34  12.50
COAX_V      760   +72     828.5    spindle I=0.433 tipR=0.170 rate73.5 kJ3.34  12.50
SAW_Z       764   +76     833.0    spindle I=0.870 tipR=0.230 rate54.3 kJ3.67  12.49
SAW_V       764   +76     833.0    spindle I=0.870 tipR=0.230 rate54.3 kJ3.67  12.49
THRUSTER    770   +82     840.2    ram    I=56.99 tipR=0.150 rate3.5  kJ1.00   0.53
DRUM_V      798  +110     859.7    spindle I=23.02 tipR=1.060 rate9.1  kJ2.74   9.65
HAMMER      798  +110     859.7    pivot  I=23.02 tipR=1.060 rate9.1  kJ2.74   9.65
DISCPIVOT   825  +137     893.3    pivot  I=44.52 tipR=0.970 rate10.0 kJ6.34   9.70

Stored energy E=0.5*I*rate^2 (J): disc 1170 · saw 1283 · drum/hammer 953 ·
discpivot 2226 · flipper 136 · thruster 349.

### Fixture faults found (mine, before they became results)
- FLAIL_V (disc on the spindle's +Z side face, vertical axle) is NOT a flail. The
  0.34 m disc reaches back into the nose beam, so the seam `Spinner blade <-> Beam`
  forms and the disc is welded to the frame. LimbReport correctly reported
  `parts=0 blocked=Spinner blade <-> Beam` and the arena agreed (limbN=0).
  THE BUILDER CAUGHT MY BAD FIXTURE. That is a point in its favour and it is why
  no off-axis result is claimed below.
- "arena parts" is always builder parts minus the wheel count. Wheels are raycast
  anchors, not bodies. Not a bug; checked before it became one.

## MY ERROR 2 (caught before filing)
My wire probe's `axisAlignToOwnFace` metric read 0.000 for DISC_Z and SAW_Z -
i.e. "the disc is tumbling end-over-end, not spinning like a saw". DISC_Z is
byte-for-byte owen's own SPINDLE build and the tipper/widowmaker roster recipe,
so that would have been a CRITICAL. I tried to disprove it and did:

  spinnerZP_10  localEuler=(0,0,0)  colSize=(0.34,0.34,0.10)
                specSize=(0.34,0.34,0.10)  spin_disc worldY=(0.00,-0.02,1.00)

The builder PERMUTES the spec size onto the mount normal instead of rotating
the transform, so for a +Z mount the disc's thin axis is local Z, not local Y.
My metric assumed local Y. Physics box thin axis, visual cylinder axis and the
spindle axle all point +Z: the disc spins cleanly in its own plane. NOT A BUG.
(The 1.000 readings on the +Y mounts were the accident, not the 0.000 ones.)

## MY ERROR 1
I recompiled RB1Probe.cs AFTER creating the fresh BuilderManager, which is
exactly the domain-reload trap the brief warns about. Cost ~8 minutes of a
stalled wire probe. Order is: compile, THEN create the BM, THEN measure.

## M2. THE RE-BASELINE (qa_rb1_bite_mauler.txt) - 13 builds x 3 runs x 24 s vs mauler
Trigger genuinely held (`Phase0Input.FireHeld()` returns `debugFire || Space`,
verified in Phase0Input.cs:91/135/202; header records debugFire True).

TOTALS over all 39 runs / 936 s of combat:
  chassis ram  3284 dmg over 207 hits  =  15.9 per bite
  Phase-4 limb 1027 dmg over  25 hits  =  41.1 per bite
  RAM IS 76% OF ALL DAMAGE DEALT.
The old, trigger-off answer was "of 306 mean damage, 209 was ramming" (68%).
HOLDING THE TRIGGER DID NOT CHANGE THE ANSWER. Ramming's share went UP, not down.

Mean damage per 24 s run, by build (2.4x noise floor shown for scale):
  SAW_Z    255      COAX_V   131      DISCPIVOT 128     DISC_Z   117
  FIXSPIKE 113      FLAIL_V  111      DRUM_V    108     FLIPPER  100
  THRUSTER  90      NOWEAP    88      FIXDISC    77     HAMMER    72
NOWEAP is a chassis with NOTHING bolted to the nose. Every base-component
weapon except SAW_Z sits inside 2.4x of it - i.e. INDISTINGUISHABLE FROM
DRIVING AN EMPTY BOX. HAMMER (110 cr of pivot+beam+blade) is BELOW it.

Per-bite, where n allows (these are the tight numbers):
  disc on pivot   62.4/bite (n=3)
  spinnerSaw      51.5/bite (n=10)
  drum blade      38.3/bite (n=7)
  chassis ram     15.9/bite (n=207)
  wedge flipper   11.4/bite (n=5)
  SPINNER DISC    -- NO BITES AT ALL, n=0 over 9 runs / 216 s --

## M3. THE SPINNER DISC LANDS NOTHING
limb bites per build, 3 runs each:
  DISC_Z (fwd axle, spinner r=0.17)   0,0,0
  COAX_V (vert axle, spinner r=0.17)  0,0,0
  SAW_V  (vert axle, saw     r=0.23)  0,0,0
  SAW_Z  (fwd axle, saw      r=0.23)  2,0,8
  DRUM_V (vert axle, r=1.06)          0,7,0
  HAMMER (vert pivot, r=1.06)         0,0,0
  DISCPIVOT (vert pivot, r=0.97)      0,0,3
  FLIPPER (X pivot, wedge)            0,5,0
  THRUSTER (ram+spike)                0,0,0
25 weapon bites in 936 s = ONE BITE EVERY 37 SECONDS OF COMBAT.

Fixture is verified good: DiscProbe (qa_rb1_disc_rate.txt) shows DISC_Z's disc
reaching 73.5 rad/s (its exact quoted maxRate) by t=2.2 s and HOLDING it for the
full 22 s, 36.1 revolutions, seam peak 33 vs threshold 2700, SHEARED=False,
damage dealt 0. The disc spins perfectly and hurts nothing.

### Mechanism (code-supported hypothesis, not yet isolated)
DamageResolver.ApplyHit:173
    if (Time.time - p.lastHitTime < PART_IMMUNITY) return;   // 0.5 s
is a SHARED per-victim-part window covering rams AND limb bites, and it is
checked BEFORE attribution - so a suppressed limb bite increments nothing and
reads as 0/0 rather than as "hit for 0". Meanwhile Actuator.Bite:1391
    rate *= Mathf.Sqrt(1f - DRAIN_FRAC);
runs UNCONDITIONALLY, after ApplyHit has already returned. A weapon mounted at
the nose - which is every disc in this game - contacts at the same instant as
the chassis it is bolted to, so the chassis ram stamps the immunity first and
the disc pays 40% of its kinetic energy for a hit that is discarded.

## M4. RAISING n DISPROVED MY OWN STRONGEST CLAIM (qa_rb1_bite_focus.txt, n=6)
I was about to file "the spinner disc cannot bite". At n=6 more, DISC_Z r2
landed limb 238/5 (47.6/bite). It CAN bite. Retracted. What survives is weaker
and truer: WEAPON ENGAGEMENT IS BIMODAL AND RARE.

Pooled over both sweeps (n=9 per focus build, 24 s each, trigger held throughout):
  build     mean dmg/run   ratio vs NOWEAP   limb bites in 9 runs   runs w/ >=1 bite
  NOWEAP        86.2           1.00x                 -                    -
  DISC_Z       115.2           1.34x                 5                   1/9
  COAX_V       120.4           1.40x                 0                   0/9
  HAMMER       126.8           1.47x                11                   3/9
  SAW_Z        213.4           2.48x                29                   4/9
NOISE FLOOR IS 2.4x. ONLY THE SAW CLEARS IT, AND ONLY JUST.

Across ALL 69 runs / 1656 s: 54 were armed with an actuated weapon. 43 of those
54 (80%) landed ZERO weapon hits in 24 s with the trigger held the whole time.
  chassis ram  5928 dmg / 383 hits = 15.5 per bite
  actuated limb 2355 dmg /  60 hits = 39.3 per bite
  RAM IS 72% OF ALL DAMAGE DEALT.

### ANSWER TO (a), THE RE-BASELINE
The old answer was "chassis ramming dominates - of 306 mean damage, 209 was
ramming", and it was measured with the player's weapon switched off. THE ANSWER
IS UNCHANGED. With the trigger genuinely held, ramming is 72% of damage (it was
68%). Not one member of the base-component kit - hammer, drum, thruster,
flipper - is separable from driving an unarmed box into the enemy at n=9 against
this game's own 2.4x noise floor. The hammer costs 110 cr and reads 1.47x; the
thruster and flipper read BELOW an empty nose in the 13-build sweep.
Per bite a weapon is worth 2.5 rams. It lands one sixth as often.

## M5. spinnerSaw STRICTLY DOMINATES spinner FOR 4 CREDITS
Same mount, same spindle, same chassis:
                spinner        spinnerSaw
  cost          760 (72 kit)   764 (76 kit)      +4 cr
  tipRadius     0.170          0.230             +35% reach
  stored E      1170 J         1283 J            +10%
  edgeHardness  1.5            1.6
  volume/HP     0.0116 (floored 0.012)  0.0127   more HP
  MEASURED bites/run (n=9 each, fwd axle)  0.6    3.2      5x
  MEASURED dmg/run                        115     213      1.9x
The spinner has no advantage on any axis. The spinner is the ONLY weapon on
tipper, on widowmaker AND on owen's own qa_owen_build_SPINDLE. The spinnerSaw
is on no roster bot and, per the brief, has never been fought. That is backwards.

## M6. THE UNDRIVEN-DISC WARNING IS WRONG ON THE BEST DISC BUILD
BuilderManager.cs:3006 gates on `li.act.def.id.StartsWith("spindle")`, so a disc
driven by a PIVOT counts as undriven. Fixture DISCPIVOT (pivot + beam + spinner):
  builder : disc spindleDriven=False  -> "1 disc(s) with no spindle - a disc is
            an unpowered rotor... or keep it as a fixed edge."
  arena   : pivotYP kind=Pivot limbN=2 members=[beam spinnerZP], I=44.5, tipR=0.97
  live    : the disc moves (2.4 rev of arc in 6 s) and BITES - 187 dmg / 3 hits
            = 62.4 PER BITE, the highest per-bite figure in my whole dataset.
The build screen tells the player his best-performing disc configuration is dead
weight. Classic one-side-only: the warning was written against the spindle and
never asked what the OTHER actuators do with a disc.

## M7. (b) OPPONENT QUALITY AFTER THE CONVERSION
Identical unarmed punching bag (NOWEAP), 24 s, enemy damage dealt:
  widowmaker (Champion) 786,194,130,62,502  mean 335   stripped player 13->4 and 13->5
  tipper     (Rookie)    80,242,201,186,  3  mean 142
  mauler     (Veteran)   n=9                 mean  85
The two bots the disc conversion touched are now the two most dangerous on the
roster, and the ROOKIE out-damages the VETERAN 1.7x against an identical target.
The conversion did NOT defang them - the AI holds a spindle from SPIN_HOLD_RANGE
5.5 m (AIController.UpdateFire), well before contact, so its disc is at speed on
arrival. It made them better, and it deepened the standing tier inversion.
(Caveat: eDealt is not source-split; some of widowmaker's 335 is ram.)

## M8. WHAT I CHECKED AND FOUND NOT BROKEN (disproof attempts)
- "The AI cannot hold a spindle trigger, so the conversion defanged every disc
  bot." FALSE. AIController.UpdateFire:244 fires Spindles on dist < 5.5 m
  (vs 3.0 m + arc for pivot/ram). Widowmaker's 335 mean confirms it empirically.
- "The disc no longer spins up on a no-engine machine." FALSE. DiscProbe:
  73.5 rad/s by t=2.2 s at MotorKW 1.0, held 22 s. My earlier revs-per-second
  reading was Quaternion.Angle aliasing, not slow spin.
- "The disc tumbles instead of spinning." FALSE - my metric's bug (see ERROR 2).
- "The disc's centre seam shears." FALSE. peak 26/33 vs threshold 2700.
- "RobotVisuals still needs SpinnerWeapon for the disc's motion trail."
  FALSE - the dead `GetComponentsInChildren<SpinnerWeapon>` loop at
  RobotVisuals.cs:81 is now unreachable, but the Actuator limb loop above it
  already covers the disc (WeaponEdge matches "spinner"). Harmless dead code.
- "The builder double-bills or under-bills disc power." Checked: the second
  power block really is gone and the limb loop prices the spindle. No double-bill.

## M9. STILL ASSUMES A DISC HAS ITS OWN MOTOR (dead/rotten, not shipping bugs)
FuncProbe.cs:56, UP1Dev2.cs:210, UP2Func2.cs:199, UP2Func3.cs:143, UP3Func.cs:261
all still locate the weapon via GetComponentsInChildren<SpinnerWeapon>(), which
now returns empty. UP1Dev2 explicitly FAILS with "no SpinnerWeapon in the arena".
Five of this project's own user-path regression tests will now report a false
failure on every disc build. Not a game bug; it is evidence-base rot, and the
brief's own rule ("retire stale findings") applies to harnesses too.

## MY ERROR 3
FLAIL_V was not a flail - the disc reached back into the nose beam and welded
itself to the frame. The builder caught it; I did not, until I read the wire
dump. No off-axis-disc result is claimed.

## MY ERROR 4
I claimed "the spinner disc lands nothing, n=9" after 9 runs, and wrote it into
this file. Six more runs produced a 5-bite run. Filed the weaker true claim.

## Standing findings - status after this round
- 2 (Validate passes immobile machines): not re-tested, no new evidence.
- 3 (tier ladder inverted): RE-CONFIRMED with a cleaner instrument and new
  post-conversion numbers (M7). Still a roster-chassis problem, still declined
  as an AI-knobs fix - but it is now worse, and the conversion is why.
- 4 ("doing nothing wins"): consistent with M2/M4 - if 72% of damage is mutual
  ramming and weapons land once per 37 s, whether you charge barely matters.
- 9 (ISO pane): not re-tested. Not confirmed an eighth time.

## SCORE: 3.5 / 10
Previous rounds: 5.5 / 4.5 / 4 / 5.5 / 4.5. This is lower, and the brief permits
that: "a sharper measurement legitimately scores DOWN work a blunter one scored
up." The five earlier rounds argued about which weapon was best using data
gathered with the weapon switched off. The trigger is now held, and the answer
did not move: weapon choice does not measurably change a match. 80% of armed
24 s engagements land zero weapon hits. A 110 cr hammer reads 1.47x an empty
nose against a 2.4x noise floor. That is not a balance problem, it is the core
loop - build a machine, pick a weapon, watch it matter - not closing.

Credit where due, and it is real:
 * the disc conversion is internally coherent and does what it says. The disc
   turns at exactly its quoted maxRate, the x1 centre seam holds with 80x
   headroom, the second power block really is gone, the AI holds its spindle
   from further out than a pivot, and the roster disc bots got MORE dangerous.
 * the builder caught a bad fixture I did not (FLAIL_V) and named the seam.
 * LimbReport and the arena's Actuator.Wire agreed on every one of 13 fixtures.
 * nothing crashed, nothing sheared wrongly, no NaNs, 0 new console errors.
The conversion is good work. It is sitting on top of a combat model where
mutual chassis ramming produces 72% of all damage.

## Editor left as found
1 BuilderManager (fresh), 0 harness GameObjects, Build mode, qa_owen_build_SPINDLE
loaded (17 parts / 786 cr / Validate OK), timeScale 1, debugFire false,
debugPointer false. No new console error in 35 minutes (newest entry 08:25:16
predates the recovery; state check at 09:00:04).
qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes - UNCHANGED.
New file left on disk: Assets/Phase1/Scripts/RB1Probe.cs (wire + bite sections),
plus qa_rb1_fixtures.txt / qa_rb1_fix_focus.txt / qa_rb1_fix_noweap.txt and the
four data files qa_rb1_wire.txt, qa_rb1_bite_mauler.txt, qa_rb1_bite_focus.txt,
qa_rb1_disc_rate.txt, qa_rb1_opp_tipper.txt, qa_rb1_opp_widow.txt.
