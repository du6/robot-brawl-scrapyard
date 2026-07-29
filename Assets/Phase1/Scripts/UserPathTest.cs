using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// THE TEST THAT SHOULD HAVE EXISTED FROM THE START.
///
/// Every automated check on this project until now went through
/// BuilderManager.LoadSnapshot -> AddPart. That path SKIPS everything a player
/// touches: the face pick, the socket rule, the snap, the overlap test, R, and
/// the click. A bug that made the blade impossible to place BY MOUSE therefore
/// survived five QA rounds, ten agents and ~300 matches, because every build in
/// all of them was authored by script.
///
/// This drives the REAL path: synthetic pointer -> UpdateBuild -> ghost ->
/// AddPart, reading exactly what the player would see in the panel.
///
/// TWO HONESTY RULES, both learned from this harness's own first run, which
/// reported eighteen false failures:
///  1. VERIFY THE HIT. Projecting a face centre to screen and raycasting back
///     does not necessarily hit that face - it can hit a nearer part, or the
///     same part's near side. Every probe now confirms TestGhostTarget and
///     TestGhostNormal are what was asked for, and reports "off-camera" rather
///     than "failed" when they are not.
///  2. ISOLATE THE FIXTURE. The first fixture had a plate over the core, so
///     every wheel probe came back "Blocked by another part" - true, and
///     nothing to do with wheels. The fixture is now a bare core.
/// </summary>
public class UserPathTest : MonoBehaviour
{
    public string outFile = "qa_userpath.txt";
    BuilderManager bm;
    string path;
    public bool done;
    public int checks, failures, offCamera;

    const string BASE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    /// <summary>Parts whose box is oriented by the MOUNT NORMAL rather than by
    /// yaw (see BuilderManager.PlacedPart.Half): R is legitimately inert on
    /// these, and the way to turn them is to bolt them to a different face.</summary>
    static bool MountOriented(P1PartDef d)
    {
        return d.category == P1Category.Mobility
            || d.id.StartsWith("spinner") || d.id == "spike";
    }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# USER-PATH TEST v2 - real ghost pipeline, hit-verified\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }

        Phase0Input.debugPointer = true;
        yield return null;

        var pal = P1PartDef.Palette();
        W("palette=" + pal.Length + "  budget=" + BuilderManager.CREDIT_BUDGET);

        string[] fn = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        Vector3[] fd = { Vector3.right, Vector3.left, Vector3.up,
                         Vector3.down, Vector3.forward, Vector3.back };

        W("\n## 1. ATTACH each part onto each face of a bare core (hit-verified)");
        for (int pi = 0; pi < pal.Length; pi++)
        {
            var def = pal[pi];
            if (def.id == "core") continue;
            string row = string.Format("{0,-11}", def.id);
            for (int f = 0; f < 6; f++)
            {
                yield return LoadBase();
                bm.selected = pi;
                bool reached = false;
                yield return Reach(fd[f], r => reached = r);
                if (!reached) { offCamera++; row += " " + fn[f] + ":off-cam"; continue; }

                bool valid = bm.TestGhostValid;
                string reason = bm.TestGhostReason;
                int before = bm.placed.Count;
                if (valid)
                {
                    Phase0Input.DebugClick();
                    yield return null; yield return null; yield return null;
                }
                bool landed = bm.placed.Count > before;
                checks++;
                if (valid && !landed) { failures++; row += " " + fn[f] + ":!!GHOST-OK-NO-PLACE"; }
                else if (valid) row += " " + fn[f] + ":ok";
                else row += " " + fn[f] + ":X(" + Short(reason) + ")";
            }
            W(row);
        }

        W("\n## 2. ROTATE - R must move the box AND the drawn mesh");
        for (int pi = 0; pi < pal.Length; pi++)
        {
            var def = pal[pi];
            if (def.id == "core") continue;
            yield return LoadBase();
            bm.selected = pi;

            bool ok = false;
            foreach (var d in new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back })
            {
                bool reached = false;
                yield return Reach(d, r => reached = r);
                if (reached && bm.TestGhostValid) { ok = true; break; }
            }
            if (!ok) { W(string.Format("{0,-11} no valid+visible face, rotation untested", def.id)); continue; }

            int y0 = bm.TestGhostYaw;
            Vector3 m0 = bm.TestGhostMeshSize();
            Phase0Input.DebugRotate();
            yield return null; yield return null; yield return null;
            int y1 = bm.TestGhostYaw;
            Vector3 m1 = bm.TestGhostMeshSize();

            bool yawMoved = y0 != y1;
            bool meshMoved = (m0 - m1).sqrMagnitude > 1e-5f;
            bool square = Mathf.Abs(def.size.x - def.size.z) < 1e-4f;
            checks++;
            string v;
            if (MountOriented(def)) v = "n/a - oriented by mount face, not by R";
            else if (!yawMoved) { v = "!!R-DID-NOTHING"; failures++; }
            else if (meshMoved) v = "ok";
            else if (square) v = "ok (square in XZ - nothing to see)";
            else { v = "!!BOX-ROTATED-MESH-DID-NOT"; failures++; }
            W(string.Format("{0,-11} yaw {1}->{2}  mesh {3} -> {4}  {5}",
                def.id, y0, y1, m0.ToString("F3"), m1.ToString("F3"), v));
        }

        W(string.Format("\n## DONE checks={0} failures={1} offCamera={2}", checks, failures, offCamera));
        Phase0Input.debugPointer = false;
        bm.selected = -1;
        done = true;
    }

    static string Short(string r)
    {
        if (string.IsNullOrEmpty(r)) return "no-reason";
        if (r.StartsWith("No socket")) return "no-socket";
        if (r.StartsWith("Blocked")) return "blocked";
        if (r.StartsWith("Nothing attaches to a wheel")) return "not-on-wheel";
        if (r.StartsWith("Nothing attaches to a weapon")) return "not-on-weapon";
        if (r.StartsWith("Wheels attach")) return "wheel-side-only";
        if (r.StartsWith("Spinner")) return "no-underside";
        if (r.StartsWith("Too low")) return "too-low";
        return r.Length > 20 ? r.Substring(0, 20) : r;
    }

    IEnumerator LoadBase() { bm.LoadSnapshot(BASE); yield return null; }

    /// <summary>Swing the camera until the wanted face is genuinely the one the
    /// builder's own raycast reports, then leave the pointer on it. Returns
    /// false if no orbit angle exposes it (the underside never is).</summary>
    IEnumerator Reach(Vector3 dir, System.Action<bool> report)
    {
        var p = bm.placed[0];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        // Pitch matters as much as yaw: the underside is only reachable from
        // BELOW the horizon, which the builder could not do at all until the
        // camera stopped being pinned at 30 degrees.
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
