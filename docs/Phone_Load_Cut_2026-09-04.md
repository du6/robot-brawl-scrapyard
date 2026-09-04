# The phone-load cut — 33 MB → 8 MB (2026-09-04)

**Why.** The web funnel: nine in ten arrivals are phones, and phones reached a
playable game less than half the time (iPhone 7/15, Android 3/9) while desktop
reached it every time. The payload was 33 MB. Nothing downstream — the hand,
the boxes, the crate — can matter to a player who never gets that far.

## What the 33 MB was (build report, uncompressed user assets 38.8 MB)

| what | size | why it was there |
|---|---|---|
| six MP3 music tracks in `Assets/Resources` | **21.0 MB (54%)** | packed in full, incompressible, downloaded before frame one |
| `com.unity.ai.inference` (Sentis) compute shaders | ~8 MB | a package in the manifest with **no script reference anywhere** — Resources folders ship whether used or not |
| Unity splash logo texture | 2.7 MB | the splash screen, no longer required on any plan in Unity 6 |
| URP post-processing textures (film grain ×10, SMAA) | 2.8 MB | `postProcessData` linked on both renderers; the scene has no Volume |
| TextMesh Pro essentials | 1.3 MB | no TMPro reference in any game script (the UI is legacy `Text`) |
| the game's own code (wasm) | 7.7 MB | the part that is actually the game |

## What was done (web clone only; main/iOS untouched)

1. **Music streams instead of shipping.** `MusicLoader` fetches a track from
   `play/rb/music/<name>.mp3` (same origin, beside the page) the moment a
   theme is picked, caches it for the session, and falls through the list on
   a miss — exactly the old Resources fallthrough. `FightManager` and the
   build playlist in `BuilderManager` go through it; on every other platform
   it is `Resources.Load`, synchronous, unchanged. The site copies are
   re-encoded mono 32 kHz 56 kbps: 6.5 MB for all six, ~2 MB fetched per
   session (one build theme + one fight theme), after boot, off the critical
   path.
2. Sentis and the editor AI assistant removed from the clone's manifest.
3. Splash off (`PlayerSettings.SplashScreen.show = false` in `BuildWebGL`).
4. `postProcessData` unlinked on `Mobile_Renderer` / `PC_Renderer`.
5. `Assets/TextMesh Pro` deleted from the clone.
6. **A loading screen with something to watch**: two robots charge, collide
   and spark on a canvas (60 lines, no image), a tip rotates underneath, and
   the 100% state says what it is doing — "starting the engine… (on phones
   this is the slow part)".

## Measured

| | before | after |
|---|---|---|
| `webgl.data.unityweb` | 26.5 MB | **1.5 MB** |
| `webgl.wasm.unityweb` | 7.7 MB | 6.7 MB |
| on-disk payload | 33 MB | **8.0 MB** |
| local load to `ready` | 2–5 s | 1 s |

Verified in the browser: loading screen animates with a tip; boot; the build
playlist fetched `music/BuildTheme_NeonAtriumDrift.mp3` (200) and played; no
console errors; every beacon phase still fires in order.

## What to watch

`zsh server/scripts/web_funnel.zsh 3d` in a few days: the phone playable rate
(was 12/27) and THE LOAD GAP's silent count. If phones still die at
"starting", the next lever is the engine's initial heap, not the download.

## Parity ledger

`MusicLoader.cs`, the two music call sites and the manifest/renderer/splash
changes are in the web clone only. iOS still packs the music (a phone app's
download is one-time and expected); nothing here should be ported except the
splash setting, which is a free 2.7 MB on iOS too.
