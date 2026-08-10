-- ===========================================================================
-- Robot Brawl — §2.4 season badges.
--
-- "At rollover: … season badges + payouts awarded." The payouts shipped with
-- migration 007; the badges did not, because there was no schema for one and
-- inventing a table mid-rollover seemed worse than saying it was not done
-- (M3_Server_Complete_2026-08-09.md). This is that table.
--
-- A badge is the PERMANENT half of a season result. The ledger row pays the
-- scrap and is an audit line nobody looks at again; the badge is what a
-- scouting card shows two seasons later. It stores the final rating because
-- the card wants to say "1st, at 1574" — the ratings row for a closed season
-- also holds it, but a badge should survive even if an old season's ratings
-- are ever archived away.
--
-- The PRIMARY KEY is (robot, season, category) for the same reason the season
-- payout's idempotency key is: §2.1 rates per robot PER CATEGORY, so a
-- featherweight that punched up can hold a podium place in two categories in
-- the same season — two badges. A robot cannot hold two badges in ONE
-- category per season, and the key refuses it the same way the ledger's
-- unique index refuses a double payout.
-- ===========================================================================
BEGIN;

CREATE TABLE season_badges (
    robot_id     UUID NOT NULL REFERENCES robots(id) ON DELETE CASCADE,
    season_id    INT  NOT NULL REFERENCES seasons(id),
    category     TEXT NOT NULL CHECK (category IN ('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')),
    place        INT  NOT NULL CHECK (place >= 1),
    final_rating DOUBLE PRECISION NOT NULL,
    awarded_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (robot_id, season_id, category)
);

INSERT INTO schema_version (version) VALUES (9);

COMMIT;
