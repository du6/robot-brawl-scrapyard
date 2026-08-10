-- ===========================================================================
-- Robot Brawl — Multiplayer v3: the ladder actually ranks people (§2.1).
--
-- Until now a fight settled scrap and moved nobody. `ratings` has existed
-- since 001 and nothing had ever written a row to it.
--
-- Three changes, all additive:
--
-- 1. ladder_config.value becomes NUMERIC. 002 made it INT because every
--    economy constant was a whole number of scrap. Glicko-2's tau is 0.5,
--    and storing it as "tau_milli = 500" would be a unit trap for whoever
--    tunes it next. INT -> NUMERIC is lossless, so the existing rows are
--    untouched.
--
-- 2. The two rating constants §2.1 names. The PLACEMENT values (1200/350/
--    0.06) are deliberately NOT here - they are already the column defaults
--    on `ratings`, and duplicating them would create two sources of truth
--    that can disagree.
--
-- 3. A season row. ratings.season_id is NOT NULL with no foreign key, so
--    the code could have hardcoded 1 - but a magic number with no row
--    behind it is the kind of thing that is still there in M3. Season 1
--    runs to 2099 because §2.4's season rollover does not exist yet, and a
--    season that silently ended would stop every rating write.
-- ===========================================================================
BEGIN;

ALTER TABLE ladder_config ALTER COLUMN value TYPE NUMERIC;

INSERT INTO ladder_config (key, value, note) VALUES
  ('glicko_tau', 0.5,
   '2.1: Glicko-2 system constant. Smaller = volatility moves more slowly. 0.5 is Glickman''s suggested range midpoint.'),
  ('cross_category_offset', 150,
   '2.1: a punch-up challenger is rated against defender_rating + offset * category_gap. The defender is not rated at all in a cross-category fight.');

INSERT INTO seasons (id, starts_at, ends_at)
VALUES (1, now(), TIMESTAMPTZ '2099-01-01 00:00:00+00');

INSERT INTO ladder_config (key, value, note) VALUES
  ('current_season', 1, '2.4: the season new ratings are written into. Season rollover is not built yet.');

INSERT INTO schema_version (version) VALUES (3);

COMMIT;
