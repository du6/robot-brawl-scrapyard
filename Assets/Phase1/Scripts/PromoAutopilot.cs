#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Drives the game for a CAPTURE, and exists for one reason: a
/// browser tab under automation is HIDDEN, so requestAnimationFrame never
/// fires (measured: 0 frames in 2 s) and the page has to be stepped by hand -
/// and under stepped frames Unity's input layer ignores synthetic key and
/// pointer events, so nothing can be driven from JavaScript. The game
/// therefore drives itself, through the same `Phase0Input.debugThrottle` /
/// `debugSteer` seam every bench uses.
///
/// It is compiled ONLY into the editor and development builds, and even
/// there it does nothing unless the page URL carries `promo=1`. A release
/// build - the one that is published - does not contain this file at all.
///
/// The route is deliberately the first minute a new player gets: drive to
/// the chest the guide points at, take it, cross to the parked machine, and
/// fight it. Nothing is faked; the footage is the game playing itself.</summary>
public class PromoAutopilot : MonoBehaviour
{
    public static bool Wanted
    {
        get
        {
            string url = Application.absoluteURL ?? "";
            return url.Contains("promo=1") || System.Environment.GetCommandLineArgs().Length > 0
                   && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rbPromo") >= 0;
        }
    }

    /// <summary>Where the route has got to, so a capture rig can label frames
    /// and know when the run is over without reading the screen.</summary>
    public static string Stage = "boot";
    public static bool Done;

    BuilderManager bm;

    public static void Install()
    {
        if (!Wanted || Object.FindFirstObjectByType<PromoAutopilot>() != null) return;
        var go = new GameObject("promo_autopilot");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<PromoAutopilot>();
    }

    IEnumerator Start()
    {
        Stage = "waiting";
        // The title screen owns the first frames; wait for a builder and a map.
        float dead = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < dead)
        {
            bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm != null && bm.mode == BuilderManager.Mode.Map && bm.testRobot != null) break;
            yield return null;
        }
        if (bm == null || bm.testRobot == null) { Stage = "no-map"; Done = true; yield break; }

        StartCoroutine(TendRewardBoxes());
        yield return Hold(1.0f, 0f, 0f, "settle");
        yield return DriveTo(() => NearestCrate(), 1.2f, 30f, "to-chest");
        yield return Hold(2.6f, 0f, 0f, "chest-open");     // the burst and the toast
        // CARD_REACH is 4 m: stopping at 6 left the encounter card unshown and
        // ChallengeParked with nothing to challenge, so the first take ended
        // eight metres short of its own fight.
        yield return DriveTo(() => EnemyAt(), 2.8f, 70f, "to-robot");
        yield return Hold(1.4f, 0f, 0f, "card");
        Stage = "challenge";
        for (int tries = 0; tries < 90 && bm.mode != BuilderManager.Mode.Fight; tries++)
        {
            bm.ChallengeParked();
            yield return null;
        }
        // The bout drives itself from here: PumpStickFight feeds the player's
        // side from this same seam, so a gentle forward lean is a real fight.
        float t0 = Time.realtimeSinceStartup;
        while (bm.mode == BuilderManager.Mode.Fight && Time.realtimeSinceStartup - t0 < 40f)
        {
            Stage = "fight";
            Phase0Input.debugThrottle = 0.85f;
            Phase0Input.debugSteer = Mathf.Sin((Time.realtimeSinceStartup - t0) * 1.7f) * 0.45f;
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        Stage = "result";
        yield return Hold(4f, 0f, 0f, "result");
        Stage = "done"; Done = true;
    }

    /// <summary>A reward box waits for a tap, and a synthetic tap does not
    /// reach Unity under stepped frames, so the run would stall on the first
    /// chest. Press the box's own buttons instead - the same onClick a finger
    /// fires, which is the house rule for driving UI in this project.</summary>
    IEnumerator TendRewardBoxes()
    {
        for (;;)
        {
            var box = RewardBox.active;
            if (box != null)
            {
                yield return new WaitForSecondsRealtime(1.1f);  // REALTIME: an open box sets timeScale 0, so a scaled wait never returns
                box.TestTapBody();
                yield return new WaitForSecondsRealtime(2.3f);  // the reveal
                box.TestTapClaim();
            }
            yield return null;
        }
    }

    Vector3? NearestCrate()
    {
        var best = bm.NearestCratePos();
        return best;
    }

    Vector3? EnemyAt()
    {
        var e = bm.YardParked;
        return e != null ? (Vector3?)e.rb.position : null;
    }

    /// <summary>Point the stick where the target is, in CAMERA space, which is
    /// exactly what a player's thumb does - the drive model turns a direction
    /// into a heading itself (BuilderManager.Drive.cs).</summary>
    IEnumerator DriveTo(System.Func<Vector3?> target, float stopAt, float seconds, string stage)
    {
        Stage = stage;
        float t0 = Time.realtimeSinceStartup;
        var cam = Camera.main;
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            var tp = target();
            if (tp == null) break;
            Vector3 me = bm.testRobot.rb.position, to = tp.Value; to.y = me.y;
            if ((to - me).magnitude <= stopAt) break;
            Vector3 flat = (to - me).normalized;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Phase0Input.debugThrottle = Vector3.Dot(flat, fwd);
            Phase0Input.debugSteer = Vector3.Dot(flat, right);
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
    }

    IEnumerator Hold(float seconds, float thr, float steer, string stage)
    {
        Stage = stage;
        float t0 = Time.realtimeSinceStartup;   // realtime throughout: a reward box pauses the game clock
        while (Time.realtimeSinceStartup - t0 < seconds)
        {
            Phase0Input.debugThrottle = thr; Phase0Input.debugSteer = steer;
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
    }
}
}
#endif
