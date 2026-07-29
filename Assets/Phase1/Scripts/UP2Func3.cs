
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// UP2 weapon-bite pass 3. Pass 2 mounted every weapon on the REAR face while
// the ram dummy was parked in FRONT, so all nine rigs read dealt=0.0 - my
// fixture, not the game. Everything now hangs off the FRONT (+Z) face of the
// fore chassis, facing the dummy, with the arm's mount offset derived from
// PlacedPart.Half() instead of guessed.
//
// It also isolates the pivot finding pass 2 turned up: a pivot bolted to a
// VERTICAL face hinges about a horizontal axis, so its arm sweeps down into
// the floor. Section 3 measures the same pivot+blade on a vertical mount and
// on a horizontal (roof) mount and reports the arc each one actually reaches.
public class UP2Func3 : MonoBehaviour
{
    public string outFile = "qa_up2_func3.txt";
    BuilderManager bm;
    string path;
    public bool done;

    const string SKEL =
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
    int Idx(string id)
    { var pal = P1PartDef.Palette(); for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC weapon bite, pass 3 (front-mounted, pointer-assembled)\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        yield return Bite();
        yield return ArcVsMount();
        W("\n## DONE");
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f;
        Phase0Input.debugPointer = false; Time.timeScale = 1f;
        bm.Deselect(); done = true;
    }

    IEnumerator Reach(int pi, Vector3 dir, System.Action<bool> report)
    {
        if (pi < 0 || pi >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[pi];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y > 0.5f ? new[] { 55f, 30f, 70f } : new[] { 20f, 30f, 0f, -10f };
        foreach (float pitch in pitches)
        foreach (float yaw in new[] { 35f, 125f, 215f, 305f, 80f, 260f })
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

    IEnumerator Rig(string act, string arm, Vector3 face, int host0, System.Action<string> note)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(SKEL); yield return null;
        Phase0Input.debugPointer = true;
        bm.activeMat = "Steel";
        int host = host0; string n = "";
        if (act != "-")
        {
            int ai = -1;
            yield return Place(act, host0, face, r => ai = r);
            if (ai < 0) { note("actuator NOT placed (" + (ai == -2 ? "off-cam" : bm.TestGhostReason) + ")"); Phase0Input.debugPointer = false; yield break; }
            host = ai; n += act + " ";
        }
        if (arm != "-")
        {
            int bi = -1;
            yield return Place(arm, host, face, r => bi = r);
            if (bi < 0) { note("arm NOT placed (" + (bi == -2 ? "off-cam" : bm.TestGhostReason) + ")"); Phase0Input.debugPointer = false; yield break; }
            n += arm;
        }
        Phase0Input.debugPointer = false; bm.Deselect(); bm.activeMat = "Aluminum";
        note("built: " + n);
    }

    IEnumerator Enter(System.Action<bool> ok)
    {
        string v = bm.Validate();
        if (v != null) { W("   !! invalid: " + v); ok(false); yield break; }
        bm.StartTest();
        for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
        ok(bm.testRobot != null);
    }
    IEnumerator Leave()
    { Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f; bm.BackToBuild(); yield return null; }

    // ------------------------------------------------------------ 1. bite
    IEnumerator Bite()
    {
        W("\n## WEAPON BITE, front-mounted, dummy re-parked 1.15 m ahead every 2 s, 12 s of trigger");
        var cases = new[] {
            new[]{"pivot","blade"}, new[]{"pivot","wedge"}, new[]{"pivot","hook"}, new[]{"pivot","spike"},
            new[]{"spindle","blade"}, new[]{"-","spinner"}, new[]{"-","spike"}, new[]{"-","wedge"}, new[]{"-","-"},
        };
        foreach (var c in cases)
        {
            string note = "";
            yield return Rig(c[0], c[1], Vector3.forward, 1, s => note = s);
            string tag = string.Format("{0,-11}{1,-11}", c[1], c[0] == "-" ? "(direct)" : "(on " + c[0] + ")");
            if (!note.StartsWith("built") && !(c[0] == "-" && c[1] == "-")) { W(tag + note); continue; }
            bool ok = false; yield return Enter(o => ok = o);
            if (!ok || bm.dummyRobot == null) { W(tag + "no bodies"); yield return Leave(); continue; }
            var lr = bm.LimbReport();
            var sw = bm.testRobot.GetComponentInChildren<SpinnerWeapon>(true);
            var ac = bm.testRobot.GetComponentInChildren<Actuator>(true);

            var pr = bm.testRobot; var du = bm.dummyRobot;
            float hp0 = HP(du), php0 = HP(pr);
            float dy0 = du.transform.position.y;
            float lift = 0f, pull = 0f, push = 0f, omega = 0f, arc = 0f;
            Vector3 prStart = pr.transform.position;
            Vector3 anchor = prStart, axis = pr.transform.forward;
            Phase0Input.debugFire = true;
            float t = 0f, repark = 0f;
            while (t < 12f)
            {
                if (repark <= 0f)
                {
                    axis = pr.transform.forward; anchor = pr.transform.position;
                    Vector3 tg = anchor + axis * 1.15f;
                    du.transform.position = new Vector3(tg.x, dy0, tg.z);
                    if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                    repark = 2f;
                }
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                lift = Mathf.Max(lift, du.transform.position.y - dy0);
                float along = Vector3.Dot(du.transform.position - anchor, axis);
                push = Mathf.Max(push, along - 1.15f);
                pull = Mathf.Max(pull, 1.15f - along);
                if (sw != null) omega = Mathf.Max(omega, Mathf.Abs(sw.omega));
                if (ac != null) arc = Mathf.Max(arc, Mathf.Abs(ac.travel));
            }
            Phase0Input.debugFire = false;
            W(string.Format("{0} dealt={1,7:F1}  self={2,6:F1}  lift={3:F3}  push={4:F3}  pull={5:F3}  selfMove={6:F2}  omega={7:F1}  arc={8:F3}  {9}",
                tag, hp0 - HP(du), php0 - HP(pr), lift, push, pull,
                Vector3.Distance(prStart, pr.transform.position), omega, arc,
                lr.Count > 0 ? "builder said tipR=" + lr[0].tipRadius.ToString("F3") + " rateMax=" + lr[0].rateMax.ToString("F2") : "no actuator"));
            yield return Leave();
        }
    }

    // ------------------------------------------- 2. arc vs mount orientation
    IEnumerator ArcVsMount()
    {
        W("\n## PIVOT ARC vs MOUNT FACE - the builder quotes the same limb for both");
        var faces = new[] { Vector3.forward, Vector3.back, Vector3.up };
        string[] fn = { "front +Z (vertical face, horizontal hinge)",
                        "rear  -Z (vertical face, horizontal hinge)",
                        "roof  +Y (horizontal face, vertical hinge)" };
        int[] hosts = { 1, 2, 2 };
        for (int i = 0; i < faces.Length; i++)
        {
            string note = "";
            yield return Rig("pivot", "blade", faces[i], hosts[i], s => note = s);
            if (!note.StartsWith("built")) { W(fn[i] + ": " + note); continue; }
            var lr = bm.LimbReport();
            string promised = lr.Count > 0
                ? string.Format("BUILDER PROMISES tipR={0:F3} rateMax={1:F2} kJ/swing={2:F3} blockedBy={3}",
                    lr[0].tipRadius, lr[0].rateMax, lr[0].kjPerSwing, lr[0].blockedBy ?? "null")
                : "no limb";
            bool ok = false; yield return Enter(o => ok = o);
            if (!ok) { W(fn[i] + ": no robot"); yield return Leave(); continue; }
            var ac = bm.testRobot.GetComponentInChildren<Actuator>(true);
            if (ac == null) { W(fn[i] + ": no actuator"); yield return Leave(); continue; }
            Phase0Input.debugFire = true;
            float arc = 0f, rate = 0f; int cyc = 0; var ph = ac.phase; float t = 0f;
            while (t < 10f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                arc = Mathf.Max(arc, Mathf.Abs(ac.travel));
                rate = Mathf.Max(rate, Mathf.Abs(ac.rate));
                if (ac.phase != ph) { if (ac.phase == Actuator.Phase.Driving) cyc++; ph = ac.phase; }
            }
            Phase0Input.debugFire = false;
            float full = Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad;
            W(string.Format("{0}\n   {1}\n   ARENA DELIVERS arc={2:F3} of {3:F3} = {4:F0}%  peakRate={5:F2}  cycles={6}",
                fn[i], promised, arc, full, 100f * arc / full, rate, cyc));
            yield return Leave();
        }
    }

    static float HP(CompoundRobot r)
    { if (r == null) return 0f; float s = 0f; foreach (var p in r.parts) if (!p.detached) s += p.hp; return s; }
}
}
