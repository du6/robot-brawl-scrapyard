-- ===========================================================================
-- Robot Brawl — THE ARENA STOPS PAYING PER MATCH (owen, 2026-08-19:
-- "users can abuse arena rewards by unloading junk robots. let's remove per
-- game rewards, but give rewards per session. top 10 users for each season
-- get rewards.")
--
-- THE ABUSE, stated exactly, because a fix nobody can check against the
-- exploit is a fix nobody can trust. Nothing stopped a user challenging
-- ROBOTS THEY THEMSELVES OWNED — the only self-challenge guard was
-- `ch.RobotId == df.RobotId`, which compares ROBOTS, not OWNERS. So:
--
--   1. enlist a strong robot and a junk one;
--   2. challenge the junk one with the strong one;
--   3. win. The stake comes back (escrow, not a fee) and PURSE pays
--      win_purse_base = 100 for a fight that cost nothing and risked nothing.
--
-- §2.2's repeat-opponent taper bounds step 3 against ONE opponent. It does
-- not bound the number of opponents, and an account may enlist as many
-- robots as it likes — so the taper is defeated by unloading more junk.
-- That is the reported abuse, and it is a genuine faucet: scrap minted out
-- of a fight with no counterparty.
--
-- THE FIX IS STRUCTURAL, not a threshold. Per-match arena payment is
-- abolished — there is no longer any amount a match can pay, so there is
-- nothing for a junk robot to farm. The arena's whole faucet becomes ONE
-- payout at season end, to the top 10 USERS, which is a fixed and knowable
-- number per season rather than a rate per fight.
--
-- WHAT MOVES, and why each is DELETE vs UPDATE:
--
--   * win_purse_base / defense_purse are DELETED, not set to 0. These two
--     keys ARE the exploit. ladder_config is deliberately tunable without a
--     deploy (§2.3), which means a zeroed key is a live lever that
--     re-opens a known hole with one UPDATE — the exact half-resurrection
--     012_no_entry_fees dropped a COLUMN to prevent. The code that read
--     them is deleted in the same commit; a key with no reader is litter.
--
--   * stake_base is UPDATED to 0, and stays. A stake is not a reward, it is
--     escrow, and with no purse to win a stake would be a pure penalty owen
--     did not ask for: every challenge EV-negative, so nobody challenges
--     and the ladder that the season prize is meant to rank goes quiet.
--     Zero here means "free to enter", which is exactly what 012 already
--     decided for the league. This one IS a lever and 0 is a legitimate
--     setting for it, so it stays tunable.
--
--   * season_payout_places 3 -> 10, and its MEANING changes: it used to
--     count places PER CATEGORY and pay a ROBOT's owner; it now counts
--     USERS across the whole ladder. Read the endpoint, not this number,
--     for what it ranks.
--
--   * season_payout_base 300 -> 1000, so base/rank over ten places pays
--     1000/500/333/250/200/166/142/125/111/100 = 2927 scrap per season.
--     Chosen to land near the OLD theoretical ceiling (3 places x 5
--     categories x (300+150+100) = 2750) so the total faucet is roughly
--     preserved while its shape changes completely. Unlike that ceiling
--     this one is exact: at most ten users are paid, so the arena's entire
--     per-season exposure is 2927 and cannot be farmed upward.
--
--   * season_badge_places is NEW and takes over what season_payout_places
--     used to mean. Badges are the PERMANENT per-category record a
--     scouting card shows two seasons later — they are glory, not money,
--     and a junk robot cannot abuse an honour. Splitting them from the
--     payout is what lets the money go per-user without deleting the
--     per-category history.
-- ===========================================================================
BEGIN;

DELETE FROM ladder_config WHERE key IN ('win_purse_base', 'defense_purse');

UPDATE ladder_config
   SET value = 0,
       note  = '2.3: challenge entry stake = stake_base * (1 + gap). ZERO since 2026-08-19: the arena pays at season end, so a stake would be a cost with nothing to win. The lever stays; the purse it used to pay for is gone.',
       updated_at = now()
 WHERE key = 'stake_base';

UPDATE ladder_config
   SET value = 10,
       note  = '2.4: how many USERS are paid at season end. Was 3 places PER CATEGORY paying a robot owner; since 2026-08-19 it is the top 10 users of the whole ladder, ranked by the best rating they earned in it.',
       updated_at = now()
 WHERE key = 'season_payout_places';

UPDATE ladder_config
   SET value = 1000,
       note  = '2.4: scrap to the top-ranked USER at season end; place N gets base/N. Ten places = 2927 total, the arena''s entire faucet for a season.',
       updated_at = now()
 WHERE key = 'season_payout_base';

-- ⚠ WHO IS ELIGIBLE, and why this is NOT the board's "provisional" line.
-- A rating has to have been EARNED to rank, or an account walks into the paid
-- places by uploading a robot and never fighting — the same abuse in a
-- different coat. Deviation is the honest instrument for that: enlistment
-- assigns exactly 1200 at deviation 350 and only real fights move it down.
--
-- 350 EXCLUSIVE therefore means "this robot has fought at least once".
--
-- The obvious tighter choice was the leaderboard's own provisional line
-- (deviation > 200), and it was tried and MEASURED FIRST: on the dev ladder
-- after a full api_smoke run the tightest rating in the season sat at RD 258,
-- so a 200 gate made every player ineligible and the season paid NOBODY.
-- Glicko-2 deviation falls slowly; 200 is a many-fight bar, not a
-- did-you-turn-up bar. A prize that pays nobody is not a stricter prize, it
-- is an absent one. Tighten this if the ladder ever gets crowded enough to
-- need it — that is what makes it a config key and not a constant.
INSERT INTO ladder_config (key, value, note) VALUES
  ('season_rank_max_deviation', 350,
   '2.4: a rating ranks for the season payout only if deviation is STRICTLY BELOW this. 350 is the enlistment value, so this means "has fought at least once". Deliberately looser than the board''s provisional line (200), which was measured to pay nobody.'),
  ('season_badge_places', 3,
   '2.4: how many places PER CATEGORY get a permanent season badge. This is what season_payout_places used to mean. Badges are a record, not money — they are deliberately still per-category and per-robot.')
ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, note = EXCLUDED.note, updated_at = now();

INSERT INTO schema_version (version) VALUES (16);

COMMIT;
