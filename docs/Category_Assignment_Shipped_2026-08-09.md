# M1 gap 2 closed — category assignment, mass only — 2026-08-09

Took the evening handover's next-direction item 1 ("category assignment +
bench, M1, unblocked, Unity only"). It is **shipped and proven**: 44/44 on
a new `CategoryBench`, and `ReplayBench` 57 → **60/60** with the three
checks that cover the seam. Career save came back byte-identical.

**But the piece as specified could not be built.** §1.2's rule depends on a
size box that has not existed in this game since 2026-08-03. That is the
substance of this session; the code is the small part.

## ⚠ THE DECISION — §1.2's size box is stale, and the ladder does not use it

§1.2 defines a robot's natural category as *"the smallest category whose
weight cap **AND** size box it satisfies"*, and justifies reusing the
career table on the grounds that the caps and boxes are *"already tuned,
already enforced by `Validate()`"*.

**That sentence is false, and has been since before the doc's M1 work
started.** Commit `baabe3a` (2026-08-03) removed the size box from the
game on owen's explicit call — *"we already have the weight limit. why do
we also need size limit?"* — and removed it properly, not merely
unenforced:

- `League.sizeBox` gone from the struct, the constructor and all five
  league definitions
- `BuilderManager.OverSizeBox` and `SizeBoxLine` deleted
- the BUILD-bar readout deleted (it lived exactly one commit)
- `CareerValidate` has checked **mass alone** ever since

Verified in the live source this session, not read off the doc:
`Career.cs:51-62` has `weightCap` and no `sizeBox`; the only surviving
`sizeBox` references are in `.bundle*/`, `.push_tmp/` and `.nobox.bak`
copies, which `CLAUDE.md` names as stale duplicates.

**OWEN DECISION 2026-08-09: mass only.** Presented with the three
options — mass only, reinstate the box per §1.2, or ship mass-only with a
seam for a box later — he chose mass only. Recorded here because §1.2
still reads the other way and the next session will hit the same
contradiction.

### Why mass-only is also the better build, independent of the call

- **Two definitions of legal is the exact drift §5.2 exists to prevent.**
  §5.2's argument for a Unity worker is *one implementation, zero drift*.
  A ladder that refuses a robot the builder accepts breaks that promise
  from the inside.
- **The box could not discriminate anyway.** Feather and Light share
  `2.0 × 1.5 × 2.0`; Middle and Heavy share `2.5 × 1.8 × 2.5`. Within
  those pairs the box never changes the answer — it can only push a robot
  **up** a class or out of the sport.
- **It would manufacture a state the schema cannot hold.** Anything over
  2.0 m tall fits *no* box, so it would be legal with no category, and
  `ratings.category` is `NOT NULL`. There is no ladder row to put it on.

### ⚠ The open cost, so it is not rediscovered as a surprise

**Weight bounds density, not size.** Under a 1,500 kg cap one Beam is
463 kg in Tungsten and 25 kg in ABS, so a legal **Featherweight** can be a
59-beam ABS tower ~**35 m tall in a 14 m arena**
(`Size_Box_On_Build_Bar_2026-08-03`). In career the only opponent is the
AI and nobody builds that. **On the ladder the opponent is a human looking
for exactly this.**

**Nothing has measured whether a tower actually wins.** That is the open
question, and it is a bench, not an argument. If it does win, the lever is
**density or an explicit reach cap applied to the whole game** — not a
size box quietly added back into `RobotCategory`, and not a ladder-only
special case career does not share. The same warning already sits on
`BuilderManager.CareerValidate`'s docstring, at the other place someone
would be tempted; it is now on `RobotCategory`'s header too.

## What shipped

Backups: `_claude_backups/category__{RobotSnapshot.cs,ReplayBench.cs}`.

**`Assets/Phase1/Scripts/RobotCategory.cs`** — new, the authoritative
table and the rule.

```
FEATHER 1500 · LIGHT 2000 · MIDDLE 2800 · HEAVY 4000 · SUPER 5500   (kg, inclusive)
```

- `Assign(int massKg)` — smallest class whose cap the robot meets;
  **null** when none can hold it.
- `Reason(int massKg)` — why `Assign` returned null, phrased like
  `CareerValidate`'s over-cap message so the ladder and the builder refuse
  a robot in the same words.
- `CapKg` / `Label` / `IsKnown`, and `MayChallengeInto(natural, target)` —
  §1.2's *punch up, never down*, written once so "heavier" means one thing
  in the whole game.

Four deliberate choices:

- **The boundary is `CareerValidate`'s boundary.** `mass > cap` is over,
  so a robot exactly ON a cap is legal in that class. Not a style
  preference: §1.2 **resets rating to placement** whenever a re-upload
  changes natural category, so a cap one kilogram out raises no error
  anywhere — it moves a robot to another ladder and wipes what it earned,
  on an upload the player thinks is a tweak.
- **The ids are a database contract.** `001_init.sql` constrains *both*
  `snapshots.category` and `ratings.category` to
  `('FEATHER','LIGHT','MIDDLE','HEAVY','SUPER')`. A sixth name here is an
  INSERT that fails at the far end of a job, after the work is done. The
  bench asserts the exact set, in order.
- **The caps are pinned to `CareerDB.Leagues`, not retyped.** The bench
  compares all five against the shipped league caps. Retune a league and
  the bench fails, instead of the ladder silently drifting away from
  career — which is the whole point of not having two definitions.
- **The table's order is the rule.** "Smallest satisfying" is a
  first-match scan, so the bench asserts the caps are *strictly*
  ascending rather than trusting that they look sorted.

**`RobotSnapshot.cs`** — `SnapshotMeta` gains `category`; `Describe()`
computes it from the **validated** mass read off the real builder, before
legality is decided:

```csharp
m.category = RobotCategory.Assign(m.massKg) ?? "";
if (m.category.Length == 0) m.failReasons.Add(RobotCategory.Reason(m.massKg) ?? …);
```

**No category is a rejection, not an "unrated" state.** The alternative —
`legal` with `category` NULL — passes the `snapshots` CHECK, is accepted
by `validate-result`, and then has nowhere to be rated. The invariant is
**`legal` ⇒ non-empty category**, and both benches assert it. The stale
docstring saying *"category assignment lives server-side in M1"* is
replaced: §5.2's worker runs this method, so "server-side" and "this file"
are the same place.

## Verification, prediction first

Predicted before running: build clean · `CategoryBench` **44/44** ·
`ReplayBench` **57 → 60**, because the three new checks are the only
additions and the fixture rig is far under any cap.

**Measured: exactly that.**

- **`CategoryBench` 44/44, 0 fail** (`Assets/Phase1/qa_category_bench.txt`).
  Pure data — no scene, no play mode, no career state, no fight.
- **`ReplayBench` 60/60, 0 fail.** Fixture reads **924 kg → FEATHER**;
  all three seam checks pass.
- **Career save `18614d0e…`, mtime 08-05 21:57 — byte-identical** after
  the play-mode run. Editor left out of play mode.

### The bench is a sweep, not a list of examples

Section E walks **every integer mass from -10 to 7,000 kg** — 7,011 of
them — and asserts the three properties that *define* "smallest
satisfying": the class returned holds the robot, **no lighter class would
have**, and every refusal carries a reason. 5,500 assigned, 1,511 refused.
House rule 1: measure over everything rather than over a named list. The
named boundary cases (section B, both sides of all five caps) are kept
anyway — **a sweep tells you something broke, a named case tells you
where.**

Two guards against the OpeningBench failure mode (*a swept variable with
no variance has usually not been swept*): the sweep must see **both**
outcomes, and the assigned count must be exactly 5,500 — the number of
placeable masses. A sweep that silently assigned everything, or nothing,
fails.

### Why the seam is checked in ReplayBench and not a second fixture

`CategoryBench` proves the **table** with no scene. The three checks in
`ReplayBench` prove the **seam** — that `Describe` actually calls it,
feeds it the validated mass off the real builder, and never reports a
legal robot the ladder cannot place. `ReplayBench` is the only place a
real build is already loaded, so a second fixture would have been a second
thing to keep true. It was also stale-green since before 08-09; it has now
run.

## M1 after this

| M1 deliverable | state |
|---|---|
| API v0, queue, worker claim/heartbeat/validate-result | done, local only |
| Claim response resolves to a payload | fixed 08-09 |
| **Category assignment** | **done and proven, mass only** |
| FIGHT endpoints — match create, match result, replay upload | do not exist |
| Worker image / Docker / GCP deploy | not started |
| Client login + ENLIST UI | not started |

**Next is the VALIDATE worker loop** — claim → read storage →
`Open()` (hash verified) → `Describe()` → category → POST validate-result
→ heartbeat. Every piece it needs now exists. In-editor against the local
API; no Docker, no GCP.

One mapping note for whoever writes it: `SnapshotMeta.category` is `""`
for "no category", and `ValidateResult.Category` is `string?` stored as
SQL NULL. **The worker must send null, not `""`** — the `snapshots` CHECK
allows NULL but not the empty string.

## Method notes

**A doc that describes code is evidence about the day it was written.**
§1.2 did not merely omit that the size box was gone — it asserted the
boxes were "already enforced by `Validate()`" as the *reason* to reuse the
career table. Reading it as current would have shipped a ladder enforcing
a rule the game deleted, and nothing downstream would have complained: the
schema accepts any of the five names, and a robot pushed to the wrong
ladder looks like a robot on a ladder.

**Two consecutive sessions found the same class of fault in the same
milestone.** 08-09 morning: the claim SQL returned a UUID nothing could
resolve. 08-09 evening: §1.2 depends on a rule that no longer exists. Both
were found by reading the *other* side of a seam before building against
it, and both were invisible to a green suite. **The M1 pattern is that
each piece is individually right and the joins are where the work is.**

**Ask when the disagreement is about what the product should be.** The
size box is not a technical detail with a correct answer — it is a rule
owen deliberately removed, and re-adding it for multiplayer is a design
change wearing an implementation's clothes. The three options each had a
real cost; guessing would have buried the choice in a file header.

**Pin a derived table to its source instead of copying it.** The five caps
could have been retyped from the doc and would have been correct today.
Comparing them to `CareerDB.Leagues` in the bench means the day someone
retunes a league is the day the bench says so — the difference between a
constant that is right and a constant that stays right.

**Name what a green bench proves.** `CategoryBench` proves the table over
its whole domain and proves nothing about whether `Describe` calls it.
That is why there are three checks in `ReplayBench`, and why they are
described as covering the seam rather than as more category tests.
