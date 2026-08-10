-- ===========================================================================
-- Robot Brawl — §2.4 season rollover.
--
-- "4-week seasons. At rollover: ratings compress 50% toward 1200, deviation
--  resets high, wallet scrap persists, season badges + payouts awarded.
--  Seasons are what keep a solved ladder from fossilizing and give lapsed
--  players a re-entry point."
--
-- The compression factor and the season length are config, not constants:
-- §2.4 says explicitly that "length is a LadderConfig value, so lengthening
-- to 8 weeks later as the ladder deepens is a config change, not a client
-- update".
--
-- ⚠ THE PAYOUT NUMBERS ARE MINE, NOT THE DOC'S. §2.3's table says only
-- "season-end payout | by final rating rank per category" with no figures.
-- The scheme here is base/rank over the top few places - 300 / 150 / 100 -
-- chosen because §2.3 says the faucets start CONSERVATIVE and inflating
-- career progression from the ladder is a one-way door: buffing later is a
-- celebrated event, nerfing is a riot. They are dials, so owen can raise them
-- without a deploy once there is evidence about what a season is worth.
--
-- Wallet scrap persisting needs no code at all: the ledger is append-only and
-- the rollover simply does not touch it. That is the design working.
-- ===========================================================================
BEGIN;

INSERT INTO ladder_config (key, value, note) VALUES
  ('season_weeks', 4,
   '2.4: season length. Lengthening to 8 as the ladder deepens is a config change, not a client update.'),
  ('season_compress_pct', 50,
   '2.4: percent of the distance to 1200 that a rating keeps at rollover. 50 means a 1600 becomes 1400.'),
  ('season_payout_base', 300,
   '2.4: scrap to the top-ranked robot in a category at season end. Lower places get base/rank. NOT a doc figure - the doc says only "by final rating rank"; conservative per 2.3.'),
  ('season_payout_places', 3,
   '2.4: how many places per category are paid.');

INSERT INTO schema_version (version) VALUES (7);

COMMIT;
