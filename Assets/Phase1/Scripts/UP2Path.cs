
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ROUND-2 CRITIC user-path sweep. New ground only: actuator mount axis by
// pointer, WYSIWYG builder-vs-arena, R full cycle -> placed yaw, budget gate,
// destructive verbs, floor rule edges, and a complete weaponised robot built
// start to finish by pointer alone.
public class UP2Path : MonoBehaviour
{
    public string outFile = "qa_up2_path.txt";
    BuilderManager bm;
    string path;
    public bool done;
    public int checks, failures;

    const string BARE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";
    // proven skeleton
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
    void CK(bool ok, string label)
    {
        checks++; if (!ok) failures++;
        W((ok ? "  PASS  " : "  !!FAIL ") + label);
    }

    int Idx(string id)
    {
        var pal = P1PartDef.Palette();
        for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i;
        return -1;
    }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC pointer sweep\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no bm"); done = true; yield break; }
        Phase0Input.debugPointer = true;
        yield return null;

        yield return SecA();
        yield return SecR();
        yield return SecBudget();
        yield return SecVerbs();
        yield return SecFloor();
        yield return SecFull();

        W(string.Format("\n## DONE checks={0} failures={1}", checks, failures));
        Phase0Input.debugPointer = false;
        bm.Deselect();
        done = true;
    }

    IEnumerator Load(string s) { bm.LoadSnapshot(s); yield return null; yield return null; }

    // Swing the camera until the builder's own raycast reports part `pi`'s face
    // `dir`, leaving the synthetic pointer on it. Verified hit, per rule 1.
    IEnumerator Reach(int pi, Vector3 dir, System.Action<bool> report)
    {
        if (pi < 0 || pi >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[pi];
        string wantId = p.def.id;
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
            if (bm.TestGhostTarget == wantId
                && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f
                && ReferenceEquals(TargetPart(), p)) { report(true); yield break; }
        }
        report(false);
    }

    BuilderManager.PlacedPart TargetPart()
    {
        // ghostTarget is exposed only by id; find the placed part whose face we
        // are actually snapped against by matching the ghost position.
        BuilderManager.PlacedPart best = null; float bd = float.MaxValue;
        foreach (var q in bm.placed)
        {
            float d = (q.pos - bm.TestGhostPos).sqrMagnitude;
            if (q.def.id == bm.TestGhostTarget && d < bd) { bd = d; best = q; }
        }
        return best;
    }

    // Place part `id` on face `dir` of placed[pi]. Returns index of new part or -1.
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

    // ------------------------------------------------- A. actuator mount axis
    IEnumerator SecA()
    {
        W("\n## A. ACTUATOR MOUNT AXIS RECORDED BY POINTER (fix from round 1)");
        Vector3[] dirs = { Vector3.up, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        string[] dn = { "+Y", "+Z", "-Z", "+X", "-X" };
        foreach (string act in new[] { "pivot", "spindle", "ram" })
        for (int d = 0; d < dirs.Length; d++)
        {
            yield return Load(SKEL);
            // chassis fore = placed[1]
            int ai = -1;
            yield return Place(act, 1, dirs[d], r => ai = r);
            if (ai < 0) { W(string.Format("{0,-8} {1}: not placed ({2})", act, dn[d], ai == -2 ? "off-cam" : bm.TestGhostReason)); continue; }
            Vector3 rec = bm.placed[ai].wheelAxis;
            bool axOk = Vector3.Dot(rec.normalized, dirs[d]) > 0.9f;
            // now bolt an arm PAST the actuator, perpendicular to the hinge
            int bi = -1;
            yield return Place("blade", ai, dirs[d], r => bi = r);
            string limbTxt = "no-arm(" + (bi == -2 ? "off-cam" : bm.TestGhostReason) + ")";
            if (bi >= 0)
            {
                var lr = bm.LimbReport();
                if (lr.Count == 0) limbTxt = "!!NO LIMB ENTRY";
                else limbTxt = string.Format("limb parts={0} blockedBy={1} tipR={2:F3} rateMax={3:F2}",
                        lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius, lr[0].rateMax);
            }
            CK(axOk, string.Format("{0,-8} {1}: wheelAxis={2}  {3}", act, dn[d], rec.ToString("F2"), limbTxt));
        }
    }

    // ------------------------------------------------- R full cycle
    IEnumerator SecR()
    {
        W("\n## B. R FULL CYCLE BEFORE PLACING, AND DOES THE PLACED PART KEEP IT");
        foreach (string id in new[] { "beam", "beamlong", "plate", "chassis", "bracket", "engine", "battery", "gyro" })
        {
            yield return Load(BARE);
            bm.selected = Idx(id);
            bool reached = false;
            yield return Reach(0, Vector3.up, r => reached = r);
            if (!reached) { W(id + ": top face off-cam"); continue; }
            var yaws = new List<int>();
            var meshes = new List<Vector3>();
            for (int k = 0; k < 8; k++)
            {
                yaws.Add(bm.TestGhostYaw);
                meshes.Add(bm.TestGhostMeshSize());
                Phase0Input.DebugRotate();
                yield return null; yield return null;
            }
            // distinct meshes over the cycle
            var dist = new List<string>();
            foreach (var m in meshes) { string k2 = m.ToString("F2"); if (!dist.Contains(k2)) dist.Add(k2); }
            string ystr = ""; foreach (int y in yaws) ystr += y + " ";
            // place at the CURRENT yaw and check the placed box matches the ghost
            int gy = bm.TestGhostYaw;
            Vector3 gh = Vector3.zero; bool valid = bm.TestGhostValid;
            int ni = -1;
            if (valid)
            {
                int before = bm.placed.Count;
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                if (bm.placed.Count > before) ni = bm.placed.Count - 1;
            }
            bool keep = ni >= 0 && bm.placed[ni].yaw == gy;
            if (ni >= 0) gh = bm.placed[ni].Half() * 2f;
            CK(ni >= 0 && keep, string.Format("{0,-9} yaws[{1}] distinctMesh={2} placedYaw={3} box={4}",
                id, ystr.Trim(), dist.Count, ni >= 0 ? bm.placed[ni].yaw.ToString() : "n/a", gh.ToString("F2")));
        }
    }

    // ------------------------------------------------- budget gate
    IEnumerator SecBudget()
    {
        W("\n## C. CREDIT BUDGET IN THE POINTER PATH");
        yield return Load(SKEL);
        bm.activeMat = "Tungsten";
        int cost0 = bm.BuildCost();
        int n = 0; int over = 0; string firstRefusal = null;
        // stack plates onto the top of both chassis and the core repeatedly
        for (int k = 0; k < 14; k++)
        {
            int host = bm.placed.Count - 1;
            // find a structural host with a free top
            int hi = -1;
            for (int i = bm.placed.Count - 1; i >= 0; i--)
                if (bm.placed[i].def.category == P1Category.Structural
                    || bm.placed[i].def.category == P1Category.Control) { hi = i; break; }
            int ni = -1;
            yield return Place("plate", hi, Vector3.up, r => ni = r);
            if (ni < 0) { if (firstRefusal == null) firstRefusal = (ni == -2 ? "off-cam" : bm.TestGhostReason); break; }
            n++;
            if (bm.BuildCost() > BuilderManager.CREDIT_BUDGET && over == 0) over = n;
        }
        int cost1 = bm.BuildCost();
        string v = bm.Validate();
        W(string.Format("  cost {0} -> {1} cr (budget {2}); {3} tungsten plates clicked in; over-budget after {4}; ghost refusal='{5}'",
            cost0, cost1, BuilderManager.CREDIT_BUDGET, n, over == 0 ? "never" : over.ToString(), firstRefusal ?? "-"));
        W("  Validate() after = " + (v ?? "null"));
        CK(!(cost1 > BuilderManager.CREDIT_BUDGET && firstRefusal == null),
           "ghost refuses placements that blow the credit budget");
        bm.activeMat = "Aluminum";
    }

    // ------------------------------------------------- destructive verbs
    IEnumerator SecVerbs()
    {
        W("\n## D. ESC / PANEL / EMPTY / INVALID / REMOVE / REPAINT");
        yield return Load(SKEL);
        bm.selected = Idx("bracket");
        bool r0 = false; yield return Reach(1, Vector3.up, x => r0 = x);
        bool shownBefore = bm.TestGhostShown;
        Phase0Input.DebugEsc();
        yield return null; yield return null;
        CK(bm.selected == -1 && !bm.TestGhostShown, "Esc clears selection and ghost (shown " + shownBefore + " -> " + bm.TestGhostShown + ", selected=" + bm.selected + ")");

        // panel click
        bm.selected = Idx("bracket");
        yield return Reach(1, Vector3.up, x => r0 = x);
        int pc = bm.placed.Count;
        Phase0Input.debugMousePos = new Vector3(120f, 300f, 0f);
        yield return null; yield return null;
        Phase0Input.DebugClick();
        yield return null; yield return null;
        CK(bm.placed.Count == pc, "click inside the palette panel places nothing (" + pc + "->" + bm.placed.Count + ")");

        // empty space click
        Phase0Input.debugMousePos = new Vector3(Screen.width - 12f, Screen.height - 12f, 0f);
        yield return null; yield return null;
        Phase0Input.DebugClick();
        yield return null; yield return null;
        CK(bm.placed.Count == pc, "click on empty space places nothing (" + pc + "->" + bm.placed.Count + ")");

        // right-click a wheel (leaf) -> should remove; right-click the core -> refused
        bm.Deselect();
        yield return null;
        int wi = -1;
        for (int i = 0; i < bm.placed.Count; i++) if (bm.placed[i].def.category == P1Category.Mobility) { wi = i; break; }
        bool rr = false; yield return Reach(wi, Vector3.right, x => rr = x);
        if (!rr) yield return Reach(wi, Vector3.left, x => rr = x);
        int b4 = bm.placed.Count;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        CK(bm.placed.Count == b4 - 1, "right-click removes a leaf wheel (" + b4 + "->" + bm.placed.Count + ")");

        bool rc = false; yield return Reach(0, Vector3.up, x => rc = x);
        b4 = bm.placed.Count;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        CK(bm.placed.Count == b4, "right-click on the CORE is refused (" + b4 + "->" + bm.placed.Count + ")");

        // interior part: the fore chassis has wheels + engine on it
        yield return Load(SKEL);
        bool ri = false; yield return Reach(1, Vector3.up, x => ri = x);
        b4 = bm.placed.Count;
        int wheelsBefore = 0; foreach (var q in bm.placed) if (q.def.category == P1Category.Mobility) wheelsBefore++;
        Phase0Input.DebugClick(1);
        yield return null; yield return null;
        int wheelsAfter = 0; foreach (var q in bm.placed) if (q.def.category == P1Category.Mobility) wheelsAfter++;
        string vv = bm.Validate();
        W(string.Format("  right-click the LOAD-BEARING fore chassis: parts {0}->{1}, wheels {2}->{3}, Validate()={4}",
            b4, bm.placed.Count, wheelsBefore, wheelsAfter, vv ?? "null"));
        CK(bm.placed.Count == b4 || vv == null,
           "removing an interior part never leaves an invalid/orphaned build");

        // repaint
        yield return Load(SKEL);
        bm.Deselect(); yield return null;
        bm.activeMat = "Titanium";
        bool rp = false; yield return Reach(2, Vector3.up, x => rp = x);
        string m0 = rp ? bm.placed[2].MatName() : "?";
        Phase0Input.DebugClick(2);
        yield return null; yield return null;
        string m1 = rp ? bm.placed[2].MatName() : "?";
        CK(rp && m1 == "Titanium", "middle-click repaint aft chassis " + m0 + " -> " + m1);
        bm.activeMat = "Aluminum";

        // material of a POINTER-PLACED part follows activeMat
        yield return Load(SKEL);
        bm.activeMat = "Tungsten";
        int ni2 = -1; yield return Place("bracket", 2, Vector3.up, r => ni2 = r);
        CK(ni2 >= 0 && bm.placed[ni2].MatName() == "Tungsten",
           "pointer-placed part takes the active material (" + (ni2 >= 0 ? bm.placed[ni2].MatName() : "not placed") + ")");
        bm.activeMat = "Aluminum";
    }

    // ------------------------------------------------- floor rule edges
    IEnumerator SecFloor()
    {
        W("\n## E. FLOOR RULE (round-1 FIX B) - is it too strict?");
        yield return Load(SKEL);
        // deck (wheel bottom) = 0.700 - 0.180 = 0.520
        // a wedge on the FRONT face of the fore chassis: classic ground scraper
        int ni = -1;
        yield return Place("wedge", 1, Vector3.forward, r => ni = r);
        W("  wedge on fore chassis +Z: " + (ni >= 0 ? "placed at y=" + bm.placed[ni].pos.y.ToString("F3")
              + " bottom=" + (bm.placed[ni].pos.y - bm.placed[ni].Half().y).ToString("F3")
            : "REFUSED reason='" + (ni == -2 ? "off-cam" : bm.TestGhostReason) + "'"));
        CK(ni >= 0, "a front wedge (the archetypal ground scraper) can be placed by pointer");

        // plate hung under the aft chassis - should be refused (below wheel line)
        yield return Load(SKEL);
        bm.selected = Idx("plate");
        bool r2 = false; yield return Reach(2, Vector3.down, x => r2 = x);
        W("  plate under aft chassis: reached=" + r2 + " valid=" + bm.TestGhostValid
          + " reason='" + bm.TestGhostReason + "' ghostBottom="
          + (r2 ? (bm.TestGhostPos.y - 0.03f).ToString("F3") : "-"));
        CK(!r2 || !bm.TestGhostValid, "a plate hung below the wheel line is refused");

        // and the UNDERSIDE is reachable at all (round-1 camera fix)
        CK(r2, "the underside of a part is reachable by the builder camera");
    }

    // ------------------------------------------------- full robot by pointer
    IEnumerator SecFull()
    {
        W("\n## F. COMPLETE WEAPONISED ROBOT, POINTER ONLY, FROM A BARE CORE");
        yield return Load(BARE);
        bm.activeMat = "Aluminum";
        var log = new List<string>();
        int step = 0;
        int fore = -1, aft = -1;
        yield return Place("chassis", 0, Vector3.forward, r => fore = r); log.Add("chassis+Z=" + fore);
        yield return Place("chassis", 0, Vector3.back, r => aft = r);   log.Add("chassis-Z=" + aft);
        if (fore < 0 || aft < 0) { W("  ABORT: chassis failed " + string.Join(",", log.ToArray())); CK(false, "pointer robot"); yield break; }
        int w = 0;
        foreach (var pair in new[] { new object[]{fore, Vector3.right}, new object[]{fore, Vector3.left},
                                     new object[]{aft, Vector3.right}, new object[]{aft, Vector3.left} })
        {
            int wi = -1;
            yield return Place("wheel", (int)pair[0], (Vector3)pair[1], r => wi = r);
            if (wi >= 0) w++; else log.Add("wheelFAIL(" + bm.TestGhostReason + ")");
        }
        log.Add("wheels=" + w);
        int bat = -1, eng = -1;
        yield return Place("battery", 0, Vector3.up, r => bat = r); log.Add("battery=" + bat);
        yield return Place("engine", fore, Vector3.up, r => eng = r); log.Add("engine=" + eng);
        int piv = -1, bl = -1;
        yield return Place("pivot", aft, Vector3.up, r => piv = r); log.Add("pivot=" + piv);
        if (piv >= 0) { yield return Place("blade", piv, Vector3.up, r => bl = r); log.Add("blade=" + bl); }
        W("  steps: " + string.Join(" ", log.ToArray()));
        string v = bm.Validate();
        var lr = bm.LimbReport();
        string limb = lr.Count > 0 ? string.Format("parts={0} blockedBy={1} tipR={2:F3} rateMax={3:F2} kJ/swing={4:F2}",
            lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius, lr[0].rateMax, lr[0].kjPerSwing) : "NONE";
        W(string.Format("  parts={0} cost={1} Validate={2} limb: {3}", bm.placed.Count, bm.BuildCost(), v ?? "null", limb));
        CK(v == null && bm.placed.Count >= 10, "complete robot built by pointer alone is valid");
        CK(lr.Count > 0 && lr[0].blockedBy == null && lr[0].parts > 0, "its weapon limb is genuinely driven");
        if (v == null)
        {
            int wheels = 0; foreach (var q in bm.placed) if (q.def.category == P1Category.Mobility) wheels++;
            if (bm.placed.Count >= 5 && wheels >= 2)
            {
                File.WriteAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"), "qa_up2_pointerbot.txt"),
                                  bm.SnapshotString());
                W("  saved -> Assets/Phase1/qa_up2_pointerbot.txt");
            }
        }
    }
}
}
