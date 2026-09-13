# Playtest fixes — 2026-09-13

Implemented after a desktop browser playtest of the published Scrapyard game and a source review.

## Player-facing changes

- Chest rolls use the world and individual chest key. The first three provide a wedge, a weld kit, and armor; farther exploration offers better materials and larger drops.
- The opening guide tracks completed actions separately. Visiting the shop early cannot skip the first bout, and the guide follows through fitting an upgrade and trying it in combat.
- Exploration keeps a safe position and heading across reloads. Flipped or fallen positions cannot replace the checkpoint.
- The garage fits the robot into the space above the dock, exposes Fit and zoom controls, and offers readable browser text with an interface-size setting. The shop begins with useful available parts and a collapsed catalog.
- Fight results lead with the deciding metric and a practical tip. The detailed scorecard expands on demand, with a direct route to fitting upgrades.
- The combat camera checks both robots against walls. The arena skirt sits below the floor, and intersecting exploration props and the objective beam are hidden during combat.
- The loader explains manual controls, stops its animation and tip timers after completion, and offers retry on loading failure.

## Reliability

Career saves validate staged writes, retain a backup, recover valid staging or backups, and preserve corrupt originals. An unrecoverable save produces a persistent notice and blocks overwriting the original. Procedural terrain and scatter meshes now have explicit ownership and are released when their chunks leave the world. Failed opponent-pool fetches retry with a bounded delay.

WebGL builds enable explicitly thrown exceptions so filesystem and JSON recovery can catch failures. The first browser build exposed a startup halt with the old `None` setting, which disables catching; see [Unity's exception settings](https://docs.unity3d.com/6000.0/ScriptReference/PlayerSettings.WebGL-exceptionSupport.html).

The shared fight-core files were not edited. The server snapshot was not deployed. Separate concurrent work added repeat-opponent reward rules; those edits were preserved.

## Validation record

Unity 6000.5.4f1 ran in `/private/tmp/scrapyard-qa-20260912`, an isolated checkout. Play-mode runs used a separate company/product identity and the development-only `-scrapyardSaveDir` argument. No bench ran in the owner's live editor.

| Check | Result |
|---|---:|
| Unchanged map baseline at `f8e5956` | 157 pass, 0 fail |
| Updated MapBench | 160 pass, 0 fail |
| JourneyBench: guide, checkpoint reload, collected chests, mesh release | 16 pass, 0 fail |
| QuickFightBench: actual timed bout and rewards | 24 pass, 0 fail |
| TouchSmoke: controls and layout | 59 pass, 0 fail |
| CareerRewardsBench: rewards, migration, staged/backup recovery | 52 pass, 0 fail |
| CombatReadabilityBench | 24 pass, 0 fail |
| GarageUXBench: font sizing and projection | 300 pass, 0 fail |

Logs are in `/private/tmp/scrapyard-review-*.log`. Unity emitted an editor search-index startup exception that also appeared in the unchanged baseline; it did not fail these suites. The seven listed updated suites total **635 passing checks**.

## Browser verification

The final release build succeeded with zero errors. Its generated WebGL payload is 7,265,658 bytes; the complete output including six streamed music tracks is about 13.5 MB. The build logged 70 warnings, including existing obsolete Unity API warnings. Content-stamped payload URLs use `03f7eb7bc4`.

The build was played on localhost in the in-app browser. Checks covered:

- Fresh boot, first treasure (+45 scrap and an aluminum wedge), opening and claiming rewards.
- Shop suggestions, the collapsed catalog, fitting the wedge, and the next-step guidance.
- Garage framing, visible Fit/−/+ controls, and 100%, 115%, and 130% interface sizing. The minus glyph was changed to an ASCII hyphen after browser inspection showed the original glyph missing.
- Desktop 1280×720, the normal 639×678 panel, and a 390×844 portrait viewport; temporary viewport settings were reset afterward.
- Reloading restored the safe expedition checkpoint away from home and retained the 12-part upgraded robot, rewards, and guide state.
- A complete manual Scout bout, including the closing-wall phase. Both robots remained visible; the floor was stable and the exploration beam and intersecting props were absent from the arena.
- Victory summary (damage 124 vs 12), the expandable scorecard, +32 scrap, and the Fit Upgrades route back to the garage.
- Reloading the final build retained post-fight progress. No new browser errors or warnings were logged after correcting WebGL exception support.

The ready-to-serve output is `build/review-webgl/`. It has not been published. These checks do not cover physical iOS/Android devices, server deployment, or broad statistical balance testing.
