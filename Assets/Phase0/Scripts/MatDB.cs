using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// The material table from design doc §4.3. One small table drives mass,
/// strength, durability, and cost across every part. Phase 3 completes it:
/// all six materials are selectable in the builder, and costPerKg makes the
/// economy fall out of the same numbers.
/// </summary>
public class MatDef
{
    public string key;            // palette/snapshot key ("Steel")
    public string name;           // display name ("Steel")
    public float density_g_cm3;   // real-world density
    public float strengthRel;     // relative strength (steel = 1.0)
    public float costPerKg;       // §4.3 relative cost per kg
    public string character;      // one-line build advice for the panel
    public Color color;
    /// <summary>PBR finish, so a mixed build LOOKS mixed: plastic is matte and
    /// non-metallic, steel is glossy, titanium is dull, carbon fibre is a
    /// near-black lacquer. Four of the six materials are grey in real life, so
    /// finish carries most of the passive visual difference — colour alone
    /// cannot, and exaggerating the colours would undercut the premise that
    /// these are real metals.</summary>
    public float metallic;
    public float smoothness;
    /// <summary>High-contrast categorical colour used ONLY by the builder's
    /// material view. Deliberately unrealistic and deliberately not `color`:
    /// this one is for telling six things apart at a glance (Okabe-Ito, which
    /// stays distinguishable for the common colour-vision deficiencies).</summary>
    public Color auditColor;

    public MatDef(string key, string name, float density, float strength, float cost,
                  string character, Color color, float metallic, float smoothness, Color auditColor)
    {
        this.key = key;
        this.name = name;
        this.density_g_cm3 = density;
        this.strengthRel = strength;
        this.costPerKg = cost;
        this.character = character;
        this.color = color;
        this.metallic = metallic;
        this.smoothness = smoothness;
        this.auditColor = auditColor;
    }
}

public static class MatDB
{
    /// <summary>Builder display order: launch trio first (§10), then the
    /// premium three that Phase 4 will gate behind the unlock ladder.</summary>
    public static readonly string[] Order =
        { "ABS", "Aluminum", "Steel", "Titanium", "CarbonFiber", "Tungsten" };

    /// <summary>Materials available without an unlock (§10 "at launch").
    /// Phase 4 replaces this with the profile's unlock set.</summary>
    public static readonly string[] Starter = { "ABS", "Aluminum", "Steel" };

    static readonly Dictionary<string, MatDef> mats = new Dictionary<string, MatDef>
    {
        // Round-5 fix 7 (critic finding 7: "ABS is a trap material"). At 0.2 the
        // cheapest launch-tier material was not a trade-off, it was a losing
        // move: a 190 kg all-ABS scout was cored in 2.4 s while parked. 0.35
        // keeps it clearly the fragile option (Aluminium 0.6) but lets it
        // survive an opening ram, so "cheap and light" is a build style rather
        // than a mistake the palette does not warn you about.
        { "ABS",         new MatDef("ABS", "ABS Plastic",  1.05f, 0.35f, 0.5f,
                            "Ultralight and cheap - seams shear first, armour the core", new Color(0.92f, 0.90f, 0.85f),
                            0.00f, 0.30f, new Color(0.94f, 0.89f, 0.26f)) },   // matte plastic / yellow
        { "Aluminum",    new MatDef("Aluminum", "Aluminum", 2.70f, 0.6f, 1.0f,
                            "Light, moderate strength - the all-rounder", new Color(0.75f, 0.78f, 0.82f),
                            0.85f, 0.55f, new Color(0.34f, 0.71f, 0.91f)) },   // satin / sky blue
        { "Steel",       new MatDef("Steel", "Steel",       7.85f, 1.0f, 0.8f,
                            "Heavy, strong, cheap - brute force", new Color(0.35f, 0.37f, 0.42f),
                            0.92f, 0.78f, new Color(0.00f, 0.45f, 0.70f)) },   // glossy / dark blue
        { "Titanium",    new MatDef("Titanium", "Titanium", 4.51f, 1.4f, 3.0f,
                            "Strong AND lightish - expensive", new Color(0.62f, 0.60f, 0.56f),
                            0.85f, 0.30f, new Color(0.90f, 0.62f, 0.00f)) },   // dull warm / orange
        { "CarbonFiber", new MatDef("CarbonFiber", "Carbon Fiber", 1.60f, 1.1f, 4.0f,
                            "Very light + strong, pricey", new Color(0.10f, 0.10f, 0.12f),
                            0.25f, 0.88f, new Color(0.80f, 0.47f, 0.65f)) },   // black lacquer / pink
        { "Tungsten",    new MatDef("Tungsten", "Tungsten", 19.3f, 1.2f, 6.0f,
                            "Extreme density - spinner rims, counterweights", new Color(0.25f, 0.22f, 0.28f),
                            0.97f, 0.42f, new Color(0.00f, 0.62f, 0.45f)) },   // heavy metal / teal
        // PINNED-ONLY material: deliberately absent from Order, so it never
        // appears in the builder's picker. A wheel is a tyre over a metal hub,
        // not a structural member — 1.50 g/cm3 is that blend, not pure rubber
        // (1.1 would read implausibly light for something with a hub in it).
        // strengthRel is inert for wheels (they form no seams and carry no HP)
        // and is set low only so the number is not misleading if it is ever read.
        { "Rubber",      new MatDef("Rubber", "Rubber",     1.50f, 0.3f, 0.6f,
                            "Tyre compound over a metal hub - grip, not structure", new Color(0.18f, 0.18f, 0.20f),
                            0.00f, 0.18f, new Color(0.60f, 0.60f, 0.60f)) },   // matte / grey
    };

    /// <summary>ROUND-1 IMPL FIX (critic MAJOR: "an unknown material key
    /// silently becomes Aluminum and Validate() reports OK - and the project's
    /// own docs use the wrong key"). The critic authored a build with `Carbon`,
    /// it loaded as ALUMINIUM, Validate() returned null, LimbReport quoted
    /// numbers and four matches ran with nothing warning - so a whole measured
    /// result turned out to be a result about a different material.
    ///
    /// Two halves to the fix. This is the first: the spellings that are
    /// genuinely the SAME material resolve instead of degrading. The second is
    /// in BuilderManager.LoadSnapshot, which now warns on a key even this
    /// cannot resolve. Deliberately NOT added to `mats` itself - an alias must
    /// not become a seventh entry in the picker or a second spelling in a
    /// saved snapshot.</summary>
    static readonly Dictionary<string, string> alias = new Dictionary<string, string>
    {
        { "Carbon",      "CarbonFiber" },   // the spelling the project's own docs use
        { "CarbonFibre", "CarbonFiber" },   // en-GB
        { "Aluminium",   "Aluminum"    },   // en-GB
    };

    /// <summary>The canonical key for `name`, or null if this table has never
    /// heard of it. Callers that need to KNOW (rather than degrade) use this.</summary>
    public static string Canon(string name)
    {
        if (name == null) return null;
        if (mats.ContainsKey(name)) return name;
        string a;
        return alias.TryGetValue(name, out a) ? a : null;
    }

    public static MatDef Get(string name)
    {
        MatDef d;
        if (name != null && mats.TryGetValue(name, out d)) return d;
        string c = Canon(name);
        if (c != null) return mats[c];
        return mats["Aluminum"];   // unknown key (old snapshot, typo) degrades safely
    }

    /// <summary>True when `name` names a real material, alias spellings
    /// included. EffectiveMat() gates on this, so an aliased key now survives
    /// into the placement instead of silently falling back to the default.</summary>
    public static bool Has(string name) { return Canon(name) != null; }

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
}
