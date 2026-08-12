# Seasons wired end-to-end — and the board bug the first rollover would have detonated (2026-08-12)

## What was true this morning

The ladder's top-robot rewards **already existed, in three deliberate
tiers**, benched and deployed: challenge matches escrow a stake and pay a
tapered purse; league nights deliberately pay nothing (rating is the reward —
no faucet on server-generated content); and `/v1/admin/season/rollover` pays
a per-category podium (300/150/100 scrap, configurable) and writes a
**permanent season badge** in the same transaction, idempotent against
double-payouts.

And none of it was reachable:

1. **Nothing called the rollover in production** — api_smoke's curl was its
   only caller ever. The missing-caller pattern, third occurrence (after
   ENLIST and `LadderClient.BaseUrl`).
2. **The client rendered none of it.** The server has sent `seasonHistory`
   on every scouting card since badges shipped; the client dropped it
   unparsed. No screen showed a season, a podium, or a countdown.

## The latent defect the scheduler would have armed

`GET /v1/leaderboard` had **no season filter**. `ratings` is keyed
(robot, category, **season**) and the rollover carries compressed rows into
the new season while keeping the old season's rows for the record — so the
first production rollover would have listed **every robot once per season**
on the board. Invisible until now for the same reason the feature was: no
rollover had ever run outside the dev DB. Fixed
(`WHERE ra.season_id = current_season`), and section P now ends with the
regression check: board count == current-season ratings count, after a roll.

## What changed

**Server** (`Program.cs`):
- **Timing guard**: an unforced rollover answers `200 {rolled:false, endsAt}`
  until the current season's `ends_at` has passed. 200, not 4xx — "not due"
  is the daily scheduler's normal outcome ~27 days in 28, and Cloud Scheduler
  retries and alerts on non-2xx. The first unforced call ever **starts season
  1's clock** (writes the missing seasons row) instead of instantly closing
  it. `?force=1` skips the clock only — the duplicate-season 409 and the
  payout idempotency keys still apply to forced calls.
- **Board**: season-filtered (above), and the response now carries
  `season` + `seasonEndsAt` so the board can say what season it is showing.

**Scheduler** (`server/scripts/deploy_season_scheduler.zsh`, idempotent):
daily POST at 09:30 UTC — after the 09:00 DB backup, so the last
pre-rollover state is always restorable. The endpoint owns the calendar, the
cron just ticks; retuning `season_weeks` in `ladder_config` needs no
scheduler change.

**Client**:
- `LadderClient` parses `seasonHistory` into `ScoutCard.badges` and the
  board's `season`/`seasonEndsAt` into statics; `SeasonLabel()` renders
  "season 2 · ends in 6d" — one producer for both renderers.
- Scouting card shows the podium line ("podium: S1 #1 MIDDLE (1574)"),
  capped at four, absent when empty — in BOTH renderers (OnGUI card and the
  dock card, gold text, `ArenaScreen.BadgeLine` shared).
- Board headers (both) append the season label.

## Measured

- `run_local.sh`: sql_bench **53/53**, api_smoke **208/208** (P grew to 16
  checks: guard-refuses / clock-starts / forced-rolls / double-close-409 /
  no-double-pay / board-single-listing), restore_drill 10/10.
- `LadderClientBench.RunPure()` **37/37** (+9: season/seasonEndsAt exact-key
  non-collision, old-server silence, badge objects, empty shelf,
  string-array-vs-object-array).
- `CategoryBench` 44/44.

## NOT measured, said plainly

- **The two render sites have not been photographed against a live server.**
  Parsing is benched; the render lines compile and follow the existing
  MkText/GUILayout idiom, but no eye has seen a badge on a card. Owed on the
  next live ARENA pass (needs a robot with a badge, i.e. a dev-DB rollover +
  scout).
- **Nothing has been deployed.** Production still runs the unfiltered board
  and has no scheduler job. Going live is two commands, in this order:
      zsh server/scripts/deploy_api.zsh                # ships guard + board filter
      zsh server/scripts/deploy_season_scheduler.zsh   # starts the daily tick
  Order matters: the scheduler's first tick against the OLD api would roll
  the season instantly (no guard). Deliberately left for owen — it changes
  the live service and starts a real (guarded) scrap faucet.

## Postscript — deployed the same day (c2649cc)

Owen said run it, and the deploy immediately surfaced a fourth finding:
**production's season 1 already had a row — ending at a 2099 sentinel**
(`003_ratings.sql`, "runs to 2099 because §2.4's season rollover does not
exist yet"). Two consequences, both fixed before the scheduler went in:

- The guard would have ticked politely until 2099. It now **retires the
  sentinel on the first unforced tick** (`ON CONFLICT DO UPDATE … WHERE
  ends_at = sentinel` — structurally unable to touch a real clock), and the
  smoke checks assert the retirement, not row-existence — the existence
  check had been passing **vacuously** against the migrated row.
- `SeasonLabel` would have rendered "ends in 4500d"; it now caps the
  countdown at a year. Its new fixtures also caught
  `RoundtripKind|AdjustToUniversal` being an illegal `DateTimeStyles`
  combination — the label would have **thrown on the first real date it
  ever parsed**. The first caller was the bench, not a player.

**Live state, verified**: API revision `20260812-151959`; scheduler
`rb-season-rollover` ENABLED, daily 09:30 UTC; first tick fired manually —
production season 1 ends **2026-09-09**, board lists 3 robots once, ledger
untouched. The first real podium pays out on that day's tick.
Re-measured after: run_local 53/53 + **209/209**, LadderClientBench
**40/40**. Still owed: a screenshot of a badge on a live scouting card.
