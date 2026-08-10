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

/// <summary>
/// ROUND-2 IMPLEMENTATION HARNESS. One MonoBehaviour, one `section` string,
/// so the SAME code produces the BEFORE number and the AFTER number and a
/// disagreement between them cannot be a difference in how they were measured.
///
/// Every builder claim in here comes through the pointer (rule 5). LoadSnapshot
/// is used ONLY to set a fixture up, never to make a claim about the builder.
///
/// Inherits UserPathTest's two hard-won rules: VERIFY THE HIT before trusting a
/// probe, and ISOLATE THE FIXTURE.
/// </summary>
public class UP2Dev : MonoBehaviour
{
    public string section = "orient";
    public string outFile = "qa_up2_measure.txt";
    public bool done;
    public int checks, failures;

    BuilderManager bm;
    string path;

    const string BASE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    static int PalIdx(string id)
    {
        var pal = P1PartDef.Palette();
        for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i;
        return -1;
    }

    /// <summary>World-space rendered bounds of a placed part - what the player
    /// actually SEES, which is the whole point of the orientation check.</summary>
    static Vector3 Drawn(GameObject go)
    {
        if (go == null) return Vector3.zero;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return Vector3.zero;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b.size;
    }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        W("\n==================== SECTION " + section + " ====================");
        Phase0Input.debugPointer = true;
        yield return null;

        if (section == "orient")   yield return Orient();
        else if (section == "yawleak") yield return YawLeak();
        else if (section == "budget")  yield return Budget();
        else if (section == "plateR")  yield return PlateR();
        else W("unknown section");

        W(string.Format("## SECTION {0} DONE checks={1} failures={2}", section, checks, failures));
        Phase0Input.debugPointer = false;
        bm.selected = -1;
        bm.activeMat = "Aluminum";
        done = true;
    }

    // ---------------------------------------------------------------- C1
    // Does the BUILDER draw a mount-oriented part differently on different
    // faces? Pointer-placed, drawn bounds measured off the real renderers.
    IEnumerator Orient()
    {
        W("part        face  drawnBounds(world)                 pos");
        string[] ids = { "pivot", "spindle", "ram", "blade", "wedge", "hook", "spike" };
        Vector3[] dirs = { Vector3.back, Vector3.up, Vector3.right };
        string[] dn = { "-Z", "+Y", "+X" };
        foreach (string id in ids)
        {
            int pi = PalIdx(id);
            if (pi < 0) { W(id + " NOT IN PALETTE"); continue; }
            var drawn = new List<Vector3>();
            for (int f = 0; f < dirs.Length; f++)
            {
                yield return LoadBase();
                bm.selected = pi;
                bool reached = false;
                yield return Reach(dirs[f], r => reached = r);
                if (!reached) { W(string.Format("{0,-11} {1}  off-cam", id, dn[f])); continue; }
                if (!bm.TestGhostValid)
                { W(string.Format("{0,-11} {1}  REFUSED {2}", id, dn[f], bm.TestGhostReason)); continue; }
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                if (bm.placed.Count < 2) { W(string.Format("{0,-11} {1}  NO-PLACE", id, dn[f])); continue; }
                Vector3 d = Drawn(bm.placed[1].go);
                drawn.Add(d);
                W(string.Format("{0,-11} {1}  {2}   {3}", id, dn[f], d.ToString("F3"),
                                bm.placed[1].pos.ToString("F3")));
            }
            checks++;
            if (drawn.Count >= 2)
            {
                bool anyDiff = false;
                for (int i = 1; i < drawn.Count; i++)
                    if ((drawn[i] - drawn[0]).sqrMagnitude > 1e-5f) anyDiff = true;
                if (!anyDiff) { failures++; W("  ^^ " + id + " !!DRAWN IDENTICAL ON EVERY FACE (orientation ignored)"); }
                else W("  ^^ " + id + " orientation follows the mount face: OK");
            }
        }
    }

    // ---------------------------------------------------------------- C6
    IEnumerator YawLeak()
    {
        int beam = PalIdx("beam"), bl = PalIdx("beamlong"), eng = PalIdx("engine");
        yield return LoadBase();
        bm.selected = beam;
        bool r0 = false;
        yield return Reach(Vector3.up, r => r0 = r);
        W("beam reached=" + r0 + " startYaw=" + bm.TestGhostYaw);
        Phase0Input.DebugRotate(); yield return null; yield return null;
        int afterR = bm.TestGhostYaw;
        W("beam after one R: yaw=" + afterR);

        bm.selected = bl;
        yield return null; yield return null; yield return null;
        int blYaw = bm.TestGhostYaw;
        checks++;
        if (blYaw != 0) { failures++; W("!!LEAK beamlong first yaw=" + blYaw + " (expected 0)"); }
        else W("beamlong first yaw=0 OK");

        bm.selected = eng;
        yield return null; yield return null; yield return null;
        int eYaw = bm.TestGhostYaw;
        checks++;
        if (eYaw != 0) { failures++; W("!!LEAK engine first yaw=" + eYaw + " (expected 0)"); }
        else W("engine first yaw=0 OK");
    }

    // ---------------------------------------------------------------- C3
    IEnumerator Budget()
    {
        int plate = PalIdx("plate");
        yield return LoadBase();
        bm.activeMat = "Tungsten";
        bm.selected = plate;
        W("budget=" + BuilderManager.CREDIT_BUDGET + "  stacking Tungsten plates on +Y by pointer");
        int overFirstRefused = -1, firstOverCost = -1, n = 0;
        for (int i = 0; i < 12; i++)
        {
            bool reached = false;
            yield return ReachPart(bm.placed.Count - 1, Vector3.up, r => reached = r);
            if (!reached) { W("  step " + i + " off-cam, stop"); break; }
            bool valid = bm.TestGhostValid;
            string reason = bm.TestGhostReason;
            int costNow = bm.BuildCost();
            int projected = costNow + P1PartDef.Palette()[plate].CostOf("Tungsten");
            W(string.Format("  step {0}: cost {1} -> would be {2}, ghost valid={3} {4}",
                            i, costNow, projected, valid, reason));
            if (!valid && projected > BuilderManager.CREDIT_BUDGET && overFirstRefused < 0)
            { overFirstRefused = i; firstOverCost = costNow; }
            if (!valid) break;
            Phase0Input.DebugClick();
            yield return null; yield return null;
            n++;
        }
        checks++;
        int final = bm.BuildCost();
        W(string.Format("placed {0} plates, final cost {1} cr vs budget {2} ({3:F2}x)",
                        n, final, BuilderManager.CREDIT_BUDGET,
                        (float)final / BuilderManager.CREDIT_BUDGET));
        if (final > BuilderManager.CREDIT_BUDGET)
        { failures++; W("!!POINTER PATH ALLOWED AN OVER-BUDGET BUILD"); }
        else W("pointer path refused at the budget: OK (first refusal at step " + overFirstRefused
               + ", cost was " + firstOverCost + ")");
        W("Validate() = " + (bm.Validate() ?? "OK"));
    }

    // ---------------------------------------------------------------- C2
    IEnumerator PlateR()
    {
        int plate = PalIdx("plate");
        string[] fn = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        Vector3[] fd = { Vector3.right, Vector3.left, Vector3.up,
                         Vector3.down, Vector3.forward, Vector3.back };
        W("plate R-cycle on every face of a bare core (hit-verified)");
        for (int f = 0; f < 6; f++)
        {
            yield return LoadBase();
            bm.selected = plate;
            bool reached = false;
            yield return Reach(fd[f], r => reached = r);
            if (!reached) { W(fn[f] + "  off-cam"); continue; }
            string row = fn[f] + " ";
            for (int k = 0; k < 4; k++)
            {
                yield return null;
                row += string.Format(" yaw{0}:{1}", bm.TestGhostYaw,
                        bm.TestGhostValid ? "OK" : "X(" + bm.TestGhostReason + ")");
                checks++;
                Phase0Input.DebugRotate();
                yield return null; yield return null;
            }
            W(row);
        }
    }

    // ---------------------------------------------------------------- plumbing
    IEnumerator LoadBase() { bm.LoadSnapshot(BASE); yield return null; yield return null; }

    IEnumerator Reach(Vector3 dir, System.Action<bool> report)
    { yield return ReachPart(0, dir, report); }

    /// <summary>Swing the camera until the wanted face of placed[idx] is
    /// genuinely the face the BUILDER'S OWN raycast reports, then leave the
    /// pointer there. Reports false (off-camera) rather than a failure when no
    /// orbit angle exposes it - the first version of UserPathTest logged 18
    /// false failures for want of exactly this.</summary>
    IEnumerator ReachPart(int idx, Vector3 dir, System.Action<bool> report)
    {
        if (idx < 0 || idx >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[idx];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        string wantId = p.def.id;
        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f }
                        : dir.y > 0.5f  ? new[] { 55f, 30f, 70f }
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
            if (bm.TestGhostTarget == wantId
                && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f) { report(true); yield break; }
        }
        report(false);
    }
}

}
#endif
