using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spec for one hard-coded part. Phase 0 skips ScriptableObjects on purpose
/// (design doc §12, Phase 0) — parts are code-defined boxes.
/// </summary>
public struct PartSpec
{
    public string id;
    public Vector3 size;      // meters
    public Vector3 localPos;  // relative to robot root
    public string mat;        // key into MatDB
    public bool isCore;       // the KO / connectivity anchor part

    public PartSpec(string id, Vector3 size, Vector3 localPos, string mat, bool isCore = false)
    {
        this.id = id; this.size = size; this.localPos = localPos; this.mat = mat; this.isCore = isCore;
    }
}

/// <summary>
/// THE Phase 0 system under test (design doc §5.2–5.3).
///
/// The fused structure is ONE compound Rigidbody (child box colliders, no
/// physics joints between structural parts). Connections are "virtual joints"
/// in a graph: each fixed step we take the engine's contact impulses on the
/// compound body, attribute stress to nearby connections with a distance
/// falloff, and when a connection's stress exceeds its threshold we break it
/// and dynamically split the body — severed parts re-spawn as their own
/// rigidbodies inheriting velocity (linear + ω×r), and the survivor's
/// mass/center-of-mass are recomputed (so losing a chunk changes handling).
/// </summary>
public class CompoundRobot : MonoBehaviour
{
    // ------------------------------------------------------------------ tuning
    // Static (not const) so scripted tuning passes can iterate live; values
    // below are the round-2 MEASURED calibration (see dev notes): a ~5-6 m/s
    // cruise ram into the dummy costs at most one peripheral part, a ~10 m/s
    // full-throttle wall crash still shears convincingly.
    // Impulse threshold (N·s) per unit of relative material strength.
    // THE key Phase 0 tuning knob: lower = parts tear off easier.
    public static float BREAK_K = 1500f;
    // How sharply stress attribution falls off with distance (m) from a
    // contact point to a connection midpoint. Higher = more localized damage.
    public static float STRESS_FALLOFF = 6f;
    // Ignore tiny contact impulses (rolling, resting contact).
    public static float MIN_IMPULSE = 40f;

    public class Part
    {
        public PartSpec spec;
        public GameObject go;
        public float mass;
        public MatDef mat;
        public bool detached;
    }

    public class Edge
    {
        public int a, b;
        public float threshold;
        public bool broken;
        public float peak;      // max stress ever seen (for the HUD / tuning)
        public string label;
    }

    public Rigidbody rb;
    public List<Part> parts = new List<Part>();
    public List<Edge> edges = new List<Edge>();
    public int coreIndex;

    // Mass carried by the body that is NOT a structural part (Phase 1: the
    // raycast wheels — they have no colliders/parts of their own but their
    // mass must load the suspension, the HUD, and the CoM). Survives
    // RecomputeMass so part breaks don't erase it.
    public float extraMass;
    public Vector3 extraMassLocalCoM = Vector3.zero;

    public static System.Action<string> Log = delegate { };
    public static readonly List<GameObject> Spawned = new List<GameObject>();

    // ---- diagnosis telemetry (scripted tests only; off by default) ----
    public static bool telemetryOn = false;
    public static int telemetryHits;      // impulses >= MIN_IMPULSE
    public static int telemetrySubMin;    // impulses filtered out
    public static float telemetryMaxJ;
    public static float telemetrySumJ;
    public static string telemetryMaxInfo = "";
    public static void TelemetryReset()
    { telemetryHits = 0; telemetrySubMin = 0; telemetryMaxJ = 0f; telemetrySumJ = 0f; telemetryMaxInfo = ""; }

    readonly List<Edge> pendingBreaks = new List<Edge>();

    // ------------------------------------------------------------------ build

    public struct Conn
    {
        public int a, b;
        public Conn(int a, int b) { this.a = a; this.b = b; }
    }

    public static CompoundRobot Build(string name, PartSpec[] specs, Conn[] conns,
                                      Vector3 pos, Quaternion rot)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(pos, rot);
        Spawned.Add(root);

        var rb = root.AddComponent<Rigidbody>();
        var robot = root.AddComponent<CompoundRobot>();
        robot.rb = rb;

        for (int i = 0; i < specs.Length; i++)
        {
            var s = specs[i];
            var mat = MatDB.Get(s.mat);
            // Unscaled part root with an explicit BoxCollider (physically
            // identical to the old scaled cube primitive); the full compound
            // visual is built by the SAME factory the builder uses, so parts
            // keep their identity in the arena. Decorative children carry no
            // colliders and travel with the part GameObject when it becomes
            // debris.
            var go = new GameObject(s.id);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = s.localPos;
            var box = go.AddComponent<BoxCollider>();
            box.size = s.size;
            PartVisualFactory.BuildPart(s.id, go.transform, s.size, s.isCore,
                s.isCore ? Color.Lerp(mat.color, Color.yellow, 0.5f) : mat.color);

            var part = new Part { spec = s, go = go, mat = mat, mass = MatDB.MassOf(s.size, mat) };
            robot.parts.Add(part);
            if (s.isCore) robot.coreIndex = i;
        }

        // Connector collars on every connection seam (visual only).
        foreach (var c in conns)
            PartVisualFactory.BuildCollar(robot.parts[c.a].go.transform,
                specs[c.a].localPos, specs[c.a].size * 0.5f,
                specs[c.b].localPos, specs[c.b].size * 0.5f);

        foreach (var c in conns)
        {
            robot.edges.Add(new Edge
            {
                a = c.a,
                b = c.b,
                // Break threshold = the WEAKER of the two mated parts (§5.2).
                threshold = Mathf.Min(robot.parts[c.a].mat.strengthRel,
                                      robot.parts[c.b].mat.strengthRel) * BREAK_K,
                label = specs[c.a].id + "↔" + specs[c.b].id,
            });
        }

        // Solver survival kit basics (§6.4).
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 50f;

        robot.RecomputeMass();
        return robot;
    }

    /// <summary>
    /// §4.4: per-part masses aggregate into the body's mass and center of mass.
    /// We set mass and CoM explicitly from part masses (Unity's automatic CoM
    /// assumes uniform density across colliders, which would ignore materials).
    /// Inertia tensor stays engine-computed from colliders — a known Phase 0
    /// approximation, good enough to prove the loop.
    /// </summary>
    public void RecomputeMass()
    {
        float total = 0f;
        Vector3 weighted = Vector3.zero;
        foreach (var p in parts)
        {
            if (p.detached) continue;
            total += p.mass;
            weighted += p.mass * p.go.transform.localPosition;
        }
        if (total <= 0f) { Destroy(gameObject); return; }
        rb.mass = total + extraMass;
        rb.centerOfMass = (weighted + extraMass * extraMassLocalCoM) / (total + extraMass);
        rb.ResetInertiaTensor();
    }

    public float ActiveMass()
    {
        float t = 0f;
        foreach (var p in parts) if (!p.detached) t += p.mass;
        return t + extraMass;
    }

    // --------------------------------------------------- stress from contacts

    void OnCollisionEnter(Collision c) { Accumulate(c); }
    void OnCollisionStay(Collision c) { Accumulate(c); }

    void Accumulate(Collision c)
    {
        float J = c.impulse.magnitude;
        if (telemetryOn)
        {
            telemetrySumJ += J;
            if (J < MIN_IMPULSE) telemetrySubMin++; else telemetryHits++;
            if (J > telemetryMaxJ)
            {
                telemetryMaxJ = J;
                telemetryMaxInfo = name + " vs " + (c.collider != null ? c.collider.name : "?")
                                 + " J=" + J.ToString("F0");
            }
        }
        if (J < MIN_IMPULSE) return;

        // Average contact point is representative enough for box parts.
        Vector3 p = Vector3.zero;
        int n = c.contactCount;
        if (n == 0) return;
        for (int i = 0; i < n; i++) p += c.GetContact(i).point;
        p /= n;

        foreach (var e in edges)
        {
            if (e.broken) continue;
            Vector3 jointPos = 0.5f * (parts[e.a].go.transform.position +
                                       parts[e.b].go.transform.position);
            float d = Vector3.Distance(p, jointPos);
            float stress = J / (1f + STRESS_FALLOFF * d * d);
            if (stress > e.peak) e.peak = stress;
            if (stress > e.threshold)
            {
                e.broken = true;
                pendingBreaks.Add(e);
            }
        }
    }

    void FixedUpdate()
    {
        if (pendingBreaks.Count == 0) return;
        foreach (var e in pendingBreaks)
            Log(name + ": connection BROKE — " + e.label +
                "  (stress " + e.peak.ToString("F0") + " > " + e.threshold.ToString("F0") + " N·s)");
        pendingBreaks.Clear();
        RebuildIslands();
    }

    // ------------------------------------------------------ dynamic splitting

    /// <summary>
    /// §5.3 steps 2–4: connectivity from the core part; any subgraph no longer
    /// connected is severed and re-spawned as its own rigidbody with inherited
    /// velocity; the survivor's mass/CoM are recomputed.
    /// </summary>
    void RebuildIslands()
    {
        var reachable = FloodFrom(coreIndex, null);

        // Group severed parts into connected components.
        var severed = new HashSet<int>();
        for (int i = 0; i < parts.Count; i++)
            if (!parts[i].detached && !reachable.Contains(i)) severed.Add(i);
        if (severed.Count == 0) { RecomputeMass(); return; }

        var claimed = new HashSet<int>();
        foreach (int seed in severed)
        {
            if (claimed.Contains(seed)) continue;
            var component = FloodFrom(seed, severed);
            foreach (int i in component) claimed.Add(i);
            SpawnDebris(component);
        }
        RecomputeMass();
        Log(name + ": lost " + claimed.Count + " part(s) — mass now " +
            ActiveMass().ToString("F0") + " kg, handling will change.");
    }

    HashSet<int> FloodFrom(int start, HashSet<int> restrictTo)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<int>();
        if (!parts[start].detached) { stack.Push(start); seen.Add(start); }
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            foreach (var e in edges)
            {
                if (e.broken) continue;
                int other = e.a == cur ? e.b : (e.b == cur ? e.a : -1);
                if (other < 0 || seen.Contains(other) || parts[other].detached) continue;
                if (restrictTo != null && !restrictTo.Contains(other)) continue;
                seen.Add(other);
                stack.Push(other);
            }
        }
        return seen;
    }

    void SpawnDebris(HashSet<int> component)
    {
        // World CoM of the severed chunk.
        float total = 0f;
        Vector3 worldCoM = Vector3.zero;
        foreach (int i in component)
        {
            total += parts[i].mass;
            worldCoM += parts[i].mass * parts[i].go.transform.position;
        }
        worldCoM /= total;

        // Capture inherited motion BEFORE touching the hierarchy (§5.3 step 3):
        // linear velocity at the chunk's CoM (= v + ω×r) plus the body's ω.
        Vector3 vel = rb.GetPointVelocity(worldCoM);
        Vector3 angVel = rb.angularVelocity;

        var go = new GameObject(name + "_debris");
        go.transform.SetPositionAndRotation(worldCoM, transform.rotation);
        Spawned.Add(go);
        var drb = go.AddComponent<Rigidbody>();

        foreach (int i in component)
        {
            parts[i].detached = true;
            parts[i].go.transform.SetParent(go.transform, true); // keep world pose
        }

        // Mass properties of the debris body from its parts.
        float t = 0f; Vector3 weighted = Vector3.zero;
        foreach (int i in component)
        {
            t += parts[i].mass;
            weighted += parts[i].mass * parts[i].go.transform.localPosition;
        }
        drb.mass = t;
        drb.centerOfMass = weighted / t;
        drb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        drb.maxAngularVelocity = 50f;
        VelUtil.SetLinearVelocity(drb, vel);
        drb.angularVelocity = angVel;
    }

    public static void ClearAll()
    {
        foreach (var go in Spawned)
        {
            if (go == null) continue;
            // Deactivate first: Destroy is deferred to end of frame, and a
            // respawn in the same frame would overlap the old colliders and
            // cause a depenetration explosion that breaks every joint.
            go.SetActive(false);
            Destroy(go);
        }
        Spawned.Clear();
    }
}

/// <summary>Rigidbody.velocity was renamed linearVelocity in Unity 6.</summary>
public static class VelUtil
{
    public static void SetLinearVelocity(Rigidbody r, Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        r.linearVelocity = v;
#else
        r.velocity = v;
#endif
    }

    public static Vector3 GetLinearVelocity(Rigidbody r)
    {
#if UNITY_6000_0_OR_NEWER
        return r.linearVelocity;
#else
        return r.velocity;
#endif
    }
}
