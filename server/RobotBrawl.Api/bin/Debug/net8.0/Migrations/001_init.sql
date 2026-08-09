-- ===========================================================================
-- Robot Brawl — Multiplayer v3, M1 schema (design doc §6).
--
-- PORTABILITY (§7 rule 2): plain PostgreSQL only. No extensions beyond
-- pgcrypto for gen_random_uuid (core since PG13), no Cloud SQL specifics,
-- no custom types that a migration would have to carry. Statuses are TEXT
-- with CHECK constraints rather than ENUMs on purpose: adding a value to a
-- PG enum is a migration, adding one to a CHECK is a migration you can also
-- run backwards.
--
-- The rules that matter are enforced HERE, not in the API:
--   * the ledger is append-only, by trigger. §2.3 says balance is
--     SUM(delta) and §8/M5 says this is the audit trail a real-money phase
--     will need. A rule that lives only in application code is a rule that
--     one bad endpoint deletes.
--   * exactly one ACTIVE snapshot per robot, by partial unique index.
--   * a job describes exactly one thing, by CHECK — a FIGHT job with a
--     snapshot_id, or a VALIDATE job with a match_id, is a bug that should
--     not be representable.
-- ===========================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS schema_version (
    version     INT PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- --------------------------------------------------------------- users
CREATE TABLE users (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email         TEXT NOT NULL,
    email_lower   TEXT GENERATED ALWAYS AS (lower(email)) STORED,
    pw_hash       TEXT NOT NULL,
    display_name  TEXT NOT NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    flags         TEXT[] NOT NULL DEFAULT '{}',
    CONSTRAINT users_display_name_len CHECK (char_length(display_name) BETWEEN 2 AND 24)
);
-- Case-insensitive uniqueness: "Owen@x.com" and "owen@x.com" are one person.
CREATE UNIQUE INDEX users_email_lower_key ON users (email_lower);

-- -------------------------------------------------------------- robots
CREATE TABLE robots (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name        TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    retired     BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT robots_name_len CHECK (char_length(name) BETWEEN 1 AND 32)
);
CREATE INDEX robots_user_idx ON robots (user_id) WHERE NOT retired;

-- ----------------------------------------------------------- snapshots
-- The payload itself lives in object storage (§5.1); the row is metadata
-- the API can serve without ever parsing a build.
CREATE TABLE snapshots (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    robot_id        UUID NOT NULL REFERENCES robots(id) ON DELETE CASCADE,
    storage_url     TEXT NOT NULL,
    sha256          TEXT NOT NULL CHECK (sha256 ~ '^[0-9a-f]{64}$'),
    client_version  TEXT NOT NULL,
    status          TEXT NOT NULL DEFAULT 'PENDING'
                    CHECK (status IN ('PENDING','ACTIVE','REJECTED','SUPERSEDED')),
    -- filled by the validate job (§5.2) — NULL until the worker has spoken.
    mass_kg         INT,
    aabb_x          REAL, aabb_y REAL, aabb_z REAL,
    category        TEXT CHECK (category IS NULL OR category IN
                        ('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')),
    parts_manifest  JSONB,
    program_hash    TEXT CHECK (program_hash IS NULL OR program_hash = '' OR program_hash ~ '^[0-9a-f]{64}$'),
    fail_reasons    TEXT[],
    uploaded_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    validated_at    TIMESTAMPTZ
);
-- §1.2: one ACTIVE snapshot per robot; history is kept for audit.
CREATE UNIQUE INDEX snapshots_one_active_per_robot
    ON snapshots (robot_id) WHERE status = 'ACTIVE';
CREATE INDEX snapshots_robot_idx ON snapshots (robot_id, uploaded_at DESC);
CREATE INDEX snapshots_status_idx ON snapshots (status);

-- ------------------------------------------------------------- ratings
CREATE TABLE ratings (
    robot_id    UUID NOT NULL REFERENCES robots(id) ON DELETE CASCADE,
    category    TEXT NOT NULL CHECK (category IN ('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')),
    season_id   INT NOT NULL,
    rating      DOUBLE PRECISION NOT NULL DEFAULT 1200,
    deviation   DOUBLE PRECISION NOT NULL DEFAULT 350,
    volatility  DOUBLE PRECISION NOT NULL DEFAULT 0.06,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (robot_id, category, season_id)
);
CREATE INDEX ratings_board_idx ON ratings (season_id, category, rating DESC);

-- ------------------------------------------------------------- matches
CREATE TABLE matches (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    challenger_snapshot_id   UUID NOT NULL REFERENCES snapshots(id),
    defender_snapshot_id     UUID NOT NULL REFERENCES snapshots(id),
    category                 TEXT NOT NULL,
    gap                      INT NOT NULL DEFAULT 0 CHECK (gap >= 0),
    arena                    TEXT NOT NULL DEFAULT 'league',
    seeds                    INT[] NOT NULL,
    status                   TEXT NOT NULL DEFAULT 'QUEUED'
                             CHECK (status IN ('QUEUED','RUNNING','COMPLETE','FAILED')),
    verdict                  TEXT CHECK (verdict IS NULL OR verdict IN ('CHALLENGER','DEFENDER','DRAW')),
    replay_urls              TEXT[],
    rating_deltas            JSONB,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at             TIMESTAMPTZ,
    CONSTRAINT matches_not_self CHECK (challenger_snapshot_id <> defender_snapshot_id),
    CONSTRAINT matches_seeds_len CHECK (array_length(seeds, 1) BETWEEN 1 AND 5)
);
CREATE INDEX matches_status_idx ON matches (status, created_at);

-- ---------------------------------------------------------- match_jobs
-- §5.1: the queue is a table, claimed with SELECT … FOR UPDATE SKIP LOCKED.
-- Deliberately not Pub/Sub: transactional with the data it describes, and
-- portable to anything that speaks Postgres.
CREATE TABLE match_jobs (
    id            BIGSERIAL PRIMARY KEY,
    kind          TEXT NOT NULL CHECK (kind IN ('FIGHT','VALIDATE')),
    match_id      UUID REFERENCES matches(id) ON DELETE CASCADE,
    snapshot_id   UUID REFERENCES snapshots(id) ON DELETE CASCADE,
    status        TEXT NOT NULL DEFAULT 'READY'
                  CHECK (status IN ('READY','CLAIMED','DONE','FAILED')),
    claimed_by    TEXT,
    claimed_at    TIMESTAMPTZ,
    heartbeat_at  TIMESTAMPTZ,
    attempts      INT NOT NULL DEFAULT 0,
    last_error    TEXT,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    -- A job describes exactly one thing. A FIGHT job carrying a snapshot_id
    -- is not a state we want to be able to write down.
    CONSTRAINT match_jobs_target CHECK (
        (kind = 'FIGHT'    AND match_id IS NOT NULL AND snapshot_id IS NULL) OR
        (kind = 'VALIDATE' AND snapshot_id IS NOT NULL AND match_id IS NULL)
    ),
    CONSTRAINT match_jobs_claim CHECK (
        (status = 'CLAIMED') = (claimed_by IS NOT NULL)
    )
);
-- The claim query's index: only READY rows are ever scanned.
CREATE INDEX match_jobs_ready_idx ON match_jobs (created_at) WHERE status = 'READY';
-- The reaper's index: only CLAIMED rows are ever scanned.
CREATE INDEX match_jobs_claimed_idx ON match_jobs (heartbeat_at) WHERE status = 'CLAIMED';
-- One live job per target: a retry must reuse the row, never race a twin.
CREATE UNIQUE INDEX match_jobs_one_live_fight ON match_jobs (match_id)
    WHERE kind = 'FIGHT' AND status IN ('READY','CLAIMED');
CREATE UNIQUE INDEX match_jobs_one_live_validate ON match_jobs (snapshot_id)
    WHERE kind = 'VALIDATE' AND status IN ('READY','CLAIMED');

-- -------------------------------------------------------------- ledger
-- §2.3: the server scrap wallet. balance = SUM(delta). Append-only, and
-- enforced in the database rather than by convention.
CREATE TABLE ledger (
    id          BIGSERIAL PRIMARY KEY,
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    delta       INT NOT NULL,
    reason      TEXT NOT NULL CHECK (reason IN
                  ('SIGNING_BONUS','STAKE','STAKE_REFUND','PURSE','DEFENSE',
                   'FIRST_BLOOD','SEASON','DEPOSIT_TO_CAREER','ADJUSTMENT')),
    match_id    UUID REFERENCES matches(id),
    -- §8/M3 acceptance: "a deposited balance replayed from a tampered client
    -- is rejected (idempotency key per deposit)". The key is UNIQUE, so the
    -- replay is refused by the database, not by a code path that might be
    -- skipped.
    idem_key    TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT ledger_delta_nonzero CHECK (delta <> 0),
    -- One-way valve (§2.3): scrap may leave the wallet for the local career,
    -- and may never come back. A DEPOSIT_TO_CAREER row can only be negative.
    CONSTRAINT ledger_deposit_is_withdrawal CHECK
        (reason <> 'DEPOSIT_TO_CAREER' OR delta < 0)
);
CREATE UNIQUE INDEX ledger_idem_key ON ledger (idem_key) WHERE idem_key IS NOT NULL;
CREATE INDEX ledger_user_idx ON ledger (user_id, id);

CREATE OR REPLACE FUNCTION ledger_append_only() RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'ledger is append-only (attempted %)', TG_OP;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER ledger_no_update BEFORE UPDATE ON ledger
    FOR EACH ROW EXECUTE FUNCTION ledger_append_only();
CREATE TRIGGER ledger_no_delete BEFORE DELETE ON ledger
    FOR EACH ROW EXECUTE FUNCTION ledger_append_only();

CREATE OR REPLACE VIEW wallet_balances AS
    SELECT user_id, COALESCE(SUM(delta), 0)::BIGINT AS balance
      FROM ledger GROUP BY user_id;

-- ------------------------------------------------------------- tickets
-- §2.2: each robot may INITIATE 10 challenges per day. Defences are free.
CREATE TABLE tickets (
    robot_id  UUID NOT NULL REFERENCES robots(id) ON DELETE CASCADE,
    day       DATE NOT NULL,
    used      INT NOT NULL DEFAULT 0 CHECK (used >= 0),
    PRIMARY KEY (robot_id, day)
);

-- ------------------------------------------------------------- seasons
CREATE TABLE seasons (
    id         INT PRIMARY KEY,
    starts_at  TIMESTAMPTZ NOT NULL,
    ends_at    TIMESTAMPTZ NOT NULL,
    config     JSONB NOT NULL DEFAULT '{}',
    CONSTRAINT seasons_span CHECK (ends_at > starts_at)
);

INSERT INTO schema_version (version) VALUES (1);

COMMIT;
