# The first real match, end to end — 2026-08-09

**A robot uploaded through the API, validated by the real worker, challenged
through `/v1/challenges`, fought by the real fight worker, settled into the
ledger, with a replay on disk.** No step was faked by a bench.

`matchId 6d1427ee` · **verdict CHALLENGER** · challenger wallet 500 → 450 →
**600** · every job `DONE` · server log **0 errors** · career save
byte-identical with mtime unchanged.

Evidence: `server/qa_fight_e2e.txt`.

## The prediction

Written before the run, per hard rule 6:

> The armed challenger beats the unarmed defender → verdict `CHALLENGER`, and
> its wallet ends at 500 − 50 stake + 50 refund + 100 purse = **600**.

**Both halves exact.** Verdict `CHALLENGER`, wallet `600`. This is the first
prediction this session that landed on the number as well as the direction.

## The fixture problem, and the failure that fixed it

`FightWorkerBench`'s real fight ended in a **DRAW**, because both sides were
identical wheel-less cores. That proved the loop completes a match, but left
the `A→CHALLENGER` / `B→DEFENDER` mapping unexercised — and a silent
inversion there would report every ladder result backwards. So this run
needed two robots where one could actually win.

**The first attempt failed, and the failure is the useful part.** Taking
owen's `spinner1` and deleting its `spindle` and `spike` to make an "unarmed"
opponent produced:

```
REJECTED — Structure has floating parts — Beam at (0.00, 1.25, 0.00)
           isn't flush with anything. Faces must touch; sockets must mate.
```

**The spindle is structural, not just a weapon.** It carries the beam above
it. Deleting parts to weaken a robot breaks the build; the game's own
validator caught it, which is the validator working.

The fix keeps every part and its geometry and softens the weapon alone —
`spike|…|Steel` → `spike|…|ABS`, a one-token diff. Same 16 parts, same
category, mass 799 vs 750, gap 0.

**Rule for the next fixture: weaken by MATERIAL, not by deletion.** Geometry
is load-bearing in this builder.

## What the run proves that nothing before it did

| step | who did it |
|---|---|
| upload | real `/v1/snapshots` |
| validate | **real `ValidateWorkerLoop`** in play mode |
| challenge | real `/v1/challenges`, real escrow debit |
| fight | **real `FightWorkerLoop` + `MatchRunner`** |
| settle | real `/v1/worker/jobs/{id}/fight-result` |

Previously the server contract was proven by `api_smoke` posting a verdict it
invented, and the client was proven against a stub. This is the join, and it
is the first time either half met the other.

The replay is real, not just a URL — 551 bytes at the `file://` location the
API returned:

```
replayVersion 1, decidedBy "bouts"
bout 0  seed 976552167   winner A  PlayerWin  49.0s
bout 1  seed 1684946207  winner A  PlayerWin  32.7s
```

**2–0, so the third bout never ran** — `stopWhenDecided` doing its job. The
runner says `A`; the wire says `CHALLENGER`. That is the mapping finally
exercised by a robot winning rather than by a unit test.

## Owner state

`ExportRaw` again, so the builder was never driven and owen's bay was never
touched. Career save byte-identical **and mtime unchanged** — never written,
across a run containing two real fights. Editor left not playing, scene
clean, no stray objects.

## What is still not done

- **This is a hand-run, not a bench.** It catches no regression tomorrow.
  Automating it needs a bench that can boot the API, which none currently
  assume.
- **Glicko-2 is still absent.** The match settled *money* and moved nobody on
  the ladder — `matches.rating_deltas` is NULL and `ratings` is untouched.
  Two robots fought and the standings do not know.
- **Nothing was killed mid-fight.** M1's *"kill a worker mid-fight; job
  retries and completes"* clause remains untested against a real worker.
- **Localhost only.** No Docker, no GCP, no signed URLs. The `file://` blob
  URL is the local stand-in for a cloud one.
- **No client UI.** M1's accept says *"watch the replay in-client from the
  cloud URL"*. The replay exists and is fetchable; nothing in the game
  displays it, and there is still no login or ENLIST flow.

So M1's *machinery* is proven locally end to end. M1's *acceptance* is not,
and the gap is now entirely deployment and client UI rather than contract.
