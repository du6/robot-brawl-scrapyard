# Beating Spinner1 — the design log

**Started 2026-08-18.** A standing brief for whoever (human or agent) is trying
to build a FEATHER machine that beats owen's champion. **This document is the
memory of that effort**: read it before designing anything, and add to it after
every measured run. A candidate that failed for a reason nobody wrote down will
be built again.

> ## ✅ SOLVED, 2026-08-18 — read §4.5 first, then §6
>
> **`bulwark_v1` beats Spinner1 16–0** across two independent 8-bout sweeps,
> losing 0 parts and disarming the champion in 8 of 8. A stripped version at
> **Spinner1's own 855 kg and 15 parts** (`abl_min`) goes 15–1. Both builds
> are printed verbatim in §6. **Both carry an EMPTY program** — they run the
> same built-in AI the champion does.
>
> The answer, in one line: **bolt on a two-armed Titanium rotor and a
> CarbonFiber frame, and weld the joints.** Nine measured sweeps (§4) say the
> rotor is the whole result and everything else is refinement — including the
> 700 kg of spare weight this page used to call "the single biggest fact"
> (§5.1: wrong).
>
> ⚠ **Every conclusion here is conditional on Spinner1**, a machine with no
> engine, no gussets and a single-sided aluminium bar. It is now the *easy*
> fight. See "What I would try next".

Rules of engagement, and they are enforced by the harness, not by good manners:

1. **Same weight class.** FEATHER, cap **1500 kg** (`RobotCategory.LADDER`).
2. **No copying owen's design.** At most **30%** of a candidate's parts may sit
   at exactly the same id + position + yaw + axis as one of Spinner1's.
3. **Legal.** The game's own validator (`RobotSnapshot.Describe`) must pass it —
   the same one the cloud worker runs on an enlisted robot.

---

## 1. The target, measured

```
Spinner1 · owen · FEATHER · rating 1617 · 8W-2L · 3 career titles
16 parts · 799 kg · legal · PROGRAM: EMPTY (it fights on the built-in AI)
```

Architecture, read off its build string:

| what | detail |
|---|---|
| chassis | 4 aluminium beams in two rails, core at the origin |
| drive | **4 rubber wheels**, wide track (±0.42 m), long base (0.15 → −0.75) |
| power | one aluminium battery, mounted high at the back |
| weapon | **spindle on the Y axis** at 1.00 m → `beam` + `beamlong` bar → **steel spike** at the tip |
| reach | the spike sits at z ≈ **+1.37 m** from the core |

So: **a horizontal spinner.** A long bar on a vertical axle sweeping a circle
about 1.4 m in radius, with the only hard point (steel spike) at the very end,
where the tip speed is highest. Everything else on the machine is aluminium.

⚠ **It weighs 799 kg in a 1500 kg class.** A challenger may legally carry
**700 kg more machine than the champion** — nearly double.

⚠ **CORRECTED 2026-08-18. This paragraph used to end "that is the single
biggest fact on this page", and it is not.** It was written before anything
had been measured and it was wrong: `abl_min` beats Spinner1 15–1 at **855 kg**,
spending 56 kg of the 700. The surplus buys resistance to being flipped and
nothing else that showed up in 72 bouts. The single biggest fact on this page
is that **Spinner1 carries no engine**, so its rotor winds at 1.0 kW — see
§2.5. Left in with the correction attached rather than deleted, because "a
plausible sentence nobody had measured" is this project's signature failure
and the shape of it is worth seeing.

⚠ **It has no program.** It is driven by the built-in AI, because that is how
it is enlisted. Do not "fix" this by giving it one: that would be measuring a
robot that does not exist on the ladder.

## 2. The harness

`Assets/Phase1/Scripts/ChallengeBench.cs`. Play mode, over the Unity bridge.

```csharp
ChallengeBench.Seeds = new[] { 101, 202, 303, 404 };   // bouts = seeds x 2 sides
ChallengeBench.RunSpec("my_candidate", buildText, programJson);
// poll .finished, then read .report / .wins / .losses / .verdict
```

* **Every candidate is fought from both spawn sides.** `SpawnPoses` gives A and
  B different positions and headings and this game has a documented opening-ram
  effect; a number measured from one side is partly measuring the spawn. The
  report splits wins by side — **a lopsided split is the spawn talking.**
* The per-candidate report is overwritten each run
  (`Assets/Phase1/qa_challenge_bench.txt`); a one-line ledger of every candidate
  ever fought is appended to `Assets/Phase1/qa_challenge_history.txt`.
* ⚠ **The engine's `cause` string is written from side A's point of view** —
  "your core was destroyed" means A's core. The report keys each cause by the
  challenger's own result so it cannot be misread. Do not undo that.
* ⚠ **Career state**: the bench suspends autosave for the sweep. Fingerprint
  `robotbrawl_career.json` (md5 **and mtime**) either side of a session anyway.

### ⚠ THE BENCH IS NOT REPRODUCIBLE. Measured, by accident, on day one.

`REFERENCE_ARMED` was run **twice, same build, same program, same two seeds**.
It lost 0-4 both times, and nothing else matched:

| | run 1 | run 2 |
|---|---|---|
| damage dealt | 716.1 | 488.9 |
| damage taken | 4322.8 | 4183.0 |
| parts lost | **76** | **58** |
| bouts ending with no weapon left | **4/4** | **2/4** |
| how one bout ended | KO, core destroyed | **counted out — out of power** |

Same inputs, a bout that ended a completely different way. Whether the cause is
physics non-determinism or arena state carried between sweeps, the consequence
for a designer is identical and it is the single most important thing on this
page after the champion's mass:

> **A 4-bout run cannot distinguish a better design from a luckier one.**

So: **8 bouts (4 seeds × 2 sides) is the minimum for a real reading, and any
candidate that looks like a winner MUST be re-run before it is believed.** The
project has been here before — `CareerBench_Sample_Size_2026-08-10` found three
of eleven balance verdicts were coin flips, and two runs of an *unchanged*
build gave 12/5 and 10/7. Do not report a 5-3 as a win.

### Two traps that cost an hour on day one

* **`Main.unity` has no `BuilderManager`** and only a title-screen click ever
  creates one. The bench makes its own.
* **A builder that survives a domain reload comes back with an empty palette**
  and refuses every `LoadSnapshot`, returning 0 — which reads as an illegal
  candidate, not a broken harness. The readiness signal is neither `!= null`
  nor `PaletteCount`: it is `buildRoot`, which nothing exposes. The bench polls
  the *operation* (load a known-legal build until it returns non-zero).

## 2.5 The physics, read off the source — the numbers a designer needs

⚠ Everything in this section is quoted from the code, not measured in a bout.
It tells you what the engine *can* do; only the ledger tells you what wins.
File refs are given so the next reader checks the artifact, not this note.

**Durability.** `HpOf = 6000 × strengthRel × volume_m³`, and a WEAPON part's
volume is floored at `EDGE_MIN_VOL = 0.012 m³` (`DamageResolver.cs:241`).

**Damage.** `dmg = effImpulse × strikerHardness × 0.045`, then
`× 0.25` if the struck part is ITSELF a weapon (`WEAPON_VS_WEAPON`),
`× 1.75` if the victim is flipped. One hit per victim part per **0.5 s**
(`PART_IMMUNITY`). `edgeHardness`: blade **1.7** · spinner 1.5 · hook 1.3 ·
spike 1.2 · wedge 1.15 · everything structural 1.0.

**A structural bump does NOTHING.** `STRUCT_RAM_DMG = 0` and
`STRUCT_SEAM_MUL = 0`: a part with `edgeHardness ≤ 1.01` striking another
robot deals zero HP *and* zero seam stress. Every damage path in the game
needs an edge.

**Seams.** `threshold = min(matA, matB).strengthRel × 1500 × mult`, where
`mult` = mated socket count, `× 3` if either side is an actuator, `× 4` if
either mating face carries a gusset. Robot-vs-robot contact stress is hard-
capped at **4200 N·s** (`STRESS_J_HARD`), so **any seam over 4200 cannot be
sheared by anything.** Sockets are on a 0.15 m pitch and multiply across BOTH
tangent axes; wheels and non-actuator weapons expose a **single centre
socket**, so every blade/spike/disc seam is ×1 until it is gusseted.

**Rotors.** A disc has no motor — it is a `spindle` (actuator) with a limb
bolted past it. `E = ½·I·ω²`, `ω = min(94.25, v_tip/R_tip)`, and
`v_tip = clamp(12.5·√strengthRel, 9, 18)` **taken from the WEAKEST material
anywhere on the limb** (`Actuator.cs:156`). A bite drains `0.4·E` and feeds it
straight in as effective impulse. Spin-up time ≈ `E / (motorKW × 1000)`;
`motorKW = min(12, 1 + 1.5 × engines)`, so an engineless machine winds at
**1.0 kW** and that — not top speed — is what an engine buys.

**Power.** battery = 240 kJ / 8 kW; engine = 60 kJ / 14 kW. A bite costs
`0.4·E / 0.35` off the pack. Driving is nearly free: 1.25 kW per tonne.

**Losing.** KO (core gone) · count-out after 12 s of: flipped-and-slow, no
wheels, beached, or **flat pack** · then the judges, in strict order:
**structure fraction** (`partsNow/startParts`, band `max(0.06, 1.5/startParts)`)
→ damage dealt (band `max(25, 8%)`) → aggression → time spent inverted.
**Structure outranks damage**, and the band is set by the SMALLER part count —
against Spinner1's 16 parts it is 0.094.

### The four consequences that decided this design

1. **Weapon parts are the best armour in the game.** A Titanium `blade` is
   6.8 kg, is HP-floored to 0.012 m³ = **100.8 HP**, and takes **×0.25** from
   any edge — ~400 effective HP per 6.8 kg. A Titanium `plate` is 68 kg for
   126 HP at full rate. **Blades are roughly twenty times better armour per
   kilogram than armour plate**, and they hit back at 1.7.
2. **CarbonFiber is the best structural material** (strengthRel 1.1 at
   1.6 g/cm³): 4.1 HP/kg against Titanium's 1.9 and Steel's 0.76, and a
   3-socket CF seam is 4950 — already past the 4200 stress cap.
3. **Titanium is the best ROTOR material**, because rotor energy per kilogram
   is `½·v_tip²` and `v_tip` goes as `√strengthRel`. Tungsten wins per cubic
   metre and loses per kilogram, and mass is the thing the class caps.
4. **Spinner1 winds at 1.0 kW because it carries no engine.** Its ~4.7 kJ
   rotor needs ~4.7 s to spool from cold and ~1.9 s to recover from each bite.
   Its whole sustained output is bounded by that 1 kW.

## 3. What the game already knows, before anyone builds anything

Read these before proposing a design — they are measured, not folklore:

* **Weapons are LOST, not beaten.** 69% of weapons that leave a body still have
  HP — the limb carrying them fails first
  (`Mutual_Disarm_Root_Cause_2026-08-09`). Armour on the weapon is the wrong
  place to spend mass; **the seam behind it** is the right place.
* **A gusset makes one joint ×4 harder to tear** for +10 kg
  (`Gusset_x4_2026-08-18`). Ten kilograms out of a 700 kg surplus is nothing.
  Spinner1 carries **no gussets at all** — its bar is bolted on plain.
* **Seam strength is socket count × material × gusset.** A one-socket seam is
  the weakest joint in the game; a five-socket seam is 5× stronger before any
  gusset. Wide, flush contact patches are free strength.
* **Direction is the SIGN of the power argument**, and
  `SensorBus.forwardLocal` is the BUILD's drive axis, not `transform.forward`.
* **A program needs the sensor for the question it asks** — compass for the
  enemy, wall sensor for the wall, and so on (`RobotProgram.SensorIdOf`).
  Blocks you have no sensor for cannot be placed.
* **The topmost hat whose WHEN is true wins**, every tick.

## 4. The ledger

| # | candidate | idea in one line | W/L/D | what it taught |
|---|---|---|---|---|
| 0 | `REFERENCE_ARMED` | the bench's stock fixture, no program — a control leg, not a design | **0/4/0** (twice) | Spinner1 dismantles; and the bench is noisy — see §2 |
| 1 | `bulwark_v1` | 1314 kg · CF frame · Titanium two-armed rotor · blades as armour · gussets · no program | **8/0/0** | it works, and it is not close — 0 parts lost, Spinner1 disarmed 8/8 |
| 2 | `bulwark_v1` re-run, seeds `17/555/909/1234` | the confirmation leg | **8/0/0** | **16–0 over 16 bouts across two seed sets.** Not luck |
| 3 | `abl_norotor` | v1 with the spindle and its whole limb deleted — 860 kg, 23 parts, **no weapon at all** | **5/3/0** | **prediction WRONG (I said 1–3).** An unarmed fortress beats the champion 63% of the time — *on the judges' STRUCTURE criterion, while being out-damaged 3-to-1* |
| 4 | `abl_noengine` | v1 with both engines gone — motor 1.0 kW, 1082 kg | **6/2/0** | prediction right on the record, **wrong on the mechanism**: bouts got no longer (32.6 s). The engines' real value is that they turn 6–2 into 8–0 — damage taken 1000 → 4353, parts lost 0 → 15 |
| 5 | `abl_alurotor` | v1 with the rotor limb in Aluminium — `v_tip` 9.68 not 14.79, so E ≈ 2.0 kJ not 8.7, bite ≈ 60 not 265 | **8/0/0** | **prediction WRONG (I said 2–5).** A rotor four times weaker still goes 8–0 and still disarms Spinner1 8/8. **Having a spinner is what matters; how hard it hits is a refinement** |
| 6 | `abl_min` | the stripped design: 2 rails, 4 wheels, core, spindle, rotor, 1 engine, 1 battery — **855 kg / 15 parts, i.e. Spinner1's own mass and part count** | **8/0/0** | prediction right. **The 700 kg surplus was never necessary.** 1 part lost to 97 |
| 7 | `abl_min` re-run, fresh seeds | the confirmation leg | **7/1/0** | **15–1 over 16 bouts.** The single loss was a flip with no way back up — which is what the surplus mass in `bulwark_v1` was actually buying |
| 8 | `abl_min_nogusset` | byte-identical to `abl_min` with every `\|G:` mask stripped — 765 kg, same 15 parts | **7/1/0** | gussets do **not** decide the win here, they decide the damage: parts lost per 8 bouts **1–2 → 16**, and one bout went 15-of-15 and a cored chassis |

### Run 0 — the control leg

Not a design attempt. `VerbBench.ARMED` (924 kg, 19 parts, a small spindle
spinner) with no program, purely to prove the harness runs and to get a floor.

```
0 W / 4 L / 0 D          damage 716 dealt vs 4323 taken
parts lost 19 vs 3       lost EVERY part in EVERY bout
no weapon left: 4/4 bouts for the challenger, 0/4 for Spinner1
mean bout 33.8 s
```

**Spinner1 does not merely win, it dismantles.** Nineteen parts lost out of
nineteen, four times out of four, while taking three parts of damage in total
across the whole sweep. Any candidate that fights it in the open and trades
hits is going to read like this.

That is the floor. Everything from here is trying to beat it.

### Run 1 — `bulwark_v1`, the prediction (WRITTEN BEFORE THE RUN)

A 1314 kg, 31-part machine built from the mechanics in §6 below: CarbonFiber
frame, Titanium two-armed spindle rotor (~8.7 kJ), Titanium blades as armour,
gussets on every load-bearing seam, no program (AI-driven, like the champion).

**Prediction: 6–8 wins of 8**, most bouts ending by KO or by Spinner1 being
counted out, mean bout well under 90 s.

Reasoning: one bite of my rotor should be ~265 HP against structure, which
one-shots every part on Spinner1 (its heaviest is a 144 HP aluminium
`beamlong`); one bite of its rotor is ~100 against structure but ~25 against a
Titanium blade. Named risks, in the order I expect them to bite: (a) my strike
band is 0.58–0.83 m above the deck and most of Spinner1's mass is below that —
if the heights miss, nothing lands; (b) 2.2 s of spin-up at 4 kW motor;
(c) my rotor ARMS are bare structure and eat full damage; (d) 1314 kg on six
wheels may simply be too slow to close.

### Runs 3–6 — the ablations, and their predictions (WRITTEN BEFORE THE RUNS)

A machine that wins 16–0 has told you it wins and nothing about **why**. Four
legs, each removing ONE thing from `bulwark_v1`, so the doc can attribute the
result instead of asserting it. Predictions first, scored after.

| leg | what changes | prediction |
|---|---|---|
| `abl_norotor` | the spindle and its whole limb are deleted (827 kg fortress, cladding blades still bill as rams) | **1–3 W of 8** — it can no longer kill, so it needs the judges |
| `abl_noengine` | both engines gone → motor 1.0 kW, exactly Spinner1's wind-up. Rotor ENERGY is unchanged (the tip-speed cap sets ω, not the motor) | **5–8 W of 8**, but visibly longer bouts |
| `abl_alurotor` | rotor limb Aluminium instead of Titanium → `v_tip` 14.79 → 9.68 m/s, E ≈ 8.7 kJ → ~2.0 kJ, bite 265 → ~60 | **2–5 W of 8** |
| `abl_min` | 824 kg / 15 parts — the same mass and part count as Spinner1 itself, keeping only the rotor, the gussets and the CF frame | **6–8 W of 8** |

The one I most expect to be wrong is `abl_noengine`: if the AI only holds the
spindle trigger inside 5.5 m, a 8.7 s cold spin-up at 1 kW may never finish.

### Run 3 scored — `abl_norotor` 5W/3L, and it is the most interesting number on this page

I predicted 1–3 wins. It won **5 of 8 with no weapon on it at all.** Read the
report before deciding what that means, because the raw record hides the
mechanism completely:

```
damage dealt: 1936.1 by abl_norotor  vs  8111.5 by Spinner1      <- out-damaged 4:1
parts lost:     51 by abl_norotor    vs    25 by Spinner1        <- out-broken 2:1
mean bout length 81.8 s  (six of eight bouts went the full 90 s to the judges)
five wins all read: "Judges' decision — damage 301 vs 582 (48.3% margin THEIR way) · pieces …"
```

**It won five bouts on the judges' STRUCTURE criterion while losing the damage
criterion in every one of them.** `FightManager.Judge` tests structure FIRST
and returns on it (`FightManager.cs:798-878`); damage is only reached when the
structure fractions are within `max(0.06, 1.5/startParts)`. `structFrac` is
`partsNow / startParts` — a *fraction* — so 23 parts losing 3 (0.87) beats
16 parts losing 5 (0.69) and the 1190-vs-176 damage column is never consulted.

Three things follow, and they are worth more than the win column:

1. **Part count is a defensive statistic in this game.** Every extra part you
   bolt on makes each part you lose cheaper at the judges' table. This is
   probably the single most under-exploited rule in the ruleset.
2. **A 5–3 is not a win** (§2), and this is exactly the case the rule was
   written for. `abl_norotor`'s three losses were not near misses — they were
   dismantlings (23 of 23 parts, 14 parts) at 48 s and 55 s. It is a machine
   that either survives to the judges or is destroyed outright, and which of
   those happens is the spawn.
3. ⚠ **Flagged for owen, as a balance observation and not a finding I acted
   on:** a machine that cannot deal damage can take the FEATHER champion to a
   63% record purely by being made of more pieces. Whether that is the
   intended shape of the judges' card is a design call, not a bench call.

## 4.5 How to build a machine that beats Spinner1

Written for whoever picks this up next, human or agent. It is ordered by
**measured** importance, not by how clever it sounds. Everything here is
backed by a row in the ledger; where it is not, it says so.

### The five rules, in order

1. **BOLT ON A SPINDLE ROTOR. Nothing else in the ablations came close.**
   Removing the rotor took a machine from 16–0 to 5–3; making the rotor four
   times weaker changed nothing at all (`abl_alurotor`, 8–0). Spinner1's own
   weapon is a bar on a spindle — **you beat it by doing the same thing
   properly**, not by finding a clever counter. §5's old hypothesis 2 ("do not
   out-spin a spinner") was the most expensive wrong sentence on this page.
2. **Two arms, not one.** Spinner1's bar is single-sided. A balanced two-armed
   rotor hits twice per revolution for the same top speed, and it does not
   fight its own gyroscopic wobble.
3. **CarbonFiber for structure. Titanium for the rotor. Never mix into a
   limb.** CF is 4.1 HP/kg (Titanium 1.9, Steel 0.76) and a 3-socket CF seam
   already exceeds the 4200 N·s stress ceiling — a CF raft cannot be broken
   apart. But `v_tip` is set by the **weakest** material anywhere on the limb,
   so one CF part on a Titanium arm costs you 22% of your rotor energy.
4. **Gusset the whole load chain.** ×4 for 10 kg. A gusseted 1-socket seam is
   6600 in CF and 8400 in Titanium; both are past the 4200 cap, i.e. literally
   unshearable. Gusset the actuator seams (they go to ×12), the arm joints,
   the tips and the battery mount. Do NOT bother gusseting cladding.
   ⚠ **Measured (run 8): gussets did not change the win rate — they changed
   the wreckage.** Stripping every weld off `abl_min` left it 7–1, the same
   record, while parts lost per 8 bouts went **1–2 → 16** and one bout ended
   with the machine stripped to nothing and cored. Fit them because you want
   to still have a robot afterwards, not because they win the bout.
5. **Carry parts.** The judges test STRUCTURE before damage and score it as a
   *fraction*. 31 parts against 16 means every piece you lose costs you half
   what it costs the champion. This alone is worth 5–3 with no weapon fitted.

### What each lever is actually worth, measured

| lever | evidence | verdict |
|---|---|---|
| a spindle rotor, at all | `abl_norotor` 5–3 vs `bulwark_v1` 16–0 | **decisive — this is the whole thing** |
| rotor MATERIAL (Ti vs Alu) | `abl_alurotor` 8–0, damage taken 1000 → 2180 | wins either way; Titanium buys a cleaner sheet |
| engines (motor 4.0 vs 1.0 kW) | `abl_noengine` 6–2, parts lost 0 → 15 | the difference between winning and winning every time |
| the 700 kg mass surplus | `abl_min` 15–1 at 855 kg | not needed to win; it is **flip insurance** |
| gussets | run 8, 7–1, parts lost 1–2 → 16 | survivability, not victory |
| a program | never used; best candidate is 16–0 without one | unnecessary here |

### The mass budget, as actually spent (1314 kg of a 1500 kg class)

| | kg | why |
|---|---|---|
| CF frame, 4 `beamlong` rails | ~256 | 5-socket lateral seams = 8250 N·s, unbreakable |
| Titanium rotor (hub + 2 arms + 4 blades) | ~365 | ~8.7 kJ, ~265 damage a bite |
| 2 engines | ~213 | motor 1.0 → 4.0 kW. **Worth 6–2 → 8–0** |
| 3 batteries | ~127 | 840 kJ; a bite costs ~10 kJ off the pack |
| 6 wheels | ~82 | wheels carry no HP and cannot be HP-killed — cheap insurance against the no-wheels count-out |
| core + spindle + 7 cladding blades | ~150 | |
| 12 gussets | 120 | the cheapest strength in the game |

### Things that are true and cost nothing to exploit

* **A structural bump does zero damage and zero seam stress.** If a part's
  `edgeHardness ≤ 1.01` it cannot hurt anything. Corollary: your armour can be
  hit by the enemy's *chassis* all day for free.
* **Weapon parts take ×0.25 from weapons.** A Titanium `blade` is 6.8 kg for
  100.8 HP at quarter rate — call it 400 effective HP per 6.8 kg, against an
  armour `plate`'s 126 HP at full rate for 68 kg. **Clad in blades, not
  plate.**
  ⚠ **This is the one rule here that is NOT measured, and it cannot be
  cleanly measured with this catalogue.** `bulwark_v1` never lost a part, so
  its cladding was never loaded; and there is no non-weapon part with a
  blade's footprint, so a swap changes geometry and part count at the same
  time as it changes the multiplier. It is read straight out of
  `DamageResolver.ApplyHit` (`WEAPON_VS_WEAPON = 0.25`) plus `HpOf`'s
  `EDGE_MIN_VOL` floor. Treat it as sound arithmetic and an open bench, not
  as a result — and do not quote it back as measured. **A clean test would
  need a structural part sized like a blade, which the catalogue does not
  have; that, not another sweep, is what would settle it.**
* **Spinner1 has no engine**, so its motor is 1.0 kW and its whole sustained
  output is bounded by it. One engine on your side halves your wind-up.

### Things that are NOT needed, measured

* **A program.** `bulwark_v1` runs the built-in AI, exactly like the champion,
  and goes 16–0. Do not spend effort here until something else is exhausted.
* **A maximal rotor.** 2 kJ was as good as 8.7 kJ for the win column.
* **The full 1500 kg.** 855 kg goes 15–1 (`abl_min`).

### What I would try next, and why

Nine sweeps in, the design space against *this* opponent is exhausted — every
remaining lever is a refinement of a machine that already wins 16–0. The four
questions actually left open, in the order I would spend sweeps on them:

1. **The wedge.** Hypothesis 4 is untried and three wins already came from
   Spinner1 being flipped *by accident*. `wedge` is the only part with
   `liftBias` (0.75) and `Actuator.ApplyTopple` reads it off the striking
   part, so a wedge on the rotor tip converts bite energy into roll. It could
   plausibly win faster and with less damage than a blade.
2. **Blades versus plate, honestly.** Needs a structural part the size of a
   blade, which the catalogue does not have (see the ⚠ above). This is a
   catalogue question, not a bench question.
3. **A harder opponent.** Every conclusion on this page is conditional on
   Spinner1: no engine, no gussets, a single-sided aluminium bar. The right
   next target is `bulwark_v1` vs `abl_min` — the champion is now the easy
   fight, and `ChallengeBench` hard-codes `CHAMPION_BUILD`.
4. **A program, finally.** Only worth it once the opponent is good enough that
   the built-in AI's backoff-after-8-damage and its off-axis flanking are
   costing bouts. Against Spinner1 they never did.

### The one thing that will silently break your build

`Actuator.Wire` defines the limb as *whatever component touches the actuator*.
Any chassis part whose face lands within 0.03 m of the swept limb is welded
into the rotor and spins with it. The build stays legal and the mass stays
right; the machine is just no longer the machine you drew. Check the vertical
clearances by arithmetic before you spend a sweep — in `bulwark_v1` the power
cells top out at 0.53 m and the rotor's underside is at 0.58 m, and that 0.05 m
is the tightest number in the design.

## 5. Open hypotheses — untested, listed so they are not re-invented

None of these are recommendations. They are the obvious first moves, written
down so that a run which kills one is recorded as progress.

1. **Spend the 700 kg.** A machine at ~1450 kg is nearly twice Spinner1's mass.
   Momentum resists being thrown, and mass is free within the class.
2. **Do not out-spin a spinner.** The control leg brought a smaller spinner to
   a bigger spinner's fight and lost every part. A design that survives the
   first exchange is worth more than one that trades.
3. **Attack the bar, not the body.** Spinner1's whole offence hangs off one
   spindle seam with no gusset. If that seam goes, it is a 799 kg drive with a
   battery on it.
4. **A wedge gets under things.** The champion's spike is at 1.03 m, well above
   the floor; a low wedge may pass under the swing entirely.
5. **Armour is a shape, not a material.** A plate that deflects is worth more
   than a plate that absorbs, and both cost the same kilograms.
6. **The program may matter more than the parts.** Spinner1 has none. A
   challenger that keeps its distance until the bar spins down, or that only
   commits when the bearing is right, is playing a different game from the AI.

⚠ **Write the prediction down BEFORE the run** (house rule 6) and score it
after. A hypothesis that quietly becomes a conclusion is how this project
ended up with three documents repeating a fact nobody had measured.

**Scored, 2026-08-18, against the ledger.**

1. **Spend the 700 kg — WRONG as stated.** It was the loudest claim on this
   page and it is not what wins. `abl_min` goes 15–1 at **855 kg**, inside
   60 kg of Spinner1's own mass. What the surplus actually buys is narrower
   and worth knowing: `bulwark_v1` (1314 kg) is 16–0 and `abl_min` is 15–1,
   and `abl_min`'s one loss is *being flipped with nothing aboard to right
   it*. **Mass is flip insurance, not a win condition.**
2. **Do not out-spin a spinner — WRONG, and the most expensive sentence
   here.** Out-spinning it is exactly what works. The control leg
   (`REFERENCE_ARMED`) lost because it brought a small **aluminium** rotor,
   not because it brought a rotor: `abl_alurotor` shows even an aluminium
   rotor wins 8–0 once the rest of the machine is right.
3. **Attack the bar** — untested as an aiming policy; nothing here steers.
   Spinner1 loses its weapon in **31 of 32 bouts** against any rotor-carrying
   candidate regardless, so the intent never had to be expressed.
4. **A wedge gets under things — STILL OPEN, and now the most interesting
   untried idea.** Three of the eight `abl_min` wins were Spinner1 flipped and
   counted out, achieved with no lifting surface at all; `wedge` carries
   `liftBias 0.75` and `ApplyTopple` reads it. Nobody has tried it.
5. **Armour is a shape** — superseded by something sharper: armour is a
   *category*. See §2.5.
6. **The program may matter more than the parts — NO, not against this
   opponent.** Every candidate in this ledger carries an EMPTY program and
   runs the built-in AI, exactly as Spinner1 does, and the best of them is
   16–0. A program cannot be necessary here. Whether it would help against a
   *good* opponent is untouched.

## 6. The winners, verbatim

Two of them: the one with the perfect record, and the one that shows how
little it actually takes.

### `bulwark_v1` — the 16–0

`bulwark_v1` · **16 W / 0 L / 0 D** over two independent 8-bout sweeps
(seeds `101/202/303/404` and `17/555/909/1234`) · 1314 kg · 31 parts · FEATHER
· 0% identical placement with Spinner1 · **program: EMPTY** (AI-driven, same
as the champion).

```
#fmt4-gusset
beamlong|-0.300,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
beamlong|-0.100,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
beamlong|0.100,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
beamlong|0.300,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
wheel|-0.470,0.180,-0.350|0|-1.00,0.00,0.00|Rubber
wheel|-0.470,0.180,0.000|0|-1.00,0.00,0.00|Rubber
wheel|-0.470,0.180,0.350|0|-1.00,0.00,0.00|Rubber
wheel|0.470,0.180,-0.350|0|1.00,0.00,0.00|Rubber
wheel|0.470,0.180,0.000|0|1.00,0.00,0.00|Rubber
wheel|0.470,0.180,0.350|0|1.00,0.00,0.00|Rubber
core|0.000,0.230,-0.650|0|0.00,0.00,0.00|CarbonFiber|G:16
spindle|0.000,0.430,0.450|0|0.00,1.00,0.00|CarbonFiber|G:8
beam|0.000,0.680,0.450|0|0.00,0.00,0.00|Titanium|G:8
beam|0.000,0.680,1.050|0|0.00,0.00,0.00|Titanium|G:32
beam|0.000,0.680,-0.150|0|0.00,0.00,0.00|Titanium|G:16
blade|0.000,0.680,1.410|0|0.00,0.00,0.00|Titanium|G:32
blade|0.000,0.680,-0.510|0|0.00,0.00,0.00|Titanium|G:16
blade|0.000,0.805,1.200|0|0.00,0.00,0.00|Titanium|G:8
blade|0.000,0.805,-0.300|0|0.00,0.00,0.00|Titanium|G:8
engine|-0.190,0.405,-0.270|0|0.00,0.00,0.00|Aluminum|G:8
engine|0.190,0.405,-0.270|0|0.00,0.00,0.00|Aluminum|G:8
battery|-0.240,0.405,0.060|0|0.00,0.00,0.00|Aluminum|G:8
battery|0.000,0.405,0.060|0|0.00,0.00,0.00|Aluminum|G:8
battery|0.240,0.405,0.060|0|0.00,0.00,0.00|Aluminum|G:8
blade|-0.425,0.515,-0.270|90|0.00,0.00,0.00|Titanium
blade|-0.425,0.515,0.300|90|0.00,0.00,0.00|Titanium
blade|0.425,0.515,-0.270|90|0.00,0.00,0.00|Titanium
blade|0.425,0.515,0.300|90|0.00,0.00,0.00|Titanium
blade|0.000,0.130,0.560|0|0.00,0.00,0.00|Titanium
blade|0.000,0.180,0.560|0|0.00,0.00,0.00|Titanium
blade|0.000,0.230,0.560|0|0.00,0.00,0.00|Titanium
```

How it is put together, in four sentences. A **CarbonFiber raft** of four
`beamlong` rails carries six wheels on a 0.94 m track; the rails mate
side-by-side through **five sockets**, which is 8250 N·s — double the 4200
stress ceiling, so the frame cannot be broken apart at all. The core sits
**low and at the back**, bolted to the rail ends and gusseted. A
**CarbonFiber `spindle`** stands on the deck at the front carrying an
**all-Titanium** two-armed rotor — `beam` hub, two `beam` arms, four
Titanium `blade`s — every joint of which is gusseted, so the arm seams are
8400 and the actuator seams 19,800. Two engines and three batteries sit on the
deck with their tops at 0.53 m, which is **0.05 m clear of the rotor's
underside** — the one dimension in the whole build that has to be exact,
because anything that *touches* the limb is welded into it.

### The same machine with nine tenths of the fat cut off — `abl_min`

**15 W / 1 L / 0 D** over two seed sets · **855 kg · 15 parts** — Spinner1's
own weight and piece count — · 0% identical placement. Started life as an
ablation and ended up the more instructive robot: it is the minimum that
works, so every line in it is load-bearing. Its one loss was a flip it could
not recover from; if you want the perfect sheet, that is what `bulwark_v1`'s
extra 460 kg and its wider six-wheel track are for.

```
#fmt4-gusset
beamlong|-0.100,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
beamlong|0.100,0.180,0.000|0|0.00,0.00,0.00|CarbonFiber
wheel|-0.270,0.180,-0.350|0|-1.00,0.00,0.00|Rubber
wheel|-0.270,0.180,0.350|0|-1.00,0.00,0.00|Rubber
wheel|0.270,0.180,-0.350|0|1.00,0.00,0.00|Rubber
wheel|0.270,0.180,0.350|0|1.00,0.00,0.00|Rubber
core|0.000,0.230,-0.650|0|0.00,0.00,0.00|CarbonFiber|G:16
spindle|0.000,0.430,0.450|0|0.00,1.00,0.00|CarbonFiber|G:8
beam|0.000,0.680,0.450|0|0.00,0.00,0.00|Titanium|G:8
beam|0.000,0.680,1.050|0|0.00,0.00,0.00|Titanium|G:32
beam|0.000,0.680,-0.150|0|0.00,0.00,0.00|Titanium|G:16
blade|0.000,0.680,1.410|0|0.00,0.00,0.00|Titanium|G:32
blade|0.000,0.680,-0.510|0|0.00,0.00,0.00|Titanium|G:16
engine|0.000,0.405,-0.270|0|0.00,0.00,0.00|Aluminum|G:8
battery|0.000,0.405,0.060|0|0.00,0.00,0.00|Aluminum|G:8
```

Read it as the recipe: **two rails, four wheels, a core on the back of them, a
spindle on the deck at the front, three Titanium beams and two Titanium blades
past it, one engine, one battery, and a weld on every joint that carries
load.** Everything else in `bulwark_v1` is refinement.

⚠ Before you edit either build string, re-read **"The one thing that will
silently break your build"** in §4.5. Moving a power cell up by 2 cm welds it
into the rotor, and nothing anywhere will tell you.

## 7. Session record — 2026-08-18

Nine measured sweeps, 72 bouts, all local (`ChallengeBench` in the editor over
the Unity MCP bridge). No production contact, no ladder writes, no product code
touched — `ChallengeBench.cs` was read but not modified.

**Owner state (hard rule 5), fingerprinted either side of the work:**

| | md5 | mtime | size |
|---|---|---|---|
| before | `b12cdfdc36cf1eb88ccc6c9fb98dd55e` | Aug 13 22:13:18 2026 | 5820 |
| after | `b12cdfdc36cf1eb88ccc6c9fb98dd55e` | Aug 13 22:13:18 2026 | 5820 |

Byte-identical, and the mtime never moved — the save was never written.

**Predictions scored: 3 of 6 right.** `bulwark_v1` (right), `abl_min` (right),
`abl_noengine` (right on the record, wrong on the mechanism — I predicted
longer bouts and got shorter ones), `abl_norotor` (**wrong**, 5 wins against a
predicted 1–3), `abl_alurotor` (**wrong**, 8 wins against a predicted 2–5).
Both misses were in the same direction: **I over-estimated how much of the
result came from the parts of the design I had reasoned hardest about.**
That is worth more than the 16–0.

The generator that produced these builds — it checks flushness, socket counts,
seam thresholds, overlaps and orphans offline, before a sweep is spent — is
not committed; it lived in a scratch directory. If this work resumes, rebuild
it first. Getting a build *legal* is fiddly and costs sweeps otherwise.
