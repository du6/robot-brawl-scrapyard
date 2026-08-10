# Four defects that only exist in the cloud — 2026-08-09

Object storage landed, and deploying it surfaced three more faults that
**cannot be reproduced on a local run**, plus a fourth found while clearing
the wreckage the first one left behind. They are recorded together because
they share a cause worth naming: *on a developer's machine the process is the
whole world, and in the cloud it is one replaceable piece of one.* Every
assumption that quietly depended on being the whole world broke at once.

None of them made anything go red. All four looked like success.

---

## 1. The disk that isn't there

`FileBlobStore` wrote uploaded payloads to `BLOB_ROOT` on local disk. On Cloud
Run with `min-instances=0` that disk is **per-instance and ephemeral**: it
vanishes when the service scales to zero, and a second instance never had it.

The upload returns **200**. That is the entire problem — the write succeeds,
the player is told their robot is uploaded, and the failure appears minutes
later to a worker that cannot fetch the payload, or to nobody at all.

**Fixed** with `S3BlobStore` over GCS's S3 interop API, selected when
`BLOB_S3_BUCKET` is set. `FileBlobStore` stays for local runs so
`run_local.sh` still needs no network.

A stored `s3://` URI is deliberately **not** a fetchable URL. It says where
the bytes are; *who may read them* is decided per request by `GET /v1/blobs`,
which applies §1.3's prefix rule — replays public, everything else worker-key.

## 2. `Request.Scheme` is a lie behind a proxy

Cloud Run terminates TLS and speaks plain HTTP to the container, so
`ctx.Request.Scheme` is `"http"`. Blob URLs handed to workers were therefore
`http://`, and:

```
http://…/v1/blobs/…   ->  HTTP 302
https://…/v1/blobs/…  ->  HTTP 200, 242 bytes
```

A redirect **drops the `X-Worker-Key` header**. A worker that follows it gets
a 401 it cannot explain; one that doesn't follow it fails outright.

## 3. One rate-limit bucket for the entire world

Worse, and the same root cause. The anonymous auth limiter partitioned on
`ctx.Connection.RemoteIpAddress` — which behind a proxy is **the proxy**, for
every caller on earth. Ten registrations per minute, globally, for all
players at once.

Locally this is invisible, because locally the connection really is the
client.

**Both fixed** with `UseForwardedHeaders`, gated on `K_SERVICE`/`TRUST_PROXY`
because trusting those headers with no proxy in front is a forgery hole.

⚠ **`ForwardLimit = 1` with the known-proxy lists cleared reads the RIGHTMOST
value, and that is the point.** Cloud Run *appends* the real client address to
whatever `X-Forwarded-For` the caller sent, so the rightmost entry is the
platform's and cannot be forged. Reading the leftmost — the "obvious" choice —
would let anyone set their own IP and evade the limiter completely.

## 4. The snapshot nobody would ever hear about again

Clearing the payloads orphaned by defect 1 exposed a fourth. When a VALIDATE
job exhausts its attempts the reaper marks the **job** FAILED — and never
touched the **snapshot**. It stayed `PENDING` forever:

> the player uploaded a robot, the ladder says it is still being checked, and
> nothing will ever move it. No error, no reason, and indistinguishable from
> a queue that is merely busy.

`reap_stale_jobs.sql` now REJECTS the snapshot with a readable reason, and
**only when it is still PENDING**, so a late-dying job from an earlier attempt
cannot revoke a snapshot that has already passed.

---

## Measured, with controls

House rule 4 — every check below was run against a control leg first, so none
of these greens are vacuous.

| check | without the fix | with it |
|---|---|---|
| a different forwarded client keeps its own bucket | **429** | 200 |
| blob URLs are built with the forwarded scheme | **`http://…`** | `https://…` |
| a snapshot whose validation died is REJECTED | **PENDING** | REJECTED |
| …and carries a reason | **empty** | populated |

Green at the end: `api_smoke` **174/174 against real GCS, 0 skipped** (185/185
once the metrics section landed), `sql_bench` **48/48**, `restore_drill`
**10/10**, `CategoryBench` **44/44**.

Verified live on Cloud Run, fetching a blob exactly as a worker is handed it:
**HTTP 200, 0 redirects**, payload opened, `sha256` matched, anonymous **401**.

---

## Two of my own checks were wrong first

Recorded because house rule 2 keeps earning its place.

- The metrics log-shape regex used `[0-9]*`, which **matches the empty
  string**. It would have passed on `ready= claimed=`.
- It then passed on a line of **all zeros** — indistinguishable from a query
  matching nothing — while the database held 15 DONE and 4 FAILED jobs. The
  cause was mundane: `run_local.sh` finishes in 13 seconds and the reaper
  emitted every 30, so the only line captured was the startup one against a
  freshly truncated database. `run_local.sh` now sets
  `Queue__ReapEverySeconds=5`, and the bench asserts a non-zero count was
  actually observed.

**Both were green before they were right.** That is the whole reason this
project runs control legs.

---

## Still open

- **Point-in-time recovery is off** on `rb-db`. Daily backups at 09:00 UTC
  with 7 retained are enabled and the restore drill passes, but without PITR
  a restore loses up to 24 hours. It costs WAL storage against a $25/mo
  budget, so it is **owen's call**, not a default to flip.
- Deletion protection **was** off and is now **on** — that one was free.
- The queue metrics only exist while an instance is alive. The uptime check
  every 5 minutes is what keeps them flowing; see
  `scripts/gcp_monitoring.zsh`.
