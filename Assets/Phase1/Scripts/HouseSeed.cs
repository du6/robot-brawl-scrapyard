using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>Seeds the ladder's HOUSE robots — owen's cold-start fix,
    /// 2026-08-14: a brand-new player's ARENA board must never be empty, so
    /// the house enlists TWO beatable robots into EVERY weight class.
    ///
    /// Design constraints, all deliberate:
    /// - A house robot is an ORDINARY account with ordinary snapshots — the
    ///   worker, ratings and settlement need no special cases.
    /// - BEATABLE (owen's word): every build carries the modest spike fixture
    ///   and the sensor-free FirstSteps program. They drive, they fight, they
    ///   lose to any decent build; their ratings settle where they settle.
    /// - Geometry is EnlistLiveBench's proven-legal chassis, never edited.
    ///   Mass is scaled by MATERIAL substitution only, and the class is READ
    ///   OFF THE EXPORT rather than computed here — the client's own category
    ///   rule is the referee (the size-box lesson: never a second copy).
    /// - Idempotent: robots are found by NAME on the class board and only the
    ///   missing ones are enlisted. Safe to re-run forever.
    ///
    /// Run: HouseSeed.Run() in play mode. Refuses production unless
    /// HouseSeed.allowProduction was set in the same session — production
    /// seeding is the goal but must be said out loud.</summary>
    public class HouseSeed : MonoBehaviour
    {
        public static bool finished;
        public static string report = "";
        public static bool allowProduction;

        const string EMAIL = "house@robotbrawl-ladder.com";
        /// <summary>DEAD ON PRODUCTION — rotated via the operator reset the
        /// moment seeding finished (2026-08-14), precisely because it is
        /// committed here. Runs against production must set passwordOverride
        /// from .house_account.local (gitignored) first.</summary>
        const string PASS  = "house-of-the-yard-2026";
        public static string passwordOverride;
        static string Pass { get { return string.IsNullOrEmpty(passwordOverride) ? PASS : passwordOverride; } }

        /// <summary>Skip the by-name idempotency check and re-enlist anyway —
        /// harmless by the server's replace-by-name rule, and how a worker
        /// latency probe manufactures real validate jobs on demand.</summary>
        public static bool forceReenlist;

        // Two names per class, HOUSE-prefixed so nobody mistakes the house
        // for a person.
        static readonly string[,] NAMES =
        {
            { "HOUSE Tinplate", "HOUSE Bolt"    },   // FEATHER
            { "HOUSE Crowbar",  "HOUSE Piston"  },   // LIGHT
            { "HOUSE Anvil",    "HOUSE Girder"  },   // MIDDLE
            { "HOUSE Flatbed",  "HOUSE Boiler"  },   // HEAVY
            { "HOUSE Foundry",  "HOUSE Monolith"},   // SUPER
        };

        // Candidate materials for the structural parts (beams, beamlong,
        // spindle). Densities span ~1:7, which is what walks the fixed
        // geometry across all five caps.
        static readonly string[] MATS = { "ABS", "Aluminum", "Titanium", "Steel", "Tungsten" };

        public static void Run()
        {
            finished = false; report = "";
            var go = new GameObject("HouseSeed");
            go.AddComponent<HouseSeed>().StartCoroutine(go.GetComponent<HouseSeed>().All());
        }

        static string Build(string structMat, string extraMat) { return Build(structMat, extraMat, false); }

        static string Build(string structMat, string extraMat, bool ballast)
        {
            // EnlistLiveBench's LEGAL_BUILD, with the structural parts'
            // materials as parameters. Wheels stay Rubber, core/battery stay
            // Aluminum, the spike stays Steel — beatable, not armoured.
            return
              "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n"
            + "beam|-0.250,0.700,0.000|0|0.00,0.00,0.00|" + structMat + "\n"
            + "beam|-0.250,0.700,-0.600|0|0.00,0.00,0.00|" + structMat + "\n"
            + "beam|0.250,0.700,0.000|0|0.00,0.00,0.00|" + structMat + "\n"
            + "beam|0.250,0.700,-0.600|0|0.00,0.00,0.00|" + structMat + "\n"
            + "wheel|0.420,0.700,0.150|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|0.420,0.700,-0.750|0|1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,0.150|0|-1.00,0.00,0.00|Rubber\n"
            + "wheel|-0.420,0.700,-0.750|0|-1.00,0.00,0.00|Rubber\n"
            + "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|" + extraMat + "\n"
            + "battery|0.000,0.925,-0.450|0|0.00,0.00,0.00|Aluminum\n"
            + "spindle|0.000,1.000,0.000|0|0.00,1.00,0.00|" + extraMat + "\n"
            + "beam|0.000,1.250,0.000|0|0.00,0.00,0.00|" + structMat + "\n"
            + "beamlong|0.000,1.250,0.800|0|0.00,0.00,0.00|" + extraMat + "\n"
            // The all-Tungsten mix tops out at ~4,039 kg (measured), one row
            // into SUPER — so SUPER's SECOND robot carries a tungsten cube,
            // stacked in the same over-beam pattern the battery already uses.
            + (ballast ? "cube|0.000,1.475,0.000|0|0.00,0.00,0.00|Tungsten\n" : "")
            + "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";
        }

        IEnumerator All()
        {
            var log = new List<string>();
            Action<string> say = s => { log.Add(s); Debug.Log("[HouseSeed] " + s); };

            if (LadderClient.IsProduction && !allowProduction)
            {
                say("REFUSED: BaseUrl is PRODUCTION and allowProduction is not set.");
                Finish(log); yield break;
            }
            say("server: " + LadderClient.BaseUrl);

            // For each class, find TWO material mixes that land in it —
            // MEASURED with the worker's own recipe (LoadSnapshot + Describe
            // against the live builder; RobotWorker.cs:230), never computed
            // here. The builder's own build is saved and restored around the
            // probes. Variants differ so the two robots are not clones.
            var bm = FindAnyObjectByType<BuilderManager>();
            if (bm == null) { say("FAIL: no BuilderManager in the scene"); Finish(log); yield break; }
            string ownerBuild = bm.SnapshotString();
            var chosen = new Dictionary<string, List<string[]>>();
            var masses = new Dictionary<string, int>();
            for (int b = 0; b < 2; b++) foreach (var s in MATS) foreach (var e in MATS)
            {
                string bt = Build(s, e, b == 1);
                if (bm.LoadSnapshot(BuilderManager.SNAP_STAMP + "\n" + bt) <= 0) continue;
                var meta = RobotSnapshot.Describe(bm, new SnapshotPayload
                    { build = bt, program = RobotProgram.FirstSteps().ToJson() });
                if (!meta.legal || string.IsNullOrEmpty(meta.category)) continue;
                if (!chosen.ContainsKey(meta.category)) chosen[meta.category] = new List<string[]>();
                if (chosen[meta.category].Count < 2)
                { chosen[meta.category].Add(new[] { s, e, b == 1 ? "1" : "" }); masses[s + "/" + e + "/" + b] = meta.massKg; }
            }
            bm.LoadSnapshot(ownerBuild);
            for (int i = 0; i < RobotCategory.LADDER.Length; i++)
            {
                string cat = RobotCategory.LADDER[i].id;
                say(cat + ": " + (chosen.ContainsKey(cat) ? chosen[cat].Count : 0) + " mixes found");
            }

            // House account: login first (idempotent), register on a miss.
            string savedToken = LadderClient.Token;
            string err = null;
            yield return LadderClient.Login(EMAIL, Pass, (who, e) => err = e);
            if (!string.IsNullOrEmpty(err))
            {
                err = null;
                yield return LadderClient.Register(EMAIL, Pass, "The Yard", (who, e) => err = e);
                if (!string.IsNullOrEmpty(err))
                { say("FAIL: house register: " + err); LadderClient.Token = savedToken; Finish(log); yield break; }
                say("house account created (The Yard)");
            }
            else say("house account already exists — logged in");

            int enlisted = 0, present = 0, failed = 0;
            for (int c = 0; c < RobotCategory.LADDER.Length; c++)
            {
                string cat = RobotCategory.LADDER[c].id;
                List<LadderEntry> board = null;
                yield return LadderClient.Leaderboard(cat, (r, e) => { board = r; err = e; });
                var have = new HashSet<string>();
                if (board != null) foreach (var b in board) have.Add(b.robotName);

                for (int v = 0; v < 2; v++)
                {
                    string name = NAMES[c, v];
                    if (!forceReenlist && have.Contains(name)) { say(cat + ": '" + name + "' already on the board"); present++; continue; }
                    if (!chosen.ContainsKey(cat) || chosen[cat].Count <= v)
                    { say("FAIL " + cat + ": no material mix reaches this class"); failed++; continue; }
                    var mix = chosen[cat][v];
                    bool bal = mix[2] == "1";
                    var env = RobotSnapshot.ExportRaw(name,
                        BuilderManager.SNAP_STAMP + "\n" + Build(mix[0], mix[1], bal),
                        RobotProgram.FirstSteps().ToJson());
                    string snapId = null; err = null;
                    yield return LadderClient.Enlist(name, env, (id, e) => { snapId = id; err = e; });
                    string mk = mix[0] + "/" + mix[1] + "/" + (bal ? 1 : 0);
                    if (string.IsNullOrEmpty(err) && !string.IsNullOrEmpty(snapId))
                    { say(cat + ": enlisted '" + name + "' (" + mix[0] + "/" + mix[1] + (bal ? "+ballast" : "") + ", "
                         + (masses.ContainsKey(mk) ? masses[mk] : 0) + " kg)"); enlisted++; }
                    else { say("FAIL " + cat + " '" + name + "': " + err); failed++; }
                }
            }

            say("RESULT: " + enlisted + " enlisted, " + present + " already present, " + failed + " failed"
                + (failed == 0 ? "" : " - FIX NEEDED"));
            LadderClient.Token = savedToken;
            Finish(log);
        }

        void Finish(List<string> log)
        {
            report = string.Join("\n", log);
            finished = true;
            Destroy(gameObject);
        }
    }
}
