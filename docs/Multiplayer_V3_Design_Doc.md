# Robot Brawl — Multiplayer v3: Cloud Contests, Global Ladder & Economy — Design Document

**2026-08-08 · v1.1 — all §10 questions RESOLVED with owen; headline
change from v1.0: ONE currency — multiplayer pays **scrap** via a
server-side wallet, the separate "credits" token is gone (§2.3) · builds on
`Programmable_Robots_V2_Design_Doc.md` (v2 COMPLETE) and
`Career_Mode_Design_Doc.md` (weight caps, size boxes, scrap economy,
commercialization notes §14) · nothing in this doc changes the
single-player game except where §3 (client work) says so explicitly**

## 0. Why v3, in owen's words

> "Bring up a cloud server to enable multi-player in automated robot
> contests. Users upload their design and program to the server to
> compete. The best robots are ranked globally in weight categories.
> Users can scout other robots' design but not code. Challenging a higher
> weight category gets more rewards. Eventually commercialize: users
> purchase scrap with real money — hence real servers are needed anyway."

The v2 program system makes this cheap in exactly the right place: robots
are fully autonomous (hats + macro sequences, no live input), so a
multiplayer "fight" is two uploaded artifacts running against each other
on a server. No netcode, no lobbies, no latency. The server simulates;
players watch replays.

### Decision log (owen, 2026-08-07)

| question | call | consequence |
|---|---|---|
| Replays visible to everyone? | **YES — watching fights is the fun part**; behavior leakage is accepted as scouting-by-film | replays are public; program *source* stays hidden; replay stream records physics + damage only, never program internals (§1.3, §5.4) |
| Sim authority | **Real servers simulate** (not client-reported results) | headless Unity workers; results and rewards are server-computed, unforgeable |
| Ranking model | **Rating + economy split** (chosen over currency-as-rank) | Glicko-2-style rating IS the rank (moves on wins AND losses); the currency is the spendable economy, never the rank (§2.3) |
| Multiplayer currency (2026-08-08) | **ONE currency — scrap**, not a separate credits token | multiplayer scrap lives in a server-side wallet; one-way deposits INTO the local career; the server never trusts a client-reported balance (§2.3) |
| API stack | **C# / ASP.NET Core** | one language everywhere; build/program payloads stay in the game's own serialization, validated by the Unity worker itself (§5.2) |
| Cloud | **Google Cloud to start, built portable** | $300/90-day credit covers the prototype; Docker + plain Postgres + S3-compatible storage keep the exit door open (§7) |
| Commercialization | **Phased last** — ladder must be fun before payments exist | real-money scrap is Phase M5, gated on traction (§8) |

## 1. Product spec — the player-facing loop

### 1.1 Core loop

1. **Build & program locally** (unchanged single-player flow).
2. **Enlist**: upload a robot — design + program — as an immutable
   *snapshot*. The server validates it (weight, part legality, program
   gate — see §1.2 on the size box) and places it in its weight
   category's ladder.
3. **Scout**: browse any ladder; open any robot's scouting card — build
   render, parts, materials, mass, weapon class, record, rating history,
   and its public replays. **The program is never shown.**
4. **Challenge**: pick a target in your category *or any heavier
   category* (your robot must be legal for its own category; you may
   always punch UP, never down). Stake the entry fee. The server queues
   the fight.
5. **Watch**: the fight runs server-side (usually within a minute); both
   players get the replay. Anyone can watch it later from either robot's
   card.
6. **Climb**: rating moves on wins and losses; scrap is earned and
   spent; seasons keep the ladder alive.

Defenders never need to be online: a challenge fights the defender's
uploaded snapshot. Defense is free for the defender and pays them a small
purse when they win (§2.3).

### 1.2 Weight categories

> ⚠ **AS-BUILT 2026-08-09 — THE SIZE BOX IS NOT PART OF THIS RULE.** The
> paragraph and table below are the design as drafted, kept for the record.
> They are wrong about the code in one load-bearing way: the size box was
> removed from the game on 2026-08-03 (`baabe3a`, owen — *"we already have
> the weight limit. why do we also need size limit?"*). `League.sizeBox`,
> `BuilderManager.OverSizeBox` and `SizeBoxLine` are gone, and
> `CareerValidate` has checked mass alone ever since, so "already enforced
> by `Validate()`" below has been untrue since before M1 started.
> **OWEN DECISION 2026-08-09: the ladder is MASS ONLY.** Natural category is
> the smallest class whose **weight cap** the robot meets; the size box
> column below is historical. Shipped as `RobotCategory.cs` + `CategoryBench`
> (44/44) — full record in `Category_Assignment_Shipped_2026-08-09.md`.
> The caps themselves are unchanged and correct.
>
> The open cost, so it is not rediscovered as a surprise: **weight bounds
> density, not size.** Under a 1,500 kg cap one Beam is 463 kg in Tungsten
> and 25 kg in ABS, so a legal Featherweight can be a ~35 m ABS tower in a
> 14 m arena. Nothing has measured whether a tower actually wins. If it
> does, the lever is **density or an explicit reach cap applied to the whole
> game** — not a size box added back for the ladder alone.

Reuse the career table (`Career_Mode_Design_Doc.md` §3b) — the caps and
size boxes are already tuned, already enforced by `Validate()`, and
already legible to players from career mode:

| category | weight cap | size box (m) | maps to career league |
|---|---|---|---|
| **Featherweight** | 1,500 kg | 2.0 × 1.5 × 2.0 | Scrapyard Open |
| **Lightweight** | 2,000 kg | 2.0 × 1.5 × 2.0 | Garage League |
| **Middleweight** | 2,800 kg | 2.5 × 1.8 × 2.5 | Regional Circuit |
| **Heavyweight** | 4,000 kg | 2.5 × 1.8 × 2.5 | National Series |
| **Super-heavy** | 5,500 kg | 3.0 × 2.0 × 3.0 | World Championship |

A robot's **natural category** is the smallest category whose weight cap
it satisfies (as built — the drafted rule also required the size box; see
the as-built note above). It appears on that category's leaderboard. It
may challenge into its natural category or any heavier one.

**Category integrity rules** (anti-gaming):

- Category is computed server-side from the validated snapshot — never
  client-reported.
- Re-uploading a snapshot that changes the robot's natural category moves
  it to the new ladder and **resets its rating to placement** (carrying a
  rating earned at 1,500 kg into the 4,000 kg ladder is meaningless in
  both directions). Same-category re-uploads keep rating — iterating on
  your build is the game.

### 1.3 Scouting: design public, code private

The scouting card exposes exactly what the career scouting screen already
shows for roster bots (build render on a turntable, mass, value, weapon
class) plus ladder data (rating, record, streak, replays). Explicitly
**not** exposed: the program, the hat list, the sensor thresholds, the
conflict-lint output, or anything derived from program internals.

Replays inevitably reveal *behavior* — that is embraced as the metagame
("I watched three of its fights; it always backs off from walls; I'll
bait it into the corner"). The replay format enforces the boundary
structurally: it records part transforms and damage events only, so even
a modified client cannot extract program data from a replay file (§5.4).

### 1.4 What a "fight" is

The existing autonomy-fight path, verbatim: both robots under
`ProgramRunner` control, 5 Hz decision cadence, same energy budget, same
damage attribution, judges' verdict on timeout. One challenge = **best of
3 bouts**, same arena, three different spawn seeds — single-bout variance
is real (the V2.3 matrix showed preset matchups are not 100/0) and
best-of-3 keeps a lucky wall-slam from deciding a ladder position.
Arena: the neutral league arena for v3.0; hazard arenas become a
challenge option later (they're already data-driven from CareerDB).

## 2. Ranking & rewards design

### 2.1 Rating (the rank)

Per-robot **Glicko-2** (rating + deviation + volatility — better than
plain Elo for a ladder where some robots fight rarely; deviation decays
so idle robots' ranks go soft rather than squatting).

- New snapshot in a category → placement rating 1200, high deviation.
- Same-category fights: standard Glicko-2 update for both sides, from
  the best-of-3 outcome (win = 1, loss = 0; no draws — judges decide).
- **Cross-category (punch-up) fights**: the challenger's update treats
  the defender as `defender_rating + 150 × category_gap` (beating a
  heavier robot is worth more, losing to one costs almost nothing — the
  expected-score curve does this automatically once the offset is in).
  The **defender's rating is untouched** by cross-category fights: heavies
  must not be able to farm rating by squashing lightweights, nor lose
  their rank to a swarm of speculative punch-up attempts.
- Leaderboard = rating, ranked within category. Global "pound-for-pound"
  board = rating across all categories (display only, no mechanics).

### 2.2 Defender protection (same-category)

- **Challenge tickets**: each robot may *initiate* 10 challenges per day
  (tickets, refreshed daily). Defenses are unlimited and free.
- **Defense loss floor**: a defender's rating cannot drop more than 75
  points per day from defenses. Excess challenges still pay out scrap
  to winners but apply zero rating delta to the defender (shown honestly
  in the fight report: "defender daily floor reached").
- **Repeat-opponent taper**: rating and scrap gains vs the same opponent
  taper to zero after 3 wins in a rolling 24 h (kills win-trading and
  griefing in one rule).

### 2.3 Scrap (the economy) — single currency, server wallet

**Owen's call (2026-08-08): there is ONE currency in Robot Brawl —
scrap.** Multiplayer does not introduce a separate "credits" token.

The implementation must respect one hard constraint: pre-M5, career
scrap lives in the *local save*, which a modified client can edit
freely — so the server can never trust a client-reported balance.
Resolution (owen, same date): **a server-side scrap wallet with one-way
deposits**:

- Every account has a **server scrap wallet**. All multiplayer stakes,
  purses, and payouts debit/credit this wallet exclusively, via the
  append-only ledger (§5.3). The server never reads or accepts the
  local career balance.
- **DEPOSIT TO CAREER** (one-way): the player may move wallet scrap
  into their local career save at any time. Server debits the wallet
  (ledger row), client credits the career. The reverse direction does
  not exist — local scrap can never enter the ladder, so a hacked save
  is worthless online.
- New wallets start with a **signing bonus of 500 scrap** — two stakes'
  worth, so a fresh account can start challenging without grinding.
- At M5, career scrap itself moves server-side and wallet + career
  merge into one authoritative balance; the one-way valve was the
  forward-compatible half of that design all along.

| event | scrap |
|---|---|
| challenge entry stake | 50 × (1 + gap) — staked by challenger, returned + purse on win, lost on loss |
| challenge win purse | 100 × (1 + 0.5 × gap)² — punching up 2 categories pays 4× base |
| defense win purse | 40 flat (passive income for being worth challenging) |
| first-blood bonus vs a robot never beaten before | +50 |
| season-end payout | by final rating rank per category |

All constants live in one server-side table (`LadderConfig`, the CareerDB
pattern applied to the backend) and are tunable without a client update.
Because deposits feed the career economy, the faucet rates above start
**conservative** — inflating career progression from the ladder is a
one-way door (buffing later is a celebrated event; nerfing is a riot,
per the career doc's own underdog-cap reasoning).

### 2.4 Seasons

**4-week seasons** (owen, 2026-08-08 — faster churn suits a small,
active early player base; length is a `LadderConfig` value, so
lengthening to 8 weeks later as the ladder deepens is a config change,
not a client update). At rollover: ratings compress 50% toward 1200,
deviation resets high, **wallet scrap persists**, season badges +
payouts awarded. Seasons are what keep a solved ladder from fossilizing
and give lapsed players a re-entry point.

## 3. Client work (Unity — the part that touches the game)

1. **Snapshot export**: serialize (build + program + material choices)
   into the existing JsonUtility formats, wrapped in a signed envelope
   `{payload, clientVersion, sha256}`. No new format — the server treats
   payloads as opaque and lets the Unity worker parse them (§6.3).
2. **Replay recorder + player**: new. Recorder runs in the sim worker
   (record part transforms at 10 Hz + damage/verdict events, gzip
   JSON-lines). Player is a client playback mode: spawn both builds,
   physics off, interpolate transforms from the stream, play damage VFX
   from events. This is the largest single piece of new client code and
   it is Phase M0 for a reason — it is also independently useful
   single-player (career fight replays) before the server exists.
3. **ARENA tab** (new dock tab beside BUILD/PROGRAM/SHOP/LEAGUE):
   login/account, ENLIST (upload + category readout), ladder browser,
   scouting card, challenge flow (stake confirm), fight inbox
   (pending/completed challenges), replay launcher, **wallet readout +
   DEPOSIT TO CAREER flow** (§2.3).
4. **Account/auth UI**: email+password v1; Sign in with Apple/Google at
   Phase M5 (required by the stores once money is involved).
5. **Networking**: plain `UnityWebRequest` against the REST API + JWT.
   No realtime socket needed anywhere in v3.

Explicitly unchanged: the builder, the program canvas, the career, all
benches, the save format (multiplayer state lives server-side; the local
save gains nothing but a cached auth token).

## 4. What this deliberately does NOT change

Single-player stays fully offline-capable · the v2 program model and its
caps (≤12 hats, ≤64 blocks — these caps are also the server's sim-cost
bound) · 5 Hz decision fairness · the energy/damage model · the career
economy's internal rules (prices, purses, entry fees — its ONLY new
input is the one-way deposit faucet of §2.3, rate-controlled server-side)
· the by-hand authoring acceptance rule for any new UI.

## 5. Server architecture

```
Unity client ──HTTPS/JSON──▶ API (ASP.NET Core, Cloud Run)
                               │        │
                               │        ├─▶ Postgres (Cloud SQL): users, robots,
                               │        │   snapshots, ratings, matches, ledger
                               │        └─▶ Cloud Storage (S3-compatible):
                               │            snapshot payloads, replays
                               │
                        match_jobs table (Postgres queue, SKIP LOCKED)
                               │
                  ┌────────────┴────────────┐
                  ▼                         ▼
           sim worker VM(s)          (scale by adding VMs)
        headless Unity (Linux
        Dedicated Server build,
        Docker) — validate jobs
        + match jobs
```

### 5.1 Components

- **API** — ASP.NET Core minimal API in a container on **Cloud Run**
  (scales to zero; free tier covers prototype traffic). Owns auth,
  ladder logic, rating math, the scrap ledger, and job enqueueing. It
  never parses build payloads and never computes fight outcomes.
- **Queue** — a `match_jobs` Postgres table claimed with
  `SELECT … FOR UPDATE SKIP LOCKED`. Deliberately not Pub/Sub in Phase 1:
  one fewer moving part, transactional with the data it describes, and
  trivially portable. Swap for a real broker only if worker count makes
  it a bottleneck (it won't for a long time).
- **Sim workers** — Docker image containing the game's **Linux Dedicated
  Server build** with a `MatchRunner` boot scene. Loop: claim job →
  fetch snapshots from storage → run (validate | best-of-3 fight,
  faster-than-realtime via manual `Physics.Simulate` stepping on the
  fixed tick) → upload replay → POST signed result to the API →
  heartbeat. Stateless and dumb by design: killing one mid-job just
  returns the job to the queue (visibility timeout).
- **Postgres** — Cloud SQL smallest tier (or Postgres on the worker VM
  during the credit period; migrate to Cloud SQL before real users).
- **Storage** — Cloud Storage buckets `snapshots/` (private) and
  `replays/` (public-read via signed URLs). Replays are kB–low-MB
  (keyframes, not video) so egress cost is negligible.

### 5.2 Why the worker validates AND fights

`Validate()`, weight math (as built: no size box, §1.2), part legality, the program gate, and
the conflict lint all live in Unity C# against Unity types. Porting them
to a plain .NET API would create a second implementation that drifts —
the classic two-sources-of-truth bug. Instead the API treats uploads as
opaque; a **validate job** runs in the same Unity worker image, which
executes the *actual game code* against the payload and returns metadata:
`{legal, mass, aabb, category, partsManifest, programHash, failReasons[]}`.
One implementation, zero drift, and a hacked client can't upload an
illegal robot because the same code that would refuse it in the builder
refuses it on the server.

### 5.3 Match lifecycle (the one flow that must be airtight)

1. `POST /challenges` — API checks: ticket available, stake affordable,
   category legality (challenger natural ≤ defender natural), taper not
   exhausted. Debits stake into escrow **in the same transaction** that
   inserts the match row (`status=QUEUED`) and job.
2. Worker claims job, runs best-of-3, uploads replay, POSTs
   `{matchId, bouts[], verdict, replayUrl, workerSig}`.
3. API (single transaction): verify worker signature + job ownership →
   write result → apply Glicko-2 deltas (with cross-category and
   floor/taper rules) → settle escrow + purses into the **append-only
   scrap ledger** → mark `COMPLETE`. Wallet balances are always derivable by
   summing the ledger; the ledger is the audit trail the real-money phase
   will require anyway (§8, Phase M5).
4. Both players' fight inboxes show the result; replay is live.

Timeouts: a job unclaimed or unheartbeated for 5 min returns to the
queue; a match failing 3 attempts refunds the stake and flags for
inspection.

### 5.4 Determinism, honestly stated

Cross-machine physics determinism is NOT assumed. The authoritative
outcome is whatever the worker's run produced, and the replay is a
**recording of that run** — so replay always matches result by
construction, and nothing ever needs re-simulation. Same-binary,
same-machine runs are repeatable enough for debugging; that is all we
need. Best-of-3 with seed variation handles fight-to-fight variance as a
*design* feature rather than pretending it away.

### 5.5 Security & abuse checklist

- All rewards server-computed; client never reports outcomes.
- Snapshots validated by real game code (§5.2); category server-computed.
- JWT auth (short-lived access + refresh); rate limits per user and per
  IP on upload/challenge endpoints; upload size caps (a legal snapshot is
  small — the v2 program caps bound it).
- Worker→API calls authenticated with a worker key + per-job nonce.
- Display names filtered + reportable (the one UGC surface besides the
  builds themselves).
- Postgres daily automated backups; ledger append-only from day one.

## 6. Data model (Postgres, abridged)

```
users        (id, email, pw_hash, display_name, created_at, flags)
robots       (id, user_id, name, created_at, retired)
snapshots    (id, robot_id, storage_url, sha256, client_version,
              status: PENDING|ACTIVE|REJECTED, mass, aabb, category,
              parts_manifest jsonb, program_hash, uploaded_at)
              -- one ACTIVE per robot; history kept for audit
ratings      (robot_id, category, rating, deviation, volatility,
              season_id, updated_at)
matches      (id, challenger_snapshot_id, defender_snapshot_id, category,
              gap, arena, seeds int[], status, verdict, replay_url,
              rating_deltas jsonb, created_at, completed_at)
match_jobs   (id, match_id|snapshot_id, kind: FIGHT|VALIDATE, status,
              claimed_by, heartbeat_at, attempts)
ledger       (id, user_id, delta, reason, match_id?, created_at)
              -- append-only scrap wallet; balance = SUM(delta)
              -- reasons incl. STAKE, PURSE, DEFENSE, BONUS, SEASON,
              --   SIGNING_BONUS, DEPOSIT_TO_CAREER (negative, one-way)
tickets      (robot_id, date, used)
seasons      (id, starts_at, ends_at, config jsonb)
```

## 7. Google Cloud deployment & portability rules

**Phase 1 footprint** (fits inside the $300/90-day credit, then
~$30–60/mo): Cloud Run API (≈$0 at prototype traffic) · Cloud SQL
smallest Postgres (~$10–15/mo; or co-locate Postgres on the worker VM at
$0 extra during the credit) · one `e2-small`/`e2-medium` worker VM
(~$13–25/mo; stop it when idle — the queue just waits) · Cloud Storage
(pennies) · **billing alert at $25/mo configured on day one** — an
idle-spinning Unity worker is exactly the thing that quietly eats a
budget.

**Portability rules** (the exit door, kept open deliberately):

1. Everything ships as Docker images — API and worker both.
2. Plain Postgres only; no Firestore/Spanner/Datastore.
3. Storage access through the S3-compatible interoperability API.
4. No GCP-only service in the critical path (Cloud Run is just a
   container host; the queue is Postgres).
5. If sim demand grows, the well-worn split: control plane (API, DB,
   payments) stays on GCP; the stateless worker fleet moves to whoever
   sells CPU cheapest (Hetzner-class boxes are ~3–5× cheaper per core).
   Workers only need HTTPS to the API and storage — they run anywhere.

## 8. Implementation plan (phases, in order, with acceptance)

Estimates use the project's session-scale convention.

### Phase M0 — Foundations, no server yet (1.5–2 sessions)

*Goal: everything multiplayer needs that can be built and tested
entirely locally, in-editor, with benches.*

- Snapshot envelope export/import (build + program + materials, sha256).
- **Replay recorder**: hook the autonomy-fight path; record 10 Hz part
  transforms + damage/verdict events; gzip JSON-lines. **Replay player**:
  kinematic playback mode in the client (also a single-player feature —
  career fight replays — so it earns its keep even if v3 stalls).
- `MatchRunner`: boot scene that takes two snapshots + seeds, runs
  best-of-3 faster than realtime via stepped `Physics.Simulate`, emits
  results + replay. This is MatrixBench's fight loop, promoted to a
  product path.
- **Accept:** a bench (`ReplayBench`) runs snapshot → MatchRunner →
  replay file → playback, asserting verdict consistency and playback
  frame coverage; existing green set stays green; a recorded fight
  visually replays correctly by hand (the house rule).

### Phase M1 — Server skeleton, one fight in the cloud (1.5–2 sessions)

*Goal: end-to-end proof: upload from the client, fight on GCP, watch the
replay on the client.*

- GCP project + billing alert; `gcloud` setup scripts checked into the
  repo (no console-clicking that can't be reproduced).
- API v0: register/login (Identity + JWT), `POST /robots`,
  `POST /snapshots`, `GET /snapshots/{id}` status.
- Worker image: Linux Dedicated Server build + MatchRunner in Docker;
  validate-job and fight-job loops; Postgres queue with SKIP LOCKED +
  heartbeat + retry.
- Client: login UI + ENLIST flow (minimal).
- **Accept:** from the running game, upload two robots, trigger a match
  by hand (SQL insert is fine at this phase), watch the replay in-client
  from the cloud URL. Kill a worker mid-fight; job retries and
  completes. Reject an over-cap and a program-gate-failing snapshot with
  the same message strings the builder shows.

### Phase M2 — The ladder (2 sessions)

*Goal: the full player loop — scout, challenge, climb.*

- Glicko-2 service + unit tests (deterministic vectors); category
  assignment; cross-category offset; defense floor; repeat taper;
  tickets.
- `POST /challenges` with escrow transaction; fight inbox endpoints;
  leaderboards per category; scouting card endpoint (metadata from the
  validate job — server never leaks program payloads to other users).
- Client ARENA tab: ladder browser, scouting card (reuses the career
  scouting turntable), challenge flow with stake confirm, inbox, replay
  launcher.
- **Accept:** scripted two-account run: enlist ×2 → challenge → rating
  and ledger deltas match hand-computed Glicko-2 to 4 decimals →
  defender floor and taper demonstrably engage under a challenge loop →
  punch-up pays the (1+0.5g)² purse → a third account can scout both and
  watch the replay but cannot fetch either program payload (authz test).

### Phase M3 — Economy, seasons, liveness (1–1.5 sessions)

*Goal: reasons to come back.*

- **Wallet + DEPOSIT TO CAREER** end-to-end (§2.3): signing bonus,
  deposit endpoint + ledger rows, client flow, conservative faucet
  rates in `LadderConfig`. Scrap sinks beyond stakes (cosmetic robot
  plates/titles v1 — cheap, no balance impact); 4-week season rollover
  job (compression, payouts, badges); season history on robot cards.
- Scheduled **league nights** (server-initiated round-robin among top 8
  per category, weekly) — passive content that makes the ladder move
  even when nobody challenges, and produces featured replays for the
  ARENA tab's front page.
- **Accept:** forced season rollover on a staging DB produces correct
  compression + payouts; league night generates and settles 28 matches
  unattended; ledger conservation check passes (every scrap created is
  accounted to a config'd faucet, every deposit-to-career debit has a
  matching career credit on the client); a deposited balance replayed
  from a tampered client is rejected (idempotency key per deposit).

### Phase M4 — Hardening & soft launch (1 session + ongoing)

- Rate limits, input fuzzing on the upload path (malformed payloads must
  land in REJECTED, never crash a worker — the worker's crash IS the
  fuzz oracle), monitoring/alerting (Cloud Monitoring: queue depth, job
  failure rate, worker heartbeat age), nightly DB backup verified by
  restore drill, privacy policy + account deletion endpoint (store
  requirement even pre-revenue).
- Soft launch **on the iPad/iPhone client via TestFlight** (owen's call
  — it's where all the UI and bench investment lives) to a small
  circle; watch cost and queue-depth dashboards. Desktop client follows
  once the loop is proven.
- **Accept:** a deliberately malformed snapshot corpus (10+ cases) all
  land REJECTED with reasons; restore drill from backup passes; $/day
  within projection at 100 fights/day.

### Phase M5 — Commercialization (gated on traction; separate spec)

Deliberately NOT specced in detail here — it deserves its own doc and
its own critic loop when the ladder has proven fun. Its known shape:

- Server-authoritative inventory/scrap migration (the career economy's
  source of truth moves server-side; local save becomes cache).
- IAP via Apple/Google (15–30% cut; no external payment links for
  consumables); Sign in with Apple required.
- The career doc §14 rules carry over verbatim: **sell speed, never
  exclusivity**; weight caps are the pay-to-win firewall (money cannot
  buy past the cap); paid scrap is deterministic, never randomized
  (loot-box law).
- Compliance: age ratings, refunds/chargebacks, regional rules, tax.
- The M3 ledger + M4 account deletion + audit trail were designed so
  this phase is an extension, not a rewrite.

## 9. Risks

| risk | mitigation |
|---|---|
| Replay/playback drift makes fights look wrong | replay is a recording, not a re-sim (§5.4); ReplayBench asserts coverage; by-hand visual check is an M0 acceptance gate |
| Unity headless Linux build fights differently than the editor | M0 accept includes cross-checking MatchRunner verdicts against the same seeds in-editor; any systematic divergence is a bug to fix BEFORE the server exists |
| Ladder is solvable by one dominant archetype | the V2.3 matrix already says no preset sweeps the upper ladder; seasons + LadderConfig tunables + weight categories give three correction levers without client updates |
| Win-trading / smurf farming | repeat taper (§2.2), tickets, placement deviation makes farmed rating decay-prone; ledger audit trail |
| Ladder faucet inflates the career economy (single currency, §2.3) | deposits are one-way and rate-controlled by `LadderConfig`; faucet starts conservative; total deposited per account is a monitored metric from day one |
| Cloud bill surprise | billing alert day one; workers stoppable; queue tolerates worker absence; §7 cost table reviewed at each phase gate |
| Solo-dev ops burden | scale-to-zero API, one VM, Postgres queue — the whole Phase 1 system has two moving parts; monitoring before soft launch, not after |
| Scope creep into realtime PvP | out of scope by decision — the async model IS the product; any realtime mode is a future doc |

## 10. Open questions — ALL RESOLVED (owen, 2026-08-08)

1. **Display names & moderation** — ✅ **filter + report** is the soft
   launch bar: profanity filter at creation, report button on scouting
   cards, manual review of reports (M4).
2. **Platform priority** — ✅ **iPad/iPhone first**, soft launch via
   TestFlight (M4); desktop follows once the loop is proven.
3. **Season length** — ✅ **4 weeks** (owen chose faster churn over the
   drafted 8; a `LadderConfig` value, lengthen later if the ladder
   deepens).
4. **Currency** — ✅ **single currency: scrap.** Owen rejected both the
   separate-credits draft and a bridge; resolved as the server-side
   wallet with one-way deposits to career (§2.3). This supersedes the
   v1.0 "credits" design everywhere.
5. **League nights** — ✅ **weekly top-8** round-robin per category
   (M3); cadence tunable once the ladder is populated.
6. **GCP account** — ✅ under **leondu167@gmail.com**, with the $25/mo
   billing alert to the same address.

## 11. First moves (next session)

1. §10 is resolved — no review gate remains. Phase M0 starts in the
   editor with the existing protocols (it needs no cloud account):
   snapshot envelope, then the replay recorder/player, then
   MatchRunner + ReplayBench.
2. GCP account under leondu167@gmail.com + the $25/mo billing alert can
   be set up any time before M1, owen's hands (card required); nothing
   blocks on it during M0.

---
*v3 multiplayer builds on v2 exactly as v2 built on v1: the shipped
runtime is the foundation, and the first phase is the one you can bench
in the editor before trusting it anywhere else.*
