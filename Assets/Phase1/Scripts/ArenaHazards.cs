using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>C3A - arena hazards (design doc section 4b). Every hazard deals
/// damage through DamageResolver.ApplyHit with a NULL attacker (the neutral
/// path: spawn protection, per-part immunity, HP, shear and SFX all apply,
/// and neither fighter is credited). Damaging hazards are placed in
/// point-symmetric pairs about the arena centre - fairness is validated in
/// code (ValidateMirrored), not by eye.</summary>
public abstract class HazardBase : MonoBehaviour
{
    public static readonly List<HazardBase> Active = new List<HazardBase>();
    static readonly Dictionary<CompoundRobot, float> envDmg = new Dictionary<CompoundRobot, float>();
    public float damageScale = 1f;
    /// <summary>AI repulsion radius; also the telegraph footprint.</summary>
    public float avoidRadius = 1.6f;
    /// <summary>True while this hazard can hurt right now (saw up, hammer
    /// striking, screw always). Pillars are never dangerous.</summary>
    public virtual bool Dangerous { get { return false; } }
    /// <summary>Harness seams: a strike that was never telegraphed is the
    /// one unfair thing a hazard can do.</summary>
    public float lastTelegraphAt = -1f, lastStrikeAt = -1f;
    public bool struckWithoutTelegraph;
    public int strikes;

    protected virtual void OnEnable() { Active.Add(this); }
    protected virtual void OnDisable() { Active.Remove(this); }

    public static void ResetTally() { envDmg.Clear(); }
    public static float EnvDamage(CompoundRobot bot)
    { float t; return bot != null && envDmg.TryGetValue(bot, out t) ? t : 0f; }

    /// <summary>Neutral-attacker hit through the standard pipeline.</summary>
    protected void HitPart(Collider other, float impulse, float hardness)
    {
        var bot = other.GetComponentInParent<CompoundRobot>();
        if (bot == null || bot.dead) return;
        int idx = bot.PartIndexOf(other);
        if (idx < 0) return;
        if (lastTelegraphAt < 0f) struckWithoutTelegraph = true;
        lastStrikeAt = Time.time;
        strikes++;
        float before = bot.damageTaken;
        DamageResolver.ApplyHit(null, bot, idx, impulse * damageScale, hardness,
                                other.bounds.center, DamageResolver.SRC_RAM);
        float d = bot.damageTaken - before;
        if (d > 0f) { float t; envDmg.TryGetValue(bot, out t); envDmg[bot] = t + d; }
    }

    /// <summary>Make a renderer read as MACHINED METAL rather than pale
    /// plastic. owen, 2026-08-02: the risen saw blade "should look like real
    /// vertical saws" - it was rendering as a white blob. Cause: URP/Lit from
    /// code comes up at metallic 0 / smoothness 0.5, so a 0.78-albedo steel is
    /// a bright diffuse lump with no specular shape. Real steel is DARK with a
    /// tight highlight: the brightness is reflection, not albedo. Dropping
    /// albedo and pushing metallic to 1 is what makes a blade read as a blade.</summary>
    protected static void Metal(GameObject go, float smooth)
    {
        if (go == null) return;
        var m = go.GetComponent<Renderer>();
        if (m == null) return;
        var mat = m.material;
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 1f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth);
    }

    protected static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Color c, bool collider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (!collider) Object.Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null) r.material = new Material(sh);
        r.material.color = c;
        return go;
    }
}

/// <summary>R4 (critic finding 1). Shared telegraph vocabulary for every
/// DAMAGING hazard. The round-4 critic's diagnosis was that the two phases
/// which looked most different were the two SAFE ones (full red field vs bare
/// floor) while "about to cut" and "cutting now" differed only by a pale grey
/// puck - i.e. the loudest channel on screen carried the wrong bit. Three
/// layers now carry three different bits:
///
///   pad   - PERMANENT dark-amber floor plate, sized to the DAMAGE TRIGGER and
///           never hidden. Dangerous ground is geography you can learn before
///           it ever touches you (doc section 4b, "learnable rhythm"). The old
///           code hid the footprint for 45-62% of every cycle.
///   warn  - a quad above the pad that exists ONLY while a strike is coming.
///           It FILLS (0.40 -> 1.00 of the pad) and HEATS (amber -> red) across
///           the telegraph, so "about to strike" is a visibly progressing
///           state, not a binary.
///   mech  - the machine itself: blade standing proud of the floor / hammer
///           head on the plate, plus a red tint on the striking surface, so
///           "striking NOW" is a silhouette change you cannot miss.
///
/// COLOUR LAW: amber = warning, red = live damage, steel = machine. Nothing
/// else in a hazard may be orange-red (the old hammer arm and screw drum both
/// were, which is why red stopped meaning anything).
///
/// DETERMINISM: every animated value is a pure function of Time.time and the
/// hazard's phase. Nothing integrates Time.deltaTime any more - the round-3
/// capture trap (a Time.deltaTime lerp cannot move in a frozen frame, so
/// telegraph and strike photographed byte-identical) is designed out.</summary>
public abstract class TelegraphHazard : HazardBase
{
    public const string Stamp = "r4-h1";

    protected GameObject pad, warn;
    protected Renderer warnRend;
    protected Vector3 warnFull;

    protected static readonly Color PAD_AMBER    = new Color(0.34f, 0.19f, 0.03f, 1f);
    protected static readonly Color PAD_INNER    = new Color(0.09f, 0.06f, 0.03f, 1f);
    protected static readonly Color WARN_AMBER   = new Color(1f, 0.72f, 0.12f, 1f);
    protected static readonly Color WARN_RED     = new Color(1f, 0.14f, 0.04f, 1f);
    protected static readonly Color STEEL        = new Color(0.34f, 0.35f, 0.39f, 1f);
    protected static readonly Color STEEL_DARK   = new Color(0.13f, 0.14f, 0.16f, 1f);
    /// <summary>Saw plate and tooth tips. Deliberately DARK: with metallic 1
    /// the brightness comes from the specular highlight, so the blade catches
    /// the light as it spins instead of sitting there as a white cut-out.</summary>
    protected static readonly Color BLADE_STEEL  = new Color(0.20f, 0.21f, 0.24f, 1f);
    protected static readonly Color TOOTH_STEEL  = new Color(0.62f, 0.63f, 0.66f, 1f);
    protected static readonly Color STEEL_BRIGHT = new Color(0.78f, 0.80f, 0.84f, 1f);

    // Floor stack, bottom to top. Kept far enough apart that no two plates
    // z-fight and the marking still reads as flush from a 35-degree camera.
    protected const float Y_PAD = 0.008f, Y_INNER = 0.021f, Y_WARN = 0.034f, Y_TRIM = 0.047f;

    /// <summary>Lit + EMISSIVE, not unlit. A telegraph that dims with the room
    /// is not a telegraph, so these plates carry their own light - but
    /// URP/Unlit materials built from code come up without their keyword state
    /// and render as flat cyan garbage (measured: r4dev1_SAFE showed every
    /// floor plate cyan). URP/Lit is the path the rest of this file has always
    /// used and it is the one that works. Emission gives the same "does not
    /// dim" property and reads as the glow section 4b actually asks for.</summary>
    protected static GameObject Flat(Transform parent, Vector3 pos, Vector3 scale, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        Object.Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null) r.material = new Material(sh);
        SetFlat(r, c);
        return go;
    }

    /// <summary>Base colour AND emission together, so a plate reads the same
    /// in the Yard's flat light and the Crucible's dark.</summary>
    protected static void SetFlat(Renderer r, Color c) { SetFlat(r, c, 0.85f); }
    protected static void SetFlat(Renderer r, Color c, float emis)
    {
        if (r == null) return;
        var m = r.material;
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * emis);
    }

    /// <summary>sx/sz are the DAMAGE TRIGGER's footprint. Art follows the
    /// volume, never the other way round - the screw shipped a trigger 80%
    /// wider than its drum with no floor mark at all.</summary>
    protected void BuildFootprint(float cx, float cz, float sx, float sz)
    {
        pad = Flat(transform, new Vector3(cx, Y_PAD, cz), new Vector3(sx, 0.014f, sz), PAD_AMBER);
        Flat(transform, new Vector3(cx, Y_INNER, cz), new Vector3(sx * 0.88f, 0.012f, sz * 0.88f), PAD_INNER);
        warnFull = new Vector3(sx * 0.80f, 0.012f, sz * 0.80f);
        warn = Flat(transform, new Vector3(cx, Y_WARN, cz), warnFull, WARN_AMBER);
        warnRend = warn.GetComponent<Renderer>();
        warn.SetActive(false);
    }

    // Harness seams. A visual claim that cannot be measured in the same tick
    // as the screenshot is an assertion, and this loop does not accept those.
    public bool WarnOn { get { return warn != null && warn.activeSelf; } }
    public Vector3 WarnScale { get { return warn != null ? warn.transform.localScale : Vector3.zero; } }
    public Color WarnColor { get { return warnRend != null ? warnRend.material.color : Color.clear; } }
    public bool PadOn { get { return pad != null && pad.activeSelf; } }

    /// <summary>k = 0..1 progress through the telegraph. live = the strike
    /// window (full size, red, strobing).</summary>
    protected void SetWarn(bool show, float k, bool live, float strobeHz)
    {
        if (warn == null) return;
        if (warn.activeSelf != show) warn.SetActive(show);
        if (!show) return;
        k = Mathf.Clamp01(k);
        float f = live ? 1f : Mathf.Lerp(0.40f, 1f, k);
        warn.transform.localScale = new Vector3(warnFull.x * f, warnFull.y, warnFull.z * f);
        // The strike pulse rides the EMISSION, not the base colour. Lerping
        // the base toward white made the live plate read PALE PINK, which is
        // exactly the confusion the colour law exists to prevent: live damage
        // has to stay RED (measured on r4dev2_STRIKE).
        Color c = live ? WARN_RED : Color.Lerp(WARN_AMBER, WARN_RED, k);
        float emis = 0.85f;
        if (live)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * strobeHz * 6.28318f);
            emis = 0.9f + 1.5f * pulse;
        }
        SetFlat(warnRend, c, emis);
    }
}

/// <summary>Floor saw. SAFE: amber plate + dark slot, no blade. TELEGRAPH:
/// the warn quad fills amber->red inside the plate. STRIKE: a toothed steel
/// disc stands ON EDGE through the slot over a solid strobing red field.
/// The disc used to lie FLAT and spin about its symmetry axis, which is
/// invisible on an untextured solid - it read as a manhole cover.</summary>
public class FloorSaw : TelegraphHazard
{
    public float period = 4f, telegraph = 1.0f, upTime = 1.2f, phase;
    Transform bladePivot;
    bool risen;
    public override bool Dangerous { get { return risen; } }

    const float DOWN_Y = -0.72f, UP_Y = 0.34f, RISE = 0.16f, FALL = 0.30f;
    public float BladeY { get { return bladePivot != null ? bladePivot.localPosition.y : 0f; } }

    public void Build()
    {
        // The trigger is authored FIRST and the art is sized to it.
        var trig = gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(1.3f, 1.0f, 1.3f);
        trig.center = new Vector3(0f, 0.4f, 0f);
        BuildFootprint(0f, 0f, 1.3f, 1.3f);
        // The slot sits ON TOP of the warn quad so it is legible in every
        // phase: even at full red you can see where the blade comes out.
        Flat(transform, new Vector3(0f, Y_TRIM, 0f), new Vector3(1.16f, 0.010f, 0.24f), new Color(0.012f, 0.012f, 0.016f, 1f));

        var pv = new GameObject("blade");
        pv.transform.SetParent(transform, false);
        pv.transform.localPosition = new Vector3(0f, DOWN_Y, 0f);
        bladePivot = pv.transform;
        // disc plane = XY, i.e. standing on edge across the slot and FACING the
        // arena camera (which sits on -Z): a floor saw has to read as a circle,
        // and edge-on it would be a line.
        // A REAL CIRCULAR SAW (owen, 2026-08-02). It was a pale smooth puck
        // with ten small nubs, which at arena distance is a white disc. What
        // makes a saw blade legible is not brightness, it is TOOTH SILHOUETTE
        // against a dark plate - so the plate goes dark and metallic and the
        // teeth get big, angled and alternating, the way a real rip blade does.
        var disc = Prim(PrimitiveType.Cylinder, bladePivot, Vector3.zero,
                        new Vector3(0.98f, 0.040f, 0.98f), BLADE_STEEL, false);
        Metal(disc, 0.72f);

        // 16 canted teeth. Each is rotated about the disc axis so it rakes
        // forward like a cutting tooth instead of sitting square like a bolt.
        const int TEETH = 16;
        for (int i = 0; i < TEETH; i++)
        {
            float a = i * Mathf.PI * 2f / TEETH;
            float deg = -a * Mathf.Rad2Deg;
            var tooth = Prim(PrimitiveType.Cube, bladePivot,
                 new Vector3(Mathf.Cos(a) * 0.50f, 0f, Mathf.Sin(a) * 0.50f),
                 new Vector3(0.17f, 0.052f, 0.115f),
                 (i % 2 == 0) ? TOOTH_STEEL : BLADE_STEEL, false);
            // rake: point the long edge around the rim, then cant it 18 deg
            tooth.transform.localRotation = Quaternion.Euler(0f, deg + 18f, 0f);
            Metal(tooth, 0.80f);
        }
        // Arbor hub + collar, so the centre reads as a driven shaft.
        var hub = Prim(PrimitiveType.Cylinder, bladePivot, Vector3.zero,
                       new Vector3(0.30f, 0.070f, 0.30f), STEEL, false);
        Metal(hub, 0.45f);
        var nut = Prim(PrimitiveType.Cylinder, bladePivot, Vector3.zero,
                       new Vector3(0.13f, 0.090f, 0.13f), STEEL_DARK, false);
        Metal(nut, 0.35f);

        // Housing: the blade now emerges from a machine bolted under the deck
        // rather than floating up out of a painted line.
        var hsgL = Prim(PrimitiveType.Cube, transform, new Vector3(-0.62f, 0.055f, 0f),
                        new Vector3(0.16f, 0.11f, 0.40f), STEEL, false);
        var hsgR = Prim(PrimitiveType.Cube, transform, new Vector3(0.62f, 0.055f, 0f),
                        new Vector3(0.16f, 0.11f, 0.40f), STEEL, false);
        Metal(hsgL, 0.35f); Metal(hsgR, 0.35f);
        Pose(0f);
    }

    void Update()
    {
        float t = Mathf.Repeat(Time.time + phase, period);
        bool tel = t < telegraph;
        risen = t >= telegraph && t < telegraph + upTime;
        if (tel) lastTelegraphAt = Time.time;
        SetWarn(tel || risen, tel ? t / Mathf.Max(0.01f, telegraph) : 1f, risen, 5f);
        Pose(t);
    }

    /// <summary>Pure function of cycle time - no deltaTime integration, so the
    /// blade is where the phase says it is even on the first frame after a
    /// pause, a timeScale change or a screenshot.</summary>
    void Pose(float t)
    {
        if (bladePivot == null) return;
        float y;
        if (t < telegraph) y = DOWN_Y;
        else if (t < telegraph + upTime)
            y = Mathf.Lerp(DOWN_Y, UP_Y, Mathf.Clamp01((t - telegraph) / Mathf.Min(RISE, upTime * 0.5f)));
        else
            y = Mathf.Lerp(UP_Y, DOWN_Y, Mathf.Clamp01((t - telegraph - upTime) / FALL));
        bladePivot.localPosition = new Vector3(0f, y, 0f);
        float spin = Mathf.Repeat(Time.time * 720f, 360f);
        bladePivot.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, spin, 0f);
    }

    void OnTriggerStay(Collider other) { if (risen) HitPart(other, 320f, 1.3f); }
}

/// <summary>Pulverizer hammer. SAFE: amber plate, steel arm resting level
/// ABOVE its own plate so cause and effect are co-located. COCK: the arm
/// rears to 65 degrees while the warn quad fills. STRIKE: the head lands ON
/// the plate (it used to stop a metre short of and a metre above it) and goes
/// red. The arm is now hinged INTO the post instead of floating beside it.</summary>
public class Hammer : TelegraphHazard
{
    public float period = 5f, cockTime = 1.4f, strikeTime = 0.5f, recoverTime = 0.9f, phase;
    Transform armPivot;
    Renderer headRend;
    bool striking;
    public override bool Dangerous { get { return striking; } }

    // Geometry solved so the head centre lands at (y 0.36, z 0.75) - the exact
    // centre of the damage trigger - at HIT degrees. r = 1.70, pivot y = 1.45.
    const float REST = 0f, COCK = -65f, HIT = 40f;
    public float ArmAngle { get { return armPivot != null ? armPivot.localEulerAngles.x : 0f; } }
    Transform headTf;
    /// <summary>Was armPivot.GetChild(1), which broke the moment the arm grew
    /// a haunch and web plates in the 2026-08-02 art pass - it started
    /// reporting the haunch's position, and every harness reading it would have
    /// been quietly wrong. Cached by reference, not by sibling index.</summary>
    public Vector3 HeadPos { get { return headTf != null ? headTf.position : Vector3.zero; } }

    public void Build()
    {
        var trig = gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(1.2f, 1.2f, 1.4f);
        trig.center = new Vector3(0f, 0.5f, 0.75f);
        BuildFootprint(0f, 0.75f, 1.2f, 1.4f);
        // anvil trim: the plate reads as a machined bed even when safe
        Flat(transform, new Vector3(0f, Y_TRIM, 0.75f), new Vector3(1.24f, 0.010f, 0.10f), new Color(0.012f, 0.012f, 0.016f, 1f));

        Prim(PrimitiveType.Cube, transform, new Vector3(0f, 0.75f, -0.55f),
             new Vector3(0.44f, 1.5f, 0.44f), new Color(0.30f, 0.31f, 0.34f, 1f), true);

        var pv = new GameObject("arm");
        pv.transform.SetParent(transform, false);
        pv.transform.localPosition = new Vector3(0f, 1.45f, -0.55f);   // inside the post: no gap
        armPivot = pv.transform;
        // A REAL PULVERIZER (owen, 2026-08-02). It was a thin bar with a plain
        // box on the end - a lollipop. A hammer reads as a hammer because of
        // three things this had none of: a HEAVY head that is visibly wider
        // than its arm, a distinct STRIKING FACE, and an arm that thickens
        // toward the hinge because that is where the moment is.
        var arm = Prim(PrimitiveType.Cube, armPivot, new Vector3(0f, 0f, 0.78f),
                       new Vector3(0.26f, 0.20f, 1.50f), STEEL, false);
        Metal(arm, 0.30f);
        // haunch: the arm is thickest at the pivot and tapers out
        var haunch = Prim(PrimitiveType.Cube, armPivot, new Vector3(0f, 0f, 0.34f),
                          new Vector3(0.38f, 0.34f, 0.66f), STEEL, false);
        Metal(haunch, 0.30f);
        // web plates either side, like a fabricated steel arm
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            var web = Prim(PrimitiveType.Cube, armPivot, new Vector3(sgn * 0.17f, 0f, 0.86f),
                           new Vector3(0.05f, 0.30f, 1.30f), STEEL_DARK, false);
            Metal(web, 0.25f);
        }

        // The head: a heavy block across the arm, not a cube on a stick.
        var head = Prim(PrimitiveType.Cube, armPivot, new Vector3(0f, 0f, 1.78f),
                        new Vector3(0.86f, 0.58f, 1.06f), STEEL_DARK, false);
        head.name = "head";
        headTf = head.transform;
        Metal(head, 0.35f);
        // cheeks: the head is banded, so it reads as forged mass
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            var cheek = Prim(PrimitiveType.Cube, armPivot, new Vector3(sgn * 0.45f, 0f, 1.78f),
                             new Vector3(0.09f, 0.50f, 0.94f), STEEL, false);
            Metal(cheek, 0.40f);
        }
        // STRIKING FACE: a chisel edge running across the swing, pointing at
        // the plate. Rotated 45 deg about X so its cross-section is a diamond
        // in YZ - the edge itself lies along X, which is the direction the head
        // travels through. This is the part that tells you which way it hits.
        var edge = Prim(PrimitiveType.Cube, armPivot, new Vector3(0f, -0.32f, 1.78f),
                        new Vector3(0.90f, 0.16f, 0.16f), TOOTH_STEEL, false);
        edge.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
        Metal(edge, 0.75f);
        // back peen, so the head is not symmetric and the strike face is obvious
        var peen = Prim(PrimitiveType.Cube, armPivot, new Vector3(0f, 0.10f, 2.42f),
                        new Vector3(0.42f, 0.30f, 0.26f), STEEL, false);
        Metal(peen, 0.40f);
        headRend = head.GetComponent<Renderer>();
        var collar = Prim(PrimitiveType.Cylinder, armPivot, Vector3.zero, new Vector3(0.34f, 0.30f, 0.34f), new Color(0.19f, 0.20f, 0.23f, 1f), false);
        collar.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        Pose(0f);
    }

    void Update()
    {
        float t = Mathf.Repeat(Time.time + phase, period);
        bool cocking = t < cockTime;
        striking = t >= cockTime && t < cockTime + strikeTime;
        if (cocking) lastTelegraphAt = Time.time;
        SetWarn(cocking || striking, cocking ? t / Mathf.Max(0.01f, cockTime) : 1f, striking, 5f);
        Pose(t);
        if (headRend != null) headRend.material.color = striking ? WARN_RED : STEEL_DARK;
    }

    void Pose(float t)
    {
        if (armPivot == null) return;
        float ang;
        if (t < cockTime)
            ang = Mathf.Lerp(REST, COCK, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, cockTime)));
        else if (t < cockTime + strikeTime)
            ang = Mathf.Lerp(COCK, HIT, Mathf.Clamp01((t - cockTime) / Mathf.Max(0.01f, strikeTime)));
        else
            ang = Mathf.Lerp(HIT, REST, Mathf.Clamp01((t - cockTime - strikeTime) / Mathf.Max(0.01f, recoverTime)));
        armPivot.localRotation = Quaternion.Euler(ang, 0f, 0f);
    }

    void OnTriggerStay(Collider other) { if (striking) HitPart(other, 520f, 1.1f); }
}

/// <summary>Perimeter screw: a slow constant push-drag along one wall, with
/// chip damage. Always live, so it telegraphs by EXISTING - which means it
/// needs a permanent floor mark, which it did not have. The drum is now the
/// same size as the damage trigger (it was 44% of its width and 56% of its
/// height, so the thing that hurt you was invisible) and the strip beneath it
/// holds red and breathes, because for this hazard there is no safe phase.</summary>
public class PerimeterScrew : TelegraphHazard
{
    public Vector3 dragDir = Vector3.forward;
    Transform drumPivot;
    public Bounds DrumBounds { get { var r = drumPivot != null ? drumPivot.GetComponentInChildren<Renderer>() : null; return r != null ? r.bounds : new Bounds(); } }
    public override bool Dangerous { get { return true; } }

    public void Build(float length)
    {
        lastTelegraphAt = 0f;   // permanently visible = permanently telegraphed
        var trig = gameObject.AddComponent<BoxCollider>();
        trig.isTrigger = true;
        trig.size = new Vector3(0.9f, 0.9f, length);
        trig.center = new Vector3(0f, 0.45f, 0f);
        BuildFootprint(0f, 0f, 0.9f, length);

        var pv = new GameObject("drum");
        pv.transform.SetParent(transform, false);
        pv.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        drumPivot = pv.transform;
        // diameter 0.9 and length `length` == the trigger, exactly
        Prim(PrimitiveType.Cylinder, drumPivot, Vector3.zero, new Vector3(0.9f, length * 0.5f, 0.9f), new Color(0.27f, 0.28f, 0.31f, 1f), false);
        // The floor strip under a 0.9 m drum is occluded by the drum from every
        // angle but one, so the screw's "permanently live" signal has to live
        // on the DRUM: emissive red/amber flutes that visibly turn.
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f;
            var fl = Prim(PrimitiveType.Cube, drumPivot,
                 new Vector3(Mathf.Cos(a) * 0.40f, 0f, Mathf.Sin(a) * 0.40f),
                 new Vector3(0.13f, length * 0.49f, 0.13f),
                 (i == 1) ? WARN_AMBER : WARN_RED, false);
            SetFlat(fl.GetComponent<Renderer>(), (i == 1) ? WARN_AMBER : WARN_RED, 0.9f);
        }
        Pose();
    }

    void Update() { Pose(); SetWarn(true, 1f, true, 0.8f); }

    void Pose()
    {
        if (drumPivot == null) return;
        drumPivot.localRotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, Mathf.Repeat(Time.time * 240f, 360f), 0f);
    }

    void OnTriggerStay(Collider other)
    {
        var rb = other.attachedRigidbody;
        if (rb != null && !rb.isKinematic) rb.AddForce(transform.TransformDirection(dragDir) * 220f, ForceMode.Force);
        HitPart(other, 70f, 1.0f);
    }
}

public static class ArenaHazards
{
    static GameObject root;

    public static string Summary(string arenaId)
    {
        switch (arenaId)
        {
            case "yard":     return "pillars";
            case "dock":     return "twin wall screws";
            case "sawmill":  return "2 floor saws";
            case "press":    return "2 pulverizer hammers";
            case "crucible": return "2 saws + 2 hammers";
            default:          return "no hazards";
        }
    }

    public static void Clear()
    {
        if (root != null) Object.Destroy(root);
        root = null;
        HazardBase.ResetTally();
    }

    static T Mk<T>(string name, Vector3 pos, float yaw, float scale, float phase) where T : HazardBase
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        var h = go.AddComponent<T>();
        h.damageScale = scale;
        var saw = h as FloorSaw; if (saw != null) saw.phase = phase;
        var ham = h as Hammer;   if (ham != null) ham.phase = phase;
        return h;
    }

    /// <summary>Damaging hazards are authored as HALF layouts and mirrored
    /// through the origin by construction; ValidateMirrored re-checks the
    /// spawned scene so a layout typo cannot ship an unfair arena.</summary>
    public static void Build(string arenaId, float half)
    {
        Clear();
        root = new GameObject("arena_hazards");
        switch (arenaId)
        {
            case "yard":
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var pil = new GameObject("pillar");
                    pil.transform.SetParent(root.transform, false);
                    pil.transform.localPosition = new Vector3(sgn * 2.6f, 0f, 0f);
                    var pb = pil.AddComponent<PillarHazard>();
                    pb.Build();
                }
                break;
            case "dock":
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var sc = Mk<PerimeterScrew>("screw", new Vector3(sgn * (half - 0.55f), 0f, 0f), 0f, 0.5f, 0f);
                    sc.dragDir = new Vector3(0f, 0f, sgn);   // opposite drags = point symmetric
                    sc.Build(half * 1.6f);
                }
                break;
            case "sawmill":
                foreach (var sgn in new[] { 1f, -1f })
                    Mk<FloorSaw>("saw", new Vector3(sgn * 2.3f, 0f, 0f), 0f, 0.5f, sgn > 0f ? 0f : 2f).Build();   // C5 tune: 0.7 -> 0.5
                break;
            case "press":
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var hm = Mk<Hammer>("hammer", new Vector3(sgn * 2.5f, 0f, 0f), sgn > 0f ? -90f : 90f, 0.55f, sgn > 0f ? 0f : 2.5f);   // C5 tune: 0.8 -> 0.55
                    hm.Build();
                }
                break;
            case "crucible":
                foreach (var sgn in new[] { 1f, -1f })
                    Mk<FloorSaw>("saw", new Vector3(sgn * 2.1f, 0f, sgn * 2.1f), 0f, 0.7f, sgn > 0f ? 0f : 2f).Build();   // C5 tune: 1.0 -> 0.7
                foreach (var sgn in new[] { 1f, -1f })
                {
                    var hm = Mk<Hammer>("hammer", new Vector3(sgn * 2.4f, 0f, sgn * -2.4f), sgn > 0f ? -90f : 90f, 0.7f, sgn > 0f ? 1f : 3.5f);   // C5 tune: 1.0 -> 0.7
                    hm.Build();
                }
                break;
        }
    }

    /// <summary>Every DAMAGING hazard must have a partner at -position
    /// (point symmetry about the centre). Pillars must too - obstacles are
    /// also fairness. True when the spawned layout is fair.</summary>
    public static bool ValidateMirrored()
    {
        if (root == null) return true;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            var a = root.transform.GetChild(i);
            bool ok = false;
            for (int j = 0; j < root.transform.childCount; j++)
            {
                if (i == j) continue;
                var b = root.transform.GetChild(j);
                if ((a.localPosition + b.localPosition).magnitude < 0.01f) { ok = true; break; }
            }
            if (!ok) return false;
        }
        return true;
    }

    /// <summary>AI repulsion query: the nearest hazard that is dangerous (or
    /// about to be) within `range`; away = horizontal unit vector out.</summary>
    public static bool Near(Vector3 pos, float range, out Vector3 away)
    {
        away = Vector3.zero;
        float best = range;
        foreach (var h in HazardBase.Active)
        {
            if (h == null) continue;
            if (!h.Dangerous && Time.time - h.lastTelegraphAt > 0.2f) continue;
            Vector3 d = pos - h.transform.position; d.y = 0f;
            if (d.magnitude < best) { best = d.magnitude; away = d.normalized; }
        }
        return away != Vector3.zero;
    }
}

/// <summary>Static pillar: an obstacle, never a damager. Real collider.</summary>
public class PillarHazard : HazardBase
{
    public override bool Dangerous { get { return false; } }
    public void Build()
    {
        // ⚠ THE COLLIDER IS ADDED EXPLICITLY, AND THAT IS A FIX, NOT A STYLE
        // CHOICE — 2026-08-10. Read this before "simplifying" it back to
        // Prim(..., collider: true).
        //
        // GameObject.CreatePrimitive(PrimitiveType.Cylinder) attaches a
        // CAPSULECOLLIDER — the cylinder is the odd primitive out (Cube→Box,
        // Sphere→Sphere, Plane→Mesh). So this pillar's collider used to arrive
        // implicitly, and NO managed code anywhere named the type. With
        // stripEngineCode: 1 the engine stripper therefore dropped the class,
        // and the built player could not create one:
        //
        //     Can't add component because class 'CapsuleCollider' doesn't exist!
        //
        // Measured in the shipped Xcode export rather than guessed —
        // build/ios/Il2CppOutputProject/Source/il2cppOutput/UnityClassRegistration.cpp
        // registers BoxCollider, Collider, MeshCollider and SphereCollider
        // under "Physics" and NOT CapsuleCollider. Device and simulator builds
        // both. The other hazards survive because their damage volumes are
        // explicit AddComponent<BoxCollider>() calls, and BoxCollider is named
        // in managed code.
        //
        // WHAT IT COST: this is the ONLY cylinder in the project that keeps its
        // collider (every other one is decoration and destroys it), so on
        // device the pillars were NOT SOLID and robots drove through them —
        // silently, with no exception beyond the log spam. The yard is L1
        // "Scrapyard Open", the first league in the game, and the league card
        // advertises its hazard as "pillars": the app promised an obstacle it
        // did not have, in the first fights every new player sees.
        //
        // WHY NO BENCH CAUGHT IT: HazardBench is green at 23/23 in the EDITOR,
        // and the editor does not strip. This class of defect is invisible to
        // every bench in this project by construction — it can only be seen in
        // a built player.
        //
        // THE FIX LIVES IN Assets/link.xml, which preserves
        // UnityEngine.CapsuleCollider so the stripper cannot drop it. This call
        // is therefore UNCHANGED — `collider: true` still means "the primitive's
        // own capsule is this pillar's real collider".
        //
        // ⚠ AN EXPLICIT AddComponent<CapsuleCollider>() HERE WAS TRIED FIRST AND
        // IS WORSE. It is the obvious fix — a managed reference to the type,
        // right where the bug bites — and it was measured rather than assumed,
        // which is the only reason these two faults were found:
        //
        //   1. THE DEFAULTS DO NOT MATCH. Measured in the editor against a
        //      primitive-supplied capsule:
        //          primitive-supplied    r=0.5 h=2 dir=1  bounds (0.60, 1.00, 0.60)
        //          AddComponent default  r=0.5 h=1 dir=1  bounds (0.60, 0.60, 0.60)
        //      CreatePrimitive's capsule is height 2; a bare AddComponent is
        //      height 1. Taking the defaults gives the pillar a collider 60% of
        //      its own height — a quieter version of this same bug, and one that
        //      looks perfectly correct in every screenshot.
        //   2. IT NEEDS `collider: false`, AND THAT RELIES ON Object.Destroy.
        //      Destroy is deferred to end of frame in play mode and is a NO-OP in
        //      edit mode, so the pillar carries TWO overlapping capsules for a
        //      frame at runtime and permanently under any edit-mode caller —
        //      measured, 2 colliders per pillar, 4 across the yard.
        //
        // link.xml has neither problem: the primitive keeps supplying a capsule
        // that is correctly sized by construction and cannot drift from the mesh,
        // there is exactly one collider in every mode, and preservation is the
        // documented mechanism rather than a reliance on the linker noticing a
        // type reference. The cost is that the reason lives in another file,
        // which is what this comment is for. link.xml names this line.
        Prim(PrimitiveType.Cylinder, transform, new Vector3(0f, 0.5f, 0f),
             new Vector3(0.6f, 0.5f, 0.6f), new Color(0.45f, 0.42f, 0.38f, 1f), true);
    }
}
}
