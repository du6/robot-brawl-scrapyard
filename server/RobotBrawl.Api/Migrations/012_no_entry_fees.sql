-- ===========================================================================
-- Robot Brawl — league entry fees removed COMPLETELY (owen, 2026-08-13,
-- hours after 010 introduced them server-side). The league is free to enter
-- at every level; the purse and the first-win rule are the whole economy.
--
-- The COLUMN is dropped, not zeroed: a fee field constrained to zero is how
-- a rule gets half-resurrected later by someone who assumes it is still
-- live (the size-box lesson, Career.cs's own words). The LEAGUE_ENTRY
-- ledger reason stays legal — the ledger is append-only and any dev rows
-- that paid a fee are history, not errors — but the claims endpoint no
-- longer accepts the kind, so nothing can ever write another one.
-- ===========================================================================
BEGIN;

ALTER TABLE league_contests DROP COLUMN entry_fee;

INSERT INTO schema_version (version) VALUES (12);

COMMIT;
