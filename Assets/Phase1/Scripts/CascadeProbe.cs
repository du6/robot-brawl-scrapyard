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
/// CASCADE REMOVAL VERIFICATION (2026-07-29).
///
/// owen asked for right-click to take the whole branch. Three ways that can be
/// wrong, and only the first is what he asked about:
///   1. it removes too LITTLE - the old bug, still one right-click per part;
///   2. it removes too MUCH - takes parts that are braced to the core by some
///      other route, i.e. deletes work that should have survived; or
///   3. it leaves the build INVALID - orphans, or a byCollider map still
///      pointing at destroyed parts, which would make the next click hit a
///      ghost.
///
/// This drives the REAL pointer path - orbit the camera until the part is under
/// the cursor, then Phase0Input.DebugClick(1) - because this project's standing
/// failure is that harnesses go through LoadSnapshot and skip the entire mouse
/// pipeline. A cascade verified only by calling RemovePart() in code would
/// prove nothing about the verb the player actually has.
///
/// The oracle is computed independently of the code under test: for each part,
/// flood-fill from the core over the OTHER parts using the snapshot geometry,
/// and whatever is unreachable is what must go. If DependentsOf and this
/// disagree, one of them is wrong and the run says so.
/// </summary>
public class CascadeProbe : MonoBehaviour
{
    public BuilderManager bm;
    public string outPath = "Assets/Phase1/qa_cascade.txt";
    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();
    int pass, fail;

    void W(string s) { rows.Add(s); }

    public void Run() { StartCoroutine(Go()); }

    struct Node { public Vector3 pos; public Vector3 half; public string id; }

    IEnumerator Go()
    {
        if (bm == null) bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Finish("no BuilderManager"); yield break; }

        string snap = File.ReadAllText(Path.Combine(Application.dataPath, "Phase1/qa_disc_spinner.txt"));
        Phase0Input.debugPointer = true;
        yield return null;

        int n0 = bm.LoadSnapshot(snap);
        W("# CASCADE REMOVAL - real right-click path");
        W("# fixture parts=" + n0 + "  (independent oracle: flood fill from core)");
        W("");

        // ---- oracle over the loaded geometry ------------------------------
        var nodes = new List<Node>();
        foreach (var p in bm.placed)
            nodes.Add(new Node { pos = p.pos, half = p.Half(), id = p.def.id });

        W("idx part          expected  actual   parts_before -> after   verdict");

        for (int target = 1; target < n0; target++)
        {
            int n = bm.LoadSnapshot(snap);
            if (n != n0) { W("reload drift " + n + " != " + n0); break; }
            yield return null;

            int expect = 1 + OrphanCount(nodes, target);
            var part = bm.placed[target];
            string label = part.def.id;

            bool reached = false;
            yield return Reach(part, r => reached = r);
            if (!reached) { W(string.Format("{0,3} {1,-13} OFF-CAMERA (fixture limit, not a failure)", target, label)); continue; }

            int before = bm.placed.Count;
            Phase0Input.DebugClick(1);
            yield return null; yield return null;
            int after = bm.placed.Count;
            int actual = before - after;

            bool okCount = actual == expect;
            // CONNECTIVITY, not Validate(). The first version asserted
            // Validate()==null here and reported ONE spurious FAIL: cutting the
            // only battery leaves a perfectly connected machine that Validate
            // rejects with "Needs an engine or battery." That is a build
            // COMPLETENESS rule - true of any half-finished build, and exactly
            // the state a player is in mid-edit - not an orphan. Asserting it
            // would have made the probe demand that you may never remove your
            // last battery, which is not the invariant cascade removal owes.
            bool okConn  = StillConnected();
            bool okMap   = MapClean();
            string vmsg  = bm.Validate();
            string verdict = okCount && okConn && okMap ? "PASS"
                           : "FAIL" + (okCount ? "" : " count")
                                    + (okConn ? "" : " ORPHAN")
                                    + (okMap ? "" : " stale-collider");
            if (verdict == "PASS") pass++; else fail++;
            W(string.Format("{0,3} {1,-13} {2,8} {3,7}   {4,5} -> {5,-5}    {6,-8} {7}",
                            target, label, expect, actual, before, after, verdict,
                            vmsg == null ? "" : "(build rule: " + vmsg + ")"));
        }

        // ---- undo restores exactly what the cascade took ------------------
        W("");
        bm.LoadSnapshot(snap);
        yield return null;
        string beforeSnap = bm.SnapshotString();
        int cut = -1;
        for (int i = 1; i < bm.placed.Count; i++)
            if (bm.placed[i].def.id == "spindle") { cut = i; break; }
        if (cut < 0) cut = 1;
        var cp = bm.placed[cut];
        bool got = false;
        yield return Reach(cp, r => got = r);
        if (got)
        {
            int b = bm.placed.Count;
            Phase0Input.DebugClick(1);
            yield return null; yield return null;
            int a = bm.placed.Count;
            Phase0Input.DebugUndo();
            yield return null; yield return null;
            string afterSnap = bm.SnapshotString();
            bool same = afterSnap == beforeSnap;
            if (same) pass++; else fail++;
            W(string.Format("UNDO: cut {0} ({1} -> {2} parts), Z restored {3} parts, byte-identical={4}  {5}",
                            cp.def.id, b, a, bm.placed.Count, same, same ? "PASS" : "FAIL"));
            if (!same)
            {
                W("  before: " + beforeSnap.Replace("\n", " ; "));
                W("  after : " + afterSnap.Replace("\n", " ; "));
            }
        }
        else W("UNDO: target off-camera, not tested");

        Phase0Input.debugPointer = false;
        Finish(string.Format("pass={0} fail={1}", pass, fail));
    }

    /// <summary>Independent oracle: how many parts become unreachable from the
    /// core (index 0) when `skip` is deleted. Uses only the snapshot geometry.</summary>
    int OrphanCount(List<Node> nodes, int skip)
    {
        var seen = new bool[nodes.Count];
        seen[0] = true;
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (i == skip || seen[i]) continue;
                if (!Touch(nodes[cur], nodes[i])) continue;
                seen[i] = true; stack.Push(i);
            }
        }
        int orphans = 0;
        for (int i = 0; i < nodes.Count; i++) if (i != skip && !seen[i]) orphans++;
        return orphans;
    }

    static bool Touch(Node a, Node b)
    {
        int flush = -1;
        for (int i = 0; i < 3; i++)
        {
            float gap = Mathf.Abs(a.pos[i] - b.pos[i]) - (a.half[i] + b.half[i]);
            if (gap > 0.03f) return false;
            if (gap > -0.03f) flush = i;
        }
        return flush >= 0;
    }

    /// <summary>Is every surviving part still reachable from the core? This is
    /// the invariant cascade removal must preserve, stated over the LIVE build
    /// rather than over the snapshot oracle.</summary>
    bool StillConnected()
    {
        var live = new List<Node>();
        foreach (var q in bm.placed) live.Add(new Node { pos = q.pos, half = q.Half(), id = q.def.id });
        if (live.Count <= 1) return true;
        var seen = new bool[live.Count];
        seen[0] = true;
        var stack = new Stack<int>();
        stack.Push(0);
        int reached = 1;
        while (stack.Count > 0)
        {
            int cur = stack.Pop();
            for (int i = 0; i < live.Count; i++)
            {
                if (seen[i] || !Touch(live[cur], live[i])) continue;
                seen[i] = true; reached++; stack.Push(i);
            }
        }
        return reached == live.Count;
    }

    /// <summary>No collider in the lookup may point at a part that is gone.</summary>
    bool MapClean()
    {
        foreach (var p in bm.placed) if (p.go == null) return false;
        return true;
    }

    IEnumerator Reach(BuilderManager.PlacedPart p, System.Action<bool> report)
    {
        foreach (float pitch in new[] { 25f, 45f, 8f })
        foreach (float yaw in new[] { 35f, 125f, 215f, 305f, 80f, 260f })
        {
            bm.TestOrbitYaw = yaw;
            bm.TestOrbitPitch = pitch;
            yield return null;
            var cam = bm.TestCam;
            if (cam == null) { report(false); yield break; }
            Phase0Input.debugMousePos = cam.WorldToScreenPoint(p.pos);
            yield return null;
            if (bm.HoverIs(p)) { report(true); yield break; }
        }
        report(false);
    }

    void Finish(string msg)
    {
        summary = msg;
        W("# " + msg);
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception ex) { Debug.LogWarning("CascadeProbe write failed: " + ex.Message); }
        done = true;
        Debug.Log("CascadeProbe: " + msg);
    }
}

}
#endif
