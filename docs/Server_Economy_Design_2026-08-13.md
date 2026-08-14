# Server-authoritative economy — the design, decided before it is needed (2026-08-13)

**Status: DIRECTION DECIDED by owen, 2026-08-13 ("we should prevent users
from getting free scrap"). Nothing below is built. This record exists so the
decision is made once, on paper, before TestFlight — because the migration
cost is near zero today and painful after launch.**

## Context, and the constraint that forces the design

The game is offline-first: a player can play the whole single-player career
without an account. Scrap lives solely in the local save
(`robotbrawl_career.json`) as a balance plus an append-only ledger of
`when / delta / cause` entries. The server has accounts, robots, snapshots,
matches, ratings, seasons and badges — **no scrap column anywhere**. Today a
save-editor cheats only their own single-player game plus a grind-skip into
the arena (every part is purchasable by every player and categories are mass
only, so an edited save fields nothing a legitimate player couldn't
eventually field).

Owen intends to commercialize: **scrap purchasable with real money.** That
converts "edit the JSON" from a grind-skip into minting free money, and it
runs into a constraint no client-side scheme escapes:

> **You cannot sell for money what the client is allowed to mint for free.**
> Signatures, obfuscation and ledger audits all verify with a key that ships
> inside the binary being edited. If the client is authoritative for scrap,
> paying customers subsidize editors. The only fix is that the wallet and
> the inventory move server-side and the client becomes a cache.

## The design

1. **Login/signup gates the game.** (Owen's call, same day, replacing the
   first draft's anonymous device-id accounts — **considered and rejected**
   because a device-keyed identity fights the multi-device player: the same
   person on a phone and an iPad would mint two strangers.) The existing
   ladder account system (`LadderClient` register/login) moves to boot: no
   account, no game. One account works from any device, which is exactly
   what a real-money wallet needs. The session persists on device, so after
   first login the career still plays offline — the gate is at first run
   and at commerce, not in front of every fight.
   Compliance this drags onto the critical path, named now: **account
   deletion in-app** (App Store 5.1.1(v), mandatory once accounts gate the
   app), password reset, and **Sign in with Apple** becomes mandatory the
   moment any third-party login (Google etc.) is offered beside email.

2. **Wallet and part ownership live in Postgres.** Balance, owned parts, and
   every earn/spend is a server-validated transaction. The server already
   knows the purse tables and shop prices, so it can refuse a delta that
   matches no legitimate cause. The local save keeps what it is good at —
   robot designs, programs, progress. Ownership fields in the local file
   become a read cache; **on conflict, server wins.**

3. **Fights stay offline; the SHOP goes online.** Building and fighting with
   owned parts needs no connection. Buying, selling and IAP require one.
   This preserves the offline-first feel and is the standard mobile split.

4. **IAP is server-verified, never client-claimed.** StoreKit purchase →
   client sends the signed transaction → API verifies with Apple → wallet
   credited **idempotently by transaction id**. App Store Server
   Notifications drive refunds with a clawback. "I bought it" from the
   client is never sufficient.

5. **The league-purse leak is bounded, not denied — and owen's first-win
   rule (same day) collapses it to a one-shot.** League fights run on the
   device, so "I won L3C2, pay me" is inherently a client claim — the
   server cannot referee an offline fight. **The rule: a contest pays on
   the FIRST win only.** Re-entering an already-won contest charges no
   entry fee and pays nothing — purse, first-win bonus, damage bonus, all
   of it — it is a practice bout, and the row should say so. Server
   enforcement is then a uniqueness constraint, not a heuristic:
   `ledger.ref_id = "purse:<contest_id>"` per account, so a duplicate claim
   is a no-op by construction and **total league scrap per account has a
   hard ceiling — the sum of the purse table** — independent of grinding
   or cheating rate. A save-editor can at worst claim wins they didn't
   earn, once each, capped at what one honest completionist earns anyway.
   Rate caps are no longer load-bearing for purses; they remain only if a
   repeatable league reward is ever added. Arena payouts have no leak at
   all — the cloud worker referees those fights, so they are server-truth
   natively. (This subsumes the earlier "grant claim" sketch for arena
   rewards: an arena payout is just a server ledger entry like any other.)

   **The ceiling, measured against `Career.cs` as of `5181573`** (14
   contests, purse sum 125+150+…+1500 = **7,125**): worst case per account
   = 7,125 × 1.6 underdog cap + 14 × 100 damage-bonus cap + 14 × 75
   first-win bonus = **13,850 scrap, ever, per account** — roughly two
   honest completionist runs' worth. That is the whole exposure.

   ⚠ **One repeatable payment survives the first-win rule and must not:
   loss consolation.** `LossPay()` pays 40–150 per LOSS (`LOSS_BASE` +
   damage, capped), gated by nothing — under a server wallet, "I lost, pay
   me 150" would be an infinitely repeatable claim and the ceiling above
   would be false. The rule needs a companion: consolation claims are
   accepted only for contests not yet first-won, and at most N per contest
   (N small — it exists to soften early failure, not to be an income). The
   exact N is owen's (register #6); without SOME bound the whole
   first-win construction leaks through the loss path.
   ⚠ Player-facing consequence to ship with it: today's game CHARGES a
   re-entry fee (the "✓ … 50 scrap (re-entry)" rows) and pays again.
   Both sides of that change — free entry, zero payout — land together or
   the economy is asymmetric; the LEAGUE row copy changes from
   "(re-entry)" to a practice label; `CareerSmoke`'s contest-row checks
   will need the same update when this is implemented.

## Sketches — superseded by AS BUILT, same day

The first draft sketched a `wallets` table before reading the server: the
ladder ALREADY had the right spine (001's `ledger` — balance = SUM(delta),
append-only via triggers, `idem_key` UNIQUE enforced by Postgres, and a
one-way DEPOSIT_TO_CAREER valve). **"The server has no scrap column
anywhere" in the context section above was WRONG** — the wallet existed;
what was missing was the career's paths into it. Corrected here rather
than silently rewritten: check the artifact, not the note about the
artifact.

**AS BUILT (2026-08-13, migrations 010+011, api_smoke 255/255):**
```
ledger      widened: LEAGUE_PURSE/LEAGUE_CONSOLATION/LEAGUE_ENTRY/
            SHOP_BUY/SHOP_SELL/IAP/IAP_REFUND, each with a direction
            CHECK; new ref column (contest id / "part:mat")
inventory   (user_id, part_id, mat, count>=0) — count>=0 IS the
            ownership rule
part_prices GENERATED from the editor's live defs (84 rows, 22 parts;
            pinned mats emit only what they Accept; rosterOnly absent)
league_contests  hand-ported purse table (14 rows, sum 7125 — pinned
            by a bench)
iap_receipts (transaction_id PK — Apple's id, so double-credit is
            refused by the database)

GET  /v1/wallet              balance + recent + inventory (cache refill)
POST /v1/economy/claims      kind purse|consolation; purse idem_key is
                             SERVER-constructed (user,contest) so the
                             first-win ceiling is a uniqueness constraint;
                             consolation pre-first-win only, ≤3/contest
                             (register #6 default) under an advisory lock.
                             The "entry" kind existed for a few hours and
                             is GONE: owen removed league entry fees
                             completely the same day (migration 012 drops
                             the column; the client's fee field, gate,
                             charge/refund pair and every fee string are
                             hard-deleted, CareerSmoke 138/138)
POST /v1/economy/purchase    buy/sell at server prices, transactional,
                             idempotent
POST /v1/iap/verify          501 until App Store keys exist — reserved,
                             never fake
```
The client's first-win half shipped the same day (`7cb5738`): practice
bouts pay nothing in either direction, charge nothing, and say so on
every surface. Client wallet integration (the boot gate + cache) is the
remaining piece and is tracked with its hazards on the board.

Client: boot-time login/signup screen in front of ModeSelect (reusing the
ARENA sign-in flows and `LadderClient` sessions); the SHOP tab gates on
connectivity with honest copy; `Career` ownership/scrap reads become cache
reads with a server-wins reconcile on sync.

## Why now is the cheap moment

There are **zero shipped customers**. Exactly one career save exists and it
is owen's. Moving authority server-side today is a schema and some endpoints
on infrastructure that already runs (accounts, Postgres, the worker, deploy
scripts, `sql_bench`/`api_smoke` cover). After launch it is a migration of
live players' local balances that the server cannot verify. **This belongs
on the launch checklist** (`LAUNCH_CHECKLIST_2026-08-10.md`) if
commercialization precedes or accompanies launch.

## What this buys beyond revenue protection

Server-side inventory finally makes an **enlist-time ownership check**
possible — closing the standing arena-fairness hole (a save-editor fielding
parts they never earned). Recommended once inventory exists; today the
server validates build legality only.

## Costs and cautions, named

- SHOP-requires-online is a real UX change to the shipped single-player
  game — owen should see the copy before it ships.
- App Store Server API integration (receipt verification, refund
  notifications, key management) is new surface with compliance weight.
- **Owner state stays sacred.** Any reconcile that *refuses* a local save
  (signature mismatch, unknown ledger line) locks a paying customer out of
  their career — tonight's own restore-from-backup would have tripped it.
  Reconciles flag and overwrite the cache; they never brick the save.
- ⚠ **And the adoption bit its author the same night.** Owen signed into the
  gate in the EDITOR with a fresh dev account, and server-wins did exactly
  what this doc specifies: adopted 500 over his real career's 6,513 (−6013,
  in his ledger). A BUILD can never hit this — the gate forces sign-in
  before a career exists, so no device save carries a pre-wallet balance —
  but the editor career predates the wallet and is owner state. The fix:
  `EconomySync` in the editor runs only when a probe opts in
  (`editorOptIn`); the balance was repaired with an audited compensating
  transaction, adoption rows kept as history.
- Rate caps are a lever with a false-positive edge (a legitimate binge
  player); start generous, measure, tighten with numbers — never the
  reverse (house rule 3). Under the first-win rule they guard nothing in
  the league today — keep them out of v1 rather than shipping an untested
  lever, and add them only when a repeatable reward exists to bound.

## Open for owen (the register)

1. Single currency (scrap, server-side — recommended) vs a separate premium
   currency. A second currency only helps if premium goods are *exclusive*;
   if both currencies buy the same parts, the substitution attack returns.
2. Enlist-time ownership check: recommended yes, once inventory is
   server-side.
3. Scrap pack pricing, and whether any parts become IAP-exclusive.
4. Whether commercialization gates the TestFlight launch or follows it —
   decides whether this design is on the critical path.
5. **Career roaming.** The login gate makes multi-device play the promise,
   and wallet + inventory keep it (they live with the account). Robot
   designs, programs and league progress do NOT — they live in the local
   save. A player on a second device finds their scrap and parts but not
   their robots. Cloud career sync is a separate, larger decision; until it
   is made, the honest statement is "your wallet roams, your workshop does
   not."
6. **The consolation bound.** First-win-only purses cap the win path at
   13,850 per account, but `LossPay()` is repeatable and would leak
   unbounded through the loss path. How many consolation payments per
   contest (and whether they stop once the contest is won) is owen's call —
   the design only requires that SOME bound exist.
