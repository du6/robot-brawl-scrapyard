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
   ⚠ Player-facing consequence to ship with it: today's game CHARGES a
   re-entry fee (the "✓ … 50 scrap (re-entry)" rows) and pays again.
   Both sides of that change — free entry, zero payout — land together or
   the economy is asymmetric; the LEAGUE row copy changes from
   "(re-entry)" to a practice label; `CareerSmoke`'s contest-row checks
   will need the same update when this is implemented.

## Sketches (to guide, not to prescribe)

Schema:
```
wallets    (account_id PK, balance, updated_at)
ledger     (id, account_id, delta, cause, ref_id UNIQUE, created_at)
iap        (transaction_id UNIQUE, account_id, product, scrap, status)
inventory  (account_id, part_id, mat, count)
```
`ref_id` carries the idempotency key (purse claim id, purchase id, IAP
transaction id) — replays are no-ops by construction, the same discipline
the match/claim contract already uses.

API:
```
GET  /v1/wallet                     balance + inventory (the cache refill)
POST /v1/economy/claims             batch of league purse claims (capped)
POST /v1/economy/purchase           shop buy/sell at server prices
POST /v1/iap/verify                 signed StoreKit transaction
```

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
