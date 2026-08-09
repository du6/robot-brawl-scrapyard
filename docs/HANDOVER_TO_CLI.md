# HANDOVER — Robot Brawl passes to Claude Code on owen's Mac

**Written 2026-08-09 by the Cowork session that had been driving this
project. Owen's decision: full handover. You are the primary worker now.**

---

## 0. The one thing to do before you trust this document

**Verify you can drive the Unity editor. Do it first, before any work.**

```
RobotBrawl.Phase0.CategoryBench.RunPure()
```

via `unity-mcp`'s `Unity_RunCommand`. It is pure data — no scene, no play
mode, under a second. **Expect exactly `44 pass, 0 fail`.**

- **If you get 44/44:** you have shell, `git`, `dotnet`, `psql` *and* the
  editor. Everything below applies and you can do all of it.
- **If you cannot:** stop and tell owen. Most of this project's
  verification lives in play mode, and a session that cannot bench must
  write "unverified" on every `Assets/` change rather than reporting green.

✅ **MEASURED 2026-08-09, and it works.** A Claude Code session ran exactly
this and reported **`44 pass, 0 fail`** through its own `unity-mcp` bridge,
with the career save md5 unchanged and without entering play mode. So the
edit-mode path is proven, not assumed.

✅ **AND PLAY MODE IS NOW PROVEN TOO — done 2026-08-09, the run this
section asked for.** A CLI session entered play mode over the bridge,
survived the domain reload, ran `RobotBrawl.Phase0.WorkerBench.Run()`,
polled to `finished`, and got exactly **39 pass, 0 fail** — then exited
cleanly, leaving `isPlaying=False`, the scene undirtied and no stray bench
object. **Career save byte-identical AND mtime unchanged**, so it was never
written at all.

**So you have the whole bench suite.** Method, the five-call split the
domain reload forces, and the traps are in
`docs/Play_Mode_From_CLI_Proven_2026-08-09.md`. Read it before your first
play-mode run rather than rediscovering that `EnterPlaymode()` returns
before `isPlaying` is true.

⚠ **What that run did NOT cover.** `WorkerBench` is a well-behaved
play-mode bench: no fight, no arena, a stub transport, and it isolates
`Career.Data` itself. The benches that run **real fights** —
`MatrixBench`, `LadderSweepBench`, `OpeningBench` — plus `CareerSmoke`,
which is documented as not isolated, are still unmeasured from a CLI
session, and they are where isolation actually bites:
`FightManager.End()` calls `Progression.OnMatchEnd` unconditionally.
Also note the window nothing protects: `BuilderManager` saves the career on
nine paths gated only on `Career.autosave`, which stays `True` from
play-mode entry until a bench's own `Start()` clears it. **Fingerprint
either side, mtime included.**

### ⚠ And the correction that produced that caution

`CLAUDE.md` and `docs/CLI_SESSION_BRIEF_2026-08-09.md` both say a CLI
session **"probably cannot run any Unity bench."** *That is almost
certainly false, and the Cowork session wrote it into both files.* It came
from a handover written by a session that could not check, was repeated
without testing, and then a CLI session's own status report said *"I did
not run a single Unity bench **and cannot**"* — phrased as a capability
rather than as something tried. The bridge appears to have been connected
the entire time.

**Delete that claim from both files once you have your 44/44.** Leaving it
hands the next session the same wrong premise. It is the fourth stale
document to cost this project real time in one day, and the only one
authored here rather than inherited.

The general lesson, which is worth more than the specific fix: **this
project's documents describe the world on the day they were written.**
Check the artifact, not the note about the artifact. Three separate
examples are in `docs/`, all from 2026-08-09.

---

## 1. What you are now responsible for

Everything. Owen chose a full handover, knowing what it ends:

- **No unattended work.** A Cowork session could schedule itself awake and
  keep working while he was away. You end when your terminal does. Nothing
  progresses unless he is running you.
- **No phone alerts.** Telegram belonged to the Cowork session.
- **The claude.ai Project is frozen.** `docs/` in git is now the source of
  truth. The Project carries a `READ_FIRST_source_of_truth_moved.md` notice
  saying so. Do not treat anything in it as current; you cannot read it
  anyway.

`docs/` is better for this regardless: versioned, diffable, and a change to
the code and a change to its record can land in the same commit.

If owen later wants work continuing while he is away, a Cowork session has
to exist again — and its first act must be reading `docs/`.

---

## 2. Hard rules. Not style preferences.

1. **Owner state is sacred.** The career save at
   `~/Library/Application Support/owen/Robot Brawl_ Bolt & Blade/robotbrawl_career.json`
   must come back byte-identical — md5 **`18614d0e6603869f28fb65992b7d1484`**,
   mtime 2026-08-05 21:57. Fingerprint it at session start and after
   anything that could touch it. Timestamped backups in `career_backups/`.
   Anything running a fight headlessly **must** isolate `Career.Data` —
   `FightManager.End()` calls `Progression.OnMatchEnd` unconditionally.
2. **Check `EditorApplication.isPlaying` and `isCompiling` before taking
   the editor.** If either is true, owen may be using it. Ask, do not
   seize.
3. **Where a quality is measurable, measure it over EVERYTHING rather than
   over a named list.**
4. **When a check fails, ask whether the CHECK is wrong before changing the
   product.** It was the check three separate times in one day. A CLI
   session already applied this correctly to one of its own instruments.
5. **Do not loosen a threshold to make a check pass.** If one genuinely
   must be loose, say why in the file, with the measured numbers.
6. **Predict before you measure, and write the prediction down.** Every
   result in `docs/` from 2026-08-09 was predicted first. A test that can
   fail is worth running even when you expect to win.
7. **Back up before editing**: `_claude_backups/<topic>__<file>`.
8. **Never force-push.** There is no `origin`; pushes go by full URL with a
   token from `.gh_token.local` (never print it). Local and GitHub
   histories have different roots.

---

## 3. Editor discipline — this has never been written down before

It lived in the Cowork session's head and in relay requests. It is the
part of this handover most likely to be lost, so it is here in full.

- **`CategoryBench.RunPure()`** — pure data, no play mode, under a second,
  44/44. The cheapest green in the project. Run it after any edit near the
  ladder; there is no excuse not to.
- **⚠ CareerSmoke is not isolated.** Run it FIRST, or in its own play
  session, or it reports 115/128 and you will chase a phantom.
- **⚠ A `MatrixBench.RunFloor()` failure is not a regression on its own.**
  It measured 6/10, 8/10 and 7/10 with no relevant code change. The `>= 7`
  threshold sits at the true rate, so it fails about half the time at any
  sample size. Do not "fix" the product because FLOOR went red.
- **⚠ `OpeningBench` and `LadderSweepBench` are MEASUREMENT benches** — no
  pass/fail by design. `OpeningBench`'s `CUTOFF` is **per-subject** (7 s
  suits Brawler, 25 s is needed for Matador; the wrong one produced sixty
  byte-identical rows). Its point estimate **wanders ±~17 points at N=12** —
  the same program scored 42, 42 and 25 across three runs. **Never compare
  arms differing by less than ~20 points unless they ran in the same
  batch**, and always keep a control arm inside the batch.
- **A swept variable producing no variance has usually not been swept.**
- **Unity `RunCommand` traps:** game types are in namespace
  `RobotBrawl.Phase0` (`RobotBrawl.Phase0.BuilderManager` — bare fails with
  CS0246, `global::` fails with a misleading CS0400); the command class
  must be named `CommandScript` and be `internal`; `result.Log` substitutes
  only `{0}` and `{1}`, so pre-format anything longer.
- **The bridge drops.** Wait 60–70 s, verify state, retry the same command.

---

## 4. Where the project stands

### Committed 2026-08-09

`fbd2706..028522e` — seven commits covering the opening-disarm fix,
category assignment, the VALIDATE worker, the server claim contract,
`CLAUDE.md`, and twelve `docs/` records that had never been tracked. Then
`4653009` — the register transaction and the category-contract checks.

A fresh clone **builds the API**: `dotnet build` exit 0, 0 warnings, 0
errors, measured from a clone that had never seen the working tree. All ten
Unity scripts went in **with their `.meta`** — verified in both directions.

### Green at last measurement

| bench | result |
|---|---|
| `CategoryBench` | **44/44** |
| `ReplayBench` | **60/60** |
| `WorkerBench` | **39/39** |
| `ProgramBench` | **40/40** |
| `MatrixBench` | 10/10 FLOOR + 3/3 SWEEP |
| `sql_bench.sh` | **34/34** |
| `api_smoke.sh` | 43 checks, **3 failing on purpose** (see §5.1) |

**Stale-green, verify before trusting:** VerbBench (32) · AutonomyBench
(24) · CareerSmoke (128) · CanvasDragBench (31) · TestDebugBench (30) ·
TouchSmoke · HazardBench · SensorProbe · CareerBench.

### M1

| deliverable | state |
|---|---|
| API v0, queue, claim/heartbeat/validate-result | done, local only |
| Claim resolves to a payload | fixed |
| Category assignment — **mass only** | done, proven |
| VALIDATE worker loop | done, benched against a stub |
| **Worker ↔ live API, end to end** | **never done** |
| FIGHT endpoints, replay upload | **do not exist** |
| Worker image / Docker / GCP | not started |
| Client login + ENLIST UI | not started — zero `UnityWebRequest` in the client |

---

## 5. The work queue, in order

### 5.1 — ✅ DONE 2026-08-09: the validate-result 500 that livelocks

**Fixed. `api_smoke` 48 pass / 0 fail / 0 skipped** (was 43/3/0),
`sql_bench` 34/34 unchanged, `qa_api_server.log` back to **0 error lines**
and 0 occurrences of `23514`. The queue ends a full run `DONE 5 / FAILED 3`
with **zero `READY` and zero `CLAIMED`** — nothing spinning. Record and the
three refusal messages: `docs/Validate_Result_Livelock_Fixed_2026-08-09.md`.

Two things worth carrying forward: the fix validates *before* the write
rather than catching `23514` after, so the 400 can name the trap; and a
refused result retires the job to `FAILED` rather than `READY`, because a
deterministic payload gains nothing from a retry. **The snapshot is left
`PENDING`** — visible-stuck rather than invisibly-retrying — and whether it
should become `REJECTED` is a product question left open.

The original statement of the problem follows.

Found by the server-side checks and **deliberately left unfixed** so it
would get its own commit.

Posting `"category":""` or `"category":"BANTAM"` to
`/v1/worker/jobs/{id}/validate-result` raises an **unhandled Postgres
`23514` (CHECK violation) → HTTP 500** — *and the job is never completed*.
It returns to the queue on the visibility timeout and is retried **forever**.
A malformed result does not fail loudly; it **livelocks a worker slot**.

`""` is not hypothetical: Unity's `JsonUtility` serialises a null string as
`""`, so it is exactly what a worker written the obvious way emits. The
shipped worker hand-builds its JSON to send a bare `null` and asserts the
literal bytes — but the API must not depend on one client being careful.

Second finding, same area: **`legal:true` with `category:null` is currently
ACCEPTED.** That combination can never be placed — `ratings.category` is
NOT NULL. Refuse or flag it.

Both need: a clean 400 with a readable reason, and **the job marked failed
rather than left to spin.** The three failing `api_smoke` checks become
green and are the acceptance.

### 5.2 — The FIGHT half of the server contract

Does not exist: no match-create, no match-result, no replay upload. §5.3 of
`docs/Multiplayer_V3_Design_Doc.md` specifies the four-step lifecycle in
prose; none of it is code. The schema is ready (`matches`,
`match_jobs.kind='FIGHT'` with a CHECK on job shape), and a FIGHT claim
already returns both snapshots' URLs and hashes plus `arena` and `seeds`.

Requirements: escrow debit and the match row **in one transaction**; worker
signature and job ownership verified; ledger append-only (balance is
`SUM(delta)`). Extend **both** `sql_bench.sh` and `api_smoke.sh`.

⚠ **Add at least one check that walks the whole lifecycle**, not each
endpoint alone. `api_smoke` sat at **37/37 green while the claim response
was unusable** — every endpoint answered correctly in isolation and no
consumer had ever tried to *complete* a job. An endpoint suite that never
runs the loop it exists to serve is testing its own reachability.

### 5.3 — ✅ DONE 2026-08-09: the worker against the live API, end to end

**Proven.** 3 jobs handled, 3 posts succeeded, 0 failed, 0 left for retry.
A legal robot (`spinner1`, 16 parts) went **ACTIVE** with `category=FEATHER`,
`mass=799`; a wheel-less core was REJECTED carrying the builder's own string
*"Needs at least 1 wheel."*; an unreadable payload was REJECTED cleanly.
Queue ended `DONE 3` with zero `READY`/`CLAIMED`/`FAILED`, server log 0
errors, career save byte-identical with mtime unchanged.

Record: `docs/Worker_Live_API_E2E_2026-08-09.md` ·
evidence: `server/qa_worker_e2e.txt`.

⚠ **Still open even though §5.3 is done:** this is a **hand-run, not a
bench** — it catches nothing tomorrow. Nothing was killed mid-flight, so
M1's *"kill a worker mid-fight; job retries and completes"* clause is
untested. `RunForever` was never exercised, only `RunOnce` three times. And
it is localhost only: the `file://` blob URL becomes a signed cloud URL in
production and is unproven there.

The original statement of the problem follows.

`run_local.sh` in one terminal, `ValidateWorkerLoop` pointed at
`http://localhost:5000` with `HttpWorkerTransport` in the editor. **This
has never been done.** WorkerBench's 39/39 proves the decision and the wire
format against a stub; it proves nothing about whether the API accepts what
is sent. This is the first moment M1 would be proven end to end rather than
half at a time. Do it after 5.1, or the first malformed post livelocks the
queue.

`HttpWorkerTransport` is the first `UnityWebRequest` in this client. It
compiles and has never made a request.

### 5.4 — The ladder's 43% mutual disarm

The biggest open single-player problem. The opening-ram fix moved it by
**zero bouts** — 13/30 before and after — so it is its own mechanism, and
three cheap explanations are already ruled out (see the closed doors in
`docs/SESSION_HANDOVER_2026-08-09_evening.md`). It needs its own bench;
`OpeningBench` is the template — **measure the disarm event directly, not
the fight outcome.** That is what took the opening disarm from 42% to 0%.

### 5.5 — Does an ABS tower actually win?

The open question created by shipping mass-only categories. Weight bounds
density, not size: under a 1,500 kg cap one Beam is 463 kg in Tungsten and
25 kg in ABS, so a legal **Featherweight** can be a 59-beam ABS tower ~35 m
tall in a 14 m arena. In career the only opponent is the AI. On the ladder
it is a human looking for exactly this.

**Nothing has measured whether it wins.** If it does, the lever is density
or an explicit reach cap **applied to the whole game** — *not* a size box
added back for the ladder, which owen deliberately deleted on 2026-08-03.
Read `RobotCategory.cs`'s header before touching this.

### 5.6 — Then

Docker (never built; owen has none) · GCP deploy (`deploy_api.zsh` expects
Secret Manager entries `rb-jwt-secret`, `rb-worker-key`, `rb-pg-conn` and
**they do not exist**) · client ENLIST UI · re-run the stale-green benches,
CareerSmoke first · the chassis sweep · `WallShy`'s retreat burst ·
mismatched wheel roll axes · braking overshoot · `SIDE TO BACK TO` ~18° out
· critic loop 7's open list · Roblox port scoping (wants a fresh session).

---

## 6. `_relay/` is finished

`_relay/` was the channel between the Cowork session and you: requests in
`inbox/`, results in `outbox/`, published by a `.done` marker. It ran four
requests and every one produced a real result. **With one session it has no
purpose.** It is gitignored; leave it or delete it.

Two things it taught that outlive it:

- **A result must say what the request got wrong.** Requests were written
  by a session that could not run the commands it was asking for, and were
  wrong twice — once about the git index, once about `/tmp`. A channel that
  only carries compliance back teaches nothing.
- **Stopping beats improvising.** The first attempt was sandbox-blocked
  mid-protocol and stopped rather than working around it. That report
  exposed a design flaw — the protocol depended on `mv`, the operation most
  likely to be gated — which a partial success would have hidden.

---

## 7. The five documents worth reading before you change anything

1. `docs/SESSION_HANDOVER_2026-08-09_late.md` — current state.
2. `docs/SESSION_HANDOVER_2026-08-09_evening.md` — **Part 1 is still live
   for single player**, including three closed doors and the ram rule.
3. `docs/Category_Assignment_Shipped_2026-08-09.md` — why the size box is
   gone and what it costs.
4. `docs/Validate_Worker_Shipped_2026-08-09.md` — the worker, and the three
   traps in its contract. Read before touching the server or the worker.
5. `docs/Multiplayer_V3_Design_Doc.md` — the spec. **§1.2 carries an
   as-built amendment**; the drafted table below it is historical.

---

*Owen built this game and every rule above exists because something went
wrong once. When a document and the code disagree, the code is right and
the document is a bug — fix it in the same commit.*
