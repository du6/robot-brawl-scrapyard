# ROUND 2 CRITIC - Robot Brawl: Bolt & Blade
Date 2026-07-27. Critic 2 of 3.

## SOURCE REGIME STAMP (taken before first measurement)
concat md5 (BuilderManager+Actuator+DamageResolver+AIController+FightManager+CompoundRobot) = bca1791306627f9a47e353ee0c0d8121
BuilderManager.cs   fd9fd06193ee9ce20e03976d26e60f56
Actuator.cs         993a937e49aabda9527f9c3ec0ae01ea
DamageResolver.cs   64856e3c170583ee038aa051aa76166a
AIController.cs     00a36691e7ae964df348f716bd5e75b4
FightManager.cs     06142d38a9e6d8cfeadf9c3b0ad71f42
CompoundRobot.cs    41be3e105ba49c662fac77f984248f16
RepeatHarness.cs    5829e77e416c8206c8c3d26a1ac2bc88
DiscProbe.cs        8ee34f0b61e7b3dde24e55806e66b0a8
qa_owen_build_SAFE.txt f4e052c2081911eeadc5502e86a63a4b  (SACRED, matches)

## EDITOR STATE AT START
0 console errors, 30 warnings (all pre-existing CS0618/UAC1001 + 4 "referenced script missing").

## INSTRUMENT
`Assets/Phase1/Scripts/RB2Probe.cs` (new, one file, one reload). Controlled
comparison harness: the PLAYER build is held byte-constant (round 1's NOWEAP
fixture - owen's own frame with the weapon and the two cosmetic beams removed,
13 parts) and exactly ONE thing varies per case: the opponent recipe, the TIER
KNOBS, or the player's own policy.

It reports the ENEMY's damage SPLIT BY SOURCE (ram vs limb), which is the column
round 1 explicitly named as missing from its tier measurement ("eDealt is not
source-split; some of widowmaker's 335 is ram"), plus behaviour telemetry -
mean COM range, seconds inside 2.0 m, seconds inside 1.3 m, and mean commanded
|throttle| - so "the tier changed the AI's behaviour" can be tested DIRECTLY
rather than inferred from an outcome that sits under a 2.4x noise floor.

Source regime at first measurement: SOURCE REGIME 43a5fe92 (42 scripts, newest
2026-07-27 17:23:46Z). Constants: DMG_K 0.045, HP_K 6000, PART_IMMUNITY 0.50,
BITE_TIP_SPEED_MS 4.00, DRAIN_FRAC 0.40, MATCH_TIME 90.

### MY ERROR 1 (caught by the fixture rule, before any number was believed)
First tier pass returned `INVALID Needs at least 1 wheel` on all six cases. My
case parser did `r.Split('|')` and the SNAPSHOT FORMAT IS ALSO PIPE-DELIMITED
(`core|0.000,0.700,0.000|0|...`), so field 5 was the single token "core". Fixed
to `Split(new char[]{'|'}, 5)`. Exactly the failure mode the brief warns about -
if I had not printed Validate()'s message I would have had six silent zero rows.

## M1. THE DIFFICULTY TIER IS INVERTED BY THE TIER KNOBS THEMSELVES
(Assets/Phase1/qa_rb2_tier.txt - 36 runs, 22.5 s each, timeScale 3, regime 34ff221d)

This is a CONTROLLED experiment, which is what standing finding 3 has never had.
Round 1's M7 compared DIFFERENT ROSTER BOTS at different tiers, so its result
("Rookie tipper out-damages Veteran mauler 1.7x") could always be answered with
"that is the recipe, not the tier" - and it was, three rounds running: finding 3
is on the standing list DECLINED as "roster-chassis work, not AI knobs".

Here the RECIPE IS FIXED and only `bm.opponentTier` changes. Same mauler, same
identical stationary 13-part NOWEAP bag, same 22.5 s, n=6 per cell:

  mauler tier   mean dmg   RAM HITS   s within 2.0 m   s within 1.3 m   mean range
  Rookie           61.8      17          9.70             3.97            2.74 m
  Veteran          53.0      17          7.38             3.45            3.64 m
  Champion         17.8       4          2.48             0.83            4.29 m

THE CHAMPION LANDS 4 HITS WHERE THE ROOKIE LANDS 17, ON THE SAME MACHINE.
It deals 3.5x LESS damage. Four of its six runs dealt EXACTLY ZERO damage to a
target that never moved, in 22.5 s.

Why this is not the 2.4x noise floor talking:
 * ram hits are an EVENT COUNT, not a match total - 17 vs 4 over 21 events.
   The project's own rule is "claim per-bite numbers, not match totals"; this is
   the count itself.
 * the behaviour telemetry separates almost completely. Rookie's six
   time-within-2 m values are 9.0/8.1/11.7/11.0/8.0/10.4 (min 8.0); Champion's
   are 8.1/0.0/0.0/1.0/0.0/5.8 (max 8.1). ONE value of twelve overlaps.
   Mann-Whitney on n=6 vs n=6 with one overlap is p < 0.01.
 * mean range and mean commanded throttle are monotone in tier and in the
   direction the mechanism predicts (range 2.74 -> 3.64 -> 4.29 m; commanded
   |throttle| 0.69 -> 0.86 -> 0.92).

MECHANISM. Every knob EnemyRoster exposes for difficulty makes the bot go
FASTER or commit HARDER: Aggression 0.75 -> 1.0 -> 1.0, OpeningThrottle
0.40 -> 0.55 -> 0.80, SteerAggr 0.22 -> 0.10 -> 0.06 (less governor damping,
so more steer lock at speed), DecisionInterval 0.45 -> 0.20 -> 0.10. In this
physics, approach speed is a LIABILITY: AIController's threat-arc layer commands
an orbit/flank aim point at ~2.7 m and the Champion overshoots it by 1.6 m
because it arrives too fast to track it. Contact time falls, and round 1 already
proved contact opportunity is the only thing that gates damage in this game
(its M3 funnel). So the difficulty system's single lever pushes directly against
the game's only damage bottleneck. The tiers are not mislabelled by accident -
they are anti-correlated BY CONSTRUCTION.

This answers the reason finding 3 was declined. It IS the AI knobs.

### The widowmaker arm is NOT evidence and I am not claiming it
  WIDOWMAKER Rookie/Veteran/Champion: mean 22.5 / 105.3 / 32.8
Non-monotone and worthless: those means are driven by 3 limb events in 18 runs
(611, 179, 103 dmg). Disc damage is bimodal exactly as round 1 found. n=6 cannot
resolve a bimodal distribution. Reported for completeness, claimed for nothing.
The mauler is the clean control precisely because it is spike-only - every point
of its damage is ram, so the number measures DRIVING and nothing else.

## M2. I TRIED TO DISPROVE M1 AND FAILED (qa_rb2_b2.txt, 54 runs)
The obvious objection to M1: an AFK bag is a special case. AIController's
threat-arc layer has a `parked` branch (PARKED_TIME -> orbit at ~2.7 m radius,
CIRCLE_MAX_S deadline, then commit), so a stationary target engages a code path
a real match might never see, and "the Champion orbits further out" could be an
artefact of orbiting rather than of the tier.

So I re-ran the identical tier ladder with the PLAYER CHARGING - `parked` false,
orbit layer off, contact time supplied by the player instead of the AI.
Same mauler recipe, same NOWEAP player, n=6 per cell:

  mauler tier   E dmg   E RAM HITS   player PIECES LOST (6 runs)   s within 2.0 m
  Rookie         80.8      29                 11                      9.35
  Veteran        62.8      19                 10                      8.97
  Champion       40.5      16                  3                      9.68

THE INVERSION SURVIVES, AND GETS SHARPER. With contact time now EQUAL across
tiers (9.35 / 8.97 / 9.68 s within 2 m - the player supplies it), the Champion
still lands 16 hits to the Rookie's 29 and strips 3 of the player's pieces where
the Rookie strips 11. Hits per second of contact: 0.517 / 0.353 / 0.275,
monotone DOWNWARD with tier. Damage per hit is flat (16.7 / 19.8 / 15.2), so
this is a HIT RATE effect, not a damage-model effect.

Pooled over both policies (24 runs): Rookie 46 ram hits, Champion 20.
On an event count of 66, sqrt-n error is +-6.8/+-4.5, so the 26-hit gap is
> 3 sigma. This is not the 2.4x match-total noise floor; it is a Poisson count.

### M2b. The shipped roster ladder points the same wrong way
Six roster bots, own shipped tier, identical AFK NOWEAP bag, n=6, 22.5 s:
  bot         tier       mean dmg   ram hits   limb hits   s within 2.0 m
  SCOUT       Rookie        9.5        5          0           3.15
  TIPPER      Rookie        3.5        5          0           3.05
  MAULER      Veteran      22.7       15          0          10.78
  RIPPER      Veteran      22.2        8          2           7.67
  BULWARK     Champion      4.7        5          0           2.98
  WIDOWMAKER  Champion     22.7        4          1           7.22
By tier, ram hits landed: Rookie 10, Veteran 23, CHAMPION 9. The Champion tier
lands FEWER hits than the Rookie tier and less than HALF the Veteran tier.
BULWARK - "steel juggernaut, hits like a truck", the top of the ladder - lands
5 hits in 6 runs and spends 2.98 s of 22.5 inside 2 m of a target that never
moves. So both halves of the ladder (the knobs, M1/M2; the recipes, here) push
in the same wrong direction.

### RECONCILIATION with round 1 M7 - not a contradiction, a policy difference
Round 1 read widowmaker 335 / tipper 142 / mauler 85 where I read 22.7 / 3.5 /
22.7 on the same bag. Cause found before filing anything: RB1Probe.Drive has no
AFK branch - it ALWAYS charges. Round 1's M7 is a CHARGING-player measurement,
mine is a parked one. Both are valid, they answer different questions, and the
tier ORDERING (which is what finding 3 is about) is the same in both. No round-1
number is retired by this.

### An honest noise datum from my own data
MAULER at Veteran, AFK, n=6, run twice in two separate batches under identical
settings: mean damage 53.0 and 22.7 - a 2.3x gap, independently re-deriving this
project's 2.4x noise floor for the third time. The RAM HIT COUNT over the same
two samples was 17 and 15. That is why every claim above is stated in hit counts
and contact seconds and not in match damage totals.

## M3. THE JUDGES' CARD IS READ IN THE ORDER THAT PUNISHES AGGRESSION
FightManager.Judge() (line 592) tests STRUCTURE FIRST and short-circuits the
damage column entirely:
    if (Mathf.Abs(player.structFrac - enemy.structFrac) > StructBand()) -> verdict
    ... only then damage, only then control.
StructBand() = max(0.060, 1.5/min(startParts)). For a 13-part player vs an
11-part mauler that is 0.136, so losing TWO of thirteen pieces (0.154) decides
the card against you no matter what the damage column says.

Measured, mauler at Veteran, 22.5 s, NOWEAP player (qa_rb2_b2.txt/qa_rb2_tier.txt):
  policy   player pieces lost / match   ENEMY pieces lost / match   dmg dealt
  afk              0.17  (2 in 12)              0.00                    0
  charge           1.67  (10 in 6)              0.00                   57
Driving into the enemy costs the player TEN TIMES the structure and takes
exactly ZERO pieces off the enemy. The 57 damage it buys is checked SECOND and
only if structure is level. That is not a balance tuning issue, it is a
SCORING ORDER that makes the aggressive verb strictly dominated on the criterion
the referee reads first.

## M4. STANDING FINDING 4 SETTLED: "DOING NOTHING WINS" DEPENDS ON WHETHER YOU
##     HAVE A WEAPON - WHICH IS WHY IT FLIPPED FIVE TIMES
(qa_rb2_b3.txt - 20 matches run TO JUDGEMENT, 90 s, vs mauler at Veteran)

  build (17-part SPINDLE = owen's own armed build) vs (13-part NOWEAP)
                          outcomes        player pieces lost   enemy pieces lost
  NOWEAP  afk             L5              -  (0/13 x5, 1 run 4)     4 in 5
  NOWEAP  charge          W1 L4              21 in 5                 5 in 5
  SPINDLE afk             W3 D2 L0           10 in 5                14 in 5
  SPINDLE charge          W2 L3              20 in 5                15 in 5

UNARMED, charging is (weakly) better: 1 win vs 0.
ARMED WITH THE TRIGGER HELD, PARKING NEVER LOST A MATCH and charging lost 3 of 5.
Parking halves the player's structural losses (10 vs 20 pieces) while taking the
SAME number off the enemy (14 vs 15).

THE PLAYER WAS NOT MOVING. Throttle 0, steer 0, for 90 s. Its disc still landed
7/9/1/6/5 limb bites for 379/428/72/308/250 damage and ZERO ram, because the AI
drives itself into a spinning rotor. Per limb bite, parked 51.3 (n=28 bites) vs
charging 39.8 (n=26 bites).

This also AMENDS round 1's CRITICAL rather than contradicting it. Round 1 asked
"does a weapon beat an empty nose?" while CHARGING, and correctly answered no.
Parked, the same comparison is 0 damage (NOWEAP) vs 250-428 (SPINDLE). The
weapon is not weak and it is not un-landable. THE WAY TO USE IT IS TO STOP
PLAYING. Round 1's own mechanism explains it exactly: contact opportunity is the
only gate, your chassis is ~9 solid boxes and your weapon is one box on the
nose, so when YOU close, the chassis arrives first and eats the exchange; when
the AI closes, it arrives on your weapon face.

Why the finding flipped in all five previous rounds: every one of those sweeps
was measured with `Phase0Input.debugFire` never set, i.e. effectively with the
NOWEAP row - where the afk/charge difference is one win in five and pure noise.

## MY ERROR 2 - M4 DID NOT REPLICATE, AND I AM RETRACTING IT
I wrote M4 from n=5 per arm and it looked decisive: SPINDLE parked W3/D2/L0,
charging W2/L3. I then ran four MORE parked matches on the identical config
(qa_rb2_b4.txt) and got L/L/L/L with ZERO limb bites in all four - where the
first five had landed bites in 5 of 5.

Pooled at n=9 per arm, run to judgement, vs mauler at Veteran:
  policy   outcomes      limb bites (runs with >=1)   dealt   taken   pieces lost P/E
  afk      W3 D2 L4         28   (5 of 9)             159.7   213.7    1.78 / 1.89
  charge   W4    L5         59   (5 of 9)             375.8   290.7    3.33 / 4.22
  hitrun   W2    L2  (n=4)  22   (4 of 4)             393.0   228.5    3.75 / 1.75

THE OUTCOME DISTRIBUTIONS ARE THE SAME. "Doing nothing wins" is NOT supported at
n=9 and the version of M4 above is withdrawn. This is the sixth round in which
this finding has moved, and I now think I know why it keeps moving: at n<=6 the
disc's BIMODAL engagement (round 1's own conclusion) dominates the sample. Five
of nine parked runs landed every bite; four landed none.

WHAT SURVIVES, AND IT IS WORSE NEWS THAN WHAT I RETRACTED:
The player's driving input does not change who wins. A player who gives NO INPUT
AT ALL for 90 s - throttle 0, steer 0 - goes W3/D2/L4 against the reference
opponent, i.e. does not lose 5 of 9 matches, and its damage differential (-54)
loses to charging's (+85) on a column the judges only read if structure is
level, which it is. Charging deals 2.4x the damage, takes 1.4x the damage,
destroys 2.2x the enemy structure and loses 1.9x its own - and ends up in the
same place on the card.

## M5. THE HEADLINE, AT n=15: WEAPON ENGAGEMENT IS A COIN FLIP THAT NOTHING THE
##     PLAYER DOES - INCLUDING BEING IN CONTACT - PREDICTS
(qa_rb2_b3/b4/b5.txt. owen's own qa_owen_build_SPINDLE, 17 parts, trigger held,
parked, 90 s to judgement, vs mauler at Veteran. Fifteen matches.)

  run   s within 1.3 m   limb bites   damage dealt
   1        11.0             7            379
   2        12.8             9            428
   3         5.0             1             72
   4        16.6             6            308
   5        23.3             5            250
   6         9.8             0              0
   7         9.9             0              0
   8        21.7             0              0
   9         3.9             0              0
  10        11.2             0              0
  11        11.6             0              0
  12        16.1             0              0
  13        11.3             0              0
  14        13.0             1             72
  15         7.1            16            614

EIGHT OF FIFTEEN MATCHES LANDED ZERO WEAPON HITS. The seven that landed dealt
72-614. Mean time spent within 1.3 m of the enemy: 12.7 s for the runs that bit,
11.9 s for the runs that did not. THOSE ARE THE SAME NUMBER. Contact does not
predict engagement. Run 8 spent 21.7 s pressed against the enemy and landed
nothing; run 15 spent 7.1 s and landed sixteen bites and a KO.

This is round 1's CRITICAL sharpened into its worst form. Round 1 said contact
opportunity is the bottleneck. It is not even that: at n=15 the machine HAS the
contact and the weapon still does not fire in half the matches. Whether owen's
own build works today is settled by something that is not the build, not the
opponent, not the policy and not the time spent in contact.

Outcomes over the same 15: W4 D2 L9. Charging, n=9: W4 L5. Hit-and-run, n=4:
W2 L2. Nothing the player does moves the result.

## M6. ROUND 1 DEV'S TWO FIXES VERIFIED INDEPENDENTLY (qa_rb2_fixverify.txt)
Re-derived from the builder over all 13 round-1 fixtures, on my regime:
  DISCPIVOT  undrivenWARN 0   <- the false positive round 1 filed is GONE
  FIXDISC    undrivenWARN 1   <- true positive KEPT
  FLAIL_V    undrivenWARN 1   <- welded-to-frame disc, KEPT (correct)
  DISC_Z rotorDip 0.080 m · SAW_Z 0.165 m · COAX_V/SAW_V 0.000 m (vertical
  axle never changes height as it turns - correctly silent) · every non-spindle
  build 0.000 m.
Both fixes hold exactly as the dev documented them. No regression found.

## M7. A MEASUREMENT-INTEGRITY PROBLEM I CANNOT EXPLAIN AND WILL NOT HIDE
The 15 parked runs above were three consecutive batches of the SAME config:
  batch b3 (5 runs): 5 of 5 landed bites
  batch b4 (4 runs): 0 of 4
  batch b5 (6 runs): 1 of 6
Under an i.i.d. model with the pooled rate p=7/15=0.47, P(5 of 5) = 0.023 and
P(<=1 of 10) = 0.0057; jointly ~1e-4. Engagement CLUSTERS BY BATCH, not by run.
I do not know the cause. Candidates I could not exclude in the time available:
ordering (b3 ran two NOWEAP cases before the SPINDLE ones; b4/b5 started on
SPINDLE), or some editor-side state that survives BackToBuild.

IF THIS IS REAL IT MATTERS MORE THAN ANY BALANCE FINDING IN THIS DOCUMENT.
Every sweep this project has ever run is a single batch of n<=9. If runs inside
a batch are correlated, then an n=6 batch is not six samples, and the project's
2.4x "noise floor" - derived from comparing two n=3 batches - is a BETWEEN-BATCH
figure being applied to within-batch comparisons. Round 2's dev should spend
its first hour interleaving two configs in one batch (A,B,A,B,...) instead of
running them as blocks, and see whether the clustering follows the config or
the batch. That is a two-line change to any harness here.

## STANDING FINDINGS - status after this round
1 (hook does not pull): not re-tested. No new evidence, not re-filed.
2 (Validate passes immobile machines): not re-tested.
3 (tier ladder inverted): RE-FILED WITH THE EVIDENCE ITS THREE DECLINES ASKED
  FOR. It was declined as "roster-chassis work, not AI knobs". M1/M2 hold the
  recipe byte-constant and vary ONLY bm.opponentTier, under two different player
  policies, and the inversion is there in both. It IS the AI knobs.
4 ("doing nothing wins"): NOT SUPPORTED at n=15 vs n=9. I filed it from n=5 and
  retracted it myself (ERROR 2). What replaces it is stronger: no policy changes
  the outcome distribution at all.
5 (weapon silhouettes): not re-tested.
6 (ghost thinner than placed part): not re-tested.
7 (no reach limit): not re-tested.
8 (whole-machine teardown): not re-tested. NOTE: SPINDLE_charge b3 r2 and b4 r2
  both took the mauler from 11 parts to 0. Consistent with the standing repro;
  no new work done.
9 (ISO capture pane): NOT confirmed an eighth time, per the brief.

## SCORE: 3.0 / 10
Rounds so far: 5.5 / 4.5 / 4 / 5.5 / 4.5 / 3.5(r1). Lower again, and for the
same reason round 1 gave: a sharper instrument found the answer is worse than
the blunter one said.

What is broken, in the order a player would meet it:
 · BUILD A MACHINE THAT MATTERS. Round 1: no base-component weapon separates
   from an empty nose at n=9. I did not overturn that, and M5 says why it will
   be hard to: on owen's own armed build, run to judgement, the weapon fires in
   7 of 15 matches and time-in-contact does not predict which 7.
 · DRIVE IT WELL. afk W4/D2/L9, charge W4/L5, hitrun W2/L2. The player's only
   verb does not move the result.
 · CLIMB A LADDER THAT GETS HARDER. The Champion mauler lands 20 ram hits where
   the Rookie mauler lands 46, on the identical machine. Every difficulty knob
   the game has makes the opponent drive faster, and speed is the thing that
   destroys contact time - which round 1 proved is the only gate on damage. The
   ladder is anti-correlated with difficulty BY CONSTRUCTION, not by accident.

Credit, and it is real:
 · round 1 dev's FIX 1 and FIX 2 both verify clean over all 13 fixtures (M6),
   with the true positives kept and no collateral. Careful, checked work.
 · the disc conversion continues to hold up: seams intact, no NaNs, 0 console
   errors across ~120 matches I ran, the AI holds its spindle, and a parked
   armed player really can KO a mauler in 55 s (b5 r5, 614 limb damage).
 · the judging code is thoughtfully written and its comments are honest about
   its own history; M3 is a criticism of the ORDER, not the quality.
 · the builder caught my bad fixture in round 1 and Validate() caught mine in
   this round (ERROR 1) before either became a false result.

## EDITOR LEFT
1 BuilderManager (fresh, created after the last measurement), 0 probe/harness
GameObjects, Build mode, qa_owen_build_SPINDLE loaded (17 parts / 786 cr /
Validate OK), Time.timeScale 1, Phase0Input.debugFire false, debugPointer false.
qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes -
UNCHANGED, verified on exit. qa_owen_build_SPINDLE.txt read only, unmodified.

CONSOLE, stated honestly: it holds a burst of the documented post-reload
`BuilderManager.OnGUI:2812` NRE from the two domain reloads I caused mid-session
(newest entry timestamp 10:30:46). It is NOT ongoing - I re-queried 30 s later
with the live fresh BuilderManager drawing every frame and the newest timestamp
had not moved. This is the same handover artefact round 1's critic and round 1's
dev both recorded; it is the reload, not the agent. The recovery in the brief
was applied after each reload.

FILES LEFT ON DISK
  Assets/Phase1/Scripts/RB2Probe.cs        new instrument (+ .rb2.bak)
  Assets/Phase1/qa_rb2_critic.md           this file
  qa_rb2_tier.txt        36 runs, the controlled tier ladder (M1)
  qa_rb2_b2.txt          54 runs, roster ranking + charge-policy tier control (M2)
  qa_rb2_b3.txt          20 runs to judgement, afk/charge x NOWEAP/SPINDLE (M4)
  qa_rb2_b4.txt          12 runs to judgement, the batch that broke M4 (ERROR 2)
  qa_rb2_b5.txt           6 runs to judgement, the n=15 pool (M5)
  qa_rb2_fixverify.txt   round-1 dev FIX 1 / FIX 2 re-verification (M6)
  qa_rb2_cases_*.txt     the case files those runs were driven from
  qa_rb2_tier_sanity.txt the six INVALID rows from ERROR 1, kept as the record
