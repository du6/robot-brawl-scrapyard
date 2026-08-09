# Play-mode benches run from a CLI session — proven 2026-08-09

**Measured by:** Claude Code on owen's Mac, immediately after taking over
per `HANDOVER_TO_CLI.md`.
**Result: `WorkerBench` 39 pass, 0 fail. Career save untouched.**

This closes the last open capability question from the handover. A CLI
session has shell, `git`, `dotnet`, `psql`, **and the whole Unity bench
suite**. Nothing in this project now requires a second kind of session to
verify.

## The prediction, written before the measurement

Per hard rule 6, recorded first:

> Play mode will work over the bridge, but the domain reload on entering
> play mode will kill the `CommandScript` statics, so a single blocking
> call will not survive — it will need entry, then separate polling calls.
> WorkerBench will report 39/39 and the career save will be unchanged,
> because WorkerBench uses a stub transport and isolates `Career.Data`.

**Both halves held.** The bench returned exactly 39/39 and the career save
was byte-identical. The reload did require the split-call approach.

## What was actually done

Five separate `Unity_RunCommand` calls. The split is not stylistic — each
call is compiled fresh and cannot hold a reference across the domain
reload, so the bench must be found by `FindFirstObjectByType` each time
rather than kept in a variable.

| # | call | result |
|---|---|---|
| 1 | scene + career probe | `scene=Main dirty=False`, `isPlaying=False`, `BuilderManager=False`, `Career.autosave=True` |
| 2 | `EditorApplication.EnterPlaymode()` | requested; `isPlaying` still `False` on return |
| 3 | state check | `isPlaying=True`, `BuilderManager=True` — **the bridge survived the reload** |
| 4 | `WorkerBench.Run()` | coroutine started |
| 5 | poll | **`finished=True passed=39 failed=0`** |
| 6 | `EditorApplication.ExitPlaymode()` | guarded on `finished` |
| 7 | post-check | `isPlaying=False`, `scene=Main`, `dirty=False`, no stray bench object |

`Assets/Phase1/qa_worker_bench.txt` ends `RESULT: 39 pass, 0 fail - ALL
GREEN`, and is **byte-identical to the copy committed in `d9149c4`** —
`git status` reports it unmodified. The bench is deterministic and
reproduces its own committed evidence.

## Owner state

| | before | after |
|---|---|---|
| md5 | `18614d0e6603869f28fb65992b7d1484` | `18614d0e6603869f28fb65992b7d1484` |
| mtime | 2026-08-05 21:57 | 2026-08-05 21:57 |

**The mtime is the stronger check.** An unchanged md5 only says the
contents match; an unchanged mtime says the file was **never written at
all**. Entering play mode on `Main` did not cause a career save.

A backup was taken to `career_backups/` before entering play mode and
removed afterwards, once the mtime showed it had been unnecessary.

## Why it was safe, and what actually protects you

Two independent things, and only one of them is the bench's doing:

1. **`WorkerBench` isolates career state itself.** It captures
   `Career.Data`, forces `Career.autosave = false`, asserts *"the career
   object was never swapped"* as one of its 39 checks, and restores
   `autosave`/`active` at the end. That covers the bench.
2. **Entering play mode at all is the part the bench cannot protect.**
   `BuilderManager` calls `Career.Save()` on nine separate paths, gated
   only on `Career.autosave`, which is `True` in edit mode and stays `True`
   until the bench's own `Start()` sets it false. Nothing wrote the save
   this time — but that is a property of what `Main` does on load, not a
   guarantee the bench provides.

**So: fingerprint before and after every play-mode session, mtime included.
Do not infer safety from the bench having an isolation check in it.** The
window between play-mode entry and the bench's first line is not covered by
anything.

## Editor discipline this adds

- **Check `isPlaying`/`isCompiling` before entering, and re-check after
  requesting.** `EnterPlaymode()` returns before the reload completes;
  `isPlaying` is still `False` on the same call that requested it. Poll in
  a separate call.
- **Guard `ExitPlaymode()` on the bench's `finished` flag**, or you kill
  the run you are measuring.
- **Confirm the editor came back clean** — `isPlaying=False`, scene not
  dirty, no stray bench GameObject left in the scene.
- **`System.Reflection` is refused by the bridge sandbox** — "Script uses
  one or more unauthorized namespaces". It fails at *validation*, before
  compilation, so the error does not look like a C# error. Call types
  directly.
- The bridge did **not** drop across the domain reload in this run. The
  handover's 60–70 s reconnect advice was not needed, but one clean run is
  not evidence it never will.

## What is still unproven

`WorkerBench` is one play-mode bench and a well-behaved one: no fight, no
arena, a stub transport. Still unmeasured from a CLI session:

- **`MatrixBench`, `LadderSweepBench`, `OpeningBench`** — these run real
  fights. `FightManager.End()` calls `Progression.OnMatchEnd`
  unconditionally, so these are the ones where career isolation genuinely
  matters.
- **`CareerSmoke`** — documented as not isolated; must run first or in its
  own play session.
- **Long runs.** A 30-bout sweep is not a sub-second call, and nothing here
  tested how the bridge behaves across minutes.

Prove those the same way — prediction first, fingerprint both sides, mtime
included — before relying on them.
