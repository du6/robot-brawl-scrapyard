
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ROUND-2 CRITIC part-function proof. Every part gets a NUMBER measured in
// TEST DRIVE, on fixtures that are the proven skeleton plus the part under
// test. Weapons are proven by hitting the ram dummy, which is teleported to a
// fixed spot ahead of the player so the measurement is repeatable instead of
// depending on whether a coroutine can steer.
public class UP2Func : MonoBehaviour
{
    public string outFile = "qa_up2_func.txt";
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

    void W(string s) { File.AppendAllText(path, s + "\n"); Debug.Log("[UP2F] " + s); }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        File.WriteAllText(path, "# UP2 CRITIC part-function proof\n");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no bm"); done = true; yield break; }

        yield return Actuators();
        yield return Weapons();
        yield return Mobility();
        yield return Power();

        W("\n## FUNC DONE");
        Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        Time.timeScale = 1f;
        done = true;
    }

    IEnumerator Enter(string snap)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(snap);
        yield return null;
        string v = bm.Validate();
        if (v != null) { W("   !! fixture invalid: " + v); yield break; }
        bm.StartTest();
        for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();
    }

    IEnumerator Leave()
    {
        Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        bm.BackToBuild();
        yield return null;
    }

    // --------------------------------------------------------- 1. actuators
    IEnumerator Actuators()
    {
        W("\n## 1. ACTUATORS - rate / travel / cycles with the trigger held 10 s");
        var rows = new[] {
            new[]{"pivot",   "blade"},
            new[]{"spindle", "blade"},
            new[]{"ram",     "spike"},
        };
        foreach (var row in rows)
        {
            // actuator on TOP of the aft chassis, arm on top of it: the arm runs
            // perpendicular to the hinge axis (+Y), so the radius is non-zero.
            string snap = SKEL
                + row[0] + "|0.000,1.000,-0.400|0|0.00,1.00,0.00|Steel\n"
                + row[1] + "|0.000,1.325,-0.400|0|0.00,1.00,0.00|Steel\n";
            yield return Enter(snap);
            var lr = bm.LimbReport();
            string lrs = lr.Count > 0 ? string.Format("parts={0} blockedBy={1} tipR={2:F3}",
                lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius) : "NO-LIMB";
            if (bm.testRobot == null) { W(row[0] + ": no test robot"); yield return Leave(); continue; }
            var acts = bm.testRobot.GetComponentsInChildren<Actuator>(true);
            if (acts.Length == 0) { W(row[0] + ": NO ACTUATOR COMPONENT (" + lrs + ")"); yield return Leave(); continue; }
            var a = acts[0];
            Phase0Input.debugFire = true;
            float maxRate = 0f, maxTravel = 0f;
            int cycles = 0; var ph = a.phase;
            var seen = new List<string>();
            float t = 0f;
            while (t < 10f)
            {
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
                maxRate = Mathf.Max(maxRate, Mathf.Abs(a.rate));
                maxTravel = Mathf.Max(maxTravel, Mathf.Abs(a.travel));
                if (a.phase != ph)
                {
                    if (!seen.Contains(a.phase.ToString())) seen.Add(a.phase.ToString());
                    if (a.phase == Actuator.Phase.Driving) cycles++;
                    ph = a.phase;
                }
            }
            Phase0Input.debugFire = false;
            string extra = row[0] == "ram"
                ? string.Format("stroke {0:F3}/{1:F3} m = {2:F0}%", maxTravel, Actuator.RAM_STROKE_M, 100f * maxTravel / Actuator.RAM_STROKE_M)
                : row[0] == "spindle"
                ? string.Format("{0:F1} rad/s = {1:F0} RPM", maxRate, maxRate * 60f / (Mathf.PI * 2f))
                : string.Format("arc {0:F3} rad of {1:F3}", maxTravel, Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad);
            W(string.Format("{0,-8} playerControlled={1} maxRate={2:F2} maxTravel={3:F3} cycles={4} phases[{5}]  {6}  | builder {7}",
                row[0], a.playerControlled, maxRate, maxTravel, cycles, string.Join(",", seen.ToArray()), extra, lrs));
            yield return Leave();
        }
    }

    // ----------------------------------------------------------- 2. weapons
    // Same chassis, same actuator, same 12 s, dummy parked 1.15 m dead ahead
    // and re-parked every 2 s so a shove does not end the measurement.
    IEnumerator Weapons()
    {
        W("\n## 2. WEAPON BITE - identical rig, dummy parked ahead, 12 s of trigger");
        W("   tip = the part on the pivot; direct = bolted to the chassis, no actuator");
        var cases = new[] {
            new[]{"blade","pivot"}, new[]{"wedge","pivot"}, new[]{"hook","pivot"}, new[]{"spike","pivot"},
            new[]{"spinner","-"}, new[]{"spike","-"}, new[]{"none","-"},   // saw removed 2026-08-12
        };
        foreach (var c in cases)
        {
            string snap = SKEL;
            if (c[1] == "pivot")
                snap += "pivot|0.000,0.700,0.775|0|0.00,0.00,1.00|Steel\n"
                      + c[0] + "|0.000,0.700,1.075|0|0.00,0.00,1.00|Steel\n";
            else if (c[0] != "none")
                snap += c[0] + "|0.000,0.700,0.775|0|0.00,0.00,1.00|Steel\n";
            yield return Enter(snap);
            if (bm.testRobot == null || bm.dummyRobot == null) { W(c[0] + "/" + c[1] + ": no bodies"); yield return Leave(); continue; }

            var lr = bm.LimbReport();
            string lrs = lr.Count > 0 ? "limb " + lr[0].parts + " blocked=" + (lr[0].blockedBy ?? "null") : "no-actuator";

            var pr = bm.testRobot; var du = bm.dummyRobot;
            Vector3 fwd = pr.transform.forward;
            float hp0 = HP(du), php0 = HP(pr);
            float dy0 = du.transform.position.y;
            float maxRise = 0f, maxPull = 0f;
            Vector3 anchor = pr.transform.position;
            Phase0Input.debugFire = true;
            float t = 0f, repark = 0f;
            while (t < 12f)
            {
                if (repark <= 0f)
                {
                    Vector3 tgt = pr.transform.position + pr.transform.forward * 1.15f;
                    du.transform.position = new Vector3(tgt.x, dy0, tgt.z);
                    if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                    repark = 2f;
                    anchor = pr.transform.position;
                }
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                maxRise = Mathf.Max(maxRise, du.transform.position.y - dy0);
                // PULL = dummy dragged back toward the attacker (distance shrinks)
                float d = Vector3.Distance(new Vector3(du.transform.position.x, 0f, du.transform.position.z),
                                           new Vector3(anchor.x, 0f, anchor.z));
                maxPull = Mathf.Max(maxPull, 1.15f - d);
            }
            Phase0Input.debugFire = false;
            float dealt = hp0 - HP(du);
            float taken = php0 - HP(pr);
            W(string.Format("{0,-11}{1,-7} dealt={2,7:F1} hp  selfDmg={3,6:F1}  maxLift={4:F3} m  maxPull={5:F3} m  {6}",
                c[0], c[1] == "pivot" ? "(tip)" : "(direct)", dealt, taken, maxRise, maxPull, lrs));
            yield return Leave();
        }
    }

    static float HP(CompoundRobot r)
    {
        if (r == null) return 0f;
        float s = 0f;
        foreach (var p in r.parts) if (!p.detached) s += p.hp;
        return s;
    }

    // ---------------------------------------------------------- 3. mobility
    IEnumerator Mobility()
    {
        W("\n## 3. WHEELS + GYRO");
        // DRIVE
        yield return Enter(SKEL);
        if (bm.testRobot != null)
        {
            Vector3 p0 = bm.testRobot.transform.position;
            Phase0Input.debugThrottle = 1f;
            float peak = 0f; float t = 0f;
            while (t < 5f) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                peak = Mathf.Max(peak, bm.testRobot.rb != null ? bm.testRobot.rb.linearVelocity.magnitude : 0f); }
            Phase0Input.debugThrottle = 0f;
            W(string.Format("wheel DRIVE: {0:F2} m in 5 s, peak {1:F2} m/s", Vector3.Distance(p0, bm.testRobot.transform.position), peak));
        }
        yield return Leave();

        // STEER, in slow motion so the 14 m box does not end the run
        yield return Enter(SKEL);
        if (bm.testRobot != null)
        {
            Time.timeScale = 0.15f;
            float y0 = bm.testRobot.transform.eulerAngles.y;
            Phase0Input.debugThrottle = 0.6f; Phase0Input.debugSteer = 1f;
            float t = 0f, maxYawRate = 0f, turned = 0f, prev = y0;
            while (t < 3.5f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                float y = bm.testRobot.transform.eulerAngles.y;
                turned += Mathf.Abs(Mathf.DeltaAngle(prev, y)); prev = y;
                if (bm.testRobot.rb != null) maxYawRate = Mathf.Max(maxYawRate, Mathf.Abs(bm.testRobot.rb.angularVelocity.y));
            }
            Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
            Time.timeScale = 1f;
            W(string.Format("wheel STEER: cumulative yaw {0:F1} deg, peak yaw rate {1:F3} rad/s", turned, maxYawRate));
        }
        yield return Leave();

        // GYRO: flip the bot and time the recovery, with and without
        foreach (bool withGyro in new[] { false, true })
        {
            string snap = SKEL + (withGyro ? "gyro|0.000,0.975,-0.400|0|0.00,0.00,0.00|Aluminum\n" : "");
            yield return Enter(snap);
            if (bm.testRobot == null) { yield return Leave(); continue; }
            var rb = bm.testRobot.rb;
            bm.testRobot.transform.rotation = Quaternion.Euler(0f, 0f, 175f);
            bm.testRobot.transform.position += Vector3.up * 0.6f;
            if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            float t = 0f, rec = -1f;
            while (t < 6f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                if (rec < 0f && Vector3.Dot(bm.testRobot.transform.up, Vector3.up) > 0.7f) rec = t;
            }
            var g = bm.testRobot.GetComponentInChildren<GyroStabilizer>(true);
            W(string.Format("gyro RIGHTING withGyro={0}: recovered at {1}  (gyro comp {2}, active={3})",
                withGyro, rec < 0f ? "NEVER in 6 s" : rec.ToString("F2") + " s",
                g != null ? "present" : "absent", g != null ? g.active.ToString() : "-"));
            yield return Leave();
        }
    }

    // ------------------------------------------------------------- 4. power
    IEnumerator Power()
    {
        W("\n## 4. ENGINE + BATTERY");
        W(string.Format("Actuator.MotorKW(0/1/2/3) = {0:F2} / {1:F2} / {2:F2} / {3:F2} kW",
            Actuator.MotorKW(0), Actuator.MotorKW(1), Actuator.MotorKW(2), Actuator.MotorKW(3)));
        // engines: 0, 1, 2 (battery keeps the build legal at 0 engines)
        string[] snaps = {
            SKEL.Replace("engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n", ""),
            SKEL,
            SKEL + "engine|0.000,0.975,-0.400|0|0.00,0.00,0.00|Steel\n",
        };
        for (int e = 0; e < snaps.Length; e++)
        {
            yield return Enter(snaps[e]);
            var pp = bm.testRobot != null ? bm.testRobot.GetComponentInChildren<PowerPlant>(true) : null;
            W(string.Format("engines={0}: peakKW={1:F2} capacityKJ={2:F1} storedKJ={3:F1}",
                e, pp != null ? pp.peakKW : -1f, pp != null ? pp.capacityKJ : -1f, pp != null ? pp.storedKJ : -1f));
            yield return Leave();
        }
        // batteries: 1 vs 2, plus how long a held spindle runs before flat
        for (int b = 1; b <= 2; b++)
        {
            string snap = SKEL + (b == 2 ? "battery|0.000,1.250,0.000|0|0.00,0.00,0.00|ABS\n" : "")
                + "spindle|0.000,0.700,-0.775|0|0.00,0.00,-1.00|Steel\n"
                + "spinner|0.000,0.700,-1.100|0|0.00,0.00,-1.00|Tungsten\n";   // was the saw; removed 2026-08-12
            yield return Enter(snap);
            var pp = bm.testRobot != null ? bm.testRobot.GetComponentInChildren<PowerPlant>(true) : null;
            if (pp == null) { W("batteries=" + b + ": no powerplant"); yield return Leave(); continue; }
            float cap = pp.capacityKJ, st0 = pp.storedKJ;
            Phase0Input.debugFire = true; Phase0Input.debugThrottle = 1f;
            float t = 0f, flatAt = -1f;
            while (t < 25f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                if (flatAt < 0f && pp.Flat) flatAt = t;
            }
            Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f;
            W(string.Format("batteries={0}: capacityKJ={1:F1} stored {2:F1} -> {3:F1}, flat at {4}, SecondsLeft={5:F1}",
                b, cap, st0, pp.storedKJ, flatAt < 0f ? "never in 25 s" : flatAt.ToString("F1") + " s", pp.SecondsLeft()));
            yield return Leave();
        }
    }
}
}
