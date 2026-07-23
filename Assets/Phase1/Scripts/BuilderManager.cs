using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Phase 1 — Assembly MVP (design doc §12 Phase 1, §5).
///
/// Snap-together build mode: click a part in the palette, click a face of the
/// robot to attach it (magnetic-LEGO face snapping — the §5.1 socket model,
/// simplified to axis-aligned faces for the MVP). Live consequences (§5.4):
/// mass/cost/part totals, the center-of-mass marker with the wheel support
/// polygon (green = stable, yellow = marginal, red = it WILL tip), and
/// validation gates. One tap drops the build into a physics sandbox with the
/// Phase 0 ram dummy — same CompoundRobot virtual joints, so a badly built bot
/// really does shed parts.
///
/// Build: click face place · R rotate · right-click remove · Q/E orbit ·
/// scroll zoom · T test.  Test: WASD drive · R reset · B back to builder.
/// </summary>
public class BuilderManager : MonoBehaviour
{
    public enum Mode { Build, Test }

    public class PlacedPart
    {
        public P1PartDef def;
        public GameObject go;
        public Vector3 pos;        // world center (build space)
        public int yaw;            // 0 or 90 — swaps x/z of size
        public Vector3 wheelAxis;  // wheels only: outward face normal

        public Vector3 Half()
        {
            Vector3 s = def.size;
            if (def.category == P1Category.Mobility)
            {
                // Orientation-aware wheel extents (critic fix): a wheel is a
                // squat cylinder — half WIDTH along its axle axis, radius in
                // the two tangent axes. Using the raw def.size box pushed
                // ±Z-face wheels 0.11 m off their mount and made the ghost
                // overlap check clip neighbors. wheelAxis unset (bare probes)
                // → assume an X axle.
                int ax = 0;
                float bx = Mathf.Abs(wheelAxis.x);
                if (Mathf.Abs(wheelAxis.y) > bx) { ax = 1; bx = Mathf.Abs(wheelAxis.y); }
                if (Mathf.Abs(wheelAxis.z) > bx) ax = 2;
                Vector3 h = Vector3.one * (s.x * 0.5f); // radius
                h[ax] = s.y * 0.5f;                     // half width
                return h;
            }
            if (yaw == 90) s = new Vector3(s.z, s.y, s.x);
            return s * 0.5f;
        }
    }

    const float SNAP = 0.1f;
    const float PANEL_W = 280f;

    public Mode mode = Mode.Build;
    public readonly List<PlacedPart> placed = new List<PlacedPart>();

    P1PartDef[] palette;
    int selected = -1;
    int ghostYaw;
    GameObject ghost;                 // the REAL compound part model, mouse-following
    string ghostBuiltKey = "";        // partId_yaw the current ghost was built for
    readonly List<Material> ghostMats = new List<Material>();
    bool ghostTintValid = true;
    bool ghostValid;
    string ghostReason = "";          // why the ghost is red (panel hint)
    PlacedPart ghostTarget;
    Vector3 ghostPos, ghostNormal;
    GUIStyle headStyle, btnStyle, descStyle, bodyStyle, warnStyle, bigStyle;

    // Stability readout (build mode): support-polygon margin from RefreshOverlay.
    float supportMargin = -1f;
    int wheelCount;
    // Round-3 fixes: wheels that can't reach the ground don't support anything,
    // and wheels whose roll directions disagree fight each other.
    int floatingWheels;
    bool mixedRoll;
    // Arena HUD mass (sum of the SAME per-part rounded values the builder shows).
    int hudWheelMassInt;

    // Test-mode wreck/recovery HUD state (public: scripted tests read them).
    int lastTestParts;
    public float shearTimer;
    public string shearText = "";
    public bool wrecked;
    // Beached-bot feedback: throttle held but the bot isn't moving.
    public float stuckTimer;
    public bool stuck;

    GameObject buildRoot, sandboxRoot;
    Transform comMarker, comLine, driveArrow;
    Vector3 driveDir = Vector3.forward;   // canonical drive direction, derived from wheels
    readonly List<Transform> polyEdges = new List<Transform>();
    readonly Dictionary<Collider, PlacedPart> byCollider = new Dictionary<Collider, PlacedPart>();

    Camera cam;
    float orbitYaw = 35f, orbitDist = 4.5f;

    CompoundRobot testRobot;
    RaycastWheelDrive testDrive;
    FollowCamera followCam;
    string message = "";

    // ------------------------------------------------------------------ setup

    void Start()
    {
        Application.runInBackground = true;
        palette = P1PartDef.Palette();
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        var oldFollow = cam.GetComponent<FollowCamera>();
        if (oldFollow != null) Destroy(oldFollow);

        BuildRoom();

        // Seed the build with the core — the KO target and connectivity anchor.
        AddPart(palette[0], new Vector3(0f, 0.7f, 0f), 0, Vector3.zero);
        RefreshOverlay();
    }

    void BuildRoom()
    {
        buildRoot = new GameObject("build_room");

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "builder_floor";
        floor.transform.SetParent(buildRoot.transform, false);
        floor.transform.localScale = new Vector3(1.5f, 1f, 1.5f);
        floor.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Mat(new Color(0.35f, 0.35f, 0.37f), 0.3f, 0.35f);

        // Enclosed workshop: four dark walls (decorative — colliders destroyed
        // so they can never eat placement raycasts).
        for (int i = 0; i < 4; i++)
        {
            bool alongX = i < 2;
            float sgn = (i % 2 == 0) ? 1f : -1f;
            PartVisualFactory.Deco(PrimitiveType.Cube, buildRoot.transform,
                alongX ? new Vector3(0f, 2.2f, sgn * 7.4f) : new Vector3(sgn * 7.4f, 2.2f, 0f),
                alongX ? new Vector3(15f, 4.4f, 0.15f) : new Vector3(0.15f, 4.4f, 15f),
                Vector3.zero, PartVisualFactory.Mat(new Color(0.10f, 0.11f, 0.13f), 0.4f, 0.3f), "workshop_wall_" + i);
        }

        // Raised circular build pedestal with a hazard-striped rim.
        PartVisualFactory.Deco(PrimitiveType.Cylinder, buildRoot.transform, new Vector3(0f, 0.03f, 0f),
            new Vector3(2.6f, 0.03f, 2.6f), Vector3.zero,
            PartVisualFactory.Mat(new Color(0.23f, 0.24f, 0.27f), 0.7f, 0.5f), "pedestal");
        int seg = 20;
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            PartVisualFactory.Deco(PrimitiveType.Cube, buildRoot.transform,
                new Vector3(Mathf.Cos(a) * 1.32f, 0.045f, Mathf.Sin(a) * 1.32f),
                new Vector3(0.42f, 0.06f, 0.09f),
                new Vector3(0f, -a * Mathf.Rad2Deg - 90f, 0f),
                i % 2 == 0 ? PartVisualFactory.HazardYellow : PartVisualFactory.HazardBlack,
                "hazard_seg_" + i);
        }

        bool hasLight = Object.FindAnyObjectByType<Light>() != null;
        if (!hasLight)
        {
            var sun = new GameObject("Sun");
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }

        // Workshop lighting (all parented to buildRoot so it switches off in
        // test mode): strong key spot on the pedestal, warm fill point light,
        // cool rim directional, emissive ceiling strips for bounce/character.
        var spotGo = new GameObject("pedestal_spot");
        spotGo.transform.SetParent(buildRoot.transform, false);
        spotGo.transform.position = new Vector3(0f, 4.2f, 0f);
        spotGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var spot = spotGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 50f;
        spot.range = 12f;
        spot.intensity = 20f; // URP intensity from 3.5 m up — actually lights the art
        spot.color = new Color(1f, 0.95f, 0.85f);

        var fillGo = new GameObject("fill_light");
        fillGo.transform.SetParent(buildRoot.transform, false);
        fillGo.transform.position = new Vector3(0f, 2f, 0f);
        var fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 8f;
        fill.intensity = 4f;
        fill.color = new Color(1f, 0.92f, 0.8f);

        var rimGo = new GameObject("rim_light");
        rimGo.transform.SetParent(buildRoot.transform, false);
        rimGo.transform.rotation = Quaternion.Euler(18f, 160f, 0f);
        var rim = rimGo.AddComponent<Light>();
        rim.type = LightType.Directional;
        rim.intensity = 0.55f;
        rim.color = new Color(0.55f, 0.65f, 1f);

        // Emissive ceiling light strips.
        for (int i = 0; i < 3; i++)
        {
            float z = (i - 1) * 1.8f;
            PartVisualFactory.Deco(PrimitiveType.Cube, buildRoot.transform,
                new Vector3(0f, 4.35f, z), new Vector3(5f, 0.05f, 0.3f), Vector3.zero,
                PartVisualFactory.CeilingStrip, "ceiling_strip_" + i);
        }

        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>());
        s.name = "com_marker";
        s.transform.SetParent(buildRoot.transform, false);
        s.transform.localScale = Vector3.one * 0.1f;
        comMarker = s.transform;

        var dl = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(dl.GetComponent<Collider>());
        dl.name = "com_dropline";
        dl.transform.SetParent(buildRoot.transform, false);
        comLine = dl.transform;

        for (int i = 0; i < 4; i++)
        {
            var e = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(e.GetComponent<Collider>());
            e.name = "poly_edge_" + i;
            e.transform.SetParent(buildRoot.transform, false);
            e.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(new Color(0.2f, 0.9f, 1f));
            polyEdges.Add(e.transform);
        }

        // Drive-direction arrow: shows which way W will push the finished
        // bot (derived from the mounted wheels). Green = "this is the front".
        var arrowRoot = new GameObject("drive_arrow");
        arrowRoot.transform.SetParent(buildRoot.transform, false);
        // Unmistakably GREEN (critic: GreenGlow read pale/cyan at a glance).
        var arrowMat = PartVisualFactory.Emissive(new Color(0.15f, 0.95f, 0.25f),
                                                  new Color(0.1f, 2.0f, 0.25f), 0f, 0.5f);
        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(arrowRoot.transform, false);
        shaft.transform.localPosition = new Vector3(0f, 0f, 0.55f);
        shaft.transform.localScale = new Vector3(0.07f, 0.015f, 1.1f);
        shaft.GetComponent<Renderer>().sharedMaterial = arrowMat;
        for (int side = -1; side <= 1; side += 2)
        {
            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(wing.GetComponent<Collider>());
            wing.transform.SetParent(arrowRoot.transform, false);
            wing.transform.localPosition = new Vector3(side * 0.09f, 0f, 1.0f);
            wing.transform.localRotation = Quaternion.Euler(0f, -side * 40f, 0f);
            wing.transform.localScale = new Vector3(0.07f, 0.015f, 0.32f);
            wing.GetComponent<Renderer>().sharedMaterial = arrowMat;
        }
        driveArrow = arrowRoot.transform;
    }

    // ------------------------------------------------------------------ parts

    PlacedPart AddPart(P1PartDef def, Vector3 pos, int yaw, Vector3 wheelAxis, PlacedPart attachTo = null)
    {
        // Unscaled root that owns the ONE placement collider; the compound
        // visual (PartVisualFactory) lives in collider-free children. This is
        // the same visual path the arena spawn uses, so parts keep their look.
        var part = new PlacedPart { def = def, pos = pos, yaw = yaw, wheelAxis = wheelAxis };
        Vector3 size = part.Half() * 2f;

        var go = new GameObject(def.id + "_" + placed.Count);
        go.transform.SetParent(buildRoot.transform, true);
        var box = go.AddComponent<BoxCollider>();

        if (def.category == P1Category.Mobility)
        {
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, wheelAxis);
            box.size = def.size; // local Y = wheel axis
            PartVisualFactory.BuildWheel(go.transform, def.size.x * 0.5f, def.size.y, -1);
        }
        else
        {
            box.size = size;
            PartVisualFactory.BuildPart(def.id, go.transform, size,
                def.category == P1Category.Control, MatDB.Get(def.matName).color);
        }
        go.transform.position = pos;
        part.go = go;

        // Connector collar at the seam with the part it snapped onto.
        if (attachTo != null && def.category != P1Category.Mobility)
            PartVisualFactory.BuildCollar(go.transform, pos, part.Half(), attachTo.pos, attachTo.Half());

        placed.Add(part);
        byCollider[box] = part;
        return part;
    }

    void RemovePart(PlacedPart p)
    {
        if (p == placed[0]) { message = "Can't remove the core."; return; }
        // Refuse removals that would orphan other parts (§5.4 validation:
        // connected structure, no floating parts).
        var keep = new List<PlacedPart>(placed);
        keep.Remove(p);
        if (!AllConnected(keep)) { message = "Removing that would orphan parts."; return; }
        placed.Remove(p);
        foreach (var kv in new List<Collider>(byCollider.Keys))
            if (byCollider[kv] == p) byCollider.Remove(kv);
        Destroy(p.go);
        message = "";
        RefreshOverlay();
    }

    bool AllConnected(List<PlacedPart> parts)
    {
        if (parts.Count == 0) return true;
        var seen = new HashSet<PlacedPart> { parts[0] };
        var stack = new Stack<PlacedPart>();
        stack.Push(parts[0]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var other in parts)
                if (!seen.Contains(other) && Touching(cur, other)) { seen.Add(other); stack.Push(other); }
        }
        return seen.Count == parts.Count;
    }

    bool Touching(PlacedPart a, PlacedPart b)
    {
        Vector3 ha = a.Half(), hb = b.Half();
        // Wheels: treat as a thin box along their axis.
        int flushAxis = -1;
        for (int i = 0; i < 3; i++)
        {
            float gap = Mathf.Abs(a.pos[i] - b.pos[i]) - (ha[i] + hb[i]);
            if (gap > 0.03f) return false;
            if (gap > -0.03f) flushAxis = i;
        }
        return flushAxis >= 0;
    }

    // ------------------------------------------------------------------ build-mode loop

    void Update()
    {
        if (mode == Mode.Build) UpdateBuild();
        else UpdateTest();
    }

    void UpdateBuild()
    {
        // Orbit camera.
        orbitYaw += Phase0Input.OrbitAxis() * 80f * Time.deltaTime;
        orbitDist = Mathf.Clamp(orbitDist - Phase0Input.Scroll() * 0.5f, 2.2f, 9f);
        Vector3 target = new Vector3(0f, 0.7f, 0f);
        Quaternion rot = Quaternion.Euler(30f, orbitYaw, 0f);
        cam.transform.position = target + rot * new Vector3(0f, 0f, -orbitDist);
        cam.transform.LookAt(target + Vector3.up * 0.1f);

        if (Phase0Input.RotateDown()) ghostYaw = ghostYaw == 0 ? 90 : 0;
        if (Phase0Input.TestDown()) { StartTest(); return; }

        UpdateGhost();

        Vector3 m = Phase0Input.MousePos();
        bool overPanel = m.x < PANEL_W;

        if (!overPanel && Phase0Input.MouseDown(0) && selected >= 0 && ghostValid)
        {
            AddPart(palette[selected], ghostPos, ghostYaw,
                    palette[selected].category == P1Category.Mobility ? ghostNormal : Vector3.zero,
                    ghostTarget);
            message = "";
            RefreshOverlay();
        }
        if (!overPanel && Phase0Input.MouseDown(1))
        {
            var hitPart = PartUnderMouse();
            if (hitPart != null) RemovePart(hitPart);
        }
    }

    PlacedPart PartUnderMouse()
    {
        Vector3 m = Phase0Input.MousePos();
        Ray ray = cam.ScreenPointToRay(m);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 60f) && byCollider.ContainsKey(hit.collider))
            return byCollider[hit.collider];
        return null;
    }

    /// <summary>
    /// Build (or rebuild) the ghost as the REAL compound part model — the same
    /// PartVisualFactory output a placed part gets — with per-renderer material
    /// instances so validity tinting never touches the shared materials.
    /// </summary>
    void EnsureGhostBuilt(P1PartDef def, bool isWheel)
    {
        string key = def.id + "_" + ghostYaw;
        if (ghost != null && ghostBuiltKey == key) return;
        if (ghost != null) Destroy(ghost);
        ghostMats.Clear();

        ghost = new GameObject("ghost");
        var probe = new PlacedPart { def = def, yaw = ghostYaw };
        if (isWheel)
            PartVisualFactory.BuildWheel(ghost.transform, def.size.x * 0.5f, def.size.y, -1);
        else
            PartVisualFactory.BuildPart(def.id, ghost.transform, probe.Half() * 2f,
                def.category == P1Category.Control, MatDB.Get(def.matName).color);

        foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            var inst = new Material(r.sharedMaterial);
            inst.EnableKeyword("_EMISSION");
            r.sharedMaterial = inst;
            ghostMats.Add(inst);
        }
        ghostBuiltKey = key;
        ghostTintValid = true;
        TintGhost(false);
    }

    void TintGhost(bool valid)
    {
        if (ghostTintValid == valid) return;
        ghostTintValid = valid;
        // Subtle green glow = will attach here; red glow = can't place.
        Color e = valid ? new Color(0.04f, 0.45f, 0.10f) : new Color(0.55f, 0.05f, 0.03f);
        foreach (var m in ghostMats) m.SetColor("_EmissionColor", e);
    }

    void UpdateGhost()
    {
        if (selected < 0) { HideGhost(); return; }
        var def = palette[selected];
        bool isWheelSel = def.category == P1Category.Mobility;

        Vector3 m = Phase0Input.MousePos();
        if (m.x < PANEL_W) { HideGhost(); return; }
        EnsureGhostBuilt(def, isWheelSel);
        ghost.SetActive(true);

        Ray ray = cam.ScreenPointToRay(m);
        RaycastHit hit;
        bool onFace = Physics.Raycast(ray, out hit, 60f) && byCollider.ContainsKey(hit.collider);
        bool grazed = false;

        if (!onFace)
        {
            // Placement forgiveness (critic fix): a near-miss ray that grazes
            // past a part still snaps to it — fatten the ray and take the
            // closest placed-part face it touches.
            RaycastHit near = default(RaycastHit);
            bool found = false;
            foreach (var h in Physics.SphereCastAll(ray, 0.07f, 60f))
                if (byCollider.ContainsKey(h.collider) && (!found || h.distance < near.distance))
                { near = h; found = true; }
            if (found) { hit = near; onFace = true; grazed = true; }
        }

        if (!onFace)
        {
            // Free-follow: the real part model tracks the mouse across the
            // build plane until a snap face is hovered. Not placeable here.
            ghostTarget = null;
            ghostValid = false;
            ghostReason = "Aim at the robot to attach";
            Vector3 o = ray.origin, d = ray.direction;
            float planeY = 0.7f;
            float t = Mathf.Abs(d.y) > 0.001f ? (planeY - o.y) / d.y : -1f;
            Vector3 p = t > 0f ? o + d * t : o + d * 4f;
            p.x = Mathf.Round(p.x / SNAP) * SNAP;
            p.z = Mathf.Round(p.z / SNAP) * SNAP;
            p.y = planeY;
            ghostPos = p;
            ghost.transform.rotation = isWheelSel
                ? Quaternion.FromToRotation(Vector3.up, new Vector3(1f, 0f, 0f))
                : Quaternion.identity;
            ghost.transform.position = p;
            TintGhost(false);
            return;
        }

        ghostTarget = byCollider[hit.collider];
        ghostNormal = hit.normal;
        if (grazed)
        {
            // A grazing sphere-cast reports edge/diagonal normals, which made
            // mirrored sockets disagree (left face read as a +X face → overlap
            // → phantom "Blocked"). Derive the face from WHERE the graze sits
            // relative to the part instead: dominant axis of the offset,
            // normalized by the part's half extents.
            Vector3 off = hit.point - ghostTarget.pos;
            Vector3 th = ghostTarget.Half();
            int ga = 0;
            float gb = Mathf.Abs(off.x) / Mathf.Max(th.x, 0.01f);
            float gy = Mathf.Abs(off.y) / Mathf.Max(th.y, 0.01f);
            if (gy > gb) { ga = 1; gb = gy; }
            float gz = Mathf.Abs(off.z) / Mathf.Max(th.z, 0.01f);
            if (gz > gb) ga = 2;
            Vector3 derived = Vector3.zero;
            derived[ga] = off[ga] >= 0f ? 1f : -1f;
            ghostNormal = derived;
        }

        // Dominant world axis of the face normal (all parts are axis-aligned).
        int axis = 0;
        float best = Mathf.Abs(ghostNormal.x);
        if (Mathf.Abs(ghostNormal.y) > best) { axis = 1; best = Mathf.Abs(ghostNormal.y); }
        if (Mathf.Abs(ghostNormal.z) > best) { axis = 2; }
        float sign = ghostNormal[axis] >= 0f ? 1f : -1f;

        bool isWheel = def.category == P1Category.Mobility;
        // Probes carry the candidate axle so Half() returns the wheel's
        // EFFECTIVE extents (half-width along the axle, radius tangentially) —
        // wheels sit flush on the face AND the overlap box matches reality.
        var probe = new PlacedPart { def = def, yaw = ghostYaw,
                                     wheelAxis = isWheel ? ghostNormal : Vector3.zero };
        Vector3 newHalf = probe.Half();
        Vector3 tHalf = ghostTarget.Half();

        Vector3 pos = ghostTarget.pos;
        pos[axis] = ghostTarget.pos[axis] + sign * (tHalf[axis] + newHalf[axis]);
        // §5.1 socket model: each face carries ONE connector socket at its
        // center (the gray dot). Parts click together socket-to-socket, so
        // the tangent coordinates are LOCKED to the target's face center —
        // no off-grid sliding, no overhanging half-offsets.

        ghostValid = true;
        ghostReason = "";
        // Both mating faces must actually carry a socket. The visual factory
        // only draws a dot on faces at least 0.15 m in both tangent
        // directions — mirror that rule exactly (plate edges = no socket).
        int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
        if (ghostTarget.def.category == P1Category.Mobility)
        { ghostValid = false; ghostReason = "Nothing attaches to a wheel"; }
        else if (isWheel && Mathf.Abs(ghostNormal.y) > 0.5f)
        { ghostValid = false; ghostReason = "Wheels attach to side faces only"; }
        else if (Mathf.Min(tHalf[t1], tHalf[t2]) < 0.074f
                 || (!isWheel && Mathf.Min(newHalf[t1], newHalf[t2]) < 0.074f))
        { ghostValid = false; ghostReason = "No socket on this face"; }
        else if (pos.y - newHalf.y < 0.05f)
        { ghostValid = false; ghostReason = "Too low — would clip the floor"; }

        // Overlap check against existing parts (slightly shrunk, using the
        // ORIENTED effective extents so wheels never phantom-clip neighbors).
        if (ghostValid)
        {
            foreach (var c in Physics.OverlapBox(pos, newHalf * 0.92f, Quaternion.identity))
                if (byCollider.ContainsKey(c))
                { ghostValid = false; ghostReason = "Blocked by another part"; break; }
        }

        ghostPos = pos;
        ghost.transform.rotation = isWheel
            ? Quaternion.FromToRotation(Vector3.up, ghostNormal)
            : Quaternion.identity;
        ghost.transform.position = pos;
        TintGhost(ghostValid);
    }

    void HideGhost() { if (ghost != null) ghost.SetActive(false); ghostValid = false; ghostReason = ""; }

    // ------------------------------------------------- CoM + support polygon

    void RefreshOverlay()
    {
        float total = 0f;
        Vector3 weighted = Vector3.zero;
        foreach (var p in placed) { total += p.def.Mass(); weighted += p.def.Mass() * p.pos; }
        Vector3 com = total > 0f ? weighted / total : Vector3.zero;

        comMarker.position = com;
        comLine.position = new Vector3(com.x, com.y * 0.5f, com.z);
        comLine.localScale = new Vector3(0.02f, com.y, 0.02f);

        // Build's lowest feature — a wheel only supports the bot if its bottom
        // actually reaches the ground (round-3 fix: wheels mounted high on the
        // engine read as support but the CHASSIS lands on the floor instead).
        float lowest = float.MaxValue;
        foreach (var p in placed)
            lowest = Mathf.Min(lowest, p.def.category == P1Category.Mobility
                                       ? p.pos.y - 0.18f : p.pos.y - p.Half().y);

        // Support polygon = x/z extent of the GROUNDED wheels only.
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        int wheels = 0, grounded = 0;
        floatingWheels = 0;
        bool anyRollX = false, anyRollZ = false;
        Vector3 groundedRollSum = Vector3.zero;
        foreach (var p in placed)
        {
            if (p.def.category != P1Category.Mobility) continue;
            wheels++;
            // Canonical roll direction (same rule as RaycastWheelDrive).
            Vector3 roll = Vector3.Cross(p.wheelAxis, Vector3.up);
            if (roll.sqrMagnitude >= 0.01f)
            {
                roll.Normalize();
                if (roll.z < -0.01f || (Mathf.Abs(roll.z) <= 0.01f && roll.x < 0f)) roll = -roll;
                if (Mathf.Abs(roll.x) > Mathf.Abs(roll.z)) anyRollX = true; else anyRollZ = true;
            }
            if (p.pos.y - 0.18f > lowest + 0.05f) { floatingWheels++; continue; }
            grounded++;
            if (roll.sqrMagnitude >= 0.01f) groundedRollSum += roll;
            minX = Mathf.Min(minX, p.pos.x); maxX = Mathf.Max(maxX, p.pos.x);
            minZ = Mathf.Min(minZ, p.pos.z); maxZ = Mathf.Max(maxZ, p.pos.z);
        }
        mixedRoll = anyRollX && anyRollZ;

        bool hasPoly = grounded >= 2 && (maxX - minX) > 0.01f;
        foreach (var e in polyEdges) e.gameObject.SetActive(hasPoly);

        float margin = -1f;
        if (hasPoly)
        {
            float y = 0.02f;
            PlaceEdge(polyEdges[0], new Vector3((minX + maxX) * 0.5f, y, minZ), new Vector3(maxX - minX, 0.015f, 0.015f));
            PlaceEdge(polyEdges[1], new Vector3((minX + maxX) * 0.5f, y, maxZ), new Vector3(maxX - minX, 0.015f, 0.015f));
            PlaceEdge(polyEdges[2], new Vector3(minX, y, (minZ + maxZ) * 0.5f), new Vector3(0.015f, 0.015f, maxZ - minZ));
            PlaceEdge(polyEdges[3], new Vector3(maxX, y, (minZ + maxZ) * 0.5f), new Vector3(0.015f, 0.015f, maxZ - minZ));

            margin = Mathf.Min(Mathf.Min(com.x - minX, maxX - com.x),
                               Mathf.Min(com.z - minZ, maxZ - com.z));
        }
        supportMargin = margin;
        wheelCount = wheels;

        Color c = margin > 0.08f ? new Color(0.2f, 1f, 0.3f)
                : margin > 0f ? new Color(1f, 0.85f, 0.2f)
                : Color.red;
        comMarker.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(c);
        comLine.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(c);

        // Canonical drive direction from the GROUNDED wheels (floating wheels
        // can't push, so they must not skew the heading/dummy placement); fall
        // back to all wheels if nothing reaches the ground.
        Vector3 sum = groundedRollSum;
        if (sum.sqrMagnitude < 0.01f)
        {
            foreach (var p in placed)
            {
                if (p.def.category != P1Category.Mobility) continue;
                Vector3 roll = Vector3.Cross(p.wheelAxis, Vector3.up);
                if (roll.sqrMagnitude < 0.01f) continue;
                roll.Normalize();
                if (roll.z < -0.01f || (Mathf.Abs(roll.z) <= 0.01f && roll.x < 0f)) roll = -roll;
                sum += roll;
            }
        }
        driveDir = sum.sqrMagnitude < 0.01f ? Vector3.forward
                 : (Mathf.Abs(sum.x) > Mathf.Abs(sum.z) ? new Vector3(Mathf.Sign(sum.x), 0f, 0f)
                                                        : new Vector3(0f, 0f, Mathf.Sign(sum.z)));
        if (driveArrow != null)
        {
            // Always visible (critic fix): with zero wheels the arrow shows
            // the default +Z front so the player never loses orientation.
            driveArrow.gameObject.SetActive(true);
            driveArrow.position = new Vector3(com.x, 0.07f, com.z);
            driveArrow.rotation = Quaternion.LookRotation(driveDir);
        }
    }

    void PlaceEdge(Transform t, Vector3 pos, Vector3 scale)
    {
        t.position = pos;
        t.localScale = scale;
    }

    // ------------------------------------------------------------ validation

    public string Validate()
    {
        int wheels = 0, power = 0;
        foreach (var p in placed)
        {
            if (p.def.category == P1Category.Mobility) wheels++;
            if (p.def.category == P1Category.Power) power++;
        }
        if (wheels < 1) return "Needs at least 1 wheel.";
        if (power < 1) return "Needs an engine or battery.";
        if (!AllConnected(placed)) return "Structure has floating parts.";
        return null; // valid
    }

    // ------------------------------------------------------------- test mode

    public void StartTest()
    {
        string err = Validate();
        if (err != null) { message = err; return; }

        HideGhost();
        buildRoot.SetActive(false);
        mode = Mode.Test;
        message = "";

        // Phase 1 test arena: intimate 14x14 m box (the Phase 0 sandbox keeps
        // its own big arena in Phase0Manager) with floor markings so the bot
        // isn't lost in a featureless expanse.
        const float HALF = 7f;
        sandboxRoot = new GameObject("sandbox");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "arena_floor";
        floor.transform.SetParent(sandboxRoot.transform, false);
        floor.transform.localScale = new Vector3(HALF / 5f, 1f, HALF / 5f);
        floor.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Mat(new Color(0.34f, 0.34f, 0.36f), 0.2f, 0.35f);
        for (int i = 0; i < 4; i++)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(sandboxRoot.transform, false);
            bool alongX = i < 2;
            float sign = (i % 2 == 0) ? 1f : -1f;
            wall.transform.position = alongX ? new Vector3(0f, 0.75f, sign * HALF) : new Vector3(sign * HALF, 0.75f, 0f);
            wall.transform.localScale = alongX ? new Vector3(2f * HALF + 0.5f, 1.5f, 0.5f) : new Vector3(0.5f, 1.5f, 2f * HALF + 0.5f);
            wall.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Mat(new Color(0.22f, 0.22f, 0.25f), 0.5f, 0.4f);

            // Hazard-striped wall tops (decorative, collider-free).
            for (int k = 0; k < 7; k++)
            {
                float off = -HALF + 1f + k * 2f;
                PartVisualFactory.Deco(PrimitiveType.Cube, sandboxRoot.transform,
                    alongX ? new Vector3(off, 1.6f, sign * HALF) : new Vector3(sign * HALF, 1.6f, off),
                    alongX ? new Vector3(2f, 0.22f, 0.7f) : new Vector3(0.7f, 0.22f, 2f),
                    Vector3.zero,
                    k % 2 == 0 ? PartVisualFactory.HazardYellow : PartVisualFactory.HazardBlack,
                    "wall_hazard_" + i + "_" + k);
            }
        }

        // Emissive corner posts.
        for (int i = 0; i < 4; i++)
        {
            float sx = (i % 2 == 0) ? 1f : -1f;
            float sz = (i < 2) ? 1f : -1f;
            PartVisualFactory.Deco(PrimitiveType.Cylinder, sandboxRoot.transform,
                new Vector3(sx * (HALF - 0.3f), 1.6f, sz * (HALF - 0.3f)), new Vector3(0.35f, 1.6f, 0.35f),
                Vector3.zero, PartVisualFactory.CyanGlow, "corner_post_" + i);
        }

        // Painted floor markings: center circle + two radial lines (thin
        // emissive quads, NO colliders — invisible to wheel suspension rays).
        int circSeg = 24;
        for (int i = 0; i < circSeg; i++)
        {
            float a = i * Mathf.PI * 2f / circSeg;
            PartVisualFactory.Deco(PrimitiveType.Cube, sandboxRoot.transform,
                new Vector3(Mathf.Cos(a) * 2f, 0.012f, Mathf.Sin(a) * 2f),
                new Vector3(0.55f, 0.012f, 0.07f),
                new Vector3(0f, -a * Mathf.Rad2Deg - 90f, 0f),
                PartVisualFactory.FloorMark, "circle_seg_" + i);
        }
        PartVisualFactory.Deco(PrimitiveType.Cube, sandboxRoot.transform,
            new Vector3(0f, 0.012f, 0f), new Vector3(2f * HALF - 0.6f, 0.012f, 0.07f),
            Vector3.zero, PartVisualFactory.FloorMark, "line_x");
        PartVisualFactory.Deco(PrimitiveType.Cube, sandboxRoot.transform,
            new Vector3(0f, 0.012f, 0f), new Vector3(0.07f, 0.012f, 2f * HALF - 0.6f),
            Vector3.zero, PartVisualFactory.FloorMark, "line_z");

        // The Phase 0 ram dummy, so builds can be crash-tested immediately.
        var dummySpecs = new[]
        {
            new PartSpec("base",   new Vector3(0.5f, 0.2f, 0.5f),  Vector3.zero, "Steel", isCore: true),
            new PartSpec("pillar", new Vector3(0.14f, 0.5f, 0.14f), new Vector3(0f, 0.35f, 0f), "ABS"),
            new PartSpec("head",   new Vector3(0.3f, 0.3f, 0.3f),  new Vector3(0f, 0.75f, 0f), "ABS"),
        };
        var dummyConns = new[] { new CompoundRobot.Conn(0, 1), new CompoundRobot.Conn(1, 2) };
        // Place the dummy OFF the drive axis (critic fix: it used to sit dead
        // ahead, so the very first W press was an unavoidable full-speed ram).
        // Ahead along the drive direction but 2.5 m to the side — the player
        // must steer to engage, whatever the build's driveDir is.
        Vector3 perp = Vector3.Cross(Vector3.up, driveDir).normalized;
        // Sidestep toward the arena CENTER, never toward the spawn-side wall —
        // otherwise the bounds clamp below eats the offset and the dummy ends
        // up nearly back on the drive axis.
        if (Vector3.Dot(perp, new Vector3(0f, 0f, -4f)) > 0f) perp = -perp;
        Vector3 dpos = new Vector3(0f, 0f, -4f) + driveDir * 5.5f + perp * 2.5f;
        dpos.x = Mathf.Clamp(dpos.x, -(HALF - 1.2f), HALF - 1.2f);
        dpos.z = Mathf.Clamp(dpos.z, -(HALF - 1.2f), HALF - 1.2f);
        CompoundRobot.Build("RamDummy", dummySpecs, dummyConns, new Vector3(dpos.x, 0.15f, dpos.z), Quaternion.identity);

        SpawnDesign();

        followCam = cam.gameObject.AddComponent<FollowCamera>();
        followCam.target = testRobot.transform;
        followCam.forwardHint = driveDir; // chase from behind the DRIVE direction
        // Keep the camera INSIDE the arena (critic fix: the raw chase offset
        // put it 4 m outside the wall, which occluded the bot into a distant
        // speck). Clamp to the walls minus margin; DesiredPos raises the
        // camera when clamped so the view stays clear over the wall.
        followCam.clampHalf = HALF - 0.8f;
        // Snap instantly to the (clamped) chase position — the smoothing lerp
        // would otherwise spend ~1 s crossing from the build-room view.
        followCam.SnapNow();

        // Fresh wreck/recovery HUD state for this run.
        lastTestParts = ActivePartCount();
        shearTimer = 0f;
        shearText = "";
        wrecked = false;
        stuckTimer = 0f;
        stuck = false;

        // Headlight strip on the core's leading face so the front stays
        // obvious while driving.
        foreach (Transform child in testRobot.transform)
        {
            if (!child.name.StartsWith("core")) continue;
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(child, false);
            lamp.transform.localPosition = driveDir * 0.16f + Vector3.up * 0.06f;
            lamp.transform.localRotation = Quaternion.LookRotation(driveDir);
            lamp.transform.localScale = new Vector3(0.16f, 0.05f, 0.02f);
            lamp.GetComponent<Renderer>().sharedMaterial = PartVisualFactory.Emissive(
                new Color(0.15f, 0.95f, 0.25f), new Color(0.1f, 2.0f, 0.25f), 0f, 0.5f);
            break;
        }
    }

    int ActivePartCount()
    {
        if (testRobot == null) return 0;
        int n = 0;
        foreach (var p in testRobot.parts) if (!p.detached) n++;
        return n;
    }

    void SpawnDesign()
    {
        var core = placed[0];
        var body = new List<PlacedPart>();
        var wheels = new List<PlacedPart>();
        foreach (var p in placed)
            (p.def.category == P1Category.Mobility ? wheels : body).Add(p);

        var specs = new PartSpec[body.Count];
        float minY = float.MaxValue;
        for (int i = 0; i < body.Count; i++)
        {
            var p = body[i];
            Vector3 rel = p.pos - core.pos;
            Vector3 size = p.Half() * 2f;
            specs[i] = new PartSpec(p.def.id + "_" + i, size, rel, p.def.matName, p == core);
            minY = Mathf.Min(minY, rel.y - p.Half().y);
        }

        var conns = new List<CompoundRobot.Conn>();
        for (int i = 0; i < body.Count; i++)
            for (int j = i + 1; j < body.Count; j++)
                if (Touching(body[i], body[j])) conns.Add(new CompoundRobot.Conn(i, j));

        var anchors = new Vector3[wheels.Count];
        var steer = new bool[wheels.Count];
        var axes = new Vector3[wheels.Count];
        float meanAlong = 0f;
        for (int i = 0; i < wheels.Count; i++)
            meanAlong += Vector3.Dot(wheels[i].pos - core.pos, driveDir) / wheels.Count;
        for (int i = 0; i < wheels.Count; i++)
        {
            Vector3 rel = wheels[i].pos - core.pos;
            anchors[i] = rel;
            // Wheels in the leading half ALONG THE DRIVE DIRECTION steer —
            // "front" is wherever the wheels actually drive, not body +Z.
            steer[i] = Vector3.Dot(rel, driveDir) > meanAlong + 0.03f;
            axes[i] = wheels[i].wheelAxis; // keep the mounted axle orientation in the arena
            minY = Mathf.Min(minY, rel.y - 0.18f);
        }

        // Spawn AT suspension height (round-3 fix): 0.55 dropped the bot 0.35 m
        // in free fall — the HUD showed phantom 2 m/s "speed" while visibly
        // stationary, and builds whose wheels sit high slammed down onto the
        // CHASSIS, which then skidded on the floor (slow, grindy, shear-prone).
        // 0.22 puts wheel anchors ~0.40 up: rays (0.48) reach the floor on
        // frame one and the springs catch the bot after a ~2 cm settle.
        float spawnY = 0.22f - minY;
        testRobot = CompoundRobot.Build("PlayerBuild", specs, conns.ToArray(),
                                        new Vector3(0f, spawnY, -4f), Quaternion.identity);

        // Fold the wheels' mass into the body (critic fix: builder said
        // 407 kg, arena HUD said 309 — the wheels were massless in the arena).
        // Must happen BEFORE RaycastWheelDrive.Init, which auto-sizes the
        // suspension from rb.mass.
        float wheelMass = 0f;
        Vector3 wheelMoment = Vector3.zero;
        foreach (var w in wheels)
        {
            float wm = w.def.Mass();
            wheelMass += wm;
            wheelMoment += wm * (w.pos - core.pos);
        }
        testRobot.extraMass = wheelMass;
        testRobot.extraMassLocalCoM = wheelMass > 0f ? wheelMoment / wheelMass : Vector3.zero;
        testRobot.RecomputeMass();

        // Arena HUD mass must equal the builder panel EXACTLY: same per-part
        // rounding, summed (round-3 fix: float-sum showed builder+1 kg).
        hudWheelMassInt = 0;
        foreach (var w in wheels) hudWheelMassInt += Mathf.RoundToInt(w.def.Mass());

        testDrive = testRobot.gameObject.AddComponent<RaycastWheelDrive>();
        testDrive.Init(testRobot.rb, anchors, steer, axes);
    }

    void UpdateTest()
    {
        if (Phase0Input.BackDown()) { BackToBuild(); return; }
        // R = full arena reset (critic fix: no recovery after a wreck).
        // Reuses the two existing paths so there is exactly one lifecycle.
        if (Phase0Input.ResetDown()) { ResetTest(); return; }

        if (shearTimer > 0f) shearTimer -= Time.deltaTime;

        if (testRobot == null)
        {
            // Total destruction: the shear poll below never sees the final
            // part count, so latch the callout here (round-3 toast fix).
            if (lastTestParts > 0)
            {
                shearText = "ROBOT DESTROYED";
                shearTimer = 3.5f;
                lastTestParts = 0;
            }
            wrecked = true;
            return;
        }

        // Poll part count: dramatic HUD callout when something shears off.
        // Latches for 3.5 s (critic fix: 2 s vanished in the crash chaos) and
        // stacks WITH the wrecked line instead of being replaced by it.
        int n = ActivePartCount();
        if (n < lastTestParts)
        {
            shearText = "PART SHEARED OFF — " + n + " left";
            shearTimer = 3.5f;
        }
        lastTestParts = n;

        // Flipped and (nearly) stationary = wrecked; tell the player the way out.
        float speed = VelUtil.GetLinearVelocity(testRobot.rb).magnitude;
        bool flipped = Vector3.Dot(testRobot.transform.up, Vector3.up) < 0.2f;
        wrecked = flipped && speed < 1.0f;

        // Beached: throttle held ~1.5 s while the bot barely moves (high-
        // sided on debris, wedged in a corner, wheels off the ground...).
        // Fires REGARDLESS of wrecked/flipped (round-3 fix: a tilted-but-not-
        // wrecked bot with throttle held showed nothing at all).
        if (Mathf.Abs(Phase0Input.Throttle()) > 0.3f && speed < 0.4f)
            stuckTimer += Time.deltaTime;
        else
            stuckTimer = 0f;
        stuck = stuckTimer > 1.5f;
    }

    public void ResetTest()
    {
        BackToBuild();
        StartTest();
    }

    public void BackToBuild()
    {
        CompoundRobot.ClearAll();
        // Arena litter sweep (critic fix): severed debris chunks
        // ("<robot>_debris" roots from CompoundRobot.SpawnDebris) and any
        // robot/dummy root that escaped the Spawned registry must never
        // survive into the builder or stack up across T/B cycles.
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go == null || go == buildRoot || go == sandboxRoot || go == gameObject) continue;
            if (go.GetComponent<CompoundRobot>() != null || go.name.Contains("_debris")
                || go.name.StartsWith("RamDummy") || go.name.StartsWith("PlayerBuild"))
            {
                go.SetActive(false); // deferred Destroy must not leave live colliders
                Destroy(go);
            }
        }
        if (sandboxRoot != null) Destroy(sandboxRoot);
        if (followCam != null)
        {
            // Disable BEFORE the deferred Destroy: on the R-reset path a new
            // FollowCamera is added in the SAME frame, and a still-live old
            // one would fight it for the camera transform until end of frame.
            followCam.enabled = false;
            Destroy(followCam);
        }
        testRobot = null;
        testDrive = null;
        shearTimer = 0f;
        wrecked = false;
        stuckTimer = 0f;
        stuck = false;
        buildRoot.SetActive(true);
        mode = Mode.Build;
        RefreshOverlay();
    }

    // ------------------------------------------------------ programmatic API
    // (used by remote/automated tests: place by target index + face normal)

    public bool PlaceByFace(int paletteIndex, int targetIndex, Vector3 normal, int yaw)
    {
        var def = palette[paletteIndex];
        var target = placed[targetIndex];
        int axis = 0;
        float best = Mathf.Abs(normal.x);
        if (Mathf.Abs(normal.y) > best) { axis = 1; best = Mathf.Abs(normal.y); }
        if (Mathf.Abs(normal.z) > best) { axis = 2; }
        float sign = normal[axis] >= 0f ? 1f : -1f;
        var probe = new PlacedPart { def = def, yaw = yaw,
                                     wheelAxis = def.category == P1Category.Mobility ? normal : Vector3.zero };
        Vector3 pos = target.pos;
        pos[axis] = target.pos[axis] + sign * (target.Half()[axis] + probe.Half()[axis]);
        AddPart(def, pos, yaw, def.category == P1Category.Mobility ? normal : Vector3.zero, target);
        RefreshOverlay();
        return true;
    }

    // ------------------------------------------------------------------- GUI

    void OnGUI()
    {
        if (mode == Mode.Test)
        {
            EnsureStyles();
            GUI.Box(new Rect(10, 10, 380, 46), "TEST DRIVE — WASD drive · R reset · B builder");
            GUILayout.BeginArea(new Rect(20, 30, 360, 24));
            if (testRobot != null)
            {
                // Identical rounding path to the builder panel: sum of
                // per-part rounded masses (drops as parts shear off).
                int hudMass = hudWheelMassInt;
                foreach (var p in testRobot.parts)
                    if (!p.detached) hudMass += Mathf.RoundToInt(p.mass);
                GUILayout.Label(string.Format("Mass {0} kg · speed {1:F1} km/h",
                    hudMass, testDrive != null ? testDrive.SpeedKmh() : 0f));
            }
            GUILayout.EndArea();
            // Center-screen event toasts. Shear and wrecked/stuck STACK —
            // the shear callout must survive even when the wreck line shows.
            float ty = 0.34f;
            if (shearTimer > 0f)
            {
                BigLine(shearText, new Color(1f, 0.55f, 0.15f), ty);
                ty += 0.09f;
            }
            if (wrecked)
            {
                BigLine("WRECKED — press R to reset", new Color(1f, 0.30f, 0.25f), ty);
                ty += 0.09f;
            }
            if (stuck && !wrecked)
                BigLine("STUCK — press R to reset", new Color(1f, 0.85f, 0.25f), ty);
            return;
        }

        EnsureStyles();
        GUI.Box(new Rect(0, 0, PANEL_W, Screen.height), "");
        GUILayout.BeginArea(new Rect(14, 14, PANEL_W - 28, Screen.height - 24));

        GUILayout.Label("ROBOT BUILDER — Phase 1", headStyle);
        GUILayout.Space(6);
        GUILayout.Label("Click a part, then click a socket (gray dot) on the robot:", bodyStyle);
        GUILayout.Space(2);
        for (int i = 1; i < palette.Length; i++)  // 0 = core, auto-placed
        {
            var d = palette[i];
            string tag = selected == i ? "▶ " : "";
            if (GUILayout.Button(string.Format("{0}{1}  ·  {2} kg", tag, d.label, Mathf.RoundToInt(d.Mass())), btnStyle))
                selected = selected == i ? -1 : i;
            GUILayout.Label(d.desc, descStyle);
            GUILayout.Space(3);
        }

        GUILayout.Space(8);
        // Panel mass = sum of the SAME rounded per-part values the palette
        // buttons show (critic fix: float-sum-then-round drifted by a few kg
        // from the sum of the displayed labels).
        int massInt = 0; int cost = 0;
        foreach (var p in placed) { massInt += Mathf.RoundToInt(p.def.Mass()); cost += p.def.Cost(); }
        GUILayout.Label(string.Format("Parts: {0}   Mass: {1} kg   Cost: {2}", placed.Count, massInt, cost), bodyStyle);
        GUILayout.Label(string.Format("CoM height: {0:F2} m", comMarker != null ? comMarker.position.y : 0f), bodyStyle);

        GUILayout.Space(6);
        string err = Validate();
        if (err != null)
            GUILayout.Label("⚠ " + err, bodyStyle);
        else if (wheelCount > 0 && supportMargin <= 0f)
            // Legal but physically doomed (critic fix): never claim "Ready to
            // fight" while the CoM sits outside the GROUNDED-wheel polygon
            // (wheels that can't reach the floor don't support anything).
            GUILayout.Label("⚠ UNSTABLE — will tip over", warnStyle);
        else if (floatingWheels > 0 || mixedRoll)
            GUILayout.Label("⚠ Not ready — see wheel warnings below", warnStyle);
        else
            GUILayout.Label("✓ Ready to fight", bodyStyle);
        if (floatingWheels > 0)
            GUILayout.Label("⚠ " + floatingWheels + " wheel(s) can't reach the ground — the chassis will drag", warnStyle);
        if (mixedRoll)
            GUILayout.Label("⚠ Wheels disagree on direction — the bot will fight itself", warnStyle);
        if (ghost != null && ghost.activeSelf && !ghostValid && ghostReason.Length > 0)
            GUILayout.Label("✗ " + ghostReason, warnStyle);
        // Suppress the duplicate when a refused T just re-states the standing
        // validation error already shown above (critic fix).
        if (message.Length > 0 && message != err) GUILayout.Label("⚠ " + message, bodyStyle);

        GUILayout.Space(6);
        if (GUILayout.Button("TEST DRIVE  (T)", btnStyle)) StartTest();

        GUILayout.Space(10);
        GUILayout.Label("R rotate part · right-click remove\nQ/E orbit · scroll zoom\nGreen arrow = FRONT (W drives that way)\nRed dot = center of mass\nBlue rect = wheel support\n(dot outside rect → it tips)", descStyle);
        GUILayout.EndArea();
    }

    void EnsureStyles()
    {
        if (btnStyle != null) return;
        headStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
        btnStyle.padding = new RectOffset(10, 8, 7, 7);
        bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        descStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        descStyle.normal.textColor = new Color(0.72f, 0.76f, 0.82f);
        warnStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, fontStyle = FontStyle.Bold };
        warnStyle.normal.textColor = new Color(1f, 0.42f, 0.32f);
        bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 38, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    void BigLine(string text, Color c, float yFrac)
    {
        // Drop-shadow pass so the toast reads against any arena backdrop.
        float y = Screen.height * yFrac;
        bigStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(3f, y + 3f, Screen.width, 60f), text, bigStyle);
        bigStyle.normal.textColor = c;
        GUI.Label(new Rect(0f, y, Screen.width, 60f), text, bigStyle);
    }
}

/// <summary>Tiny startup menu: choose the Phase 0 sandbox or the Phase 1 builder.</summary>
public class ModeSelect : MonoBehaviour
{
    void OnGUI()
    {
        float w = 320f, h = 130f;
        float x = (Screen.width - w) * 0.5f, y = (Screen.height - h) * 0.5f;
        GUI.Box(new Rect(x, y, w, h), "ROBOT BRAWL — prototypes");
        if (GUI.Button(new Rect(x + 20, y + 34, w - 40, 36), "PHASE 1 — Robot Builder"))
        {
            new GameObject("BuilderManager").AddComponent<BuilderManager>();
            Destroy(gameObject);
        }
        if (GUI.Button(new Rect(x + 20, y + 78, w - 40, 36), "PHASE 0 — Physics Sandbox"))
        {
            new GameObject("Phase0Manager").AddComponent<Phase0Manager>();
            Destroy(gameObject);
        }
    }
}
