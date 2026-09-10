-- ===========================================================================
-- Robot Brawl — THE SERVER LEARNS A SECOND GAME (owen, 2026-09-09:
-- "Robot Brawl: Scrapyard" — docs/Scrapyard_Design_2026-09-09.md §7.3).
--
-- Scrapyard is a separate game forked from this repo and it SHARES THIS
-- SERVER: one API, one database, one worker, one set of accounts, ratings
-- and seasons. Three things the shared server needs, and this migration
-- carries the one that is schema:
--
--   1. snapshots.game — which game uploaded the robot. A LABEL, not a
--      ruleset: the pool and the board badge robots by origin. Existing
--      rows are Robot Brawl's ('rb'); the default keeps every existing
--      caller (Bolt & Blade's LadderClient sends no game field) exactly as
--      it was.
--
--   2. the 'yard' ruleset rides on matches.arena, which ALREADY EXISTS
--      ('league' by default, 'league_night' from the admin endpoint, and
--      api_smoke writes 'synthetic' straight into the table). No CHECK is
--      added here on purpose: the column carries values a CHECK on
--      {league, yard} would refuse, and the API validates the request
--      field instead (POST /v1/challenges accepts league|yard). The WORKER
--      reads arena from its claim — it always received it and never read
--      it for rules until now — and fights a 'yard' match on the Quick
--      profile (30 s, 5-s count-out, crusher walls over the last 10 s).
--
--   3. GET /v1/pool — anonymous, class-filtered, build-only (never the
--      program). Code, not schema; the partial index below is what it
--      rides: "ACTIVE snapshots of this class", sampled.
-- ===========================================================================
BEGIN;

ALTER TABLE snapshots
    ADD COLUMN game TEXT NOT NULL DEFAULT 'rb'
        CHECK (game IN ('rb', 'scrapyard'));

-- The pool asks one question: ACTIVE snapshots in a weight class. Partial
-- on status so it stays the size of the ladder, not of the upload history.
CREATE INDEX IF NOT EXISTS snapshots_pool_idx
    ON snapshots (category)
    WHERE status = 'ACTIVE';

INSERT INTO schema_version (version) VALUES (17);

COMMIT;
