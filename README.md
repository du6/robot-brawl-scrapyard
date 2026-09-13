# Robot Brawl: Scrapyard

A physics robot building and combat game made in Unity. Drive across a seeded scrapyard, collect treasure chests, fit the parts you find, and challenge parked robots in short fights that you control.

[Play in your browser](https://cyberduck.club/play/scrapyard/) · [Game design](docs/Scrapyard_Design_2026-09-09.md) · [Contributor notes](CLAUDE.md)

## The game

- Start in the yard with a small robot and a passive front spike. Drive into chests to collect scrap and parts.
- Follow the compass and world markers to robots, trading posts, and other landmarks. Opponents get tougher farther from home.
- Open **GARAGE** to change your build, manage saved robots, or buy parts in **SHOP**. Save your build and **DRIVE OUT** to try it.
- Challenge a yard robot for a 30-second fight. Closing walls put pressure on the final ten seconds; damage, detached pieces, and mobility matter.
- Read the result, fit an upgrade, and return to the yard. Your world seed, inventory, and saved builds persist locally.

Exploration and yard-bot fights need no account. Challenging another player's machine prompts sign-in; those challenges use the shared online referee and points board. Online features need the API; the local game does not need a server running on your computer.

## Controls

| Input | Action |
| --- | --- |
| WASD / arrow keys, or the on-screen stick | Drive and steer in the yard and fights |
| Ram with the robot's front | Use the starter's passive spike; reverse to line up the next hit |
| Space / **FIRE**, when an active weapon is fitted | Operate powered weapons |
| **GARAGE** | Open the workshop |
| Part palette and attachment points | Choose and place parts; use the workshop's rotate/remove controls to adjust them |
| **SAVE**, then **DRIVE OUT** | Keep the build and resume exploring |

The starter has no active weapon, so it has no **FIRE** button. The robot's front arrow shows its drive direction.

## Open the project

1. Clone this repository, including `Assets`, `Packages`, and `ProjectSettings`.
2. In Unity Hub, add this repository folder as an existing project. Use **Unity 6000.5.4f1**, the version recorded in `ProjectSettings/ProjectVersion.txt`.
3. Open `Assets/Scenes/Main.unity` and press **Play**. The game creates its world and workshop at runtime and loads the editor's saved career.

A fresh editor career deliberately starts with a bare core so test fixtures stay isolated. Build a driveable robot in the workshop and use **DRIVE OUT**, or run the map bench below for its isolated starter fixture. Browser and device builds grant the complete **SCRAPPER** starter automatically.

For browser builds, install the editor's **WebGL Build Support** module. Do not copy these scripts into an empty project: the repository includes the scene, render settings, package configuration, and build tooling they expect.

## Build for the browser

Use an isolated clone or worktree for batch builds, with no editor using that checkout. Switching its active target to WebGL must not change the project open in your working editor. Replace `Unity` below with the path to your Unity executable, and run the command from the isolated checkout:

```sh
Unity -quit -batchmode -nographics -projectPath . \
  -buildTarget WebGL \
  -executeMethod RobotBrawl.Editor.BuildWebGL.Build \
  -logFile /tmp/scrapyard-webgl-build.log
```

The default output is `build/webgl`; `-rbOutDir /absolute/output/path` overrides it. The build uses `Assets/WebGLTemplates/RobotBrawl/index.html`, with Brotli compression and the decompression fallback for static hosting. Optional MP3 tracks come from `music_web/` or a directory supplied with `-rbMusicDir`.

Serve the output over HTTP to play it locally, for example:

```sh
python3 -m http.server 8080 --directory build/webgl
```

Then open [the local build](http://localhost:8080). Building does not publish the game.

## Validation

The Unity benches live alongside the game scripts and are compiled only for the editor or development builds. Use an isolated clone or worktree containing the changes under test, with no editor using that checkout. Run one play-mode bench at a time. Select any required test/build target in that isolated copy, leaving the active target in your working editor untouched.

Replace `Unity` with the path to your Unity executable and supply an absolute path to the isolated checkout:

```sh
Unity -batchmode -nographics -projectPath /absolute/path/to/isolated-checkout \
  -executeMethod RobotBrawl.EditorTools.BatchSmoke.ReviewPure \
  -scrapyardSaveDir /tmp/scrapyard-review-bench \
  -logFile /tmp/scrapyard-review-bench.log
```

`ReviewPure` runs the rewards/persistence, combat readability, and garage sizing/projection benches without entering play mode. `BatchSmoke.Rewards` runs only the rewards/persistence portion.

For a play-mode check, substitute `BatchSmoke.Journey`, `BatchSmoke.Map`, or `BatchSmoke.Quick` for the method name. For example:

```sh
Unity -batchmode -nographics -projectPath /absolute/path/to/isolated-checkout \
  -executeMethod RobotBrawl.EditorTools.BatchSmoke.Journey \
  -scrapyardSaveDir /tmp/scrapyard-journey-bench \
  -logFile /tmp/scrapyard-journey-bench.log
```

Give every invocation its own absolute `-scrapyardSaveDir` directory and log file. Do not add `-quit` to play-mode commands: the driver waits for its coroutine to finish and exits itself. `BatchSmoke.FightWorker` remains available for the shared worker path.

The implementation review on **2026-09-13** measured these results in Unity:

| Entry point | Coverage | Passed | Failed |
| --- | --- | --- | --- |
| `BatchSmoke.ReviewPure` | Rewards/persistence; combat camera/results; garage sizing/projection | 45; 24; 300 | 0 |
| `BatchSmoke.Rewards` (final hardening pass) | Rewards, migration and save recovery | 52 | 0 |
| `BatchSmoke.Journey` | Guided upgrade loop, expedition resume, generated mesh cleanup | 16 | 0 |
| `BatchSmoke.Map` | Exploration, encounters, landmarks, driving and world transitions | 160 | 0 |
| `BatchSmoke.Quick` | Quick fights and their economy | 24 | 0 |
| `BatchSmoke.Touch` | Touch controls and dock layout | 59 | 0 |

Read each bench's `RESULT` / pass-fail summary and Unity errors in the log; a process that finishes is not by itself a passing test. These counts record that run and are not thresholds to preserve when adding meaningful coverage.

Player saves must survive validation unchanged. Benches that mutate career data must swap in isolated data, hold `Career.SuspendAutosave()`, and restore the original state. The development-only `-scrapyardSaveDir` argument also isolates writes during boot. The four new benchmark files (`CareerRewardsBench`, `CombatReadabilityBench`, `GarageUXBench`, and `JourneyBench`) are guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD`; their batch entry points live under `Assets/Editor`, so release builds exclude them. See `CLAUDE.md` before running or extending other historical benches; some target systems inherited from the original game.

## Code map

| Location | Responsibility |
| --- | --- |
| `Assets/Phase0/Scripts/` | Robot bodies, materials, joints, raycast wheel physics, input |
| `Assets/Phase1/Scripts/BuilderManager*.cs` | Garage, world generation, exploration, encounters and landmarks |
| `Assets/Phase1/Scripts/FightManager.cs`, `FightCamera.cs` | Local fight flow, judging presentation and camera |
| `Assets/Phase1/Scripts/Career.cs` | Inventory, rewards and local career persistence |
| `Assets/Phase1/Scripts/MobileBuilderUI.cs`, `MapHudUI.cs` | Workshop and exploration UI |
| `Assets/Editor/` | Build entry points and batch bench drivers |
| `Assets/WebGLTemplates/RobotBrawl/` | Browser loader and anonymous progress notices |
| `docs/` | Design records, decisions and measured QA results |
| `server/` | Snapshot of the shared API/worker code; see below |

Scrapyard is a separate game forked from Robot Brawl: Bolt & Blade. Older `RobotBrawl` namespaces and Phase0/Phase1 folder names remain, but the product and saves are separate: `scrapyard_save.json` lives under Unity's persistent data directory for **Robot Brawl: Scrapyard**. Browser saves use IndexedDB through Unity's persistent filesystem.

The authoritative shared server is maintained in the original Robot Brawl repository; **do not deploy the snapshot in this repository**. The fight-core files listed in `CLAUDE.md` must remain identical to the referee's implementation. Changes to that shared core require coordinated fixes and passing suites in both repositories.
