using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Phase 1 visual language (critic round 1). Every part is a compound of
/// 3–8 primitives built under an UNSCALED part root: the root owns the single
/// physics collider, and every decorative child's collider is destroyed the
/// moment it is created. Used by BOTH the builder (BuilderManager.AddPart) and
/// the arena spawn path (CompoundRobot.Build) so a part looks identical in the
/// workshop and in the fight — that shared path is the fix for the "engine
/// turns gray in the arena" bug.
///
/// Material language: structural gray metal 0.8/0.6 · rubber 0/0.2 ·
/// engine dark iron 0.6/0.4 + amber emissive · core saturated yellow with
/// cyan emissive status lights · sockets/bolts near-black.
/// </summary>
public static class PartVisualFactory
{
    // ------------------------------------------------------------- materials

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static Material Mat(Color c, float metallic, float smoothness)
    {
        string key = "m" + c.r + "," + c.g + "," + c.b + "," + metallic + "," + smoothness;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;
        m = MatDB.MakeRenderMat(c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);  // URP Lit
        m.SetFloat("_Glossiness", smoothness);  // Built-in Standard
        cache[key] = m;
        return m;
    }

    public static Material Emissive(Color c, Color emission, float metallic, float smoothness)
    {
        string key = "e" + c.r + "," + c.g + "," + c.b + "," + emission.r + "," + emission.g + "," + emission.b + "," + metallic + "," + smoothness;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;
        m = MatDB.MakeRenderMat(c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Glossiness", smoothness);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emission);
        cache[key] = m;
        return m;
    }

    public static Material Structural { get { return Mat(new Color(0.55f, 0.57f, 0.60f), 0.8f, 0.6f); } }
    public static Material DarkSteel { get { return Mat(new Color(0.28f, 0.29f, 0.33f), 0.8f, 0.55f); } }
    public static Material DarkIron { get { return Mat(new Color(0.19f, 0.19f, 0.22f), 0.6f, 0.4f); } }
    public static Material SocketDark { get { return Mat(new Color(0.10f, 0.10f, 0.12f), 0.6f, 0.35f); } }
    public static Material Rubber { get { return Mat(new Color(0.06f, 0.06f, 0.07f), 0f, 0.2f); } }
    public static Material HubMetal { get { return Mat(new Color(0.66f, 0.67f, 0.72f), 0.8f, 0.55f); } }
    public static Material CoreYellow { get { return Mat(new Color(0.95f, 0.74f, 0.10f), 0.5f, 0.5f); } }
    public static Material AmberGlow { get { return Emissive(new Color(1f, 0.62f, 0.12f), new Color(1.6f, 0.9f, 0.15f), 0f, 0.5f); } }
    public static Material CyanGlow { get { return Emissive(new Color(0.25f, 0.92f, 1f), new Color(0.2f, 1.8f, 2.2f), 0f, 0.5f); } }
    public static Material GreenGlow { get { return Emissive(new Color(0.35f, 1f, 0.45f), new Color(0.3f, 1.6f, 0.5f), 0f, 0.5f); } }
    public static Material HazardYellow { get { return Mat(new Color(0.95f, 0.76f, 0.12f), 0.2f, 0.45f); } }
    public static Material HazardBlack { get { return Mat(new Color(0.09f, 0.09f, 0.10f), 0.2f, 0.45f); } }
    public static Material Copper { get { return Mat(new Color(0.78f, 0.45f, 0.22f), 0.85f, 0.6f); } }
    public static Material TargetRed { get { return Mat(new Color(0.85f, 0.12f, 0.10f), 0.1f, 0.45f); } }
    public static Material TargetWhite { get { return Mat(new Color(0.92f, 0.92f, 0.90f), 0.1f, 0.45f); } }
    public static Material CeilingStrip { get { return Emissive(new Color(0.96f, 0.96f, 0.93f), new Color(2.4f, 2.4f, 2.25f), 0f, 0.5f); } }
    public static Material FloorMark { get { return Emissive(new Color(0.45f, 0.75f, 0.9f), new Color(0.22f, 0.45f, 0.58f), 0f, 0.3f); } }

    // ---------------------------------------------------------- prim helper

    /// <summary>Decorative primitive: collider destroyed immediately (stray
    /// colliders would break face-raycast placement, overlap checks, wheel
    /// suspension rays, and the physics compound). Public so environment
    /// dressing (workshop/arena) can use the same path.</summary>
    public static GameObject Deco(PrimitiveType type, Transform parent, Vector3 localPos,
                                  Vector3 localScale, Vector3 euler, Material m, string name)
    {
        var g = GameObject.CreatePrimitive(type);
        Object.Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(euler.x, euler.y, euler.z);
        g.transform.localScale = localScale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // ---------------------------------------------------------- part visuals

    /// <summary>Builds the full compound visual for part id (prefix-matched:
    /// "engine_3" → engine) into an unscaled root. size = effective world size
    /// (yaw already folded in by the caller). Unknown ids get a plain box in
    /// the fallback color, so Phase 0's hard-coded bots keep working.</summary>
    public static void BuildPart(string id, Transform root, Vector3 size, bool isCore, Color fallback)
    {
        Color accent = fallback; // socket-core tint source: the part's body color
        if (id.StartsWith("beam")) { Beam(root, size); accent = new Color(0.55f, 0.57f, 0.60f); }
        else if (id.StartsWith("plate")) { Plate(root, size); accent = new Color(0.55f, 0.57f, 0.60f); }
        else if (id.StartsWith("engine")) { Engine(root, size); accent = new Color(0.19f, 0.19f, 0.22f); }
        else if (id.StartsWith("battery")) { Battery(root, size); accent = new Color(0.12f, 0.17f, 0.14f); }
        else if (id.StartsWith("core")) { Core(root, size); accent = new Color(0.95f, 0.74f, 0.10f); }
        else if (id.StartsWith("head")) { DummyHead(root, size); return; } // rings, no sockets
        else if (id.StartsWith("base")) DummyBase(root, size);
        else if (id.StartsWith("pillar")) DummyPillar(root, size);
        else Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero,
                  Mat(fallback, 0.5f, 0.45f), "body");

        bool skipTop = id.StartsWith("core") || id.StartsWith("battery"); // dome/terminals live there
        Sockets(root, size, skipTop, accent);
    }

    /// <summary>Beam frame trick: designs are authored with the long axis on Z;
    /// if the effective size is longer in X (yaw 90), details go under a child
    /// frame rotated 90° about Y. The parent stays unscaled, so this is safe.</summary>
    static Transform LongAxisFrame(Transform root, ref Vector3 s)
    {
        if (s.x <= s.z) return root;
        var f = new GameObject("frame");
        f.transform.SetParent(root, false);
        f.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        s = new Vector3(s.z, s.y, s.x);
        return f.transform;
    }

    static void Beam(Transform root, Vector3 size)
    {
        Vector3 s = size;
        var f = LongAxisFrame(root, ref s);
        Deco(PrimitiveType.Cube, f, Vector3.zero, new Vector3(s.x, s.y, s.z * 0.94f), Vector3.zero, Structural, "beam_body");
        float capT = Mathf.Min(0.07f, s.z * 0.15f);
        Deco(PrimitiveType.Cube, f, new Vector3(0f, 0f, (s.z - capT) * 0.5f),
             new Vector3(s.x * 1.05f, s.y * 1.05f, capT), Vector3.zero, DarkSteel, "cap_f");
        Deco(PrimitiveType.Cube, f, new Vector3(0f, 0f, -(s.z - capT) * 0.5f),
             new Vector3(s.x * 1.05f, s.y * 1.05f, capT), Vector3.zero, DarkSteel, "cap_b");
        // Technic-hole language: dark through-cylinders on the side faces.
        for (int i = 0; i < 2; i++)
        {
            float z = (i == 0 ? -1f : 1f) * s.z * 0.26f;
            Deco(PrimitiveType.Cylinder, f, new Vector3(0f, 0f, z),
                 new Vector3(0.05f, s.x * 0.52f, 0.05f), new Vector3(0f, 0f, 90f), SocketDark, "inlay_" + i);
        }
    }

    static void Plate(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero, Structural, "plate_body");
        // Two raised stiffener ribs (leave the center clear for the socket boss).
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * size.x * 0.30f;
            Deco(PrimitiveType.Cube, root, new Vector3(x, size.y * 0.5f, 0f),
                 new Vector3(size.x * 0.22f, 0.02f, size.z * 0.78f), Vector3.zero, DarkSteel, "rib_" + i);
        }
        // Corner rivets.
        for (int i = 0; i < 4; i++)
        {
            float cx = (i % 2 == 0 ? -1f : 1f) * (size.x * 0.5f - 0.055f);
            float cz = (i < 2 ? -1f : 1f) * (size.z * 0.5f - 0.055f);
            Deco(PrimitiveType.Cylinder, root, new Vector3(cx, size.y * 0.5f, cz),
                 new Vector3(0.035f, 0.014f, 0.035f), Vector3.zero, SocketDark, "rivet_" + i);
        }
    }

    static void Engine(Transform root, Vector3 size)
    {
        Vector3 s = size;
        var f = LongAxisFrame(root, ref s);
        // Dark iron block, slightly dropped so pistons read on top.
        Deco(PrimitiveType.Cube, f, new Vector3(0f, -s.y * 0.06f, 0f),
             new Vector3(s.x, s.y * 0.88f, s.z), Vector3.zero, DarkIron, "block");
        // Three upright pistons along the long axis.
        for (int i = 0; i < 3; i++)
        {
            float z = (i - 1) * s.z * 0.26f;
            Deco(PrimitiveType.Cylinder, f, new Vector3(0f, s.y * 0.38f, z),
                 new Vector3(s.x * 0.30f, s.y * 0.22f, s.x * 0.30f), Vector3.zero, HubMetal, "piston_" + i);
        }
        // Angled exhaust stack at the rear corner.
        Deco(PrimitiveType.Cylinder, f, new Vector3(s.x * 0.28f, s.y * 0.42f, -s.z * 0.34f),
             new Vector3(s.x * 0.18f, s.y * 0.30f, s.x * 0.18f), new Vector3(-40f, 0f, 0f), DarkSteel, "exhaust");
        // Amber emissive strip wrapping both long side faces (kept low so the
        // side socket boss stays clear).
        Deco(PrimitiveType.Cube, f, new Vector3(0f, -s.y * 0.28f, 0f),
             new Vector3(s.x * 1.02f, s.y * 0.14f, s.z * 0.55f), Vector3.zero, AmberGlow, "glow_strip");
    }

    static void Core(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero, CoreYellow, "core_body");
        // Flattened sensor dome.
        Deco(PrimitiveType.Sphere, root, new Vector3(0f, size.y * 0.5f, 0f),
             new Vector3(size.x * 0.68f, size.y * 0.34f, size.z * 0.68f), Vector3.zero, DarkSteel, "dome");
        // Two cyan status lights on the front face.
        for (int i = 0; i < 2; i++)
        {
            float x = (i == 0 ? -1f : 1f) * size.x * 0.20f;
            Deco(PrimitiveType.Cube, root, new Vector3(x, size.y * 0.13f, size.z * 0.5f),
                 Vector3.one * 0.035f, Vector3.zero, CyanGlow, "status_" + i);
        }
    }

    static void Battery(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, new Vector3(0f, -size.y * 0.03f, 0f),
             new Vector3(size.x, size.y * 0.94f, size.z), Vector3.zero,
             Mat(new Color(0.12f, 0.17f, 0.14f), 0.3f, 0.4f), "cell");
        Deco(PrimitiveType.Cylinder, root, new Vector3(-size.x * 0.26f, size.y * 0.47f, 0f),
             new Vector3(0.05f, 0.03f, 0.05f), Vector3.zero, Copper, "terminal_pos");
        Deco(PrimitiveType.Cylinder, root, new Vector3(size.x * 0.26f, size.y * 0.47f, 0f),
             new Vector3(0.05f, 0.03f, 0.05f), Vector3.zero, HubMetal, "terminal_neg");
        // Green charge strip wrapping the sides.
        Deco(PrimitiveType.Cube, root, new Vector3(0f, -size.y * 0.2f, 0f),
             new Vector3(size.x * 1.02f, size.y * 0.16f, size.z * 0.55f), Vector3.zero, GreenGlow, "charge_strip");
    }

    // -------------------------------------------------- dummy (issue 6 color)

    static void DummyBase(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero, HazardYellow, "base_body");
        for (int i = 0; i < 2; i++)
        {
            float z = (i == 0 ? -1f : 1f) * size.z * 0.28f;
            Deco(PrimitiveType.Cube, root, new Vector3(0f, 0f, z),
                 new Vector3(size.x * 1.02f, size.y * 1.02f, size.z * 0.16f), Vector3.zero, HazardBlack, "stripe_" + i);
        }
    }

    static void DummyPillar(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero, HazardYellow, "pillar_body");
        for (int i = 0; i < 2; i++)
        {
            float y = (i == 0 ? -1f : 1f) * size.y * 0.25f;
            Deco(PrimitiveType.Cube, root, new Vector3(0f, y, 0f),
                 new Vector3(size.x * 1.05f, size.y * 0.14f, size.z * 1.05f), Vector3.zero, HazardBlack, "band_" + i);
        }
    }

    static void DummyHead(Transform root, Vector3 size)
    {
        Deco(PrimitiveType.Cube, root, Vector3.zero, size, Vector3.zero, TargetWhite, "head_body");
        // Red/white bullseye on both ±z faces (stacked flattened cylinders).
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            float z = sgn * size.z * 0.5f;
            Deco(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, z),
                 new Vector3(size.x * 0.82f, 0.006f, size.x * 0.82f), new Vector3(90f, 0f, 0f), TargetRed, "ring_o");
            Deco(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, z),
                 new Vector3(size.x * 0.54f, 0.010f, size.x * 0.54f), new Vector3(90f, 0f, 0f), TargetWhite, "ring_m");
            Deco(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, z),
                 new Vector3(size.x * 0.26f, 0.014f, size.x * 0.26f), new Vector3(90f, 0f, 0f), TargetRed, "ring_c");
        }
    }

    // ------------------------------------------------------- socket language

    /// <summary>Socket ports: small two-tone bosses centered on every
    /// attachable face — dark outer ring + inner disc tinted a LIGHTER shade
    /// of the part color, ~0.09 total diameter. Distinct from the (smaller,
    /// plain dark) decorative Technic holes: functional vs decorative reads
    /// at a glance.</summary>
    public static void Sockets(Transform root, Vector3 size, bool skipTop, Color accent)
    {
        Material core = Mat(Color.Lerp(accent, new Color(1f, 1f, 1f), 0.45f), 0.5f, 0.5f);
        for (int axis = 0; axis < 3; axis++)
        {
            int c1 = (axis + 1) % 3, c2 = (axis + 2) % 3;
            if (Mathf.Min(size[c1], size[c2]) < 0.15f) continue;
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                if (skipTop && axis == 1 && sgn == 1) continue;
                Vector3 pos = Vector3.zero;
                pos[axis] = sgn * size[axis] * 0.5f;
                Deco(PrimitiveType.Cylinder, root, pos, new Vector3(0.09f, 0.005f, 0.09f),
                     AxisEuler(axis), SocketDark, "socket_ring_" + axis + "_" + sgn);
                Deco(PrimitiveType.Cylinder, root, pos, new Vector3(0.055f, 0.009f, 0.055f),
                     AxisEuler(axis), core, "socket_core_" + axis + "_" + sgn);
            }
        }
    }

    static Vector3 AxisEuler(int axis)
    {
        if (axis == 0) return new Vector3(0f, 0f, 90f);
        if (axis == 2) return new Vector3(90f, 0f, 0f);
        return Vector3.zero;
    }

    /// <summary>Connector collar: 4 small dark bolt cylinders at the corners of
    /// the seam rectangle between two mated parts. Parented to partRoot (the
    /// newer part in the builder / part A in the arena), which must be unscaled
    /// and un-rotated in the space aCenter/bCenter are expressed in.</summary>
    public static void BuildCollar(Transform partRoot, Vector3 aCenter, Vector3 aHalf,
                                   Vector3 bCenter, Vector3 bHalf)
    {
        Vector3 d = bCenter - aCenter;
        int axis = 0;
        float best = Mathf.Abs(d.x);
        if (Mathf.Abs(d.y) > best) { axis = 1; best = Mathf.Abs(d.y); }
        if (Mathf.Abs(d.z) > best) { axis = 2; }
        float sign = d[axis] >= 0f ? 1f : -1f;
        float seam = aCenter[axis] + sign * aHalf[axis];

        int c1 = (axis + 1) % 3, c2 = (axis + 2) % 3;
        float lo1 = Mathf.Max(aCenter[c1] - aHalf[c1], bCenter[c1] - bHalf[c1]);
        float hi1 = Mathf.Min(aCenter[c1] + aHalf[c1], bCenter[c1] + bHalf[c1]);
        float lo2 = Mathf.Max(aCenter[c2] - aHalf[c2], bCenter[c2] - bHalf[c2]);
        float hi2 = Mathf.Min(aCenter[c2] + aHalf[c2], bCenter[c2] + bHalf[c2]);
        if (hi1 <= lo1 || hi2 <= lo2) return; // no shared face area

        float e1 = Mathf.Max((hi1 - lo1) * 0.5f - 0.045f, 0.02f);
        float e2 = Mathf.Max((hi2 - lo2) * 0.5f - 0.045f, 0.02f);

        for (int i = 0; i < 4; i++)
        {
            Vector3 p = Vector3.zero;
            p[axis] = seam;
            p[c1] = (lo1 + hi1) * 0.5f + (i % 2 == 0 ? -e1 : e1);
            p[c2] = (lo2 + hi2) * 0.5f + (i < 2 ? -e2 : e2);
            Deco(PrimitiveType.Cylinder, partRoot, p - aCenter,
                 new Vector3(0.045f, 0.02f, 0.045f), AxisEuler(axis), SocketDark, "bolt_" + i);
        }
    }

    // --------------------------------------------------------------- wheels

    /// <summary>Wheel: fat rubber tire + inset lighter hub (60% radius, a hair
    /// wider) + 5 lug bolts + visible axle. Local Y is the wheel axis.
    /// axleSide: -1 = axle toward local -Y (builder: body side), 0 = through-
    /// axle stubs both sides (arena, where the code doesn't track sides).</summary>
    public static void BuildWheel(Transform root, float radius, float width, int axleSide)
    {
        Deco(PrimitiveType.Cylinder, root, Vector3.zero,
             new Vector3(radius * 2f, width * 0.5f, radius * 2f), Vector3.zero, Rubber, "tire");
        Deco(PrimitiveType.Cylinder, root, Vector3.zero,
             new Vector3(radius * 1.2f, width * 0.5f * 1.12f, radius * 1.2f), Vector3.zero, HubMetal, "hub");
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f;
            Deco(PrimitiveType.Cylinder, root,
                 new Vector3(Mathf.Cos(a) * radius * 0.36f, 0f, Mathf.Sin(a) * radius * 0.36f),
                 new Vector3(radius * 0.16f, width * 0.5f * 1.3f, radius * 0.16f), Vector3.zero, SocketDark, "lug_" + i);
        }
        if (axleSide != 0)
        {
            float half = (radius - width * 0.5f) * 0.5f + 0.015f;
            float mid = axleSide * (radius + width * 0.5f) * 0.5f;
            Deco(PrimitiveType.Cylinder, root, new Vector3(0f, mid, 0f),
                 new Vector3(radius * 0.4f, half, radius * 0.4f), Vector3.zero, DarkSteel, "axle");
        }
        else
        {
            Deco(PrimitiveType.Cylinder, root, Vector3.zero,
                 new Vector3(radius * 0.4f, width * 0.5f + 0.04f, radius * 0.4f), Vector3.zero, DarkSteel, "axle");
        }
    }
}
