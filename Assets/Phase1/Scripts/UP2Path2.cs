
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// UP2 pass 2. Fixes two fixture mistakes in my own pass 1:
//  * section A used the proven skeleton, whose fore chassis has an engine on
//    top and wheels on both sides, so five of six faces were legitimately
//    occupied and read as "off-cam". Now an ISOLATED host.
//  * the right/middle-click checks called Deselect() first, which stops the
//    ghost raycast, so Reach() could not verify the hit and the click landed
//    on whatever the last probe left under the pointer. Now the selection is
//    kept so the hit is verified, then the destructive button is pressed.
public class UP2Path2 : MonoBehaviour
{
    public string outFile = "qa_up2_path2.txt";
    BuilderManager bm;
    string path;
    public bool done;
    public int checks, failures;

    const string BARE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";
    // isolated host: core + ONE chassis out on +X, wheels kept on the core so
    // every face of the chassis except -X is free.
    const string ISO =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.350,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.000,0.700,0.220|0|0.00,0.00,1.00|Rubber\n" +
        "wheel|0.000,0.700,-0.220|0|0.00,0.00,-1.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n";
    const string SKEL =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n" +
        "engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }
    void CK(bool ok, string label) { checks++; if (!ok) failures++; W((ok ? "  PASS  " : "  !!FAIL ") + label); }

    int Idx(string id)
    { var pal = P1PartDef.Palette(); for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC pointer sweep, pass 2\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        Phase0Input.debugPointer = true;
        yield return null;

        yield return SecA();
        yield return SecVerbs();
        yield return SecBudget();
        yield return SecPlate();
        yield return SecWys();

        W(string.Format("\n## DONE checks={0} failures={1}", checks, failures));
        Phase0Input.debugPointer = false;
        bm.Deselect();
        done = true;
    }

    IEnumerator Load(string s) { bm.LoadSnapshot(s); yield return null; yield return null; }

    IEnumerator Reach(int pi, Vector3 dir, System.Action<bool> report)
    {
        if (pi < 0 || pi >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[pi];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f, -30f }
                        : dir.y > 0.5f ? new[] { 55f, 30f, 70f }
                                       : new[] { 20f, 30f, 0f, -10f };
        foreach (float pitch in pitches)
        foreach (float yaw in new[] { 35f, 125f, 215f, 305f, 80f, 170f, 260f, 350f })
        {
            bm.TestOrbitYaw = yaw; bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report(false); yield break; }
            Phase0Input.debugMousePos = cam.WorldToScreenPoint(world);
            yield return null; yield return null;
            if (bm.TestGhostTarget == p.def.id && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f
                && (bm.TestGhostPos - p.pos).magnitude < 1.4f) { report(true); yield break; }
        }
        report(false);
    }

    IEnumerator Place(string id, int pi, Vector3 dir, System.Action<int> report)
    {
        bm.selected = Idx(id);
        bool reached = false;
        yield return Reach(pi, dir, r => reached = r);
        if (!reached) { report(-2); yield break; }
        if (!bm.TestGhostValid) { report(-1); yield break; }
        int before = bm.placed.Count;
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        report(bm.placed.Count > before ? bm.placed.Count - 1 : -1);
    }

    // ---------------------------------------- A. actuator axis, ISOLATED host
    IEnumerator SecA()
    {
        W("\n## A2. ACTUATOR MOUNT AXIS BY POINTER, on an isolated chassis (5 free faces)");
        W("   host = chassis at (0.35,0.70,0.00); -X is the seam to the core, the rest are free");
        Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right };
        string[] dn = { "+Y", "-Y", "+Z", "-Z", "+X" };
        foreach (string act in new[] { "pivot", "spindle", "ram" })
        for (int d = 0; d < dirs.Length; d++)
        {
            yield return Load(ISO);
            int ai = -1;
            yield return Place(act, 1, dirs[d], r => ai = r);
            if (ai < 0)
            { W(string.Format("{0,-8} {1}: NOT PLACED ({2})", act, dn[d], ai == -2 ? "off-cam" : bm.TestGhostReason)); continue; }
            Vector3 rec = bm.placed[ai].wheelAxis;
            bool axOk = Vector3.Dot(rec.normalized, dirs[d]) > 0.9f;
            int bi = -1;
            yield return Place("blade", ai, dirs[d], r => bi = r);
            string limbTxt = "arm NOT placed (" + (bi == -2 ? "off-cam" : bm.TestGhostReason) + ")";
            if (bi >= 0)
            {
                var lr = bm.LimbReport();
                limbTxt = lr.Count == 0 ? "!!NO LIMB ENTRY"
                    : string.Format("limb parts={0} blockedBy={1} tipR={2:F3} rateMax={3:F2}",
                        lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius, lr[0].rateMax);
            }
            bool radiusOk = bi < 0 || bm.LimbReport().Count == 0 || bm.LimbReport()[0].tipRadius > 0.05f;
            CK(axOk && radiusOk, string.Format("{0,-8} {1}: wheelAxis={2}  {3}", act, dn[d], rec.ToString("F2"), limbTxt));
        }
    }

    // ---------------------------------------- D2. destructive verbs, hit-verified
    IEnumerator SecVerbs()
    {
        W("\n## D2. RIGHT-CLICK / MIDDLE-CLICK, with the hit VERIFIED first");
        // Keep a part selected so the ghost raycast runs and Reach can confirm
        // which face the pointer is genuinely on; button 1/2 are independent of it.
        yield return Load(SKEL);
        bm.selected = Idx("bracket");
        bool rc = false;
        yield return Reach(0, Vector3.up, x => rc = x);     // core top is under the battery
        if (!rc) yield return Reach(0, Vector3.right, x => rc = x);
        if (!rc) yield return Reach(0, Vector3.forward, x => rc = x);
        int b4 = bm.placed.Count;
        string tgt = bm.TestGhostTarget;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        CK(rc && bm.placed.Count == b4,
           "right-click on the CORE refused (verified target=" + tgt + ", parts " + b4 + "->" + bm.placed.Count + ")");

        // leaf wheel
        yield return Load(SKEL);
        bm.selected = Idx("bracket");
        bool rw = false;
        yield return Reach(3, Vector3.right, x => rw = x);
        b4 = bm.placed.Count; tgt = bm.TestGhostTarget;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        CK(rw && bm.placed.Count == b4 - 1,
           "right-click removes a leaf wheel (verified target=" + tgt + ", " + b4 + "->" + bm.placed.Count + ")");

        // repaint
        yield return Load(SKEL);
        bm.selected = Idx("bracket");
        bm.activeMat = "Titanium";
        bool rp = false;
        yield return Reach(2, Vector3.back, x => rp = x);
        string m0 = bm.placed[2].MatName(); tgt = bm.TestGhostTarget;
        int pc = bm.placed.Count;
        Phase0Input.DebugClick(2);
        yield return null; yield return null;
        string m1 = bm.placed[2].MatName();
        CK(rp && m1 == "Titanium" && bm.placed.Count == pc,
           "middle-click repaints the aft chassis (verified target=" + tgt + "): " + m0 + " -> " + m1);
        bm.activeMat = "Aluminum";
    }

    // ---------------------------------------- C2. budget, unobstructed stack
    IEnumerator SecBudget()
    {
        W("\n## C2. CREDIT BUDGET IN THE GHOST - tungsten plates stacked up a bare core");
        yield return Load(BARE);
        bm.activeMat = "Tungsten";
        int host = 0, n = 0, overAt = 0;
        string refusal = null;
        for (int k = 0; k < 12; k++)
        {
            int ni = -1;
            yield return Place("plate", host, Vector3.up, r => ni = r);
            if (ni < 0) { refusal = ni == -2 ? "off-cam" : bm.TestGhostReason; break; }
            host = ni; n++;
            if (overAt == 0 && bm.BuildCost() > BuilderManager.CREDIT_BUDGET) overAt = n;
        }
        int cost = bm.BuildCost();
        W(string.Format("  {0} tungsten plates clicked in, cost now {1} cr of {2}; first over budget at plate {3}; stopped by '{4}'",
            n, cost, BuilderManager.CREDIT_BUDGET, overAt == 0 ? "never" : overAt.ToString(), refusal ?? "loop end"));
        W("  Validate() = " + (bm.Validate() ?? "null"));
        CK(!(cost > BuilderManager.CREDIT_BUDGET && (refusal == null || !refusal.Contains("udget"))),
           "the ghost refuses a click that puts the build over budget");
        bm.activeMat = "Aluminum";
    }

    // ---------------------------------------- plate R-then-place reason
    IEnumerator SecPlate()
    {
        W("\n## B2. WHY A ROTATED PLATE WOULD NOT PLACE");
        yield return Load(BARE);
        bm.selected = Idx("plate");
        bool r = false;
        yield return Reach(0, Vector3.up, x => r = x);
        if (!r) { W("  top face off-cam"); yield break; }
        for (int k = 0; k < 4; k++)
        {
            W(string.Format("  yaw={0,3} half={1} valid={2} reason='{3}'",
                bm.TestGhostYaw, (bm.TestGhostMeshSize() * 0.5f).ToString("F3"),
                bm.TestGhostValid, bm.TestGhostReason));
            Phase0Input.DebugRotate();
            yield return null; yield return null;
        }
    }

    // ---------------------------------------- WYSIWYG builder vs arena
    IEnumerator SecWys()
    {
        W("\n## G. WYSIWYG: does the arena draw a weapon the way the builder drew it?");
        W("   builder box = PlacedPart.Half()*2; arena AABB = the spec SpawnBot sends.");
        foreach (string wid in new[] { "blade", "wedge", "hook", "spike", "spinner" })
        foreach (var d in new[] { Vector3.forward, Vector3.right, Vector3.up })
        {
            yield return Load(SKEL);
            int ni = -1;
            // aft chassis: +Z is the core, so use -Z for "forward-ish"
            int hostIdx = 2;
            Vector3 dir = d == Vector3.forward ? Vector3.back : d;
            yield return Place(wid, hostIdx, dir, r => ni = r);
            if (ni < 0) { W(string.Format("{0,-11}{1}: not placed ({2})", wid, dir.ToString("F0"), ni == -2 ? "off-cam" : bm.TestGhostReason)); continue; }
            var p = bm.placed[ni];
            Vector3 box = p.Half() * 2f;
            // rendered bounds of the builder's own visual
            Bounds b = new Bounds(p.pos, Vector3.zero); bool got = false;
            foreach (var rr in p.go.GetComponentsInChildren<Renderer>(true))
            { if (!got) { b = rr.bounds; got = true; } else b.Encapsulate(rr.bounds); }
            bool agree = !got || (b.size - box).magnitude < 0.09f;
            CK(agree, string.Format("{0,-11}on {1}: axis={2} collisionBox={3} drawnMesh={4}",
                wid, dir.ToString("F0"), p.wheelAxis.ToString("F0"), box.ToString("F3"), got ? b.size.ToString("F3") : "none"));
        }
    }
}
}
