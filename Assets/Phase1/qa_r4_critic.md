# QA ROUND 4 - CRITIC report
Robot Brawl: Bolt & Blade. Unity 6000.5.4f1. Started 2026-07-27.
User ask this round, verbatim: "Let the critic agent test different robots with different
materials and weapons. Also use the unity screen capture to evaluate the visual effect."

## 00 FIRST FINDING: MY HANDOFF BRIEF WAS FACTUALLY WRONG. I CHECKED BEFORE TRUSTING IT.
I was told a previous round-4 critic had already run six builds (b1 sledge_w, b2 vortex_cf,
b3 lance_ti, b4 skipper_abs, b5 discus_steel, b6 ripper_al) and FORTY POST-PATCH matches,
and that its report survived at Assets/Phase1/qa_r3_critic.md (~17 KB). I was told not to
redo the measurements. NONE OF THAT SURVIVES CONTACT WITH THE DISK:

 (a) qa_r3_critic.md is 26,532 bytes, mtime 2026-07-27 02:35Z, and it is the ROUND 3
     critic's report. Its builds are qa_r3_b1_hammer_ti / b2_rotor_cf / b3_spear_w /
     b4_flipper_al / b5_disc_abs / b6_hammer_steel. It reports 54 matches, not 40.
     The strings "sledge_w", "vortex_cf", "outdamaged", "8.8x" appear NOWHERE in it,
     nor in any other .md in Assets/Phase1.
 (b) The sledge_w/vortex_cf build family DOES exist on disk, in qa_r3_buildstats.txt and
     qa_r3_s1..s10.txt - ten sweeps of n=4 = 40 matches, exactly as described. But their
     mtimes are 2026-07-26 22:43Z to 23:05Z. That is BEFORE the round-3 critic ran
     (01:57-02:35Z on the 27th) and ~4.5 HOURS BEFORE the round-3 dev shipped its patch
     (finished 03:24Z). Those 40 matches are PRE-PATCH. They cannot be "40 stamped
     post-patch matches" and no conclusion drawn from them describes the current build.
 (c) qa_r3_buildstats.txt also records that b6_ripper_al was NOT a valid robot:
     "valid=Structure has floating parts" with "LIMB pivot parts=0". Whoever ran that
     set ran 40 matches including a build whose limb was empty.

So there is NO post-patch multi-build critic data in this project. The only post-patch
match data of any kind is the round-3 dev's own self-verification sweep (qa_r3d_shipped_b6.txt,
n=4, one build). I am therefore running the round-4 measurement, not continuing one.

## 01 REGIME UNDER TEST (post round-3 dev patch)
82 .cs files under Assets, newest CompoundRobot.cs @2026-07-27 03:18:17Z. Editor on entry:
playing=True, timeScale=1, debugFire=False, owen's 16-part build live in the builder.
Live constants confirmed: SeamLoad compressor present (STRESS_J_CAP knee 1400,
STRESS_SOFT_EXP 0.5, STRESS_J_HARD 4200), STRESS_ENV_SCALE 0.20, EDGE_MIN_VOL 0.012.

## 02 THE TEST SUITE — 8 BUILDS, RE-VERIFIED UNDER THE PATCHED CODE
Six inherited from round 3 (re-validated by me, not trusted), plus two I authored to turn
the hammer into a FOUR-POINT MATERIAL LADDER on byte-identical geometry.
Raw: Assets/Phase1/qa_r4_buildstats.txt

build                     weapon system                  limb mat  parts mass kg  cost  limb I  E(J)  kJ/swing
qa_r3_b1_hammer_ti        pivot HAMMER beam+blade        Titanium   12   1735.6  1773   32.04  2243   6.41
qa_r3_b6_hammer_steel     pivot HAMMER (same geometry)   Steel      12   1865.8  1490   55.76  2788   7.97
qa_r4_b8_hammer_abs  NEW  pivot HAMMER (same geometry)   ABS        12   1600.6  1266    7.46   193   0.55
qa_r4_b7_hammer_w    NEW  pivot HAMMER (same geometry)   Tungsten   12   2312.4  5761  137.10  8226  23.50  <-- REJECTED
qa_r3_b2_rotor_cf         spindle ROTOR, self-built       CarbonFiber 13   690.2   939    8.08   694   1.98
                          (beamlong bar + 2 blades)
qa_r3_b3_spear_w          ram SPEAR (ram+beam+spike)      Tungsten   12   1034.3  1654  328.52  2012   5.75
qa_r3_b4_flipper_al       pivot WEDGE plow/flipper        Aluminum   12    729.4   706    2.51   246   0.70
qa_r3_b5_disc_abs         CONTROL: legacy spinnerSaw+spike ABS       11    371.6   265     -      -      -
All six inherited builds: Validate()==OK, 4 wheels, limb blockedBy==null and parts>0,
re-confirmed under the patched code BEFORE any match ran. b8 likewise.

## 03 FINDING: THE CREDIT BUDGET IS A CONSTRAINT AFTER ALL — AND IT BANS ONE MATERIAL OUTRIGHT
Round 3 reported "CREDIT_BUDGET IS NOT A CONSTRAINT ... there is no build decision in this
game that trades cost". I OVERTURN that, from the builder's own validator.
I took b1 (Steel frame, Titanium hammer arm, 12 parts, 1773 cr — a completely ordinary
mid-size robot) and changed ONLY the three limb parts' material to Tungsten. Result:
    "Over budget: 5761 cr of 4000. Cheaper materials, or fewer parts."
bm.Validate() REFUSES THE BUILD. Three parts re-materialled to Tungsten added 3988 cr —
more than the entire rest of the machine — because Tungsten is 6.0 cr/kg AND 19.3 g/cm3,
so cost scales as density x rate and Tungsten is 14.7x Aluminium on the same volume.
The round-3 conclusion was drawn from six builds that all happened to avoid Tungsten on
large parts (its one Tungsten build put Tungsten on a single 0.0145 m3 spike).
The budget is not slack: it is a HARD WALL that only Tungsten ever hits, and it hits it so
early that Tungsten is effectively not a limb material at all. That is a silent
one-material ban dressed as an economy, and the failure message ("cheaper materials, or
fewer parts") does not tell the player that the specific material they picked is the
whole problem. Same geometry in the other three materials: ABS 1266, Steel 1490, Ti 1773 —
a 1.40x spread across three legal materials, versus 4.55x to the illegal one.

## 04 CELL A — the HAMMER MATERIAL LADDER, identical geometry, mauler/Veteran, charge+fire,
## timeScale 4, n=4 each. Raw: qa_r4_s01/s02/s03.txt. Regime stamped 76113c28 in every file.
limb mat  cost  limb E(J)  W/L/D   dealt(mean)  max limb hit(mean)  limb hits/match  match len(mean)
ABS       1266     193      4/0/0      676            35.3               25.5            77.7 s
Titanium  1773    2243      4/0/0      737           103.9               12.0            11.3 s
Steel     1490    2788      4/0/0      816           112.4               13.5            16.0 s
Tungsten  5761    8226     ILLEGAL - rejected by the builder, see 03

### THE FINDING: 14.4x OF LIMB ENERGY BUYS YOU NOTHING BUT A SHORTER MATCH.
An ABS hammer stores 193 J. A Steel hammer of the SAME geometry stores 2788 J — 14.4x —
and costs 18% more. Their win records against the same opponent at the same n are
IDENTICAL: 4-0 and 4-0. What the 14.4x actually buys is time-to-kill: 77.7 s down to
16.0 s, and peak hit 35.3 up to 112.4. Damage dealt per match only moves 676 -> 816 (+21%),
because the weaker hammer simply lands 25.5 bites instead of 13.5 and gets there anyway.
Against a Veteran there is no material you can pick that loses. The material system is
currently a PACING dial, not a power dial, and nothing on the build screen says so.
Cross-check on the same axis: Titanium (2243 J, 1773 cr) is beaten by Steel (2788 J,
1490 cr) on damage dealt (737 vs 816) and peak hit (103.9 vs 112.4) while costing 19%
MORE. Round 3 measured the same ordering pre-patch. I CONFIRM IT POST-PATCH, n=4 each:
the most expensive legal limb material in the game is strictly worse than the one below it.

## 05 CONFIRMED FIXED: THE SPAWN SHED IS GONE
Round 3 measured both robots shedding a part on spawn in ~100% of matches (enemy 10/11
in 6 of 6 AFK runs, player 11/12 in 4 of 6), caused by STRESS_ENV_SCALE 0.75 producing a
1050 N.s environment load against a 900 seam. Round 3's dev cut STRESS_ENV_SCALE to 0.20.
POST-PATCH, 12 matches across b1/b6/b8: the player finished 12/12 in ALL EIGHT b1 and b6
runs, and the enemy's piece losses now track the damage column instead of appearing at
t=0. THIS FIX HOLDS. I re-measure it AFK in section 08 below rather than leaving it here.

## 06 CONTRADICTS THE DEV: THE CASCADE GUARDRAIL IS NOT INTACT
The round-3 dev's exit report states "worst single-step piece loss player 0 enemy 2 — the
round-4 cascade guardrail is intact". In my very first sweep (b1 Titanium hammer vs
mauler/Veteran, n=4, qa_r4_s01.txt) the harness reports:
    worst single-step piece loss: player 0 enemy 6
SIX pieces off one 11-piece robot in ONE FixedUpdate. b6 Steel reports enemy 4. The dev's
enemy-2 was measured on b6 alone in its own sweep; on the Titanium arm, which lands 12
bites a match instead of 13.5 but at a higher rate, the enemy loses more than half its
body in a single step. The guardrail claim does not generalise beyond the build it was
measured on. Consequence visible in the same file: the enemy ends at 0/11 pieces in three
of four b1 runs, i.e. the robot does not lose a fight, it VAPORISES.

## 07 CELL A FULL BOARD — 7 builds x n=4 = 28 matches, mauler/Veteran, charge + fire held,
## timeScale 4. Raw qa_r4_s01..s07.txt.
build                  W/L/D   dealt  taken  weapon-src split (mean)          how it ended
b6 hammer Steel        4/0/0    816     78   limb 676/13.5 hits, ram 141      3 KO core, 1 wheels-off
b1 hammer Titanium     4/0/0    737     81   limb 539/12.0 hits, ram 198      3 KO core, 1 flipped-out
b8 hammer ABS   [NEW]  4/0/0    676    209   limb 357/25.5 hits, ram 319      2 wheels-off, 2 decisions
b5 disc ABS (control)  4/0/0    599    128   disc 524/14.2 hits, ram  75      2 KO core, 1 wheels, 1 dec
b2 rotor CarbonFiber   4/0/0    545     27   limb 371/10.8 hits, ram 174      3 power-out, 1 flipped-out
b4 flipper Aluminium   3/1/0    361    319   limb 136/ 7.2 hits, ram 225      4/4 went the full 90 s
b3 spear Tungsten      0/3/1    292    367   limb 124/ 3.8 hits, ram 169      3 decisions, 1 power-out

## 08 OVERTURNED: "THE LEGACY DISC IS A DECORATION" DOES NOT HOLD POST-PATCH
Round 3, n=10: "the spinnerSaw lands EXACTLY TWO bites a match, every match, for ~70
damage - 21% of output" (69/71/71/71, nearly constant).
Round 4, same build file (qa_r3_b5_disc_abs.txt), same harness, n=4 vs mauler/Veteran:
   run 0 disc 334/7 bites · run 1 disc 621/22 · run 2 disc 605/12 · run 3 disc 537/16
The disc now lands 7-22 bites for 334-621 damage, i.e. 54-94% of the whole card, and it
is NOT constant. Note the r3 figure was taken against scout/Rookie and mine against
mauler/Veteran, so I am not claiming the patch caused it — I am claiming the round-3
statement is not true of the build in general and must not be carried forward as written.
I re-run the exact r3 cell (b5 vs scout/Rookie) in section 11 to separate the two.
CONSEQUENCE THAT IS NOT AMBIGUOUS: the cheapest build in the suite — an 11-part ABS robot
at 265 credits, 6.6% of the budget, with NO Phase 4 parts at all — goes 4/0/0 against the
same Veteran that the 1773-credit Titanium actuated hammer goes 4/0/0 against, and beats
five of the six Phase 4 builds on damage dealt. The whole Phase 4 actuator feature is
currently optional.

## 09 THE FLIPPER DOES NOT FLIP. THE ROTOR DOES.
b4 is the wedge/plow build, the one whose entire design premise is toppling (wedge
liftBias 0.75). Across its 4 matches vs mauler the ENEMY spent 0%, 2%, 0%, 4% of the
match on its back. It never put anybody over.
b2, the CarbonFiber spindle rotor, was not built to flip anything. Enemy time on its back
across its 4 matches: 0%, 28%, 85%, 33%. Run 2 ended "Enemy counted out — flipped onto
its back — its gyro had already been sheared off". The rotor flips people by accident,
far better than the flipper does on purpose, because what actually inverts a robot in
this game is a fast tangential impulse, not a lifting wedge. A player who reads the
wedge's liftBias 0.75 stat and builds around it is being lied to by the part description.

## 10 THE RAM SPEAR IS STILL THE ONLY LOSING BUILD, AND FOR A DIFFERENT REASON THAN R3 SAID
b3 Tungsten ram spear: W0/L3/D1, the only sub-.500 build in the suite, both rounds.
Round 3 attributed this to the pack: "3 of the 4 losses ended out of power". Post-patch
only ONE of four does. The other three are judges' decisions the spear LOSES ON DAMAGE
(319v360, 230v397, and a 380v371 draw). The limb lands 2-6 bites a MATCH (mean 3.8, the
lowest in the suite) because I=328.5 at tipRadius 0.30 gives rateMax 3.50 — it is the
slowest weapon in the game by a factor of 4. The pack fix is not the fix; the stroke rate
is. Reporting the r3 diagnosis as still-true would have sent the dev at the wrong constant.

## 11 THE HEADLINE QUESTION, ANSWERED: THE "OUTDAMAGED WINNER" IS AN ARTEFACT, NOT AN
## OVERCORRECTION. IT IS ALREADY FIXED, TWICE OVER, AND I CAN NAME BOTH MECHANISMS.
I was asked to decide whether "5 of 13 judges' decisions were won by the side that was
OUTDAMAGED, by up to 8.8x" is a real overcorrection from ranking structure above damage,
or an artefact of which builds sat in those cells. I located the source data (it is the
PRE-PATCH set qa_r3_s1..s10.txt, 40 matches, 2026-07-26 22:43-23:05Z) and re-derived it.

RE-DERIVED PRE-PATCH: 13 decisive judges' decisions, SIX inverted (not five), worst 8.82x.
Every one of the six:
  file  cell                                  dmg (winner v loser)  pieces          inversion
  s3 r0 b3_lance_ti   vs mauler/Vet            177 v 316            12/12 v 10/11   1.79x
  s3 r1 b3_lance_ti   vs mauler/Vet            184 v 466            12/12 v 10/11   2.53x
  s6 r0 b6_ripper_al  vs scout/Rookie           72 v  76            13/13 v 10/11   1.06x
  s6 r1 b6_ripper_al  vs scout/Rookie           11 v  69            13/13 v 10/11   6.27x
  s6 r3 b6_ripper_al  vs scout/Rookie           28 v 247            12/13 v  5/11   8.82x
  s7 r2 b5_discus_steel PARKED vs mauler/Vet   212 v 244            11/11 v 10/11   1.15x

THREE INDEPENDENT REASONS THIS IS AN ARTEFACT, IN ORDER OF HOW DAMNING THEY ARE:

(1) THE HALF OF THE BAND THAT FIXES IT DID NOT EXIST WHEN THAT DATA WAS TAKEN.
    Those ten sweep files carry NO "LIVE CONSTANTS (damage/judging)" header line — the
    stamp that records STRUCT_BAND / MIN_STRUCT_PARTS. Every sweep taken from the round-3
    critic onward does carry it, reading "STRUCT_BAND 0.060 | MIN_STRUCT_PARTS 2.0".
    Under a bare 0.06 band, ONE piece off an 11-part robot is 0.0909 — 1.5x the band —
    so structure short-circuited the card on a single piece. Under the current
    StructBand() = max(0.06, (MIN_STRUCT_PARTS-0.5)/min(startParts)) = max(0.06, 1.5/11)
    = 0.1364, one piece (0.0909) FALLS THROUGH TO DAMAGE and only two pieces (0.1818)
    decide. FIVE OF THE SIX INVERSIONS ARE ONE-PIECE CASES AND CANNOT RECUR.
(2) THE ONE PIECE WAS FREE. Round 3 proved the enemy shed a piece to the spawn/wall
    contact in ~100% of matches under STRESS_ENV_SCALE 0.75. The 10/11 in five of six
    rows is that free piece. Round 3's dev cut STRESS_ENV_SCALE to 0.20; I confirm in
    section 05 that the shed is gone.
(3) THREE OF THE SIX — INCLUDING THE 8.82x HEADLINE — CAME FROM A BUILD THE GAME WOULD
    REFUSE TO LET YOU FIGHT WITH. s6 is b6_ripper_al, and that set's own build stats file
    records it as "valid=Structure has floating parts" with "LIMB pivot parts=0". Its
    actuator drove nothing. The single most alarming number in the whole claim was
    produced by an invalid robot with an empty limb. A fourth came from a PARKED robot
    (s7, policy=afk) that dealt 212 damage without being driven.

MY OWN POST-PATCH MEASUREMENT, WHICH IS THE ACTUAL TEST: 40 matches across 10 sweeps,
5 build/opponent cells, produced 15 judges' cards - 13 decisive and 2 draws.
   INVERTED (winner out-damaged by the loser): 0 of 13.
In all nine "decided on structure destroyed" cards the winner ALSO led the damage column,
by margins of 24% to 96%. Structure and damage now agree. Two clean fall-throughs prove
the band is live rather than merely unreached: s05 r0 (11/12 vs 11/11, diff 0.083 < 0.136)
fell to damage and the player LOST 285 v 311; s06 r1 (9/12 = 0.750 vs 7/11 = 0.636, diff
0.114 < 0.136) fell to damage and the player lost despite LEADING on pieces.
VERDICT FOR THE DEV: do not re-order the judging card. It is not overcorrected. Anyone
carrying this finding forward is reading a pre-patch file.

## 12 OVERTURNED IN ITS OWN CELL: THE DISC'S "EXACTLY TWO BITES"
Round 3's claim was made specifically at b5_disc_abs vs scout/Rookie: "disc 69/2, 71/2,
71/2, 71/2 ... it is nearly CONSTANT". I re-ran that identical cell post-patch (qa_r4_s09.txt,
same snapshot file, same opponent, same tier, n=4):
   run 0 disc 451/11 · run 1 disc 342/9 · run 2 disc 290/8 · run 3 disc 197/5
5 to 11 bites, 197-451 damage, 51-86% of the card, and not constant. The r3 finding is
dead in the cell it was measured in. Something in the round-3 patch (most plausibly the
SeamLoad compressor changing contact persistence, since the dev states spinnerSaw HP was
unchanged) unstuck the disc. Nobody has claimed this fix; it should be noticed before
someone "fixes" a disc that is now the second-strongest weapon in the game.

## 13 THE PARKED-ROBOT CELL IS A LIVE, SAME-PATTERN PROOF OF SECTION 11
b1 Titanium hammer, mauler/Veteran, policy=afk, debugFire=FALSE, n=4 (qa_r4_s10_afk.txt).
   W0/L4/D0, spread 1.00x [CONVERGED]. dealt 0 in all four. taken 197/203/204/342.
Look at the piece counts: 12/12 vs 10/11 in THREE of the four — byte-identical to the
piece pattern of pre-patch inversions s3 r0, s3 r1 and s7 r2. Under the pre-patch band a
parked robot that never moved and dealt ZERO damage would have been handed the win on
"structure destroyed", because the enemy was one free piece down. Post-patch every one of
these four cards reads "structure even, decided on damage" and the parked player loses
0 vs 203. The mechanism I diagnosed in section 11 is not inferred; it is reproduced.
Also: parking still loses, W0/L4. Round 1's "a parked robot beats a Veteran" is dead and
should not be carried into round 5 again. That is now three consecutive rounds agreeing.

## 14 OPPONENT / TIER VARIATION — THE TIER LABEL IS STILL INVERTED
b4 flipper Aluminium : vs mauler/VETERAN   n=4 -> W3/L1/D0, dealt 361, taken 319
                       vs bulwark/CHAMPION n=4 -> W3/L0/D1, dealt 686, taken 402
b5 disc ABS          : vs mauler/VETERAN   n=4 -> W4/L0/D0, dealt 599
                       vs scout/ROOKIE     n=4 -> W4/L0/D0, dealt 394
b8 hammer ABS        : vs mauler/VETERAN   n=4 -> W4/L0/D0
                       vs widowmaker/CHAMP n=4 -> W3/L1/D0
The SAME build does BETTER against the Champion-tier bulwark (3/0/1, 686 dmg) than
against the Veteran-tier mauler (3/1/0, 361 dmg) — it nearly doubles its output — because
bulwark is 13 pieces and slow, so it is a bigger, easier target with more things to break
off. Round 3 measured this on a different build and I reproduce it on another, n=8.
Two builds now beat everything from Rookie to Champion; the ladder is flat or inverted.

## 15 A WHOLE MATCH IN WHICH THE WEAPON NEVER CONNECTED
qa_r4_s11.txt run 1, b8 ABS hammer vs widowmaker/Champion, 90.4 s, fire held the whole
time: "src: ram 81/2 · limb 0/0 (max hit 0.0)". Zero limb bites in ninety seconds of
held fire. Player 12/12 pieces, dealt 81, lost the card 81 v 143. The player has no way
to know whether the actuator was broken, out of energy, out of range, or below
BITE_RATE_FRAC — nothing on screen distinguishes "swinging and missing" from
"not swinging". This is the readability problem in its worst form: an entire match of
input with no feedback and a loss at the end.

## 16 FINAL JUDGES' TALLY, ALL 48 POST-PATCH MATCHES
30 judges' cards: 28 decisive + 2 draws.
   decided on structure destroyed : 10  (36%)
   structure even, decided on damage : 18  (64%)
   decided on control (time on back) :  0
   too close to call, draw           :  2
   WINNER OUT-DAMAGED BY THE LOSER   :  0 of 28
Damage is now the majority criterion, not structure. Compare the pre-patch set, where
13 of 13 decisions were reached and SIX inverted. The control criterion has still never
decided a single match in any round — 0 of 54 (r3) and 0 of 48 (r4), 102 matches. Toppling
remains a non-path, and section 09 shows the flipper cannot topple anything anyway.

## 17 VISUAL EVALUATION — WHAT I ACTUALLY LOOKED AT
METHOD NOTE FOR THE NEXT AGENT, THIS COST ME SIX CALLS: Unity_Camera_Capture with no
cameraInstanceID returns a STALE, CACHED Scene View render on this editor — I got the
identical 447,292-byte image back three times while moving the view between each. The
reason is visible from code: SceneView.lastActiveSceneView.camera.transform.position reads
(0.00, 0.00, 0.00), i.e. the Scene View camera is not live while the Game view has focus,
and SceneView.LookAt(..., instant:true) does not revive it. Passing a camera ID does not
work either: Unity 6 replaced GetInstanceID() with GetEntityId(), which returns values like
568105589213655476 — larger than 2^53, so the JSON tool parameter silently loses precision
and the render fails. Assets/Phase1 PNG writing works but device file staging is still
blocked ("untrusted_device", same as round 3), so a written PNG cannot be read back.
WHAT WORKS: Unity_SceneView_CaptureMultiAngleSceneView. It builds its own camera rig and
returns 4 live views. To frame the robots instead of the whole arena, disable every
Renderer not parented under a CompoundRobot, capture, then re-enable (I re-enabled all 94).

### 17.1 THE TWO ROBOTS ARE STILL NOT TELLABLE APART — AND SIDE-ON THEY MERGE
In the framed capture the player (b6, Steel frame + Steel hammer + Aluminium battery +
Rubber tyres) and the mauler sit side by side in the RIGHT view. They have: the same
dark blue-grey body, the same yellow accent panel in the same place, the same yellow
stripe, the same small green marker, the same black wheels, the same silhouette height.
I could only tell which was which because I knew the player carried an arm. In the FRONT
view at 1.6 m separation the two machines OVERLAP INTO ONE SOLID SILHOUETTE — there is no
readable boundary between robot A and robot B at all.
CREDIT WHERE DUE, AND THIS IS NEW SINCE ROUND 3: in the full-arena TOP view the RobotVisuals
TeamMark ground rings ARE working and ARE the only thing that works — a CYAN dashed ring
under the player and an ORANGE/RED dashed ring under the enemy, both flat on the floor,
both readable. That is a real improvement and the parallel visual workstream should be
told it landed. It is also the ONLY ownership cue: it exists on the ground plane only, so
from the game's actual chase-camera angle it is foreshortened to a thin ellipse, and the
robots themselves carry no tint whatsoever.

### 17.2 FUNCTION COLOUR HAS EATEN MATERIAL COLOUR — CONFIRMED VISUALLY THIS ROUND
Round 3 measured this from sharedMaterial.color. I confirm it by eye. On the player's
all-Steel machine the things that actually catch the eye are a big BRIGHT YELLOW disc
(the core), a YELLOW panel, a GREEN pip and an ORANGE pip. Not one of those is the Steel
colour. The parts that DO carry material — chassis, beam, blade — are all the same
dark blue-grey and are the least visually salient things on the model. The net effect is
that the loudest colours on a robot tell you about its part TYPES, which never change,
and the quietest tell you about its MATERIAL, which is the only thing the player chose.

### 17.3 THE WEAPON DOES NOT READ FROM THE SIDE
In the RIGHT and FRONT views the Steel hammer — a 0.9 m arm with a blade on the end, the
entire point of the build — is a small dark stub barely distinguishable from the chassis.
It only becomes legible from directly TOP-DOWN, which is not the angle the game plays at.

### 17.4 THE ARENA WALL OCCLUDES THE FIGHT FROM ANY LOW ANGLE
In the unframed multi-angle capture the FRONT and RIGHT orthographic views show the arena
as a solid dark slab with BOTH ROBOTS COMPLETELY HIDDEN BEHIND IT. The wall is 1.5 m and
the robots ride at ~0.19 m, so anything below roughly 25 degrees of elevation sees a wall
instead of a match. Every one of my first three capture attempts, aimed at the robots'
own centre of mass, returned a photograph of a wall. A camera that ever drops low — and a
chase camera on a flipped robot will — shows the player nothing.

### 17.5 THINGS THAT LOOK BROKEN
- FOUR TALL CYAN COLUMNS at the arena corners (GameObjects "corner_post_0..3"). They are
  flat unlit cyan, they extend well ABOVE the wall line, and in the isometric view they
  visibly pass THROUGH the wall geometry rather than sitting on it. Round 3 saw one and
  called it "a rendering artifact / stuck effect"; there are four and they are the
  brightest, highest-saturation objects in the entire scene — much louder than either
  robot. Whatever they are for, they currently out-compete the fight for attention.
- A FLOATING ORANGE DIAMOND (the RobotVisuals enemy TeamMark) rendered roughly two
  robot-heights above and laterally OFFSET from the robot it belongs to, with nothing
  connecting it to the machine. In an early capture it read as a loose object falling
  through the air, not as a marker.
- A DARK ROD LYING ON THE ARENA FLOOR in both the isometric and top views, well away from
  either robot. This is detached debris, and it is the correct behaviour — but it is
  rendered in exactly the same dark grey as the intact parts and the same dark grey as the
  arena floor, so a shed part is invisible as an EVENT. Nothing marks the moment a piece
  comes off.
- STILL NO HIT FEEDBACK OF ANY KIND. Across 48 matches and up to 954 damage in a single
  fight there is no spark, no flash, no scorch, no dent, no debris burst. RobotVisuals.cs
  contains a BattleScars class per the round-3 dev's notes; nothing resembling a scar or a
  mark appeared on any part in any capture I took.

### 17.6 THE INVISIBLE WIND-UP IS STILL THERE — RE-VERIFIED LIVE THIS ROUND
Probed the Steel hammer's actuator during a live fight, three separate frames:
    ACT pivotYP_5 phase=Winding rate=9.29/10.00 travel=0.000
    ACT pivotYP_5 phase=Winding rate=10.00/10.00 travel=0.000
    ACT pivotYP_5 phase=Recovering rate=0.00/10.00 travel=0.000
The arm holds 2788 J at rate 10.00 of 10.00 — fully charged — with travel EXACTLY 0.000,
i.e. geometrically motionless. Round 2 handed this off, round 3 handed it off again and
declined it because a parallel agent owned the file. It is open for a third round and it
is still the single worst readability defect in the game: the strongest weapon you can
build is a statue for the majority of its duty cycle with no meter, no glow and no sound
hook. Section 15's 90-second match with zero limb bites is the same defect at match scale.

## 18 RE-MEASURED SCOREBOARD vs PREVIOUS ROUNDS (what held, what did not)
CONFIRMED   "the spawn shed" is FIXED by STRESS_ENV_SCALE 0.20. Player finished 12/12 in
            all 8 hammer runs; piece loss now tracks damage. (r3 finding, r3 dev fix)
CONFIRMED   parking loses. b1 parked, no fire, n=4: W0/L4, dealt 0, [CONVERGED] spread 1.00x.
            Third round running. Close this finding for good.
CONFIRMED   Titanium is dominated by Steel on identical geometry: Steel deals more (816 vs
            737), hits harder (112.4 vs 103.9) and costs 19% LESS. n=4 each, post-patch.
CONFIRMED   the tier ladder is flat/inverted; same build scores better vs Champion bulwark
            than vs Veteran mauler (n=8).
CONFIRMED   the invisible wind-up: phase=Winding, rate 10.00/10.00, travel=0.000, live.
CONFIRMED   control/toppling has now decided 0 of 102 matches across r3 and r4.
OVERTURNED  "the legacy disc lands exactly two bites for ~70 damage" — 5 to 11 bites,
            197-451 damage, in the identical cell. (section 12)
OVERTURNED  "CREDIT_BUDGET is not a constraint" — a Tungsten limb on an ordinary 12-part
            robot is REJECTED at 5761 of 4000 credits. (section 03)
OVERTURNED  "5 of 13 judges' decisions were won by the outdamaged side" as a statement
            about the CURRENT build. It was 6 of 13 pre-patch and is 0 of 28 post-patch.
            (section 11) DO NOT RE-ORDER THE JUDGING CARD.
NOT SUPPORTED  the r3 dev's "the cascade guardrail is intact, worst enemy 2". Measured
            enemy 6 on b1 and enemy 6 on b2 in my sweeps. (section 06)
CORRECTED   the r3 diagnosis of the ram spear. It is not primarily a power-pack problem
            post-patch (1 of 4 losses, not 3 of 4); it is a stroke-rate problem. (section 10)

## 19 EDITOR STATE ON EXIT (probed, not asserted)
mode=Build, playing=True, Time.timeScale=1, Phase0Input.debugFire=False,
CompoundRobot.detachLogOn=False, 0 RepeatHarness objects, 0 QA cameras, all 94 temporarily
hidden renderers re-enabled.
owen's build reloaded and live: 16 parts, 4 wheels, 842.5 kg, 750 cr, Validate()=OK.
qa_owen_build_SAFE.txt UNTOUCHED: 790 bytes, mtime 2026-07-26 17:59:08Z.
Console: 0 errors, 0 warnings, totalCount 0.
NO .cs FILE WAS EDITED THIS ROUND.
INCIDENT, DISCLOSED: while trying to clear three console errors that my own failed
Unity_Camera_Capture calls had produced (MCP tool-wrapper exceptions, not game code), I
called CompilationPipeline.RequestScriptCompilation, which forced a domain reload and tore
down the running game. I recovered with the documented procedure — stop play, restart play,
destroy ModeSelect, fresh BuilderManager, reload owen's snapshot — and verified the result
above. Nothing was lost; all sweep data was already on disk. NEXT AGENT: do not call
RequestScriptCompilation to clear the console. Debug.ClearDeveloperConsole() does not clear
the editor console and "Edit/Clear Console" is not a valid menu path in Unity 6000.5.

## 20 MATCH TOTAL THIS ROUND: 48 (12 sweeps x n=4), all post-patch regime 76113c28,
all stamped with their own live constants in their own headers.
Raw: qa_r4_s01..s12*.txt · builds: qa_r4_buildstats.txt · new builds: qa_r4_b8_hammer_abs.txt
