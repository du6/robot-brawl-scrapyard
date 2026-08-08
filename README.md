# Robot Brawl: Bolt & Blade — Prototypes (Unity)

Playable prototypes for a physics-based robot construction & combat game
(design doc in `docs/`). Two phases so far, both built procedurally at Play
time — no scenes to wire, no prefabs, no packages beyond a default Unity
install.

- **Phase 0 — Physics proof** (`Assets/Phase0`): a hard-coded 3-part bot
  proving the two make-or-break systems: emergent tipping from honest mass
  data, and virtual-joint breaking with dynamic body splitting.
- **Phase 1 — Assembly MVP** (`Assets/Phase1`): snap-together robot builder
  (socket-to-socket placement, live mass/cost/center-of-mass readouts,
  stability warnings) plus a test-drive arena with a ram dummy. Iterated
  through a four-round critic playtest loop to a SATISFIED 8/10: honest
  damage physics, wheel-aligned drive, a full wreck/reset loop.

## Setup (5 minutes)

1. Install **Unity 2021.3 LTS or newer** (Unity 6 works — the scripts handle
   the renamed APIs) via Unity Hub.
2. Create a new project with the **3D template** (Built-in RP or URP —
   materials pick the right shader automatically).
3. Copy the `Assets/Phase0` and `Assets/Phase1` folders into your project's
   `Assets/` folder.
4. Open any scene (the default SampleScene is fine) and press **Play**, then
   pick a mode from the menu: **PHASE 1 — Robot Builder** or
   **PHASE 0 — Physics Sandbox**.

Both the legacy Input Manager and the new Input System are supported
(`Phase0Input` shims whichever backend is active).

## Controls

### Phase 1 builder
| Input | Action |
|---|---|
| Click a part button, then a socket (gray dot) on the robot | Attach part |
| R | Rotate the pending beam |
| Right-click a placed part | Remove it (orphan-protected) |
| Q/E · scroll | Orbit · zoom |
| T | Test drive |

The green floor arrow marks the FRONT (the direction W will drive, derived
from how the wheels are mounted). Red dot = center of mass; the panel warns
when a build will tip, drag its chassis, or fight its own wheels.

### Test drive
| Input | Action |
|---|---|
| W/A/S/D or arrows | Drive / steer |
| R | Reset the arena run (works from any wreck) |
| B | Back to the builder (design preserved) |

### Phase 0 sandbox
W/A/S/D drive · T toggles the top-heavy variant · F flip impulse · R reset.
The per-joint "peak stress / threshold" HUD exists so `BREAK_K` can be tuned
against real collisions instead of guesses.

## Layout

```
Assets/Phase0/Scripts   physics core: CompoundRobot (virtual joints + dynamic
                        body splitting), RaycastWheelDrive (custom raycast
                        wheels, per-wheel axle-aligned drive), MatDB, input
                        shim, Phase 0 sandbox + bootstrap menu, FollowCamera
Assets/Phase1/Scripts   builder: BuilderManager, part palette (Phase1Parts),
                        PartVisualFactory (compound part visuals shared by
                        builder and arena spawns)
docs/                   design doc v0.2
```

## Design pillars proven so far

1. **Driving feels physical** — spring/damper raycast wheels, load-clamped
   friction, forces at the contact point; bots lean, wheelie, and roll over
   when the mass data says they should.
2. **Nobody coded "tipping"** — stability is an emergent consequence of
   per-part masses from material densities.
3. **Bodies really split** — virtual joints accumulate contact-impulse
   stress; past threshold, connectivity re-runs from the core and severed
   chunks respawn as their own rigidbodies with inherited velocity.

## Known simplifications (deliberate)

- Inertia tensor is engine-computed from collider shapes (mass and CoM are
  per-part-correct).
- Stress attribution is distance-falloff from the contact point, not
  load-path analysis (§5.2's stated approximation).
- Severed debris is inert; parts are axis-aligned; IMGUI; no design
  save/load yet.
- No damage HP, energy budget, weapons, or AI — that's Phase 2+.

## What's next (per the roadmap)

Phase 2 combat vertical slice: damage HP model, spinner + rammer weapons,
one AI opponent, win/lose + results screen. First housekeeping task: design
save/load to JSON.
