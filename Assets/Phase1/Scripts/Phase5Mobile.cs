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
    /// <summary>False when the player's machine has no actuated weapon - the
    /// FIRE pad would be a dead button (critic round 1, mobile).</summary>
    public static bool hasFire = true;
    FightManager fm;   // results-card detection: pads hide while it is up
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
        if (fm == null) fm = Object.FindFirstObjectByType<FightManager>();
        if (fm != null && fm.state == FightManager.State.Ended) { Release(); return; }
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
            else if (hasFire && p.x >= halfW && p.y < Screen.height * 0.7f)
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

    static Texture2D ringTex, discTex;
    static GUIStyle padLbl;

    /// <summary>Anti-aliased white circle; innerFrac > 0 hollows it into a
    /// ring. Tinted via GUI.color at draw time, so two textures cover every
    /// pad element (critic round 1: the pads read as debug boxes).</summary>
    static Texture2D MakeCircle(int size, float innerFrac)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f - 1.5f, c = size * 0.5f - 0.5f;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float alpha = Mathf.Clamp01(r - d);
                if (innerFrac > 0f) alpha *= Mathf.Clamp01(d - r * innerFrac);
                px[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        t.SetPixels(px); t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    void OnGUI()
    {
        if (!fightActive || (!HasTouch && !mouseTest)) return;
        if (fm != null && fm.state == FightManager.State.Ended) return;   // results card owns the screen
        if (ringTex == null) ringTex = MakeCircle(128, 0.86f);
        if (discTex == null) discTex = MakeCircle(128, 0f);
        if (padLbl == null)
        {
            padLbl = new GUIStyle(GUI.skin.label);
            padLbl.alignment = TextAnchor.MiddleCenter;
            padLbl.fontStyle = FontStyle.Bold;
            padLbl.fontSize = 17;
            padLbl.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
        }
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = Screen.height / s;
        Color saved = GUI.color;

        // Drive stick: ring base + faint fill, knob follows the finger.
        Vector2 a = stickHeld ? new Vector2(anchor.x / s, vh - anchor.y / s)
                              : new Vector2(140f, vh - 140f);
        Rect baseR = new Rect(a.x - 74f, a.y - 74f, 148f, 148f);
        GUI.color = new Color(1f, 1f, 1f, stickHeld ? 0.10f : 0.06f);
        GUI.DrawTexture(baseR, discTex);
        GUI.color = new Color(1f, 1f, 1f, stickHeld ? 0.75f : 0.40f);
        GUI.DrawTexture(baseR, ringTex);
        Vector2 k = a;
        if (stickHeld)
        {
            k = new Vector2(stickPos.x / s, vh - stickPos.y / s);
            k = a + Vector2.ClampMagnitude(k - a, 60f);
        }
        GUI.color = stickHeld ? new Color(0.55f, 0.75f, 1f, 0.90f) : new Color(1f, 1f, 1f, 0.30f);
        GUI.DrawTexture(new Rect(k.x - 27f, k.y - 27f, 54f, 54f), discTex);
        GUI.color = saved;
        if (!stickHeld) GUI.Label(baseR, "DRIVE", padLbl);

        // FIRE: red disc, flashes brighter while held. Hidden for weaponless builds.
        if (hasFire)
        {
            Rect fr = new Rect(vw - 205f, vh - 195f, 140f, 140f);
            GUI.color = fireHeld ? new Color(1f, 0.42f, 0.25f, 0.95f) : new Color(0.85f, 0.24f, 0.20f, 0.70f);
            GUI.DrawTexture(fr, discTex);
            GUI.color = saved;
            GUI.Label(fr, "FIRE", padLbl);
        }
        GUI.matrix = Matrix4x4.identity;
    }
}
}
