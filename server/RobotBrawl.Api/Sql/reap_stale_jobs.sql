-- The visibility timeout (§5.3): a job unheartbeated for $1 seconds goes back
-- to the queue, unless it has already burned $2 attempts, in which case it
-- FAILS and is flagged for inspection rather than looping forever.
-- $1 = stale seconds, $2 = max attempts
--
-- A dead VALIDATE job also REJECTS its snapshot, and that half is not a
-- nicety. Without it the job stops but the snapshot stays PENDING forever:
-- the player uploaded a robot, the ladder shows it as still being checked,
-- and nothing will ever move it — no error, no reason, no way to tell the
-- difference from a queue that is merely busy. This was found in production
-- on 08-09, where snapshots written by a revision using per-instance disk
-- were left pointing at storage that no longer existed. Their jobs could
-- never be worked, and the robots would have sat PENDING indefinitely.
--
-- Only PENDING snapshots are touched. An ACTIVE one has already passed
-- validation, and a late-dying job from an earlier attempt must not revoke
-- it; SUPERSEDED and REJECTED are already terminal.
WITH reaped AS (
    UPDATE match_jobs
       SET status      = CASE WHEN attempts >= $2 THEN 'FAILED' ELSE 'READY' END,
           claimed_by  = NULL,
           claimed_at  = NULL,
           heartbeat_at = NULL,
           last_error  = 'heartbeat timeout after ' || attempts || ' attempt(s)'
     WHERE status = 'CLAIMED'
       AND heartbeat_at < now() - make_interval(secs => $1)
    RETURNING id, kind, snapshot_id, status, attempts
), rejected AS (
    UPDATE snapshots s
       SET status       = 'REJECTED',
           validated_at = now(),
           fail_reasons = ARRAY[
             'validation gave up after ' || r.attempts ||
             ' attempt(s); the worker never reported a result']
      FROM reaped r
     WHERE r.kind = 'VALIDATE'
       AND r.status = 'FAILED'
       AND s.id = r.snapshot_id
       AND s.status = 'PENDING'
    RETURNING s.id
)
SELECT id, status, attempts FROM reaped
