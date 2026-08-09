// ===========================================================================
// CategoryBench.cs — acceptance harness for RobotCategory (M1, §1.2).
//
// PURE DATA. No scene, no play mode, no career state, no fight. Call it from
// an editor RunCommand:
//
//     RobotBrawl.Phase0.CategoryBench.RunPure();
//
// It is a bench and not a set of examples because of what a wrong boundary
// costs. §1.2 resets a robot's rating to placement whenever a re-upload
// changes its natural category. A cap that is one kilogram out therefore
// raises no error anywhere — it moves a robot to a different ladder and wipes
// what that robot earned, on an upload the player thinks is a tweak. The
// failure is silent by construction, so the boundary has to be checked rather
// than looked at.
//
// Section E is the house rule ("where a quality is measurable, measure it over
// EVERYTHING rather than over a named list"): rather than sampling boundaries,
// it walks every integer mass from -10 to 7,000 kg and asserts the three
// properties that define "smallest satisfying" — the returned class holds the
// robot, no LIGHTER class would have, and a refusal always comes with a
// reason. Named boundary cases (section B) are kept anyway, because a sweep
// tells you something broke and a named case tells you where.
// ===========================================================================

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class CategoryBench
    {
        public static string report = "";
        static int passed, failed;
        static readonly List<string> log = new List<string>();

        static void Check(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            log.Add((ok ? "PASS  " : "FAIL  ") + what);
        }
        static void Note(string s) { log.Add("      " + s); }

        /// <summary>Returns true when every check passed.</summary>
        public static bool RunPure()
        {
            passed = failed = 0; log.Clear(); report = "";

            var L = RobotCategory.LADDER;

            // ============================================ A. table integrity
            log.Add("== A. the table itself ==");

            // The DB contract. server/RobotBrawl.Api/Migrations/001_init.sql
            // constrains snapshots.category AND ratings.category to exactly
            // this set; a name that is not in it is an INSERT that fails after
            // the job has already done its work.
            string[] SCHEMA_IDS = { "FEATHER", "LIGHT", "MIDDLE", "HEAVY", "SUPER" };
            Check(L.Length == SCHEMA_IDS.Length,
                  "the ladder has exactly " + SCHEMA_IDS.Length + " classes (has " + L.Length + ")");
            bool idsMatch = L.Length == SCHEMA_IDS.Length;
            for (int i = 0; i < L.Length && i < SCHEMA_IDS.Length; i++)
                if (L[i].id != SCHEMA_IDS[i]) idsMatch = false;
            Check(idsMatch, "every id matches 001_init.sql's CHECK set, in order");

            // "Smallest satisfying" is a first-match scan, so the order IS the
            // rule. A table that merely looks sorted is not evidence.
            bool ascending = true;
            for (int i = 1; i < L.Length; i++) if (L[i].capKg <= L[i - 1].capKg) ascending = false;
            Check(ascending, "caps are STRICTLY ascending — the scan order is the rule");

            // The whole reason the size box is gone (see RobotCategory's header)
            // is that the game must not hold two definitions of legal. The caps
            // are the half that remains, so they are pinned to the shipped
            // league caps rather than retyped. If owen retunes a league, this
            // fails instead of the ladder silently drifting away from career.
            bool capsMatchCareer = CareerDB.Leagues.Length == L.Length;
            if (capsMatchCareer)
                for (int i = 0; i < L.Length; i++)
                    if (Mathf.RoundToInt(CareerDB.Leagues[i].weightCap) != L[i].capKg) capsMatchCareer = false;
            Check(capsMatchCareer,
                  "caps are the five shipped league caps, in order — one definition, not two");
            var caps = new StringBuilder();
            for (int i = 0; i < L.Length; i++)
                caps.Append(i > 0 ? " / " : "").Append(L[i].id).Append(' ').Append(L[i].capKg);
            Note("table: " + caps.ToString());
            for (int i = 0; i < CareerDB.Leagues.Length; i++)
                Note("career league " + CareerDB.Leagues[i].id + " (" + CareerDB.Leagues[i].name +
                     ") cap " + Mathf.RoundToInt(CareerDB.Leagues[i].weightCap) + " kg");

            // ============================================== B. the boundaries
            log.Add("== B. every cap, from both sides ==");
            for (int i = 0; i < L.Length; i++)
            {
                int cap = L[i].capKg;
                string id = L[i].id;

                // Inclusive, exactly as BuilderManager.CareerValidate treats it
                // (`mass > lg.weightCap` is over). One robot exactly on the cap
                // is the single most likely build to sit at a boundary forever.
                Check(RobotCategory.Assign(cap) == id,
                      "a robot exactly ON the " + id + " cap (" + cap + " kg) is " + id);
                Check(RobotCategory.Assign(cap - 1) == id,
                      "one kilogram under the " + id + " cap is still " + id);

                string above = RobotCategory.Assign(cap + 1);
                if (i + 1 < L.Length)
                    Check(above == L[i + 1].id,
                          "one kilogram over the " + id + " cap is " + L[i + 1].id + " (got " +
                          (above ?? "none") + ")");
                else
                    Check(above == null,
                          "one kilogram over the heaviest cap has NO category (got " +
                          (above ?? "none") + ")");
            }

            // ================================================ C. the refusals
            log.Add("== C. refusals carry a reason ==");
            Check(RobotCategory.Assign(1) == "FEATHER", "1 kg is a Featherweight, not a rounding error");
            Check(RobotCategory.Assign(0) == null, "a 0 kg build has no category");
            Check(RobotCategory.Assign(-5) == null, "a negative mass has no category");
            Check(!string.IsNullOrEmpty(RobotCategory.Reason(0)), "0 kg is refused WITH a reason");
            Check(!string.IsNullOrEmpty(RobotCategory.Reason(-5)), "negative mass is refused WITH a reason");

            int over = RobotCategory.HeaviestCapKg + 1;
            string overWhy = RobotCategory.Reason(over);
            Check(!string.IsNullOrEmpty(overWhy) && overWhy.Contains("1 kg over"),
                  "the over-cap reason names the shortfall: " + (overWhy ?? "null"));
            Check(RobotCategory.Reason(1500) == null,
                  "a robot that HAS a category is given no rejection reason");
            Check(RobotCategory.Reason(L[0].capKg) == null && RobotCategory.Reason(RobotCategory.HeaviestCapKg) == null,
                  "neither end of the legal range is refused");

            // ================================================== D. the helpers
            log.Add("== D. helpers ==");
            Check(RobotCategory.CapKg("FEATHER") == 1500 && RobotCategory.CapKg("SUPER") == 5500,
                  "CapKg reads the table");
            Check(RobotCategory.CapKg("BANTAM") == -1, "CapKg says -1 for an id that is not a class");
            Check(RobotCategory.IsKnown("MIDDLE") && !RobotCategory.IsKnown("middle"),
                  "ids are case sensitive — the DB CHECK is too");
            Check(!RobotCategory.IsKnown("") && !RobotCategory.IsKnown(null),
                  "empty and null are not categories");
            Check(RobotCategory.Label("SUPER") == "Super-heavy", "Label is the player-facing name");
            Check(RobotCategory.Label("BANTAM") == "BANTAM",
                  "an unknown id renders as itself, never as an empty slot");

            // §1.2: you may always punch UP, never down.
            Check(RobotCategory.MayChallengeInto("FEATHER", "SUPER"), "a Featherweight may challenge up to Super-heavy");
            Check(RobotCategory.MayChallengeInto("HEAVY", "HEAVY"), "a robot may challenge in its own category");
            Check(!RobotCategory.MayChallengeInto("SUPER", "FEATHER"), "a Super-heavy may NOT punch down");
            Check(!RobotCategory.MayChallengeInto("MIDDLE", "LIGHT"), "a Middleweight may NOT punch down");
            Check(!RobotCategory.MayChallengeInto("MIDDLE", "BANTAM") &&
                  !RobotCategory.MayChallengeInto("", "SUPER"),
                  "an unknown category may not challenge and may not be challenged into");

            // ======================================= E. the whole mass domain
            log.Add("== E. every integer mass from -10 to 7000 kg ==");
            int assigned = 0, refused = 0;
            int badHold = 0, badMinimal = 0, badUnknown = 0, badSilent = 0;
            int firstBad = int.MinValue;
            for (int kg = -10; kg <= 7000; kg++)
            {
                string c = RobotCategory.Assign(kg);
                if (c == null)
                {
                    refused++;
                    if (string.IsNullOrEmpty(RobotCategory.Reason(kg)))
                    { badSilent++; if (firstBad == int.MinValue) firstBad = kg; }
                    continue;
                }
                assigned++;
                if (!RobotCategory.IsKnown(c))
                { badUnknown++; if (firstBad == int.MinValue) firstBad = kg; continue; }

                // 1. the class it was given actually holds it
                if (kg > RobotCategory.CapKg(c))
                { badHold++; if (firstBad == int.MinValue) firstBad = kg; }

                // 2. and no LIGHTER class would have — this is the whole rule
                for (int i = 0; i < L.Length; i++)
                {
                    if (L[i].id == c) break;
                    if (kg <= L[i].capKg)
                    { badMinimal++; if (firstBad == int.MinValue) firstBad = kg; break; }
                }
            }
            Check(badUnknown == 0, "every assigned category is one of the five (" + badUnknown + " bad)");
            Check(badHold == 0, "every assigned class actually holds its robot (" + badHold + " bad)");
            Check(badMinimal == 0, "no robot was put in a class heavier than it needed (" + badMinimal + " bad)");
            Check(badSilent == 0, "every refusal across the sweep carries a reason (" + badSilent + " silent)");
            Note("swept 7011 masses: " + assigned + " assigned, " + refused + " refused" +
                 (firstBad == int.MinValue ? "" : ", first bad at " + firstBad + " kg"));

            // A sweep that assigned nothing, or refused nothing, is an
            // instrument fault and not a green run (the OpeningBench lesson:
            // a swept variable with no variance has usually not been swept).
            Check(assigned > 0 && refused > 0,
                  "the sweep saw BOTH outcomes — " + assigned + " assigned / " + refused + " refused");
            Check(assigned == RobotCategory.HeaviestCapKg,
                  "exactly " + RobotCategory.HeaviestCapKg + " masses are placeable (1 kg .. the top cap), got " + assigned);

            return Finish();
        }

        static bool Finish()
        {
            var sb = new StringBuilder();
            foreach (var l in log) { Debug.Log("[CategoryBench] " + l); sb.Append(l).Append('\n'); }
            string line = string.Format("[CategoryBench] RESULT: {0} pass, {1} fail{2}",
                                        passed, failed, failed == 0 ? " - ALL GREEN" : " - TUNING NEEDED");
            Debug.Log(line);
            sb.Append(line.Substring(1 + line.IndexOf(']'))).Append('\n');
            report = sb.ToString();
            try
            {
                System.IO.File.WriteAllText(
                    Application.dataPath + "/Phase1/qa_category_bench.txt", report);
            }
            catch { }
            return failed == 0;
        }
    }
}
