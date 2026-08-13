# Hardening the flagships: 92% → 50%, in two measured steps (2026-08-12)

Owen's ask: "make higher level league robots more sophisticated and harder to
beat." The instrument: CareerBench at SampleMul=3 (~500 fights per run,
timeScale 10). Baseline: `docs/CareerBench_Sample_Size_2026-08-10.md` —
**CEILING L4 at 22/24 = 92%**, a robot two classes below beating the L4
flagship nearly always.

## What was deliberately NOT touched

The AI knobs. The tuning history has TWO documented reversals ("faster =
harder" measured backwards twice, `DecisionInterval` flattened for cause),
which is owen's stated low confidence, vindicated in writing. Everything here
changes what the flagships ARE, not how they think.

## The three runs

| band | baseline | +gussets (both flagships) | +lance, L5 reverted |
|---|---|---|---|
| **CEILING L4** (target) | 22/24 = 92% | 19/24 = 79% | **12/24 = 50%** |
| CEILING L5 | 1/24 = 4% | 6/24 = **25% — WORSE** | 2/24 = 8% ✓ reverted |
| **FLOOR L4** (the needle) | pass | 15/18 | **11/18 = 61% — AT the line** |
| STRETCH L3 | 17% | 44% | 56% — swings with NO change |
| STRETCH L4 | 11% | 6% | 0% — consistently dead (pre-existing) |
| FLOOR L1/L2/L5 | mixed | mixed | ±1-2 fights across runs |
| BLOB | pass | pass | pass |

## What shipped

1. **`Contest.hardened` on L4C4 only** — BASTION spawns with every part
   except core and wheels gusseted (the measured ×1.5 seam lever). One
   surgical flag; STRETCH's cheapest contests untouched (they already fail
   LOW — hardening them would deepen a different failure).
2. **BASTION's passive spike is now a POWERED LANCE** (ram + tungsten
   spike). Measured first: gussets alone moved 92→79 because the two-below
   chip build wins on the DAMAGE criterion, which seam strength cannot touch
   — BASTION's "still standing is enough" premise loses to the verdict
   cascade when its weapon never lands.
3. **L5C1 hardening MEASURED AND REVERTED**: +~100 kg of gussets made the
   hunter WORSE (4% → 25% → back to 8%). A controlled reversal, recorded in
   the contest table.
4. **polyEdges crash guard** — take one died on its first LoadSnapshot:
   `RefreshOverlay` indexed a 4-entry cosmetic overlay list that was short,
   killing the bench coroutine. The overlay now declines to draw and NAMES
   the state instead of taking LoadSnapshot down. Root cause of the short
   list is UNFOUND (one occurrence; did not reproduce in takes two/three).

## Why this is the stopping point

FLOOR L4 is at **61% against a ≥60% bar** — a same-class robot barely still
clears the hardened flagship. Any further BASTION buff breaks the peer
experience to chase the last 50 points of CEILING. The remaining gap is not
reachable with toughness or damage; it is one of:
- **AI behaviour** (the landmine field — needs its own measured campaign),
- **the verdict rules** (should HP-remaining count at timeout? owner call —
  it changes game feel everywhere), or
- **the band's bar** (is "exactly 0 wins for a disc archetype vs a tank
  flagship" achievable at all? — a check-vs-product question, owner's).

## Also learned, for free

- **The noise band at 3x is ±2-3 fights (~10-15%)**: STRETCH L3 went
  17%→44%→56% and FLOOR L1 flipped twice with NO change to those contests.
  Verdicts moving less than ~15 points are not evidence.
- `AuditPowerMounting` reports **`ripper.engine hangs on 1 seam`** — surfaced
  during this work, almost certainly pre-existing (nothing today moved
  ripper's geometry); no green bench asserts the audit. Filed here.
- STRETCH L4 = 0/18 across all three runs: a clever one-below build NEVER
  beats L4C1 (widowmaker-Veteran). Consistent, pre-existing, and the
  opposite problem to the CEILING — owed its own look.
