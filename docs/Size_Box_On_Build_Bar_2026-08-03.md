# The size box: shown, then removed (2026-08-03)

Two commits, same day, opposite directions. `f17cb8d` put the size box on the BUILD
bar; `baabe3a` deleted the rule entirely. Both were right at the time.

## Part 1 — why it was invisible (`f17cb8d`)

> owen: "Why do I see the size limit in the league? When I build the robot I only
> see weight limit?"

Enrollment had **two** rules. `CareerValidate` checked both; the BUILD bar printed
only mass. owen's build read **799/1500 kg** — 700 kg of headroom, true — while
being **1.0 × 0.8 × 2.3 m against a 2.0 × 1.5 × 2.0 m box**, 0.3 m too tall. Nothing
mentioned the box until FIGHT refused, by which point the robot is built.

Same failure mode as the FIGHT silent refusal and SAVE's dead end: **the rule
existed, was enforced, and was invisible at the only moment you could act on it.**
Third instance this session — worth watching for the fourth.

## Part 2 — why it went away (`baabe3a`)

> owen: "we already have the weight limit. why do we also need size limit?"

The honest answer, with numbers, is that they constrain different things —
**weight limits density, not size**:

| material | density | one Beam (0.024 m³) | beams under a 1500 kg cap |
| --- | --- | --- | --- |
| Tungsten | 19.3 | 463 kg | **3** |
| Aluminum | 2.70 | 65 kg | 23 |
| ABS Plastic | 1.05 | 25 kg | **59** |

18× spread. Fifty-nine ABS beams stacked is a legal **35 m tower** in a **14 m**
arena. That gap is what the box was closing.

Shown that, owen chose to drop it anyway — simplicity over the constraint. That is
a legitimate call (BattleBots uses weight classes only; FRC uses a size box at
inspection), and it is his game.

## What the removal costs — read this before "fixing" anything

Without a size rule, ultralight structure may become **strictly** correct: ABS and
carbon fibre for everything load-bearing, dense materials only where inertia is
actively wanted. That would collapse the material trade-off Phase 6 exists to
create.

**If that happens, the lever is density or an explicit reach cap — NOT putting the
box back.** The docstring on `CareerValidate` says so at the point someone would be
tempted. Nothing has measured whether it actually happens yet; that is the open
question.

## How it was removed

Removed, not merely unenforced:

- `League.sizeBox` gone from the struct **and the constructor** (all 5 league defs
  and 2 test leagues updated)
- `OverSizeBox` / `SizeBoxLine` deleted
- the LEAGUE list no longer prints a box column
- the BUILD bar readout from `f17cb8d` deleted — it lived one commit

A field that no longer means anything is how a deleted rule gets half-resurrected
by someone who assumes it is still live. Same class as the shop harness that spent
weeks clicking buttons that did not exist.

## The assertion that replaced the size tests

After deleting a rule, the test that matters is that a build which **would** have
been refused is now accepted — otherwise the removal is merely uncalled, not
proven:

```csharp
var roomyCap = new CareerDB.League("LZ", "Roomy", "Z", "z", 99999f, new CareerDB.Contest[0]);
Check(bm.CareerValidate(roomyCap) == null,
      "with a huge cap nothing else refuses the build — weight is the only rule");
```

CareerSmoke: 58 pass, 0 fail.
