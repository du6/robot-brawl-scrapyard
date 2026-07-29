# ROUND 2 DEV - Robot Brawl: Bolt & Blade
Date 2026-07-27. Dev 2 of 3. Appended continuously.

## SOURCE REGIME STAMP (taken before first edit)
BuilderManager.cs   fd9fd06193ee9ce20e03976d26e60f56
Actuator.cs         993a937e49aabda9527f9c3ec0ae01ea
DamageResolver.cs   64856e3c170583ee038aa051aa76166a
AIController.cs     00a36691e7ae964df348f716bd5e75b4
FightManager.cs     06142d38a9e6d8cfeadf9c3b0ad71f42
RepeatHarness.cs    5829e77e416c8206c8c3d26a1ac2bc88
qa_owen_build_SAFE.txt f4e052c2081911eeadc5502e86a63a4b  (SACRED, matches critic's stamp)
=> IDENTICAL to the round-2 critic's stamp. Its numbers and mine are on one regime.

## PLAN, in the order the critic asked for
1. M7 FIRST (measurement integrity). If runs inside a batch are correlated then
   nothing else I measure this round means anything. Cheap test: interleave.
2. M5 (weapon is a coin flip). Instrument the bite FUNNEL + rotor telemetry so a
   zero-bite match states its own cause instead of being a mystery.
3. M1/M2 (tier inverted BY the knobs). Only after 1 and 2.

## INSTRUMENT
`Assets/Phase1/Scripts/RB2Dev.cs` - derived on-device from the critic's own
RB2Probe.cs by 10 exact-string replacements (each asserted count==1), so the
harness half is BYTE-IDENTICAL to the one that produced the critic's numbers and
only the additions are new. Additions:
  * `interleave` (default true): reps OUTER, cases INNER. The critic's probe ran
    reps INNER, i.e. blocks - which is exactly the confound M7 names.
  * per-run Actuator bite funnel (fnCall .. fnApply) + DamageResolver immunity.
  * per-run rotor telemetry: edge speed mean/max in m/s, SECONDS ABOVE THE BITE
    GATE, tipRadius, MaxRate, per actuator.
  * per-run power floor (supplyFrac min, stored-fraction min).
  * per-run WEAPON PART SURVIVAL: the elapsed time at which each limb member
    detached, or "none".
  * flush to disk after EVERY run, not at the end.

### A hypothesis I killed by reading before spending a reload on it
My first guess at the coin flip was `GroundBlocked`: a rotor that clips the deck
sets rate=0 and drops to Returning, so a machine sitting a few mm nose-down
would never reach the 4 m/s bite gate while an identical one nose-up would - an
all-or-nothing, per-match, chassis-attitude cause with exactly the bimodal
signature M5 reports. WRONG: Actuator.GroundBlocked() line 1088 opens with
`if (kind == ActuatorKind.Spindle) return false;`. Spindles are explicitly
exempt. Round-1 dev's FIX 2 predicts rotor dip in the BUILDER; the arena does
not act on it. Not the cause. Cost: 3 minutes of reading instead of a 2 minute
reload plus a 20 minute sweep.

## D1. THE FUNNEL SETTLES M5 IN ONE SWEEP: THE WEAPON IS NOT FAILING
(Assets/Phase1/qa_rb2d_m7.txt - 11 matches to judgement, owen's qa_owen_build_SPINDLE
17 parts, trigger held, mauler/Veteran, 90 s, timeScale 3, regime 4f35baa2,
SPIN_afk and SPIN_chg INTERLEAVED A,B,A,B in ONE batch)

Every run, without exception:
  rotor edgeMean 12.38-12.45 m/s · edgeMax 12.50 · ABOVE THE BITE GATE for
  89.9-90.2 s of a 90.3 s match · supplyFracMin 1.00 · weaponPartLostAt none
  funnel gate 0 · energy 0   (in ALL 11 runs)

So: the disc is at its material tip-speed ceiling essentially 100% of every
match, fully powered, never sheared off, and the 4.0 m/s bite gate rejected
EXACTLY ZERO callbacks in eleven matches. Nothing on the weapon side of the
funnel fails, ever. The "coin flip" is not the weapon failing to fire.

WHERE IT ACTUALLY DIES - `onTarget`, the number of FIXED STEPS on which the
disc's swept trigger overlapped a live enemy part:
  SPIN_afk r0..r5 : 52, 17, 19, 144, 15, 0   -> APPLY 5, 2, 2, 8, 2, 0
  SPIN_chg r0..r4 : 22, 34,  4,   0,  0      -> APPLY 2, 4, 2, 0, 0
A match is ~4500 fixed steps. Median onTarget is 17 steps = 0.34 SECONDS.
THE WEAPON IS IN CONTACT WITH THE ENEMY FOR ABOUT A THIRD OF A SECOND PER
90-SECOND MATCH. Everything else in the funnel (fnCall 54k-69k) is the swept
trigger scraping the arena floor: `noRb` is 99.8% of every call.

Two things this KILLS as explanations, with numbers:
 * HIT_COOLDOWN is not eating bites. onTarget 144 -> cool 136 -> APPLY 8 is one
   bite per 0.36 s of a 2.9 s CONTINUOUS touch; the 136 are the same contact
   re-reported on consecutive frames, which is what a 0.5 s per-collider gate is
   for. Shortening it does not create contacts that do not exist.
 * PART_IMMUNITY has never eaten a limb bite. `imm ate limb 0` in 11 of 11 runs
   (it ate 3-185 RAM hits per run). Round-1 dev refuted this and it re-refutes.

AND THE CRITIC'S BINARY IS NOT BINARY. onTarget over 11 runs is
0,0,0,4,15,17,19,22,34,52,144 - a continuous, hard right-skewed distribution
whose bottom tail is zero. "8 of 15 landed nothing" is the left tail of a
skewed count, not a coin flip. That matters: a coin flip has no mechanism to
find, a skewed contact count has one.

## D2. M7 (batch clustering): NOT REPRODUCED as a between-batch effect
The critic's b3/b4/b5 were BLOCKS (its probe loops reps INNER). I ran the same
config interleaved A,B,A,B in one batch. Bites landed in 8 of 11 runs, and the
zero runs are at batch positions 8, 10 and 11 of 11 - i.e. inside a single
batch, engagement still falls off toward the end. So the clustering the critic
saw is NOT "batch b4 was cursed": whatever it is survives interleaving and looks
like drift WITHIN a batch. n=11 cannot separate drift from a skewed count with
three zeros, and I am not claiming it does. What I can say is that the critic's
proposed cheap test has now been run and does NOT rescue the i.i.d. model - so
the honest reading of every n<=6 sweep in this project, mine included, stays
"correlated sample, claim orderings only".

## D3. THE CAUSE, AND IT IS NOT STOCHASTIC: THE AI IS CODED TO AVOID YOUR WEAPON
Reading AIController.Decide() layer 4 (lines 434-466) after D1 pointed at
geometry rather than the weapon: EVERY aim branch in the armed case aims AWAY
from the player's weapon.
  * `threatDir` = sum of the directions to the target's parts with
    edgeHardness > 1.01, i.e. LITERALLY the player's weapons, weighted by how
    hard they hit.
  * parked target  -> `aim = tpos + fromT*2.2 + orbit*1.6`, orbit chosen so
    Dot(orbit, threatDir) < 0 - "slide AWAY from the spinner arc".
  * inside the weapon arc -> `aim = tpos + perp*FLANK_OFFSET` (2.6 m sideways).
  * outside it -> `aim = tpos - threatDir*REAR_BIAS` (0.9 m behind the weapon).
  * even the stalemate-breaking COMMIT branch aims `tpos - threatDir*REAR_BIAS`.
There is no branch, in any state, that ever aims at the player's weapon. The
comments say so out loud: "never feed the disc head-on".

### D3a. Tested it instead of believing it (qa_rb2d_avoid.txt, 19 matches)
The knob arms are INTERLEAVED A,B,C,D,A,B,C,D inside ONE batch, so the round-2
critic's M7 batch confound cannot produce this. Nothing varies but the aim
offsets: FLANK_OFFSET 2.6->0, REAR_BIAS 0.9->0, orbit layer off. Same build,
same opponent, same tier, same trigger, same speed.

owen's qa_owen_build_SPINDLE, PARKED, trigger held, mauler/Veteran, 90 s, n=5:

  arm                onTarget steps per run   bites  dealt/m  taken/m  ePcs/m  W/D/L
  SHIPPED (avoid)    10, 0, 102, 51, 29        15     160.0    208.2    1.20   1/1/3
  avoidance OFF      133, 52, 5, 248, 50       46     426.8    198.0    6.20   4/0/1

  bites 15 -> 46 (3.1x) - an EVENT COUNT over 10 matches, not a match total.
  enemy pieces destroyed 6 -> 31 over 5 matches each (5.2x) - also a count.
  damage TAKEN essentially unchanged, 208 -> 198.
  seconds inside 1.3 m went DOWN, 17.5 -> 13.0.

THAT LAST LINE IS THE WHOLE FINDING. The weapon landed 3x more while spending
LESS time near the enemy. Contact time was never the bottleneck. The critic's
own puzzle - "mean seconds within 1.3 m: 12.7 for biting runs, 11.9 for zero
runs, THOSE ARE THE SAME NUMBER" - is answered: it is the same number because
the variable is not how long you are near the enemy, it is whether the enemy's
AIM POINT ever passes through the part you built to hurt it with. It does not,
by design.

Charging arm, same batch, same direction: 6 bites -> 16, ePcs 1.00 -> 2.75.

### D3b. What this retires
Round 1's CRITICAL "no Phase 4 weapon beats an empty nose, ramming is 72% of
damage WITH the trigger held" is now EXPLAINED rather than merely re-confirmed.
An unarmed nose has no hardened parts, so `threatDir` degenerates to
`target.transform.forward`; an armed nose points the avoidance layer straight at
the weapon and weights it BY edgeHardness. The better the edge you paid for, the
harder the AI works to never touch it. That is why five rounds of weapon tuning
moved nothing: the tuning was downstream of a targeting layer that removes the
weapon from the fight before any of it applies.

## FIX 1 (shipped this round) - weapon-avoidance becomes a TIER COMPETENCE
Files: EnemyRoster.cs (new `WeaponRespect(AiTier)`), AIController.cs
(`rtAvoid`, set in ApplyTier; every aim offset scaled by it), BuilderManager.cs
(the opponent panel's tier line).
  Rookie 0.00 · Veteran 0.55 · Champion 1.00
At Champion the arithmetic is IDENTICAL to what shipped, so this change cannot
make the top of the ladder harder; it can only make the bottom easier, which is
the direction the ladder was broken in. A Rookie now drives at you weapon and
all - which is both what a rookie would do and the arm I measured above.

THE OTHER SIDE, checked (this project's signature failure is the one-side fix):
 a. BUILDER SIDE. The opponent panel said "reacts every 0.20 s · opens at 55%
    throttle - same physics, same parts". That line enumerated the tier's
    effects, and the tier just gained one, so it now also says what the
    opponent will do about your weapon. A difficulty whose main effect is
    invisible is not a difficulty.
 b. ENEMY DAMAGE OUTPUT. Removing avoidance could have made the bot MORE
    dangerous by putting it in contact more. Measured: 208.2 -> 198.0 taken per
    match, parked arm. It does not.
 c. THE TOP OF THE LADDER. Champion av = 1.0 -> `aim` arithmetic is unchanged
    to the float. Verified again by measurement in D4 below.
 d. TEST HOOK. `AIController.avoidOverride` (default -1 = "use the tier") lets
    the BEFORE and AFTER arms of the verification run in ONE interleaved batch
    rather than as two blocks - the exact confound the critic's M7 named.

## D4. FIX 1 VERIFIED - BEFORE AND AFTER IN ONE INTERLEAVED BATCH
(qa_rb2d_ladder.txt, 24 matches to judgement, regime aba88391. Six cases
interleaved R_before,R_after,V_before,V_after,C_before,C_after x4. The recipe is
held byte-constant - the SAME mauler at all three tiers, owen's own SPINDLE
build parked with the trigger held - so only the tier knobs vary, which is the
controlled design the critic's M1 established. "before" = avoidOverride 1.0,
i.e. exactly the shipped code path.)

  tier      arm      onTarget steps/run    BITES  dealt/m  taken/m  W/D/L
  Rookie    before   24,   0,  21,   0        4     59.5    277.0   0/0/4
  Rookie    AFTER     0, 120, 173,  65       28    314.5    202.8   2/1/1
  Veteran   before  181,  71,   6,   0       17    198.5    180.0   2/0/2
  Veteran   AFTER   180,  40,  31,   0       18    248.8    288.2   1/0/3
  Champion  before   24,   0,  37,   0        4     54.2    229.8   0/0/4
  Champion  AFTER     0,  15,   0,   0        3     41.8    152.2   0/2/2

THE NULL CONTROL PASSES. Champion runs av = 1.0 in BOTH arms, so before and
after are the same code path with different labels: 4 bites vs 3, 54.2 vs 41.8
damage, 0 wins vs 0 wins. That is the instrument's own noise, measured, in the
same batch - and it is the number every other row has to beat.

THE LADDER NOW POINTS THE RIGHT WAY, on three independent columns:
  bites landed by the player   before  4 / 17 /  4   AFTER  28 / 18 /  3
  damage dealt per match       before 59 / 199 / 54  AFTER 315 / 249 / 42
  player wins in 4             before  0 /   2 /  0  AFTER   2 /   1 /  0
Before: non-monotone on all three, with Rookie and Champion INDISTINGUISHABLE
(4 bites each, 59 vs 54 damage, 0 wins each) - which is standing finding 3,
measured. After: monotone decreasing on all three. Rookie bites 4 -> 28 is a
7x event count over 8 matches.

HONEST LIMITS, stated rather than buried:
 * The Veteran rung barely moved on BITES (17 -> 18) at WeaponRespect 0.55. It
   separates on damage and on the card, not on the bite count. I am NOT
   retuning 0.55 on n=4 - fitting a constant to four matches under a 2.4x noise
   floor is the mistake this project has made repeatedly. The value is left
   where design says it should be (halfway) and flagged for round 3.
 * Veteran damage TAKEN went 180 -> 288. On n=4 that is inside the noise floor
   and I am not claiming it either way, but it is the one column that moved in
   an unwanted direction and round 3 should watch it.
 * onTarget is still zero in 1 of 4 Rookie runs. The fix moves the distribution
   hard; it does not make engagement deterministic.

## D5. REGRESSION CHECK - THE THING I DID NOT CHANGE (qa_rb2d_roster.txt)
All six SHIPPED roster bots at their own shipped tiers, SPINDLE parked, 90 s to
judgement, n=2, interleaved. Purpose: catch a crash, a NaN, a gone robot or a
broken AI that the controlled ladder would not show. Result: 12/12 matches ran
to a verdict, no console error added by any of them, no bot lost its own
weapon, no seam failure, part counts sane throughout.

It also surfaced the SECOND-ORDER EFFECT of FIX 1, which I am naming rather
than leaving for the critic to find:
  turning weapon-respect down does not only let YOUR weapon land - it drives
  the BOT's weapon into you as well.
  tipper  (Rookie,   now av 0.00) dealt 585 and 430 LIMB damage in its two runs
  ripper  (Veteran,  now av 0.55) dealt 419 and 275 limb
  widowmaker (Champion, av 1.00, unchanged) dealt 1043 and 568 limb
So the Rookie rung is now "we both bring our weapons" rather than "neither
does". That is a live design question for round 3 - a Rookie arguably should
not be scoring 585 with its own weapon - and it is a RECIPE question (which
roster bots carry limbs at which tier), which is exactly the half of the ladder
the critic filed as MAJOR and which I deliberately did not touch.

## WHAT I DECLINED, AND WHY
1. CRITIC MAJOR "Judge() reads structure before damage, so aggression is
   dominated on the criterion checked first". DECLINED THIS ROUND.
   a) The order is not an accident: FightManager.cs:549 documents it as a fix a
      PREVIOUS critic explicitly asked for ("damage that never removes a piece
      is scratch"). Reversing a requested fix needs more than one round's data.
   b) The critic's measurement is a NOWEAP player. "Charging costs you 10x your
      structure and takes ZERO pieces off theirs" is close to a tautology when
      the player has no weapon - of course an unarmed machine removes no pieces.
   c) THE PREMISE MOVED UNDER IT. With FIX 1 the armed player takes 1.75-2.00
      enemy pieces per match at Rookie (D4) against 1.00 before, and 6.20 in the
      avoidance-off arm (D3a) against 1.20. The structure column was dead
      because the weapon was; it is no longer dead. Re-measure the card BEFORE
      re-ordering it. That is a round-3 job with a real fixture.
2. CRITIC MAJOR "shipped roster ladder points the same wrong way". DECLINED as
   recipe work, and because shipping a knob change AND a recipe change to the
   same subsystem in one round is exactly what the brief forbids. D5 shows the
   recipes still dominate the taken-damage column; that is round 3's.
3. CRITIC MODERATE M7 (batch clustering): TESTED as asked, NOT FIXED. See D2.
   The cheap interleave test the critic specified has been run; it does not
   rescue the i.i.d. model, and every claim in this document is therefore stated
   as an event count or an ordering, never as a match total.
4. Standing findings 1, 2, 5, 6, 7, 8: no new evidence gathered, none re-filed.
   Finding 9 (ISO capture pane): not confirmed an eighth time.
5. Rotary HIT_COOLDOWN = rotor period (round 1's declined candidate): DECLINED
   AGAIN, now with the measurement that settles it. See D1 - the cooldown is
   rejecting consecutive frames of ONE touch, and PART_IMMUNITY (0.5 s, shared,
   and NOT bypassed by shortening the actuator's own gate) would eat every
   extra bite while still charging the rotor 40% of its energy per swallowed
   bite. It would cost rotor speed and buy nothing. `imm ate limb 0` in 35 of
   35 runs this round is the number that says so.

## EDITOR LEFT
One fresh BuilderManager, 0 probe GameObjects, Build mode,
qa_owen_build_SPINDLE loaded (Validate OK), opponent mauler / Veteran,
Time.timeScale 1, Phase0Input.debugFire false, debugPointer false,
AIController.avoidOverride -1 (the test hook is off).
Console: 6 entries, all the documented post-reload BuilderManager.OnGUI:2812
NRE burst from my last domain reload (newest timestamp 11:58:50). The ladder and
roster sweeps - 36 matches, ~30 minutes of play - ran entirely AFTER that
timestamp and added nothing to it.
qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes,
UNCHANGED. qa_owen_build_SPINDLE.txt read only, unmodified.

## FILES CHANGED
  Assets/Phase1/Scripts/EnemyRoster.cs     (+ .rb2dev.bak)   FIX 1: WeaponRespect
  Assets/Phase1/Scripts/AIController.cs    (+ .rb2dev.bak)   FIX 1: rtAvoid, av-scaled aim
  Assets/Phase1/Scripts/BuilderManager.cs  (+ .rb2dev.bak)   FIX 1: tier line names it
  Assets/Phase1/Scripts/RB2Dev.cs          (new)             instrument
## FILES WRITTEN
  Assets/Phase1/qa_rb2_dev.md        this file
  Assets/Phase1/qa_rb2d_m7.txt       11 matches, the funnel that settled M5
  Assets/Phase1/qa_rb2d_avoid.txt    19 matches, avoidance ON vs OFF interleaved
  Assets/Phase1/qa_rb2d_ladder.txt   24 matches, tier ladder before/after
  Assets/Phase1/qa_rb2d_roster.txt   12 matches, shipped-roster regression
