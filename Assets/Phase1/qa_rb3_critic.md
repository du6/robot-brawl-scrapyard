# ROUND 3 CRITIC (of 3) - Robot Brawl: Bolt & Blade
Date 2026-07-27. Role: critic. Mandate: OPEN AND CLOSE THE LOOP.

## SOURCE REGIME STAMP (md5, taken before any measurement)
AIController.cs      bfcc2454e1be8e3b6ea8d7ee52ce16c0
Actuator.cs          993a937e49aabda9527f9c3ec0ae01ea
BuilderManager.cs    670e092c5541d1b55701a52bc0a51a41
DamageResolver.cs    64856e3c170583ee038aa051aa76166a
EnemyRoster.cs       bc7f0b9aa8c01ed883862596db7846e2
FightManager.cs      06142d38a9e6d8cfeadf9c3b0ad71f42
Actuator.cs          993a937e49aabda9527f9c3ec0ae01ea
RepeatHarness.cs     5829e77e416c8206c8c3d26a1ac2bc88
qa_owen_build_SAFE.txt   f4e052c2081911eeadc5502e86a63a4b  (SACRED, verified at start)
qa_owen_build_SPINDLE.txt b5fc6469d3e92ab36daf88dc035105d1

## M0 - editor state on arrival
NOT CLEAN. Console was flooding NullReferenceException at
BuilderManager.OnGUI:2812, ~30 errors in the last visible second - the known
post-domain-reload survivor-BuilderManager failure. Recovered per brief:
destroyed 1 BuilderManager, 0 ModeSelect, created one fresh, timeScale 1,
debugFire/debugPointer false. Handover note: the previous agent's "0 console
errors" claim did not survive to my arrival.

## M1 - THE TIER LADDER, RE-RUN AFTER ROUND 2'S FIX  (qa_rb3_M1_tier.txt)
Controlled: player = owen's SPINDLE build (17 parts, 786 cr) held constant,
opponent RECIPE held constant (mauler), only opponentTier varies. RB2Dev,
interleaved (reps outer, cases inner), n=6/tier = 18 matches, 45 s timebox,
speed 4, fire=true. Instrument choice matters: round 2's inversion was measured
on hit COUNTS, so I report counts, not means (2.4x noise floor).

              Rookie(av 0)  Veteran(av .55)  Champion(av 1)
enemy hits landed on player   58 (5 matches)   39 (5)          31 (5)
pTaken total                  875              844             427
pTaken mean/match             145.8            140.7            71.2
player limb (weapon) hits     26               28              33
player limb damage total      1138             958            1195
close-contact time (<1.3 m)   92.6 s           56.6 s          62.6 s
outcomes                      1W/5timebox      1W/5timebox     1W/5timebox

**ROUND 2's CRITICAL 1 SURVIVES ITS OWN FIX, AND THE FIX WIDENED IT.**
Enemy hits landed is MONOTONE DECREASING with tier: 58 > 39 > 31. Round 2
independently measured 46 (Rookie) vs 20 (Champion) on the same axis. Two
independent batches, same direction, similar ratio (1.9x me, 2.3x r2).
The top of the difficulty ladder hurts the player HALF as much as the bottom.

The fix could only ever have made this worse, and EnemyRoster.cs:153 says so in
its own words: "Kept at 1.0 for Champion ON PURPOSE - ... this change can only
make the game easier at the bottom, never harder at the top." On an INVERTED
ladder, making the bottom "easier" means removing the avoidance that was holding
the Rookie back - so the Rookie now charges you and is the most dangerous rung
on the ladder. The dev verified the knob's effect on damage the player DEALS and
never checked damage the player TAKES. That is this project's signature
ONE-SIDE-ONLY FIX, third occurrence.

**The knob's stated mechanism does not reproduce under a charging player.**
EnemyRoster.cs:137 justifies WeaponRespect from an AFK measurement: avoidance
ON = 15 bites, OFF = 46 bites. Under the shipped tier knob with the player
CHARGING, player limb hits are 26 / 28 / 33 - flat, and if anything INCREASING
with avoidance, the opposite sign. The constant was fitted on one policy and
shipped for all of them. (This is the retune the round-2 dev explicitly flagged
for round 3.)

## M1b - THE AVOIDANCE KNOB IS NOT MONOTONE IN THE BEHAVIOUR IT NAMES
aiState histograms, mean % of match, n=6/tier:
                 flank    circle   commit
  Rookie  av 0    0.0%     0.0%     0.0%   (correct - av 0 collapses the layer)
  Veteran av .55 16.7%     7.7%     7.7%
  Champion av 1  14.0%     4.2%     8.0%
Champion, at avoid=1.00, FLANKS LESS AND CIRCLES LESS THAN VETERAN at 0.55. The
knob is monotone by construction and non-monotone in behaviour, because the
Champion's OTHER tier knobs (react 0.10 s vs 0.20, open at 80% vs 55% throttle,
steer governor 0.06 vs 0.10) make it too fast to satisfy the orbit layer's
PARKED gate; Champion instead spends up to 48% of the match in `turn`.
The builder panel (BuilderManager.cs:3197) promises the player, for Champion,
"circles your weapon and attacks from behind it". Measured: 4.2% of the match.
The two axes of the difficulty ladder cancel each other.

DISPROVED BEFORE FILING (my own errors, kept honest):
- The `avoid=-1.00` rows in the aiState dump looked like the tier failing to
  apply. They are not: RB2Dev reads rtAvoid AFTER the match and prints -1 when
  fm.enemy.ai is null. All three -1 rows are exactly the three PlayerWin rows
  where E[gone]. Instrument artefact, not a bug. Checked before writing.

## M2 - THE DECISIVE CONTROL: AVOIDANCE FORCED EQUAL  (qa_rb3_M2_fullavoid.txt)
Identical to M1 except AIController.avoidOverride = 1.0 on ALL THREE tiers
(RB2Dev "+fullavoid" token). The avoidance layer is now a constant; the ONLY
remaining differences between tiers are the SPEED/REACTION knobs
(DecisionInterval, OpeningThrottle, SteerAggr, Aggression). n=6/tier, 18 matches,
interleaved.

              Rookie   Veteran   Champion
enemy hits landed  65      60       25 (in 5 matches; 1 PlayerWin)
pTaken total     1044    1097      551
pTaken mean       174     183       92

**THE INVERSION IS UNCHANGED WITH AVOIDANCE HELD CONSTANT.** So the cause is the
SPEED knobs - exactly what round 2's CRITICAL 1 said - and round 2's shipped fix
does not touch that cause. It added a second, orthogonal axis and left the
inverted one running.

PAIRED SIGN TEST (the count-based claim; means are inside the 2.4x noise floor).
Champion vs Rookie, enemy hits landed, paired by rep index across BOTH batches
(M1 + M2, 12 rep-pairs, 3 unusable because one arm ended early):
  Champion lower in 8 of 9 comparable pairs, 1 tie, 0 higher.  Sign test p=0.004.
This is the first time on this project the ladder inversion has been shown on
paired data rather than on batch means.

## M2b - THE AVOIDANCE KNOB MOVES NEITHER SIDE RELIABLY
Champion is by design identical in M1 and M2 (avoid was already 1.0): pTaken 427
vs 551 - consistent. Rookie goes avoid 0 -> 1 between the batches and pTaken goes
875 -> 1044, i.e. the SAME DIRECTION as no change at all, inside noise. The knob
that was shipped to fix the ladder does not measurably move the damage the AI
deals, in either direction, on 36 matches.

## M2c - ROUND 2's CRITICAL 2 REPRODUCES ALMOST EXACTLY
Matches in which the player's weapon landed ZERO bites, owen's own armed SPINDLE
build, trigger held, 45 s:
  M1  7/18    M2 13/18    combined 20 of 36 = 56%
Round 2 measured 8 of 15 = 53% at a 90 s timebox. Independent batch, independent
window, same number. CONFIRMED, and it is the single worst fact about this game:
on the player's own weaponised machine, more than half of all fights produce no
weapon damage at all.

## RETIRED THIS ROUND
- ROUND 1's "ramming is 72% of damage". Not reproducible as a property of the
  game. On owen's SPINDLE build the ram share of player damage is 29% in M1 and
  62% in M2 - two n=18 batches of the SAME build, same regime, 2.1x apart. The
  ram/limb split is not a stable statistic at n=18 and should not be quoted as
  one. Round 1's figure was measured on base-component fixtures, not on a
  player build; it was generalised beyond what it could carry.

## M3 - THE TIGHTEST CONTROL THIS PROJECT HAS EVER HAD ON "DO WEAPONS MATTER"
(qa_rb3_M3_safe_vs_spindle.txt)
owen's two saved builds are a ONE-PART controlled pair. Verified by diff:
  SAFE     ... spinner|0,0.7,0.800|0|0,0,1|Steel                  (16 parts, 750 cr)
  SPINDLE  ... spindle|0,0.7,0.900 + spinner|0,0.7,1.100          (17 parts, 786 cr)
Same chassis, same wheels, same battery, same material on every other part. The
only difference is 36 credits of spindle - i.e. whether the disc is DRIVEN.
Opponent mauler/Veteran held constant, interleaved, n=6 each, 45 s, fire=true.

                       SAFE (disc undriven)   SPINDLE (disc driven)
  weapon (limb) hits         0 / 0 / 0 / 0 / 0 / 0        0,0,0,6,1,0
  weapon damage, 6 matches   0                            310
  actuators on the machine   NONE (rotor line empty)      1 (spindleZP, 12.39 m/s
                                                          edge, above bite gate
                                                          43.7 s of a 43.6 s match)
  total damage dealt         622                          793      (1.27x - noise)
  outcomes                   0W / 6 timebox               0W / 6 timebox
  ram damage                 622                          483  (SAFE rams MORE)

### FINDING A - owen's own saved build was SILENTLY DISARMED by the conversion.
Verified at source. Pre-conversion, Phase0/Scripts/CompoundRobot.cs.disc.bak:575
    if (s.id.StartsWith("spinner")) go.AddComponent<SpinnerWeapon>().Init(robot, i);
That line is DELETED in the shipped CompoundRobot.cs, and nothing replaces it.
SAFE has a `spinner` and no `spindle`, so it went from "self-powered disc" to
"inert lump of steel" with the conversion. Measured: zero Actuator components on
the machine and zero weapon damage in 6 of 6 matches.
THERE IS NO MIGRATION. No load-time upgrade, no auto-insert, no version stamp on
the snapshot format. The only mitigation shipped is a builder LABEL
(BuilderManager.cs:3093) - and Validate() still returns OK and the FIGHT button
is still enabled, so the game will happily send a disarmed machine into a fight
and never say the word "disarmed". Every build any player saved before today
that used a spinner without a spindle is now unarmed.
This is the ONE-SIDE-ONLY pattern again: the conversion was verified forwards
(a spindle-driven disc turns at 73.5 rad/s, the seam holds, power was
de-duplicated) and never verified backwards, against the saved builds that
already existed. It is the fourth occurrence of that pattern.

### FINDING B - and paying the 36 credits does not buy a measurable fight.
The repair for Finding A is "insert a spindle". On the tightest fixture
available - the same machine, one part apart - it moves total damage 622 -> 793
(1.27x, far inside the 2.4x noise floor), leaves the record at 0W/6-timebox
versus 0W/6-timebox, and produces any weapon damage at all in only 2 of 6
matches. Round 1 said no base-component weapon separates from an unarmed nose.
I now say it on the strongest possible control, where mass, wheels, battery,
materials and driver are all identical and only "is the weapon powered"
differs. CONFIRMED, and upgraded: it is not a fixture artefact.

DISPROOF ATTEMPTS ON M3 (all recorded, all failed to overturn it):
1. "SAFE only looks unarmed because RB2Dev collects Actuator and the old weapon
   was a SpinnerWeapon." Checked: nothing anywhere attaches SpinnerWeapon
   (grep over Phase0+Phase1, 0 hits for AddComponent<SpinnerWeapon> outside the
   .disc.bak). And limb damage is 0 in 6 of 6 regardless of which component
   would have produced it. Does not overturn.
2. "SAFE is just a worse machine." It is not - it deals MORE ram damage than
   SPINDLE (622 vs 483) and its record is identical. The disc still functions as
   a fixed hardened steel edge on the ram channel; what it lost is all actuated
   damage. Stated that way in Finding A.
3. "The comparison is confounded because SPINDLE's disc sits 0.3 m further
   forward." True, and I cannot remove it without editing a SACRED file. It
   biases in SPINDLE's FAVOUR (more reach), so it cannot explain a null result.
   Finding B is conservative.

## STATUS OF EVERY PRIOR HEADLINE (obligation (a): close the loop)
CONFIRMED, re-measured this round on independent data:
 * R2 CRITICAL 1 - tier ladder inverted. Enemy hits R58/V39/C31 (M1). Now shown
   on PAIRED data: Champion lands fewer than Rookie in 8 of 9 comparable rep
   pairs, 1 tie, 0 higher (M1+M2), sign test p=0.004. NOT FIXED by round 2's fix.
 * R2 CRITICAL 2 - weapon engagement. 20 of 36 armed 45 s matches (56%) landed
   ZERO weapon hits on owen's own build. Round 2 got 53% at 90 s. Confirmed.
 * R1 CRITICAL - no weapon separates from an unarmed nose. Confirmed and
   STRENGTHENED on a one-part control (M3 Finding B).
 * R1's credit for the disc conversion (rotor turns, seam holds, power
   de-duplicated) - I reproduce the mechanism: spindleZP holds 12.39 m/s edge
   speed above the bite gate for 43.7 s of a 43.6 s match, supplyFrac 1.00,
   weapon part never lost. The FORWARD half of the conversion is sound. It is
   the BACKWARD half (saved builds) that was never checked - M3 Finding A.

RETIRED / DOWNGRADED:
 * R1's "ramming is 72% of damage." Not a property of the game. 29% (M1) and
   62% (M2), same build, same regime, two n=18 batches. Do not quote it.
 * R1's "80% of armed engagements land zero weapon hits" - superseded, window-
   dependent. Use 56% at 45 s / 53% at 90 s.
 * EnemyRoster.WeaponRespect's justifying measurement (15 bites vs 46) does not
   generalise off the AFK policy it was fitted on. Under a charging player the
   ordering vanishes and weakly reverses (limb hits 26/28/33 for avoid 0/.55/1).
   The round-2 dev flagged exactly this retune for round 3. Verdict: the
   constant is not supported outside its fitting condition.

NOT RE-TESTED THIS ROUND, NOT RE-FILED (no new evidence - do not treat silence
as agreement or as refutation): standing findings 1,2,4,5,6,7,8. Finding 9 (ISO
capture pane) NOT confirmed an eighth time, per the brief.

## HANDOVER (obligation (b))
MEASURED, trust these: everything above, with the raw rows in
  qa_rb3_M1_tier.txt  qa_rb3_M2_fullavoid.txt  qa_rb3_M3_safe_vs_spindle.txt

ASSUMED, do not trust without re-checking:
 * That 45 s is long enough. All my numbers use a 45 s timebox at speed 4; round
   2 used 90 s. Outcome-based claims (W/L) are therefore weak in my data - 33 of
   36 matches timed out. Every claim I make is a COUNT or an ORDERING, never a
   win rate. If you need win rates, run toKO.
 * That mauler/Veteran is representative. M3 used one opponent recipe.

DETERMINISTIC REPRO for the highest-value open item (M3 Finding A), ~40 s:
   bm.LoadSnapshot(File.ReadAllText("Assets/Phase1/qa_owen_build_SAFE.txt"));
   // Validate() returns null (OK) and FIGHT is enabled.
   RB2Dev: cases = "SAFE|mauler|Veteran|charge|<safe snapshot, \n -> ~>"
           reps=2 seconds=45 speed=4 fire=true
   Expect: every row reads `limb 0/0` and every `rotor` line is EMPTY.
   Compare against the same command with qa_owen_build_SPINDLE.txt.

MY OWN ERRORS THIS ROUND (kept honest, all caught before filing):
 1. I first read `avoid=-1.00` in the aiState dump as the tier knob failing to
    apply. It is RB2Dev printing -1 when fm.enemy.ai is null after a KO. All
    three -1 rows are exactly the three PlayerWin rows. Instrument, not bug.
 2. I initially intended to call SAFE-vs-SPINDLE a perfectly matched pair. The
    diff shows the spinner also MOVES 0.3 m forward. Confound acknowledged in
    the write-up; it biases toward SPINDLE so it cannot manufacture my null.
 3. I assumed on arrival that the brief's "0 console errors, editor clean" was
    still true and nearly measured on top of a BuilderManager that was throwing
    an NRE every OnGUI frame. Checked first; it was not clean. Always check.

## SCORE: 2.5 / 10
Not one measured axis improved between round 2 and round 3. Round 2's two
CRITICALs both survive, and the fix shipped against the first of them provably
targets the wrong axis - with avoidance forced equal, the inversion is fully
intact, so the cause was always the speed knobs and they were never touched. The
core loop still does not close, now demonstrated on the tightest control the
project can construct: owen's own machine, one part apart, powered weapon versus
dead weapon, statistically indistinguishable. And the change that shipped an
hour ago silently disarmed owen's own saved build with no migration path, which
is a player-facing regression, not a tuning miss. Below round 2's 3 because the
round added a regression and subtracted no defect.

## EDITOR LEFT
Fresh BuilderManager, Build mode, timeScale 1, debugFire false, debugPointer
false, AIController.avoidOverride reset to -1, all harness GameObjects destroyed.
Console verified at exit: the only Error entries in the buffer are the arrival
flood, all stamped 11:58:50 (pre-recovery). Zero new errors across ~4.5 minutes
and 54 matches of measurement after the recovery. SAFE md5 re-verified at exit:
f4e052c2081911eeadc5502e86a63a4b - unchanged, never written.
