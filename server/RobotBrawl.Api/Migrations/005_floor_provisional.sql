-- ===========================================================================
-- Robot Brawl — the defense loss floor protects EARNED ranks only.
--
-- §2.1 and §2.2 pull against each other and the design doc never reconciled
-- them. §2.1 gives a new robot deviation 350 so placement converges fast;
-- §2.2 caps its daily fall at 75. Measured against the shipped Glicko2.cs, a
-- single loss at placement deviation is worth 162.3 points -- more than
-- double the whole day's allowance:
--
--     RD 350 -> drop 162.3      RD 200 -> drop  67.2
--     RD 260 -> drop 104.2      RD 150 -> drop  40.2
--
-- So a new robot that loses badly sits ABOVE its true rating, which makes it
-- a more attractive target, which it then cannot fall away from until its
-- deviation has tightened. The ladder actively misleads challengers about how
-- hard a fight is, for days.
--
-- RESOLUTION: the floor is grouped under "Defender protection" with tickets
-- and the taper. Its job is to stop a dogpile tanking an ESTABLISHED rank in
-- a day. A placement rating is not established -- it is a guess with a high
-- deviation attached, and the entire point of that deviation is "we do not
-- know yet, move me fast". Protecting a rank that does not exist yet is a
-- category error.
--
-- WHY 200, AND WHY THE CLIFF IS NEARLY FREE: at RD 200 the natural drop
-- (67.2) has already fallen below the floor (75) on its own, so the
-- exemption switches off exactly where it stops mattering. A robot crossing
-- the boundary sees almost no change in behaviour. 200 was not chosen to
-- make that true; it is where the curve put it. Deviation only decreases
-- with play, so the boundary is crossed once, downward, and cannot be farmed
-- by oscillating across it.
--
-- It is also the SAME threshold the leaderboard already calls `provisional`,
-- so this is one concept in two places rather than a second unexplained
-- constant: "provisional ranks are not floor-protected" is a sentence a
-- player has already read.
--
-- What it costs: a new robot can be knocked down hard on day one. That is
-- correct rather than harmful -- tickets cap one challenger at 10/day and the
-- taper kills gains after 3 wins from the same opponent, so knocking a
-- provisional robot a long way needs MANY DISTINCT attackers, and many
-- distinct opponents beating you is information, not griefing.
-- ===========================================================================
BEGIN;

INSERT INTO ladder_config (key, value, note) VALUES
  ('floor_applies_below_deviation', 200,
   '2.2 + 2.1: the defense loss floor only protects a rating whose deviation is BELOW this - an earned rank. At or above it the rating is provisional and takes its full natural movement, which is what the high placement deviation is for. Same threshold the leaderboard calls provisional.');

INSERT INTO schema_version (version) VALUES (5);

COMMIT;
