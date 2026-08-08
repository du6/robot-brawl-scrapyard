// ===========================================================================
// ReplayRecorder.cs — Multiplayer v3, Phase M0 piece 2a: the replay format
// and the recorder that produces it.
//
// Design doc: Multiplayer_V3_Design_Doc.md §3.2, §1.3 and §5.4.
//
// WHAT A REPLAY IS, AND WHAT IT DELIBERATELY IS NOT
//
// §5.4: the authoritative outcome is whatever the run produced, and the replay
// is a RECORDING of that run — not a re-simulation. So replay always matches
// result by construction and nothing ever needs deterministic cross-machine
// physics. That is the whole reason this file records transforms rather than
// inputs.
//
// §1.3: design is public, code is private. The replay records part transforms
// and damage events ONLY. It carries each robot's BUILD text (playback has to
// spawn something) and never its program, not even a hash. That boundary is
// structural, not a policy comment: there is no field here a modified client
// could read a program out of.
//
// FORMAT — gzip'd JSON-lines. One line per record, first char is the kind:
//   H {header json}   exactly one, first
//   F {frame json}    10 Hz of SIM time, one per side per sample
//   E {event json}    damage hits, part destruction, the verdict
//
// A frame's floats are [rootPos3, rootRot4, then 7 per part in parts order].
// Positions are rounded to 3 dp and rotations to 4 dp before writing: at
// 10 Hz over a 90 s bout that is the difference between a ~1.5 MB and a
// ~300 kB replay, and it is far below what any eye can see on playback.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    [Serializable]
    public class ReplayHeader
    {
        public const string KIND = "robotbrawl.replay";
        public const int REPLAY_VERSION = 1;

        public string kind = KIND;
        public int v = REPLAY_VERSION;
        public string matchId = "";
        public int bout;
        public int seed;
        public float arenaHalf = 7f;
        public float hz = ReplayRecorder.HZ;
        public string aName = "";
        public string bName = "";
        /// <summary>Build text only. Never a program — see §1.3.</summary>
        public string aBuild = "";
        public string bBuild = "";
        public List<string> aParts = new List<string>();
        public List<string> bParts = new List<string>();
        public float duration;
    }

    [Serializable]
    public class ReplayFrame
    {
        public float t;
        public int s;          // 0 = side A, 1 = side B
        public float[] f;      // root(7) + 7 per part
    }

    [Serializable]
    public class ReplayEvent
    {
        public float t;
        public string e = "";  // "hit" | "verdict"
        public int s = -1;     // victim side, -1 unknown
        public int i = -1;     // victim part index
        public float a;        // damage amount
        public float x, y, z;  // world position
        public int k;          // 1 = this hit destroyed the part
        public string o = "";  // verdict outcome
        public string c = "";  // verdict cause
    }

    /// <summary>Attach to a live fight; it samples until Finish() is called.
    /// Sampling runs on FixedUpdate against Time.time, so a bench running at
    /// timeScale 10 records the same 10 Hz of SIM time as a real-time fight —
    /// the replay is in fight seconds, never wall-clock seconds.</summary>
    public class ReplayRecorder : MonoBehaviour
    {
        public const float HZ = 10f;
        public const float DT = 1f / HZ;

        public ReplayHeader header = new ReplayHeader();
        public int frameCount;
        public int eventCount;
        /// <summary>Damage exchanges in this bout, and when the last one
        /// landed. A ladder bout with no contact is a defect, not a draw —
        /// so it has to be measurable from the result, not from watching.</summary>
        public int hitCount;
        public float lastHitT = -1f;
        public bool recording;

        CompoundRobot botA, botB;
        List<CompoundRobot.Part> partsA, partsB;
        readonly List<string> lines = new List<string>();
        readonly Dictionary<object, int> partKey = new Dictionary<object, int>();
        // last written transform per side, so a destroyed part holds its pose
        // instead of snapping to the origin on playback.
        float[] lastA, lastB;
        float t0, acc;
        Action<Vector3, float, bool, object> hitHook;

        public static ReplayRecorder Attach(GameObject host,
                                            CompoundRobot a, string aName, string aBuild,
                                            CompoundRobot b, string bName, string bBuild,
                                            string matchId, int bout, int seed, float arenaHalf)
        {
            var r = host.AddComponent<ReplayRecorder>();
            r.Begin(a, aName, aBuild, b, bName, bBuild, matchId, bout, seed, arenaHalf);
            return r;
        }

        void Begin(CompoundRobot a, string aName, string aBuild,
                   CompoundRobot b, string bName, string bBuild,
                   string matchId, int bout, int seed, float arenaHalf)
        {
            botA = a; botB = b;
            partsA = new List<CompoundRobot.Part>(a.parts);
            partsB = new List<CompoundRobot.Part>(b.parts);

            header.matchId = matchId ?? "";
            header.bout = bout;
            header.seed = seed;
            header.arenaHalf = arenaHalf;
            header.aName = aName ?? "A";
            header.bName = bName ?? "B";
            header.aBuild = aBuild ?? "";
            header.bBuild = bBuild ?? "";
            foreach (var p in partsA) header.aParts.Add(p.spec.id);
            foreach (var p in partsB) header.bParts.Add(p.spec.id);

            lastA = new float[7 + 7 * partsA.Count];
            lastB = new float[7 + 7 * partsB.Count];

            // Part identity for the damage stream. CompoundRobot.Part is a
            // class and DestroyPart never re-indexes `parts`, so a reference
            // is a stable key for the whole bout.
            partKey.Clear();
            for (int i = 0; i < partsA.Count; i++) partKey[partsA[i]] = i;
            for (int i = 0; i < partsB.Count; i++) partKey[partsB[i]] = 1000 + i;

            hitHook = OnHit;
            DamageResolver.OnHit += hitHook;

            t0 = Time.time;
            acc = DT;              // sample immediately, so frame 0 is the bell
            recording = true;
            lines.Clear();
            Sample();
        }

        void OnDisable() { Unhook(); }

        void Unhook()
        {
            if (hitHook != null) { DamageResolver.OnHit -= hitHook; hitHook = null; }
        }

        void OnHit(Vector3 pos, float amount, bool destroyed, object victimPart)
        {
            if (!recording) return;
            int key;
            if (victimPart == null || !partKey.TryGetValue(victimPart, out key)) return;
            var e = new ReplayEvent();
            e.t = Round(Time.time - t0, 100f);
            e.e = "hit";
            e.s = key >= 1000 ? 1 : 0;
            e.i = key >= 1000 ? key - 1000 : key;
            e.a = Round(amount, 100f);
            e.x = Round(pos.x, 1000f); e.y = Round(pos.y, 1000f); e.z = Round(pos.z, 1000f);
            e.k = destroyed ? 1 : 0;
            Write('E', JsonUtility.ToJson(e));
            eventCount++;
            hitCount++;
            lastHitT = e.t;
        }

        void FixedUpdate()
        {
            if (!recording) return;
            acc += Time.fixedDeltaTime;
            if (acc < DT) return;
            acc -= DT;
            Sample();
        }

        void Sample()
        {
            float t = Round(Time.time - t0, 100f);
            Emit(0, botA, partsA, lastA, t);
            Emit(1, botB, partsB, lastB, t);
            frameCount++;
        }

        void Emit(int side, CompoundRobot bot, List<CompoundRobot.Part> parts, float[] last, float t)
        {
            if (bot != null)
            {
                var tr = bot.transform;
                Pack(last, 0, tr.position, tr.rotation);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                // A destroyed part's GameObject is gone; hold its last pose so
                // playback has something continuous to hide.
                if (p != null && p.go != null)
                    Pack(last, 7 + i * 7, p.go.transform.position, p.go.transform.rotation);
            }
            var fr = new ReplayFrame();
            fr.t = t; fr.s = side;
            fr.f = (float[])last.Clone();
            Write('F', JsonUtility.ToJson(fr));
        }

        static void Pack(float[] a, int o, Vector3 p, Quaternion q)
        {
            a[o] = Round(p.x, 1000f); a[o + 1] = Round(p.y, 1000f); a[o + 2] = Round(p.z, 1000f);
            a[o + 3] = Round(q.x, 10000f); a[o + 4] = Round(q.y, 10000f);
            a[o + 5] = Round(q.z, 10000f); a[o + 6] = Round(q.w, 10000f);
        }

        static float Round(float v, float scale)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Mathf.Round(v * scale) / scale;
        }

        void Write(char kind, string json) { lines.Add(kind + " " + json); }

        public void Verdict(string outcome, string cause)
        {
            var e = new ReplayEvent();
            e.t = Round(Time.time - t0, 100f);
            e.e = "verdict"; e.o = outcome ?? ""; e.c = cause ?? "";
            Write('E', JsonUtility.ToJson(e));
            eventCount++;
        }

        /// <summary>Stop recording and write the file. Returns the full path,
        /// or null if the write failed (a failed replay must never take a
        /// match result down with it — the caller keeps its verdict).</summary>
        public string Finish(string path)
        {
            if (!recording) return null;
            recording = false;
            Unhook();
            header.duration = Round(Time.time - t0, 100f);

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using (var fs = File.Create(path))
                using (var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal))
                using (var w = new StreamWriter(gz, new UTF8Encoding(false)))
                {
                    w.Write('H'); w.Write(' '); w.Write(JsonUtility.ToJson(header)); w.Write('\n');
                    for (int i = 0; i < lines.Count; i++) { w.Write(lines[i]); w.Write('\n'); }
                }
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ReplayRecorder] write failed: " + ex.Message);
                return null;
            }
        }

        public static string DefaultDir()
        {
            return Path.Combine(Application.persistentDataPath, "replays");
        }

        public static string PathFor(string matchId, int bout)
        {
            return Path.Combine(DefaultDir(), matchId + "_b" + bout + ".rbr.gz");
        }
    }

    /// <summary>Reader half of the format. Kept beside the writer on purpose:
    /// a format with its reader in another file drifts.</summary>
    public static class ReplayFile
    {
        public static bool Read(string path, out ReplayHeader header,
                                out List<ReplayFrame> frames, out List<ReplayEvent> events,
                                out string err)
        {
            header = null; frames = new List<ReplayFrame>(); events = new List<ReplayEvent>(); err = null;
            if (!File.Exists(path)) { err = "replay: no file at " + path; return false; }
            try
            {
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var r = new StreamReader(gz, new UTF8Encoding(false)))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length < 3) continue;
                        char k = line[0];
                        string body = line.Substring(2);
                        if (k == 'H') header = JsonUtility.FromJson<ReplayHeader>(body);
                        else if (k == 'F') frames.Add(JsonUtility.FromJson<ReplayFrame>(body));
                        else if (k == 'E') events.Add(JsonUtility.FromJson<ReplayEvent>(body));
                    }
                }
            }
            catch (Exception ex) { err = "replay: unreadable — " + ex.Message; return false; }

            if (header == null) { err = "replay: no header line"; return false; }
            if (header.kind != ReplayHeader.KIND) { err = "replay: wrong kind '" + header.kind + "'"; return false; }
            if (header.v > ReplayHeader.REPLAY_VERSION)
            { err = "replay: v" + header.v + " is newer than this client"; return false; }
            return true;
        }

        public static string VerdictOf(List<ReplayEvent> events)
        {
            for (int i = events.Count - 1; i >= 0; i--)
                if (events[i].e == "verdict") return events[i].o;
            return "";
        }
    }
}
