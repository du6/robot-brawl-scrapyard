using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// ROUND-1 CRITIC. "It compiled" and "it placed" are not evidence that a part
/// WORKS. This drives one build in TEST mode and produces a NUMBER for each
/// claim: does the spindle spin, does the pivot swing and return, does the ram
/// reach its stroke, does the wheel move the bot, does the gyro right it, does
/// the engine raise the motor rating, does the battery hold more.
/// </summary>
public class FuncProbe : MonoBehaviour
{
    public BuilderManager bm;
    public string snapshot = "";
    public string label = "BUILD";
    public string mode = "fire";        // fire | drive | steer | flip | idle
    public float seconds = 6f;
    public float settle = 1.4f;
    public string outPath = "qa_up1_func.txt";

    public bool done;
    public string summary = "";
    public string err = "";

    void W(string s)
    {
        File.AppendAllText(Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outPath), s + "\n");
    }

    IEnumerator Start()
    {
        Time.timeScale = 1f;
        if (bm == null) bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { err = "no BuilderManager"; done = true; yield break; }
        if (snapshot.Length > 0)
        {
            bm.LoadSnapshot(snapshot);
            yield return null;
            string v = bm.Validate();
            if (v != null) { err = "invalid build: " + v; summary = err; W("[" + label + "] " + err); done = true; yield break; }
        }
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        yield return null;
        bm.StartTest();
        yield return null;
        var bot = bm.testRobot;
        if (bot == null) { err = "no test robot"; summary = err; done = true; yield break; }

        var acts = new List<Actuator>(bot.GetComponentsInChildren<Actuator>(true));
        var spins = new List<SpinnerWeapon>(bot.GetComponentsInChildren<SpinnerWeapon>(true));
        var pw = bot.GetComponentInChildren<PowerPlant>(true);
        var gy = bot.GetComponentInChildren<GyroStabilizer>(true);

        // let it settle on its suspension before anything is measured
        float ts = Time.time;
        while (Time.time - ts < settle) yield return new WaitForFixedUpdate();

        float kj0 = pw != null ? pw.storedKJ : 0f;
        float cap = pw != null ? pw.capacityKJ : 0f;
        float peak = pw != null ? pw.peakKW : 0f;
        Vector3 p0 = bot.transform.position;
        Vector3 f0 = bot.transform.forward;

        Phase0Input.debugFire = (mode == "fire");
        Phase0Input.debugThrottle = (mode == "drive" || mode == "steer") ? 1f : 0f;
        Phase0Input.debugSteer = (mode == "steer") ? 1f : 0f;

        if (mode == "flip")
        {
            // Put it on its back, in the air by a hand's width, and let go.
            bot.rb.position = new Vector3(p0.x, p0.y + 0.45f, p0.z);
            bot.rb.rotation = Quaternion.Euler(0f, 0f, 180f) * bot.rb.rotation;
            bot.rb.linearVelocity = Vector3.zero;
            bot.rb.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }

        int n = acts.Count;
        var maxRate = new float[n]; var maxTravel = new float[n];
        var cycles = new int[n]; var lastPhase = new Actuator.Phase[n];
        var phaseSeen = new List<string>();
        for (int i = 0; i < n; i++) { lastPhase[i] = acts[i].phase; maxRate[i] = 0f; }
        float maxOmega = 0f, maxSpeed = 0f, minStored = kj0, minUp = 1f;
        float flipRecoverAt = -1f;
        float t0 = Time.time;
        int samples = 0;

        while (Time.time - t0 < seconds)
        {
            yield return new WaitForFixedUpdate();
            samples++;
            if (bot == null) break;
            for (int i = 0; i < n; i++)
            {
                var a = acts[i]; if (a == null) continue;
                float r = Mathf.Abs(a.rate); if (r > maxRate[i]) maxRate[i] = r;
                float tv = Mathf.Abs(a.travel); if (tv > maxTravel[i]) maxTravel[i] = tv;
                if (a.phase != lastPhase[i])
                {
                    if (phaseSeen.Count < 40) phaseSeen.Add(i + ":" + lastPhase[i] + ">" + a.phase);
                    if (a.phase == Actuator.Phase.Driving) cycles[i]++;
                    lastPhase[i] = a.phase;
                }
            }
            foreach (var s in spins) if (s != null && Mathf.Abs(s.omega) > maxOmega) maxOmega = Mathf.Abs(s.omega);
            if (pw != null && pw.storedKJ < minStored) minStored = pw.storedKJ;
            float sp = bot.rb.linearVelocity.magnitude; if (sp > maxSpeed) maxSpeed = sp;
            float up = Vector3.Dot(bot.transform.up, Vector3.up);
            if (up < minUp) minUp = up;
            if (flipRecoverAt < 0f && mode == "flip" && Time.time - t0 > 0.25f && up > 0.7f)
                flipRecoverAt = Time.time - t0;
        }

        Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;

        Vector3 p1 = bot != null ? bot.transform.position : p0;
        Vector3 f1 = bot != null ? bot.transform.forward : f0;
        float disp = new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude;
        float yawTurn = Vector3.SignedAngle(new Vector3(f0.x, 0f, f0.z), new Vector3(f1.x, 0f, f1.z), Vector3.up);

        var sb = new System.Text.StringBuilder();
        sb.AppendFormat(CultureInfo.InvariantCulture,
            "[{0}] mode={1} {2:F1}s samples={3} | disp={4:F2}m maxSpeed={5:F2}m/s yaw={6:F0}deg | "
            + "cap={7:F0}kJ peak={8:F1}kW used={9:F1}kJ | minUp={10:F2}",
            label, mode, seconds, samples, disp, maxSpeed, yawTurn, cap, peak, kj0 - minStored, minUp);
        if (mode == "flip") sb.AppendFormat(CultureInfo.InvariantCulture, " recoverAt={0:F2}s gyros={1}",
            flipRecoverAt, gy != null ? gy.LiveGyros() : -1);
        if (spins.Count > 0) sb.AppendFormat(CultureInfo.InvariantCulture, " | discs={0} maxOmega={1:F2}rad/s", spins.Count, maxOmega);
        for (int i = 0; i < n; i++)
        {
            var a = acts[i];
            sb.AppendFormat(CultureInfo.InvariantCulture,
                "\n    act{0} {1} limb={2} motorKW={3:F2} tipCap={4:F1} inertia={5:F3} tipR={6:F3}"
                + " -> maxRate={7:F2} maxTravel={8:F3} (limit {9:F3}) cycles={10} phase={11}",
                i, a.kind, a.limb.Length, a.motorKW, a.tipSpeedCap, a.inertia, a.tipRadius,
                maxRate[i], maxTravel[i],
                a.kind == ActuatorKind.Ram ? Actuator.RAM_STROKE_M : Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad,
                cycles[i], a.phase);
        }
        if (phaseSeen.Count > 0)
        {
            sb.Append("\n    phases: ");
            for (int i = 0; i < phaseSeen.Count && i < 24; i++) sb.Append(phaseSeen[i]).Append("  ");
        }
        summary = sb.ToString();
        W(summary);
        if (bm.mode != BuilderManager.Mode.Build) bm.BackToBuild();
        done = true;
    }
}

}
