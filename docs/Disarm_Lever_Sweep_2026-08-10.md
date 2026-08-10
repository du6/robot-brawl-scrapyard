# Which lever moves the 43% mutual disarm — swept, 2026-08-10

`Mutual_Disarm_Root_Cause_2026-08-09` found the mechanism and named three
candidate levers, then stopped, correctly: *"It does not license a balance
change. Balance is owen's call and gets made from the table, not from a
threshold invented by whoever ran the bench."*

**This is the table.** `DisarmLeverBench`, 7 arms × 15 bouts = **105 real
fights**. It changes nothing: every constant is restored in a `finally`, and
`WEAPON_VS_STRUCT` ships at 1.0 (a no-op) so the third candidate could be
measured at all.

---

## The table

| arm | BOTH disarmed | shed | hpKill | struct | left w/ hp | mean hp |
|---|---|---|---|---|---|---|
| **baseline (shipped)** | **6/15 (40%)** | 12 | 6 | 52 | 12/18 (67%) | 76% |
| BREAK_K ×1.5 (seam) | **4/15 (27%)** | 9 | 8 | 59 | 9/17 (53%) | 83% |
| BREAK_K ×2.0 (seam) | **4/15 (27%)** | 9 | 6 | 65 | 9/15 (60%) | 75% |
| WEAPON_VS_STRUCT 0.50 | 6/15 (40%) | 11 | 8 | 57 | 11/19 (58%) | 79% |
| WEAPON_VS_STRUCT 0.25 | 7/15 (47%) | 11 | 8 | 46 | 11/19 (58%) | 76% |
| SHORT LIMB (chassis) | 8/15 (53%) | 8 | **13** | 49 | **8/21 (38%)** | 86% |
| **SHORT LIMB + BREAK_K ×1.5** | **3/15 (20%)** | 7 | 9 | 80 | 7/16 (44%) | 83% |

`shed` = weapons that left via a seam · `hpKill` = weapons actually beaten on
hit points · `struct` = non-weapon parts destroyed · `left w/ hp` = the 69%
figure, weapons that left with hit points still in them.

**The instrument reproduces the known number.** Baseline is 40% against the
documented 43% (13/30) on a different sample size. That is the validity check;
without it none of the other rows mean anything.

---

## What it says

### 1. WEAPON_VS_STRUCT does nothing. Candidate 3 is dead.

40% → 40% → 47% across a 4× change in the constant, and `shed` barely moves
(12 → 11 → 11). Weapon-on-structure damage was the one lever no weapon rule
touched, and the obvious reading of "90 structural parts died to edges" was
that softening that would keep the arms alive. **It does not.** Structure is
not dying to weapon damage fast enough for the multiplier to matter — it is
dying to the seam.

### 2. BREAK_K works, and saturates at ×1.5.

40% → 27%, with `shed` 12 → 9 and `left w/ hp` 67% → 53%. **Both the outcome
and the mechanism move**, which is the criterion that separates a real lever
from a coincidence. ×2.0 buys nothing further (identical 4/15, identical shed)
— so if this is the lever, ×1.5 is the whole of it and ×2.0 is cost with no
return.

### 3. The chassis fixes the mechanism and makes the OUTCOME WORSE.

This is the finding worth the whole sweep.

Shortening the weapon arm does exactly what the root-cause doc predicted to
the mechanism: `left w/ hp` collapses 67% → **38%**, the best reduction in the
table, and `shed` drops 12 → 8. Weapons stop falling off.

**And mutual disarm goes UP, 40% → 53%**, because `hpKill` more than doubles
(6 → 13). The weapon stops being *dropped* and starts being *destroyed*. Held
close to the body it is in the fight instead of dangling out of it.

So "limb geometry matters more than weapon material" is **right about the
mechanism and wrong about the result**, and a session that had measured only
the disarm rate would have concluded the short limb was a bad idea. A session
that had measured only the shed rate would have called it a triumph.

### 4. The two real levers are complementary.

`SHORT LIMB + BREAK_K ×1.5` is the best row in the table at **20%**, half the
baseline — better than either alone (27% and 53%). Geometry converts sheds
into HP kills; the stronger seam then stops the remaining sheds. Neither one
gets there by itself.

⚠ Note its `struct` column: **80** destroyed structural parts against a
baseline 52. Fights under that combination are markedly more destructive
elsewhere. That may be desirable or may not; it is not a disarm question.

---

## Predictions, written before the run

Hard rule 6. Three of four were wrong, and the wrong ones are the useful ones.

1. **"BREAK_K reduces both mutual disarm and left-w/-hp."** — **HELD.**
2. **"WEAPON_VS_STRUCT also reduces them."** — **REFUTED.** No effect at any
   value tested. Had this been reasoned about rather than measured, it is the
   candidate that sounds most obviously right.
3. **"SHORT LIMB beats every single constant on left-w/-hp."** — **HELD on
   that metric (38%, the best) and REFUTED on the thing anyone actually
   cares about**: mutual disarm got worse. Being right about the mechanism
   and wrong about the outcome is the most instructive way to be wrong.
4. **"Baseline lands 30–60%."** — HELD, 40%.

---

## Scope, stated honestly

- **15 bouts per arm.** A 1–2 bout difference in the BOTH column is noise;
  6 vs 4 vs 3 is suggestive, not settled. **The mechanism columns are the
  trustworthy ones** — they count 15–21 weapon departures per arm, so
  `left w/ hp` 67% → 38% is a real signal in a way that 40% → 27% is not yet.
- **One chassis pair per arm**, both sides identical, so the only variable
  within an arm is the program. Inherited from `DisarmBench` and the same
  limitation.
- ⚠ **The first attempt used 3 programs and came back with a 100% baseline** —
  Brawler/Rusher/WallShy are all aggressive, so the subset was not merely
  noisier but *biased*, and saturated at a ceiling where no arm could be
  distinguished. The five-program set of `DisarmBench` is what pulls it off
  the ceiling. A subset chosen for speed is a subset chosen for something.

## What this does and does not license

It still does not license a balance change — that is owen's, and the shipped
values are untouched. What it narrows:

- **Candidate 3 (weapon-on-structure) can be crossed off.** Measured, no
  effect. `WEAPON_VS_STRUCT` stays at 1.0 and could be deleted again if the
  dial is unwanted.
- **Candidate 1 (the seam) is a real lever** worth ×1.5 and not more.
- **The chassis is a real lever too, and it does not act like a smaller
  version of the constants** — it changes *how* weapons are lost, not just how
  often. If disarm is meant to feel like a machine being beaten apart rather
  than shaking itself apart, that difference is the interesting one and the
  raw rate is the wrong thing to optimise.

The open question this raises, which nobody has asked yet: **is 43% actually
wrong?** Every measurement so far has assumed it should come down. A ladder
where two thirds of fights end with someone still armed may be the intent, and
the sweep cannot answer that.
