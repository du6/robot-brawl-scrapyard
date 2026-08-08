using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2B — floating damage numbers + hit flash (§7 readability), round-1
/// rework (critic fix 6: the old chips were tiny, overlapped, and vanished at
/// broadcast-camera distance; part RIPS showed nothing at all):
///   · rapid ticks on the SAME victim part within ~0.45 s merge into ONE
///     growing number instead of confetti;
///   · numbers carry a 4-way dark outline and scale with BOTH damage amount
///     and camera distance, so they stay legible from the fight camera;
///   · HP-destroyed parts render big and red (as before), and sheared-off
///     chunks (CompoundRobot.OnPartsLost) now get an emphasized orange
///     "PART RIPPED OFF" callout.
/// All spawned objects self-destroy; roots are named "dmg_*" so the
/// BackToBuild litter sweep catches any stragglers.
/// </summary>
public class FloatingDamageSpawner : MonoBehaviour
{
    /// <summary>Ticks on one part within this window merge into one number.</summary>
    public static float AGG_WINDOW = 0.45f;

    readonly Dictionary<object, FloatingDamage> agg = new Dictionary<object, FloatingDamage>();

    void OnEnable()
    {
        DamageResolver.OnHit += Spawn;
        CompoundRobot.OnPartsLost += SpawnRip;
    }

    void OnDisable()
    {
        DamageResolver.OnHit -= Spawn;
        CompoundRobot.OnPartsLost -= SpawnRip;
    }

    void Spawn(Vector3 pos, float amount, bool destroyed, object victimKey)
    {
        if (!destroyed && victimKey != null)
        {
            FloatingDamage prev;
            if (agg.TryGetValue(victimKey, out prev) && prev != null && prev.age < AGG_WINDOW)
            {
                prev.Add(amount);
                Flash(pos, 5f);
                return;
            }
        }
        var fd = FloatingDamage.Create(Declutter(pos), amount,
            destroyed ? FloatingDamage.Kind.Destroyed : FloatingDamage.Kind.Normal, null);
        if (!destroyed && victimKey != null) agg[victimKey] = fd;
        Flash(pos, destroyed ? 10f : 5f);
    }

    // Round-5 fix 6: aggregation is keyed on the VICTIM, so a mutual ram —
    // which damages both bodies at the same contact point in the same step —
    // produced two numbers at the same world position that rendered as one
    // illegible glyph pile. Numbers landing close in space and time now stack
    // upward instead of overprinting. w = spawn time.
    readonly List<Vector4> recentNums = new List<Vector4>();
    Vector3 Declutter(Vector3 pos)
    {
        for (int i = recentNums.Count - 1; i >= 0; i--)
            if (Time.time - recentNums[i].w > 0.3f) recentNums.RemoveAt(i);
        int stack = 0;
        foreach (var r in recentNums)
            if ((new Vector3(r.x, r.y, r.z) - pos).sqrMagnitude < 0.16f) stack++;
        recentNums.Add(new Vector4(pos.x, pos.y, pos.z, Time.time));
        return pos + Vector3.up * (0.42f * stack);
    }

    // Round-2 fix 4: rip callouts landing within ~1.5 s of each other stack
    // vertically instead of overprinting into an illegible blob.
    float lastRipTime = -99f;
    int ripChain;

    void SpawnRip(Vector3 pos, int count)
    {
        if (Time.time - lastRipTime < 1.5f) ripChain++; else ripChain = 0;
        lastRipTime = Time.time;
        Vector3 p = pos + Vector3.up * (0.55f * ripChain)
                  + new Vector3(Random.Range(-0.08f, 0.08f), 0f, 0f);
        FloatingDamage.Create(p, 0f, FloatingDamage.Kind.Ripped,
            count <= 1 ? "PART RIPPED OFF" : count + " PARTS RIPPED OFF");
        Flash(pos, 9f);
    }

    void Flash(Vector3 pos, float intensity)
    {
        var lgo = new GameObject("dmg_flash");
        lgo.transform.position = pos + Vector3.up * 0.25f;
        var l = lgo.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 2.5f;
        l.intensity = intensity;
        l.color = Color.white;
        lgo.AddComponent<HitFlash>();
    }
}

/// <summary>One rising, fading, outlined damage number. Static live counter
/// lets scripted tests verify spawn + self-destroy without scene scans.</summary>
public class FloatingDamage : MonoBehaviour
{
    public enum Kind { Normal, Destroyed, Ripped }

    public static int live;
    public static float LIFETIME = 1.2f;
    public static float RISE_SPEED = 0.8f;

    /// <summary>Seconds since spawn/refresh — the spawner's aggregation key.</summary>
    public float age;
    public float total;

    Kind kind;
    TextMesh tm;
    readonly List<TextMesh> layers = new List<TextMesh>();
    Color c0;

    public static FloatingDamage Create(Vector3 pos, float amount, Kind kind, string label)
    {
        var go = new GameObject("dmg_number");
        go.transform.position = pos + Vector3.up * 0.22f
            + new Vector3(Random.Range(-0.05f, 0.05f), 0f, Random.Range(-0.05f, 0.05f));
        var fd = go.AddComponent<FloatingDamage>();
        fd.kind = kind;
        fd.total = amount;
        fd.Build(label);
        return fd;
    }

    void Build(string label)
    {
        c0 = kind == Kind.Destroyed ? new Color(1f, 0.28f, 0.12f)
           : kind == Kind.Ripped ? new Color(1f, 0.55f, 0.12f)
           : new Color(1f, 0.88f, 0.22f);
        string txt = label != null ? label : Mathf.Max(1, Mathf.RoundToInt(total)).ToString();
        // Thick outline: four dark copies offset behind the face text.
        Vector2[] offs = { new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f) };
        foreach (var o in offs)
            layers.Add(MakeTM(new Vector3(o.x * 0.016f, o.y * 0.016f, 0.012f), new Color(0f, 0f, 0f, 0.95f), txt));
        tm = MakeTM(Vector3.zero, c0, txt);
        layers.Add(tm);
    }

    TextMesh MakeTM(Vector3 localOff, Color c, string txt)
    {
        var go = new GameObject("t");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localOff;
        var t = go.AddComponent<TextMesh>();
        t.text = txt;
        t.anchor = TextAnchor.MiddleCenter;
        t.fontSize = 64;
        t.characterSize = kind == Kind.Normal ? 0.045f : 0.06f;
        t.color = c;
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            t.font = font;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = font.material;
        }
        return t;
    }

    /// <summary>Aggregate another tick into this number: grow, refresh.</summary>
    public void Add(float amount)
    {
        total += amount;
        string txt = Mathf.Max(1, Mathf.RoundToInt(total)).ToString();
        foreach (var t in layers) if (t != null) t.text = txt;
        age = Mathf.Min(age, 0.15f);   // hold on screen while it's still growing
    }

    void Start() { live++; }
    void OnDestroy() { live--; }

    void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * RISE_SPEED * Time.deltaTime;
        var cam = Camera.main;
        float dist = 8f;
        if (cam != null)
        {
            Vector3 look = transform.position - cam.transform.position;
            dist = look.magnitude;
            if (look.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(look);
        }
        // Legibility at broadcast distance: scale with camera distance AND
        // with the damage the number represents.
        float dscale = Mathf.Clamp(dist / 7f, 0.9f, 2.6f);
        float grow = kind == Kind.Ripped ? 1.35f : 1f + Mathf.Min(total, 120f) * 0.006f;
        transform.localScale = Vector3.one * dscale * grow;

        float f = 1f - Mathf.Clamp(age / LIFETIME, 0f, 1f);
        foreach (var t in layers)
            if (t != null)
            {
                Color c = t.color;
                t.color = new Color(c.r, c.g, c.b, (t == tm ? 1f : 0.95f) * f);
            }
        if (age >= LIFETIME) Destroy(gameObject);
    }
}

/// <summary>The 0.12 s white hit-flash light: quick intensity decay, then gone.</summary>
public class HitFlash : MonoBehaviour
{
    public static float LIFETIME = 0.12f;
    float age;
    Light l;

    void Start() { l = GetComponent<Light>(); }

    void Update()
    {
        age += Time.deltaTime;
        if (l != null) l.intensity = l.intensity * Mathf.Max(0f, 1f - age / LIFETIME);
        if (age >= LIFETIME) Destroy(gameObject);
    }
}

}