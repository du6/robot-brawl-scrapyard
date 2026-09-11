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

owen, 2026-09-10, after seeing the first prototype: "The map is not an
arena. It should be a global map like real world. There is no
leagues/program. It is a brand new experience like minecraft, where users
explore the world and collect parts and fight enemies."

You start on a glowing pad in the wastes with a rookie machine, already
built. The world goes on in every direction: rust flats, scrap steppe, ash
fields, wrecks and ruins, made as you drive from a seed that is yours.
Crates glint between the wrecks; drive into one and it opens on the spot:
scrap, a part, sometimes both. Other machines are out there too, idling by
their own wrecks, tougher the further you range. Drive up to one and a card
appears: name, class, CHALLENGE. Say yes and thirty seconds later one of you
is on its back. Nobody programs anything — every machine drives itself in a
fight, yours included. Win and the crate you were fighting over is yours.
Beat a real person's machine and your name climbs a board they can see. The
board is the only reason to sign in.

There is no title screen, no league table, no tutorial, no program canvas
anywhere. The world is the tutorial; the GARAGE button opens the workshop
wherever you are, like an inventory.

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
- **MANUAL FIGHT** as a mode (the fight is always autonomous) and the
  **PROGRAM tab** (there is no program); TEST DRIVE's start routine became
  the world's drive loop rather than being deleted;
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

### 3.2 The world

- **Generated, not drawn.** Terrain is chunked (48 m squares, 25 loaded
  around you, one built per frame as you approach, dropped behind you) and
  made from three octaves of noise on a **per-player seed** saved in the
  career, so your world is yours and persists. Code, not data: the web
  budget (§5) is untouched by the size of the world, which has no edge.
- **Home is flat** for the first 20 m, so the first minute is a drive and
  not a climb; beyond it the ground rolls at driveable slopes.
- **Biomes by region**: rust flats, scrap steppe, ash fields — the ground's
  colour changes as you range, and later its parts and enemies do too.
- **Wrecks and ruins** from each chunk's own seed (the world's seed mixed
  with the chunk's coordinates), so a place looks the same every time you
  come back.
- **Danger grows with distance.** Within ~120 m of home the enemies are
  rookies (SCOUT, TIPPER); to ~300 m veterans (MAULER, RIPPER, MILLSTONE);
  beyond, champions (BULWARK, WIDOWMAKER, BASTION). The world is its own
  weight class.
- **Places worth driving to** (2026-09-10, owen: "it looks like a desert
  with some cubes… add buildings, shops, robot parks, toys, arenas with
  robots fighting each other"). One 150 m cell in five holds nothing; the
  rest hold a PLACE from the seed — a composition, not a prop, that
  **flattens the ground under it** (blended over 14 m), lays a plaza and
  four roads, and does something:
  - **TOWN** — 9–14 buildings with lit window bands, roof masts, a beacon.
  - **TRADING POST** — a shopfront with an amber sign and a **lit pad**;
    drive onto it and the workshop opens on SHOP where you stand.
    GARAGE/DRIVE OUT resume where you left, not at home; the pad re-arms
    only once you are clear of it.
  - **ROBOT PARK** — a fenced garden with a fountain, benches, three
    roster machines on pedestals.
  - **ARENA** — a 12 m ring with walls, stands on two sides, floodlights,
    and **two roster machines fighting each other live** on the game's
    own `AIController`, combat armed, no `FightManager`; a referee
    respawns the pair five seconds after one dies, leaves the ring or
    stays flipped for six. Tougher pairs further from home.
  - **PLAYGROUND** — two ramps, three pushable balls, a turnstile that
    shoves, domes to bounce over.
  The three cells nearest home are fixed — a trading post ~57 m out, a
  park to the east, an arena to the north — so the first ten minutes
  have somewhere to go. Random structures and parked enemies keep out of
  a place's plaza.
- **A compass strip** across the top: bearings to the nearest crate, the
  nearest enemy, **the nearest place**, and HOME; inside a place its name
  and a one-line hint sit under the strip. No minimap in v1.

### 3.3 Driving — point where you want to go

Input is the floating stick on the left half (`TouchControls`, big: the
ring's edge is full lock, a mouse counts) and WASD.

**The stick is a direction, not a wheel** (2026-09-10; owen: "the driving
still feels tricky. Can you check popular mobile driving games and see how
they design the driving experience?"). What the top mobile drivers share —
Asphalt 9's TouchDrive, Mario Kart Tour's auto-accelerate + smart steering,
Real Racing 3's assists-on defaults, Brawl Stars' re-anchoring stick — is
that **the player picks a direction and the game handles the steering**.
So, in `BuilderManager.Drive.cs`:

- The stick's **angle is a heading in the world, relative to the camera**
  (up = away from the camera); its **length is speed**. Push straight up
  and the machine drives straight whatever its nose was doing; a held
  40° turns *to* 40° and stops turning.
- The frame is the camera's heading **the moment the stick is pressed**,
  held until it is released. Measured the other way first: with the frame
  following an auto-recentring camera, "up" meant "wherever the nose
  points" and the machine's own veer went uncorrected.
- **Two control loops**, because the skid-steer's response to steer is
  wildly speed-dependent (full lock at rest pivots at 250–500 deg/s; at
  6 m/s 0.50 turns 15 deg/s and 0.55 breaks grip into a spin): heading
  error → a wanted yaw rate (3 deg/s per degree, capped 140), then a PI on
  yaw rate → steer, capped by speed (1.0 at rest, 0.5 at 6 m/s).
- **Slow down to turn**: throttle eases with the heading error, and a turn
  sharper than 15° above 3.5 m/s brakes first — as a driver does. That is
  what turned a 2.4-s stalling turn into a 0.9-s clean one.
- **Straight back reverses** (a 35° cone, from a crawl); **let go and it
  brakes to a stop** — coasting ran more than 6 s from top speed.
- The chase camera follows a **pivot** with a lagged heading: it swings
  behind the machine fast (140 deg/s) with the stick up or released, only
  slowly (15 deg/s) while the stick points off-axis, so a held angle is a
  heading, not a pirouette.
- **The same stick fights.** A challenge bout puts the player's side on
  the AI channel at the bell and feeds it from here; FIRE goes to the
  actuators. One control scheme for the whole game.

Measured (MapBench, 2026-09-10): stick up for 2 s → 0.1° off the
camera's heading; a held 40° → 2° short by 2.4 s, peak 54 deg/s, no
overshoot; released → at rest inside 6 s; straight back → 2.9 m of
reverse; in the ring, "up" from a side-on camera → turned to heading and
drove 6.2 m in 1.5 s.

- Speed cap on the map is 6 m/s (`MAP_MAX_SPEED`; 10 crosses the yard in
  8 s, too fast to read). Battery does **not** drain on the map in v1
  (owen's call, §9).
- **Righting.** On the map, holding the stick 1 s while inverted flips
  the machine back — a visible mercy the fight does not offer.

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

### 3.6 The challenge — you drive

Accept and it is a **Quick bout**: 30 s, 5-s count-out, walls at −10 s,
`SettleQuickFight()` at the bell.

**You drive the fight** (owen, 2026-09-10: "replace auto fight with manual
fight"). The same stick that brought you to the encounter drives the bout,
and FIRE works the weapon; `FightManager.playerSource` stays `Keyboard`
and no `ProgramRunner` is put on your machine. The opponent is the game's
`AIController`, as in every fight. One control scheme for the whole game:
the stick, everywhere.

**There is no program in this game.** The PROGRAM tab is deleted and the
auto-brain that briefly drove the challenge (`BrainPick`: RamHunter /
Brawler / WallShy / FirstSteps by the build's sensors) is deleted with it.
Robot Brawl keeps autonomy programming; Scrapyard's depth is the build,
the world, and your driving.

*Open, from this call:* a challenge against a **pool** robot (another
player's snapshot, §3.5) is fought here, by you, against the snapshot's
build under the AI — so POINTS for it come from the referee of a fight the
player drove, and the server-fought `yard` job (§7.3) becomes the path for
challenges the player is not present for, if any. Decide when the pool
challenge is built.

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

BUILD / ROBOTS / SHOP, the dock, unchanged from Robot Brawl (no PROGRAM). New:
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
repos with both suites green. `RaycastWheelDrive` was never touched in the
end: the map's steering lives entirely on the drive's AI channel (§3.3), so
the wheel model is identical across games and the referee's bouts stay
fair. `FightManager` (not in the list, but identical so far) carries two
hooks for the debrief's loud button, `quickNextLabel` / `quickNext`
(defaults: NEXT FIGHT, another quick bout — Robot Brawl's card unchanged),
which the fork's `BuilderManager.Awake` sets to CONTINUE EXPLORING → back
to the map. Port the hooks to Robot Brawl and the two files are identical
again.

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

- **2026-09-10 — the stick (owen: "it still feels a bit hard to drive. can
  you try to increase the size of the joystick and then simulate driving
  the robot with joystick as a human?").** The stick's travel to full
  deflection was 120 units, so a 40-unit thumb move was already a third of
  lock; it is **175** now, and the ring (148 → 220), knob (54 → 70), knob
  travel and resting spot grew with it. The stick also **takes a mouse on
  every platform** (it took one only under a bench flag, so a desktop web
  player had no stick at all). MapBench 60/0, TouchSmoke 57/0.
  **Then, from three pointer drives on the live page:** (1) the ring's edge
  was not full lock (radius 110 vs travel 175, the old stick's 0.62 ratio
  too) — now travel 140 = ring radius 140, the knob drawn out to the edge;
  (2) the stick anchored where the pointer was on the first POLLED frame,
  not where the press began — a fast flick anchored at its end and the
  throttle read zero; the anchor is now the last unpressed position for a
  mouse. **The rig's limit, stated plainly:** the extension's tab is
  HIDDEN (`document.visibilityState`, rAF 0/s, measured), so the game
  advances only when a screenshot forces a paint and every timed drive
  from that tab lands inside one game frame. The two fixes are measured
  by MapBench where they can be (travel = ring, mouse accepted) and the
  anchor by reasoning; **a real drive needs a visible tab or a thumb.**
- **2026-09-10 — TREASURE (owen: "treasure box doesn't look like treasure
  right now… when a part is acquired, we should display the image of the
  part in addition to text. for scraps we should show something like
  coins").** In the world the crate is now a **chest**: dark body, domed
  lid, gold bands, a lit clasp, light in the seam, the beam. Driving into
  it: **ten coins fly and the part itself rises out of the chest and
  turns** (`SpawnTreasureBurst`). In the reward panel every line carries a
  **picture**: the part, built by the game's own `PartVisualFactory` in a
  studio 500 m under the world and photographed once into a texture
  (`RewardThumb.Render`), or a **coin stack** for scrap. No image assets
  anywhere. **Measured: MapBench 59/0** (coins fly, the part rises, a
  64 px picture renders for a beam and for coins, the studio is torn down
  the same frame).
- **2026-09-10 — LEAGUE and ARENA tabs removed (owen).** The dock strip
  is BUILD · GARAGE · SHOP with **DRIVE OUT** at its right end, visible
  from every tab; indices 1 and 4 stay allocated and hidden (CLAUDE.md's
  renumbering trap), `ShowTab(1|4)` lands on BUILD. Deleted with the ARENA
  tab: `EnlistUiBench`, `ReturningPlayerBench`, `ArenaShots` (all comment
  references). TouchSmoke's two strip checks were the CHECK: they encoded
  the FIGHT tab and its TEST DRIVE chip; they now assert the new strip and
  that DRIVE OUT blocks an illegal build with a visible message.
  **Measured: TouchSmoke 57/0, MapBench 54/0, QuickFightBench 24/0**; web
  10.4 MB; live.
- **2026-09-10 — THE PLANET (owen: "The map looks ugly. It should look
  like some planets where robots live with modern designs. Driving the
  robot around and exploring the planet itself should be an enjoyable
  experience.")** All code, no assets: two URP shaders always included
  (`Scrapyard/PlanetGround` — vertex colour, light, fog and a faint
  6 m / 24 m grid etched into the flats; `Scrapyard/SkyBody` — banded,
  limb-shaded, no fog); `BuilderManager.WorldLook.cs` — a tinted copy of
  the scene's procedural skybox, linear fog to the edge of the loaded
  world, trilight ambient, the sun low and warm, a 300 m sister planet and
  a 70 m moon 800–900 m out following the camera at a fixed bearing;
  ground colour per vertex (biome, height band, slope, the home plaza);
  six structures in one language — monolith, antenna mast, crystal
  cluster, solar array, ring beacon, relay hub — in graphite with cyan,
  amber and violet light; crates as supply pods; a home plaza; terrain
  with mesas and craters. Restored at GARAGE. **MapBench 54/0**; web
  10.4 MB. Seen live: it reads as a planet — then two tunes from the
  screenshot: the sister planet was dark and half out of frame (now ahead,
  lit from behind the camera, brighter), and the sky's yellow band too
  strong (thinner atmosphere, cooler tint).
- **2026-09-10 — steering (owen: "turning is too sensitive, making it hard
  to drive straight").** The fight's drive took the stick raw, and a
  skid-steer build (SCRAPPER: four fixed wheels) turns by braking one side
  at full stick whatever the speed — the drive's speed scaling only touches
  steerable wheels. On the map the player's inputs now go through the
  drive's AI channel, which the map file owns (the shared wheel model is
  untouched): a dead zone (0.18), a gain (0.60, 0.45 at speed), a rate, a
  **speed cap of 6 m/s**, and a **heading hold** — the heading is
  remembered when the stick centres and steered back to (error + rate).
  MapBench's trace found what a player felt: **the rookie veers by itself,
  1° at 2 m/s and 12° at 10 m/s over two seconds** (the build is not
  symmetrical; a 14 m ring never shows it). The hold's sign was measured
  wrong once from a near-stationary pivot and fixed from the moving case.
  **Measured: MapBench 45/0** — full throttle, stick centred, two seconds:
  9.5 m and **0.0° of drift**; a held stick still turns 19° in 1.5 s.
- **2026-09-10 — THE WORLD replaces the yard (owen: "not an arena… like
  minecraft").** `BuilderManager.Map.cs` rewritten: chunked terrain from
  three octaves of noise on a per-player seed (`CareerData.worldSeed`,
  rolled on the first drive and saved), 48 m chunks, 25 loaded around the
  player, one built per frame, dropped behind; biomes by region; wrecks
  and ruins, crates (keyed per chunk in `worldOpened`, never respawn) and
  enemies per chunk, tougher with distance from home; no fence, no edge;
  a fall net; HOME on the compass. The PROGRAM tab is gone. Fights still
  cut to the standard ring — **fighting in place is the next pass**, and
  so is roaming. **Measured: MapBench 40/0** (25 chunks around home, the
  seed rolled and saved, crates on the terrain, the first 8 m from home,
  SCOUT nearest, home flat and the world not, the same world on re-entry,
  crate-once with a per-chunk key, 400 m out still 25 chunks and on the
  ground, the card, the brain, a fight to the bell, the ledger);
  TouchSmoke 55/0, QuickFightBench 24/0. Two real bugs the bench caught
  first: home sat on a chunk CORNER (a hair of drift loaded two extra
  chunks) — now a chunk centre; and a teleport through `rb.position`
  alone tore the machine in half — `TeleportPlayer` moves every part.
  **DEPLOYED and seen live** (`/play/scrapyard/`, stamp `056a1a4977`,
  initial 10.3 MB — the §5 miss stands): a fresh load boots INTO THE
  WORLD on the home pad, the first crate 8 m ahead, crates and wrecks to
  the horizon, a biome edge in view, the compass readable (CRATE 8 m ·
  SCOUT 34 m · HOME 0 m), the stick and GARAGE. Driven by a held key: the
  crate opened where it stood and the reward box popped in the world.
  Next pass: fighting in place (no cut to the ring), enemies that roam,
  the ARENA tab replaced by the board.
- **2026-09-10 — M0 step 3 DEPLOYED: `https://cyberduck.club/play/scrapyard/`.**
  First web build of the fork was **26 MB of data**: the six music themes
  sit in `Assets/Resources` for iOS (21 MB) and `com.unity.ai.inference`
  shipped 6.5 MB of compute shaders. Fixed: the two AI packages are out of
  the manifest; `BuildWebGL` hides `Assets/Resources` behind a `~` for the
  web build (restored in `finally`) and ships the spike's re-encoded
  tracks from `music_web/` beside the page, which `MusicLoader` streams
  after boot. **Second build: data 3.5 MB, wasm 6.6 MB, initial 10.3 MB —
  a MISS on the §5 budget of 9 MB by ~1.3 MB.** Named, not waved through:
  ~1.6 MB is URP film-grain/SMAA textures the game never draws and ~1.3 MB
  is the TextMesh Pro font; both are a settings pass, not code. Robot
  Brawl's page is 7.8 MB for comparison. Beacon path `scrapyard-play`;
  read the funnel with `RB_BEACON=scrapyard-play zsh
  server/scripts/web_funnel.zsh` in the Robot Brawl repo. M0's exit (median
  time on the map > 60 s, ≥ 50 % open a crate) now waits on players.
  **Seen live in Chrome (2026-09-10):** boot lands in the garage with
  SCRAPPER, no title screen, no gate; LEAGUE tab → DRIVE OUT → the yard
  renders: the machine at the door, the first crate 8 m ahead under its
  cyan beacon, wrecks, the fence, the stick, GARAGE. Two things to fix
  first thing: the compass strip is too faint to read on a desktop, and
  the tip strip still carries Robot Brawl's league tips ("open the LEAGUE
  tab to enter your first contest"). The ARENA tab is still there, as
  planned until the board screen replaces it.
- **2026-09-10 — M0 step 3: THE YARD EXISTS (headless).** Grown out of
  `StartTest()` as a partial of BuilderManager (`BuilderManager.Map.cs`,
  ~470 lines): `Mode.Map`, an 80 × 80 m plane with a fence and a glowing
  garage door, 28 seeded wrecks, **three crates** (the first 8 m from the
  door), **one parked SCOUT** 38 m out, the touch stick + WASD, a
  FollowCamera, a compass strip (bearings to crate / bot / garage), a
  righting mercy, DRIVE OUT in the dock and GARAGE on the HUD. A crate is
  a `qbox` opened where it stands (`QuickBoxRoll` + `QueueReward`); the
  card shows within 4 m and folds when you leave; CHALLENGE is a Quick
  bout against the parked bot under **BrainPick** (compass+wall →
  RamHunter, compass → Brawler, wall → WallShy, none → FirstSteps, always
  validated). Funnel events `map crate meet challenge`; save fields
  `yardDay`/`yardOpened`. **Measured: MapBench 31/0** (fence, seed,
  crate-once, no-respawn, card, brain, fight-to-the-bell, ledger), and no
  regression: TouchSmoke 55/0, QuickFightBench 24/0, FightWorkerBench
  28/0 + 29/0. The web template, beacon plugin (path
  `/v1/beacon/scrapyard-play`) and `BuildWebGL` are carried in from the
  spike; `web_funnel.zsh` takes `RB_BEACON=scrapyard-play`. M0 still owes:
  the deployed web build at `/play/scrapyard/` and its size, then the
  live-site exit numbers (median time on the map, crate rate).
- **2026-09-10 — the league campaign is off the screen.** The LEAGUE tab's
  board (trophy case, rookie checklist, five league headers, contest rows
  with SCOUT / MANUAL FIGHT / AUTONOMY FIGHT) and the two sandbox buttons
  (LADDER, EXHIBITION) are gone from the dock; the tab is Quick Fight
  until the map replaces it. Deleted with them: MedalDev, R3Dev,
  CareerShot, CareerBench, ChallengeBench. Kept as data: `Career.Leagues`,
  `EnemyRoster`, medals and `doneContests` fields (saves still load;
  `StartCareerFight` and the settle path are unreachable and go when the
  map lands). Measured: compile 0 errors, TouchSmoke **55/0**,
  QuickFightBench **24/0**. The day-one list (§2.2) is done as amended:
  four deletions landed, TEST DRIVE deliberately kept (it is the map's
  drive loop). Next: M0 step 3, the prototype yard grown out of
  `StartTest()`.
- **2026-09-10, small hours — day-one deletions, three of them.** On the
  fork: the `RB_PORTAL` gate (`e602c2c`), the Rookie Warm-Up guide
  (`db27aa0`), and the title screen + login gate + CareerSmoke (this
  commit) — `ModeSelect` is now a straight boot into the garage on every
  platform, the editor included. Measured after each: compile 0 errors;
  TouchSmoke **55/0** and QuickFightBench **24/0** in the fork, the same
  numbers as Robot Brawl. **Two changes to §2.2, found by reading the
  code:** (1) **TEST DRIVE stays** — `StartTest()` is the map's drive loop
  already (spawn the player's robot, touch stick, `FollowCamera`, a
  target), so M0 step 3 grows the map out of it instead of deleting it and
  rebuilding the same thing; (2) **the leagues are a TRIM, not a delete** —
  `Career.Leagues` is where the roster's contest opponents (SCOUT, TIPPER,
  BULWARK…) are defined and `QuickPool()` draws from it, so the campaign
  UI, `StartCareerFight`, medals and the trophy case go and the contest
  table stays as the roster (renamed in M1). MANUAL FIGHT lives in the
  contest rows and goes with them.
- **2026-09-09, late — M0 step 2 begun: THE FORK EXISTS.** `du6/robot-brawl-
  scrapyard`, main = Robot Brawl's `1dc752e`, cloned to
  `~/robot-brawl-scrapyard` with Robot Brawl removed as a remote. First
  commit on the fork: identity — product "Robot Brawl: Scrapyard", bundle
  `club.cyberduck.scrapyard`, 0.1.0 build 1, `scrapyard_save.json` and
  `scrapyard_profile.json` (a different folder by construction: Unity
  derives persistentDataPath from company + product), Android artifact
  names, and a `CLAUDE.md` header that says what this repo is. Compiled
  clean in batchmode (0 errors). Not yet: the day-one deletions (§2.2), the
  web template (it lives in `~/rb-webgl-spike`, not in this repo), the
  beacon path.
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
- **2026-09-10 — PLACES (owen: "Can we make the planet look more
  realistic? Now it looks like a desert with some cubes and other shapes,
  which feels boring to explore. For example, can we add buildings, shops,
  robot parks, toys, arenas with robots fighting each other, etc.?").**
  `BuilderManager.Places.cs`: a 150 m place grid from the seed (towns,
  trading posts, robot parks, arenas, playgrounds — §3.2), each flattening
  its ground through `TerrainHeight` (now `RawHeight × HomeFlat`, then
  `FlattenForPlaces`), built by the chunk that holds its centre and torn
  down with it. The trading post's pad opens the workshop on SHOP
  (`EnterShop`); the map resumes where you left (`lastMapPos`). The arena
  runs two roster machines on `AIController` against each other with no
  `FightManager` — the first time this project has fought outside one —
  and `PumpArenas` is the referee. Compass names the nearest place.
  **Measured: MapBench 83/0** (48 places in 81 cells, all five kinds, the
  three fixed places flat to 5 cm, pad opens SHOP and resumes at the pad
  without re-entering, arena fighters wired at each other and moved 11 m
  / 7.5 m in 1.5 s inside the ring, unloaded with their chunk),
  **TouchSmoke 57/0, QuickFightBench 24/0.**
- **2026-09-10 — YOU DRIVE THE FIGHT (owen: "Let's replace auto fight with
  manual fight").** `StartYardFight` no longer adds a `ProgramRunner`;
  `playerSource` stays `Keyboard`, so the stick and FIRE that brought you
  to the encounter drive the bout. `BrainPick`/`BrainPickTitle` deleted;
  the card reads "you drive: stick to move, FIRE for the weapon". §3.6
  rewritten. MapBench's CHALLENGE section now asserts the Keyboard source,
  no ProgramRunner, the stick up, the bell, and that a held throttle moves
  the machine >1 m in a second; the BrainPick section is gone.
- **2026-09-10 — POINT WHERE YOU WANT TO GO (owen: "the driving still
  feels tricky… check popular mobile driving games").** Researched (Asphalt
  9 TouchDrive, Mario Kart Tour, Real Racing 3, Hill Climb Racing, the
  Brawl Stars stick): direction from the player, steering from the game.
  `BuilderManager.Drive.cs` replaces the tank stick: camera-relative
  heading + speed on one stick, the frame latched at press, a
  heading→yaw-rate→steer two-loop controller, brake-into-turn, brake on
  release, reverse straight back, a lagged camera pivot; the challenge
  bout runs on the same stick (AI channel + `aiFire`). Fourteen bench
  iterations, most of them the CHECK: two run-out spots drove into the
  home plaza's monolith and relay hub (the second read as a 25° "drift"),
  the reverse check pressed back while still coasting, the camera had not
  settled after a facing teleport. §3.3 rewritten. **Measured: MapBench
  94/0, TouchSmoke 57/0, QuickFightBench 24/0.**
- **2026-09-10 — CONTINUE EXPLORING (owen: "replace the next fight button
  with continue exploring").** The debrief's loud button now goes back to
  the map where the challenge began (`FightManager.quickNextLabel` /
  `quickNext`, defaults unchanged for Robot Brawl; set in the fork's
  `BuilderManager.Awake`). MapBench drives the hook: mode Map, within 6 m
  of where the challenge began.
  Also today: a purchase shows on the BUILD palette at once
  (`Career.inventorySeq`; TouchSmoke 61/0).
