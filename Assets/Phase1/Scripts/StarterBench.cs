// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY — see CategoryBench.cs's header for why
// every harness in this project carries this guard.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// StarterBench.cs — is the pre-built starter robot actually WINNABLE?
//
//     RobotBrawl.Phase0.StarterBench.Run();      // play mode
//
// WHY. The web funnel measured (2026-09-03): 14 strangers reached the
// workshop, one placed a part, zero ever saw a fight. The fix (CATS model:
// hand the player a working machine, fight first, build second) stands or
// falls on ONE property — the handed machine must actually beat SCOUT with
// the FIRST STEPS program driving. Both prior 3-part starters lost 2/2 by
// flipping. So the candidate build fights here BEFORE it ships, against a
// CONTROL leg (a bot shaped like the ones that lost) that must keep losing,
// or the harness itself is not measuring anything. House rules 4 and 6.
//
// PREDICTION, written before the first run:
//   STARTER (4-wheel wide-track, spike fore) wins >= 6/10 vs SCOUT.
//   CONTROL (1-wheel tower, the observed real-player shape) wins <= 3/10.
//
// ⚠ CAREER ISOLATION: fights call Progression.OnMatchEnd unconditionally, so
// Career.Data is swapped and autosave held for the whole run (hard rule 5).
// ===========================================================================
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class StarterBench : MonoBehaviour
    {
        public static string report = "";
        public static bool finished;
        public static int starterWins = -1, controlWins = -1, bouts;
        public static int[] winsAll = new int[0];

        const int N = 10;

        public static StarterBench Run()
        {
            finished = false; report = ""; starterWins = controlWins = -1;
            var go = new GameObject("StarterBench");
            return go.AddComponent<StarterBench>();
        }

        // ---- the CANDIDATE: every part from the (aluminum) starter kit ----
        // Beams lie ACROSS the core (yaw 1) as axles: track 0.74 m vs SCOUT's
        // 0.54, wheelbase 0.50, all mass in a 0.20 m-tall band. The shape is
        // the anti-flip answer to "on its back 61-100% of the match".
        static List<BuilderManager.PlacedPart> StarterParts(P1PartDef[] pal)
        {
            var core = D(pal, "core"); var beam = D(pal, "beam");
            var wheel = D(pal, "wheel"); var batt = D(pal, "battery");
            var spike = D(pal, "spike");
            return new List<BuilderManager.PlacedPart>
            {
                P(core, 0f, 0.70f,  0f,    "Aluminum", 0),
                // yaw is DEGREES (Half(): 90 swaps z<->x), so 90 lays the beam
                // ACROSS the core as an axle spanning x ±0.30.
                P(beam, 0f, 0.70f,  0.25f, "Aluminum", 90),   // fore axle
                P(beam, 0f, 0.70f, -0.25f, "Aluminum", 90),   // aft axle
                W(wheel,  0.37f, 0.70f,  0.25f,  1f),
                W(wheel, -0.37f, 0.70f,  0.25f, -1f),
                W(wheel,  0.37f, 0.70f, -0.25f,  1f),
                W(wheel, -0.37f, 0.70f, -0.25f, -1f),
                // Round 2 tried the battery low-rear on the aft axle
                // (prediction >=7/10): measured 5/10 with flips UNCHANGED at
                // 2/10 - the lever does nothing and cost a win, so the roof
                // battery stays. SHIPPED CONFIG = this one, measured 6/10 WIN
                // 2/10 dominant-draw 2/10 flip vs SCOUT, control 0/10.
                P(batt, 0f, 0.975f, 0f,   "Aluminum", 0),
                // A spike POINTS ALONG wheelAxis (Half()'s special case) — the
                // roster's own scout sets Vector3.forward, so ours does too.
                SpkWelded(spike, 0f, 0.70f, 0.50f),
            };
        }

        // ---- the CONTROL: the shape real players actually built and lost
        // with (core + one wheel + battery on top + spike). If THIS starts
        // winning, the harness is wrong, not the candidate right.
        static List<BuilderManager.PlacedPart> ControlParts(P1PartDef[] pal)
        {
            var core = D(pal, "core"); var wheel = D(pal, "wheel");
            var batt = D(pal, "battery"); var spike = D(pal, "spike");
            return new List<BuilderManager.PlacedPart>
            {
                P(core, 0f, 0.70f, 0f, "Aluminum", 0),
                W(wheel, 0.22f, 0.70f, 0f, 1f),
                P(batt, 0f, 0.975f, 0f, "Aluminum", 0),
                Spk(spike, 0f, 0.70f, 0.30f),   // core face .15 + spike half .15
            };
        }

        // ---- SENSORED (owen, 2026-09-03): the starter that HUNTS. STARTER KIT
        // needs a Compass (EnemyRange/seek), a Wall sensor (EdgeDist) and a
        // Damage bus (HitRecently). Mounted on the two free core side-faces
        // and the battery roof - Validate() is the arbiter, this is a
        // hypothesis. Prediction: closes more fights than the blind ram, so
        // >= its 6/10 and FEWER dominant draws.
        static List<BuilderManager.PlacedPart> SensoredParts(P1PartDef[] pal)
        {
            var l = StarterParts(pal);
            var compass = D(pal, "compass"); var wall = D(pal, "wallsensor");
            l.Add(new BuilderManager.PlacedPart { def = compass, pos = new Vector3(0.25f, 0.70f, 0f), matName = compass.matName });
            l.Add(new BuilderManager.PlacedPart { def = wall,    pos = new Vector3(-0.25f, 0.70f, 0f), matName = wall.matName });
            return l;
        }

        static P1PartDef D(P1PartDef[] pal, string id)
        { foreach (var d in pal) if (d.id == id) return d; return null; }
        static BuilderManager.PlacedPart P(P1PartDef def, float x, float y, float z, string mat, int yaw)
        { return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z), yaw = yaw, matName = mat }; }
        static BuilderManager.PlacedPart W(P1PartDef def, float x, float y, float z, float ax)
        { return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z), wheelAxis = new Vector3(ax, 0f, 0f) }; }
        static BuilderManager.PlacedPart Spk(P1PartDef def, float x, float y, float z)
        { return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z), wheelAxis = Vector3.forward, matName = "Aluminum" }; }
        /// <summary>Same spike, with its mounting seam (-z world face, bit 32)
        /// GUSSETED. Round 1 measured the ungusseted starter losing 10/10 to
        /// MUTUAL DISARM: shape fine (0% flipped), aluminum spike sheared on
        /// first contact every bout, judges called it early. The seam x4 weld
        /// is the game's own designed answer to exactly that.</summary>
        static BuilderManager.PlacedPart SpkWelded(P1PartDef def, float x, float y, float z)
        { return new BuilderManager.PlacedPart { def = def, pos = new Vector3(x, y, z), wheelAxis = Vector3.forward, matName = "Aluminum", gussetFaces = 32 }; }

        static string Snap(string stamp, List<BuilderManager.PlacedPart> parts)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(); sb.Append(stamp).Append('\n');
            foreach (var p in parts)
                sb.Append(p.def.id).Append('|')
                  .Append(p.pos.x.ToString("F3", inv)).Append(',')
                  .Append(p.pos.y.ToString("F3", inv)).Append(',')
                  .Append(p.pos.z.ToString("F3", inv)).Append('|')
                  .Append(p.yaw).Append('|')
                  .Append(p.wheelAxis.x.ToString("F2", inv)).Append(',')
                  .Append(p.wheelAxis.y.ToString("F2", inv)).Append(',')
                  .Append(p.wheelAxis.z.ToString("F2", inv)).Append('|')
                  .Append(p.def.materialChoice ? p.def.EffectiveMat(p.matName) : p.MatName())
                  .Append(p.gussetFaces != 0 ? "|G:" + p.gussetFaces : "")
                  .Append('\n');
            return sb.ToString();
        }

        IEnumerator Start()
        {
            var log = new StringBuilder();
            var bm = FindFirstObjectByType<BuilderManager>();
            if (bm == null)
            {
                // Main.unity has no BuilderManager and only a click creates one
                // (docs/Gusset_x4_2026-08-18.md §3) — create it headlessly.
                bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
                yield return null;
            }

            var savedData = Career.Data; bool savedActive = Career.active;
            var hold = Career.SuspendAutosave();
            try
            {
                Career.active = true;
                Career.Data = new CareerData();
                Career.GrantStarterKit();
                // The WEB clone's kit is all-aluminum (owen, 2026-09-02); this
                // repo's kit still grants Steel/ABS. The bench measures the
                // CLONE's starter, so grant the aluminum trio it actually has.
                Career.AddItem("spike", "Aluminum", 1);
                Career.AddItem("wedge", "Aluminum", 1);
                Career.AddItem("plate", "Aluminum", 2);
                Career.AddItem("compass", "Aluminum", 1);
                Career.AddItem("wallsensor", "Aluminum", 1);
                Career.AddItem("dmgbus", "Aluminum", 1);
                Career.AddItem("gusset", "Steel", 1);   // consumed by the welded seam

                string stamp = bm.SnapshotString().Split('\n')[0];
                var pal = P1PartDef.Palette();
                string firstSteps = RobotProgram.FirstSteps().ToJson();
                string ramHunter = RobotProgram.RamHunter().ToJson();

                var legs = new[] {
                    new { name = "SENSORED", snap = Snap(stamp, SensoredParts(pal)), prog = ramHunter },
                    new { name = "STARTER",  snap = Snap(stamp, StarterParts(pal)),  prog = firstSteps },
                    new { name = "CONTROL",  snap = Snap(stamp, ControlParts(pal)),  prog = firstSteps },
                };
                var winsOut = new int[legs.Length];

                for (int L = 0; L < legs.Length; L++)
                {
                    Career.Data.stable.Clear();
                    Career.Data.stable.Add(new CareerRobot {
                        name = legs[L].name, snapshot = legs[L].snap, program = legs[L].prog });
                    Career.Data.activeRobot = 0;

                    int wins = 0;
                    for (int i = 0; i < N; i++)
                    {
                        int n = bm.LoadSnapshot(legs[L].snap);
                        yield return null;
                        string why = bm.Validate();
                        if (why != null)
                        { log.Append(legs[L].name).Append(" INVALID: ").Append(why).Append('\n'); break; }

                        bm.StartCareerFight(0, 0, true);   // L1 contest 0 = SCOUT, autonomy
                        yield return null; yield return null;
                        var fm = FindFirstObjectByType<FightManager>();
                        if (fm == null)
                        { log.Append(legs[L].name).Append(" bout ").Append(i).Append(": fight never started - ").Append(bm.LastMessage).Append('\n'); break; }

                        Time.timeScale = 10f;
                        float t0 = Time.realtimeSinceStartup;
                        while (fm != null && fm.state != FightManager.State.Ended
                               && Time.realtimeSinceStartup - t0 < 25f)
                            yield return null;
                        if (fm != null && fm.state != FightManager.State.Ended)
                            fm.End(FightManager.Outcome.Draw, "bench timeout");
                        yield return null;

                        bool win = fm.outcome == FightManager.Outcome.PlayerWin;
                        if (win) wins++;
                        log.Append(legs[L].name).Append(" bout ").Append(i)
                           .Append(win ? "  WIN   " : "  loss  ").Append(fm.causeLine).Append('\n');
                        Time.timeScale = 1f;
                        bm.BackToBuild();
                        yield return null; yield return null;
                    }
                    winsOut[L] = wins;
                }
                winsAll = winsOut; starterWins = winsOut.Length>1?winsOut[1]:-1; controlWins = winsOut.Length>2?winsOut[2]:-1; bouts = N;
            }
            finally
            {
                Time.timeScale = 1f;
                Career.Data = savedData; Career.active = savedActive;
                hold.Dispose();                        // owner state is sacred
            }

            log.Append("RESULT: SENSORED ").Append(winsAll.Length>0?winsAll[0]:-1).Append('/').Append(N)
               .Append("  STARTER ").Append(winsAll.Length>1?winsAll[1]:-1).Append('/').Append(N)
               .Append("  CONTROL ").Append(winsAll.Length>2?winsAll[2]:-1).Append('/').Append(N)
               .Append("   (prediction: sensored >= starter's 6, fewer draws)\n");
            report = log.ToString();
            Debug.Log("[StarterBench]\n" + report);
            finished = true;
            Destroy(gameObject);
        }
    }
}
#endif
