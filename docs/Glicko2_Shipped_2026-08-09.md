# The ladder ranks people — Glicko-2 shipped 2026-08-09

Until today a fight settled scrap and **moved nobody**. `ratings` had existed
since `001_init.sql` and had never held a row; `matches.rating_deltas` was
always NULL. Two robots fought and the standings did not know.

`api_smoke` **74 → 80**, `sql_bench` 45/45 unchanged, server log 0 errors.

A real ladder after one bench run:

```
Defiant  | FEATHER | 1267 | rd 260     won its defense
Smoky    | FEATHER | 1133 | rd 260     lost that one
Smoky    | SUPER   | 1574 | rd 327     beat a SUPER from FEATHER — four classes up
Colossus |         | (no row)          punched-up-at, therefore not rated at all
```

## The algorithm is checked against the paper, not against itself

A subtly wrong Glicko-2 still produces a plausible leaderboard, and nobody
notices for a season. So `Glicko2.cs` is deliberately pure — no database, no
HTTP — and `server/tests/glicko_check` runs the worked example from Mark
Glickman's *"Example of the Glicko-2 system"* (2013):

| | r' | RD' | σ' |
|---|---|---|---|
| paper | 1464.06 | 151.52 | 0.05999 |
| ours | **1464.05** | **151.52** | **0.06000** |

Within the paper's own two-decimal rounding.

**This is why `Update` takes a list of opponents when the ladder only ever
passes one.** The paper's example uses three; a single-opponent-only
implementation could only ever have been tested against itself.

It also checks the idle case — a rating period with no games leaves the
rating alone and **widens** the deviation (200 → 200.27). That is the "idle
ranks go soft rather than squatting" property §2.1 chose Glicko-2 for, and it
is the one behaviour Elo cannot express.

## The bench caught a real design leak

The punch-up check failed on the first run: `got '1', wanted '0'`.

`LoadRating` created a placement row for **every** participant, including a
defender the code was about to deliberately *not* rate. So Colossus — a SUPER
that had never fought a SUPER — appeared on the SUPER leaderboard at 1200
purely because a featherweight reached up at it.

No *number* had changed, so a loose reading of §2.1's "the defender's rating
is untouched" would have passed. But the leaderboard was wrong, which is what
the rule is protecting. **"Untouched" has to mean no row appears, not just no
value changes.** `LoadRating` now takes `create:` and the punch-up path passes
`false`, returning placement values in memory for the offset maths without
persisting anything.

House rule 2 says ask whether the CHECK is wrong before changing the product.
Here the check was right and the product was wrong — the first time that has
happened this session.

## What §2.1 says, and what the code does

- **Placement 1200, high deviation** — the column defaults on `ratings`, not
  constants in code. One source of truth; an INSERT naming no values gets
  them.
- **Same-category: both sides update**, win = 1, loss = 0.
- **No draws.** §2.1: "no draws — judges decide", so a DRAW verdict applies
  no rating change at all. That also covers the worker's refusal path, which
  posts DRAW when no fight happened — nobody should climb for a match that
  never ran.
- **Punch-up:** the challenger is rated against `defender_rating + 150 × gap`.
  Smoky's SUPER rating of 1574 is that rule working — it beat an opponent
  treated as 1200 + 150×4 = 1800.
- **The defender is not rated at all in a cross-category fight**, so heavies
  cannot farm rating off lightweights nor lose rank to a swarm of speculative
  punch-ups.

Ratings are written **in the same transaction as the money**. A rating that
survived a rollback of its own match is a rank nobody can explain.

## Migration 003

- `ladder_config.value` **INT → NUMERIC**. 002 made it INT because every
  economy constant was whole scrap; `glicko_tau` is 0.5, and storing it as
  `tau_milli = 500` would be a unit trap for whoever tunes it next. Lossless,
  so existing rows are untouched.
- `glicko_tau = 0.5` and `cross_category_offset = 150`. The **placement**
  values are deliberately absent — duplicating the column defaults would
  create two sources of truth that can disagree.
- **A season row.** `ratings.season_id` is NOT NULL with no foreign key, so
  the code could have hardcoded `1` — but a magic number with no row behind
  it is still there in M3. Season 1 runs to 2099 because §2.4's rollover does
  not exist and a season that silently ended would stop every rating write.

## What is still NOT here

All of §2.2, and none of it was in scope:

- **Challenge tickets** (10/day per robot). The `tickets` table exists and
  nothing consults it, so challenge frequency is unbounded.
- **Defense loss floor** (a defender may not drop more than 75 points/day
  from defenses).
- **Repeat-opponent taper** (gains taper to zero after 3 wins vs the same
  opponent in 24 h). Without it, win-trading is unpriced.

Also absent: season rollover, and any leaderboard endpoint — the ratings are
computed and stored, but nothing serves them yet.
