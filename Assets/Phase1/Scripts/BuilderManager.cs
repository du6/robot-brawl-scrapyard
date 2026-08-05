using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
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
/// Build: click face place · R rotate · right-click removes a whole branch ·
/// Z undo · Q/E orbit · scroll zoom · T test.  Test: WASD drive · R reset · B back to builder.
/// </summary>
public class BuilderManager : MonoBehaviour
{
    public enum Mode { Build, Test, Fight }

    public class PlacedPart
    {
        public P1PartDef def;
        public GameObject go;
        public Vector3 pos;        // world center (build space)
        public int yaw;            // 0 or 90 — swaps x/z of size
        public Vector3 wheelAxis;  // wheels/weapons: outward mount face normal
        /// <summary>Phase 3: the §4.3 material THIS placement is made of.
        /// Empty falls back to the def's default, so v1 snapshots and any
        /// code that never sets it keep the old behaviour exactly.</summary>
        public string matName;

        /// <summary>========= ACTUATOR DRIVE AXIS (owen, 2026-07-28) =========
        /// "give more flexibility on mounting spindle and pivot".
        ///
        /// Until now an actuator's drive axis WAS its mount face normal, so the
        /// only way to change what a pivot swings around, or which plane a
        /// spindle spins in, was to bolt it to a different face. That makes the
        /// most iconic weapon in the genre - a vertical spinner centred on the
        /// nose - unbuildable: a forward-facing face can only ever give a Z
        /// axle, and an X axle can only be had by mounting on a side.
        ///
        /// The axis now rides in `yaw`, which for an actuator was DEAD DATA:
        /// pivot, spindle and ram are 0.30 m CUBES, so Half() returns the same
        /// box at all four yaws - measured by the round-3 dev when it refuted
        /// the "collider disagrees with mesh" finding for these three parts.
        /// That is what makes this safe: changing the axis cannot move a
        /// collider, a socket, a snap or an overlap test. Only the drawing and
        /// Actuator.ActuatorAxis read it.
        ///
        /// yaw 0 keeps the OLD behaviour exactly, so every saved build and every
        /// roster recipe is unchanged without a migration or a format bump.
        ///   yaw 0   -> follow the mount face (legacy)
        ///   yaw 90  -> X axle    yaw 180 -> Y axle    yaw 270 -> Z axle</summary>
        public Vector3 DriveAxis()
        {
            if (def == null || !def.actuator || yaw == 0) return wheelAxis;
            if (yaw == 90)  return Vector3.right;
            if (yaw == 180) return Vector3.up;
            return Vector3.forward;
        }

        /// <summary>Human-readable name for the panel and the ghost hint.</summary>
        public string DriveAxisLabel()
        {
            if (def == null || !def.actuator) return "";
            if (yaw == 0)   return "mount face";
            if (yaw == 90)  return "X (side-to-side)";
            if (yaw == 180) return "Y (vertical)";
            return "Z (front-to-back)";
        }

        public string MatName() { return def.EffectiveMat(matName); }
        public float Mass()     { return def.MassOf(MatName()); }
        public int Cost()       { return def.CostOf(MatName()); }
        public MatDef Mat()     { return MatDB.Get(MatName()); }

        public Vector3 Half()
        {
            Vector3 s = def.size;
            if (def.id == "spike")
            {
                // Wedge points along its mount normal: long axis (z of the
                // def size) follows wheelAxis; square cross-section otherwise.
                int sa = 2;
                float sb = Mathf.Abs(wheelAxis.z);
                if (Mathf.Abs(wheelAxis.x) > sb) { sa = 0; sb = Mathf.Abs(wheelAxis.x); }
                if (Mathf.Abs(wheelAxis.y) > sb) sa = 1;
                Vector3 sh = Vector3.one * (s.x * 0.5f);
                sh[sa] = s.z * 0.5f;
                return sh;
            }
            if (def.category == P1Category.Mobility || def.id.StartsWith("spinner"))
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
            if (yaw == 90) s = new Vector3(s.z, s.y, s.x);        // long axis → X
            else if (yaw == 180) s = new Vector3(s.x, s.z, s.y);  // stood up (y/z swap): vertical beam, wall plate
            else if (yaw == 270) s = new Vector3(s.y, s.x, s.z);  // stood up (x/y swap): wall plate facing X

            // ROUND-UP3 FIX A (round-3 critic CRITICAL). Round 2 rotated the
            // VISUALS of the mount-oriented Phase-4 parts and left THIS box
            // alone, so the drawn part and the thing you collide with pointed
            // different ways on every non-default face - the same one-side-only
            // pattern this project keeps repeating. spike, Mobility and
            // spinner* were already axis-aware (their branches return above);
            // these are the rest of the parts whose visual is built inside a
            // frame rotated onto the mount normal.
            //
            // MEASURED BEFORE, every placement made by POINTER onto a bare core,
            // box = this function x2, mesh = the real renderer bounds:
            //   wedge +Y  box 0.500x0.120x0.350   mesh 0.500x0.413x0.149
            //   wedge +X  box 0.500x0.120x0.350   mesh 0.413x0.149x0.500
            //   hook  +Y  box 0.160x0.300x0.200   mesh 0.112x0.185x0.309
            // 26 of 42 probes had the box's long/short axes disagreeing with
            // the mesh's. blade (0 of 6) and spike (0 of 6) were the controls:
            // BladeVis builds straight out of the AABB and spike has its own
            // axis branch here, and both were already clean.
            //
            // MountFrameAxis names the axis each visual rotates FROM, and
            // PartVisualFactory.MountRot/AbsRotate are the SAME two helpers the
            // visual uses, applied in the opposite direction. That is what makes
            // the agreement structural: the visual un-rotates whatever AABB it
            // is handed, this rotates the canonical box into the world, and the
            // two compose to the identity. One helper, both sides - which is
            // exactly what was missing.
            Vector3 half = s * 0.5f;   // `h` is taken by the Mobility branch above
            Vector3 from = MountFrameAxis(def.id);
            if (from != Vector3.zero && wheelAxis.sqrMagnitude > 0.01f)
                half = PartVisualFactory.AbsRotate(
                           PartVisualFactory.MountRot(from, wheelAxis), half);
            return half;
        }
    }

    const float SNAP = 0.1f;
    /// <summary>R2 (critic finding 1). Was 280 RAW pixels on a 2640 px back
    /// buffer - 10.6% of the frame - which cut "NEW ROBOT" to "NEW ROBO",
    /// "· re-entry" to "· re-en" and pushed the third garage slot off the
    /// right edge. Now measured in SCALED units (see GuiScale), so the real
    /// width is PANEL_W * GuiScale.</summary>
    const float PANEL_W = 340f;

    /// <summary>R2 (critic finding 1), and the critic's root cause was WRONG in
    /// a way that matters. The four IMGUI paths that "already scale" - ShearHud
    /// (3372), ScoutHud (4187), MobileTestHud (4222), ModeSelect.OnGUI (5053) -
    /// all use `Screen.dpi > 250 ? min(2.5, dpi/160) : 1`. Probed live in this
    /// editor, Screen.dpi reports **72**, so that expression evaluates to
    /// exactly 1 and scales NOTHING. Copying it into the career panel, as the
    /// finding asked, would have shipped a no-op that screenshots could not
    /// tell apart from the bug. The back-buffer height is the signal that is
    /// actually available here, so the dpi rule is kept (it is right on a real
    /// device that reports dpi) and a height rule is taken alongside it,
    /// whichever is larger. 1656 px tall -> 1.84x, so IMGUI's ~11 px default
    /// type reads at ~20 px.
    ///
    /// R4 (critic finding 3) - THE ROOT CAUSE OF A THREE-ROUND ARGUMENT. The
    /// dpi expression existed in SEVEN copies and R2 fixed exactly ONE (this
    /// one), so at the dpi this editor actually reports the career panel scaled
    /// and the scout screen, the fight HUD, the whole results screen, the
    /// TEST DRIVE HUD, the dev banner, ModeSelect and the touch overlay all
    /// rendered at 1x. Screen.dpi has measured 72, 266 and 108 in this one
    /// editor across four sessions - it tracks the Game view, not the project,
    /// so it is the wrong thing to scale UI by on its own. This property is now
    /// the SINGLE SOURCE OF TRUTH: the six duplicates are deleted and every
    /// call site reads GuiScale. Do not re-inline the expression anywhere.
    /// Call sites: ScoutHud, MobileTestHud, the dev/draft banner,
    /// ModeSelect.OnGUI, FightManager.UIS, Phase5Mobile PerfHUD + S.</summary>
    public static float GuiScale
    {
        get
        {
            float dpiS = Screen.dpi > 250f ? Mathf.Min(2.5f, Screen.dpi / 160f) : 1f;
            float hS   = Mathf.Clamp(Screen.height / 900f, 1f, 2.2f);
            return Mathf.Max(dpiS, hS);
        }
    }
    /// <summary>The panel's width in REAL pixels. Every mouse gate must use
    /// this, not PANEL_W, or the click-blocking region desyncs from the drawn
    /// panel by the scale factor.</summary>
    public static float PanelPixelW { get { return PANEL_W * GuiScale; } }

    public Mode mode = Mode.Build;
    public readonly List<PlacedPart> placed = new List<PlacedPart>();

    P1PartDef[] palette;
    public int selected = -1;
    /// <summary>ROUND-UP2 FIX B: the selection ghostYaw was last reset for.
    /// See UpdateBuild.</summary>
    int lastSelected = -1;
    /// <summary>Phase 3: the material every NEW placement is made of, and the
    /// one the middle-click re-material tool paints with.</summary>
    public string activeMat = "Aluminum";
    /// <summary>Phase 3 legibility: the part under the cursor right now, so the
    /// panel can name its material without the player having to click anything.
    /// Plain managed object - safe to hold across a Destroy of its GameObject.</summary>
    PlacedPart hoverPart;
    /// <summary>Phase 3 legibility: MATERIAL VIEW. Physical finish tells you what
    /// a part is MADE of the way a real part does - steel reads glossy, ABS matte -
    /// which is right for the fight but a poor AUDIT tool: titanium and aluminium
    /// are both grey metals and on a phone that difference vanishes. Material view
    /// is the deliberate opposite: one flat, maximally-distinct colour per
    /// material (Okabe-Ito, colour-blind safe) plus a legend. It swaps shared
    /// materials and puts the originals back, so toggling it can never alter the
    /// build - it is a lens, not an edit.</summary>
    bool matView;
    readonly Dictionary<Renderer, Material> matViewSaved = new Dictionary<Renderer, Material>();
    static readonly Dictionary<string, Material> auditMats = new Dictionary<string, Material>();
    static Texture2D swatchTex;
    int ghostYaw;
    GameObject ghost;                 // the REAL compound part model, mouse-following
    string ghostBuiltKey = "";        // partId_yaw the current ghost was built for
    readonly List<Material> ghostMats = new List<Material>();
    readonly List<Color> ghostBaseCols = new List<Color>();   // original tints, for re-tinting
    bool ghostTintValid = true;
    bool ghostValid;
    string ghostReason = "";          // why the ghost is red (panel hint)
    public int ghostSockets = 1;      // seam STRENGTH multiplier at the hovered snap (>=1)
    /// <summary>HONEST mated-socket count at the hovered snap. 0 means a legal
    /// OFF-SOCKET placement: it still holds, at the weakest x1 seam. Kept
    /// separate from ghostSockets so the strength number never reads as 0.</summary>
    public int ghostMated = 1;
    // Viewport feedback for placement: the target face is outlined and its
    // sockets are drawn, lit where they will actually mate. Without this,
    // sub-socket placement would just be "it moves more" with no way to tell
    // a x1 seam from a x9 one until after you commit.
    GameObject snapOverlay;
    string snapKey = "";
    Material snapPlateMat;
    readonly List<Material> snapDotMats = new List<Material>();
    readonly List<Vector2> snapDotOffs = new List<Vector2>();
    PlacedPart ghostTarget;
    Vector3 ghostPos, ghostNormal;
    GUIStyle headStyle, btnStyle, descStyle, bodyStyle, warnStyle, bigStyle, matStyle;

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
    /// <summary>Parent for the actuator motion preview - see RefreshArcPreview.</summary>
    Transform arcRoot;
    /// <summary>Markers showing what a right-click would take with it. Same
    /// ArcMark machinery as the arc preview, its own root so the two previews
    /// cannot clear each other.</summary>
    Transform doomRoot;
    PlacedPart doomShownFor;
    bool doomDirty;
    int doomCount;
    /// <summary>Undo history for the PLAYER's two destructive verbs (place and
    /// right-click-remove). Snapshot strings, not part lists: SnapshotString /
    /// LoadSnapshot is the one round-trip in this class that is already proven
    /// to carry material, drive axis and yaw, so undo cannot drift from save.
    /// Pushed at the INPUT call sites only - programmatic callers (the disc
    /// migration inside LoadSnapshot, harnesses) must not stack history.</summary>
    readonly List<string> undoStack = new List<string>();
    const int UNDO_DEPTH = 20;
    /// <summary>Same idea as arcRoot but for the part still on the cursor,
    /// plus the key it was last built for so it is not rebuilt every frame.</summary>
    Transform ghostArcRoot;
    string ghostArcKey = "";
    Vector3 driveDir = Vector3.forward;   // canonical drive direction, derived from wheels
    readonly List<Transform> polyEdges = new List<Transform>();
    readonly Dictionary<Collider, PlacedPart> byCollider = new Dictionary<Collider, PlacedPart>();

    Camera cam;
    float orbitYaw = 35f, orbitDist = 4.5f;
    /// <summary>Builder camera elevation, degrees. Was a hard-coded 30, which
    /// meant the UNDERSIDE of every part was permanently off-camera: the
    /// user-path harness probed all six faces of a core with all 18 placeable
    /// parts and -Y came back unreachable 18 times out of 18. You could not
    /// mount anything underneath anything, ever, and nothing said so - the
    /// ghost simply never appeared. W/S tilt it; those keys are throttle in the
    /// arena and were dead in the builder.</summary>
    float orbitPitch = 30f;

    public CompoundRobot testRobot;
    public RaycastWheelDrive testDrive;
    FollowCamera followCam;
    /// <summary>ROUND-3 FIX (critic MAJOR 3, the half of it round 3 owns):
    /// the desktop transient message had NO lifetime. A LoadSnapshot warning
    /// wall ("this build uses parts you don't own: 6x Aluminum Beam, ...")
    /// wrapped to six lines, sat at the top of the pinned band forever and was
    /// never re-evaluated - it was still on screen after every missing part had
    /// been granted and the fight started normally. MobileBuilderUI.cs got
    /// MSG_LIFE = 7f in round 2; this is the same rule on the desktop path.
    /// Implemented as a property rather than 40 assignment sites: every write
    /// to `message` anywhere in this file now stamps its own expiry, and
    /// `message +=` (LoadSnapshot does that twice) still works because the
    /// getter runs first and both writes land in the same frame.</summary>
    const float MSG_LIFE = 7f;
    string _message = "";
    float messageAt = -999f;
    string message
    {
        get { return (_message == null || Time.unscaledTime - messageAt > MSG_LIFE) ? "" : _message; }
        set { _message = value; messageAt = Time.unscaledTime; }
    }
    /// <summary>C2: palette index armed for a confirmed in-use sell, and the
    /// MATERIAL of that armed row - the shop lists every legal material at once
    /// now, so the index alone would arm all six of a part's rows together.</summary>
    int desktopArmSell = -1;
    string desktopArmSellMat = "";
    /// <summary>C4: desktop stable UI buffers.</summary>
    string stableNameBuf = "";
    int retireArm = -1;
    /// <summary>Blueprint index armed for deletion (arm-then-confirm).</summary>
    int bpDelArm = -1;

    // Round-1 combat fixes: builder-click grace after any full-screen UI /
    // mode transition (results buttons must never leak a placement click into
    // the build), and the test-mode arm timer for spawn protection.
    float clickGraceUntil;
    public CompoundRobot dummyRobot;
    float combatArmAt = -1f;

    // Phase 2B fight mode.
    public CompoundRobot aiRobot;
    public RaycastWheelDrive aiDrive;
    public AIController aiCtrl;
    public FightManager fight;
    FightCamera fightCam;

    // ------------------------------------------------------------------ setup

    /// <summary>TWO things that must not wait for Start (2026-07-29).
    ///
    /// Awake runs synchronously inside AddComponent; Start waits for the next
    /// FRAME - and a Unity player/editor that is not ticking has no next frame.
    /// An unfocused editor with runInBackground still false renders zero
    /// frames, so a BuilderManager created in that state sat with a NULL
    /// PALETTE indefinitely and the first LoadSnapshot threw NRE at
    /// `foreach (var d in palette)`. That is the crash on the outstanding list
    /// ("guard LoadSnapshot against a null palette"), and setting the flag in
    /// Start could never have fixed it: the flag that makes frames happen was
    /// itself waiting for a frame.
    ///
    /// Palette() is a pure factory, so building it twice is harmless and doing
    /// it here means every entry point - LoadSnapshot, OnGUI, Update - can rely
    /// on it existing from the instant the component does.</summary>
    void Awake()
    {
        Application.runInBackground = true;
        if (palette == null) palette = P1PartDef.Palette();
    }

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
        // A build_room is NOT owned by this component, so destroying a
        // BuilderManager and creating a fresh one - the standard recovery from a
        // domain reload, run dozens of times a session - left the old room and
        // all its furniture behind and the new one drew on top of it. It went
        // unnoticed until the arc preview shipped: owen would have seen TWO
        // rings, one of them stale. Adopt-or-clear before building a new one.
        foreach (var stale in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            if (stale != null && stale.name == "build_room" && stale.transform.parent == null)
                Destroy(stale);
        buildRoot = new GameObject("build_room");

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "builder_floor";
        FloorBoxCollider(floor);
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
        //
        // R5 (critic finding 1 - ROOT CAUSE). Four rounds of this review filed
        // "hard diagonal bands lie across the Workshop floor corresponding to
        // NO OBJECT in the scene", and diagnosed it as URP shadow bias/cascade
        // acne. It is not. These three 5.0 x 0.3 m emissive strips sit at
        // y = 4.35 directly above the build platform with shadowCastingMode ON,
        // and the key light is a directional at (50, 330), so the room's own
        // LIGHT FIXTURES cast three hard parallel diagonal bars onto the floor.
        // The bands do correspond to an object - one nobody looked up at.
        //
        // A light fixture casting a shadow is wrong on its own terms, so this is
        // the fix rather than a bias tweak: an emissive strip that IS the light
        // must not also block it.
        for (int i = 0; i < 3; i++)
        {
            float z = (i - 1) * 1.8f;
            var strip = PartVisualFactory.Deco(PrimitiveType.Cube, buildRoot.transform,
                new Vector3(0f, 4.35f, z), new Vector3(5f, 0.05f, 0.3f), Vector3.zero,
                PartVisualFactory.CeilingStrip, "ceiling_strip_" + i);
            var sr = strip.GetComponent<Renderer>();
            if (sr != null) sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

        // owen, 2026-07-28: "it is not very clear which direction the pivot is
        // rotating when being assembled - I only know how it moves in testing
        // mode." Parented to buildRoot so it disappears with the rest of the
        // build furniture when a fight starts.
        var ar = new GameObject("arc_preview");
        ar.transform.SetParent(buildRoot.transform, false);
        arcRoot = ar.transform;

        var dr = new GameObject("doom_preview");
        dr.transform.SetParent(buildRoot.transform, false);
        doomRoot = dr.transform;
        doomShownFor = null; doomCount = 0; doomDirty = true;

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

    PlacedPart AddPart(P1PartDef def, Vector3 pos, int yaw, Vector3 wheelAxis, PlacedPart attachTo = null,
                       string matName = null)
    {
        // Unscaled root that owns the ONE placement collider; the compound
        // visual (PartVisualFactory) lives in collider-free children. This is
        // the same visual path the arena spawn uses, so parts keep their look.
        // EffectiveMat(null) resolves to the def's OWN default material, so a
        // v1 snapshot line (no material field) restores a steel spinner as
        // steel. Only the click path below opts into activeMat.
        var part = new PlacedPart { def = def, pos = pos, yaw = yaw, wheelAxis = wheelAxis,
                                    matName = def.EffectiveMat(matName) };
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
        else if (def.id.StartsWith("spinner"))
        {
            // Spin axis = mount face normal (side = vertical blade, top =
            // horizontal disc). Same oriented-box treatment as wheels.
            Vector3 sax = wheelAxis.sqrMagnitude > 0.01f ? wheelAxis : Vector3.up;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, sax);
            box.size = def.size;
            PartVisualFactory.BuildSpinner(go.transform, def.size.x * 0.5f, def.size.y, -1);
        }
        else if (def.id == "spike")
        {
            Vector3 kax = wheelAxis.sqrMagnitude > 0.01f ? wheelAxis : Vector3.forward;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, kax);
            box.size = def.size;
            PartVisualFactory.BuildSpike(go.transform, def.size);
        }
        else
        {
            box.size = size;
            // The material's FINISH, not just its colour: the arena already
            // renders a steel chassis glossy and an ABS one matte, and the
            // builder is precisely where the player needs to tell them apart.
            // ROUND-UP2 FIX A: axis-coded id, so the drawn part points the way
            // it was bolted. See VisualId.
            // DRIVE AXIS (2026-07-28): `part.DriveAxis()`, not the raw mount
            // normal. THIS is the call site that draws a part the instant it is
            // clicked, and it was the LAST of four to be converted - owen saw a
            // spindle whose ghost stood vertical snap back to horizontal the
            // moment he placed it, because the ghost had been fixed and this had
            // not. All four VisualId call sites (AddPart here, the material
            // rebuild, the ghost, the arena spec id) now read the same thing.
            PartVisualFactory.BuildPart(VisualId(def, part.DriveAxis()), go.transform, size,
                def.category == P1Category.Control, part.Mat().color,
                part.Mat().metallic, part.Mat().smoothness);
        }
        go.transform.position = pos;
        part.go = go;

        // Connector collar at the seam with the part it snapped onto (skip
        // wheels and weapons: their roots are rotated, and BuildCollar needs
        // an un-rotated parent).
        if (attachTo != null && def.category != P1Category.Mobility && def.category != P1Category.Weapon)
            PartVisualFactory.BuildCollar(go.transform, pos, part.Half(), attachTo.pos, attachTo.Half());

        placed.Add(part);
        byCollider[box] = part;
        doomDirty = true;      // the tree changed; any cascade preview is stale
        foulDirty = true;      // and the rotor-sweep baseline is stale with it
        // A part placed while the audit lens is up must be born wearing it.
        if (matView) ApplyMatView();
        return part;
    }

    /// <summary>CASCADE REMOVAL (owen, 2026-07-29): "right clicking a component
    /// with other component connected doesn't remove it. I have to remove piece
    /// by piece from the end component, which wastes time."
    ///
    /// The old rule was "refuse anything that would orphan parts", which is the
    /// correct INVARIANT stated as the wrong VERB. Every build is a tree rooted
    /// at the core, so the invariant only ever needed the branch to go with the
    /// cut - and instead the player was handed the error and told to do the
    /// tree-walk by hand, outwards-in, one right-click per part. On a 17-part
    /// machine that is sixteen clicks in an order you have to work out yourself.
    ///
    /// What goes: `p`, plus exactly the parts whose ONLY path to the core runs
    /// through `p`. Anything double-attached elsewhere survives, so cutting one
    /// leg off a braced frame takes that leg and nothing else. The invariant is
    /// unchanged - the result is still fully connected - it is now maintained by
    /// construction rather than by refusal.
    ///
    /// `cascade: false` keeps the old leaf-only behaviour for programmatic
    /// callers (MigrateUndrivenDiscs, which swaps ONE disc mid-load and must not
    /// take a branch with it).</summary>
    void RemovePart(PlacedPart p) { RemovePart(p, true); }

    void RemovePart(PlacedPart p, bool cascade)
    {
        if (placed.Count == 0 || p == placed[0]) { message = "Can't remove the core."; return; }

        List<PlacedPart> doomed = null;
        if (cascade) doomed = DependentsOf(p);
        else
        {
            var keep = new List<PlacedPart>(placed);
            keep.Remove(p);
            if (!AllConnected(keep)) { message = "Removing that would orphan parts."; return; }
        }

        string label = p.def.label;
        int n = 1;
        DeleteOne(p);
        if (doomed != null) foreach (var q in doomed) { DeleteOne(q); n++; }

        if (matView) PruneMatView();
        message = n == 1
            ? ""
            : string.Format("Removed {0} parts \u2014 {1} and the {2} it carried.  Z = undo.",
                            n, label, n - 1);
        hoverPart = null;
        doomDirty = true;
        RefreshDoomPreview();
        RefreshOverlay();
    }

    void DeleteOne(PlacedPart p)
    {
        placed.Remove(p);
        foulDirty = true;
        foreach (var kv in new List<Collider>(byCollider.Keys))
            if (byCollider[kv] == p) byCollider.Remove(kv);
        Destroy(p.go);
    }

    /// <summary>The parts that would be orphaned by removing `p` - i.e. every
    /// part no longer reachable from the CORE once `p` is gone. Same flood fill
    /// AllConnected uses, so "what the cascade takes" and "what counts as
    /// connected" can never disagree.</summary>
    List<PlacedPart> DependentsOf(PlacedPart p)
    {
        var doomed = new List<PlacedPart>();
        if (placed.Count == 0 || p == placed[0]) return doomed;
        var keep = new List<PlacedPart>(placed);
        keep.Remove(p);
        if (keep.Count == 0) return doomed;
        // keep[0] is still the core: p is not placed[0], so index 0 survived.
        var seen = new HashSet<PlacedPart> { keep[0] };
        var stack = new Stack<PlacedPart>();
        stack.Push(keep[0]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var other in keep)
                if (!seen.Contains(other) && Touching(cur, other)) { seen.Add(other); stack.Push(other); }
        }
        foreach (var q in keep) if (!seen.Contains(q)) doomed.Add(q);
        return doomed;
    }

    // ------------------------------------------------------------------ undo

    /// <summary>Snapshot the build before a player edit. Call sites are the
    /// mouse handlers ONLY - see undoStack.</summary>
    void PushUndo()
    {
        undoStack.Add(SnapshotString());
        while (undoStack.Count > UNDO_DEPTH) undoStack.RemoveAt(0);
    }

    /// <summary>Restore the build as it was before the last player edit.</summary>
    public bool Undo()
    {
        if (undoStack.Count == 0) { message = "Nothing to undo."; return false; }
        string snap = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        // LoadSnapshot writes `message`, so say our piece after it, not before.
        int n = LoadSnapshot(snap);
        Deselect();
        hoverPart = null;
        doomDirty = true;
        message = string.Format("Undone \u2014 {0} parts restored.  {1} step(s) left.",
                                n, undoStack.Count);
        return true;
    }

    /// <summary>Red markers on everything a right-click would take. Rebuilt only
    /// when the hovered part changes or the build does - ArcMark creates real
    /// GameObjects and doing that every frame would churn.
    ///
    /// WHY THIS SHIPS WITH THE CASCADE AND NOT AFTER IT. A verb that deletes an
    /// unbounded number of parts on a single click has to say how many BEFORE
    /// the click, or the first time it surprises someone they lose work they
    /// cannot get back by looking at the screen. The count also goes on the
    /// hover readout, because markers answer "which" and the readout answers
    /// "how many" and a player mid-build wants the number.</summary>
    void RefreshDoomPreview()
    {
        if (doomRoot == null) return;
        for (int i = doomRoot.childCount - 1; i >= 0; i--)
            Destroy(doomRoot.GetChild(i).gameObject);
        doomCount = 0;
        if (mode != Mode.Build || hoverPart == null) return;
        if (placed.Count == 0 || hoverPart == placed[0]) return;

        var doomed = DependentsOf(hoverPart);
        doomCount = doomed.Count;
        if (doomCount == 0) return;

        Transform save = arcRoot; arcRoot = doomRoot;   // ArcMark parents here
        Color red = new Color(1f, 0.10f, 0.05f);
        ArcMark(hoverPart.pos, Vector3.zero, 0.11f, red, "doom_cut");
        for (int i = 0; i < doomed.Count; i++)
        {
            ArcMark(doomed[i].pos, Vector3.zero, 0.09f, red, "doom" + i);
            ArcMark((doomed[i].pos + hoverPart.pos) * 0.5f, Vector3.zero, 0.035f, red, "doomlink" + i);
        }
        arcRoot = save;
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

    /// <summary>R1-CRITIC FIX (finding 6): "Structure has floating parts" named
    /// nothing — a spike four trial placements from flush got the same message
    /// every time. Same flood as AllConnected, returns the first part the
    /// flood cannot reach so the message can point at it.</summary>
    string FirstOrphanLabel(List<PlacedPart> parts)
    {
        if (parts.Count == 0) return "";
        var seen = new HashSet<PlacedPart> { parts[0] };
        var stack = new Stack<PlacedPart>();
        stack.Push(parts[0]);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            foreach (var other in parts)
                if (!seen.Contains(other) && Touching(cur, other)) { seen.Add(other); stack.Push(other); }
        }
        foreach (var p in parts)
            if (!seen.Contains(p))
                return string.Format("{0} at ({1:F2}, {2:F2}, {3:F2})", p.def.label, p.pos.x, p.pos.y, p.pos.z);
        return "";
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

    // -------------------------------------------------- socket grid (Phase 2C)

    static readonly float[] CenterSocket = { 0f };

    /// <summary>Socket offsets along one face dimension for SNAPPING: the
    /// shared PartVisualFactory table, but degenerate faces fall back to a
    /// single center socket so the ghost always has SOMEWHERE to sit (the
    /// validity check still rejects socketless faces separately).</summary>
    static float[] FaceSockets(float dim)
    {
        float[] o = PartVisualFactory.SocketOffsets(dim);
        return o.Length > 0 ? o : CenterSocket;
    }

    /// <summary>Candidate tangent alignments = {target socket − new-part
    /// socket}; return the one nearest `want` (the mouse hit's tangent offset
    /// from the target center). Duplicated candidates are harmless here.</summary>
    static float NearestSnap(float[] tOffs, float[] nOffs, float want)
    {
        float best = 0f, bestD = float.MaxValue;
        foreach (float t in tOffs)
            foreach (float n in nOffs)
            {
                float c = t - n;
                float d = Mathf.Abs(c - want);
                if (d < bestD) { bestD = d; best = c; }
            }
        return best;
    }

    /// <summary>Where the new part sits along ONE tangent axis: ALWAYS a socket
    /// alignment, never between them.
    ///
    /// A previous version let the pointer resolve at a 0.05 m sub-socket grid,
    /// to fix the fact that any face under 0.50 m carries exactly one socket
    /// and so ignored where on it you pointed. It worked, and it was wrong:
    /// owen's build ended up with beams sitting 0.05 m below the frame plane,
    /// and an off-lattice seam is INVISIBLE after the fact - SharedSockets
    /// clamps to Max(1, total), so "no bolts mated" reports the same x1 as a
    /// legitimately single-socket seam. It also breaks the stud metaphor the
    /// whole builder rests on. Parts snap to studs. Full stop.
    ///
    /// The aiming complaint it was meant to fix is real but belongs elsewhere:
    /// the yaw cycle has a dead state, the camera cannot see the underside,
    /// and the ghost lands half its own length from the cursor.</summary>
    static float SnapAlong(float[] tOffs, float[] nOffs, float want, float tHalf)
    {
        return NearestSnap(tOffs, nOffs, want);
    }

    /// <summary>How many socket pairs coincide on one tangent axis when the
    /// new part sits `d` off the target center.</summary>
    static int MatedCount(float[] tOffs, float[] nOffs, float d)
    {
        int c = 0;
        foreach (float t in tOffs)
            foreach (float n in nOffs)
                if (Mathf.Abs(t - (d + n)) < 0.02f) c++;
        return c;
    }

    /// <summary>Per-face socket offsets of a PLACED part along tangent axis t.
    /// Wheels and weapons mate through a single center socket.</summary>
    static float[] PartFaceSockets(PlacedPart p, int t)
    {
        // Actuators are excluded: they are housings you BOLT THINGS TO, and a
        // single centre socket would make every limb hang off a x1 seam.
        if ((p.def.category == P1Category.Mobility || p.def.category == P1Category.Weapon)
            && !p.def.actuator)
            return CenterSocket;
        return FaceSockets(p.Half()[t] * 2f);
    }

    /// <summary>Mated-socket count between two touching parts: find the flush
    /// axis (same rule as Touching), then count coinciding WORLD socket
    /// coordinates per tangent axis (|a.pos[u]+ta − (b.pos[u]+nb)| &lt; 0.02);
    /// total = product of the two axis counts, clamped to ≥1 (touching parts
    /// always get at least one bolt). Feeds Conn.mult → linear, uncapped
    /// break-threshold scaling (BREAK_K stays the global tuning knob).</summary>
    public static int SharedSockets(PlacedPart a, PlacedPart b)
    {
        Vector3 ha = a.Half(), hb = b.Half();
        int flush = -1;
        for (int i = 0; i < 3; i++)
        {
            float gap = Mathf.Abs(a.pos[i] - b.pos[i]) - (ha[i] + hb[i]);
            if (gap > 0.03f) return 0;
            if (gap > -0.03f) flush = i;
        }
        if (flush < 0) return 0;
        int total = 1;
        for (int k = 1; k <= 2; k++)
        {
            int t = (flush + k) % 3;
            float[] oa = PartFaceSockets(a, t);
            float[] ob = PartFaceSockets(b, t);
            int n = 0;
            foreach (float x in oa)
                foreach (float y in ob)
                    if (Mathf.Abs(a.pos[t] + x - (b.pos[t] + y)) < 0.02f) n++;
            total *= n;
        }
        return Mathf.Max(1, total);
    }

    // ------------------------------------------------------------------ build-mode loop

    void Update()
    {
        // Same guard as OnGUI, and adding it there ALONE was a one-side-only
        // fix - my own. A BuilderManager that survives a domain reload comes
        // back with a null palette; OnGUI stopped flooding and Update took over,
        // NRE-ing at `palette[selected]` in UpdateGhost every frame and stalling
        // the MCP bridge outright. Both entry points now refuse to run.
        if (palette == null)
        {
            if (!warnedUnbuilt)
            {
                warnedUnbuilt = true;
                CompoundRobot.Log("BuilderManager: palette is null (survived a domain reload?) - "
                    + "not updating. Destroy this BuilderManager and create a fresh one.");
            }
            return;
        }
        PumpBuildMusic();
        PumpUiFraming();
        if (mode == Mode.Build) UpdateBuild();
        else if (mode == Mode.Test) UpdateTest();
        else UpdateFight();
    }

    // ---- BUILD-mode music (OWEN 2026-08-02) --------------------------------
    /// <summary>owen: "Use this music as the background music in BUILD model".
    /// His Steel Atrium.wav, transcoded to mp3 and dropped in Resources beside
    /// FightTheme so it ships in device builds - a 34 MB 48 kHz stereo WAV in
    /// Resources would be carried into every build and into git, where the
    /// whole repo is currently 3.3 MB.
    ///
    /// Driven by ONE rule here rather than by hooks in StartFight, StartTest,
    /// BackToBuild, StartCareerFight and EndScout. That is five call sites
    /// today and a sixth would eventually be added without the music hook -
    /// the same class of defect as a button whose look drifts from what it
    /// does. `mode` is the truth; the music simply follows it.
    ///
    /// PAUSE, not Stop, on leaving Build: coming back from a fight resumes the
    /// track where it left off instead of restarting a three-minute piece from
    /// the top every single time you return to the workshop.</summary>
    public static float BUILD_MUSIC_VOL = 0.40f;

    /// <summary>OWEN 2026-08-04: "Adding two new songs for the build mode too.
    /// Similar as fighting mode, randomly shuffle the three songs in build mode
    /// and play them one by one."
    ///
    /// Note the word ONE BY ONE - this is deliberately NOT what fight mode
    /// does. A fight draws one track at random and loops it, because a fight is
    /// two minutes long and ends. The workshop is where the hours go, so a
    /// single track on repeat is the thing you eventually mute. This is a
    /// PLAYLIST: shuffle the three, play them through in that order, reshuffle
    /// and go again.
    ///
    /// Resources names, so they ship in device builds. Adding a fourth track is
    /// one line here plus the file - nothing else in this class counts to
    /// three.</summary>
    public static readonly string[] BUILD_THEMES =
    { "BuildTheme", "BuildTheme_NeonAtriumDrift", "BuildTheme_CircuitGarden" };

    /// <summary>What is playing right now, by Resources name - "" when the
    /// workshop is silent. Exposed for the smoke suite and for a probe, which
    /// otherwise has no way to ask.</summary>
    public static string buildTrackNow = "";

    AudioSource buildMusic;
    bool buildMusicMissing;

    /// <summary>Indices into BUILD_THEMES, in the order this pass will play
    /// them. buildPos is the one currently sounding.</summary>
    readonly List<int> buildOrder = new List<int>();
    int buildPos = -1;

    /// <summary>Did WE pause it (left the workshop), as opposed to the track
    /// simply having ended? AudioSource.isPlaying is false in both cases and
    /// the old code conflated them - fine when the source looped forever, but
    /// with a playlist "not playing" has to mean two different things, and
    /// calling UnPause on a finished clip does nothing at all. That would have
    /// presented as the music stopping for good after the first song.</summary>
    bool buildMusicPaused;

    /// <summary>Fisher-Yates over the whole list, then one fix-up: if the new
    /// first track is the one that just finished, swap it away. Without that,
    /// a fresh shuffle can legally put the same song back-to-back across the
    /// seam between passes - which sounds exactly like the bug this replaced.</summary>
    void ShuffleBuildOrder()
    {
        int last = (buildPos >= 0 && buildPos < buildOrder.Count) ? buildOrder[buildPos] : -1;
        buildOrder.Clear();
        for (int i = 0; i < BUILD_THEMES.Length; i++) buildOrder.Add(i);
        for (int i = buildOrder.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            int t = buildOrder[i]; buildOrder[i] = buildOrder[j]; buildOrder[j] = t;
        }
        if (buildOrder.Count > 1 && buildOrder[0] == last)
        {
            int k = UnityEngine.Random.Range(1, buildOrder.Count);
            int t = buildOrder[0]; buildOrder[0] = buildOrder[k]; buildOrder[k] = t;
        }
        buildPos = -1;
    }

    /// <summary>Advance to the next track and start it. Reshuffles at the end
    /// of a pass.
    ///
    /// A clip that will not load is SKIPPED rather than fatal: one missing mp3
    /// used to mean a silent workshop, and now it just means a shorter
    /// playlist. Returns false only when none of them load, which is the one
    /// case worth a log line.</summary>
    bool NextBuildTrack()
    {
        for (int guard = 0; guard < BUILD_THEMES.Length + 1; guard++)
        {
            if (buildOrder.Count == 0 || buildPos + 1 >= buildOrder.Count) ShuffleBuildOrder();
            buildPos++;
            if (buildPos >= buildOrder.Count) break;   // nothing to play at all
            string name = BUILD_THEMES[buildOrder[buildPos]];
            var clip = Resources.Load<AudioClip>(name);
            if (clip == null)
            {
                CompoundRobot.Log("build track missing from Resources: " + name + " - skipping it");
                buildOrder.RemoveAt(buildPos);
                buildPos--;
                if (buildOrder.Count == 0) break;
                continue;
            }
            buildMusic.clip = clip;
            buildMusic.time = 0f;
            buildMusic.Play();
            buildMusicPaused = false;
            buildTrackNow = name;
            return true;
        }
        buildTrackNow = "";
        return false;
    }

    /// <summary>Smoke hook: advance the playlist by hand and report what came
    /// up. The suite asserts against the REAL shuffle rather than a copy of it,
    /// because a re-implemented shuffle in the test proves only that the test
    /// can shuffle.</summary>
    public string DebugNextBuildTrack()
    {
        if (buildMusic == null)
        {
            buildMusic = gameObject.AddComponent<AudioSource>();
            buildMusic.loop = false;
            buildMusic.spatialBlend = 0f;
            buildMusic.playOnAwake = false;
        }
        NextBuildTrack();
        return buildTrackNow;
    }

    /// <summary>How far into the current pass we are; 0 is the first track of a
    /// freshly shuffled pass. The suite needs this to line its window up with a
    /// pass BOUNDARY - by the time it runs, the workshop has been open for
    /// minutes and the playlist is somewhere in the middle of a pass, so six
    /// advances from wherever-we-are straddle two passes and "every track once
    /// per pass" is not even a meaningful claim about them.</summary>
    public int DebugBuildPos { get { return buildPos; } }

    void PumpBuildMusic()
    {
        if (buildMusicMissing) return;
        if (buildMusic == null)
        {
            buildMusic = gameObject.AddComponent<AudioSource>();
            buildMusic.loop = false;          // a playlist, not one track on repeat
            buildMusic.spatialBlend = 0f;     // 2D: same in both ears, everywhere
            buildMusic.playOnAwake = false;
        }
        buildMusic.volume = BUILD_MUSIC_VOL;   // live-tunable from a probe

        if (mode == Mode.Build)
        {
            if (buildMusicPaused) { buildMusic.UnPause(); buildMusicPaused = false; }
            // Covers both "nothing has started yet" and "the last one ended".
            else if (!buildMusic.isPlaying && !NextBuildTrack())
            {
                buildMusicMissing = true;
                CompoundRobot.Log("no build theme found in Resources - the build screen is silent");
            }
        }
        else if (buildMusic.isPlaying)
        {
            // PAUSE, not Stop: coming back from a fight resumes the track where
            // it left off instead of restarting a three-minute piece from the
            // top every single time you return to the workshop.
            buildMusic.Pause();
            buildMusicPaused = true;
        }
    }

    bool camShifted;

    /// <summary>Frame the robot in the part of the screen you can actually SEE.
    ///
    /// OWEN 2026-08-04: "Looks like the menu partially blocks the building
    /// area." The dock's height was half the story; the other half is that this
    /// camera aimed at the centre of the full viewport, which on a phone is a
    /// point underneath the dock. The machine was being deliberately posed
    /// behind the furniture.
    ///
    /// This is a LENS SHIFT, not a camera move. Moving the camera down would
    /// also frame the robot higher, but it changes the angle you view it from -
    /// you would start seeing the underside, and near the floor it would clip
    /// through. An off-centre frustum slides the framing without touching the
    /// eye position, which is what a tilt-shift lens does and exactly what is
    /// wanted: same shot, different part of the film.
    ///
    /// TRAP worth knowing: overriding projectionMatrix can desynchronise
    /// ScreenPointToRay, and ScreenPointToRay is how parts get placed - a
    /// silent version of this bug would leave the robot looking right and
    /// dropping parts a few centimetres from the finger. Verified by
    /// round-tripping a known world point through WorldToScreenPoint and back;
    /// see the R1 evidence in the critic-loop doc.
    ///
    /// Driven by `mode` in one place, like the music, so a future mode cannot
    /// forget to reset it.</summary>
    void PumpUiFraming()
    {
        float below = 0f, above = 0f;
        if (mode == Mode.Build && MobileBuilderUI.Active)
        {
            below = MobileBuilderUI.coverBottom;
            above = MobileBuilderUI.coverTop;
        }

        // Centre of the visible band, as an offset from the centre of the screen.
        float delta = (below - above) * 0.5f;

        if (Mathf.Abs(delta) < 0.002f)
        {
            if (camShifted) { cam.ResetProjectionMatrix(); camShifted = false; }
            return;
        }

        float t = cam.nearClipPlane * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float aspect = cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : 1.777f;
        float r = t * aspect;
        float s = 2f * t * delta;   // shift the window DOWN so the subject rides UP
        cam.projectionMatrix = Matrix4x4.Frustum(-r, r, -t - s, t - s,
                                                 cam.nearClipPlane, cam.farClipPlane);
        camShifted = true;
    }

    void UpdateBuild()
    {
        // Orbit camera.
        orbitYaw += Phase0Input.OrbitAxis() * 80f * Time.deltaTime;
        orbitDist = Mathf.Clamp(orbitDist - Phase0Input.Scroll() * 0.5f, 2.2f, 9f);
        Vector3 target = new Vector3(0f, 0.7f, 0f);
        orbitPitch = Mathf.Clamp(orbitPitch - Phase0Input.Throttle() * 55f * Time.deltaTime, -70f, 80f);
        Quaternion rot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        cam.transform.position = target + rot * new Vector3(0f, 0f, -orbitDist);
        cam.transform.LookAt(target + Vector3.up * 0.1f);

        // ROUND-UP2 FIX B (round-2 critic MODERATE: "ghostYaw leaks across
        // palette selections"). ghostYaw is a property of the part you are
        // holding, but it lived on across a change of part, so an R pressed on
        // a beam silently rotated the NEXT thing you picked up. MEASURED
        // before this, through the pointer: one R on a beam left ghostYaw 90,
        // and the next selection (long beam, then engine) both started at 90
        // rather than 0 - an orientation the player never asked for and, on a
        // weapon, one they cannot see the point of. Reset on the transition
        // rather than in the palette button handler, so it is also right for
        // any other path that changes `selected`.
        if (selected != lastSelected) { ghostYaw = 0; lastSelected = selected; }

        if (Phase0Input.RotateDown())
        {
            // Structural parts (beam, plate) cycle 4 orientations including
            // VERTICAL (stood-up) states; everything else keeps the flat
            // 0/90 toggle (weapon/wheel orientation comes from the mount face).
            bool full = selected >= 0 && palette[selected].category == P1Category.Structural;
            // ACTUATOR DRIVE AXIS: R was a DEAD KEY on pivot/spindle/ram - it
            // toggled a yaw nothing read. It now steps the drive axis through
            // all four states, starting from "follow the mount face" so the
            // first press is a visible change from the legacy default.
            bool act = selected >= 0 && palette[selected].actuator;
            if (act)
            {
                // Fix 2026-07-29 (playtest): on a top face, yaw 0 (follow the
                // face = Y) and yaw 180 (Y axle) are the SAME axis, so one R
                // press per cycle read as a dead key. Step until the drive axis
                // actually changes; at most one full turn.
                var adef = palette[selected];
                Vector3 a0 = new PlacedPart { def = adef, yaw = ghostYaw, wheelAxis = ghostNormal }.DriveAxis();
                int gy = ghostYaw;
                for (int i = 0; i < 4; i++)
                {
                    gy = (gy + 90) % 360;
                    Vector3 a1 = new PlacedPart { def = adef, yaw = gy, wheelAxis = ghostNormal }.DriveAxis();
                    if ((a1 - a0).sqrMagnitude > 1e-4f) break;
                }
                ghostYaw = gy;
            }
            else if (!full) ghostYaw = (ghostYaw == 0 ? 90 : 0);
            else
            {
                // ROUND-UP1 FIX E: advance to the next yaw that is actually a
                // DIFFERENT SHAPE, so no R press is a no-op. Half() maps
                // yaw 270 (x,y,z)->(y,x,z), which does nothing to a beam
                // (0.20,0.20,0.60), and yaw 90 (x,y,z)->(z,y,x), which does
                // nothing to a plate (0.50,0.06,0.50). MEASURED before this,
                // stepping the full cycle and reading the ghost's RENDERED
                // bounds: beam, beamlong and plate each showed 4 yaw states
                // but only 3 distinct meshes - one dead press per part per
                // cycle; chassis (0.40,0.30,0.50) showed 4 and 4 and is
                // unchanged by this. The loop runs at most one full turn, so a
                // part that is symmetric in every axis simply keeps its yaw
                // instead of spinning forever.
                var rdef = palette[selected];
                Vector3 h0 = new PlacedPart { def = rdef, yaw = ghostYaw }.Half();
                int ry = ghostYaw;
                bool moved = false;
                for (int i = 0; i < 4; i++)
                {
                    ry = (ry + 90) % 360;
                    if ((new PlacedPart { def = rdef, yaw = ry }.Half() - h0).sqrMagnitude > 1e-6f)
                    { moved = true; break; }
                }
                // A part that is the SAME SHAPE at all four yaws - the bracket
                // is a 0.20 m cube - has nothing to skip TO. Caught by the
                // regression sweep: without this fallback the loop found no
                // different shape and left the yaw alone, so the bracket went
                // from four states to ONE and R stopped recording anything at
                // all. Falling back to the plain +90 keeps the pre-existing
                // behaviour exactly for those parts (it was already invisible
                // in the drawing - MEASURED, bracket ghost bounds are
                // 0.218 cubed at every yaw) while beam/beamlong/plate keep the fix.
                ghostYaw = moved ? ry : (ghostYaw + 90) % 360;
            }
        }
        if (Phase0Input.TestDown()) { StartTest(); return; }
        if (Phase0Input.FlipDown()) { Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1; StartFight(); return; }  // F = exhibition FIGHT
        // Esc = drop the selected part / ghost. Round-2 fix 5: ALSO handled
        // through the IMGUI event pipeline in OnGUI — in the editor the input
        // backends can swallow Escape (it doubles as the cursor-release key),
        // which left the ghost armed; IMGUI receives the KeyDown regardless.
        if (Phase0Input.EscDown()) Deselect();

        UpdateGhost();

        Vector3 m = Phase0Input.MousePos();
        bool overPanel = uiPointerBlocked || (!MobileBuilderUI.Active && m.x < PanelPixelW);   // Phase 5: also block under mobile UI. R2: PanelPixelW, not PANEL_W - the panel is drawn scaled.
        // Hover readout: one raycast a frame answers "what is THAT part made
        // of" with no click, no mode change and no selection side effects.
        hoverPart = overPanel ? null : PartUnderMouse();
        // Rebuild the cascade preview only on a real change - see RefreshDoomPreview.
        if (hoverPart != doomShownFor || doomDirty)
        {
            doomShownFor = hoverPart; doomDirty = false;
            RefreshDoomPreview();
        }
        // Click grace (round-1 fix 3): clicks within a beat of returning from
        // a full-screen UI (results screen, mode switch) belong to that UI —
        // they must never fall through into placement/removal raycasts.
        bool clicksLive = Time.unscaledTime >= clickGraceUntil;

        // ROUND-UP3 FIX B (round-3 critic MAJOR: "debug click seam is not
        // faithful - clicks leak to a later frame"). These three reads USED to
        // sit last in their conditions, behind `!overPanel && clicksLive`. C#
        // short-circuits, so with the pointer over the palette MouseDown was
        // never called, Phase0Input.TakeClick never consumed the pending debug
        // click, and it fired on some later frame at a place nothing had
        // clicked. A real mouse is unaffected - Input.GetMouseButtonDown expires
        // with the frame whether or not anyone asks - but every harness on this
        // project drives the pointer through this seam, so the bug was in the
        // EVIDENCE, which is worse.
        // MEASURED BEFORE, 3 of 3 repeats: click posted with the pointer over
        // the panel placed nothing (correct), then moving onto the model with NO
        // new click placed a beam anyway. AFTER: 0 of 3, with the control (a
        // click actually on the face) still placing - so this cannot be passed
        // by a "fix" that merely swallows clicks.
        // Reading them here means a click is consumed on the frame it happened,
        // exactly like the real one, and the gates below decide only what it
        // DOES - which is the order the conditions should always have been in.
        bool down0 = Phase0Input.MouseDown(0);
        bool down1 = Phase0Input.MouseDown(1);
        bool down2 = Phase0Input.MouseDown(2);

        if (Phase0Input.UndoDown() && clicksLive) Undo();

        if (down0 && !overPanel && clicksLive && selected >= 0 && ghostValid && !CareerAllows(selected))
        {
            // C1: the placement is legal but the shelf is empty - refuse in the
            // same amber channel every other refusal uses.
            var cdef = palette[selected];
            message = "No " + MatDB.Get(cdef.EffectiveMat(activeMat)).name + " " + cdef.label
                    + " left \u2014 shop or sell-back.";
            SfxSynth.Deny();
        }
        else if (down0 && !overPanel && clicksLive && selected >= 0 && ghostValid)
        {
            PushUndo();
            AddPart(palette[selected], ghostPos, ghostYaw,
                    NeedsAxis(palette[selected]) ? ghostNormal : Vector3.zero,
                    ghostTarget, activeMat);
            message = "";
            RefreshOverlay();
            SfxSynth.Place();
        }
        else if (down0 && !overPanel && clicksLive && selected >= 0 && !ghostValid)
        {
            SfxSynth.Deny();   // clicked, but the ghost is red - say no out loud
            // ...and say WHY, in words, on every UI (owen, 2026-08-05).
            //
            // ghostReason had exactly ONE reader: an IMGUI label in the desktop
            // panel. The touch UI pumps `message` and has never read ghostReason
            // at all, so on a phone EVERY red-ghost refusal - "Too low", "No
            // socket on this face", "Over budget", and now the rotor sweep - was
            // a red shape and a buzz with no words anywhere. Found by hovering
            // the real ghost rather than by asking the rule whether it fired;
            // the same blind spot that shipped four defects on 2026-08-04.
            //
            // The rotor rule is the one that needed this most: "why is it red?"
            // has a genuinely invisible answer - a circle that is not drawn.
            // Routing it through `message` reuses the channel the empty-shelf
            // refusal two branches up already uses, so the touch status bar and
            // the desktop panel now say the same thing for the same click.
            if (ghostReason.Length > 0) message = ghostReason;
        }
        if (down1 && !overPanel && clicksLive)
        {
            var hitPart = PartUnderMouse();
            if (hitPart != null)
            {
                // Push BEFORE the edit and only if the edit can actually happen,
                // so Z never burns a step on a refused core click.
                if (hitPart != placed[0]) PushUndo();
                if (hitPart != placed[0]) SfxSynth.Remove();
                RemovePart(hitPart);
            }
        }
        // Phase 3: middle-click repaints one placed part in the active
        // material. Re-places it through the SAME AddPart path so mass, cost,
        // durability, seam strength and the visual all re-derive together.
        if (down2 && !overPanel && clicksLive)
        {
            var hitPart = PartUnderMouse();
            if (hitPart != null) SetPartMaterial(hitPart, activeMat);
        }
    }

    /// <summary>Phase 3: change one placed part's material in place. The part
    /// keeps its position, yaw, mount axis and place in the build order; only
    /// the §4.3 numbers and its colour change.</summary>
    public bool SetPartMaterial(PlacedPart p, string mat)
    {
        if (p == null) return false;
        string want = p.def.EffectiveMat(mat);
        if (want == p.MatName()) return false;
        if (!placed.Contains(p)) return false;
        if (!CareerAllowsMat(p, want))
        {
            // C1: repaint is a transmute - it needs a spare of the target
            // material in stock, or Steel could be conjured from Aluminium.
            message = "No " + MatDB.Get(want).name + " " + p.def.label + " in stock \u2014 shop or sell-back.";
            SfxSynth.Deny();
            return false;
        }

        p.matName = want;

        // Rebuild the visual CHILDREN in place. Going through RemovePart +
        // AddPart would be refused for the core and for any interior part
        // (removing it would orphan the build), so the root GameObject, its
        // placement collider and the byCollider mapping all stay put and only
        // the model underneath is regenerated.
        var go = p.go;
        var def = p.def;
        for (int i = go.transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(go.transform.GetChild(i).gameObject);

        if (def.category == P1Category.Mobility)
            PartVisualFactory.BuildWheel(go.transform, def.size.x * 0.5f, def.size.y, -1);
        else if (def.id.StartsWith("spinner"))
            PartVisualFactory.BuildSpinner(go.transform, def.size.x * 0.5f, def.size.y, -1);
        else if (def.id == "spike")
            PartVisualFactory.BuildSpike(go.transform, def.size);
        else
        {
            // ROUND-UP2 FIX A: a repaint regenerates the model from scratch,
            // so it has to re-state the mount axis or the part would snap back
            // to the fallback orientation the moment the player recoloured it.
            PartVisualFactory.BuildPart(VisualId(def, p.DriveAxis()), go.transform, p.Half() * 2f,
                def.category == P1Category.Control, p.Mat().color,
                p.Mat().metallic, p.Mat().smoothness);
            // Re-derive the connector collar from whatever it is touching.
            PlacedPart attach = null;
            foreach (var q in placed) if (q != p && Touching(p, q)) { attach = q; break; }
            if (attach != null)
                PartVisualFactory.BuildCollar(go.transform, p.pos, p.Half(), attach.pos, attach.Half());
        }

        // The rebuild above destroyed the children the lens had saved; re-apply
        // it so the recoloured part immediately shows its NEW audit colour.
        if (matView) ApplyMatView();
        message = def.label + " -> " + MatDB.Get(want).name;
        RefreshOverlay();
        return true;
    }

    /// <summary>Phase 3 convenience: repaint the whole build (minus parts
    /// pinned to a default material) so a titanium-vs-steel chassis is one
    /// click to compare.</summary>
    public int SetAllMaterials(string mat)
    {
        int n = 0;
        var snapshot = new List<PlacedPart>(placed);
        foreach (var p in snapshot) if (SetPartMaterial(p, mat)) n++;
        message = n + " part(s) -> " + MatDB.Get(mat).name;
        return n;
    }

    // ---- Phase 3 legibility: MATERIAL VIEW -----------------------------------

    static Material AuditMat(string key)
    {
        Material m;
        if (auditMats.TryGetValue(key, out m) && m != null) return m;
        // Flat and unlit-looking on purpose: a specular highlight would make two
        // swatches read as different colours depending which way a part faces.
        m = PartVisualFactory.Mat(MatDB.Get(key).auditColor, 0f, 0.12f);
        auditMats[key] = m;
        return m;
    }

    static Texture2D SwatchTex()
    {
        if (swatchTex == null)
        {
            swatchTex = new Texture2D(1, 1);
            swatchTex.SetPixel(0, 0, Color.white);
            swatchTex.Apply();
            swatchTex.hideFlags = HideFlags.HideAndDontSave;
        }
        return swatchTex;
    }

    public void SetMatView(bool on)
    {
        if (on == matView) { if (on) ApplyMatView(); return; }
        matView = on;
        if (on) ApplyMatView(); else ClearMatView();
    }

    public bool MatViewOn() { return matView; }

    /// <summary>Paint every live part in its material's audit colour, saving what
    /// it was wearing first. Idempotent: a renderer already on file keeps its
    /// ORIGINAL entry, so a second pass can never record an audit colour as the
    /// thing to restore - that bug would stay invisible until the toggle went off.</summary>
    void ApplyMatView()
    {
        PruneMatView();
        foreach (var p in placed)
        {
            if (p.go == null) continue;
            var am = AuditMat(p.MatName());
            foreach (var r in p.go.GetComponentsInChildren<Renderer>(true))
            {
                if (!matViewSaved.ContainsKey(r)) matViewSaved[r] = r.sharedMaterial;
                r.sharedMaterial = am;
            }
        }
    }

    void ClearMatView()
    {
        foreach (var kv in matViewSaved)
            if (kv.Key != null) kv.Key.sharedMaterial = kv.Value;
        matViewSaved.Clear();
    }

    /// <summary>Forget renderers destroyed underneath us - RemovePart and
    /// SetPartMaterial both delete child objects. Rebuilt rather than Remove()d:
    /// a destroyed UnityEngine.Object is == null but is still a live dictionary
    /// key, and Remove(null) would throw.</summary>
    void PruneMatView()
    {
        if (matViewSaved.Count == 0) return;
        var live = new Dictionary<Renderer, Material>();
        foreach (var kv in matViewSaved) if (kv.Key != null) live[kv.Key] = kv.Value;
        if (live.Count == matViewSaved.Count) return;
        matViewSaved.Clear();
        foreach (var kv in live) matViewSaved[kv.Key] = kv.Value;
    }

    // ---- snap overlay ----------------------------------------------------
    // Draws the face you are bolting to and the sockets on it, LIT where a
    // socket on the incoming part will actually line up. This is the readout
    // that makes sub-socket placement legible: nudge until more dots light and
    // the seam gets stronger, on screen, before you commit.

    void HideSnapOverlay()
    {
        if (snapOverlay != null && snapOverlay.activeSelf) snapOverlay.SetActive(false);
    }

    static Material OverlayMat()
    {
        var m = PartVisualFactory.Mat(Color.white, 0f, 0.2f);
        m.EnableKeyword("_EMISSION");
        return m;
    }

    static void Tint(Material m, Color c, float emit)
    {
        m.color = c;
        m.SetColor("_EmissionColor", c * emit);
    }

    void ShowSnapOverlay(PlacedPart t, int axis, float sign, int t1, int t2,
                         float[] tu, float[] tv, float[] nu, float[] nv,
                         float du, float dv, bool valid)
    {
        if (t == null) { HideSnapOverlay(); return; }
        Vector3 th = t.Half();

        // Rebuild only when the FACE changes - the tints below update every
        // frame, so a rebuild per frame would churn materials for nothing.
        string key = t.go.GetEntityId() + "|" + axis + "|" + (sign > 0f ? 1 : 0)
                   + "|" + tu.Length + "x" + tv.Length
                   + "|" + th[t1].ToString("F2") + "x" + th[t2].ToString("F2");
        if (snapOverlay == null || snapKey != key)
        {
            if (snapOverlay != null) Destroy(snapOverlay);
            snapDotMats.Clear();
            snapDotOffs.Clear();
            snapOverlay = new GameObject("snapOverlay");

            Vector3 plate = Vector3.one;
            plate[t1] = th[t1] * 2f;
            plate[t2] = th[t2] * 2f;
            plate[axis] = 0.005f;
            snapPlateMat = OverlayMat();
            PartVisualFactory.Deco(PrimitiveType.Cube, snapOverlay.transform,
                Vector3.zero, plate, Vector3.zero, snapPlateMat, "face");

            foreach (float a1 in tu)
                foreach (float a2 in tv)
                {
                    Vector3 lp = Vector3.zero;
                    lp[t1] = a1;
                    lp[t2] = a2;
                    var dm = OverlayMat();
                    snapDotMats.Add(dm);
                    snapDotOffs.Add(new Vector2(a1, a2));
                    PartVisualFactory.Deco(PrimitiveType.Sphere, snapOverlay.transform,
                        lp, Vector3.one * 0.055f, Vector3.zero, dm, "dot");
                }
            snapKey = key;
        }

        if (!snapOverlay.activeSelf) snapOverlay.SetActive(true);
        Vector3 c = t.pos;
        c[axis] += sign * (th[axis] + 0.004f);
        snapOverlay.transform.position = c;

        Tint(snapPlateMat, valid ? new Color(0.30f, 0.70f, 0.40f) : new Color(0.75f, 0.25f, 0.20f),
             valid ? 0.28f : 0.35f);

        for (int i = 0; i < snapDotMats.Count; i++)
        {
            bool m1 = false, m2 = false;
            foreach (float n in nu) if (Mathf.Abs(snapDotOffs[i].x - (du + n)) < 0.02f) { m1 = true; break; }
            foreach (float n in nv) if (Mathf.Abs(snapDotOffs[i].y - (dv + n)) < 0.02f) { m2 = true; break; }
            bool lit = valid && m1 && m2;
            Tint(snapDotMats[i], lit ? new Color(0.35f, 1f, 0.45f) : new Color(0.35f, 0.37f, 0.40f),
                 lit ? 1.4f : 0.05f);
        }
    }

    // ---- Phase 4: weapon-assembly feedback ---------------------------------

    /// <summary>What the build screen can tell the player about one actuator
    /// before they commit to a fight.</summary>
    public struct LimbInfo
    {
        public PlacedPart act;
        public int parts;
        /// <summary>The actual parts this actuator drives. Carried so a caller
        /// can ask "is THIS part driven, and by what kind of actuator?" - which
        /// is what the undriven-disc warning needs and what `parts` (a count)
        /// could never answer.</summary>
        public List<PlacedPart> memberParts;
        public float inertia, tipRadius, rateMax, energyJ, kjPerSwing;
        /// <summary>Round-3-critic CRITICAL 2: the material tip-speed ceiling
        /// this limb is quoted at, m/s. Carried so the endurance estimate and
        /// the fight agree on which motor/edge they are talking about.</summary>
        public float tipSpeedCap;
        /// <summary>Non-null: this actuator cannot move, and this names the seam
        /// holding it.</summary>
        public string blockedBy;
        /// <summary>ROUND-UP2 FIX D (round-2 critic MAJOR: "builder promises a
        /// hammer arc the arena delivers at 11-21%"). How much of its travel
        /// this limb can actually use before it reaches the deck, and that as a
        /// fraction of the full arc/stroke. 1.0 = clear all the way. Radians
        /// for a pivot, metres for a ram; a spindle is exempt and reports 1.0,
        /// exactly as Actuator.GroundBlocked exempts it.</summary>
        public float arcFree, arcLimit, arcFrac;
        /// <summary>+1 or -1: WHICH WAY this actuator sweeps. Rebuilt from
        /// the same rule Actuator.PickArcSign uses in the arena, so the
        /// builder's preview arrow points where the limb will really go.</summary>
        public float arcSign;
        /// <summary>ROUND-1-DEV. How far the swept circle of a SPINDLE's rotor
        /// reaches below the deck clearance band, metres; 0 = clear. arcFrac
        /// cannot express this: a spindle turns continuously, so it is exempt
        /// from the arc guard and always reports arcFrac 1.0. That exemption is
        /// correct in the ARENA (Actuator.GroundBlocked: "a rotor built to sweep
        /// through the deck is a build error the arc picker cannot fix either")
        /// but it left the player with no way to see the build error at all.
        /// Measured, not assumed: the shipped SPINDLE recipe - owen's own
        /// qa_owen_build_SPINDLE, and the tipper/widowmaker roster bots - puts
        /// the disc's underside at 0.000 m of ground clearance in the arena
        /// against a GROUND_CLEAR band of 0.020, so the source comment's "no
        /// measured build does it" is no longer true. Prediction only; the
        /// arena's guard is untouched.</summary>
        public float rotorDip;
    }

    /// <summary>Partition `placed` at every actuator the same way
    /// Actuator.Wire partitions the arena graph, and additionally NAME THE SEAM
    /// when an intended limb turns out to be welded to the frame.
    ///
    /// A limb is only a limb if the actuator is its only route to the core. The
    /// arena is right to insist on that - a part bolted to the frame at two
    /// points cannot swing - but the builder used to say nothing, and the
    /// failure is invisible: the hammer looks perfect and simply never moves.
    /// Two of the first three test hammers were built this way, including one
    /// where the arm and the frame shared nothing but a zero-width EDGE, which
    /// Touching() accepts. Nobody is going to spot that by eye.
    ///
    /// The mount face gives us intent for free: an actuator's wheelAxis is its
    /// OUTWARD mount normal, so the neighbour on the -axis side is what it is
    /// bolted TO, and anything else bolted to it was meant to swing.</summary>
    public List<LimbInfo> LimbReport()
    {
        var outp = new List<LimbInfo>();
        var body = new List<PlacedPart>();
        foreach (var p in placed) if (p.def.category != P1Category.Mobility) body.Add(p);
        int n = body.Count;
        if (n == 0) return outp;

        var isAct = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++) if (body[i].def.actuator) { isAct[i] = true; any = true; }
        if (!any) return outp;

        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Touching(body[i], body[j])) { adj[i].Add(j); adj[j].Add(i); }

        int core = 0;
        for (int i = 0; i < n; i++) if (body[i] == placed[0]) core = i;

        // Flood from the core WITHOUT crossing an actuator, keeping parents so a
        // blocked limb can be told exactly which seam is holding it.
        var parent = new int[n];
        var seen = new bool[n];
        for (int i = 0; i < n; i++) parent[i] = -1;
        var q = new List<int>();
        if (!isAct[core]) { seen[core] = true; q.Add(core); }
        for (int h = 0; h < q.Count; h++)
            foreach (int nb in adj[q[h]])
            {
                if (isAct[nb] || seen[nb]) continue;
                seen[nb] = true; parent[nb] = q[h]; q.Add(nb);
            }

        for (int a = 0; a < n; a++)
        {
            if (!isAct[a]) continue;
            var info = new LimbInfo();
            info.act = body[a];
            var members = new List<int>();
            string blocked = null;
            foreach (int nb in adj[a])
            {
                if (isAct[nb]) continue;
                Vector3 d = body[nb].pos - body[a].pos;
                if (d.sqrMagnitude > 1e-6f
                    && Vector3.Dot(d.normalized, body[a].wheelAxis) < -0.5f) continue;  // the host
                if (seen[nb])
                {
                    // Reachable from the core without passing through this
                    // actuator, so it is frame, not limb. parent[] gives the
                    // first seam on its path home - the one to move.
                    if (blocked == null && parent[nb] >= 0)
                        blocked = body[nb].def.label + " ↔ " + body[parent[nb]].def.label;
                    continue;
                }
                var stack = new List<int>();
                stack.Add(nb);
                while (stack.Count > 0)
                {
                    int cur = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    if (members.Contains(cur)) continue;
                    members.Add(cur);
                    foreach (int m in adj[cur]) if (!isAct[m] && !seen[m]) stack.Add(m);
                }
            }

            info.parts = members.Count;
            // LimbInfo is a STRUCT, so this cannot be a field initializer
            // (CS8983/CS8773 under C# 9) - it is allocated per report here.
            info.memberParts = new List<PlacedPart>();
            for (int i = 0; i < members.Count; i++) info.memberParts.Add(body[members[i]]);
            info.blockedBy = members.Count == 0 ? blocked : null;
            var pos = new Vector3[members.Count];
            var mass = new float[members.Count];
            var half = new float[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                pos[i] = body[members[i]].pos - body[a].pos;
                mass[i] = body[members[i]].Mass();
                Vector3 h = body[members[i]].Half();
                half[i] = Mathf.Max(h.x, Mathf.Max(h.y, h.z));
            }
            var kind = Actuator.KindOfId(body[a].def.id);
            // DRIVE AXIS, not the mount face. Missing this was a one-side-only
            // fix: with a chosen axis the arena spins about DriveAxis while the
            // builder was still quoting inertia about the face it was bolted
            // to. MEASURED on the first vertical-spinner fixture - the panel
            // said I=2.248 tipR=0.370 (a flail) for a limb the arena drives
            // COAXIALLY at I=0.43. Same class of defect the critics have named
            // four times; caught here by re-reading every wheelAxis in the file.
            Actuator.LimbMaths(kind != ActuatorKind.Ram, body[a].DriveAxis(), pos, mass, half,
                               out info.inertia, out info.tipRadius);
            // Round-3-critic CRITICAL 2: the build screen has to quote the
            // SAME ceiling the fight will use, so it needs the limb's weakest
            // material exactly as Actuator.Recompute derives it.
            float weakest = -1f;
            for (int i = 0; i < members.Count; i++)
            {
                float s = MatDB.Get(body[members[i]].MatName()).strengthRel;
                if (weakest < 0f || s < weakest) weakest = s;
            }
            info.tipSpeedCap = weakest < 0f ? Actuator.TIP_SPEED_BASE : Actuator.TipSpeedCap(weakest);
            info.rateMax = Actuator.RateCeiling(kind, info.tipRadius, info.tipSpeedCap);
            info.energyJ = 0.5f * info.inertia * info.rateMax * info.rateMax;
            info.kjPerSwing = info.energyJ / Mathf.Max(Actuator.EFFICIENCY, 0.01f) / 1000f;
            ArcClearance(body, members, a, kind, out info.arcFree, out info.arcLimit,
                         out info.arcSign);
            info.arcFrac = info.arcLimit > 1e-4f ? info.arcFree / info.arcLimit : 1f;
            info.rotorDip = kind == ActuatorKind.Spindle
                          ? RotorFloorDip(body, members, body[a]) : 0f;
            outp.Add(info);
        }
        return outp;
    }

    /// <summary>ROUND-UP2 FIX D. How far this limb can actually swing (or
    /// push) before it reaches the deck the machine stands on.
    ///
    /// WHY THIS EXISTS. The round-2 critic pointer-built a pivot+blade hammer
    /// on a VERTICAL chassis face. The build screen told them tipR 0.250,
    /// rateMax 14.00, blockedBy null - i.e. "this works" - and the arena then
    /// delivered 0.560 rad of the advertised 2.618 (21%) one way and 0.280
    /// (11%) the other, because the arm sweeps into the floor. The same rig on
    /// the ROOF reaches the full arc. Nothing on the build screen distinguished
    /// the two, and the number it did quote described the roof case.
    ///
    /// This is deliberately a PREDICTION, not a change to the physics. Round 1
    /// declined to loosen Actuator.GroundBlocked / GROUND_CLEAR on an
    /// unreproduced report and was right to: that guard is what stopped an arm
    /// posing through the floor and levering the player's own machine over. The
    /// arena keeps its guard exactly as it is; the builder stops promising an
    /// arc the guard is going to eat.
    ///
    /// FAITHFUL PORT, not an approximation. It runs the same sweep
    /// Actuator.ArcDig/GroundBlocked run: rotate each limb member about the
    /// hinge, test its lowest point against deck + GROUND_CLEAR, and use the
    /// EXACT vertical support of the rotated box (VertExtent), never a bounding
    /// sphere - a wedge's bounding sphere is 0.25 m where the part is 0.06 m
    /// thick, which would read a legal floor-level flipper as already
    /// underground at rest. It also replicates PickArcSign's conservative
    /// reversal rule, so it predicts the direction the arena will actually
    /// choose rather than the best of the two.
    ///
    /// 48 steps: PIVOT_ARC_DEG 150 / 48 = 3.1 deg of resolution, which is finer
    /// than the 5-deg granularity anything downstream reports and still one
    /// short sweep per actuator per frame on a build screen that already does a
    /// full O(n^2) Touching() pass.</summary>
    const int ARC_STEPS = 48;

    void ArcClearance(List<PlacedPart> body, List<int> members, int actIdx,
                      ActuatorKind kind, out float free, out float limit, out float signOut)
    {
        limit = kind == ActuatorKind.Ram ? Actuator.RAM_STROKE_M
                                         : Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad;
        free = limit;
        signOut = 1f;
        // A spindle turns continuously; Actuator.GroundBlocked exempts it and
        // so must this, or every drum would be reported as jammed.
        if (kind == ActuatorKind.Spindle || members.Count == 0) return;

        var act = body[actIdx];
        Vector3 axis = act.DriveAxis();          // the axis it SWINGS about
        if (axis.sqrMagnitude < 0.01f) return;   // no recorded axis: say nothing
        axis = axis.normalized;

        // The deck is the plane the wheels stand on - the same plane
        // FloorPlane() polices for placement, so the two rules agree.
        float deck = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
            if (placed[i].def.category == P1Category.Mobility)
                deck = Mathf.Min(deck, placed[i].pos.y - placed[i].def.size.x * 0.5f);
        if (deck == float.MaxValue)
            for (int i = 0; i < placed.Count; i++)
                deck = Mathf.Min(deck, placed[i].pos.y - placed[i].Half().y);
        float floor = deck + Actuator.GROUND_CLEAR;

        float sign = 1f;
        if (kind == ActuatorKind.Pivot)
        {
            // PickArcSign, rebuilt: keep the built direction unless it digs in
            // inside the first ARC_EARLY_DEG and the other way digs less.
            float digPos = ArcDig(body, members, act, axis, 1f, floor);
            if (digPos > 0f && ArcDig(body, members, act, axis, -1f, floor) < digPos) sign = -1f;
        }
        signOut = sign;

        for (int s = 1; s <= ARC_STEPS; s++)
        {
            float t = limit * s / ARC_STEPS;
            Quaternion q = kind == ActuatorKind.Ram
                ? Quaternion.identity
                : Quaternion.AngleAxis(sign * t * Mathf.Rad2Deg, axis);
            Vector3 d = kind == ActuatorKind.Ram ? axis * t : Vector3.zero;
            bool hit = false;
            for (int i = 0; i < members.Count && !hit; i++)
            {
                var m = body[members[i]];
                Vector3 lp = kind == ActuatorKind.Ram
                    ? m.pos + d
                    : act.pos + q * (m.pos - act.pos);
                if (lp.y - VertExtent(q, m.Half()) < floor) hit = true;
            }
            if (hit) { free = limit * (s - 1) / ARC_STEPS; return; }
        }
    }

    /// <summary>Deepest the first ARC_EARLY_DEG of a swing in direction `s`
    /// would dig below `floor`. Actuator.ArcDig, in build space.</summary>
    float ArcDig(List<PlacedPart> body, List<int> members, PlacedPart act,
                 Vector3 axis, float s, float floor)
    {
        float worst = 0f;
        for (int step = 1; step <= 9; step++)
        {
            Quaternion q = Quaternion.AngleAxis(s * Actuator.ARC_EARLY_DEG * step / 9f, axis);
            for (int i = 0; i < members.Count; i++)
            {
                var m = body[members[i]];
                Vector3 lp = act.pos + q * (m.pos - act.pos);
                float y = lp.y - VertExtent(q, m.Half());
                if (floor - y > worst) worst = floor - y;
            }
        }
        return worst;
    }

    /// <summary>Deepest that a SPINDLE's rotor reaches below the clearance
    /// band over a full turn, metres. 0 = the rotor clears the deck.
    ///
    /// Same machinery as ArcClearance - same deck rule, same exact VertExtent
    /// support of the rotated box, never a bounding sphere - but swept over the
    /// whole 360 deg, because that is what a spindle does. 72 steps = 5 deg,
    /// matching the granularity everything downstream reports.
    ///
    /// A rotor on a VERTICAL axle never changes height as it turns, so this
    /// correctly returns whatever its resting dip is and does not invent one.
    /// </summary>
    const int ROTOR_STEPS = 72;

    float RotorFloorDip(List<PlacedPart> body, List<int> members, PlacedPart act)
    {
        if (members == null || members.Count == 0) return 0f;
        Vector3 axis = act.DriveAxis();                // the axis it SPINS about
        if (axis.sqrMagnitude < 0.01f) return 0f;      // no recorded axis: say nothing
        axis = axis.normalized;
        float deck = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
            if (placed[i].def.category == P1Category.Mobility)
                deck = Mathf.Min(deck, placed[i].pos.y - placed[i].def.size.x * 0.5f);
        if (deck == float.MaxValue)
            for (int i = 0; i < placed.Count; i++)
                deck = Mathf.Min(deck, placed[i].pos.y - placed[i].Half().y);
        float floor = deck + Actuator.GROUND_CLEAR;
        float worst = 0f;
        for (int s = 0; s < ROTOR_STEPS; s++)
        {
            Quaternion q = Quaternion.AngleAxis(360f * s / ROTOR_STEPS, axis);
            for (int i = 0; i < members.Count; i++)
            {
                var m = body[members[i]];
                Vector3 lp = act.pos + q * (m.pos - act.pos);
                float y = lp.y - VertExtent(q, m.Half());
                if (floor - y > worst) worst = floor - y;
            }
        }
        return worst;
    }

    // ---- ROTOR SWEEP LEGALITY (owen, 2026-08-05) --------------------------

    /// <summary>A spindle's rotor must not sweep through a part it is not part
    /// of. This is the build-time legality rule for it, and it sits alongside
    /// "Needs at least 1 wheel."
    ///
    /// WHY THE BUILDER AND NOT THE ARENA. A rotor's hit volume is a TRIGGER,
    /// not a solid collider: it reports overlaps so it can bite, and nothing
    /// ever pushes it back out of anything. That is CORRECT for combat - a
    /// solid blade at 900 rpm would launch both machines instead of cutting
    /// one - so a disc passes through its own chassis by exactly the mechanism
    /// that lets it pass through an opponent, and making it solid would wreck
    /// the damage model to fix what is really a build error. The defect was
    /// never in the arena: it is that the BUILDER let you bolt on a rotor whose
    /// swept circle runs through your own machine and then said nothing at all.
    ///
    /// (For anyone who goes looking: SpinnerWeapon.cs also sets isTrigger and
    /// is the file this was first written up against. It has been DEAD CODE
    /// since 2026-07-27 - spinners are unpowered rotor edges now and Actuator
    /// drives them, so Actuator's trigger is the live one.)
    ///
    /// EXACT, NOT CONSERVATIVE - which is why, unlike ArcClearance and
    /// RotorFloorDip, it does not step the sweep at all. Every part in build
    /// space is an axis-aligned box (PlacedPart.Half()) and every drive axis is
    /// cardinal (PlacedPart.DriveAxis()), so the volume a member sweeps over a
    /// full turn is precisely an annular cylinder: a radial band about the axle
    /// and an axial band along it. With a cardinal axle those two coordinates
    /// are INDEPENDENT - the axial one is a single component of the box, the
    /// radial one a function of the other two - so the (radius, axial) set a
    /// box occupies is a true rectangle and the rectangle-overlap test below is
    /// an equality, not an approximation. The other two sweeps ask about
    /// HEIGHT, which a rotation does change, so they have to march; this one
    /// asks about distance from the axle, which a rotation cannot change.
    ///
    /// The spindle itself is exempt: it is what the rotor is bolted to, and a
    /// rotor resting against its face cannot rotate INTO that face.</summary>
    public struct RotorFoul
    {
        public PlacedPart act;   // the spindle
        public PlacedPart hit;   // the part its rotor sweeps through
    }

    /// <summary>1 mm - the same float-tie tolerance FloorPlane uses. Parts snap
    /// face to face and therefore TOUCH; only real overlap is a foul.</summary>
    const float SWEEP_EPS = 0.001f;

    static int CardinalIndex(Vector3 v)
    {
        int ax = 0;
        float b = Mathf.Abs(v.x);
        if (Mathf.Abs(v.y) > b) { ax = 1; b = Mathf.Abs(v.y); }
        if (Mathf.Abs(v.z) > b) ax = 2;
        return ax;
    }

    /// <summary>The radial band [rMin,rMax] and axial band [aMin,aMax] that an
    /// axis-aligned box occupies about a cardinal axle through `hub`.</summary>
    static void SweepBands(Vector3 c, Vector3 h, Vector3 hub, int ax,
                           out float rMin, out float rMax, out float aMin, out float aMax)
    {
        aMin = c[ax] - h[ax] - hub[ax];
        aMax = c[ax] + h[ax] - hub[ax];
        int u = (ax + 1) % 3, v = (ax + 2) % 3;
        float u0 = c[u] - h[u] - hub[u], u1 = c[u] + h[u] - hub[u];
        float v0 = c[v] - h[v] - hub[v], v1 = c[v] + h[v] - hub[v];
        // Nearest point of that footprint to the axle is 0 when the footprint
        // straddles the axle; the farthest is always one of its corners.
        float du = Mathf.Max(0f, Mathf.Max(u0, -u1));
        float dv = Mathf.Max(0f, Mathf.Max(v0, -v1));
        rMin = Mathf.Sqrt(du * du + dv * dv);
        float fu = Mathf.Max(Mathf.Abs(u0), Mathf.Abs(u1));
        float fv = Mathf.Max(Mathf.Abs(v0), Mathf.Abs(v1));
        rMax = Mathf.Sqrt(fu * fu + fv * fv);
    }

    static bool BandsOverlap(float a0, float a1, float b0, float b1)
    {
        return Mathf.Min(a1, b1) - Mathf.Max(a0, b0) > SWEEP_EPS;
    }

    /// <summary>Every (spindle, part-its-rotor-saws) pair in the CURRENT build.
    /// The limb partition is LimbReport's own, not a second copy of it, so this
    /// rule and the build panel can never disagree about what a limb is.</summary>
    void CollectRotorFouls(List<RotorFoul> into)
    {
        into.Clear();
        var limbs = LimbReport();
        for (int i = 0; i < limbs.Count; i++)
        {
            var li = limbs[i];
            if (li.memberParts == null || li.memberParts.Count == 0) continue;
            if (Actuator.KindOfId(li.act.def.id) != ActuatorKind.Spindle) continue;
            Vector3 axis = li.act.DriveAxis();
            if (axis.sqrMagnitude < 0.01f) continue;   // no recorded axis: say nothing
            int ax = CardinalIndex(axis);
            for (int m = 0; m < li.memberParts.Count; m++)
            {
                var mem = li.memberParts[m];
                float mr0, mr1, ma0, ma1;
                SweepBands(mem.pos, mem.Half(), li.act.pos, ax, out mr0, out mr1, out ma0, out ma1);
                for (int o = 0; o < placed.Count; o++)
                {
                    var other = placed[o];
                    if (other == li.act || li.memberParts.Contains(other)) continue;
                    float or0, or1, oa0, oa1;
                    SweepBands(other.pos, other.Half(), li.act.pos, ax, out or0, out or1, out oa0, out oa1);
                    if (!BandsOverlap(mr0, mr1, or0, or1)) continue;
                    if (!BandsOverlap(ma0, ma1, oa0, oa1)) continue;
                    var f = new RotorFoul();
                    f.act = li.act;
                    f.hit = other;
                    into.Add(f);
                }
            }
        }
    }

    readonly List<RotorFoul> foulBase = new List<RotorFoul>();
    readonly List<RotorFoul> foulTest = new List<RotorFoul>();
    /// <summary>The baseline is only rebuilt when the build actually changes -
    /// UpdateGhost asks this question every frame the pointer is over a face.
    /// Set beside doomDirty at all three sites that mutate `placed`.</summary>
    bool foulDirty = true;

    static bool HasFoul(List<RotorFoul> set, RotorFoul f)
    {
        for (int i = 0; i < set.Count; i++)
            if (set[i].act == f.act && set[i].hit == f.hit) return true;
        return false;
    }

    /// <summary>Why `probe` cannot be placed, or null.
    ///
    /// PLACEMENT-TIME ONLY, AND BASELINED (owen, 2026-08-05). The rule refuses
    /// the placement that CREATES a foul; it never audits a build that already
    /// has one. Validate() is deliberately untouched, so nothing already saved
    /// is retroactively made illegal - spinner1 still loads and still fights.
    ///
    /// The baseline is the second half of that promise and the less obvious
    /// one. A save that already violates the rule has to stay EDITABLE: you can
    /// keep bolting parts onto it, you just cannot add a NEW pair of sawn
    /// parts. Without baselining, the first such build would have gone
    /// permanently read-only the moment you touched it - which is the same
    /// retroactive punishment wearing a different hat.</summary>
    public string RotorSweepRefusal(PlacedPart probe)
    {
        if (probe == null || probe.def == null) return null;
        if (foulDirty) { CollectRotorFouls(foulBase); foulDirty = false; }
        placed.Add(probe);
        try { CollectRotorFouls(foulTest); }
        finally { placed.Remove(probe); }
        for (int i = 0; i < foulTest.Count; i++)
        {
            var f = foulTest[i];
            if (HasFoul(foulBase, f)) continue;   // already true before this part
            return f.hit == probe
                 ? "That sits inside the " + f.act.def.label + "'s swept circle"
                 : "Rotor would sweep through " + f.hit.def.label
                   + " \u2014 clear its circle, or turn the " + f.act.def.label;
        }
        return null;
    }

    /// <summary>Every rotor foul in the build as it stands, "" for none.
    ///
    /// DIAGNOSTIC ONLY - nothing in the game consults it. It exists because
    /// "placement-time only" is a promise about EXISTING builds, and a promise
    /// about existing builds that nobody ever measured is a guess. This is what
    /// answers "does spinner1 actually violate the rule?" using the rule's own
    /// code instead of arithmetic done by hand on the side.
    ///
    /// It is also the hook if the answer ever turns out to be yes and a
    /// non-blocking panel warning is wanted - the same channel rotorDip already
    /// uses. That would be a separate decision, and it has not been made.</summary>
    public string RotorFoulReport()
    {
        var found = new List<RotorFoul>();
        CollectRotorFouls(found);
        string s = "";
        for (int i = 0; i < found.Count; i++)
            s += (s.Length > 0 ? "; " : "") + found[i].act.def.label
               + " saws " + found[i].hit.def.label;
        return s;
    }

    /// <summary>Palette index of a part id, or -1, so tests and tools do not
    /// have to hard-code the palette order.</summary>
    public int PaletteIndexOf(string id)
    {
        if (palette == null) return -1;
        for (int i = 0; i < palette.Length; i++) if (palette[i].id == id) return i;
        return -1;
    }

    /// <summary>Exact vertical support of a box of half-extents h under
    /// rotation r. Identical to Actuator.VertExtent - see that comment for why
    /// it must not be a bounding sphere.</summary>
    static float VertExtent(Quaternion r, Vector3 h)
    {
        return Mathf.Abs((r * Vector3.right).y) * h.x
             + Mathf.Abs((r * Vector3.up).y) * h.y
             + Mathf.Abs((r * Vector3.forward).y) * h.z;
    }

    /// <summary>Parts whose orientation follows the mount face normal
    /// (stored in PlacedPart.wheelAxis): wheels and both weapons.</summary>
    static bool NeedsAxis(P1PartDef def)
    {
        return def.category == P1Category.Mobility || def.category == P1Category.Weapon;
    }

    PlacedPart PartUnderMouse()
    {
        Vector3 m = Phase0Input.MousePos();
        Ray ray = cam.ScreenPointToRay(m);
        RaycastHit hit = default(RaycastHit);
        if (Physics.Raycast(ray, out hit, 60f) && byCollider.ContainsKey(hit.collider))
            return byCollider[hit.collider];
        return null;
    }

    /// <summary>
    /// Build (or rebuild) the ghost as the REAL compound part model — the same
    /// PartVisualFactory output a placed part gets — with per-renderer material
    /// instances so validity tinting never touches the shared materials.
    /// </summary>
    /// <summary>ROUND-UP2 FIX A: `axis` is the mount face normal the ghost is
    /// currently hovering (Vector3.zero when it is not on a face). It is part
    /// of the rebuild KEY as well as the id, so swinging the pointer from one
    /// face to another re-draws the ghost in the new orientation - and, because
    /// the key still matches on every other frame, only then.</summary>
    void EnsureGhostBuilt(P1PartDef def, bool isWheel, Vector3 axis)
    {
        string key = def.id + "_" + ghostYaw + "_" + AxisCode(axis);
        if (ghost != null && ghostBuiltKey == key) return;
        if (ghost != null) Destroy(ghost);
        ghostMats.Clear();
        ghostBaseCols.Clear();

        ghost = new GameObject("ghost");
        // ROUND-UP3 FIX A: the ghost probe carries the MOUNT AXIS. It did not
        // before, which was harmless while Half() ignored the axis and is not
        // now: BuildPart un-rotates the AABB it is handed, so handing it the
        // unrotated canonical box would un-rotate a box that was never rotated
        // and draw a squashed part. The ghost has to be measured with the same
        // axis the placed part will be.
        var probe = new PlacedPart { def = def, yaw = ghostYaw, wheelAxis = axis };
        if (isWheel)
            PartVisualFactory.BuildWheel(ghost.transform, def.size.x * 0.5f, def.size.y, -1);
        else if (def.id.StartsWith("spinner"))
            PartVisualFactory.BuildSpinner(ghost.transform, def.size.x * 0.5f, def.size.y, -1);
        else if (def.id == "spike")
            PartVisualFactory.BuildSpike(ghost.transform, def.size);
        else
            // DRIVE AXIS: the ghost must be DRAWN with the axis R has chosen,
            // not the face it is hovering, or R is a feature the player cannot
            // see. MEASURED by AxisProbe before this line was fixed: ghostYaw
            // cycled 0/90/180/270 and the placed part came out spindleXP/YP/ZP
            // correctly, while the ghost's rendered AABB was byte-identical at
            // every press - the logic worked and the drawing never moved, which
            // is indistinguishable from "R does nothing" from the player's side.
            PartVisualFactory.BuildPart(VisualId(def, probe.DriveAxis()), ghost.transform, probe.Half() * 2f,
                def.category == P1Category.Control, MatDB.Get(def.EffectiveMat(activeMat)).color,
                MatDB.Get(def.EffectiveMat(activeMat)).metallic,
                MatDB.Get(def.EffectiveMat(activeMat)).smoothness);

        // Ghost look v2 (owen): keep the REAL part appearance - per-renderer
        // material instances - and blend a strong green/red tint over it in
        // TintGhost, so the texture stays visible while validity is still
        // unmissable on a phone (the original emission-only glow was not).
        foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
        {
            var inst = new Material(r.sharedMaterial);
            inst.EnableKeyword("_EMISSION");
            r.sharedMaterial = inst;
            ghostMats.Add(inst);
            ghostBaseCols.Add(inst.color);
        }
        ghostBuiltKey = key;
        ghostTintValid = true;
        TintGhost(false);
    }

    void TintGhost(bool valid)
    {
        if (ghostTintValid == valid) return;
        ghostTintValid = valid;
        // Strong tint OVER the real material (owen): the part keeps its
        // texture but reads clearly green (will attach) or red (can't place).
        Color tint = valid ? new Color(0.20f, 1.00f, 0.30f) : new Color(1.00f, 0.15f, 0.10f);
        for (int i = 0; i < ghostMats.Count; i++)
        {
            Color b = i < ghostBaseCols.Count ? ghostBaseCols[i] : Color.white;
            ghostMats[i].color = Color.Lerp(b, tint, 0.45f);
            ghostMats[i].SetColor("_EmissionColor", tint * 0.35f);
        }
    }

    void UpdateGhost()
    {
        if (selected < 0) { HideGhost(); return; }
        var def = palette[selected];
        bool isWheelSel = def.category == P1Category.Mobility;

        Vector3 m = Phase0Input.MousePos();
        if (uiPointerBlocked || (!MobileBuilderUI.Active && m.x < PanelPixelW)) { HideGhost(); return; }   // Phase 5. R2: scaled width.
        // The ghost must EXIST before the raycast (the free-follow branch below
        // draws it), so this first call uses the normal we are already standing
        // on. The authoritative rebuild happens once the face is resolved.
        EnsureGhostBuilt(def, isWheelSel, NeedsAxis(def) ? ghostNormal : Vector3.zero);
        ghost.SetActive(true);

        Ray ray = cam.ScreenPointToRay(m);
        RaycastHit hit = default(RaycastHit);
        bool onFace = false;
        // Fix 2026-07-29 (playtest): the nearest-hit ray let a wheel parked in
        // front of a mount face swallow the hover — a ground-level front wedge
        // was unreachable because the wheels flank the chassis nose. Nothing
        // attaches to a wheel or a bare weapon, so the aiming ray now passes
        // through them to the first attachable face; if ONLY they are hit, the
        // old behaviour (target it, show the reason) stands.
        {
            RaycastHit hAtt = default(RaycastHit), hAny = default(RaycastHit);
            float dAtt = float.MaxValue, dAny = float.MaxValue;
            foreach (var h in Physics.RaycastAll(ray, 60f))
            {
                if (!byCollider.ContainsKey(h.collider)) continue;
                if (h.distance < dAny) { dAny = h.distance; hAny = h; }
                var tpp = byCollider[h.collider];
                bool through = tpp.def.category == P1Category.Mobility
                            || (tpp.def.category == P1Category.Weapon && !tpp.def.actuator);
                if (!through && h.distance < dAtt) { dAtt = h.distance; hAtt = h; }
            }
            if (dAtt < float.MaxValue) { hit = hAtt; onFace = true; }
            else if (dAny < float.MaxValue) { hit = hAny; onFace = true; }
        }
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

        // 2026-07-30 (owen: end-to-end beams hard to aim): a ray aimed just
        // past a beam's tip flies on and hits whatever sits BEHIND it - the
        // ghost jumped to the core or the floor. Tip capture: if the ray
        // passes within 10 cm of an elongated part's end-face centre, and
        // that tip is no farther than what the ray actually hit, the end
        // face wins. End faces are single-socket, so the snap self-centres.
        PlacedPart capPart = null;
        Vector3 capNormal = Vector3.zero;
        {
            float bestD = 0.10f;
            foreach (var pp in placed)
            {
                Vector3 th2 = pp.Half();
                int la2 = 0;
                if (th2.y > th2[la2]) la2 = 1;
                if (th2.z > th2[la2]) la2 = 2;
                float other2 = Mathf.Max(th2[(la2 + 1) % 3], th2[(la2 + 2) % 3]);
                if (th2[la2] < 2f * other2) continue;   // beams and long beams only
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 axisV = Vector3.zero;
                    axisV[la2] = sgn;
                    Vector3 e = pp.pos + axisV * th2[la2];
                    float t = Vector3.Dot(e - ray.origin, ray.direction);
                    if (t <= 0f) continue;
                    float d = (ray.origin + ray.direction * t - e).magnitude;
                    if (d >= bestD) continue;
                    if (onFace && t > hit.distance + 0.05f) continue;   // tip is behind what we hit
                    bestD = d;
                    capPart = pp;
                    capNormal = axisV;
                }
            }
        }

        if (!onFace && capPart == null)
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
            RefreshGhostArc(null, Vector3.zero, Vector3.zero, 0, false);
            HideSnapOverlay();
            TintGhost(false);
            return;
        }

        if (onFace)
        {
            ghostTarget = byCollider[hit.collider];
            ghostNormal = hit.normal;
        }
        if (capPart != null)
        {
            // Tip capture overrides: aim just past a beam tip = end-mount.
            ghostTarget = capPart;
            ghostNormal = capNormal;
            grazed = false;
        }
        if (grazed)
        {
            // A grazing sphere-cast reports edge/diagonal normals, which made
            // mirrored sockets disagree (left face read as a +X face → overlap
            // → phantom "Blocked"). Derive the face from WHERE the graze sits
            // relative to the part instead: dominant axis of the offset,
            // normalized by the part's half extents.
            Vector3 off = hit.point - ghostTarget.pos;
            Vector3 th = ghostTarget.Half();
            // 2026-07-30 (owen: end-to-end beams hard to aim): the old pick
            // used |off|/half per axis, which biased a BEAM's tip grazes 3:1
            // toward its side faces - aiming just past the tip flipped the
            // ghost sideways. Use OVERSHOOT (distance outside the box per
            // axis, the true nearest-face metric), plus a tip-preference
            // band on elongated parts: anything at or beyond the last 2 cm
            // resolves to the END face.
            int ga = 0;
            float gb = Mathf.Abs(off.x) - th.x;
            float gy = Mathf.Abs(off.y) - th.y;
            if (gy > gb) { ga = 1; gb = gy; }
            float gz = Mathf.Abs(off.z) - th.z;
            if (gz > gb) ga = 2;
            int la = 0;
            if (th.y > th[la]) la = 1;
            if (th.z > th[la]) la = 2;
            float laOther = Mathf.Max(th[(la + 1) % 3], th[(la + 2) % 3]);
            if (th[la] >= 2f * laOther && Mathf.Abs(off[la]) > th[la] - 0.02f) ga = la;
            Vector3 derived = Vector3.zero;
            derived[ga] = off[ga] >= 0f ? 1f : -1f;
            ghostNormal = derived;
        }

        // ROUND-UP2 FIX A: the face is now known, so re-key the ghost on it.
        // EnsureGhostBuilt early-outs when the key is unchanged, so this costs
        // one rebuild per FACE CHANGE, not one per frame.
        EnsureGhostBuilt(def, isWheelSel, NeedsAxis(def) ? ghostNormal : Vector3.zero);
        ghost.SetActive(true);

        // Dominant world axis of the face normal (all parts are axis-aligned).
        int axis = 0;
        float best = Mathf.Abs(ghostNormal.x);
        if (Mathf.Abs(ghostNormal.y) > best) { axis = 1; best = Mathf.Abs(ghostNormal.y); }
        if (Mathf.Abs(ghostNormal.z) > best) { axis = 2; }
        float sign = ghostNormal[axis] >= 0f ? 1f : -1f;

        bool isWheel = def.category == P1Category.Mobility;
        bool isSpinner = def.id.StartsWith("spinner");
        bool isSpike = def.id == "spike";
        // ORIENTED = mates through a single centre socket AND records the mount
        // face normal in wheelAxis. This used to read
        //     isWheel || isSpinner || isSpike
        // and the six Phase 4 parts were never added to it. Two consequences,
        // both of which made the mouse path unusable while every scripted build
        // kept working, because LoadSnapshot -> AddPart bypasses this whole
        // validation block:
        //
        //  1. A blade is 0.05 m thick against SOCKET_PITCH 0.15, so EVERY one of
        //     its faces reports zero sockets - measured: faceX(0.05x0.12),
        //     faceY(0.12x0.50), faceZ(0.50x0.05), all NO-SOCKET. A blade could
        //     therefore not be placed anywhere at all by hand. The wedge was
        //     mountable only on a horizontal face for the same reason.
        //  2. A pivot/spindle/ram placed by hand got wheelAxis = Vector3.zero,
        //     so Actuator.ActuatorAxis fell back to +X regardless of which face
        //     you actually bolted it to - a hinge that swings in the wrong plane
        //     and no way to tell from the builder.
        //
        // NeedsAxis(def) is the existing predicate for exactly this property
        // (Mobility or Weapon) and is already what SpawnBot uses to decide which
        // parts carry an axis code into the arena. Using it here makes the two
        // agree, which is what they should always have done.
        bool oriented = NeedsAxis(def);
        // Probes carry the candidate axle so Half() returns the part's
        // EFFECTIVE extents (half-width along the axle, radius tangentially) —
        // wheels/weapons sit flush on the face AND the overlap box matches
        // reality.
        var probe = new PlacedPart { def = def, yaw = ghostYaw,
                                     wheelAxis = oriented ? ghostNormal : Vector3.zero };
        Vector3 newHalf = probe.Half();
        Vector3 tHalf = ghostTarget.Half();

        Vector3 pos = ghostTarget.pos;
        pos[axis] = ghostTarget.pos[axis] + sign * (tHalf[axis] + newHalf[axis]);
        // §5.1 socket GRID model: each face ≥0.15 m carries a center-inclusive
        // grid of sockets at 0.15 m pitch (PartVisualFactory.SocketOffsets —
        // the same table that draws the dots). The ghost snaps to the
        // candidate alignment (target socket minus new-part socket, per
        // tangent axis) nearest the mouse hit — off-center mounting is now a
        // feature, and more mated sockets = a stronger joint.
        int t1 = (axis + 1) % 3, t2 = (axis + 2) % 3;
        float[] tu = FaceSockets(tHalf[t1] * 2f);
        float[] tv = FaceSockets(tHalf[t2] * 2f);
        // Wheels/spinners/spikes mate through a single center socket.
        float[] nu = oriented ? CenterSocket : FaceSockets(newHalf[t1] * 2f);
        float[] nv = oriented ? CenterSocket : FaceSockets(newHalf[t2] * 2f);
        float du = SnapAlong(tu, nu, hit.point[t1] - ghostTarget.pos[t1], tHalf[t1]);
        float dv = SnapAlong(tv, nv, hit.point[t2] - ghostTarget.pos[t2], tHalf[t2]);
        pos[t1] = ghostTarget.pos[t1] + du;
        pos[t2] = ghostTarget.pos[t2] + dv;
        ghostMated = MatedCount(tu, nu, du) * MatedCount(tv, nv, dv);
        ghostSockets = Mathf.Max(1, ghostMated);

        // Fix 2026-07-29 (playtest): hovering LOW on a side face picked a
        // floor-clipping candidate even when a valid socket sat higher on the
        // SAME face — a red ghost at exactly the spot a new player aims. If the
        // vertical tangent has a candidate that clears the floor, take the
        // nearest such candidate instead of refusing.
        int vt = t1 == 1 ? 1 : t2 == 1 ? 2 : 0;
        if (vt != 0 && pos.y - newHalf.y < FloorPlane(isWheel) - 1e-4f)
        {
            float minD = FloorPlane(isWheel) + newHalf.y - ghostTarget.pos[1];
            float[] tt = vt == 1 ? tu : tv;
            float[] nn = vt == 1 ? nu : nv;
            float wantV = hit.point[1] - ghostTarget.pos[1];
            float bestD = float.NaN; float bErr = float.MaxValue;
            foreach (float toff in tt)
                foreach (float noff in nn)
                {
                    float d = toff - noff;
                    if (d < minD - 1e-4f) continue;
                    float e = Mathf.Abs(d - wantV);
                    if (e < bErr) { bErr = e; bestD = d; }
                }
            if (!float.IsNaN(bestD))
            {
                if (vt == 1) du = bestD; else dv = bestD;
                pos[t1] = ghostTarget.pos[t1] + du;
                pos[t2] = ghostTarget.pos[t2] + dv;
                ghostMated = MatedCount(tu, nu, du) * MatedCount(tv, nv, dv);
                ghostSockets = Mathf.Max(1, ghostMated);
            }
        }

        ghostValid = true;
        ghostReason = "";
        // Both mating faces must actually carry a socket: SocketOffsets is
        // empty on faces under 0.15 m (plate edges = no socket). Wheels and
        // weapons keep their center-socket exemption on the NEW-part side.
        if (ghostTarget.def.category == P1Category.Mobility)
        { ghostValid = false; ghostReason = "Nothing attaches to a wheel"; }
        else if (ghostTarget.def.category == P1Category.Weapon && !ghostTarget.def.actuator)
        { ghostValid = false; ghostReason = "Nothing attaches to a weapon"; }
        else if (isWheel && Mathf.Abs(ghostNormal.y) > 0.5f)
        { ghostValid = false; ghostReason = "Wheels attach to side faces only"; }
        else if (isSpinner && ghostNormal.y < -0.5f)
        { ghostValid = false; ghostReason = "Spinner can't mount underneath"; }
        else if (PartVisualFactory.SocketOffsets(tHalf[t1] * 2f).Length == 0
                 || PartVisualFactory.SocketOffsets(tHalf[t2] * 2f).Length == 0)
        { ghostValid = false; ghostReason = "No socket on this face"; }
        // ROUND-UP2 FIX C2. Same rule as above, but failing on the NEW part's
        // own mating face rather than the target's - and it needs to say so.
        // The round-2 critic filed "two of the armour plate's four R
        // orientations can never be placed anywhere" against this message. That
        // turns out NOT to be true: a stood-up plate is 0.06 m through its thin
        // axis, so it mounts on the two faces whose normal IS that axis and
        // nowhere else. MEASURED, hit-verified, all six faces of a bare core,
        // stepping the whole R cycle: 24 probes, and every one of the plate's
        // three distinct orientations is accepted somewhere - yaw 0 on +Y/-Y,
        // yaw 180 on +Z/-Z, yaw 270 on +X/-X. The stood-up wall plate IS
        // buildable. What was wrong was the FEEDBACK: the player who presses R
        // on the roof and gets "No socket on this face" three times has been
        // told the face is the problem when the fix is to aim at a side. So the
        // rule is unchanged and the message now names the right end of it.
        else if (!oriented && (PartVisualFactory.SocketOffsets(newHalf[t1] * 2f).Length == 0
                               || PartVisualFactory.SocketOffsets(newHalf[t2] * 2f).Length == 0))
        { ghostValid = false; ghostReason = "This way up it has no socket to mate with — press R, or aim at a side face"; }
        else if (pos.y - newHalf.y < FloorPlane(isWheel))
        { ghostValid = false; ghostReason = "Too low — would clip the floor"; }
        // ROUND-UP2 FIX C (round-2 critic MAJOR: "credit budget is not enforced
        // or surfaced anywhere in the pointer path"). Validate() has always
        // refused an over-budget build at the FIGHT button, but nothing on the
        // way in did, so the player could keep clicking parts that could never
        // be flown. MEASURED before this, from a bare core, clicking Tungsten
        // plates through the pointer: twelve of them went in with the ghost
        // green every single time, for 21,014 cr against a 4,000 budget -
        // 5.25x, over from the third plate onward. Tested here beside the floor
        // and socket rules so the refusal lands where every other refusal
        // does: on the ghost, at the moment of the click, with a reason.
        else if (CREDIT_BUDGET > 0
                 && BuildCost() + def.CostOf(def.EffectiveMat(activeMat)) > CREDIT_BUDGET)
        { ghostValid = false;
          ghostReason = string.Format("Over budget — {0} cr of {1} spent, this costs {2}",
                                      BuildCost(), CREDIT_BUDGET,
                                      def.CostOf(def.EffectiveMat(activeMat))); }

        // Overlap check against existing parts (slightly shrunk, using the
        // ORIENTED effective extents so wheels never phantom-clip neighbors).
        if (ghostValid)
        {
            foreach (var c in Physics.OverlapBox(pos, newHalf * 0.92f, Quaternion.identity))
                if (byCollider.ContainsKey(c))
                { ghostValid = false; ghostReason = "Blocked by another part"; break; }
        }

        // ROTOR SWEEP (owen, 2026-08-05). Last of the placement rules, because
        // it is the only one that has to reason about the whole machine rather
        // than about this part's own box - and because there is no point asking
        // it about a placement the cheap rules have already refused.
        //
        // The probe must be what AddPart is ABOUT to build, not an
        // approximation of it: same pos, same yaw, same mount axis, same
        // material as the commit a few lines up in Update(). A probe that
        // disagrees with the placement is a rule that polices a machine the
        // player is not building.
        if (ghostValid)
        {
            var swProbe = new PlacedPart { def = def, pos = pos, yaw = ghostYaw,
                                           wheelAxis = NeedsAxis(def) ? ghostNormal : Vector3.zero,
                                           matName = activeMat };
            string swWhy = RotorSweepRefusal(swProbe);
            if (swWhy != null) { ghostValid = false; ghostReason = swWhy; }
        }

        ghostPos = pos;
        ghost.transform.rotation = (isWheel || isSpinner)
            ? Quaternion.FromToRotation(Vector3.up, ghostNormal)
            : isSpike
            ? Quaternion.FromToRotation(Vector3.forward, ghostNormal)
            : Quaternion.identity;
        ghost.transform.position = pos;
        // owen: show the swept window BEFORE the part is committed.
        RefreshGhostArc(def, pos, ghostNormal, ghostYaw, true);
        ShowSnapOverlay(ghostTarget, axis, sign, t1, t2, tu, tv, nu, nv, du, dv, ghostValid);
        TintGhost(ghostValid);
    }

    /// <summary>ROUND-UP1 FIX B. The build-space height a NEW part must not go
    /// below - i.e. where the FLOOR actually is for THIS machine.
    ///
    /// The rule used to read `pos.y - newHalf.y &lt; 0.05f`: "is the part below
    /// build-space y = 0.05". Build space is not height above the deck.
    /// MEASURED on the proven skeleton: the core sits at build y 0.700 and
    /// spawns at world y 0.200 - an offset of -0.500, which is exactly
    /// (wheel centre 0.700 - wheel radius 0.180). SpawnBot re-bases every
    /// machine on its LOWEST point, so the plane a robot stands on is the
    /// WHEEL-BOTTOM plane; for that build it is build y 0.520, and the old
    /// test was therefore being evaluated 0.470 m underground.
    ///
    /// Consequence, reproduced through the pointer before this fix: aiming at
    /// the core's UNDERSIDE gave target=core normal=(0,-1,0) valid=True
    /// reason="". The bracket that click placed had its bottom at build y
    /// 0.350, SpawnBot lifted the whole robot 0.170 m to clear it, and a
    /// machine that drove 10.11 m in 6 s at full throttle drove 0.00 m with
    /// all four wheels in the air. Validate() said the build was fine.
    ///
    /// Tolerance is 1 mm, purely for float ties: a ground-scraping wedge whose
    /// bottom sits ON the wheel line is a legitimate and desirable design and
    /// stays legal; only going BELOW the wheel line is refused. MEASURED
    /// positive control (a body raised 0.20 m on its wheels, 230 mm of belly
    /// clearance): a bracket under its chassis lands at bottom 0.550 and is
    /// still accepted, while a second bracket hung under that one lands at
    /// 0.350 and is refused.
    ///
    /// Mobility parts are exempt: a lower wheel does not clip the floor, it
    /// REDEFINES the deck. With no wheel placed yet there is no deck to
    /// measure, so the original absolute build-floor rule stands unchanged.
    /// This gates NEW placements only - it never retroactively invalidates a
    /// build the player already saved.</summary>
    float FloorPlane(bool isWheel)
    {
        if (isWheel) return 0.05f;
        float deck = float.MaxValue;
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (p.def.category != P1Category.Mobility) continue;
            // Wheel radius, from the def rather than a literal, so this and
            // PlacedPart.Half() can never disagree (Half uses size.x * 0.5).
            deck = Mathf.Min(deck, p.pos.y - p.def.size.x * 0.5f);
        }
        return deck == float.MaxValue ? 0.05f : deck - 0.001f;
    }

    /// <summary>The 150 deg window a PIVOT will sweep, drawn while the part is
    /// still on the cursor (owen, 2026-07-28: "the covered angle should be
    /// visible before being attached").
    ///
    /// A held pivot has no arm yet, so there is no rest direction to start the
    /// arc from and no tip radius to draw it at. Both are stated rather than
    /// invented: the window STARTS along the mount face normal projected into
    /// the plane of travel - which is where an arm bolted to the pivot's free
    /// face would stick out - and is drawn at a nominal 0.70 m, about the reach
    /// of a beam-plus-blade arm. Once the part is placed and has a real limb,
    /// RefreshArcPreview redraws it at the limb's true radius and true rest
    /// angle. The two are deliberately different colours so a preview is never
    /// mistaken for a measurement.</summary>
    void RefreshGhostArc(P1PartDef def, Vector3 pos, Vector3 normal, int yaw, bool show)
    {
        if (ghostArcRoot == null)
        {
            if (buildRoot == null) return;
            var g = new GameObject("ghost_arc");
            g.transform.SetParent(buildRoot.transform, false);
            ghostArcRoot = g.transform;
        }
        bool want = show && def != null && def.actuator;
        string key = !want ? "" : def.id + "|" + yaw + "|" + AxisCode(normal) + "|"
                   + Mathf.RoundToInt(pos.x * 50f) + "," + Mathf.RoundToInt(pos.y * 50f)
                   + "," + Mathf.RoundToInt(pos.z * 50f);
        if (key == ghostArcKey) return;
        ghostArcKey = key;
        for (int i = ghostArcRoot.childCount - 1; i >= 0; i--)
            Destroy(ghostArcRoot.GetChild(i).gameObject);
        if (!want) return;

        var probe = new PlacedPart { def = def, yaw = yaw, wheelAxis = normal };
        Vector3 axis = probe.DriveAxis();
        if (axis.sqrMagnitude < 0.01f) return;
        axis = axis.normalized;
        Color c = new Color(1f, 0.92f, 0.35f);          // amber = not placed yet
        var kind = Actuator.KindOfId(def.id);
        Transform save = arcRoot; arcRoot = ghostArcRoot;   // ArcMark parents here
        ArcMark(pos, axis * 0.70f, 0.024f, c, "gaxle");
        if (kind == ActuatorKind.Ram)
        {
            for (int k = 1; k <= 6; k++)
                ArcMark(pos + axis * (Actuator.RAM_STROKE_M * k / 6f),
                        Vector3.zero, 0.034f, c, "gstroke" + k);
            ArrowHead(pos + axis * Actuator.RAM_STROKE_M, axis, 0.065f, c);
            arcRoot = save; return;
        }
        // THE GHOST DRAWS THE PLANE, NOT A WINDOW. It used to draw a confident
        // 150 deg arc with an arrow on it, and owen photographed the result:
        // ghost promises a full sweep up and over, then the assembled arm shows
        // arcFree = 0 because it grinds the deck from the first degree. The
        // ghost had no way to know that - a HELD pivot has no arm, so there is
        // no tip radius, no rest angle, and nothing for ArcClearance to sweep.
        // Every term that decides the real window needs the limb.
        //
        // So it stops guessing. What IS knowable before you attach is the AXLE
        // and the PLANE the arm will travel in, and that never changes when the
        // arm goes on. The window, the direction arrow and the floor-blocked
        // band all appear once there is a limb to measure - and from that moment
        // they are the truth rather than a promise.
        // CONSISTENT BY CONSTRUCTION. The ghost keeps its arrow, but every term
        // below is now the SAME expression RefreshArcPreview evaluates for an
        // actuator that has no limb yet - which is exactly what this part will
        // be one frame after the click. Basis, radius, sweep and sign are all
        // mirrored, so the before/after comparison is an identity, not a
        // resemblance. Previously the ghost invented its own basis (the mount
        // normal) and its own radius (0.70), and the arc visibly jumped the
        // instant you placed it.
        //
        // The arc DOES change later, when you bolt an arm on: the radius becomes
        // the arm's real tip radius and the floor check starts biting. That
        // change is information - it is the build telling you what the arm costs
        // - and it is why the two are drawn in different colours.
        Vector3 u = Vector3.Cross(axis, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(axis, Vector3.forward);
        u = u.normalized;
        Vector3 v = Vector3.Cross(axis, u).normalized;
        float r = 0.28f;                                     // == Max(tipRadius 0.001, 0.28)
        float sweepDeg = kind == ActuatorKind.Spindle
                       ? 360f : Actuator.PIVOT_ARC_DEG;      // == arcLimit
        const float sign = 1f;                               // == arcSign with no limb
        int N = Mathf.Max(6, Mathf.RoundToInt(sweepDeg / 10f));
        for (int k = 0; k <= N; k++)
        {
            float t = sign * (sweepDeg * k / N) * Mathf.Deg2Rad;
            ArcMark(pos + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r,
                    Vector3.zero, 0.032f, c, "garc" + k);
        }
        float te = sign * sweepDeg * Mathf.Deg2Rad;
        ArrowHead(pos + (u * Mathf.Cos(te) + v * Mathf.Sin(te)) * r,
                  ((-u * Mathf.Sin(te) + v * Mathf.Cos(te)) * sign).normalized, 0.075f, c);
        arcRoot = save;
    }

    void HideGhost()
    {
        RefreshGhostArc(null, Vector3.zero, Vector3.zero, 0, false);
        if (ghost != null) ghost.SetActive(false);
        HideSnapOverlay();
        ghostValid = false; ghostReason = ""; ghostSockets = 1; ghostMated = 1;
    }

    /// <summary>Round-2 critic fix 5/7: full deselect — clears the selection
    /// AND destroys the ghost hierarchy object (HideGhost only deactivates it,
    /// which left an inert "ghost" object lingering in the hierarchy). Used by
    /// Esc, palette toggle-off, and every mode transition.</summary>
    // ---- TEST OBSERVATION SEAM -------------------------------------------
    // Read-only windows onto the ghost state, so a harness can drive the REAL
    // player path (Phase0Input.debugPointer -> UpdateBuild -> ghost -> AddPart)
    // and check what the player would actually see. Adding this rather than
    // widening the fields themselves keeps the ghost state private to the
    // builder, which is where it belongs. Nothing here mutates anything.
    public bool    TestGhostValid  { get { return ghostValid; } }
    public string  TestGhostReason { get { return ghostReason; } }
    public Vector3 TestGhostPos    { get { return ghostPos; } }
    public int     TestGhostYaw    { get { return ghostYaw; } }
    public bool    TestGhostShown  { get { return ghost != null && ghost.activeSelf; } }
    public string  TestGhostTarget { get { return ghostTarget != null ? ghostTarget.def.id : "none"; } }
    public Vector3 TestGhostNormal { get { return ghostNormal; } }
    /// <summary>The camera the placement raycast actually uses. A harness MUST
    /// project through this one - projecting through Camera.main would put the
    /// synthetic pointer somewhere the builder never looks.</summary>
    public Camera  TestCam         { get { return cam; } }
    /// <summary>Builder orbit yaw, so a harness can swing the camera round to a
    /// face before hovering it. Without this a test can only reach the three
    /// faces that happen to point at the default 35-degree view, and it will
    /// mis-report the other three as placement failures when they are simply
    /// off-camera - which is exactly what the first run of UserPathTest did.</summary>
    /// <summary>Is the pointer currently over exactly this part? The cascade
    /// probe needs the answer from the SAME field the right-click handler and
    /// the preview read, so a test cannot pass against a hover state the game
    /// does not actually have.</summary>
    public bool HoverIs(PlacedPart p) { return hoverPart == p; }
    /// <summary>How many parts the preview says a right-click would ALSO take.</summary>
    public int TestDoomCount { get { return doomCount; } }
    public float TestOrbitYaw { get { return orbitYaw; } set { orbitYaw = value; } }
    public float TestOrbitPitch { get { return orbitPitch; } set { orbitPitch = value; } }

    // ---- Phase 5: public API for the touch-native builder (MobileBuilderUI) ----
    /// <summary>Set true by the mobile UI when a touch is over one of its
    /// panels, so the placement raycast doesn't fire under the UI (the phone
    /// analogue of the old PANEL_W gate).</summary>
    public static bool uiPointerBlocked;
    // OWEN 2026-08-02 save dialog, desktop half. Both front ends or neither -
    // a rule that lives in one front end is not a rule.
    public const string NAME_GATE_HINT = "Type a name in the box first \u2014 every robot and draft needs one.";
    bool deskSaveDlg, deskSaveFocus;
    /// <summary>Desktop twin of MobileBuilderUI's confirm face.</summary>
    bool deskSaveConfirm;
    string deskSaveBuf = "", deskSaveErr = "", deskSaveNote = "";
    public bool TestDesktopSaveDialogOpen { get { return deskSaveDlg; } }
    public float TestOrbitDist { get { return orbitDist; } set { orbitDist = Mathf.Clamp(value, 2.2f, 9f); } }
    public int PaletteCount { get { return palette != null ? palette.Length : 0; } }
    public string PartLabel(int i) { return (i >= 0 && i < PaletteCount) ? palette[i].label : ""; }
    public string PartCategory(int i) { return (i >= 0 && i < PaletteCount) ? palette[i].category.ToString() : ""; }
    public bool PartIsActuator(int i) { return i >= 0 && i < PaletteCount && palette[i].actuator; }
    public int PartCost(int i) { if (i < 0 || i >= PaletteCount) return 0; var d = palette[i]; return d.CostOf(d.EffectiveMat(activeMat)); }
    public int PartMass(int i) { if (i < 0 || i >= PaletteCount) return 0; return Mathf.RoundToInt(palette[i].MassOf(activeMat)); }
    // ---- C1: career inventory. DERIVED accounting: the inventory is never
    // mutated by building - remaining = owned minus used - so place / remove /
    // undo / load can never drift the counts.
    //
    // OWEN 2026-08-05: "used" means used BY THE BUILD THAT IS LOADED, and by
    // nothing else. R4 briefly made it mean used by the whole stable (doc
    // section 7's shared pool); that made keeping a second design cost the
    // hardware to build it, so the only way to try an idea was to retire the
    // robot that works. See Career.SnapshotShortfall for the full reasoning.
    // Saved designs are blueprints; the loaded one is the machine.
    /// <summary>Remaining stock of palette part i in the ACTIVE material.
    /// -1 = unlimited (career off, dev free-build, or the core).</summary>
    public int CareerRemaining(int i)
    {
        if (!Career.active || Career.FreeParts) return -1;
        if (i <= 0 || i >= PaletteCount) return -1;
        var d = palette[i];
        string mat = d.EffectiveMat(activeMat);
        int rem = Career.CountOf(d.id, mat) - CareerUsed(d.id, mat);
        // Clamp: a shortfall build (loaded with more parts than owned) reads
        // as 0 in stock - a negative here would collide with the -1
        // 'unlimited' sentinel and open the gate exactly one part short.
        return rem < 0 ? 0 : rem;
    }
    int CareerUsed(string id, string mat)
    {
        string cm = MatDB.Canon(mat);
        int u = 0;
        for (int k = 1; k < placed.Count; k++)
            if (placed[k].def.id == id && MatDB.Canon(placed[k].MatName()) == cm) u++;
        // Nothing else consumes. Other saved robots are DESIGNS, not machines
        // holding hardware, so they make no claim on this number (owen,
        // 2026-08-05). Whether each of THEM could be built is answered per-row
        // in the stable list by Career.RobotReady, not by taxing this one.
        return u;
    }
    /// <summary>C1 placement gate. Negative remaining (a shortfall build was
    /// loaded) refuses further placement too.</summary>
    public bool CareerAllows(int i) { int r = CareerRemaining(i); return r == -1 || r > 0; }
    bool CareerAllowsMat(PlacedPart pp, string newMat)
    {
        if (!Career.active || Career.FreeParts) return true;
        if (placed.Count > 0 && pp == placed[0]) return true;
        return Career.CountOf(pp.def.id, newMat) - CareerUsed(pp.def.id, newMat) > 0;
    }
    /// <summary>C1: "N× Material Label" for every line this build uses beyond
    /// what the career inventory owns. Empty = fully owned.</summary>
    public List<string> CareerShortfall()
    {
        var lack = new List<string>();
        if (!Career.active || Career.FreeParts) return lack;
        var seen = new List<string>();
        for (int k = 1; k < placed.Count; k++)
        {
            string id = placed[k].def.id, mat = placed[k].MatName();
            string key2 = id + "|" + MatDB.Canon(mat);
            if (seen.Contains(key2)) continue;
            seen.Add(key2);
            int miss = CareerUsed(id, mat) - Career.CountOf(id, mat);
            if (miss > 0) lack.Add(miss + "× " + MatDB.Get(mat).name + " " + placed[k].def.label);
        }
        return lack;
    }
    // PoolWarning() lived here. It warned that "the stable is over the parts
    // pool ... buy them, or retire a robot to free its parts" - a sentence that
    // is now false in both halves: the stable has no pool to be over, and
    // retiring frees nothing because nothing was held. Deleted rather than
    // reworded; the honest version of this warning is the per-robot readiness
    // badge in the stable list, which says it about the right object and says
    // it before you commit rather than after (owen, 2026-08-05).
    public string PartId(int i) { return (i >= 0 && i < PaletteCount) ? palette[i].id : ""; }
    /// <summary>R2 (critic finding 2): the material part i is PINNED to, or null
    /// when the picker actually applies to it. The six chips on the BUILD row
    /// are the only material filter the player has, and for wheel/battery/gyro
    /// they do nothing at all - which is why "Wheel  14 kg  ×0" on the default
    /// Aluminum chip read as a bug instead of as "wheels are Rubber".</summary>
    public string PartPinnedMat(int i) { return (i >= 0 && i < PaletteCount && !palette[i].materialChoice) ? palette[i].matName : null; }
    public string PartMatKey(int i) { return (i >= 0 && i < PaletteCount) ? palette[i].EffectiveMat(activeMat) : activeMat; }
    /// <summary>SHOP CATALOG (owen 2026-08-01: "how to shop parts with
    /// different materials?" - he tried and could not work it out, because
    /// material was a GLOBAL MODE set by chips on the BUILD tab which the shop
    /// silently inherited). Every material palette part i may legally be BOUGHT
    /// in, in picker order. A pinned part (materialChoice == false) has exactly
    /// one - its own - and allowedMats restricts the rest, so the catalog can
    /// never offer a carbon-fibre engine block or a titanium wheel.</summary>
    public string[] PartLegalMats(int i)
    {
        var outl = new List<string>();
        if (i < 0 || i >= PaletteCount) return outl.ToArray();
        var d = palette[i];
        if (!d.materialChoice) { outl.Add(d.matName); return outl.ToArray(); }
        var src = d.allowedMats != null ? d.allowedMats : MatDB.Order;
        foreach (var k in src) if (d.Accepts(k) && !outl.Contains(k)) outl.Add(k);
        if (outl.Count == 0) outl.Add(d.matName);
        return outl.ToArray();
    }
    /// <summary>True when part i's material is a permanent property of the part
    /// (wheel to Rubber, battery, gyro) and not a choice at the counter.</summary>
    public bool PartMatFixed(int i) { return i >= 0 && i < PaletteCount && !palette[i].materialChoice; }

    /// <summary>The part's one-line explanation. OWEN 2026-08-05: "where do I
    /// see the introduction of each part?" - nowhere, on touch. These strings
    /// have always existed and rendered in exactly ONE place, a GUILayout.Label
    /// in the desktop IMGUI panel, which the Device Simulator cannot even
    /// dispatch a click to. MobileBuilderUI did not mention `desc` once. So the
    /// game shipped a written explanation of every part that a player on a
    /// phone could not reach.</summary>
    public string PartDesc(int i)
    { return (i >= 0 && i < PaletteCount && palette[i].desc != null) ? palette[i].desc : ""; }

    /// <summary>Durability of part i in `mat`: DamageResolver's own formula,
    /// HP_K x strengthRel x volume. Not a second copy of the rule - the two
    /// numbers below are read straight off the same inputs the arena uses.
    ///
    /// WHY THE SHOP SHOWS HP PER KG AND NOT JUST HP. Both HP and mass are
    /// linear in volume, so WITHIN one material HP is a fixed multiple of the
    /// kg already on the row - measured at 1.3 HP/kg for every structural part
    /// in Aluminium, so a bare HP column would repeat the mass column in
    /// different units. The number that actually decides a purchase is HP per
    /// kg, which is HP_K x strengthRel / density and varies ELEVEN-FOLD across
    /// the table (CarbonFiber 4.1, Tungsten 0.4). Section 3b says weight caps
    /// make strength per kilogram the whole question; this is where that
    /// question gets answered.</summary>
    public float PartHP(int i, string mat)
    {
        if (i < 0 || i >= PaletteCount) return 0f;
        Vector3 sz = palette[i].size;
        return DamageResolver.HP_K * MatDB.Get(mat).strengthRel * sz.x * sz.y * sz.z;
    }
    // SwapSourceFor / CheapestSwapSource lived here and are gone with the
    // REWORK button (owen, 2026-08-05: "Let's remove rework"). Both existed
    // only to answer "which material would this be reworked FROM", which is
    // now a question nothing asks. CheapestSwapSource had already lost its
    // last caller when the shop went per-material and was dead before this.
    //
    // Changing a part's material is SELL + BUY now, and the measurements say
    // that was usually the better trade anyway: sell-back is 50% of the SOURCE
    // while the rework fee was 10% of the TARGET, so rework only won while the
    // target cost under 5x the source. Beam Aluminium -> Titanium is exactly
    // 5x and came out 292 against 293 - a one-scrap difference sitting on the
    // formula's own boundary. Every downgrade was strictly worse: reworking a
    // Tungsten beam to Aluminium cost 6 scrap where selling and rebuying paid
    // 1325, and nothing on screen said so.

    /// <summary>Free units of part i in a SPECIFIC material (owned minus used by
    /// the build being edited). -1 = unlimited. The shop's sell guard rail needs
    /// this per material now that every material is on screen at once.</summary>
    public int CareerRemainingMat(int i, string mat)
    {
        if (!Career.active || Career.FreeParts) return -1;
        if (i <= 0 || i >= PaletteCount) return -1;
        var d = palette[i];
        int rem = Career.CountOf(d.id, mat) - CareerUsed(d.id, mat);
        return rem < 0 ? 0 : rem;
    }
    /// <summary>Harness seam: the desktop panel's scroll offset, so a capture
    /// can photograph the shop block instead of the top of the panel.</summary>
    public float PanelScrollY { get { return panelScroll.y; } set { panelScroll.y = value; } }
    /// <summary>C3: the build's summed catalog value - the number the
    /// underdog multiplier compares against the opponent's.</summary>
    public int BuildValueCareer()
    {
        int v = 0;
        foreach (var p2 in placed) v += CareerDB.PartPrice(p2.def.id, p2.MatName());
        return v;
    }
    /// <summary>C3: union AABB of the placed build (world axes).</summary>
    public Vector3 BuildAabbSize()
    {
        bool got = false; Bounds b = new Bounds();
        foreach (var p2 in placed)
        {
            if (p2.go == null) continue;
            foreach (var r2 in p2.go.GetComponentsInChildren<Renderer>())
            {
                if (!got) { b = r2.bounds; got = true; }
                else b.Encapsulate(r2.bounds);
            }
        }
        return got ? b.size : Vector3.zero;
    }
    // ---- onboarding tips (OWEN 2026-08-03) -----------------------------
    /// <summary>OWEN 2026-08-03: "should we add a tutorial to teach new users
    /// how to play the game?"
    ///
    /// There was one, and it had rotted into misinformation: it opened with
    /// "open the ROBOTS tab -> found your stable" and told you to "SAVE on
    /// ROBOTS", both of which describe the UI as it was that MORNING. SAVE had
    /// moved to the build bar and founding-first stopped being required the
    /// moment SAVE learned to name its own build. A tutorial that narrates a
    /// flow you deliberately deleted is worse than none - it sends a first-time
    /// player to a tab to hunt for a button that is not there.
    ///
    /// It also only ever taught NAVIGATION. Nothing said what makes a machine
    /// legal, what the six materials are for, where scrap comes from, or that
    /// the core is the thing you lose by. All four are things this code knows
    /// and the player was left to infer.
    ///
    /// ONE list, both front ends, because two copies of onboarding is how the
    /// desktop half ends up a version behind - which is exactly what had
    /// happened to the strip in OnGUI.</summary>
    public const int TIP_COUNT = 7;

    /// <summary>Which tip the player is actually on, derived from real state so
    /// it can never sit there telling you to do something you have done.
    /// Returns TIP_COUNT when there is nothing left to say.</summary>
    public int CareerTipStep()
    {
        if (!Career.active || Career.Data == null || Career.Data.tipsOff) return TIP_COUNT;
        int ts = Career.Data.tutorialStep;
        // Derive forward from what actually exists, not just the stored step -
        // a save edited or carried across a version should not strand anyone.
        var st = Career.Data.stable;
        if (st != null && st.Count > 0)
        {
            if (ts < 1) ts = 1;
            for (int i = 0; i < st.Count; i++)
                if (st[i] != null && !string.IsNullOrEmpty(st[i].snapshot)) { if (ts < 2) ts = 2; break; }
        }
        if (ts >= 3)
        {
            // Past the first fight the rest are the SYSTEMS tips, paced one per
            // fight rather than arriving as a wall of text nobody reads before
            // they have felt the problem it describes.
            int after = 4 + Mathf.Max(0, Career.Data.fights - 1);
            return after >= TIP_COUNT ? TIP_COUNT : after;
        }
        if (placed.Count <= 1) return 0;      // core only
        if (Validate() != null) return 1;     // placed, but it cannot fight yet
        if (ts < 2) return 2;                 // legal and unsaved
        return 3;
    }

    /// <summary>ctx is the front end's current surface: "build", "league", or
    /// anything else. Desktop passes "" because its whole panel is on screen at
    /// once, so "go to the X tab" would be nonsense there.</summary>
    public string CareerTip(int i, string ctx)
    {
        bool onBuild  = ctx == "build"  || ctx == "";
        bool onLeague = ctx == "league" || ctx == "";
        string n = "TIP " + (i + 1) + "/" + TIP_COUNT + "  \u00b7  ";
        if (i <= 0)
            return n + (onBuild ? "pick a part below, then tap the robot to bolt it on"
                                : "open the BUILD tab to start your machine");
        if (i == 1)
        {
            // The LIVE reason, not a paraphrase. Validate() is what the FIGHT
            // button consults, so quoting it means the tip and the refusal can
            // never say different things.
            string v = Validate();
            return n + "to fight, a machine needs a wheel, a battery, and every part touching another"
                     + (v != null ? "  \u2014  " + v : "");
        }
        if (i == 2)
            return n + (onBuild ? "SAVE names this build and founds it in your stable"
                                : "open BUILD and press SAVE to name your machine");
        if (i == 3)
            return n + (onLeague ? "SCOUT is free \u2014 study the opponent, then FIGHT"
                                 : "open the LEAGUE tab to enter your first contest");
        if (i == 4)
            return n + "materials: the same beam is 7\u00d7 heavier in Tungsten than Aluminium, and twice as strong \u2014 spend that weight where you hit, not everywhere";
        if (i == 5)
            return n + "scrap: SHOP buys parts and SELL returns half \u2014 to change a part's material, sell it and buy the one you want";
        return n + "the core is the KO target \u2014 lose it and you lose the fight. Armour it, and bolt the battery across two seams so one break cannot take it";
    }

    /// <summary>C3 enrollment validation: weight cap, refusals specific enough
    /// to act on. Null = fits.
    ///
    /// OWEN 2026-08-03: "we already have the weight limit. why do we also need
    /// size limit?" The size box is gone - one rule, mass. Worth knowing what
    /// that costs, because it is not nothing: mass = volume x density, and the
    /// density spread here is 18x (ABS 1.05 -> Tungsten 19.3). A 1500 kg cap
    /// therefore buys 3 Tungsten beams or 59 ABS ones, so a light material now
    /// bounds nothing but the wallet. If ultralight structure starts reading
    /// as strictly correct, the lever to pull is density or a reach cap - NOT
    /// this function.</summary>
    public string CareerValidate(CareerDB.League lg)
    {
        var lack = CareerShortfall();
        if (lack.Count > 0)
            return "Build uses parts you don't own: " + string.Join(", ", lack.ToArray()) + " \u2014 shop or sell-back first.";
        int mass = BuildMassInt;
        if (mass > lg.weightCap)
            return string.Format("{0} kg over the {1} cap ({2} kg limit, build is {3} kg).",
                mass - Mathf.RoundToInt(lg.weightCap), lg.name, Mathf.RoundToInt(lg.weightCap), mass);
        return null;
    }

    /// <summary>OWEN 2026-08-02: "why clicking fight doesn't trigger anything
    /// in this view".
    ///
    /// It WAS triggering. StartCareerFight refused, set `message` and played
    /// Deny() - and the bar that carries `message` lives at the TOP of the
    /// screen for 7 s, while the button he tapped is at the bottom right of a
    /// contest list. Reproduced on his own save: career scrap 0, inventory =
    /// the starter kit (no engine, no spinner blade), build named "Spinner1"
    /// -> CareerShortfall listed three missing parts and the fight was refused
    /// before a FightManager ever existed. Confirmed msgtext still held the
    /// full explanation and had simply expired.
    ///
    /// The real defect is an asymmetry. Career legality has TWO rules and the
    /// UI treated them completely differently: the weight cap is printed in the
    /// status bar continuously ("799/1500 kg Scrapyard Open"), while
    /// parts-you-own only ever appeared as a 7-second flash AFTER you failed.
    /// One rule you can answer before committing; the other you could only
    /// discover by being refused. This makes both answerable before the tap.
    ///
    /// ONE set of checks, two renderings. StartCareerFight calls this, so a
    /// button's appearance can never drift from what the button actually does -
    /// that drift is how you ship a live-looking button that refuses, or a
    /// greyed one that would have worked.
    ///
    /// Returns null when the contest can be entered right now. Otherwise it
    /// returns the LONG message (what the message bar shows) and hands back a
    /// row-sized restatement in `shortTag`.</summary>
    public string CareerFightBlocker(int li, int ci, out string shortTag)
    {
        shortTag = null;
        if (!Career.active) return null;
        if (li < 0 || li >= CareerDB.Leagues.Length) { shortTag = "unavailable"; return "Contest unavailable."; }
        var blg = CareerDB.Leagues[li];
        if (ci < 0 || ci >= blg.contests.Length) { shortTag = "unavailable"; return "Contest unavailable."; }
        var bc = blg.contests[ci];

        if (!Career.LeagueUnlocked(li))
        {
            shortTag = "league locked";
            return blg.name + " is locked \u2014 beat every " + CareerDB.Leagues[li - 1].name + " contest first.";
        }

        // OWEN 2026-08-02, found while fixing the overlapping banner: that
        // banner has been promising "can't enroll" since C6.5 and NOTHING
        // enforced it. CareerShortfall returns empty while devFreeBuild is on,
        // so a draft sailed through CareerValidate and could be entered into a
        // contest with parts the player does not own - the exact thing the
        // whole inventory system exists to prevent. A UI that asserts a rule
        // the code does not implement is worse than no rule; the claim is now
        // true, and it is stated on the row that refuses.
        if (Career.Drafting)
        {
            shortTag = "draft \u2014 CONVERT to enroll";
            return "This is a DRAFT \u2014 parts are unlimited, so it cannot be entered. "
                 + "CONVERT buys the parts it is missing and leaves draft mode.";
        }

        // THE GATE - exactly the checks StartCareerFight runs, in its order.
        string err = Validate();
        if (err == null) err = CareerValidate(blg);
        if (err == null && Career.Data.scrap < bc.entryFee)
            err = "Entry fee is " + bc.entryFee + " scrap \u2014 you hold " + Career.Data.scrap + ".";
        if (err == null) return null;

        // Row-sized restatement of the SAME failure. Falls back to the long
        // text, so a new rule added to CareerValidate can never make the button
        // lie - at worst the row label gets verbose.
        var lack = CareerShortfall();
        if (lack.Count > 0) shortTag = "needs " + string.Join(", ", lack.ToArray());
        else if (BuildMassInt > blg.weightCap)
            shortTag = (BuildMassInt - Mathf.RoundToInt(blg.weightCap)) + " kg over cap";
        else if (Career.Data.scrap < bc.entryFee)
            shortTag = "needs " + bc.entryFee + " scrap entry fee";
        else shortTag = err;
        return err;
    }

    public string CareerFightBlocker(int li, int ci)
    { string t; return CareerFightBlocker(li, ci, out t); }

    public void SelectPart(int i) { selected = (selected == i) ? -1 : i; }
    public int SelectedPart { get { return selected; } }
    public bool HasSelection { get { return selected >= 0; } }
    public string ActiveMatKey { get { return activeMat; } set { if (MatDB.Has(value)) activeMat = value; } }
    public int BuildMassInt { get { int m = 0; foreach (var p in placed) m += Mathf.RoundToInt(p.Mass()); return m; } }
    public int CreditBudgetNow { get { return CREDIT_BUDGET; } }
    /// <summary>C5: the build's canonical drive direction (also the spawned
    /// bot's local drive axis) - the bench gives its AI driver this.</summary>
    public Vector3 DriveDirNow { get { return driveDir; } }
    public int PlacedCount { get { return placed.Count; } }
    public string LastMessage { get { return message; } }   // Phase 5: surfaced in the touch UI status bar
    /// <summary>Bounds of the ghost's rendered mesh, for checking that a rotate
    /// moves the DRAWING and not just the collision box. The blade bug was
    /// exactly this: box rotated, mesh did not.</summary>
    public Vector3 TestGhostMeshSize()
    {
        if (ghost == null) return Vector3.zero;
        var rs = ghost.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return Vector3.zero;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.size;
    }

    public void Deselect()
    {
        selected = -1;
        if (ghost != null) { Destroy(ghost); ghost = null; }
        ghostMats.Clear();
        ghostBaseCols.Clear();
        ghostBuiltKey = "";
        ghostValid = false;
        ghostReason = "";
        ghostSockets = 1;
        ghostMated = 1;
        if (snapOverlay != null) { Destroy(snapOverlay); snapOverlay = null; }
        snapKey = "";
        snapDotMats.Clear();
        snapDotOffs.Clear();
    }

    // ------------------------------------------------- CoM + support polygon

    /// <summary>A cube marker under arcRoot. `along` non-zero stretches it into
    /// a rod pointing that way.</summary>
    void ArcMark(Vector3 pos, Vector3 along, float size, Color c, string name)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(arcRoot, false);
        g.transform.position = pos;
        if (along.sqrMagnitude > 1e-6f)
        {
            g.transform.rotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
            g.transform.localScale = new Vector3(size, along.magnitude, size);
        }
        else g.transform.localScale = Vector3.one * size;
        g.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(c);
    }

    /// <summary>A chevron head: three cubes of shrinking size marching along
    /// `dir`, which reads as an arrow from any camera angle without needing a
    /// mesh. Placed at the END of the travel, so the arrow says both WHICH WAY
    /// and HOW FAR.</summary>
    void ArrowHead(Vector3 tip, Vector3 dir, float size, Color c)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        dir = dir.normalized;
        for (int k = 0; k < 3; k++)
            ArcMark(tip + dir * (size * k * 0.6f), Vector3.zero,
                    size * (1f - 0.25f * k), c, "head" + k);
    }

    /// <summary>SHOW THE MOTION, DO NOT JUST NAME IT (owen, 2026-07-28).
    ///
    /// The panel already said "drive axis: X (side-to-side)", which still asks
    /// the player to derive a swing from an axis name and a right-hand rule.
    /// This draws it: a rod along the axle, and a RING through the plane the
    /// limb actually travels in, at the limb's own tip radius. Cyan = this
    /// actuator drives something; orange = it is blocked and will not move at
    /// all, which is the state the "beam <-> beam" warning describes in words.
    ///
    /// It draws the PLANE rather than an arrow, deliberately. Which of the two
    /// directions a pivot sweeps is chosen in the ARENA by Actuator.PickArcSign
    /// (whichever way digs into the deck less) and the builder does not run
    /// that. A preview that is right about the plane and silent about the sign
    /// is worth more than one that is confidently backwards half the time.</summary>
    void RefreshArcPreview()
    {
        if (arcRoot == null) return;
        for (int i = arcRoot.childCount - 1; i >= 0; i--)
            Destroy(arcRoot.GetChild(i).gameObject);

        foreach (var li in LimbReport())
        {
            var act = li.act;
            Vector3 axis = act.DriveAxis();
            if (axis.sqrMagnitude < 0.01f) continue;
            axis = axis.normalized;
            bool live = li.blockedBy == null && li.parts > 0;
            Color c = live ? new Color(0.20f, 0.95f, 1f) : new Color(1f, 0.45f, 0.25f);

            ArcMark(act.pos, axis * 0.70f, 0.028f, c, "axle");

            var kind = Actuator.KindOfId(act.def.id);
            if (kind == ActuatorKind.Ram)
            {
                // straight stroke + a head at the far end
                for (int k = 1; k <= 6; k++)
                    ArcMark(act.pos + axis * (Actuator.RAM_STROKE_M * k / 6f),
                            Vector3.zero, 0.04f, c, "stroke" + k);
                ArrowHead(act.pos + axis * Actuator.RAM_STROKE_M, axis, 0.075f, c);
                continue;
            }

            float r = Mathf.Max(li.tipRadius, 0.28f);
            // Basis for the plane of travel. `u` is put along the limb's own
            // rest direction where there is one, so the arc STARTS where the
            // arm actually is instead of at an arbitrary angle.
            Vector3 u = Vector3.zero;
            if (li.memberParts != null && li.memberParts.Count > 0)
            {
                Vector3 cen = Vector3.zero;
                foreach (var mp in li.memberParts) cen += mp.pos;
                cen /= li.memberParts.Count;
                u = Vector3.ProjectOnPlane(cen - act.pos, axis);
            }
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(axis, Vector3.forward);
            u = u.normalized;
            Vector3 v = Vector3.Cross(axis, u).normalized;

            // A SPINDLE really does turn continuously, so it keeps the full
            // ring. A PIVOT does not, and drawing it one was owen's complaint:
            // "it looks like it can rotate 360 degrees, which is confusing."
            float sweepDeg = kind == ActuatorKind.Spindle
                           ? 360f
                           : li.arcLimit * Mathf.Rad2Deg;      // PIVOT_ARC_DEG
            float sign = li.arcSign >= 0f ? 1f : -1f;
            // How much of that sweep is actually clear of the deck. Beyond it
            // the arm is grinding the floor, and the panel already says so in
            // words - this is the same fact as a picture.
            float freeDeg = kind == ActuatorKind.Spindle
                          ? sweepDeg
                          : Mathf.Clamp(li.arcFree * Mathf.Rad2Deg, 0f, sweepDeg);

            int N = Mathf.Max(6, Mathf.RoundToInt(sweepDeg / 10f));
            for (int k = 0; k <= N; k++)
            {
                float deg = sweepDeg * k / N;
                float t = sign * deg * Mathf.Deg2Rad;
                Vector3 pos = act.pos + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * r;
                bool blockedHere = deg > freeDeg + 0.01f;
                // LOUDER WHEN BLOCKED, NOT QUIETER. This was 0.022 and dark
                // red - smaller and dimmer than the clear case - so an arm with
                // arcFree = 0 drew its whole sweep in tiny dark dots on a dark
                // floor and read as NOTHING DRAWN. owen reported the preview as
                // broken twice; it was working and whispering the most important
                // thing it had to say. A fully blocked arc is now the loudest
                // object in the builder.
                ArcMark(pos, Vector3.zero, blockedHere ? 0.050f : 0.032f,
                        blockedHere ? new Color(1f, 0.08f, 0.05f) : c, "arc" + k);
            }
            // head at the END of the travel, pointing along the tangent
            float te = sign * sweepDeg * Mathf.Deg2Rad;
            Vector3 endPos = act.pos + (u * Mathf.Cos(te) + v * Mathf.Sin(te)) * r;
            Vector3 tangent = (-u * Mathf.Sin(te) + v * Mathf.Cos(te)) * sign;
            ArrowHead(endPos, tangent.normalized, 0.075f, c);
        }
    }

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
            // Round-1 critic fix 8: a wheel is only "floating" when the
            // ground plane (the build's lowest feature) sits beyond its
            // SUSPENSION'S reach, not merely below its resting tire. Reach
            // comes straight from RaycastWheelDrive geometry: rays run
            // travel+radius = 0.48 m below the anchor, i.e. the tire finds
            // ground up to travel (0.30 m) below its nominal bottom; +0.10 m
            // slack because low skids far from the CoM let the chassis tilt
            // the last bit. Standing armor hanging below wheel level is a
            // SKID, not a disqualifier — the old lowest+0.05 rule read the
            // user's build as "4 wheels can't reach the ground" while it
            // drove fine. A genuinely high-mounted wheel (≥0.40 m above the
            // lowest feature) still warns.
            if (p.pos.y - 0.18f > lowest + 0.40f) { floatingWheels++; continue; }
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
        RefreshArcPreview();

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

    /// <summary>ROUND-1-IMPL FIX (r1 critic CRITICAL 1: "no credit budget
    /// exists, so the material table is a ladder rather than a tradeoff and
    /// Tungsten is strictly dominant"). The spend limit for one machine.
    ///
    /// WHY A BUDGET AT ALL, in the critic's own numbers. Cost was computed,
    /// displayed and spent against nothing, so the §4.3 material table was not
    /// a set of trades, it was an ordering: everything Tungsten does is
    /// strictly better than what Aluminium does, and it costs a number that
    /// never comes due. Measured over 40 matches: qa_c3_lance_w (all-Tungsten,
    /// 3904 kg, 22386 cr) went W4/L0 against a Champion and did not lose a
    /// single part in four matches, while the same-geometry ABS twin went
    /// W1/L3 and reached 0 of 12 parts twice. There is no decision in that.
    ///
    /// WHY 4000, and the numbers it was chosen from. I priced every build in
    /// the project through this exact path (P1Placed.Cost, all Validate-clean):
    ///     qa_c6_hammer_abs   279     qa_c5_discus_abs    344
    ///     qa_owen_build_SAFE 750     qa_c4_flipper_al   1361
    ///     qa_hammer_test    1355     qa_c2_rotor_cf     1533
    ///     qa_c1_hammer_ti   3278     qa_c3_lance_w     22386
    /// and the whole authored enemy roster, which is what the budget really has
    /// to be fair against, since the player is scored against these machines:
    ///     scout 341 · mauler 660 · ripper 724 · tipper 743
    ///     bulwark 1516 · widowmaker 1827
    /// (tipper and widowmaker each gained a 36 cr spindle in the 2026-07-27
    ///  disc conversion, so they read 779 and 1863 now; 4000 still clears the
    ///  dearest opponent by ~2.1x and the conclusion is unchanged.)
    /// 4000 is ~2.2x the dearest opponent on the roster: generous enough that
    /// every build anyone has ever tested here is still legal - including the
    /// 3278 cr Titanium hammer that went W3/L1, and owen's own 750 cr machine
    /// with 5.3x headroom - and it is the ONLY value in that spread that
    /// excludes the strictly-dominant Tungsten lance, which sits 5.6x over it.
    /// The point is not to ban Tungsten: 4000 buys roughly 190 kg of it, which
    /// is a blade and a spike, or a beam, and NOT a whole machine. That is
    /// exactly the trade the material table was written to offer.
    ///
    /// Static rather than const so a tuning pass, a sweep or a future
    /// difficulty/campaign tier can move it without a recompile.</summary>
    public static int CREDIT_BUDGET = 0;   // Phase 5: credit limit removed — 0 = unlimited (every guard tests > 0)

    /// <summary>Total credits this build spends. Same per-part Cost() the panel
    /// and the hover readout already show, so the three numbers can never
    /// disagree.</summary>
    public int BuildCost()
    {
        int c = 0;
        foreach (var p in placed) c += p.Cost();
        return c;
    }

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
        if (!AllConnected(placed))
            return "Structure has floating parts — " + FirstOrphanLabel(placed)
                 + " isn't flush with anything. Faces must touch; sockets must mate.";
        // Budget last, so a half-finished machine complains about being
        // half-finished before it complains about being expensive.
        int cost = BuildCost();
        if (CREDIT_BUDGET > 0 && cost > CREDIT_BUDGET)
            return string.Format("Over budget: {0} cr of {1}. Cheaper materials, or fewer parts.",
                                 cost, CREDIT_BUDGET);
        return null; // valid
    }

    // ------------------------------------------------------------- test mode

    /// <summary>Phase 4: no longer const — ladder rungs may shrink the arena
    /// (StartFight sets it per fight; StartTest resets to the standard 7 m).</summary>
    public static float ARENA_HALF = 7f;

    public void StartTest()
    {
        string err = Validate();
        if (err != null) { message = err; return; }

        ARENA_HALF = 7f;   // Phase 4: the test box is always the standard arena
        ArenaHazards.Clear();   // C3A: test drives stay hazard-free
        TouchControls.Ensure();
        TouchControls.fightActive = true;   // Phase 5
        if (Progression.Data.tutorialStep == 1) { Progression.Data.tutorialStep = 2; Progression.Save(); }

        Deselect();      // round-1 fix 3 / round-2 fix 7: no stale selection or ghost object
        // The lens is a build-mode tool; drop it before the build root is
        // hidden so the real finishes are back the moment you return.
        SetMatView(false);
        hoverPart = null;
        buildRoot.SetActive(false);
        mode = Mode.Test;
        message = "";
        BuildArena();

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
        dpos.x = Mathf.Clamp(dpos.x, -(ARENA_HALF - 1.2f), ARENA_HALF - 1.2f);
        dpos.z = Mathf.Clamp(dpos.z, -(ARENA_HALF - 1.2f), ARENA_HALF - 1.2f);
        dummyRobot = CompoundRobot.Build("RamDummy", dummySpecs, dummyConns, new Vector3(dpos.x, 0.15f, dpos.z), Quaternion.identity);
        dummyRobot.combatEnabled = false;   // settle-protected like everything else

        testRobot = SpawnBot(placed, "PlayerBuild", new Vector3(0f, 0f, -4f),
                             Quaternion.identity, driveDir, out testDrive);
        TouchControls.hasFire = testRobot != null && testRobot.GetComponentInChildren<Actuator>() != null;
        // Spawn protection (round-1 fix 1, test-drive parity): both bodies
        // settle onto their suspension for 1 s before damage/shear arms.
        combatArmAt = Time.time + 1.0f;
        // ROUND-UP1 FIX A. StartFight has always done this loop; StartTest
        // never did. Actuator.Fire() is
        //     playerControlled ? Phase0Input.FireHeld() : aiFire
        // and aiFire is only ever written by AIController, which test drive
        // does not create - so EVERY player actuator was permanently dead in
        // the one mode whose entire purpose is checking that a build works.
        // MEASURED on a pivot+blade limb, trigger held 9 s:
        //     TEST  before fix: maxRate 0.00 rad/s, travel 0.000, 0 cycles
        //     FIGHT (control):  maxRate 14.00 rad/s, travel 2.618, 14 cycles
        // Input isolation is unaffected: the ram dummy carries no actuators,
        // and nothing else in test mode reads Phase0Input.
        foreach (var act in testRobot.GetComponentsInChildren<Actuator>(true))
            act.playerControlled = true;
        // P0: test drive has no FightManager to grant controls, so the spawn
        // path is the writer here. SpawnBot machines default to AI (fail
        // closed); the one machine the human drives is granted Keyboard.
        testRobot.controlSource = ControlSource.Keyboard;
        hudWheelMassInt = WheelMassInt(placed);

        followCam = cam.gameObject.AddComponent<FollowCamera>();
        followCam.target = testRobot.transform;
        followCam.forwardHint = driveDir; // chase from behind the DRIVE direction
        // Keep the camera INSIDE the arena (critic fix: the raw chase offset
        // put it 4 m outside the wall, which occluded the bot into a distant
        // speck). Clamp to the walls minus margin; DesiredPos raises the
        // camera when clamped so the view stays clear over the wall.
        followCam.clampHalf = ARENA_HALF - 0.8f;
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

        AddHeadlight(testRobot, driveDir);
    }

    /// <summary>Shared arena build — Phase 2B extracts this from StartTest so
    /// FIGHT reuses the exact same 14x14 m box, walls, posts and markings.
    /// Also mounts the floating-damage-number spawner (subscribed to
    /// DamageResolver.OnHit) for the arena's lifetime.</summary>
    /// <summary>iPad fix (2026-07-30): a Plane's zero-thickness MeshCollider
    /// let heavy bodies tunnel straight through under device frame pacing -
    /// robots fell out of the arena while the camera chased them down. Every
    /// floor keeps its Plane visual but collides as a solid 1 m deep box.</summary>
    public static void FloorBoxCollider(GameObject floor)
    {
        var mc = floor.GetComponent<MeshCollider>();
        if (mc != null) Object.Destroy(mc);
        var box = floor.AddComponent<BoxCollider>();
        box.size = new Vector3(10f, 1f, 10f);    // plane footprint is 10x10 local
        box.center = new Vector3(0f, -0.5f, 0f); // top flush with the surface
    }

    /// <summary>Last-ditch floor net (2026-07-30): if a machine still ends up
    /// under the arena, lift it back and kill its velocity instead of letting
    /// the chase camera follow it into the void.</summary>
    static void FloorNet(CompoundRobot r)
    {
        if (r == null) return;
        Transform t = r.transform;
        Vector3 p = t.position;
        float m = ARENA_HALF + 0.3f;
        bool below = p.y < -3f;
        bool outside = Mathf.Abs(p.x) > m || Mathf.Abs(p.z) > m;
        if (!below && !outside) return;
        Vector3 target = new Vector3(
            Mathf.Clamp(p.x, -(ARENA_HALF - 1.2f), ARENA_HALF - 1.2f),
            1.2f,
            Mathf.Clamp(p.z, -(ARENA_HALF - 1.2f), ARENA_HALF - 1.2f));
        t.position += target - p;
        foreach (var rb in r.GetComponentsInChildren<Rigidbody>())
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        CompoundRobot.Log("FloorNet: machine returned to the arena");
    }

    void BuildArena()
    {
        float HALF = ARENA_HALF;   // Phase 4: arena size is per-fight now
        sandboxRoot = new GameObject("sandbox");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "arena_floor";
        FloorBoxCollider(floor);
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

            // iPad bug fix (2026-07-30): the 1.5 m visual wall was low enough
            // to beach a rammed bot on its flat top or throw it clean out of
            // the arena. Invisible barrier continues the wall up to 6 m.
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = "wall_barrier_" + i;
            barrier.transform.SetParent(sandboxRoot.transform, false);
            barrier.transform.position = alongX ? new Vector3(0f, 3f, sign * HALF) : new Vector3(sign * HALF, 3f, 0f);
            barrier.transform.localScale = alongX ? new Vector3(2f * HALF + 0.5f, 6f, 0.5f) : new Vector3(0.5f, 6f, 2f * HALF + 0.5f);
            Object.Destroy(barrier.GetComponent<MeshRenderer>());

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

        // Floating damage numbers + hit flashes (Phase 2B §7 readability):
        // the spawner lives on the arena root, so its DamageResolver.OnHit
        // subscription exists exactly as long as the arena does.
        sandboxRoot.AddComponent<FloatingDamageSpawner>();
    }

    /// <summary>Headlight strip on the core's leading face so the front stays
    /// obvious while driving.</summary>
    void AddHeadlight(CompoundRobot bot, Vector3 dDir)
    {
        foreach (Transform child in bot.transform)
        {
            if (!child.name.StartsWith("core")) continue;
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(child, false);
            lamp.transform.localPosition = dDir * 0.16f + Vector3.up * 0.06f;
            lamp.transform.localRotation = Quaternion.LookRotation(dDir);
            lamp.transform.localScale = new Vector3(0.16f, 0.05f, 0.02f);
            var lr = lamp.GetComponent<Renderer>();
            lr.sharedMaterial = PartVisualFactory.Emissive(
                new Color(0.15f, 0.95f, 0.25f), new Color(0.1f, 2.0f, 0.25f), 0f, 0.5f);
            // R5 finding 1, same rule: a light does not cast a shadow.
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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

    /// <summary>
    /// Phase 2B generalization of the old SpawnDesign(): spawn ANY builder-space
    /// part list as a fighting CompoundRobot — the player's `placed` design or
    /// the AI's authored recipe — through the SAME path (AI parity: identical
    /// physics, damage and shear rules). `dDir` is the build's local drive
    /// direction (decides which wheels steer); `rot` yaws the whole bot so
    /// opponents can spawn facing each other.
    /// </summary>
    CompoundRobot SpawnBot(List<PlacedPart> build, string botName, Vector3 xz,
                           Quaternion rot, Vector3 dDir, out RaycastWheelDrive drv)
    {
        var core = build[0];
        var body = new List<PlacedPart>();
        var wheels = new List<PlacedPart>();
        foreach (var p in build)
            (p.def.category == P1Category.Mobility ? wheels : body).Add(p);

        var specs = new PartSpec[body.Count];
        float minY = float.MaxValue;
        for (int i = 0; i < body.Count; i++)
        {
            var p = body[i];
            Vector3 rel = p.pos - core.pos;
            Vector3 size = p.Half() * 2f;
            // Weapons ride their mount axis in the id (the arena visual
            // factory only sees id + AABB) and are billed at the builder's
            // half-solid mass so the HUDs agree.
            bool weapon = p.def.category == P1Category.Weapon;
            // ROUND-UP2 FIX A: was `p.def.id + AxisCode(p.wheelAxis)` inline
            // here and NOWHERE ELSE, which is exactly how the builder ended up
            // drawing a different robot from the one that fought. Same string,
            // one definition (VisualId).
            string sid = weapon ? VisualId(p.def, p.DriveAxis()) + "_" + i
                                : p.def.id + "_" + i;
            // ROUND-6 FIX 6. This used to read `p.def.Mass()` - the DEF's
            // default material - so every weapon entered the arena at its
            // catalogue mass no matter what the player made it of. MEASURED
            // before the fix: a spinner billed on the build screen at 111.6 kg
            // in Tungsten and 15.6 kg in Aluminium was spawned at 45.4 kg (the
            // Steel default) in BOTH cases, with an identical rotational
            // inertia of 0.66 kg m2. Two consequences, both severe: the build
            // screen's mass and the arena's mass disagreed by up to 2.5x on any
            // machine carrying a disc, breaking the round-3 guarantee that they
            // are the same number; and section 6.2's central premise - "a
            // tungsten rim has ~2.5x the inertia of a steel one and therefore
            // costs ~2.5x to spin" - was simply not implemented, which also
            // made the WIDOWMAKER's authored identity (tungsten disc,
            // enormous bite, drinks its own battery) fiction. p.Mass() is the
            // placement's material, which is what the panel already quotes.
            specs[i] = new PartSpec(sid, size, rel, p.MatName(), p == core,
                                    p.def.edgeHardness, weapon ? p.Mass() : 0f);
            minY = Mathf.Min(minY, rel.y - p.Half().y);
        }

        var conns = new List<CompoundRobot.Conn>();
        for (int i = 0; i < body.Count; i++)
            for (int j = i + 1; j < body.Count; j++)
                if (Touching(body[i], body[j]))
                {
                    // Socket-count connection strength: every derived seam
                    // carries its mated-socket count as the break multiplier.
                    float mult = SharedSockets(body[i], body[j]);
                    // Phase 4: a seam onto an actuator is a machined bearing
                    // housing, not two boxes bolted face to face. A 0.30 face
                    // mates through ONE socket, and a x1 seam is the weakest
                    // joint in the game - the battery mount that decides
                    // matches. A weapon that falls off the instant it touches
                    // anything is not a weapon, so actuator seams carry x3.
                    if (body[i].def.actuator || body[j].def.actuator) mult *= 3f;
                    conns.Add(new CompoundRobot.Conn(i, j, mult));
                }

        var anchors = new Vector3[wheels.Count];
        var steer = new bool[wheels.Count];
        var axes = new Vector3[wheels.Count];
        var mounts = new int[wheels.Count];
        var wMasses = new float[wheels.Count];
        float meanAlong = 0f;
        for (int i = 0; i < wheels.Count; i++)
            meanAlong += Vector3.Dot(wheels[i].pos - core.pos, dDir) / wheels.Count;
        for (int i = 0; i < wheels.Count; i++)
        {
            Vector3 rel = wheels[i].pos - core.pos;
            anchors[i] = rel;
            // Wheels in the leading half ALONG THE DRIVE DIRECTION steer —
            // "front" is wherever the wheels actually drive, not body +Z.
            steer[i] = Vector3.Dot(rel, dDir) > meanAlong + 0.03f;
            axes[i] = wheels[i].wheelAxis; // keep the mounted axle orientation in the arena
            minY = Mathf.Min(minY, rel.y - 0.18f);

            // Mount tracking (wheel fall-off fix): record which BODY part this
            // wheel is bolted to — the nearest touching part. When that part
            // dies or shears off, RaycastWheelDrive drops the wheel as debris
            // instead of leaving it hovering on an invisible anchor.
            wMasses[i] = wheels[i].def.Mass();
            mounts[i] = -1;
            float bestD = float.MaxValue;
            for (int b = 0; b < body.Count; b++)
                if (Touching(wheels[i], body[b]))
                {
                    float d = (body[b].pos - wheels[i].pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; mounts[i] = b; }
                }
        }

        // Spawn AT suspension height (round-3 fix): 0.55 dropped the bot 0.35 m
        // in free fall — the HUD showed phantom 2 m/s "speed" while visibly
        // stationary, and builds whose wheels sit high slammed down onto the
        // CHASSIS, which then skidded on the floor (slow, grindy, shear-prone).
        // 0.22 put wheel anchors ~0.40 up, which was right only while the
        // arena hung each wheel 0.375 m under its mount. Now the wheel rests
        // ON its mount (RaycastWheelDrive.RestExtension), so minY - which has
        // always assumed a wheel bottom exactly `radius` under the anchor - is
        // finally the truth, and 0.02 gives the same ~2 cm settle without the
        // 0.20 m free fall. Rays still reach: strut top 0.195 up + 0.48 long.
        float spawnY = 0.02f - minY;
        var robot = CompoundRobot.Build(botName, specs, conns.ToArray(),
                                        new Vector3(xz.x, spawnY, xz.z), rot);
        // Round-1 fix 1: EVERY arena bot spawns protected — no joint stress,
        // no HP damage — until its owner arms it (FightManager's bell, or the
        // test-mode combatArmAt timer). The settle drop can't cost parts.
        robot.combatEnabled = false;

        // Fold the wheels' mass into the body (critic fix: builder said
        // 407 kg, arena HUD said 309 — the wheels were massless in the arena).
        // Must happen BEFORE RaycastWheelDrive.Init, which auto-sizes the
        // suspension from rb.mass.
        float wheelMass = 0f;
        Vector3 wheelMoment = Vector3.zero;
        foreach (var w in wheels)
        {
            float wm = w.Mass();
            wheelMass += wm;
            wheelMoment += wm * (w.pos - core.pos);
        }
        robot.extraMass = wheelMass;
        robot.extraMassLocalCoM = wheelMass > 0f ? wheelMoment / wheelMass : Vector3.zero;
        robot.RecomputeMass();

        drv = robot.gameObject.AddComponent<RaycastWheelDrive>();
        drv.Init(robot.rb, anchors, steer, axes, mounts, wMasses, robot);

        // Phase 3 (§7.1): self-righting is a PART. Any build - player or AI -
        // carrying at least one gyro gets the same stabilizer component, with
        // the part indices so a sheared gyro stops working mid-fight.
        var gyroIdx = new List<int>();
        for (int i = 0; i < body.Count; i++)
            if (body[i].def.id == "gyro") gyroIdx.Add(i);
        // Every build still gets the component, but as of 2026-08-02 it does
        // NOTHING without a gyro (GyroStabilizer.BASE_ARM = 0). It is attached
        // unconditionally so that fitting or shearing a gyro is a live change
        // mid-fight rather than something fixed at spawn.
        var gs = robot.gameObject.AddComponent<GyroStabilizer>();
        gs.self = robot;
        gs.gyroParts = gyroIdx.ToArray();
        gs.forwardLocal = dDir.sqrMagnitude > 0.01f ? dDir.normalized : Vector3.forward;

        // Phase 3 (§6.2): the energy budget is HARDWARE too. Every power part
        // contributes its capacity and its draw ceiling, indexed, so shearing
        // the battery off takes the energy with it exactly the way shearing the
        // gyro off takes the self-righting.
        var pIdx = new List<int>(); var pKJ = new List<float>(); var pKW = new List<float>();
        for (int i = 0; i < body.Count; i++)
        {
            if (body[i].def.category != P1Category.Power) continue;
            pIdx.Add(i); pKJ.Add(body[i].def.energyKJ); pKW.Add(body[i].def.powerKW);
        }
        var pp = robot.gameObject.AddComponent<PowerPlant>();
        pp.Init(robot, pIdx.ToArray(), pKJ.ToArray(), pKW.ToArray());
        if (drv != null) drv.power = pp;
        return robot;
    }

    /// <summary>Arena HUD mass must equal the builder panel EXACTLY: same
    /// per-part rounding, summed (round-3 fix: float-sum showed builder+1 kg).</summary>
    int WheelMassInt(List<PlacedPart> build)
    {
        int m = 0;
        foreach (var p in build)
            if (p.def.category == P1Category.Mobility) m += Mathf.RoundToInt(p.Mass());
        return m;
    }

    // ------------------------------------------------------------ fight mode
    // (Phase 2B, §7/§7.1/§8: player vs the authored "Mauler" AI opponent)

    /// <summary>
    /// FIGHT: same Validate() gate and the same arena as TEST DRIVE, but the
    /// ram dummy is replaced by the AI-driven Mauler. Player spawns one side,
    /// Mauler the other, facing each other 8 m apart along the player's drive
    /// axis. FightManager owns the match timer/win conditions/results.
    /// </summary>
    /// <summary>§8: which roster bot to fight, and at what tier. Both are
    /// panel choices; the tier defaults to the entry's own rating.</summary>
    public string opponentId = "scout";   // Fix 2026-07-29 (playtest): the first fight must be winnable — three straight 100-0 Mauler shutouts. Was "mauler".
    public AiTier opponentTier = AiTier.Rookie;

    // Fix 2026-07-29 (playtest, QHD): scroll state for the builder panel and the
    // height reserved for the always-visible aim-feedback + fight controls band.
    Vector2 panelScroll;
    const float FIGHT_BAND_H = 540f;   // Phase 4: + ladder & scrap rows in the pinned band

    /// <summary>P4c: why the CURRENT build cannot attempt challenge `c`, or
    /// null if it qualifies. Pinned-material parts (core, battery, gyro,
    /// wheels) are exempt from material constraints — the player cannot
    /// choose their material, so it cannot disqualify them.</summary>
    public string ChallengeBlocker(Progression.Challenge c)
    {
        int m = 0;
        foreach (var p in placed) m += Mathf.RoundToInt(p.Mass());
        if (c.maxMass > 0 && m > c.maxMass)
            return string.Format("build is {0} kg — needs ≤ {1} kg", m, c.maxMass);
        if (c.maxCost > 0 && BuildCost() > c.maxCost)
            return string.Format("build costs {0} scrap — needs ≤ {1} scrap", BuildCost(), c.maxCost);
        if (c.noWeapons)
            foreach (var p in placed)
                // R2-CRITIC FIX (finding 1): a strictly-bare shover CANNOT win —
                // bumps deal zero by owen's rule, so the judges' damage column
                // always ruled against it (measured: 0 dealt, 100% margin loss).
                // Real "unarmed" combat robots ARE wedges: the wedge (near-zero
                // damage, throws upward) is the one legal edge, making the win
                // path flips and count-outs, as intended.
                if (p.def.category == P1Category.Weapon && !p.def.id.StartsWith("wedge"))
                    return p.def.label + " is a weapon part — wedges only in this one";
        if (c.onlyMat != null)
            foreach (var p in placed)
            {
                string pid = p.def.id;
                if (pid.StartsWith("core") || pid.StartsWith("battery")
                    || pid.StartsWith("gyro") || pid.StartsWith("wheel")) continue;
                if (MatDB.Canon(p.MatName()) != c.onlyMat)
                    return p.def.label + " is " + MatDB.Get(p.MatName()).name
                         + " — everything choosable must be " + MatDB.Get(c.onlyMat).name;
            }
        return null;
    }

    /// <summary>P4c: start a build-constraint challenge fight (validated
    /// against the current build at the click).</summary>
    public void StartChallenge(int i)
    {
        if (i < 0 || i >= Progression.Challenges.Length) return;
        var c = Progression.Challenges[i];
        string blocked = ChallengeBlocker(c);
        if (blocked != null) { message = "Challenge " + c.label + ": " + blocked; return; }
        opponentId = c.oppId;
        opponentTier = c.tier;
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = i;
        StartFight();
    }

    /// <summary>Phase 4: fight the current ladder rung — sets opponent/tier
    /// from the rung and marks the fight for the reward pass.</summary>
    public void StartLadderFight()
    {
        var r = Progression.CurrentRung();
        if (r == null)
        {
            // R2-CRITIC FIX (finding 3): after LADDER COMPLETE there was no
            // repeatable income beyond ~350-max exhibitions, starving the
            // 10.5k budget sinks. The champion now defends the title: replay
            // the final rung at full purse, no advancement.
            if (Progression.Data.rung >= Progression.Ladder.Length)
            {
                var last = Progression.Ladder[Progression.Ladder.Length - 1];
                opponentId = last.oppId;
                opponentTier = last.tier;
                Progression.activeRungIndex = Progression.Ladder.Length - 1;
                Progression.activeChallengeIdx = -1;
                StartFight();
                return;
            }
            Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1; StartFight(); return;
        }
        opponentId = r.oppId;
        opponentTier = r.tier;
        if (Progression.Data.tutorialStep == 2) { Progression.Data.tutorialStep = 3; Progression.Save(); }
        Progression.activeRungIndex = Progression.Data.rung;
        Progression.activeChallengeIdx = -1;
        StartFight();
    }

    // ---- C3: career contests -------------------------------------------
    GameObject scoutRoot;
    string scoutTitle = "", scoutStats = "", scoutBlurb = "";
    // R5 (critic finding 5): the contest being scouted, so the screen can end
    // in a decision. Scouting that cannot say "yes" sends the player back to
    // re-find the row they just came from.
    int scoutLi = -1, scoutCi = -1;
    float scoutSaveYaw, scoutSavePitch, scoutSaveDist;
    GUIStyle scoutHeadStyle, scoutBodyStyle; Texture2D scoutBgTex;
    public bool Scouting { get { return scoutRoot != null; } }

    /// <summary>The scouting card's three lines and the contest it refers to,
    /// for whoever is drawing the card.
    ///
    /// OWEN 2026-08-04: "when I click FIGHT or BACK button at the scout page,
    /// nothing happens." Same root cause as the boot chooser fixed in d1f08e4:
    /// Unity's Device Simulator disables the mouse device and substitutes a
    /// simulated touchscreen, and IMGUI dispatches from mouse events - so every
    /// GUI.Button in this game renders in the simulator and none of them can be
    /// pressed. ScoutHud was the last IMGUI screen still reachable from the
    /// touch UI, and it is a dead end: the only way out of scouting is those
    /// two buttons.
    ///
    /// The touch UI draws the card in uGUI now, which receives the simulated
    /// touches. These accessors are how it gets the text without duplicating
    /// the formatting.</summary>
    public string ScoutTitle { get { return scoutTitle; } }
    public string ScoutStats { get { return scoutStats; } }
    public string ScoutBlurb { get { return scoutBlurb; } }
    public int ScoutLeague { get { return scoutLi; } }
    public int ScoutContest { get { return scoutCi; } }

    /// <summary>C3: enroll and fight a league contest. Validates build +
    /// league rules, debits the entry fee, captures both sides' value for
    /// the underdog multiplier, then runs the normal fight path.</summary>
    public void StartCareerFight(int li, int ci)
    {
        if (!Career.active || li < 0 || li >= CareerDB.Leagues.Length) return;
        var lg = CareerDB.Leagues[li];
        if (ci < 0 || ci >= lg.contests.Length) return;
        var c = lg.contests[ci];
        Career.targetLeagueIdx = li;
        // Crowd size follows the league: The Yard is a handful of people in a
        // scrapyard, The Crucible is section 4b's "full broadcast kit". The
        // crowd is progression feedback you can hear. (StartScout deliberately
        // does NOT set it - a scouting turntable has no audience.)
        CrowdAudio.SetVenue(li);
        EndScout();
        // Single gate - the same call the FIGHT buttons use to decide whether
        // they look available. See CareerFightBlocker.
        string blocked = CareerFightBlocker(li, ci);
        if (blocked != null) { message = blocked; SfxSynth.Deny(); return; }
        if (c.entryFee > 0) Career.Txn(-c.entryFee, "entry fee " + c.id);
        Career.fightBuildValue = BuildValueCareer();
        var recipe = EnemyRoster.Recipe(c.oppId, palette, c.armourMat);
        int ov = 0;
        foreach (var p2 in recipe) ov += CareerDB.PartPrice(p2.def.id, p2.MatName());
        Career.fightOppValue = ov;
        Career.activeLeague = lg.id;
        Career.activeContest = c.id;
        if (Career.autosave) Career.Save();
        opponentId = c.oppId;
        opponentTier = c.tier;
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = -1;
        StartFight();
        // StartFight re-validates and can refuse; if the fight never started,
        // hand the fee back and clear the contest context.
        if (mode != Mode.Fight && Career.activeContest != null)
        {
            if (c.entryFee > 0) Career.Txn(c.entryFee, "entry fee refund " + c.id);
            Career.activeLeague = null; Career.activeContest = null;
        }
    }

    /// <summary>C3 scouting: the opponent's real build on a turntable, with
    /// value / mass / weapon / flavor. Same recipe + spawn path the fight
    /// uses - what you scout is what you meet.</summary>
    public void StartScout(int li, int ci)
    {
        if (li < 0 || li >= CareerDB.Leagues.Length) return;
        var lg = CareerDB.Leagues[li];
        if (ci < 0 || ci >= lg.contests.Length) return;
        var c = lg.contests[ci];
        Career.targetLeagueIdx = li;
        EndScout();
        scoutLi = li; scoutCi = ci;
        // R5 (critic finding 5): the scout camera inherited whatever the build
        // orbit happened to be, which is how the opponent ended up photographed
        // from almost straight overhead at 2.3% of the frame - you could not
        // read the silhouette and the weapon named in the text was not
        // identifiable in the image at all, which is the whole job of this
        // screen. Frame it: low three-quarter angle, close in.
        scoutSaveYaw = orbitYaw; scoutSavePitch = orbitPitch; scoutSaveDist = orbitDist;
        orbitYaw = 35f; orbitPitch = 16f; orbitDist = 2.6f;
        var entry = EnemyRoster.Find(c.oppId);
        var recipe = EnemyRoster.Recipe(c.oppId, palette, c.armourMat);
        float smass = 0f; int sval = 0; string weapon = "none";
        foreach (var p2 in recipe)
        {
            smass += p2.Mass();
            sval += CareerDB.PartPrice(p2.def.id, p2.MatName());
            if (p2.def.category == P1Category.Weapon) weapon = p2.def.label;
        }
        RaycastWheelDrive sdrv;
        var bot = SpawnBot(recipe, "scout_display", new Vector3(0f, 0.9f, 0f),
                           Quaternion.identity, Vector3.forward, out sdrv);
        scoutRoot = bot.gameObject;
        if (sdrv != null) Destroy(sdrv);
        foreach (var rb in scoutRoot.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
        scoutRoot.AddComponent<ScoutSpin>();
        buildRoot.SetActive(false);
        scoutTitle = string.Format("SCOUTING \u2014 {0} ({1}) \u00b7 {2} \u00b7 {3} \u00b7 hazards: {4}",
            entry.label, c.tier, lg.name, lg.arenaName, ArenaHazards.Summary(lg.arenaId));
        scoutStats = string.Format("mass {0} kg \u00b7 value {1} scrap \u00b7 weapon: {2} \u00b7 purse {3} scrap \u00b7 entry fee {4} scrap",
            Mathf.RoundToInt(smass), sval, weapon, c.purse, c.entryFee);
        scoutBlurb = entry.blurb;
    }

    public void EndScout()
    {
        if (scoutRoot != null)
        {
            Destroy(scoutRoot);
            orbitYaw = scoutSaveYaw; orbitPitch = scoutSavePitch; orbitDist = scoutSaveDist;
        }
        scoutRoot = null;
        scoutLi = -1; scoutCi = -1;
        if (buildRoot != null && mode == Mode.Build) buildRoot.SetActive(true);
    }

    void ScoutHud()
    {
        float sc2 = GuiScale;   // R4 finding 3: one rule, one place
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc2, sc2, 1f));
        float w = Screen.width / sc2;
        // R5 (critic finding 2): GuiScale was already being applied here - the
        // remaining problem was that everything INSIDE the scaled matrix used
        // the stock IMGUI skin size, so the three lines a player reads before
        // spending an entry fee were the smallest type in the game AND grey on
        // the lightest pixels of a default GUI.Box gradient. Explicit styles,
        // near-white on the dock's own near-opaque panel colour.
        if (scoutBgTex == null)
        {
            // Linear texture: the project renders in LINEAR colour space, so a
            // default (sRGB) 1x1 would draw this near-black panel at about sRGB
            // 0.28 - a mid grey, which is most of what made the old GUI.Box
            // header grey-on-grey in the first place.
            scoutBgTex = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            scoutBgTex.hideFlags = HideFlags.HideAndDontSave;
            scoutBgTex.SetPixel(0, 0, new Color(0.06f, 0.07f, 0.09f, 0.96f));
            scoutBgTex.Apply();
        }
        if (scoutHeadStyle == null)
        {
            scoutHeadStyle = new GUIStyle(GUI.skin.label);
            scoutHeadStyle.fontSize = 22; scoutHeadStyle.fontStyle = FontStyle.Bold; scoutHeadStyle.wordWrap = true;
            scoutHeadStyle.normal.textColor = new Color(0.82f, 0.90f, 1f);
            scoutBodyStyle = new GUIStyle(GUI.skin.label);
            scoutBodyStyle.fontSize = 19; scoutBodyStyle.wordWrap = true;
            scoutBodyStyle.normal.textColor = new Color(0.94f, 0.95f, 0.98f);
        }
        // OWEN 2026-08-04, with a screenshot of the scouting card: the header
        // ran off the right edge ("hazards: 2 floor" cut mid-phrase) and BACK
        // sat half under the notch.
        //
        // This screen is IMGUI, so every bit of the safe-area and physical-size
        // work done on the touch UI passed it by - and so does the legibility
        // sweep in CareerSmoke, which walks uGUI Text components and therefore
        // cannot see a single pixel drawn here. It was the one screen with no
        // check of any kind pointed at it.
        //
        // Screen.width is also the wrong width twice over: under the simulator
        // it reports the editor WINDOW, and even on a real device it counts the
        // pixels behind the notch as usable.
        var sa = UnityEngine.Device.Screen.safeArea;
        float dw = UnityEngine.Device.Screen.width, dhh = UnityEngine.Device.Screen.height;
        if (dw < 1f || sa.width < 1f) { dw = Screen.width; dhh = Screen.height; sa = new Rect(0f, 0f, dw, dhh); }
        float insL = Mathf.Max(0f, sa.x) / sc2;
        float insR = Mathf.Max(0f, dw - (sa.x + sa.width)) / sc2;
        float insT = Mathf.Max(0f, dhh - (sa.y + sa.height)) / sc2;
        float x0 = insL + 10f;
        float x1 = (dw / sc2) - insR - 10f;
        float avail = Mathf.Max(120f, x1 - x0);

        // R5 (critic finding 5): the header used to span the full width while
        // its content ended at 30% of it. Size the panel to what is in it.
        float need = Mathf.Max(scoutHeadStyle.CalcSize(new GUIContent(scoutTitle)).x,
                     Mathf.Max(scoutBodyStyle.CalcSize(new GUIContent(scoutStats)).x,
                               scoutBodyStyle.CalcSize(new GUIContent(scoutBlurb)).x));
        float bw = Mathf.Min(avail, need + 44f);

        // Wrap rather than clip. The old rects were fixed 28-30 units tall with
        // wordWrap off, so a title longer than the box simply lost its end -
        // and the end is where the hazards live, which is the whole reason you
        // paid to scout. Heights come from the styles so the panel is as tall
        // as its content needs, whatever the arena is called.
        float tw = bw - 30f;
        float h1 = scoutHeadStyle.CalcHeight(new GUIContent(scoutTitle), tw);
        float h2 = scoutBodyStyle.CalcHeight(new GUIContent(scoutStats), tw);
        float h3 = scoutBodyStyle.CalcHeight(new GUIContent(scoutBlurb), tw);
        float panelY = insT + 8f;
        float panelH = 12f + h1 + 6f + h2 + 4f + h3 + 12f;

        GUI.DrawTexture(new Rect(x0, panelY, bw, panelH), scoutBgTex);
        float ly = panelY + 12f;
        GUI.Label(new Rect(x0 + 14f, ly, tw, h1), scoutTitle, scoutHeadStyle); ly += h1 + 6f;
        GUI.Label(new Rect(x0 + 14f, ly, tw, h2), scoutStats, scoutBodyStyle); ly += h2 + 4f;
        GUI.Label(new Rect(x0 + 14f, ly, tw, h3), scoutBlurb, scoutBodyStyle);

        int fs2 = GUI.skin.button.fontSize;
        GUI.skin.button.fontSize = 18;
        float by = panelY + panelH + 10f;
        bool back = GUI.Button(new Rect(x1 - 96f, by, 96f, 44f), "BACK");
        // R5 (critic finding 5): scouting must end in a decision. Having paid
        // to look, the player used to have to go back and re-find the row.
        bool fight = false;
        Color bgOld = GUI.backgroundColor;
        if (scoutLi >= 0)
        {
            GUI.backgroundColor = new Color(1f, 0.62f, 0.24f);
            fight = GUI.Button(new Rect(x1 - 96f - 10f - 126f, by, 126f, 44f), "FIGHT \u25b8");
            GUI.backgroundColor = bgOld;
        }
        GUI.skin.button.fontSize = fs2;
        GUI.matrix = saved;
        if (fight) { int fl = scoutLi, fc = scoutCi; EndScout(); StartCareerFight(fl, fc); return; }
        if (back) EndScout();
    }

    /// <summary>C4: UI-side refusals surface through the same amber
    /// message channel everything else uses.</summary>
    public void Toast(string msg)
    { if (!string.IsNullOrEmpty(msg)) { message = msg; SfxSynth.Deny(); } }

    // ---- C4: the stable + the Drafting Table ---------------------------
    public string ActiveRobotName()
    {
        int a = Career.active ? Career.Data.activeRobot : -1;
        return a >= 0 && a < Career.Data.stable.Count ? Career.Data.stable[a].name : null;
    }
    public string StableCreate(string name)
    {
        if (!Career.active) return "Career is off.";
        // OWEN 2026-08-02: founding a MACHINE out of a DESIGN is the category
        // error that fills a stable with robots that refuse at the LEAGUE tab -
        // they look real, carry a 0-0 record, and only fail once you are trying
        // to enter a contest. A stable robot is a machine you own.
        if (Career.Drafting)
            return "You are editing a draft \u2014 CONVERT to buy the missing parts first, "
                 + "then found it as a robot.";
        // OWEN 2026-08-02: "we should force user to provide a name when
        // creating a new robot". It used to invent "ROBOT 1", "ROBOT 2"... and
        // that is how a stable fills with machines nobody can tell apart -
        // exactly the same failure as the blueprints that all came out "DRAFT n".
        // Enforced HERE, not only in the touch UI, because desktop IMGUI calls
        // this too and a rule that lives in one front end is not a rule.
        if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
            return "Name the robot first \u2014 type a name in the box above.";
        name = name.Trim();
        foreach (var r in Career.Data.stable) if (r.name == name) return "A robot named " + name + " already exists.";
        Career.Data.stable.Add(new CareerRobot { name = name, snapshot = SnapshotString() });
        Career.Data.activeRobot = Career.Data.stable.Count - 1;
        Career.Data.activeBlueprint = -1;
        if (Career.Data.tutorialStep == 0) Career.Data.tutorialStep = 1;
        if (Career.autosave) Career.Save();
        message = name + " founded \u2014 it holds the current build. SAVE keeps it current.";
        return null;
    }
    /// <summary>OWEN 2026-08-02: "The UI of saving/loading robot is not very
    /// intuitive." True, and the reason is that the OBJECT you are editing and
    /// the TOOLS that edit it lived on different tabs: ROBOTS -> EDIT -> find
    /// BUILD -> work -> find ROBOTS -> SAVE. Five moves for one edit.
    ///
    /// This is the state the build screen was missing to close that loop. It
    /// already knew WHICH robot was being edited (the header prints its name);
    /// it just could not tell you whether you owed it a save.</summary>
    public bool ActiveEditDirty()
    {
        if (!Career.active) return false;
        int b = Career.Data.activeBlueprint;
        if (b >= 0 && b < Career.Data.blueprints.Count)
            return Career.Data.blueprints[b].snapshot != SnapshotString();
        int a = Career.Data.activeRobot;
        if (a < 0 || a >= Career.Data.stable.Count) return false;
        return Career.Data.stable[a].snapshot != SnapshotString();
    }

    /// <summary>Name of whatever is open - robot or draft - or null.</summary>
    public string ActiveEditName()
    {
        if (!Career.active) return null;
        int b = Career.Data.activeBlueprint;
        if (b >= 0 && b < Career.Data.blueprints.Count) return Career.Data.blueprints[b].name;
        int a = Career.Data.activeRobot;
        return a >= 0 && a < Career.Data.stable.Count ? Career.Data.stable[a].name : null;
    }

    public bool ActiveEditIsDraft
    {
        get
        {
            return Career.active && Career.Data.activeBlueprint >= 0
                   && Career.Data.activeBlueprint < Career.Data.blueprints.Count;
        }
    }

    /// <summary>OWEN 2026-08-02: "consider merging to one save button. depending
    /// on what user is editing, save to the corresponding robot or blueprint."
    ///
    /// One commit verb. Which object it lands on is state the game already has
    /// to track anyway - it prints the name in the header - so making the
    /// player choose the button was making them restate something the game
    /// knew. activeRobot and activeBlueprint are mutually exclusive by
    /// construction; every method that opens one clears the other.</summary>
    public string SaveActive()
    {
        if (!Career.active) return "Career is off.";
        int b = Career.Data.activeBlueprint;
        if (b >= 0 && b < Career.Data.blueprints.Count)
        {
            Career.Data.blueprints[b].snapshot = SnapshotString();
            if (Career.autosave) Career.Save();
            message = "Draft " + Career.Data.blueprints[b].name + " saved.";
            return null;
        }
        int a = Career.Data.activeRobot;
        if (a < 0 || a >= Career.Data.stable.Count)
            return "Nothing open to save \u2014 name this build first.";
        return StableSave();
    }

    /// <summary>OWEN 2026-08-02: "The SAVE button on the build tab is disabled
    /// by default, and requires the user to create a new robot or draft from
    /// the ROBOT tab first. I think we should allow the user to save directly
    /// under BUILD."
    ///
    /// He is right, and the shape of the bug is familiar: SAVE was a commit
    /// verb that only worked once you had already performed the real creation
    /// step somewhere else. So the first build a player ever makes - the one
    /// they care most about - is the one SAVE refuses. The naming step was
    /// never the hard part; making them go and find it was.
    ///
    /// True when SAVE has nothing to commit to and should ask for a name.</summary>
    public bool NothingOpen
    {
        get
        {
            if (!Career.active) return false;
            int b = Career.Data.activeBlueprint;
            if (b >= 0 && b < Career.Data.blueprints.Count) return false;
            int a = Career.Data.activeRobot;
            return a < 0 || a >= Career.Data.stable.Count;
        }
    }

    /// <summary>Would a fresh save keep this build as a DRAFT rather than
    /// found a robot? A robot is a machine you OWN; a build leaning on parts
    /// you have not bought is a design, and calling it a robot is how a stable
    /// fills with machines that only fail at the LEAGUE tab.</summary>
    public bool SaveWouldDraft { get { return CareerShortfallItems().Count > 0; } }

    /// <summary>The one line the dialog shows, so the player knows which of
    /// the two they are about to make BEFORE they commit - and why. The
    /// robot/draft distinction is then a consequence of what they built
    /// rather than a quiz they have to pass.</summary>
    public string SaveAsNewNote()
    {
        if (!Career.active) return "";
        var need = CareerShortfallItems();
        if (need.Count == 0)
            return "Founds a robot \u2014 you own every part in this build ("
                 + Mathf.Max(0, placed.Count - 1) + ").";
        var bits = new List<string>();
        foreach (var it in need) bits.Add(it.count + "\u00d7 " + it.mat + " " + it.partId);
        return "Keeps a draft \u2014 you do not own " + string.Join(", ", bits.ToArray())
             + ". CONVERT buys them when you are ready.";
    }

    /// <summary>Name a build that has no object behind it yet. Robot when every
    /// part is owned, draft when it is not - the same rule StableCreate and the
    /// LEAGUE gate already enforce, just applied at creation time instead of
    /// sprung on the player later.</summary>
    public string SaveAsNew(string name)
    {
        if (!Career.active) return "Career is off.";
        if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
            return "Name it first \u2014 type a name in the box above.";
        if (SaveWouldDraft) return BlueprintSave(name);
        // OWEN 2026-08-03 (SAVE AS): founding a robot while a DESIGN is open
        // used to hit StableCreate's draft guard - "CONVERT to buy the missing
        // parts first" - which is the wrong sentence when there are no missing
        // parts. That guard exists to stop a machine being minted from parts
        // you do not own, and SaveWouldDraft above has already established
        // that you own all of them. So close the design and found the machine:
        // this IS a convert, with nothing left to buy.
        Career.Data.activeBlueprint = -1;
        return StableCreate(name);
    }

    public string StableSave()
    {
        int a = Career.Data.activeRobot;
        if (a < 0 || a >= Career.Data.stable.Count) return "No robot selected \u2014 NEW ROBOT first, or LOAD one.";
        Career.Data.stable[a].snapshot = SnapshotString();
        if (Career.Data.tutorialStep == 1 && Validate() == null) Career.Data.tutorialStep = 2;
        if (Career.autosave) Career.Save();
        message = Career.Data.stable[a].name + " saved.";
        return null;
    }
    /// <summary>Open stable slot i: make it the active robot and load its
    /// snapshot into the builder. The button that calls this is labelled LOAD
    /// (owen, 2026-08-05); the method keeps its name because every caller and
    /// every test already says StableEdit, and renaming it would churn four
    /// files to relabel one button.</summary>
    public string StableEdit(int i)
    {
        if (i < 0 || i >= Career.Data.stable.Count) return "No such robot.";
        Career.Data.activeRobot = i;
        Career.Data.activeBlueprint = -1;   // a machine is open, so not drafting
        LoadSnapshot(Career.Data.stable[i].snapshot);
        if (Career.autosave) Career.Save();
        return null;
    }
    public string StableRename(int i, string name)
    {
        if (i < 0 || i >= Career.Data.stable.Count) return "No such robot.";
        if (string.IsNullOrEmpty(name)) return "Type the new name first.";
        message = Career.Data.stable[i].name + " is now " + name.Trim() + ".";
        Career.Data.stable[i].name = name.Trim();
        if (Career.autosave) Career.Save();
        return null;
    }
    public string StableRetire(int i)
    {
        if (i < 0 || i >= Career.Data.stable.Count) return "No such robot.";
        message = Career.Data.stable[i].name + " retired.";
        Career.Data.stable.RemoveAt(i);
        if (Career.Data.activeRobot == i) Career.Data.activeRobot = -1;
        else if (Career.Data.activeRobot > i) Career.Data.activeRobot--;
        if (Career.autosave) Career.Save();
        return null;
    }
    public string BlueprintSave(string name)
    {
        // OWEN 2026-08-02: "the naming requirements for 'new robot' and 'new
        // draft' should be consistent." They sit side by side in the same row;
        // one demanding a name while the other quietly invented "DRAFT 3" was
        // an inconsistency you could see without reading any code.
        //
        // Both now require one, and this is the auto-namer that produced the
        // DRAFT 2 / DRAFT 3 owen actually had in his stable - names that tell
        // you nothing about which design is which. Same argument as ROBOT 1 /
        // ROBOT 2 in StableCreate.
        if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
            return "Name the draft first \u2014 type a name in the box above.";
        name = name.Trim();
        Career.Data.blueprints.Add(new CareerBlueprint { name = name, snapshot = SnapshotString() });
        // The draft you just made is the one you are now editing, so a second
        // SAVE updates it instead of making another copy.
        Career.Data.activeBlueprint = Career.Data.blueprints.Count - 1;
        Career.Data.activeRobot = -1;
        if (Career.autosave) Career.Save();
        message = "Draft " + name + " saved.";
        return null;
    }
    /// <summary>OWEN 2026-08-02: "How do I delete drafts" - you could not.
    /// BlueprintSave and BlueprintEdit shipped without a counterpart, so the
    /// list could only ever grow: every SAVE BP with an empty name box added
    /// another "DRAFT n" and nothing in the game could remove it. Stable robots
    /// had RETIRE from the start; blueprints had nothing, which is the same
    /// asymmetry in miniature as the one the FIGHT gate fixed this morning.
    ///
    /// Mirrors StableRetire exactly, including the arm-then-confirm both UIs
    /// wrap it in: deleting a design is not undoable and must not be a mis-tap.</summary>
    public string BlueprintDelete(int i)
    {
        if (!Career.active) return "Career is off.";
        if (i < 0 || i >= Career.Data.blueprints.Count) return "No such blueprint.";
        message = "Blueprint " + Career.Data.blueprints[i].name + " deleted.";
        Career.Data.blueprints.RemoveAt(i);
        // Same index bookkeeping StableRetire does, for the same reason: the
        // list shifted under the pointer.
        if (Career.Data.activeBlueprint == i) Career.Data.activeBlueprint = -1;
        else if (Career.Data.activeBlueprint > i) Career.Data.activeBlueprint--;
        if (Career.autosave) Career.Save();
        return null;
    }
    public string BlueprintEdit(int i)
    {
        if (i < 0 || i >= Career.Data.blueprints.Count) return "No such blueprint.";
        // No flag to set any more - opening a design IS drafting.
        Career.Data.activeBlueprint = i;
        Career.Data.activeRobot = -1;
        LoadSnapshot(Career.Data.blueprints[i].snapshot);
        message = "Drafting " + Career.Data.blueprints[i].name + " \u2014 everything unlocked. CONVERT buys the missing parts.";
        return null;
    }
    /// <summary>C4: the shortfall as data (part, material, missing count).
    /// Unlike CareerShortfall it does NOT gate on devFreeBuild - the convert
    /// quote is computed while draft mode is on.</summary>
    public List<CareerItem> CareerShortfallItems()
    {
        var need = new List<CareerItem>();
        if (!Career.active) return need;
        var seen = new List<string>();
        for (int k = 1; k < placed.Count; k++)
        {
            string id = placed[k].def.id, mat = MatDB.Canon(placed[k].MatName());
            string key2 = id + "|" + mat;
            if (seen.Contains(key2)) continue;
            seen.Add(key2);
            int miss = CareerUsed(id, mat) - Career.CountOf(id, mat);
            if (miss > 0) need.Add(new CareerItem { partId = id, mat = mat, count = miss });
        }
        return need;
    }
    public int ConvertQuote()
    {
        int q = 0;
        foreach (var it in CareerShortfallItems()) q += CareerDB.PartPrice(it.partId, it.mat) * it.count;
        return q;
    }
    /// <summary>One-tap draft conversion: buy exactly the missing parts,
    /// leave draft mode. Null on success, else the amber refusal.</summary>
    public string ConvertDraft()
    {
        var need = CareerShortfallItems();
        int quote = 0;
        foreach (var it in need) quote += CareerDB.PartPrice(it.partId, it.mat) * it.count;
        if (Career.Data.scrap < quote)
            return "Converting needs " + quote + " scrap for missing parts \u2014 you hold " + Career.Data.scrap + ".";
        foreach (var it in need)
            for (int n = 0; n < it.count; n++) Career.TryBuy(it.partId, it.mat);
        Career.devFreeBuild = false;
        // The build is a real machine now, not a design, so SAVE must not write
        // it back over the draft it came from.
        Career.Data.activeBlueprint = -1;
        message = need.Count == 0 ? "Draft converted \u2014 everything was already owned."
                : "Draft converted \u2014 bought the missing parts for " + quote + " scrap.";
        return null;
    }

    public void StartFight()
    {
        string err = Validate();
        if (err != null) { message = err; return; }

        // Phase 5 fix: a fight must never stack on top of a live test drive
        // (the touch dock used to stay tappable during TEST DRIVE).
        if (mode == Mode.Test) BackToBuild();

        // ---- Phase 4: fight context. A ladder fight (StartLadderFight) has
        // already set activeRungIndex; every other entry point is an
        // exhibition. The rung may shrink the arena — honest content variety:
        // the box changes, the bots never do.
        var p4r = Progression.activeRungIndex >= 0 && Progression.activeRungIndex < Progression.Ladder.Length
                ? Progression.Ladder[Progression.activeRungIndex] : null;
        ARENA_HALF = p4r != null ? p4r.arenaHalf : 7f;
        Progression.rewarded = false;
        Progression.lastRewardLine = "";
        TouchControls.Ensure();
        TouchControls.fightActive = true;   // Phase 5: virtual stick + FIRE on touchscreens

        Deselect();      // round-1 fix 3 / round-2 fix 7: no stale selection or ghost object
        // The lens is a build-mode tool; drop it before the build root is
        // hidden so the real finishes are back the moment you return.
        SetMatView(false);
        hoverPart = null;
        buildRoot.SetActive(false);
        mode = Mode.Fight;
        message = "";
        BuildArena();
        // C3A: per-contest hazard arenas - career contests only; exhibitions
        // and the ladder keep the clean box.
        ArenaHazards.Clear();
        if (Career.active && Career.activeContest != null)
        {
            var lgH = Career.FindLeague(Career.activeLeague);
            if (lgH != null) ArenaHazards.Build(lgH.arenaId, ARENA_HALF);
        }

        // Engagement axis = the player build's drive direction, so W always
        // means "toward the enemy" on the opening exchange.
        Vector3 axis = driveDir;
        testRobot = SpawnBot(placed, "PlayerBuild", -axis * 4f,
                             Quaternion.identity, driveDir, out testDrive);
        TouchControls.hasFire = testRobot != null && testRobot.GetComponentInChildren<Actuator>() != null;
        hudWheelMassInt = WheelMassInt(placed);
        // Input isolation, same contract as RaycastWheelDrive.useAI: only the
        // player's actuators ever read Phase0Input. The AI's are driven by
        // AIController writing aiFire.
        foreach (var act in testRobot.GetComponentsInChildren<Actuator>(true))
            act.playerControlled = true;
        // P0: in a fight the FightManager owns control routing from Setup()
        // (Protect → AI freeze, Bell → its playerSource). No grant here — the
        // AI spawn default carries the pre-bell freeze on its own.
        AddHeadlight(testRobot, driveDir);

        // §8: the chosen roster bot, spawned through the SAME path as the
        // player's build — its local drive dir is +Z, so LookRotation(-axis)
        // faces the player. Nothing about the opponent is privileged.
        var entry = EnemyRoster.Find(opponentId);
        var recipe = EnemyRoster.Recipe(entry.id, palette);
        RaycastWheelDrive drv;
        aiRobot = SpawnBot(recipe, entry.label, axis * 4f,
                           Quaternion.LookRotation(-axis), Vector3.forward, out drv);
        aiDrive = drv;
        aiRobot.controlSource = ControlSource.AI;   // input isolation: never reads Phase0Input (spawn default, said explicitly)
        aiCtrl = aiRobot.gameObject.AddComponent<AIController>();
        aiCtrl.self = aiRobot;
        aiCtrl.drive = aiDrive;
        aiCtrl.target = testRobot;
        aiCtrl.forwardLocal = Vector3.forward;
        aiCtrl.power = aiRobot.GetComponent<PowerPlant>();
        // Difficulty = reaction time + commitment ONLY (§8). Applied to the
        // INSTANCE, never to the shared statics, so the tier cannot leak.
        aiCtrl.ApplyTier(opponentTier);

        // The visual layer (RobotVisuals): ownership marker, motion smear on
        // anything that swings, and battle damage you can see on the machine.
        // Purely additive - it reads gameplay state and writes none of it, so it
        // cannot change a match outcome. Installed AFTER both machines exist so
        // each one knows which side it is.
        RobotVisuals.Install(testRobot, true);
        RobotVisuals.Install(aiRobot, false);

        var fgo = new GameObject("fight_manager");
        fight = fgo.AddComponent<FightManager>();
        fight.enemyName = entry.label;
        fight.Setup(this, testRobot, testDrive, aiRobot, aiDrive, aiCtrl);
        aiCtrl.fm = fight;   // round-2 fix 6: desperation reads the clock/cards

        fightCam = cam.gameObject.AddComponent<FightCamera>();
        fightCam.a = testRobot.transform;
        fightCam.b = aiRobot.transform;
        fightCam.sideDir = Vector3.Cross(Vector3.up, axis).normalized;
        fightCam.clampHalf = ARENA_HALF - 0.8f;
        fightCam.SnapNow();
    }

    /// <summary>
    /// The one authored AI build for Phase 2B: "Mauler", a fair mid-tier
    /// wedge-brawler (~695 kg) from the same P1PartDef palette. Core, a chassis
    /// block fore and aft, engine over the front block, gyro on the core, four
    /// wheels (axles ±X → drives along +Z), front ram spike on +Z.
    /// Plain data — SpawnBot gives it colliders, HP, shear joints, the works.
    ///
    /// Round-4 fix 4 (critic finding 4: "the authored opponent is a broken
    /// build"). Two defects, both fixed by moving the spine from 0.20-wide
    /// beams to 0.40-wide chassis blocks:
    ///   · The wheels were at ±0.24 against a beam whose face is at ±0.10+0.07
    ///     = ±0.17, so they touched NOTHING. Every StartFight logged
    ///     "[wheel-mount] wheel N touches NO part", the drive fell back to a
    ///     guessed support radius, and the wheels could not be lost with their
    ///     mount. The chassis face is at ±0.20, so ±0.27 is FLUSH — the track
    ///     is wider than the broken one was AND the wheels are really bolted on.
    ///   · The gyro hung off the rear beam's back face — the most exposed
    ///     single-socket seam on the machine, and the AI lost it in 3 of 4
    ///     traced matches, after which it flipped and stayed flipped. It now
    ///     sits ON the core (z 0.01 so it also lands on the front block's top
    ///     edge): two independent seams at the geometric centre, so one shear
    ///     cannot take the self-righting away.
    /// Rollover threshold goes from ~0.52 g (0.24 track, CoM ~0.46 up) to
    /// ~0.58 g, and the chassis mass sits low, which is the other half of it.
    /// </summary>
    List<PlacedPart> MaulerRecipe()
    {
        P1PartDef core = null, chassis = null, engine = null, wheel = null, spike = null, gyro = null;
        foreach (var d in palette)
        {
            if (d.id == "core") core = d;
            else if (d.id == "chassis") chassis = d;
            else if (d.id == "engine") engine = d;
            else if (d.id == "wheel") wheel = d;
            else if (d.id == "spike") spike = d;
            else if (d.id == "gyro") gyro = d;
        }
        var L = new List<PlacedPart>
        {
            new PlacedPart { def = core,    pos = new Vector3(0f, 0.7f, 0f) },
            // Chassis half-extents 0.20,0.15,0.25 → flush on the core's ±Z
            // faces at |z| = 0.15 + 0.25 = 0.40.
            new PlacedPart { def = chassis, pos = new Vector3(0f, 0.7f, 0.40f) },   // front block
            new PlacedPart { def = chassis, pos = new Vector3(0f, 0.7f, -0.40f) },  // rear block
            // Engine on the front block's roof; it also reaches the core's top
            // rear edge, so the power part hangs off two seams, not one.
            new PlacedPart { def = engine,  pos = new Vector3(0f, 0.975f, 0.40f) },
            // Gyro on the core roof (0.85 + 0.12 = 0.97), nudged 1 cm forward
            // so it picks up a second seam against the front block.
            new PlacedPart { def = gyro,    pos = new Vector3(0f, 0.97f, 0.01f) },
            // Wheels flush on the blocks' side faces: 0.20 + 0.07 = 0.27.
            new PlacedPart { def = wheel,   pos = new Vector3(0.27f, 0.7f, 0.40f),   wheelAxis = new Vector3(1f, 0f, 0f) },
            new PlacedPart { def = wheel,   pos = new Vector3(-0.27f, 0.7f, 0.40f),  wheelAxis = new Vector3(-1f, 0f, 0f) },
            new PlacedPart { def = wheel,   pos = new Vector3(0.27f, 0.7f, -0.40f),  wheelAxis = new Vector3(1f, 0f, 0f) },
            new PlacedPart { def = wheel,   pos = new Vector3(-0.27f, 0.7f, -0.40f), wheelAxis = new Vector3(-1f, 0f, 0f) },
            // Ram spike off the front block's nose: 0.65 + 0.15 = 0.80.
            new PlacedPart { def = spike,   pos = new Vector3(0f, 0.7f, 0.80f),      wheelAxis = new Vector3(0f, 0f, 1f) },
        };
        return L;
    }

    void UpdateFight()
    {
        FloorNet(testRobot);
        FloorNet(aiRobot);
        if (Phase0Input.BackDown()) { BackToBuild(); return; }
        // R = restart the fight (rematch), same builds — also the REMATCH
        // button on the results screen.
        if (Phase0Input.ResetDown()) { ResetFight(); return; }
    }

    public void ResetFight()
    {
        BackToBuild();
        StartFight();
    }

    /// <summary>"XP"/"XN"/"YP"/"YN"/"ZP"/"ZN" for a (near-)axis vector,
    /// "" when degenerate — the arena-side id suffix for weapon mount axes.</summary>
    static string AxisCode(Vector3 a)
    {
        int ax = 0;
        float b = Mathf.Abs(a.x);
        if (Mathf.Abs(a.y) > b) { ax = 1; b = Mathf.Abs(a.y); }
        if (Mathf.Abs(a.z) > b) { ax = 2; b = Mathf.Abs(a.z); }
        if (b < 0.5f) return "";
        string s = ax == 0 ? "X" : ax == 1 ? "Y" : "Z";
        return s + (a[ax] >= 0f ? "P" : "N");
    }

    /// <summary>ROUND-UP2 FIX A (round-2 critic CRITICAL 1: "builder draws all
    /// six Phase-4 parts in a fallback orientation, ignoring the mount face").
    ///
    /// The id a VISUAL needs in order to know which way it points. Every
    /// Phase-4 visual in PartVisualFactory resolves its orientation through
    /// ParseAxis(id, prefix, fallback), which reads the two-character axis code
    /// off the END of the id. SpawnBot has always appended that code; the three
    /// BUILDER call sites (AddPart, SetPartMaterial, EnsureGhostBuilt) passed
    /// the BARE def.id, so ParseAxis fell through to its fallback every time
    /// and the build screen drew every pivot/spindle hinged about +X and every
    /// ram/blade/wedge/hook pointing +Z no matter which face you bolted it to.
    ///
    /// MEASURED BEFORE THIS, pointer-placed on the -Z, +Y and +X faces of a
    /// bare core and read off the real renderers: pivot/spindle 0.396x0.318x
    /// 0.318, ram 0.318x0.318x0.483, blade 0.495x0.045x0.122, wedge 0.500x
    /// 0.149x0.413, hook 0.112x0.309x0.185 - each IDENTICAL on all three
    /// faces. spike was the only one that turned, because it has its own
    /// branch that rotates the whole GameObject.
    ///
    /// This exists so builder and arena derive orientation from ONE place.
    /// SpawnBot now calls it too, and gets exactly the string it built by hand
    /// before (AxisCode returns "" for a degenerate axis, which appends
    /// nothing - the same no-op the old concatenation produced).</summary>
    public static string VisualId(P1PartDef def, Vector3 axis)
    {
        return NeedsAxis(def) ? def.id + AxisCode(axis) : def.id;
    }

    /// <summary>ROUND-UP3 FIX A. The axis a part's VISUAL is rotated FROM when
    /// it is bolted to a face - i.e. which way the part points when its mount
    /// normal is the default. Vector3.zero means "this part's drawing does not
    /// rotate with the mount face", and its box must not either.
    ///
    /// This is a direct transcription of PartVisualFactory.BuildPart's dispatch
    /// table and has to stay one, so it is written to match it line for line:
    ///   WedgeVis / HookVis   FromToRotation(FORWARD, axis)
    ///   ActuatorVis          FromToRotation(UP,      axis)
    ///   BladeVis             no frame rotation at all - it shapes itself
    ///                        straight out of the incoming AABB, which is why
    ///                        blade measured clean on all six faces and why
    ///                        listing it here would BREAK it.
    /// spike, spinner/spinnerSaw and the wheels are absent on purpose: Half()
    /// returns for those before it ever reaches this, through branches that
    /// have their own (already correct, already verified) axis handling.</summary>
    static Vector3 MountFrameAxis(string id)
    {
        if (id == "wedge" || id == "hook") return Vector3.forward;
        if (id == "pivot" || id == "spindle" || id == "ram") return Vector3.up;
        return Vector3.zero;
    }

    void UpdateTest()
    {
        FloorNet(testRobot);
        FloorNet(dummyRobot);
        if (Phase0Input.BackDown()) { BackToBuild(); return; }
        // R = full arena reset (critic fix: no recovery after a wreck).
        // Reuses the two existing paths so there is exactly one lifecycle.
        if (Phase0Input.ResetDown()) { ResetTest(); return; }

        if (shearTimer > 0f) shearTimer -= Time.deltaTime;

        // Arm damage/shear after the spawn settle (round-1 fix 1).
        if (combatArmAt > 0f && Time.time >= combatArmAt)
        {
            combatArmAt = -1f;
            if (testRobot != null) testRobot.combatEnabled = true;
            if (dummyRobot != null) dummyRobot.combatEnabled = true;
        }

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
        ArenaHazards.Clear();   // C3A: hazards never outlive the fight
        TouchControls.fightActive = false;   // Phase 5
        CompoundRobot.ClearAll();
        // Arena litter sweep (critic fix): severed debris chunks
        // ("<robot>_debris" roots from CompoundRobot.SpawnDebris) and any
        // robot/dummy root that escaped the Spawned registry must never
        // survive into the builder or stack up across T/B cycles.
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go == null || go == buildRoot || go == sandboxRoot || go == gameObject) continue;
            if (go.GetComponent<CompoundRobot>() != null || go.name.Contains("_debris")
                || go.name.StartsWith("RamDummy") || go.name.StartsWith("PlayerBuild")
                || go.name.StartsWith("Mauler") || go.name.StartsWith("fight_manager")
                || go.name.StartsWith("dmg_"))
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
        if (fightCam != null)
        {
            fightCam.enabled = false;   // same same-frame-respawn hazard as followCam
            Destroy(fightCam);
            fightCam = null;
        }
        if (fight != null)
        {
            Destroy(fight.gameObject);  // also caught by the name sweep above
            fight = null;
        }
        testRobot = null;
        testDrive = null;
        dummyRobot = null;
        combatArmAt = -1f;
        aiRobot = null;
        aiDrive = null;
        aiCtrl = null;
        shearTimer = 0f;
        wrecked = false;
        stuckTimer = 0f;
        stuck = false;
        buildRoot.SetActive(true);
        mode = Mode.Build;
        // Round-1 fix 3: entering the builder NEVER inherits a stale part
        // selection (round-2 fix 7: including the inert ghost object), and the
        // click that dismissed the previous screen gets a grace window before
        // placement raycasts go live again.
        Deselect();
        clickGraceUntil = Time.unscaledTime + 0.35f;
        RefreshOverlay();
    }

    // ------------------------------------------------------ programmatic API
    // (used by remote/automated tests: place by target index + face normal)

    public bool PlaceByFace(int paletteIndex, int targetIndex, Vector3 normal, int yaw,
                            float offU = 0f, float offV = 0f)
    {
        var def = palette[paletteIndex];
        var target = placed[targetIndex];
        int axis = 0;
        float best = Mathf.Abs(normal.x);
        if (Mathf.Abs(normal.y) > best) { axis = 1; best = Mathf.Abs(normal.y); }
        if (Mathf.Abs(normal.z) > best) { axis = 2; }
        float sign = normal[axis] >= 0f ? 1f : -1f;
        var probe = new PlacedPart { def = def, yaw = yaw,
                                     wheelAxis = NeedsAxis(def) ? normal : Vector3.zero };
        Vector3 pos = target.pos;
        pos[axis] = target.pos[axis] + sign * (target.Half()[axis] + probe.Half()[axis]);
        // Socket-grid tangent offsets (u = (axis+1)%3, v = (axis+2)%3):
        // defaults keep every existing 4-arg call byte-identical.
        pos[(axis + 1) % 3] += offU;
        pos[(axis + 2) % 3] += offV;
        // The programmatic path answers to the same rotor-sweep rule the
        // pointer does, or a test could build a machine the player cannot -
        // which is the whole reason this API exists in the first place.
        probe.pos = pos;
        string swWhy = RotorSweepRefusal(probe);
        if (swWhy != null) { message = swWhy; return false; }
        AddPart(def, pos, yaw, NeedsAxis(def) ? normal : Vector3.zero, target);
        RefreshOverlay();
        return true;
    }

    /// <summary>Serialize the current build, one part per line:
    /// id|x,y,z|yaw|axisX,axisY,axisZ (build-space centers, invariant
    /// culture). The automated test pipeline's snapshot format.</summary>
    /// <summary>ROUND-3-DEV FIX 1b. The snapshot format had NO version stamp,
    /// which the round-3 critic named as part of the migration defect, and
    /// without one the disc migration would have the very flaw it repairs.
    /// A disc bolted straight to the frame is a LEGAL design - the builder
    /// says so ("or keep it as a fixed edge"). So a file saved TODAY, where
    /// the author chose a fixed edge under the new rules, must never be
    /// "repaired"; only a file saved BEFORE the rules changed can be. This
    /// line is what tells those two apart.
    /// It is written FIRST and it is inert to every reader: LoadSnapshot
    /// splits on '|', gets one field, and drops any line with fewer than 4 -
    /// so an OLD build of the game loads a NEW file unharmed too.</summary>
    public const string SNAP_STAMP = "#fmt3-disc";

    public string SnapshotString()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append(SNAP_STAMP).Append('\n');
        foreach (var p in placed)
            sb.Append(p.def.id).Append('|')
              .Append(p.pos.x.ToString("F3", inv)).Append(',')
              .Append(p.pos.y.ToString("F3", inv)).Append(',')
              .Append(p.pos.z.ToString("F3", inv)).Append('|')
              .Append(p.yaw).Append('|')
              .Append(p.wheelAxis.x.ToString("F2", inv)).Append(',')
              .Append(p.wheelAxis.y.ToString("F2", inv)).Append(',')
              .Append(p.wheelAxis.z.ToString("F2", inv)).Append('|')
              .Append(p.MatName()).Append('\n');   // v2: trailing material field
        return sb.ToString();
    }

    /// <summary>Rebuild `placed` from SnapshotString text — the scripted
    /// restore path for the automated test pipeline (survives editor domain
    /// reloads via Assets/user_build_snapshot.txt). Bypasses the ghost/socket
    /// UI but funnels through the SAME AddPart the click path uses; the
    /// connector collar re-derives from the first already-placed touching
    /// part. Build mode only (forces BackToBuild first). Returns the part
    /// count.</summary>
    /// <summary>ROUND-3-DEV FIX 1 (round-3 critic CRITICAL "the disc conversion
    /// silently disarmed owen's own saved build; no migration exists").
    ///
    /// THE DEFECT. Before 2026-07-27 a `spinner` was self-powered: CompoundRobot
    /// .Build bolted a SpinnerWeapon onto it. The disc conversion deleted that
    /// line and made a disc an UNPOWERED rotor edge that turns only when it sits
    /// past a spindle. The conversion was verified FORWARDS (a driven disc turns
    /// at 73.5 rad/s, the seam holds, power was de-duplicated) and never
    /// BACKWARDS, against builds that were already saved. qa_owen_build_SAFE.txt
    /// has a spinner and no spindle, so it went from armed to inert steel with
    /// no version stamp, no upgrade and no message. Validate() still returned
    /// OK and FIGHT was still enabled. Measured before this fix: SAFE loads with
    /// ZERO actuators and dealt limb 0/0 in 6 of 6 matches (critic M3).
    /// That is the ONE-SIDE-ONLY FIX pattern, fourth occurrence.
    ///
    /// THE REPAIR, and why the geometry is not a guess. The repair a player
    /// would make by hand is "put a spindle between the frame and the disc",
    /// and that is exactly what owen did by hand to produce SPINDLE from SAFE.
    /// So the migration is derivable, not invented: along the disc's own mount
    /// axis, seat the spindle where the disc's INNER face already is, and push
    /// the disc out by the spindle's full depth.
    ///     spindlePos = discPos - u*halfDisc + u*halfSpindle
    ///     newDiscPos = discPos + u*2*halfSpindle
    /// Run on SAFE this derives spindle (0, 0.700, 0.900) and disc
    /// (0, 0.700, 1.100) on the spindle's default Aluminum at 36 cr. owen's own
    /// hand-migrated SPINDLE file is spindle (0, 0.700, 0.900) Aluminum and
    /// disc (0, 0.700, 1.100), 750 -> 786 cr. The rule reproduces the human
    /// repair EXACTLY, which is the only evidence that would justify doing this
    /// to somebody's saved machine automatically.
    ///
    /// WHAT IT REFUSES TO DO. This mutates a saved build, so every failure mode
    /// is a REFUSAL, never a bodge: it skips the disc if the disc has no mount
    /// axis, if the spindle or the moved disc would overlap an existing part,
    /// if the spindle would not actually touch both the parent and the disc, or
    /// if the insert would break CREDIT_BUDGET. A refused disc keeps the
    /// existing "bolted straight to the frame" builder warning, which is what
    /// shipped an hour ago and is strictly better than a silent bodge.
    /// It NEVER writes the snapshot file back - the player's file on disk is
    /// untouched until they save, so this is reversible by not saving.
    ///
    /// Returns the number of discs actually migrated.</summary>
    public int MigrateUndrivenDiscs(List<string> notes)
    {
        P1PartDef spDef = null;
        foreach (var d in palette) if (d.id == "spindle") { spDef = d; break; }
        if (spDef == null) return 0;

        // Snapshot the list first: AddPart appends to `placed` while we walk it.
        var discs = new List<PlacedPart>();
        var limbs = LimbReport();
        foreach (var p in placed)
        {
            if (!p.def.id.StartsWith("spinner")) continue;
            bool driven = false;
            foreach (var li in limbs)
                if (li.blockedBy == null && li.memberParts != null && li.memberParts.Contains(p))
                { driven = true; break; }
            if (!driven) discs.Add(p);
        }

        int done = 0;
        foreach (var disc in discs)
        {
            Vector3 a = disc.wheelAxis;
            if (a.sqrMagnitude < 0.01f) continue;            // no mount axis - refuse
            a = a.normalized;
            int k = 0; float b = Mathf.Abs(a.x);
            if (Mathf.Abs(a.y) > b) { k = 1; b = Mathf.Abs(a.y); }
            if (Mathf.Abs(a.z) > b) k = 2;
            Vector3 u = Vector3.zero; u[k] = Mathf.Sign(a[k]);

            var probe = new PlacedPart { def = spDef, pos = disc.pos, yaw = disc.yaw,
                                         wheelAxis = disc.wheelAxis };
            float hs = probe.Half()[k], hd = disc.Half()[k];
            Vector3 spPos = disc.pos - u * hd + u * hs;
            Vector3 dPos  = disc.pos + u * (2f * hs);

            var spTest   = new PlacedPart { def = spDef, pos = spPos, yaw = disc.yaw,
                                            wheelAxis = disc.wheelAxis };
            var discTest = new PlacedPart { def = disc.def, pos = dPos, yaw = disc.yaw,
                                            wheelAxis = disc.wheelAxis, matName = disc.matName };

            // Refuse on any overlap with a part that is not the disc itself.
            bool clash = false;
            foreach (var q in placed)
            {
                if (q == disc) continue;
                if (Overlapping(spTest, q) || Overlapping(discTest, q)) { clash = true; break; }
            }
            if (clash) continue;
            // The spindle has to actually bridge the two, or it drives nothing.
            if (!Touching(spTest, discTest)) continue;
            PlacedPart parent = null;
            foreach (var q in placed)
                if (q != disc && Touching(spTest, q)) { parent = q; break; }
            if (parent == null) continue;
            if (CREDIT_BUDGET > 0 && BuildCost() + spDef.CostOf(spDef.EffectiveMat(null)) > CREDIT_BUDGET)
                continue;

            string dMat = disc.matName;
            int dYaw = disc.yaw; Vector3 dAxis = disc.wheelAxis; var dDef = disc.def;
            RemovePart(disc, false);   // ONE disc; a branch must not follow it
            AddPart(spDef, spPos, dYaw, dAxis, parent, null);
            AddPart(dDef, dPos, dYaw, dAxis, null, dMat);
            done++;
            if (notes != null)
                notes.Add(string.Format(
                    "{0} moved {1:F2} m out and a spindle fitted behind it",
                    dDef.label, 2f * hs, spDef.CostOf(spDef.EffectiveMat(null))));
        }
        return done;
    }

    /// <summary>Strict box overlap - the complement of Touching(), which counts
    /// a FLUSH face as contact. Used only by MigrateUndrivenDiscs, which has to
    /// distinguish "sits against" from "occupies the same space".</summary>
    static bool Overlapping(PlacedPart a, PlacedPart b)
    {
        Vector3 ha = a.Half(), hb = b.Half();
        for (int i = 0; i < 3; i++)
            if (Mathf.Abs(a.pos[i] - b.pos[i]) - (ha[i] + hb[i]) > -0.03f) return false;
        return true;
    }

    public int LoadSnapshot(string text)
    {
        // Refuse rather than throw. Loading into a half-constructed builder
        // (Start not yet run, or a domain reload that left the component alive
        // and its scene furniture destroyed) used to NRE deep inside the parse
        // loop, which is both a crash and a lie about the cause.
        if (palette == null) palette = P1PartDef.Palette();
        if (buildRoot == null)
        {
            message = "Builder is still starting up - try that again in a moment.";
            return 0;
        }
        if (mode != Mode.Build) BackToBuild();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var p in placed) if (p.go != null) Destroy(p.go);
        placed.Clear();
        byCollider.Clear();
        foulDirty = true;
        var badMats = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            var f = line.Split('|');
            if (f.Length < 4) continue;
            P1PartDef def = null;
            foreach (var d in palette) if (d.id == f[0]) { def = d; break; }
            if (def == null) continue;
            var c = f[1].Split(',');
            var pos = new Vector3(float.Parse(c[0], inv), float.Parse(c[1], inv), float.Parse(c[2], inv));
            int yaw = int.Parse(f[2], inv);
            var ax = f[3].Split(',');
            var axis = new Vector3(float.Parse(ax[0], inv), float.Parse(ax[1], inv), float.Parse(ax[2], inv));
            // v2 adds a 5th field: the part's material. v1 lines (4 fields)
            // load exactly as before, on the def's default material.
            string mat = f.Length >= 5 ? f[4].Trim() : null;
            // ROUND-1 IMPL FIX (critic MAJOR: unknown material key loads
            // silently as Aluminum). Resolve alias spellings to the real key so
            // the placement stores what the author meant, and NAME the ones
            // that cannot be resolved - a load that quietly changes what your
            // robot is made of invalidates every measurement taken afterwards,
            // which is exactly what happened to the round-1 carbon-spear test.
            if (!string.IsNullOrEmpty(mat))
            {
                string canon = MatDB.Canon(mat);
                if (canon == null) badMats.Add(mat);
                else mat = canon;
            }
            var probe = new PlacedPart { def = def, pos = pos, yaw = yaw, wheelAxis = axis };
            PlacedPart attach = null;
            foreach (var q in placed) if (Touching(probe, q)) { attach = q; break; }
            AddPart(def, pos, yaw, axis, attach, mat);
        }
        // Round-5 fix 8: the material panel used to keep describing whatever
        // was selected before the load, so a Tungsten build read "Aluminum —
        // the all-rounder". Follow the build: select its most-used material.
        if (placed.Count > 0)
        {
            string modal = null; int best = 0;
            foreach (string key in MatDB.Order)
            {
                int n = 0;
                foreach (var p in placed) if (p.MatName() == key) n++;
                if (n > best) { best = n; modal = key; }
            }
            if (modal != null) activeMat = modal;
        }
        message = "";
        // ROUND-3-DEV FIX 1. The backward half of the 2026-07-27 disc
        // conversion. A build saved before today can contain a disc that used
        // to be self-powered and now is not; repair it HERE, at the one moment
        // the harm happens, and say so in the same channel the material
        // repair already uses. Silence is what the conversion shipped with.
        var discNotes = new List<string>();
        // Only a file with no format stamp predates the disc conversion. A
        // stamped file was authored under the current rules, so a disc bolted
        // to the frame in one is a DELIBERATE fixed edge and is left alone.
        bool preConversion = text.IndexOf(SNAP_STAMP) < 0;
        int migrated = preConversion ? MigrateUndrivenDiscs(discNotes) : 0;
        if (migrated > 0)
        {
            message = string.Format(
                "\u2699 This build predates the disc change - a disc is now an UNPOWERED rotor. "
              + "Repaired {0} of them: {1}. Your saved file is untouched until you save.",
                migrated, string.Join("; ", discNotes.ToArray()));
            CompoundRobot.Log("LoadSnapshot: " + message);
        }
        if (badMats.Count > 0)
        {
            var seen = new List<string>();
            foreach (var b in badMats) if (!seen.Contains(b)) seen.Add(b);
            message += (message.Length > 0 ? "  " : "")
                    + "\u26a0 unknown material key(s) in snapshot: "
                    + string.Join(", ", seen.ToArray())
                    + " - those parts loaded on their DEFAULT material, not the one written.";
            CompoundRobot.Log("LoadSnapshot: " + message);
        }
        // ---- C1: career reconciliation. A loaded build may use parts the
        // inventory does not own (saved pre-career, or sold since). Flag the
        // shortfall - never silently duplicate; the placement gate stays shut
        // while remaining is negative.
        var lack = CareerShortfall();
        if (lack.Count > 0)
        {
            message += (message.Length > 0 ? "  " : "")
                    + "\u26a0 This build uses parts you don't own: "
                    + string.Join(", ", lack.ToArray())
                    + " \u2014 shop or sell-back before fighting.";
            CompoundRobot.Log("LoadSnapshot: " + message);
        }
        RefreshOverlay();
        return placed.Count;
    }

    // ------------------------------------------------------------------- GUI

    /// <summary>Set once when OnGUI has already refused to draw, so the reason
    /// is stated exactly one time instead of sixty times a second.</summary>
    bool warnedUnbuilt;

    /// <summary>Minimal touch HUD for TEST DRIVE (Phase 5 fix): BACK / RESET
    /// as tappable buttons plus the shear/wreck callouts. IMGUI on purpose -
    /// it must draw while the uGUI builder canvas is hidden.</summary>
    void MobileTestHud()
    {
        float s = GuiScale;   // R4 finding 3: one rule, one place
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = Screen.width / s;
        int fs = GUI.skin.button.fontSize;
        GUI.skin.button.fontSize = 16;
        bool back  = GUI.Button(new Rect(w - 96f, 10f, 86f, 40f), "BACK");
        bool reset = GUI.Button(new Rect(w - 192f, 10f, 86f, 40f), "RESET");
        GUI.skin.button.fontSize = fs;
        if (shearTimer > 0f && !string.IsNullOrEmpty(shearText))
        {
            var st = new GUIStyle(GUI.skin.label);
            st.fontSize = 20; st.fontStyle = FontStyle.Bold; st.alignment = TextAnchor.MiddleCenter;
            st.normal.textColor = new Color(1f, 0.5f, 0.3f);
            GUI.Label(new Rect(0f, 60f, w, 30f), shearText, st);
        }
        GUI.matrix = saved;
        if (back) { BackToBuild(); return; }
        if (reset) ResetTest();
    }

    // ---- C6.5: mode banners. The dev sandbox and the Drafting Table both
    // grant everything - the only thing separating them from the career, for
    // a player, is KNOWING which one they are looking at. So say it, loudly,
    // every frame, above both UI stacks (IMGUI draws over the touch canvas).
    // bannerNow is the harness seam: "dev" / "draft" / "".
    public static string bannerNow = "";
    void ModeBanner()
    {
        bool dev = !Career.active;
        bool draft = Career.active && Career.Drafting;
        bannerNow = dev ? "dev" : draft ? "draft" : "";
        if (!dev && !draft) return;
        // OWEN 2026-08-02: "The draft banner overlaps with other text."
        //
        // It did, and it always would have. This is IMGUI drawn OVER a uGUI
        // screen at a GUESSED fraction of screen height (0.075), so it knew
        // nothing about where the touch UI's bars actually end - and the bar
        // stack is variable: the message bar and the tip bar come and go. Any
        // constant here is wrong for some combination of them.
        //
        // The floating overlay is not the right instrument on a screen that
        // owns its own layout. When the touch UI is up it now prints the mode
        // in its STATUS LINE instead (MobileBuilderUI, ModeTag) - present on
        // every tab, and part of the layout, so it cannot overlap anything by
        // construction. bannerNow is still set above, because it is the state
        // seam CareerSmoke asserts on and that is independent of who draws it.
        //
        // Desktop keeps the banner: IMGUI is the whole UI there, and it has no
        // status line to put this in.
        if (MobileBuilderUI.Active) return;
        var prevM = GUI.matrix;
        var prevC = GUI.color;
        float sc = GuiScale;   // R4 finding 3: one rule, one place
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float bw = dev ? 430f : 350f;
        // Below the mobile stats bar, never behind it - R1 critic caught the
        // banner rendering tiny under the onboarding hint text.
        float by = MobileBuilderUI.Active ? Screen.height / sc * 0.075f : 4f;
        var r = new Rect((Screen.width / sc - bw) * 0.5f, by, bw, 30f);
        var bst = new GUIStyle(GUI.skin.box);
        bst.fontSize = 15; bst.fontStyle = FontStyle.Bold;
        GUI.color = dev ? new Color(1f, 0.35f, 0.3f, 0.95f) : new Color(1f, 0.8f, 0.25f, 0.95f);
        GUI.Box(r, dev ? "DEV SANDBOX \u2014 nothing here touches your career"
                       : "DRAFT \u2014 parts unlimited, can't enroll", bst);
        GUI.color = prevC;
        GUI.matrix = prevM;
    }

    void OnGUI()
    {
        // Read LAST frame's hover and clear. Doing it here rather than at the
        // end means the several early returns below cannot skip it, and only
        // re-toasting on CHANGE stops a resting cursor from pinning the message
        // bar and clobbering everything else that wants to speak.
        if (Event.current.type == EventType.Repaint)
        {
            if (gateHoverWhy == null) gateHoverShown = null;
            else if (gateHoverWhy != gateHoverShown) { message = gateHoverWhy; gateHoverShown = gateHoverWhy; }
            gateHoverWhy = null;
        }
        ModeBanner();
        if (MobileBuilderUI.Active)
        {
            // Phase 5 fix: the touch UI replaces the builder panel, but TEST
            // DRIVE still needs a HUD - without these buttons a touch player
            // has no way back (B and R are keyboard-only).
            // The touch UI owns the scouting card now - it can actually be
            // tapped. Drawing both would double the text and put an unclickable
            // BACK on top of a working one.
            if (scoutRoot != null) { if (!MobileBuilderUI.ScoutCardLive) ScoutHud(); return; }
            if (mode == Mode.Test) MobileTestHud();
            return;
        }
        // A BuilderManager that SURVIVES A DOMAIN RELOAD comes back with a null
        // palette - Unity re-creates the component but not the state Start()
        // built - and every line below dereferences it. The result was an NRE
        // per OnGUI frame, which floods the console, breaks GUILayout's
        // Begin/End pairing ("Invalid GUILayout state") and can stall the MCP
        // bridge outright. SIX separate agent handovers on this project have
        // opened with that flood, each one spending its first minutes on a
        // recovery, and one of them lost ~8 minutes of measurement to it.
        // Drawing nothing for one frame is not a fix for the reload - the
        // caller still has to rebuild the manager - but it turns an unbounded
        // error storm into a single actionable line.
        if (palette == null)
        {
            if (!warnedUnbuilt)
            {
                warnedUnbuilt = true;
                CompoundRobot.Log("BuilderManager: palette is null (survived a domain reload?) - "
                    + "not drawing. Destroy this BuilderManager and create a fresh one.");
            }
            return;
        }
        if (scoutRoot != null) { ScoutHud(); return; }   // C3: scouting overlay
        if (mode == Mode.Fight) return;  // FightManager draws the fight HUD/results
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
                GUILayout.Label(string.Format("Mass {0} kg · speed {1:F1} km/h · dmg {2:F0} / taken {3:F0}",
                    hudMass, testDrive != null ? testDrive.SpeedKmh() : 0f,
                    testRobot.damageDealt, testRobot.damageTaken));
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
        // Round-2 fix 5: Escape also handled through the IMGUI event pipeline —
        // in the editor the input backends can swallow Escape (it doubles as
        // the cursor-release key), which left the pending part armed with a
        // red ghost. IMGUI receives the KeyDown regardless of backend.
        var ev = Event.current;
        if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape && selected >= 0)
        {
            Deselect();
            ev.Use();
        }
        // ---- Phase 5: first-run tutorial strip ----------------------------
        int tut = Progression.Data.tutorialStep;
        // R4 (critic finding 4, desktop half): two onboarding systems, neither
        // aware of the other - the Phase-5 sandbox strip was still telling a
        // career player to "Press T" while MobileBuilderUI ran its own career
        // tips. Career mode owns onboarding when it is active.
        if (tut < 3 && !Career.active)
        {
            if (tut == 0 && placed.Count >= 2)
            { Progression.Data.tutorialStep = tut = 1; Progression.Save(); }
            string tmsg = tut == 0 ? "Click a part in the panel, then click a glowing socket on the robot to bolt it on."
                        : tut == 1 ? "It's alive. Press T (or TEST DRIVE) to take it for a spin — WASD drives, R resets."
                        : "Ready to fight? The LADDER button (bottom of the panel) starts your first ranked match.";
            float tw = 620f;
            GUILayout.BeginArea(new Rect(PanelPixelW + Mathf.Max(8f, (Screen.width - PanelPixelW - tw) * 0.5f), 10f, tw, 60f), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("TUTORIAL {0}/3 — {1}", tut + 1, tmsg), bodyStyle);
            if (GUILayout.Button("skip", matStyle, GUILayout.Width(46f)))
            { Progression.Data.tutorialStep = 3; Progression.Save(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        // OWEN 2026-08-03: career mode got NO onboarding on desktop at all -
        // the strip above is gated on !Career.active, and career IS the game
        // (design doc v1.4). The comment up there says "career mode owns
        // onboarding when it is active", which was true of the intent and
        // false of the code: career owned it only on the touch UI, so a
        // desktop player was handed a builder and no words whatsoever.
        if (Career.active)
        {
            int ct = CareerTipStep();
            if (ct < TIP_COUNT)
            {
                float cw = 760f;
                GUILayout.BeginArea(new Rect(PanelPixelW + Mathf.Max(8f, (Screen.width - PanelPixelW - cw) * 0.5f), 10f, cw, 60f), GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label(CareerTip(ct, ""), bodyStyle);
                if (GUILayout.Button("skip", matStyle, GUILayout.Width(46f)))
                { Career.Data.tipsOff = true; if (Career.autosave) Career.Save(); }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
        }
        // R2 (critic finding 1): the career panel was the ONLY IMGUI path in
        // this file drawing in raw back-buffer pixels. Everything from here to
        // the matching EndArea is now in scaled units, so Screen.height has to
        // be divided by the same factor or the panel runs off the bottom.
        Matrix4x4 panelSavedMatrix = GUI.matrix;
        float panelScale = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(panelScale, panelScale, 1f));
        float panelH = Screen.height / panelScale;
        GUI.Box(new Rect(0, 0, PANEL_W, panelH), "");
        GUILayout.BeginArea(new Rect(14, 14, PANEL_W - 28, panelH - 24));
        // Fix 2026-07-29 (playtest, QHD): with a part selected the panel grew past
        // the bottom of the screen and took the opponent picker and FIGHT with it
        // — fights were only startable by hotkey, always against the default.
        // Everything above the aim feedback now scrolls; the rest stays pinned.
        panelScroll = GUILayout.BeginScrollView(panelScroll, false, true,
            GUILayout.Height(Mathf.Max(200f, panelH - 24f - FIGHT_BAND_H)));

        GUILayout.Label("ROBOT BUILDER — Phase 4", headStyle);
        CREDIT_BUDGET = Progression.BudgetFor();   // Phase 4: the budget rides the profile
        // ---- Phase 4: garage — three persistent build slots on the profile,
        // plus the two scrap sinks (budget upgrades, dev unlock toggle).
        // R2 (critic finding 1): this is Progression.Data's economy and four
        // rows below it the CAREER line prints Career.Data's. In career mode the
        // player was told "scrap 0 / record 0-0" and "scrap 640 / record 3-3" in
        // the same 120 px. One economy on screen at a time.
        if (!Career.active)
            GUILayout.Label(string.Format("Garage   ·   scrap {0}   ·   record {1}-{2}",
                Progression.Data.scrap, Progression.Data.fightsWon,
                Progression.Data.fightsFought - Progression.Data.fightsWon), bodyStyle);
        else
            GUILayout.Label("Garage · build slots", bodyStyle);
        GUILayout.BeginHorizontal();
        for (int gi = 0; gi < 3; gi++)
        {
            var slot = Progression.Data.garage[gi];
            bool has = slot.snapshot != null && slot.snapshot.Length > 0;
            if (GUILayout.Button(has ? "Load " + (char)('A' + gi) : "· " + (char)('A' + gi) + " ·", matStyle) && has)
            { LoadSnapshot(slot.snapshot); message = "Garage " + (char)('A' + gi) + " loaded."; }
        }
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int gi = 0; gi < 3; gi++)
            if (GUILayout.Button("Save " + (char)('A' + gi), matStyle))
            {
                Progression.Data.garage[gi].snapshot = SnapshotString();
                Progression.Save();
                message = "Build saved to garage slot " + (char)('A' + gi) + ".";
            }
        GUILayout.EndHorizontal();
        if (Progression.Data.budgetLevel < 4
            && GUILayout.Button(string.Format("Expand build budget {0} → {1} — costs {2} scrap",
                CREDIT_BUDGET, CREDIT_BUDGET + 500, Progression.BudgetUpgradeCost()), matStyle))
            message = Progression.TryBuyBudget()
                ? "Budget expanded to " + Progression.BudgetFor() + " scrap."
                : "Not enough scrap for the budget upgrade.";
        if (GUILayout.Button(Progression.Data.devUnlockAll
                ? "DEV unlock-all: ON" : "DEV unlock-all: off", matStyle))
        { Progression.Data.devUnlockAll = !Progression.Data.devUnlockAll; Progression.Save(); }

        // ---- C3: career league board ---------------------------------------
        if (Career.active)
        {
            // R2 (critic finding 4): the doc's §3b weight budget was
            // unimplemented on the desktop path - this line printed a bare mass
            // with no cap, no ratio and no amber, so a desktop player had no way
            // to know they were over the contest limit until the entry was
            // refused. Same readout the mobile status line carries, against the
            // same targeted league.
            var tlg4 = CareerDB.Leagues[Mathf.Clamp(Career.targetLeagueIdx, 0, CareerDB.Leagues.Length - 1)];
            bool over4 = BuildMassInt > tlg4.weightCap;
            Color savedCol4 = GUI.color;
            if (over4) GUI.color = new Color(1f, 0.82f, 0.25f);
            GUILayout.Label(string.Format("CAREER \u00b7 scrap {0} \u00b7 record {1}-{2}\nBUILD {3} / {4} kg \u00b7 {5}{6}",
                Career.Data.scrap, Career.Data.fightWins,
                Mathf.Max(0, Career.Data.fights - Career.Data.fightWins),
                BuildMassInt, Mathf.RoundToInt(tlg4.weightCap), tlg4.name,
                over4 ? " \u2014 OVER" : ""), bodyStyle);
            GUI.color = savedCol4;
            // ---- C4: the stable ----
            GUILayout.BeginHorizontal();
            stableNameBuf = GUILayout.TextField(stableNameBuf ?? "", GUILayout.Width(110f));   // R2: 150 left no room for NEW ROBOT
            // Desktop half of the same gate: reads unavailable while the name
            // box is empty, but still clickable so the click explains itself.
            bool noName = string.IsNullOrEmpty(stableNameBuf) || stableNameBuf.Trim().Length == 0;
            if (GatedButton("NEW ROBOT", matStyle, noName ? NAME_GATE_HINT : null))
            { string e4 = StableCreate(stableNameBuf); if (e4 != null) { message = e4; SfxSynth.Deny(); } stableNameBuf = ""; }
            // OWEN 2026-08-03: "pop up a confirmation window when clicking
            // save to confirm overwrite vs save a new robot." Nothing open ->
            // nothing to overwrite, so that case goes straight to naming
            // rather than asking a question with only one answer.
            if (GUILayout.Button("SAVE", matStyle))
            {
                if (NothingOpen) OpenDesktopSaveDialog();
                else { deskSaveConfirm = true; deskSaveErr = ""; }
            }
            GUILayout.EndHorizontal();
            for (int ri = 0; ri < Career.Data.stable.Count; ri++)
            {
                var rob = Career.Data.stable[ri];
                GUILayout.BeginHorizontal();
                // MEDALS (2026-08-02): titles are one-per-league-campaign now,
                // so name the championship instead of printing a bare count.
                // Mirrors the mobile ROBOTS card - this project's signature bug
                // is the one-side-only fix.
                string champD = "";
                foreach (var h in rob.leagueHistory)
                    if (h.EndsWith(" champion")) champD += (champD.Length > 0 ? ", " : "") + h.Substring(0, h.Length - 9);
                // READINESS (owen, 2026-08-05). Designs no longer hold parts,
                // so any row here may or may not be buildable out of what is in
                // the box right now. Printing that on the row is the whole
                // point: without it "which robot can I field?" is answerable
                // only by loading each one and being refused - the exact
                // asymmetry the 2026-08-02 FIGHT-button fix removed. Amber
                // beats gold here; a champion you cannot bolt together is
                // still a champion you cannot enter.
                var lackD = Career.SnapshotShortfall(rob.snapshot);
                Color savedRob = GUI.color;
                if (rob.titles > 0) GUI.color = new Color(1f, 0.87f, 0.46f);
                if (lackD.Count > 0) GUI.color = new Color(1f, 0.82f, 0.25f);
                GUILayout.Label(string.Format("{0}{1} \u00b7 {2}-{3}{4}{5} \u00b7 {6}",
                    ri == Career.Data.activeRobot ? "\u25b8 " : "", rob.name, rob.wins, rob.losses,
                    rob.titles > 0 ? " \u00b7 \u2605\u00d7" + rob.titles : "",
                    champD.Length > 0 ? " \u00b7 " + champD + " champion" : "",
                    lackD.Count > 0 ? "\u26a0 needs " + Career.ShortfallText(lackD) : "\u2713 ready"),
                    descStyle);
                GUI.color = savedRob;
                if (GUILayout.Button("load", matStyle, GUILayout.Width(46f))) StableEdit(ri);
                if (GUILayout.Button("retire", matStyle, GUILayout.Width(56f)))
                {
                    if (retireArm == ri) { retireArm = -1; StableRetire(ri); }
                    else { retireArm = ri; message = "Retire " + rob.name + "? Click retire again."; }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            // The Drafting Table toggle is gone: drafting is now derived from
            // whether a design is open (Career.Drafting), so there is no
            // independent switch that could contradict it.
            if (Career.Drafting) GUILayout.Label("DRAFTING \u2014 parts unlimited, cannot enrol", descStyle);
            if (Career.Drafting && GUILayout.Button("CONVERT \u2014 " + ConvertQuote() + " scrap", matStyle))
            { string e4 = ConvertDraft(); if (e4 != null) { message = e4; SfxSynth.Deny(); } }
            GUILayout.EndHorizontal();
            // ---- blueprints (OWEN 2026-08-02) ----------------------------
            // Desktop could not even SEE a blueprint. BlueprintSave/Edit
            // existed but ONLY MobileBuilderUI called them, so IMGUI showed the
            // Drafting Table toggle and a CONVERT quote with nothing to apply
            // them to, and a design saved on the touch UI was invisible here.
            // Mobile uGUI and IMGUI are two separate paths and the
            // one-side-only fix is this project's signature bug.
            GUILayout.BeginHorizontal();
            // Same gate and same dimming as NEW ROBOT above.
            if (GatedButton("NEW DRAFT", matStyle, noName ? NAME_GATE_HINT : null, GUILayout.Width(96f)))
            { string e5 = BlueprintSave(stableNameBuf); if (e5 != null) { message = e5; SfxSynth.Deny(); } stableNameBuf = ""; }
            GUILayout.Label("blueprints \u2014 designs you do not own the parts for yet", descStyle);
            GUILayout.EndHorizontal();
            // Deletion is DEFERRED to after the loop. Removing a row mid-loop
            // changes the control count between the Layout and Repaint passes
            // of the same OnGUI, which is exactly what throws "GUILayout:
            // Mismatched LayoutGroup".
            int bpKill = -1;
            for (int bi = 0; bi < Career.Data.blueprints.Count; bi++)
            {
                var bpd = Career.Data.blueprints[bi];
                GUILayout.BeginHorizontal();
                GUILayout.Label("\u270e " + bpd.name, descStyle);
                if (GUILayout.Button("draft it", matStyle, GUILayout.Width(66f))) BlueprintEdit(bi);
                bool armedB = bpDelArm == bi;
                Color savedBp = GUI.color;
                if (armedB) GUI.color = new Color(1f, 0.55f, 0.45f);
                if (GUILayout.Button(armedB ? "confirm \u2715" : "delete", matStyle, GUILayout.Width(72f)))
                {
                    if (armedB) { bpDelArm = -1; bpKill = bi; }
                    else { bpDelArm = bi; message = "Delete blueprint " + bpd.name + "? Click delete again \u2014 this cannot be undone."; }
                }
                GUI.color = savedBp;
                GUILayout.EndHorizontal();
            }
            if (bpKill >= 0) { string e5 = BlueprintDelete(bpKill); if (e5 != null) message = e5; }
            for (int li = 0; li < CareerDB.Leagues.Length; li++)
            {
                var clg2 = CareerDB.Leagues[li];
                bool open = Career.LeagueUnlocked(li);
                GUILayout.Label(string.Format("{0}{1} \u00b7 {2} \u00b7 cap {3} kg \u00b7 {4}",
                    open ? "" : "[locked] ", clg2.name, clg2.arenaName, Mathf.RoundToInt(clg2.weightCap),
                    ArenaHazards.Summary(clg2.arenaId)), descStyle);
                if (!open) continue;
                for (int ci3 = 0; ci3 < clg2.contests.Length; ci3++)
                {
                    var cc = clg2.contests[ci3];
                    bool cdone = Career.Data.doneContests.Contains(cc.id);
                    GUILayout.BeginHorizontal();
                    // R5 (critic finding 8): SCOUT reads first here too - the
                    // free, reversible action, matching the hint that tells the
                    // player to scout before committing an entry fee.
                    if (GUILayout.Button("SCOUT", matStyle, GUILayout.Width(72f)))
                        StartScout(li, ci3);
                    // OWEN 2026-08-02: the row now answers "can I actually
                    // enter this?" BEFORE the click. Desktop IMGUI and the
                    // mobile uGUI board are two separate code paths and this
                    // project's signature bug is the one-side-only fix, so both
                    // get it, from the same CareerFightBlocker call.
                    string cTag; string cWhy = CareerFightBlocker(li, ci3, out cTag);
                    bool cBlocked = cWhy != null;
                    if (GatedButton(string.Format("{0} {1} ({2}) \u00b7 {3} scrap{4}{5}{6}",
                        cdone ? "\u2713" : "\u25b8", EnemyRoster.Find(cc.oppId).label, cc.tier,
                        cdone ? Mathf.RoundToInt(cc.purse * 0.4f) : cc.purse,
                        cdone ? " (re-entry)" : "",
                        cc.entryFee > 0 ? " \u00b7 fee " + cc.entryFee + " scrap" : "",
                        cBlocked ? "   \u2014   " + cTag : ""), matStyle, cWhy))
                        StartCareerFight(li, ci3);
                    GUILayout.EndHorizontal();
                }
            }
            // ---- MEDALS (2026-08-02, owen): the trophy case ------------------
            // The desktop twin of MobileBuilderUI's TROPHIES tab. Same content,
            // same rule that every league is listed whether or not it has been
            // won, because the gaps are the motivation. Desktop is NOT optional
            // here: mobile uGUI and IMGUI are two separate code paths and this
            // project's signature bug is the one-side-only fix.
            GUILayout.Space(6);
            Color savedTr = GUI.color;
            GUI.color = Career.Data.medals.Count > 0 ? new Color(1f, 0.87f, 0.46f) : Color.white;
            GUILayout.Label(string.Format("TROPHIES \u00b7 {0} of {1} league campaigns won",
                Career.Data.medals.Count, CareerDB.Leagues.Length), bodyStyle);
            GUI.color = savedTr;
            if (Career.Data.medals.Count == 0)
                GUILayout.Label("   No medals yet \u2014 win EVERY contest in a league and its champion medal lands here.", descStyle);
            for (int mi = 0; mi < CareerDB.Leagues.Length; mi++)
            {
                var mlg = CareerDB.Leagues[mi];
                var med = Career.MedalFor(mi);
                bool mopen = Career.LeagueUnlocked(mi);
                int mdone = 0;
                foreach (var mc in mlg.contests) if (Career.Data.doneContests.Contains(mc.id)) mdone++;
                Color savedM = GUI.color;
                if (med != null) GUI.color = new Color(1f, 0.87f, 0.46f);
                else if (!mopen) GUI.color = new Color(0.62f, 0.66f, 0.74f);
                string mline = med != null
                    ? string.Format("\u2605 {0} CHAMPION \u00b7 {1} \u00b7 {2} {3}-{4} \u00b7 {5} contest{6} swept \u00b7 {7}",
                        med.leagueName.ToUpper(), med.arenaName, med.robot, med.wins, med.losses,
                        med.contests, med.contests == 1 ? "" : "s", med.when)
                    : !mopen
                    ? string.Format("\u25cb {0} \u2014 LOCKED \u00b7 {1} \u00b7 win the {2} first",
                        mlg.name.ToUpper(), mlg.arenaName, CareerDB.Leagues[mi - 1].name)
                    : string.Format("\u25cb {0} \u2014 NOT YET WON \u00b7 {1} \u00b7 {2} of {3} contests beaten \u00b7 sweep them all for the medal",
                        mlg.name.ToUpper(), mlg.arenaName, mdone, mlg.contests.Length);
                GUILayout.Label(mline, descStyle);
                GUI.color = savedM;
            }
            GUILayout.Space(6);
        }
        // ---- P4c: build-constraint challenges (§9) -------------------------
        GUILayout.Label("Challenges — one-time purses for constrained builds:", bodyStyle);
        for (int ci = 0; ci < Progression.Challenges.Length; ci++)
        {
            var ch = Progression.Challenges[ci];
            bool done = Progression.ChallengeDone(ch.id);
            string blocked2 = done ? null : ChallengeBlocker(ch);
            if (GUILayout.Button((done ? "✓ " : blocked2 != null ? "✗ " : "▸ ")
                + ch.label + " — " + ch.reward + " scrap" + (done ? " (complete)" : ""), matStyle) && !done)
                StartChallenge(ci);
            GUILayout.Label("   " + ch.desc + (blocked2 != null && !done ? "   [" + blocked2 + "]" : ""), descStyle);
        }
        GUILayout.Space(6);
        GUILayout.Label("Click a part, then click a socket (gray dot) on the robot:", bodyStyle);
        GUILayout.Space(2);
        // ---- Phase 3: material picker (§4.3). The SAME part in a different
        // material is a different robot - mass, cost, durability and joint
        // strength all move together, so the palette masses below update live.
        var am = MatDB.Get(activeMat);
        GUILayout.Label("Material — new parts, and middle-click to repaint one:", bodyStyle);
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int k = 0; k < 3; k++)
            {
                string key = MatDB.Order[row * 3 + k];
                var md = MatDB.Get(key);
                string sel = activeMat == key ? "• " : "";
                // Phase 4: the premium three are gated on the profile (§10).
                // A locked chip explains itself and offers the early-buy.
                bool unlocked = Progression.MatUnlocked(key);
                if (GUILayout.Button(unlocked ? sel + md.name : "[LOCKED] " + md.name, matStyle))
                {
                    if (unlocked) activeMat = key;
                    else if (Progression.TryBuyMat(key))
                    { activeMat = key; message = md.name + " unlocked with scrap."; }
                    else message = Progression.LockHint(key);
                }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(string.Format("{0} — {1}   ·   density {2:F2}   strength ×{3:F1}   cost ×{4:F1}/kg",
                        am.name, am.character, am.density_g_cm3, am.strengthRel, am.costPerKg), descStyle);
        if (GUILayout.Button("Repaint whole build in " + am.name, btnStyle)) SetAllMaterials(activeMat);
        // A panel button, not a hotkey: Phase0Input has three backend variants
        // and no spare bound key, and a toggle whose state you cannot see is
        // worse than no toggle at all.
        if (GUILayout.Button(matView ? "◉ Material view ON — showing material colours"
                                     : "○ Material view OFF — showing real finishes", btnStyle))
            SetMatView(!matView);

        GUILayout.Space(8);
        for (int i = 1; i < palette.Length; i++)  // 0 = core, auto-placed
        {
            var d = palette[i];
            string tag = selected == i ? "▶ " : "";
            // Mass shown in the ACTIVE material, so switching to steel visibly
            // triples the beam before you place it.
            // Cost is deliberately NOT on this row. Nothing spends it yet, and
            // Aluminium's costPerKg is exactly 1.0 - so on the default material
            // the cost printed identical to the mass, and an unlabelled repeat
            // of the number next to it reads as a bug rather than a stat. It is
            // still on the hover readout ("cost 325") and the panel total, both
            // of which say the word. Put it back here when there is a budget to
            // spend it against, with a label.
            // ROUND-1-IMPL (critic CRITICAL 1): cost is back on the row, with
            // the label the comment above asked for, now that there IS a budget
            // to spend it against. The old objection - that on Aluminium
            // (costPerKg exactly 1.0) an unlabelled cost printed identical to
            // the mass and read as a bug - is answered by the "cr" unit and by
            // the budget line at the foot of the panel giving it somewhere to
            // go. Choosing Tungsten now visibly costs 8x the same beam.
            // C1: stock badge - owned minus used in the active material. -1 =
            // unlimited (career off / dev free-build); 0 dims the row but keeps
            // it clickable so the amber message can still explain itself.
            int stock = CareerRemaining(i);
            Color rowCol = GUI.color;
            if (stock == 0) GUI.color = new Color(1f, 1f, 1f, 0.45f);
            bool rowClick = GUILayout.Button(string.Format("{0}{1}  ·  {2} kg  ·  {3} cr{4}", tag, d.label,
                                 Mathf.RoundToInt(d.MassOf(activeMat)),
                                 d.CostOf(activeMat),
                                 stock < 0 ? "" : "  ·  " + Mathf.Max(0, stock) + " free"), btnStyle);
            GUI.color = rowCol;
            if (rowClick)
            {
                bool off = selected == i;
                selected = off ? -1 : i;
                if (off) Deselect();   // round-2 fix 7: toggle-off kills the ghost object too
            }
            // A PINNED part's material is a permanent property of the part, so
            // it is stated permanently and quietly. The first version put a
            // bracketed tag on the button, which appeared and vanished as the
            // picker changed — drawing the eye to the one row the picker does
            // not affect, and reading identically to a RESTRICTED part that had
            // merely fallen back. The two cases are now visually distinct.
            // OWEN 2026-07-29: "Why does the menu show 'always Aluminum' for
            // Gyro stabilizer and Wheel, but 'always Rubber' for weapons?"
            // Because IMGUI stacks vertically and this label is emitted AFTER
            // its own button, so it landed in the gap between its row and the
            // NEXT row and the eye read it downward: battery's pin read as the
            // gyro's, the gyro's as the wheel's, and the wheel's "always
            // Rubber" as the first weapon's. Three pinned parts (battery,
            // gyro, wheel) sitting consecutively in the palette made the whole
            // block look shifted by one. Moving the label above the button
            // just flips the ambiguity, and folding it into the button text
            // overflows PANEL_W at fontSize 15 - so the label now NAMES the
            // part it describes and a space below closes it off, which is
            // unambiguous no matter which neighbour the eye reaches for.
            if (!d.materialChoice)
            {
                GUILayout.Label("\u21b3 " + d.label + ": always "
                                + MatDB.Get(d.matName).name, descStyle);
                GUILayout.Space(4);
            }
            // Only the selected part explains itself - 12 parts of prose does
            // not fit the panel any more. A restricted part names the set it
            // accepts rather than showing a fallback the player never chose.
            if (selected == i)
            {
                GUILayout.Label(d.desc, descStyle);
                if (d.materialChoice && d.allowedMats != null)
                    GUILayout.Label("accepts: " + string.Join(" / ",
                        System.Array.ConvertAll(d.allowedMats, k => MatDB.Get(k).name)), descStyle);
                // ---- C2 SHOP: the material CATALOG for this part ----------
                // owen 2026-08-01: "how to shop parts with different
                // materials?" The old block priced exactly ONE material - the
                // global chip on the BUILD row above - so buying a titanium
                // beam meant leaving the shop, changing a mode, and coming
                // back, and nothing on screen said so. Design doc 7 asks for
                // ONE catalog: every part x every legal material, priced by the
                // physics data, compared side by side at the counter. Section
                // 3b is why it matters - weight caps make strength per kilogram
                // the whole question, and that comparison was invisible at the
                // point of purchase.
                if (Career.active)
                {
                    GUILayout.Label("SHOP \u00b7 scrap " + Career.Data.scrap
                        + " \u00b7 sell-back 50% \u00b7 to change a material, SELL and BUY", descStyle);
                    bool pinnedMat = !d.materialChoice;
                    foreach (var mk in PartLegalMats(i))
                    {
                        int mprice = CareerDB.PartPrice(d.id, mk);
                        int mown = Career.CountOf(d.id, mk);
                        if (GUILayout.Button(string.Format("BUY {0} cr  \u00b7  {1}{2}  \u00b7  {3} kg  \u00b7  own {4}",
                                mprice, MatDB.Get(mk).name, pinnedMat ? " (fixed)" : "",
                                Mathf.RoundToInt(d.MassOf(mk)), mown), matStyle))
                        {
                            desktopArmSell = -1;
                            if (Career.TryBuy(d.id, mk)) message = MatDB.Get(mk).name + " " + d.label + " bought \u2014 " + Career.CountOf(d.id, mk) + " owned.";
                            else { message = Career.shopMsg; SfxSynth.Deny(); }
                        }
                        if (mown > 0)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Space(18f);
                            if (mown > 0 && GUILayout.Button("SELL " + CareerDB.SellPrice(d.id, mk) + " scrap", matStyle))
                            {
                                // Guard rail: every owned unit of THIS material is
                                // bolted to the current build - a second click.
                                bool inUse = CareerRemainingMat(i, mk) == 0;
                                if (inUse && (desktopArmSell != i || desktopArmSellMat != mk))
                                {
                                    desktopArmSell = i; desktopArmSellMat = mk;
                                    message = "Every " + MatDB.Get(mk).name + " " + d.label + " is in use by this build \u2014 click SELL again to sell anyway.";
                                    SfxSynth.Deny();
                                }
                                else
                                {
                                    desktopArmSell = -1; desktopArmSellMat = "";
                                    if (Career.TrySell(d.id, mk)) message = MatDB.Get(mk).name + " " + d.label + " sold.";
                                    else { message = Career.shopMsg; SfxSynth.Deny(); }
                                }
                            }
                            GUILayout.EndHorizontal();
                        }
                    }
                }
            }
        }

        GUILayout.Space(6);
        // Hover readout. The SAME numbers the fight will use, so what you read
        // here is what the physics does - seam is this material's share of the
        // joint threshold, before the socket multiplier.
        if (hoverPart != null)
        {
            var hm = hoverPart.Mat();
            GUILayout.Label(string.Format("▸ {0}  ·  {1}  ·  {2} kg  ·  cost {3}  ·  seam {4}",
                hoverPart.def.label, hm.name, Mathf.RoundToInt(hoverPart.Mass()),
                hoverPart.Cost(), Mathf.RoundToInt(hm.strengthRel * CompoundRobot.BREAK_K)), bodyStyle);
            // The cascade says its price BEFORE you pay it, not after.
            if (hoverPart == placed[0])
                GUILayout.Label("right-click: the core cannot be removed", descStyle);
            else if (doomCount > 0)
                GUILayout.Label(string.Format(
                    "\u2716 right-click removes this + {0} more (marked red)", doomCount), warnStyle);
            else
                GUILayout.Label("right-click removes this part only", descStyle);
        }
        else
            GUILayout.Label("▸ point at a part to read its material", descStyle);

        GUILayout.Space(8);
        // Panel mass = sum of the SAME rounded per-part values the palette
        // buttons show (critic fix: float-sum-then-round drifted by a few kg
        // from the sum of the displayed labels).
        int massInt = 0; int cost = 0;
        foreach (var p in placed) { massInt += Mathf.RoundToInt(p.Mass()); cost += p.Cost(); }
        // ROUND-1-IMPL FIX (critic CRITICAL 1): the total is now a SPEND
        // against a limit, and it says so. A number with no limit next to it is
        // decoration; the whole reason Tungsten read as strictly dominant is
        // that nothing on this panel ever pushed back on the price.
        Color oldC = GUI.color;
        bool hasBudget = BuilderManager.CREDIT_BUDGET > 0;   // Phase 5: unlimited when <= 0
        GUI.color = hasBudget && cost > BuilderManager.CREDIT_BUDGET ? new Color(1f, 0.42f, 0.30f)
                  : hasBudget && cost > BuilderManager.CREDIT_BUDGET * 0.85f ? new Color(1f, 0.82f, 0.25f)
                  : Color.white;
        GUILayout.Label(hasBudget
            ? string.Format("Parts: {0}   Mass: {1} kg   Credits: {2} / {3}{4}",
                placed.Count, massInt, cost, BuilderManager.CREDIT_BUDGET,
                cost > BuilderManager.CREDIT_BUDGET ? "   ✗ OVER BUDGET" : "")
            : string.Format("Parts: {0}   Mass: {1} kg", placed.Count, massInt), bodyStyle);
        GUI.color = oldC;
        // ROUND-2-DEV (critic CRITICAL 1, "a dataset must announce its own
        // regime"). The build number is on the screen the player and the next
        // QA round both look at, so a screenshot is self-dating. Same value
        // that heads every sweep file - see Actuator.SourceRegime().
        GUILayout.Label("build " + Actuator.SourceRegime(), bodyStyle);
        // ROUND-1-IMPL (critic CRITICAL 3): the roster already enforces that
        // every authored bot mounts its power parts on at least two independent
        // seams (EnemyRoster.AuditPowerMounting) - the player was never told
        // this rule existed, let alone that his own pack is the one part whose
        // loss ends the match. Same rule, stated, on his side of the screen.
        for (int pi = 0; pi < placed.Count; pi++)
        {
            if (!CompoundRobot.IsVital(placed[pi].def.id)) continue;
            int seams = 0;
            for (int pj = 0; pj < placed.Count; pj++)
                if (pj != pi && SharedSockets(placed[pi], placed[pj]) > 0) seams++;
            GUI.color = seams >= 2 ? new Color(0.62f, 0.72f, 0.62f) : new Color(1f, 0.55f, 0.2f);
            GUILayout.Label(seams >= 2
                ? string.Format("▸ battery bolted on {0} seams — losing one will not cost you the pack", seams)
                : string.Format("▸ battery bolted on {0} seam — the pack is the one part whose loss ends the match", seams),
                descStyle);
            GUI.color = oldC;
        }
        // Material census, so a mixed build is legible at a glance.
        // Count what the parts ACTUALLY are, not what the picker offers. Walking
        // MatDB.Order silently dropped every pinned-only material — a build with
        // four Rubber wheels read as 14 parts out of 18. Order still decides the
        // display sequence; anything outside it is appended in the order found.
        var census = new System.Text.StringBuilder();
        var seen = new List<string>();
        foreach (string key in MatDB.Order)
            foreach (var p in placed) if (p.MatName() == key) { seen.Add(key); break; }
        foreach (var p in placed) if (!seen.Contains(p.MatName())) seen.Add(p.MatName());
        foreach (string key in seen)
        {
            int n = 0;
            foreach (var p in placed) if (p.MatName() == key) n++;
            if (n > 0) { if (census.Length > 0) census.Append("  ·  "); census.Append(MatDB.Get(key).name).Append(" ×").Append(n); }
        }
        if (matView)
        {
            // Legend, in the census's own order. Each swatch is EXACTLY the
            // colour that material is wearing in the viewport right now.
            foreach (string key in seen)
            {
                int n = 0;
                foreach (var p in placed) if (p.MatName() == key) n++;
                if (n == 0) continue;
                GUILayout.BeginHorizontal();
                Rect sw = GUILayoutUtility.GetRect(13f, 13f, GUILayout.Width(13f), GUILayout.Height(13f));
                Color oc = GUI.color;
                GUI.color = MatDB.Get(key).auditColor;
                GUI.DrawTexture(sw, SwatchTex());
                GUI.color = oc;
                GUILayout.Label(string.Format("{0} ×{1}", MatDB.Get(key).name, n), descStyle);
                GUILayout.EndHorizontal();
            }
        }
        else if (census.Length > 0) GUILayout.Label(census.ToString(), descStyle);
        int gyroCount = 0;
        foreach (var p in placed) if (p.def.id == "gyro") gyroCount++;
        // Round-5 fix 3: a gyro-less build is no longer dead on a flip, so the
        // line states the actual trade (recovery SPEED) instead of threatening.
        GUILayout.Label(gyroCount > 0
            ? string.Format("Self-right: {0} gyro{1} fitted — back on its wheels in under a second", gyroCount, gyroCount == 1 ? "" : "s")
            : "Self-right: emergency struts only — a flip costs you a few seconds", bodyStyle);

        // ---- Phase 4: what each actuator will actually DO -------------------
        // The whole point of the base-component kit is that the weapon is the
        // assembly, so the build screen has to quote the assembly, not the part.
        var limbs = LimbReport();
        foreach (var li in limbs)
        {
            // ROUND-1-DEV. A spindle is EXEMPT from the arc guard, so its
            // arcFrac is always 1.0 and the chain below can structurally never
            // say this. Reported separately and first, because unlike a pivot
            // that merely stops early, a rotor that reaches through the deck is
            // a build error the arena will not correct for you - it just grinds.
            if (li.blockedBy == null && li.rotorDip > 0.005f)
                GUILayout.Label(string.Format(
                    "\u26a0 {0}'s rotor reaches {1:F0} cm below the deck \u2014 a spindle is exempt from the arena's ground guard, so it grinds on the floor instead of stopping. Mount it higher, or fit a smaller disc.",
                    li.act.def.label, li.rotorDip * 100f), warnStyle);
            if (li.blockedBy != null)
            {
                GUILayout.Label(string.Format("⚠ {0} can't move — its arm is bolted to the frame at {1}",
                    li.act.def.label, li.blockedBy), warnStyle);
                GUILayout.Label("   An actuator has to be the ONLY thing joining its arm to the body. "
                              + "Move the arm clear, or remount the actuator where nothing else touches it.", descStyle);
            }
            else if (li.parts == 0)
                GUILayout.Label(string.Format("⚠ {0} drives nothing — bolt an arm to its free face",
                    li.act.def.label), warnStyle);
            // ROUND-UP2 FIX D: an arm that is going to hit the floor gets the
            // same kind of warning a blocked seam gets, and for the same
            // reason - it is invisible on the build screen and decisive in the
            // arena. 0.9 rather than 1.0 because the last few degrees of a
            // clean overhead arc routinely graze the clearance band and
            // flagging those would train the player to ignore the line.
            else if (li.arcFrac < 0.9f)
                GUILayout.Label(li.act.def.id == "ram"
                    ? string.Format("⚠ {0} hits the floor after {1:F2} m of its {2:F2} m stroke ({3:F0}%) — mount it higher",
                        li.act.def.label, li.arcFree, li.arcLimit, 100f * li.arcFrac)
                    : string.Format("⚠ {0}'s arm hits the floor after {1:F0}° of its {2:F0}° swing ({3:F0}%) — mount it higher, or on the roof",
                        li.act.def.label, li.arcFree * Mathf.Rad2Deg,
                        li.arcLimit * Mathf.Rad2Deg, 100f * li.arcFrac), warnStyle);
            else
            {
                // Round-3-critic CRITICAL 2. The build screen used to quote
                // "kJ to wind up" without ever saying how LONG that takes or
                // how fast the edge actually travels, which is precisely the
                // tradeoff the player is being asked to make. Both are now on
                // the line, and both move when you fit an engine or change the
                // edge material.
                int eng2 = 0;
                foreach (var pe in placed) if (pe.def.id.StartsWith("engine")) eng2++;
                float mkw = Actuator.MotorKW(eng2);
                var k2 = Actuator.KindOfId(li.act.def.id);
                GUILayout.Label(string.Format(
                    "▸ {0}: {1} part{2} · {3:F0} J a hit · {4:F1} kJ to wind up · reach {5:F2} m · tip {6:F1} m/s · motor {7:F1} kW · {8:F2} s a swing",
                    li.act.def.label, li.parts, li.parts == 1 ? "" : "s",
                    li.energyJ * Actuator.DRAIN_FRAC, li.kjPerSwing, li.tipRadius,
                    li.rateMax * (k2 == ActuatorKind.Ram ? 1f : li.tipRadius), mkw,
                    Actuator.CycleSeconds(k2, li.inertia, li.tipRadius, mkw, li.tipSpeedCap)),
                    bodyStyle);
                // Fix 2026-07-29 (playtest): adding a Blade bar DROPPED "J a hit"
                // (3747 -> 2581) with no explanation. It is the tip-speed cap: the
                // tip may not exceed the tunneling-safe speed, so a longer arm
                // turns slower and stores less energy. True, but it must be said,
                // or the number reads as a bug.
                if (k2 != ActuatorKind.Ram
                    && li.rateMax * li.tipRadius >= li.tipSpeedCap - 0.05f)
                    GUILayout.Label("   tip-speed capped: a longer arm swings slower and stores less energy — shorten the arm or fit more engines", descStyle);
                // Fix 2026-07-29 (playtest): a pivot arm that RESTS over the nose
                // sweeps up and BACKWARD (the arc sign avoids the floor), so the
                // power stroke lands behind the bot. Say so while it can be fixed.
                if (k2 == ActuatorKind.Pivot && li.memberParts != null && li.memberParts.Count > 0)
                {
                    Vector3 rest = Vector3.zero;
                    foreach (var mp in li.memberParts) rest += mp.pos - li.act.pos;
                    rest /= li.memberParts.Count;
                    if (rest.z > 0.15f)
                        GUILayout.Label("⚠ arm rests over the NOSE — this swing fires up and backward. For a forward hammer, bolt the arm to the pivot's rear face so it rests behind.", warnStyle);
                }
            }
        }

        // ---- §6.2 power budget, BEFORE you commit to the fight -------------
        // §5.4 asks the build screen to show what the physics is about to do.
        // Endurance is the honest number: capacity divided by what this exact
        // machine draws driving flat out with its discs at a steady grind.
        float capKJ = 0f, peakKW = 0f;
        foreach (var p in placed) { capKJ += p.def.energyKJ; peakKW += p.def.powerKW; }
        float driveKW = PowerPlant.DriveKW(massInt, 1f);
        // Round-3-critic CRITICAL 2: wind-up rate, and therefore the weapon's
        // share of the power budget, is now set by how many engines are fitted.
        int liveEngines = 0;
        foreach (var pe in placed) if (pe.def.id.StartsWith("engine")) liveEngines++;
        float motorKW = Actuator.MotorKW(liveEngines);
        float spinKW = 0f, spinUpKJ = 0f;
        // Phase 4: a swinging weapon is a real load, so it belongs in the
        // endurance number rather than surprising the player mid-match.
        foreach (var li in limbs)
        {
            if (li.blockedBy != null || li.parts == 0) continue;
            var k = Actuator.KindOfId(li.act.def.id);
            spinUpKJ += li.kjPerSwing;
            spinKW += li.kjPerSwing / Actuator.CycleSeconds(k, li.inertia, li.tipRadius,
                                                            motorKW, li.tipSpeedCap);
        }
        // DISC CONVERSION (2026-07-27). This used to be a SECOND power block
        // that charged every spinner a self-powered spin-up + drain + windage
        // draw out of SpinnerWeapon's constants. A disc has no motor now: it is
        // a limb member like a blade, so the loop above already prices it out
        // of the SPINDLE's inertia and the same MotorKW the arena uses. Leaving
        // this here would have double-billed every disc-on-a-spindle and still
        // billed a disc bolted to the frame, which draws nothing at all - the
        // exact one-side-only-fix pattern this project keeps repeating.
        float demandKW = driveKW + spinKW;
        float endur = demandKW > 0.01f ? capKJ / demandKW : 999f;
        GUILayout.Label(string.Format("Power: {0:F0} kJ · {1:F0} kW draw ceiling", capKJ, peakKW), bodyStyle);
        GUILayout.Label(string.Format("   drive {0:F1} kW · weapons {1:F1} kW steady ({2:F0} kJ to wind up)",
                        driveKW, spinKW, spinUpKJ), descStyle);
        // Round-6 fix 4c. A power part bolted on a SINGLE seam is a hidden
        // instant loss: it is the densest small part on the machine, its own
        // face is below one socket pitch so that seam can never be more than
        // x1, and when it shears it takes the entire energy budget with it.
        // Measured in the round-5 sweep: the shipped Mauler lost its battery
        // this way in 17 of 17 matches, and the player had no way at all to see
        // it coming from the build screen. Now he does, before he commits.
        // DISC CONVERSION (2026-07-27): a disc bolted straight to the frame is
        // legal and not useless - it is a hardened fixed edge, same as a spike -
        // but it is NOT what someone who dragged out a part labelled "Spinner
        // blade" is expecting, and the disappointment is silent in the arena.
        // Say it on the build screen, where it can still be acted on.
        // ROUND-1-CRITIC MODERATE ("the undriven-disc warning fires on the
        // highest per-bite disc build in the dataset"). This used to require a
        // SPINDLE, so a disc driven by a PIVOT - which swings it through an arc
        // and bites perfectly well; the critic measured 62.4 per bite off one,
        // the best per-bite figure in its whole sweep - was told it was dead
        // weight. Textbook one-side-only: the rule was written against the
        // spindle and never asked what the OTHER actuators do with a disc.
        //
        // The real condition is "no actuator drives this disc at all", i.e. it
        // is bolted to the frame. A blocked limb still counts as undriven,
        // because a limb welded to the frame genuinely cannot move - but that
        // case already has its own louder warning above naming the seam, so it
        // is not double-reported here.
        int undrivenDiscs = 0;
        foreach (var p in placed)
        {
            if (!p.def.id.StartsWith("spinner")) continue;
            bool driven = false;
            foreach (var li in limbs)
                if (li.blockedBy == null && li.memberParts != null
                    && li.memberParts.Contains(p))
                { driven = true; break; }
            if (!driven) undrivenDiscs++;
        }
        if (undrivenDiscs > 0)
            GUILayout.Label(string.Format(
                "\u26a0 {0} disc(s) bolted straight to the frame \u2014 a disc is an unpowered rotor. Put it past a spindle to spin it, or a pivot to swing it, or keep it as a fixed edge.",
                undrivenDiscs), warnStyle);

        int lonePower = 0;
        foreach (var p in placed)
        {
            if (p.def.category != P1Category.Power) continue;
            int seams = 0;
            foreach (var q in placed) if (q != p && SharedSockets(p, q) > 0) seams++;
            if (seams < 2) lonePower++;
        }
        if (lonePower > 0)
            GUILayout.Label(string.Format(
                "⚠ {0} power part(s) held by ONE seam — if it shears you lose that energy instantly. Seat it where it abuts two or three parts.",
                lonePower), warnStyle);
        if (capKJ <= 0f)
            GUILayout.Label("⚠ No stored energy — fit a battery or an engine", warnStyle);
        else if (peakKW < demandKW - 0.05f)
            GUILayout.Label(string.Format("⚠ Underpowered — wants {0:F0} kW, has {1:F0}. It will drive slow and the disc won't reach speed. Add an engine.",
                            demandKW, peakKW), warnStyle);
        else if (endur < 90f)
            GUILayout.Label(string.Format("⚠ Runs flat in {0:F0} s of hard use — the match is 90 s. Add a battery or a lighter disc.", endur), warnStyle);
        else
            GUILayout.Label(string.Format("✓ Endurance {0}s of hard use (match is 90 s)",
                            endur > 900f ? "900+" : endur.ToString("F0")), bodyStyle);
        // Round-5 fix 7: ABS's only warning used to be the palette's "strength
        // x0.2"; a new player picking the cheapest option had no idea it meant
        // the opening ram would core him.
        int absCount = 0;
        foreach (var p in placed) if (p.MatName() == "ABS") absCount++;
        if (absCount > 0 && absCount >= placed.Count / 2)
            GUILayout.Label("⚠ Mostly ABS — its seams shear first; armour the core or upgrade the frame", warnStyle);
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
        GUILayout.EndScrollView();
        // Aim feedback and the fight controls below stay PINNED (Fix 2026-07-29):
        // the red-ghost reason was being pushed below the screen edge by the very
        // overflow it was meant to explain.
        if (ghost != null && ghost.activeSelf && !ghostValid && ghostReason.Length > 0)
            GUILayout.Label("✗ " + ghostReason, warnStyle);
        // ACTUATOR DRIVE AXIS: R used to be a dead key on these three parts, and
        // the old rule - the axis follows the mount face - was discoverable only
        // by reading the source. Both are now on screen while the part is held.
        if (selected >= 0 && palette[selected].actuator)
        {
            var axProbe = new PlacedPart { def = palette[selected], yaw = ghostYaw,
                                           wheelAxis = ghostNormal };
            GUILayout.Label(string.Format(
                "↻ R = drive axis: {0}   (a spindle spins in the plane ACROSS this axis; a pivot swings AROUND it)",
                axProbe.DriveAxisLabel()), bodyStyle);
        }
        // Socket-grid feedback: how many socket pairs the hovered placement
        // would bolt through (more mated sockets = stronger joint).
        if (ghost != null && ghost.activeSelf && ghostValid)
            // ghostMated is now always >= 1: SnapAlong only ever returns socket
            // alignments. The zero branch is kept as an assertion, not a mode.
            GUILayout.Label(ghostMated > 0
                ? string.Format("✓ {0} socket{1} mated — joint strength ×{0}",
                    ghostMated, ghostMated == 1 ? "" : "s")
                : "⚠ internal: no socket mated — this should be unreachable", bodyStyle);
        // Suppress the duplicate when a refused T just re-states the standing
        // validation error already shown above (critic fix).
        if (message.Length > 0 && message != err) GUILayout.Label("⚠ " + message, bodyStyle);

        GUILayout.Space(6);
        // ---- §8 opponent + difficulty --------------------------------------
        // The tier changes reaction time and commitment, nothing else. Picking
        // Champion does not make the bot tougher, it makes it quicker.
        var oppEntry = EnemyRoster.Find(opponentId);
        GUILayout.Label("Opponent:", bodyStyle);
        for (int r = 0; r < 2; r++)
        {
            GUILayout.BeginHorizontal();
            for (int k = 0; k < 3; k++)
            {
                int oi = r * 3 + k;
                if (oi >= EnemyRoster.All.Length) { GUILayout.Label("", matStyle); continue; }
                var en = EnemyRoster.All[oi];
                string sel = opponentId == en.id ? "• " : "";
                if (GUILayout.Button(sel + en.label, matStyle))
                { opponentId = en.id; opponentTier = en.tier; }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(oppEntry.blurb, descStyle);
        GUILayout.BeginHorizontal();
        for (int t = 0; t < 3; t++)
        {
            var tv = (AiTier)t;
            string sel = opponentTier == tv ? "• " : "";
            if (GUILayout.Button(sel + tv.ToString(), matStyle)) opponentTier = tv;
        }
        GUILayout.EndHorizontal();
        // ROUND-2-DEV FIX 1, builder side: the tier gained a knob that is not
        // speed - how much the opponent respects your weapon - and the panel
        // has to say so, or the player is choosing a difficulty whose main
        // effect is invisible.
        float wr = EnemyRoster.WeaponRespect(opponentTier);
        GUILayout.Label(string.Format(
            "reacts every {0:F2} s · opens at {1:F0}% throttle · {2} — same physics, same parts",
            EnemyRoster.DecisionInterval(opponentTier),
            EnemyRoster.OpeningThrottle(opponentTier) * 100f,
            wr <= 0.01f ? "drives straight at you, weapon and all"
            : wr >= 0.99f ? "circles your weapon and attacks from behind it"
            : "wary of your weapon, but still closes"), descStyle);

        GUILayout.Space(4);
        // ---- Phase 4: the challenge ladder (§9) — the structured path. The
        // opponent picker above stays as free exhibition.
        var p4rung = Progression.CurrentRung();
        if (p4rung != null)
        {
            if (GUILayout.Button(string.Format(
                "LADDER {0}/{1} — {2} ({3}) · win {4} scrap{5}",
                Progression.Data.rung + 1, Progression.Ladder.Length, p4rung.label, p4rung.tier,
                p4rung.reward,
                p4rung.unlockMat != null ? " + " + MatDB.Get(p4rung.unlockMat).name : ""), btnStyle))
                StartLadderFight();
            if (p4rung.arenaHalf < 6.9f)
                GUILayout.Label(string.Format("   this rung fights in a tighter {0:F0} m box", p4rung.arenaHalf * 2f), descStyle);
        }
        else if (GUILayout.Button(string.Format(
                     "LADDER COMPLETE — defend the title vs {0} ({1} purse)",
                     Progression.Ladder[Progression.Ladder.Length - 1].label,
                     Progression.Ladder[Progression.Ladder.Length - 1].reward), btnStyle))
            StartLadderFight();
        if (GUILayout.Button("TEST DRIVE  (T)", btnStyle)) StartTest();
        if (GUILayout.Button("EXHIBITION FIGHT  (F)", btnStyle))
        { Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1; StartFight(); }

        GUILayout.Space(10);
        GUILayout.Label("Aim at a face — it lights up with its sockets.\nParts snap to sockets only. Green dots = the\nsockets that will mate; more mated = stronger\njoint.\nR rotate part (beams/plates stand up too)\nright-click removes a part AND everything it\ncarries — red markers show what goes · Z undo\nQ/E orbit · scroll zoom\nGreen arrow = FRONT (W drives that way)\nRed dot = center of mass\nBlue rect = wheel support\n(dot outside rect → it tips)", descStyle);
        GUILayout.EndArea();
        GUI.matrix = panelSavedMatrix;   // R2: restore before anything else draws
        // LAST. IMGUI paints in call order, so a modal drawn any earlier ends
        // up underneath the panel it is supposed to be blocking.
        SaveDialogGUI();
    }

    /// <summary>OWEN 2026-08-03: "whenever a button is disabled, it should show
    /// hint to user on why it is disabled when hovering or being clicked."
    ///
    /// IMGUI has no hover events, so this is the desktop half: draw the button
    /// dim when `why` is non-null, catch the pointer resting on it during the
    /// Repaint pass, and refuse the click with the reason instead of running
    /// the action. The caller writes the condition ONCE and gets all three.
    ///
    /// Returns true only when the button was pressed AND is live, so callers
    /// read exactly like a plain GUILayout.Button.</summary>
    string gateHoverWhy, gateHoverShown;
    bool GatedButton(string label, GUIStyle st, string why, params GUILayoutOption[] opt)
    {
        Color sv = GUI.color;
        if (why != null) GUI.color = new Color(0.60f, 0.60f, 0.64f);
        bool hit = GUILayout.Button(label, st, opt);
        GUI.color = sv;
        // GetLastRect is only meaningful once layout has been resolved, and
        // mousePosition is already in the scaled GUI space this panel draws in.
        if (why != null && Event.current.type == EventType.Repaint
            && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            gateHoverWhy = why;
        if (hit && why != null) { message = why; SfxSynth.Deny(); return false; }
        return hit;
    }

    /// <summary>"Did you mean to replace it, or to keep it and start a copy?"
    /// Both front ends ask the same question in the same words - a rule that
    /// lives in one front end is not a rule.</summary>
    void SaveConfirmGUI()
    {
        if (MobileBuilderUI.Active) { deskSaveConfirm = false; return; }
        string open = ActiveEditName();
        if (open == null) { deskSaveConfirm = false; return; }
        EnsureStyles();
        var prevM = GUI.matrix;
        var prevC = GUI.color;
        float sc = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float sw = Screen.width / sc, sh = Screen.height / sc;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
        GUI.color = prevC;

        float w = 470f, h = 210f;
        var r = new Rect((sw - w) * 0.5f, (sh - h) * 0.5f, w, h);
        GUI.Box(r, "");
        GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 14f, r.width - 32f, r.height - 28f));
        GUILayout.Label("SAVE", headStyle);
        GUILayout.Space(4f);
        GUILayout.Label("OVERWRITE replaces " + open + " with what is on the bench now.  "
                      + "SAVE AS NEW keeps " + open + " as it was and starts a copy.", descStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("OVERWRITE " + open, matStyle, GUILayout.Height(30f)))
        {
            string e = SaveActive();
            if (e != null) { message = e; SfxSynth.Deny(); }
            deskSaveConfirm = false;
        }
        if (GUILayout.Button("SAVE AS NEW\u2026", matStyle, GUILayout.Height(30f)))
        {
            deskSaveConfirm = false;
            OpenDesktopSaveDialog();
        }
        GUILayout.Space(4f);
        if (GUILayout.Button("CANCEL", matStyle, GUILayout.Height(26f))) deskSaveConfirm = false;
        GUILayout.EndArea();
        GUI.matrix = prevM;
        GUI.color = prevC;
    }

    void OpenDesktopSaveDialog()
    {
        deskSaveDlg = true; deskSaveFocus = true;
        deskSaveBuf = ""; deskSaveErr = "";
        deskSaveNote = SaveAsNewNote();
        uiPointerBlocked = true;   // modal: no placing parts through the window
    }
    void CloseDesktopSaveDialog()
    {
        deskSaveDlg = false;
        uiPointerBlocked = false;
    }

    /// <summary>Desktop twin of MobileBuilderUI's savedlg.</summary>
    void SaveDialogGUI()
    {
        if (deskSaveConfirm) { SaveConfirmGUI(); return; }
        if (!deskSaveDlg) return;
        if (MobileBuilderUI.Active) { CloseDesktopSaveDialog(); return; }
        EnsureStyles();
        var prevM = GUI.matrix;
        var prevC = GUI.color;
        float sc = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(sc, sc, 1f));
        float sw = Screen.width / sc, sh = Screen.height / sc;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
        GUI.color = prevC;

        float w = 470f, h = 240f;
        var r = new Rect((sw - w) * 0.5f, (sh - h) * 0.5f, w, h);
        GUI.Box(r, "");
        GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 14f, r.width - 32f, r.height - 28f));
        GUILayout.Label("NAME THIS BUILD", headStyle);
        GUILayout.Space(4f);
        GUI.SetNextControlName("desksavename");
        deskSaveBuf = GUILayout.TextField(deskSaveBuf ?? "", GUILayout.Height(22f));
        if (deskSaveFocus) { GUI.FocusControl("desksavename"); deskSaveFocus = false; }
        GUILayout.Space(4f);
        GUILayout.Label(deskSaveNote, descStyle);
        // Drawn UNCONDITIONALLY, empty string and all. A label that appears
        // only when there is an error changes the control count between the
        // Layout and Repaint passes of the very frame the error is set -
        // "GUILayout: Mismatched LayoutGroup", the same trap the blueprint
        // list hit when it deleted a row mid-loop.
        GUI.color = new Color(1f, 0.78f, 0.30f);
        GUILayout.Label(deskSaveErr, bodyStyle);
        GUI.color = prevC;
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("CANCEL", matStyle, GUILayout.Width(96f))) CloseDesktopSaveDialog();
        bool noName = string.IsNullOrEmpty(deskSaveBuf) || deskSaveBuf.Trim().Length == 0;
        // The window has its own error lane an inch away, so the reason goes
        // THERE rather than to the message bar behind the modal. Same gate,
        // nearer surface - a toast under a dimmed backdrop is worse than no
        // toast, and this is the one place on screen that already has a better
        // place to put it.
        Color svSave = GUI.color;
        if (noName) GUI.color = new Color(0.60f, 0.60f, 0.64f);
        bool saveHit = GUILayout.Button("SAVE", matStyle, GUILayout.Width(110f));
        GUI.color = svSave;
        if (noName && Event.current.type == EventType.Repaint
            && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            deskSaveErr = NAME_GATE_HINT;
        if (saveHit)
        {
            string e = SaveAsNew(deskSaveBuf);
            // A refusal keeps the window open with the reason in it - closing
            // would drop the name they typed and hide why it did not take.
            if (e != null) { deskSaveErr = e; SfxSynth.Deny(); }
            else CloseDesktopSaveDialog();
        }
        GUI.color = prevC;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUI.matrix = prevM;
        GUI.color = prevC;
    }

    void EnsureStyles()
    {
        if (btnStyle != null) return;
        matStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
        matStyle.padding = new RectOffset(2, 2, 5, 5);
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
    /// <summary>OWEN 2026-08-03: "for a mobile user, it doesn't make sense to
    /// see the desktop option, and vice versa. can we detect the device type
    /// and skip the first screen?"
    ///
    /// Yes - and the screen was worse than redundant. MobileBuilderUI already
    /// auto-detected, so on an iPad "START CAREER - Desktop" handed you the
    /// touch UI regardless. The chooser asked a question the game then
    /// overruled; both buttons landed in the same place.
    ///
    /// A build now boots straight into career in the detected mode. The EDITOR
    /// keeps the chooser, so testing touch-on-desktop and the dev sandbox
    /// stays one click away - the dev sandbox was always a dev door anyway,
    /// and this is the honest place for it.</summary>
    /// <summary>Should we skip the chooser and boot straight in?
    ///
    /// OWEN 2026-08-04: "I'm using the device simulator in unity to test
    /// running on iphone. But when i click the 'start career' button there is
    /// no response."
    ///
    /// Nothing was wrong with the button. Unity's Device Simulator DISABLES the
    /// mouse and substitutes a simulated touchscreen - measured, with this
    /// project's Input System-only handling:
    ///
    ///     devices: Keyboard[on] Mouse[off] Pen[off] Touchscreen[on]
    ///
    /// IMGUI wants mouse events, so with no mouse device every OnGUI screen in
    /// the game RENDERS in the simulator and none of them can be clicked. That
    /// includes this chooser, which is why it was a dead end rather than a
    /// cosmetic annoyance.
    ///
    /// "Application.isEditor" was the wrong question all along. The real one is
    /// "is a human going to drive the DESKTOP builder here", and under a
    /// simulated phone the answer is no - the same as in a build, which is
    /// exactly the thing the simulator exists to imitate. So the simulator now
    /// boots like a build: past this screen, into the touch UI, which is uGUI
    /// and does receive the simulated touches.
    ///
    /// A real editor on a real desktop still gets the chooser, so the dev
    /// sandbox door stays one click away.</summary>
    public static bool ShouldAutoBoot()
    {
        if (!Application.isEditor) return true;
        return MobileBuilderUI.DeviceWantsTouch();
    }

    void Start()
    {
        if (!ShouldAutoBoot()) return;
        StartCareer(MobileBuilderUI.DeviceWantsTouch());
        Destroy(gameObject);
    }

    /// <summary>One path in, so the two buttons and the auto-boot cannot drift.
    /// forceMobileUI is set EXPLICITLY either way rather than only on the touch
    /// branch - it is a static that survives a play-mode restart in the editor,
    /// so "not setting it" quietly meant "keep whatever the last run chose".</summary>
    public static void StartCareer(bool touch)
    {
        // C4: the game IS the career now (owner decision 2026-07-31, no
        // sandbox split). Load grants the starter kit on first run.
        Career.active = true;
        Career.Load();
        MobileBuilderUI.forceMobileUI = touch;
        new GameObject("BuilderManager").AddComponent<BuilderManager>();
    }

    void OnGUI()
    {
        // Builds never draw this, and neither does a simulated device - Start()
        // has already booted past it. Destroy is deferred to the end of the
        // frame and OnGUI runs before that, so without this guard the chooser
        // would flash over the game for one frame on the way through.
        if (ShouldAutoBoot()) return;
        // Critic round 1 (mobile): raw pixels made these buttons thumbnail
        // sized on a 264-dpi iPad. Same DPI scale as the rest of the HUD.
        float s = BuilderManager.GuiScale;   // R4 finding 3: one rule, one place
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        // R2 critic: the first screen a player sees was a thumbnail-sized
        // default-skin box floating in a void. Bigger, bolder, same skin.
        float w = 440f, h = 200f;
        float x = (Screen.width / s - w) * 0.5f, y = (Screen.height / s - h) * 0.5f;
        var tst = new GUIStyle(GUI.skin.box);
        tst.fontSize = 26; tst.fontStyle = FontStyle.Bold; tst.alignment = TextAnchor.UpperCenter; tst.padding.top = 16;
        GUI.Box(new Rect(x, y, w, h), "ROBOT BRAWL", tst);
        var mbst = new GUIStyle(GUI.skin.button); mbst.fontSize = 17;
        // C6.5: five taps on the title reveal the dev sandbox entry - players
        // never see a mode choice; the career IS the game (design doc v1.4).
        if (GUI.Button(new Rect(x, y, w, 30), "", GUIStyle.none)) devTaps++;
        // The detected default is marked, so the editor chooser doubles as a
        // readout of what a real build would have done on this machine.
        bool wantsTouch = MobileBuilderUI.DeviceWantsTouch();
        if (GUI.Button(new Rect(x + 24, y + 64, w - 48, 44),
                       "START CAREER \u2014 Desktop" + (wantsTouch ? "" : "   (detected)"), mbst))
        { StartCareer(false); Destroy(gameObject); }
        if (GUI.Button(new Rect(x + 24, y + 118, w - 48, 44),
                       "START CAREER \u2014 Touch" + (wantsTouch ? "   (detected)" : ""), mbst))
        { StartCareer(true); Destroy(gameObject); }
        if (devTaps >= 5 && GUI.Button(new Rect(x + 24, y + h + 8, w - 48, 32), "DEV SANDBOX"))
        {
            Career.active = false;   // free-build test mode: loud banner, no career file
            new GameObject("BuilderManager").AddComponent<BuilderManager>();
            Destroy(gameObject);
        }
    }
    int devTaps;
}

}