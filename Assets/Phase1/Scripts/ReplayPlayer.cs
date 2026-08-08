// ===========================================================================
// ReplayPlayer.cs — Multiplayer v3, Phase M0 piece 2b: playback.
//
// Design doc: Multiplayer_V3_Design_Doc.md §3.2.
//
// Playback is kinematic and dumb ON PURPOSE. It spawns both builds through
// the SAME SpawnBot path a fight uses, freezes every rigidbody, and then
// writes recorded world transforms straight onto the parts. Nothing about the
// original run is re-derived here: no physics, no program, no AI, no damage.
// That is what makes §5.4 true — a replay cannot disagree with its result,
// because it is not computing anything.
//
// It is also, per §3.2, a single-player feature in its own right (career fight
// replays), which is why it lives in the client and earns its keep before the
// server exists.
//
// Two honest limits of v1, stated here rather than discovered later:
//   * WHEELS are not recorded. Wheels are not entries in CompoundRobot.parts
//     — they are RaycastWheelDrive channels whose visuals hang off the root.
//     They ride the recorded root transform, so an intact robot looks right;
//     after a break-up the shed island's wheels lag its body. Fixing that
//     means recording drive channels too, and is deliberately deferred.
//   * A part destroyed mid-bout is hidden at its destruction event, not
//     shattered — the shard VFX is a physics effect and physics is off.
// ===========================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public class ReplayPlayer : MonoBehaviour
    {
        public ReplayHeader header;
        public bool finished;
        public string error;
        public float time;
        public float speed = 1f;
        public int framesApplied;
        public int destroysApplied;
        public bool showDamageNumbers = true;

        /// <summary>Applied frames as a fraction of the frames the header says
        /// the recording covers. The bench asserts on this — a player that
        /// silently stops halfway is the failure mode worth catching.</summary>
        public float Coverage
        {
            get
            {
                if (header == null || header.duration <= 0f) return 0f;
                float expect = header.duration * header.hz + 1f;
                return expect <= 0f ? 0f : framesApplied / expect;
            }
        }

        BuilderManager bm;
        CompoundRobot botA, botB;
        List<ReplayFrame> framesA = new List<ReplayFrame>();
        List<ReplayFrame> framesB = new List<ReplayFrame>();
        List<ReplayEvent> events = new List<ReplayEvent>();
        int cursorA, cursorB, cursorE;
        FightCamera cam;
        Action<ReplayPlayer> onDone;

        public static ReplayPlayer Play(BuilderManager bm, string path, float speed,
                                        bool attachCamera, Action<ReplayPlayer> onDone)
        {
            var go = new GameObject("replay_player");
            var rp = go.AddComponent<ReplayPlayer>();
            rp.bm = bm;
            rp.speed = speed <= 0f ? 1f : speed;
            rp.onDone = onDone;
            rp.Load(path, attachCamera);
            return rp;
        }

        void Load(string path, bool attachCamera)
        {
            ReplayHeader h; List<ReplayFrame> frames; List<ReplayEvent> evs; string err;
            if (!ReplayFile.Read(path, out h, out frames, out evs, out err)) { Fail(err); return; }
            header = h; events = evs;

            foreach (var f in frames) (f.s == 0 ? framesA : framesB).Add(f);
            framesA.Sort((x, y) => x.t.CompareTo(y.t));
            framesB.Sort((x, y) => x.t.CompareTo(y.t));
            if (framesA.Count == 0 || framesB.Count == 0) { Fail("replay: a side has no frames"); return; }

            // Parse both builds BEFORE the arena exists — LoadSnapshot forces
            // build mode and would tear an arena down under us. SpawnBot reads
            // only PlacedPart DATA (never its build-space GameObject), which is
            // what makes these detached copies spawnable later.
            List<BuilderManager.PlacedPart> listA, listB;
            Vector3 axisA, axisB;
            if (!MatchRunner.ParseBuild(bm, header.aBuild, out listA, out axisA, out err)) { Fail("A: " + err); return; }
            if (!MatchRunner.ParseBuild(bm, header.bBuild, out listB, out axisB, out err)) { Fail("B: " + err); return; }

            bm.EnterMatchArena(header.arenaHalf);
            RaycastWheelDrive dA, dB;
            botA = bm.SpawnBot(listA, header.aName, -axisA * 4f, Quaternion.identity, axisA, out dA);
            botB = bm.SpawnBot(listB, header.bName, axisA * 4f, Quaternion.LookRotation(-axisA), axisB, out dB);
            if (botA == null || botB == null) { Fail("replay: a build failed to spawn"); return; }

            string idErr = CheckParts(botA, header.aParts, "A") ?? CheckParts(botB, header.bParts, "B");
            if (idErr != null) { Fail(idErr); return; }

            Freeze(botA, dA); Freeze(botB, dB);
            RobotVisuals.Install(botA, true);
            RobotVisuals.Install(botB, false);

            if (attachCamera && Camera.main != null)
            {
                cam = Camera.main.gameObject.AddComponent<FightCamera>();
                cam.a = botA.transform; cam.b = botB.transform;
                cam.sideDir = Vector3.Cross(Vector3.up, axisA).normalized;
                cam.clampHalf = header.arenaHalf - 0.8f;
                cam.SnapNow();
            }

            time = 0f;
            Apply(0f);
        }

        static string CheckParts(CompoundRobot bot, List<string> ids, string tag)
        {
            if (bot.parts.Count != ids.Count)
                return "replay: side " + tag + " spawned " + bot.parts.Count +
                       " parts, recording has " + ids.Count;
            for (int i = 0; i < ids.Count; i++)
                if (bot.parts[i].spec.id != ids[i])
                    return "replay: side " + tag + " part " + i + " is '" +
                           bot.parts[i].spec.id + "', recording says '" + ids[i] + "'";
            return null;
        }

        static void Freeze(CompoundRobot bot, RaycastWheelDrive drive)
        {
            bot.controlSource = ControlSource.AI;
            bot.combatEnabled = false;
            if (bot.rb != null)
            {
                bot.rb.isKinematic = true;
                bot.rb.detectCollisions = false;
            }
            if (drive != null)
            {
                drive.ClearWheelCmd();
                drive.directWheelCmd = false;
                drive.aiThrottle = 0f; drive.aiSteer = 0f;
                drive.enabled = false;
            }
            foreach (var a in bot.GetComponentsInChildren<Actuator>(true)) a.enabled = false;
            foreach (var s in bot.GetComponentsInChildren<SpinnerWeapon>(true)) s.enabled = false;
            var pr = bot.GetComponent<ProgramRunner>(); if (pr != null) pr.enabled = false;
            var ac = bot.GetComponent<AIController>(); if (ac != null) ac.enabled = false;
        }

        void Fail(string err)
        {
            error = err;
            finished = true;
            Debug.LogWarning("[ReplayPlayer] " + err);
            Done();
        }

        void Update()
        {
            if (finished || header == null) return;
            time += Time.deltaTime * speed;
            Apply(time);
            if (time >= header.duration) { finished = true; Done(); }
        }

        void Done()
        {
            var cb = onDone; onDone = null;
            if (cb != null) cb(this);
        }

        void Apply(float t)
        {
            // Only side A advances the coverage counter: both sides are
            // sampled on every frame, so counting both made a perfect
            // playback report 200% and the 95% floor unfalsifiable.
            ApplySide(botA, framesA, ref cursorA, t, true);
            ApplySide(botB, framesB, ref cursorB, t, false);
            ApplyEvents(t);
        }

        void ApplySide(CompoundRobot bot, List<ReplayFrame> fr, ref int cursor, float t, bool counts)
        {
            if (bot == null || fr.Count == 0) return;
            while (cursor + 1 < fr.Count && fr[cursor + 1].t <= t) { cursor++; if (counts) framesApplied++; }
            var a = fr[cursor];
            var b = cursor + 1 < fr.Count ? fr[cursor + 1] : a;
            float span = b.t - a.t;
            float u = span > 0.0001f ? Mathf.Clamp01((t - a.t) / span) : 0f;

            Vector3 p; Quaternion q;
            Lerp(a.f, b.f, 0, u, out p, out q);
            bot.transform.SetPositionAndRotation(p, q);

            int n = Mathf.Min(bot.parts.Count, (a.f.Length - 7) / 7);
            for (int i = 0; i < n; i++)
            {
                var part = bot.parts[i];
                if (part == null || part.go == null) continue;
                Lerp(a.f, b.f, 7 + i * 7, u, out p, out q);
                part.go.transform.SetPositionAndRotation(p, q);
            }
        }

        static void Lerp(float[] a, float[] b, int o, float u, out Vector3 p, out Quaternion q)
        {
            bool ok = b != null && b.Length > o + 6;
            Vector3 pa = new Vector3(a[o], a[o + 1], a[o + 2]);
            Quaternion qa = new Quaternion(a[o + 3], a[o + 4], a[o + 5], a[o + 6]);
            if (!ok || u <= 0f) { p = pa; q = Norm(qa); return; }
            Vector3 pb = new Vector3(b[o], b[o + 1], b[o + 2]);
            Quaternion qb = new Quaternion(b[o + 3], b[o + 4], b[o + 5], b[o + 6]);
            p = Vector3.Lerp(pa, pb, u);
            q = Quaternion.Slerp(Norm(qa), Norm(qb), u);
        }

        static Quaternion Norm(Quaternion q)
        {
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 0.0001f) return Quaternion.identity;
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }

        void ApplyEvents(float t)
        {
            while (cursorE < events.Count && events[cursorE].t <= t)
            {
                var e = events[cursorE++];
                if (e.e != "hit") continue;
                if (showDamageNumbers && e.a > 0f)
                    FloatingDamage.Create(new Vector3(e.x, e.y, e.z), e.a,
                        e.k == 1 ? FloatingDamage.Kind.Destroyed : FloatingDamage.Kind.Normal, null);
                if (e.k != 1) continue;
                var bot = e.s == 0 ? botA : botB;
                if (bot == null || e.i < 0 || e.i >= bot.parts.Count) continue;
                var part = bot.parts[e.i];
                if (part != null && part.go != null) { part.go.SetActive(false); destroysApplied++; }
            }
        }

        /// <summary>Tear playback down and hand the editor back to build mode.</summary>
        public void Close()
        {
            if (cam != null) Destroy(cam);
            if (bm != null) bm.BackToBuild();
            Destroy(gameObject);
        }
    }
}
