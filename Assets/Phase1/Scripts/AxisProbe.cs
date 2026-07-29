using System.Collections;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ACTUATOR DRIVE-AXIS PROBE (2026-07-28).
///
/// owen: "The R button for spindle still doesn't work."
///
/// The previous verification of the drive-axis feature went through
/// LoadSnapshot, i.e. it proved that a FILE containing yaw=90 wires up. It never
/// pressed R. That is the exact bypass this project has been burned by before -
/// "my testing went through LoadSnapshot -> AddPart, bypassing the entire ghost
/// pipeline". This probe drives the REAL path: select in the palette, aim the
/// pointer at a face, press R through Phase0Input, read the ghost, click, and
/// read what actually landed.
///
/// It reports the ghost's RENDERED bounds at every R state as well as the
/// logical yaw, because "R does nothing" and "R does something invisible" are
/// different bugs with different fixes and the player cannot tell them apart.
/// </summary>
public class AxisProbe : MonoBehaviour
{
    public string outFile = "qa_axisprobe.txt";
    public bool done;
    BuilderManager bm;
    string path;

    const string BASE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# ACTUATOR DRIVE-AXIS PROBE - the REAL R key path\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }

        Phase0Input.debugPointer = true;
        yield return null;

        var pal = P1PartDef.Palette();
        string[] fn = { "+X", "+Y", "+Z" };
        Vector3[] fd = { Vector3.right, Vector3.up, Vector3.forward };

        foreach (string wanted in new[] { "spindle", "pivot", "ram" })
        {
            int pi = -1;
            for (int i = 0; i < pal.Length; i++) if (pal[i].id == wanted) pi = i;
            if (pi < 0) { W("no such part " + wanted); continue; }

            for (int f = 0; f < fd.Length; f++)
            {
                bm.LoadSnapshot(BASE);
                yield return null;
                bm.selected = pi;
                yield return null;

                bool reached = false;
                yield return Reach(fd[f], r => reached = r);
                if (!reached) { W(wanted + " " + fn[f] + ": OFF-CAMERA (fixture, not a bug)"); continue; }

                W("");
                W("== " + wanted + " on the " + fn[f] + " face, pressing R five times ==");
                for (int k = 0; k <= 5; k++)
                {
                    // read BEFORE pressing, so state 0 is the untouched default
                    Vector3 mesh = bm.TestGhostMeshSize();
                    var probe = new BuilderManager.PlacedPart {
                        def = pal[pi], yaw = bm.TestGhostYaw, wheelAxis = bm.TestGhostNormal };
                    W(string.Format(
                        "  press#{0} ghostYaw={1,3} driveAxis={2} '{3}'  ghostMesh=({4:F3},{5:F3},{6:F3}) valid={7}",
                        k, bm.TestGhostYaw, probe.DriveAxis(), probe.DriveAxisLabel(),
                        mesh.x, mesh.y, mesh.z, bm.TestGhostValid));
                    if (k == 5) break;
                    Phase0Input.DebugRotate();
                    yield return null; yield return null;
                }

                // and does the click PERSIST it?
                int before = bm.placed.Count;
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                if (bm.placed.Count > before)
                {
                    var p = bm.placed[bm.placed.Count - 1];
                    W(string.Format("  PLACED yaw={0} driveAxis={1} '{2}' specId={3}",
                        p.yaw, p.DriveAxis(), p.DriveAxisLabel(),
                        BuilderManager.VisualId(p.def, p.DriveAxis())));
                }
                else W("  PLACED nothing (ghost was invalid at the final R state)");
            }
        }

        Phase0Input.debugPointer = false;
        W("\n# done");
        done = true;
        Debug.Log("AxisProbe: done -> " + outFile);
    }

    IEnumerator Reach(Vector3 dir, System.Action<bool> report)
    {
        var p = bm.placed[0];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y > 0.5f ? new[] { 55f, 30f } : new[] { 20f, 30f, 0f };
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
