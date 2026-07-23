using UnityEngine;

/// <summary>
/// The Phase 1 starter palette (design doc §12 Phase 1: ~6 parts, ONE material).
/// Everything is aluminum for now — the full material table arrives in Phase 3;
/// mass and cost still derive from the §4.3 math so nothing is hand-tuned.
/// </summary>
public enum P1Category { Structural, Mobility, Power, Control }

public class P1PartDef
{
    public string id;
    public string label;
    public string desc;           // one-line functional description for the catalog
    public P1Category category;
    public Vector3 size;          // meters (wheels: x/z = diameter, y = width)
    public string matName = "Aluminum";

    public float Mass()
    {
        // Wheels are hollow-ish in spirit — bill them at half a solid block.
        float solid = MatDB.MassOf(size, MatDB.Get(matName));
        return category == P1Category.Mobility ? solid * 0.5f : solid;
    }

    public int Cost()
    {
        // §4.3: cost = mass × material cost/kg (aluminum = 1.0).
        return Mathf.RoundToInt(Mass() * 1.0f);
    }

    public static P1PartDef[] Palette()
    {
        return new[]
        {
            new P1PartDef { id = "core",    label = "Core (controller)", category = P1Category.Control,    size = new Vector3(0.30f, 0.30f, 0.30f),
                            desc = "The robot's brain and KO target. Lose it, lose the fight." },
            new P1PartDef { id = "beam",    label = "Beam",              category = P1Category.Structural, size = new Vector3(0.20f, 0.20f, 0.60f),
                            desc = "Structural spar. Extends the chassis and gives wheels somewhere to mount. R rotates it." },
            new P1PartDef { id = "plate",   label = "Armor plate",       category = P1Category.Structural, size = new Vector3(0.50f, 0.06f, 0.50f),
                            desc = "Thin armor sheet. Cheap protection for the core and engine." },
            new P1PartDef { id = "engine",  label = "Engine",            category = P1Category.Power,      size = new Vector3(0.35f, 0.25f, 0.45f),
                            desc = "Drive power. You need at least one power part to fight." },
            new P1PartDef { id = "battery", label = "Battery",           category = P1Category.Power,      size = new Vector3(0.25f, 0.25f, 0.25f),
                            desc = "Compact energy pack. Lighter power source than the engine." },
            new P1PartDef { id = "wheel",   label = "Wheel",             category = P1Category.Mobility,   size = new Vector3(0.36f, 0.14f, 0.36f), // width ≥ 40% of radius — fat tires, not coins
                            desc = "Drive wheel. Attach to side faces; wheels in the front half steer." },
        };
    }
}
