# Scraplands — the map is the front door (design, 2026-09-09)

owen: "I'm thinking about developing another version of the game (a
completely new game), where user starts with a rookie robot. User can drive
the rookie robot in a global map and explore the map. The user can find
treasure boxes while exploring the map. The treasure box includes parts or
scrap. User can go to their garage to upgrade their robot. While exploring
the map, the user may encounter another robot built by AI or a different
user. The user can choose to challenge the robot. No program is needed, and
every robot should be able to fight automatically if challenged. If user
wins a challenge, they get both rewards and points. There is a global board
displaying the top robots ranked by points. User only needs to log in if
they want to challenge other robots."

Then: "build it with a different code branch so that we can reverse it if
needed" and "create a design doc".

**Scraplands** is a working name. It is not a new game. It is a new front
door on the game that exists: the fight, the garage, the ladder, the
referee, the replays and the seasons are all reused, and the new code is a
map, a drive, a spawner and a box. Everything below is written so that a
`git checkout release/ios-build18` is the whole reversal.

---

## 0. The pitch

You start in a garage with SCRAPPER, already built. The door opens onto a
scrapyard the size of ten arenas. You drive. Crates glint between the
wrecks; drive into one and it opens on the spot: scrap, a part, sometimes
both. Other machines are out there too, idling by their own wrecks — some
are the game's, some belong to real people who enlisted theirs. Drive up to
one and a card appears: name, weight class, record, CHALLENGE. Say yes and
the walls of a pocket arena rise around you both; thirty seconds later one
of you is on its back. Nobody programs anything — every machine drives
itself in a fight, yours included. Win and the crate you were fighting over
is yours. Beat a real person's machine and your name climbs a board they
can see. The board is the only reason to sign in.

---

## 1. Why this, and why now

The funnel (`server/scripts/web_funnel.zsh`) for the seven days to
2026-09-09:

| step | sessions |
|---|---|
| opened the page | 187 |
| game became playable | 108 |
| scene first frame | 55 |
| started a fight | 1 |
| finished a fight | 1 |

Ladder accounts created since 2026-08-16: **0**. Every one of the nine
players who reached the game on 09-08 was a returning career.

Three things follow, and the map answers all three:

1. **Nothing pulls a player from the first frame to the first fight.** A
   map with visible crates is a pull that needs no instruction. "Drive to
   the shiny thing" is understood by everyone who has held a phone.
2. **The program is our deepest feature and our tallest wall.** The
   Rookie Warm-Up cut the taps-to-fight to 3–4, and one person a week
   takes them. Making *every* robot fight itself, with the program as an
   optional garage upgrade rather than a prerequisite, keeps the autonomy
   fight as the product and removes the wall.
3. **Nobody fights a real person's machine.** The ladder holds validated
   snapshots and a worker that fights them, and the career never draws
   from it (`CATS_Gap_Analysis_Plan_2026-09-07.md` gap 4). Robots parked
   on the map are that pool with a face on it. This is the CATS lesson
   that matters most: other players' machines are free, endless content.

The `CATS_Gap_Analysis_Plan` still stands; Scraplands is Steps 2–3 of that
plan (Quick Fight loop, real opponents without a sign-in) rehoused in a
world instead of a menu. Quick Fight itself ships and stays as the fight
profile the map uses.

---

## 2. What is reused, and what is new

| piece | today | in Scraplands |
|---|---|---|
| the fight | `FightManager` — 30-s Quick profile, 5-s count-out, crusher walls at −10 s (`QUICK_*`) | **as is** — every map fight is a Quick bout |
| the drive | `RaycastWheelDrive` (max 10 m/s, μ 1.3, 35° steer), `TouchControls` floating stick (`Phase5Mobile.cs`), WASD | **as is** for input; retuned for minutes of driving, see §3.3 |
| the camera | `FightCamera` follow, clamped to `ARENA_HALF − 0.8` | clamp lifted to the map bounds; otherwise as is |
| the robot | `CompoundRobot`, `STARTER_SNAPSHOT` (core, 2 beams, 4 wheels, battery, spike, compass, wall sensor) | **as is** — the rookie is SCRAPPER |
| the brain | `RobotProgram.RamHunter()` — measured 9/10 vs SCOUT with no player input | the **auto-brain**, picked by what the build carries, §3.6 |
| the garage | BUILD / ROBOTS / SHOP tabs, `Career.Data` (scrap, inventory, stable) | **as is** — the map's GARAGE door opens the dock |
| the box | `RewardBox` ceremony, `Career.QuickBoxRoll` → `qbox:<scrap>:<part>:<mat>:<n>`, `pendingRewards` deferred grant | **as is** — a treasure crate is a `qbox` opened where it stands |
| the cap | `QUICK_BOXES_PER_DAY = 6`, `quickBoxDay` | one daily cap shared by crates and fight boxes |
| opponents | league roster bots (SCOUT, TIPPER, …); ladder snapshots server-side | roster bots **and** a new anonymous pool of real snapshots, §3.5 |
| the referee | Cloud Run worker, `POST /v1/challenges`, `GET /v1/inbox`, replays playable end to end | **as is** — points come only from here, §4 |
| the board | `GET /v1/leaderboard/{category}` (anonymous), ratings 1200-start per weight class, `GET /v1/seasons/standings` | **as is** — POINTS is a label on the rating, §3.7 |
| the wall | none on web; sign-in kept on iOS | login **only** to send a challenge to a real robot, §3.8 |
| the funnel | `open/ready/boot/fight/result/quick/box/streak/return` | + `map`, `crate`, `meet`, `challenge`, `rank` |

**New code, all in new files:** `MapMode.cs` (state, the door in and out),
`MapWorld.cs` (the seeded yard: floor, wrecks, bounds, zones),
`TreasureCrate.cs`, `MapEncounter.cs` (the parked robot, its card, the
challenge), `MapHud.cs`, `MapBench.cs`, `DriveBench.cs`; server:
`GET /v1/pool` and an `api_smoke` section for it. The hooks into existing
files are the door (`BuilderManager.EnterMap()` / `LeaveMap()`, paired like
`EnterMatchArena()` / `BackToBuild()` because `BuildArena()` is not
idempotent) and one button.

**Not built:** terrain, a streamed world, a navmesh, a second economy, a
new fight, gangs, real-time anything.

---

## 3. The design

### 3.1 The first minute

Boot lands in the **garage**, SCRAPPER on the turntable, the dock closed,
one loud button: DRIVE OUT. (The ghost-hand guide from the Rookie Warm-Up
points at it; the rest of the guide is retired on the map branch — the map
is the guide.) The door opens; the stick appears under the thumb; the
first crate is **eight metres away, in view**. The first encounter is
parked thirty metres past it, facing away, and it is **SCOUT, not another
SCRAPPER** — see §9 on mirror lock.

Target, measured by the funnel: a new player opens a crate within 30 s of
`map`, and finishes a fight within 3 minutes. Today one player a week
finishes a fight at all.

### 3.2 The map

- **A flat yard, not terrain.** One `Plane` scaled to **80 × 80 m** for the
  prototype (the arena is 14 × 14), ringed by the same perimeter walls the
  arena uses. Terrain is a data cost we cannot afford on the web (§5) and a
  drive-tuning cost we cannot afford in week one.
- **Wrecks from primitives**, procedurally placed from a seed: slabs,
  pillars, broken beams in the arena's materials. They are cover and
  landmarks, and they are code, not assets.
- **Zones by weight class**, outward from the garage: FEATHER wrecks near
  the door, then LIGHT, MIDDLE, HEAVY, SUPER at the far fence. An encounter
  is drawn from the zone's class, so the rookie meets rookies and can *see*
  the heavies it is not ready for. The weight caps are our "power
  capacity" already; no new constraint.
- **The seed is the date.** Crates and parked robots re-roll at local
  midnight. That is the daily return, without a timer on anything: "the
  yard has new crates tomorrow" (`quickBoxDay` already does the day
  arithmetic).
- **A compass strip** at the top of the HUD: garage, nearest crate, nearest
  encounter, as icons on a bearing line. The map has no minimap in v1.

### 3.3 Driving

- Input is what fights already use: the floating stick on the left half
  (`TouchControls`), WASD on desktop.
- Retune, and **bench before believing**: `maxSpeed` 10 m/s crosses the
  yard in 8 s, which is too fast to read; try 6. `diffSteer` 0.8 is tuned
  for shoving; a map drive wants a tighter turn at low speed. Both are
  fields on `RaycastWheelDrive`, both get a **`DriveBench`**: drive from
  the garage to each of 12 seeded waypoints; pass if every leg arrives
  within 1.5× straight-line time at the tuned speed and no leg reads
  "stuck" (position unchanged 2 s under throttle). Run it on both legs of
  any tuning change.
- Battery does **not** drain on the map in v1. Draining it makes the map a
  resource puzzle before it is a place; owen's call (§8).
- Righting: a map robot that lands on its back after clipping a wreck must
  self-right or the run ends in a ditch. `SensorBus` already knows "needs
  righting"; on the map, holding the stick for 1 s while inverted flips
  the robot (a free, visible mercy that the fight does not offer).

### 3.4 Treasure crates

- A crate is a `qbox`. Drive into it: it cracks open in place with the
  `RewardBox` ceremony, the grant is exact and self-describing, and the
  funnel gets `crate`.
- **Roll:** the zone sets the material band (FEATHER zone rolls ABS and
  aluminium; SUPER rolls tungsten and carbon). Scrap always; a part on
  two crates in three; the second part only with a crown.
- **Cap:** the daily cap counts crates *and* fight boxes together —
  `quickBoxesToday` is the meter. Past the cap a crate still pays scrap,
  so the drive is never pointless; the *parts* wait for tomorrow.
- **Count:** 8 crates per day in the prototype, scattered so that at most
  3 are in view from anywhere. Tune from `crate` per session.

### 3.5 Encounters

A parked robot is a **`MapEncounter`**: a real `CompoundRobot` built from a
snapshot, idling (wheels twitch, core glows) beside a wreck. Drive within
4 m and its **card** slides up: name, weight class, W–L, a one-line owner
tag (`by <name>` for a real robot, `yard bot` for ours), and CHALLENGE.

Two kinds of robot, one card:

- **Yard bots** — the roster the leagues already have (SCOUT, TIPPER,
  BULWARK, MILLSTONE, …), placed by zone. They are the content until the
  pool is deep, and the first three encounters of a new career are always
  yard bots so the first fight is winnable and not a mirror.
- **Enlisted robots** — real players' validated snapshots from the ladder.
  Today `GET /v1/robots` and `GET /v1/snapshots/{id}` require a login.
  The map needs a **new anonymous read, `GET /v1/pool?category=&n=`**,
  returning up to *n* validated snapshots for a weight class with the
  owner's display name and record and **without the program** — the
  privacy policy promises programs stay private, and the map never needs
  one because of §3.6. Rate-limited like `upload`, cached on the client
  per day (the seed), served from the snapshots the worker already
  validated. Seeded from the ladder's existing accounts and the champions
  bake until real enlistment fills it.

**Decline is free.** Drive away and the card folds. Nothing on the map
attacks you.

### 3.6 The challenge, and the auto-brain

Accept and the pocket arena rises where you stand: the existing
`BuildArena()` at the encounter's position (the camera and bounds re-clamp;
the wrecks under the footprint are lifted for the bout), or, if that fights
the non-idempotent arena for too long in week one, a cut to the standard
arena and back. Either way it is a **Quick bout**: 30 s, 5-s count-out,
walls at −10 s, `Career.SettleQuickFight()` at the bell.

**No program is needed.** Both robots fight under an **auto-brain chosen by
what the build carries**, the player's included:

| build carries | brain | note |
|---|---|---|
| compass + wall sensor (SCRAPPER) | `RamHunter` | measured 9/10 vs SCOUT |
| compass only | `Brawler` | needs a Compass tracker |
| wall sensor only | `WallShy` | |
| no sensors | `FirstSteps` | the sensor-free preset; validation already accepts it |
| a saved program on the robot | the player's program | the garage upgrade; opt-in |

The player's own saved program overrides the auto-brain — that is where
autonomy programming lives now: an *upgrade* discovered in the garage,
never a prerequisite. The PROGRAM tab stays.

**Manual driving in a fight is out of the map mode.** One control scheme
per screen: you drive on the map, the brain drives in the ring. (The
DRIVE-stick-in-autonomy bug of 09-05 is exactly the confusion to avoid.)
MANUAL FIGHT stays available from the league door for people who want it.

### 3.7 Rewards, points, the board

Two currencies of outcome, deliberately separated (§4 says why):

- **Rewards** come from every fight, yard bot or enlisted, fought
  **locally**: purse 20–60 scrap, the streak meter, 3 wins = a toolbox,
  5 = a crown — `SettleQuickFight` unchanged.
- **Points** come only from **refereed** fights against enlisted robots,
  and only for a signed-in player. POINTS is the per-class rating the
  server already keeps (1200 start), shown as an integer. The board is
  `GET /v1/leaderboard/{category}`, already anonymous, already rendered on
  the site's game page; in-game it is one screen off the garage: top 20 per
  class, your row pinned, the season's end date from `seasons/standings`.
- **A challenge to an enlisted robot, signed in, does two things:** it
  plays the local bout at once (so there is always something to watch and
  a reward to claim), and it `POST /v1/challenges` — the referee fights it
  within the worker's cadence (Cloud Scheduler, 5 min, the $/latency dial in
  `CLAUDE.md`), and the verdict lands in `GET /v1/inbox` with a replay.
  The card says *"sent to the referee — points on their verdict"*, and the
  inbox badge lights when it is back. The local bout is labelled
  UNOFFICIAL on its result card. See §8, call 2: owen may prefer to drop
  the local bout for refereed challenges and show only the wait.

### 3.8 The login gate

Everything is a guest's: the map, crates, yard bots, enlisted robots
fought locally for rewards, the garage, the program tab. **Sign in for one
thing: to send a challenge to a real robot**, which puts your name on the
board and (by the same enlist step that exists today) parks *your* robot
in other people's yards. The wall copy is the reason, not a demand:

> "Beating **Spinner1** for points puts your name on the board, and parks
> SCRAPPER in their yard. Sign in to send the challenge."

The web sign-in wall measured 95 % bounce when it stood at the door. Here
it stands at the moment the player wants the thing it gives.

### 3.9 The garage

Unchanged: BUILD / ROBOTS / SHOP / PROGRAM, the dock, the checklist rewards
(retargeted: first crate, first challenge, first win, first upgrade). A
new **DRIVE OUT** button, and the map's GARAGE door returns here. Career
fights (the leagues) keep their door in the dock; on the map branch they
are the "long game", not the front door.

---

## 4. Economy and trust

- **Local fights pay rewards; only the referee pays points.** A client can
  be made to report any result; it cannot make the worker fight a rigged
  bout. Every refereed fight re-validates both snapshots server-side (mass,
  class, legality), as today. Separating the two currencies means cheating
  the client earns scrap in a single-player economy and *nothing* on the
  board.
- **Crates are a faucet.** They are client-side scrap and parts, like every
  reward the game grants today; the server wallet
  (`Server_Economy_Design_2026-08-13.md`) is deployed and unused by the
  client, and this design does not change that. Inventory provenance for
  ranked robots stays an **open hole**: a modified client can build a
  tungsten robot from nothing and enlist it. The category cap bounds the
  damage (a FEATHER robot is a FEATHER robot whatever it is made of), and
  the fix is the boot gate + server inventory already tracked in that doc,
  not a Scraplands task.
- **Programs stay private.** The pool endpoint never ships one; map fights
  use the auto-brain; enlisted robots with a saved program fight under
  their *owner's* program only in the referee, where it already lives.
  Replays and rankings are public, as the policy says. No change to the
  policy is needed for v1.
- **No timers, no hard currency, no ads** in v1 — the non-gaps list of the
  CATS plan stands.

---

## 5. The web build budget

The CrazyGames package today: **7.8 MB initial** (wasm 6.3, data 1.4,
framework 0.1), 14 MB with the music. Half of web sessions never reach the
first frame, and 45 % never become playable; anything that grows the
download costs players before it earns any.

- **Rule:** the map adds **code, not data**. Wrecks are primitives; the
  yard is a plane; crates and cards reuse the dock's atlas. Budget: data
  file ≤ 2.5 MB, initial ≤ 9 MB, measured on every build by the stamp step
  in `BuildWebGL.cs`. CrazyGames' mobile-homepage bar is 20 MB; ours is
  self-imposed and tighter because the funnel says so.
- A map that needs an art pass later loads it *after* `boot`, the way the
  music does today.

---

## 6. Measurement

**Funnel events** (`index.html` beacon + `web_funnel.zsh`): `map` (drove
out), `crate` (opened one), `meet` (a card shown), `challenge` (accepted),
`rank` (a refereed challenge sent). The dead steps `door/build/saved` come
out of the table. The step the plan must move first is `map → crate`
within 30 s; the one it must move most is `boot → challenge`.

**Benches, all under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, all holding
`Career.SuspendAutosave()` and swapping `Career.Data` — owner state is
sacred and `FightManager.End()` still calls `Progression.OnMatchEnd`
unconditionally:**

- `DriveBench` — 12 waypoints, arrival within 1.5× straight-line, no
  stuck leg; run on both legs of every tuning change.
- `MapBench` — the seed is deterministic (same date → same yard, twice);
  8 crates and N encounters spawn inside the fence and outside every
  wreck; the first three encounters are yard bots and none is SCRAPPER;
  a crate opens once, grants exactly its id, counts against the cap, and
  past the cap pays scrap only; an accepted challenge enters a Quick bout
  (`FightManager.quickBout`, 30-s clock) and returns to the map with the
  arena torn down; the ledger still audits.
- `BrainPickBench` — the §3.6 table: for each of the five build shapes
  the chosen brain validates against the build ("needs a Wall sensor"
  never fires on the map).
- `api_smoke` section for `GET /v1/pool`: anonymous, filtered by class,
  never returns a `program` field, rate-limited, empty class returns `[]`
  not 500.
- The existing suite stays green: CareerSmoke at the control's floor,
  TouchSmoke, QuickFightBench 24/0, `ReturningPlayerBench` (the inbox
  refetch after a refereed challenge is exactly its shape).

Run headless via `BatchSmoke` from a **worktree**, never a second editor on
owen's project.

---

## 7. Milestones

Each has an exit that can fail. Predict before measuring, in the doc.

**M0 — the prototype (week 1).** 80 × 80 yard, 3 crates, 1 yard-bot
encounter (SCOUT), stick + WASD drive, follow camera, DRIVE OUT / GARAGE
doors, `map` and `crate` events, in the web build.
*Exit:* initial ≤ 9 MB; DriveBench green; on the live site, of sessions that
reach `map`, **median time on the map > 60 s** and **≥ 50 % open a crate**.
*Prediction:* the drive will feel too fast and the camera too close on a
phone; expect two tuning rounds. If people who reach the map leave it in
under a minute, **stop here** — the premise (driving around is fun before
the fight) is false and no amount of economy fixes it.

**M1 — the loop (weeks 2–3).** Seeded daily yard, zones, 8 crates, the
cap, encounter cards, the pocket arena, Quick bouts with the auto-brain
table, rewards, the checklist retargeted, MapBench + BrainPickBench.
*Exit:* fights per map session ≥ 2; `return` among map sessions ≥ 30 %
(vs 11 % today); CareerSmoke/TouchSmoke at their floors.

**M2 — real people (weeks 4–5).** `GET /v1/pool`, enlisted robots parked
by zone, the sign-in-to-challenge wall with its copy, refereed challenges
via `POST /v1/challenges`, inbox verdicts with WATCH, the board screen.
*Exit:* `api_smoke` green with the pool section; **new ladder accounts
per week > 0** (it has been 0 for 3 weeks); ≥ 1 refereed challenge per
day from a non-bench account.

**M3 — ship (week 6).** iOS **2.3.0** (Quick Fight is not in 2.2.1 either;
both go together), CrazyGames build update (a new version on the same
listing, Brotli-only, ARENA still gated by `RB_PORTAL`), the site's game
page copy.

Six weeks is the estimate for this session's work at the pace of the last
three; M0 is the only one to commit to now.

---

## 8. Owen's calls, in the order they block work

1. **Front door.** Does the map *replace* the boot-to-garage-with-guide
   flow (recommended: yes — two front doors is how we got a league nobody
   enters and an arena nobody signs into), or sit beside it as a tab?
2. **Points on the referee's verdict, with a wait**, or on the local bout,
   instantly and cheatably? Recommended: the referee. The 5-minute cadence
   is a scheduler line and ~$35/mo if it must be instant.
3. **Battery on the map:** no drain (recommended for v1) or drain as pacing.
4. **The numbers:** 8 crates/day, cap 6 boxes shared, yard 80 m, first
   crate at 8 m. All tunable from `crate`/`meet`/`challenge` per session.
5. **The name.** Scraplands is a placeholder.

---

## 9. Risks, and what would make me stop

- **Driving feel** is the largest unmeasured piece. The wheel model was
  tuned for shoving in a 14-m box. M0's exit is the test; DriveBench is the
  regression guard afterwards.
- **Mirror lock is unsolved** (`CLAUDE.md`, not-done #6): two identical
  robots meet nose to nose and mutually disarm ~43 % of the time. A yard
  full of rookies fighting rookies is the mirror case by construction.
  Mitigations already in the design: the first encounters are never
  SCRAPPER, zones mix the roster, and the crusher walls end a stalled bout
  at 30 s instead of the judges. Measure it: MatrixBench at the Quick clock
  before M1 ships.
- **A map is a content treadmill** for one person. The yard is procedural
  and the content is the robots; if M1's `return` does not move, the answer
  is more *robots* (the pool), not more *map*.
- **Load size.** The rule in §5 is a gate on every build, not a hope.
- **The referee wait** may read as broken to a player used to instant
  results. The local UNOFFICIAL bout is the mitigation; the inbox badge is
  the return hook.
- **Auto-brain fairness.** An enlisted robot built for its owner's program
  fights under the auto-brain in a guest's yard and may look worse than it
  is. It is labelled a *local* bout and pays no points; the refereed fight
  uses the owner's program. Acceptable for v1; say so on the card.

---

## 10. Branch and reversibility

- Branch **`feature/scraplands`** from the head of `release/ios-build18`
  (`51f8bac`). All new files; hooks into existing files are the two doors
  and one button, each behind `MapMode.Enabled`, so the old boot path is
  one flag away even on the branch.
- Web deploys from the branch go to a **separate path** on the site
  (`/play/rb-map/`) until M1's exit is met, so the current game keeps its
  funnel and its URL. The CrazyGames listing is not touched before M3.
- Pushes to the side branch only; never `main`; never force. Docs for the
  branch live here and in a progress log at the foot of this file.
- Reversal is `git checkout release/ios-build18`. Nothing on the branch
  migrates `Career.Data` in a way the old build cannot read: new fields are
  additive with defaults, as `quick*` were.

## Progress log

- **2026-09-09** — design written; nothing built. Next: M0.
