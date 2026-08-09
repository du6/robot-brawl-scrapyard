# Matador does NOT have the ram bug — a retraction — 2026-08-09

Follows `Opening_Ram_Fix_2026-08-09.md`, which fixed `Brawler`'s closing
ram (100% → 45%, L1 floor 6–8/10 → 10/10) and asserted, in its own
"deliberately not done" section and again in the evening handover:

> **`Matador`'s STRIKE has the same bug**: `MoveRel(Enemy, 100%, 1.5 s)`
> inside 2.2 m. […] It is the obvious next single-variable change.

**That claim was never measured. It is wrong, and it is retracted.**

## What was measured

`OpeningBench` with `MATADOR = true`, sweeping STRIKE's throttle and
holding its shipped 1.5 s duration. N=12/arm, interleaved.

### Run 3 — and why it had to be thrown away

`qa_opening_run3_matador_7s.txt`. All **60 bouts byte-identical**: dealt
15, taken 12, weapon intact, upright, at every throttle from 100% to 30%.

Zero variance across a swept variable is not a result. The 7 s cutoff was
tuned to *Brawler's* timeline — Brawler makes contact at ~2 s. Matador
stalks in at 55% and had not got past its first bite by the cutoff, so
every arm was measured **before the difference could express**. The
perfect agreement was the tell.

**A bench that reports the same number for every arm is failing in the
same way `api_smoke.sh` failed on 08-09 morning when it passed on two
empty strings.** Both look like data. Neither compared anything.

### Run 4 — the real answer, at a 25 s cutoff

`qa_opening_run4_matador_25s.txt`

| STRIKE ram % | disarmed | flipped | dealt@25s |
|---|---|---|---|
| **100 (shipped)** | **0%** | **0%** | **307** |
| 70 | 0% | 8% | 295 |
| 55 | 0% | 17% | 269 |
| 45 | 0% | 8% | 283 |
| 30 | 0% | **33%** | 223 |

**Matador never disarms itself — 0 out of 60 bouts, at any throttle.** And
lowering the throttle is strictly *worse*: less damage all the way down,
and by 30% it is flipping in a third of bouts.

**The shipped 100% is the best value for Matador on every measured axis.**
No change shipped. The right move was to leave it alone, and only a
measurement could say so.

## The rule this replaces the old one with

The old rule, implied by the Brawler fix, was "a 100% closing ram shears
your own weapon mount". Matador rams at 100% for *longer* — 1.5 s versus
Brawler's 1.0 s — and never shears anything. So that rule is wrong. The
rule that fits both results:

> **A closing ram is dangerous when nothing disengages after the bite.**

`Matador` has a **TOO HOT** hat — `EnemyRange < 3.0` and `HpFrac > 0.35`
and `HitRecently > 0.5` → reverse at 90% for 0.8 s, then turn. It hits and
leaves. Its ram is a hit-and-run and the mount is never held in a
sustained weapon-on-weapon press.

`Brawler` had **no disengage at all**. Its IN RANGE hat rammed and *held*,
so the disc's contact impulse and the hull's ram impulse were summed at
the weapon mount for as long as the two robots stayed together. Cutting
its throttle worked because it cut the sustained press — not because
100% is intrinsically wrong.

This also explains the flip result at the bottom of the table. A robot
that disengages after each bite needs authority to do it; at 30% Matador
is under-driven while the Rookie opponent is still charging at full
throttle, so it gets pushed and tipped instead of driving clear. **Low
throttle is not "safe" — it is only safe for a robot whose plan is to
stay in contact.**

## What this reopens, in a good way

The evening handover recorded an accepted cost: the ladder got less lively
after Brawler's fix (dead air 41.5% → 54.0%, last hit 13.9 s → 11.4 s).
That decision stands on its evidence. But the rule above suggests a
different shape of fix that was not on the table when the decision was
taken:

> **Give Brawler a disengage and let it keep its speed**, rather than
> slowing it down so it can survive holding on.

That is Matador's design, and Matador is the preset with the healthiest
numbers in this table. It would plausibly recover the closing authority
that 45% gave up — which is exactly what the dead-air regression is made
of — without reopening the disarm. It is a preset change, not a language
change, so it is cheap to try, and `OpeningBench` plus `LadderSweepBench`
measure it in about ten minutes.

**This does not mean 45% was wrong.** It means the option space had a
third member nobody had measured, and the Matador null result is what
surfaced it.

## Method notes

**A retraction is a result.** "Matador has the same bug" was read off the
source — same verb, same range gate, bigger number — and written into two
documents as fact. It survived one session unchallenged because it sounded
obviously true. The measurement took twenty minutes and reversed it.
Source-reading generates hypotheses; it does not generate findings.

**Identical arms mean the instrument, not the product.** Run 3's sixty
identical rows would have read as "the throttle doesn't matter, Matador is
fine" to anyone who wanted that answer. It was really "the cutoff is
shorter than the phenomenon". The general form: **a swept variable that
produces no variance has usually not been swept.**

**A cutoff tuned to one subject is not a constant.** `CUTOFF = 7 s` was
correct for Brawler and silently wrong for Matador, because the two
presets have different closing speeds by design. It is a public static;
set it per subject and say which value produced the numbers.

## Artifacts

`Assets/Phase1/qa_opening_run3_matador_7s.txt` (the null run, kept —
it is the evidence for the instrument note),
`qa_opening_run4_matador_25s.txt`.

Career save `18614d0e6603869f28fb65992b7d1484` verified unchanged after
both runs. Editor left out of play mode.
