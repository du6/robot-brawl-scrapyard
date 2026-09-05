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

## 5. Still open

- The web tester's first-run pass (sign-in wall, ghost hand, first boxes,
  rescue crate) and the iOS Simulator report.
- Owen's career save restore (one `cp`, in the build-18 record).
