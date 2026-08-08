using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// Builds the whole Phase 0 test scene procedurally (no scene authoring, no
/// prefabs — press Play in any empty scene) and runs the three checks from
/// design doc §12 Phase 0:
///
///   1. Drive a hard-coded bot on custom raycast wheels.
///   2. Toggle a top-heavy variant (T) and watch it tip in corners — emergent
///      from mass data alone (§3), nothing scripted.
///   3. Ram the dummy hard enough to break a virtual joint and trigger a
///      dynamic body split (§5.2–5.3) — the project's biggest technical risk.
///
/// Controls: WASD/arrows drive · T toggle top-heavy · F flip impulse · R reset.
/// </summary>
public class Phase0Manager : MonoBehaviour
{
    CompoundRobot player, dummy;
    RaycastWheelDrive drive;
    bool topHeavy;
    readonly List<string> eventLog = new List<string>();
    Transform comMarker, comLine;
    FollowCamera followCam;

    void Start()
    {
        // Fixed 50 Hz timestep + solver settings (§2.3, §6.4).
        Time.fixedDeltaTime = 0.02f;
        Physics.defaultSolverIterations = 12;
        // (per-rigidbody maxAngularVelocity is set in CompoundRobot.Build —
        // Physics.defaultMaxAngularVelocity was removed in newer Unity 6 versions.)

        CompoundRobot.Log = AddLog;

        BuildArena();
        SetupCameraAndLight();
        BuildComMarker();
        Respawn();
    }

    // ------------------------------------------------------------------ scene

    void BuildArena()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "arena_floor";
        BuilderManager.FloorBoxCollider(ground);
        ground.transform.localScale = new Vector3(4f, 1f, 4f); // 40 m × 40 m
        ground.GetComponent<Renderer>().sharedMaterial =
            MatDB.MakeRenderMat(new Color(0.55f, 0.53f, 0.50f));

        for (int i = 0; i < 4; i++)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "arena_wall_" + i;
            bool alongX = i < 2;
            float sign = (i % 2 == 0) ? 1f : -1f;
            wall.transform.position = alongX
                ? new Vector3(0f, 0.75f, sign * 20f)
                : new Vector3(sign * 20f, 0.75f, 0f);
            wall.transform.localScale = alongX
                ? new Vector3(40f, 1.5f, 0.5f)
                : new Vector3(0.5f, 1.5f, 40f);
            wall.GetComponent<Renderer>().sharedMaterial =
                MatDB.MakeRenderMat(new Color(0.35f, 0.32f, 0.30f));
        }
    }

    void SetupCameraAndLight()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
        }
        followCam = cam.GetComponent<FollowCamera>();
        if (followCam == null) followCam = cam.gameObject.AddComponent<FollowCamera>();

        bool hasLight = false;
#if UNITY_2023_1_OR_NEWER
        hasLight = Object.FindFirstObjectByType<Light>() != null;
#else
        hasLight = Object.FindObjectOfType<Light>() != null;
#endif
        if (!hasLight)
        {
            var lightGo = new GameObject("Sun");
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }
    }

    void BuildComMarker()
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>()); // visual only — must not block wheel rays
        s.name = "com_marker";
        s.transform.localScale = Vector3.one * 0.12f;
        s.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(Color.red);
        comMarker = s.transform;

        var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(l.GetComponent<Collider>());
        l.name = "com_dropline";
        l.GetComponent<Renderer>().sharedMaterial = MatDB.MakeRenderMat(new Color(1f, 0.3f, 0.3f, 1f));
        comLine = l.transform;
    }

    // ------------------------------------------------------------------ bots

    void Respawn()
    {
        CompoundRobot.ClearAll();
        eventLog.Clear();

        // ----- player bot: chassis + front ram spike + mast (the 3-part bot) --
        // The top-heavy variant swaps the small aluminum mast for a tall, heavy
        // steel one. Nothing else changes — the tipping is pure mass data (§3).
        PartSpec mast = topHeavy
            ? new PartSpec("mast", new Vector3(0.2f, 1.2f, 0.2f), new Vector3(0f, 0.70f, -0.1f), "Steel")
            : new PartSpec("mast", new Vector3(0.15f, 0.3f, 0.15f), new Vector3(0f, 0.25f, -0.1f), "Aluminum");

        var playerSpecs = new[]
        {
            new PartSpec("chassis", new Vector3(0.5f, 0.2f, 0.7f), Vector3.zero, "Aluminum", isCore: true),
            new PartSpec("spike",   new Vector3(0.1f, 0.1f, 0.3f), new Vector3(0f, -0.02f, 0.5f), "Steel"),
            mast,
        };
        var playerConns = new[]
        {
            new CompoundRobot.Conn(0, 1), // chassis↔spike
            new CompoundRobot.Conn(0, 2), // chassis↔mast
        };

        player = CompoundRobot.Build("PlayerBot", playerSpecs, playerConns,
                                     new Vector3(0f, 0.6f, -8f), Quaternion.identity);

        drive = player.gameObject.AddComponent<RaycastWheelDrive>();
        drive.Init(player.rb,
            new[]
            {
                new Vector3(-0.30f, -0.08f,  0.30f), // front-left  (outboard → wider track)
                new Vector3( 0.30f, -0.08f,  0.30f), // front-right
                new Vector3(-0.30f, -0.08f, -0.30f), // rear-left
                new Vector3( 0.30f, -0.08f, -0.30f), // rear-right
            },
            new[] { true, true, false, false }); // front wheels steer

        // ----- ram dummy: heavy steel base + fragile ABS pillar and head -----
        var dummySpecs = new[]
        {
            new PartSpec("base",   new Vector3(0.5f, 0.2f, 0.5f),  Vector3.zero, "Steel", isCore: true),
            new PartSpec("pillar", new Vector3(0.14f, 0.5f, 0.14f), new Vector3(0f, 0.35f, 0f), "ABS"),
            new PartSpec("head",   new Vector3(0.3f, 0.3f, 0.3f),  new Vector3(0f, 0.75f, 0f), "ABS"),
        };
        var dummyConns = new[]
        {
            new CompoundRobot.Conn(0, 1), // base↔pillar
            new CompoundRobot.Conn(1, 2), // pillar↔head
        };

        dummy = CompoundRobot.Build("RamDummy", dummySpecs, dummyConns,
                                    new Vector3(0f, 0.15f, 4f), Quaternion.identity);

        followCam.target = player.transform;
        AddLog(topHeavy
            ? "Spawned TOP-HEAVY bot (" + player.ActiveMass().ToString("F0") + " kg, tall steel mast). Corner hard and watch it go over."
            : "Spawned stable bot (" + player.ActiveMass().ToString("F0") + " kg). Ram the dummy to snap its pillar off.");
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        if (Phase0Input.ResetDown()) Respawn();
        if (Phase0Input.ToggleDown()) { topHeavy = !topHeavy; Respawn(); }
        // Crude roll impulse for quick tip-over tests — enough to flip the
        // top-heavy bot, a dramatic barrel roll for the stable one.
        if (Phase0Input.FlipDown() && player != null)
            player.rb.AddTorque(player.transform.forward * player.rb.mass * 0.45f, ForceMode.Impulse);

        // Live center-of-mass marker + drop line (§5.4's most important UI
        // element, in embryonic form): red = what the physics actually uses.
        if (player != null && comMarker != null)
        {
            Vector3 com = player.rb.worldCenterOfMass;
            comMarker.position = com;
            float h = Mathf.Max(0.01f, com.y);
            comLine.position = new Vector3(com.x, com.y - h * 0.5f, com.z);
            comLine.localScale = new Vector3(0.02f, h, 0.02f);
        }
    }

    void AddLog(string msg)
    {
        eventLog.Add(Time.time.ToString("F1") + "s  " + msg);
        if (eventLog.Count > 8) eventLog.RemoveAt(0);
        Debug.Log("[Phase0] " + msg);
    }

    // ------------------------------------------------------------------ HUD

    void OnGUI()
    {
        GUI.Box(new Rect(10, 10, 430, 200), "ROBOT BRAWL — Phase 0 physics proof");
        GUILayout.BeginArea(new Rect(20, 35, 410, 170));
        GUILayout.Label("WASD/arrows drive · T top-heavy toggle · F flip impulse · R reset");
        if (player != null)
        {
            GUILayout.Label(string.Format(
                "Mode: {0}   Mass: {1:F0} kg   Speed: {2:F1} km/h   CoM height: {3:F2} m",
                topHeavy ? "TOP-HEAVY" : "stable",
                player.ActiveMass(),
                drive != null ? drive.SpeedKmh() : 0f,
                player.rb.worldCenterOfMass.y));
            GUILayout.Space(4);
            GUILayout.Label("Virtual joints (peak stress / break threshold, N·s):");
            DrawEdges(player);
            if (dummy != null) DrawEdges(dummy);
        }
        GUILayout.EndArea();

        GUI.Box(new Rect(10, 220, 430, 24 + 18 * Mathf.Max(1, eventLog.Count)), "Events");
        GUILayout.BeginArea(new Rect(20, 242, 410, 18 * Mathf.Max(1, eventLog.Count)));
        foreach (var line in eventLog) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    void DrawEdges(CompoundRobot r)
    {
        foreach (var e in r.edges)
        {
            string status = e.broken ? "BROKEN" : (e.peak / e.threshold).ToString("P0");
            GUILayout.Label(string.Format("  {0} · {1}: {2:F0} / {3:F0}  ({4})",
                r.name, e.label, e.peak, e.threshold, status));
        }
    }
}
}
