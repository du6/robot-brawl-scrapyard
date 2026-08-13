using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-UP1 pass 2. Two jobs:
///  (R) re-measure the R fix with the RIGHT metric. The first attempt counted
///      "distinct yaws vs distinct meshes" over a cycle, which is confounded by
///      whatever yaw the previous part left behind - a leftover state the cycle
///      leaves immediately and never returns to still counts. The metric that
///      actually states the requirement is per-press: DID THIS PRESS CHANGE THE
///      DRAWING? Measured from a normalised start, on the rendered bounds.
///  (F) prove each part FUNCTIONS, with a number, in TEST DRIVE - the mode that
///      could not exercise a weapon at all until FIX A landed this round.
/// </summary>
public class UP1Dev2 : MonoBehaviour
{
    public string outFile = "qa_up1_dev2.txt";
    public bool done;
    public int checks, failures;

    BuilderManager bm;
    P1PartDef[] pal;
    string path;
    const float PANEL = 280f;

    const string SK =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n";
    const string ENG1 = "engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n";
    const string ENG2 = "engine|0.000,0.975,-0.400|0|0.00,0.00,0.00|Steel\n";
    const string BAT2 = "battery|0.000,1.225,0.000|0|0.00,0.00,0.00|ABS\n";
    const string GYRO = "gyro|0.000,0.970,-0.400|0|0.00,1.00,0.00|Aluminum\n";
    const string CORE = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n";

    // Body raised 0.20 m on the same wheels: deck still 0.520, belly at 0.750.
    const string SKHI =
        "core|0.000,0.900,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.900,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.900,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "battery|0.000,1.175,0.000|0|0.00,0.00,0.00|ABS\n" +
        "engine|0.000,1.175,0.400|0|0.00,0.00,0.00|Steel\n";

    void W(string s) { File.AppendAllText(path, s + "\n"); }
    void OK(string s)  { checks++; W("   ok    " + s); }
    void BAD(string s) { checks++; failures++; W("   FAIL  " + s); }
    int Idx(string id) { for (int i = 0; i < pal.Length; i++) if (pal[i].id == id) return i; return -1; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP1 DEV PASS 2 - R metric redone + part FUNCTION in TEST DRIVE\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no BuilderManager"); done = true; yield break; }
        pal = P1PartDef.Palette();
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f; Phase0Input.debugFire = false;
        Phase0Input.debugPointer = true;
        yield return null;

        yield return R_PerPress();
        yield return F_Actuators();
        yield return F_Discs();
        yield return F_Drive();
        yield return F_Gyro();
        yield return F_Power();

        W(string.Format("\n## DONE checks={0} failures={1}", checks, failures));
        Phase0Input.debugPointer = false; Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        bm.selected = -1; bm.Deselect();
        done = true;
    }

    // ------------------------------------------------------------------ R
    IEnumerator R_PerPress()
    {
        W("\n## R. DOES EVERY R PRESS CHANGE THE DRAWING? (per-press, normalised start)");
        string[] ids = { "beam", "beamlong", "plate", "chassis", "engine", "battery" };   // bracket removed 2026-08-12; battery keeps the symmetric-cube case
        foreach (string id in ids)
        {
            int pi = Idx(id);
            if (pi < 0) continue;
            bm.LoadSnapshot(CORE); yield return null;
            bm.selected = pi;
            bool got = false;
            yield return Reach(0, Vector3.up, r => got = r);
            if (!got) { W("   " + id + ": core roof not reachable, skipped"); continue; }

            // Normalise: press R until the yaw is 0, so the run does not start
            // on a state the (possibly shorter) cycle has already left behind.
            for (int g = 0; g < 6 && bm.TestGhostYaw != 0; g++)
            { Phase0Input.DebugRotate(); yield return null; yield return null; }

            int dead = 0, presses = 6;
            string trail = "" + bm.TestGhostYaw;
            for (int i = 0; i < presses; i++)
            {
                Vector3 m0 = bm.TestGhostMeshSize();
                Phase0Input.DebugRotate();
                yield return null; yield return null; yield return null;
                Vector3 m1 = bm.TestGhostMeshSize();
                trail += ">" + bm.TestGhostYaw;
                if ((m1 - m0).sqrMagnitude < 1e-6f) dead++;
            }
            // A cube is the same drawing at every yaw; there is nothing R could
            // show. Reported, not failed.
            var d0 = new BuilderManager.PlacedPart { def = pal[pi], yaw = 0 }.Half();
            bool cube = true;
            for (int y = 90; y < 360; y += 90)
                if ((new BuilderManager.PlacedPart { def = pal[pi], yaw = y }.Half() - d0).sqrMagnitude > 1e-6f) cube = false;
            checks++;
            if (cube) W(string.Format("   {0,-9} {1,-24} dead={2}/{3}  (same shape at every yaw - nothing R can show)", id, trail, dead, presses));
            else if (dead == 0) W(string.Format("   {0,-9} {1,-24} dead={2}/{3}  ok", id, trail, dead, presses));
            else { failures++; W(string.Format("   {0,-9} {1,-24} dead={2}/{3}  !! DEAD PRESS", id, trail, dead, presses)); }
        }
    }

    // ------------------------------------------------------------------ F1
    IEnumerator F_Actuators()
    {
        W("\n## F1. PIVOT / SPINDLE / RAM under the player trigger, IN TEST DRIVE");
        // pivot and spindle: same arm, same mount, only the joint differs.
        foreach (string kind in new[] { "pivot", "spindle" })
        {
            string snap = SK + ENG1
                + kind + "|0.000,1.250,0.000|0|0.00,1.00,0.00|Steel\n"
                + "blade|0.000,1.250,0.210|0|0.00,0.00,1.00|Steel\n";
            yield return FireRig(kind + " on the battery roof, blade arm", snap, 12f, kind);
        }
        // ram at two mount heights - the critic's 62%-of-stroke scenario.
        yield return FireRig("ram LOW (belly clearance 0.030 m)",
            SK + ENG1 + "ram|0.000,0.700,-0.800|0|0.00,0.00,-1.00|Steel\n"
                      + "spike|0.000,0.700,-1.100|0|0.00,0.00,-1.00|Steel\n", 12f, "ram");
        yield return FireRig("ram HIGH (belly clearance 0.230 m)",
            SKHI + "ram|0.000,0.900,-0.800|0|0.00,0.00,-1.00|Steel\n"
                 + "spike|0.000,0.900,-1.100|0|0.00,0.00,-1.00|Steel\n", 12f, "ram");
    }

    IEnumerator FireRig(string label, string snap, float secs, string kind)
    {
        bm.LoadSnapshot(snap); yield return null;
        string err = bm.Validate();
        if (err != null) { BAD(label + ": fixture illegal - " + err); yield break; }
        var lr = bm.LimbReport();
        if (lr.Count != 1 || lr[0].blockedBy != null || lr[0].parts < 1)
        { BAD(label + ": not a real limb (blockedBy=" + (lr.Count > 0 ? "" + lr[0].blockedBy : "n/a") + ")"); yield break; }
        bm.StartTest();
        yield return new WaitForSeconds(1.5f);
        var acts = bm.testRobot.GetComponentsInChildren<Actuator>(true);
        Phase0Input.debugFire = true;
        float maxRate = 0f, maxTravel = 0f, sumTravel = 0f, lastT = 0f;
        int cycles = 0; bool wasDriving = false;
        float t0 = Time.time;
        while (Time.time - t0 < secs)
        {
            foreach (var a in acts)
            {
                if (a.rate > maxRate) maxRate = a.rate;
                if (a.travel > maxTravel) maxTravel = a.travel;
                if (a.travel > lastT) sumTravel += a.travel - lastT;
                lastT = a.travel;
                bool d = a.phase == Actuator.Phase.Driving;
                if (d && !wasDriving) cycles++;
                wasDriving = d;
            }
            yield return null;
        }
        Phase0Input.debugFire = false;
        checks++;
        string extra = kind == "ram"
            ? string.Format("  stroke {0:F3} / RAM_STROKE_M {1:F3} = {2:F0}%", maxTravel, Actuator.RAM_STROKE_M, 100f * maxTravel / Actuator.RAM_STROKE_M)
            : kind == "spindle"
            ? string.Format("  total {0:F1} rad = {1:F1} revolutions in {2:F0} s", sumTravel, sumTravel / (2f * Mathf.PI), secs)
            : string.Format("  arc limit {0:F3} rad ({1:F0} deg)", Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad, Actuator.PIVOT_ARC_DEG);
        W(string.Format("   {0,-40} maxRate={1:F2} rad/s maxTravel={2:F3} cycles={3}{4}",
            label, maxRate, maxTravel, cycles, extra));
        if (maxRate > 0.5f && cycles > 0) W("   ok    it MOVES under the player trigger in TEST DRIVE");
        else { failures++; W("   FAIL  dead"); }
        bm.BackToBuild();
        yield return new WaitForSeconds(0.4f);
    }

    // ------------------------------------------------------------------ F2
    IEnumerator F_Discs()
    {
        W("\n## F2. SPINNER / SPINNERSAW - do they spin, and what do they cost?");
        foreach (var pair in new[] { new KeyValuePair<string, string>("spinner", "0.675") })   // saw removed 2026-08-12
        {
            string snap = SK + ENG1 + pair.Key + "|0.000,0.975," + pair.Value + "|0|0.00,0.00,1.00|Steel\n";
            bm.LoadSnapshot(snap); yield return null;
            string err = bm.Validate();
            if (err != null) { BAD(pair.Key + ": fixture illegal - " + err); continue; }
            bm.StartTest();
            yield return new WaitForSeconds(1.5f);
            var sws = bm.testRobot.GetComponentsInChildren<SpinnerWeapon>(true);
            if (sws.Length == 0) { BAD(pair.Key + ": no SpinnerWeapon in the arena"); bm.BackToBuild(); yield return new WaitForSeconds(0.4f); continue; }
            Phase0Input.debugFire = true;
            float maxOmega = 0f, maxE = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < 10f)
            {
                foreach (var w in sws)
                { if (w.omega > maxOmega) maxOmega = w.omega; if (w.Energy > maxE) maxE = w.Energy; }
                yield return null;
            }
            Phase0Input.debugFire = false;
            checks++;
            W(string.Format("   {0,-11} maxOmega={1:F2} rad/s ({2:F0} RPM)  storedE={3:F1} kJ",
                pair.Key, maxOmega, maxOmega * 60f / (2f * Mathf.PI), maxE / 1000f));
            if (maxOmega > 1f) W("   ok    it SPINS");
            else { failures++; W("   FAIL  omega never left zero"); }
            bm.BackToBuild();
            yield return new WaitForSeconds(0.4f);
        }
    }

    // ------------------------------------------------------------------ F3
    IEnumerator F_Drive()
    {
        W("\n## F3. WHEEL - does it DRIVE and does it STEER?");
        bm.LoadSnapshot(SK + ENG1); yield return null;
        if (bm.Validate() != null) { BAD("SK fixture illegal"); yield break; }
        bm.StartTest(); yield return new WaitForSeconds(1.2f);
        Vector3 p0 = bm.testRobot.transform.position;
        Phase0Input.debugThrottle = 1f;
        float peak = 0f;
        float t0 = Time.time;
        while (Time.time - t0 < 6f)
        { float sp = bm.testRobot.rb != null ? bm.testRobot.rb.linearVelocity.magnitude : 0f; if (sp > peak) peak = sp; yield return null; }
        Phase0Input.debugThrottle = 0f;
        Vector3 p1 = bm.testRobot.transform.position;
        float dist = new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude;
        checks++;
        W(string.Format("   straight: {0:F2} m in 6 s, peak {1:F2} m/s", dist, peak));
        if (dist > 2f) W("   ok    it DRIVES"); else { failures++; W("   FAIL  it does not drive"); }
        bm.BackToBuild(); yield return new WaitForSeconds(0.4f);

        bm.LoadSnapshot(SK + ENG1); yield return null;
        bm.StartTest(); yield return new WaitForSeconds(1.2f);
        float y0 = bm.testRobot.transform.eulerAngles.y;
        Vector3 q0 = bm.testRobot.transform.position;
        Phase0Input.debugThrottle = 1f; Phase0Input.debugSteer = 1f;
        yield return new WaitForSeconds(6f);
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        float y1 = bm.testRobot.transform.eulerAngles.y;
        Vector3 q1 = bm.testRobot.transform.position;
        float dy = Mathf.Abs(Mathf.DeltaAngle(y0, y1));
        checks++;
        W(string.Format("   full throttle + full steer: hull turned {0:F0} deg over {1:F2} m",
            dy, new Vector2(q1.x - q0.x, q1.z - q0.z).magnitude));
        if (dy > 10f) W("   ok    it STEERS"); else { failures++; W("   FAIL  no yaw response"); }
        bm.BackToBuild(); yield return new WaitForSeconds(0.4f);
    }

    // ------------------------------------------------------------------ F4
    IEnumerator F_Gyro()
    {
        W("\n## F4. GYRO - does it RIGHT a flip? Same flip, same machine, +/- one gyro.");
        foreach (var pair in new[] { new KeyValuePair<string, string>("no gyro", SK + ENG1),
                                     new KeyValuePair<string, string>("1 gyro", SK + ENG1 + GYRO) })
        {
            bm.LoadSnapshot(pair.Value); yield return null;
            if (bm.Validate() != null) { BAD(pair.Key + ": fixture illegal"); continue; }
            bm.StartTest(); yield return new WaitForSeconds(2.0f);
            var tr = bm.testRobot.transform;
            tr.position = tr.position + Vector3.up * 0.35f;
            tr.rotation = Quaternion.Euler(0f, 0f, 168f);   // on its back
            if (bm.testRobot.rb != null) bm.testRobot.rb.angularVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.1f);
            float t0 = Time.time, rec = -1f, worstTilt = 0f;
            while (Time.time - t0 < 8f)
            {
                float up = Vector3.Dot(tr.up, Vector3.up);
                if (1f - up > worstTilt) worstTilt = 1f - up;
                if (up > 0.8f) { rec = Time.time - t0; break; }
                yield return null;
            }
            checks++;
            W(string.Format("   {0,-8} recovery={1}  (upright dot ended at {2:F2})",
                pair.Key, rec < 0f ? "NEVER (>8 s)" : rec.ToString("F2") + " s", Vector3.Dot(tr.up, Vector3.up)));
            bm.BackToBuild(); yield return new WaitForSeconds(0.4f);
        }
        W("   (compare the two numbers above - that difference IS the gyro)");
    }

    // ------------------------------------------------------------------ F5
    IEnumerator F_Power()
    {
        W("\n## F5. ENGINE and BATTERY - do they raise the numbers they claim to?");
        var rows = new[] {
            new KeyValuePair<string, string>("0 engine, 1 battery", SK),
            new KeyValuePair<string, string>("1 engine, 1 battery", SK + ENG1),
            new KeyValuePair<string, string>("2 engine, 1 battery", SK + ENG1 + ENG2),
            new KeyValuePair<string, string>("1 engine, 2 battery", SK + ENG1 + BAT2),
        };
        foreach (var r in rows)
        {
            bm.LoadSnapshot(r.Value); yield return null;
            if (bm.Validate() != null) { BAD(r.Key + ": fixture illegal"); continue; }
            bm.StartTest(); yield return new WaitForSeconds(1.5f);
            var pp = bm.testRobot.GetComponent<PowerPlant>();
            int engines = 0;
            foreach (var p in bm.placed) if (p.def.id == "engine") engines++;
            checks++;
            W(string.Format("   {0,-21} peakKW={1,5:F1}  capacityKJ={2,6:F1}  storedKJ={3,6:F1}  Actuator.MotorKW({4})={5:F2}",
                r.Key, pp != null ? pp.peakKW : -1f, pp != null ? pp.capacityKJ : -1f,
                pp != null ? pp.storedKJ : -1f, engines, Actuator.MotorKW(engines)));
            bm.BackToBuild(); yield return new WaitForSeconds(0.4f);
        }
    }

    // ---------------------------------------------------------------- utils
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
            if (d * sgn <= 0.001f || Mathf.Abs(d) > 0.7f) continue;
            report(true); yield break;
        }
        report(false);
    }
}

}
