# SESSION HANDOVER — 2026-08-09 evening → next session

**Read this first.** Robot Brawl: Bolt & Blade — owen's physics robot
construction/combat game, built live in his Unity editor via the Unity
MCP bridge. CURRENT handover; supersedes `SESSION_HANDOVER_2026-08-09.md`
(morning) and everything before it. The morning handover's *environment
and protocol* sections are all still accurate — reread them, they are not
repeated in full here.

**Two threads this session.** Single-player: the opening disarm is
**fixed** — the tutorial's first fight went from 6–8/10 to **10/10, every
bout a KO** — followed by three follow-ups that each measured negative,
so most of this handover is **closed doors**. Multiplayer: M1's worker
contract turned out to be **unfinishable as written**; the blocker is now
fixed and proven, two gaps remain.

---

# PART 1 — Single player

## The one change shipped

`RobotProgram.Brawler()`'s IN RANGE hat commanded **100% throttle inside
2.2 m**. **Now 45%** (backup `_claude_backups/openram__RobotProgram.cs`;
`WallShy` derives from `Brawler` and inherits it).

| | before | after |
|---|---|---|
| FLOOR | 6–8/10, run to run | **10/10, every bout a KO** |
| L1 opening disarm rate | 42% | **0%** |

Full record: `Opening_Ram_Fix_2026-08-09.md`.

## ⚠ THE RULE — read before changing any preset

Not "a 100% closing ram shears your own weapon mount" — Matador rams at
100% for *longer* (1.5 s) and shears nothing. The rule that fits all
three measured presets:

> **A closing ram is dangerous when nothing disengages after the bite.**

The mechanism is the **sustained press**: with no disengage the disc's
contact impulse and the hull's ram impulse sum at the weapon mount for as
long as the robots stay together. Confirmed directly — Brawler at 100%
*with* a disengage disarms 0%.

Corollary, also measured: **low throttle is not "safe".** Matador at 30%
flipped in 33% of bouts (0% at 100%).

## 🚪 CLOSED DOORS — do not reopen without new evidence

1. **Approach geometry / spawn offset.** A deliberately non-pursuit
   approach, ram held at 100%, scored **42% — identical**. Pure pursuit
   converges to head-on whatever the starting geometry.
   *(`Opening_Ram_Fix`, run 1 arm C.)*
2. **Matador's ram.** ~~"same bug"~~ **RETRACTED** — 0 disarms in 60
   bouts at every throttle; shipped 100% is best on every axis.
   *(`Matador_Ram_Retraction_2026-08-09.md`.)*
3. **Giving Brawler a disengage to "put its speed back."** Rule confirmed
   (100% + disengage = 0% disarmed) but every disengage arm deals
   **209–227** vs the shipped arm's **405**. Costs ~half the damage.
   **The shipped preset is the best arm on the bench.** Also closes the
   hope that a disengage would fix ladder dead air.
   *(`Brawler_Disengage_Negative_2026-08-09.md`.)*

## `Assets/Phase1/Scripts/OpeningBench.cs` — and how to read it

Measures the **disarm rate** directly, cuts each bout at `CUTOFF`, runs
arms **interleaved**. Prints rates; **does not pass or fail**.

- ⚠ **`CUTOFF` is per-subject.** 7 s suits Brawler (contact ~2 s), is
  silently wrong for Matador — run 3 returned **sixty byte-identical
  rows**. **A swept variable producing no variance has usually not been
  swept.** Matador needs 25 s. Always say which cutoff produced the
  numbers.
- ⚠ **The point estimate wanders ±~17 points at N=12.** The same program
  scored 42, 42 and 25 across three runs. **Do not compare arms differing
  by less than ~20 points unless they ran in the same batch.**
- **Keep controls inside the batch**, never across runs.

`RAM_PCTS`, `DISENGAGE`, `MATADOR`, `CUTOFF` are public statics — set them
in the same RunCommand that calls `Run()`, **after** entering play mode
(the domain reload resets statics).

## ✅ OWNER DECISION — Brawler stays at 45%

Alternatives declined, not overlooked: 55% (also 0% disarmed, dealt 357
vs 358) and a conditional ram (a *language* change — no "enemy weapon
alive" sensor). The disengage option that surfaced later is closed door 3.

## Open, single player

- **The ladder's 43% mutual disarm — the biggest open balance problem.**
  `LadderSweepBench` runs the real presets, so Brawler and WallShy are in
  18 of its 30 bouts, yet the fix moved it by **zero bouts**: 13/30
  before and after, 26/30 either-disarmed before and after. Dynamics moved
  (last hit 13.9 s → 11.4 s, dead air 41.5% → **54.0%**); the disarm
  outcome did not. **It is its own mechanism, and it needs its own bench —
  `OpeningBench` is the template.**
- **The dead-air regression is knowingly accepted.** Its one plausible
  cure was measured and makes it worse.
- **MatrixBench's UPPER sample cannot resolve changes this size.** N=3 per
  cell; **Matador, code untouched, went 4/12 → 7/12 in the same run.**
  Do not steer by UPPER cell counts. The SWEEP *claim* is fine (1/4, 1/4,
  2/4).
- **`AIController` is unbenched.** `OPENING_THROTTLE = 0.55` covers only
  the first 3 s; after that `thr = 1f` unconditionally. Under the
  corrected rule the question is whether its back-off layer disengages
  fast enough after a bite. **Not** the ladder explanation — LadderSweep
  is preset-vs-preset.

---

# PART 2 — Multiplayer (M1)

Full record: **`M1_Worker_Contract_Gaps_2026-08-09.md`**. Read it before
touching the server.

## ✅ Blocker fixed and proven: the claim now says what to fetch

`claim_job.sql` returned a snapshot UUID and nothing else, while
`storage_url` was read by no code path, `IBlobStore.GetAsync` was called
by nothing, and no endpoint served a payload to a worker. **The worker
could not fetch the thing it was asked to validate.** Nothing noticed
because `api_smoke.sh` tested each endpoint alone and no consumer had ever
tried to *complete* a job.

Fixed: the claim carries `payloadUrl` + `payloadSha256` (and both
snapshots + `arena` + `seeds` for FIGHT). Columns 0–4 unchanged and in
order, so both benches passed **unmodified**. LEFT joins on purpose — an
inner join returns no row, which a worker reads as "queue empty".

**Also fixed: `run_local.sh` did not run `sql_bench.sh`.** The one command
that proves the server excluded the only bench covering
`RobotBrawl.Api/Sql/*.sql`. It runs it now — after boot (migration creates
the schema), before api_smoke (sql_bench truncates).

**Verified, prediction first:** `sql_bench` **34/34** including the
four-concurrent-worker SKIP LOCKED section · `api_smoke` **37/37**, 0
skipped, with a real resolvable location · server log **0** exception or
error lines.

Backups: `_claude_backups/m1claim__{claim_job.sql,Infra.cs,api_smoke.sh,run_local.sh}`.

## ⚠ Still open in M1

- **Category assignment does not exist on either side.** `ValidateResult`
  requires it; `RobotSnapshot.Describe()` does not compute it. The schema
  names the categories (`FEATHER/LIGHT/MIDDLE/HEAVY/SUPER`) but the caps
  and size boxes live **only in the design doc**; `CareerDB` has league
  caps under different names and no size boxes. A new build, small, fully
  specified, benchable in-editor. **The unblocked next piece.** Trap: a
  re-upload that changes category **resets rating to placement**, so a
  wrong boundary silently wipes ratings.
- **The FIGHT half of the contract does not exist** — no match-create, no
  match-result, no replay upload. A worker can run VALIDATE jobs only,
  which is the right first target anyway.
- Worker image, Docker (owen has none), GCP deploy (Secret Manager entries
  do not exist), client login + ENLIST UI (**zero `UnityWebRequest` in the
  client**) — all not started.

---

## Bench inventory

**Run this session, green:** MatrixBench **10/10 FLOOR + 3/3 SWEEP** ·
ProgramBench **40/40** · `sql_bench.sh` **34/34** · `api_smoke.sh`
**37/37** · OpeningBench ×5 (no pass/fail by design) · LadderSweepBench
30 bouts (measurement only).

**Still stale-green, not run since before 08-09:** VerbBench (32) ·
AutonomyBench (24) · ReplayBench (57) · CareerSmoke (128, run FIRST or in
its own play session) · CanvasDragBench (31) · TestDebugBench (30) ·
TouchSmoke · HazardBench · SensorProbe · CareerBench.

Artifacts: `qa_opening_run{1..5}_*.txt`, `qa_matrix_bench_after_ram45.txt`,
`qa_ladder_sweep_after_ram45.txt`, `server/qa_sql_bench.txt`,
`server/qa_api_smoke.txt`, `server/qa_api_server.log`.
⚠ `qa_ladder_sweep.txt`'s 08-08 baseline was overwritten in place; the
numbers survive in Part 1 above.

## Career save

md5 **`18614d0e6603869f28fb65992b7d1484`**, mtime 08-05 21:57 — verified
at session start against the morning handover's fingerprint and **again
after every bench**. Backed up to
`career_backups/career_2026-08-09_1113_sessionstart.json` + profile.
Every bench ran an in-memory career with autosave off. Editor left out of
play mode.

## Next direction

1. **Category assignment + bench** (M1, unblocked, Unity only).
2. **The VALIDATE worker loop** — claim → read storage → `Open()` (hash
   verified) → `Describe()` → category → POST validate-result →
   heartbeat. In-editor against the local API; no Docker, no GCP.
3. **The ladder's 43% mutual disarm** — biggest single-player problem,
   three cheap explanations already ruled out.
4. **Fix the git index** — still four commands from owen's Terminal; the
   API is still unbuildable from a clone. Carried since 08-08.
5. FIGHT endpoints + replay upload · Docker · GCP deploy · client ENLIST
   UI · `/v1/auth/register` transaction · re-run the stale-green benches,
   CareerSmoke first · the chassis sweep · `WallShy`'s retreat burst ·
   mismatched wheel roll axes · braking overshoot · `SIDE TO BACK TO`
   ~18° out · critic loop 7's open list.
6. **Roblox port scoping** — wants a fresh session.

## First moves

1. Health RunCommand (`isPlaying`, `isCompiling`) → probe owen before
   taking the editor. **Owner state is sacred.**
2. Timestamped career backup; verify md5 vs **`18614d0e`**.
3. Read `M1_Worker_Contract_Gaps_2026-08-09.md` and the two negative-result
   docs (`Matador_Ram_Retraction`, `Brawler_Disengage_Negative`) — the
   latter two are what stops the next session redoing this one.
4. Then Next direction item 1.

## Method notes worth keeping

**Build the instrument before you tune.** A bench measuring the *event*
rather than the *outcome* made the effect unmissable: 42% → 0%.

**A retraction is a result.** "Matador has the same bug" was read off the
source and written into two documents as fact. Twenty minutes of
measurement reversed it — and produced a better rule than the fix shipped
with. **Source reading generates hypotheses; it does not generate
findings.**

**Read the consumer's contract before building the consumer.** "The sim
worker is the last piece" was intent, not connectivity. Reading the claim
SQL and `IBlobStore`'s call sites found a blocker before any worker code
existed to be built around it.

**"Done and proven" must name what it was proven to do.** 35/35 proved
each endpoint answers in isolation. It was never evidence a worker could
complete a job.

**A green suite that had to be edited to stay green is weaker evidence.**
Keeping the claim's first five columns meant both benches passed
unmodified except for the assertions that were missing.

**One command should mean one command.** `run_local.sh` excluded the only
bench covering the files most likely to be hand-edited. Not a missing
test — a missing *route to* the test.

**Confirming a rule is not the same as the rule being useful.** The
disengage rule is well supported and still does not pay for itself.

**A test that can fail is worth running even when you expect to win.**
The disengage prediction's first half landed and its second half failed —
on the number that mattered.

**Identical arms mean the instrument, not the product.**

**Ship the null arm, and keep an untouched arm in the batch.**

**Read the parts, not the verdict.** Brawler's second failure mode — disc
still bolted on, damage frozen at 19 because the *spindle* had sheared —
is invisible to any bench that logs only who won.
