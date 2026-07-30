using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RobotBrawl.Phase0
{

/// <summary>Phase 5 (§12 \"polish & ship\") — the mobile kit, per the
/// Mobile_Deploy_Feasibility audit: (1) PerfMode + PerfHUD answer the never-
/// measured \"60 fps on a phone\" question with zero touch controls needed;
/// (2) TouchControls is the fight-mode virtual stick + FIRE button, driven
/// entirely through the Phase0Input.debug* seam (which OVERRIDES keyboard
/// when non-zero and passes through when zeroed). The builder panel remains
/// desktop-only for now — that is the acknowledged Phase-5 rewrite.</summary>
public class PerfHUD : MonoBehaviour
{
    float acc; int n; float worst; float windowEnd;
    float fpsShown; float worstShown; int rbShown;
    GUIStyle style;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        acc += dt; n++;
        if (dt > worst) worst = dt;
        if (Time.unscaledTime >= windowEnd)
        {
            fpsShown = n / Mathf.Max(acc, 1e-4f);
            worstShown = worst * 1000f;
            rbShown = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            acc = 0f; n = 0; worst = 0f;
            windowEnd = Time.unscaledTime + 1f;
        }
    }

    void OnGUI()
    {
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        float s = Screen.dpi > 250f ? Mathf.Min(2.5f, Screen.dpi / 160f) : 1f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float w = 430f;
        GUI.Box(new Rect(Screen.width / s - w - 8f, 8f, w, 30f), "");
        GUI.Label(new Rect(Screen.width / s - w, 12f, w, 26f), string.Format(
            "PERF  {0:F0} fps · worst {1:F0} ms · {2} rigidbodies · 50 Hz physics",
            fpsShown, worstShown, rbShown), style);
        GUI.matrix = Matrix4x4.identity;
    }
}

/// <summary>Boots straight into an AI-vs-player-build fight with the fps HUD.
/// The build is EMBEDDED (player builds ship no Assets/*.txt); the opponent is
/// the WIDOWMAKER Champion — tungsten disc, debris, worst-case physics.</summary>
public class PerfMode : MonoBehaviour
{
    const string BUILTIN = "core|0.000,0.700,0.000|0|0.00,0.00,0.00|Steel\nchassis|0.000,0.700,0.400|0|0.00,0.00,0.00|Steel\nchassis|0.000,0.700,-0.400|0|0.00,0.00,0.00|Steel\nwheel|0.270,0.700,0.400|0|1.00,0.00,0.00|Rubber\nwheel|-0.270,0.700,0.400|0|-1.00,0.00,0.00|Rubber\nwheel|0.270,0.700,-0.400|0|1.00,0.00,0.00|Rubber\nwheel|-0.270,0.700,-0.400|0|-1.00,0.00,0.00|Rubber\nbattery|0.000,0.975,0.000|0|0.00,0.00,0.00|Aluminum\nengine|0.000,0.975,0.400|0|0.00,0.00,0.00|Steel\npivot|0.000,1.000,-0.400|0|0.00,1.00,0.00|Aluminum\nbeam|0.450,1.000,-0.400|90|0.00,0.00,0.00|Aluminum\nblade|1.000,1.000,-0.400|0|1.00,0.00,0.00|Steel\n";
    int frames; BuilderManager bm; bool started;

    void Update()
    {
        if (started) return;
        if (bm == null)
        {
            bm = Object.FindFirstObjectByType<BuilderManager>();
            if (bm == null) new GameObject("BuilderManager").AddComponent<BuilderManager>();
            return;
        }
        frames++;
        if (frames < 3) return;   // builder defers its own init one frame
        int n = bm.LoadSnapshot(BUILTIN);
        if (n <= 0) return;       // still starting up; retry next frame
        bm.opponentId = "widowmaker";
        bm.opponentTier = AiTier.Champion;
        Progression.activeRungIndex = -1;
        Progression.activeChallengeIdx = -1;
        bm.StartFight();
        gameObject.AddComponent<PerfHUD>();
        started = true;
    }
}

/// <summary>Fight/test-mode touch layer: floating virtual stick (left half)
/// + FIRE button (right). Writes Phase0Input.debugThrottle/debugSteer/
/// debugFire — which override keyboard while held and pass through when
/// zeroed — so nothing downstream changes. Inert without a touchscreen
/// (set `mouseTest` for editor verification with the mouse).</summary>
public class TouchControls : MonoBehaviour
{
    public static bool fightActive;
    public static bool mouseTest;
    static TouchControls inst;

    public static void Ensure()
    {
        if (inst == null) inst = new GameObject("touch_controls").AddComponent<TouchControls>();
    }

    bool driving;          // we currently own the debug inputs
    bool stickHeld, fireHeld;
    Vector2 anchor;        // floating stick anchor, input space (y up)
    Vector2 stickPos;

    static float S { get { float d = Screen.dpi; return d > 250f ? Mathf.Min(2.5f, d / 160f) : 1f; } }

    bool HasTouch
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Touchscreen.current != null;
#else
            return Input.touchSupported;
#endif
        }
    }

    int Points(Vector2[] buf)
    {
        int c = 0;
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts != null)
            foreach (var t in ts.touches)
            {
                if (c >= buf.Length) break;
                if (t.press.isPressed) buf[c++] = t.position.ReadValue();
            }
        if (c == 0 && mouseTest && Mouse.current != null && Mouse.current.leftButton.isPressed)
            buf[c++] = Mouse.current.position.ReadValue();
#else
        for (int i = 0; i < Input.touchCount && c < buf.Length; i++) buf[c++] = Input.GetTouch(i).position;
        if (c == 0 && mouseTest && Input.GetMouseButton(0)) buf[c++] = Input.mousePosition;
#endif
        return c;
    }

    readonly Vector2[] pts = new Vector2[8];

    void Update()
    {
        if (!fightActive || (!HasTouch && !mouseTest)) { Release(); return; }
        int c = Points(pts);
        bool sHeld = false, fHeld = false;
        float thr = 0f, str = 0f;
        float halfW = Screen.width * 0.5f;
        float range = 120f * S;
        for (int i = 0; i < c; i++)
        {
            Vector2 p = pts[i];
            if (p.x < halfW && p.y < Screen.height * 0.7f)
            {
                if (!stickHeld) anchor = p;   // floating stick: anchors where you touch
                stickPos = p;
                thr = Mathf.Clamp((p.y - anchor.y) / range, -1f, 1f);
                str = Mathf.Clamp((p.x - anchor.x) / range, -1f, 1f);
                sHeld = true;
            }
            else if (p.x >= halfW && p.y < Screen.height * 0.7f)
                fHeld = true;
        }
        stickHeld = sHeld;
        fireHeld = fHeld;
        if (sHeld || fHeld)
        {
            Phase0Input.debugThrottle = sHeld ? (Mathf.Abs(thr) < 0.08f ? 0.001f : thr) : 0.001f;
            Phase0Input.debugSteer = sHeld ? str : 0f;
            Phase0Input.debugFire = fHeld;
            driving = true;
        }
        else Release();
    }

    void Release()
    {
        if (!driving) return;
        Phase0Input.debugThrottle = 0f;
        Phase0Input.debugSteer = 0f;
        Phase0Input.debugFire = false;
        driving = false; stickHeld = false; fireHeld = false;
    }

    void OnGUI()
    {
        if (!fightActive || (!HasTouch && !mouseTest)) return;
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = Screen.height / s;
        // stick: anchored where held, parked bottom-left otherwise
        Vector2 a = stickHeld ? new Vector2(anchor.x / s, vh - anchor.y / s)
                              : new Vector2(140f, vh - 140f);
        GUI.Box(new Rect(a.x - 70f, a.y - 70f, 140f, 140f), "DRIVE");
        if (stickHeld)
        {
            Vector2 k = new Vector2(stickPos.x / s, vh - stickPos.y / s);
            GUI.Box(new Rect(k.x - 24f, k.y - 24f, 48f, 48f), "");
        }
        var old = GUI.backgroundColor;
        GUI.backgroundColor = fireHeld ? new Color(1f, 0.45f, 0.2f) : old;
        GUI.Box(new Rect(vw - 200f, vh - 190f, 130f, 130f), "FIRE");
        GUI.backgroundColor = old;
        GUI.matrix = Matrix4x4.identity;
    }
}
}
