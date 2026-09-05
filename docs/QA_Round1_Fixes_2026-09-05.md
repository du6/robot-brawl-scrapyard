# QA round 1: the web play-test, what was fixed, web published, build 19 — 2026-09-05

Two play-test agents were put on the game on 2026-09-04 evening: one on the
live web build (Chrome, 500 px viewport, returning guest then a true first
run), one on iOS 2.2.0 build 18 in the Simulator. This records the web
tester's first report, owen's decision ("execute your plan"), and what landed.
The first-run pass and the iOS report are appended when they arrive.

## 1. The web tester's report (returning guest, live build of 2026-09-04)

Reached a fight in ~4 taps; first load ~22 s, cached ~10 s; fight to VICTORY;
"CLAIM & UPGRADE >" landed in SHOP with the purse credited; console clean.

| # | severity | finding | disposition |
|---|---|---|---|
| 1 | gameplay | count-out resets while SCRAPPER rams the flipped TIPPER; bout goes to judges | already fixed on the branch (`e0a5f11`); **web republished** |
| 2 | layout | debrief money/medal lines drawn under the buttons at phone width | **fixed**: button row clears the measured money + medal block; buttons shrink to the width |
| 3 | layout | fight HUD: QUIT overprinted by the title, names clipped both sides, banner see-through | **fixed**: HUD box fits the screen; narrow mode drops side lines to name + HP; banner narrows; title clears QUIT |
| 4 | confusing | a "DRIVE joystick" on screen during an autonomy fight | **fixed** (owen's phone, 2026-09-05): it is `Phase5Mobile.TouchControls` — the floating DRIVE stick + FIRE pad that give touch players their steering in MANUAL fights (`Phase0Input.debugThrottle/Steer`). It drew in autonomy fights too; both pads now release and draw nothing while `playerSource == Program`. ⚠ Two earlier records said "no touch steering exists" — that was a grep of the wrong files; TEST DRIVE's hide stands on owen's ask, not on that claim |
| 5 | confusing | a bought+welded gusset, fought with and rewarded, was lost on reload (never SAVEd) | **fixed**: a career fight saves the build it fights with (the fight gate already proves ownership + legality) |
| 6 | confusing | reward boxes pre-credit scrap/parts before opening; opening changes nothing | **fixed**: grants are deferred to the box opening (`CareerData.pendingRewards`, granted on Load if a box was lost) |
| 7 | confusing | sweeping the league grants the medal with no celebration | **fixed**: a LEAGUE SWEPT box |
| 8 | cosmetic | checklist "+" / "o" glyphs unexplained | **fixed**: ✓ / ○ on iOS, [x] / [ ] on web (Latin-1 font) |
| 9 | cosmetic | dock hops a row when a message appears; taps miss | **fixed**: the notice bar is reserved, not toggled |
| 10 | cosmetic | "HOLDING Gusset…" status follows the player to SHOP/LEAGUE | **fixed**: held-part status only on BUILD |
| 11 | cosmetic | ROBOTS: greyed RENAME/NEW ROBOT/NEW DRAFT unexplained; RETIRE un-greyed | **fixed** placeholder now says "type a name to enable NEW ROBOT / RENAME"; RETIRE already has a two-tap CONFIRM |
| 12 | cosmetic | PROGRAM footer prints part ids; "pieces" vs "parts" | **fixed**: labels via the palette; "pieces incl. wheels" |
| 13 | cosmetic | empty panel space; loading bar parks at 90% | skipped on purpose |

Also shipped in the same round: TEST DRIVE hidden on the career LEAGUE tab
(owen, 2026-09-04) — the "drive" button beside AUTONOMY FIGHT.

## 2. Measured

Every fix was applied to BOTH lineages (iOS repo `release/ios-build18`, web
clone `~/rb-webgl-spike`). Headless from the worktree, corrected runner:

| bench | before | after |
|---|---|---|
| CareerSmoke | 141 / 2 | 141 / 2 (the two 640×480 artifacts) |
| TouchSmoke | 53 / 0 | 53 / 0 |

Career save untouched (bench-written stub on disk; owen's restore still
pending, see `Build18_iOS_Port_2026-09-04.md` §3). The live editor compiled
the fixes clean after one slip (`Phase1Parts` → `P1PartDef.Palette()`).

⚠ The deferred-reward change touches the save format: `pendingRewards` is a
new list; JsonUtility hands every older save an empty one (the medals
precedent), which IS the migration. A reward queued on a build with this
change and opened on an older build cannot happen (the queue is in-memory).

## 3. Web: published 2026-09-05 07:55 PDT

`BuildWebGL.Build` headless from the clone: Succeeded, 0 errors, 8.35 MB,
payload stamp `?v=7e5a46a7fe`. The build's `index.html` matched the live
page except the stamp, so the deploy was a copy of `build/webgl/` into
`website/play/rb/` and `website/publish_site.zsh` (the force-push full-site
deploy — the established path). Verified live: the page serves the new stamp
and the wasm at 6,654,034 bytes.

## 4. iOS: build 19 swaps for 18 in review

`ProjectSettings` iPhone buildNumber 19, version stays 2.2.0 (`6640f05`).
`BuildIOS.Build` headless from the worktree pinned to that commit: Succeeded,
0 errors, Info.plist 2.2.0 / 19. Archive → export → `altool` upload →
`release_ios_build18.zsh swap` (new stage: encryption=no on 19, cancel the
open submission, re-point 2.2.0 at 19, submit).

**Result, 2026-09-05 08:05 PDT:** upload delivery
`7bb27462-1dad-4f34-9e0d-652335d8d0cb`, VALID in ~3 min. Submission
`cfa14a1d…` (build 18) cancelled; version 2.2.0 `2531b692…` now carries
**build 19**; new submission `3ceb637b-5fb4-4e2d-b216-566b0d84af00` →
**WAITING_FOR_REVIEW** (submitted 15:05:32Z). Release type still MANUAL.

⚠ The first `swap` run left build 18 attached and a new submission with NO
items: cancelling DEVELOPER_REJECTS the version for a moment and an attach
fired inside it is dropped silently (HTTP 204, no change). Finished by hand
(attach again → 204 and the read-back said 19 → add item → submitted=true);
the script now reads the version back until it carries the new build and
prints the API's error bodies instead of assuming success.

## 4b. Build 20 swaps for 19 (2026-09-05 09:08 PDT)

Three fixes landed after 19 went in: the guide no longer coaches mounting a
part already on the machine (both the fight-crossing and the refresh path),
and the DRIVE/FIRE touch pads stay hidden in autonomy fights. Build 20
(`d3f6d64`), same chain; delivery `e42cc69e-a7f1-460b-9e6a-21d1cd869377`,
VALID in ~3 min. The hardened `swap` stage did it in one command: cancelled
`3ceb637b…`, read the version back until it carried 20 (try 2), new
submission `6766e42f-4789-406e-8743-f4da446f30c6` → **WAITING_FOR_REVIEW**.

## 4c. The iOS Simulator report (build 18) and round 2 — 2026-09-05

The iOS tester (iPhone 17 Pro simulator, dev-pointed release build of
61000cf, guest play, no accounts) reached and won the first fight in three
taps, ~2 min from launch. One blocker: **the debrief overflowed the phone** —
BACK/REMATCH over the purse lines, the orange rookie door below the screen.
Fixed with a short-screen layout (H < 560 pt: smaller stat text, a one-line
money block, three buttons in one row pinned to the bottom). Also fixed:
the SAVE lesson now opens the collapsed dock and hides its hand behind the
save dialog; the guide says "Tap BUILD" instead of leaving a stale "Tap
AUTONOMY FIGHT" on LEAGUE; the wedge lesson aims at the lowest front face;
reward boxes compress vertically on a short screen; count-out toasts start
below the PROGRAM ARMED banner; the save card stays inside the safe area;
QUIT clears the rounded corner; the SHOP tile reads "SHOP >"; the palette
gets the "more below" hint; the ARENA note has room for two lines. Not
addressed: portrait launch (landscape-only by design), the SHOP card gap,
the small robot on the phone. Not covered by the tester: FIRST WELD, the
loss path + rescue crate, MANUAL FIGHT, TEST DRIVE, the hand's animation.

**Measured:** TouchSmoke **55/0** (+2: the palette is now a registered
scroller under the fixed-axis invariant). CareerSmoke **136/7** — and the
seven are the day-one runner artifacts, back: a control run of the
PREVIOUSLY GREEN code in the same worktree also read 136/7. The five extra
are all one thing: `TouchRow()` clamps at 110 canvas units, and with the
headless run now reporting `Screen.dpi = 266` at 640×480 (scale 0.577) that
clamp is 38.9 pt; the 141/2 runs saw `dpi = 0`, which both the sizing and
the check treat as "unknown" and pass. What flipped the headless DPI is not
known — the iOS tester was building a simulator player in the SAME worktree
during the first failing run, and the value stayed flipped afterwards. On a
real phone (dpi 460, scale ~1.13) the same clamp is exactly 44.0 pt. The
web was published from this code (`?v=bc092dd18b`); build 21 carries it.

## 4d. Build 21 swaps for 20 (2026-09-05 10:33 PDT)

Build 21 (`98fc3e2`), same chain; delivery `b1f8fe20-f40f-43d7-8bb0-ae39c6bd0806`,
VALID in ~3 min. ⚠ Build 20 had reached **IN_REVIEW** — Apple was already
looking at it — and the swap cancelled that; 2.2.0 now carries 21 under
submission `cbb856d4-3662-4c1d-a17d-02c7a88a3ec5`, **WAITING_FOR_REVIEW**,
back at the end of the queue. Worth weighing before the next swap: a
cosmetic round is not worth a lost review slot.

## 4e. Round 3 — the build-20 re-test and the web first-run pass (2026-09-05)

**The iOS re-test of build 20 found my debrief clamp WORSE than build 18:**
the purse lines were legible but the whole button row was below the 402 pt
screen - a stranger stuck on VICTORY with nothing to tap, exit only by
killing the app (the FIRST BOUT box was then lost; its grant was ledgered).
The tester's diagnosis: the wrapped cause line + two stat rows per side + a
"(pack damaged)" third line + the money block already reach the bottom, so
a clamp cannot help - something above must shrink, or the buttons must
overlay. Round 3 does both: on a short screen the title is 40 pt in a 56
band, the contest id and cause line 17 pt, stat rows 14 pt, the money block
one line, and the button row is PINNED to the bottom edge unconditionally
and drawn last. ⚠ Build 21 (in review) carries the round-2 layout, which
was NOT re-tested on the simulator before it went in; the round-3 layout is
being verified there now and goes in as build 22 if it passes.

Confirmed fixed by the same re-test: no DRIVE/FIRE pads in autonomy
fights; the LEAGUE dock no longer shifts when the coach line fades (byte-
identical frames 1 s and 6 s in); the post-fight coach line is no longer
stale; check-mark glyphs.

**The web first-run pass** (fresh IndexedDB, new build): no sign-in wall on
web by design; SCRAPPER present and active; the hand leads LEAGUE →
enabled AUTONOMY FIGHT → wedge → SAVE; FIRST BOUT / FIRST PART BOLTED /
FIRST WELD boxes all opened and credited at claim time (the deferred grant
works). New findings, fixed in round 3: the box auto-dismissed after 9 s so
a slow tap landed on the HIDE PANEL bar beneath it (now 30 s); the first
SAVE asks OVERWRITE vs SAVE AS NEW with no steer (the guide now says
"Tap OVERWRITE"); the empty weld kit stayed HELD so stray taps said "No
Gusset left" (put down on the last weld, like a part's auto-done); a fresh
start showed "[SCRAPPER *]" (the hand-written starter snapshot re-serialises
differently; the untouched starter is canonicalised once at boot). Noted,
not changed: a fresh player has 0 scrap and 0 free parts until the first
purse - the bolt/weld/buy checklist items are reachable only after the
first fight (design: kit = exactly SCRAPPER, spares as rewards). Owen's
browser career was backed up, cleared, and restored byte-for-byte (verified
by hash before and after boot).

**Measured, round 3:** TouchSmoke 55/0; CareerSmoke 136/7 = the control's
fail set (one bench line updated: the gusset probe re-selects the tool the
product now puts down). Web published as `?v=0c5a4e815a`.

## 5. Still open

- The web tester's first-run pass (sign-in wall, ghost hand, first boxes,
  rescue crate) and the iOS Simulator report.
- Owen's career save restore (one `cp`, in the build-18 record).
