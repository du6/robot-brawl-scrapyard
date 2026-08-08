// ===========================================================================
// ReplayBench.cs — Multiplayer v3, Phase M0 acceptance.
//
// Design doc acceptance (§8, Phase M0): "a bench (ReplayBench) runs snapshot →
// MatchRunner → replay file → playback, asserting verdict consistency and
// playback frame coverage; existing green set stays green".
//
// It also asserts the one thing that is a PRODUCT promise rather than a
// mechanism: §1.3's "the program is never shown". The replay file is scanned,
// decompressed, for any trace of either program — not for a field named
// "program", for the actual bytes. A boundary you only assert structurally is
// a boundary you will lose during a refactor.
//
// House rules honoured here (CriticLoop6): every claim is measured over the
// artifact rather than over a named list, and no threshold is loosened without
// saying why in the file.
// ===========================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ReplayBench : MonoBehaviour
    {
        public static ReplayBench Run()
        {
            return new GameObject("replay_bench").AddComponent<ReplayBench>();
        }

        public int passed, failed;
        public bool finished;
        public string report = "";
        readonly List<string> log = new List<string>();

        void Check(bool ok, string what)
        {
            if (ok) { passed++; log.Add("PASS  " + what); }
            else { failed++; log.Add("FAIL  " + what); }
        }
        void Note(string s) { log.Add("      " + s); }

        static string F(float v) { return v.ToString("F2"); }

        IEnumerator Start()
        {
            var bm = UnityEngine.Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) { Check(false, "BuilderManager in scene"); Finish(); yield break; }

            string ownerBuild = bm.SnapshotString();
            var savedData = Career.Data;
            bool savedAuto = Career.autosave, savedActive = Career.active;
            Career.autosave = false;

            // ================================================ A. snapshot
            int n = bm.LoadSnapshot(VerbBench.ARMED);
            Check(n > 0, "fixture rig loads (" + n + " parts)");
            if (n <= 0) { Finish(); yield break; }
            yield return null;

            var progA = RobotProgram.Brawler();
            var progB = RobotProgram.WallShy();

            var envA = RobotSnapshot.Export(bm, "Alpha", progA);
            var envA2 = RobotSnapshot.Export(bm, "Alpha", progA);
            Check(envA.sha256 == envA2.sha256,
                  "sha256 is stable across two exports of the same state");
            Check(envA.sha256 == RobotSnapshot.Sha256Hex(envA.payload),
                  "envelope hash covers the stored payload bytes");
            Check(!string.IsNullOrEmpty(envA.clientVersion) &&
                  envA.clientVersion.Contains(BuilderManager.SNAP_STAMP),
                  "clientVersion carries the build-format stamp (" + envA.clientVersion + ")");

            SnapshotPayload payA; string err;
            Check(RobotSnapshot.Open(envA, out payA, out err), "envelope opens: " + (err ?? "ok"));
            Check(payA != null && payA.build == bm.SnapshotString(),
                  "payload build text is byte-identical to the builder's");
            Check(payA != null && payA.program == progA.ToJson(),
                  "payload program json is byte-identical to the program's");

            // Tamper detection — one character, in the middle of the payload.
            var bad = SnapshotEnvelope.FromJson(envA.ToJson());
            int mid = bad.payload.Length / 2;
            bad.payload = bad.payload.Substring(0, mid) +
                          (bad.payload[mid] == 'x' ? 'y' : 'x') + bad.payload.Substring(mid + 1);
            SnapshotPayload junk;
            Check(!RobotSnapshot.Open(bad, out junk, out err) && junk == null,
                  "a one-character payload edit is refused (" + err + ")");

            var truncated = SnapshotEnvelope.FromJson(envA.ToJson());
            truncated.payload = truncated.payload.Substring(0, truncated.payload.Length / 2);
            Check(!RobotSnapshot.Open(truncated, out junk, out err), "a truncated payload is refused");

            var wrongKind = SnapshotEnvelope.FromJson(envA.ToJson());
            wrongKind.kind = "not.a.snapshot";
            Check(!RobotSnapshot.Open(wrongKind, out junk, out err), "a foreign envelope kind is refused");

            var future = SnapshotEnvelope.FromJson(envA.ToJson());
            future.envelopeVersion = SnapshotEnvelope.ENVELOPE_VERSION + 1;
            Check(!RobotSnapshot.Open(future, out junk, out err),
                  "an envelope from a newer client is refused, not half-read");

            // Envelope survives a JSON round trip (this is the wire path).
            var wire = SnapshotEnvelope.FromJson(envA.ToJson());
            SnapshotPayload payWire;
            Check(RobotSnapshot.Open(wire, out payWire, out err) &&
                  payWire.build == payA.build && payWire.program == payA.program,
                  "envelope survives a JSON round trip intact");

            // Meta / validate-job shape.
            var meta = RobotSnapshot.Describe(bm, payA);
            Check(meta.massKg > 0, "meta mass is real (" + meta.massKg + " kg)");
            Check(meta.aabb.x > 0f && meta.aabb.y > 0f && meta.aabb.z > 0f,
                  "meta size box is real (" + F(meta.aabb.x) + " x " + F(meta.aabb.y) + " x " + F(meta.aabb.z) + ")");
            Check(meta.partCount == n, "meta part count matches the loaded build");
            Check(meta.partsManifest.Count == n, "meta parts manifest lists every part");
            Check(meta.programHash == RobotSnapshot.Sha256Hex(payA.program),
                  "meta program hash is the hash of the program, not of the build");
            Check(RobotSnapshot.ProgramHash("") == "",
                  "no program hashes to empty, not to a collidable constant");
            Check(meta.legal, "the fixture rig + Brawler validates legal: " +
                  (meta.failReasons.Count == 0 ? "no reasons" : string.Join(" / ", meta.failReasons.ToArray())));

            // Import round trip: the builder can be rebuilt from the envelope.
            bm.LoadSnapshot(VerbBench.ARMED);
            yield return null;
            string beforeImport = bm.SnapshotString();
            RobotProgram backProg;
            Check(RobotSnapshot.ImportInto(bm, envA, out backProg, out err),
                  "import reconstructs a build: " + (err ?? "ok"));
            Check(bm.SnapshotString() == beforeImport,
                  "imported build is byte-identical to the exported one");
            Check(backProg != null && backProg.ToJson() == progA.ToJson(),
                  "imported program is byte-identical to the exported one");
            yield return null;

            var envB = RobotSnapshot.Export(bm, "Beta", progB);
            Check(envB.sha256 != envA.sha256, "two different programs give two different hashes");

            // ================================================ B. match
            string matchId = "bench" + (Mathf.Abs(envA.sha256.GetHashCode()) % 100000);
            MatchRunner.MatchResult mres = null;
            var mr = MatchRunner.Run(envA, envB, new int[] { 11, 22, 33 }, matchId, 10f, true,
                                     r => { mres = r; });
            float t0 = Time.realtimeSinceStartup;
            while (mres == null && Time.realtimeSinceStartup - t0 < 300f) yield return null;
            if (mr != null) Destroy(mr.gameObject);

            Check(mres != null, "match completed within the bench budget");
            if (mres == null) { Finish(); yield break; }
            Check(mres.Ok, "match ran clean: " + (mres.error ?? "ok"));
            Check(mres.bouts.Count >= 2 && mres.bouts.Count <= 3,
                  "best-of-3 ran " + mres.bouts.Count + " bouts");
            Check(mres.verdict == "A" || mres.verdict == "B" || mres.verdict == "Draw",
                  "match has a verdict: " + mres.verdict + " (by " + mres.decidedBy + ")");
            Note("record A " + mres.aWins + " / B " + mres.bWins + " / draw " + mres.draws);

            // Seeds must actually change the fight. Two bouts landing on the
            // identical duration AND identical damage on both sides would mean
            // the seed is decorative — the whole point of §1.4's three seeds.
            if (mres.bouts.Count >= 2)
            {
                var b0 = mres.bouts[0]; var b1 = mres.bouts[1];
                bool same = Mathf.Abs(b0.simSeconds - b1.simSeconds) < 0.001f &&
                            Mathf.Abs(b0.aDealt - b1.aDealt) < 0.001f &&
                            Mathf.Abs(b0.bDealt - b1.bDealt) < 0.001f;
                Check(!same, "different seeds produce different bouts");
            }

            foreach (var b in mres.bouts)
            {
                Note("bout " + b.bout + " seed " + b.seed + " -> " + b.winner +
                     " (" + b.outcome + ", " + b.cause + ") " +
                     F(b.simSeconds) + "s  dealt A " + F(b.aDealt) + " B " + F(b.bDealt));
                Check(b.simSeconds > 0.5f, "bout " + b.bout + " actually simulated (" + F(b.simSeconds) + "s)");
                // A bout the two robots never touch in is not a close fight,
                // it is a broken one — and it scores as a draw either way, so
                // nothing downstream would ever have told us.
                Check(b.hits > 0, "bout " + b.bout + " had contact (" + b.hits + " damage exchanges)");
                Note("bout " + b.bout + " dead air after the last hit: " + F(b.DeadAir) + "s of " + F(b.simSeconds) + "s");
                Check(!string.IsNullOrEmpty(b.replayPath) && File.Exists(b.replayPath),
                      "bout " + b.bout + " wrote a replay file");
            }

            // ================================================ C. replay file
            var first = mres.bouts[0];
            if (string.IsNullOrEmpty(first.replayPath) || !File.Exists(first.replayPath))
            { Check(false, "a replay to read back"); Finish(); yield break; }

            long bytes = new FileInfo(first.replayPath).Length;
            Note("replay size " + bytes + " B for " + F(first.simSeconds) + "s");

            ReplayHeader head; List<ReplayFrame> frames; List<ReplayEvent> events;
            Check(ReplayFile.Read(first.replayPath, out head, out frames, out events, out err),
                  "replay reads back: " + (err ?? "ok"));
            if (head == null) { Finish(); yield break; }

            Check(head.seed == first.seed, "replay header carries its seed");
            Check(head.aBuild == payA.build, "replay carries side A's build verbatim");
            Check(head.aParts.Count > 0 && head.bParts.Count > 0, "replay names both sides' parts");
            Check(ReplayFile.VerdictOf(events) == first.winner,
                  "replay verdict matches the match result (" + ReplayFile.VerdictOf(events) +
                  " vs " + first.winner + ")");

            // Frame coverage of the RECORDING itself: 10 Hz over the bout,
            // two sides per sample. Tolerance is +/-4 samples, which is the
            // bell/verdict boundary rounding, not a fudge factor.
            int perSide = 0;
            foreach (var f in frames) if (f.s == 0) perSide++;
            float expect = head.duration * ReplayRecorder.HZ;
            Check(Mathf.Abs(perSide - expect) <= 4f,
                  "recording covers the bout at 10 Hz (" + perSide + " samples, expected ~" + F(expect) + ")");
            int aSide = 0, bSide = 0;
            foreach (var f in frames) { if (f.s == 0) aSide++; else bSide++; }
            Check(aSide == bSide, "both sides are sampled on every frame");

            int stride = 7 + 7 * head.aParts.Count;
            bool widthOk = true;
            foreach (var f in frames) if (f.s == 0 && (f.f == null || f.f.Length != stride)) widthOk = false;
            Check(widthOk, "every side-A frame is root + 7 floats per part");

            // §1.3 — THE PROGRAM IS NEVER SHOWN. Scan the decompressed bytes.
            string raw = Gunzip(first.replayPath);
            Check(raw.Length > 0, "replay decompresses");
            Check(raw.IndexOf(payA.program, StringComparison.Ordinal) < 0,
                  "side A's program does not appear in the replay");
            SnapshotPayload payB2; RobotSnapshot.Open(envB, out payB2, out err);
            Check(payB2 != null && raw.IndexOf(payB2.program, StringComparison.Ordinal) < 0,
                  "side B's program does not appear in the replay");
            Check(raw.IndexOf("\"hats\"", StringComparison.Ordinal) < 0,
                  "no program structure leaks into the replay at all");

            // ================================================ D. playback
            ReplayPlayer done = null;
            var rp = ReplayPlayer.Play(bm, first.replayPath, 30f, false, p => { done = p; });
            t0 = Time.realtimeSinceStartup;
            while (done == null && Time.realtimeSinceStartup - t0 < 120f) yield return null;

            Check(done != null, "playback ran to the end");
            if (done != null)
            {
                Check(string.IsNullOrEmpty(done.error), "playback clean: " + (done.error ?? "ok"));
                Check(done.Coverage >= 0.95f && done.Coverage <= 1.05f,
                      "playback applied " + done.framesApplied + " frames, coverage " +
                      F(done.Coverage * 100f) + "% (band 95-105%)");
                int destroyedInReplay = 0;
                foreach (var e in events) if (e.e == "hit" && e.k == 1) destroyedInReplay++;
                Check(done.destroysApplied == destroyedInReplay,
                      "playback hid every part the recording says was destroyed (" +
                      done.destroysApplied + "/" + destroyedInReplay + ")");
                Note("playback events: " + events.Count + ", destroys " + destroyedInReplay);
            }
            if (rp != null) rp.Close();
            yield return null;

            // ================================================ E. owner state
            Check(Career.Data == savedData, "the match handed the career object back untouched");
            Check(!Career.fightAutonomous, "fightAutonomous is not left set");
            Check(Mathf.Approximately(Time.timeScale, 1f), "timeScale is back to 1");
            Career.autosave = savedAuto;
            Career.active = savedActive;
            if (!string.IsNullOrEmpty(ownerBuild))
            {
                bm.LoadSnapshot(ownerBuild);
                yield return null;
                Check(bm.SnapshotString() == ownerBuild, "the builder is back on the build it started with");
            }

            Finish();
        }

        static string Gunzip(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                using (var r = new StreamReader(gz, new UTF8Encoding(false)))
                    return r.ReadToEnd();
            }
            catch { return ""; }
        }

        void Finish()
        {
            var sb = new StringBuilder();
            foreach (var l in log) { Debug.Log("[ReplayBench] " + l); sb.Append(l).Append('\n'); }
            report = sb.ToString();
            Debug.Log("[ReplayBench] ===== passed " + passed + " failed " + failed + " =====");
            try
            {
                File.WriteAllText(Application.dataPath + "/Phase1/qa_replay_bench.txt",
                                  "passed " + passed + " failed " + failed + "\n" + report);
            }
            catch { }
            finished = true;
        }
    }
}
