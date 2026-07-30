using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// THE VISUAL LAYER — the findings five straight implementation rounds declined.
///
/// Four independent critic agents, across five rounds and roughly three hundred
/// matches, raised the same three things and no dev ever took them, because a
/// sweep can measure a damage number and cannot measure "I could not tell which
/// robot was mine". The evidence is not opinion:
///
///  * OWNERSHIP. Two separate critics, looking at their own live captures, could
///    not identify their own machine. Both robots are grey-blue hulls with the
///    same yellow accent bar, the same black wheels and the same green engine
///    face. One reported "I identified mine only by 'it has an arm'".
///  * MOTION. A self-built rotor was captured on SIX CONSECUTIVE PHYSICS STEPS
///    at 10.00 rad/s (12.5 m/s at the tip, confirmed by telemetry in the same
///    frame) and rendered as a rigid cross snapped to six discrete angles. A
///    hammer captured mid-swing with fire held looks exactly like a parked
///    hammer. There is no blur, trail, arc ghost or wind-up tell anywhere.
///  * DAMAGE. At 500+ points dealt, both machines render pristine — no dent, no
///    scorch, no mark of any kind. Damage exists only as a HUD number.
///
/// The sharpest single piece of evidence in the whole loop: a critic MISREAD ITS
/// OWN SCREENSHOT, reporting a blade as detached and lying on the floor, then
/// checked and found 14/14 parts attached and the arm perfectly assembled. An
/// intact limb reads as a broken one.
///
/// Everything here is additive and self-contained: one Install() call per robot,
/// no change to any existing visual, no collider, no physics, no gameplay value
/// read or written. It cannot alter a match outcome.
/// </summary>
public static class RobotVisuals
{
    /// <summary>Cyan for the player, warm orange for the opponent. Picked to stay
    /// separable for the commonest colour-vision deficiencies (they differ in
    /// blue AND in luminance, not just red-vs-green), and to avoid the yellow
    /// the hazard stripes already use and the green the engine face uses.</summary>
    public static Color PLAYER_COL = new Color(0.20f, 0.85f, 1f);
    public static Color ENEMY_COL  = new Color(1f, 0.45f, 0.12f);

    public static void Install(CompoundRobot r, bool isPlayer)
    {
        if (r == null) return;
        Color c = isPlayer ? PLAYER_COL : ENEMY_COL;

        if (r.GetComponent<TeamMark>() == null)
            r.gameObject.AddComponent<TeamMark>().Init(r, c, isPlayer);
        if (r.GetComponent<BattleScars>() == null)
            r.gameObject.AddComponent<BattleScars>().Init(r);

        InstallMotionCues(r);
    }

    /// <summary>Put a smear on everything that can move under its own power: each
    /// driven limb part, and each spinner disc. Deliberately driven by MEASURED
    /// motion rather than by asking the Actuator how fast it thinks it is going,
    /// so it cannot disagree with what is on screen — which is the entire bug.</summary>
    public static void InstallMotionCues(CompoundRobot r)
    {
        foreach (var act in r.GetComponentsInChildren<Actuator>(true))
        {
            if (act.limb == null) continue;
            foreach (int i in act.limb)
            {
                if (i < 0 || i >= r.parts.Count) continue;
                var p = r.parts[i];
                if (p.detached || p.go == null) continue;
                if (p.go.GetComponent<MotionTrail>() != null) continue;
                Vector3 s = p.spec.size;
                // Emit from the part's outermost corner: that point moves under
                // rotation as well as translation, so a limb that only spins
                // still draws an arc.
                p.go.AddComponent<MotionTrail>().Init(new Vector3(s.x, s.y, s.z) * 0.5f, WeaponEdge(p.spec.id));
            }
        }

        foreach (var sp in r.GetComponentsInChildren<SpinnerWeapon>(true))
        {
            Transform disc = null;
            foreach (var t in sp.GetComponentsInChildren<Transform>(true))
                if (t.name == "spin_disc") { disc = t; break; }
            if (disc == null || disc.GetComponent<MotionTrail>() != null) continue;
            int idx = sp.partIdx;
            float rad = 0.2f;
            if (idx >= 0 && idx < r.parts.Count)
            {
                Vector3 s = r.parts[idx].spec.size;
                rad = Mathf.Max(s.x, Mathf.Max(s.y, s.z)) * 0.5f;
            }
            // Rim point, in the disc's own frame: the disc spins about local Y.
            disc.gameObject.AddComponent<MotionTrail>().Init(new Vector3(rad, 0f, 0f), true);
        }
    }

    static bool WeaponEdge(string id)
    {
        return id.StartsWith("blade") || id.StartsWith("wedge") || id.StartsWith("hook")
            || id.StartsWith("spike") || id.StartsWith("spinner");
    }

    /// <summary>Unlit transparent material for trails and markers. Sprites/Default
    /// exists under both URP and built-in and needs no keywords, which matters
    /// because this project runs on whichever pipeline the machine resolves.</summary>
    public static Material UnlitTint(Color c)
    {
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        var m = new Material(sh);
        m.color = c;
        return m;
    }
}

// ===========================================================================

/// <summary>
/// Whose robot is this. A ground ring plus a floating diamond, both in the side's
/// colour.
///
/// Deliberately NOT a tint on the hull. Hull tinting is what the game already
/// does, and it is exactly what failed: material colour is legible on the build
/// screen and dies at fight distance, and it shifts four to five shades between
/// camera angles (a critic measured Tungsten reading near-black in FRONT and pale
/// PINK in TOP). A marker that lives OUTSIDE the machine is immune to all of
/// that, and to the machine being upside down.
///
/// The ring is unparented and kept flat on the deck, so it survives a flip - the
/// state where you most need to find your robot.
/// </summary>
public class TeamMark : MonoBehaviour
{
    public static float RING_RADIUS = 0.95f;
    public static int RING_SEGMENTS = 24;
    public static float MARK_HEIGHT = 1.35f;

    CompoundRobot robot;
    Transform ring, diamond;
    float spin;

    public void Init(CompoundRobot r, Color c, bool isPlayer)
    {
        robot = r;
        var mat = RobotVisuals.UnlitTint(c);

        var ringGo = new GameObject("team_ring");
        ring = ringGo.transform;
        for (int i = 0; i < RING_SEGMENTS; i++)
        {
            // A dashed ring: every other segment is left out, so it reads as a
            // marker rather than as a piece of arena geometry.
            if (i % 2 == 1) continue;
            float a = i * Mathf.PI * 2f / RING_SEGMENTS;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(seg.GetComponent<Collider>());
            seg.name = "seg";
            seg.transform.SetParent(ring, false);
            seg.transform.localPosition = new Vector3(Mathf.Cos(a) * RING_RADIUS, 0f, Mathf.Sin(a) * RING_RADIUS);
            seg.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            seg.transform.localScale = new Vector3(0.05f, 0.012f, 0.20f);
            seg.GetComponent<Renderer>().sharedMaterial = mat;
        }

        var dGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(dGo.GetComponent<Collider>());
        dGo.name = "team_diamond";
        diamond = dGo.transform;
        diamond.localScale = new Vector3(0.16f, 0.16f, 0.16f);
        dGo.GetComponent<Renderer>().sharedMaterial = mat;

        CompoundRobot.Spawned.Add(ringGo);
        CompoundRobot.Spawned.Add(dGo);
    }

    void LateUpdate()
    {
        if (robot == null || robot.rb == null)
        {
            if (ring != null) Object.Destroy(ring.gameObject);
            if (diamond != null) Object.Destroy(diamond.gameObject);
            Object.Destroy(this);
            return;
        }
        Vector3 p = robot.transform.position;
        if (ring != null)
        {
            // Flat on the deck and never rotated with the body: a flipped robot
            // still sits inside its own ring.
            ring.SetPositionAndRotation(new Vector3(p.x, 0.015f, p.z), Quaternion.identity);
        }
        if (diamond != null)
        {
            spin += Time.deltaTime * 90f;
            diamond.SetPositionAndRotation(
                new Vector3(p.x, p.y + MARK_HEIGHT, p.z),
                Quaternion.Euler(45f, spin, 0f));
        }
    }
}

// ===========================================================================

/// <summary>
/// A smear behind anything moving fast enough to be a weapon.
///
/// It measures its own emitter point's WORLD displacement per frame instead of
/// reading Actuator.rate. That is on purpose: the whole failure being fixed is
/// that the telemetry said 10.00 rad/s while the screen showed a parked cross,
/// so a cue derived from the telemetry could reproduce the same disagreement. A
/// cue derived from where the pixels actually were cannot.
///
/// The emitter sits at the part's outer corner, so pure rotation produces a
/// trail too - which is the rotor case, and the one that failed worst.
/// </summary>
public class MotionTrail : MonoBehaviour
{
    /// <summary>Below this the part is manoeuvring, not attacking; no smear. A
    /// robot driving at 5 m/s must not leave weapon trails on its own hull.</summary>
    public static float MIN_SPEED = 3.0f;
    /// <summary>Speed at which the smear is at full width and opacity.</summary>
    public static float FULL_SPEED = 11.0f;
    /// 0.16 s rather than 0.13: at the rotor speeds this game actually reaches
    /// (16.19 rad/s measured live) a 0.13 s tail closes only ~120 deg of arc, so
    /// the smear reads as a bar rather than as a disc of revolution.
    public static float TRAIL_TIME = 0.16f;
    public static float MAX_WIDTH = 0.13f;
    /// Peak opacity. The first pass ran at 0.85 and the smear rendered as a
    /// SOLID tan lozenge - it looked like a part, not like speed, which is the
    /// exact failure this whole component exists to fix. Motion has to read as
    /// something you can see through.
    public static float PEAK_ALPHA = 0.5f;

    Transform emitter;
    TrailRenderer tr;
    Vector3 lastPos;
    bool primed;
    Color baseCol;

    public void Init(Vector3 localTip, bool isEdge)
    {
        var go = new GameObject("motion_emitter");
        emitter = go.transform;
        emitter.SetParent(transform, false);
        emitter.localPosition = localTip;

        // Near-white for an edge, cool grey for plain structure. The first pass
        // used a warm cream (1, 0.93, 0.72) which sat in the same hue band as
        // the hazard-yellow trim and the brass hub, so the smear was mistakable
        // for bodywork. White-hot is the one colour nothing on the machine wears.
        baseCol = isEdge ? new Color(1f, 0.98f, 0.94f) : new Color(0.82f, 0.86f, 0.92f);

        tr = go.AddComponent<TrailRenderer>();
        tr.time = TRAIL_TIME;
        tr.minVertexDistance = 0.02f;
        tr.autodestruct = false;
        tr.emitting = false;
        tr.numCapVertices = 2;
        tr.alignment = LineAlignment.View;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.sharedMaterial = RobotVisuals.UnlitTint(baseCol);
        tr.widthMultiplier = MAX_WIDTH;

        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(baseCol, 0f), new GradientColorKey(baseCol, 1f) },
            new[] { new GradientAlphaKey(PEAK_ALPHA, 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;

        lastPos = emitter.position;
        primed = false;
    }

    void LateUpdate()
    {
        if (tr == null || emitter == null) return;
        float dt = Time.deltaTime;
        if (dt <= 1e-5f) return;

        Vector3 now = emitter.position;
        if (!primed) { lastPos = now; primed = true; return; }
        float speed = (now - lastPos).magnitude / dt;
        lastPos = now;

        if (speed < MIN_SPEED)
        {
            tr.emitting = false;
            return;
        }
        float f = Mathf.Clamp01((speed - MIN_SPEED) / Mathf.Max(0.01f, FULL_SPEED - MIN_SPEED));
        tr.emitting = true;
        tr.widthMultiplier = MAX_WIDTH * (0.35f + 0.65f * f);
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(baseCol, 0f), new GradientColorKey(baseCol, 1f) },
            new[] { new GradientAlphaKey(PEAK_ALPHA * (0.35f + 0.65f * f), 0f), new GradientAlphaKey(0f, 1f) });
        tr.colorGradient = grad;
    }
}

// ===========================================================================

/// <summary>
/// Damage you can see on the machine.
///
/// Two layers, because they answer two different questions:
///  * a per-part DARKENING driven by hp/maxHp answers "how beaten up is that
///    bit", continuously, from any angle and at any distance;
///  * persistent SCORCH MARKS at the actual impact points answer "where has it
///    been hit", and accumulate so a long fight leaves a history.
///
/// Tinting goes through a MaterialPropertyBlock and NOT through the material.
/// PartVisualFactory.Mat() is cached by colour key, so every part sharing a
/// colour shares one Material instance - across both robots. Writing to
/// sharedMaterial here would scorch the opponent's hull, and the arena, whenever
/// anything took a hit. A property block is per-renderer and allocates nothing.
///
/// Capped at CHAR_MAX so a part can never go fully black: material identity has
/// to survive, since the build screen sells it as a choice.
/// </summary>
public class BattleScars : MonoBehaviour
{
    public static float CHAR_MAX = 0.72f;
    public static Color CHAR_COL = new Color(0.10f, 0.09f, 0.08f);
    public static float REFRESH_S = 0.15f;
    public static int MAX_MARKS_PER_PART = 5;
    public static float MARK_MIN_DMG = 12f;

    CompoundRobot robot;
    MaterialPropertyBlock mpb;
    readonly Dictionary<int, int> markCount = new Dictionary<int, int>();
    readonly Dictionary<int, float> shown = new Dictionary<int, float>();
    float nextRefresh;

    public void Init(CompoundRobot r)
    {
        robot = r;
        mpb = new MaterialPropertyBlock();
        DamageResolver.OnHit += OnHit;
    }

    void OnDestroy() { DamageResolver.OnHit -= OnHit; }

    void OnHit(Vector3 worldPos, float dmg, bool destroyed, object victimPart)
    {
        if (robot == null || dmg < MARK_MIN_DMG) return;
        var part = victimPart as CompoundRobot.Part;
        if (part == null) return;
        int idx = -1;
        for (int i = 0; i < robot.parts.Count; i++)
            if (robot.parts[i] == part) { idx = i; break; }
        if (idx < 0) return;                       // the other robot's hit
        if (part.detached || part.go == null) return;

        int n;
        markCount.TryGetValue(idx, out n);
        if (n >= MAX_MARKS_PER_PART) return;
        markCount[idx] = n + 1;

        var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(mark.GetComponent<Collider>());
        mark.name = "scorch";
        mark.transform.SetParent(part.go.transform, true);
        mark.transform.position = worldPos;
        // Lie the mark flat against the surface it hit by pointing it back at
        // the part's centre; a slightly random roll stops repeats looking stamped.
        Vector3 outward = worldPos - part.go.transform.position;
        if (outward.sqrMagnitude < 1e-5f) outward = Vector3.up;
        mark.transform.rotation = Quaternion.LookRotation(outward.normalized)
                                * Quaternion.Euler(0f, 0f, (idx * 37 + n * 61) % 180);
        float s = Mathf.Clamp(0.06f + dmg * 0.0012f, 0.07f, 0.22f);
        mark.transform.localScale = new Vector3(s, s * 0.75f, 0.015f);
        mark.GetComponent<Renderer>().sharedMaterial =
            RobotVisuals.UnlitTint(new Color(0.06f, 0.05f, 0.05f, 0.9f));
    }

    void LateUpdate()
    {
        if (robot == null) { Object.Destroy(this); return; }
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + REFRESH_S;

        for (int i = 0; i < robot.parts.Count; i++)
        {
            var p = robot.parts[i];
            if (p.go == null || p.maxHp <= 0f) continue;
            float frac = Mathf.Clamp01(p.hp / p.maxHp);
            // Front-loaded on purpose. Linear darkening put a part at 80% HP -
            // which has visibly been in a fight - at 0.14 of full char, i.e.
            // invisible at fight distance. The 0.55 exponent puts that same part
            // at 0.29, so the FIRST real hit shows, and the last ones still have
            // somewhere to go.
            float want = Mathf.Pow(1f - frac, 0.55f) * CHAR_MAX;
            float have;
            if (shown.TryGetValue(i, out have) && Mathf.Abs(have - want) < 0.02f) continue;
            shown[i] = want;
            if (want <= 0.001f) continue;

            foreach (var rend in p.go.GetComponentsInChildren<Renderer>(true))
            {
                if (rend is TrailRenderer) continue;
                if (rend.sharedMaterial == null) continue;
                rend.GetPropertyBlock(mpb);
                Color baseCol = rend.sharedMaterial.color;
                Color c = Color.Lerp(baseCol, CHAR_COL, want);
                // Written under BOTH names: URP Lit reads _BaseColor, built-in
                // Standard reads _Color, and a property block ignores a name the
                // shader does not have.
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                rend.SetPropertyBlock(mpb);
            }
        }
    }
}

}
