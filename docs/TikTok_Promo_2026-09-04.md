# The TikTok promo — 17.8 s vertical (2026-09-04)

Master: `_claude_backups/tiktok__robot-brawl-tiktok.mp4` (1080x1920, 30 fps,
H.264 + AAC, 8.9 MB). Rig: `website/tools/build_tiktok.sh`,
`website/tools/tiktok_captions.html`, `website/tools/framecollect.py`.

## The cut

| t | shot | source | words |
|---|---|---|---|
| 0–2.9 | cold open on the hit, slight slow-mo | FEATHER champion's recorded fight, frames 20–89 at 24 fps | **It drives itself.** Your robot. Your program. |
| 2.9–5.4 | the champion turning | model turntable, 75 frames | **Build it. Program it.** Then watch it fight. |
| 5.4–8.9 | second fight | MIDDLE champion (flipper_v2), 105 frames | **Every win pays.** Every loss drops a crate. |
| 8.9–11.6 | third fight, link only | SUPER champion (Spinner1), 80 frames | — |
| 11.6–14.6 | SCRAPPER in the workshop, slow push-in | Unity still, centre crop | **A robot is waiting for you.** Fight in 30 seconds. |
| 14.6–17.8 | end card | canvas | Play free in your browser · no download · no sign-up · **cyberduck.club/play/rb** |

Every 3D frame is the real game: the champions page's own three.js stage,
the same recorded ladder matches it shows visitors, rendered at 9:16 with
`__stage.setSize(540, 960)` and driven frame by frame (the tab is hidden, so
nothing can be recorded in real time — see `docs/Promo_Video_2026-08-18.md`).
The link is pinned at ~78% height on every shot, above TikTok's own UI band,
and the end card puts it in a yellow block. Music is FightTheme_ChromeWar.

## Two traps that cost a cut each

- **`-frames:v` counts OUTPUT frames.** A 24 fps shot given `-frames:v 70`
  ended at 2.33 s of output — before its own fade-out, with 14 of its frames
  never shown. Use `-t`.
- **The concat DEMUXER decoded every join dark for about a second, even when
  re-encoding** — segments born from `-loop 1` image inputs and segments from
  frame sequences carry different H.264 parameter sets. The concat FILTER
  (decode every segment, encode once) is clean. The check that settled it is
  worth keeping: an every-frame `signalstats` YAVG scan, which should find
  dark frames only inside the fades (34 of 533 here, at the five joins).
  ⚠ And `ffmpeg -ss T -i file` input-seeking read those joins as black
  regardless — verify with sequential decoding (`-i file -ss T`), not seeks.

## Reuse

Frames: open `champions/` on a local server, `setSize(540,960)`,
`isolate.tags(false)`, `settle(tPeak - 1.4)`, `play(true)`, then `run(2)` +
`toDataURL` + POST per frame (30 fps). Captions: open `tiktok_captions.html`
on a local server with the collector running and call `make()`. Then
`bash website/tools/build_tiktok.sh`.
