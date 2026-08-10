# The ladder's 43% mutual disarm — root cause found, 2026-08-09

**The weapon is not the fragile thing. The limb carrying it is.**

Two fixes were aimed at this and both moved it by **zero bouts**. This says
why: `WEAPON_VS_WEAPON` scales HP damage, and **69% of weapons are lost
without being beaten on HP at all** — they leave with a mean of **78% of
their hit points still in them**, because the structure holding them failed.

No value of `WEAPON_VS_WEAPON` can reach those losses. Lowering it further
will not help.

---

## The measurement

`DisarmBench` — new, and the first thing here to measure the disarm **event**
rather than the fight outcome, which is the standing instruction in the
handover and is what took the opening disarm from 42% to 0%.

Same 15 pairings × 2 seeds and the same one-chassis fixture as
`LadderSweepBench`, so the numbers are directly comparable. It reproduces the
historical outcome exactly:

| | measured | previously recorded |
|---|---|---|
| both sides disarmed | **13/30 (43%)** | 13/30 |
| either side disarmed | **26/30 (87%)** | 26/30 |

**How weapons actually leave a body — 39 departures over 30 bouts:**

| route | count | |
|---|---|---|
| `STRUCTURAL-SHED` | **18** | 46% |
| `HP-DESTROYED` | 12 | 31% |
| `STRUCTURAL-WRECK` | 9 | 23% |
| **left while still having HP** | **27** | **69%, mean 78.1% hp remaining** |

The count reconciles with the outcome, which is the check that says the
instrument is sound: 13 bouts × 2 weapons + 13 bouts × 1 = **39**, exactly the
39 departures recorded.

And of the 12 that *were* beaten on HP, **100% had an edge attacker** — so
`WEAPON_VS_WEAPON` was applying to every single one of them. It was working.
It was simply irrelevant to two thirds of the problem.

Meanwhile **90 non-weapon parts were destroyed, all 90 by an edge.** Edges
chew through structure; the structure fails; the weapon it was carrying falls
off undamaged.

---

## Two predictions, written before the run

Hard rule 6. One held and one was refuted, and the refuted one is the more
useful of the two because it is what forced the second measurement.

1. **"Most weapon losses have a NON-edge attacker."** — **REFUTED.** 100% of
   HP kills came from an edge. Had the bench stopped here it would have
   concluded "0.25 was not low enough", **which is wrong**, and would have
   sent the next session to tune a constant that cannot move the number.
2. **"Most weapons are lost WITHOUT being beaten on HP."** — **HELD**, 69%.

The first bench run only counted HP deaths, and its own arithmetic exposed the
gap: 14 bouts of mutual disarm require at least 39 weapon losses, and it had
found 17. **A count that cannot reconcile with the outcome it is explaining is
not a finding, it is a missing mechanism.**

---

## What this does and does not license

**It does not license a balance change.** Balance is owen's call and gets made
from the table, not from a threshold invented by whoever ran the bench.

What the table says is that the lever has to act on **structure**, not on
weapon HP. The candidates, in the order they seem cheapest:

- the **seam** carrying a weapon-bearing limb, so an arm survives contact that
  currently sheds it
- the durability of the **beam/limb** parts specifically, the way
  `EDGE_MIN_VOL` was a floor aimed at one measured outlier rather than a
  global multiplier
- weapon-on-**structure** damage, currently untouched by any of the weapon
  rules — 90 structural parts died to edges in 30 bouts

⚠ **Do not read this as "make weapons tougher."** They already survive; that
is the finding. Nine tenths of what an edge destroys is structure.

## Scope, stated honestly

One chassis (`VerbBench.ARMED`) on both sides, so the only variable is the
program — inherited from `LadderSweepBench` and the same limitation. A build
that carries its weapon closer to the core, or on a shorter limb, may shed it
far less. **Varying the chassis is the next sweep, and it is now the obvious
one**, because this result predicts limb geometry matters more than weapon
material.

## Cost of the instrument

`DamageResolver.OnPartDestroyed` is a new hook that fires **only on
destruction** — a handful of times per bout — and defaults to a no-op, so it
changes no behaviour. `CompoundRobot.LogDetach` gained an `EDGE`/`struct`
marker so a reader can tell a weapon leaving from a beam leaving without a
part catalogue; it sits behind `detachLogOn`, which is off in a shipped build.

Career save byte-identical across 30 real fights — md5 `18614d0e` and an
**unchanged mtime**, so it was never written.
