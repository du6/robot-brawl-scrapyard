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

    /// <summary>§4.3: cost = mass × the material's cost/kg.</summary>
    public int CostOf(string mat)
    {
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
            new P1PartDef { id = "core",    label = "Core (controller)", category = P1Category.Control,    size = new Vector3(0.30f, 0.30f, 0.30f),
                            desc = "The robot's brain and KO target. Lose it, lose the fight." },
            new P1PartDef { id = "beam",    label = "Beam",              category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 0.60f),
                            desc = "Structural spar. Extends the chassis and gives wheels somewhere to mount. R rotates it." },
            new P1PartDef { id = "beamlong", label = "Long beam",        category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 1.00f),
                            desc = "Full-length spar. One piece instead of two seams - fewer joints to shear." },
            new P1PartDef { id = "bracket", label = "Bracket",           category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 0.20f),
                            desc = "Short cube joiner. Cheap filler for tying two runs together." },
            new P1PartDef { id = "chassis", label = "Chassis block",     category = P1Category.Structural, size = new Vector3(0.40f, 0.30f, 0.50f),
                            desc = "Big load-bearing block. Lots of sockets, so the joints around it are strong." },
            new P1PartDef { id = "plate",   label = "Armor plate",       category = P1Category.Structural, size = new Vector3(0.50f, 0.06f, 0.50f),
                            desc = "Thin armor sheet. Cheap protection for the core and engine." },
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
            new P1PartDef { id = "spinnerSaw", label = "Circular saw",   category = P1Category.Weapon,     size = new Vector3(0.46f, 0.06f, 0.46f),
                            matName = "Steel", edgeHardness = 1.6f, massMul = 0.60f,
                            desc = "Wide, thin rotor. NO motor - bolt it past a spindle. 35% more reach than the spinner and spins up in a third of the time, but far less rim mass behind each bite." },
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
        };
    }
}
}
