using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-1 CRITIC (user-path). UserPathTest covered "every part onto a bare
/// core". This covers what it did NOT: part-onto-part, multi-part stacks, the
/// full R cycle at every yaw, Esc, mis-clicks, panel clicks, empty space, and
/// building a complete legal robot start-to-finish by pointer alone.
/// Same two honesty rules: verify the hit, isolate the fixture.
/// </summary>
public class UP1Sweep : MonoBehaviour
{
    public string outFile = "qa_up1_sweep.txt";
    public bool done;
    public int checks, failures;
    public string builtSnapshot = "";

    BuilderManager bm;
    P1PartDef[] pal;
    string path;

    const float PANEL = 280f;
    const string CORE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }
    void OK(string s)  { checks++; W("   ok    " + s); }
    void BAD(string s) { checks++; failures++; W("   FAIL  " + s); }

    int Idx(string id) { for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP1 SWEEP - pointer path beyond the bare core\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        pal = P1PartDef.Palette();
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        Phase0Input.debugPointer = true;
        yield return null;
        W("screen " + Screen.width + "x" + Screen.height + "  palette=" + pal.Length);

        yield return SecA();
        yield return SecB();
        yield return SecC();
        yield return SecD();
        yield return SecE();
        yield return SecF();

        W(string.Format("\n## DONE checks={0} failures={1}", checks, failures));
        Phase0Input.debugPointer = false;
        bm.selected = -1;
        bm.Deselect();
        done = true;
    }

    // ---------------------------------------------------------------- A
    // Part ONTO PART. Fixture: core + one beam on +X (loaded), then probe
    // every palette part onto the BEAM's four free faces by pointer.
    IEnumerator SecA()
    {
        W("\n## A. PART-ONTO-PART: attach each part to a BEAM that is itself on the core");
        W("   fixture: core(0,.70,0) + beam(+X, centre .55) ; beam free faces +X +Y -Y +Z -Z");
        string fix = CORE + "beam|0.550,0.700,0.000|90|0.00,0.00,0.00|Aluminum\n";
        string[] fn = { "+X", "+Y", "-Y", "+Z", "-Z" };
        Vector3[] fd = { Vector3.right, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int pi = 0; pi < pal.Length; pi++)
        {
            if (pal[pi].id == "core") continue;
            string row = string.Format("{0,-11}", pal[pi].id);
            for (int f = 0; f < fd.Length; f++)
            {
                bm.LoadSnapshot(fix); yield return null;
                bm.selected = pi;
                bool got = false;
                yield return Reach(1, fd[f], r => got = r);
                if (!got) { row += "  " + fn[f] + ":off-cam"; continue; }
                bool valid = bm.TestGhostValid; string why = bm.TestGhostReason;
                int before = bm.placed.Count;
                if (valid) { Phase0Input.DebugClick(); yield return null; yield return null; yield return null; }
                bool landed = bm.placed.Count > before;
                checks++;
                if (valid && !landed) { failures++; row += "  " + fn[f] + ":!!GHOST-OK-NO-PLACE"; }
                else if (valid) row += "  " + fn[f] + ":ok";
                else row += "  " + fn[f] + ":X(" + Short(why) + ")";
            }
            W(row);
        }
    }

    // ---------------------------------------------------------------- B
    // MULTI-PART STACK, every placement by pointer. core -> chassis -> bracket
    // -> beam -> blade, four levels deep, checking each landed where the ghost
    // said it would and that the recorded yaw / mount axis survived.
    IEnumerator SecB()
    {
        W("\n## B. MULTI-PART STACK (4 deep), every step by pointer");
        bm.LoadSnapshot(CORE); yield return null;
        string[] chain = { "chassis", "bracket", "beam", "blade" };
        Vector3[] face  = { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        int target = 0;
        for (int s = 0; s < chain.Length; s++)
        {
            bm.selected = Idx(chain[s]);
            bool got = false;
            yield return Reach(target, face[s], r => got = r);
            if (!got) { BAD("stack " + chain[s] + " onto " + bm.placed[target].def.id + " +Z : face unreachable"); yield break; }
            if (!bm.TestGhostValid) { BAD("stack " + chain[s] + " onto " + bm.placed[target].def.id + " +Z : " + bm.TestGhostReason); yield break; }
            Vector3 want = bm.TestGhostPos; int wyaw = bm.TestGhostYaw; Vector3 wn = bm.TestGhostNormal;
            int before = bm.placed.Count;
            Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
            if (bm.placed.Count != before + 1) { BAD("stack " + chain[s] + ": ghost valid, click placed nothing"); yield break; }
            var np = bm.placed[bm.placed.Count - 1];
            if ((np.pos - want).sqrMagnitude > 1e-5f)
                BAD(string.Format("stack {0}: landed at {1} but ghost promised {2}", chain[s], np.pos.ToString("F3"), want.ToString("F3")));
            else OK(string.Format("stack L{0} {1} on {2} +Z -> {3} yaw{4}", s + 1, chain[s], bm.placed[target].def.id, np.pos.ToString("F3"), np.yaw));
            if (np.yaw != wyaw) BAD("stack " + chain[s] + ": recorded yaw " + np.yaw + " != ghost yaw " + wyaw);
            bool needAxis = pal[Idx(chain[s])].category == P1Category.Weapon || pal[Idx(chain[s])].category == P1Category.Mobility;
            if (needAxis)
            {
                if (Vector3.Dot(np.wheelAxis.normalized, wn.normalized) < 0.9f)
                    BAD("stack " + chain[s] + ": mount axis " + np.wheelAxis.ToString("F2") + " != face normal " + wn.ToString("F2"));
                else OK("stack " + chain[s] + " recorded mount axis " + np.wheelAxis.ToString("F2"));
            }
            target = bm.placed.Count - 1;
        }
        W("   stack snapshot: " + bm.SnapshotString().Replace("\n", " / "));
    }

    // ---------------------------------------------------------------- C
    // R at EVERY yaw. Structural parts cycle 0/90/180/270; everything else
    // toggles 0/90. Check the full cycle returns home and the drawn mesh
    // follows at each step, and that the yaw the ghost showed is the yaw the
    // placed part gets.
    IEnumerator SecC()
    {
        W("\n## C. ROTATE (R) THROUGH THE WHOLE CYCLE, and does the placed part keep it");
        for (int pi = 0; pi < pal.Length; pi++)
        {
            var def = pal[pi];
            if (def.id == "core") continue;
            bm.LoadSnapshot(CORE); yield return null;
            bm.selected = pi;
            bool got = false;
            foreach (var d in new[] { Vector3.up, Vector3.forward, Vector3.right, Vector3.back })
            { yield return Reach(0, d, r => got = r); if (got && bm.TestGhostValid) break; got = false; }
            if (!got) { W(string.Format("{0,-11} no valid+visible face - rotation untested", def.id)); continue; }

            bool structural = def.category == P1Category.Structural;
            int steps = structural ? 4 : 2;
            var yaws = new List<int>(); var meshes = new List<Vector3>();
            yaws.Add(bm.TestGhostYaw); meshes.Add(bm.TestGhostMeshSize());
            for (int k = 0; k < steps; k++)
            {
                Phase0Input.DebugRotate();
                yield return null; yield return null; yield return null;
                yaws.Add(bm.TestGhostYaw); meshes.Add(bm.TestGhostMeshSize());
            }
            string cyc = ""; foreach (int y in yaws) cyc += y + " ";
            bool wrapped = yaws[yaws.Count - 1] == yaws[0];
            int distinct = 0; var seen = new List<int>();
            foreach (int y in yaws) if (!seen.Contains(y)) { seen.Add(y); distinct++; }
            int distinctMesh = 0; var ms = new List<Vector3>();
            foreach (var m in meshes) { bool dup = false; foreach (var q in ms) if ((q - m).sqrMagnitude < 1e-5f) dup = true; if (!dup) { ms.Add(m); distinctMesh++; } }
            checks++;
            string verdict;
            if (!wrapped) { failures++; verdict = "!!CYCLE-DOES-NOT-WRAP"; }
            else if (structural && distinct != 4) { failures++; verdict = "!!STRUCTURAL-ONLY-" + distinct + "-STATES"; }
            else if (!structural && distinct != 2) { failures++; verdict = "!!EXPECTED-2-STATES-GOT-" + distinct; }
            else verdict = "ok  distinctMesh=" + distinctMesh;
            W(string.Format("{0,-11} R cycle: {1} ({2} states) mesh {3} -> {4}   {5}",
                def.id, cyc, distinct, meshes[0].ToString("F3"), meshes[1].ToString("F3"), verdict));

            // and does the yaw the ghost was showing survive the click?
            if (bm.TestGhostValid)
            {
                int gy = bm.TestGhostYaw; int before = bm.placed.Count;
                Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
                if (bm.placed.Count == before + 1)
                {
                    var np = bm.placed[bm.placed.Count - 1];
                    if (np.yaw != gy) BAD(def.id + ": ghost showed yaw " + gy + ", placed part recorded " + np.yaw);
                }
            }
        }
    }

    // ---------------------------------------------------------------- D
    IEnumerator SecD()
    {
        W("\n## D. ESC / DESELECT");
        bm.LoadSnapshot(CORE); yield return null;
        bm.selected = Idx("beam");
        bool got = false; yield return Reach(0, Vector3.up, r => got = r);
        if (!got) { W("   (could not park the pointer on the core top; Esc untested)"); yield break; }
        bool shownBefore = bm.TestGhostShown;
        Phase0Input.DebugEsc();
        yield return null; yield return null; yield return null;
        if (bm.selected == -1) OK("Esc cleared the selection (was " + Idx("beam") + ")"); else BAD("Esc left selected=" + bm.selected);
        if (!bm.TestGhostShown) OK("Esc removed the ghost (shown before=" + shownBefore + ")"); else BAD("Esc left a ghost on screen");
        // and a click after Esc must place nothing
        int before = bm.placed.Count;
        Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
        if (bm.placed.Count == before) OK("click after Esc places nothing"); else BAD("click after Esc placed a part");
    }

    // ---------------------------------------------------------------- E
    IEnumerator EParkOnCoreTop(System.Action<bool> r) { yield return Reach(0, Vector3.up, r); }

    IEnumerator SecE()
    {
        W("\n## E. MIS-CLICKS: panel, empty space, invalid ghost, right-click");
        bm.LoadSnapshot(CORE); yield return null;
        bm.selected = Idx("beam");
        var cam = bm.TestCam;

        // E1: pointer over the palette panel
        Phase0Input.debugMousePos = new Vector3(PANEL * 0.5f, Screen.height * 0.5f, 0f);
        yield return null; yield return null;
        int before = bm.placed.Count;
        bool ghostOverPanel = bm.TestGhostShown;
        Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
        if (bm.placed.Count == before) OK("click over the palette panel places nothing"); else BAD("click over the panel PLACED a part");
        if (!ghostOverPanel) OK("ghost hidden while the pointer is over the panel"); else BAD("ghost still drawn under the panel");

        // E2: empty space, far from the robot
        Phase0Input.debugMousePos = new Vector3(Screen.width - 6f, Screen.height - 6f, 0f);
        yield return null; yield return null;
        before = bm.placed.Count;
        string why = bm.TestGhostReason; bool v = bm.TestGhostValid;
        Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
        if (bm.placed.Count == before) OK("click on empty space places nothing (ghost valid=" + v + " reason=\"" + why + "\")");
        else BAD("click on EMPTY SPACE placed a part");
        if (!v) OK("empty-space ghost is invalid and says: \"" + why + "\""); else BAD("empty-space ghost reported VALID");

        // E3: right-click on empty space must not remove anything
        before = bm.placed.Count;
        // no synthetic right-click seam exists; note it and move on
        W("   note: Phase0Input exposes DebugClick for BUTTON 0 only - right-click (remove) and");
        W("         middle-click (repaint) have NO synthetic seam, so the two destructive mouse");
        W("         verbs in the builder remain untestable through the player path.");

        // E4: click while the ghost is invalid on a real face (wheel on core top)
        bm.selected = Idx("wheel");
        bool got = false; yield return EParkOnCoreTop(r => got = r);
        if (got)
        {
            before = bm.placed.Count;
            if (bm.TestGhostValid) BAD("wheel on the core's TOP face reported VALID (wheels are side-only)");
            else
            {
                Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
                if (bm.placed.Count == before) OK("click on an INVALID ghost (" + bm.TestGhostReason + ") places nothing");
                else BAD("click on an invalid ghost PLACED a part");
            }
        }

        // E5: rapid double click on one valid face - must place exactly ONE part
        bm.LoadSnapshot(CORE); yield return null;
        bm.selected = Idx("bracket");
        got = false; yield return EParkOnCoreTop(r => got = r);
        if (got && bm.TestGhostValid)
        {
            before = bm.placed.Count;
            Phase0Input.DebugClick();
            yield return null;
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            int added = bm.placed.Count - before;
            if (added == 2) OK("two clicks on one face stack two brackets (added=2) - legal, they snap onward");
            else if (added == 1) OK("second click on the same spot was refused (added=1)");
            else BAD("two clicks added " + added + " parts");
        }
    }

    // ---------------------------------------------------------------- F
    // THE WHOLE ROBOT, BY POINTER ALONE. If the builder works, this is the
    // thing a player actually does.
    IEnumerator SecF()
    {
        W("\n## F. COMPLETE ROBOT BUILT START-TO-FINISH BY POINTER");
        bm.LoadSnapshot(CORE); yield return null;
        bm.activeMat = "Aluminum";
        int placedOK = 0, placedFail = 0;

        // 0 core. 1,2 chassis fore/aft. 3..6 wheels. 7 battery. 8 engine.
        // 9 pivot on the aft chassis roof, 10 beam arm, 11 blade tip.
        int iChassis = Idx("chassis"), iWheel = Idx("wheel"), iBat = Idx("battery"),
            iEng = Idx("engine"), iPivot = Idx("pivot"), iBeam = Idx("beam"),
            iBlade = Idx("blade"), iGyro = Idx("gyro");

        int cFore = -1, cAft = -1;
        yield return Place(iChassis, 0, Vector3.forward, "chassis fore", r => { if (r >= 0) { cFore = r; placedOK++; } else placedFail++; });
        yield return Place(iChassis, 0, Vector3.back,    "chassis aft",  r => { if (r >= 0) { cAft = r; placedOK++; } else placedFail++; });
        if (cFore < 0 || cAft < 0) { BAD("cannot continue - chassis would not go on by pointer"); yield break; }

        foreach (var pair in new[] { new KeyValuePair<int, Vector3>(cFore, Vector3.right),
                                     new KeyValuePair<int, Vector3>(cFore, Vector3.left),
                                     new KeyValuePair<int, Vector3>(cAft,  Vector3.right),
                                     new KeyValuePair<int, Vector3>(cAft,  Vector3.left) })
        {
            yield return Place(iWheel, pair.Key, pair.Value, "wheel", r => { if (r >= 0) placedOK++; else placedFail++; });
        }
        yield return Place(iBat, 0, Vector3.up, "battery on core roof", r => { if (r >= 0) placedOK++; else placedFail++; });
        yield return Place(iEng, cFore, Vector3.up, "engine on fore chassis roof", r => { if (r >= 0) placedOK++; else placedFail++; });

        int pv = -1;
        yield return Place(iPivot, cAft, Vector3.up, "pivot on aft chassis roof", r => { if (r >= 0) { pv = r; placedOK++; } else placedFail++; });
        int arm = -1;
        if (pv >= 0) yield return Place(iBeam, pv, Vector3.back, "beam arm off the pivot", r => { if (r >= 0) { arm = r; placedOK++; } else placedFail++; });
        if (arm >= 0) yield return Place(iBlade, arm, Vector3.back, "blade on the arm tip", r => { if (r >= 0) placedOK++; else placedFail++; });

        W(string.Format("   pointer placements: {0} ok, {1} failed, parts now {2}", placedOK, placedFail, bm.placed.Count));
        string err = bm.Validate();
        checks++;
        if (err == null) W("   ok    Validate() == null : the pointer-built robot is LEGAL");
        else { failures++; W("   FAIL  Validate() says: " + err); }

        var lr = bm.LimbReport();
        foreach (var li in lr)
            W(string.Format("   limb {0}: parts={1} blockedBy={2} inertia={3:F3} tipR={4:F3} rateMax={5:F2} kJ/swing={6:F2}",
                li.act.def.id, li.parts, li.blockedBy == null ? "null" : li.blockedBy,
                li.inertia, li.tipRadius, li.rateMax, li.kjPerSwing));
        checks++;
        if (lr.Count == 1 && lr[0].blockedBy == null && lr[0].parts > 0)
            W("   ok    the pointer-built hammer is a REAL limb (unblocked, " + lr[0].parts + " parts)");
        else { failures++; W("   FAIL  pointer-built limb is not driveable"); }

        builtSnapshot = bm.SnapshotString();
        File.WriteAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"), "qa_up1_pointerbuilt.txt"), builtSnapshot);
        W("   snapshot written to Assets/Phase1/qa_up1_pointerbuilt.txt");
        foreach (var line in builtSnapshot.Split('\n')) if (line.Length > 0) W("      " + line);
    }

    IEnumerator Place(int partIdx, int target, Vector3 dir, string what, System.Action<int> report)
    {
        if (partIdx < 0 || target < 0 || target >= bm.placed.Count) { BAD(what + ": bad indices"); report(-1); yield break; }
        bm.selected = partIdx;
        bool got = false;
        yield return Reach(target, dir, r => got = r);
        if (!got) { BAD(what + ": face " + dir.ToString("F0") + " of " + bm.placed[target].def.id + " never resolved under the pointer"); report(-1); yield break; }
        if (!bm.TestGhostValid) { BAD(what + ": ghost REFUSED - " + bm.TestGhostReason); report(-1); yield break; }
        int before = bm.placed.Count;
        Phase0Input.DebugClick(); yield return null; yield return null; yield return null;
        if (bm.placed.Count != before + 1) { BAD(what + ": ghost was valid but the click placed nothing"); report(-1); yield break; }
        OK(what + " -> " + bm.placed[bm.placed.Count - 1].pos.ToString("F3"));
        report(bm.placed.Count - 1);
    }

    // ---------------------------------------------------------------- utils
    /// <summary>Swing the camera until the builder's OWN raycast resolves the
    /// wanted face of the wanted part, with the pointer clear of the panel.</summary>
    IEnumerator Reach(int targetIdx, Vector3 dir, System.Action<bool> report)
    {
        if (targetIdx < 0 || targetIdx >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[targetIdx];
        string wantId = p.def.id;
        int ax = Mathf.Abs(dir.x) > 0.5f ? 0 : (Mathf.Abs(dir.y) > 0.5f ? 1 : 2);
        float sgn = dir[ax] >= 0f ? 1f : -1f;
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float faceCoord = p.pos[ax] + sgn * p.Half()[ax];

        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f, -35f }
                        : dir.y > 0.5f  ? new[] { 55f, 30f, 70f, 40f }
                                        : new[] { 20f, 30f, 0f, 45f, 10f };
        float[] yaws = { 35f, 125f, 215f, 305f, 80f, 170f, 260f, 350f };
        foreach (float pitch in pitches)
        foreach (float yaw in yaws)
        {
            bm.TestOrbitYaw = yaw; bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report(false); yield break; }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f || sp.x < PANEL + 8f || sp.x > Screen.width - 4f || sp.y < 4f || sp.y > Screen.height - 4f) continue;
            Phase0Input.debugMousePos = sp;
            yield return null; yield return null;
            if (bm.TestGhostTarget != wantId) continue;
            if (Vector3.Dot(bm.TestGhostNormal.normalized, dir) < 0.9f) continue;
            float d = bm.TestGhostPos[ax] - faceCoord;
            if (d * sgn <= 0.001f || Mathf.Abs(d) > 0.7f) continue;   // wrong part of the same type
            report(true); yield break;
        }
        report(false);
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
        if (r.StartsWith("Aim at")) return "no-face";
        return r.Length > 22 ? r.Substring(0, 22) : r;
    }
}

}
