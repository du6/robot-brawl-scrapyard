-- $1 = job id, $2 = worker id, $3 = reason. The deliberate counterpart to
-- complete_job.sql: the worker reached a verdict the API cannot store, and
-- retrying is pointless because the payload is deterministic.
--
-- 2026-08-09. Before this existed, a validate-result carrying a category the
-- CHECK forbids raised an unhandled 23514, returned 500, and never completed
-- the job — so the reaper returned it to the queue on the visibility timeout
-- and a worker picked it up to fail identically, forever. A malformed result
-- did not fail loudly; it livelocked a worker slot.
--
-- FAILED rather than READY on purpose: attempts is not the right gate here.
-- A heartbeat timeout is worth retrying because the worker may have died
-- mid-flight; a payload the schema refuses will be refused every time.
--
-- Same ownership rule as complete_job.sql — a result from a worker whose job
-- was already reclaimed updates nothing and returns no row, so a late failure
-- cannot knock over a job someone else is legitimately working.
--
-- claimed_by MUST be cleared: match_jobs_claim asserts
--   (status = 'CLAIMED') = (claimed_by IS NOT NULL)
-- so leaving the claim in place while moving to FAILED violates it.
UPDATE match_jobs
   SET status = 'FAILED', claimed_by = NULL, claimed_at = NULL, heartbeat_at = NULL,
       last_error = $3
 WHERE id = $1 AND claimed_by = $2 AND status = 'CLAIMED'
RETURNING id
