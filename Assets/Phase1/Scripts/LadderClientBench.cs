// ===========================================================================
// LadderClientBench — cover for LadderClient's parsing, 2026-08-09.
//
//   RobotBrawl.Phase0.LadderClientBench.RunPure()
//
// Pure data, no scene, no play mode, no network. The HTTP is exercised by
// pointing ArenaScreen at a live API; what needs a BENCH is the parsing,
// because its failure mode is silence: a wrong key or an unhandled shape
// returns an EMPTY LIST, which renders as "nobody ranked here yet" and looks
// exactly like a fresh ladder.
//
// Every fixture below is a real response shape from server/tests/api_smoke.sh
// output, not an invented one.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class LadderClientBench
    {
        static int passed, failed;
        static readonly List<string> log = new List<string>();
        static void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }

        public static bool RunPure()
        {
            passed = 0; failed = 0; log.Clear();

            log.Add("== A. an array of objects, which Field() cannot do ==");
            string lb = "{\"category\":\"FEATHER\",\"count\":2,\"entries\":["
              + "{\"rank\":1,\"category\":\"FEATHER\",\"rating\":1295,\"deviation\":120.5,"
              + "\"provisional\":false,\"robotId\":\"r-1\",\"robotName\":\"Defiant\",\"owner\":\"DF\","
              + "\"activeSnapshotId\":\"s-1\",\"updatedAt\":\"2026-08-09T00:00:00Z\"},"
              + "{\"rank\":2,\"category\":\"FEATHER\",\"rating\":1160.6,\"deviation\":255.1,"
              + "\"provisional\":true,\"robotId\":\"r-2\",\"robotName\":\"Smoky\",\"owner\":\"SM\","
              + "\"activeSnapshotId\":null,\"updatedAt\":\"2026-08-09T00:00:00Z\"}]}";
            var objs = LadderClient.Objects(lb, "entries");
            Check(objs.Count == 2, "two entries split out of one array");
            Check(objs[0].Contains("Defiant") && objs[1].Contains("Smoky"), "…in order, not jumbled");
            Check(RobotWorker.Field(objs[1], "rank") == "2", "…and each is independently readable");
            // The silent-failure case: a wrong key must not look like an
            // empty ladder.
            Check(LadderClient.Objects(lb, "nope").Count == 0, "an unknown key yields nothing rather than throwing");
            Check(LadderClient.Objects("", "entries").Count == 0, "an empty body yields nothing");
            Check(LadderClient.Objects("{\"entries\":[]}", "entries").Count == 0, "a genuinely empty board yields nothing");

            log.Add("== B. braces that are not structure ==");
            // A robot NAME is player-controlled text. A brace or a bracket in
            // it must not end an object, or one troll renames the leaderboard
            // into nothing.
            string tricky = "{\"entries\":[{\"rank\":1,\"robotName\":\"Bracket}Bot\",\"owner\":\"x\"},"
                          + "{\"rank\":2,\"robotName\":\"Square]Bot\",\"owner\":\"y\"}]}";
            var t = LadderClient.Objects(tricky, "entries");
            Check(t.Count == 2, "a '}' inside a robot name does not end the object");
            Check(RobotWorker.Field(t[0], "robotName") == "Bracket}Bot", "…and the name survives intact");
            Check(RobotWorker.Field(t[1], "robotName") == "Square]Bot", "…as does a ']'");

            log.Add("== C. nested objects inside an entry ==");
            // rating_deltas is a nested object. Brace-counting has to survive
            // it; anything regex-shaped would stop at the first inner '}'.
            string nested = "{\"matches\":[{\"matchId\":\"m-1\",\"outcome\":\"WON\","
                          + "\"ratingDeltas\":{\"challenger\":{\"before\":1200,\"after\":1362}},"
                          + "\"replayUrls\":[\"file:///a.rbr.gz\",\"file:///b.json\"]}]}";
            var n = LadderClient.Objects(nested, "matches");
            Check(n.Count == 1, "one match, despite two nested objects inside it");
            Check(RobotWorker.Field(n[0], "outcome") == "WON", "…and its own fields still read");

            log.Add("== D. string arrays — the replay urls ==");
            var urls = LadderClient.StringArray(n[0], "replayUrls");
            Check(urls.Count == 2, "both replay urls parse");
            // The ordering contract from FightWorkerLoop: the RECORDING is
            // first, the summary last, so a caller can take [0] and play it.
            Check(urls[0].EndsWith(".rbr.gz"), "…the playable recording is FIRST, so [0] is safe to hand to ReplayPlayer");
            Check(LadderClient.StringArray("{\"replayUrls\":[]}", "replayUrls").Count == 0, "an empty url array is empty");
            Check(LadderClient.StringArray("{\"x\":1}", "replayUrls").Count == 0, "a missing url array is empty, not a crash");

            log.Add("== E. the fields the screen actually renders ==");
            // provisional drives what the board SAYS about a rank, so parsing
            // it wrong is a lie rather than a blank.
            Check(RobotWorker.Field(objs[0], "provisional") == "false", "provisional reads false when earned");
            Check(RobotWorker.Field(objs[1], "provisional") == "true", "…and true when not");
            Check(RobotWorker.Field(objs[1], "activeSnapshotId") == null, "a JSON null comes back as null, not \"null\"");

            log.Add(" RESULT: " + passed + " pass, " + failed + " fail" + (failed == 0 ? " - ALL GREEN" : ""));
            Debug.Log("[LadderClientBench] RESULT: " + passed + " pass, " + failed + " fail");
            try
            {
                System.IO.File.WriteAllText(Application.dataPath + "/Phase1/qa_ladder_client.txt",
                                            string.Join("\n", log.ToArray()) + "\n");
            }
            catch (System.Exception e) { Debug.LogWarning("could not write report: " + e.Message); }
            return failed == 0;
        }
    }
}
