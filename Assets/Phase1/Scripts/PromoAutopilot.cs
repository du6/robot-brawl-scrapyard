#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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

    /// <summary>Where the route has got to. Logged on every change, because
    /// the capture rig can read the browser console but cannot read a C#
    /// field - and a run that stalls silently costs a whole take to diagnose.</summary>
    static string stage = "boot";
    public static string Stage
    {
        get { return stage; }
        set
        {
            if (value == stage) return;
            stage = value;
            Debug.Log("[promo] " + value);
#if UNITY_WEBGL && !UNITY_EDITOR
            try { ScrapyardUiStage(value); } catch (System.Exception) { }
#endif
        }
    }
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    static extern void ScrapyardUiStage(string s);
#endif
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
        for (int f = 0; f < 90 * FPS; f++)
        {
            bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm != null && bm.mode == BuilderManager.Mode.Map && bm.testRobot != null) break;
            yield return null;
        }
        if (bm == null || bm.testRobot == null) { Stage = "no-map"; Done = true; yield break; }

        StartCoroutine(TendRewardBoxes());
        yield return Hold(1.0f, 0f, 0f, "settle");
        // The workshop FIRST. Assembly and the bout are the two shots the video
        // actually needs; a chest that happens to be far away must never eat
        // the budget before either of them is filmed.
        yield return Garage();
        Debug.Log("[promo] first crate at " + (bm.NearestCratePos().HasValue ? bm.NearestCratePos().Value.ToString("0") : "NONE"));
        yield return DriveTo(() => bm.NearestCratePos(), 1.2f, 12f, "to-chest");
        yield return Hold(2.5f, 0f, 0f, "chest-open");

        // ONE target, held. Asking for the nearest enemy every frame made the
        // machine swap targets mid-approach and drive past both.
        target = bm.YardParked;
        Debug.Log("[promo] target " + (target != null ? target.name : "NONE"));
        yield return DriveTo(() => target != null ? (Vector3?)target.rb.position : null, 2.6f, 22f, "to-robot");
        // Parked machines sit on raised pads and the drive cannot always climb
        // one; thirty-five seconds of nosing at a kerb is not footage, and the
        // route reached "result" without ever having a fight. Close the last
        // gap the way every bench does, then let the real encounter happen.
        if (target != null && bm.testRobot != null
            && (target.rb.position - bm.testRobot.rb.position).magnitude > 3.2f)
        {
            Stage = "close-in";
            Vector3 at = target.rb.position;
            Vector3 from = bm.testRobot.rb.position;
            Vector3 dir = from - at; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; else dir.Normalize();
            Vector3 spot = at + dir * 3.0f;
            bm.TeleportPlayer(new Vector3(spot.x, 0f, spot.z),
                              Quaternion.LookRotation(new Vector3(at.x - spot.x, 0f, at.z - spot.z)).eulerAngles.y);
            yield return WaitRealtime(1.2f);
        }
        yield return Hold(2.0f, 0f, 0f, "card");
        Stage = "challenge";
        for (int tries = 0; tries < 120 && bm.mode != BuilderManager.Mode.Fight; tries++)
        {
            bm.ChallengeParked();
            yield return null;
        }
        for (int f = 0; f < 45 * FPS && bm.mode == BuilderManager.Mode.Fight; f++)
        {
            Stage = "fight";
            // Drive AT the other machine, which is what makes a ram fight read
            // as a fight: a fixed forward lean just drove into a wall.
            var foe = Object.FindFirstObjectByType<FightManager>();
            Vector3 aim = Vector3.forward;
            if (foe != null && foe.enemy != null && foe.enemy.bot != null && bm.testRobot != null)
                aim = foe.enemy.bot.rb.position - bm.testRobot.rb.position;
            var cam = Camera.main;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.01f) aim.Normalize(); else aim = fwd;
            Phase0Input.debugThrottle = Vector3.Dot(aim, fwd);
            Phase0Input.debugSteer = Vector3.Dot(aim, right);
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
        Stage = "result";
        yield return Hold(6f, 0f, 0f, "result");
        Stage = "done"; Done = true;
    }

    CompoundRobot target;

    /// <summary>The workshop: open it, hold a part, put it on the machine, and
    /// drive back out. Placement goes through Phase0Input's debug pointer, the
    /// seam TouchSmoke uses, because a synthetic tap reaches nothing here.</summary>
    IEnumerator Garage()
    {
        Stage = "garage-open";
        var garage = Btn("GARAGE");
        Debug.Log("[promo] garage button " + (garage != null ? "found" : "MISSING"));
        if (garage == null) yield break;
        garage.onClick.Invoke();
        yield return WaitRealtime(1.6f);
        Debug.Log("[promo] after GARAGE mode=" + bm.mode);
        if (bm.mode != BuilderManager.Mode.Build) yield break;

        Stage = "garage-pick";
        var tile = Btn("Wedge") ?? Btn("Plate") ?? Btn("Beam");
        if (tile != null) { tile.onClick.Invoke(); yield return WaitRealtime(1.2f); }

        Stage = "garage-place";
        Vector3 core = CoreOnScreen();
        if (core != Vector3.zero)
        {
            Phase0Input.debugPointer = true;
            Phase0Input.debugMousePos = core + new Vector3(0f, 46f, 0f);
            yield return null; yield return null;
            yield return WaitRealtime(1.0f);         // the ghost sits there to be seen
            Phase0Input.DebugClick(0);
            yield return null; yield return null;
            Phase0Input.debugPointer = false;
        }
        yield return WaitRealtime(1.6f);
        var done = Btn("DONE"); if (done != null) done.onClick.Invoke();
        yield return WaitRealtime(1.2f);
        Stage = "garage-out";
        var out_ = Btn("DRIVE OUT") ?? Btn("DRIVE");
        if (out_ != null) out_.onClick.Invoke();
        yield return WaitRealtime(2.0f);
    }

    static Button Btn(string prefix)
    {
        foreach (var b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null && t.text != null && t.text.StartsWith(prefix)) return b;
        }
        return null;
    }

    static Vector3 CoreOnScreen()
    {
        foreach (var col in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            if (col.gameObject.name.StartsWith("core") && Camera.main != null)
                return Camera.main.WorldToScreenPoint(col.bounds.center);
        return Vector3.zero;
    }

    /// <summary>FRAMES, not seconds. The capture rig steps the page frame by
    /// frame, and under stepping the engine's clocks do not advance the way a
    /// wait expects - every time-based wait blocked forever and a whole take
    /// sat still on the spot where it was installed. Frame counts cannot lie.</summary>
    const int FPS = 30;
    static IEnumerator WaitRealtime(float seconds)
    {
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * FPS));
        for (int i = 0; i < n; i++) yield return null;
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
                yield return WaitRealtime(1.1f);   // frames: an open box sets timeScale 0 AND the rig freezes the clocks
                box.TestTapBody();
                yield return WaitRealtime(2.3f);   // the reveal
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
        int budget = Mathf.RoundToInt(seconds * FPS);
        var cam = Camera.main;
        for (int f = 0; f < budget; f++)
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
            // Wedged against a monolith, the machine would push into it until
            // the budget ran out and the route gave up short of its own fight.
            if ((me - lastAt).sqrMagnitude < 0.0004f) stuck++; else stuck = 0;
            lastAt = me;
            if (stuck > 20)
            {
                for (int k = 0; k < 24; k++)
                { Phase0Input.debugThrottle = -0.9f; Phase0Input.debugSteer = 0.8f; yield return null; }
                stuck = 0;
            }
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
    }
    Vector3 lastAt; int stuck;

    IEnumerator Hold(float seconds, float thr, float steer, string stage)
    {
        Stage = stage;
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * FPS));
        for (int i = 0; i < n; i++)
        {
            Phase0Input.debugThrottle = thr; Phase0Input.debugSteer = steer;
            yield return null;
        }
        Phase0Input.debugThrottle = 0f; Phase0Input.debugSteer = 0f;
    }
}
}
#endif
