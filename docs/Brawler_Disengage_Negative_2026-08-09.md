# The disengage works, and is still the wrong trade — 2026-08-09

Tests the rule `Matador_Ram_Retraction_2026-08-09.md` arrived at:

> **A closing ram is dangerous when nothing disengages after the bite.**

and the option that rule opened, which the evening handover made its
number-one next step:

> **Give Brawler a disengage and put its speed back** — Brawler can carry
> a higher closing throttle if it breaks contact after each bite, which
> would recover the closing authority 45% gave up.

**Result: the rule is confirmed. The option is dead. Nothing shipped.**

## The experiment

`qa_opening_run5_disengage_15s.txt`. Five arms, N=12, interleaved, 15 s
cutoff — with **both shipped-Brawler controls in the same batch**, so the
effect is measured against numbers produced under identical conditions
rather than against a previous run.

The disengage is Matador's **TOO HOT** transplanted verbatim —
`EnemyRange < 3.0` and `HpFrac > 0.35` and `HitRecently > 0.5` → reverse
at 90% for 0.8 s, turn 30% for 0.4 s — inserted at index 0 so it outranks
IN RANGE, exactly as it does in Matador.

| arm | disarmed | flipped | dealt@15s |
|---|---|---|---|
| **45% hold (SHIPPED)** | **0%** | 8% | **405** |
| 100% hold (the old bug) | **25%** | 17% | 182 |
| **100% break** | **0%** | 8% | 221 |
| 70% break | 0% | 0% | 209 |
| 45% break | 0% | 8% | 227 |

## What it says

**The rule is right.** Holding the throttle at the old 100% and adding
nothing but a disengage takes the disarm rate from 25% to **0%**. The
sustained press is the mechanism; the throttle was only ever a way of
weakening it. Three presets now agree — Matador (disengages, never
shears, at 100% for 1.5 s), Brawler-as-shipped (no disengage, needed 45%),
and Brawler-with-disengage (no shear at 100%).

**The option it opened is dead anyway.** The entire point was to recover
damage — closing authority is what the ladder's dead-air regression is
made of. Every disengage arm lands at **209–227**, while the shipped
45%-hold arm deals **405**. Adding a disengage costs Brawler roughly
**half its damage**, because it keeps leaving.

There is no version of "put the speed back" here. At 100% with a
disengage, Brawler deals 221. At 45% with no disengage, it deals 405.
**The preset that shipped this morning is the best arm on the bench**, and
it wins on the axis the change was supposed to improve.

**So: no change. The evening handover's item 1 is closed by measurement,
and the dead-air regression stays an accepted cost with this avenue ruled
out** — the disengage does not restore pressure, it reduces it further.

## A calibration worth keeping

The `100% hold` arm scored **25%** here against **42% and 42%** in runs 1
and 2. Same program, same cell; the earlier runs used a 7 s cutoff and
this one 15 s, but a sheared weapon does not grow back, so the difference
is **run-to-run noise at N=12**.

That matters for how these tables are read. The *effect* is not in doubt —
42 / 42 / 25 against 0 / 0 / 0 / 0 / 0 is unambiguous at any of those
numbers. But **the point estimate wanders by something like ±17 points**,
and the 42/42 agreement across the first two runs was luckier than it
looked. Do not quote a single OpeningBench rate to two significant
figures, and do not compare two arms that differ by less than about 20
points unless they were in the same batch.

This is exactly why every run since has carried its controls **inside**
the batch instead of across runs.

## Method notes

**A test that can fail is worth running even when you expect to win.**
The prediction written before this run was "0% disarmed at damage at or
above the shipped arm". Half of it landed — 0% disarmed — and half of it
failed on the number that actually mattered. Had the prediction been only
"the disengage cures the disarm", the run would have read as a success and
something would have shipped that halves the preset's damage.

**Confirming a rule is not the same as the rule being useful.** The
disengage rule is now well supported across three presets and is worth
carrying into any future preset design. It just does not pay for itself
on this preset, against this opponent, at this league. Both halves of that
sentence are findings; only the first one feels like one.

**Negative results close options, which is the point.** Two sessions of
"the closing exchange" work have now ruled out approach geometry, spawn
offset, and the disengage, and confirmed that the ladder's mutual disarm
is a separate mechanism. What is left is genuinely unexplored, which is a
better place to start than a list of plausible ideas.

## Artifacts

`Assets/Phase1/qa_opening_run5_disengage_15s.txt`.
Career save `18614d0e6603869f28fb65992b7d1484` verified unchanged. Editor
left out of play mode. `OpeningBench.DISENGAGE` retained in the bench so
the arm can be re-run, defaulted to the arms used here.
