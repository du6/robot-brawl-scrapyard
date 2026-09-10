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
        float s = BuilderManager.GuiScale;   // R4 finding 3: one rule, one place
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
    // SCRAPYARD (owen, 2026-09-10: "it still feels a bit hard to drive. can you
    // try to increase the size of the joystick"). The stick's TRAVEL is what
    // sets its sensitivity: full deflection used to be 120 units, so a thumb
    // that moved 40 units was already at a third of lock. Travel is now 175
    // and the drawn ring, knob and resting spot grow with it. And the stick
    // takes a MOUSE on every platform, not only under a bench flag: a
    // desktop web player had no stick at all (keys only), and a pointer drag
    // is how a person would try it.
    // MEASURED with a pointer drive on the live page (2026-09-10): a push to
    // the ring's edge gave ~60% throttle and 16 m in ten seconds, because the
    // ring's radius (110) was not the travel (175) - the old stick had the
    // same 0.62 ratio. THE RING'S EDGE IS FULL LOCK NOW: travel = ring radius,
    // and the knob is drawn out to it.
    public static float RANGE = 140f;        // units of GuiScale to full deflection = the ring's radius
    public static float RING = 280f;         // drawn ring, units (diameter)
    public static float KNOB = 76f;          // drawn knob, units
    public static float KNOB_TRAVEL = 140f;  // the knob reaches the ring's edge at full lock
    public static float REST = 175f;         // resting spot from the bottom-left corner
    public static bool MouseAccepted = true;
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
    // THE ANCHOR IS WHERE THE PRESS BEGAN, NOT WHERE THE POINTER IS ON THE
    // FIRST FRAME WE POLL IT (2026-09-10, measured with a pointer drive on the
    // live page: a press-and-flick that moved before the first poll anchored
    // at the END of the flick, so the throttle read zero and only the steer
    // was left - the machine turned on the spot). A thumb does the same thing
    // in miniature on a fast flick. For a mouse the pointer hovers before it
    // presses, so the last unpressed position IS the press point; a touch
    // cannot move before contact, so its first sample is right already.
    Vector2 lastFree; bool lastFreeValid; bool mouseSample;

    // R4 finding 3: was a seventh copy of the dpi rule; now one rule, one place.
    static float S { get { return BuilderManager.GuiScale; } }

    /// <summary>OWEN 2026-08-03, found while wiring the device auto-boot: this
    /// was a THIRD copy of "is this a touch device", and after the handheld
    /// rule it would have disagreed with the other two. A Surface would have
    /// got the desktop builder AND a floating thumbstick in every fight.
    ///
    /// The on-screen stick is a UI affordance, not an input capability, so the
    /// question it should ask is "is the touch UI the UI here" - which is
    /// exactly ShouldActivate. That also means forcing the touch UI in the
    /// editor now brings its controls with it, instead of needing mouseTest.</summary>
    bool HasTouch { get { return MobileBuilderUI.ShouldActivate(); } }

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
        mouseSample = false;
        if (c == 0 && (mouseTest || MouseAccepted) && Mouse.current != null)
        {
            if (Mouse.current.leftButton.isPressed) { buf[c++] = Mouse.current.position.ReadValue(); mouseSample = true; }
            else { lastFree = Mouse.current.position.ReadValue(); lastFreeValid = true; }
        }
#else
        for (int i = 0; i < Input.touchCount && c < buf.Length; i++) buf[c++] = Input.GetTouch(i).position;
        if (c == 0 && (mouseTest || MouseAccepted))
        {
            if (Input.GetMouseButton(0)) { buf[c++] = Input.mousePosition; mouseSample = true; }
            else { lastFree = Input.mousePosition; lastFreeValid = true; }
        }
#endif
        return c;
    }

    readonly Vector2[] pts = new Vector2[8];

    void Update()
    {
        if (!fightActive || (!HasTouch && !mouseTest && !MouseAccepted)) { Release(); return; }
        if (fm == null) fm = Object.FindFirstObjectByType<FightManager>();
        if (fm != null && fm.state == FightManager.State.Ended) { Release(); return; }
        // AUTONOMY FIGHT (owen, 2026-09-05): the program drives, so the pads
        // are not just useless, they are a lie under the "autopilot drives
        // this fight" banner - a rookie will try the stick. Draw nothing and
        // feed nothing while the player's side is program-controlled.
        if (fm != null && fm.playerSource == ControlSource.Program) { Release(); return; }
        int c = Points(pts);
        bool sHeld = false, fHeld = false;
        float thr = 0f, str = 0f;
        float halfW = Screen.width * 0.5f;
        float range = RANGE * S;
        for (int i = 0; i < c; i++)
        {
            Vector2 p = pts[i];
            if (p.x < halfW && p.y < Screen.height * 0.7f)
            {
                if (!stickHeld)
                {
                    // floating stick: anchors where the press BEGAN (see lastFree)
                    anchor = p;
                    if (mouseSample && lastFreeValid && (lastFree - p).magnitude < range * 1.5f
                        && lastFree.x < halfW && lastFree.y < Screen.height * 0.7f)
                        anchor = lastFree;
                }
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
        if (!fightActive || (!HasTouch && !mouseTest && !MouseAccepted)) return;
        if (fm != null && fm.state == FightManager.State.Ended) return;   // results card owns the screen
        if (fm != null && fm.playerSource == ControlSource.Program) return; // autonomy: no pads (see Update)
        if (ringTex == null) ringTex = MakeCircle(128, 0.86f);
        if (discTex == null) discTex = MakeCircle(128, 0f);
        if (padLbl == null)
        {
            padLbl = new GUIStyle(GUI.skin.label);
            padLbl.alignment = TextAnchor.MiddleCenter;
            padLbl.fontStyle = FontStyle.Bold;
            padLbl.fontSize = 20;
            padLbl.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
        }
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = Screen.height / s;
        Color saved = GUI.color;

        // Drive stick: ring base + faint fill, knob follows the finger.
        Vector2 a = stickHeld ? new Vector2(anchor.x / s, vh - anchor.y / s)
                              : new Vector2(REST, vh - REST);
        Rect baseR = new Rect(a.x - RING * 0.5f, a.y - RING * 0.5f, RING, RING);
        GUI.color = new Color(1f, 1f, 1f, stickHeld ? 0.10f : 0.06f);
        GUI.DrawTexture(baseR, discTex);
        GUI.color = new Color(1f, 1f, 1f, stickHeld ? 0.75f : 0.40f);
        GUI.DrawTexture(baseR, ringTex);
        Vector2 k = a;
        if (stickHeld)
        {
            k = new Vector2(stickPos.x / s, vh - stickPos.y / s);
            k = a + Vector2.ClampMagnitude(k - a, KNOB_TRAVEL);
        }
        GUI.color = stickHeld ? new Color(0.55f, 0.75f, 1f, 0.90f) : new Color(1f, 1f, 1f, 0.30f);
        GUI.DrawTexture(new Rect(k.x - KNOB * 0.5f, k.y - KNOB * 0.5f, KNOB, KNOB), discTex);
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
