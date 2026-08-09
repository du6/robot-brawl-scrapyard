# M1 — the VALIDATE worker, shipped and benched — 2026-08-09

The last piece of M1's Unity half. Built, and proven at **WorkerBench
39/39** with no API, no Postgres, no Docker and no network. Career save
byte-identical; editor left out of play mode.

The loop §5.2 asked for: claim → fetch the payload from storage → `Open()`
(hash verified) → load into the builder → `Describe()` → category → POST
validate-result, heartbeating throughout.

**Two of the three traps below would have shipped as bugs if the worker had
been written against the design doc instead of against the other side of
the seam.** That is now the third consecutive M1 session where the pieces
were individually right and the join was where the work was.

## ⚠ TRAP 1 — `payloadSha256` is not the hash of the bytes you fetch

`POST /v1/snapshots` stores the **whole envelope** as the blob, but records
`snapshots.sha256` as the envelope's own `sha256` **field** — which is
`SHA256(env.payload)`, the inner string:

```
blob bytes     = {"kind":…,"payload":"{…}","sha256":"abc…"}
job.PayloadSha = abc…  =  SHA256(env.payload)
SHA256(blob)   = something else entirely, and nothing records it
```

So the obvious verification — hash what you downloaded, compare to the
claim — **fails on every legitimate snapshot.** A worker written that way
would have rejected 100% of real uploads, and the failure would have looked
like a corruption problem rather than a contract misreading.

The correct check turned out to be **two separate claims, and it needs
both**:

- **INTEGRITY** — `RobotSnapshot.Open()` already refuses a payload whose
  hash does not match the envelope's own `sha256`. Free, and it had been
  unreachable since M0 because no consumer existed to call it.
- **IDENTITY** — `env.sha256 == job.payloadSha256`. The blob really is the
  snapshot this job names, not a different valid one swapped into storage
  at that key.

Together: `SHA256(payload)` equals what the database recorded at upload.
Integrity alone is what you get if you call `Open()` and move on, and it
misses the swap — which is why the swap is its own bench case (D3).

## ⚠ TRAP 2 — a fetch failure is not an illegal robot

The most damaging thing this worker could do is answer a question it was
not asked. If storage is unreachable, or the URL 404s, or the process dies
mid-download, **the robot is not illegal — we do not know yet.** Posting
`legal:false` there writes REJECTED onto a good snapshot, permanently, on
the strength of a network blip, and tells the player their robot is bad.

So `Judge()` returns an **action** alongside the verdict:

| action | meaning |
|---|---|
| `PostVerdict` | we read the payload and reached a conclusion about it |
| `LeaveForRetry` | we never got to read it — post **nothing**, stop heartbeating, let §5.3's 5-minute visibility timeout return the job to the queue |

**Doing nothing is a real answer**, and the queue was already built to
handle it. A sha mismatch is deliberately *not* in this category: those
bytes hash wrong now and will hash wrong on the next attempt, so retrying
is an infinite loop. That is a verdict.

Same reasoning covers a claim that arrives with no `payloadUrl` — that is a
server fault, the exact bug class fixed this morning, and it must never
reject a robot.

## ⚠ TRAP 3 — `JsonUtility` cannot send `null`, and this endpoint needs it

`SnapshotMeta.category` is `""` for "no category". `snapshots.category` is
`CHECK (category IS NULL OR category IN (…))` — **NULL passes, `""` does
not.** Unity's `JsonUtility` serialises a null string as `""`, so the one
tool anybody would reach for produces exactly the value the database
refuses, and it fails inside the API's UPDATE at the far end of a completed
job.

The result JSON is therefore hand-built, `category` is emitted as a bare
`null`, and WorkerBench asserts the **literal bytes**:

```json
{"snapshotId":"…","workerId":"bench","legal":false,"massKg":1234,
 "aabbX":1.5,"aabbY":0.75,"aabbZ":2,"category":null,
 "partsManifest":["core","beam"],"programHash":"aaaa…",
 "failReasons":["1 kg over the heaviest category (5500 kg limit, build is 5501 kg)."]}
```

The claim parser is hand-written for a related reason: the API omits nulls,
and `JsonUtility` would turn a missing `payloadUrl` into `""`
indistinguishably from a present empty one — and telling those apart is how
a FIGHT job is recognised.

## What shipped

- **`Assets/Phase1/Scripts/RobotWorker.cs`** — `Judge()` (the whole
  decision, **no I/O in it**), the result/claim JSON, `IWorkerTransport`,
  and `HttpWorkerTransport` (the first `UnityWebRequest` in this client;
  compiles, unexercised).
- **`Assets/Phase1/Scripts/ValidateWorkerLoop.cs`** — the part that talks.
  Borrows the builder's bay to measure a payload and restores it on every
  path out, including the throwing ones.
- **`Assets/Phase1/Scripts/WorkerBench.cs`** — `StubWorkerTransport` plus
  39 checks. `Assets/Phase1/qa_worker_bench.txt`.

Splitting *decide* from *talk* is what makes the bench possible: every
failure the worker must survive is provoked by handing `Judge` a different
string.

## Verification, prediction first

Predicted: compile clean · **WorkerBench 39/39**. **Measured: exactly
that**, 0 fail.

Sections: **A** the wire format (where `JsonUtility` would have lied) ·
**B** parsing a claim, including telling FIGHT from VALIDATE · **C** a real
robot end to end — the fixture reads **924 kg → FEATHER**, one post, right
job, bay byte-identical afterwards · **D** the four failures — storage
down (`LeaveForRetry`, **0 posts**), altered payload (rejected, reason
names the hash, no category, reaches the API as `null`), valid blob at the
wrong key (rejected on identity), claim with no `payloadUrl` (retry, not
rejection) · **E** empty queue and a FIGHT job, both left alone.

### What this bench does NOT prove, stated plainly

**That the API accepts the JSON.** The shapes are written from
`Program.cs`'s `ValidateResult` record and `001_init.sql`'s CHECKs, and
section A asserts literal bytes — but only `run_local.sh` with a real
worker pointed at it closes that loop. `api_smoke` was **37/37 green while
the claim response was unusable**, for exactly this reason. Relay request
003 asks the server side to pin the same contract independently, so the
two halves are asserted by different code rather than by one belief.

## M1 after this

| deliverable | state |
|---|---|
| API v0, queue, claim/heartbeat/validate-result | done, local only |
| Claim resolves to a payload | fixed 08-09 |
| Category assignment | done and proven, mass only |
| **VALIDATE worker loop** | **done, benched 39/39 against a stub** |
| Worker ↔ live API, end to end | **not yet** — needs `run_local.sh` + a worker |
| FIGHT endpoints, replay upload | do not exist |
| Worker image / Docker / GCP | not started |
| Client login + ENLIST UI | not started |

**Next:** point the worker at the running API. That is one editor session
plus one `run_local.sh`, and it is the first moment anything in M1 has been
proven end to end rather than half at a time.

## Method notes

**Read the other side of the seam, not the doc about it.** Trap 1 was found
by reading the upload path before writing the fetch path. The design doc
says the worker fetches the payload and verifies the hash; it does not say
*which* hash, and the two plausible readings differ by "works" versus
"rejects everything".

**An action is part of a verdict.** The instinct is to make a validator
return true or false. The important third answer here is *I could not
tell*, and it needed a distinct return value, a distinct code path, and a
bench case asserting that **nothing was posted** — an assertion about
absence, which is the kind a test rarely makes and this one had to.

**Separate deciding from talking and the bench writes itself.** No API, no
Postgres, no Docker, no network, and four failure modes that would be near
impossible to provoke on demand against a real server.

**Name what a green bench proves.** 39/39 proves the decision and the wire
format. It proves nothing about whether the API accepts what is sent — so
that sentence is in the file, in this record, and in the request that asks
the other side to check it independently.
