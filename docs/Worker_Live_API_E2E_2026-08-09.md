# The VALIDATE worker met the live API — 2026-08-09

**`HANDOVER_TO_CLI.md` §5.3. Never done before today.** The first moment M1
has been proven end to end rather than half at a time.

**Result: 3 jobs handled, 3 posts succeeded, 0 failed, 0 left for retry.**
A legal robot went **ACTIVE** with `category=FEATHER`, `mass=799`, 16 parts
and no fail reasons. Career save byte-identical, mtime unchanged.

Evidence: `server/qa_worker_e2e.txt`.

## The prediction, and it was wrong in a useful way

Written before starting, per hard rule 6:

> The worker will claim and fetch fine, but I expect a real chance of
> failure at the **post** step — `HttpWorkerTransport` has never made a
> request, and the wire format was only ever proven against a stub. If it
> fails, the most likely cause is the JSON the API rejects or a header the
> stub never checked.

**It did not fail. The post worked on the first attempt**, and so did every
other step. `WorkerBench`'s 39/39 against a stub turned out to be a genuine
predictor of the live contract — the hand-built JSON, the
`X-Worker-Key` header, the 204-means-empty-queue rule and the sha256
verification all held against a real server. That is a good result for the
bench, and worth recording precisely because the prediction expected
otherwise.

What the prediction got right in spirit: the interesting failures were not
in the transport at all. They were in **what a payload has to be** — see
below.

## What was actually run

The setup deliberately differs from the handover's suggestion in one way:
it does **not** use `run_local.sh`, because that script boots the API, runs
the benches and stops it. The worker needs the server up while the editor
drives it, so the API was started by hand with the same environment
`run_local.sh` uses.

1. Dev DB truncated; API started with `dotnet run`.
2. Register → `POST /v1/robots` → `POST /v1/snapshots` through the real
   endpoints, exactly as a client would. Job `1 VALIDATE READY` appeared.
3. Unity play mode; `ValidateWorkerLoop.RunOnce(new HttpWorkerTransport(
   "http://localhost:5000", …), bm, null)`.
4. Poll the static counters — `JobsHandled`, `PostsSucceeded`,
   `PostsFailed`, `LastPostedJson` — across separate `Unity_RunCommand`
   calls, since the domain reload rules out holding a reference.

## Three payloads, three verdicts — and why that mattered

The first run **worked** and still told me almost nothing, because the
verdict was `legal:false — "payload is not a readable snapshot"`. The
transport was fine; my *payload* was a synthetic envelope of the shape
`api_smoke` uses for HTTP-level checks, which the real `RobotSnapshot`
parser correctly refuses.

That is the trap worth writing down: **an end-to-end test can pass its
plumbing and prove nothing about its subject.** A green round trip carrying
a rejection is not the happy path, and it would have been easy to report
"§5.3 done" on the strength of `postsOK=1`.

So it was run three times, with escalating realism:

| payload | verdict | stored |
|---|---|---|
| synthetic `api_smoke`-shaped envelope | REJECTED | `cat=NULL mass=0 parts=0`, *"payload is not a readable snapshot"* |
| `RobotSnapshot.Export` of the live bay (a bare core) | REJECTED | `cat=FEATHER mass=73 parts=1`, *"Needs at least 1 wheel."* |
| `spinner1`, one of owen's own stable robots, 16 parts | **ACTIVE** | `cat=FEATHER mass=799 parts=16`, no fail reasons |

The middle row is quietly the most valuable. **"Needs at least 1 wheel." is
the builder's own message string**, carried intact from the worker's
judgement into `snapshots.fail_reasons` — which is one of M1's stated
accept criteria (*"reject a program-gate-failing snapshot with the same
message strings the builder shows"*), demonstrated rather than assumed.

## How the legal robot was obtained without touching owen's bay

Driving `BuilderManager` to construct a legal robot would have meant
mutating owner state to run a test. Instead:

1. Read a build string out of the career save — **read-only** —
   `stable[0].snapshot`, `spinner1`, 811 chars, 4 wheels.
2. `RobotSnapshot.ExportRaw(name, buildText, "")`, which builds an envelope
   from raw text and **never touches the bay at all**.

`ExportRaw` is the right tool for any future server-side test that needs a
realistic robot. `Export(bm, …)` reads the live bay and couples your test to
whatever owen last built — which is exactly why run 2 produced a bare core.

## What this proves

- `HttpWorkerTransport` works — claim, fetch, post. First requests it has
  ever made.
- The blob URL contract works: the API hands back `file:///tmp/rb-blobs/…`
  and `UnityWebRequest.Get` resolves it. **In production this becomes a
  signed cloud URL and is unproven there.**
- The sha256 in the claim matches the fetched bytes; judging was reached
  every time.
- The API accepts the worker's hand-built JSON — no 4xx, no 5xx, across
  three different result shapes including `category:null` with
  `legal:false`.
- Jobs are completed, not left to spin: `DONE 3`, zero `READY`, zero
  `CLAIMED`, zero `FAILED`.
- Server log clean: 10 lines, **0 errors**.

## What this does NOT prove

- **This is not a bench.** It is a hand-run sequence of editor commands. It
  will not catch a regression tomorrow. Turning it into one is real work:
  it needs the API running, which no existing bench assumes.
- **Nothing was killed mid-flight.** M1's accept clause *"kill a worker
  mid-fight; job retries and completes"* is untested — the reaper's timeout
  path is covered by `sql_bench` at the SQL level only.
- **`RunForever` was never used** — only `RunOnce`, three times. The idle
  poll loop, and how it behaves against an empty queue for a sustained
  period, is unexercised.
- **Localhost only.** No GCP, no Docker, no signed URLs, no network
  latency, no TLS.
- **The over-cap rejection is still untested.** Only the wheel gate fired;
  the mass-cap path in the accept criteria has not been driven.

## Editor discipline this adds

- `ValidateWorkerLoop`'s state is in **statics** (`JobsHandled`,
  `LastPostedJson`, …), which is what makes it pollable across separate
  bridge calls at all. An instance-only design would not have been drivable
  this way.
- `RunOnce` handles exactly one job and returns. Three jobs meant three
  calls — do not expect it to drain a queue.
- Start the API **outside** `run_local.sh` for anything interactive; that
  script stops the server as its last act.
