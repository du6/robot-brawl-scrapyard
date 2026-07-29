using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotBrawl.Phase0
{

/// <summary>
/// WHAT DOES THE ENGINE ACTUALLY DO? (2026-07-29)
///
/// owen: "Looks like I can move the robot and the weapon as long as I have
/// battery." He is right, and that is the finding, not the misunderstanding.
/// The engine feeds exactly two places:
///
///   1. PowerPlant  - +60 kJ of capacity and +14 kW of DRAW CEILING; and
///   2. Actuator.MotorKW(liveEngines) = min(12, 1.0 + 1.5 * engines) - the
///      rate at which every weapon on the machine winds up.
///
/// (1) is a ceiling and (2) is a throughput. A ceiling only matters when
/// something reaches it, so this probe records the DEMAND alongside the
/// ceiling: if peak demand never approaches a battery's own 8 kW, then (1) is
/// dead weight for that build and the engine's whole value is (2), which the
/// part description does not say. That is a testable claim and it is what the
/// engine ladder below is for.
///
/// Fixtures are IDENTICAL except for engine count, mounted at socket positions
/// already proven valid by BuilderManager.Validate. Wind-up is measured as the
/// time for the driven limb to first reach 90% of the peak rate it actually
/// achieves in that run - not a quoted number, and not time-to-cap, because a
/// limb that never reaches its cap has no time-to-cap.
/// </summary>
public class EngineProbe : MonoBehaviour
{
    public BuilderManager bm;
    public string opponent = "mauler";
    public float seconds = 20f;
    public float speed = 3f;
    public int reps = 3;
    public string outPath = "Assets/Phase1/qa_engineprobe.txt";

    public bool done;
    public string summary = "";

    readonly List<string> rows = new List<string>();

    // Proven-valid mounts (see "find valid engine mounts", 2026-07-29).
    static readonly string[] MOUNTS = {
        "engine|0.000,0.925,0.450|0|0.00,0.00,0.00|Aluminum",
        "engine|-0.450,0.925,-0.150|0|0.00,0.00,0.00|Aluminum",
        "engine|0.450,0.925,-0.150|0|0.00,0.00,0.00|Aluminum",
    };

    public void Run() { StartCoroutine(Go()); }

    IEnumerator Go()
    {
        if (bm == null) bm = Object.FindFirstObjectByType<BuilderManager>();
        if (bm == null) { Finish("no BuilderManager"); yield break; }

        string bas = File.ReadAllText(Path.Combine(Application.dataPath, "Phase1/qa_disc_spinner.txt"));

        rows.Add("# ENGINE LADDER - identical build, engine count is the ONLY difference");
        rows.Add("# opponent=" + opponent + " seconds=" + seconds + " timeScale=" + speed + " reps=" + reps);
        rows.Add("#");
        rows.Add("# eng  parts cost  capKJ ceilKW motorKW maxRate | windUp peakRate peakDemand strained flatAt  dealt");

        var perEng = new Dictionary<int, List<float>>();

        for (int eng = 0; eng <= 3; eng++)
        {
            string snap = bas;
            for (int i = 0; i < eng; i++) snap += MOUNTS[i] + "\n";
            perEng[eng] = new List<float>();

            for (int rep = 0; rep < reps; rep++)
            {
                int np = bm.LoadSnapshot(snap);
                string v = bm.Validate();
                if (v != null) { rows.Add("# eng " + eng + " INVALID: " + v); break; }
                int cost = bm.BuildCost();

                bm.opponentId = opponent;
                bm.StartFight();
                yield return null;
                yield return new WaitForSeconds(0.4f);

                var player = bm.testRobot;
                var enemy = bm.aiRobot;
                if (player == null || enemy == null) { rows.Add("# eng " + eng + " no robots"); break; }

                var acts = new List<Actuator>(player.GetComponentsInChildren<Actuator>(true));
                var pp = player.GetComponentInChildren<PowerPlant>(true);
                if (acts.Count == 0) { rows.Add("# eng " + eng + " NO ACTUATOR - fixture broken"); break; }
                var act = acts[0];

                bool fireWas = Phase0Input.debugFire;
                Phase0Input.debugFire = true;
                float tsWas = Time.timeScale;
                Time.timeScale = speed;

                float t = 0f, peakRate = 0f, peakDemand = 0f, flatAt = -1f;
                bool strained = false;
                var trace = new List<Vector2>();   // (t, |rate|)

                while (t < seconds && !player.dead && !enemy.dead)
                {
                    yield return null;
                    t += Time.deltaTime;
                    float r = Mathf.Abs(act.rate);
                    if (r > peakRate) peakRate = r;
                    trace.Add(new Vector2(t, r));
                    if (pp != null)
                    {
                        if (pp.demandKW > peakDemand) peakDemand = pp.demandKW;
                        if (pp.Strained) strained = true;
                        if (pp.Flat && flatAt < 0f) flatAt = t;
                    }
                }

                // wind-up: first time |rate| reached 90% of the peak this run hit
                float target = peakRate * 0.9f, windUp = -1f;
                for (int i = 0; i < trace.Count; i++)
                    if (trace[i].y >= target) { windUp = trace[i].x; break; }

                Time.timeScale = tsWas;
                Phase0Input.debugFire = fireWas;

                rows.Add(string.Format(
                    "  {0}   {1,5} {2,5} {3,6:F0} {4,6:F1} {5,7:F2} {6,7:F1} | {7,6:F2} {8,8:F1} {9,10:F1} {10,8} {11,6:F1} {12,6:F0}",
                    eng, np, cost,
                    pp != null ? pp.capacityKJ : 0f, pp != null ? pp.peakKW : 0f,
                    act.motorKW, act.MaxRate,
                    windUp, peakRate, peakDemand, strained,
                    flatAt, player.damageDealt));

                perEng[eng].Add(player.damageDealt);
            }
        }

        rows.Add("#");
        rows.Add("# ---- damage per engine count ----");
        foreach (var kv in perEng)
        {
            var L = kv.Value; if (L.Count == 0) continue;
            L.Sort();
            float med = L[L.Count / 2], sum = 0f;
            foreach (float f in L) sum += f;
            rows.Add(string.Format("# eng {0}: n={1} median={2:F0} mean={3:F0}  [{4}]",
                     kv.Key, L.Count, med, sum / L.Count, string.Join(", ",
                     L.ConvertAll(f => Mathf.RoundToInt(f).ToString()).ToArray())));
        }

        Finish("engine ladder complete");
    }

    void Finish(string msg)
    {
        summary = msg;
        rows.Add("# " + msg);
        try { File.WriteAllText(outPath, string.Join("\n", rows.ToArray()) + "\n"); }
        catch (System.Exception ex) { Debug.LogWarning("EngineProbe write failed: " + ex.Message); }
        done = true;
        Debug.Log("EngineProbe: " + msg);
    }
}

}
