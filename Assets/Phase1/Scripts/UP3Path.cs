using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ROUND-3 CRITIC, pointer path. Section-dispatched so one compile serves the
// whole round. Inherits UserPathTest's two rules: VERIFY THE HIT (target id,
// face normal, and that the snap landed on the right SIDE of the target) and
// ISOLATE THE FIXTURE. Everything here goes through debugMousePos ->
// UpdateBuild -> ghost -> DebugClick. Nothing calls AddPart directly.
public class UP3Path : MonoBehaviour
{
    public string outFile = "qa_up3_path.txt";
    public string section = "robot";
    public bool done;
    public int checks, failures, offcam;
    public string summary = "";

    BuilderManager bm;
    string path;
    string lastWhy = "";

    const string BARE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    static int Idx(string id)
    {
        var pal = P1PartDef.Palette();
        for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i;
        return -1;
    }

    static Vector3 MeshSize(GameObject go)
    {
        if (go == null) return Vector3.zero;
        var rs = go.GetComponentsInChildren<Renderer>(true);
        bool any = false; Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        return any ? b.size : Vector3.zero;
    }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        if (!File.Exists(path)) File.WriteAllText(path, "# UP3 CRITIC - pointer path sweep\n");
        W("\n===== SECTION " + section + " =====");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        Phase0Input.debugPointer = true;
        yield return null;

        if (section == "robot") yield return SecRobot();
        else if (section == "onto") yield return SecOnto();
        else if (section == "stack") yield return SecStack();
        else if (section == "ryaw") yield return SecRYaw();
        else if (section == "misc") yield return SecMisc();
        else W("unknown section");

        summary = string.Format("checks={0} failures={1} offcam={2}", checks, failures, offcam);
        W("## SECTION " + section + " DONE " + summary);
        Phase0Input.debugPointer = false;
        bm.Deselect();
        done = true;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Orbit until the builder's OWN raycast resolves the wanted face
    /// of the wanted placed part. Returns false rather than lying.</summary>
    IEnumerator Reach(int ti, Vector3 dir, System.Action<bool> rep)
    {
        if (ti < 0 || ti >= bm.placed.Count) { rep(false); yield break; }
        var p = bm.placed[ti];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y < -0.5f ? new float[] { -45f, -20f, -65f, -35f }
                        : dir.y > 0.5f ? new float[] { 55f, 30f, 70f, 45f }
                                       : new float[] { 20f, 30f, 0f, -10f, 45f };
        float[] yaws = new float[] { 35f, 125f, 215f, 305f, 80f, 170f, 260f, 350f };
        foreach (float pit in pitches)
        foreach (float yw in yaws)
        {
            bm.TestOrbitYaw = yw; bm.TestOrbitPitch = pit;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { rep(false); yield break; }
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.05f || sp.x < 292f || sp.x > Screen.width - 4f
                || sp.y < 4f || sp.y > Screen.height - 4f) continue;
            Phase0Input.debugMousePos = sp;
            yield return null; yield return null;
            if (bm.TestGhostTarget == p.def.id
                && Vector3.Dot(bm.TestGhostNormal, dir) > 0.9f
                && Vector3.Dot(bm.TestGhostPos - p.pos, dir) > 0.01f
                && (bm.TestGhostPos - p.pos).magnitude < 1.6f) { rep(true); yield break; }
        }
        rep(false);
    }

    /// <summary>-2 off-camera, -3 ghost refused (lastWhy = reason),
    /// -4 ghost green but the click placed nothing, else new part index.</summary>
    IEnumerator Place(string id, int ti, Vector3 dir, int rots, System.Action<int> rep)
    {
        lastWhy = "";
        int pi = Idx(id);
        if (pi < 0) { lastWhy = "no such part"; rep(-3); yield break; }
        bm.selected = pi;
        yield return null;
        bool reached = false;
        yield return Reach(ti, dir, r => reached = r);
        if (!reached) { offcam++; lastWhy = "off-camera"; rep(-2); yield break; }
        for (int r = 0; r < rots; r++) { Phase0Input.DebugRotate(); yield return null; yield return null; }
        yield return null;
        if (!bm.TestGhostValid) { lastWhy = bm.TestGhostReason; rep(-3); yield break; }
        int before = bm.placed.Count;
        Phase0Input.DebugClick();
        yield return null; yield return null; yield return null;
        if (bm.placed.Count > before) rep(bm.placed.Count - 1);
        else { lastWhy = "GHOST GREEN, CLICK DID NOTHING"; rep(-4); }
    }

    int Cost()
    { int c = 0; foreach (var p in bm.placed) c += p.Cost(); return c; }

    int Wheels()
    { int n = 0; foreach (var p in bm.placed) if (p.def.category == P1Category.Mobility) n++; return n; }

    string Snap()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var p in bm.placed)
            sb.Append(string.Format("{0}|{1:F3},{2:F3},{3:F3}|{4}|{5:F2},{6:F2},{7:F2}|{8}\n",
                p.def.id, p.pos.x, p.pos.y, p.pos.z, p.yaw,
                p.wheelAxis.x, p.wheelAxis.y, p.wheelAxis.z, p.MatName()));
        return sb.ToString();
    }

    // ---------------------------------------------- 1. whole robot by pointer
    IEnumerator SecRobot()
    {
        W("## A COMPLETE ROBOT, START TO FINISH, BY POINTER ALONE (no LoadSnapshot after the bare core)");
        bm.LoadSnapshot(BARE); yield return null;
        bm.activeMat = "Aluminum";

        // id, hostIndex, face, label
        var steps = new List<object[]>();
        steps.Add(new object[] { "chassis", 0, Vector3.forward, "Aluminum" });
        steps.Add(new object[] { "chassis", 0, Vector3.back, "Aluminum" });
        steps.Add(new object[] { "wheel", 1, Vector3.right, "Rubber" });
        steps.Add(new object[] { "wheel", 1, Vector3.left, "Rubber" });
        steps.Add(new object[] { "wheel", 2, Vector3.right, "Rubber" });
        steps.Add(new object[] { "wheel", 2, Vector3.left, "Rubber" });
        steps.Add(new object[] { "battery", 0, Vector3.up, "ABS" });
        steps.Add(new object[] { "engine", 2, Vector3.up, "Steel" });
        steps.Add(new object[] { "gyro", 0, Vector3.up, "ABS" });
        steps.Add(new object[] { "spinner", 1, Vector3.forward, "Steel" });

        foreach (var st in steps)
        {
            string id = (string)st[0]; int host = (int)st[1];
            Vector3 f = (Vector3)st[2]; bm.activeMat = (string)st[3];
            int r = -9;
            yield return Place(id, host, f, 0, x => r = x);
            checks++;
            if (r < 0) { failures++; W(string.Format("  {0,-9} on part {1} face {2}  FAILED: {3}", id, host, f, lastWhy)); }
            else W(string.Format("  {0,-9} on part {1} face {2} -> idx {3} at {4}", id, host, f, r, bm.placed[r].pos.ToString("F3")));
        }
        bm.activeMat = "Aluminum";
        string v = bm.Validate();
        W(string.Format("  RESULT parts={0} wheels={1} cost={2} Validate={3}",
            bm.placed.Count, Wheels(), Cost(), v == null ? "OK" : v));
        checks++; if (v != null) failures++;

        var lr = bm.LimbReport();
        W("  LimbReport entries=" + lr.Count);
        foreach (var li in lr)
            W(string.Format("    {0} parts={1} blockedBy={2} tipR={3:F3} rateMax={4:F2} arcFrac={5:F2}",
                li.act != null ? li.act.def.id : "?", li.parts, li.blockedBy ?? "null",
                li.tipRadius, li.rateMax, li.arcFrac));

        // GATE: only write a snapshot file if it is a real, legal machine.
        if (bm.placed.Count >= 5 && Wheels() >= 2 && v == null)
        {
            File.WriteAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"),
                "qa_up3_pointerbot.txt"), Snap());
            W("  snapshot written to qa_up3_pointerbot.txt (gate passed)");
        }
        else W("  snapshot NOT written (gate failed)");
    }

    // -------------------------------------------------- 2. part ONTO part
    IEnumerator SecOnto()
    {
        var pal = P1PartDef.Palette();
        // fixture, host index, faces
        var fixtures = new[] {
            new object[]{ BARE + "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n", 1,
                          new Vector3[]{ Vector3.right, Vector3.up, Vector3.forward, Vector3.down }, "CHASSIS" },
            new object[]{ BARE + "beam|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n", 1,
                          new Vector3[]{ Vector3.right, Vector3.up, Vector3.forward }, "BEAM" },
            new object[]{ BARE + "plate|0.000,0.880,0.000|0|0.00,0.00,0.00|Aluminum\n", 1,
                          new Vector3[]{ Vector3.up, Vector3.right }, "PLATE" },
        };
        string[] fn = { "+X", "-X", "+Y", "-Y", "+Z", "-Z" };
        Vector3[] fd = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        foreach (var fx in fixtures)
        {
            string snap = (string)fx[0]; int host = (int)fx[1];
            Vector3[] faces = (Vector3[])fx[2]; string label = (string)fx[3];
            W("\n## ATTACH ONTO A " + label + " (not a core) - the case no sweep has ever run");
            for (int pi = 0; pi < pal.Length; pi++)
            {
                if (pal[pi].id == "core") continue;
                string row = string.Format("  {0,-11}", pal[pi].id);
                foreach (var f in faces)
                {
                    bm.LoadSnapshot(snap); yield return null;
                    int r = -9;
                    yield return Place(pal[pi].id, host, f, 0, x => r = x);
                    string nm = "?";
                    for (int k = 0; k < 6; k++) if ((fd[k] - f).sqrMagnitude < 0.01f) nm = fn[k];
                    if (r == -2) { row += " " + nm + ":off-cam"; continue; }
                    checks++;
                    if (r == -4) { failures++; row += " " + nm + ":!!GREEN-NO-PLACE"; }
                    else if (r == -3) row += " " + nm + ":X(" + Short(lastWhy) + ")";
                    else row += " " + nm + ":ok";
                }
                W(row);
            }
        }
    }

    // --------------------------------------------------------- 3. deep stacks
    IEnumerator SecStack()
    {
        W("## MULTI-PART STACKS - does the snap stay true four deep?");
        bm.LoadSnapshot(BARE); yield return null;
        bm.activeMat = "ABS";
        int host = 0;
        for (int d = 0; d < 5; d++)
        {
            int r = -9;
            yield return Place("beam", host, Vector3.forward, 0, x => r = x);
            checks++;
            if (r < 0) { failures++; W(string.Format("  depth {0} beam FAILED: {1}", d + 1, lastWhy)); break; }
            var p = bm.placed[r]; var h = bm.placed[host];
            float gapZ = (p.pos.z - p.Half().z) - (h.pos.z + h.Half().z);
            W(string.Format("  depth {0} beam at {1} gap-to-host={2:F4} (0 = flush)",
                d + 1, p.pos.ToString("F3"), gapZ));
            if (Mathf.Abs(gapZ) > 0.001f) { failures++; W("    !! NOT FLUSH"); }
            host = r;
        }
        W(string.Format("  stack result parts={0} cost={1} Validate={2}",
            bm.placed.Count, Cost(), bm.Validate() ?? "OK"));

        W("\n## MIXED STACK core->chassis->beam->bracket->blade, each onto the LAST one");
        bm.LoadSnapshot(BARE); yield return null;
        bm.activeMat = "Aluminum";
        string[] chain = { "chassis", "beam", "beam", "blade" };   // bracket removed 2026-08-12
        host = 0;
        foreach (var id in chain)
        {
            int r = -9;
            yield return Place(id, host, Vector3.forward, 0, x => r = x);
            checks++;
            if (r < 0) { failures++; W(string.Format("  {0} onto idx {1} FAILED: {2}", id, host, lastWhy)); break; }
            W(string.Format("  {0,-8} onto idx {1} -> idx {2} at {3}", id, host, r, bm.placed[r].pos.ToString("F3")));
            host = r;
        }
        W("  chain Validate=" + (bm.Validate() ?? "OK"));
    }

    // ----------------------------------- 4. R at every yaw, ghost vs placed
    IEnumerator SecRYaw()
    {
        W("## R AT EVERY YAW: does the PLACED part match the GHOST the player was shown?");
        string[] ids = { "beam", "beamlong", "plate", "chassis" };   // bracket removed 2026-08-12
        foreach (var id in ids)
        {
            W("  --- " + id);
            for (int step = 0; step < 4; step++)
            {
                bm.LoadSnapshot(BARE); yield return null;
                bm.selected = Idx(id);
                yield return null;
                bool reached = false;
                yield return Reach(0, Vector3.up, r => reached = r);
                if (!reached) { offcam++; W("    off-camera"); continue; }
                for (int r = 0; r < step; r++) { Phase0Input.DebugRotate(); yield return null; yield return null; }
                yield return null;
                int gy = bm.TestGhostYaw;
                Vector3 gm = bm.TestGhostMeshSize();
                Vector3 gp = bm.TestGhostPos;
                bool gv = bm.TestGhostValid;
                string gr = bm.TestGhostReason;
                if (!gv) { W(string.Format("    R x{0} yaw={1} ghost REFUSED: {2}", step, gy, Short(gr))); checks++; continue; }
                int before = bm.placed.Count;
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                checks++;
                if (bm.placed.Count <= before) { failures++; W(string.Format("    R x{0} yaw={1} !!GREEN-NO-PLACE", step, gy)); continue; }
                var np = bm.placed[bm.placed.Count - 1];
                Vector3 pm = MeshSize(np.go);
                bool yawSame = np.yaw == gy;
                bool posSame = (np.pos - gp).sqrMagnitude < 1e-6f;
                bool meshSame = (pm - gm).sqrMagnitude < 2e-4f;
                if (!yawSame || !posSame || !meshSame) failures++;
                W(string.Format("    R x{0} ghost yaw={1} pos={2} mesh={3} | placed yaw={4} pos={5} mesh={6} {7}{8}{9}",
                    step, gy, gp.ToString("F3"), gm.ToString("F3"),
                    np.yaw, np.pos.ToString("F3"), pm.ToString("F3"),
                    yawSame ? "" : " !!YAW-MISMATCH", posSame ? "" : " !!POS-MISMATCH",
                    meshSame ? "" : " !!MESH-MISMATCH"));
            }
        }
    }

    // -------------------------- 5. Esc, panel clicks, empty space, mis-clicks
    IEnumerator SecMisc()
    {
        W("## ESC / PANEL / EMPTY SPACE / RIGHT + MIDDLE CLICK");

        // -- Esc drops the held part
        bm.LoadSnapshot(BARE); yield return null;
        bm.selected = Idx("beam");
        bool reached = false;
        yield return Reach(0, Vector3.up, r => reached = r);
        checks++;
        if (!reached) { offcam++; W("  ESC: could not arm ghost (off-cam)"); }
        else
        {
            bool shownBefore = bm.TestGhostShown;
            Phase0Input.DebugEsc();
            yield return null; yield return null;
            bool ok = bm.selected < 0 && !bm.TestGhostShown;
            if (!ok) failures++;
            W(string.Format("  ESC: shownBefore={0} -> selected={1} shown={2}  {3}",
                shownBefore, bm.selected, bm.TestGhostShown, ok ? "ok" : "!!ESC DID NOT DROP THE PART"));
        }

        // -- click over the palette panel must not place anything
        bm.LoadSnapshot(BARE); yield return null;
        bm.selected = Idx("beam");
        yield return null;
        yield return Reach(0, Vector3.up, r => reached = r);
        if (!reached) { W("  PANEL: could not arm ghost"); offcam++; }
        else
        {
            Vector3 armed = Phase0Input.debugMousePos;
            Phase0Input.debugMousePos = new Vector3(120f, Screen.height * 0.5f, 0f);
            yield return null; yield return null;
            bool ghostHidden = !bm.TestGhostShown && !bm.TestGhostValid;
            int before = bm.placed.Count;
            Phase0Input.DebugClick();
            yield return null; yield return null;
            int mid = bm.placed.Count;
            checks++;
            if (mid > before) { failures++; W("  PANEL: !! CLICK OVER THE PANEL PLACED A PART"); }
            else W(string.Format("  PANEL: ghostHidden={0} placed+0 ok", ghostHidden));
            // Does the swallowed click LEAK to the next frame over the model?
            Phase0Input.debugMousePos = armed;
            yield return null; yield return null; yield return null;
            checks++;
            if (bm.placed.Count > mid)
            { failures++; W("  PANEL-LEAK: !! the click made over the panel fired later, over the model, with no new click"); }
            else W("  PANEL-LEAK: no leak");
        }

        // -- click on empty space
        bm.LoadSnapshot(BARE); yield return null;
        bm.selected = Idx("beam");
        yield return null;
        bm.TestOrbitYaw = 35f; bm.TestOrbitPitch = 25f;
        yield return null;
        Phase0Input.debugMousePos = new Vector3(Screen.width - 20f, Screen.height - 20f, 0f);
        yield return null; yield return null;
        {
            int before = bm.placed.Count;
            bool gv = bm.TestGhostValid; string gr = bm.TestGhostReason;
            string tg = bm.TestGhostTarget;
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            checks++;
            bool placedSomething = bm.placed.Count > before;
            if (placedSomething) { failures++; W(string.Format("  EMPTY: !! CLICK IN THE SKY PLACED A PART at {0}", bm.placed[bm.placed.Count-1].pos.ToString("F3"))); }
            else W(string.Format("  EMPTY: ghostValid={0} target={1} reason=\"{2}\" placed+0 ok", gv, tg, gr));
        }

        // -- right-click removal: leaf yes, core no
        bm.LoadSnapshot(BARE); yield return null;
        bm.activeMat = "ABS";
        int bi = -9;
        yield return Place("beam", 0, Vector3.forward, 0, x => bi = x);
        if (bi < 0) W("  RCLICK: could not build the leaf (" + lastWhy + ")");
        else
        {
            bm.Deselect();
            yield return null;
            bool got = false;
            yield return Reach(0, Vector3.up, r => got = r);   // aim at the CORE top
            if (!got) { offcam++; W("  RCLICK core: off-cam"); }
            else
            {
                int before = bm.placed.Count;
                Phase0Input.DebugClick(1);
                yield return null; yield return null; yield return null;
                checks++;
                if (bm.placed.Count < before)
                { failures++; W("  RCLICK core: !! REMOVED THE CORE / ORPHANED THE BUILD, parts now " + bm.placed.Count); }
                else W("  RCLICK core: refused, ok (parts=" + bm.placed.Count + ")");
            }
            // now the leaf
            if (bm.placed.Count > 1)
            {
                bool got2 = false;
                yield return Reach(1, Vector3.forward, r => got2 = r);
                if (!got2) { offcam++; W("  RCLICK leaf: off-cam"); }
                else
                {
                    int before = bm.placed.Count;
                    Phase0Input.DebugClick(1);
                    yield return null; yield return null; yield return null;
                    checks++;
                    if (bm.placed.Count == before - 1) W("  RCLICK leaf: removed, ok");
                    else { failures++; W("  RCLICK leaf: !! NOT REMOVED (parts " + before + " -> " + bm.placed.Count + ")"); }
                }
            }
        }

        // -- middle-click repaint
        bm.LoadSnapshot(BARE); yield return null;
        bm.activeMat = "ABS";
        int mi = -9;
        yield return Place("beam", 0, Vector3.forward, 0, x => mi = x);
        if (mi < 0) W("  MCLICK: could not build the leaf");
        else
        {
            bm.Deselect(); yield return null;
            string m0 = bm.placed[mi].MatName();
            int c0 = bm.placed[mi].Cost();
            bm.activeMat = "Titanium";
            bool got = false;
            yield return Reach(mi, Vector3.forward, r => got = r);
            if (!got) { offcam++; W("  MCLICK: off-cam"); }
            else
            {
                Phase0Input.DebugClick(2);
                yield return null; yield return null; yield return null;
                checks++;
                string m1 = bm.placed[mi].MatName();
                int c1 = bm.placed[mi].Cost();
                if (m1 == m0) { failures++; W("  MCLICK: !! MATERIAL DID NOT CHANGE (" + m0 + ")"); }
                else W(string.Format("  MCLICK: {0}({1}cr) -> {2}({3}cr) ok", m0, c0, m1, c1));
            }
            bm.activeMat = "Aluminum";
        }

        // -- click while the ghost is RED must never place
        bm.LoadSnapshot(BARE); yield return null;
        bm.selected = Idx("wheel");
        yield return null;
        bool gotTop = false;
        yield return Reach(0, Vector3.up, r => gotTop = r);
        if (!gotTop) { offcam++; W("  REDCLICK: off-cam"); }
        else
        {
            checks++;
            if (bm.TestGhostValid) W("  REDCLICK: wheel on the roof was ACCEPTED - expected the side-only rule; skipping");
            else
            {
                int before = bm.placed.Count;
                Phase0Input.DebugClick();
                yield return null; yield return null; yield return null;
                if (bm.placed.Count > before) { failures++; W("  REDCLICK: !! RED GHOST STILL PLACED A PART"); }
                else W("  REDCLICK: refused \"" + bm.TestGhostReason + "\" ok");
            }
        }

        // -- two clicks in consecutive frames on the same spot
        bm.LoadSnapshot(BARE); yield return null;
        bm.selected = Idx("beam");
        yield return null;
        bool gotD = false;
        yield return Reach(0, Vector3.forward, r => gotD = r);
        if (!gotD) { offcam++; W("  DOUBLE: off-cam"); }
        else
        {
            int before = bm.placed.Count;
            Phase0Input.DebugClick();
            yield return null;
            Phase0Input.DebugClick();
            yield return null; yield return null; yield return null;
            checks++;
            int added = bm.placed.Count - before;
            bool overlap = false;
            for (int a = 0; a < bm.placed.Count && !overlap; a++)
            for (int b = a + 1; b < bm.placed.Count; b++)
            {
                var pa = bm.placed[a]; var pb = bm.placed[b];
                Vector3 d = pa.pos - pb.pos; Vector3 s = pa.Half() + pb.Half();
                if (Mathf.Abs(d.x) < s.x - 0.002f && Mathf.Abs(d.y) < s.y - 0.002f
                    && Mathf.Abs(d.z) < s.z - 0.002f) { overlap = true; break; }
            }
            if (overlap) { failures++; W("  DOUBLE: !! two fast clicks produced OVERLAPPING parts (added " + added + ")"); }
            else W("  DOUBLE: added " + added + " part(s), no overlap, ok");
        }
        bm.activeMat = "Aluminum";
    }

    static string Short(string r)
    {
        if (string.IsNullOrEmpty(r)) return "no-reason";
        if (r.StartsWith("No socket")) return "no-socket-target";
        if (r.StartsWith("This way up")) return "no-socket-self";
        if (r.StartsWith("Blocked")) return "blocked";
        if (r.StartsWith("Nothing attaches to a wheel")) return "not-on-wheel";
        if (r.StartsWith("Nothing attaches to a weapon")) return "not-on-weapon";
        if (r.StartsWith("Wheels attach")) return "wheel-side-only";
        if (r.StartsWith("Spinner")) return "no-underside";
        if (r.StartsWith("Too low")) return "too-low";
        if (r.StartsWith("Over budget")) return "over-budget";
        return r.Length > 26 ? r.Substring(0, 26) : r;
    }
}
}
