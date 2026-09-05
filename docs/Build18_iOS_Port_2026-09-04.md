# Build 18 (2.2.0): the web warm-up ported to iOS — 2026-09-04

owen: "apply all features other than the sign-in wall in the feature table to
the iOS app and cut a new build to release." This is the record of that port,
what was measured, what the measuring broke, and the three outward steps that
are owen's to run.

## 1. What went into build 18

Branch `release/ios-build18`, from `61000cf` (the port) + `29a1dbd` (bench
reconciliation) + this record's commit. Version **2.2.0**, build **18**.

| feature | iOS 2.2.0 | note |
|---|---|---|
| sign-in wall | **kept** | owen's one exclusion |
| SCRAPPER pre-built starter (sensored, RamHunter) | yes | injected on any career with no robots and no fights |
| kit = exactly SCRAPPER, spares as checklist rewards | yes | |
| aluminum starter kit | yes | |
| pre-armed FIRST STEPS / RamHunter program | yes | |
| ghost-hand guide, pre + post fight | yes | `RookieGuide.Tick` is `#if !UNITY_EDITOR` — device builds only |
| reward boxes, checklist, rescue crate, debrief buttons | yes | `Career.QueueReward` is `#if !UNITY_EDITOR` |
| one-core rule | yes | |
| boot loads the active robot | yes | |
| starter inventory top-up until the first fight | yes | the greyed-AUTONOMY-FIGHT fix from owen's phone |
| QA fixes: auto-orient, keystroke leak, material-button sync, shop-hover mute, weld debounce, count-out no-reset, followable defeat copy | yes | |
| Unity splash off | yes | the free iOS load win; the web payload cut itself does not apply |
| telemetry | **no** | web-only by construction (`#if UNITY_WEBGL`), so iOS still has no funnel |
| glyphs | unicode | the web clone substitutes ASCII (`-`, `^`/`v`) because its font is Latin-1 only; iOS keeps `—`, `▴▾`, `▲▼`, `⚠` |

## 2. Measured

All headless, from a git worktree (`BatchSmoke.Career` / `.Touch`, batchmode
-nographics), because owen's live editor was open on the project with the
Device Simulator up — and a bench run *in* that editor is not comparable
(its screen-coordinate placements fail under the simulator's geometry; it
read 119/24 for reasons that had nothing to do with the port).

| bench | pre-port control | port, first runner | port, corrected runner |
|---|---|---|---|
| CareerSmoke | 136 / 7 | 128 / 15 → 136 / 7 after reconciliation | **141 / 2** |
| TouchSmoke | 50 / 3, then 46 / 7 | 39 / 14, 38 / 15, 39 / 14 | **53 / 0** (control: 53 / 0) |

Both remaining CareerSmoke fails are the long-standing runner artifacts
(`C6.5 DRAFT banner`, the 10.5 pt label sweep at 640×480).

### The eight port-mechanical CareerSmoke fails (none a product defect)

- **material button + dock handle carets ×3** — the port carried the web
  clone's ASCII `^`/`v`; the bench asserts the unicode disclosure glyphs, and
  iOS's font renders them. Restored.
- **GUSSET ×4** — the deterministic bare-core reset that isolates C3/C10 from
  the rookie economy left the build with only a core, so the weld target was
  null. The section now loads the SAWYER fixture when the build is bare, and
  waits a frame between welds so the product's same-frame double-weld debounce
  (a synthetic-input artifact on the web) does not eat the deliberate second.
- **auto-done ×2** — C3's "own the bolted beam" grant became a spare once C3
  starts from a bare core; the setup now consumes down to exactly one.
- **economy drift** — the rookie checklist pays +10 scrap and parts for the
  first bolt/weld/buy/fight, which shifts every bench-hardcoded scrap total.
  `CoreEconomyOnly()` marks the checklist done after each `new CareerData()`.

### The TouchSmoke fails were the RUNNER, proven with a control

Three port samples failed the same 11 gusset checks the control passed. The
instrumented tap said: the click landed on the beam's exact screen centre
(320, 307.9), the beam's collider was registered, enabled, layer 0 — and
`RaycastAll` along that ray saw **only the floor**. Frame timing explained it:
`-batchmode -nographics` ran at **3,000–4,000 fps** (`dt=0.0003`), so the
bench's "yield two frames" passed in under a millisecond, the 20 ms physics
step never ran between the placement and the tap, and the new collider was
not yet in the physics scene. `Application.targetFrameRate = 60` is ignored in
batchmode (measured). A `Thread.Sleep(16)` per editor-loop frame in
`BatchSmoke.Tick` is what caps it: at ~55 fps the same ray hits `beam_1` at
d=3.88 and both legs read 53/53. Prediction written before the run: the
gusset and REMOVE fails vanish. They did.

⚠ This also means **every headless placement/raycast bench before today was
racing physics**, and the control leg's own "armed tap removes the part" fail
was the same race. On a phone at 60 fps two frames are 33 ms; a finger cannot
tap inside a physics step. Not a product defect.

## 3. THE CAREER-SAVE INCIDENT (house rule 5, again)

**The first headless TouchSmoke overwrote owen's career save** with a
10-scrap bench career. Root cause: TouchSmoke's C1 section swaps
`Career.Data` in memory and relied on "nothing here calls Save()" — true until
the rookie checklist started paying rewards: `RookieTaskBolt()` ends in
`if (autosave) Save()`. CareerSmoke was safe because it holds
`Career.SuspendAutosave()` for its whole run; TouchSmoke never did.

- **Fixed**: TouchSmoke now takes the same counted hold across its run, and C1
  marks the checklist done so the +2-beam bolt reward cannot refill the shelf
  it empties on purpose (those were its three `career:` fails).
- **Recovered, not byte-exact**: no copy of the lost file (md5 `12ad5a4e`,
  mtime 2026-08-20 06:30) exists anywhere on disk, in `career_backups/`, or in
  Time Machine (none configured). The closest is the file as this session
  wrote it on 2026-08-19 22:01 after adding `flipper_v4_welded` — scrap 6513,
  five robots, 11 fights — recovered from the session transcript and saved as
  `career_backups/robotbrawl_career.20260904_restored_from_20260819_2201_transcript.json`.
  Whatever the game wrote between then and 08-20 06:30 (probably a session
  counter or a fight) is lost. The bench-written file is kept beside it for
  the record (`…20260904_1851_touchsmoke_overwrite.json`).
- **Owen restores it** — this session cannot write under `~/Library`:
  ```sh
  cp career_backups/robotbrawl_career.20260904_restored_from_20260819_2201_transcript.json \
     "$HOME/Library/Application Support/owen/Robot Brawl_ Bolt & Blade/robotbrawl_career.json"
  ```
- Every bench run after the fix left the file byte-identical, except the
  corrected-runner CareerSmoke, which wrote once at boot: the stub on disk had
  `kitGranted=false`, so the product's own fresh-career kit grant fired and
  saved. That path never runs on a real save (kit already granted) and it
  did not run on owen's file across the earlier runs. Restore first, then run.

## 4. The build

- `BuildIOS.Build` headless from the worktree (`-buildTarget iOS`,
  `-rbOutDir`): **Succeeded, 0 errors, 419 warnings, 1:53**, ATS arbitrary
  loads off, bundle `club.cyberduck.robotbrawl`, minOS 15.0, Info.plist
  2.2.0 / 18, no `RB_DEV_SERVER` in the pbxproj.
- `xcodebuild archive` → `/tmp/rb_b18/build/RobotBrawl-b18.xcarchive`,
  `-exportArchive` → `/tmp/rb_b18/build/export/RobotBrawlBoltBlade.ipa`
  (113 MB). Both **succeeded**.
- **SUBMITTED, 2026-09-04 19:56 PDT, on owen's explicit "send it out for
  app store review".** Upload delivery UUID
  `6fe9ff3b-11bf-4c4c-8aed-26e7b4b98008` (2.6 min, processed VALID in ~3);
  App Store version 2.2.0 `2531b692-68f9-4354-928d-8763cca4404f`, build 18
  attached, `usesNonExemptEncryption=false`; review submission
  `cfa14a1d-27fa-4922-b0fd-b6ee45470663` → **WAITING_FOR_REVIEW**. Release
  type is **MANUAL**: when Apple approves, 2.2.0 waits for owen to press
  Release in ASC; 2.1.3 stays on sale until then. (The earlier auto-mode
  refusal of `altool` lifted once the instruction was explicit.)

### The three commands that did it (each explicit, `status` is read-only)

```sh
zsh server/scripts/release_ios_build18.zsh status    # verified working today
zsh server/scripts/release_ios_build18.zsh upload    # altool → processing 10-20 min
zsh server/scripts/release_ios_build18.zsh version   # encryption=no, create 2.2.0 + What's New, attach 18
zsh server/scripts/release_ios_build18.zsh submit    # review submission, submitted=true
```

The What's New text is in the script; edit it there. The token helper
`server/scripts/asc_jwt_env.sh` is now in the repo — it had been lost from a
scratchpad twice.

## 5. What is still owed

- **A device run of 2.2.0** — the ghost-hand guide, reward boxes, and rescue
  crate are `#if !UNITY_EDITOR`, so no bench in this project has ever drawn
  them on iOS; the web clone is the only place they have been seen. TestFlight
  build 18 on owen's phone before `submit` is the honest gate.
- The 640×480 label-size and DRAFT-banner artifacts remain the headless
  floor (2 fails).
- iOS has no funnel. If the App Store should be measured the way the web is,
  that is a design decision (privacy policy), not a port.
