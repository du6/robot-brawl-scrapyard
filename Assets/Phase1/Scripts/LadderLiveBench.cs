// ===========================================================================
// LadderLiveBench — LadderClient against a REAL server. 2026-08-09.
//
//   RobotBrawl.Phase0.LadderLiveBench.Run();      // play mode, API running
//   RobotBrawl.Phase0.LadderLiveBench.finished / passed / failed
//
// WHY THIS EXISTS, in LadderClientBench's own words:
//
//     "Pure data, no scene, no play mode, no network. The HTTP is exercised
//      by pointing ArenaScreen at a live API"
//
// — which is to say, by hand, by whoever remembers to. That is the one part
// of the ladder client with NO regression cover, and its failure mode is the
// worst kind:
//
//     "a wrong key or an unhandled shape returns an EMPTY LIST, which renders
//      as 'nobody ranked here yet' and looks exactly like a fresh ladder."
//
// A parse that silently yields nothing cannot be told from an empty ladder by
// looking. LadderClientBench proves the parser against FIXTURES; this proves
// it against whatever the server actually sends today. The two fail for
// different reasons and that is the point — fixtures go stale the moment a
// response shape changes, and nothing tells you.
//
// ⚠ THIS BENCH SKIPS RATHER THAN FAILS when there is no server, and skips
// again when the board is empty. A bench that goes red because a developer
// did not happen to have Postgres running teaches people to ignore it. Skips
// are counted and printed, never silent — a run that skips everything must
// not read as a pass, so the RESULT line says so explicitly.
//
// It writes nothing and needs no career state: every call is a GET.
// ===========================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class LadderLiveBench : MonoBehaviour
    {
        public static bool finished;
        public static int passed, failed, skipped;
        static readonly List<string> log = new List<string>();

        static void Pass(string w) { passed++; log.Add("PASS  " + w); }
        static void Fail(string w) { failed++; log.Add("FAIL  " + w); }
        static void Skip(string w, string why) { skipped++; log.Add("SKIP  " + w + " -- " + why); }
        static void Note(string w) { log.Add("      " + w); }

        public static void Run()
        {
            finished = false; passed = 0; failed = 0; skipped = 0; log.Clear();
            var go = new GameObject("ladder_live_bench");
            go.AddComponent<LadderLiveBench>().StartCoroutine(All());
        }

        static IEnumerator All()
        {
            Note("server: " + LadderClient.BaseUrl);

            // ---- ALL, which is the board ArenaScreen opens on -------------
            List<LadderEntry> all = null; string err = null;
            yield return LadderClient.Leaderboard("", (r, e) => { all = r; err = e; });

            if (!string.IsNullOrEmpty(err))
            {
                Skip("the whole bench", "no server at " + LadderClient.BaseUrl + " (" + err + ")");
                Done(); yield break;
            }
            Pass("the leaderboard answers without an error");

            if (all == null || all.Count == 0)
            {
                Skip("everything that needs a ranked robot",
                     "the board is empty — run server/tests/run_local.sh first, which leaves rated robots behind");
                Done(); yield break;
            }
            Pass("the board parses to at least one entry");
            Note(all.Count + " ranked");

            // The silent-empty failure is a parse that returns rows with all
            // fields blank, so assert the FIELDS, not the row count. A row of
            // empty strings would satisfy "count > 0" and render as a board of
            // blank lines.
            var top = all[0];
            if (!string.IsNullOrEmpty(top.robotName)) Pass("an entry carries a robot name");
            else Fail("robotName is empty — the shape parsed but the keys did not match");

            if (!string.IsNullOrEmpty(top.category)) Pass("an entry carries a category");
            else Fail("category is empty — a board cannot be filtered by a field that did not parse");

            if (top.rating > 0f) Pass("an entry carries a rating");
            else Fail("rating is 0 — Glicko-2 never returns 0, so this did not parse");

            if (top.rank > 0) Pass("an entry carries a rank");
            else Fail("rank is 0 — the server ranks from 1");

            // ---- per category ---------------------------------------------
            // The sum over the five classes must equal ALL. This is the check
            // that catches a filter silently ignored: if the category were
            // dropped, every class would return the FULL board and the sum
            // would be five times too big.
            int sum = 0; bool catErr = false;
            string[] cats = { "FEATHER", "LIGHT", "MIDDLE", "HEAVY", "SUPER" };
            foreach (var c in cats)
            {
                List<LadderEntry> rows = null; string e2 = null;
                yield return LadderClient.Leaderboard(c, (r, e) => { rows = r; e2 = e; });
                if (!string.IsNullOrEmpty(e2)) { catErr = true; break; }
                sum += rows == null ? 0 : rows.Count;
                foreach (var r2 in rows)
                    if (r2.category != c)
                    { Fail("the " + c + " board returned a " + r2.category + " robot"); catErr = true; }
            }
            if (catErr) Skip("the categories partition the board", "a category query failed");
            else if (sum == all.Count)
                Pass("the five categories partition the board exactly (" + sum + " = " + all.Count + ")");
            else
                Fail("the categories sum to " + sum + " but ALL has " + all.Count
                     + " — a filter that is ignored returns the whole board five times");

            // ---- the scouting card, and §1.3 ------------------------------
            var scoutable = all.Find(e3 => !string.IsNullOrEmpty(e3.activeSnapshotId));
            if (scoutable == null)
            {
                Skip("the scouting card (4 checks)", "no entry has an active snapshot");
            }
            else
            {
                ScoutCard card = null; string e4 = null;
                yield return LadderClient.ScoutCard(scoutable.activeSnapshotId, (c, e) => { card = c; e4 = e; });
                if (!string.IsNullOrEmpty(e4) || card == null)
                {
                    Fail("the scouting card loads -- " + e4);
                    Skip("the card's contents (3 checks)", "the card did not load");
                }
                else
                {
                    Pass("the scouting card loads");
                    if (!string.IsNullOrEmpty(card.robotName)) Pass("the card names the robot");
                    else Fail("the card's robotName is empty");

                    if (card.massKg > 0) Pass("the card carries a mass");
                    else Fail("massKg is 0 — a validated snapshot always has one");

                    // §1.3: design public, code private. The card may say a
                    // program EXISTS; it must never carry the program.
                    if (card.parts != null && card.parts.Count > 0)
                        Pass("the card lists part ids — the design is public (" + card.parts.Count + " parts)");
                    else
                        Fail("parts is empty; §1.3 makes the design public and this is how a scout sees it");
                }
            }

            Done();
        }

        static void Done()
        {
            // A run that skipped everything is NOT a pass. Said out loud,
            // because "0 failed" is what gets quoted into a handover.
            string verdict = failed > 0 ? "FAILED"
                           : passed == 0 ? "NOTHING RAN — the server was not there"
                           : skipped > 0 ? "green, with skips" : "ALL GREEN";
            log.Add("RESULT: " + passed + " pass, " + failed + " fail, "
                    + skipped + " skip - " + verdict);
            foreach (var l in log) Debug.Log("[LadderLiveBench] " + l);
            try
            {
                System.IO.File.WriteAllText(Application.dataPath + "/Phase1/qa_ladder_live.txt",
                                            string.Join("\n", log.ToArray()) + "\n");
            }
            catch (System.Exception e) { Debug.LogWarning("could not write report: " + e.Message); }
            finished = true;
        }

        public static string Report() { return string.Join("\n", log.ToArray()); }
    }
}
