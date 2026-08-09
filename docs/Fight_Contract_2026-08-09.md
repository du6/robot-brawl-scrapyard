# The FIGHT half of the server contract — 2026-08-09

**`HANDOVER_TO_CLI.md` §5.2.** Before this there was no match-create, no
match-result and no replay upload; §5.3 of the design doc specified the
lifecycle in prose and none of it was code.

`sql_bench` **34 → 45**. `api_smoke` **48 → 74**. 0 failures, 0 skips,
server log 0 errors.

## What exists now

| endpoint | §5.3 step | auth |
|---|---|---|
| `POST /v1/challenges` | 1 — checks, escrow debit, match row, job | player JWT |
| `POST /v1/worker/matches/{id}/replay` | 2 — replay upload | worker key |
| `POST /v1/worker/jobs/{id}/fight-result` | 3 — verify, settle, complete | worker key |

Plus migration `002_ladder_config.sql`.

## The three things §5.2 asked for

**Escrow debit and the match row in one transaction.** The match row, the
`STAKE` ledger row and the FIGHT job are a single transaction. A stake
debited without a match is money destroyed; a match without a stake is money
created. The bench does not take the endpoint's word for it — it reads the
wallet before and after and asserts the difference.

**Worker signature and job ownership verified.** `SELECT match_id FROM
match_jobs WHERE id = $1 AND claimed_by = $2 AND status = 'CLAIMED' AND kind
= 'FIGHT' FOR UPDATE`. The `FOR UPDATE` holds the row for the rest of the
transaction so two workers cannot settle the same match twice, and the
worker must be reporting on the match its job actually holds. A second
settlement attempt returns 409 **and the bench asserts the wallet did not
move** — a 409 with a side effect would still have minted scrap.

**Ledger append-only, balance is `SUM(delta)`.** No cached balance column
exists to disagree with the ledger. Affordability is checked by summing it.

## The economy, and why a table appeared

§2.3 is explicit: *"All constants live in one server-side table
(`LadderConfig` …) and are tunable without a client update."* It did not
exist, and the alternative was four magic numbers scattered through
`Program.cs`. So `002_ladder_config.sql` adds it, seeded with §2.3's values.

The split I chose: **formulas in code, constants in the table.** A formula
is a rule — *"punching up two categories pays 4× base"* — and belongs where
it can be read next to the code applying it. A constant is a dial and
belongs where owen can turn it without a deploy. The config is read per
request rather than cached at boot, because a cache would quietly reinstate
the deploy requirement that the table exists to remove.

Settlement, and the sums conserve scrap because the stake already left the
wallet in step 1:

| verdict | ledger rows |
|---|---|
| CHALLENGER | `STAKE_REFUND` +stake, `PURSE` +`100×(1+0.5×gap)²` |
| DEFENDER | `DEFENSE` +40 to the defender; the stake is forfeit |
| DRAW | `STAKE_REFUND` +stake |

## The check that nearly did not earn its keep

The first version of section K passed all 18 checks at **gap 0** — where
`50×(1+gap)` and `100×(1+0.5×gap)²` both collapse to their base. A wrong
formula would have been indistinguishable from a right one. House rule: *a
swept variable producing no variance has usually not been swept.*

So the section now fights the heaviest class there is. A FEATHER challenging
a SUPER is **gap 4**: stake `50×5 = 250`, purse `100×(1+2)² = 900`. Both
asserted against values the bench computes itself from the category names,
not against what the API returned.

The ledger totals across a whole run confirm it independently:

```
STAKE:        -350      (50 at gap 0 + 250 at gap 4 + 50 forfeit)
STAKE_REFUND:  300      (both wins refunded; the loss was not)
PURSE:        1000      (100 at gap 0 + 900 at gap 4)
DEFENSE:        40
```

`PURSE = 1000` is the squared multiplier proven arithmetically end to end.

Also newly covered, because every settlement had gone to the challenger:
the **DEFENDER** branch — the flat 40 defense purse paid, and the
challenger's stake forfeit rather than refunded.

## `fail_job.sql` had no cover — a gap I created

`sql_bench` is documented as the **only** cover for
`RobotBrawl.Api/Sql/*.sql`. `fail_job.sql` shipped earlier today with the
livelock fix and was never added to it. Section I closes that: a foreign
worker cannot fail someone else's job, the holder can, the job lands
`FAILED` with the claim cleared and a `last_error`, and — the point of the
whole exercise — **no worker can claim it again.**

## What is deliberately NOT here

All of this is M2 or later, and none of it was in §5.2's requirement list:

- **Glicko-2 rating deltas (§2.1).** `matches.rating_deltas` stays NULL and
  the `ratings` table is untouched. This is the single biggest omission —
  fights settle money but do not move anyone on the ladder.
- **Defender protection and taper (§2.2).** §5.3 step 1 lists "taper not
  exhausted" as a check; it is not implemented.
- **Tickets.** The table exists, `POST /v1/challenges` does not consult it,
  so challenge frequency is currently unbounded.
- **`first_blood_bonus`** is seeded in config but never applied — it needs
  match history to know "never beaten before".
- **Per-job nonce (§5.5).** Worker auth is the shared key plus job
  ownership. §5.5 asks for "worker key + per-job nonce"; the nonce does not
  exist.

## And the thing that still blocks M1

**No worker implements FIGHT.** `ValidateWorkerLoop` handles VALIDATE only;
there is no fight-job loop, so nothing claims a FIGHT job and runs
`MatchRunner` over the two payloads. In the bench, the "worker" is
`api_smoke` itself posting a verdict it made up.

So the server contract is real and proven, but M1's accept clause — *upload
two robots, trigger a match, watch the replay in-client from the cloud URL*
— is still not reachable. The next step is the client-side fight loop, and
after it, `MatchRunner` producing a real replay rather than a string the
bench invented.
