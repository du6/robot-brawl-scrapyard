// ===========================================================================
// LadderClient — the client half of M2's read side, 2026-08-09.
//
// The ladder has been computed, protected and stored server-side since this
// morning and no part of the GAME could ask for any of it. This is the seam
// that changes that: leaderboard, match, inbox, wallet.
//
// WHY HAND-PARSED. Same reason RobotWorker hand-builds its result JSON:
// JsonUtility cannot round-trip the shapes these endpoints actually return.
// It has no top-level array support, it turns a missing string into "" rather
// than null, and it silently drops fields it does not know. The API's replies
// are flat objects inside one array, which is small enough to read directly
// and specific enough that a general JSON library would be the larger risk.
// (RobotWorker.Field already handles a scalar; what was missing was splitting
// an array of OBJECTS, which is the one thing added here.)
//
// EVERY READ IS ANONYMOUS-CAPABLE. §1.3 makes standings, cards and replays
// public; only the inbox and wallet need a token. Pass one or do not.
// ===========================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RobotBrawl.Phase0
{
    public class LadderEntry
    {
        public int rank;
        public string category = "", robotName = "", owner = "", robotId = "", activeSnapshotId = "";
        public float rating, deviation;
        /// <summary>Deviation above the server's threshold — the rank is not
        /// yet earned. Shown, never hidden: a board that hides its own
        /// confidence lies about it.</summary>
        public bool provisional;
    }

    public class InboxEntry
    {
        public string matchId = "", status = "", outcome = "", role = "";
        public string opponent = "", myRobot = "", category = "";
        public int gap;
        /// <summary>replayUrls[0] is the playable recording; the summary is
        /// last. See FightWorkerLoop.</summary>
        public List<string> replayUrls = new List<string>();
    }

    public static class LadderClient
    {
        public static string BaseUrl = "http://localhost:5000";
        public static string Token = "";          // empty = anonymous

        public static string LastError = "";

        static UnityWebRequest Get(string path)
        {
            var req = UnityWebRequest.Get(BaseUrl.TrimEnd('/') + path);
            if (!string.IsNullOrEmpty(Token)) req.SetRequestHeader("Authorization", "Bearer " + Token);
            return req;
        }

        public static IEnumerator Leaderboard(string category, Action<List<LadderEntry>, string> done)
        {
            string path = "/v1/leaderboard" + (string.IsNullOrEmpty(category) ? "" : "/" + category);
            using (var req = Get(path))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }

                var rows = new List<LadderEntry>();
                foreach (string obj in Objects(req.downloadHandler.text, "entries"))
                {
                    var e = new LadderEntry();
                    int.TryParse(RobotWorker.Field(obj, "rank"), out e.rank);
                    float.TryParse(RobotWorker.Field(obj, "rating"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out e.rating);
                    float.TryParse(RobotWorker.Field(obj, "deviation"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out e.deviation);
                    e.category = RobotWorker.Field(obj, "category") ?? "";
                    e.robotName = RobotWorker.Field(obj, "robotName") ?? "";
                    e.owner = RobotWorker.Field(obj, "owner") ?? "";
                    e.robotId = RobotWorker.Field(obj, "robotId") ?? "";
                    e.activeSnapshotId = RobotWorker.Field(obj, "activeSnapshotId") ?? "";
                    e.provisional = string.Equals(RobotWorker.Field(obj, "provisional"), "true",
                                                  StringComparison.OrdinalIgnoreCase);
                    rows.Add(e);
                }
                done(rows, null);
            }
        }

        public static IEnumerator Inbox(Action<List<InboxEntry>, string> done)
        {
            using (var req = Get("/v1/inbox"))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }

                var rows = new List<InboxEntry>();
                foreach (string obj in Objects(req.downloadHandler.text, "matches"))
                {
                    var m = new InboxEntry();
                    m.matchId = RobotWorker.Field(obj, "matchId") ?? "";
                    m.status = RobotWorker.Field(obj, "status") ?? "";
                    m.outcome = RobotWorker.Field(obj, "outcome") ?? "";
                    m.role = RobotWorker.Field(obj, "role") ?? "";
                    m.opponent = RobotWorker.Field(obj, "opponent") ?? "";
                    m.myRobot = RobotWorker.Field(obj, "myRobot") ?? "";
                    m.category = RobotWorker.Field(obj, "category") ?? "";
                    int.TryParse(RobotWorker.Field(obj, "gap"), out m.gap);
                    m.replayUrls.AddRange(StringArray(obj, "replayUrls"));
                    rows.Add(m);
                }
                done(rows, null);
            }
        }

        public static IEnumerator Wallet(Action<long, string> done)
        {
            using (var req = Get("/v1/wallet"))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(0, req.error); yield break; }
                long bal; long.TryParse(RobotWorker.Field(req.downloadHandler.text, "balance"), out bal);
                done(bal, null);
            }
        }

        /// <summary>Download a replay to a local file and hand back the path.
        /// ReplayPlayer.Play takes a PATH, and the recording lives behind a
        /// url — so something has to bridge the two, and it is here rather
        /// than in the player, which should not know the ladder exists.</summary>
        public static IEnumerator FetchReplay(string url, Action<string, string> done)
        {
            if (string.IsNullOrEmpty(url)) { done(null, "no replay url"); yield break; }
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { LastError = req.error; done(null, req.error); yield break; }
                byte[] bytes = req.downloadHandler.data;
                if (bytes == null || bytes.Length == 0) { done(null, "replay was empty"); yield break; }
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "ladder_replays");
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, SafeName(url));
                    System.IO.File.WriteAllBytes(path, bytes);
                    done(path, null);
                }
                catch (Exception e) { LastError = e.Message; done(null, e.Message); }
            }
        }

        static string SafeName(string url)
        {
            int slash = url.LastIndexOf('/');
            string name = slash >= 0 && slash + 1 < url.Length ? url.Substring(slash + 1) : "replay.rbr.gz";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        // ------------------------------------------------------------- json
        /// <summary>Split "key":[ {...}, {...} ] into its object bodies.
        /// Brace-counting rather than a regex, because a nested object inside
        /// an entry (rating deltas, an aabb) makes any regex wrong. Strings
        /// are skipped so a brace inside a robot NAME cannot end an object.</summary>
        public static List<string> Objects(string json, string key)
        {
            var outv = new List<string>();
            if (string.IsNullOrEmpty(json)) return outv;
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return outv;
            int open = json.IndexOf('[', k);
            if (open < 0) return outv;

            int depth = 0, start = -1;
            bool inStr = false, esc = false;
            for (int i = open; i < json.Length; i++)
            {
                char ch = json[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (ch == '\\') esc = true;
                    else if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"') { inStr = true; continue; }
                if (ch == '{') { if (depth == 0) start = i; depth++; }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0) { outv.Add(json.Substring(start, i - start + 1)); start = -1; }
                }
                else if (ch == ']' && depth == 0) break;
            }
            return outv;
        }

        /// <summary>"key":["a","b"] -> the strings. Field() stops at the first
        /// delimiter and would return "[\"a".</summary>
        public static List<string> StringArray(string json, string key)
        {
            var outv = new List<string>();
            if (string.IsNullOrEmpty(json)) return outv;
            int k = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (k < 0) return outv;
            int open = json.IndexOf('[', k);
            if (open < 0) return outv;
            int close = open;
            bool inStr = false, esc = false;
            for (int i = open; i < json.Length; i++)
            {
                char ch = json[i];
                if (inStr) { if (esc) esc = false; else if (ch == '\\') esc = true; else if (ch == '"') inStr = false; continue; }
                if (ch == '"') { inStr = true; continue; }
                if (ch == ']') { close = i; break; }
            }
            string body = json.Substring(open + 1, Math.Max(0, close - open - 1));
            int p = 0;
            while (p < body.Length)
            {
                int q1 = body.IndexOf('"', p);
                if (q1 < 0) break;
                var sb = new System.Text.StringBuilder();
                int i2 = q1 + 1;
                while (i2 < body.Length && body[i2] != '"')
                {
                    if (body[i2] == '\\' && i2 + 1 < body.Length) { i2++; sb.Append(body[i2]); }
                    else sb.Append(body[i2]);
                    i2++;
                }
                outv.Add(sb.ToString());
                p = i2 + 1;
            }
            return outv;
        }
    }
}
