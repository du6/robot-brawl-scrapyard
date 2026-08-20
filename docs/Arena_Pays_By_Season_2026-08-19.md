# The arena stops paying per match — 2026-08-19

owen: *"users can abuse arena rewards by unloading junk robots. let's remove
per game rewards, but give rewards per session. top 10 users for each season
get rewards."*

Server + client + benches, measured. **Not deployed** — this is owen's call.

---

## 1. The abuse, stated exactly

The only self-challenge guard in `POST /v1/challenges` was

```csharp
if (ch.RobotId == df.RobotId) return Bad("a robot cannot challenge itself");
```

which compares **robots**, not **owners**. So:

1. enlist a strong robot and a junk one;
2. challenge the junk one with the strong one;
3. win — the stake comes back (it is escrow, not a fee) and `PURSE` pays
   `win_purse_base` = 100 for a fight that cost nothing and risked nothing.

§2.2's repeat-opponent taper bounds step 3 against **one** opponent. It does
not bound the number of opponents, and an account may enlist as many robots as
it likes. **That is the whole exploit: the taper is defeated by unloading more
junk.** The `DEFENSE` purse was the same hole from the other side — 40 scrap
flat for being challenged, and the attacker did not even have to win.

⚠ **The fix is structural, not a threshold.** There is no longer any amount a
match can pay, so there is nothing for a junk robot to farm and no number
anyone had to guess.

## 2. What moved

| | before | after |
|---|---|---|
| challenge stake | `stake_base` 50 × (1+gap) | **0** — free to enter |
| challenger win | `PURSE` 100 × (1+0.5·gap)² | **nothing** |
| defender win | `DEFENSE` 40 flat | **nothing** |
| season payout | top **3 robots per category**, 300/rank | top **10 users**, 1000/rank |
| season badge | same loop as the payout | unchanged: top 3 per category, per robot |
| self-dealing | allowed | refused, 400 |

**`win_purse_base` and `defense_purse` are DELETED, not zeroed.** `ladder_config`
is tunable without a deploy — that is the point of it — so a zeroed key is a
live lever that reopens a known hole with one `UPDATE`. This is the same
reasoning `012_no_entry_fees` used when it dropped a **column** instead of
constraining it to zero. `sql_bench` now asserts both keys are **absent**.

**`stake_base` is set to 0 and stays**, because it is a genuine lever and 0 is
a legitimate setting for it. With no purse, a stake would be a pure penalty
owen did not ask for — every challenge EV-negative, so nobody challenges, and
the ladder the season prize exists to rank goes quiet.

### The payout curve

`base/rank` over ten places: **1000 / 500 / 333 / 250 / 200 / 166 / 142 / 125 /
111 / 100 = 2927 scrap per season.** Chosen to land near the old theoretical
ceiling (3 places × 5 categories × (300+150+100) = 2750) so the total faucet is
roughly preserved while its shape changes completely.

⚠ **Unlike that old ceiling, this one is exact.** At most ten users are paid,
so the arena's entire per-season exposure is 2927 and cannot be farmed upward.
Both numbers are `ladder_config` keys — **tune them without a deploy.**

### How a user is ranked

**Their best rated robot.** Not a sum — a sum pays for breadth, which is
"enlist more robots", the exact behaviour being closed. Not an average, which
would punish experimenting. One account, one entry, and more robots never
helps. Ties break on rating then user id: total and deterministic, so a re-run
of the same standings pays the same people.

## 3. The eligibility rule, and the one I measured and threw away

A rating must have been **earned** to rank, or an account walks into the paid
places by uploading and never fighting — the same abuse in a different coat.

First attempt: the leaderboard's own provisional line, `deviation > 200`.
**Measured before believing it, and it was wrong.** After a full `api_smoke`
run the tightest rating on the dev ladder sat at **RD 258**, so a 200 gate made
*every* player ineligible and the season paid **nobody**. Glicko-2 deviation
falls slowly; 200 is a many-fight bar, not a did-you-turn-up bar.

Shipped rule: **`deviation < 350`**, i.e. strictly below the enlistment value.
Enlistment assigns exactly 1200/350 and only real fights move deviation down,
so this reads precisely as *"has fought at least once"*. It is
`season_rank_max_deviation`, so it tightens without a deploy if the ladder ever
gets crowded enough to need it.

⚠ **The same filter had to go on BADGES, and that was missed on the first
pass.** Badges are not money, so it is tempting to leave them open — but a
never-fought robot sits at 1200, and on a young ladder 1200 is a podium. It
would take a permanent *"2nd, FEATHER, Season 3"* off a robot that turned up.
**Found by planting the abuse in the bench, not by reasoning about it:** the
fixture took a badge on its first run.

## 4. Closing the relocation

Removing the money is only half. **Ranking now pays, and rating is what ranks**
— so unchanged, the identical self-dealing would farm *rating* instead of
scrap. Two things had to hold:

* `ch.UserId == df.UserId` is now refused (400), and the refusal **costs no
  daily ticket** — otherwise the attempt becomes a way to burn a rival's ten.
* **The repeat-opponent taper stays, and matters MORE than it used to.** It was
  written to stop win-trading for scrap; there is no scrap to trade now, and
  its remaining job is stopping one pair farming rating. Deleting it alongside
  the purse would have moved the exploit rather than closed it.

⚠ **Still open, and named rather than fixed: SYBIL.** Two accounts can still
trade wins — 3 per pair per 24 h under the taper, with 10 tickets a day. Over a
four-week season that is real rating. Nothing here addresses it, and no
eligibility threshold would.

## 5. What a player sees

`GET /v1/seasons/standings` is new. A prize nobody can see the standings for is
a prize nobody plays for: the old purse landed in the wallet minutes after a
fight, and replacing it with a payment weeks away and invisible until it
arrives would read as the arena simply having stopped paying.

⚠ **It is deliberately the SAME query the rollover pays on**, and `api_smoke`
checks the preview against a real forced rollover. A preview that ranks
differently from the payout is worse than no preview. **If one changes, change
both.**

Client copy: the challenge card no longer names a stake or a purse (both
mirrors are deleted — `StakeForPick`, `PurseForPick`, `StakeFor`). The two-tap
confirm stayed, because what it guards moved rather than vanished: a challenge
spends one of ten daily tickets and moves rating either way.

⚠ **Do not put a scrap figure back on that card from memory.** The old copy
named a stake and a purse the server no longer charges or pays, and a client
mirror of a server dial can only ever be right by luck. The one place a price
must genuinely appear — the moment a charge happens — reads the **server's**
`stake` from the challenge response and stays silent when it is 0, so turning
the dial back up needs no client deploy.

## 6. Measured

| bench | result |
|---|---|
| `sql_bench.sh` | **56/56** |
| `api_smoke.sh` | **356/356**, 1 skip (blob store, pre-existing) |
| `restore_drill.sh` | pass |
| `ChallengeGateBench.RunPure()` | **17/17** |
| `CareerSmoke` | **143/0** |
| career save | `b1e169a7…`, **mtime unchanged** |

Eight `ChallengeGateBench` checks were **deleted with the methods they pinned**
(`…stakes 50`, `…pays 225`). They were a client mirror of a server dial, and
they would have gone on passing for as long as the mirror agreed with itself
while the server charged and paid nothing. **A bench that pins a copy of a
number nobody honours is not coverage** — it is how a wrong price survives a
green suite. Three `GapForPick` checks replace them; the money claims moved
server-side where the ledger can be read.

## 7. Two traps this turned up

⚠ **AN EDITED MIGRATION IS SILENTLY SKIPPED ON A DEV DB.** `run_local.sh`
TRUNCATEs but never drops, and the runner skips any version already in
`schema_version`. Migration 016 was recorded on the first run; every later edit
to it — including a whole new config key — was invisible, and the bench failed
against a key the file plainly contained. Fix while iterating:
`DELETE FROM schema_version WHERE version = 16;`

⚠ **A NEW UPLOAD IN A BENCH SECTION CAN BREAK THE SECTION AFTER IT.** The first
draft of the self-dealing check enlisted its own sandbag robot and pushed
section K past the `upload` limiter: three unrelated checks became 429s and one
section skipped. It now reuses two ACTIVE snapshots the defender account
already owns. (Same shape as the 08-19 auth-limiter lesson in section T.)

And one bench-quality note worth keeping: **the exclusion rule was VACUOUS on
its first green run** — the report read *"0 enlisted without fighting"*, so the
check that junk enlistment earns nothing had nothing to exclude and would have
passed identically with no rule at all. The fixture is now planted on purpose,
and **seeded ABOVE the leader** — parked at 1200 it would have been excluded by
rank alone, which is the same vacuum in a subtler form.

## 8. Not done

* **Not deployed.** `deploy_api.zsh` is owen's call. Migration 016 runs at
  container boot, so deploying the API applies it.
* Existing in-flight matches with a real escrowed stake are refunded on **every**
  verdict, including a defender win that used to forfeit it — the forfeit only
  ever funded `DEFENSE`, and keeping it without the payment would burn a
  player's scrap into nobody's wallet.
* No client build carries this yet.
