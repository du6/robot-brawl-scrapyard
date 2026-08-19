-- ===========================================================================
-- Robot Brawl — a display name is now GLOBALLY UNIQUE (owen, 2026-08-19:
-- "make sure that the email is not already registered and the username is
-- globally unique").
--
-- Email was already covered: `users_email_lower_key` on the generated
-- `email_lower` column has enforced case-insensitive uniqueness since 001,
-- and /v1/auth/register maps the resulting 23505 to a 409. Display names had
-- NOTHING — only a 2-24 length CHECK — so any number of accounts could call
-- themselves "Owen", and the leaderboard's `owner` column is a display name.
-- Two players with one name on a public board is not a cosmetic problem: it
-- is impersonation of the person at the top of it.
--
-- ⚠ THIS MIGRATION MUST SURVIVE EXISTING DUPLICATES, and it will meet them.
-- `api_smoke.sh` has registered accounts against real databases for weeks
-- using FIXED display names ("Smoke", "Defender", "Econ", "Twin", "PxyB"),
-- and the ten house robots are owned by something calling itself "The Yard".
-- A migration that just adds a unique index would fail on the first of those
-- and take the whole deploy with it, at which point the pressure is to skip
-- it. So it resolves collisions instead of asserting there are none — and
-- the resolution is deliberately the conservative one:
--
--   * The EARLIEST account (by created_at, id as the tiebreak) keeps the name.
--     Seniority is the only rule here that cannot be gamed by whoever
--     re-registers fastest.
--   * Every later claimant is renamed to "<base>~<first 4 of its uuid>",
--     with the base truncated so the result still satisfies the 2-24 CHECK.
--   * NOTHING IS LOST. `display_name_migration` keeps the old name, the new
--     name, and when it happened, so a support request can undo any single
--     rename by hand and so this is auditable rather than a mystery.
--
-- ⚠ AND IT IS A USER-VISIBLE CHANGE. If a real player is renamed they will
-- see it on the board and in the dock. Read `display_name_migration` after
-- deploying and tell anyone real who was affected — the table exists so that
-- is a query, not an archaeology project.
-- ===========================================================================

BEGIN;

-- The audit trail is created FIRST so the rename below has somewhere to land
-- even if a later statement in this transaction fails and is rolled back for
-- inspection.
CREATE TABLE display_name_migration (
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    old_name    TEXT NOT NULL,
    new_name    TEXT NOT NULL,
    migrated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, migrated_at)
);
COMMENT ON TABLE display_name_migration IS
    'Renames forced by the 015 unique-display-name migration. One row per '
    'account that lost a name collision. Keep it: it is how a rename is undone.';

-- Resolve collisions, oldest account wins. `dup` numbers the claimants of each
-- lowercased name by age; everyone after the first is renamed.
WITH dup AS (
    SELECT id, display_name,
           row_number() OVER (PARTITION BY lower(display_name)
                              ORDER BY created_at, id) AS n
      FROM users
),
renamed AS (
    UPDATE users u
       SET display_name =
           -- 24-char ceiling: keep 19 of the base, then "~" + 4 hex. A base
           -- shorter than that is untouched. left() is safe on short strings.
           left(u.display_name, 19) || '~' || left(replace(u.id::text, '-', ''), 4)
      FROM dup
     WHERE u.id = dup.id AND dup.n > 1
 RETURNING u.id, dup.display_name AS old_name, u.display_name AS new_name
)
INSERT INTO display_name_migration (user_id, old_name, new_name)
SELECT id, old_name, new_name FROM renamed;

-- ⚠ The rename above can itself collide — "Smoke" and "Smoke~a3f2" could in
-- principle already both exist, and a base truncated to 19 chars can collide
-- with a different long name. The unique index below is what actually decides
-- whether this migration is correct; if it fails, the transaction rolls back
-- and nobody is half-renamed. That is the intended failure mode: refuse,
-- loudly, rather than deploy a database that does not hold the invariant.
ALTER TABLE users
    ADD COLUMN display_name_lower TEXT GENERATED ALWAYS AS (lower(display_name)) STORED;

-- Case-insensitive, exactly like email: "Owen" and "owen" are one name, so
-- one player cannot shadow another by changing capitalisation.
CREATE UNIQUE INDEX users_display_name_lower_key ON users (display_name_lower);

INSERT INTO schema_version (version) VALUES (15);

COMMIT;
