// ===========================================================================
// RobotSnapshot.cs — Multiplayer v3, Phase M0 piece 1: the snapshot envelope.
//
// Design doc: Multiplayer_V3_Design_Doc.md §3.1 and §5.2.
//
// A "snapshot" is the immutable artifact a player uploads: the BUILD plus the
// PROGRAM plus the material choices, in the game's OWN serialization, wrapped
// in a signed envelope {payload, clientVersion, sha256}. The server treats the
// payload as opaque and lets a Unity worker running THIS code parse it — one
// implementation, zero drift (§5.2). So the rules here are deliberately dumb:
//
//   * the build is exactly BuilderManager.SnapshotString() text — the stamped
//     pipe-delimited format the game already saves garages and career robots
//     in, which already carries the per-part material as its last field. No
//     second build format exists to drift from the first.
//   * the program is exactly RobotProgram.ToJson().
//   * the envelope is JsonUtility over a payload STRING, not over a nested
//     object, so the hash covers the exact bytes the server stores and the
//     worker re-reads. Hashing a re-serialized object would let a formatting
//     change silently invalidate every stored snapshot.
//
// The hash is integrity, not authentication — it catches truncation and
// tampering-by-accident. Real anti-forgery is §5.2's rule that the worker runs
// the real Validate() against the payload, and §5.5's worker key. Nothing here
// pretends otherwise.
// ===========================================================================

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>The opaque part: everything needed to reconstruct one robot.</summary>
    [Serializable]
    public class SnapshotPayload
    {
        public const int PAYLOAD_VERSION = 1;

        public int payloadVersion = PAYLOAD_VERSION;
        public string robotName = "";
        /// <summary>BuilderManager.SnapshotString() text. Carries materials.</summary>
        public string build = "";
        /// <summary>RobotProgram.ToJson(); "" means no program (AI-driven).</summary>
        public string program = "";

        public string ToJson() { return JsonUtility.ToJson(this); }

        public static SnapshotPayload FromJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var p = JsonUtility.FromJson<SnapshotPayload>(s);
                if (p == null || string.IsNullOrEmpty(p.build)) return null;
                return p;
            }
            catch { return null; }
        }
    }

    /// <summary>The wire form: {payload, clientVersion, sha256}.</summary>
    [Serializable]
    public class SnapshotEnvelope
    {
        public const string KIND = "robotbrawl.snapshot";
        public const int ENVELOPE_VERSION = 1;

        public string kind = KIND;
        public int envelopeVersion = ENVELOPE_VERSION;
        public string clientVersion = "";
        /// <summary>JSON of SnapshotPayload — the hashed bytes, verbatim.</summary>
        public string payload = "";
        public string sha256 = "";

        public string ToJson() { return JsonUtility.ToJson(this); }

        public static SnapshotEnvelope FromJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try { return JsonUtility.FromJson<SnapshotEnvelope>(s); }
            catch { return null; }
        }
    }

    /// <summary>Metadata a validate job returns (§5.2). Computed from the real
    /// game code, never from anything the client claims.</summary>
    [Serializable]
    public class SnapshotMeta
    {
        public bool legal;
        public int massKg;
        public Vector3 aabb;
        public int partCount;
        public string programHash = "";
        public bool hasProgram;
        public List<string> partsManifest = new List<string>();
        public List<string> failReasons = new List<string>();
    }

    public static class RobotSnapshot
    {
        // -------------------------------------------------------------- hash
        public static string Sha256Hex(string s)
        {
            if (s == null) s = "";
            using (var h = SHA256.Create())
            {
                byte[] d = h.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(64);
                for (int i = 0; i < d.Length; i++) sb.Append(d[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Stable identity for a program without revealing it (§1.3 —
        /// the scouting card may show that two robots share a program, never
        /// what the program is). "" for no program, so "no program" is not a
        /// hash collision target.</summary>
        public static string ProgramHash(string programJson)
        {
            return string.IsNullOrEmpty(programJson) ? "" : Sha256Hex(programJson);
        }

        /// <summary>Identifies the client that produced a snapshot. The build
        /// format stamp is in here on purpose: a snapshot made before a format
        /// bump must be diagnosable from the envelope alone.</summary>
        public static string ClientVersion()
        {
            string v = Application.version;
            if (string.IsNullOrEmpty(v)) v = "0.0";
            return v + "+" + BuilderManager.SNAP_STAMP + "+p" + SnapshotPayload.PAYLOAD_VERSION;
        }

        // ------------------------------------------------------------- wrap
        public static SnapshotEnvelope Wrap(SnapshotPayload p)
        {
            var env = new SnapshotEnvelope();
            env.clientVersion = ClientVersion();
            env.payload = p.ToJson();
            env.sha256 = Sha256Hex(env.payload);
            return env;
        }

        /// <summary>Export the builder's CURRENT build plus a program.</summary>
        public static SnapshotEnvelope Export(BuilderManager bm, string robotName, RobotProgram prog)
        {
            var p = new SnapshotPayload();
            p.robotName = robotName ?? "";
            p.build = bm.SnapshotString();
            p.program = prog != null ? prog.ToJson() : "";
            return Wrap(p);
        }

        /// <summary>Export from raw strings — the path the ARENA tab will use
        /// when enlisting a career robot that is not the one on the bench.</summary>
        public static SnapshotEnvelope ExportRaw(string robotName, string buildText, string programJson)
        {
            var p = new SnapshotPayload();
            p.robotName = robotName ?? "";
            p.build = buildText ?? "";
            p.program = programJson ?? "";
            return Wrap(p);
        }

        // ------------------------------------------------------------- open
        /// <summary>Verify and unwrap. Returns false with a reason string on
        /// any failure — callers must never see a half-opened envelope.</summary>
        public static bool Open(SnapshotEnvelope env, out SnapshotPayload payload, out string err)
        {
            payload = null; err = null;
            if (env == null) { err = "snapshot: null envelope"; return false; }
            if (env.kind != SnapshotEnvelope.KIND)
            { err = "snapshot: wrong kind '" + env.kind + "'"; return false; }
            if (env.envelopeVersion > SnapshotEnvelope.ENVELOPE_VERSION)
            { err = "snapshot: envelope v" + env.envelopeVersion + " is newer than this client"; return false; }
            if (string.IsNullOrEmpty(env.payload)) { err = "snapshot: empty payload"; return false; }
            if (Sha256Hex(env.payload) != env.sha256)
            { err = "snapshot: sha256 mismatch — payload altered or truncated"; return false; }

            var p = SnapshotPayload.FromJson(env.payload);
            if (p == null) { err = "snapshot: payload is not a readable snapshot"; return false; }
            if (p.payloadVersion > SnapshotPayload.PAYLOAD_VERSION)
            { err = "snapshot: payload v" + p.payloadVersion + " is newer than this client"; return false; }
            payload = p;
            return true;
        }

        public static bool OpenJson(string envelopeJson, out SnapshotPayload payload, out string err)
        {
            return Open(SnapshotEnvelope.FromJson(envelopeJson), out payload, out err);
        }

        /// <summary>Load a snapshot INTO the builder (build mode) and hand back
        /// its program. This is the round trip Export() must survive.</summary>
        public static bool ImportInto(BuilderManager bm, SnapshotEnvelope env,
                                      out RobotProgram prog, out string err)
        {
            prog = null;
            SnapshotPayload p;
            if (!Open(env, out p, out err)) return false;

            int n = bm.LoadSnapshot(p.build);
            if (n <= 0) { err = "snapshot: build loaded 0 parts"; return false; }

            if (!string.IsNullOrEmpty(p.program))
            {
                prog = RobotProgram.FromJson(p.program);
                if (prog == null)
                { err = "snapshot: program did not parse (pre-v2 program?)"; return false; }
            }
            return true;
        }

        // ------------------------------------------------------------- meta
        /// <summary>What a validate job reports (§5.2). Requires the build to
        /// be loaded in `bm` already — it reads the real builder state so the
        /// same code that refuses an illegal robot in the builder refuses it
        /// here. Category assignment lives server-side in M1; this is the
        /// client-side half that produces the numbers it decides from.</summary>
        public static SnapshotMeta Describe(BuilderManager bm, SnapshotPayload p)
        {
            var m = new SnapshotMeta();
            m.massKg = bm.BuildMassInt;
            m.aabb = bm.BuildAabbSize();
            m.partCount = bm.placed.Count;
            m.hasProgram = !string.IsNullOrEmpty(p.program);
            m.programHash = ProgramHash(p.program);

            var ids = new List<string>();
            foreach (var pp in bm.placed) if (pp.def != null) ids.Add(pp.def.id);
            ids.Sort(StringComparer.Ordinal);
            m.partsManifest = ids;

            string buildErr = bm.Validate();
            if (buildErr != null) m.failReasons.Add(buildErr);

            if (m.hasProgram)
            {
                var prog = RobotProgram.FromJson(p.program);
                if (prog == null) m.failReasons.Add("program did not parse");
                else
                {
                    var idSet = new List<string>();
                    foreach (var pp in bm.placed) if (pp.def != null && !idSet.Contains(pp.def.id)) idSet.Add(pp.def.id);
                    string progErr = prog.Validate(idSet);
                    if (progErr != null) m.failReasons.Add(progErr);
                }
            }
            m.legal = m.failReasons.Count == 0;
            return m;
        }
    }
}
