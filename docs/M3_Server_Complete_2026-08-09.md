# M3's server half is done — 2026-08-09

`api_smoke` **124 → 158**, `sql_bench` 45/45, server log 0 errors, career save
byte-identical throughout.

| M3 bullet | state |
|---|---|
| wallet + signing bonus | ✅ |
| **DEPOSIT TO CAREER** end to end, idempotent | ✅ |
| conservative faucet rates in `LadderConfig` | ✅ |
| **scrap sinks beyond stakes** (plates/titles v1) | ✅ |
| **4-week season rollover** (compression, payouts) | ✅ |
| **league nights** | ✅ |
| ledger conservation check | ✅ 9 invariants |
| season history on robot cards | ❌ client |
| season **badges** | ❌ no schema; see below |
| client deposit + shop flow | ❌ Unity |

## The three features

**League nights** — `POST /v1/admin/league-night/{category}`. Every pair once,
`n(n-1)/2` matches; 28 for the field of 8 the acceptance names. Three
challenge rules deliberately do not apply: **no stake** (nobody chose the
fight), **no tickets** (§2.2 caps what a robot *initiates*), **no purse**
(§2.3 keeps faucets conservative; rating movement is the content). Everything
about rating does apply.

**Season rollover** — `POST /v1/admin/season/rollover`, one transaction.
Measured on real ratings: **1574 → 1387**, halfway to the 1200 placement;
deviation reset to 350 so the season is genuinely open; wallets untouched
apart from payouts. Closing the same season twice is refused.

**Cosmetics** — the first real sink. Catalogue is public, purchases are
`COSMETIC` debits, titles show on the leaderboard because a sink nobody can
see is a sink nobody spends into.

## Three bugs, each caught by something other than the feature under test

**Settlement recomputed the stake.** It read `ladder_config` at settle time
rather than what the challenge charged. Latent on its own — tuning
`stake_base` mid-match would refund a different number than was debited — and
it made league nights *impossible*, because a recomputed stake credits a
refund against a debit that never happened. `matches.stake` now records what
was actually charged (migration 006).

**A zero-value refund, and the worse bug hiding behind it.** The first league
night settlement violated `ledger_delta_nonzero` — correctly; a zero row is
noise in an audit trail. But the resulting rollback *concealed* that the
**purse would still have paid out** on a fight nobody staked: a faucet
attached to content the server generates on a timer. Two green checks can
hide each other. Settlement is now guarded on an actual escrow.

**The season payout key collided.** `season:{n}:{robot}` — but §2.1 rates per
robot **per category**, so a featherweight that punched up is ranked in both,
and the unique index refused the duplicate the first time it happened. The
key now includes the category.

## Two checks of mine that were wrong

House rule 2 says ask whether the CHECK is wrong first. Twice it was:

- *"the same rollover cannot run twice"* called the endpoint twice — but the
  second call advances 2→3, which is legitimate. The real test points the
  config back at the season just closed, which is what a retried cron job
  does.
- *"one you cannot afford is refused"* bought the most expensive item and
  expected a 400 — then season payouts landed, the wallet hit 1400, and the
  purchase legitimately succeeded. **A check that depends on the tester being
  poor breaks the moment the game pays out.** It now prices a temporary item
  just above the actual balance.

## Decisions that are mine, not the doc's

**Season payout figures.** §2.3 says only *"by final rating rank per
category"* with no numbers. The scheme is `base/rank` over the top three —
300 / 150 / 100 — because §2.3 says faucets start conservative and inflating
career progression is a one-way door. All four values are `ladder_config`
dials.

**Cosmetic prices.** 60–400 against a 500 signing bonus, so the sink is
reachable. A sink nobody can afford does not exist in practice.

**No season badges.** §2.4 names them and there is no schema for one.
Inventing a table seemed worse than saying it is not done.

## What remains, in order

1. **M4 hardening** — input fuzzing on the upload path, abuse limits.
2. **Docker / GCP** — still never built, still no Secret Manager entries.
   This is the largest untouched risk in the project.
3. **Client** — ARENA styling, deposit and shop flows, season history.
4. **The end-to-end runs are hand-run, not benches.** Nothing catches a
   regression in the worker↔API path tomorrow.
