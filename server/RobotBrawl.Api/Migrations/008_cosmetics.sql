-- ===========================================================================
-- Robot Brawl — §M3 scrap sinks: "cosmetic robot plates/titles v1 — cheap,
-- no balance impact".
--
-- WHY A SINK AT ALL. Every faucet in this economy is live - signing bonus,
-- purses, defense purses, season payouts - and until now the only way scrap
-- could LEAVE a wallet was a stake you usually got back, or a one-way deposit
-- into the career save. An economy with faucets and no sinks inflates until
-- the numbers stop meaning anything.
--
-- "NO BALANCE IMPACT" IS THE WHOLE POINT and it is enforced by there being
-- nowhere to put a stat: the catalogue has a name, a kind and a price, and no
-- column that could ever affect a fight. A cosmetic that did something would
-- be a paid advantage, which is a different game.
--
-- The ledger's reason CHECK has to widen for this. That constraint is the
-- ledger's spine - it is what makes "every credit is a faucet the design doc
-- names" checkable - so widening it is deliberate and the new value is a
-- DEBIT-only reason, guarded below the same way DEPOSIT_TO_CAREER is.
-- ===========================================================================
BEGIN;

ALTER TABLE ledger DROP CONSTRAINT ledger_reason_check;
ALTER TABLE ledger ADD CONSTRAINT ledger_reason_check CHECK (reason IN
    ('SIGNING_BONUS','STAKE','STAKE_REFUND','PURSE','DEFENSE',
     'FIRST_BLOOD','SEASON','DEPOSIT_TO_CAREER','ADJUSTMENT','COSMETIC'));

-- A sink can only ever take scrap out. Same shape as
-- ledger_deposit_is_withdrawal: the rule lives in the database so no endpoint
-- can turn a shop into a faucet.
ALTER TABLE ledger ADD CONSTRAINT ledger_cosmetic_is_purchase CHECK
    (reason <> 'COSMETIC' OR delta < 0);

CREATE TABLE cosmetics (
    id      TEXT PRIMARY KEY,
    kind    TEXT NOT NULL CHECK (kind IN ('PLATE','TITLE')),
    name    TEXT NOT NULL,
    price   INT  NOT NULL CHECK (price > 0)
    -- No stat column, on purpose. See the header.
);

CREATE TABLE user_cosmetics (
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    cosmetic_id TEXT NOT NULL REFERENCES cosmetics(id),
    acquired_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, cosmetic_id)   -- buying twice is not a thing
);

-- Equipped per ROBOT, because §M3 calls them "robot plates/titles" and a
-- stable's robots should be able to look different from each other.
ALTER TABLE robots ADD COLUMN plate_id TEXT REFERENCES cosmetics(id);
ALTER TABLE robots ADD COLUMN title_id TEXT REFERENCES cosmetics(id);

-- v1 catalogue. Cheap on purpose: §2.3 says the faucets start conservative,
-- so the sinks have to be reachable or nobody ever spends and the sink does
-- not exist in practice. A signing bonus is 500; the cheapest plate is 60.
INSERT INTO cosmetics (id, kind, name, price) VALUES
    ('plate_brushed',  'PLATE', 'Brushed Steel',      60),
    ('plate_hazard',   'PLATE', 'Hazard Stripe',     120),
    ('plate_champion', 'PLATE', 'Champion Gold',     400),
    ('title_scrapper', 'TITLE', 'Scrapper',           60),
    ('title_giantkill','TITLE', 'Giant-Killer',      250),
    ('title_ironclad', 'TITLE', 'Ironclad',          250);

INSERT INTO schema_version (version) VALUES (8);

COMMIT;
