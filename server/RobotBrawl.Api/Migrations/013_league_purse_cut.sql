-- ===========================================================================
-- Robot Brawl — league purses CUT BY ONE THIRD (owen, 2026-08-15: "cut league
-- rewards by one third"). Follows the 2026-08-12 HALVING's scope exactly: only
-- the WIN payment moves. The purse table drops from a sum of 7125 to 4750 —
-- exactly two-thirds, each value rounded to the nearest scrap. The matching
-- WIN-payment constants move in the API (ECON_WIN_DMG_K 0.25 -> 0.1667,
-- ECON_FIRST_WIN_BONUS 75 -> 50) and in the client (Career.cs FIRST_WIN_BONUS
-- / WIN_DMG_K / the Leagues table) — the three copies are HAND-PORTED and must
-- stay identical; api_smoke's SUM(purse) check is what notices drift.
--
-- Data migration, not schema: an UPDATE so already-applied databases (the live
-- Cloud Run DB) take the cut too; editing 010 would only reach fresh DBs.
-- Season/arena purses are deliberately NOT touched (they stay the richer earn).
-- ===========================================================================
BEGIN;

UPDATE league_contests SET purse =  83 WHERE id = 'L1C1';
UPDATE league_contests SET purse = 100 WHERE id = 'L1C2';
UPDATE league_contests SET purse = 133 WHERE id = 'L2C1';
UPDATE league_contests SET purse = 150 WHERE id = 'L2C2';
UPDATE league_contests SET purse = 167 WHERE id = 'L2C3';
UPDATE league_contests SET purse = 233 WHERE id = 'L3C1';
UPDATE league_contests SET purse = 267 WHERE id = 'L3C2';
UPDATE league_contests SET purse = 300 WHERE id = 'L3C3';
UPDATE league_contests SET purse = 317 WHERE id = 'L3C4';
UPDATE league_contests SET purse = 400 WHERE id = 'L4C1';
UPDATE league_contests SET purse = 467 WHERE id = 'L4C2';
UPDATE league_contests SET purse = 533 WHERE id = 'L4C3';
UPDATE league_contests SET purse = 600 WHERE id = 'L4C4';
UPDATE league_contests SET purse = 1000 WHERE id = 'L5C1';

INSERT INTO schema_version (version) VALUES (13);

COMMIT;
