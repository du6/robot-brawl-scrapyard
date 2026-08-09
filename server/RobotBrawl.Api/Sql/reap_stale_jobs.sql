-- The visibility timeout (§5.3): a job unheartbeated for $1 seconds goes back
-- to the queue, unless it has already burned $2 attempts, in which case it
-- FAILS and is flagged for inspection rather than looping forever.
-- $1 = stale seconds, $2 = max attempts
UPDATE match_jobs
   SET status      = CASE WHEN attempts >= $2 THEN 'FAILED' ELSE 'READY' END,
       claimed_by  = NULL,
       claimed_at  = NULL,
       heartbeat_at = NULL,
       last_error  = 'heartbeat timeout after ' || attempts || ' attempt(s)'
 WHERE status = 'CLAIMED'
   AND heartbeat_at < now() - make_interval(secs => $1)
RETURNING id, status, attempts
