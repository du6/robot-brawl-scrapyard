-- ===========================================================================
-- Robot Brawl — Multiplayer v3 §2.2: defender protection.
--
-- Glicko-2 (003) made ratings mean something, which immediately made them
-- worth attacking. §2.2 is the three rules that price the attacks:
--
--   * challenge tickets      — 10 initiations per robot per day
--   * defense loss floor     — a defender drops at most 75 points/day
--   * repeat-opponent taper  — gains vs the same opponent stop after 3 wins/24h
--
-- Only the constants are new. `tickets` has existed since 001 and nothing
-- ever consulted it; the floor and the taper are both computed from
-- `matches`, which already records verdict, completed_at, both snapshots and
-- (since 003) rating_deltas. No new tables: a derived rule that keeps its own
-- counter is a rule that can disagree with the history it is derived from.
--
-- The index matters. Both rules ask "what happened between these two robots
-- in the last 24 hours", and without it that is a sequential scan of every
-- match ever fought, on the hot path of settling a fight.
-- ===========================================================================
BEGIN;

INSERT INTO ladder_config (key, value, note) VALUES
  ('challenge_tickets_per_day', 10,
   '2.2: challenges a robot may INITIATE per day. Defenses are unlimited and free.'),
  ('defense_daily_floor', 75,
   '2.2: max rating a defender can lose per day from defenses. Excess challenges still pay the winner scrap but move the defender 0.'),
  ('repeat_win_taper_after', 3,
   '2.2: wins vs the SAME opponent in a rolling 24h that still pay. The 4th and beyond pay no rating and no purse - this is what kills win-trading.');

CREATE INDEX matches_completed_pairs
    ON matches (completed_at DESC)
 WHERE status = 'COMPLETE';

INSERT INTO schema_version (version) VALUES (4);

COMMIT;
