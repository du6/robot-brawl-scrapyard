-- ===========================================================================
-- Robot Brawl — record what was ACTUALLY staked on a match.
--
-- Settlement has been RECOMPUTING the stake at settle time from
-- ladder_config, rather than reading what the challenge charged. Two things
-- go wrong with that, one narrow and one about to become wide:
--
-- 1. ladder_config is tunable without a deploy - that is the entire point of
--    it (§2.3). Change stake_base between a challenge and its settlement and
--    the refund differs from the debit. Scrap is minted or destroyed, and the
--    ledger conservation checks would report it as a mystery.
--
-- 2. LEAGUE NIGHTS (§M3) are server-initiated: nobody challenged, so nobody
--    staked. With a recomputed stake, settling one would write a
--    STAKE_REFUND against a STAKE that never existed - minting scrap out of
--    a fight nobody paid for. Recording 0 makes that unrepresentable rather
--    than merely unlikely.
--
-- DEFAULT 0 is right for the backfill too: the only rows that exist when this
-- runs are from a dev database, and 0 means "refund nothing", which is the
-- safe direction for a column about money.
-- ===========================================================================
BEGIN;

ALTER TABLE matches ADD COLUMN stake INT NOT NULL DEFAULT 0
    CHECK (stake >= 0);

COMMENT ON COLUMN matches.stake IS
    'Scrap actually debited into escrow when this match was created. 0 for server-initiated matches (league nights), which nobody staked. Settlement refunds THIS, never a recomputed value.';

INSERT INTO schema_version (version) VALUES (6);

COMMIT;
