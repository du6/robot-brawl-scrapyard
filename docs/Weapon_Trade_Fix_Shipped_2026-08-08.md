<!-- Mirrored from the claude.ai Project (path: claude/Weapon_Trade_Fix_Shipped_2026-08-08.md) on 2026-08-08.
     The Project copy is the source of truth; edit there and re-mirror. -->

# Weapon trade fix — options 1, 2, 3 SHIPPED (2026-08-08)

owen: *"execute option 1, 2, 3"* from
`Ladder_Sweep_Weapon_Trade_2026-08-08.md`. All three are in, measured
over the same 30-bout sweep before and after, and the single-player
balance guard was re-run because two of them change combat for the
career too.

**Green after:** MatrixBench **9/9** · ReplayBench **57/57** ·
AutonomyBench **24/24** · CareerSmoke **128/128** · LadderSweepBench
31/31. Career save byte-identical (md5 **18614d0e**).

## What shipped

**Option 1 — a damage margin only decides a match if it is a margin.**
`MatchRunner.Decide()` used to split on any difference at all, which on
the first real match handed the tie to B off **0.13 total damage**
across three draws. It now uses **`FightManager.DrawBand`** — made
public rather than reimplemented, so "ahead" means the same thing to
the ladder as it does to the referee and the scorecard. Inside the
band it is a Draw, which §2.1's Glicko-2 takes as 0.5.

**Option 2 — `DamageResolver.WEAPON_VS_WEAPON = 0.25f`.** HP damage is
quartered when a weapon part is struck by a weapon part. Both sides of
the test use `IsEdge(edgeHardness)`, which is **already this codebase's
definition of weapon-ness** (`AIController`'s threat model and
`STRUCT_RAM_DMG` both key off it), so there is no second notion to
drift. Deliberately narrow: weapon-vs-BODY damage is untouched, because
losing a weapon to a determined opponent is a legitimate outcome and
armour-vs-weapon is the sport.

**Option 3 — `FightManager.TickStalemate()`.** Ends a bout that is over
through the **existing judges**, so nothing new scores a fight.
Two rules: `STALL_QUIET = 12 s` of no damage while **both** sides have
zero live weapon parts, and `STALL_NO_CONTACT = 30 s` with no damage at
all. The first is stricter than "it looks boring", and that is what
makes it safe: `STRUCT_RAM_DMG` is 0, so a part with no weapon edge
**cannot deal HP damage at all**. Once both machines have lost every
edge, no further damage is *physically possible* — the remaining clock
can only pad the scorecard. The quiet window keeps it honest in a
hazard arena: it watches damage **taken**, not dealt, so environmental
damage with no attacker still counts as the fight being alive. The
verdict line names it: *"Called early — both machines disarmed, no
damage possible for 12 s · Judges' decision — …"*

## Measured, over the same 30 bouts

| | before | **0.25 (shipped)** | 0.10 (tested, rejected) |
|---|---|---|---|
| mean bout length | 66.8 s | **25.0 s** | 31.1 s |
| mean dead air | 53.0 s (**79.3%**) | **10.4 s (41.5%)** | 11.0 s (35.3%) |
| mean hits per bout | 13.4 | **14.9** | 17.1 |
| mean last hit at | 11.1 s | **13.9 s** | 20.1 s |
| both sides disarmed | 15/30 (50%) | **13/30 (43%)** | 13/30 (43%) |
| either side disarmed | 26/30 | 26/30 | 26/30 |

The matchups that changed character:

- **Matador v Matador** went from a mutual-disarm Draw on both seeds to
  **decisive on both** — 32 and 24 hits, one machine dismantled, and on
  seed 101 a fight still landing damage at **65 s**.
- **Brawler v Rusher and WallShy v Rusher** were Rusher wins on the
  judges' aggression card *after both weapons were gone*. Three of
  those four are now Draws — **the ladder has stopped ranking robots on
  who shoves harder while disarmed.**
- **Statue v Statue** and **Rusher v Statue** (seed 101), which recorded
  zero contact in 91 s, now end at **31 s**.
- The end-to-end ReplayBench match: verdict went from *"B, by damage"*
  (0.13 margin) to *"Draw, inside the draw band"*, and its bouts from
  90.0 s with 86 s of dead air to **27.9 s with 12.05 s**.

## What did NOT get fixed, stated plainly

**Mirror lock is unsolved.** Brawler v Brawler, WallShy v WallShy and
Rusher v Rusher still meet nose-to-nose, mutually disarm, and Draw. The
weapon multiplier softened it — 4 hits over 5 s became 10 hits over
15 s — but did not change the *kind* of outcome.

**And 0.10 proved the multiplier is the wrong lever for it.** At 0.10
the mirror grind stretched to **24 hits with the last at 41.6 s**, and
the result was still both-disarmed and a Draw. Lowering the number only
buys a longer grind. So the cause is the **nose-to-nose lock** — two
programs that both charge and hold — which is a behaviour and geometry
problem, and the fix belongs in the presets' approach/steering, not in
a damage constant. That is the next thing to look at, and it is now a
measurable one.

**One bout still runs the full clock**: Rusher v Statue seed 202, 85.8 s
of dead air, because **both sides kept their weapons**. The stalemate
rule deliberately does not fire while more damage is physically
possible. Correct by the rule as written, and still a gap — a
"neither machine can reach the other" case that neither rule covers.

**MatrixBench's FLOOR check passed at exactly its threshold** (Brawler
clears the L1 sample 7/10, needs ≥7). It is green, but it is green with
no margin, and these changes are the kind that move it. Worth a second
run before trusting it.

## A protocol finding worth more than it looks

CareerSmoke came back **115/128 with 13 failures** — and every failure
was inventory or scrap accounting, nothing to do with damage or match
end. Run **alone in a fresh play session it is 128/128.**

So the failures were **cross-bench state pollution**, not a regression:
one of MatrixBench / AutonomyBench / ReplayBench does not fully restore
what it mutates (all three swap `Career.Data` and grant scrap and
parts). The handover rule "a bench that mutates shared fixture state
must restore it" has a hole in it that only shows when CareerSmoke runs
last.

**Run CareerSmoke first, or in its own play session.** And the general
lesson, which is the house rule anyway: when a bench fails, run the
control leg before believing the failure.
