# What C.A.T.S. does that we do not — a gap analysis and a plan (2026-09-07)

owen: "This game is similar to ours but much more popular. Do a deep research
on the gap and come up with a plan on how we can improve our game."

The comparison is with **C.A.T.S.: Crash Arena Turbo Stars** (ZeptoLab 2017,
sold to Nazara Technologies in January 2025). Sources are the developers' own
one-year retrospective on Game Developer, the PocketGamer.biz making-of
interview, the App Store and Wikipedia pages, the community wiki and three
strategy guides, and the linked gameplay video (Kindly Keyin, "TOTALLY
ACCURATE CAT TANK BATTLE SIMULATOR"). Our side is what this repo measures:
the web funnel, the two play-test agents' reports, and the bench suite.

The short version: **CATS is not a better robot game than ours. It is a
better *loop*.** Our fight is deeper, our builder is freer, and our autonomy
programming has no equivalent anywhere in their game. What they have is a
fight that lasts seconds, a reason to fight again every ten seconds, a reason
to come back tomorrow, and other real people's machines to fight. Every
item in the plan below is about the loop, not the fight.

---

## 1. Why CATS matters as a yardstick

| fact | CATS | source |
|---|---|---|
| launch | 19 Apr 2017, after a **13–14 month soft launch** in Sweden, Austria, Canada | retrospective, PG.biz |
| first weekend | **8 M downloads** | retrospective |
| 4 months | 60 M; **100 M players by year one** | Wikipedia, retrospective |
| awards | Google Play **Best Game of 2017**; IMGA finalist | Wikipedia |
| today | App Store **4.8 ★ from 408 k ratings**, still updated (3.35.7, this week) | App Store |
| team | **5 prototypes in 2 months**, 3-month prototype phase, 4 → 12 → 23 people at launch | retrospective |
| exit | acquired with King of Thieves for **$7.7 M** (Jan 2025) — a mature game, not a growing one | Wikipedia |
| also on the web | Poki, CrazyGames, Miniplay — the same game in a browser | search |

Two things to take from that table. First, the design was found by testing —
"test as many things as possible, including the radical ones, as long as you
can measure results" — and half of what they tried was cut (rival sabotage,
unmanned cars). Second, the game that made 100 M players is **eight years
old and was sold for the price of a small house**. We are not chasing CATS
2026; we are learning from CATS 2017, whose loop is public knowledge.

---

## 2. What CATS actually is (the systems, with numbers)

**The fight.** Two cars, no controls — "players have no controls over the
cars" was a design pillar from prototype one, because "fighting yourself
requires serious skills; what if players use their head instead." Fights last
**seconds**; if nobody has won after ~5 s, two "walls of death" (bulldozers)
close in and crush the nearest car. There is nothing to watch for longer
than a GIF. The cat driver's face reacts. Sharing a fight is one tap.

**The car.** A chassis (≤3 slots) + wheels + weapons + gadgets. Every chassis
has a **power capacity**; weapons and gadgets cost power, so you cannot stack
everything. Parts have a **rarity/star rating (1–5★)**; stars set the level
cap (1★ caps at level 6, each star +5 levels, 5★ = 26); you **fuse** spare
parts into one to level it, paying coins. "Each star added extra quality and
high-leveled ones had different visuals." Only a few base items exist —
variety comes from stars, levels and random slot layouts, "to reduce
production demands while maintaining variety."

**Quick Fight — the engine.** Pick one of a few opponents (other players'
real cars, fought by the AI); fight; **3 wins = a supply box**, every win
pays monkey wrenches, **5 wins in a row = a crown** that upgrades the next
box. Boxes take **time to open** (ads take 30 min off; gems skip; box slots
are limited). This is the CATS loop in one sentence: *fight-fight-fight-box,
box opens later, come back.*

**Championship — the ladder.** 24 stages. A stage holds **14 players** (real
players plus bots); each win is a medal; **top 6 by medals promote**, or **14
wins with one car = instant promotion**. Promotion pays gems, coins, an
Ultimate part and a box. Stage 24 → **Prestige**: restart at stage 1, keep
gems, boxes, parts, skills, get a sticker. Stage 15 unlocks the **Ultimate
League** (a second garage with persistent parts — the part players complain
about: "Ultimate parts. I HATE them").

**Everything else.** Bets (stake parts on other players' bouts); gangs (5+
members) and **City Kings** (co-op territory control, 3 defending cars each,
scouting, replacing); co-pilot missions on 30 min–3 h timers; skills and
skill points; daily sponsor box; seasons and leaderboard events; cosmetics.

**Money.** Coins (soft), gems (hard, $1.99–$19.99 packs), boxes with timers,
ads to shorten timers, gem boxes that skip them. The critics' one recurring
complaint is exactly this: "criticized for free-to-play wait mechanics."

---

## 3. What we are (the same table, honestly)

| | Robot Brawl today |
|---|---|
| the fight | full 3D physics, seams shear, batteries drain, count-outs, judges' decision; **90 s** by the clock, decided late; a forensic debrief that names why you won |
| control | keyboard/touch driving OR **a program you build from sensor blocks** (RamHunter etc.) — no competitor has this |
| the machine | free-form part placement on faces, 7 materials, weight-cap leagues, gussets ×4 on seams; **kit = exactly SCRAPPER**, spares only as rewards |
| opponents | **scripted bots** in 5 leagues × contests (SCOUT, TIPPER…); a real-player ladder exists server-side (validated snapshots, cloud referee, replays) but sits behind an ARENA tab and a sign-in |
| first minute | (as of this week) pre-built SCRAPPER, ghost hand → LEAGUE → AUTONOMY FIGHT; a stranger reaches a fight in 3–4 taps, wins ~2 min after launch |
| reasons to return | a checklist of four one-time rewards, a rescue crate on the first loss, the next contest; **nothing dated, nothing timed, nothing social** |
| economy | scrap only; no hard currency, no ads, no purchases (a server wallet exists, unused by the client) |
| platform | iOS (2.2.1 in review), web (no wall, 8 MB); web funnel since 1 Sep: **155 sessions, 55 % reach the game, ~1 new player/day reaches a fight, 0 ladder accounts in 3 weeks** |
| team | one person + this |

Our strengths are real and CATS cannot copy them cheaply: a physics fight
that is *legible* (the debrief tells you the seam that sheared), a builder
with no fixed slots, and autonomy programming. Our weakness is that a
player who reaches the fight has nothing pulling them to the next one.

---

## 4. The gaps, ranked by what they cost us

### Gap 1 — the fight is too long to be a loop (largest)
CATS: seconds, then walls of death. Ours: 90 s, most of it a slow shove,
decided by the count-out or the judges. One CATS fight is a *unit of play*
you repeat twenty times; ours is an *event* you sit through once. The web
tester watched TIPPER lie on its back for 80 s. Every downstream problem —
few fights per session, few reward moments, nothing to share — starts here.

### Gap 2 — no reason to fight again in the next ten seconds
After a contest we land on a debrief with BACK / REMATCH / one coaching
door. CATS lands you on "next opponent" with a box meter at 2/3. We have no
streak, no "3 wins = box", no crown. The checklist pays four times, ever.

### Gap 3 — no reason to come back tomorrow
No daily anything. No timed box (we do not want the timer — see §5 — but we
need the *return*). CATS gets its D1 from boxes that open later and a daily
sponsor box; we get ours from nothing. The funnel agrees: 12 of 15
returning web sessions in the last day were the same few people.

### Gap 4 — nobody fights a real person's machine
CATS's opponents are other players' cars, fought by the AI — free content,
endless, and every fight is a story ("I beat a real build"). Ours are five
leagues of hand-made bots, then a ladder that needs a sign-in nobody takes
(0 accounts in 3 weeks). We already HAVE the asset: the ladder holds
validated real snapshots and a worker that fights them. The gap is that the
career never draws from it.

### Gap 5 — progression is a shop, not a collection
CATS: parts drop, stars gate levels, fusing turns spares into power, higher
stars look different — the collector's itch, and a sink for duplicates.
Ours: buy a part with scrap, or get one from a one-time reward. Nothing
drops. Nothing levels. Two aluminium beams are two aluminium beams.

### Gap 6 — nothing to share, nobody to show
CATS: cat faces, one-tap replay share, bets on friends' bouts, gangs, City
Kings. Ours: replays exist (ladder), the promo tooling exists (TikTok rig),
but a player cannot share a fight from inside the game, and there is no
one to share it with.

### Gap 7 — no second currency, no offer, no revenue
Not a growth gap, but a *design* gap: without a hard currency there is no
"skip", no "one more box", no seasonal pass — and owen's stated plan is
scrap on sale eventually (`Server_Economy_Design_2026-08-13.md`).

### Gap 8 — discovery
CATS is on Poki and CrazyGames. Our web build is 8 MB, no wall, no account —
it is *exactly* what those portals list, and it is on none of them.

### Non-gaps (things CATS has that we should NOT copy)
- **Wait timers on boxes.** Their single most-criticised mechanic; on iOS
  they invite the "loot box" review question. We can have the *return*
  without the *wait* (daily caps, dated events).
- **Gacha-random slot layouts.** They did it "to reduce production demands";
  our free-form builder is the thing we are better at.
- **Ultimate parts / power creep.** The App Store's top complaint.
- **Cats.** The theme is theirs; our core already has a face.
- **Removing the driver.** They removed controls to remove skill; we keep
  manual driving as a *mode* and lead with autonomy, which is the same
  outcome ("use your head") with a stronger idea behind it.

---

## 5. The plan

Six steps, each with the metric it must move and the measurement that
already exists. Order matters: each step makes the next one worth doing.

### Step 1 — Make the fight a unit of play (2 weeks)
*Moves: fights per session (funnel: `fight`, `fight2`, new `fightN`).*
- **A 30-second bout** for Quick Fight and rookie contests: clock 0:30,
  count-out 5 s, and an **arena shrink** in the last 10 s (our "walls of
  death" — the hazard system already exists) so nothing ends on the judges.
- Championship/exhibition keep 1:30 for people who want the long game.
- **Faster resolution** where it is already close: the count-out fix shipped
  this week; damage rates are a constant sweep (`Disarm_Lever_Sweep`).
- The debrief becomes a 3-second **result card** (VICTORY / purse / one line)
  with **NEXT FIGHT** as the primary button; the full forensics stay one tap
  away for the people who love them (and they do — keep it).
- Measure with `LadderSweepBench`/`MatrixBench` at 30 s before believing
  balance holds at the shorter clock; the FLOOR/CEILING numbers will move.

### Step 2 — Quick Fight: the CATS engine, minus the timers (3 weeks)
*Moves: fights per session, return rate (`return`), reward opens (`reward`).*
- A **QUICK FIGHT** door on the first screen: three opponents offered, pick
  one, 30-s bout, straight to the next three.
- **3 wins = a toolbox** (opens *instantly* — no timer), containing one part
  drop + scrap; **5 in a row = a crown** that upgrades the next box.
- **Daily cap, not a wait:** e.g. 6 boxes a day, then wins still pay scrap
  and streak — the return comes from "tomorrow's boxes", not from a clock
  ticking on a locked box.
- Opponents come from **Step 3's pool**; until then, from the league bots
  and from the player's own saved robots.
- Boxes reuse the RewardBox ceremony that already works (deferred grant,
  `pendingRewards`), so the first week's box code is done.

### Step 3 — Fight real people without signing in (3 weeks)
*Moves: fights vs real builds (new funnel event `pvp`); ladder accounts.*
- **Guest defence:** every Quick Fight opponent is a **real player's
  snapshot** pulled from the ladder (the API already validates and stores
  them; the worker already fights them). A guest fights them *locally* with
  the same engine — no account, no upload, no referee needed for Quick
  Fight. That is exactly CATS's asynchronous PvP.
- **Sign in to be *in* the pool:** the only thing an account buys at first
  is "your robot can be somebody's opponent" and a name on their result
  card — a reason to enlist that is about pride, not access.
- Seed the pool from the ladder's existing accounts and, until it is deep,
  from the champions bake (`champions/`).
- The ARENA tab and the cloud-refereed matches stay as the *ranked* layer.

### Step 4 — Parts that drop, level and look different (4 weeks)
*Moves: session length, D7 return, scrap sinks.*
- **Drops:** boxes drop parts in our existing materials; materials become
  the visible tier (aluminium → steel → titanium → tungsten → carbon), which
  we already price and colour.
- **Levels via fusing:** a spare beam + scrap fuses into your beam: +HP per
  level, a cap by material (the CATS star cap, reskinned as the material
  ladder we already have). Duplicates finally have a use.
- **Visible upgrades:** a levelled part gets a decal/edge glow; the machine
  in the ARENA card looks earned.
- **Weight cap stays as our "power capacity."** No new constraint needed;
  the league caps already do the balancing job.
- This is the largest design change in the plan and the one to prototype
  and *measure* first (CareerBench at 3× sample, `SampleMul`), because
  every balance number in `Flagship_Hardening` shifts with part HP.

### Step 5 — Something to show, someone to show it to (3 weeks)
*Moves: shares (new event `share`), organic installs.*
- **One-tap share of a fight**: a 6-second clip of the decisive moment,
  rendered by the game (the promo rig already drives frames; the web
  MediaRecorder limitation is documented and worked around).
- **Result card with both machines' faces**: the core's face reacts to the
  verdict (we have the face; give it four expressions).
- **Bets:** stake scrap on a replay from the ladder inbox — cheap, uses
  replays that already exist, and is CATS's second most-used social feature.
- Gangs and City Kings are out of scope for one person; a **weekly league
  board with real names** (the ARENA board) is the cheap version.

### Step 6 — Hard currency, no timers, and the store (after 1–5 measure)
- Gems ("bolts"?) from boxes, promotions and dailies; sold in packs.
- Spent on: extra boxes past the daily cap, cosmetic drivers/decals, a
  seasonal pass over the **seasons that already exist server-side**.
- Never on: skipping a wait (there are none), power that cannot be earned.
- This is owen's `Server_Economy_Design` with a product in front of it.

### Step 0 — in parallel, discovery and the funnel
- **Submit the web build to Poki and CrazyGames** (8 MB, no wall — it fits).
- Replace the dead funnel steps (`build`, `saved`) with `quickfight`, `box`,
  `streak`, `pvp`, `share`; the script already groups by session.
- Fix the 45 % who never reach the game on the web: the load-gap section
  says most leave at the 25 % milestone (the wasm); that is a loader
  problem, not a game problem, and it is first because it is cheapest.

---

## 6. What to measure, and the numbers to beat

| metric | now (web, last 7 days) | target after steps 1–3 |
|---|---|---|
| sessions that reach the game | 55 % | 75 % (loader) |
| fights per playable session | ~0.1 | 3 |
| sessions with a second fight | 1 % | 40 % |
| returning sessions | 11 % | 30 % |
| new ladder accounts / week | 0 | 10 |
| shares / week | none (no feature) | any |

All of these come out of `server/scripts/web_funnel.zsh` with the new
events; the iOS build still has no funnel, which is a decision for owen
(privacy policy), not a task.

---

## 7. Sequencing, effort, and what is owen's call

Steps 0–3 are about **eight weeks** of this session's work and change no
balance constant except the clock; they can ship to the web weekly and to
iOS as one build. Step 4 is the one that needs a bench campaign before it
ships. Steps 5–6 follow the numbers from 1–3.

Owen's calls, in the order they block work:
1. **The 30-second bout** for Quick Fight — it changes the feel of the game
   most, and it is the pillar CATS built everything on.
2. **Guest defence** — a player's robot being fought by strangers without
   opt-in. CATS does it; the ladder's sign-up copy would need to say it.
3. **Drops and levels** — the collection economy, which touches every
   balance number and the "no faucet" rule from 2026-08-13.
4. **Hard currency** — when, and what it may never buy.

What is *not* in the plan on purpose: gacha slots, box timers, cats, gangs,
and rewriting the fight. The fight is the part CATS players ask for and
cannot have ("one thing that would make it that much more awesome would be
to control the rig itself" — a 4.8★ review).

---

## Progress log

**2026-09-07 — Steps 1, 2 and the Step 0 instrumentation, built and measured
(`c95a1af`).** QUICK FIGHT is a door on the LEAGUE tab above the league
board: three roster opponents around the player's league (reseeded after
every fight), a 30-second bout with a 5-second count-out and the crusher
walls closing over the last 10 s, a result card whose loud button is NEXT
FIGHT, a purse of 20-60 scrap, 3 wins = a toolbox (one part drop + scrap,
self-describing id, granted at the box's opening), 5 in a row = a crown
that adds a part to the next box, and a cap of 6 boxes a day instead of a
timer. Autonomy when the robot carries a program, manual otherwise.
Measured: `QuickFightBench` 24/0 headless — the bout ended at 31.2 s with
the walls in, meter/box/crown/cap/day-reset/ledger all hold; CareerSmoke at
the control's floor; TouchSmoke 55/0; career save untouched. Funnel events
`quick`, `box`, `streak` and a same-tab `reload` beacon (Safari crash vs
return) with a `starting` milestone are in the script and the template.
Not yet: Step 3 (real players' snapshots as opponents — the pool is the
roster for now), the iOS build (2.2.1 is in review; Quick Fight goes into
2.3.0 rather than another swap), and Poki/CrazyGames submissions (owen).

## Sources
- Game Developer — "C.A.T.S. One Year Retrospective: turning fun concept into a hit" (ZeptoLab)
- PocketGamer.biz — "Feline fighters: the making of ZeptoLab's C.A.T.S."
- Wikipedia — CATS: Crash Arena Turbo Stars (release, reception, Nazara acquisition)
- App Store listing (4.8★ / 408 k, IAP tiers, reviews)
- catsthegame.com; catsthegame.fandom.com (Championship, Prestige)
- gamingonphone.com beginner's guide; withoutthesarcasm.com boxes & crowns guide; touchtapplay.com build guide
- YouTube — Kindly Keyin, "TOTALLY ACCURATE CAT TANK BATTLE SIMULATOR! | Crash Arena Turbo Stars Gameplay"
- This repo: `server/scripts/web_funnel.zsh`, `docs/QA_Round1_Fixes_2026-09-05.md`, `docs/Rookie_Warmup_Design_2026-09-03.md`, `docs/Server_Economy_Design_2026-08-13.md`
