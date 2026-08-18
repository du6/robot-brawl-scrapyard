# The promo video — how it was made, and how to remake it

2026-08-18. 21 seconds, on the home and Robot Brawl pages. **Every 3D shot is
the real game**: the same three.js scene the champions page renders, from the
same `parts.json` recipes and the same recorded match. Nothing is mocked up.

## Why the obvious routes do not work

The browser can produce the pixels but cannot write a file, and **MediaRecorder
is unusable here**. The automated tab is hidden, so `requestAnimationFrame` is
suspended entirely and `setTimeout` is throttled to about 1/s — nothing can be
recorded in real time, and a real-time recording is what MediaRecorder is.
`canvas.captureStream()` has the same problem from the other end: it timestamps
frames by wall clock, so pushing 300 frames as fast as they render produces a
two-second video.

So the clock is **driven by hand** and the frames are shipped out one at a time.

## The rig

1. `scratchpad/framecollect.py` (or any equivalent) listens on `127.0.0.1:8899`
   and writes whatever is POSTed to it into a directory.
2. The champions page is opened, the stage is taken out of layout and sized to
   16:9 with `__stage.setSize(1280, 720)`, and the on-canvas chrome is hidden.
3. A loop drives `__stage` one frame at a time — `spin(angle)` for a turntable,
   `run(2)` for the replay — reads the canvas with `toDataURL('image/jpeg')` at
   **2556x1436**, and POSTs it.
4. `website/tools/build_promo.sh` cuts the film with ffmpeg.

### ⚠ `run()` does not advance the clip unless `play(true)` is set

`settle()` sets `playing = false`, and `advance()` only moves `t` while
playing. Capturing straight after a settle produced 150 frames of which the
first 40 differed (the camera was still converging) and the rest were
byte-identical. It looks exactly like a frozen replay. Call `play(true)` first.

### ⚠ `setSize` exists because ResizeObserver does not fire here

ResizeObserver delivery is tied to the browser's rendering steps, and a hidden
tab has none. Setting the stage's CSS size changes the box and the renderer
never hears about it: the drawing buffer stays at the old resolution. That is
also why the framing work could only be checked at one aspect ratio — `setSize`
closes that gap and is worth using for aspect-ratio testing, not just capture.

## ⚠ This ffmpeg has no `drawtext`

It is built without libfreetype, so **every word on screen** — title, captions,
end card — is drawn on a `<canvas>` in the browser and composited as a PNG.
A side benefit: the type matches the site exactly, because it is the same font
stack.

## ⚠ A still image input is one frame at timestamp 0

The first cut had **no text on any shot**. `fade=t=in:st=0.6` on a
single-frame input never reaches 0.6s, so the overlay sat at alpha 0 for the
whole segment. It looked precisely as though the overlay filter had been left
out of the command. The fix is `-loop 1` on the caption input (advancing
timestamps) plus `overlay=shortest=1` so the segment still ends with the
footage.

## What is in the cut, and why

| shot | source | why |
|---|---|---|
| title card | canvas | — |
| Spinner1 turning | live 3D, 120 frames | the hero machine |
| Hammer turning | live 3D, 84 frames | **a second, differently shaped machine** |
| the 167-damage hit | live 3D, 150 frames | the product's actual selling point |
| the ladder | `league.jpg` | "take it online" has to show a real ladder |
| end card | canvas | — |

The third slot **used to be a UI screenshot and was the weakest shot in the
film.** The game's panels are dense tables; downscaled into a 1080p frame the
text is illegible, so it read as a generic dark rectangle — it showed that the
game *has* a shop without showing anything worth wanting. "You choose every
part" is a claim; two machines that look nothing alike is the evidence.

## Serving it

`preload="none"` with a poster, so nothing but a 167 KB image moves until
someone presses play. `assets/promo.js` picks the 720p file on narrow screens
and when Save-Data is set — **`<source media="...">` is ignored by browsers for
`<video>`**, they take the first playable source, so without this a phone would
pull the 10 MB 1080p file to play it in a 360px box.

Both files are H.264 / `yuv420p` (without which Safari and QuickTime refuse the
file outright) with `+faststart`, verified.

### ⚠ Asset links are versioned now, and that is load-bearing

`?v=20260818b` on `style.css` and the scripts. The video block needs new CSS,
and the stylesheet had no cache-buster — a returning visitor would have been
served the new markup with their cached stylesheet and seen the video render at
its intrinsic **1920x1080, bursting out of the page**. Caught locally by exactly
that symptom: the rule was on the server and absent from the loaded sheet.
**Bump the stamp whenever markup starts depending on new CSS.**

## No audio

Adding music means licensing it. A silent promo is also what inline autoplay
would require, if that is ever wanted.

## Remaking it

Re-run the capture (the frames are not committed — 92 MB of JPEGs), then:

```sh
bash website/tools/build_promo.sh /tmp/promo_frames /tmp/promo_out
```

The script is committed; the frames are not. If the champions page's models,
recipes or shot solver change, the film is stale — it is a rendering of that
page at a moment in time.
