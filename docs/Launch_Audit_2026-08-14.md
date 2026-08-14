# Launch audit — 2026-08-14: DO NOT LAUNCH the live-fight build

The five-lens adversarial review of the live-arena-fight delta, plus a
device-faithful determinism probe, found **defects that block launch and one
live security hole on the production server**. Raw agent output:
`Launch_Audit_Findings_2026-08-14.json`. Nothing here is a style note; every
item below was adversarially verified (0 findings refuted).

## Measured, not argued: the determinism probe

`DeterminismProbe.cs` plays N real challenges LOCALLY at speed 1 (what an
iPad runs) and compares each to the cloud referee's settled verdict.

- **Batch 1** (ABS mirror robots): 6/6 agree — all DRAWS. **A false pass.**
  The house robots genuinely draw that matchup, so both sides agreeing on
  DRAW proved nothing. This is the trap the earlier "determinism HOLDS"
  reading fell into.
- **Batch 2** (Titanium bruiser that wins decisively): **5 agree, 1 DISAGREE**
  — match 0 local DRAW vs referee WON. In the EDITOR, same platform, before
  cross-platform PhysX is even in question. The cause is the wall-cap bug
  below: a full-distance fight at speed 1 hits the 90 s real-time cap and is
  force-scored DRAW locally while the worker (speed 10) fights to the bell
  and judges a winner.

## LAUNCH-BLOCKING

1. **The 90 s wall cap is real-time, sized for headless speed 10.** At the
   live speed of 1, settle (~1 s) + a full 90-sim-second match > 90 s cap, so
   EVERY distance fight ends `match wall timeout` → local DRAW seconds before
   the real bell, skipping the judges' decision entirely. (Found by 3 lenses
   independently AND by the probe.) The player is shown a draw the referee
   contradicts. Fix before anything else: scale the cap by 1/speed (or make it
   sim-time), and re-run the probe.
2. **Live arena fights pay REAL single-player profile scrap.** MatchRunner
   isolates `Career.Data` but NOT `Progression.Data` (a separate persisted
   file). `FightManager.End → Progression.OnMatchEnd` runs the SANDBOX branch
   (Career.active=false), which credits exhibition scrap into the shipped
   single-player economy and `Save()`s it — every live challenge mints local
   scrap, and QUIT-conceding pays too. Also mis-displays "+N scrap (exhibition
   win)" on a ladder match the cloud settles.
3. **Cross-platform PhysX is not guaranteed deterministic** (iOS arm64 vs
   Linux x64). Even with the wall cap fixed, the two fights may diverge. The
   results copy promises the purse ("VICTORY — the purse is on its way")
   before the referee rules. **This is architectural**: either the copy must
   stop asserting an outcome the referee owns (recommended — show "fighting…
   the referee is settling this" and let MY FIGHTS deliver the verdict), or
   the client must not simulate at all and instead poll for and replay the
   worker's recording.

## REAL (fix before wide release, not necessarily before a fix-and-retest)

4. **Program-exfiltration on the LIVE production server.** `GET
   /v1/matches/{id}/envelopes` is "participant-gated," but participation is
   self-service: challenge any victim's ACTIVE snapshot (gap≥0 always
   satisfiable from the lowest class), then read the endpoint — it returns the
   defender's full snapshot **including the secret program**, at QUEUED,
   before any fight. Everywhere else the API exposes only `program_hash`. Any
   account can lift any rival's control program for the price of one stake.
   Low exposure today (only house robots + owen exist) but it is LIVE. Fix:
   the envelope feed must require the match to be past QUEUED, or strip the
   program from the defender envelope and have the client fetch the defender's
   program only when it is settling — or accept the leak only for genuinely
   mutual matches. Needs a design call.
5. **Exception mid-bout leaves `Career.Data` swapped to scratch forever** and
   LiveFight spinning — the restore is in a `finally`, but a throw inside the
   hold path can strand it. (Verify the finally covers the live-hold wait.)
6. **MatchRunner bypasses the counted `SuspendAutosave` hold** with a raw
   autosave save/restore — reintroduces the exact clobber `SuspendAutosave`
   was built to prevent if anything else holds it concurrently.
7. **Confirming a challenge while a replay plays** starts the live fight in
   the replay's arena (two owners of BuildArena; BackToBuild lands mid-fight).
8. **Envelope-fetch-failure message is invisible** (written to SC_INBOX while
   the player is on the board; showInbox never set).
9. **AIController / referee phase off absolute session time and render
   frames** — `Sin(Time.time*6)`, decision ticks, opening-caution clock, and
   every end condition quantized to render frames (which differ ~10–20× in
   sim granularity between device and worker). Feeds finding 3.
10. **LiveFight verdict message keys on win COUNTS, not `res.verdict`** —
    damage-band judges' decisions get reported as draws.

## Recommendation

Hold the launch. The live-fight feature as shipped shows players outcomes the
server contradicts — the one thing the dual-referee design promised it would
never do. Two coherent paths:

- **A — keep local simulation** as a preview only: fix the wall cap (1),
  isolate Progression (2), and change the copy so the client NEVER asserts a
  verdict the referee owns (3/10). The on-device fight becomes "watch a fight
  while the real one settles," and MY FIGHTS delivers the official result.
  Cheapest; keeps the instant spectacle.
- **B — client replays the worker's recording** instead of simulating: no
  determinism dependency at all, but the fight isn't instant (wait for the
  worker) and it's more work.

Either way the program-leak (4) is an independent server fix worth doing
now.
