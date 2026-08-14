-- ===========================================================================
-- Robot Brawl — the career economy joins the server ledger.
-- docs/Server_Economy_Design_2026-08-13.md, decided by owen the same day:
-- scrap will eventually be sold for real money, and you cannot sell for
-- money what the client is allowed to mint for free. So the wallet that
-- already existed for the LADDER (001, §2.3: balance = SUM(ledger.delta),
-- append-only, idem_key UNIQUE) becomes the wallet for EVERYTHING, and the
-- career's earn/spend paths become ledger rows like any other.
--
-- WHAT THIS SUPERSEDES, ON PURPOSE. 001's one-way valve (DEPOSIT_TO_CAREER,
-- "local scrap can never enter the ladder") was the right rule when the
-- career was client-authoritative. Under this design there is no
-- client-authoritative scrap left to quarantine: the career reads the same
-- wallet the ladder pays into. The valve's constraint stays in place and its
-- reason stays legal — existing rows are history and the ledger is
-- append-only — but no new code path should write it once the client's
-- wallet integration lands.
--
-- THE LEAGUE CEILING (design doc §5, measured): purses pay on the FIRST win
-- only, enforced here by idem_key uniqueness with a SERVER-constructed key —
-- total league purse credit per account is hard-capped at the sum of this
-- table (7,125) times the 1.6 underdog cap, plus 14×100 damage caps and
-- 14×75 first-win bonuses = 13,850 scrap, ever. Consolation is the one
-- repeatable credit and is bounded in the endpoint (pre-first-win only,
-- at most N per contest); the direction guards below make sure no reason
-- can flow the wrong way regardless of endpoint bugs.
-- ===========================================================================
BEGIN;

-- The 008 pattern: the reason CHECK is the ledger's spine, so widening it is
-- explicit and each new value gets a direction guard.
ALTER TABLE ledger DROP CONSTRAINT ledger_reason_check;
ALTER TABLE ledger ADD CONSTRAINT ledger_reason_check CHECK (reason IN
    ('SIGNING_BONUS','STAKE','STAKE_REFUND','PURSE','DEFENSE',
     'FIRST_BLOOD','SEASON','DEPOSIT_TO_CAREER','ADJUSTMENT','COSMETIC',
     'LEAGUE_PURSE','LEAGUE_CONSOLATION','LEAGUE_ENTRY',
     'SHOP_BUY','SHOP_SELL','IAP','IAP_REFUND'));

ALTER TABLE ledger ADD CONSTRAINT ledger_league_purse_is_credit
    CHECK (reason <> 'LEAGUE_PURSE' OR delta > 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_consolation_is_credit
    CHECK (reason <> 'LEAGUE_CONSOLATION' OR delta > 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_entry_is_debit
    CHECK (reason <> 'LEAGUE_ENTRY' OR delta < 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_shop_buy_is_debit
    CHECK (reason <> 'SHOP_BUY' OR delta < 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_shop_sell_is_credit
    CHECK (reason <> 'SHOP_SELL' OR delta > 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_iap_is_credit
    CHECK (reason <> 'IAP' OR delta > 0);
ALTER TABLE ledger ADD CONSTRAINT ledger_iap_refund_is_debit
    CHECK (reason <> 'IAP_REFUND' OR delta < 0);

-- What a row is ABOUT (a contest id, a "part:mat"), so bounds like "at most
-- N consolations per contest" are one indexed COUNT instead of key-prefix
-- string matching. Nullable: ladder rows have no ref.
ALTER TABLE ledger ADD COLUMN ref TEXT;
CREATE INDEX ledger_user_reason_ref_idx ON ledger (user_id, reason, ref);

-- The purse table, hand-ported from Career.cs (14 contests, sum 7,125 —
-- api_smoke asserts the sum so a client-side rebalance that forgets this
-- table fails a bench instead of paying wrong money). Purses are per-contest
-- facts, not config: the client's Career.cs remains the display copy, THIS
-- is what pays.
CREATE TABLE league_contests (
    id         TEXT PRIMARY KEY,
    purse      INT NOT NULL CHECK (purse > 0),
    entry_fee  INT NOT NULL CHECK (entry_fee >= 0)
);
INSERT INTO league_contests (id, purse, entry_fee) VALUES
    ('L1C1',  125,   0),
    ('L1C2',  150,   0),
    ('L2C1',  200,   0),
    ('L2C2',  225,   0),
    ('L2C3',  250,   0),
    ('L3C1',  350,  50),
    ('L3C2',  400,  50),
    ('L3C3',  450,  50),
    ('L3C4',  475,  50),
    ('L4C1',  600, 100),
    ('L4C2',  700, 100),
    ('L4C3',  800, 100),
    ('L4C4',  900, 100),
    ('L5C1', 1500, 200);

-- Part ownership, server-truth. count >= 0 is the database refusing to sell
-- what you do not own, the same way the direction guards refuse a backwards
-- reason. (users are never row-deleted — /v1/account tombstones — so the FK
-- action is belt-and-braces, not policy.)
CREATE TABLE inventory (
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    part_id    TEXT NOT NULL,
    mat        TEXT NOT NULL,
    count      INT  NOT NULL CHECK (count >= 0),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, part_id, mat)
);

-- Shop prices, server-truth. SEEDED BY 011, which is GENERATED from the
-- editor's real part defs (Career.PartPrice, TechFloor included) — never
-- hand-typed, so a part rebalance regenerates one file instead of drifting
-- two price tables apart. Sell price is derived in code (floor half), not
-- stored, so it cannot drift either.
CREATE TABLE part_prices (
    part_id TEXT NOT NULL,
    mat     TEXT NOT NULL,
    price   INT  NOT NULL CHECK (price > 0),
    PRIMARY KEY (part_id, mat)
);

-- IAP receipts: one row per Apple transaction, PRIMARY KEY = the Apple
-- transaction id, so crediting twice is refused by Postgres. RESTRICT like
-- the ledger: this is an audit trail with money on it.
CREATE TABLE iap_receipts (
    transaction_id TEXT PRIMARY KEY,
    user_id        UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    product        TEXT NOT NULL,
    scrap          INT  NOT NULL CHECK (scrap > 0),
    status         TEXT NOT NULL CHECK (status IN ('VERIFIED','REFUNDED')),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO schema_version (version) VALUES (10);

COMMIT;
