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
| 4 | confusing | a "DRIVE joystick" on screen during an autonomy fight | **open** — no control by that name exists in either lineage (throttle/steer are keyboard-only); tester asked for a screenshot |
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
open submission, re-point 2.2.0 at 19, submit). Result recorded below.

## 5. Still open

- Defect 4 (the "DRIVE joystick") — waiting on the tester's screenshot.
- The web tester's first-run pass (sign-in wall, ghost hand, first boxes,
  rescue crate) and the iOS Simulator report.
- Owen's career save restore (one `cp`, in the build-18 record).
