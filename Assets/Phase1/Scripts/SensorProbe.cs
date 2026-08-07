using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// P1 acceptance harness (design doc v1.1 §9 P1). Runs the sensor palette
/// through the accept criteria on a purpose-built fixture:
///   1. placement — the fixture's five sensors load, spawn, and register a bus
///   2. rangefinder reads a spawned target at a known distance ±5 cm, tag "wall"
///   3. tilt flips sign when the machine is turned over
///   4. a detached sensor reads NO SIGNAL (and comes back when restored)
///   5. determinism — two fresh spawns of the same scenario produce the same
///      first-second reading trace
///   6. idle draw — a parked machine with 5 sensors drains ~0.45 kW
///   7. prices and masses match the design table; scouting line names the kit
/// Sandbox career state (Career.active = false) — a rule test a shop can veto
/// is measuring the shop (RotorProbe lesson).
/// </summary>
public class SensorProbe : MonoBehaviour
{
    public static bool finished;
    public static string report = "";
    static int passed, failed;
    static readonly List<string> log = new List<string>();

    public static SensorProbe Run()
    {
        finished = false; passed = failed = 0; log.Clear(); report = "";
        return new GameObject("sensor_probe").AddComponent<SensorProbe>();
    }

    static void Check(bool ok, string what)
    {
        if (ok) passed++; else failed++;
        log.Add((ok ? "PASS  " : "FAIL  ") + what);
    }

    // core + 2 beams + battery + 5 sensors (body) + 4 wheels. No gyro on
    // purpose: the tilt test turns the machine over and nothing must right it.
    const string FIXTURE =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
        "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\n" +
        "rangefinder|0.000,0.700,0.840|0|0.00,0.00,1.00|Aluminum\n" +
        "compass|0.000,0.880,-0.450|0|0.00,1.00,0.00|Aluminum\n" +
        "tiltsensor|0.000,1.180,0.000|0|0.00,1.00,0.00|Aluminum\n" +
        "wallsensor|0.000,0.700,-0.840|0|0.00,0.00,-1.00|Aluminum\n" +
        "trapsensor|-0.230,0.700,0.000|0|-1.00,0.00,0.00|Aluminum\n" +
        "dmgbus|0.230,0.700,0.000|0|1.00,0.00,0.00|Aluminum\n" +
        "wheel|0.170,0.700,0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,0.450|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.170,0.700,-0.450|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.170,0.700,-0.450|0|-1.00,0.00,0.00|Rubber\n";

    IEnumerator Start()
    {
        yield return null;
        var bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Check(false, "builder present"); Finish(); yield break; }

        bool savedActive = Career.active;
        Career.active = false;   // sandbox: the stock gate must not veto a sensor test
        // Owner-state hygiene (the HazardBench leftover-build lesson, same
        // day): whatever build is in the bay goes back when the probe ends,
        // or the next harness measures OUR fixture instead of its own world.
        string savedBay = bm.SnapshotString();

        // ---- 7. table checks (no scene needed) --------------------------
        Check(CareerDB.PartPrice("rangefinder", "Aluminum") == 120, "rangefinder priced 120");
        Check(CareerDB.PartPrice("compass", "Aluminum") == 180, "compass priced 180");
        Check(CareerDB.PartPrice("tiltsensor", "Aluminum") == 60, "tilt sensor priced 60");
        Check(CareerDB.PartPrice("wallsensor", "Aluminum") == 90, "wall sensor priced 90");
        Check(CareerDB.PartPrice("trapsensor", "Aluminum") == 70, "trap sensor priced 70");
        Check(CareerDB.PartPrice("dmgbus", "Aluminum") == 70, "damage bus priced 70");
        float[] wantKg = { 8f, 10f, 4f, 6f, 4f, 3f };
        string[] ids = { "rangefinder", "compass", "tiltsensor", "wallsensor", "trapsensor", "dmgbus" };
        for (int i = 0; i < ids.Length; i++)
        {
            var d = CareerDB.Def(ids[i]);
            float m = d != null ? d.Mass() : -1f;
            Check(d != null && Mathf.Abs(m - wantKg[i]) < 0.35f,
                  ids[i] + " mass ~" + wantKg[i] + " kg (" + m.ToString("F1") + ")");
        }

        // ---- 1. load + spawn -------------------------------------------
        bm.BackToBuild();
        yield return null;
        int n = bm.LoadSnapshot(FIXTURE);
        Check(n == 14, "fixture loads 14 parts (" + n + ")");
        Check(SensorBus.LoadoutLine(bm.placed).Contains("Rangefinder"),
              "scouting loadout line names the kit");
        bm.StartTest();
        yield return null;
        var bot = bm.testRobot;
        var bus = bot != null ? bot.GetComponent<SensorBus>() : null;
        Check(bot != null, "fixture spawns");
        Check(bus != null, "SensorBus registered");
        if (bus == null) { Restore(bm, savedActive, savedBay); Finish(); yield break; }
        Check(bus.LiveCount() == 6, "6 live sensors (" + bus.LiveCount() + ")");
        Check(bus.target != null, "compass target wired to the dummy");

        yield return new WaitForSeconds(1.5f);   // settle onto suspension

        // ---- 2. rangefinder at a known distance ------------------------
        int rfIdx = PartIdx(bot, "rangefinder");
        Check(rfIdx >= 0, "rangefinder part present on the body");
        GameObject wallCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wallCube.name = "sensor_probe_wall";
        wallCube.transform.localScale = new Vector3(2f, 2f, 0.30f);
        {
            Vector3 origin = bot.parts[rfIdx].go.transform.position;
            Vector3 dir = bot.transform.forward;
            wallCube.transform.position = origin + dir * (3.0f + 0.15f);
            wallCube.transform.rotation = bot.transform.rotation;
        }
        yield return new WaitForSeconds(0.25f);  // several 20 Hz refreshes
        {
            Vector3 origin = bot.parts[rfIdx].go.transform.position;
            Vector3 faceP = wallCube.transform.position - bot.transform.forward * 0.15f;
            float expect = Vector3.Dot(faceP - origin, bot.transform.forward);
            Check(bus.rangeValid, "rangefinder valid");
            Check(Mathf.Abs(bus.rangeDist - expect) <= 0.05f,
                  "rangefinder ±5 cm (read " + bus.rangeDist.ToString("F3")
                  + " vs " + expect.ToString("F3") + ")");
            Check(bus.rangeTag == "wall", "rangefinder tags the block 'wall' (" + bus.rangeTag + ")");
        }
        Object.Destroy(wallCube);

        // ---- 3. tilt flips ---------------------------------------------
        Check(bus.tiltValid && bus.upY > 0.9f && !bus.flipped, "tilt reads upright");
        Vector3 savedPos = bot.transform.position;
        Quaternion savedRot = bot.transform.rotation;
        bot.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
        bot.transform.position = savedPos + Vector3.up * 0.8f;
        VelUtil.SetLinearVelocity(bot.rb, Vector3.zero);
        bot.rb.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(0.2f);
        Check(bus.tiltValid && bus.upY < -0.5f && bus.flipped,
              "tilt sees the flip (upY " + bus.upY.ToString("F2") + ")");
        bot.transform.rotation = savedRot;
        bot.transform.position = savedPos + Vector3.up * 0.3f;
        VelUtil.SetLinearVelocity(bot.rb, Vector3.zero);
        bot.rb.angularVelocity = Vector3.zero;
        yield return new WaitForSeconds(1.0f);

        // ---- 4. no-signal on detach ------------------------------------
        bot.parts[rfIdx].detached = true;
        yield return new WaitForSeconds(0.15f);
        Check(!bus.rangeValid, "detached rangefinder reads no-signal");
        bool saysNoSignal = false;
        foreach (var l in bus.TelemetryLines()) if (l.Contains("no signal")) saysNoSignal = true;
        Check(saysNoSignal, "telemetry strip says 'no signal'");
        int liveAfter = bus.LiveCount();
        Check(liveAfter == 5, "live count drops to 5 (" + liveAfter + ")");
        bot.parts[rfIdx].detached = false;
        yield return new WaitForSeconds(0.15f);
        Check(bus.rangeValid, "restored sensor reads again");

        // ---- 6. idle draw ----------------------------------------------
        var pp = bot.GetComponent<PowerPlant>();
        if (pp != null && bot.combatEnabled)
        {
            float kj0 = pp.storedKJ;
            yield return new WaitForSeconds(1.0f);
            float spent = kj0 - pp.storedKJ;
            Check(spent > 0.25f && spent < 0.75f,
                  "parked idle draw ~0.45 kJ/s (" + spent.ToString("F2") + ")");
        }
        else Check(pp != null, "power plant present for idle-draw check (combat "
                   + (bot.combatEnabled ? "armed" : "NOT ARMED") + ")");

        // ---- 5. determinism: two fresh spawns, same trace ---------------
        string traceA = null, traceB = null;
        yield return TraceRun(bm, s => traceA = s);
        yield return TraceRun(bm, s => traceB = s);
        Check(traceA != null && traceA == traceB,
              "deterministic: two runs read identically"
              + (traceA == traceB ? "" : "\n  A: " + traceA + "\n  B: " + traceB));

        Restore(bm, savedActive, savedBay);
        Finish();
    }

    static int PartIdx(CompoundRobot bot, string idPrefix)
    {
        for (int i = 0; i < bot.parts.Count; i++)
            if (bot.parts[i].spec.id.StartsWith(idPrefix)) return i;
        return -1;
    }

    /// <summary>Fresh spawn of the SAME fixture, then 10 samples over ~1 s on
    /// the fixed clock. Identical initial state must read identically.</summary>
    IEnumerator TraceRun(BuilderManager bm, System.Action<string> done)
    {
        bm.BackToBuild();
        yield return null;
        bm.LoadSnapshot(FIXTURE);
        bm.StartTest();
        yield return null;
        var bot = bm.testRobot;
        var bus = bot != null ? bot.GetComponent<SensorBus>() : null;
        if (bus == null) { done(null); yield break; }
        var sb = new StringBuilder();
        for (int s = 0; s < 10; s++)
        {
            for (int k = 0; k < 5; k++) yield return new WaitForFixedUpdate();
            sb.Append(bus.rangeDist.ToString("F2")).Append('/')
              .Append(bus.enemyBearingDeg.ToString("F1")).Append('/')
              .Append(bus.upY.ToString("F2")).Append('/')
              .Append(bus.wallDist.ToString("F1")).Append('/')
              .Append(bus.hpFrac.ToString("F2")).Append(' ');
        }
        done(sb.ToString());
    }

    void Restore(BuilderManager bm, bool savedActive, string savedBay)
    {
        bm.BackToBuild();
        if (!string.IsNullOrEmpty(savedBay)) bm.LoadSnapshot(savedBay);
        Career.active = savedActive;
    }

    void Finish()
    {
        var sb = new StringBuilder();
        foreach (var l in log) { Debug.Log("[SensorProbe] " + l); sb.AppendLine(l); }
        report = sb.ToString();
        string line = string.Format("[SensorProbe] RESULT: {0} pass, {1} fail{2}",
            passed, failed, failed == 0 ? " - ALL GREEN" : " - FIX NEEDED");
        Debug.Log(line);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(Application.dataPath, "Phase1/qa_sensor_probe.txt"),
                report + line + "\n");
        }
        catch (System.Exception e) { Debug.LogWarning("[SensorProbe] write failed: " + e.Message); }
        finished = true;
        Object.Destroy(gameObject);
    }
}
}
