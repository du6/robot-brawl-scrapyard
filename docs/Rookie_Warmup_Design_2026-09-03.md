# The Rookie Warm-Up — onboarding design (2026-09-03)

**Goal: a stranger who opens the game is still playing five minutes later, and
comes back once.** Everything below is in service of those two sentences.

## 1. Why now, in numbers

The web funnel (live since 2026-09-01, ephemeral-id sessions, this machine
excluded) has measured each onboarding wall as it fell:

| wall | measurement | status |
|---|---|---|
| 33 MB payload | 40–50% of arrivals never reach a playable game | OPEN (payload cut is separate work) |
| sign-in gate | 24 shown → 23 walked away (96%) | REMOVED on web 2026-09-02 |
| empty workshop | 14 workshop arrivals → 1 placed a part → 0 fights | REPLACED by SCRAPPER 2026-09-03 |
| first fight | starter wins 60% (measured, StarterBench 10-bout legs) | SHIPPED |

The next wall is predictable: **the 40% whose first fight is a loss or draw,
with 0 scrap, no obvious next move, and advice they cannot follow** (see §3).
That is what CATS solves with scripted early generosity, and what this design
addresses with a bounded version that respects this game's economy rules.

Comparable evidence: CATS (ZeptoLab, 200M players, the closest design
relative) opens with a guided build using animated pointers, scripts the
first fights to be winnable, and answers every early loss with a crate.
We adopt the *shape* of that, not the numbers.

## 2. Design principles (each earned by a measurement or an incident)

1. **Fight first, build second.** Shipped. The guided build happens AFTER the
   player has seen why building matters.
2. **One action per step, detected by state, never by "OK" dialogs.** The
   existing tip strip already advances on state (place → save → league). The
   warm-up extends that; it does not add modal tutorials.
3. **Show, don't tell.** The two-tap placement ritual and the
   wheels-need-opposite-faces rule were both silent drop points in playtests.
   A pulsing target beats a sentence.
4. **Every grant is BOUNDED and one-time.** owen removed the loss consolation
   because "a repeatable loss payment was the last unbounded faucet"
   (server Program.cs, 2026-08-13). Nothing here reopens a faucet: every
   reward is a once-per-career flag, and the totals (§7) stay under two
   SCOUT purses.
5. **Advice the player can act on.** The defeat screen may not recommend a
   part the catalog does not sell (§3).
6. **Skippable, always.** SKIP TIPS continues to skip everything; a player
   who builds before being told to simply completes steps out of order and
   the checklist marks them done.

## 3. A defect this design surfaced: the gyro advice is dead advice

The most common first-fight loss is the count-out flip, and the result screen
says: *"fit a gyro, or build an arm that can push you back over."* The gyro
was retired from the player catalog on 2026-08-12 (rosterOnly — enemies still
use it; see `docs/Catalog_Cuts_2026-08-12.md` before resurrecting anything).
A brand-new player is being coached to buy a part that does not exist, at a
moment when they also have 0 scrap.

**RESOLVED (owen, 2026-09-03): the gyro STAYS retired.** The confusing
advice goes instead: the count-out cause line drops "fit a gyro" for
followable physics advice (build low and wide, heavy parts down), and the
rescue crate carries a HEAVY ballast component with a hint to mount it low —
the anti-flip answer a player can actually act on.

## 4. The warm-up arc, end to end

Phase A is shipped; B–E are this design.

### A. First 60 seconds (SHIPPED 2026-09-03)
Boot → SCRAPPER assembled and fight-ready → tip 4/7 points at LEAGUE →
AUTONOMY FIGHT lit (FIRST STEPS pre-armed) → a real fight, 60% win.

### B. The debrief becomes a door (small, high leverage)
The result screen is the best screen in the game — but it dead-ends into
BACK TO WORKSHOP. Add ONE context-aware button beside it:

- after a flip loss: **[MAKE IT STABLER]** → workshop with the guided-build
  sequence (§C) targeted at the wedge/stance step
- after a disarm/damage loss: **[HIT HARDER]** → same sequence targeted at
  the gusset/weld step
- after a win: **[CLAIM PURSE & UPGRADE]** → SHOP with the checklist card up

Detection is trivial (the cause line already knows), and it converts the
screen players understand into the on-ramp for the screen they don't.

### C. The guided first improvement (the CATS-style step-by-step)
Runs ONCE, after the first fight (win or lose), in the workshop. Five steps,
each a single tap, each completed by game state, each skippable:

| # | instruction (status line) | guidance animation | done when |
|---|---|---|---|
| 1 | "Tap the **Wedge**" | palette tile pulses (scale 1.0→1.06, ~0.8 s loop) | wedge selected |
| 2 | "Tap the **glowing face** to aim" | the valid front-low face outline pulses; a drawn arrow arcs tile→face | ghost visible on a valid face |
| 3 | "Tap **again** to bolt it on" | ghost itself pulses | part count +1 — *this step exists to teach the two-tap ritual, playtest confusion #1* |
| 4 | "**SAVE** keeps it" | SAVE button pulses | robot saved |
| 5 | "**REMATCH** — see the difference" | LEAGUE tab pulses | fight 2 starts |

Mechanics: all buildable with existing primitives — a UI pulse component
(one ~40-line MonoBehaviour animating scale/alpha), the existing face-outline
renderer for step 2, and the amber status line for text. No new art pipeline,
no video. The arrow is one UGUI image rotated toward its target.

Guard rails: if the player does anything else (opens SHOP, fights again),
the guide silently completes whatever steps their actions satisfied and
resumes at the first unmet one; SKIP TIPS ends it permanently
(`Data.guideDone = true`).

**Prerequisite bug:** the save-name dialog currently eats first-focus clicks
and leaks the space key to the game underneath (playtest 2026-09-02, repro:
type "Spike Cart" → 0 chars land, scene flips to TEST DRIVE). Step 4 walks
straight into it. Fix ships with or before this phase.

### D. The first-defeat rescue crate (once per career, ever)
Trigger: first career fight loss or draw (`Data.rescueGranted == false`).
Not on wins — winners get the purse; the crate exists so a loss is a plot
point instead of a wall.

Presentation: on the result screen, under the forensics — a crate icon
(UGUI, simple open animation: lid rotates, contents fly to the status bar
counter): *"The Yard looks after rookies. One-time salvage: …"*

Contents (RESOLVED, owen 2026-09-03):
- **50 scrap** (below the 83 SCOUT purse: winning must stay better than losing)
- **2 gussets** (the measured fix for the measured loss mode: unwelded spike
  sheared 10/10 in StarterBench; welded won 6/10)
- **1 STEEL armor plate** — the ballast. Steel because heavy is the point:
  mounted LOW it drops the CoM, which is the anti-flip lever that remains
  with the gyro retired (§3). The crate hint says exactly that.

Hint line ties the crate to the debrief button (§B): the crate text names the
specific weakness the fight exposed, reusing the cause line.

Economy audit trail: granted via `Career.Txn(+50, "rookie salvage — one-time")`
so the ledger audit (TxnSum == scrap) stays true, exactly like the kit grant.

### E. The Rookie Checklist (the visible warm-up card)
A card at the top of the LEAGUE tab until completed (then it collapses to the
trophy case line). Six tasks, each once-per-career, each paying a small
bounded reward on completion. Detection points all exist already as telemetry
hooks or career flags:

| task | detection (existing seam) | reward |
|---|---|---|
| Fight a bout | first `result` (any outcome) | 10 scrap |
| Bolt on a part | first player `AddPart` commit | 10 scrap |
| Weld a seam | first `ApplyGusset` | 10 scrap |
| Buy a part | first shop purchase | 10 scrap |
| Win a contest | existing first-win purse | (purse itself) |
| Beat both Rookies | existing league-sweep medal | (medal itself) |

New money introduced: max 40 scrap. Combined with the crate (50) the total
one-time generosity is 90 — just above one SCOUT purse, less than two, and
none of it repeatable. The checklist's real job is not the scrap; it is that
each line names a verb the player hasn't tried yet.

### F. The reward box (SHIPPED 2026-09-04, owen's ask)
Every once-per-career grant - the four checklist +10s and the rescue crate -
now arrives as a CEREMONY, not a silent ledger line: the screen dims, a
confetti burst, a title ("FIRST BOUT", "FIRST WELD", "THE YARD LOOKS AFTER
ROOKIES"), and a riveted crate that pulses "TAP THE BOX TO OPEN". The tap
flips the lid over the back edge (fake-3D scale-Y, not a 2-D sweep - measured,
the sweep cut through the body), the reward lines rise out of it, and CLAIM
(or 9 s) dismisses. `RewardBox.cs`, procedural UGUI, the RookieGuide pattern.

Two rules that keep it safe: the grant is ledgered by `Career` BEFORE the box
is queued (`Career.QueueReward`), so a skipped, reloaded or crashed box loses
nothing - it is the handover moment, not the transaction; and boxes drain one
at a time from `MobileBuilderUI.Update`, never during a fight, with the ghost
hand yielding while one is up. Web-only like the rest of the warm-up:
`QueueReward` compiles to a no-op outside the WebGL player, so benches and iOS
never see one. Telemetry: `reward&o=1` on the first open.

### G. The kit is exactly SCRAPPER; the spares are rewards (SHIPPED 2026-09-04)
owen: "Should the starter kit include the initial robot only and leave
everything else as rewards?" Yes. The boot shelf now shows only the eight part
types on the machine (all "0 free" - they are on the robot) plus MORE IN SHOP,
and every spare the kit used to hand over greyed-out arrives as a box with
something in it, mapped so each reward is what the next step needs:

| moment | box contents |
|---|---|
| FIRST BOUT (win or lose) | +10 scrap · 1 wedge · 1 gusset |
| rescue crate (first loss/draw) | 50 scrap · 2 gussets · 1 steel plate |
| FIRST PART BOLTED | +10 scrap · 2 beams · 2 armor plates |
| FIRST WELD | +10 scrap · 1 long beam · 1 spindle |
| FIRST PURCHASE | +10 scrap · 1 cube |

Two things this fixed on the way: the win-path guide coaches the wedge, so the
wedge MUST arrive before the guide runs - the first-bout box hands it over and
the guide yields to the box, so the hand points at a tile that now exists; and
the checklist's "weld a seam" was unreachable for a WINNER (SCRAPPER's welded
spike consumes the kit gusset, the crate only gives gussets on a loss, the shop
wants 200) - the first-bout gusset closes it. A part type granted by a box
makes its tile appear on the next repaint (`ArrangePalette` on `uiDirtySeq`).
Same parts, same bounded total, deferred. Verified live on a fresh profile:
boot shelf → draw vs TIPPER → FIRST BOUT box (3 lines) → crate box (3 lines).

## 5. What we are deliberately NOT copying from CATS
- **Gacha crates / random rewards** — collides with "no dark patterns" on the
  website and adds an economy surface nobody needs yet.
- **Scripted fake opponents for guaranteed wins** — the 60% measured rate is
  honest; if it needs to rise, tune SCOUT or resurrect the gyro rather than
  rigging outcomes. The result screen's credibility is this game's crown
  jewel; a rigged fight would poison it.
- **Energy timers, daily-login streaks** — not at 22 sessions/day.

## 6. Instrumentation (web, same beacon, once-per-session)
- `rescue` — crate granted (fires at most once per career anyway)
- `guide` with `&s=1..5` on each guided step completion, `&s=done`/`&s=skip`
- `fight2` — second fight started (the retention-in-miniature number)
The question the funnel should answer in week one: **does a first-fight loser
with the crate fight again?** Today the answer is unmeasurable; after this it
is `result(w=0) → fight2` conversion.

## 7. Effort and order
1. Debrief buttons (§B) + scout-card AUTONOMY option — ~half a day, ships alone.
2. Rescue crate (§D) — ~a day incl. the crate animation and flags.
3. Guided improvement (§C) — ~2 days incl. the pulse/arrow components and
   the save-dialog bug fix.
4. Checklist card (§E) — ~a day.
5. Gyro resurrection (§3) — an hour of code, but it is a CATALOG decision.

Web-first (the clone), iOS in the consolidation pass after build 17 —
the parity ledger already carries: no-wall boot, aluminum kit, one-core rule,
SCRAPPER, pre-armed FIRST STEPS, telemetry, glyph substitutions.

## 8. Decisions (RESOLVED by owen, 2026-09-03)
1. **Gyro stays retired**; defeat advice rewritten to followable physics, and
   the crate carries a heavy ballast plate with a mount-it-low hint (§3).
2. **Crate: 50 scrap + 2 gussets + 1 STEEL plate** (§4D); checklist 4×10 (§4E).
3. **Web first**; iOS in the consolidation pass after build 17.
4. **Invest in richer animation now**: an animated ghost-hand that GLIDES from
   the palette tile to the target face and demonstrates the tap-tap gesture
   (glide → tap ripple → glide → tap ripple), looping until the player acts —
   procedural UGUI, no asset pipeline, synchronized with the pulse highlights.

---
⚠ `docs/` is the source of truth but is mirrored from the claude.ai Project —
this file is NEW here; add it to the Project so Cowork sessions see it.
