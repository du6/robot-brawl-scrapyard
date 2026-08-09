# The validate-result livelock — fixed 2026-08-09

**`HANDOVER_TO_CLI.md` §5.1, the first item in the queue. Done.**

`api_smoke.sh`: **48 pass, 0 fail, 0 skipped** (was 43/3/0).
`sql_bench.sh`: **34/34**, unchanged.
`qa_api_server.log`: **10 lines, 0 error lines, 0 occurrences of `23514`**
(was 84 lines, 14 error lines).

## The prediction, and where it was wrong

Written before the fix, per hard rule 6:

> `api_smoke` goes 43/3 → **46 pass / 0 fail**, `sql_bench` stays 34/34,
> and `qa_api_server.log` returns to **0 error lines**.

**Actual: 48 pass / 0 fail.** The "0 fail" and "0 error lines" halves held
exactly. The count was wrong because I wrote the prediction before deciding
to add the two livelock checks below — 46 was right for the bench as it
stood, and the extra two are the checks that measure the actual bug. The
prediction is recorded as made rather than quietly corrected.

## What was wrong

Posting `"category":""` or `"category":"BANTAM"` to
`/v1/worker/jobs/{id}/validate-result` reached the `UPDATE snapshots …`
with a value `snapshots_category_check` forbids. Postgres raised `23514`,
nothing caught it, and the request returned **500 with a stack trace**.

The 500 was the visible half. **The damage was the invisible half:**
`CompleteAsync` sits after the write, so it never ran. The job stayed
`CLAIMED` with no worker touching it, the reaper returned it to the queue
on the visibility timeout, and the next worker picked it up and failed on
it identically. **Forever.** A malformed result did not fail loudly — it
consumed a worker slot indefinitely.

`""` is not hypothetical. Unity's `JsonUtility` serialises a null string as
`""`, so it is exactly what a worker written the obvious way emits. The
shipped worker hand-builds its JSON to send a bare `null` and asserts the
literal bytes, but that is one client being careful, and the API must not
depend on it.

A second hole, same endpoint: **`legal:true` with `category:null` was
accepted.** `ratings.category` is NOT NULL, so that snapshot went ACTIVE
and could never be placed on a ladder — an unrateable robot, stored
successfully.

## What changed

**Validate before writing, rather than catching `23514` afterwards.** A
pre-write check can say *why* in words a worker author can act on; a caught
constraint violation can only report the constraint's name. `Categories`
now lives beside the handler in `Program.cs`, mirroring `001_init.sql`.

**`Sql/fail_job.sql` + `JobQueue.FailAsync()`** — the deliberate
counterpart to `complete_job.sql`. Three decisions worth keeping:

- **`FAILED`, not `READY`.** The reaper's timeout path returns jobs to the
  queue because a worker may have died mid-flight and a retry may succeed.
  A payload the schema refuses is deterministic: a retry produces the same
  refusal. Returning it to the queue *is* the livelock.
- **Same ownership rule as `complete_job.sql`** — `WHERE id = $1 AND
  claimed_by = $2 AND status = 'CLAIMED'`. A late malformed result from a
  worker whose job was already reclaimed updates nothing and gets the
  existing 409, so it cannot knock over work someone else legitimately
  holds.
- **`claimed_by` must be cleared.** `match_jobs_claim` asserts
  `(status = 'CLAIMED') = (claimed_by IS NOT NULL)`, so moving to `FAILED`
  while leaving the claim in place violates the constraint — the fix would
  have raised its own unhandled exception.

The endpoint now returns **400** with the reason and `jobStatus: "FAILED"`.

## The three refusals, as a worker author sees them

Read out of `match_jobs.last_error` after the run:

```
15 | FAILED | category was the empty string; send a bare JSON null for "no category"
              (Unity's JsonUtility serialises a null string as "" — hand-build this field)
16 | FAILED | category 'BANTAM' is not one of FEATHER, LIGHT, MIDDLE, HEAVY, SUPER
17 | FAILED | a legal robot must carry a weight category; ratings.category is NOT NULL,
              so a legal snapshot without one can never be rated
```

The first one names the trap that causes it. That is the point of
validating before the write rather than after.

## The checks that prove it

The three failing checks from 2026-08-09 went green **without being
touched** — they were written against the contract rather than against the
behaviour, so the fix is what moved them.

Two checks were **added**, because refusing the result is only half the fix
and nothing measured the other half:

```
PASS  the empty-string category is refused cleanly (HTTP 400)
PASS  …and the job is marked FAILED, so the reaper cannot hand it out again
PASS  …carrying a last_error a human can act on
```

Asserting merely "not READY" would have passed while the job sat `CLAIMED`
and leaked a worker slot a slower way, so the check names `FAILED`
explicitly.

**Queue state after a full run — the end-to-end evidence:**

```
DONE   | 5
FAILED | 3
```

Zero `READY`, zero `CLAIMED`. Nothing is spinning.

## What this does NOT do

- **The snapshot stays `PENDING`.** A refused validate-result retires the
  *job*; it does not mark the snapshot `REJECTED`. So a robot whose worker
  sends a malformed category never finishes validating — a stuck state, but
  a **visible** one (a `FAILED` job with a readable `last_error`) rather
  than an invisible infinite retry. Deciding whether the snapshot should go
  `REJECTED` is a product question, not a bug fix, and it was out of scope
  for §5.1.
- **No client changed.** The shipped worker already sends a bare `null`.
  This hardens the API against the next client, which is the stated reason
  the check exists.
