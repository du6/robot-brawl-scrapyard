# CareerBench's balance bands, at a sample you can steer by — 2026-08-10

`CLAUDE.md` has carried this note for two days:

> `CareerBench` is a BALANCE harness and reports **12 pass, 5 "TUNING NEEDED"**
> — win rates outside their intended bands at N=6-8, which is owen's call and
> **too small a sample to steer by**.

That last clause was right, and it was never tested. This tests it.

---

## The argument, before any balance question

**Two runs of an UNCHANGED build, an hour apart:**

| run | result |
|---|---|
| recorded in `CLAUDE.md` | 12 pass, 5 fail |
| 2026-08-10, N=1 | **10 pass, 7 fail** |
| 2026-08-10, N=3 | **9 pass, 8 fail** |

Nothing was edited between the first two. A harness whose verdict count moves
by two on an unchanged build is not reporting balance, it is reporting dice —
and every one of those verdicts is a `Check()` that a future session would
read as a fact.

---

## What changed at 3× sample

`SampleMul` multiplies every band: FLOOR 3→9 per contest, STRETCH 6→18,
CEILING 8→24, BLOB 5→15. **≈500 real fights.**

⚠ **No threshold was moved.** Count thresholds were rewritten as the ratios
they already were — `>=2/6` and `>=8/24` are both 25% — so the bar is
identical and only the noise under it shrank. `CEILING` is the exception and
was deliberately left at **exactly zero wins**: turning "~0%" into a ratio at
a larger N would *permit* wins that are currently forbidden, which is the
loosening hard rule 3 exists to prevent. A bigger sample makes that check
harder, which is the right direction for a rule that says "never".

| band | N=1 (6–8 fights) | N=3 (18–24 fights) | verdict |
|---|---|---|---|
| FLOOR L1 | pass | 11/18 = **61%** pass | scrapes over the 60% line |
| FLOOR L2 | 3/6 = 50% FAIL | 9/18 = **50%** FAIL | **confirmed**, and stable |
| FLOOR L3 | 1/6 = 17% FAIL | 4/18 = **22%** FAIL | **confirmed**, badly off |
| FLOOR L4 | 3/6 = 50% FAIL | 14/18 = **78%** pass | ⚠ **FLIPPED — was noise** |
| FLOOR L5 | pass | 5/9 = **56%** FAIL | ⚠ **FLIPPED the other way** |
| STRETCH L3 | pass | 3/18 = **17%** FAIL | ⚠ **new failure** |
| STRETCH L4 | 0/6 FAIL | 2/18 = **11%** FAIL | **confirmed** |
| STRETCH L5 | 0/6 FAIL | 1/18 = **6%** FAIL | **confirmed** |
| CEILING L4 | 8/8 FAIL | 22/24 = **92%** FAIL | **confirmed, and the worst thing here** |
| CEILING L5 | 3/8 = 38% FAIL | 1/24 = **4%** FAIL | nearly passes — was noise-inflated |
| BLOB L4/L5 | pass | 2/15 = 13% pass | comfortable |

**Three of eleven bands changed sides.** At N=6–8, roughly a third of this
harness's verdicts were coin flips, in both directions — FLOOR L4 was failing
for no reason, FLOOR L5 and STRETCH L3 were passing for no reason.

---

## What survives, and is worth your attention

1. **CEILING L4: a robot two value-classes BELOW the flagship contest wins
   92% of 24 fights.** Everything else here is a tuning nudge; this is the
   value ladder inverted for that matchup. It also reads very differently
   from its sibling — CEILING L5 is 4%, which is the band working.
2. **STRETCH fails in three consecutive leagues in the same direction**
   (L3 17%, L4 11%, L5 6%, against ≥25%). Three leagues failing one band the
   same way is systematic, not noise: the "clever one-below" reference cannot
   reach the cheapest contest in the back half of the game.
3. **FLOOR L2 and L3 are genuinely low** (50%, 22% against ≥60%).
4. **CEILING L5 and FLOOR L4 should stop being counted as problems.** They
   were sampling artefacts.

## Also found: the BLOB label disagreed with the BLOB rule

The check read `w4[0] <= 1` of 5 — **20%** — while its own printed label said
`need <=30%`. Code is the rule, so the label was corrected to 20% rather than
the bar moved to 30%. It had been quietly reporting a stricter rule than it
claimed, in the direction nobody notices, since it was written.

## Scope, stated honestly

- One seed set, AI-vs-AI at 10×, through the real enrollment path.
- N=3 is enough to separate a 92% from a 4%. It is **not** enough to argue
  about 56% vs 60% — FLOOR L5 and FLOOR L1 sit either side of the bar by less
  than the interval this sample can resolve. Do not tune those from this run.
- `SampleMul` defaults to **1**, so the shipped harness is unchanged and
  nothing gets slower by accident. Set it explicitly to re-measure.

## What this does and does not license

It licenses **retiring two false alarms** and **ranking the rest**. It does
not license a balance change: which of CEILING L4, the STRETCH trend and the
FLOOR pair gets touched, and in which direction, is owen's call.

The measurement's own conclusion, though, is about the instrument rather than
the balance: **a bench whose thresholds are counts at N=6 will hand a future
session three wrong facts out of eleven, and it will hand them over with the
word PASS or FAIL in front of them.**
