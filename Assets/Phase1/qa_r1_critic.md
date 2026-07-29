# QA Critic — Robot Brawl: Bolt & Blade (this session, briefed as "Round 1 of 5")
Date: 2026-07-26

NOTE UP FRONT: the disk already contains qa_r1_*, qa_r2_*, qa_r3_*, qa_r4_* artifacts from
earlier critic/dev rounds. The previous qa_r1_critic.md was preserved as
qa_r1_critic_ARCHIVED_prevsession.md before this file was rewritten. My brief's "known open
items" list describes a pre-fix build; I am re-measuring all of it against the CURRENT source
rather than trusting either the brief or the earlier reports.

Mandate: >=5 genuinely different robots (pivot hammer, self-built spindle rotor, ram spear,
wedge flipper, control disc/spike), span the material table, real matches vs varied
opponents/tiers, parked vs driving, visual evaluation via camera capture.

## Log

### STATIC RE-MEASURE of the brief's "known open items" (live source, .bak files excluded)
| brief item | claim | current source | verdict |
|---|---|---|---|
| 1 | AI cannot fire; aiFire never written | AIController.cs:244 `acts[i].aiFire = live && (...)` | **NO LONGER TRUE** |
| 1b | threatDir built only from SpinnerWeapon | AIController.cs has FIRE_RANGE/FIRE_ARC_DOT/SPIN_HOLD_RANGE fire policy | NO LONGER TRUE |
| 2 | energy: DRIVE_KW_PER_TONNE 3.5 | PowerPlant.cs:102 `DRIVE_KW_PER_TONNE = 1.25f` | changed 3.5 -> 1.25 |
| 3 | CoM does not move as a limb swings | Actuator.cs:789 calls `robot.RecomputeMass()` | **NO LONGER TRUE** |
| 5 | maxDepenetrationVelocity = 4 | CompoundRobot.cs:397 still `= 4f` | STILL TRUE |
| n/a | flat 12.5 m/s tip-speed clamp | TIP_SPEED_BASE 12.5, MIN 9, MAX 18 -> now geometry-dependent | changed |
Other live constants of interest: MOTOR_KW_BASE 1.0, MOTOR_KW_PER_ENGINE 1.5, CAP 12;
LIFT_EFFICIENCY 0.85; TOPPLE_IMP_CAP 1500; EFFICIENCY 0.35; BITE_RATE_FRAC 0.45;
SHOVE_CAP 500; EXPOSED_MULT 1.75; INCAP_GRACE 2 + COUNT_OUT 10; DEFAULT_MATCH_TIME 90.
=> The brief's open-item list is stale by several dev rounds. Everything below is measured fresh.

### SIX BUILDS AUTHORED, VALIDATED, LimbReport CONFIRMED (all blockedBy=null, limb parts>0)
LIVE CONSTANTS stamp: tipSpeedCap m/s ABS=9.00 Aluminum=9.68 Steel=12.50 Titanium=14.79
CarbonFiber=13.11 Tungsten=13.69 | MotorKW 0/1/2/3 eng = 1.0/2.5/4.0/5.5 | DRIVE_KW_PER_TONNE 1.25
| LIFT_EFFICIENCY 0.85 | REACTION_FRAC 0.60 | BITE_RATE_FRAC 0.45 | EXPOSED_MULT 1.75 | STEP_DISP_CAP 0.25

| file | archetype | materials | parts | mass kg | cost | limb | I | tipR | rateMax | E (J) | kJ/swing | cap m/s |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| qa_c1_hammer_ti  | pivot hammer, rear top mount | Titanium frame / Tungsten blade | 12 | 1135.2 | 3278 | pivot p=2 | 54.68 | 1.250 | 10.95 | 3281 | 9.37 | 13.69 |
| qa_c2_rotor_cf   | SELF-BUILT spindle rotor (2 beams + 2 blades) | CarbonFiber / Steel blades | 14 | 560.1 | 1533 | spindle p=4 | 41.87 | 1.250 | 10.00 | 2093 | 5.98 | 12.50 |
| qa_c3_lance_w    | ram spear | Tungsten everything | 12 | 3903.9 | 22386 | ram p=2 | 603.32 | 0.300 | 3.50 | 3695 | 10.56 | 13.69 |
| qa_c4_flipper_al | pivot wedge flipper, side-mounted long arm | Aluminum / Titanium wedge+beamlong | 13 | 928.8 | 1361 | pivot p=3 | 221.18 | 1.375 | 7.04 | 5484 | 15.67 | 9.68 |
| qa_c5_discus_abs | CONTROL: spinnerSaw + spike, no Phase 4 | ABS frame / Steel saw+spike | 11 | 464.1 | 344 | (none) | - | - | - | - | - | - |
| qa_c6_hammer_abs | pivot hammer, GEOMETRICALLY IDENTICAL to c1 | all ABS | 12 | 398.2 | 279 | pivot p=2 | 7.46 | 1.250 | 7.20 | 193 | 0.55 | 9.00 |

Material A/B (c1 vs c6, same 12-part geometry): Ti/W = 1135 kg / 3278 cr / 3281 J per swing.
All-ABS = 398 kg / 279 cr / 193 J per swing. **17.0x the stored energy for 11.7x the cost.**
Roster confirmed: mauler/Veteran bulwark/Champion scout/Rookie tipper/Rookie widowmaker/Champion ripper/Veteran.

### FINDING (static, before any match): TUNGSTEN IS A PRICE TRAP, NOT A CHOICE.
qa_c3_lance_w is the same 12-part skeleton as the other frames but in Tungsten: 3903.9 kg and
**22386 credits**. The next most expensive build I authored is 3278. That is a 6.8x cost cliff for
one material swap, and it buys a ram limb with rateMax 3.50 rad/s - identical to the Carbon lance,
because the ram rate is hard-capped at 3.5 and never sees the 13.69 m/s Tungsten tip ceiling at all.
A ram literally cannot spend Tungsten's headline stat.

### SWEEP 01 — c1_hammer_ti (Titanium/Tungsten pivot hammer, 1135 kg) vs MAULER/Veteran, policy=charge, n=4
W3/L1/D0, len 44.4-90.3s, spread 2.03x -> harness calls it NOISY.
run0 W  90.3s dealt 540 taken 341  pieces 11/12 vs 5/11  enemy on its back 12.3s  judges/structure
run1 W  90.2s dealt 568 taken 111  pieces 12/12 vs 2/11  enemy on its back  6.9s  judges/structure
run2 L  81.6s dealt 328 taken 433  pieces 11/12 vs 10/11 | "240 kJ of its 300 kJ pack was TORN OFF the chassis"
run3 W  44.4s dealt 504 taken 101  pieces 11/12 vs 5/11  | ENEMY "300 kJ of its 300 kJ pack was TORN OFF the chassis"
=> The hammer is genuinely lethal: it strips 6-9 of the enemy's 11 parts in 90 s and puts the
   mauler on its back for up to 12.3 s.
=> 2 of 4 matches (50%) ended because A BATTERY WAS PHYSICALLY SHEARED OFF, once each way.
   Brief item 6 (battery on a single x1 seam = instant loss) IS STILL LIVE and is now the
   single most common terminator I have seen.

### VISUAL: qa_r1_shotA.png (live fight, t~85 s, 3/4 rear view, both bots in frame)
- The two machines ARE distinguishable, but only by accident of material colour. There is NO
  ownership cue at all - no tint, no outline, no arrow, no nameplate. In a still I cannot tell
  which robot is mine without reading rb positions out of the engine.
- Part identity is carried by FUNCTION colour (battery = saturated yellow box, engine = green
  box with an orange cap, wheels = black with a bolt-circle) and that reads well. MATERIAL is
  carried only by the base grey of the structural parts, and Titanium (player) vs whatever the
  mauler's frame is are both "grey box". Two different metals are not separable by eye.
- I could not identify the player's WEAPON in the frame at all. The Tungsten blade on a 1.0 m
  Titanium arm is the whole point of the build and it does not read as a weapon; the only
  thing that stands out on the player is a yellow disc on the pivot.
- A small dark angular object sits alone against the far wall at screen right - a detached
  part. Debris IS visible but it is the same palette as the arena floor and reads as scenery.

### SWEEP 02 — SAME BUILD, SAME OPPONENT, policy=afk (the parked control), n=4
W0/L4/D0, len 90.0-90.7s, spread 1.01x, CONVERGED. All four went to the judges on damage.
dealt/taken: 138/289, 265/418, 209/364, 146/541. Enemy lost 1 of 11 parts in every match.

### **RE-MEASURED BRIEF ITEM 9 ("PARKING STILL BEATS FIGHTING"): REVERSED. n=8 matched pair.**
Identical snapshot (qa_c1_hammer_ti), identical opponent (mauler/Veteran), identical seed policy,
only the drive policy differs:
    charge  W3/L1   dealt 540/568/328/504 (mean 485)   enemy pieces stripped 6/9/1/6
    afk     W0/L4   dealt 138/265/209/146 (mean 190)   enemy pieces stripped 1/1/1/1
Charging deals 2.6x the damage and wins 75% vs 0%. The oldest open finding in the project no
longer holds on this build. A parked hammer still lands SOME damage (190 mean) because the AI
drives into a 150-degree swinging arc, but it is nowhere near a win condition.
CAVEAT for the next round: this is one build against one opponent. See sweep 04 for the
no-actuator control, which is the version of this test that the original finding was made on.

### SWEEP 03 — c5_discus_abs (CONTROL: ABS frame, Steel spinnerSaw + spike, NO Phase 4, 464 kg)
vs MAULER/Veteran, policy=charge, n=4. W1/L3/D0, CONVERGED.
run0 L 73.5s dealt 447 taken 302 pieces **0/11** vs 10/11 self-flipped 6.6s | KO - core destroyed
run1 L 90.3s dealt 271 taken 234 pieces **2/11** vs 10/11 self-flipped 16.2s | judges, structure
run2 L 90.0s dealt 295 taken 210 pieces **5/11** vs  7/11 self-flipped 22.5s | judges, structure
run3 W 90.0s dealt 488 taken  50 pieces 10/11 vs  7/11 self-flipped  0.0s | judges, structure
worst single-step piece loss: player 3.

### CRITICAL FINDING: an ABS chassis does not lose, it DISINTEGRATES - and the judges' card
### then hands the match to an opponent that took MORE damage.
In runs 1 and 2 the ABS bot WON the damage exchange (271 vs 234, i.e. +13.5%; 295 vs 210, i.e.
+28.8%) and still LOST, because FightManager scores "structure destroyed" first and the ABS bot
had shed 9 and 6 of its own 11 parts. Losing three parts in a single physics step is possible on
ABS (worst step = 3). Run 0 went from 11 attached parts to ZERO in 73 s.
Compare the identical-archetype Titanium bot in sweep 01: it never dropped below 11 of 12.
So the material table's practical range is not "cheap and fragile vs dear and tough", it is
"unplayable vs playable": 464 kg of ABS cannot stay assembled for one match against a Veteran.
Self-flip time tracks the same axis: ABS spent 6.6/16.2/22.5 s on its back, Titanium 0.0-0.9 s.

### SWEEP 04 — c5_discus_abs, policy=afk, same opponent, n=4. W1/L3/D0, CONVERGED.
run0 L dealt 291 taken 201 pieces 9/11 vs 10/11 | judges, structure
run1 L dealt   0 taken  81 pieces 9/11 vs 10/11 | judges, structure   <-- ZERO damage in 90 s
run2 L dealt 199 taken 302 pieces 5/11 vs 10/11 | judges, structure
run3 W dealt 423 taken 103 pieces 10/11 vs 7/11 | judges, structure

### PARKING vs FIGHTING, the full 2x2 (n=16)
                        charge      afk
  c1 hammer (Ti/W)      W3/L1       W0/L4      <- driving is worth +3 wins
  c5 discus (ABS)       W1/L3       W1/L3      <- driving is worth NOTHING
The old "parking beats fighting" finding is dead on an ACTUATED build and alive-as-a-tie on a
classic disc build. The thing that made input matter is Phase 4, not the drive model: a
150-degree swept arc only lands if you close the distance, whereas a passive disc gets its
contacts handed to it by an AI that drives in anyway. That is a real, measurable win for the
feature and I want it on the record as such.

### MAJOR: THE JUDGES' CARD LETS ONE PART OUTRANK A 31% DAMAGE LEAD.
FightManager.Judge scores STRUCTURE FIRST (STRUCT_BAND 0.06), damage second, time-on-back third.
That ordering is a deliberate round-2 change, but the band is now doing far more work than it was
sized for. In sweeps 03+04 (n=8) the player OUT-DAMAGED the enemy and still LOST 3 times:
  +31.1% damage, pieces 9/11 vs 10/11  (18.2% vs 9.1% loss -> 9.1 pts, just outside a 6-pt band)
  +13.5% damage, pieces 2/11 vs 10/11
  +28.8% damage, pieces 5/11 vs  7/11
The first of those is decided by ONE PART on an eleven-part robot. On a light frame, where a
single hit sheds two parts, the 6% band is smaller than the quantisation of the metric it bands.

### SWEEP 05 — c6_hammer_abs (ALL ABS, geometrically IDENTICAL to c1) vs MAULER/Veteran, charge, n=4
W1/L3/D0, len 42.9-90.0s, spread 2.10x, NOISY.
run0 L 42.9s dealt 231 taken 223 pieces 8/12  | "beached - 0 of 2 wheels touching the ground"
run1 L 46.2s dealt 290 taken 236 pieces **0/12** | KO - core destroyed
run2 W 90.0s dealt 516 taken  58 pieces 11/12 vs 5/11, enemy on its back 22.2 s
run3 L 90.0s dealt 413 taken 295 pieces 8/12 vs 10/11 | judges, structure (+28.7% damage, still lost)
**worst single-step piece loss: PLAYER 6.** Six parts left the chassis in ONE FixedUpdate.

### CRITICAL FINDING: MATERIAL BUYS ALMOST NO DAMAGE. 17x the stored energy -> 1.3x the damage.
Controlled A/B, same 12-part geometry, same opponent, same policy, n=4 each:
                     kJ/swing   cost   mass     damage dealt per match        mean
  c1 Titanium/W        9.37     3278   1135 kg  540 / 568 / 328 / 504         485
  c6 all ABS           0.55      279    398 kg  231 / 290 / 516 / 413         363
The two damage distributions OVERLAP (328-568 vs 231-516). 17.0x the stored swing energy and
11.7x the price bought at most 1.34x the damage, and at n=4 I cannot even call that significant.
What the money actually buys is STRUCTURE: Ti never went below 11 of 12 parts in 8 matches;
ABS hit 0/12 and 8/12 and shed six parts in a single step. The material table is therefore a
one-axis choice (survivability) sold to the player as a two-axis one (survivability AND hitting
power), and the LimbReport screen quotes an energy number that does not convert into the fight.
Suspect cause worth the implementer's time: DamageResolver caps and the BITE/EFFICIENCY chain
flatten the energy term, so per-hit damage saturates long before 3.3 kJ.

### Also reproduced here: the "whole-machine structural teardown" the previous dev round
### declined to fix because it could not reproduce it. It reproduces on ABS: player worst
### single-step loss 6 (sweep 05) and 3 (sweep 03), and two runs that ended at 0 of 11-12 parts.

### SWEEP 06 — c2_rotor_cf (SELF-BUILT spindle rotor: spindle + 2 CF beams + 2 Steel blades,
### 560 kg, 1533 cr) vs WIDOWMAKER/Champion, charge, n=4. W3/L1/D0, NOISY (spread 2.53x).
run0 W 36.6s dealt 505 taken  115 pieces 14/14 vs 8/11 | ENEMY pack torn off, dry after 27 s
run1 W 35.7s dealt 682 taken  696 pieces 12/14 vs 8/11 | ENEMY pack torn off, dry after 26 s
run2 L 90.3s dealt 933 taken 1277 pieces  8/14 vs 7/11 | judges, structure
run3 W 90.0s dealt 385 taken  507 pieces 14/14 vs 8/11 | judges, structure (WON while out-damaged)
The player-assembled rotor is the strongest weapon I built: 933 damage in one match, the highest
single card in this whole session, from a 560 kg CarbonFiber machine costing 1533 - less than
half the price of the Titanium hammer. It also survives: 14/14 parts intact in two of four.
Widowmaker/Champion losing 3 of 4 to a mid-cost build is consistent with brief item 7
(widowmaker 3W/17L) - that roster slot is still not a Champion-difficulty fight.

### BATTERY-SHEAR TALLY so far (n=24 matches, sweeps 01-06):
5 matches ended "N kJ of its 300 kJ pack was torn off the chassis" (2 player, 3 enemy).
That is 21% of all matches decided by one part on one seam leaving the robot. Brief item 6
is fully alive. It cuts both ways now, which makes it a coin-flip rather than a player trap,
but a single-socket instant-loss part is still the highest-variance object in the game.

### SWEEP 07 — c3_lance_w (ram spear, TUNGSTEN throughout, 3904 kg, 22386 cr)
### vs BULWARK/Champion, charge, n=4. **W4/L0/D0**, CONVERGED, spread 1.35x.
run0 W 73.5s dealt  807 taken 341 pieces 12/12 vs  9/13 | enemy pack torn off
run1 W 90.0s dealt  659 taken 487 pieces 12/12 vs 10/13 | judges, structure
run2 W 68.1s dealt 1038 taken 342 pieces 12/12 vs  0/13 | **KO - enemy core destroyed**
run3 W 66.6s dealt  512 taken 275 pieces 12/12 vs 10/13 | enemy pack torn off
**Player worst single-step piece loss: 0. It did not lose a single part in four matches.**
This is the only KO-by-core I produced from the attacking side, and the only 4-0 sweep.
Bulwark/Champion never landed a threat; brief item 7 ("bulwark never beats the player") holds.

### CRITICAL FINDING: THERE IS NO CREDIT BUDGET, SO TUNGSTEN IS STRICTLY DOMINANT AND COST IS FAKE.
`grep -Ei "budget|credits|maxCost|costCap|afford" BuilderManager.cs` finds no spend limit
anywhere; BuilderManager.cs:2222 carries the comment "Put it back here when there is a budget to
[...]". Cost is computed, displayed, and never constrains anything. The consequence is measured:
  qa_c3_lance_w  22386 cr, 3904 kg  -> W4/L0 vs a CHAMPION, 0 parts lost in 4 matches
  qa_c1_hammer_ti 3278 cr, 1135 kg  -> W3/L1 vs a VETERAN, 1 part lost per match
  qa_c6_hammer_abs  279 cr,  398 kg -> W1/L3 vs a VETERAN, dropped to 0/12 twice
The material table is presented to the player as a tradeoff and is currently a ladder. The only
brake on Tungsten is mass, and mass stopped being a brake when DRIVE_KW_PER_TONNE fell to 1.25 -
a 3904 kg machine draws 4.88 kW flat out, which the 300 kJ pack sustains for 61 s of continuous
full throttle, and no lance match ever counted the PLAYER out on power.

### SWEEP 08 — c4_flipper_al (pivot wedge flipper, Aluminum frame, Titanium beamlong+wedge,
### 929 kg, 1361 cr, HIGHEST stored energy of any build I made at 15.67 kJ/swing)
### vs MAULER/Veteran, charge, n=4. W1/L3/D0, CONVERGED.
run0 L dealt 284 taken 578  pieces  9/13 | on its back SELF 6.0s  ENEMY 0.3s
run1 L dealt  51 taken 328  pieces 12/13 | on its back SELF 7.8s  ENEMY 0.3s | player pack torn off
run2 W dealt 250 taken 344  pieces 13/13 | on its back SELF 8.4s  ENEMY 1.2s
run3 L dealt 317 taken 427  pieces  9/13 | on its back SELF 13.2s ENEMY 0.0s

### CRITICAL FINDING: THE FLIPPER FLIPS ITSELF. TOPPLING IS NOT A VIABLE WIN PATH FOR THE
### MACHINE THAT OWNS THE WEDGE - it is a win path for its OPPONENT.
Sum over the sweep: player on its back 35.4 s, enemy on its back 1.8 s. **19.7 : 1 against the
flipper's owner.** In four matches the wedge put the mauler down for a total of 1.8 s, which is
under the 2 s INCAP_GRACE, so it never once started a count. Meanwhile the flipper's own reaction
impulse (REACTION_FRAC 0.60 into a 221 kg.m^2 limb hung off ONE SIDE of the chassis at 1.375 m)
put the player down for up to 15% of the match.
And this is the highest-energy limb in the session (15.67 kJ/swing, 2x the hammer) delivering
the LOWEST damage of any actuated build (51-317 vs the hammer's 328-568). More evidence for the
"energy does not convert" finding above.

### CORRECTION TO THE PREVIOUS DEV ROUND'S OWN CLOSING FLAG.
qa_r1_dev.md MEASUREMENT 10 ends: "a wedge bot now puts its opponent on its back for up to 40%
of a match. That may be too easy". The numbers quoted there ("player flipped 4.4/16.8/16.8/36.5 s")
are the PLAYER'S OWN flipped time in sweeps where the PLAYER was the flipper. That is the wedge
bot on ITS back, not its opponent's. Measured here on the same archetype with the split
self/enemy fields the harness now prints: enemy 0.0-1.2 s. The flag as written is backwards and
the next implementer should not act on it.

### SWEEP 09 — c4_flipper_al vs TIPPER/Rookie, charge, n=4. W2/L2/D0, NOISY.
run0 W 86.4s dealt 517 taken 312 pieces 10/13 vs **0/12** | KO - enemy core destroyed | enemy on back **37.8s**
run1 W 90.0s dealt 440 taken 354 pieces 10/13 vs  6/12 | judges | enemy on back 26.4s
run2 L 90.0s dealt 200 taken 590 pieces 13/13 vs 10/12 | **"all 300 kJ spent in 80 s"** (a real drive-budget count-out, not a shear) | enemy on back 0.0s
run3 L 90.0s dealt 502 taken 790 pieces  6/13 vs  9/12 | judges | enemy on back 15.0s
worst single-step piece loss: player 2, ENEMY 5.
=> Toppling IS a real verb, but only against a machine that is already tippy. The same wedge
   that could not keep the mauler down for 2 s put the tipper down for 37.8 s and killed it.
   The verb is entirely opponent-shape-dependent and the build screen tells the player nothing
   about that.
=> The energy count-out is NOT gone. One in four here spent the whole 300 kJ on driving+spinning
   inside 80 s at 929 kg. Brief item 2 is reduced, not closed.

### CRITICAL FINDING: THE ROSTER'S DIFFICULTY LABELS ARE INVERTED. n=40 across 6 builds.
  vs MAULER   / **Veteran**   sweeps 01,02,03,04,05,08 ->  player  7W/17L   (29%)
  vs BULWARK  / **Champion**  sweep 07                 ->  player  4W/ 0L  (100%)
  vs WIDOWMAKER/**Champion**  sweep 06                 ->  player  3W/ 1L   (75%)
  vs TIPPER   / **Rookie**    sweep 09                 ->  player  2W/ 2L   (50%)
The two CHAMPION-labelled bots are the two easiest fights in the game (7W/1L combined, 88%), the
ROOKIE is harder than both, and the VETERAN mauler beats every archetype I built except the
Tungsten lance. A player climbing the ladder gets harder, then easier, then much easier. The tier
knob (EnemyRoster.DecisionInterval / SteerAggr / OpeningThrottle) is tuning AI reaction time while
the actual difficulty is coming from the ROSTER CHASSIS, and the two are not being reconciled.

## VISUAL EVALUATION (renders written by a temp Camera->RenderTexture->EncodeToPNG rig)
Files a human can open: Assets/Phase1/qa_r1_shotA.png (live 2-bot fight),
qa_r1_shot_arc0..9.png (hammer swing arc, 10 frames, timeScale 0.08, log in qa_c_arclog.txt),
qa_r1_shot_rot0..5.png (self-built spindle rotor, consecutive physics steps at timeScale 1).

Arc log (Titanium hammer, PIVOT return stroke, 3 fixed steps apart, timeScale 0.08):
  arc0 travel 2.339 rad | arc3 1.710 | arc6 1.082 | arc9 0.454  (uniform 3.49 rad/s = PIVOT_RETURN_DPS 200)

### VISUAL 1 — THE ARC READS, BUT ONLY ACROSS FRAMES. NO MOTION CUE EXISTS.
arc0 arm up at ~134 deg, arc3 arm horizontal, arc9 arm down past the chassis line. Sequenced,
the 150 deg sweep is completely legible and looks like an overhead axe. In ANY SINGLE FRAME the
arm is indistinguishable from a parked arm: no blur, no trail, no streak, no tip spark, nothing.
At the drive rate (10.95 rad/s, tip 1.25 m) the tip covers 0.27 m per physics step and roughly
0.23 m per rendered frame, so on a real display this is a hard-edged object jumping a quarter of
a metre between frames with zero cue. The physics is fine; the RENDERING of speed does not exist.

### VISUAL 2 — CRITICAL: THE WEAPON HEAD FELL OFF AND THE GAME NEVER SAID SO.
Between arc0 and arc3 (0.18 s of sim) the Tungsten blade left the arm tip. From arc3 on, the
"hammer" is a bare bolt-hole beam swinging an empty arc; the silhouette still reads as a hammer.
The blade is visible in arc9 lying flat on the floor at bottom-left as a dark plank whose value is
almost identical to the arena's shadow. There is no detach flash, no debris spin, no HUD line,
no change to the arm's appearance. A player would keep swinging a weapon that no longer exists.
(Per-part HP bars DO exist and DO read - a green strip on the engine, a yellow strip on the
damaged rear box - so the game already has the vocabulary for this and just does not use it
for the one event that matters most.)

### VISUAL 3 — PART IDENTITY GOOD, MATERIAL IDENTITY WEAK, OWNERSHIP ABSENT.
Function colour is strong and I could name every part by eye: battery = saturated yellow box with
a black ring, engine = green box with an orange cap, actuator = grey cylinder with a bright YELLOW
rotor face, wheels = black with a white bolt circle, structure = light grey with bolt holes.
Material is carried only by the base tint of structural parts. Titanium reads as a light warm grey
and the Tungsten blade as near-black, which is a real difference; but in qa_r1_shotA.png the
player and the enemy are both "grey box with yellow and green bits" and I could not tell which
machine was mine without reading rb.worldCenterOfMass out of the engine. There is no team tint,
outline, arrow or nameplate anywhere in the frame.

### VISUAL 4 — the SELF-BUILT SPINDLE ROTOR (qa_r1_shot_rot0..5.png)
Consecutive PHYSICS STEPS at timeScale 1, spindle rate 10.00 rad/s, travel 63.917 -> 64.917 rad
over 6 steps = 0.200 rad (11.5 deg) per step. Frames look like a rigid cross snapped to six
discrete angles. No blur, no arc ghost, no ring. Same absence of a motion cue as the pivot.
The actuator faces ARE kind-coded and that reads well: the PIVOT face is bright YELLOW
(qa_r1_shot_arc*.png), the SPINDLE face is bright CYAN (qa_r1_shot_rot*.png).
I checked my first read that a blade had come off the right arm: it had not. Measured
local positions confirm the limb is rigid and correct - beam at r=0.4495, blade at r=1.000,
faces flush. What made it read as detached is that the beam+blade pair has NO visual joint
and the blade is a different material tint, so a correctly assembled arm looks like a broken one.

### CRITICAL FINDING: EVERY PHASE 4 EDGE IS MADE OF GLASS - 22x LESS HP THAN THE BOX IT IS
### BOLTED TO - SO THE WEAPON DIES BEFORE THE ROBOT DOES.
Measured from a live rotor at t=28.4 s (all 14 parts still attached):
    core     178/178      chassis  396/396 and 291/396
    beam     158/158 and 155/158
    bladeXP   16/18       bladeXN   8/18     <- the two parts that do all the damage
An edge starts with 18 HP against a chassis's 396. Mass table (kg): Steel blade 12 vs Steel
chassis 471; Tungsten blade 29 vs Tungsten chassis 1158. And the edge is the ONE part that
touches the enemy on every swing, so it also eats DamageResolver.EXPOSED_MULT 1.75 every time.
Consequence, observed directly in qa_r1_shot_arc0 -> arc3: the Tungsten blade left the Titanium
hammer's arm inside 0.18 s of sim, and the bot went on swinging a bare beam.
This single number explains three other findings at once: why buying a 17x-energy limb does not
buy damage (the edge is gone), why ABS bots disintegrate (all their parts are edge-thin), and
why the highest-energy build in the session (the flipper, 15.67 kJ) dealt the least damage.

### CRITICAL FINDING (measured, qa_c_bladehp2.txt): THE BATTERY FALLS OFF AT **FULL HP**.
Instrumented run, qa_c1_hammer_ti vs mauler/Veteran, sampling every 45 fixed steps:
  t=19.2  [core_0 186/227] [battery_3 **56/56**] ...
  t=20.1  [core_0 186/227] [battery_3 **56/56 DET**] ...
The 240 kJ battery separated from the chassis with 56 of 56 hit points - it had taken ZERO damage
in the entire match. Losing it is an instant loss condition. So the most decisive object in the
game is removed by the SEAM/STRESS solver (CompoundRobot BREAK_K / STRESS_J_CAP / SUBTREE_K),
not by the damage model, which means no amount of armour, material or HP investment can protect
it and the player has no counterplay and no readable warning. This is the mechanism behind the
6 of 40 matches (15%) that ended "N kJ of its 300 kJ pack was torn off the chassis".
The same run shows the Tungsten blade at 22/22 for 27 s, so the arc0->arc3 blade loss I saw in
the visual pass was very probably the same full-HP seam shear rather than attrition.
CAVEAT, stated so nobody over-reads this run: I drove with aiThrottle=1 and NO steering, so the
bot spent most of the match not engaged. Do NOT take the 67 total damage in this run as a DPS
number; the sweep numbers above are the DPS evidence. The full-HP detach is what this run proves.

### RE-MEASURED BRIEF ITEM 3 ("CoM does not move as a limb swings"): **FIXED, and quantified.**
Live rig, qa_c1_hammer_ti parked, debugFire pulsed, sampled every fixed step (qa_c_com.txt):
  travel 0.000 -> rb.centerOfMass = (0.0684, 0.1056, -0.0072)
  travel 1.668 -> (-0.0066, 0.1056, -0.0753)
  travel 2.478 -> (-0.0539, 0.1056, -0.0493)
  full range 0.000..2.618 rad (exactly PIVOT_ARC_DEG 150), **centerOfMass.x sweeps 0.0684 ->
  -0.0592, a 12.8 cm horizontal excursion, updated every step, and it returns exactly.**
Actuator.cs:789 calls robot.RecomputeMass() and it demonstrably works. Brief item 3 is closed.

### RETRACTION - I got one of my own visual reads wrong and I am correcting it here.
Earlier in this file I wrote (VISUAL 2) that the Tungsten blade fell off the hammer arm between
arc0 and arc3. That is WRONG and I withdraw it. Two later instrumented runs contradict it:
qa_c_bladehp2.txt shows [bladeXP_7 22/22] attached for the whole 27 s match, and the rotor
position dump showed limb tip parts exactly at their build radii. What actually happened is
that the pivot's axis in this build is (0,1,0), so the hammer sweeps HORIZONTALLY about a
VERTICAL axis - confirmed independently by the CoM trace above, where centerOfMass.y is pinned
at 0.1056 through the entire sweep while x moves 12.8 cm. From an elevated 3/4 camera a
horizontal sweep traces up-across-down on screen, which is what I mistook for an overhead axe
and for a part leaving the arm.
THE UNDERLYING LEGIBILITY PROBLEM IS REAL AND SURVIVES THE RETRACTION, restated correctly:
**an intact limb reads as a broken one.** There is no visual joint or collar between the beam
and the edge, the edge is a different material tint from the arm, and there is no motion cue,
so a correctly assembled swinging arm and a snapped-off part lying on the floor look the same
in a still frame. I am a QA agent with the part table in front of me and I could not tell them
apart; a player will not either. That is the finding an implementer should act on, not my
retracted version of it.

## CLOSING STATE (verified, not assumed)
owen's build RESTORED and loaded: 790 bytes, 16 parts, 4 wheels, mass 842.5 kg, Validate()==null.
qa_owen_build_SAFE.txt untouched this session (md5 below) and byte-identical to qa_build_prewheelfix.txt.
BuilderManager.mode = Build. Time.timeScale = 1. Phase0Input.debugFire = False.
RepeatHarness / ShotHost / QACam objects destroyed. No .cs file was edited, so no domain reload
was triggered at any point in this session.
Unity console: 3 Errors total, ALL predating this session (timestamps 16:08:42, 16:08:55,
16:13:41 vs this session's 23:2x-23:5x) and all from the Unity MCP tooling itself
(Unity.Camera.Capture called with a stale GameObject instance ID, and a missing
'Edit/Clear Console' menu item). ZERO errors originate from the game or from my commands.
The previous qa_r1_critic.md was preserved as qa_r1_critic_ARCHIVED_prevsession.md.
New files this session: qa_c1..c6_*.txt (builds), qa_c_buildstats.txt, qa_c_sw01..09.txt,
qa_c_arclog.txt, qa_c_rotlog.txt, qa_c_bladehp.txt, qa_c_bladehp2.txt, qa_c_com.txt,
qa_r1_shotA.png, qa_r1_shot_arc0..9.png, qa_r1_shot_rot0..5.png.

## SUMMARY — 40 matches (36 harness runs across 9 sweeps + 4 instrumented single matches),
## 6 builds spanning all 7 materials and 5 weapon systems, 4 opponents, all 3 tiers.
WHAT IS GOOD AND SHOULD NOT BE TOUCHED:
 - Phase 4 actuators work. Every limb I built wired correctly (blockedBy=null, parts>0 on 5/5).
 - CoM now tracks limb pose live (12.8 cm measured excursion). Brief item 3 closed.
 - AI fire is wired (AIController.cs:244). Brief item 1 closed on the source side.
 - Actuators are the reason input started mattering: hammer charge 3W/1L vs afk 0W/4L, while the
   no-actuator control is 1W/3L either way. That kills the oldest open finding in the project.
 - Function-colour part identity and per-part HP bars are genuinely legible in-frame.
WHAT IS BROKEN, RANKED:
 1. No credit budget exists, so material is a ladder: Tungsten went 4-0 vs a Champion losing zero
    parts; ABS went 1-3 and hit 0/11 twice. 22386 cr costs the player nothing.
 2. Energy does not convert to damage: 17x the stored swing energy bought at most 1.34x the
    damage in a controlled same-geometry A/B.
 3. The battery detaches at FULL HP via the seam solver and that instantly loses the match;
    15% of all matches (6/40) ended that way, with no counterplay and no warning.
 4. Difficulty labels are inverted: Champions 7W/1L for the player, the Veteran mauler 7W/17L.
 5. The wedge flipper topples ITSELF 19.7x more than its opponent (35.4 s vs 1.8 s over 4 matches).
 6. No motion cue on any moving limb, at any speed, in any renderer path.
 7. An intact limb reads as a broken one - no joint/collar art between the beam and the edge.
 8. No ownership cue: I could not tell my robot from the enemy in a still frame.
