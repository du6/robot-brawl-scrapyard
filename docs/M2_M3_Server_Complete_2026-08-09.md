# M2's server half is done, and M3 has started — 2026-08-09

`api_smoke` **74 → 124**, `sql_bench` 45/45, server log 0 errors, career save
byte-identical throughout.

Four commits in sequence: the read side, DEPOSIT TO CAREER, ledger
conservation — on top of Glicko-2 and defender protection earlier in the day.

## What M2 asked for, and where it stands

| M2 bullet | state |
|---|---|
| Glicko-2 service + unit tests (deterministic vectors) | ✅ `glicko_check` runs Glickman's published example |
| category assignment · cross-category offset | ✅ |
| defense floor · repeat taper · tickets | ✅ |
| `POST /challenges` with escrow transaction | ✅ |
| fight inbox endpoints | ✅ `GET /v1/inbox` |
| leaderboards per category | ✅ `GET /v1/leaderboard/{category?}` |
| scouting card endpoint | ✅ (existed; now covered anonymously) |
| **Client ARENA tab** | ❌ **the only M2 item left, and it is Unity work** |

### M2's acceptance, clause by clause

> *scripted two-account run: enlist ×2 → challenge → rating and ledger deltas
> match hand-computed Glicko-2 → defender floor and taper demonstrably engage
> under a challenge loop → punch-up pays the (1+0.5g)² purse → a third
> account can scout both and watch the replay but cannot fetch either program
> payload*

- **two accounts, enlist, challenge** — section K does exactly this.
- **Glicko-2 matches hand-computed** — better than hand-computed: checked
  against the *published* worked example, which is a fixture neither I nor a
  future session can quietly re-derive to match a bug.
- **floor and taper demonstrably engage** — section L, and the floor asserts
  the drop is *exactly* the remaining allowance.
- **punch-up pays (1+0.5g)²** — section K, at gap 4: purse 900, confirmed
  independently by the ledger totals.
- **third account scouts and watches but cannot fetch a payload** — tested in
  its *strongest* form, **anonymously**, against both the match view and the
  scouting card.

The one thing not literally satisfied is "scripted two-account run" as a
single script: it is the bench, which is better, because it runs on every
`run_local.sh`.

## M3, started

| M3 bullet | state |
|---|---|
| signing bonus | ✅ (M1) |
| **deposit endpoint + ledger rows** | ✅ with idempotency |
| conservative faucet rates in `LadderConfig` | ✅ (002/003/004) |
| **ledger conservation check** | ✅ nine invariants, section N |
| client deposit flow | ❌ Unity |
| scrap sinks beyond stakes (cosmetic plates/titles) | ❌ |
| 4-week season rollover job | ❌ season 1 runs to 2099 |
| league nights | ❌ |
| season history on robot cards | ❌ |

## Decisions worth carrying

**The wallet got its own rate-limit bucket.** Two deposit checks first failed
with `429` rather than `400`, because deposits shared the `upload` bucket
with snapshot uploads and challenges. Splitting the bucket is **not**
threshold-loosening — the upload limit is unchanged at 20/min. Three
operations with different abuse profiles were competing for one budget, so a
player who had been challenging hard was throttled out of looking at their
own winnings.

**The deposit replay is rejected, not silently re-accepted.** §8/M3 says
"rejected", so it is a 409 — but the original row comes back with it, so an
honest client that merely lost its response can reconcile instead of
guessing. Both halves of the rule are enforced by Postgres
(`ledger_deposit_is_withdrawal`, unique `ledger_idem_key`) rather than by the
endpoint, so no future code path can turn the valve into a faucet.

**The leaderboard reports `provisional`.** A 1400 at RD 350 has not earned
the same claim as a 1400 at RD 60. A board that hides deviation lies about
its own confidence.

**The inbox states the outcome from the caller's side.** WON/LOST/DRAW rather
than a raw verdict enum — making every client re-derive "was I the
challenger" is how two clients end up disagreeing about who won.

## Still open, in the order I would take them

1. **The §2.1/§2.2 tension** — at placement deviation one loss is worth 162
   points against a 75/day floor. Needs owen's call; see
   `Defender_Protection_Shipped_2026-08-09.md`.
2. **Client ARENA tab** — the last M2 item, and the first thing that makes
   any of this visible to a player.
3. **Season rollover + league nights** — the rest of M3.
4. **Docker / GCP** — still never built, still no Secret Manager entries.
5. **The end-to-end runs are hand-run, not benches.** Nothing catches a
   regression in the worker↔API path tomorrow.
