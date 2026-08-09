# SESSION HANDOVER — 2026-08-09 late → next session

**Read this first.** Robot Brawl: Bolt & Blade — owen's physics robot
construction/combat game, built live in his Unity editor via the Unity MCP
bridge. CURRENT handover.

**This is a SHORT session on one thread.** It supersedes only **Part 2
(Multiplayer / M1)** of `SESSION_HANDOVER_2026-08-09_evening.md`.
**Everything in that handover's Part 1 (single player) still stands
unchanged and is not repeated here — go read it.** In particular its
⚠ THE RULE, its three 🚪 CLOSED DOORS, and the `OpeningBench` gotchas are
still the live guidance, and the ladder's 43% mutual disarm is still the
biggest open single-player problem.

Its *environment and protocol* sections (inherited from the 08-09 morning
handover) are also still accurate.

---

## ✅ What shipped: M1 gap 2 is closed — category assignment

Next-direction item 1 from the evening handover, done and proven.
Full record: **`Category_Assignment_Shipped_2026-08-09.md`** — read it
before touching the ladder.

**`Assets/Phase1/Scripts/RobotCategory.cs`** (new) — the authoritative
table and rule: `FEATHER 1500 · LIGHT 2000 · MIDDLE 2800 · HEAVY 4000 ·
SUPER 5500` kg, caps inclusive. `Assign(massKg)` returns the smallest
class that holds the robot, or **null** when none can. Plus `Reason`,
`CapKg`, `Label`, `IsKnown`, and `MayChallengeInto` (§1.2's *punch up,
never down*, written once).

**`RobotSnapshot.cs`** — `SnapshotMeta` gains `category`; `Describe()`
computes it from the **validated** mass off the real builder, before
legality is decided. **No category is a rejection, not an "unrated"
state** — the invariant is `legal` ⇒ non-empty category, because
`ratings.category` is `NOT NULL` and a categoryless snapshot has no ladder
row to live on.

Backups: `_claude_backups/category__{RobotSnapshot.cs,ReplayBench.cs}`.

### Verified, prediction first

Predicted: build clean · `CategoryBench` 44/44 · `ReplayBench` 57 → 60.
**Measured: exactly that.**

- **`CategoryBench` 44/44, 0 fail** — `Assets/Phase1/qa_category_bench.txt`.
- **`ReplayBench` 60/60, 0 fail** — fixture reads **924 kg → FEATHER**.
- **Career save `18614d0e…`, mtime 08-05 21:57 — byte-identical** after the
  play-mode run. Editor left out of play mode.

---

## ⚠ THE DECISION THE NEXT SESSION MUST NOT RE-LITIGATE

**§1.2's size box is gone, and the ladder is MASS ONLY (owen, 2026-08-09).**

§1.2 defines natural category as the smallest class satisfying **both** a
weight cap and a size box, and justifies reusing the career table because
the boxes are *"already tuned, already enforced by `Validate()`"*. **That
justification has been false since 2026-08-03**, when `baabe3a` removed
the size box from the game on owen's explicit call. `League.sizeBox`,
`OverSizeBox` and `SizeBoxLine` are all gone; `CareerValidate` has checked
mass alone ever since. Verified in live `Career.cs` this session.

Shown the three options, owen chose **mass only**.

**The design doc has been amended** — §1.2 now carries an ⚠ AS-BUILT
banner, and §1.1 and §5.2 were corrected to match. The drafted table and
paragraph are kept below the banner for the record.

### 🚪 The size box is a closed door — the open question is different

Do **not** add a box back to `RobotCategory`. The known cost of dropping
it is written down instead: **weight bounds density, not size.** Under a
1,500 kg cap one Beam is 463 kg in Tungsten and 25 kg in ABS, so a legal
**Featherweight** can be a 59-beam ABS tower ~**35 m tall in a 14 m
arena**. In career the only opponent is the AI; on the ladder the opponent
is a human looking for exactly this.

**Nothing has measured whether a tower actually wins.** That is a bench,
not an argument, and it is now the honest open item. If a tower does win,
the lever is **density or an explicit reach cap applied to the whole
game** — not a ladder-only special case career does not share. The same
warning sits on `RobotCategory`'s header and on
`BuilderManager.CareerValidate`'s docstring.

---

## M1 state after this session

| M1 deliverable | state |
|---|---|
| API v0, queue, worker claim/heartbeat/validate-result | done, local only |
| Claim response resolves to a payload | fixed 08-09 |
| **Category assignment** | **done and proven, mass only** |
| FIGHT endpoints — match create, match result, replay upload | **do not exist** |
| Worker image / Docker (owen has none) / GCP deploy | not started |
| Client login + ENLIST UI | not started — zero `UnityWebRequest` in the client |

---

## Next direction

1. **The VALIDATE worker loop** — claim → read storage → `Open()` (hash
   verified) → `Describe()` → category → POST validate-result → heartbeat.
   **Every piece it needs now exists.** In-editor against the local API;
   no Docker, no GCP. This is the unblocked next piece.
   ⚠ **Mapping trap:** `SnapshotMeta.category` is `""` for "no category";
   `ValidateResult.Category` is `string?` stored as SQL NULL. **The worker
   must send null, not `""`** — the `snapshots` CHECK allows NULL but not
   the empty string.
2. **The ladder's 43% mutual disarm** — still the biggest single-player
   problem, three cheap explanations already ruled out (evening handover,
   Part 1). It needs its own bench; `OpeningBench` is the template.
3. ✅ **The git index is FIXED** — owen committed it at 11:10 on 08-09
   (`651d5670`, *"Untrack .NET build output; track the hand-written server
   csproj"*). Verified by decoding `.git` directly: the csproj is in HEAD's
   tree and no `bin/`/`obj/` entries remain. Every handover before this one
   lists it as outstanding — they are stale on this point.
   **What was never done is the acceptance test:** nobody has confirmed a
   fresh clone actually builds the API, because no session had both `git`
   and `dotnet` until a Claude Code CLI session. See
   `CLI_SESSION_BRIEF_2026-08-09.md` §3. Still open alongside it:
   `server/qa_api_server.log` is tracked despite now being in `.gitignore`
   (ignore rules do not untrack), and `server/qa_sql_bench.txt` is
   untracked while its two siblings are. `RobotCategory.cs` and
   `CategoryBench.cs` are untracked — new, proven, and existing in exactly
   one copy.
4. FIGHT endpoints + replay upload · Docker · GCP deploy · client ENLIST
   UI · `/v1/auth/register` transaction · the ABS-tower bench · re-run the
   remaining stale-green benches (CareerSmoke first) · everything else in
   the evening handover's item 5.
5. **Roblox port scoping** — wants a fresh session.

---

## Bench inventory

**Run this session, green:** `CategoryBench` **44/44** (new) ·
`ReplayBench` **60/60** (was 57, three seam checks added; was stale-green
since before 08-09).

**Still green from earlier on 08-09:** MatrixBench 10/10 FLOOR + 3/3 SWEEP
· ProgramBench 40/40 · `sql_bench.sh` 34/34 · `api_smoke.sh` 37/37 ·
OpeningBench ×5 and LadderSweepBench (measurement only, no pass/fail).

**Still stale-green, not run since before 08-09:** VerbBench (32) ·
AutonomyBench (24) · CareerSmoke (128, run FIRST or in its own play
session) · CanvasDragBench (31) · TestDebugBench (30) · TouchSmoke ·
HazardBench · SensorProbe · CareerBench.

Artifacts added: `Assets/Phase1/qa_category_bench.txt`; `qa_replay_bench.txt`
refreshed.

`CategoryBench` is **pure data** — no scene, no play mode, no career state,
no fight. Run it from an editor RunCommand with
`RobotBrawl.Phase0.CategoryBench.RunPure()`. It is the cheapest green in
the project; there is no excuse for not running it after any edit near the
ladder.

## Career save

md5 **`18614d0e6603869f28fb65992b7d1484`**, mtime 08-05 21:57 — verified at
session start and again after the play-mode bench. Backed up to
`career_backups/career_2026-08-09_1328_sessionstart.json` + profile.

## First moves

1. Health RunCommand (`isPlaying`, `isCompiling`) → probe owen before
   taking the editor. **Owner state is sacred.**
2. Timestamped career backup; verify md5 vs **`18614d0e`**.
3. Read `Category_Assignment_Shipped_2026-08-09.md`, then the evening
   handover's Part 1 (it is still current for single player).
4. Then Next direction item 1.

## Method notes worth keeping

**A doc that describes code is evidence about the day it was written.**
§1.2 did not merely omit that the size box was gone — it asserted the
boxes were enforced as the *reason* to reuse the career table. Building to
it would have shipped a ladder enforcing a rule the game deleted, and
nothing downstream would have complained.

**Two consecutive sessions found the same class of fault in M1.** The
claim SQL returned a UUID nothing could resolve; §1.2 depends on a rule
that no longer exists. Both were found by reading the *other* side of a
seam before building against it, and both were invisible to a green suite.
**Each M1 piece is individually right; the joins are where the work is.**

**Ask when the disagreement is about what the product should be.** The
size box is not a technical detail with a correct answer — it is a rule
owen deliberately removed, and re-adding it for multiplayer is a design
change wearing an implementation's clothes.

**Pin a derived table to its source instead of copying it.** The five caps
would have been correct if retyped. The bench compares them to
`CareerDB.Leagues`, so the day someone retunes a league is the day the
bench says so — the difference between a constant that is right and a
constant that stays right.

**Name what a green bench proves.** `CategoryBench` proves the table over
its whole domain and proves nothing about whether `Describe` calls it.
Hence three checks in `ReplayBench`, described as covering the *seam*
rather than as more category tests.

**A sweep tells you something broke; a named case tells you where.** Keep
both. `CategoryBench` walks all 7,011 masses from -10 to 7,000 kg *and*
names both sides of all five caps.

---

## Session addendum — the CLI split

owen asked whether to move to Claude Code on the Mac. The honest shape of
it, and what was done about it:

A Cowork session's `bash` is a **separate Linux VM** — no network, no
`dotnet`, no `psql`, no route to the Mac's `localhost:5000` — but it has
the **Unity MCP bridge**, which is how every bench in this project gets
run. A CLI session on the Mac is the exact inverse: real shell, real
`git`, real `dotnet`, and **probably no way to run a bench**. Neither is
strictly better; the split is by *thread*, not by tool.

Written this session, on disk (not in the Project):

- **`docs/CLI_SESSION_BRIEF_2026-08-09.md`** — a self-contained brief for
  a fresh CLI session. Scoped to what a shell does better: prove a clone
  builds, then the FIGHT half of the server contract, which is entirely
  C# and SQL and fully benchable without Unity. It opens with **option
  zero**: register `unity-mcp` with Claude Code, and the split disappears
  because one session then has shell *and* git *and* the editor.
- **`CLAUDE.md`** — refreshed; it pointed at the 08-08 handover and knew
  nothing of `RobotCategory`, the mass-only decision, or the 08-09 fixes.
- **`docs/`** — eleven Project docs mirrored verbatim, so a CLI session
  with no Project access reads current material.

**Ownership if both run at once: CLI owns `server/**`, `.gitignore` and
git; Cowork owns `Assets/**` and the editor.**

⚠ **A method note earned the hard way this session.** The brief's first
draft told the CLI to fix the git index, quoting the four commands the
handover chain has carried since 08-08. A verification pass — decoding
`.git/index` and HEAD's tree rather than trusting the doc — found owen had
already run them that morning. **The handover chain had been repeating a
stale to-do for a day, and it would have shipped into the brief as the
first instruction a new session followed.** Same failure as §1.2's size
box, one day later and in our own records rather than the design doc:
**a to-do list is evidence about when it was written.** Check the artifact,
not the note about the artifact.
