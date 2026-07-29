# ROUND-2 IMPLEMENTATION — dev log

Stream. Every builder claim below comes through the pointer path.

## Phase 0 — read the code
- `BuilderManager.AddPart` (line ~394) calls `PartVisualFactory.BuildPart(def.id, ...)` — BARE id.
- `SetPartMaterial` (line ~701) likewise, and `EnsureGhostBuilt` (line ~1064) likewise.
- `SpawnBot` (line ~1777) is the only caller that appends `AxisCode(p.wheelAxis)`.
- `PartVisualFactory.BuildPart` resolves pivot/spindle/ram/blade/wedge/hook orientation
  through `ParseAxis(id, prefix, fallback)`, which needs that suffix. Critic C1 confirmed by reading.
- Extra, found while reading and NOT in the critic's list: `ParseAxis(id,"spinner",up)` is
  called for ids that start with "spinner" — which includes **spinnerSaw**. For
  "spinnerSawYP_3" the parser reads id[7]='S', fails, and returns the +Y fallback. So a
  side-mounted saw is drawn as a horizontal disc IN THE ARENA. Same family, one line. To be measured.
- C2 (plate R): read the socket rule. A yaw-180 plate is (0.50,0.50,0.06); mounted on a
  **±Z** face the two tangents are x and y = 0.50/0.50, both ≥ SOCKET_PITCH. So the rule
  predicts the stood-up plate IS placeable, on the two faces whose normal is its thin axis.
  The critic's "refused on every face of every part" may be a partial sweep. Measuring before touching.

## Phase 1 — BEFORE numbers (my own, pointer path, before touching any code)
| critic finding | reproduced? | my before-number |
|---|---|---|
| C1 builder ignores mount face | YES | pivot/spindle 0.396x0.318x0.318, ram 0.318x0.318x0.483, blade 0.495x0.045x0.122, wedge 0.500x0.149x0.413, hook 0.112x0.309x0.185 — each IDENTICAL on -Z, +Y and +X of a bare core. spike (own branch) correctly turns. 7 checks, 6 failures |
| C3 budget not enforced | YES, exactly | 12 Tungsten plates clicked in, 21,014 cr vs 4,000 = 5.25x, ghost green on every click, over from plate 3 |
| C6 ghostYaw leaks | YES | one R on a beam left yaw 90; beamlong AND engine both then started at 90 |
| C2 plate R unplaceable | **NO — refuted** | 24 hit-verified probes, all 6 faces x whole R cycle: every one of the plate's three distinct orientations is accepted somewhere (yaw 0 on ±Y, yaw 180 on ±Z, yaw 270 on ±X). 0 failures. The stood-up wall plate IS buildable |
| (new, found by reading) spinnerSaw axis | YES | spinnerSawXP and spinnerSawYP both draw 0.457x0.276x0.471 — identical. Plain spinner correctly differs |

## Phase 2 — the patch (one batched edit, one domain reload)
FIX A orientation · FIX A2 spinnerSaw prefix · FIX B ghostYaw reset · FIX C budget in ghost
· FIX C2 no-socket reason names the right end · FIX D arc-clearance warning · FIX E signed margin.

## Phase 3 — AFTER numbers, same harness, same fixtures
- **A**: 7 checks **6 failures -> 0**. All seven now turn with the face (hook +Y 0.112x0.185x0.309 vs +X 0.185x0.309x0.112).
- **A2**: spinnerSaw XP/YP/ZN now 0.276x0.457x0.471 / 0.457x0.276x0.471 / 0.457x0.471x0.276 — three distinct.
- **B**: 2 failures -> 0. beamlong and engine both start at yaw 0.
- **C**: 12 plates / 21,014 cr -> 2 plates / 3,644 cr. Refused at exactly the right click (3644+1737 > 4000), reason quotes all three numbers.
- **C2**: same 24-probe accept/refuse pattern, 0 failures — rule unchanged, message improved.
- **D**: pointer-built pivot+blade. VERTICAL chassis face: builder now predicts 42% of the arc, arena delivers 32%. ROOF control: builder 100%, arena 100%, no warning. Predictor reads ~10 points OPTIMISTIC against the live arena (the machine pitches under its own hammer) — stated, not calibrated on n=1.
- **E**: live fight card now reads "damage 0 vs 319 (100.0% margin **their way**)" on a PlayerLoss.
- **Regression**: UserPathTest **126 checks, 0 failures, 0 off-camera** — identical to the round-1 baseline.
- Fight path: full 90 s judges' decision vs MAULER, 0 console errors. SpawnBot's weapon sid on owen's build is "spinnerZP" — byte-identical to the string it built inline before.
