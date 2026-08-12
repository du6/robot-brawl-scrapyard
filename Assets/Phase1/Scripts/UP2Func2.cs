
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// UP2 function proof, pass 2. Pass 1 hand-authored the actuator/arm snapshot
// coordinates and got several of them wrong by 3-12 cm, so Validate() refused
// the fixture ("Structure has floating parts") and six of the rigs never
// spawned. Every fixture here is now assembled BY POINTER on top of the proven
// skeleton, which both fixes the arithmetic and makes the claim stronger: the
// weapon that is measured is a weapon a player could have clicked together.
public class UP2Func2 : MonoBehaviour
{
    public string outFile = "qa_up2_func2.txt";
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
        "engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    int Idx(string id)
    { var pal = P1PartDef.Palette(); for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC part function, pass 2 (fixtures assembled by pointer)\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        yield return Actuators();
        yield return Weapons();
        W("\n## DONE");
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f;
        Phase0Input.debugPointer = false; Time.timeScale = 1f;
        bm.Deselect();
        done = true;
    }

    // ---------------------------------------------------------- pointer bits
    IEnumerator Reach(int pi, Vector3 dir, System.Action<bool> report)
    {
        if (pi < 0 || pi >= bm.placed.Count) { report(false); yield break; }
        var p = bm.placed[pi];
        Vector3 world = p.pos + Vector3.Scale(dir, p.Half()) * 1.02f;
        float[] pitches = dir.y < -0.5f ? new[] { -45f, -20f, -65f }
                        : dir.y > 0.5f ? new[] { 55f, 30f, 70f }
                                       : new[] { 20f, 30f, 0f, -10f };
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

    // Build SKEL + [actuator on the aft chassis -Z] + [arm on the actuator -Z],
    // or SKEL + [weapon straight on the aft chassis -Z] when act == "-".
    IEnumerator Rig(string act, string arm, System.Action<string> note)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(SKEL); yield return null;
        Phase0Input.debugPointer = true;
        bm.activeMat = "Steel";
        string n = "";
        int host = 2;
        if (act != "-")
        {
            int ai = -1;
            yield return Place(act, 2, Vector3.back, r => ai = r);
            if (ai < 0) { note("actuator NOT placed (" + (ai == -2 ? "off-cam" : bm.TestGhostReason) + ")"); Phase0Input.debugPointer = false; yield break; }
            host = ai; n += act + "@" + ai + " ";
        }
        if (arm != "-")
        {
            int bi = -1;
            yield return Place(arm, host, Vector3.back, r => bi = r);
            if (bi < 0) { note("arm NOT placed (" + (bi == -2 ? "off-cam" : bm.TestGhostReason) + ")"); Phase0Input.debugPointer = false; yield break; }
            n += arm + "@" + bi;
        }
        Phase0Input.debugPointer = false;
        bm.Deselect();
        bm.activeMat = "Aluminum";
        note("built by pointer: " + n);
    }

    IEnumerator Enter(System.Action<bool> ok)
    {
        string v = bm.Validate();
        if (v != null) { W("   !! fixture invalid: " + v); ok(false); yield break; }
        bm.StartTest();
        for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
        ok(bm.testRobot != null);
    }

    IEnumerator Leave()
    {
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f;
        bm.BackToBuild(); yield return null;
    }

    // ------------------------------------------------------------ actuators
    IEnumerator Actuators()
    {
        W("\n## 1. ACTUATORS, pointer-assembled, trigger held 10 s in TEST DRIVE");
        foreach (var row in new[] { new[]{"pivot","blade"}, new[]{"spindle","blade"}, new[]{"ram","spike"} })
        {
            string note = "";
            yield return Rig(row[0], row[1], s => note = s);
            W(row[0] + ": " + note);
            if (!note.StartsWith("built")) continue;
            var lr = bm.LimbReport();
            string lrs = lr.Count > 0 ? string.Format("limb parts={0} blockedBy={1} tipR={2:F3} rateMax={3:F2} kJ/swing={4:F3}",
                lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius, lr[0].rateMax, lr[0].kjPerSwing) : "NO-LIMB";
            bool ok = false; yield return Enter(o => ok = o);
            if (!ok) { yield return Leave(); continue; }
            var acts = bm.testRobot.GetComponentsInChildren<Actuator>(true);
            if (acts.Length == 0) { W("   !! NO Actuator component in the arena  | " + lrs); yield return Leave(); continue; }
            var a = acts[0];
            Phase0Input.debugFire = true;
            float maxRate = 0f, maxTravel = 0f; int cycles = 0; var ph = a.phase;
            var seen = new List<string>(); float t = 0f;
            while (t < 10f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                maxRate = Mathf.Max(maxRate, Mathf.Abs(a.rate));
                maxTravel = Mathf.Max(maxTravel, Mathf.Abs(a.travel));
                if (a.phase != ph)
                { if (!seen.Contains(a.phase.ToString())) seen.Add(a.phase.ToString());
                  if (a.phase == Actuator.Phase.Driving) cycles++; ph = a.phase; }
            }
            Phase0Input.debugFire = false;
            string extra = row[0] == "ram"
                ? string.Format("EXTEND {0:F3}/{1:F3} m = {2:F0}% of stroke", maxTravel, Actuator.RAM_STROKE_M, 100f * maxTravel / Actuator.RAM_STROKE_M)
                : row[0] == "spindle"
                ? string.Format("SPIN {0:F2} rad/s = {1:F0} RPM, {2:F1} revs in 10 s", maxRate, maxRate * 60f / (Mathf.PI * 2f), maxTravel / (Mathf.PI * 2f))
                : string.Format("SWING arc {0:F3} rad of {1:F3} ({2:F0}%)", maxTravel, Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad, 100f * maxTravel / (Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad));
            W(string.Format("   playerControlled={0} maxRate={1:F2} cycles={2} phases[{3}]  {4}",
                a.playerControlled, maxRate, cycles, string.Join(",", seen.ToArray()), extra));
            W("   " + lrs);
            yield return Leave();
        }
    }

    // -------------------------------------------------------------- weapons
    IEnumerator Weapons()
    {
        W("\n## 2. WEAPON BITE - identical rig, dummy re-parked 1.15 m ahead every 2 s, 12 s of trigger");
        W("   dealt = ram-dummy HP lost. lift = dummy y rise. push/pull = dummy motion along the attack axis.");
        W("   selfMove = how far the ATTACKER moved, so pull can be told from the attacker closing.");
        var cases = new[] {
            new[]{"pivot","blade"}, new[]{"pivot","wedge"}, new[]{"pivot","hook"}, new[]{"pivot","spike"},
            new[]{"-","spinner"}, new[]{"-","spike"}, new[]{"-","blade"}, new[]{"-","-"},   // saw removed 2026-08-12
        };
        foreach (var c in cases)
        {
            string note = "";
            yield return Rig(c[0], c[1], s => note = s);
            string tag = string.Format("{0,-11}{1,-9}", c[1], c[0] == "-" ? "(direct)" : "(on pivot)");
            if (!note.StartsWith("built") && !(c[0] == "-" && c[1] == "-")) { W(tag + note); continue; }
            bool ok = false; yield return Enter(o => ok = o);
            if (!ok || bm.dummyRobot == null) { W(tag + "no bodies"); yield return Leave(); continue; }
            var lr = bm.LimbReport();
            string lrs = lr.Count > 0 ? "limb=" + lr[0].parts + "/blocked=" + (lr[0].blockedBy ?? "null") : "no actuator";
            var sw = bm.testRobot.GetComponentInChildren<SpinnerWeapon>(true);

            var pr = bm.testRobot; var du = bm.dummyRobot;
            float hp0 = HP(du), php0 = HP(pr);
            float dy0 = du.transform.position.y;
            float maxRise = 0f, maxPull = 0f, maxPush = 0f, maxOmega = 0f;
            Vector3 prStart = pr.transform.position;
            Vector3 anchor = prStart; Vector3 axis = pr.transform.forward;
            Phase0Input.debugFire = true;
            float t = 0f, repark = 0f;
            while (t < 12f)
            {
                if (repark <= 0f)
                {
                    axis = pr.transform.forward;
                    anchor = pr.transform.position;
                    Vector3 tgt = anchor + axis * 1.15f;
                    du.transform.position = new Vector3(tgt.x, dy0, tgt.z);
                    if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                    repark = 2f;
                }
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                maxRise = Mathf.Max(maxRise, du.transform.position.y - dy0);
                float along = Vector3.Dot(du.transform.position - anchor, axis);
                maxPush = Mathf.Max(maxPush, along - 1.15f);
                maxPull = Mathf.Max(maxPull, 1.15f - along);
                if (sw != null) maxOmega = Mathf.Max(maxOmega, Mathf.Abs(sw.omega));
            }
            Phase0Input.debugFire = false;
            W(string.Format("{0} dealt={1,7:F1}  self={2,6:F1}  lift={3:F3}  push={4:F3}  pull={5:F3}  selfMove={6:F2}  omega={7:F1}  {8}",
                tag, hp0 - HP(du), php0 - HP(pr), maxRise, maxPush, maxPull,
                Vector3.Distance(prStart, pr.transform.position), maxOmega, lrs));
            yield return Leave();
        }
    }

    static float HP(CompoundRobot r)
    { if (r == null) return 0f; float s = 0f; foreach (var p in r.parts) if (!p.detached) s += p.hp; return s; }
}
}
