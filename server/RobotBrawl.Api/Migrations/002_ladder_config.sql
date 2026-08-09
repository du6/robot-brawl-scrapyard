-- ===========================================================================
-- Robot Brawl — Multiplayer v3, M1: the ladder's tunable constants.
--
-- §2.3 is explicit: "All constants live in one server-side table
-- (LadderConfig, the CareerDB pattern applied to the backend) and are
-- tunable without a client update." Before this table the M1 escrow work had
-- nowhere to read a stake from, and the alternative was four magic numbers
-- scattered through Program.cs that a later session would have to hunt.
--
-- The FORMULAS stay in code; only their constants live here. A formula is a
-- rule ("punching up two categories pays 4x base") and belongs where it can
-- be read next to the code that applies it; a constant is a dial and belongs
-- where owen can turn it without a deploy.
--
-- Same portability rule as 001 (§7 rule 2): plain PostgreSQL only.
--
-- Values are the ones §2.3 sets, and §2.3 says to start CONSERVATIVE:
-- deposits feed the career economy, and inflating career progression from
-- the ladder is a one-way door. Buffing later is a celebrated event;
-- nerfing is a riot.
-- ===========================================================================
BEGIN;

CREATE TABLE ladder_config (
    key        TEXT PRIMARY KEY,
    value      INT  NOT NULL,
    note       TEXT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO ladder_config (key, value, note) VALUES
  ('stake_base',        50,  '2.3: challenge entry stake = stake_base * (1 + gap)'),
  ('win_purse_base',    100, '2.3: challenge win purse = win_purse_base * (1 + 0.5*gap)^2'),
  ('defense_purse',     40,  '2.3: defense win purse, flat - passive income for being worth challenging'),
  ('first_blood_bonus', 50,  '2.3: bonus vs a robot never beaten before (not yet applied; needs match history)');

INSERT INTO schema_version (version) VALUES (2);

COMMIT;
