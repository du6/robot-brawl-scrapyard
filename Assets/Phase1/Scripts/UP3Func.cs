using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{
// ROUND-3 CRITIC, FUNCTION PROOF. Requirement (B): every part must be shown to
// WORK with a number, not to compile and not to place. Section-dispatched.
// Fixtures are loaded by snapshot (fast, allowed); every CLAIM ABOUT THE
// BUILDER lives in UP3Path and goes through the pointer.
public class UP3Func : MonoBehaviour
{
    public string outFile = "qa_up3_func.txt";
    public string section = "power";
    public bool done;
    public string summary = "";

    BuilderManager bm;
    string path;

    // core + 2 chassis + 4 wheels. Power parts are appended per rig.
    const string CHASSIS =
        "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
        "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
        "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" +
        "wheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\n" +
        "wheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\n";
    const string BAT1 = "battery|0.000,0.975,0.000|0|0.00,0.00,0.00|ABS\n";
    const string BAT2 = "battery|0.000,1.225,0.000|0|0.00,0.00,0.00|ABS\n";
    const string ENG1 = "engine|0.000,0.975,-0.400|0|0.00,0.00,0.00|Steel\n";
    const string ENG2 = "engine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\n";
    const string SKEL = CHASSIS + BAT1 + ENG1;

    void W(string s) { File.AppendAllText(path, s + "\n"); }

    static float HP(CompoundRobot r)
    { if (r == null) return 0f; float s = 0f; foreach (var p in r.parts) if (!p.detached) s += p.hp; return s; }

    IEnumerator Start()
    {
        path = Path.Combine(Path.Combine(Application.dataPath, "Phase1"), outFile);
        if (!File.Exists(path)) File.WriteAllText(path, "# UP3 CRITIC - FUNCTION PROOF (numbers, not placements)\n");
        W("\n===== SECTION " + section + " =====");
        bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { W("FATAL no bm"); done = true; yield break; }
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }

        if (section == "power") yield return SecPower();
        else if (section == "wheel") yield return SecWheel();
        else if (section == "act") yield return SecAct();
        else if (section == "spin") yield return SecSpin();
        else if (section == "arm") yield return SecArm();
        else if (section == "gyro") yield return SecGyro();
        else W("unknown section");

        Phase0Input.debugFire = false;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        W("## SECTION " + section + " DONE");
        done = true;
    }

    IEnumerator Enter(string snap, System.Action<bool> ok)
    {
        if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
        bm.LoadSnapshot(snap);
        yield return null;
        string v = bm.Validate();
        if (v != null) { W("   !! rig invalid: " + v); ok(false); yield break; }
        bm.StartTest();
        for (int i = 0; i < 110; i++) yield return new WaitForFixedUpdate();
        ok(bm.testRobot != null);
    }
    IEnumerator Leave()
    {
        Phase0Input.debugFire = false; Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        bm.BackToBuild(); yield return null;
    }

    // ------------------------------------------------------ ENGINE & BATTERY
    IEnumerator SecPower()
    {
        W("## ENGINE: does it raise PowerPlant.peakKW and Actuator.MotorKW?");
        W(string.Format("   static Actuator.MotorKW: 0eng={0:F2} 1eng={1:F2} 2eng={2:F2} 3eng={3:F2} 8eng={4:F2} (cap {5:F1})",
            Actuator.MotorKW(0), Actuator.MotorKW(1), Actuator.MotorKW(2),
            Actuator.MotorKW(3), Actuator.MotorKW(8), Actuator.MOTOR_KW_CAP));

        var rigs = new[] {
            new[]{"1 battery, 0 engines", CHASSIS + BAT1},
            new[]{"1 battery, 1 engine",  CHASSIS + BAT1 + ENG1},
            new[]{"1 battery, 2 engines", CHASSIS + BAT1 + ENG1 + ENG2},
            new[]{"2 batteries, 1 engine",CHASSIS + BAT1 + BAT2 + ENG1},
        };
        foreach (var rg in rigs)
        {
            bool ok = false; yield return Enter(rg[1], o => ok = o);
            if (!ok) { W("  " + rg[0] + ": no robot"); yield return Leave(); continue; }
            var pp = bm.testRobot.GetComponentInChildren<PowerPlant>(true);
            if (pp == null) { W("  " + rg[0] + ": NO POWERPLANT COMPONENT"); yield return Leave(); continue; }

            // runtime under a constant heavy load: full throttle until flat
            Phase0Input.debugThrottle = 1f;
            float t = 0f, flatAt = -1f; float minFrac = 1f;
            while (t < 40f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                minFrac = Mathf.Min(minFrac, pp.Frac);
                if (pp.Flat && flatAt < 0f) { flatAt = t; break; }
            }
            Phase0Input.debugThrottle = 0f;
            W(string.Format("  {0,-22} capacityKJ={1,7:F1} peakKW={2,6:F2} drawKW={3,6:F2} demandKW={4,6:F2} minFrac={5:F2} flatAt={6}",
                rg[0], pp.capacityKJ, pp.peakKW, pp.drawKW, pp.demandKW, minFrac,
                flatAt < 0f ? "never in 40s" : flatAt.ToString("F1") + "s"));
            yield return Leave();
        }
    }

    // ---------------------------------------------------------------- WHEELS
    IEnumerator SecWheel()
    {
        W("## WHEEL: does it DRIVE and STEER? displacement/heading under throttle");
        var rigs = new[] {
            new[]{"4 wheels", SKEL},
            new[]{"2 wheels (front only)",
                  "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\n" +
                  "chassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Aluminum\n" +
                  "chassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Aluminum\n" +
                  "wheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\n" +
                  "wheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\n" + BAT1 + ENG1},
        };
        foreach (var rg in rigs)
        {
            bool ok = false; yield return Enter(rg[1], o => ok = o);
            if (!ok) { W("  " + rg[0] + ": no robot"); yield return Leave(); continue; }
            var tr = bm.testRobot.transform;

            // idle control: no throttle, 2 s
            Vector3 i0 = tr.position;
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();
            float drift = Vector3.Distance(i0, tr.position);

            // forward
            Vector3 p0 = tr.position; float t = 0f; float vmax = 0f;
            Phase0Input.debugThrottle = 1f;
            while (t < 4f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                if (bm.testRobot.rb != null) vmax = Mathf.Max(vmax, bm.testRobot.rb.linearVelocity.magnitude);
            }
            Phase0Input.debugThrottle = 0f;
            float fwd = Vector3.Distance(p0, tr.position);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            // reverse
            Vector3 p1 = tr.position; t = 0f;
            Phase0Input.debugThrottle = -1f;
            while (t < 3f) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; }
            Phase0Input.debugThrottle = 0f;
            float rev = Vector3.Distance(p1, tr.position);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            // steer
            float h0 = tr.eulerAngles.y; t = 0f; float turned = 0f; float prev = h0;
            Phase0Input.debugSteer = 1f; Phase0Input.debugThrottle = 0.3f;
            while (t < 4f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                float cur = tr.eulerAngles.y;
                turned += Mathf.DeltaAngle(prev, cur); prev = cur;
            }
            Phase0Input.debugSteer = 0f; Phase0Input.debugThrottle = 0f;

            W(string.Format("  {0,-22} idleDrift={1:F3}m  fwd4s={2:F2}m ({3:F2} m/s peak)  rev3s={4:F2}m  steer4s={5:F0} deg",
                rg[0], drift, fwd, vmax, rev, turned));
            yield return Leave();
        }
    }

    // ------------------------------------------------ PIVOT / SPINDLE / RAM
    IEnumerator SecAct()
    {
        W("## ACTUATORS on a ROOF mount (hinge vertical, arm horizontal, floor cannot eat the arc)");
        // pivot on the rear chassis roof, blade on the pivot's +Z face
        string[] kinds = { "pivot", "spindle", "ram" };
        foreach (var k in kinds)
        {
            string rig = SKEL
                + k + "|0.000,1.000,0.400|0|0.00,1.00,0.00|Steel\n"
                + "blade|0.000,1.000,0.610|0|0.00,0.00,1.00|Steel\n";
            if (bm.mode != BuilderManager.Mode.Build) { bm.BackToBuild(); yield return null; }
            bm.LoadSnapshot(rig); yield return null;
            var lr = bm.LimbReport();
            string promise = "no limb";
            if (lr.Count > 0)
                promise = string.Format("parts={0} blockedBy={1} tipR={2:F3} rateMax={3:F2} arcFrac={4:F2}",
                    lr[0].parts, lr[0].blockedBy ?? "null", lr[0].tipRadius, lr[0].rateMax, lr[0].arcFrac);
            bool ok = false; yield return Enter(rig, o => ok = o);
            if (!ok) { W("  " + k + ": rig failed. builder said " + promise); yield return Leave(); continue; }
            var ac = bm.testRobot.GetComponentInChildren<Actuator>(true);
            if (ac == null) { W("  " + k + ": NO ACTUATOR COMPONENT IN THE ARENA. builder said " + promise); yield return Leave(); continue; }

            // control: 3 s with the trigger OFF
            float idleRate = 0f, idleTravel = 0f; float t = 0f;
            while (t < 3f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                idleRate = Mathf.Max(idleRate, Mathf.Abs(ac.rate));
                idleTravel = Mathf.Max(idleTravel, Mathf.Abs(ac.travel));
            }
            // fire
            Phase0Input.debugFire = true;
            float maxRate = 0f, maxTravel = 0f, minTravelAfterPeak = 999f;
            int cycles = 0; var ph = ac.phase; bool sawReturn = false; bool peaked = false;
            string phases = "";
            t = 0f;
            while (t < 12f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                float tv = Mathf.Abs(ac.travel);
                maxRate = Mathf.Max(maxRate, Mathf.Abs(ac.rate));
                if (tv > maxTravel) { maxTravel = tv; peaked = true; }
                if (peaked && maxTravel > 0.05f) minTravelAfterPeak = Mathf.Min(minTravelAfterPeak, tv);
                if (ac.phase != ph)
                {
                    if (phases.Length < 90) phases += ac.phase.ToString().Substring(0, 2) + ">";
                    if (ac.phase == Actuator.Phase.Driving) cycles++;
                    if (ac.phase == Actuator.Phase.Returning) sawReturn = true;
                    ph = ac.phase;
                }
            }
            Phase0Input.debugFire = false;
            float ceiling = k == "ram" ? Actuator.RAM_STROKE_M
                          : k == "pivot" ? Actuator.PIVOT_ARC_DEG * Mathf.Deg2Rad
                          : 0f;
            W(string.Format("  {0,-8} kind={1} {2}", k, ac.kind, promise));
            W(string.Format("           IDLE(3s, no trigger): peakRate={0:F3} peakTravel={1:F3}", idleRate, idleTravel));
            W(string.Format("           FIRE(12s): peakRate={0:F2} rad/s (MaxRate={1:F2})  maxTravel={2:F3}{3}  minAfterPeak={4:F3}  cycles={5} sawReturn={6}",
                maxRate, ac.MaxRate, maxTravel,
                ceiling > 0f ? string.Format(" of {0:F3} = {1:F0}%", ceiling, 100f * maxTravel / ceiling) : "",
                minTravelAfterPeak > 900f ? 0f : minTravelAfterPeak, cycles, sawReturn));
            W("           phases: " + phases);
            yield return Leave();
        }
    }

    // ---------------------------------------------------- SPINNER / SAW
    IEnumerator SecSpin()
    {
        W("## SPINNERS: omega ramp with the trigger held, then bite against the dummy");
        string[] ids = { "spinner", "spinnerSaw" };
        foreach (var id in ids)
        {
            float hz = id == "spinnerSaw" ? 0.03f : 0.05f;
            string rig = SKEL + string.Format("{0}|0.000,0.700,{1:F3}|0|0.00,0.00,1.00|Steel\n", id, 0.65f + hz);
            bool ok = false; yield return Enter(rig, o => ok = o);
            if (!ok) { W("  " + id + ": rig failed"); yield return Leave(); continue; }
            var sw = bm.testRobot.GetComponentInChildren<SpinnerWeapon>(true);
            if (sw == null) { W("  " + id + ": NO SPINNERWEAPON IN THE ARENA"); yield return Leave(); continue; }

            float idleOmega = 0f; float t = 0f;
            while (t < 2f) { yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime; idleOmega = Mathf.Max(idleOmega, Mathf.Abs(sw.omega)); }

            Phase0Input.debugFire = true;
            float[] mark = new float[6];
            t = 0f; int mi = 0;
            float[] at = { 0.5f, 1f, 2f, 3f, 5f, 8f };
            while (t < 8.05f)
            {
                yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                while (mi < 6 && t >= at[mi]) { mark[mi] = Mathf.Abs(sw.omega); mi++; }
            }
            W(string.Format("  {0,-11} idleOmega={1:F2}  omega @0.5/1/2/3/5/8s = {2:F1}/{3:F1}/{4:F1}/{5:F1}/{6:F1}/{7:F1} rad/s  (MaxOmega={8:F1}, E={9:F0} J, I={10:F4})",
                id, idleOmega, mark[0], mark[1], mark[2], mark[3], mark[4], mark[5], sw.MaxOmega, sw.Energy, sw.Inertia));

            // bite: repark the dummy in front every 2 s for 10 s
            var pr = bm.testRobot; var du = bm.dummyRobot;
            if (du == null) { W("           no dummy - bite untested"); Phase0Input.debugFire = false; yield return Leave(); continue; }
            float hp0 = HP(du), php0 = HP(pr); float dy0 = du.transform.position.y;
            float repark = 0f; t = 0f; float lowOmega = 999f;
            while (t < 10f)
            {
                if (repark <= 0f)
                {
                    Vector3 tg = pr.transform.position + pr.transform.forward * 1.10f;
                    du.transform.position = new Vector3(tg.x, dy0, tg.z);
                    if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                    repark = 2f;
                }
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                lowOmega = Mathf.Min(lowOmega, Mathf.Abs(sw.omega));
            }
            Phase0Input.debugFire = false;
            W(string.Format("           BITE 10s: dealt={0:F1} selfDamage={1:F1} omegaAfter={2:F1} omegaLow={3:F1}",
                hp0 - HP(du), php0 - HP(pr), Mathf.Abs(sw.omega), lowOmega));
            yield return Leave();
        }
    }

    // -------------------------------------------------- BLADE/WEDGE/HOOK/SPIKE
    IEnumerator SecArm()
    {
        W("## ARMS on a ROOF pivot (horizontal sweep, full arc), n=3 each, 8 s of trigger");
        W("   wedge is supposed to LIFT, hook is supposed to PULL - measured against the dummy");
        string[] arms = { "blade", "wedge", "hook", "spike" };
        foreach (var arm in arms)
        {
            float az = arm == "blade" ? 0.060f : arm == "wedge" ? 0.175f
                     : arm == "hook" ? 0.100f : 0.150f;
            string rig = SKEL
                + "pivot|0.000,1.000,0.400|0|0.00,1.00,0.00|Steel\n"
                + string.Format("{0}|0.000,1.000,{1:F3}|0|0.00,0.00,1.00|Steel\n", arm, 0.55f + az);
            float sDealt = 0f, sLift = 0f, sPull = 0f, sPush = 0f, sSelf = 0f;
            int n = 0;
            string per = "";
            for (int rep = 0; rep < 3; rep++)
            {
                bool ok = false; yield return Enter(rig, o => ok = o);
                if (!ok) { W("  " + arm + ": rig failed"); yield return Leave(); break; }
                var pr = bm.testRobot; var du = bm.dummyRobot;
                if (du == null) { W("  " + arm + ": no dummy"); yield return Leave(); break; }
                float hp0 = HP(du), php0 = HP(pr); float dy0 = du.transform.position.y;
                float lift = 0f, pull = 0f, push = 0f;
                Vector3 anchor = pr.transform.position, axis = pr.transform.forward;
                Phase0Input.debugFire = true;
                float t = 0f, repark = 0f;
                while (t < 8f)
                {
                    if (repark <= 0f)
                    {
                        axis = pr.transform.forward; anchor = pr.transform.position;
                        Vector3 tg = anchor + axis * 1.05f;
                        du.transform.position = new Vector3(tg.x, dy0, tg.z);
                        if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                        repark = 2f;
                    }
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                    lift = Mathf.Max(lift, du.transform.position.y - dy0);
                    float along = Vector3.Dot(du.transform.position - anchor, axis);
                    push = Mathf.Max(push, along - 1.05f);
                    pull = Mathf.Max(pull, 1.05f - along);
                }
                Phase0Input.debugFire = false;
                float d = hp0 - HP(du), sf = php0 - HP(pr);
                sDealt += d; sLift += lift; sPull += pull; sPush += push; sSelf += sf; n++;
                per += string.Format(" [{0:F0}]", d);
                yield return Leave();
            }
            if (n == 0) continue;
            W(string.Format("  {0,-7} n={1}  dealt avg={2,7:F1}{3}  self={4,6:F1}  lift={5:F3}  push={6:F3}  pull={7:F3}",
                arm, n, sDealt / n, per, sSelf / n, sLift / n, sPush / n, sPull / n));
        }

        W("\n## RAM ATTACK: same four arms mounted RIGID on the nose, driven into the dummy at full throttle");
        foreach (var arm in arms)
        {
            float rz = arm == "blade" ? 0.060f : arm == "wedge" ? 0.175f
                     : arm == "hook" ? 0.100f : 0.150f;
            string rig = SKEL + string.Format("{0}|0.000,0.700,{1:F3}|0|0.00,0.00,1.00|Steel\n", arm, 0.65f + rz);
            bool ok = false; yield return Enter(rig, o => ok = o);
            if (!ok) { W("  " + arm + ": rig failed (" + (bm.Validate() ?? "?") + ")"); yield return Leave(); continue; }
            var pr = bm.testRobot; var du = bm.dummyRobot;
            if (du == null) { W("  " + arm + ": no dummy"); yield return Leave(); continue; }
            float hp0 = HP(du), php0 = HP(pr); float dy0 = du.transform.position.y;
            float lift = 0f, pull = 0f;
            float t = 0f, repark = 0f;
            Phase0Input.debugThrottle = 1f;
            Vector3 anchor = pr.transform.position, axis = pr.transform.forward;
            while (t < 9f)
            {
                if (repark <= 0f)
                {
                    axis = pr.transform.forward; anchor = pr.transform.position;
                    Vector3 tg = anchor + axis * 1.6f;
                    du.transform.position = new Vector3(tg.x, dy0, tg.z);
                    if (du.rb != null) { du.rb.linearVelocity = Vector3.zero; du.rb.angularVelocity = Vector3.zero; }
                    repark = 3f;
                }
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime; repark -= Time.fixedDeltaTime;
                lift = Mathf.Max(lift, du.transform.position.y - dy0);
                float along = Vector3.Dot(du.transform.position - anchor, axis);
                pull = Mathf.Max(pull, 1.6f - along);
            }
            Phase0Input.debugThrottle = 0f;
            W(string.Format("  {0,-7} RAM: dealt={1,7:F1}  self={2,6:F1}  lift={3:F3}  pull={4:F3}",
                arm, hp0 - HP(du), php0 - HP(pr), lift, pull));
            yield return Leave();
        }
    }

    // ------------------------------------------------------------------ GYRO
    IEnumerator SecGyro()
    {
        W("## GYRO: does it RIGHT a flip? bot rolled 165 deg, timed to recovery (up.y > 0.7)");
        string G1 = "gyro|0.000,0.970,0.400|0|0.00,0.00,0.00|ABS\n";
        string G2 = "gyro|0.000,1.210,0.400|0|0.00,0.00,0.00|ABS\n";
        var rigs = new[] {
            new[]{"0 gyros", SKEL},
            new[]{"1 gyro",  SKEL + G1},
            new[]{"2 gyros", SKEL + G1 + G2},
        };
        foreach (var rg in rigs)
        {
            string res = "";
            for (int rep = 0; rep < 2; rep++)
            {
                bool ok = false; yield return Enter(rg[1], o => ok = o);
                if (!ok) { res += " rig-failed"; yield return Leave(); break; }
                var rb = bm.testRobot.rb;
                var gs = bm.testRobot.GetComponentInChildren<GyroStabilizer>(true);
                if (rb == null) { res += " no-rb"; yield return Leave(); break; }
                if (rep == 0)
                    W(string.Format("  {0,-9} GyroStabilizer present={1} liveGyros={2} active={3}",
                        rg[0], gs != null, gs != null ? gs.LiveGyros() : -1, gs != null ? gs.active : false));

                // flip it
                Vector3 pos = bm.testRobot.transform.position;
                rb.position = new Vector3(pos.x, pos.y + 0.35f, pos.z);
                rb.rotation = Quaternion.Euler(0f, 0f, 165f);
                bm.testRobot.transform.position = rb.position;
                bm.testRobot.transform.rotation = rb.rotation;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();

                float t = 0f, rec = -1f; float bestUp = -1f;
                while (t < 15f)
                {
                    yield return new WaitForFixedUpdate(); t += Time.fixedDeltaTime;
                    float up = bm.testRobot.transform.up.y;
                    bestUp = Mathf.Max(bestUp, up);
                    if (up > 0.7f) { rec = t; break; }
                }
                res += string.Format(" [{0} bestUp={1:F2}]", rec < 0f ? "no-recover" : rec.ToString("F2") + "s", bestUp);
                yield return Leave();
            }
            W("  " + string.Format("{0,-9}", rg[0]) + res);
        }
    }
}
}
