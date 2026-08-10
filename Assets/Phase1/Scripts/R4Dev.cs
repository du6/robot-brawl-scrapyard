// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>ROUND-4 HAZARD TELEGRAPH CAPTURE (2026-08-01).
///
/// The round-4 fix is that a damaging hazard must show FOUR distinguishable
/// things - where it strikes (always), that it is about to strike, that it is
/// striking now, and that it is safe - so the only acceptable evidence is one
/// frame of each, on one camera, with the hazard state logged in the same tick
/// as the shutter.
///
/// TWO TRAPS THIS RIG IS BUILT TO AVOID.
/// 1. Round 3 photographed hazard phases at Time.timeScale = 0. FloorSaw moved
///    its blade with a Time.deltaTime lerp, which cannot advance in a frozen
///    frame, so "telegraph" and "strike" came out byte-identical and the round
///    drew the wrong conclusion. Here timeScale stays at 1 and the periods are
///    stretched x100, so a phase can be HELD for minutes while the animation
///    runs normally. (The hazard poses are now pure functions of Time.time as
///    well, so the freeze artefact is designed out at the source too.)
/// 2. Round 4's critic accidentally photographed two RESULTS screens because
///    it made the bots kinematic to hold the scene still and the enemy was
///    counted out. There are no bots in this rig at all.</summary>
public class R4Dev : MonoBehaviour
{
    public string tag = "r4dev3";
    public bool done;
    public string summary = "";

    readonly List<string> log = new List<string>();
    FloorSaw saw; Hammer ham; PerimeterScrew screw;
    GameObject rig;

    public void Run() { StartCoroutine(Go()); }

    static void SetCol(GameObject go, Color c)
    {
        var r = go.GetComponent<Renderer>();
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh != null) r.material = new Material(sh);
        r.material.color = c;
    }

    IEnumerator Go()
    {
        string dir = Path.Combine(Application.dataPath, "Phase1/qa_shots");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        foreach (var m in Object.FindObjectsByType<ModeSelect>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
        foreach (var w in Object.FindObjectsByType<MobileBuilderWatch>(FindObjectsSortMode.None)) Object.Destroy(w.gameObject);
        foreach (var u in Object.FindObjectsByType<MobileBuilderUI>(FindObjectsSortMode.None)) Object.Destroy(u.gameObject);
        foreach (var b in Object.FindObjectsByType<BuilderManager>(FindObjectsSortMode.None)) Object.Destroy(b.gameObject);
        ArenaHazards.Clear();
        Time.timeScale = 1f;
        yield return null;
        yield return null;

        rig = new GameObject("r4_rig");

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "floor"; floor.transform.SetParent(rig.transform, false);
        floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale = new Vector3(40f, 0.2f, 40f);
        SetCol(floor, new Color(0.30f, 0.30f, 0.31f, 1f));

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "wall"; wall.transform.SetParent(rig.transform, false);
        wall.transform.localPosition = new Vector3(0f, 1.2f, 9.0f);
        wall.transform.localScale = new Vector3(40f, 2.4f, 0.4f);
        SetCol(wall, new Color(0.16f, 0.16f, 0.18f, 1f));

        var lgo = new GameObject("r4_light"); lgo.transform.SetParent(rig.transform, false);
        var li = lgo.AddComponent<Light>();
        li.type = LightType.Directional; li.intensity = 1.2f; li.shadows = LightShadows.Soft;
        lgo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
        var cgo = new GameObject("r4_cam"); cgo.transform.SetParent(rig.transform, false);
        var cam = cgo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 1f);
        cam.transform.position = new Vector3(0.2f, 4.0f, -7.4f);
        cam.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
        cam.fieldOfView = 55f;

        var sg = new GameObject("saw"); sg.transform.SetParent(rig.transform, false);
        sg.transform.localPosition = new Vector3(-3.4f, 0f, 0.4f);
        saw = sg.AddComponent<FloorSaw>();
        saw.period = 400f; saw.telegraph = 100f; saw.upTime = 120f;
        saw.Build();

        var hg = new GameObject("hammer"); hg.transform.SetParent(rig.transform, false);
        // yaw 90 = the in-game orientation (press/crucible spawn hammers at
        // +/-90) and the one that shows the swing in profile
        hg.transform.localPosition = new Vector3(-0.6f, 0f, 0.9f);
        hg.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        ham = hg.AddComponent<Hammer>();
        ham.period = 500f; ham.cockTime = 140f; ham.strikeTime = 50f; ham.recoverTime = 90f;
        ham.Build();

        var eg = new GameObject("screw"); eg.transform.SetParent(rig.transform, false);
        eg.transform.localPosition = new Vector3(4.4f, 0f, 0.4f);
        screw = eg.AddComponent<PerimeterScrew>();
        screw.Build(7f);

        yield return null;
        yield return null;

        yield return Phase("SAFE", 350f, 400f);
        yield return Phase("TELEGRAPH", 72f, 101f);
        yield return Phase("STRIKE", 175f, 188f);

        Object.Destroy(rig);
        ArenaHazards.Clear();
        File.WriteAllLines(Path.Combine(dir, "index_" + tag + ".txt"), log.ToArray());
        summary = string.Join(" | ", log.ToArray());
        done = true;
    }

    IEnumerator Phase(string name, float sawT, float hamT)
    {
        saw.phase = sawT - Time.time;
        ham.phase = hamT - Time.time;
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        string dir = Path.Combine(Application.dataPath, "Phase1/qa_shots");
        File.WriteAllBytes(Path.Combine(dir, tag + "_" + name + ".png"), tex.EncodeToPNG());
        var db = screw.DrumBounds;
        log.Add(name + "  " + tex.width + "x" + tex.height
            + "  || SAW dangerous=" + saw.Dangerous + " bladeY=" + saw.BladeY.ToString("F3")
            + " padOn=" + saw.PadOn + " warnOn=" + saw.WarnOn + " warnSx=" + saw.WarnScale.x.ToString("F2")
            + " warnCol=" + saw.WarnColor.r.ToString("F2") + "," + saw.WarnColor.g.ToString("F2") + "," + saw.WarnColor.b.ToString("F2")
            + "  || HAM dangerous=" + ham.Dangerous + " arm=" + ham.ArmAngle.ToString("F1")
            + " headY=" + ham.HeadPos.y.ToString("F2") + " headZ=" + ham.HeadPos.z.ToString("F2")
            + " padOn=" + ham.PadOn + " warnOn=" + ham.WarnOn + " warnSx=" + ham.WarnScale.x.ToString("F2")
            + " warnCol=" + ham.WarnColor.r.ToString("F2") + "," + ham.WarnColor.g.ToString("F2") + "," + ham.WarnColor.b.ToString("F2")
            + "  || SCREW dangerous=" + screw.Dangerous + " padOn=" + screw.PadOn + " warnOn=" + screw.WarnOn
            + " drumSize=" + db.size.x.ToString("F2") + "," + db.size.y.ToString("F2") + "," + db.size.z.ToString("F2"));
        Object.Destroy(tex);
    }
}
}
#endif
