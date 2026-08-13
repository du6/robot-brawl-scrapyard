using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// The parts catalog (design doc §10). Phase 1 shipped ~6 parts in one
/// material; Phase 2A added the Weapon category; Phase 3 completes the
/// material story — every part's mass, cost, durability and joint strength
/// now derive from whichever §4.3 material the player picked for THAT
/// placement, so a steel beam and an aluminium beam are the same part with
/// different physics.
///
/// A part def carries its DEFAULT material; PlacedPart may override it.
/// </summary>
public enum P1Category { Structural, Mobility, Power, Control, Weapon }

public class P1PartDef
{
    public string id;
    public string label;
    public string desc;           // one-line functional description for the catalog
    public P1Category category;
    public Vector3 size;          // meters (wheels/spinner: x/z = diameter, y = width)
    public string matName = "Aluminum";
    // §7 damage multiplier when THIS part is the one striking:
    // 1.0 plain structure · 1.2 ram spike · 1.5 spinner tooth · 1.6 saw.
    public float edgeHardness = 1f;
    /// <summary>Phase 3: false pins the part to its default material. Used for
    /// parts where the §4.3 table is meaningless or an exploit — a wheel forms
    /// no seams and carries no HP, so its material would move nothing but mass
    /// and cost, and a battery's mass is its cells rather than its casing.</summary>
    public bool materialChoice = true;
    /// <summary>Phase 4: this part is an ACTUATOR - a joint boundary. Two
    /// consequences the rest of the builder needs: things may be bolted TO it
    /// (a bare pivot is useless, so the "nothing attaches to a weapon" rule
    /// must not apply), and it mates through real face sockets rather than the
    /// single centre socket a disc gets, because a limb hanging off a x1 seam
    /// would shear off on the first contact.</summary>
    public bool actuator = false;
    /// <summary>========= RIM DENSITY (owen, 2026-07-28) =========
    /// "with spinner blade, the saw looks redundant to me".
    ///
    /// He was right that one was redundant and wrong about which. MEASURED
    /// before this: for +4 cr the saw gave MORE reach (0.23 vs 0.17), MORE
    /// stored energy (1285 vs 1170 J), a HARDER edge (1.6 vs 1.5) and MORE HP
    /// (0.0127 vs 0.0116 volume). The spinner had no advantage on any axis, and
    /// the arena agreed - 3.2 bites / 213 dmg per run for the saw against
    /// 0.6 / 115 for the spinner. Every shipped roster bot carries the weaker
    /// part; the saw is on none of them.
    ///
    /// Worse, the catalogue text described the OPPOSITE of what shipped: the
    /// spinner promised "heavy rim, lots of stored energy in a small package"
    /// and the saw "less rim mass". Mass is derived from bounding-box volume,
    /// so a wider disc is automatically heavier and the intended trade could
    /// never exist.
    ///
    /// This multiplier is what makes the description true. It is the ONLY way
    /// to say "this part is denser than its box" in a model where mass comes
    /// from volume x material, and it flows into cost automatically because
    /// cost = mass x cost/kg.</summary>
    public float massMul = 1f;
    /// <summary>Non-null restricts the choice to this set (materialChoice must
    /// still be true). The engine is the case: real engine blocks are aluminium
    /// or cast iron, which is a genuine weight-vs-durability trade, but a
    /// carbon-fibre engine block is not a thing.</summary>
    public string[] allowedMats = null;

    /// <summary>ROSTER-ONLY (owen, 2026-08-12): the part exists for ENEMY
    /// recipes but is retired from the player experience — no palette tile,
    /// no shop rows, no kit grant. The def stays in Palette() so every
    /// index-keyed site keeps its numbering (the tab-renumbering trap), old
    /// saves keep loading, and EnemyRoster.D() keeps finding it. First (and
    /// so far only) case: the chassis block — the skeleton of all eight
    /// roster bots, which players demonstrably never use (every production
    /// ladder robot is all-beam).</summary>
    public bool rosterOnly = false;

    /// <summary>GUSSET (owen, 2026-08-12): this part is an APPLIQUE — it is
    /// never PLACED as geometry. Selecting it and tapping a placed part
    /// applies it TO that part (BuilderManager.ApplyGusset). It occupies no
    /// socket, adds no collider and no bounding box; only mass and an effect.
    /// The palette, the shop, inventory and the stock counters all treat it
    /// as an ordinary part, which is the point of it living in this table.</summary>
    public bool applique = false;
    /// <summary>Non-zero replaces the derived mass x costPerKg price. The
    /// gusset needs it: its 10 kg of Steel derives to 8 scrap, and a seam
    /// upgrade priced like a bracket-and-a-half is not a decision. Priced as
    /// a premium consumable (~1.5 L1 wins in the halved economy).</summary>
    public int flatCost = 0;

    /// <summary>P1 (Programmable Robots, 2026-08-05): this part is a SENSOR —
    /// a destructible eye that feeds SensorBus. Consequences elsewhere: the
    /// ghost stores its mount-face normal in wheelAxis (a nose rangefinder sees
    /// ahead, a side one covers a flank), SpawnBot registers it with the bus,
    /// and shearing it off silences its channels ("no signal"). Sensors are
    /// pinned parts: the mass is electronics, not casing — like the battery.</summary>
    public bool sensor = false;
    /// <summary>Idle draw while the machine is live, kW. More eyes, faster
    /// drain — the widowmaker lesson generalized (design doc §4.1).</summary>
    public float sensorKW = 0f;

    // ---- §6.2 energy budget ------------------------------------------------
    /// <summary>Stored energy, kJ. Batteries are the tank. The engine carries a
    /// small reserve of its own so an engine-only build still moves - a build
    /// that passes Validate() must never be inert on the grid.</summary>
    public float energyKJ = 0f;
    /// <summary>Peak draw this part permits, kW. Engines are what let you spend
    /// energy FAST; a battery alone can hold a lot and still not be able to
    /// push a heavy machine and spin a disc at the same time.</summary>
    public float powerKW = 0f;

    /// <summary>Mass in a SPECIFIC material. Wheels are hollow-ish in spirit;
    /// weapons are a disc/wedge inside their bounding box — bill both at half
    /// a solid block.</summary>
    public float MassOf(string mat)
    {
        float solid = MatDB.MassOf(size, MatDB.Get(EffectiveMat(mat)));
        float bill = (category == P1Category.Mobility || category == P1Category.Weapon)
                   ? solid * 0.5f : solid;
        return bill * massMul;
    }

    /// <summary>§4.3: cost = mass × the material's cost/kg — unless the def
    /// carries a flat price (see flatCost).</summary>
    public int CostOf(string mat)
    {
        if (flatCost > 0) return flatCost;
        return Mathf.RoundToInt(MassOf(mat) * MatDB.Get(EffectiveMat(mat)).costPerKg);
    }

    /// <summary>Resolve a requested material against this def's rules.</summary>
    public string EffectiveMat(string mat)
    {
        if (!materialChoice || string.IsNullOrEmpty(mat) || !MatDB.Has(mat)) return matName;
        if (allowedMats != null)
        {
            bool ok = false;
            foreach (var a in allowedMats) if (a == mat) { ok = true; break; }
            if (!ok) return matName;
        }
        return mat;
    }

    /// <summary>Does this def actually accept `mat`? The builder uses it to mark
    /// a part whose material the picker cannot change, so a pinned part does not
    /// look like the picker is broken.</summary>
    public bool Accepts(string mat) { return EffectiveMat(mat) == mat; }

    public float Mass() { return MassOf(matName); }
    public int Cost() { return CostOf(matName); }

    public static P1PartDef[] Palette()
    {
        return new[]
        {
            // OWEN 2026-08-02: "should core only have one material?" - yes.
            //
            // The core's material was the strongest single lever in the game
            // and the least legible one. MEASURED across the six materials:
            //   mat           kg    coreHP   seamCap(N.s)   career price
            //   ABS           28      57         525            0
            //   CarbonFiber   43     178        1650            0
            //   Aluminum      73      97         900            0
            //   Titanium     122     227        2100            0
            //   Steel        212     162        1500            0
            //   Tungsten     521     194        1800            0
            //
            // Two reasons that is too much power for one picker click:
            //  1. It is a 4x swing on the HP of the ONE part you cannot afford
            //     to lose - the KO target.
            //  2. A seam breaks at the WEAKER of its two parts (CompoundRobot:
            //     min(strengthRel) x BREAK_K), so the core also CAPPED every
            //     joint touching it. An ABS core held every core seam to
            //     525 N.s no matter what was bolted to it; Titanium raised the
            //     same seams to 2100. That is a whole-machine structural
            //     decision hidden inside one part's material.
            //
            // And in career it was not even a trade: CareerDB.PartPrice returns
            // 0 for the core in EVERY material, and BuildValueCareer sums those
            // same prices - so a Titanium core cost nothing AND made the build
            // read as poorer, which inflated the underdog payout. Meanwhile
            // Aluminium, the DEFAULT, is strictly dominated by CarbonFiber on
            // weight, HP and seam strength simultaneously. The only correct
            // play was to switch off the default immediately, for free, and
            // nothing in the UI said so.
            //
            // Pinning is one flag. PlacedPart.MatName() resolves through
            // EffectiveMat, so the builder picker, the shop shelf, the snapshot
            // loader, the arena spawn and EnemyRoster's recipes all follow from
            // this line - there is no second place to keep in sync. The armour
            // decision now lives where it is legible and priced: plates,
            // chassis blocks and the frame material.
            new P1PartDef { id = "core",    label = "Core (controller)", category = P1Category.Control,    size = new Vector3(0.30f, 0.30f, 0.30f),
                            materialChoice = false,
                            desc = "The robot's brain and KO target. Lose it, lose the fight. Always aluminium - protect it with armour plate and frame, not by rebuilding it." },
            new P1PartDef { id = "beam",    label = "Beam",              category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 0.60f),
                            desc = "Structural spar. Extends the chassis and gives wheels somewhere to mount. R rotates it." },
            new P1PartDef { id = "beamlong", label = "Long beam",        category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 1.00f),
                            desc = "Full-length spar. One piece instead of two seams - fewer joints to shear." },
            // "bracket" (0.20 cube joiner) was REMOVED 2026-08-12 — owen's
            // call, redundancy-by-usage: zero uses in the enemy roster, zero
            // in owen's save, zero in production, and the starter kit granted
            // FOUR that no author ever placed. The R-key symmetric-shape
            // fallback it motivated stays (the battery is also a cube).
            new P1PartDef { id = "chassis", label = "Chassis block",     category = P1Category.Structural, size = new Vector3(0.40f, 0.30f, 0.50f),
                            rosterOnly = true,   // player-side retirement, owen 2026-08-12 — see the rosterOnly note
                            desc = "Big load-bearing block. Lots of sockets, so the joints around it are strong." },
            new P1PartDef { id = "plate",   label = "Armor plate",       category = P1Category.Structural, size = new Vector3(0.50f, 0.06f, 0.50f),
                            desc = "Thin armor sheet. Cheap protection for the core and engine." },
            // GUSSET (owen, 2026-08-12). The disarm lever sweep (105 fights,
            // docs/Disarm_Lever_Sweep_2026-08-10.md) measured that seam
            // strength x1.5 takes mutual disarm 40% -> 27% and that x2 buys
            // NOTHING more — this part is that lever made local, priced and
            // handed to the player. One per part, no stacking (the second
            // step is the measured dead zone). size/massMul make MassOf(Steel)
            // = 10.0 kg (0.001 m3 x 7850 kg/m3 x 1.274) — mass is the ladder's
            // category currency, so reinforcing costs weight-class headroom.
            new P1PartDef { id = "gusset",  label = "Gusset (weld kit)", category = P1Category.Structural, size = new Vector3(0.10f, 0.10f, 0.10f),
                            matName = "Steel", materialChoice = false, massMul = 1.274f,
                            applique = true, flatCost = 200,
                            desc = "Weld kit. Pick it up, then tap a placed part: every joint that part makes gets x1.5 break strength. +10 kg, no space. One per part - a second adds nothing." },
            // OWEN 2026-07-29: "Looks like I can move the robot and the weapon
            // as long as I have battery." Correct, and the old description was
            // the reason it read as a mystery - it led with the +14 kW ceiling
            // and never mentioned the engine's real job.
            //
            // MEASURED (qa_engineprobe.txt, qa_eng{0,1,3}_sweep.txt; identical
            // 909 kg disc build, engine count the ONLY difference, charge
            // policy, trigger held, n=6 each vs mauler):
            //
            //   eng  mass  motorKW  windUp  maxRate  peakDemand/ceiling  KOs
            //    0   909    1.00    2.23 s   73.5     2.9 / 8.0 kW       4/6 @ 6.9-8.6 s
            //    1  1016    2.50    1.34 s   73.5     7.1 / 22.0 kW      4/6 @ 2.1-2.6 s
            //    2  1122    4.00    1.10 s   73.5    11.4 / 36.0 kW      -
            //    3  1228    5.50    0.95 s   73.5    15.7 / 50.0 kW      0/6, all 90 s
            //
            // Three things that description got wrong, in order of severity:
            //  1. The ceiling is NOT the point. A battery alone supplies 8 kW
            //     and the engineless build peaked at 2.9 - it never strained
            //     and never ran flat. The +14 kW only becomes relevant because
            //     fitting an engine is what raises demand in the first place.
            //  2. It omitted the ONE thing engines actually control: wind-up.
            //     maxRate is 73.5 rad/s at every engine count (the tip-speed
            //     cap sets it), so an engine buys time-to-speed and nothing
            //     else - which is worth 3x the kill speed at the first engine.
            //  3. "Fit one if you drive heavy" is stale. DRIVE_KW_PER_TONNE
            //     went 9 -> 3.5 -> 1.25, so this 909 kg machine draws 1.14 kW
            //     flat out against a battery's 8. Driving heavy has not been a
            //     reason to fit an engine for two rebalances.
            //
            // And the shape players need warned about: wind-up saturates while
            // mass does not. Engine 1 halves wind-up for +12% mass and wins.
            // Engines 2-3 shave 0.4 s more for +21% mass and cost the KO
            // outright. Same unmarked-trap shape the gyro had before it was
            // pinned; stated on the row rather than left to be discovered.
            new P1PartDef { id = "engine",  label = "Engine",            category = P1Category.Power,      size = new Vector3(0.35f, 0.25f, 0.45f),
                            allowedMats = new[] { "Aluminum", "Steel", "Titanium" },
                            energyKJ = 60f, powerKW = 14f,
                            desc = "Sets WEAPON WIND-UP speed: motor is 1.0 kW bare, +1.5 kW per engine. It does not raise a weapon's top speed, only how fast it gets there - so the first engine is worth far more than the third, and every one after that is mass you carry. Also +14 kW draw ceiling and 60 kJ." },
            new P1PartDef { id = "battery", label = "Battery",           category = P1Category.Power,      size = new Vector3(0.25f, 0.25f, 0.25f),
                            materialChoice = false, energyKJ = 240f, powerKW = 8f,
                            desc = "The tank: 240 kJ, and 8 kW of draw. Driving and spinning both spend it; at zero you stop. Mass is cells, not casing - no material choice." },
            // Pinned for now: GyroStabilizer derives its torque from the ROBOT's
            // mass, not the flywheel's, so a denser gyro today buys nothing and
            // costs dead weight - a trap. Making authority scale with the gyro's
            // own mass would make the material meaningful; that is a behaviour
            // change and wants its own tuning pass, so the trap is closed first.
            new P1PartDef { id = "gyro",    label = "Gyro stabilizer",   category = P1Category.Control,    size = new Vector3(0.24f, 0.24f, 0.24f),
                            materialChoice = false,
                            desc = "Reaction wheel. Rights the bot faster when it flips. Mount low and central." },
            // Pinned to Rubber: a wheel is a raycast anchor, not a body part -
            // it forms no seams and carries no HP, so strengthRel and durability
            // feed nothing and the §4.3 table would move only mass and cost. A
            // tungsten wheel set was purely a way to buy low-mounted mass.
            // Variety belongs on §4.2's frictionProfile axis instead.
            new P1PartDef { id = "wheel",   label = "Wheel",             category = P1Category.Mobility,   size = new Vector3(0.36f, 0.14f, 0.36f), // width >= 40% of radius - fat tires, not coins
                            matName = "Rubber", materialChoice = false,
                            desc = "Drive wheel. Attach to side faces; wheels in the front half steer. Rubber over a hub - no material choice." },
            new P1PartDef { id = "spinner", label = "Spinner blade",     category = P1Category.Weapon,     size = new Vector3(0.34f, 0.10f, 0.34f),
                            matName = "Steel", edgeHardness = 1.5f, massMul = 1.67f,
                            desc = "Dense steel flywheel. NO motor - bolt it past a spindle. Short reach, but the heaviest rim in the catalog: slow to wind up and it hits like nothing else." },
            // "spinnerSaw" (Circular saw) was REMOVED 2026-08-12 — owen's
            // call, redundancy cut: same role as the spinner (rotor bolted
            // past a spindle). The HISTORY, corrected: owen flagged this same
            // pair on 2026-07-28, and the measurement then showed the SAW
            // dominating on every axis — which is what massMul (above) was
            // invented to fix. The post-massMul trade (rim mass vs reach and
            // spin-up) was engineered to be real but never re-measured
            // head-to-head; the cut resolves the pair the other way round,
            // keeping the disc every roster bot and production robot already
            // carries. No shipped save, no enemy recipe and no production
            // snapshot carried a saw — verified before the cut. If a saved id
            // ever surfaces anyway, the loader's unknown-part path answers
            // for it, not this table.
            new P1PartDef { id = "spike",   label = "Ram spike",         category = P1Category.Weapon,     size = new Vector3(0.22f, 0.22f, 0.30f),
                            matName = "Steel", edgeHardness = 1.2f,
                            desc = "Hardened steel wedge. No motor - point it at the enemy and drive." },

            // ---- Phase 4: the base components -------------------------------
            // Three actuators and three edges, instead of more finished weapons.
            // Pivot + beam + blade is a hammer; pivot + plate is a flipper;
            // spindle + beams + blades is a drum the player shaped; ram + spike
            // is a thruster. The weapon is the ASSEMBLY, not the part.
            // 0.30 on every axis ON PURPOSE, and it is not cosmetic. An
            // actuator housing must stand PROUD of whatever it is bolted to, or
            // the limb hanging off its side face lands exactly on the host's
            // face too - and a limb that is also welded to the frame cannot
            // move. Measured on the first test build: a 0.20 pivot on a 0.20
            // bracket produced the seam `bracket_5 <-> beam_7`, so the arm was
            // (correctly) classified as chassis and the hammer did nothing.
            // 0.30 leaves a 0.05 m clearance on each side.
            new P1PartDef { id = "pivot",   label = "Pivot (hinge)",     category = P1Category.Weapon,     size = new Vector3(0.30f, 0.30f, 0.30f),
                            matName = "Aluminum", actuator = true,
                            desc = "Powered hinge. Everything you bolt past it swings through 150 deg on SPACE. Long heavy arms hit harder and swing slower." },
            new P1PartDef { id = "spindle", label = "Spindle (axle)",    category = P1Category.Weapon,     size = new Vector3(0.30f, 0.30f, 0.30f),
                            matName = "Aluminum", actuator = true,
                            desc = "Powered axle. Spins whatever is bolted past it continuously while SPACE is held. Drive a rotor disc with it, or build your own drum out of beams and blades." },
            new P1PartDef { id = "ram",     label = "Ram (piston)",      category = P1Category.Weapon,     size = new Vector3(0.30f, 0.30f, 0.30f),
                            matName = "Aluminum", actuator = true,
                            desc = "Linear piston, 0.45 m stroke. Punches whatever is bolted past it straight out on SPACE." },
            new P1PartDef { id = "blade",   label = "Blade bar",         category = P1Category.Weapon,     size = new Vector3(0.50f, 0.05f, 0.12f),
                            matName = "Steel", edgeHardness = 1.7f,
                            desc = "Thin, light, and the hardest edge in the catalog. Almost no mass - it is what you put at the END of an arm." },
            // ROUND-1 IMPL FIX (critic CRITICAL 3). edgeHardness was 1.0,
            // which is this codebase's own literal definition of "not a
            // weapon": AIController skips every part with edgeHardness <= 1.01
            // as structure, and Actuator.Bite prices a 1.0 edge as a plain
            // box. Measured consequence: the dedicated flipper build fired its
            // limb 10-19 times a match and went 0W/10L dealing 0-59 damage,
            // while the AI could not even see the wedge as a threat to avoid.
            // 1.15 is deliberately the SOFTEST edge in the catalog - below the
            // ram spike's 1.2 - because the wedge's identity is liftBias 0.75
            // and toppling, not cutting. It only has to clear the 1.01 line
            // that separates "weapon" from "structure".
            new P1PartDef { id = "wedge",   label = "Wedge",             category = P1Category.Weapon,     size = new Vector3(0.50f, 0.12f, 0.35f),
                            matName = "Steel", edgeHardness = 1.15f,
                            desc = "Low ramp. Deals little damage but throws what it hits UPWARD - flipping an opponent wins on the count without a scratch." },
            new P1PartDef { id = "hook",    label = "Hook",              category = P1Category.Weapon,     size = new Vector3(0.16f, 0.30f, 0.20f),
                            matName = "Steel", edgeHardness = 1.3f,
                            desc = "Curls back on itself. Instead of knocking the enemy away it drags them toward you and off their line." },

            // ---- P1 (Programmable Robots, 2026-08-05): the sensor palette ---
            // Five destructible eyes for the program a robot will one day run
            // (design doc v1.1 §4.1). All pinned to Aluminum — the mass is the
            // electronics, not the casing (the battery precedent). massMul
            // hits the design-doc masses exactly from clean ≥0.16 m boxes
            // (faces must clear SOCKET_PITCH 0.15 or the part is hand-
            // unplaceable — the blade lesson). Prices live in CareerDB's
            // TechFloor: sensors are technology, not tonnage.
            new P1PartDef { id = "rangefinder", label = "Rangefinder",   category = P1Category.Control,    size = new Vector3(0.18f, 0.16f, 0.18f),
                            materialChoice = false, sensor = true, sensorKW = 0.10f, massMul = 0.572f,
                            desc = "Distance eye, 12 m. Sees along the face you mount it on - a nose rangefinder looks ahead, a side one covers a flank. Shear it off and its channel goes dark." },
            new P1PartDef { id = "compass",     label = "Compass tracker", category = P1Category.Control,  size = new Vector3(0.20f, 0.16f, 0.20f),
                            materialChoice = false, sensor = true, sensorKW = 0.15f, massMul = 0.579f,
                            desc = "Arena beacon receiver: bearing and range to the enemy's center of mass, always on. The cheap way to FIND them - aiming is still your geometry problem." },
            new P1PartDef { id = "tiltsensor",  label = "Tilt sensor",   category = P1Category.Control,    size = new Vector3(0.16f, 0.16f, 0.16f),
                            materialChoice = false, sensor = true, sensorKW = 0.05f, massMul = 0.362f,
                            desc = "Knows which way is up: tilt, pitch and roll signs, flipped or not. The self-righting program starts here." },
            // V2.2 (owen's call, 2026-08-06): the edge sentinel SPLIT into two
            // parts — each unlocks its own macro-verb family on the program
            // canvas (MOVE TOWARD/AWAY FROM x · TURN side TO x). Career
            // migration turns every owned edge sentinel into one of EACH
            // (the old part did both jobs; the split must not shrink what a
            // player already paid for). Combined idle draw matches the old
            // 0.10 kW when both are mounted.
            new P1PartDef { id = "wallsensor", label = "Wall sensor",     category = P1Category.Control,   size = new Vector3(0.18f, 0.16f, 0.18f),
                            materialChoice = false, sensor = true, sensorKW = 0.05f, massMul = 0.429f,
                            desc = "Watches the arena walls: distance and direction to the nearest one. Wall-shy programs and TURN SIDE TO WALL read this." },
            new P1PartDef { id = "trapsensor", label = "Trap sensor",     category = P1Category.Control,   size = new Vector3(0.16f, 0.16f, 0.16f),
                            materialChoice = false, sensor = true, sensorKW = 0.05f, massMul = 0.362f,
                            desc = "Hazard eye: distance and direction to the nearest arena trap, plus a too-close flag. MOVE AWAY FROM TRAP starts here." },
            new P1PartDef { id = "dmgbus",      label = "Damage bus",    category = P1Category.Control,    size = new Vector3(0.16f, 0.16f, 0.16f),
                            materialChoice = false, sensor = true, sensorKW = 0.05f, massMul = 0.271f,
                            desc = "Self-diagnostics: own HP, parts lost, battery fraction, and a took-a-hit pulse. Retreat rules read this." },
        };
    }
}
}
