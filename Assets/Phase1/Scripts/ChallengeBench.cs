// ⚠ EDITOR AND DEVELOPMENT BUILDS ONLY. Same guard, same reason, as every
// other harness in this folder: nothing in the product references it and it
// must not compile into a shipped player.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// ===========================================================================
// ChallengeBench.cs — can anything BEAT Spinner1?
//
//   RobotBrawl.Phase0.ChallengeBench.RunSpec(name, buildText, programJson);
//   // play mode; poll .finished, then read .report
//
// WHAT THIS IS FOR. owen's Spinner1 is the FEATHER champion of season 1
// (rating 1617, 8-2, three career titles). This harness lets a designer —
// human or agent — propose a machine, fight it against the REAL champion
// build under the REAL physics, and get back not just a win rate but WHY it
// lost. Iterating on "it feels weak" is how this project got a 43% mutual
// disarm rate nobody could explain for a week.
//
// THE THREE RULES A CHALLENGER MUST PASS, and they are checked before a
// single bout runs, because a candidate that could never be enlisted is not
// a candidate:
//
//   1. LEGAL. RobotSnapshot.Describe runs the game's own validator — the
//      same one the cloud worker runs on an enlisted robot. Not a
//      reimplementation of it.
//   2. SAME WEIGHT CLASS. Category must come back FEATHER. This is checked
//      from the VALIDATED mass off the real builder, never from anything the
//      caller claims, and FEATHER is <= 1500 kg (RobotCategory.LADDER).
//   3. NOT A COPY. See the note on Similarity() below. owen's design is off
//      limits and "off limits" has to be a number or it is a matter of
//      opinion the day someone disagrees.
//
// ⚠ EVERY CANDIDATE IS FOUGHT FROM BOTH SPAWN SIDES. SpawnPoses places A and
// B at different points with different headings, and this game has a
// documented opening-ram effect strong enough to have been worth its own
// investigation (Opening_Ram_Fix_2026-08-09). A candidate measured only as A
// is measuring the spawn as much as the robot. N seeds x 2 sides, and the
// per-side split is reported so a lopsided one is visible rather than
// averaged away.
//
// ⚠ CAREER STATE. FightManager.End() calls Progression.OnMatchEnd
// UNCONDITIONALLY, so this suspends autosave for the whole sweep and puts it
// back afterwards (hard rule 5). Fingerprint the save either side anyway —
// the window between play-mode entry and this bench's first line is not
// covered by anything.
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ChallengeBench : MonoBehaviour
    {
        // ---- the champion, verbatim ---------------------------------------
        // Read out of owen's own career save (stable[0], name "spinner1") on
        // 2026-08-18: 8 wins, 2 losses, 3 titles, FEATHER #1 at 1617.
        // A four-wheel chassis with a Y-axis spindle carrying a long bar and a
        // steel spike at the tip — a horizontal spinner.
        // Its program is EMPTY, which is not an omission: that is how it is
        // enlisted, so on the ladder it fights on the built-in AI. Fighting it
        // with a program bolted on would be measuring a robot that does not
        // exist.
        public const string CHAMPION_NAME = "Spinner1";
        public const string CHAMPION_PROGRAM = "";
        // ⚠⚠ THIS FIXTURE IS STALE AND EVERY NUMBER THIS BENCH HAS EVER
        // PRODUCED IS AGAINST IT, NOT AGAINST THE LADDER'S SPINNER1.
        // Measured against production 2026-08-20, after owen went 0-3 in the
        // real arena with a design this bench scored at 75%:
        //
        //                       this fixture      the ENLISTED Spinner1
        //   parts                      16                        45
        //   blades                      0                         6
        //   spikes                      1                         4
        //   sensors            gyro only    compass, rangefinder, tiltsensor,
        //                                   wallsensor, trapsensor, dmgbus
        //   program                  NONE      REAL (hash 3f3f0328…)
        //   mass                        —                   1286 kg
        //
        // ⚠ AND IT LOOKED VERIFIED. This constant is byte-identical to the
        // `spinner1` in owen's LOCAL career save — I checked that, and it
        // passed. The career save is a stale copy on one machine; the thing a
        // player actually fights is the ACTIVE SNAPSHOT on the ladder, which
        // was uploaded 2026-08-16 and is a different machine entirely.
        // Checking a fixture against the wrong source of truth is the same
        // class of error as a green endpoint with no caller.
        //
        // WHAT IT WOULD TAKE TO FIX: the snapshot endpoint deliberately serves
        // a parts MANIFEST and never geometry or program (that protection is
        // correct and should stay). So the real build has to come from someone
        // who owns it — owen exporting Spinner1 from the device that enlisted
        // it — or from `/v1/matches/{id}/envelopes` as a participant, which
        // yields the BUILD but never the PROGRAM. A faithful fixture may not
        // be obtainable at all while the opponent runs a program we cannot read.
        //
        // UNTIL THEN: read every verdict from this bench as "beats a 16-part
        // program-less prototype", which is not the question anyone is asking.
        public const string CHAMPION_BUILD =
            "#fmt3-disc\n" +
            "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|-0.250,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|-0.250,0.700,-0.600|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.250,0.700,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beam|0.250,0.700,-0.600|0|0.00,0.00,0.00|Aluminum\n" +
            "wheel|0.420,0.700,0.150|0|1.00,0.00,0.00|Rubber\n" +
            "wheel|0.420,0.700,-0.750|0|1.00,0.00,0.00|Rubber\n" +
            "wheel|-0.420,0.700,0.150|0|-1.00,0.00,0.00|Rubber\n" +
            "wheel|-0.420,0.700,-0.750|0|-1.00,0.00,0.00|Rubber\n" +
            "beam|0.000,0.700,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
            "battery|0.000,0.925,-0.450|0|0.00,0.00,0.00|Aluminum\n" +
            "spindle|0.000,1.000,0.000|0|0.00,1.00,0.00|Aluminum\n" +
            "beam|0.000,1.250,0.000|0|0.00,0.00,0.00|Aluminum\n" +
            "beamlong|0.000,1.250,0.800|0|0.00,0.00,0.00|Aluminum\n" +
            "gyro|0.000,1.030,1.100|0|0.00,0.00,0.00|Aluminum\n" +
            "spike|0.000,1.030,1.370|0|0.00,0.00,1.00|Steel\n";

        public const string REQUIRED_CATEGORY = "FEATHER";

        /// <summary>Fraction of a challenger's parts that may sit at EXACTLY
        /// the same id+position+yaw+axis as one of the champion's before it is
        /// called a copy. Positions are free-form floats the designer chose, so
        /// an exact coincidence is not convergent design — it is the same
        /// placement. 0.30 leaves room for the obvious idioms (a core at the
        /// origin, wheels on a symmetric axle) without leaving room for
        /// "Spinner1 with a different battery".</summary>
        public const float MAX_IDENTICAL_PLACEMENT = 0.30f;

        // ---- inputs (set them AFTER entering play mode: the domain reload on
        // play-mode entry wipes statics) -----------------------------------
        public static string ChallengerName = "challenger";
        public static string ChallengerBuild = "";
        public static string ChallengerProgram = "";
        /// <summary>Bouts per side. Total bouts = Seeds.Length * 2.</summary>
        public static int[] Seeds = { 101, 202, 303, 404 };

        /// <summary>Part ids a candidate may NOT use. Empty = no restriction.
        ///
        /// Added 2026-08-19 for owen's "spinning robots are dominating — see if
        /// any other type can beat Spinner1". The whole point of that question
        /// is that the answer must not be another spinner, and a rule a
        /// designer is merely ASKED to follow is a rule that gets followed
        /// right up until it is inconvenient. This one is checked.</summary>
        public static string[] BannedParts = new string[0];

        /// <summary>Require at least one part the damage model calls a weapon
        /// — `DamageResolver.IsEdge(edgeHardness)`, which is the definition
        /// CLAUDE.md says to use rather than matching on id prefixes.
        ///
        /// owen, 2026-08-19: "we should never build a robot that can never
        /// fight." `abl_norotor` took the champion to 5-3 with NO weapon at
        /// all, purely on the judges' structure count. That is a scoring hole
        /// to close, not a design to pursue, and banning the spindle without
        /// this gate is an open invitation to rediscover it.</summary>
        public static bool RequireWeapon = false;
        /// <summary>Where the report is written, relative to the project.
        /// Overwritten every run — it is the LAST candidate, not a ledger.</summary>
        public static string ReportPath = "Assets/Phase1/qa_challenge_bench.txt";
        /// <summary>Appended to, never overwritten: one line per candidate ever
        /// fought, so a long design session has a memory.</summary>
        public static string HistoryPath = "Assets/Phase1/qa_challenge_history.txt";

        // ---- outputs ------------------------------------------------------
        public int passed, failed;
        public bool finished;
        public string report = "";
        /// <summary>Wins/losses/draws FOR THE CHALLENGER across every bout.</summary>
        public int wins, losses, draws;
        public bool eligible;
        public string verdict = "";

        readonly List<string> log = new List<string>();
        void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        void Note(string s) { log.Add("      " + s); }
        static string F(float v) { return v.ToString("F1", CultureInfo.InvariantCulture); }
        static string Pct(int a, int b) { return b == 0 ? "n/a" : (100f * a / b).ToString("F0") + "%"; }

        public static ChallengeBench Run()
        {
            return new GameObject("challenge_bench").AddComponent<ChallengeBench>();
        }

        /// <summary>One call: set the candidate and start. This is the entry
        /// point a driver should use, because setting the statics in one bridge
        /// command and starting in another works only if no domain reload
        /// happens in between — and entering play mode is exactly such a
        /// reload.</summary>
        public static ChallengeBench RunSpec(string name, string buildText, string programJson)
        {
            ChallengerName = string.IsNullOrEmpty(name) ? "challenger" : name;
            ChallengerBuild = buildText ?? "";
            ChallengerProgram = programJson ?? "";
            return Run();
        }

        // ---- the copy test --------------------------------------------------
        /// <summary>A placement key that is deliberately EXACT on position.
        /// Rounded to the millimetre only so float formatting cannot make two
        /// identical placements look different.</summary>
        static string PlacementKey(BuilderManager.PlacedPart p)
        {
            return p.def.id + "@"
                 + Mathf.RoundToInt(p.pos.x * 1000f) + "," + Mathf.RoundToInt(p.pos.y * 1000f) + ","
                 + Mathf.RoundToInt(p.pos.z * 1000f) + "|" + p.yaw + "|"
                 + Mathf.RoundToInt(p.wheelAxis.x) + Mathf.RoundToInt(p.wheelAxis.y) + Mathf.RoundToInt(p.wheelAxis.z);
        }

        static List<string> KeysOf(List<BuilderManager.PlacedPart> b)
        {
            var l = new List<string>();
            foreach (var p in b) if (p != null && p.def != null) l.Add(PlacementKey(p));
            return l;
        }

        // ---- the sweep --------------------------------------------------------
        IEnumerator Start()
        {
            // ⚠ TWO SEPARATE WAYS THE BUILDER IS NOT USABLE, and both fail
            // SILENTLY — LoadSnapshot returns 0 and Describe then reports
            // "Needs at least 1 wheel", which reads as a broken CANDIDATE
            // rather than a broken harness. Measured on the real champion
            // build, which is certainly legal.
            //
            //   1. Main.unity carries no BuilderManager at all, and only a
            //      title-screen click ever creates one (CLAUDE.md).
            //   2. A builder that WAS alive comes back from a play-mode domain
            //      reload — which any script edit triggers — as a live object
            //      with an EMPTY palette, because Awake does not re-run for
            //      objects that survived the reload. `bm != null` is true and
            //      the thing is useless. Presence is not readiness.
            //
            // So the test is PaletteCount, never null-ness, and a stale one is
            // replaced rather than waited on.
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm != null && bm.PaletteCount <= 0)
            {
                Note("found a BuilderManager with an empty palette (stale after a domain reload) — replacing it");
                UnityEngine.Object.DestroyImmediate(bm.gameObject);
                bm = null;
            }
            if (bm == null)
            {
                bm = new GameObject("BuilderManager").AddComponent<BuilderManager>();
                Note("created a headless BuilderManager");
            }
            // ⚠ AND `PaletteCount` IS NOT THE READINESS SIGNAL EITHER. The
            // palette is created lazily by LoadSnapshot itself, so it reads 25
            // on a builder that still refuses every load; the thing LoadSnapshot
            // actually requires is `buildRoot`, which Start() makes and which no
            // public member exposes. It says so in its own first lines:
            //     if (buildRoot == null) { message = "Builder is still starting
            //     up - try that again in a moment."; return 0; }
            // So poll the OPERATION, not a proxy for it — load a build known to
            // be legal (the champion's, which is about to be needed anyway) and
            // wait until it comes back non-zero.
            int ready = 0;
            for (int w = 0; w < 120 && ready <= 0; w++)
            {
                ready = bm.LoadSnapshot(CHAMPION_BUILD);
                if (ready <= 0) yield return null;
            }
            if (ready <= 0)
            {
                Check(false, "the builder came up (last message: " + bm.LastMessage + ")");
                Finish(); yield break;
            }
            Note("builder ready, palette " + bm.PaletteCount + " parts");

            // Hard rule 5.
            bool savedAuto = Career.autosave;
            Career.autosave = false;

            if (string.IsNullOrEmpty(ChallengerBuild))
            {
                Check(false, "a challenger build was supplied");
                Career.autosave = savedAuto; Finish(); yield break;
            }

            // ---- 1. the champion parses at all --------------------------------
            List<BuilderManager.PlacedPart> champParts;
            Vector3 champAxis; string err;
            if (!MatchRunner.ParseBuild(bm, CHAMPION_BUILD, out champParts, out champAxis, out err))
            {
                Check(false, "champion build parses (" + err + ")");
                Career.autosave = savedAuto; Finish(); yield break;
            }
            Check(true, "champion build parses (" + champParts.Count + " parts)");

            // ---- 2. the challenger is a legal FEATHER robot --------------------
            int n = bm.LoadSnapshot(ChallengerBuild);
            Check(n > 0, "challenger build loads (" + n + " parts)");
            if (n <= 0) { Career.autosave = savedAuto; Finish(); yield break; }

            var payload = new SnapshotPayload
            { robotName = ChallengerName, build = ChallengerBuild, program = ChallengerProgram ?? "" };
            var meta = RobotSnapshot.Describe(bm, payload);

            Note("challenger mass " + meta.massKg + " kg · " + meta.partCount
                 + " parts · category " + (meta.category.Length == 0 ? "(none)" : meta.category));
            Check(meta.legal, "challenger is a LEGAL build"
                  + (meta.legal ? "" : " — " + string.Join(" / ", meta.failReasons.ToArray())));
            Check(meta.category == REQUIRED_CATEGORY,
                  "challenger is in the champion's class (" + REQUIRED_CATEGORY + "), got "
                  + (meta.category.Length == 0 ? "(none)" : meta.category));

            // ---- 3. it is not owen's design -----------------------------------
            var champKeys = KeysOf(champParts);
            var mineKeys = KeysOf(bm.placed);
            int same = 0;
            var pool = new List<string>(champKeys);
            foreach (var k in mineKeys) { int i = pool.IndexOf(k); if (i >= 0) { pool.RemoveAt(i); same++; } }
            float frac = mineKeys.Count == 0 ? 1f : (float)same / mineKeys.Count;
            Note("identical placements with Spinner1: " + same + "/" + mineKeys.Count
                 + " = " + Pct(same, mineKeys.Count) + "  (limit " + Pct((int)(MAX_IDENTICAL_PLACEMENT * 100), 100) + ")");
            Check(frac <= MAX_IDENTICAL_PLACEMENT,
                  "challenger is not a copy of Spinner1");

            // ---- 3b. banned parts, and the weapon floor ------------------------
            if (BannedParts != null && BannedParts.Length > 0)
            {
                var used = new List<string>();
                foreach (var p in bm.placed)
                {
                    if (p == null || p.def == null) continue;
                    foreach (var ban in BannedParts)
                        if (p.def.id == ban && !used.Contains(ban)) used.Add(ban);
                }
                Note("banned parts in this run: " + string.Join(", ", BannedParts));
                Check(used.Count == 0, used.Count == 0
                      ? "challenger uses none of the banned parts"
                      : "challenger uses BANNED part(s): " + string.Join(", ", used.ToArray()));
            }

            if (RequireWeapon)
            {
                // The damage model's own definition, not a name match — see
                // CLAUDE.md on PartSpec.edgeHardness being what "is this a
                // weapon" means here.
                int edges = 0;
                var kinds = new List<string>();
                foreach (var p in bm.placed)
                {
                    if (p == null || p.def == null) continue;
                    if (DamageResolver.IsEdge(p.def.edgeHardness))
                    { edges++; if (!kinds.Contains(p.def.id)) kinds.Add(p.def.id); }
                }
                Note("weapon parts (edgeHardness above the edge floor): " + edges
                     + (kinds.Count > 0 ? "  [" + string.Join(", ", kinds.ToArray()) + "]" : ""));
                Check(edges > 0, "challenger carries something that can actually deal damage");
            }

            eligible = failed == 0;
            if (!eligible)
            {
                verdict = "INELIGIBLE";
                Note("no bouts run — an ineligible candidate is not a measurement");
                Career.autosave = savedAuto; Finish(); yield break;
            }

            // ---- 4. fight it, both sides --------------------------------------
            var champEnv = RobotSnapshot.ExportRaw(CHAMPION_NAME, CHAMPION_BUILD, CHAMPION_PROGRAM);
            var mineEnv = RobotSnapshot.ExportRaw(ChallengerName, ChallengerBuild, ChallengerProgram ?? "");

            bool savedDetach = CompoundRobot.detachLogOn;
            CompoundRobot.detachLogOn = true;
            CompoundRobot.detachLog.Clear();

            var rows = new List<string>();
            int myPartsLost = 0, hisPartsLost = 0, bouts = 0;
            float myDealt = 0f, hisDealt = 0f, totalSim = 0f;
            int myWeaponDeaths = 0, hisWeaponDeaths = 0;
            int winsAsA = 0, winsAsB = 0, boutsAsA = 0, boutsAsB = 0;
            var causes = new Dictionary<string, int>();

            for (int side = 0; side < 2; side++)
            {
                bool mineIsA = side == 0;
                MatchRunner.MatchResult res = null;
                var mr = MatchRunner.Run(mineIsA ? mineEnv : champEnv,
                                         mineIsA ? champEnv : mineEnv,
                                         Seeds, "chal_" + ChallengerName + "_" + side, 10f, false,
                                         r => { res = r; });
                // Every seed must run: stopping when the MATCH is decided
                // throws away exactly the bouts that tell you how a design
                // loses once the surprise is gone.
                mr.stopWhenDecided = false;
                float t0 = Time.realtimeSinceStartup;
                while (res == null && Time.realtimeSinceStartup - t0 < 600f) yield return null;
                if (mr != null) Destroy(mr.gameObject);
                if (res == null) { Check(false, "side " + side + " completed within 600 s"); break; }
                if (!res.Ok) { Check(false, "side " + side + " ran without error (" + res.error + ")"); break; }

                foreach (var b in res.bouts)
                {
                    bouts++;
                    string mineTag = mineIsA ? "A" : "B";
                    bool won = b.winner == mineTag;
                    bool drew = b.winner == "Draw";
                    if (won) wins++; else if (drew) draws++; else losses++;
                    if (mineIsA) { boutsAsA++; if (won) winsAsA++; } else { boutsAsB++; if (won) winsAsB++; }

                    float mine = mineIsA ? b.aDealt : b.bDealt;
                    float his = mineIsA ? b.bDealt : b.aDealt;
                    int myLost = mineIsA ? b.aPartsLost : b.bPartsLost;
                    int hisLost = mineIsA ? b.bPartsLost : b.aPartsLost;
                    int myWpn = mineIsA ? b.aWeaponsAlive : b.bWeaponsAlive;
                    int hisWpn = mineIsA ? b.bWeaponsAlive : b.aWeaponsAlive;
                    myDealt += mine; hisDealt += his;
                    myPartsLost += myLost; hisPartsLost += hisLost;
                    if (myWpn == 0) myWeaponDeaths++;
                    if (hisWpn == 0) hisWeaponDeaths++;
                    totalSim += b.simSeconds;

                    // ⚠ THE ENGINE'S CAUSE STRING IS WRITTEN FROM SIDE A'S POINT
                    // OF VIEW — "your core was destroyed" means A's core, not
                    // the challenger's. Tallying it raw produces a table that
                    // says "2x enemy core destroyed" underneath a 0-4 record,
                    // which reads as a contradiction and is really a pronoun.
                    // Key it by the challenger's own outcome so it cannot be
                    // misread, and keep the engine's words verbatim.
                    string cause = string.IsNullOrEmpty(b.cause) ? b.outcome : b.cause;
                    if (cause != null && cause.Length > 70) cause = cause.Substring(0, 70);
                    string key = (won ? "[challenger WON]  " : drew ? "[DRAW]  " : "[challenger LOST]  ")
                               + "(side " + mineTag + ", A-relative wording) " + cause;
                    int c; causes.TryGetValue(key, out c); causes[key] = c + 1;

                    rows.Add(string.Format(
                        "  seed {0,-5} as {1}  {2,-4}  dealt {3,7} vs {4,7}   partsLost {5}/{6}   " +
                        "weaponsAlive {7}/{8}   t={9}s  hits={10}",
                        b.seed, mineTag, won ? "WIN" : drew ? "DRAW" : "LOSS",
                        F(mine), F(his), myLost, hisLost, myWpn, hisWpn, F(b.simSeconds), b.hits));
                }
            }

            CompoundRobot.detachLogOn = savedDetach;
            Career.autosave = savedAuto;

            // ---- 5. what actually happened ------------------------------------
            Check(bouts > 0, "bouts were fought (" + bouts + ")");
            Note("");
            Note("---- RESULT ------------------------------------------------------");
            Note(ChallengerName + " vs " + CHAMPION_NAME + ":  "
                 + wins + " W / " + losses + " L / " + draws + " D over " + bouts + " bouts"
                 + "   (win rate " + Pct(wins, bouts) + ")");
            Note("as A (spawn side 0): " + winsAsA + "/" + boutsAsA
                 + "   as B (spawn side 1): " + winsAsB + "/" + boutsAsB
                 + "   <- a big gap here is the SPAWN talking, not the design");
            Note("damage dealt: " + F(myDealt) + " by " + ChallengerName
                 + " vs " + F(hisDealt) + " by " + CHAMPION_NAME);
            Note("parts lost:   " + myPartsLost + " by " + ChallengerName
                 + " vs " + hisPartsLost + " by " + CHAMPION_NAME);
            Note("bouts ending with NO WEAPON LEFT: " + ChallengerName + " " + myWeaponDeaths
                 + "/" + bouts + "   " + CHAMPION_NAME + " " + hisWeaponDeaths + "/" + bouts);
            Note("mean bout length " + F(bouts == 0 ? 0f : totalSim / bouts) + " s");
            Note("");
            Note("how the bouts ended:");
            foreach (var kv in causes) Note("    " + kv.Value + "x  " + kv.Key);
            Note("");
            Note("per-bout:");
            foreach (var r in rows) Note(r);

            verdict = wins > losses ? "BEATS SPINNER1"
                    : wins == losses ? "EVEN WITH SPINNER1" : "LOSES TO SPINNER1";
            Note("");
            Note("VERDICT: " + verdict + "   (" + Pct(wins, bouts) + " win rate over " + bouts + " bouts)");
            // ⚠ NOT a pass/fail. A candidate that loses is a measurement, not a
            // broken bench — the pass counts above are about ELIGIBILITY and
            // whether the sweep ran, and nothing else.
            Finish();
        }

        List<string> FailedChecks()
        {
            var l = new List<string>();
            foreach (var s in log) if (s.StartsWith("FAIL  ")) l.Add(s.Substring(6));
            return l;
        }

        void Finish()
        {
            var sb = new StringBuilder();
            sb.Append("passed ").Append(passed).Append(" failed ").Append(failed)
              .Append("   verdict ").Append(string.IsNullOrEmpty(verdict) ? "(none)" : verdict)
              .Append("   W/L/D ").Append(wins).Append('/').Append(losses).Append('/').Append(draws)
              .Append('\n');
            sb.Append("challenger: ").Append(ChallengerName).Append('\n');
            foreach (var l in log) sb.Append(l).Append('\n');
            report = sb.ToString();
            try { File.WriteAllText(ReportPath, report); }
            catch (Exception e) { Debug.LogWarning("[ChallengeBench] report not written: " + e.Message); }
            // A one-line ledger across runs. The per-candidate report is
            // overwritten every time; without this, run 40 has no memory that
            // run 12 already tried a wedge and what it scored — which is the
            // whole point of an iterating designer.
            try
            {
                File.AppendAllText(HistoryPath, string.Format(
                    "{0,-28} {1,-20} W/L/D {2}/{3}/{4}  {5}\n",
                    ChallengerName, eligible ? verdict : "INELIGIBLE",
                    wins, losses, draws,
                    eligible ? "" : string.Join(" ; ", FailedChecks().ToArray())));
            }
            catch (Exception e) { Debug.LogWarning("[ChallengeBench] history not written: " + e.Message); }
            Debug.Log("[ChallengeBench] " + ChallengerName + " -> " + verdict
                      + "  " + wins + "/" + losses + "/" + draws);
            finished = true;
        }
    }
}
#endif
