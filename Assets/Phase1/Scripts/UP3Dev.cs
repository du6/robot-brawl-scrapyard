// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — 2026-08-10.
// This is a QA harness. It was compiling into the SHIPPED iOS player: 44 such
// files, ~13k lines, and the string "dev-only-worker-key" was verified present
// in build/ios global-metadata.dat. Dead weight in the binary, and a surface
// that can register accounts and start fights inside a player's app.
// Nothing in the product references any harness — checked against every
// product script, every scene and every prefab before this guard was added.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ROUND-3 IMPLEMENTATION. One section-dispatched harness so the SAME code
// produces every BEFORE number and every AFTER number - a disagreement between
// them cannot then be a difference in how they were measured (rule 4).
//
// Inherits UserPathTest's two hard-won rules: VERIFY THE HIT (TestGhostTarget +
// TestGhostNormal before trusting any probe) and ISOLATE THE FIXTURE (bare core).
public class UP3Dev : MonoBehaviour
{
    public string outFile = "qa_up3_dev.txt";
    public string section = "geom";
    public bool done;
    public int checks, failures, offCam;
    public string summary = "";

    BuilderManager bm;
    string path;

    const string BASE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        if (!File.Exists(path)) File.WriteAllText(path, "# UP3 DEV - before/after, one instrument\n");
        W("\n===== SECTION " + section + " =====");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no bm"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }

        if (section == "geom") yield return SecGeom();
        else if (section == "leak") yield return SecLeak();
        else W("unknown section " + section);

        Phase0Input.debugPointer = false;
        bm.selected = -1;
        summary = string.Format("checks={0} failures={1} offCam={2}", checks, failures, offCam);
        W("## DONE " + summary);
        done = true;
    }

    // ------------------------------------------------------------------ geom
    // The CRITICAL finding: the box a part COLLIDES with (PlacedPart.Half, which
    // is also what SpawnBot feeds to both BoxCollider.size AND the arena visual)
    // versus the box it is DRAWN as. Every placement here is made by pointer.
    IEnumerator SecGeom()
    {
        W("## BOX (Half*2 = collider + overlap + snap) vs MESH (real renderer bounds)");
        W("   ORIENT = do the longest and shortest axes of box and mesh agree?");
        W("   OVER   = worst (mesh-box)/2 in metres: how far solid-looking mesh");
        W("            hangs outside the collider (+) on any one axis.");
        string[] ids = { "wedge", "hook", "pivot", "spindle", "ram", "blade", "spike" };
        string[] fn = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        Vector3[] fd = { Vector3.right, Vector3.left, Vector3.up,
                         Vector3.down, Vector3.forward, Vector3.back };
        var pal = P1PartDef.Palette();

        foreach (var id in ids)
        {
            int pi = -1;
            for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) pi = i;
            if (pi < 0) { W(id + ": not in palette"); continue; }
            W("");
            for (int f = 0; f < 6; f++)
            {
                yield return LoadBase();
                bm.selected = pi;
                bool reached = false;
                yield return Reach(fd[f], r => reached = r);
                if (!reached) { offCam++; W(string.Format("  {0,-8} {1}  off-camera", id, fn[f])); continue; }
                if (!bm.TestGhostValid)
                { W(string.Format("  {0,-8} {1}  refused: {2}", id, fn[f], bm.TestGhostReason)); continue; }

                int before = bm.placed.Count;
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                if (bm.placed.Count <= before)
                { failures++; checks++; W(string.Format("  {0,-8} {1}  !!GHOST-OK-NO-PLACE", id, fn[f])); continue; }

                var p = bm.placed[bm.placed.Count - 1];
                Vector3 box = p.Half() * 2f;
                Vector3 mesh = MeshSize(p.go);
                checks++;
                int verdict = Orient(box, mesh);
                float over = -99f;
                for (int i = 0; i < 3; i++) over = Mathf.Max(over, (mesh[i] - box[i]) * 0.5f);
                if (verdict < 0) failures++;
                W(string.Format("  {0,-8} {1}  box {2}  mesh {3}  ORIENT {4}  OVER {5:F3}",
                    id, fn[f], box.ToString("F3"), mesh.ToString("F3"),
                    verdict > 0 ? "ok" : verdict == 0 ? "cube-n/a" : "!!MISMATCH", over));
            }
        }
    }

    // ------------------------------------------------------------------ leak
    // The MAJOR finding: UpdateBuild's `!overPanel && clicksLive && MouseDown(0)`
    // short-circuits, so a debug click posted while the pointer is over the
    // palette is NEVER consumed and fires on a later frame at a place the test
    // never clicked. Humans are unaffected (a real mouse-down expires with the
    // frame); every harness on this project is affected, which makes it a bug in
    // the evidence base rather than in the game.
    IEnumerator SecLeak()
    {
        W("## CLICK LEAK: click over the PANEL, then move onto a valid face with NO new click.");
        W("   CORRECT: nothing is ever placed. LEAKING: the panel click fires later.");
        var pal = P1PartDef.Palette();
        int pi = -1;
        for (int i = 0; i < pal.Length; i++) if (pal[i].id == "beam") pi = i;

        for (int rep = 0; rep < 3; rep++)
        {
            yield return LoadBase();
            bm.selected = pi;

            // 1. prove the face is genuinely placeable first, so the test is not vacuous
            bool reached = false;
            yield return Reach(Vector3.up, r => reached = r);
            if (!reached) { offCam++; W("  rep " + rep + ": +Y off-camera"); continue; }
            if (!bm.TestGhostValid) { W("  rep " + rep + ": ghost not valid, test vacuous"); continue; }
            Vector3 faceScreen = Phase0Input.debugMousePos;
            int n0 = bm.placed.Count;

            // 2. park the pointer OVER THE PANEL and click there
            Phase0Input.debugMousePos = new Vector3(40f, faceScreen.y, faceScreen.z);
            yield return null;
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            int nPanel = bm.placed.Count;

            // 3. move back onto the face WITHOUT clicking again
            Phase0Input.debugMousePos = faceScreen;
            yield return null; yield return null; yield return null; yield return null;
            int nAfter = bm.placed.Count;

            checks++;
            bool leaked = nAfter > nPanel;
            bool panelPlaced = nPanel > n0;
            if (leaked || panelPlaced) failures++;
            W(string.Format("  rep {0}: parts {1} -> after panel-click {2} -> after move {3}   {4}",
                rep, n0, nPanel, nAfter,
                panelPlaced ? "!!PLACED FROM OVER THE PANEL"
                : leaked ? "!!LEAKED - placed with no click on the model"
                : "ok - click consumed where it happened"));
        }

        // Control: the SAME sequence with a real click on the face must still place,
        // so a "fix" that simply swallows every click cannot pass this section.
        yield return LoadBase();
        bm.selected = pi;
        bool ok2 = false;
        yield return Reach(Vector3.up, r => ok2 = r);
        if (ok2 && bm.TestGhostValid)
        {
            int n0 = bm.placed.Count;
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            checks++;
            bool placed = bm.placed.Count > n0;
            if (!placed) failures++;
            W("  CONTROL click on the face: " + (placed ? "ok - still places" : "!!NO LONGER PLACES"));
        }

        // Cross-MODE leak: UpdateBuild is the only consumer, so a debug click
        // posted while the game is in TEST cannot be consumed until the builder
        // is back. Measured rather than assumed - I am not fixing what I have
        // not seen fail.
        yield return LoadBase();
        bm.selected = pi;
        bool ok3 = false;
        yield return Reach(Vector3.up, r => ok3 = r);
        if (ok3 && bm.TestGhostValid)
        {
            Vector3 face = Phase0Input.debugMousePos;
            int n0 = bm.placed.Count;
            bm.LoadSnapshot(BASE
                + "beam|0.000,0.700,0.400|0|0.00,0.00,0.00|Steel\n"
                + "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n"
                + "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n"
                + "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n");
            yield return null;
            bm.StartTest();
            yield return null; yield return null;
            Phase0Input.DebugClick();          // posted in TEST mode
            for (int i = 0; i < 8; i++) yield return null;
            bm.BackToBuild();
            yield return null;
            yield return LoadBase();
            bm.selected = pi;
            Phase0Input.debugMousePos = face;
            for (int i = 0; i < 8; i++) yield return null;
            checks++;
            bool crossLeak = bm.placed.Count > 1;
            if (crossLeak) failures++;
            W("  CROSS-MODE click posted in TEST, then back to Build: parts=" + bm.placed.Count
              + (crossLeak ? "  !!LEAKED ACROSS THE MODE SWITCH" : "  ok - no cross-mode leak"));
        }
    }

    // --------------------------------------------------------------- helpers
    static Vector3 MeshSize(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return Vector3.zero;
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.size;
    }
    /// <summary>+1 agree, 0 nothing to compare, -1 disagree.
    ///
    /// MY OWN FIRST VERSION OF THIS WAS WRONG and I am leaving the reason in
    /// the file. It compared argmax/argmin of box and mesh, which is undefined
    /// for a CUBE: pivot, spindle and ram are 0.30 m cubes, ties resolved to
    /// axis 0, and the mesh's longest axis is wherever the axle boss points -
    /// so all 18 actuator probes came back "!!MISMATCH" for a box that is
    /// provably orientation-independent. That would have been 18 false findings
    /// in the same round the brief warns about a headline a re-test reversed.
    ///
    /// The honest test: for every PAIR of axes the box actually distinguishes
    /// (more than 0.02 m apart - finer than any part's smallest real feature),
    /// the mesh must order that pair the same way. A cube distinguishes no
    /// pair, so it is scored "nothing to compare" rather than pass or fail.</summary>
    static int Orient(Vector3 box, Vector3 mesh)
    {
        int comparable = 0;
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
            {
                if (Mathf.Abs(box[i] - box[j]) <= 0.02f) continue;
                comparable++;
                if (Mathf.Sign(box[i] - box[j]) != Mathf.Sign(mesh[i] - mesh[j])) return -1;
            }
        return comparable == 0 ? 0 : 1;
    }

    IEnumerator LoadBase()
    {
        Phase0Input.debugPointer = true;
        bm.LoadSnapshot(BASE);
        yield return null;
    }

    /// <summary>UserPathTest.Reach, unchanged in substance: swing the camera
    /// until the builder's OWN raycast reports the wanted face, then leave the
    /// pointer on it. Never trust a projected face centre on its own.</summary>
    IEnumerator Reach(Vector3 dir, System.Action<bool> report)
    {
        var p = bm.placed[0];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f }
                        : dir.y > 0.5f  ? new[] { 55f, 30f }
                                        : new[] { 20f, 30f, 0f };
        foreach (float pitch in pitches)
        foreach (float yaw in new[] { 35f, 125f, 215f, 305f })
        {
            bm.TestOrbitYaw = yaw;
            bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report(false); yield break; }
            Phase0Input.debugMousePos = cam.WorldToScreenPoint(world);
            yield return null; yield return null;
            if (bm.TestGhostTarget == "core"
                && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f) { report(true); yield break; }
        }
        report(false);
    }
}
}
#endif
