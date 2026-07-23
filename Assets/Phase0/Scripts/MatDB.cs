using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The material table from design doc §4.3. One small table drives mass,
/// strength, durability, and cost across every part. Phase 0 uses density
/// (→ mass) and strength (→ virtual-joint break thresholds).
/// </summary>
public class MatDef
{
    public string name;
    public float density_g_cm3;   // real-world density
    public float strengthRel;     // relative strength (steel = 1.0)
    public Color color;

    public MatDef(string name, float density, float strength, Color color)
    {
        this.name = name;
        this.density_g_cm3 = density;
        this.strengthRel = strength;
        this.color = color;
    }
}

public static class MatDB
{
    static readonly Dictionary<string, MatDef> mats = new Dictionary<string, MatDef>
    {
        { "ABS",         new MatDef("ABS Plastic",  1.05f, 0.2f, new Color(0.92f, 0.90f, 0.85f)) },
        { "Aluminum",    new MatDef("Aluminum",     2.70f, 0.6f, new Color(0.75f, 0.78f, 0.82f)) },
        { "Steel",       new MatDef("Steel",        7.85f, 1.0f, new Color(0.35f, 0.37f, 0.42f)) },
        { "Titanium",    new MatDef("Titanium",     4.51f, 1.4f, new Color(0.60f, 0.62f, 0.70f)) },
        { "CarbonFiber", new MatDef("Carbon Fiber", 1.60f, 1.1f, new Color(0.12f, 0.12f, 0.14f)) },
        { "Tungsten",    new MatDef("Tungsten",     19.3f, 1.2f, new Color(0.25f, 0.22f, 0.28f)) },
    };

    public static MatDef Get(string name) { return mats[name]; }

    /// <summary>Mass in kg for a box of the given size (meters) in this material.
    /// mass_kg = volume_m3 * density_g_cm3 * 1000 (since 1 m³ = 1e6 cm³).</summary>
    public static float MassOf(Vector3 size, MatDef mat)
    {
        float volume_m3 = size.x * size.y * size.z;
        return volume_m3 * mat.density_g_cm3 * 1000f;
    }

    /// <summary>Render material that works in both Built-in and URP projects.</summary>
    public static Material MakeRenderMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        var m = new Material(sh);
        m.color = c; // maps to _Color (Standard) or _BaseColor (URP Lit, via [MainColor])
        return m;
    }
}
