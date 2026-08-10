# Defender protection — §2.2 shipped 2026-08-09

Glicko-2 (`f76f9a3`) made ratings mean something, which immediately made them
worth attacking. §2.2 is the three rules that price the attacks.

`api_smoke` **80 → 90**, `sql_bench` 45/45 unchanged, server log 0 errors.

| rule | as shipped |
|---|---|
| challenge tickets | 10 initiations per robot per day; defending is unlimited and free |
| defense loss floor | a defender drops at most 75 rating per day from defenses |
| repeat-opponent taper | gains vs the same opponent stop after 3 wins in a rolling 24 h |

## The prediction, and it was right for the wrong reason

> Tickets and taper will work first try; the **defense floor** is the one
> likely to break, because it has to read prior matches' `rating_deltas`
> JSONB and sum a day's losses — a query bug there fails silently by simply
> never clamping.

All ten checks passed on the first run. But the floor check **passed
vacuously**: it asserted `0 ≤ drop ≤ 5`, and the drop was 0 — not because
the clamp worked, but because section K's earlier fights had already spent
the entire day's allowance (`defenderDroppedToday: 145`). A range that
includes zero cannot tell a working floor from a floor that never runs.

So the prediction was right that the floor was the risky one; it was wrong
about where the risk lived. It was not in the query — it was in my check.

Fixed by resetting the fixture (strip the `defender` key from prior matches
so the day starts clean, seed exactly 70 of the 75 spent) and asserting the
drop is **exactly** the remaining 5. Same trap as this bench's own header
warns about: *"a bench that passes on nothing is worse than one that fails,
because it is believed."*

## ⚠ A real tension between §2.1 and §2.2 that the spec does not reconcile

**At placement deviation, one loss is worth more than a whole day's floor.**
Measured against the shipped `Glicko2.cs`:

| defender RD | 1200 → | drop |
|---|---|---|
| **350 (placement)** | 1037.7 | **162.3** |
| 300 | 1069.9 | 130.1 |
| 260 | 1095.8 | 104.2 |
| 200 | 1132.8 | 67.2 |
| 150 | 1159.8 | 40.2 |

§2.1 gives a new robot high deviation *so that placement converges fast*.
§2.2 caps its daily fall at 75. Those pull against each other: a new robot
that loses badly can only drop 75 that day, so it sits **above** its true
rating, which makes it a more attractive target, which it then cannot fall
away from until its deviation has tightened below ~200.

Both rules are implemented exactly as written. **This is a game-design
decision, not a bug, so it has not been "fixed" here.** The obvious levers,
none of them mine to pick:

1. Exempt placement matches (RD above some threshold) from the floor.
2. Scale the floor with deviation instead of a flat 75.
3. Accept it — the floor is anti-griefing, and a slow fall is the price.

## Decisions taken, because §2.2 is ambiguous

**"taper to zero after 3 wins" — cliff, not ramp.** The first 3 wins in the
window pay in full; the 4th and beyond pay nothing. "Taper" invites a graded
reading (full, ⅔, ⅓, 0), but the sentence says *to zero **after** 3 wins*,
which is a cliff — and a cliff is the version a player can reason about
mid-match.

**Gains only.** A loss still costs full price. Zeroing losses too would make
the 4th rematch a free roll.

**The stake still comes back on a tapered win.** It is escrow, not a fee.
Confiscating it would be a penalty §2.2 never asks for. So a tapered win
leaves the wallet exactly level — which is what the bench asserts.

**The floor clamps rather than skips.** "Cannot drop more than 75 points per
day" is a bound on the total, so a partial allowance is spent down to it and
the *next* match gets zero — which is also the spec's "excess challenges
apply zero rating delta". Deviation and volatility still move on a clamped
match: the defender did play a game, and freezing RD would make an actively
defended robot look idle.

## No new tables

`tickets` has existed since `001_init.sql` and nothing had ever consulted it.
The floor and the taper are both computed from `matches`, which already
records verdict, `completed_at`, both snapshots and (since 003)
`rating_deltas`.

**A derived rule that keeps its own counter is a rule that can disagree with
the history it derives from.** The one addition is an index — both rules ask
"what happened between these two robots in the last 24 hours", which without
it is a sequential scan of every match ever fought, on the hot path of
settling a fight.

The ticket spend is the *same statement* as the check
(`INSERT … ON CONFLICT DO UPDATE … WHERE used < cap RETURNING used`). A
read-then-write would let two concurrent challenges both see the ninth
ticket.

## Everything is reported honestly in `rating_deltas`

§2.2 asks for the floor to be *"shown honestly in the fight report"*. Each
settled match now carries `tapered`, `priorWins`, `defenderFloorReached`,
`defenderDroppedToday` and `defenderUnrated` — a player whose win paid
nothing is owed the reason.

## Still not done

- **No leaderboard endpoint.** Ratings are computed, protected and stored,
  and nothing serves them. Nobody can see the ladder they are climbing.
- **Season rollover** (§2.4) does not exist; season 1 runs to 2099.
- **First-blood bonus** is seeded in `ladder_config` and never applied — it
  needs "has this robot ever beaten that one before", which the match history
  can now answer but no code asks.
