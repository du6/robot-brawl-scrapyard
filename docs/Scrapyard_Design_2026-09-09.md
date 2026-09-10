# Robot Brawl: Scrapyard — a new game from the same base (design, 2026-09-09)

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

And, after a first draft that put this on a branch of Robot Brawl: "I still
prefer to build a brand new game so that the current game is not impacted.
It feels very different even though a lot of things can be shared. We can
create a new game but branched from the same base."

So: **Robot Brawl: Scrapyard** is a **new game in a new repository,
forked from Robot Brawl at `release/ios-build18` = `9d41135`**, with its own
bundle id, store record, web path and portal listing, sharing the ladder
server with Robot Brawl. Robot Brawl is not touched by anything in this
document. The fork is not a branch to be merged back; it is a second game
that starts with all of the first one's code and deletes what it does not
need.

---

## 0. The pitch

You start in a garage with a rookie machine, already built. The door opens
onto a scrapyard the size of ten arenas. You drive. Crates glint between
the wrecks; drive into one and it opens on the spot: scrap, a part,
sometimes both. Other machines are out there too, idling by their own
wrecks — some are the game's, some belong to real people. Drive up to one
and a card appears: name, weight class, record, CHALLENGE. Say yes and the
walls of a pocket arena rise around you both; thirty seconds later one of
you is on its back. Nobody programs anything — every machine drives itself
in a fight, yours included. Win and the crate you were fighting over is
yours. Beat a real person's machine and your name climbs a board they can
see. The board is the only reason to sign in.

There is no title screen, no league table, no tutorial, no program
canvas on the way in. The map is the tutorial.

---

## 1. Why a new game, and why now

Robot Brawl's web funnel (`server/scripts/web_funnel.zsh`) for the seven
days to 2026-09-09:

| step | sessions |
|---|---|
| opened the page | 187 |
| game became playable | 108 |
| scene first frame | 55 |
| started a fight | 1 |
| finished a fight | 1 |

Ladder accounts created since 2026-08-16: **0**. All nine players who
reached the game on 09-08 were returning careers.

Three things follow, and the map answers all three:

1. **Nothing pulls a player from the first frame to the first fight.** A
   map with visible crates is a pull that needs no instruction.
2. **The program is our deepest feature and our tallest wall.** The Rookie
   Warm-Up cut taps-to-fight to 3–4 and one person a week takes them.
   Making *every* robot fight itself keeps the autonomy fight as the
   product and removes the wall.
3. **Nobody fights a real person's machine.** The ladder holds validated
   snapshots and a worker that fights them, and no career draws from it.
   Robots parked on the map are that pool with a face on it — other
   players' machines are free, endless content, the CATS lesson that
   matters most (`CATS_Gap_Analysis_Plan_2026-09-07.md`).

**Why a fork and not a branch.** A branch has to coexist with the title
screen, the five leagues, the rookie guide, the ARENA tab, six dock tabs
and a 7,900-line builder that serves all of them. A new game deletes those
on day one and gets *smaller*. The other thing a fork protects is the
shipped iOS app's stability and review record, which is worth protecting
even though its traffic is not. The cost is stated plainly in §7: every
fix to the shared fight core lands twice from now on.

---

## 2. Shared, deleted, new

### 2.1 Shared — copied by the fork, kept textually identical for as long as possible

| piece | files | note |
|---|---|---|
| the physics robot | `CompoundRobot`, `MatDB`, `RaycastWheelDrive`, `Actuator`, `PartVisualFactory`, `P1PartDef` | the fight core; changes here land in both games |
| the fight | `FightManager` (Quick profile: 30 s, 5-s count-out, crusher walls at −10 s), `DamageResolver`, `ArenaHazards`, `FightCamera` | Scrapyard uses **only** the Quick profile |
| the brain | `ProgramRunner`, `SensorBus`, `RobotProgram` presets, `AIController` | the auto-brain table, §3.6 |
| the drive input | `TouchControls` (`Phase5Mobile.cs`), WASD | retuned for minutes of driving |
| the garage | BUILD / ROBOTS / SHOP / PROGRAM panels of `MobileBuilderUI`, `BuilderManager`'s build half | the map's GARAGE door |
| the economy | `Career` (scrap, inventory, stable, `pendingRewards`, `QuickBoxRoll`, `SettleQuickFight`, the daily cap), `RewardBox` | crates are `qbox`es |
| the roster | `EnemyRoster` (SCOUT, TIPPER, BULWARK, MILLSTONE…) | yard bots |
| snapshots + ladder | `RobotSnapshot`, `RobotCategory`, `LadderClient`, `MatchRunner`, `RobotWorker`, `FightWorkerLoop` | the referee fights this game's robots too |
| the benches | the fight and ladder benches, `QuickFightBench`, `TouchSmoke`, `BatchSmoke` | §6 lists which survive |

### 2.2 Deleted on day one

The point of the fork. Each is a hard delete, not a flag:

- the **title screen** and `StartCareer()` as the only creator of a
  `BuilderManager` (boot creates the garage directly);
- the **five leagues**, contests, medals, `Progression`'s league ladder,
  the LEAGUE tab and its board, the champion bake;
- the **Rookie Warm-Up**: `RookieGuide`, the ghost hand, the four-task
  checklist (the map replaces it; a smaller checklist returns in §3.9);
- the **ARENA tab** and `ArenaScreen`'s sign-in wall (the board and the
  one-moment login of §3.8 replace it);
- **MANUAL FIGHT** and **TEST DRIVE** as modes (the map *is* the drive;
  the fight is always autonomous);
- the **web sign-in gate**, the `RB_PORTAL` define (nothing to gate);
- every bench that drives a deleted screen (§6).

Expect `BuilderManager` and `MobileBuilderUI` to lose a third to a half of
their lines. Delete in one commit per system, with the surviving benches
green after each, so the fork's history reads as a record of what went and
why.

### 2.3 New

`MapMode.cs` (state; the garage/map doors), `MapWorld.cs` (the seeded
yard: floor, wrecks, fence, zones), `TreasureCrate.cs`, `MapEncounter.cs`
(the parked robot, its card, the challenge), `MapHud.cs` (stick, compass
strip, doors), `BrainPick.cs` (the auto-brain table), `MapBench.cs`,
`DriveBench.cs`, `BrainPickBench.cs`. Server, shared with Robot Brawl:
`GET /v1/pool`, the `yard` ruleset (§7.3), and their `api_smoke` sections.

**Not built:** terrain, a streamed world, a navmesh, a second economy, a
new fight, gangs, real-time anything, a second server.

---

## 3. The design

### 3.1 The first minute

Boot lands in the **garage**: the rookie on the turntable, dock closed,
one loud button, DRIVE OUT. The door opens; the stick appears under the
thumb; the first crate is **eight metres away, in view**. The first
encounter is parked thirty metres past it, facing away, and it is **SCOUT,
not another rookie** — see §10 on mirror lock.

The rookie is Robot Brawl's `STARTER_SNAPSHOT` (core, two beams, four
wheels, battery, spike, compass, wall sensor). It is a proven fighter
(`StarterBench`, 9/10 vs SCOUT under RamHunter) and its sensors are what
make the auto-brain good; a new game does not need a new rookie.

Target, from the funnel: a new player opens a crate within 30 s of `map`
and finishes a fight within 3 minutes.

### 3.2 The map

- **A flat yard, not terrain.** One `Plane` at **80 × 80 m** for the
  prototype (the arena is 14 × 14), ringed by the arena's perimeter walls.
  Terrain is a data cost the web cannot afford (§5) and a drive-tuning cost
  week one cannot.
- **Wrecks from primitives**, placed from a seed: slabs, pillars, broken
  beams in the arena's materials. Cover and landmarks; code, not assets.
- **Zones by weight class**, outward from the garage: FEATHER wrecks by
  the door, then LIGHT, MIDDLE, HEAVY, SUPER at the far fence. Encounters
  are drawn from the zone's class, so the rookie meets rookies and can
  *see* the heavies it is not ready for. The class caps are the game's
  "power capacity"; no new constraint.
- **The seed is the date.** Crates and parked robots re-roll at local
  midnight — the daily return without a timer on anything ("the yard has
  new crates tomorrow"; `quickBoxDay` already does the day arithmetic).
- **A compass strip** across the top: garage, nearest crate, nearest
  encounter, as icons on a bearing line. No minimap in v1.

### 3.3 Driving

- Input is the floating stick on the left half (`TouchControls`) and WASD.
- Retune, and **bench before believing**: `maxSpeed` 10 m/s crosses the
  yard in 8 s, too fast to read; start at 6. `diffSteer` 0.8 was tuned for
  shoving; a map drive wants a tighter turn at low speed. Both are fields
  on `RaycastWheelDrive`; both get **`DriveBench`** (§6). Run it on both
  legs of every tuning change.
- Battery does **not** drain on the map in v1 (owen's call, §9).
- **Righting.** A robot that lands on its back after clipping a wreck must
  self-right or the run ends in a ditch. On the map, holding the stick 1 s
  while inverted flips it — a visible mercy the fight does not offer.

### 3.4 Treasure crates

- A crate is a `qbox`. Drive into it: it cracks open in place with the
  `RewardBox` ceremony, the grant is exact and self-describing, and the
  funnel gets `crate`.
- **Roll:** the zone sets the material band (FEATHER rolls ABS and
  aluminium; SUPER rolls tungsten and carbon). Scrap always; a part on two
  crates in three; a second part only with a crown.
- **Cap:** one daily cap for crates *and* fight boxes — `quickBoxesToday`
  is the meter. Past the cap a crate still pays scrap, so the drive is
  never pointless; the *parts* wait for tomorrow.
- **Count:** 8 per day in the prototype, at most 3 in view from anywhere.

### 3.5 Encounters

A parked robot is a `MapEncounter`: a real `CompoundRobot` from a
snapshot, idling (wheels twitch, core glows) beside a wreck. Within 4 m its
**card** slides up: name, class, W–L, an owner tag (`by <name>` for a real
robot, `yard bot` for ours), CHALLENGE.

- **Yard bots** — Robot Brawl's roster, placed by zone. The content until
  the pool is deep; the first three encounters of a new save are always
  yard bots, so the first fight is winnable and not a mirror.
- **Enlisted robots** — real players' validated snapshots from the shared
  ladder, from both games. `GET /v1/robots` and `GET /v1/snapshots/{id}`
  require a login today; the map needs a **new anonymous read,
  `GET /v1/pool?category=&n=`**: up to *n* validated snapshots for a class,
  with owner display name and record, **without the program** (the privacy
  policy promises programs stay private; the map never needs one, §3.6).
  Rate-limited like `upload`; cached on the client per day (the seed);
  served from snapshots the worker already validated.

**Decline is free.** Drive away and the card folds. Nothing attacks you.

### 3.6 The challenge, and the auto-brain

Accept and the pocket arena rises where you stand: `BuildArena()` at the
encounter's position with the camera and bounds re-clamped and the wrecks
under the footprint lifted — or, if that fights the non-idempotent arena
too long in week one, a cut to the standard arena and back. It is a
**Quick bout**: 30 s, 5-s count-out, walls at −10 s, `SettleQuickFight()`
at the bell.

**No program is needed.** Both robots fight under an auto-brain chosen by
what the build carries:

| build carries | brain | note |
|---|---|---|
| compass + wall sensor (the rookie) | `RamHunter` | measured 9/10 vs SCOUT |
| compass only | `Brawler` | needs a Compass tracker |
| wall sensor only | `WallShy` | |
| no sensors | `FirstSteps` | the sensor-free preset |
| a saved program on the robot | the player's program | the garage upgrade; opt-in |

A saved program overrides the auto-brain: that is where autonomy
programming lives in this game — an upgrade found in the garage, never a
prerequisite. The PROGRAM tab stays in the garage.

**The player never drives in a fight.** One control scheme per screen: you
drive on the map, the brain drives in the ring.

### 3.7 Rewards, points, the board

Two currencies of outcome, deliberately separated (§4):

- **Rewards** from every fight, yard bot or enlisted, fought **locally**:
  purse 20–60 scrap, the streak meter, 3 wins = a toolbox, 5 = a crown —
  `SettleQuickFight` unchanged.
- **Points** only from **refereed** fights against enlisted robots, only
  for a signed-in player. POINTS is the per-class rating the server keeps
  (1200 start), shown as an integer. The board is
  `GET /v1/leaderboard/{category}` (anonymous already): top 20 per class,
  your row pinned, the season's end from `seasons/standings`. It is one
  screen off the garage.
- **A signed-in challenge to an enlisted robot does two things:** plays the
  local bout at once (something to watch, a reward to claim) and
  `POST /v1/challenges` with the `yard` ruleset. The referee is an
  ALWAYS-ON Cloud Run worker pool (`deploy_worker_live.zsh`, since
  2026-08-14; the 5-minute scheduler job is the paused fallback), so the
  verdict lands in `GET /v1/inbox` with a replay in seconds, not minutes. The card says
  *"sent to the referee — points on their verdict"*; the local result card
  is labelled UNOFFICIAL; the inbox badge lights when the verdict is back.

### 3.8 The login gate

Everything is a guest's: the map, crates, yard bots, enlisted robots fought
locally, the garage, the program tab. **Sign in for one thing: to send a
challenge to a real robot**, which puts your name on the board and parks
*your* robot in other people's yards (the enlist step Robot Brawl already
has). The wall copy is the reason, not a demand:

> "Beating **Spinner1** for points puts your name on the board, and parks
> your machine in their yard. Sign in to send the challenge."

Robot Brawl's web wall bounced 95 % when it stood at the door. Here it
stands at the moment the player wants the thing it gives. Accounts are the
shared ladder's accounts: a Robot Brawl player signs in to Scrapyard with
the same name and the same robots.

### 3.9 The garage

BUILD / ROBOTS / SHOP / PROGRAM, the dock, unchanged from Robot Brawl. New:
DRIVE OUT, a BOARD door, and a four-line checklist that pays once — first
crate, first challenge, first win, first upgrade — reusing the reward
plumbing the old checklist used.

---

## 4. Economy and trust

- **Local fights pay rewards; only the referee pays points.** A client can
  be made to report any result; it cannot make the worker fight a rigged
  bout. Every refereed fight re-validates both snapshots server-side (mass,
  class, legality). Cheating the client earns scrap in a single-player
  economy and *nothing* on the board.
- **Crates are a faucet**, client-side like every reward today. The server
  wallet (`Server_Economy_Design_2026-08-13.md`) stays unused by the client
  in v1. Inventory provenance for ranked robots stays an **open hole** in
  both games: a modified client can build a tungsten robot from nothing
  and enlist it. The class cap bounds the damage; the fix is the boot gate
  + server inventory tracked in that doc, shared by both games when it
  comes.
- **Programs stay private.** The pool never ships one; map fights use the
  auto-brain; an enlisted robot's own program fights only in the referee,
  where it already lives. No change to the policy for v1.
- **No timers, no hard currency, no ads** in v1.

---

## 5. The web build budget

Robot Brawl's portal package: **7.8 MB initial** (wasm 6.3, data 1.4,
framework 0.1). Half of its web sessions never reach a first frame and 45 %
never become playable. The fork starts smaller (the deletions) and stays
that way:

- **Rule:** the map adds **code, not data**. Wrecks are primitives; the
  yard is a plane; crates and cards reuse the dock's atlas. Budget: data
  ≤ 2.5 MB, initial ≤ 9 MB, measured by the stamp step in `BuildWebGL.cs`
  on every build. CrazyGames' mobile-homepage bar is 20 MB; ours is tighter
  because the funnel says so.
- Any later art pass loads *after* `boot`, the way the music does.

---

## 6. Measurement

**Funnel events** (the beacon in the web template + `web_funnel.zsh`, which
the fork copies and trims): `open ready boot map crate meet challenge rank
box streak return`. The step to move first is `map → crate` within 30 s;
the one to move most is `boot → challenge`. Scrapyard's beacon path is its
own (`/v1/beacon/scrapyard-play`), so the two games' funnels never mix.

**Benches.** All under `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, all holding
`Career.SuspendAutosave()` and swapping `Career.Data` — owner state is
sacred, and `FightManager.End()` still calls `Progression.OnMatchEnd`
unconditionally (or its trimmed successor).

New:
- `DriveBench` — 12 seeded waypoints from the garage; pass if every leg
  arrives within 1.5× straight-line time at the tuned speed and no leg
  reads "stuck" (position unchanged 2 s under throttle).
- `MapBench` — same date → same yard, twice; 8 crates and N encounters
  inside the fence and outside every wreck; the first three encounters are
  yard bots and none is the rookie; a crate opens once, grants exactly its
  id, counts against the cap, past the cap pays scrap only; an accepted
  challenge enters a Quick bout (`FightManager.quickBout`, 30-s clock) and
  returns to the map with the arena torn down; the ledger still audits.
- `BrainPickBench` — the §3.6 table: each build shape's brain validates
  against the build ("needs a Wall sensor" never fires on the map).
- `api_smoke`: `GET /v1/pool` is anonymous, class-filtered, never returns
  a `program` field, rate-limited, empty class → `[]`; a `yard` challenge
  is fought by the worker on the Quick clock and settles rating.

Carried from Robot Brawl: `QuickFightBench`, `TouchSmoke` (trimmed to the
surviving screens), `VerbBench`, `AutonomyBench`, `DisarmBench`,
`MatrixBench`, `ProgramBench`, `CategoryBench`, `ReplayBench`,
`LadderClientBench`, `EnlistLiveBench`, `ReturningPlayerBench` (the inbox
refetch after a refereed challenge is exactly its shape), `WorkerBench`,
`FightWorkerBench`, `HazardBench`.

Deleted with their screens: `CareerSmoke` (leagues, guide, checklist),
`CareerBench` (league balance), `ChallengeBench`, `EnlistUiBench`
(ARENA dock), `ArenaShots`, `MedalDev`, `StarterBench`'s guide half,
`PlaytestBench`'s league paths. Headless runs via `BatchSmoke` from a
**worktree**, never a second editor on owen's project.

---

## 7. The fork

### 7.1 Repository and identity

| | Robot Brawl | Scrapyard |
|---|---|---|
| repo | `du6/robot-brawl` | **`du6/robot-brawl-scrapyard`** (new; first commit = the fork point `9d41135`, second = the doc, then one delete per system) |
| local | `~/Setup Guide In-Editor Tutorial` | `~/robot-brawl-scrapyard` — a clone, **not** a worktree (building switches the active target; this project's is iOS) |
| Unity | 6000.5.4f1 | same |
| productName | Robot Brawl: Bolt & Blade | Robot Brawl: Scrapyard |
| bundle id | `club.cyberduck.robotbrawl` | `club.cyberduck.scrapyard` |
| App Store | id6801680303 | a new app record; new screenshots, new review |
| web | `cyberduck.club/play/rb/` | `cyberduck.club/play/scrapyard/`; own `index.html` template, own beacon path |
| CrazyGames | listing b91c802d… (awaiting review) | a second submission when M2's exit is met; Basic Launch, Brotli-only, no login on the way in — nothing to gate |
| the site | `/robot-brawl/` page | `/scrapyard/`; the homepage lists two games |
| save file | `robotbrawl_career.json` | `scrapyard_save.json` — a new save, no migration; `CareerData` is copied and trimmed |

Owen's Robot Brawl career save is untouched by construction: the new game
reads and writes a different path.

### 7.2 What the fork keeps textually identical, and the backport rule

The fight core — `CompoundRobot`, `MatDB`, `RaycastWheelDrive`, `Actuator`,
`DamageResolver`, `ProgramRunner`, `SensorBus`, `RobotProgram`,
`RobotSnapshot`, `RobotCategory`, `MatchRunner`, `RobotWorker`,
`FightWorkerLoop` — stays **byte-identical** in both repos for as long as
possible, so that `diff` between the two stays readable. The reason is in
`CLAUDE.md`: `ProgramRunner` and `SensorBus` have a history of fixes that
regress each other, and from now on every such fix lands twice.

The rule: **backport fixes, never features**, in both directions, by
commit reference in the message ("port of robot-brawl `abc1234`"). A
change to any file in the list above is not done until it is in both
repos with both suites green. `RaycastWheelDrive`'s map retune (§3.3) is
the first deliberate divergence and is kept behind a `MapMode.Enabled`
check inside the file rather than a fork of it, so the fight's wheel
behaviour stays identical across games and the referee's bouts stay fair.

### 7.3 The shared server

One API, one database, one worker, one set of accounts, ratings and
seasons. Robot Brawl and Scrapyard robots are in one pool and can be
matched against each other; the class caps make that fair, and the more
robots the pool holds the better both games are. Three server changes, all
in `du6/robot-brawl` (the CLI owns `server/**`), deployed once:

1. **`GET /v1/pool`** (§3.5), anonymous, `api_smoke` section.
2. **The `yard` ruleset.** `matches.arena` already exists (`TEXT NOT NULL
   DEFAULT 'league'`) and the worker never reads it for rules.
   `POST /v1/challenges` accepts `arena: "yard"`; `MatchRunner` reads it
   and fights a `yard` match on the Quick profile (30 s, 5-s count-out,
   walls at −10 s, one bout) instead of one bout at 90 s. Rating settles
   the same way. **No CHECK on the column:** it already carries
   `league_night` (the admin endpoint) and `synthetic` (api_smoke writes it
   directly), so the API validates the request field instead
   (league | yard | omitted). Found while building it, 2026-09-09.
3. **A `game` tag on snapshots** (`'rb' | 'scrapyard'`, default `'rb'`) so the
   pool and the board can filter or badge by origin. Not a ruleset; a
   label.

The worker container is built from a clone of `du6/robot-brawl`
(`build_worker.zsh`) and keeps being; it fights both games' robots
because the fight core is identical (§7.2). **If the cores ever diverge,
the worker must be built from the game whose ruleset it is fighting** —
that is the moment to stop and reconsider one server.

### 7.4 What the fork costs

- Every fight-core fix lands twice (§7.2). Bounded by the byte-identical
  rule and the backport-by-reference rule.
- Two store records, two review pipelines, two web builds, two funnels,
  two suites. Each is a script that already exists, copied.
- The site and the homepage present two games. A launch moment for the
  second one, which is also its opportunity.
- Traffic is not split, because there is almost none to split (five web
  sessions a day). The thing protected is the shipped app's stability.

---

## 8. Milestones

Each has an exit that can fail. Predict before measuring, in the doc.

**M0 — the fork and the prototype (weeks 1–2).**
1. Server first: `GET /v1/pool` + the `yard` ruleset + the `game` tag,
   `api_smoke` green, deployed. Robot Brawl is unaffected (its challenges
   default to `league`).
2. `du6/robot-brawl-scrapyard` from `9d41135`; one delete commit per system in §2.2
   with the surviving suite green after each; new identity, save path,
   beacon path, web path.
3. The prototype: 80 × 80 yard, 3 crates, 1 yard-bot encounter (SCOUT),
   stick + WASD, follow camera, DRIVE OUT / GARAGE doors, `map` and
   `crate` events, deployed to `/play/scrapyard/`.
*Exit:* initial ≤ 9 MB; DriveBench green; on the live path, of sessions
that reach `map`, **median time on the map > 60 s** and **≥ 50 % open a
crate**.
*Prediction:* the drive will feel too fast and the camera too close on a
phone; expect two tuning rounds. **Stop rule:** if people who reach the map
leave it in under a minute, the premise — driving around is fun before the
fight — is false, and no economy fixes it.

**M1 — the loop (weeks 3–4).** Seeded daily yard, zones, 8 crates, the
cap, encounter cards, the pocket arena, Quick bouts with the auto-brain
table, rewards, the four-line checklist; MapBench + BrainPickBench.
*Exit:* fights per map session ≥ 2; `return` among map sessions ≥ 30 %
(Robot Brawl: 11 %); TouchSmoke at its floor.

**M2 — real people (weeks 5–6).** Enlisted robots from the pool parked by
zone, the sign-in-to-challenge wall, refereed `yard` challenges, inbox
verdicts with WATCH, the BOARD screen.
*Exit:* `api_smoke` green with the pool and yard sections; **new ladder
accounts per week > 0** (it has been 0 for 3 weeks); ≥ 1 refereed
challenge a day from a non-bench account.

**M3 — ship (week 7).** iOS 1.0 of Scrapyard (new record, new review),
the CrazyGames submission, the site's second game page and the homepage
listing two games. Robot Brawl's own 2.3.0 (Quick Fight) ships on its own
schedule from its own repo.

Seven weeks at the pace of the last three; M0 is the only one to commit to
now.

---

## 9. Owen's calls — all five made, 2026-09-09

| call | decision | what it fixes in this doc |
|---|---|---|
| the name | **Robot Brawl: Scrapyard** | repo `du6/robot-brawl-scrapyard`, bundle `club.cyberduck.scrapyard`, web `/play/scrapyard/`, save `scrapyard_save.json`, beacon `/v1/beacon/scrapyard-play`, referee ruleset `yard`, snapshot `game` tag `scrapyard` |
| points | **the referee's verdict** | §3.7 as written; the local bout is UNOFFICIAL. Correction: the worker is already always-on (2026-08-14), so the wait is seconds |
| the pool | **one pool across both games, badged by `game`** | §3.5, §7.3 as written; the card shows origin |
| battery | **no drain on the map** | §3.3 as written |
| the numbers | **8 crates/day, cap 6 shared, 80 m yard, first crate at 8 m** | §3.2, §3.4; tune from `crate`/`meet`/`challenge` after M0 |

Nothing in M0 is blocked.

---

## 10. Risks, and what would make me stop

- **Driving feel** is the largest unmeasured piece. The wheel model was
  tuned for shoving in a 14-m box. M0's exit is the test; DriveBench is
  the regression guard afterwards.
- **Mirror lock is unsolved** (`CLAUDE.md`, not-done #6): two identical
  robots meet nose to nose and mutually disarm ~43 % of the time, and a
  yard of rookies fighting rookies is that case by construction.
  Mitigations: the first encounters are never the rookie, zones mix the
  roster, and the crusher walls end a stalled bout at 30 s instead of the
  judges. Measure it: MatrixBench at the Quick clock before M1 ships.
- **Fork drift.** The byte-identical rule (§7.2) is a discipline, not a
  mechanism. The day a fix is applied to one repo and forgotten in the
  other, the referee is fighting two physics. The tell is a refereed
  verdict that disagrees with the local UNOFFICIAL bout more often than
  seeds explain; watch for it in the inbox.
- **A map is a content treadmill** for one person. The yard is procedural
  and the content is the robots; if M1's `return` does not move, the
  answer is more *robots* (the pool), not more *map*.
- **Load size.** The §5 rule is a gate on every build, not a hope.
- **The referee wait** is seconds on the always-on pool, minutes only if
  the pool is ever scaled back to the scheduler fallback. The local
  UNOFFICIAL bout covers either; the inbox badge is the return hook.
- **Auto-brain fairness.** An enlisted robot built for its owner's program
  fights under the auto-brain in a guest's yard and may look worse than it
  is. It is a *local* bout and pays no points; say so on the card.

## Progress log

- **2026-09-09, night — M0 step 1 DEPLOYED.** API image `20260909-203659`
  (migration 017 applied at boot, `/v1/pool` answering anonymously in
  production with real robots and no program key, the board badged by
  game); worker image `20260909-204656` on the always-on pool `rb-worker-
  live`, Ready, host up and polling. Bolt & Blade's client is unchanged and
  every old route behaves as before. The career save was restored the same
  night (a plain `cp` went through; the 09-04 refusal was the classifier
  being inconsistent). Next: M0 step 2, the fork — owen creates
  `du6/robot-brawl-scrapyard`.
- **2026-09-09, later** — M0 step 1 built in this repo (`4eb0cbf`):
  migration 017 (`snapshots.game`, the pool index), `GET /v1/pool`, the
  `yard` ruleset on `POST /v1/challenges`, `game` on the board,
  `MatchRunner.quick` set from the worker's claim. Server measured:
  sql_bench 56 → 61/0, api_smoke 356 → 384/0 (same one pre-existing skip).
  Section Y is self-sufficient (see its header for why). Worker half
  measured headlessly from a worktree (`BatchSmoke.FightWorker`):
  FightWorkerBench pure 26 → **28/0**, play 21 → **29/0** — a league claim
  is fought on the 90-s clock with no walls, the same pair under a yard
  claim on the 30-s clock with the walls closed; the career save's mtime
  unchanged. Two duration-based control legs failed first and were the
  CHECK, not the product: bare cores are counted out (10 s / 5 s), and
  spike-less shovers hit the league's own **"called early — no contact in
  30 s"** rule — a rule the yard inherits, worth knowing for §3.6. The
  bout now records the clock it was given and whether the walls closed,
  and that is what is asserted.
- **2026-09-09** — first draft put this on a branch of Robot Brawl
  (`feature/scraplands`); owen chose a new game. Rewritten for the fork.
  Then owen made all five calls (§9): the game is **Robot Brawl:
  Scrapyard**. Nothing built. Next: M0 step 1, the server changes, in
  this repo.
