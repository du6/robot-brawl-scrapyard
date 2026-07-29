# QA Round 3 — CRITIC report
Robot Brawl: Bolt & Blade. Unity 6000.5.4f1. Started 2026-07-27.
Brief: author >=5 genuinely different robots (pivot hammer, self-built spindle rotor,
ram spear, wedge flipper/plow, legacy control), span the material table, run real
matches vs varied opponents/tiers, and EVALUATE THE VISUALS with camera capture.
Re-measure round 2's claims rather than trusting them.

## 00 Session start
Editor probe: playing=True, timeScale=1, BuilderManager alive, 16 parts (owen's build) loaded.

## 01 SOURCE REGIME under test
96ce6370 (22 scripts, newest 2026-07-27 01:35:15Z) — this IS round 2's shipped code.
tipSpeedCap m/s: ABS 9.00 | Aluminum 9.68 | Steel 12.50 | Titanium 14.79 | CarbonFiber 13.11 | Tungsten 13.69
DRAIN_FRAC 0.40, LIN_IMP_CAP 600, EFFICIENCY 0.35, RECUP_FRAC 0.90, BITE_TIP_SPEED_MS 4.00,
DMG_K 0.045, RAM_MIN_J 150, RAM_J_CAP 900, MATCH_TIME 90, CREDIT_BUDGET 4000.
Material table (density g/cm3, strengthRel, costPerKg):
  ABS 1.05/0.35/0.5 · Aluminum 2.70/0.6/1.0 · Steel 7.85/1.0/0.8
  Titanium 4.51/1.4/3.0 · CarbonFiber 1.60/1.1/4.0 · Tungsten 19.3/1.2/6.0

## 02 THE SIX BUILDS (all authored this round, all Validate()==OK)
file                          weapon system              material     parts mass  cost  limb parts/blockedBy/I/tipR/E(J)/kJ-swing
qa_r3_b1_hammer_ti.txt        pivot HAMMER (beam+blade)  Titanium limb / Steel frame  12  1736  1773  2/null/I=32.04/tipR=1.25/E=2243/6.408
qa_r3_b6_hammer_steel.txt     pivot HAMMER (identical geom) Steel limb / Steel frame  12  1866  1490  2/null/I=55.76/tipR=1.25/E=2788/7.966
qa_r3_b2_rotor_cf.txt         spindle ROTOR self-built
                              (beamlong bar + 2 blades)  CarbonFiber / Alu frame      13   690   939  3/null/I=8.08/tipR=1.00/E=694/1.984
qa_r3_b3_spear_w.txt          ram SPEAR (beam+spike)     Tungsten spike / Alu frame   12  1034  1654  2/null/I=328.5/tipR=0.30/E=2012/5.749
qa_r3_b4_flipper_al.txt       pivot WEDGE plow/flipper   Aluminum                     12   729   706  2/null/I=2.51/tipR=0.30/E=246/0.703
qa_r3_b5_disc_abs.txt         CONTROL: legacy spinnerSaw
                              + spike, no Phase 4 parts  ABS (Alu battery, Rubber tyres) 11 372  265  (no actuator)

ALL FIVE ACTUATED LIMBS: blockedBy == null, parts > 0. Confirmed before any match ran.

### IMMEDIATE OBSERVATION (pre-match): CREDIT_BUDGET IS NOT A CONSTRAINT
CREDIT_BUDGET = 4000. My most expensive build of six — a 1736 kg Steel-frame machine
with a full Titanium hammer arm — costs 1773 cr, 44% of budget. The cheapest costs 265 cr,
6.6% of budget. Six deliberately maximal-in-different-directions builds and NONE of them
came within 2.2x of the ceiling. There is no build decision in this game that trades cost.

## 03 CELL A — all six builds vs MAULER / Veteran, policy=charge, debugFire held, timeScale 3, n=6
Raw: Assets/Phase1/qa_r3_A_b*.txt

### b1 Titanium pivot hammer: W6/L0/D0. len 2.9-30.2 s, spread 10.29x [NOISY]
dealt 482/699/571/462/445/779 (mean 573), taken 67/118/67/67/27/108 (mean 76)
FIVE OF SIX ENDED "KO - enemy core destroyed" IN UNDER 8 SECONDS. Fastest 2.9 s.
src split: ram 92-302 / limb 295-491. max limb hit 29.3-83.1.
TWO THINGS FALL OUT OF THE DETACH LEDGER IMMEDIATELY:
 (a) The player's Titanium BLADE is HP-DESTROYED at hp 0/25 in 5 of 6 runs, within
     ~1.5 s of the match starting. A 12 kg Titanium blade — the actual cutting edge of
     the weapon the build is named after — has 25 max HP and does not survive its own
     first two swings. Everything after that is the bare beam doing the damage.
 (b) The MAULER's first three detach events are byte-identical across all six runs:
     SEAM chassis_1<->spikeZP_6 SHEARED at 1400 vs eff 900, then core_0<->chassis_2
     at 1050 vs 900, then chassis_2<->gyro_5. Same order, same loads, every run,
     within the first ~1.3 s. Run 4 ended at 2.9 s with the enemy at 0/11 pieces and
     its chassis_1 logged STRUCTURAL-WRECK at hp 216/216 (100%) — destroyed at FULL
     HEALTH by structural cascade, not by damage. Suspect the enemy disintegrates on
     SPAWN. Queued for an AFK control test.

### b6 Steel pivot hammer (SAME geometry as b1, Steel limb): W6/L0/D0. len 4.3-30.4 s
dealt 711/572/644/599/615/558 (mean 616.5), taken 69/74/99/50/69/51 (mean 68.7)
max limb hit 111.0/87.8/115.2/88.0/90.2/89.8 (mean 93.7) vs b1 Titanium 29.3-83.1 (mean 55.0)
limb HITS per match: b6 5/8/5/11/8/10 (mean 7.8) vs b1 22/12/28/19/6/14 (mean 16.8)
Steel blade also HP-DESTROYED, hp 0/18, but only ONCE in 6 runs (b1's Titanium blade died 5/6).

### b2 CarbonFiber self-built spindle ROTOR: W6/L0/D0. len 26.8-45.6 s, spread 1.70x (least noisy build)
dealt 284/407/481/396/377/465 (mean 401.7), taken 0/37/161/83/5/25 (mean 51.8)
PLAYER LOST ZERO PARTS IN ALL SIX RUNS (13/13 every time). One run took literally 0 damage.
5 of 6 ended "Enemy counted out - out of power - 300 kJ of its 300 kJ pack was torn off the
chassis". The rotor's win condition is shearing the enemy battery off and waiting for brownout.
That is a genuinely good emergent verb and the only build of the six that wins a DIFFERENT way.

### b1 vs b6 — the material A/B (identical geometry, Titanium limb vs Steel limb), n=6 each
                     b1 Titanium    b6 Steel     ratio Ti/Steel
stored limb energy   2243 J         2788 J       0.80
limb inertia         32.04          55.76        0.57
match damage dealt   573            616          0.93
max limb hit         55.0           93.7         0.59
limb hits/match      16.8            7.8         2.15
damage taken          76             69          1.10
cost                 1773 cr        1490 cr      1.19x MORE expensive
Titanium costs 19% more, is the strongest material in the table (strengthRel 1.4 vs 1.0)
and has the highest tip-speed cap (14.79 vs 12.50 m/s) — and it loses on damage dealt,
loses badly on peak hit, and its blade dies five times as often. It buys only bite RATE.

### b3 Tungsten-tipped ram SPEAR: W2/L4/D0 — THE ONLY BUILD OF SIX THAT LOSES
dealt 435/205/298/165/322/146 (mean 262), taken 309/392/293/233/249/206 (mean 280)
THREE of the four losses ended "Counted out - out of power - all 300 kJ spent in 49/56/59 s".
Arithmetic: LimbReport says this limb costs 5.749 kJ per stroke. 300 kJ pack / 5.749 = 52 strokes.
Held-fire cycle is ~1 s, so the spear drinks its own pack flat in under a minute BY DESIGN and
the harness's own cause line tells the player "carry more battery, or spend less on driving".
The Tungsten spike is inertia 328.5 at tipRadius 0.30 — 10x b1's inertia for a quarter of the
reach. It is the highest-energy limb I built and the worst-performing build I built.
It also SHEDS ITS OWN NOSE: PlayerBuild core_0<->chassis seams SHEAR at 1400 vs eff 900 and
the whole ram/beam/spike assembly STRUCTURAL-SHEDs at 97-100% HP.

## 04 CRITICAL: THE SEAM-STRESS SYSTEM IS SATURATED — DAMAGE ABOVE 1400 N.s IS INVISIBLE
Every seam failure in this round's data, all six builds pooled (n=111 shear events):
     67 x  SHEARED at 1400 vs eff 900     <- exactly STRESS_J_CAP
     30 x  SHEARED at 1050 vs eff 900     <- exactly STRESS_J_CAP * STRESS_ENV_SCALE (0.75)
     14 x  everything else (903..1321)
97 of 111 (87%) are pinned to the constant. Source: CompoundRobot.cs:546
    float Js = Mathf.Min(J, STRESS_J_CAP) * (fromRobot ? 1f : STRESS_ENV_SCALE);
with STRESS_J_CAP = 1400 (line 69), STRESS_ENV_SCALE = 0.75 (line 99), BREAK_K = 1500 (line 54),
threshold = min(strengthRel of the two mated parts) * BREAK_K. eff 900 = Aluminium 0.6 * 1500.
CONSEQUENCE, MEASURED NOT ASSUMED: b1 (Titanium, max limb hit 55.0 mean) and b6 (Steel, max
limb hit 93.7 mean) are the SAME GEOMETRY and produce a byte-identical enemy break sequence —
chassis_1<->spikeZP_6 at 1400, then core_0<->chassis_2 at 1050, then chassis_2<->gyro_5 —
in the same order in all twelve matches. A 70% increase in peak hit changed nothing structural,
because both saturate the cap. Structural destruction is a binary "did you clear 1400 N.s",
and every joule a player spends past that is spent on nothing.

### b4 Aluminium wedge FLIPPER/plow: W4/L2/D0. FIVE OF SIX RAN THE FULL 90 s TO A JUDGES' DECISION
dealt 256/270/329/626/406/416 (mean 384), taken 592/425/108/238/137/192 (mean 282)
src split: ram 133-284 (mean 210, 18.3 contacts) vs limb 79-342 (mean 174, 12.8 contacts).
max limb hit 15.0-47.9 (mean 31.8) — the WEAKEST weapon of the six by peak hit.
55% of this "flipper's" damage output is chassis ramming, not the wedge.

## 05 TOPPLING IS STILL NOT A WIN PATH — re-measured, and the reason is in the judging order
FightManager.cs:587/593/601 resolves a decision as: structure destroyed -> damage -> control
(time on back), each gated by a band. Flip time is the THIRD tiebreak and is only ever
consulted when structure AND damage are both inside their bands.
b4 run 2 is the clean case: the enemy spent 22.6 s of 90 s on its back (25% vs the player's 2%)
— the single most successful toppling performance in 36 matches — and the verdict line reads
"decided on structure destroyed". The flip was not what won it and would not have won it.
Across all 36 matches this round, ZERO were decided by the control criterion.
A player who builds a flipper is building a weapon whose effect the scoring system reads last.

### b5 CONTROL, ABS legacy spinnerSaw + spike, no Phase 4 parts: W2/L4/D0
dealt 185/583/350/267/321/243 (mean 325), taken 135/61/282/251/205/285 (mean 203)
THREE runs ended "KO - your core was destroyed". ABS at strengthRel 0.35 gives seam
threshold 0.35*1500 = 525 vs the 1400 cap, i.e. every robot contact at the cap is 2.7x
over its seam strength. The r5 note in MatDB says ABS was raised to 0.35 so it could
"survive an opening ram"; against the cap it still cannot.
INSTRUMENTATION HOLE FOUND HERE: runs 2, 3 and 5 have NO "src:" damage-card line at all.
RepeatHarness.cs guards on `pb != null`, and when you lose by core-KO the player bot is
gone, so the per-weapon damage breakdown is silently dropped on exactly the losses a
player most needs to learn from. 3 of 36 matches this round produced no damage card.
JUDGING ODDITY, run 4: the player dealt 321 and took 205 — a 36% damage ADVANTAGE — and
LOST, because structure is ranked above damage and the player was 6/11 vs 10/11 pieces.
Out-damaging your opponent by half and losing is legal under the current order.

## 06 CELL A SUMMARY (mauler/Veteran, charge, fire held, n=6 each, 36 matches)
build                    W/L/D   dealt  taken  max limb hit  ended
b1 hammer Titanium       6/0/0    573     76      55.0   5/6 KO under 8 s
b6 hammer Steel          6/0/0    616     69      93.7   2/6 KO, 4/6 wheels torn off
b2 rotor CarbonFiber     6/0/0    402     52      44.8   5/6 enemy battery sheared -> brownout
b4 flipper Aluminium     4/2/0    384    282      31.8   5/6 ran the full 90 s to a decision
b3 spear Tungsten        2/4/0    262    280      52.2   3/4 losses = player's own pack ran flat
b5 disc ABS (control)    2/4/0    325    203       n/a   3/6 player core destroyed

## 07 VISUAL EVALUATION (scene-view captures; NOTE: SceneView.camera syncs one editor
## repaint AFTER you set pivot/rotation, so aim in call N and capture in call N+1)
Capture rig also writes Assets/Phase1/qa_r3_shot01_fight.png via a temp Camera+RenderTexture.

### 07.1 THE SWING IS MOSTLY NOT A SWING — computed by the game's own Actuator.CycleSeconds
build            rateMax  E(J)   windUp    sweep    cycle    SWING%   WIND-STANDING-STILL%
b1 pivot Ti      11.83    2243   0.897 s   0.221 s  1.518 s   14.6%        59.1%
b6 pivot Steel   10.00    2788   1.115 s   0.262 s  1.777 s   14.7%        62.8%
b2 spindle CF    13.11     694   0.278 s   0.200 s  1.000 s   20.0%        27.8%
b3 ram Tungsten   3.50    2012   0.805 s   0.129 s  1.483 s    8.7%        54.3%
b4 pivot Al      14.00     246   0.098 s   0.187 s  0.685 s   27.3%        14.4%
During Winding, Actuator freezes `travel` — VERIFIED LIVE: "phase=Winding rate=11.83
maxRate=11.83 travel=0.000", i.e. the arm holds its FULL 2243 J at a dead stop.
So the best weapon in the game is visually motionless 59-63% of the time, and there is
no charge meter, no emissive ramp, no wind-up sound hook, no tell of any kind. The
harder you build, the longer your robot stands there looking broken. The round-2 dev
explicitly left this as a handoff ("Actuator.phase now carries a Winding value... a
charge meter or emissive ramp driven off rate/MaxRate during Winding is now trivial").
It is still not there and it is now the single worst readability problem in the game.
Frames of actual motion at 60 fps: b1 sweep = 13 frames, return = 18 frames. The MOTION
is fine. It is the 54 motionless frames in front of it that are unreadable.

### 07.2 YOU CANNOT TELL THE TWO ROBOTS APART
Capture of the arena mid-fight: player and MAULER are both dark-charcoal boxes with
yellow accent stripes, the same silver cylinders and the same black wheels. With
COM separation measured at 1.14 m the two machines visually merge into one object —
I could not identify which parts were mine without querying CompoundRobot from code.
No outline, no team tint, no player marker. This has been open since round 1.

### 07.3 MATERIAL READS AS VALUE ONLY, NOT AS THE COLOUR THE DATA PROMISES
MatDB assigns each material an accent colour (Titanium orange 0.90/0.62/0.00, Steel
dark blue 0.00/0.45/0.70, Aluminum sky blue, CarbonFiber pink, Tungsten teal, ABS yellow).
On the b1 build (Steel frame + Titanium limb) I can distinguish the limb from the frame —
Titanium renders light silver, Steel dark charcoal — so LIGHTNESS separates them. But every
accent stripe on the machine renders YELLOW/cream regardless of the part's material. I did
not see one blue Steel accent or one orange Titanium accent anywhere on the model.
Practical effect: you can tell "light metal" from "dark metal" and nothing finer, so
Titanium vs Aluminum vs CarbonFiber are not separable by eye.

### 07.4 CONFIRMED VISUALLY: THE MAULER SHEDS A PART ON SPAWN, BEFORE ANY CONTACT
Capture at t=11.6 s of a manual fight with fm.player.dealt = 0.00 — zero damage dealt —
shows a pale slab lying flat on the arena floor beside the robots. Probe confirms
"ENEMY parts: 7 total, 1 detached" while all 8 player parts read det=False, hp full.
Combined with the ledger (identical chassis_1<->spikeZP_6 SHEARED at 1400 / core_0<->
chassis_2 at 1050 opening sequence in all six runs of every sweep), the enemy loses a
part to the SPAWN DROP every single match. The player is credited nothing for it, but
FightManager counts pieces for the structure criterion, so every match begins with the
opponent already 1 piece down through no one's action.

### 07.5 THINGS THAT LOOK WRONG
- A tall thin cyan column stands in the arena reaching from the floor to above the wall.
  It reads as a rendering artifact / stuck effect rather than as arena furniture.
- The forced mid-arc pose (travel 0.85 rad of 2.62) renders correctly — the arm is a
  rigid beam at an intermediate angle, no interpenetration with its own chassis, no
  jitter. The arc geometry is fine. It is the timing, not the pose, that fails.
- No hit spark, no impact flash, no scorch, no dent. 573 mean damage a match and the
  arena and both robots look exactly as they did at spawn apart from shed parts.

## 08 THE OLDEST OPEN FINDING IS DEAD: PARKING NO LONGER WINS
Identical build (b1), identical opponent (mauler/Veteran), identical n=6, same session,
same regime. Only the policy differs.
  charge + fire held : W6 / L0 / D0 · dealt 573 · taken  76 · 5/6 KO under 8 s
  afk   + no fire    : W0 / L6 / D0 · dealt   0 · taken 254 · 6/6 went the full 90 s
  [CONVERGED], spread 1.00x — the AFK cell is the least noisy dataset I produced.
Round 1's "a parked robot beats a Veteran 5 times in 6" does NOT hold under the round-2
code. Round 2 declined to touch it and was right to: the latch fixed it as a side effect.
This should be closed, not carried forward again.

## 09 CRITICAL: BOTH ROBOTS SHED PARTS ON SPAWN, EVERY MATCH, WITH NOBODY TOUCHING THEM
In the AFK cell the player did NOTHING for 90 s — throttle 0, steer 0, fire off, dealt 0.00.
Enemy piece count at the end: 10/11 in SIX runs of six. Player piece count: 11/12 in FOUR
runs of six. Damage dealt by the player: zero in all six.
So ~100% of matches begin with the opponent already one piece down and ~67% with the
player one piece down, purely from the spawn drop. Mechanism (CompoundRobot.cs:546):
    Js = min(J, STRESS_J_CAP=1400) * (fromRobot ? 1 : STRESS_ENV_SCALE=0.75) -> 1050
and 1050 > the 900 seam threshold of any Aluminium seam, so a robot that merely LANDS
shears a seam. The ledger's opening line is the same in every sweep I ran.
This corrupts the structure criterion, which FightManager ranks ABOVE damage.

## 10 CELL B — OPPONENT AND TIER VARIATION. THERE IS NO DIFFICULTY CURVE.
b6 Steel hammer, charge, fire held, timeScale 3:
  vs MAULER     / Veteran  n=6 : W6/L0  dealt  617  taken  69  max limb hit  93.7
  vs BULWARK    / Champion n=4 : W4/L0  dealt 1200  taken 182  max limb hit 161.8
  vs WIDOWMAKER / Champion n=4 : W4/L0  dealt  933  taken 315  max limb hit 109.5
14 matches against three opponents spanning Veteran and Champion. Fourteen wins, zero losses.
The player deals TWICE AS MUCH damage against the Champion-tier BULWARK (1200) as against the
Veteran-tier MAULER (617), because BULWARK is bigger (13 pieces) and slower, so it is easier
to hit, and the extra pieces are extra targets. The tier label is inverted in practice.
Both Champions lose the same way: 5 of 8 end "out of power - 300 kJ of its 300 kJ pack was
torn off the chassis". The AI's battery is a single seam away from a forfeit and every
serious weapon finds it. That is one dominant strategy, not a roster.
Peak damage observed all round: 229.6 in a single limb hit on BULWARK (b6 run 0).

## 11 MATERIAL LEGIBILITY, MEASURED FROM THE LIVE RENDERERS (not eyeballed)
Probe of every placed part's actual sharedMaterial.color against MatDB:

(a) EVERY MATERIAL IN THE GAME IS GREY. MatDB.color for all six, with luminance and saturation:
      ABS         #EBE6D9  lum 0.901  sat 0.076
      Aluminum    #BFC7D1  lum 0.777  sat 0.085
      Titanium    #9E998F  lum 0.601  sat 0.097
      Steel       #595E6B  lum 0.369  sat 0.167
      Tungsten    #403847  lum 0.231  sat 0.214
      CarbonFiber #1A1A1F  lum 0.101  sat 0.167
    Max saturation across the whole palette is 0.214. The ONLY channel carrying material
    identity is lightness, on a six-step grey ramp. ABS->Aluminum is a 0.124 luminance step
    and Tungsten->CarbonFiber is 0.130 — smaller than the swing arena lighting puts between
    a lit face and a shadowed face of the SAME part. In my b2 capture I misread a lit
    CarbonFiber blade as a light metal until I probed it.
    MatDB has a high-contrast `auditColor` per material (Okabe-Ito, colour-blind safe:
    Aluminum #57B5E8, CarbonFiber #CC78A6, Titanium orange, Tungsten teal) but its own doc
    comment says it is "used ONLY by the builder's material view". It is not what you see
    by default and there is no such view in the FIGHT, where knowing what the enemy is
    made of is the whole basis of choosing a weapon.

(b) THREE PART TYPES IGNORE THEIR MATERIAL COLOUR COMPLETELY. Measured on b2, all-Aluminum:
      chassis/Aluminum  rendered #BFC7D1  ==  MatDB #BFC7D1   correct
      core/Aluminum     rendered #F2BD19  vs  MatDB #BFC7D1   bright YELLOW
      battery/Aluminum  rendered #1F2B24  vs  MatDB #BFC7D1   dark GREEN
      engine/Aluminum   rendered #303038  vs  MatDB #BFC7D1   dark GREY
    Function colour-coding for core/battery/engine is a defensible choice on its own, but
    it means the three heaviest, most expensive parts on most builds show NO material at
    all. A Tungsten core and an ABS core render the same yellow.

(c) CREDIT WHERE DUE: actuator TYPE is colour-coded on the hub face and it works —
    pivot renders a yellow disc, spindle a cyan disc. That is the single clearest
    "what does this robot do" cue in the game and it should be extended, not replaced.

## 12 THE LEGACY DISC IS A DECORATION
b5 vs scout/Rookie, n=4, the disc's own damage column (live since round 2's SRC_DISC fix):
   run 0: disc 69/2 · ram 197/10      run 1: disc 71/2 · ram 103/10
   run 2: disc 71/2 · ram 268/16      run 3: disc 71/2 · ram 264/18
The spinnerSaw lands EXACTLY TWO bites a match, every match, for ~70 damage — 21% of output.
The other 79% is the chassis hitting things. Round 2 said "the legacy disc dominates is
really chassis ramming dominates"; at n=10 across two opponents I confirm it and can add
that the disc's contribution is not merely small, it is nearly CONSTANT (69/71/71/71),
which suggests it is landing on the same two spawn-adjacent contacts and nothing else.

## 13 CELL B (opponent variation) FULL RESULTS
b6 vs BULWARK/Champion   n=4  W4/L0  dealt 1183/822/1839/958  taken 177/191/103/255
b6 vs WIDOWMAKER/Champ   n=4  W4/L0  dealt 1040/703/1106/885  taken 168/266/341/487
b5 vs SCOUT/Rookie       n=4  W3/L0/D1 [CONVERGED]  dealt 266/173/339/334  taken 65/174/78/65
The ABS control that goes 2/4 against the Veteran goes 3/0/1 against the Rookie, so the
Rookie tier is meaningfully easier. The Champion tier is NOT meaningfully harder — the
best build is 14-0 across Veteran and both Champions.

TOTAL MATCHES THIS ROUND: 54.

## 14 MAJOR: THE PHASE 4 EDGE PARTS ARE THE MOST FRAGILE THINGS IN THE GAME
DamageResolver.cs:90  hp = HP_K(6000) * strengthRel * volume_m3.  Volume only — nothing
about a part being a WEAPON enters. Full-extent volumes from the part table give:
part      V(m3)   ABS   Alu  Steel   Ti    CF     W
blade     0.0030   6.3  10.8   18.0  25.2  19.8  21.6
hook      0.0096  20.2  34.6   57.6  80.6  63.4  69.1
wedge     0.0210  44.1  75.6  126.0 176.4 138.6 151.2
chassis   0.0600 126.0 216.0  360.0 504.0 396.0 432.0
core      0.0270  56.7  97.2  162.0 226.8 178.2 194.4
VERIFIED against live values in this round's logs: Titanium blade maxHp 25, Steel blade 18,
Aluminium chassis 216, Steel chassis 360, Steel core 162, Aluminium battery 56. All exact.
Measured limb hits this round ran 15.0 to 229.6. So a blade of ANY material is one-shot
by a mid-range hit, and an ABS or Aluminium blade (6-11 hp) is one-shot by the weakest hit
I recorded. Observed: b1's Titanium blade was HP-DESTROYED in 5 of 6 runs, usually within
1.5 s. It even died in the AFK run where the player dealt ZERO damage and never fired.
The cutting edge is 14-20x more fragile than the chassis it is supposed to cut, so the
intended fantasy — build a hammer, swing the hammer — survives about one and a half swings
and after that the player is beating people with a bare beam without being told.
Cross-check: b6's Steel blade (18 hp, LOWER than Titanium's 25) died only 1 time in 6,
because b6's slower, heavier arm spends 63% of its cycle winding and simply makes fewer
contacts. Durability is being decided by duty cycle, not by the material you paid for.

## 15 WHAT I RE-MEASURED FROM PREVIOUS ROUNDS
CONFIRMED  "chassis ramming beats the disc" (r2). Disc lands exactly 2 bites/match, ~70 dmg,
           21% of output, at n=10 over two opponents.
OVERTURNED "a parked robot beats a Veteran 5 times in 6" (r1, carried 3 rounds). Now W0/L6
           parked vs W6/L0 driving, same build/opponent/n. This finding is dead; close it.
CONFIRMED  "toppling is not a win path" (r2 reversal was too optimistic). Enemy on its back
           25-29% of a match still loses to structure and damage; control is the 3rd
           tiebreak and decided 0 of 54 matches.
NOT SUPPORTED  r2's "the 2.88x-energy limb now wins on every axis" as a general claim about
           material. In my own controlled A/B (b1 Ti vs b6 Steel, identical geometry, n=6
           each) the LOWER-energy, cheaper Steel limb wins on damage dealt (616 vs 573),
           peak hit (93.7 vs 55.0) and damage taken (69 vs 76), and costs 19% less.
           Titanium's only measured advantage is bite RATE (16.8/match vs 7.8).
STILL OPEN "you cannot tell a swinging weapon from a parked one" (r1, r2, declined twice).
           Now quantified: the best weapons are motionless 59-63% of every cycle, by design,
           with no cue. This is the top visual issue in the game.

## 16 IMAGES ON DISK (a human can open these)
Assets/Phase1/qa_r3_shot01_fight.png            live fight, temp Camera + RenderTexture
Assets/Phase1/qa_r3_shot02_b1_hammer_ti.png     b1 Titanium pivot hammer, builder
Assets/Phase1/qa_r3_shot03_b2_rotor_cf.png      b2 CarbonFiber self-built spindle rotor
Assets/Phase1/qa_r3_shot04_b3_spear_w.png       b3 Tungsten-tipped ram spear
Assets/Phase1/qa_r3_shot05_b4_flipper_al.png    b4 Aluminium wedge flipper/plow
Assets/Phase1/qa_r3_shot06_b5_disc_abs.png      b5 ABS legacy disc control
Assets/Phase1/qa_r3_shot07_b6_hammer_steel.png  b6 Steel pivot hammer (A/B against b1)
NOTE ON METHOD: the images I evaluated in-session came back inline from Unity_Camera_Capture
(scene view). Device file staging was blocked this session ("untrusted_device"), so I could
not read back a PNG I had written. Anyone reproducing this must aim SceneView in one command
and capture in the NEXT — sv.camera's transform only syncs on the following editor repaint.

## 17 EDITOR STATE ON EXIT (verified by probe, not asserted)
mode=Build, playing=True, Time.timeScale=1, Phase0Input.debugFire=False,
CompoundRobot.detachLogOn=False, 0 RepeatHarness objects, 0 QA cameras left in scene.
Console: 0 errors, 0 warnings, totalCount 0, after all 54 matches.
owen's build reloaded and live: 16 parts, 4 wheels, 842 kg, 750 cr, Validate()=OK.
qa_owen_build_SAFE.txt UNTOUCHED — 790 bytes, md5 f4e052c2081911eeadc5502e86a63a4b,
byte-identical to qa_build_prewheelfix.txt. No .cs file was edited this round.
SOURCE REGIME throughout: 96ce6370 (22 scripts, newest 2026-07-27 01:35:15Z).

## 18 NOISE CAVEAT
Round 2 measured this harness's noise floor at ~2x on n=4. I used n=6 where it mattered and
report orderings, not margins. The claims I am most confident in are the ones that do not
depend on damage magnitude at all: the 6/0-vs-0/6 AFK reversal (spread 1.00x, CONVERGED),
the seam-load histogram (n=111 events, 87% pinned to a constant), the phase budget (computed
by the game's own CycleSeconds, not sampled), the HP table (closed form, six live checks),
and the rendered-colour probe (direct read of sharedMaterial.color).
