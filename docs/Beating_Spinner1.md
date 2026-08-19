# Beating Spinner1 — the design log

**Started 2026-08-18.** A standing brief for whoever (human or agent) is trying
to build a FEATHER machine that beats owen's champion. **This document is the
memory of that effort**: read it before designing anything, and add to it after
every measured run. A candidate that failed for a reason nobody wrote down will
be built again.

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
**700 kg more machine than the champion** — nearly double. That is the single
biggest fact on this page and no candidate has spent it yet.

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
