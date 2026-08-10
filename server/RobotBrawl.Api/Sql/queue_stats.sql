-- The three numbers §M4 asks to alert on — queue depth, job failure rate and
-- worker heartbeat age — plus the one a player actually feels.
--
-- One query rather than four, because these are read together on a timer and
-- four round trips on a db-f1-micro every 30s is a cost with no benefit.
--
-- oldest_ready_age_s is the honest measure of "queue depth". A depth of 40 is
-- fine if it drains in a minute and a depth of 2 is an outage if those two
-- have been sitting for an hour. Alert on the age, not the count.
--
-- pending_snapshots is not a queue statistic. It is the player-visible
-- symptom: a robot uploaded and never resolved. It is here because the two
-- can disagree — a queue can be empty and clean while snapshots sit PENDING
-- with no job at all, which is a bug no job-based metric would ever show.
SELECT
  (SELECT count(*) FROM match_jobs WHERE status = 'READY')                     AS ready,
  (SELECT count(*) FROM match_jobs WHERE status = 'CLAIMED')                   AS claimed,
  (SELECT count(*) FROM match_jobs WHERE status = 'FAILED')                    AS failed,
  (SELECT count(*) FROM match_jobs WHERE status = 'DONE')                      AS done,
  COALESCE((SELECT EXTRACT(EPOCH FROM now() - min(created_at))::int
              FROM match_jobs WHERE status = 'READY'), 0)                      AS oldest_ready_age_s,
  COALESCE((SELECT EXTRACT(EPOCH FROM now() - min(heartbeat_at))::int
              FROM match_jobs WHERE status = 'CLAIMED'
               AND heartbeat_at IS NOT NULL), 0)                               AS oldest_heartbeat_age_s,
  (SELECT count(*) FROM snapshots WHERE status = 'PENDING')                    AS pending_snapshots,
  COALESCE((SELECT EXTRACT(EPOCH FROM now() - min(uploaded_at))::int
              FROM snapshots WHERE status = 'PENDING'), 0)                     AS oldest_pending_snapshot_age_s
