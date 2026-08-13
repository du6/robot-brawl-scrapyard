# The Gusset — the disarm lever, made local, priced, and handed to the player (2026-08-12)

Owen's ask: "a component that increases the joining strength between two
joints… to enforce weapon attachment… doesn't occupy real space but should
be visible." Built same day; the numbers are the recommended defaults he
approved.

⚠ **AMENDED SAME DAY — v3, owen's third spec, and it is the shipped one.**
The sections below describe v1 (whole part) and carry v2's amendment (one
joint per gusset, nearest-joint tap). Owen found the joint-tap confusing and
respecified: **the gusset welds a SURFACE, before anything is attached.**
As shipped:

1. **Tap a face to weld it** — the literal face under the tap
   (`hit.normal`, never a nearest-joint guess). The face turns gold (a thin
   plate, no collider). Welding a face with nothing on it is the NORMAL
   flow, so a bare face — the core's included — accepts a weld.
2. **A part bolted onto a welded face holds ×1.5**; parts on unwelded faces
   hold normal. The seam mult fires when EITHER side's mating face bit is
   set (`FaceBitFromDelta` of the centre delta), applied once.
3. **REMOVE peels before it deletes** — REMOVE on a welded face takes the
   gusset first and says so; the next REMOVE takes the part. No way to lose
   a part when you meant to lose a weld.
4. The intended order is **weld first, attach second** (the HOLDING banner
   says so); welding an already-mated face still works and strengthens that
   seam retroactively.

Format moved with it: `|G` (whole part) → **`|G:mask`** (6-bit world-axis
face mask; parts never rotate off-axis). The loader reads legacy `|G` as
mask 63. Mass and stock are now PER WELD (+10 kg each, `GussetCount()`
popcount), and stacking on one face is still refused by measurement.
Re-measured under v3: seam ×1.5000 exact and DIRECTIONAL (the wrong face
buys nothing), **TouchSmoke 47/47** (REMOVE-peel legs included),
**CareerSmoke 133/133**, career save byte-identical, mtime included.
⚠ v3 REOPENED the worker lockstep that v1 closed below —
`worker:20260812-182949` reads `|G` but not `|G:mask`. Redeploy owed
tonight; no production snapshot carries any gusset yet, so nothing live
misreads meanwhile.

⚠ **And one incident, so it is on the record:** the v3 probe session drove
REAL shop clicks in owen's career with no `SuspendAutosave` hold — the
counted hold protects BENCHES, not a hand-driven probe — and autosave wrote
six `buy gusset Steel` lines into the career save at 22:50. Caught by the
fingerprint discipline, restored byte-identical (mtime included) from
`career_backups/pre_stalegreen_20260810_010747.json`; the damaged copy is
preserved beside it. **A probe that clicks the real UI in career mode needs
the same isolation a bench does.**

## What it is (v1 text, superseded above)

**Gusset (weld kit)** — an APPLIQUE, the palette's first: never placed as
geometry. Pick up the tile, tap a placed part, and **every seam that part
makes breaks at ×1.5**. Costs **+10 kg** (counted in robot mass, so it
spends ladder weight-class headroom) and **200 scrap** flat in the shop
(derivation would have priced 10 kg of Steel at 8). One per part, **no
stacking** — the lever sweep already measured ×2 as a dead zone, so the
refusal cites a measurement, not a preference. Visible as a gold band around
the part's waist: no collider, no socket, no AABB. The core refuses (a frame
has no parent joint). Free build treats it as unlimited like every part;
career mode gates it on owned stock (`CareerUsed` counts reinforced
placements — the same declarative stock model parts use, so UNDO/remove/load
all reconcile for free).

## Why ×1.5 was never in question

`docs/Disarm_Lever_Sweep_2026-08-10.md`: global BREAK_K ×1.5 took mutual
disarm 40% → 27%; ×2.0 bought nothing more. The gusset is that lever as a
per-part flag feeding the conn builder's existing `mult` (beside the
actuator-seam ×3 precedent), applied ONCE even when both seam ends are
gusseted.

## Format: fmt4, and the lockstep rule

Snapshots carry a 6th field `|G` per reinforced part and an ADDITIONAL stamp
`#fmt4-gusset` (never replacing `#fmt3-disc`, whose absence triggers the
disc-repair migration). An OLD reader silently drops both — a gusseted robot
would fight 10 kg light with soft seams on an old worker — so **the cloud
worker must be redeployed before any gusset-capable client can enlist**.
Durable half: the loader now NAMES any unknown `#fmt` stamp and `Validate()`
refuses the whole build ("newer save format — update the game"), so the
NEXT format bump gets a rejection instead of a silent partial load.

## Measured

- **The mechanic, end to end** (prediction written first, confirmed exactly):
  MatrixBench's fixture with `|G` on the spinner — `spindle↔spinner`
  threshold **2700 → 4050 (×1.5000)**, neighbouring `beam↔spindle` unchanged
  at 2700, robot mass **+10.0 kg**, through text → loader → conn builder →
  spawned CompoundRobot edges.
- **TouchSmoke 44/44** (+13: applique selection shows no ghost, tap applies,
  +10 kg, band visible, no-stacking refusal, fmt4+|G in the save, UNDO
  removes, round-trip survives band included, unknown-stamp refusal and its
  clearing).
- **CareerSmoke 133/133** (+5: no-stock refusal in words, grant→apply,
  shelf reads zero, next application refused).
- **FuzzBench 25/25** (the loader I touched), CategoryBench 44/44,
  LadderClientBench 40/40. Career save byte-identical, mtime included.

## NOT done / owed

- ~~**Worker redeploy**~~ — **DONE the same evening**: the gusset-aware
  assembly was verified IN the built worker (`#fmt4-gusset` present in
  Assembly-CSharp.dll before packaging), deployed as
  `worker:20260812-182949`, schedule intact, and executed once in the cloud
  to a clean drain (`rb-worker-tdf29`, succeeded). The lockstep is closed:
  the ladder reads fmt4 before any client can write it.
  ⚠ **Reopened by v3 the same night** (`|G:mask`) — see the amendment at the
  top; a second redeploy is owed and in progress.
- **Arena visual** — the gold band renders in the BUILDER (and survives
  repaint and reload); the fight-arena spawn does not draw it yet. The
  PHYSICS is in the arena (measured above); only the band is builder-only.
- **A fight-level disarm measurement** — a DisarmLeverBench arm driving the
  gusset flag instead of global BREAK_K (prediction: the ×1.5 row's 27%).
  The constant-level identity is proven; the fight-level number is owed.
- Enemy roster never gussets (player-only edge, for now) — a balance choice,
  not an accident.
