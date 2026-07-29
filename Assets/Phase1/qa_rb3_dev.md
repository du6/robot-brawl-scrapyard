# ROUND 3 DEV (of 3) - Robot Brawl: Bolt & Blade
Date 2026-07-27. Role: implementation. Appended continuously.

## SOURCE REGIME ON ARRIVAL (md5) - matches the round-3 critic's stamp exactly,
## so its findings describe code that is still running.
AIController.cs      bfcc2454e1be8e3b6ea8d7ee52ce16c0
BuilderManager.cs    670e092c5541d1b55701a52bc0a51a41
EnemyRoster.cs       bc7f0b9aa8c01ed883862596db7846e2
CompoundRobot.cs     41be3e105ba49c662fac77f984248f16
qa_owen_build_SAFE.txt  f4e052c2081911eeadc5502e86a63a4b  (SACRED, verified at start)

## D0 - editor state on arrival
1 BuilderManager, 0 ModeSelect, Build mode, timeScale 1, fire/ptr false.
Console Error buffer held 15 entries, ALL stamped 11:58:50 - the critic's
documented arrival flood, pre-recovery. No new errors. Note the build loaded
was NOT the SPINDLE build the brief claims: cost 73 cr, Validate() =
"Needs at least 1 wheel." The critic's exit note says it destroyed its harness
objects and returned to Build; the loaded snapshot did not survive. Not a bug,
but the brief's "SPINDLE loaded" line is stale - recorded so the next agent
does not measure against an assumption.

## D1 - THE INSTRUMENT: testing the critic's CAUSE, not its finding
The round-3 critic's CRITICAL 3 does not just say "the ladder is inverted"
(that is round 2's, confirmed). It makes a CAUSAL claim: "with avoidance forced
equal the inversion is fully intact, so the cause is the SPEED knobs". It
proved that by holding AVOIDANCE constant. That is only half the argument: it
shows avoidance is not sufficient to explain the inversion, not that speed is.

The missing half is the mirror: hold SPEED constant and let avoidance vary. If
the inversion survives THAT too, then NEITHER knob axis explains it and the
critic's stated cause is as wrong as round 2's was.

Added (instrument only, shipped value is a no-op):
  EnemyRoster.speedTierOverride  (int, -1 = off = shipped)
      forces one tier's DecisionInterval / SteerAggr / OpeningThrottle /
      Aggression onto EVERY tier; WeaponRespect stays per-tier.
  RB2Dev policy tokens "+flatspeedR/V/C", restored after every run, and the
  aiState line now stamps spdOvr / dInt / openThr so the override is visible
  in the raw rows rather than assumed.
Verified live before measuring: speedTierOverride=1 gives Rookie dInt 0.45 ->
0.20 and Champion 0.10 -> 0.20; -1 restores 0.45 / 0.10.

## M1 - IS THE CAUSE THE SPEED KNOBS?  (qa_rb3d_M1_speedcause.txt)
Player = owen's SPINDLE build held constant. Opponent recipe = mauler held
constant. 6 cases interleaved (reps outer), n=4, 45 s timebox, speed 4,
fire=true. SHIP_* = shipped knobs. FLAT_* = identical except every tier runs
VETERAN speed knobs, so between FLAT rows the ONLY difference is WeaponRespect.
[running - results appended below]

### M1 RESULT (n=4/case, 24 matches, interleaved, raw rows in qa_rb3d_M1_speedcause.txt)
case            n  Ehits  pTaken  pT/match  pDealt  limbH  ramH  close_s  zeroWpn
SHIP_Rookie     4     44     746      186     1259     20    46     77.9     1/4
SHIP_Veteran    4     28     561      140     1159     17    32     93.7     2/4
SHIP_Champion   4     18     177       44      824     33    36    128.0     2/4
FLAT_Rookie     4     20     460      115      249      3    18     51.3     1/4
FLAT_Veteran    4     34     561      140      631      9    26     97.0     0/4
FLAT_Champion   4     47     467      117      659      1    62    113.7     3/4
("Ehits" = enemy hits LANDED on the player, ram+limb. The project's noise floor
is on damage MEANS, so I claim counts and orderings only.)

**A FREE NOISE CONTROL FELL OUT OF THIS DESIGN.** SHIP_Veteran and FLAT_Veteran
are the SAME configuration - forcing Veteran speed onto Veteran changes nothing,
and both run avoid 0.55. They are a pure replicate pair inside one interleaved
batch: Ehits 28 vs 34 (1.21x), pTaken 561 vs 561 (1.00x). So on THIS instrument
at n=4, replicate noise on hit counts is about 1.2x. Every ratio I claim below
is 2.2x or more.

### M1 FINDING 1 - THE CRITIC'S CAUSE IS CORRECT, AND CONFIRMED ON THE MIRROR TEST
SHIPPED ladder (both axes live):   Ehits 44 / 28 / 18  - MONOTONE DECREASING (inverted).
SPEED HELD CONSTANT (only avoid):  Ehits 20 / 34 / 47  - MONOTONE INCREASING (correct).
Kill the speed ladder and the inversion does not merely shrink, it REVERSES into
the intended direction. The round-3 critic held avoidance constant and showed
the inversion survived; I held SPEED constant and it vanished. Both halves now
agree: the SPEED knobs are the cause. I set out to refute this and could not.

### M1 FINDING 2 - ROUND 2'S SHIPPED FIX IS CORRECTLY SIGNED AND IS BEING CANCELLED
This is the part nobody has been able to see before, because the two axes have
never been separated in the same batch. With speed held at Veteran, the ONLY
difference between the FLAT rows is EnemyRoster.WeaponRespect:
    avoid 0.00 -> 20 Ehits      avoid 0.55 -> 34      avoid 1.00 -> 47
Monotone, 2.35x end to end, against 1.21x replicate noise. Round 2's knob works
and points the right way. It is not a failed fix - it is a working fix whose
signal is being overwritten by a larger, wrongly-signed one sitting on top of it.
The round-3 critic scored round 2 as having "targeted the wrong axis". More
precisely: it added a RIGHT axis and left the WRONG one running.

### M1 FINDING 3 - THE CLEANEST PAIR: CHAMPION'S OWN SPEED KNOBS COST IT 2.6x
SHIP_Champion vs FLAT_Champion differ in NOTHING except the four speed knobs
(avoidance is 1.00 in both, since Champion already ran 1.00):
    Ehits 18 -> 47 (2.6x)   pTaken/match 44 -> 117 (2.7x)
And it is NOT a contact-opportunity story in the usual direction: SHIP_Champion
has the MOST close-contact time in the whole batch (128.0 s <1.3 m) and lands
the FEWEST hits. It is right next to the player for more of the match than
anything else measured and cannot convert it. Speed does not stop the Champion
reaching you; it stops it landing once it is there.
Symmetric pair at the bottom: SHIP_Rookie 44 vs FLAT_Rookie 20 (2.2x) - the
Rookie's SLOW knobs are the most contact-converting set on the ladder.

### WHAT I HAVE NOT ESTABLISHED YET
Which of the four speed knobs does it. "Veteran's set is better than Champion's"
is not the same as "OpeningThrottle is the culprit", and this project's
recurring mistake is fitting a constant it never isolated. M2 isolates it before
anything ships.

## FIX 1 - THE DISC MIGRATION (round-3 critic CRITICAL "silently disarmed
## owen's own saved build; no migration exists").  BuilderManager.cs

REPRODUCED FIRST, on the shipped code, before touching anything:
  qa_owen_build_SAFE.txt    parts=16 cost=750 Validate=OK limbs=0 undrivenDiscs=1
  qa_owen_build_SPINDLE.txt parts=17 cost=786 Validate=OK limbs=1 undrivenDiscs=0
Validate() returns OK on a machine with no working weapon. Confirmed exactly as
filed.

THE GEOMETRY IS DERIVED, NOT INVENTED. Along the disc's own mount axis:
    spindlePos = discPos - u*halfDisc + u*halfSpindle
    newDiscPos = discPos + u*2*halfSpindle
Run against SAFE this predicted, BEFORE any code was written:
    spindle (0.000, 0.700, 0.900)   disc (0.000, 0.700, 1.100)   Aluminum, +36 cr
owen's own hand-migrated SPINDLE file:
    spindle (0.000, 0.700, 0.900)   disc (0.000, 0.700, 1.100)   Aluminum, 750->786
The rule reproduces the human repair EXACTLY, to the millimetre, the material
and the credit. That agreement is the only thing that would justify doing this
to somebody's saved machine automatically, and it is why I shipped a migration
rather than a fourth warning label.

MEASURED, same instrument both sides:
                            BEFORE                    AFTER
  SAFE parts                16                        17
  SAFE cost                 750 cr                    786 cr
  SAFE limbs (LimbReport)   0                         1
  SAFE undriven discs       1                         0
  SAFE disc position        (0, 0.700, 0.800)         (0, 0.700, 1.100)
  SPINDLE (control)         17 / 786 / 1 limb / 0     17 / 786 / 1 limb / 0
The SPINDLE row is the idempotence control: an already-driven disc is not
touched and nothing is double-inserted.

EVERY FAILURE MODE IS A REFUSAL. The migration skips the disc - leaving the
existing builder warning, i.e. exactly what shipped an hour ago - if the disc
has no mount axis, if the spindle or the moved disc would OVERLAP an existing
part, if the spindle would not actually bridge parent and disc, or if the
insert would break CREDIT_BUDGET. It never writes the player's file: the
snapshot on disk is untouched until they choose to save, so it is undoable by
not saving. SACRED qa_owen_build_SAFE.txt re-checked at 790 bytes after the run.

### THE OTHER SIDE OF FIX 1 - and it needed a second fix
Asking "what is the other side of this?" found the flaw immediately. The
builder says a disc bolted to the frame is LEGAL ("or keep it as a fixed
edge"). A migration that fires on every load would therefore quietly rebuild
a machine whose author DELIBERATELY chose a fixed edge under the new rules -
the same silent-mutation harm, pointed the other way. The critic had already
named the missing piece: "no version stamp on the snapshot format".
FIX 1b: SnapshotString() now writes "#fmt3-disc" as its first line, and the
migration runs ONLY on a file that lacks it. A file saved before the rules
changed is repaired; a file saved after is left exactly as authored. The stamp
is inert to both parsers - LoadSnapshot drops any line with fewer than 4
'|'-fields, so an older build of the game also loads a stamped file unharmed.
Other sides checked: BuildCost/Validate/the power panel all recompute from
`placed`, so the 786 cr and the new limb show up on the build screen in the
same pass; the enemy roster is authored in code, not loaded through
LoadSnapshot, and tipper/widowmaker already carry their spindles.
STILL TO VERIFY: that the ARENA agrees - that the migrated SAFE build actually
spawns an Actuator and lands limb damage. Builder-vs-arena disagreement is this
project's signature failure and a builder-side count is not evidence. M3 below.

## M2 - WHICH SPEED KNOB?  (qa_rb3d_M2_knobisolate.txt)
All five arms are CHAMPION tier vs mauler, so WeaponRespect is 1.00 in every
arm and cannot contribute. Each arm is the Champion speed set with exactly ONE
knob relaxed to the Veteran value. n=4, interleaved, 45 s, speed 4, fire=true.
Knob stamps verified in the raw rows, not assumed.

arm        dInt  openThr  steer | Ehits  pTaken/match  close_s
C_SHIP     0.10   0.80    0.06  |   24        50        138.6
C_thrV     0.10   0.55    0.06  |   42       183        109.3
C_dintV    0.20   0.80    0.06  |   40       176         86.7
C_steerV   0.10   0.80    0.10  |   23        64        110.0
C_VET      0.20   0.55    0.10  |   37       164         97.1

### RESULT - TWO KNOBS DO IT, THE THIRD DOES NOTHING
  OpeningThrottle 0.80 -> 0.55 alone:   Ehits 24 -> 42 (1.75x), pTaken 50 -> 183 (3.7x)
  DecisionInterval 0.10 -> 0.20 alone:  Ehits 24 -> 40 (1.67x), pTaken 50 -> 176 (3.5x)
  SteerAggr 0.06 -> 0.10 alone:         Ehits 24 -> 23 (1.04x), pTaken 50 ->  64
C_SHIP vs C_steerV is effectively a second replicate pair (1.04x on counts),
which agrees with M1's 1.21x replicate estimate. The two live knobs clear both
that and the project's 2.4x damage noise floor on pTaken.
Not additive: relaxing EITHER one alone (42, 40) already reaches or beats
relaxing all three (37). The mechanism saturates - one term is enough to keep
the machine on the player.

### AN UNASKED-FOR RESULT: THE AI's OWN WEAPON NEVER LANDS EITHER
Enemy limb hits are 0 in 20 of 20 matches in this batch (eLimb column; all 24-42
enemy hits are RAM). The mauler carries a weapon and never lands it. So R1's
CRITICAL - "weapons do not separate from an unarmed nose" - is not a property of
the player's fixture or of owen's build. It is symmetric: NEITHER side's weapon
converts. Every previous round has measured this from the player's seat only.
Recorded, not filed as new - it is the same defect seen from the other chair,
and that is worth knowing before anyone tries to fix it on the player side again.

## FIX 2 - THE SPEED LADDER (round-3 critic CRITICAL 3).  EnemyRoster.cs
SHIPPED, with the measurement written into each doc comment:
  DecisionInterval  0.45 / 0.20 / 0.10  ->  0.20 flat
  OpeningThrottle   0.40 / 0.55 / 0.80  ->  0.55 flat
  Aggression        0.75 / 1    / 1     ->  1    flat
  SteerAggr         0.22 / 0.10 / 0.06  ->  UNCHANGED, because M2 measured it inert
  WeaponRespect     0    / 0.55 / 1     ->  UNCHANGED (round 2's fix; M1 says it works)
Difficulty now rides on the axis that measured monotone, and the axis that
measured backwards has been taken out of the ladder rather than re-fitted.
I did not invert the speed ladder, which the data would also have supported
(the Rookie's slow knobs were the most dangerous set measured). Inverting it
would ship "the Champion has the slowest reaction time on the roster", a
sentence the game cannot defend to a player. Flat is the honest version of
what the data says: these knobs are not a difficulty axis in this game.

## M3 - VERIFICATION AFTER BOTH FIXES  (qa_rb3d_M3_verify.txt)
Same instrument, same build, same timebox as M1. n=4/case, interleaved, 28
matches. Knob stamps confirm dInt 0.20 / openThr 0.55 for all three tiers and
steer 0.22 / 0.10 / 0.06 - the shipped ladder, not an override.

ENEMY HITS LANDED ON THE PLAYER (the axis round 2 and round 3 both measured):
                        Rookie   Veteran   Champion
  mauler  BEFORE (M1)      44       28        18     INVERTED
  mauler  AFTER  (M3)       7       34        47     MONOTONE INCREASING
  ripper  AFTER  (M3)      68       72       108     MONOTONE INCREASING
damage TAKEN per match, mauler:  62 / 182 / 134  ;  ripper: 374 / 538 / 583.
The mauler Veteran-vs-Champion pair (182 vs 134) is 1.36x and I do not claim it
- damage means sit inside the 2.4x floor. The COUNTS are the claim, and they
are monotone on both recipes.
Round 2 checked its knob only against damage the player DEALS. That is the
one-side-only failure the critic named. This fix is measured on damage the
player TAKES (the thing the ladder is actually about) on two recipes, and the
DEALT side is reported alongside it: pDealt 184 / 530 / 827 (mauler), which
also rises with tier and does not invert.

### FIX 1's OTHER SIDE - THE ARENA AGREES  (SAFEMIG_Vet in M3)
The decisive check: feed the arena the RAW pre-conversion qa_owen_build_SAFE.txt
and see whether it spawns armed.
  BEFORE (critic M3): "rotor line EMPTY", ZERO Actuator components, limb 0/0 in
                      6 of 6 matches.
  AFTER  (this batch, 4 matches, the file byte-identical and unwritten):
    rotor [spindleZP kind=Spindle edgeMean 12.36-12.39 edgeMax 12.50
           aboveGate 43.7s tipR 0.170 maxRate 73.5] supplyFrac 1.00,
           weaponPartLost none
    player limb hits 2, limb damage 110
12.39 m/s edge and 43.7 s above the bite gate are the SAME numbers the critic
measured on owen's hand-migrated SPINDLE build. Builder and arena agree; the
migrated machine is armed where it matters.

### REGRESSION CHECKS - the things I did NOT change, re-run
 1. ROUND-TRIP. raw SAFE -> 17 parts (migrated) -> SnapshotString (stamped) ->
    reload -> 17 parts. Not 18. No double-migration.
 2. THE STAMP GUARD ACTUALLY GUARDS. Hand-built a STAMPED file carrying a disc
    bolted to the frame - i.e. a deliberate fixed edge authored under today's
    rules: loads 16 parts, undrivenDiscs 1, LEFT ALONE. The migration fires on
    pre-conversion files only, which is the whole point of FIX 1b.
 3. v1 SNAPSHOTS (4 fields, no material) still load: 3 parts from a 3-line v1.
 4. THE WEAPON DEFECT IS UNCHANGED, as expected - I did not touch it. Matches
    with zero player weapon damage: 15 of 28 = 54%, against the critic's 56% at
    45 s and round 2's 53% at 90 s. Third independent batch, same number. FIX 2
    neither helped nor hurt it.
 5. SACRED qa_owen_build_SAFE.txt: 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b
    - unchanged, never written.

### WHAT I DECLINED, AND WHY
 * THE CORE LOOP ("weapon power buys nothing"), the critic's CRITICAL 2 and the
   oldest open finding here. NOT FIXED. I will not pretend otherwise. Three devs
   have declined it and I am declining it too, but with one thing added that
   changes the shape of the problem: M2 shows the AI's OWN weapon lands ZERO
   limb hits in 20 of 20 matches. Every round so far has measured this from the
   player's seat and reasoned about the player's build, the player's trigger,
   the player's disc. It is not a player-side defect. Whatever gates weapon
   contact gates it for both machines, which means the next attempt should start
   at the contact geometry shared by both, not at the player's fixture.
 * THE ROSTER LADDER (standing finding 3). M3 shows the recipe still dominates
   the tier: ripper at ROOKIE tier lands 68 hits where mauler at CHAMPION lands
   47. That is recipe work, it is a second change to the same subsystem in the
   last round, and the brief forbids it. Handed forward with the number.
 * Standing findings 1, 2, 4, 5, 6, 7, 8: no new evidence gathered, none
   re-filed. Finding 9 (ISO capture pane) not confirmed an eighth time.
 * RE-FITTING WeaponRespect (the critic's MODERATE). Its justifying constant is
   AFK-fitted, which is a fair criticism, but M1 shows the knob is monotone and
   correctly signed once speed stops masking it: 20 / 34 / 47 at avoid 0 /
   0.55 / 1. Re-fitting a constant that measures correct, in the same round I
   changed the knobs sitting next to it, is how round 2's fix became invisible.

## WHAT I TRIED TO REFUTE AND COULD NOT
The critic's causal claim, "the cause is the speed knobs". I built the mirror of
its own experiment specifically to break it - hold SPEED constant, let avoidance
vary - expecting to find that neither knob axis explained the inversion, which
would have made round 3's diagnosis as wrong as round 2's. It did not break.
Ehits went 20 / 34 / 47, monotone in the intended direction, and M2 then named
the two responsible knobs individually. The critic was right, and its finding
is now proved on both halves of the argument rather than one.

## MY OWN ERRORS THIS ROUND
 1. I very nearly shipped the migration WITHOUT the version stamp. It would
    have silently rebuilt any future build whose author deliberately chose a
    fixed edge - the identical harm to the one I was repairing, pointing the
    other way. Caught by asking the brief's "what is the OTHER side of this
    fix?" question, not by testing. FIX 1b exists because of that question.
 2. My first plan was to fix the ladder by INVERTING the speed knobs, since the
    data says slower lands more. That would have shipped a Champion with a
    0.45 s reaction time. Rejected on design grounds, not measurement grounds,
    and I am flagging it because the measurement alone would have endorsed it.
 3. One device dropout silently ate the FIX 2 edit mid-write; a grep for the
    OLD constants (not just for the new comment) caught it before I recompiled
    against unchanged code. If I had only grepped for what I added, I would
    have "verified" a fix that was not there.

## EDITOR LEFT
1 BuilderManager, 0 ModeSelect, 0 harness objects. Build mode, SPINDLE build
loaded (17 parts, 786 cr). timeScale 1, debugFire false, debugPointer false.
AIController.avoidOverride -1; EnemyRoster speedTierOverride -1 and all four
per-knob overrides -1, so every instrument added this round is a no-op in the
shipped game.
CONSOLE: last Error is 13:19:11, the documented survivor-BuilderManager palette
NRE from the recompile-3 domain reload, thrown BEFORE the recovery. Editor
clock read 13:26:56 at exit - 7.75 minutes and 28 matches of measurement plus
all regression tests with ZERO new errors.
SACRED: qa_owen_build_SAFE.txt md5 f4e052c2081911eeadc5502e86a63a4b, 790 bytes,
read many times, never written. qa_owen_build_SPINDLE.txt b5fc6469d3e92ab36
daf88dc035105d1, unchanged.

## FILES
 CHANGED  Assets/Phase1/Scripts/EnemyRoster.cs      (FIX 2 + instrument)
 CHANGED  Assets/Phase1/Scripts/BuilderManager.cs   (FIX 1 + FIX 1b)
 CHANGED  Assets/Phase1/Scripts/RB2Dev.cs           (instrument tokens/stamps)
 BACKUPS  *.rb3dev.bak alongside each
 DATA     qa_rb3d_M1_speedcause.txt  qa_rb3d_M2_knobisolate.txt
          qa_rb3d_M3_verify.txt
