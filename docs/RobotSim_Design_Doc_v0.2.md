# Robot Combat Simulator — Technical Design Document

*A single-player, physics-based robot construction and combat game for iOS + Android. Working title: Robot Brawl: Bolt & Blade.*

Version 0.2 — living document. Author target: solo / hobby developer.

**Changelog v0.1 → v0.2:**
- Resolved the fuse-vs-break contradiction: structural connections are now explicitly modeled as **virtual joints with dynamic body splitting** (new §5.2–5.3). Flagged as the hardest system in the game and added to the risk table.
- New §6.4 "Solver survival kit": continuous collision detection, impulse clamping, virtual stored-energy spinners, and raycast wheels instead of Unity WheelColliders.
- §3 now distinguishes *faking simulation stability* (required) from *faking design outcomes* (forbidden).
- §2 now argues the Roblox-vs-Unity decision explicitly instead of assuming it.
- §7 specifies **player self-righting** as a designed mechanic, not an afterthought.
- §7 damage formula notes that spinner energy must be tracked and injected manually.
- Roadmap total revised to 12–18 months; Phase 0 now includes the body-splitting proof.
- Fixed typo in §4.2 (`corrywTorque` → `gyroTorque`).

---

## 1. Vision

Build a robot from real, granular parts — engines, gears, beams, wheels, saws, blades — where every component has genuine physical properties (weight, strength, material). Drop your creation into an arena and fight AI opponents. The realism is the point: a top-heavy design tips over when it turns, an under-powered engine can't move a heavy chassis, and a well-struck joint tears a limb clean off.

### Design pillars

1. **Emergent realism over scripted behavior.** Instability, tipping, and structural failure are *outputs* of an honest rigidbody simulation, not hand-coded rules. If the physics data is right, the drama happens by itself.
2. **Deep, legible building.** Granular parts and real materials give expressive freedom, but the build UI must make the consequences (weight, balance, power) visible before the fight.
3. **Fair, readable combat.** The player should always understand *why* they won or lost — traced to a design choice (too heavy, too tall, too weak a joint), not a random number.
4. **Ships on a phone.** Every system is chosen for what runs at 60 fps on a mid-range mobile device, solo-maintainable.

### Scope (v1)

- Single-player vs AI only. No online multiplayer. (This is the deliberate scope cut that makes the project solo-viable.)
- Cross-platform: iOS + Android from one codebase.
- A build mode, a test arena, AI battles across difficulty tiers, and a light progression/economy.

### Non-goals (v1)

- Online/PvP, spectator, or social features.
- Finite-element / soft-body material deformation (see §6 for the affordable substitute).
- Full campaign narrative. A challenge-ladder structure is enough to prove the loop.

---

## 2. Platform & Engine

This is the most expensive decision in the document, because one of the options is "keep the codebase you already have." It deserves a real argument, not an assumption.

### 2.1 The honest comparison: stay in Roblox vs. move to Unity

**The case for staying in Roblox:**

- Robot Brawl already exists there — a working codebase, tuned balance constants, and a component catalog. Moving engines resets code progress to zero.
- Free distribution and built-in discovery; no App Store / Play Store fees, review cycles, or store-listing work.
- Roblox has constraints, motors, and hinge physics; a *version* of this game is buildable there.
- You already know the platform and its tooling.

**The case for Unity (why this doc still recommends it):**

- The core pillar is *honest granular physics*: per-part mass from material density, engine-computed center of mass and inertia tensors, per-connection break thresholds, and (new in v0.2) dynamic compound-body splitting. Unity/PhysX exposes all of these directly. Roblox's physics is deliberately abstracted — you get far less control over solver behavior, mass properties, and collision response, and the body-splitting system in §5.3 would be fighting the platform instead of using it.
- 60 fps with many jointed, breakable parts on mid-range mobile is achievable in Unity with the budgets in this doc; in Roblox you don't control the frame budget or the physics step.
- Full ownership of monetization, pricing, and the store relationship.

**The verdict:** Unity — *because* the design pillar is granular physical honesty. If the pillar were "fast, fun robot fights with my existing systems," staying in Roblox would win, and that would be a legitimate different game.

**The hedge:** Phase 0 (§12) is the checkpoint. If the Unity physics proof stalls or isn't fun, a Roblox-native reboot of this design — with abstracted rather than granular physics — is the explicit plan B, and it would reuse the Robot Brawl codebase directly.

### 2.2 Engine notes

- **Unity:** best-in-class iOS + Android build pipeline from a single project; mature 3D rigidbody physics (PhysX) with joints, motors, and configurable solver parameters; huge ecosystem for a solo dev.
- **Open-source alternative: Godot 4** (Jolt physics backend). Lighter, free, no per-revenue fees, but a smaller mobile-physics track record and more plumbing to build yourself.
- **Not recommended:** native Swift/SceneKit (Apple-only, rebuilds engine plumbing) or Unreal (heavier than this art style needs on mobile).

### 2.3 Mobile budget (design constraint, not an afterthought)

- Target: 60 fps on a ~4-year-old mid-range phone.
- Practical limits: keep an assembled robot under ~40–60 parts (most fused into one rigidbody — see §5); cap simultaneous fighters (player + 1–3 AI) in v1; fixed physics timestep at 50 Hz, sub-stepping only for fast spinners.
- These numbers *shape the design* — they're why v1 is small brawls, not 12-bot royales.

---

## 3. Core Principle: Emergent Realism

The single most important idea in this document:

> "An imbalanced robot may not even stand" is **not a feature you code.** It is the automatic result of giving the physics engine correct part masses and letting it compute the assembled body's center of mass. When the center of mass projects outside the polygon formed by the ground-contact points (wheels/feet), the body tips. The engine does this for free.

Consequences:
- You get tipping, wobble, wheelies, and toppling **for free** the moment masses and the center of mass are correct.
- Your job is not to *simulate* instability — it's to *feed the simulation honest data* (real masses, real attachment strengths, real friction) and then *surface* that data to the player in the build UI.
- Chasing scripted "balance rules" on top of real physics is the classic trap that makes these games feel fake. Resist it.

### 3.1 Fake the stability, never the outcome

The principle above needs one sharpening, because taken literally it will hurt you. Every shipped physics-construction game (Besiege, Trailmakers, the licensed BattleBots titles) runs on a bed of invisible tuning: angular damping, clamped impulses, solver iteration tweaks, velocity caps. This is not cheating — it is what keeps a discrete 50 Hz solver from exploding, jittering, or launching robots into orbit.

The rule that keeps Pillar 1 intact:

- **Fake the stability of the simulation freely.** Damping, clamps, sub-stepping, sleep thresholds, contact-softening — use whatever keeps the sim smooth and believable.
- **Never fake the outcome of a design.** A top-heavy bot must tip. A weak joint must break. An under-powered engine must strain. No hidden stabilizers that rescue bad designs, no rubber-banding of results.

If a tuning hack changes *which designs win*, it's forbidden. If it changes *how smoothly the sim runs*, it's mandatory.

---

## 4. The Component System (the heart of the game)

Every part is **data**, authored as a Unity ScriptableObject and instantiated as a prefab. Data-driven design means adding a new part is a content task, not a code task — and it mirrors how your existing Roblox `ComponentCatalog` already works.

### 4.1 Component schema

```
Component (ScriptableObject)
  id                : string            // "beam_alu_short"
  displayName       : string
  category          : enum { Structural, Mobility, Power, Weapon, Control, Cosmetic }
  prefab            : GameObject         // mesh + colliders + attach sockets
  attachPoints      : AttachPoint[]      // socket transforms + allowed mate types
  volume_cm3        : float              // from mesh bounds or authored
  material          : Material (ref)     // see §4.3
  structuralStrength: float              // → connection break threshold (§5.2)
  functionData      : FunctionBlock      // category-specific, see §4.2
  costOverride?     : int                // else derived from mass × material.costPerKg

  // DERIVED at build time (never hand-entered):
  mass_kg           = volume_cm3 * material.density_g_cm3 / 1000
  cost              = costOverride ?? round(mass_kg * material.costPerKg)
  durabilityHP      = volume_cm3 * material.strength * DURABILITY_K
```

### 4.2 Component categories & their function blocks

| Category | Example parts | Function block fields | Physics role |
|---|---|---|---|
| Structural | beam, plate, frame block, bracket | (none) | Rigidly fuse into the main body; carry loads; break past strength |
| Mobility | wheel, tread, leg, hover pad | radius, frictionProfile, maxSteer | Raycast wheel (§6.4); traction & steering |
| Power | engine, motor, battery, gearbox | powerKW, maxTorque, maxRPM, gearRatio, energyKJ | Torque source driving mobility joints; energy budget |
| Weapon | spinner+blade, saw, hammer, spike, flipper | weaponType, maxRPM (spin), reach, edgeHardness | Motorized joint or actuator; damage on contact (§7) |
| Control | gyro/stabilizer, sensor, controller | gyroTorque, range | Optional active-balance torque; AI targeting inputs |
| Cosmetic | paint, decals, shells | (none) | No physics; visual only |

### 4.3 Materials

Materials are the elegant heart of the realism. One small table drives mass, strength, durability, and cost across every part.

| Material | Density (g/cm³) | Rel. strength | Rel. cost/kg | Character |
|---|---|---|---|---|
| ABS Plastic | 1.05 | 0.2 | 0.5 | Ultralight, fragile, cheap — early game |
| Aluminum | 2.70 | 0.6 | 1.0 | Light, moderate strength — the all-rounder |
| Steel | 7.85 | 1.0 | 0.8 | Heavy, strong, cheap — brute-force builds |
| Titanium | 4.51 | 1.4 | 3.0 | Strong *and* lightish, expensive — premium |
| Carbon Fiber | 1.60 | 1.1 | 4.0 | Very light + strong, pricey, brittle under impact |
| Tungsten | 19.3 | 1.2 | 6.0 | Extreme density — spinner rims & counterweights |

Design levers this unlocks automatically:
- A steel beam and an aluminum beam are the *same part* with different mass, strength, and cost — huge expressive range from one mesh.
- Tungsten's absurd density makes it the natural choice for a spinner rim (stores more rotational energy) or a low counterweight (lowers center of mass) — players will *discover* this from physics, not from a tooltip.
- Cost = mass × cost/kg ties the economy to real choices: a titanium bot is light and tough but expensive; steel is cheap but tips easily if built tall.

### 4.4 Mass, center of mass, moment of inertia

Do **not** hand-author these for the assembled robot. When parts are joined into a compound rigidbody, Unity aggregates per-part mass and automatically computes the combined center of mass and inertia tensor. That aggregate *is* your entire stability and handling model. Set part masses correctly (§4.1) and everything downstream — tipping, turning inertia, wheelies — is physically consistent by construction.

---

## 5. The Assembly System

### 5.1 Connection model

- Parts expose **attach points** (sockets): a position, orientation, and a set of allowed mate types (e.g. a wheel hub only mates with an axle socket).
- Snapping in build mode aligns a part's socket to a target socket, like magnetic LEGO.
- Two join modes:
  - **Rigid fuse** (structural + most bolted-on parts): parts become one compound rigidbody. Cheapest, most stable.
  - **Jointed** (anything that must move): wheels, spinners, hinges, flippers attach with a Unity joint (Hinge/Configurable) so they can rotate/actuate.

### 5.2 Structural strength = virtual joints, not physics joints

v0.1 of this doc contained a contradiction: it said structural parts *rigid-fuse into one rigidbody* (fast, stable) **and** that *every connection has a breakForce* (which requires a physics joint between separate bodies). You can't have both from the engine directly. Jointing every part is a jitter-and-performance disaster at 40–60 parts on mobile; fusing every part means nothing can ever break off.

The resolution — and this is a core system, not a detail:

- Fused parts live in **one compound rigidbody** (keeping §4.4's free mass aggregation and mobile performance).
- The assembly additionally maintains a **connection graph**: a node per part, an edge per socket-to-socket connection, each edge carrying a break threshold derived from the weaker of the two mated parts' `structuralStrength`.
- These edges are **virtual joints**: they don't exist in the physics engine. Instead, each fixed step the damage resolver takes the frame's contact impulses on the compound body and attributes stress to nearby edges (impulse magnitude scaled by proximity and lever arm to the contact point — an approximation, tuned for feel, not an FEA solve).
- When an edge's accumulated stress exceeds its threshold, it **breaks** (§5.3).

Physics joints (wheels, spinners, flippers) keep their native `breakForce`/`breakTorque` — the engine handles those for free. Virtual joints are only for the fused structure.

### 5.3 Dynamic body splitting (the hardest system in the game)

When a virtual joint breaks:

1. Remove the edge from the connection graph.
2. Run connectivity from the **core/controller part**. Any subgraph no longer connected to the core is *severed*.
3. Re-parent the severed parts out of the compound body and spawn them as their own rigidbody (or debris bodies), inheriting the parent body's velocity at their position (linear + ω × r).
4. Recompute the surviving body's mass, center of mass, and inertia (Unity redoes this automatically when child colliders/masses change).
5. Fire the game-feel layer: mesh swap to damaged variant, sparks, sound, impact number.

Notes and warnings, in order of importance:

- **This is the single largest engineering risk in the project.** It is listed in §13 accordingly. Everything else in this doc is well-trodden; this system is custom.
- Losing a chunk mid-fight *changes the survivor's center of mass and handling* — which is exactly the emergent drama Pillar 1 promises. A bot that loses its left armor row starts pulling right. This is the payoff that justifies the system's cost.
- Severed debris must be aggressively pooled, put to sleep quickly, and hard-capped in count (despawn oldest) to protect the frame budget.
- Prototype the split mechanism in **Phase 0** with a hard-coded 3-part bot. If splitting can't be made robust there, the fallback is coarser: parts don't detach individually, they only *destroy in place* (mesh swap + mass removal) — less spectacular, still honest.

This mechanic gives you satisfying, physics-honest destruction (arms torn off, wheels knocked loose), a real reason to care about material strength and connection placement, and no separate "hit points per limb" bookkeeping for detachment (weapons can still ablate part `durabilityHP`, see §7).

### 5.4 Build-mode UX (make consequences visible)

The build screen must show, live, what the physics is about to do:
- **Total mass, cost, and part count** (running totals).
- **Center-of-mass marker** projected onto the ground, with the wheel/foot **support polygon** drawn. If the marker is inside it → stable; near the edge → warning; outside → it *will* tip. This is the single most important UI element in the game.
- **Power-to-weight readout**: engine torque vs mass tells the player if it'll even move.
- **A one-tap "Test" button** dropping the build into a sandbox to drive/flip/ram before committing.
- Validation gates before battle: must have ≥1 power source, ≥1 mobility part, a connected structure (no floating parts), and a controller.

---

## 6. Physics & Simulation

### 6.1 Functional parts → physics primitives

| Part | Implemented as | Behavior |
|---|---|---|
| Engine | Torque budget applied to driven joints | Power = torque × angular velocity; capped by maxTorque and maxRPM |
| Gearbox | Multiplier on the engine→wheel transfer | High ratio = more torque, less top speed (and vice-versa) |
| Wheel / tread | **Raycast wheel** (custom, §6.4) | Traction from friction profile; steer via steer angle |
| Spinner / saw | Hinge joint with a motor + **virtual energy model** (§6.4) | Stores rotational kinetic energy; drains battery to spin up |
| Hammer / flipper | Configurable joint actuated on a cooldown | Impulse actuator; energy cost per swing |
| Blade / spike | Passive collider on a rigid part | Damage from impact impulse (§7) |
| Beam / plate | Collider fused into the compound body | Carries load; virtual joint breaks past strength (§5.2) |

### 6.2 Energy budget

Batteries hold `energyKJ`. Driving, spinning weapons, and actuating cost energy per second / per use. This ports your existing Robot Brawl battery mechanic directly and adds a real design tension: a monster spinner that drains flat in 20 seconds vs a sustainable build.

### 6.3 The mobile-realism tradeoff (read this before over-engineering)

- **What you should NOT do:** finite-element stress simulation, soft-body deformation, or per-triangle fracture. These are gorgeous and completely infeasible at 60 fps on a phone for a solo dev.
- **What delivers ~90% of the feel affordably:** rigidbody parts + joints + accurate mass/friction + virtual-joint break thresholds + a handful of pre-fractured "damaged" meshes swapped in on part destruction. This is the industry-standard trick and it looks great.
- Budget discipline: pool rigidbodies, sleep idle ones, cap part counts, and sub-step physics only around active spinners.

### 6.4 Solver survival kit (the tuning hacks §3.1 permits)

Places where the naive "let PhysX handle it" approach *will* fail, and the standard fixes:

- **Fast spinners tunnel and explode the solver.** A tungsten-rimmed blade at high RPM carries enormous energy (½·I·ω²); raw contact resolution will tunnel through thin colliders or produce absurd impulses. Fixes: continuous collision detection on all weapon parts; raise Unity's default `maxAngularVelocity` (it silently caps at 7 rad/s — far below any real spinner); clamp applied impulses to a sane ceiling; sub-step around active spinners.
- **Model spinner damage as stored energy, not raw contacts.** Track each spinner's kinetic energy explicitly. On weapon contact, *compute* damage from tracked energy (§7), *drain* the tracker, and apply a clamped physical impulse for the visible shove. The engine's contact impulse is the trigger, not the source of truth — this keeps damage consistent and the solver stable.
- **Unity's WheelCollider is wrong for this game.** It's tuned for cars with known, fixed mass distributions and behaves badly under arbitrary player-built masses. Use a custom raycast wheel (spring + damper + friction from the part's frictionProfile) or a hinge joint + physic material. Decide in Phase 0, not Phase 3.
- **General stability:** modest angular damping on all bodies, solver iteration counts tuned once, aggressive sleep thresholds on debris. All legal under §3.1 — none of it changes which designs win.

---

## 7. Combat & Damage Model

A hybrid of pure-physics and light bookkeeping, so fights are dramatic *and* readable.

- **Impact damage** on any weapon-vs-part contact:
  `damage = EffectiveImpulse × attackerEdgeHardness × K`
  where `EffectiveImpulse` is *assembled* by the damage resolver, not read raw from the engine: a spinner contributes its **tracked rotational kinetic energy** (½·I·ω², from the §6.4 energy model — this is NOT present in the engine's contact impulse and must be injected manually), and a rammer contributes **linear impulse** (relative velocity × attacker mass).
- **Part durability**: each part has `durabilityHP` (from material × volume). Damage ablates it; at zero the part is destroyed (mesh swap + optional detach).
- **Connection break**: independently, enough stress snaps the mating connection (§5.2–5.3) even before HP is gone — this is how a clean hit "sends a wheel flying."
- **Win conditions** (any of):
  - Opponent immobilized (no working mobility part or engine).
  - Opponent's core/controller part destroyed (KO).
  - Timeout → judged by damage dealt + mobility remaining (mirrors your Robot Brawl overtime).
- **Readability:** floating impact numbers, a hit-flash, and a post-match "cause of defeat" line ("your bot tipped and couldn't self-right") — carrying over the clear feedback that already works well in Robot Brawl.

### 7.1 Being flipped: a designed state, not an accident

Getting flipped and lying helpless is the #1 frustration in this genre, so it's specified, not left to chance:

- **Flip detection:** a bot is *incapacitated* when its drive wheels can't reach the ground and its velocity is near zero for 2 seconds.
- **Self-right attempts:** any actuated weapon (flipper, hammer, spinner gyro-torque) can be fired while flipped — often enough to bounce a light bot back over. This is emergent (the actuator just applies its normal impulse) and free.
- **Dedicated hardware:** the **gyro stabilizer** part (Control category) grants an active self-right: hold a button to apply its `gyroTorque` and roll upright. It costs mass, cost, and energy — a real design tradeoff, not a freebie.
- **The countdown:** an incapacitated bot with no working self-right option gets a visible 10-second count-out, then loses by immobilization (mirrors real robot-combat rules). No infinite stalemates, and the defeat line tells the player exactly what to change ("add a self-right mechanism or lower your center of mass").
- **AI parity:** AI bots use identical rules and will attempt self-rights with the same hardware (§8).

---

## 8. AI Opponents

Single-player lives or dies on the AI, so budget real time here.

- **Enemy builds:** authored roster of hand-designed bots per tier (reuse your Robot Brawl archetypes — Brawler/wedge, Juggernaut/cannon-analog, Scout/fast) plus a few "gimmick" builds (tall-and-tippy, all-spinner glass cannon) that teach players to exploit physics.
- **AI behavior layers:**
  1. Drive controller — pathing to target, using the *same* physics inputs a player has (so AI can also tip over — fair and emergent).
  2. Combat logic — aim the weapon's active arc at the opponent; retreat to spin up; target exposed/weak sides.
  3. Self-preservation — attempt self-right if flipped (per §7.1), back off when energy is low.
- **Difficulty tiers** come from reaction time, aggression, and build quality — *not* from stat cheating. (This is exactly the philosophy your Robot Brawl training tiers already use.)

---

## 9. Progression & Economy (single-player)

- **Currency: Scrap** (port the name and feel from Robot Brawl). Earned by winning fights and completing challenges.
- **Unlocks:** new parts and materials gated behind a challenge ladder, so complexity ramps with player skill.
- **Structure:** a ladder/championship of AI opponents grouped into tiers, plus optional "build challenges" (e.g., "beat this bot with a build under 80 kg"). This is enough content structure to prove the loop without a full narrative campaign.
- **Persistence:** local save (garage builds, unlocks, scrap) via a serialized profile — no server needed.

---

## 10. Starter Parts Catalog (v1 target)

Enough to prove the fun; each is one mesh usable in multiple materials.

- Structural: **chassis block**, **beam (short/long)**, **armor plate**, **bracket**.
- Mobility: **wheel**, **tread unit**, (stretch) **leg**.
- Power: **small engine**, **large engine**, **gearbox**, **battery**.
- Weapon: **horizontal spinner + blade**, **circular saw**, **ram spike**, (stretch) **flipper**, **hammer**.
- Control: **controller/core** (the KO target), **gyro stabilizer** (promoted from stretch — it's the self-right mechanic, §7.1).
- Materials at launch: **ABS, Aluminum, Steel** (add Titanium/Carbon/Tungsten as unlocks).

*Next step available: I can extract your current Robot Brawl component stats (weights, damage, cooldowns, archetypes) and translate them into a seed values table for this catalog, so your tuned balance carries over.*

---

## 11. Technical Architecture (Unity)

```
/Assets
  /Scripts
    /Data        ScriptableObjects: Component, Material, EnemyBuild, Profile
    /Assembly    Socket/snap logic, compound-body builder, connection graph, validation
    /Physics     Drive controller (raycast wheels), weapon actuators, damage resolver,
                 virtual-joint stress + body splitter (§5.2–5.3)
    /AI          Behavior, targeting, difficulty
    /UI          Build screen (CoM/support overlay), HUD, results
    /Economy     Scrap, unlocks, save/load
    /Sim         Match manager, win conditions, arena hazards
  /Prefabs       Part prefabs (mesh + colliders + sockets)
  /Arenas
  /Materials(render)
```

Principles:
- **Data-driven everything** (parts, materials, enemies, balance constants are assets, not code) — same philosophy as your Roblox catalog, so tuning is fast and non-programmers can help.
- **One assembled robot = one compound Rigidbody** for the fused structure, physics joints only for moving parts, and the connection graph + body splitter (§5.2–5.3) handling structural failure.
- **Deterministic-ish fixed timestep** for consistent feel; no networking determinism needed since single-player.

---

## 12. Phased Roadmap (realistic for solo / hobby)

Time ranges assume part-time solo work and are honest, not optimistic. The goal of each phase is a *provable question answered*, not a feature count.

- **Phase 0 — Physics proof (3–5 weeks).** Hard-coded 3-part bot in Unity: drive it (on raycast wheels — settle the wheel approach now), watch a top-heavy version tip, ram a dummy hard enough to **trigger a virtual-joint break and body split** (§5.3). *Questions: is the core physics fun, and does the split system work?* Both must pass — the split system is the project's biggest risk, so it gets proven first, not discovered in month six. If either fails, stop or fall back (destroy-in-place, or the Roblox plan B from §2.1) — cheap to learn.
- **Phase 1 — Assembly MVP (1–2 months).** Snap-together build mode with ~6 parts, one material, the center-of-mass/support overlay, and a test sandbox. *Question: is building fun and legible?*
- **Phase 2 — Combat vertical slice (1–2 months).** Damage model, one spinner + one rammer (exercising the §6.4 spinner energy model), one AI opponent, win/lose, results screen. Prototype touch controls here. *Question: is a fight satisfying and readable?*
- **Phase 3 — Materials & depth (1–2 months).** Full material table, more parts, energy budget, 3–4 enemy builds, difficulty tiers.
- **Phase 4 — Progression & content (2–3 months).** Scrap economy, unlock ladder, challenge roster, more arenas, save system.
- **Phase 5 — Polish & ship (1–2 months).** Mobile performance pass, touch-control refinement, tutorial, art pass, store setup, soft launch.

**Honest total:** the phase sums say 8–14 months, but that assumes the body-splitting system (§5.3) lands without major rework — the honest planning number is **12–18 months** of part-time solo effort to a shippable first release. The single most valuable thing you can do is nail Phase 0 fast, because it tells you whether the whole idea is worth the rest.

---

## 13. Risks & Mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| **Dynamic body splitting (§5.3) is harder than planned** | **High** | Prove it in Phase 0 with 3 parts; fallback = destroy-in-place (no detach); it's the only custom-engineering system — everything else is well-trodden |
| Physics is fiddly / not fun to build in | High | Phase 0 exists precisely to find out cheaply |
| Mobile performance with many jointed parts | High | Fuse structure into one body (§5.2); hard part-count caps; debris pooling; the no-FEA rule (§6.3) |
| Spinner physics destabilizes the solver | Med-High | The §6.4 survival kit: CCD, impulse clamps, virtual energy model — designed in from Phase 0, not patched in later |
| Scope creep back toward multiplayer / huge catalogs | Med | This doc's non-goals; ship v1 small |
| AI feels dumb or unfair | Med | AI uses the same physics inputs as the player; tiers via skill not stats |
| Touch controls for driving + aiming feel bad | Med | Prototype controls in Phase 2; steal proven mobile-driving schemes |
| Solo burnout | High | Phased "provable questions"; each phase is independently satisfying |

---

## 14. What Carries Over From Robot Brawl

You are not starting from zero on *design*, only on *code*:
- **Balance knowledge** — the tuned constants, archetype win-rates, and the "why" comments in your BattleService are a priceless spec for what makes fights fair.
- **The economy & progression feel** — Scrap, build value, matchmaking-by-value → maps to single-player tiering.
- **Readability lessons** — the flanking callouts, damage numbers, and clear defeat messaging that already work.
- **Component philosophy** — your data-driven catalog approach ports directly to ScriptableObjects.

And per §2.1: if Unity's granular-physics bet doesn't survive Phase 0, the Roblox codebase isn't just design salvage — it's the plan-B foundation.

**Immediate next steps I can take for you:**
1. Extract the Robot Brawl component/balance data into a seed values table for the new catalog.
2. Draft the Phase 0 Unity prototype spec (exact scripts, scene setup, the 3-part test bot, and the virtual-joint/body-split proof).
3. Sketch the build-mode UI (the center-of-mass/support-polygon overlay) as a mockup.
