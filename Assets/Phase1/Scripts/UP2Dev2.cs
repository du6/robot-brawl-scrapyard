using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-2 IMPL, part 2: the ARC-CLEARANCE verification (FIX D).
///
/// Builds the round-2 critic's exact rig - a pivot+blade hammer on a VERTICAL
/// chassis face - BY POINTER, reads the builder's new prediction, then drives
/// the same machine in TEST DRIVE with the trigger held and measures the arc
/// the arena actually delivers. A prediction that does not match the thing it
/// predicts is worth nothing, so both numbers land in the same file.
///
/// The ROOF mount is the positive control: the critic measured it reaching the
/// full arc, so FIX D must NOT warn about it.
/// </summary>
public class UP2Dev2 : MonoBehaviour
{
    public string section = "arc";
    public string outFile = "qa_up2_measure.txt";
    public bool done;
    public int checks, failures;

    BuilderManager bm;
    string path;

    // Proven skeleton, minus anything that would get in the pointer's way.
    const string RIG =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n" +
        "engine|0.000,0.975,-0.400|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    static int PalIdx(string id)
    {
        var pal = P1PartDef.Palette();
        for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i;
        return -1;
    }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        W("\n==================== SECTION " + section + " ====================");
        Phase0Input.debugPointer = true;
        yield return null;

        if (section == "arc") yield return Arc();
        else W("unknown section");

        W(string.Format("## SECTION {0} DONE checks={1} failures={2}", section, checks, failures));
        Phase0Input.debugPointer = false;
        Phase0Input.debugFire = false;
        bm.selected = -1;
        done = true;
    }

    IEnumerator Arc()
    {
        // ---- rig 1: hammer on the FRONT (vertical +Z) chassis face -------
        yield return BuildHammer(Vector3.forward, "VERTICAL front face");
        yield return MeasureArena("vertical");
        // ---- rig 2: positive control, hammer on the ROOF -----------------
        yield return BuildHammer(Vector3.up, "ROOF (+Y) face");
        yield return MeasureArena("roof");
    }

    /// <summary>Pointer-build pivot on `face` of the front chassis, then a
    /// blade on the pivot's +Y face so the arm runs PERPENDICULAR to the hinge
    /// axis (bolting it along the axis gives a zero-radius limb that swings
    /// nothing).</summary>
    IEnumerator BuildHammer(Vector3 face, string label)
    {
        W("\n--- rig: pivot on the " + label + " of the front chassis, blade on the pivot ---");
        bm.LoadSnapshot(RIG);
        yield return null; yield return null;
        bm.activeMat = "Steel";

        bm.selected = PalIdx("pivot");
        bool reached = false;
        yield return ReachPart(1, face, r => reached = r);
        if (!reached) { W("  pivot face off-cam - ABORT"); failures++; yield break; }
        if (!bm.TestGhostValid) { W("  pivot REFUSED: " + bm.TestGhostReason); failures++; yield break; }
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        int pivotIdx = bm.placed.Count - 1;
        W(string.Format("  pivot placed by pointer at {0} mountNormal {1}",
            bm.placed[pivotIdx].pos.ToString("F3"), bm.placed[pivotIdx].wheelAxis.ToString("F2")));

        // Arm face: perpendicular to the hinge axis.
        Vector3 armFace = Mathf.Abs(face.y) > 0.5f ? Vector3.forward : Vector3.up;
        bm.selected = PalIdx("blade");
        reached = false;
        yield return ReachPart(pivotIdx, armFace, r => reached = r);
        if (!reached) { W("  blade face off-cam - ABORT"); failures++; yield break; }
        if (!bm.TestGhostValid) { W("  blade REFUSED: " + bm.TestGhostReason); failures++; yield break; }
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        W("  blade placed by pointer at " + bm.placed[bm.placed.Count - 1].pos.ToString("F3"));

        W("  parts=" + bm.placed.Count + "  Validate()=" + (bm.Validate() ?? "OK")
          + "  cost=" + bm.BuildCost());
        foreach (var li in bm.LimbReport())
            W(string.Format("  BUILDER SAYS: {0} parts={1} blockedBy={2} tipR={3:F3} rateMax={4:F2}"
                          + "  arcFree={5:F3} of {6:F3} = {7:F0}%",
                li.act.def.id, li.parts, li.blockedBy ?? "null", li.tipRadius, li.rateMax,
                li.arcFree, li.arcLimit, 100f * li.arcFrac));
    }

    /// <summary>Drive the same build in TEST DRIVE with the trigger held and
    /// record the largest travel the actuator actually reaches.</summary>
    IEnumerator MeasureArena(string tag)
    {
        if (bm.Validate() != null) { W("  arena skipped: " + bm.Validate()); yield break; }
        bm.selected = -1;
        Phase0Input.debugPointer = false;
        bm.StartTest();
        yield return null; yield return null;
        var acts = new List<Actuator>();
        if (bm.testRobot != null)
            foreach (var a in bm.testRobot.GetComponentsInChildren<Actuator>(true)) acts.Add(a);
        if (acts.Count == 0) { W("  arena: NO ACTUATOR WIRED"); failures++; yield break; }

        yield return new WaitForSeconds(1.5f);   // spawn settle
        Phase0Input.debugFire = true;
        float maxTravel = 0f, maxRate = 0f;
        int cycles = 0;
        float prev = 0f;
        float t0 = Time.time;
        while (Time.time - t0 < 10f)
        {
            foreach (var a in acts)
            {
                if (a.travel > maxTravel) maxTravel = a.travel;
                if (a.rate > maxRate) maxRate = a.rate;
                if (prev > 0.02f && a.travel <= 0.0001f) cycles++;
                prev = a.travel;
            }
            yield return null;
        }
        Phase0Input.debugFire = false;
        float limit = Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad;
        checks++;
        W(string.Format("  ARENA DELIVERS ({0}): maxTravel {1:F3} rad of {2:F3} = {3:F0}%  maxRate {4:F2}  cycles {5}",
            tag, maxTravel, limit, 100f * maxTravel / limit, maxRate, cycles));
        bm.BackToBuild();
        yield return null; yield return null;
        Phase0Input.debugPointer = true;
    }

    IEnumerator ReachPart(int idx, Vector3 dir, System.Action<bool> report)
    {
        if (idx < 0 || idx >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[idx];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        string wantId = p.def.id;
        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f }
                        : dir.y > 0.5f  ? new[] { 55f, 30f, 70f }
                                        : new[] { 20f, 30f, 0f, 45f };
        foreach (float pitch in pitches)
        foreach (float yaw in new[] { 0f, 35f, 125f, 215f, 305f, 180f })
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
