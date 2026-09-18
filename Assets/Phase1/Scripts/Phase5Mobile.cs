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
    // owen's phone, 2026-09-14: "the driver joy stick and font size looks too
    // big on my phone", with a screenshot in which the ring covers roughly 60%
    // of the canvas height.
    //
    // ⚠ THESE WERE LITERALS, AND THE UNIT UNDER THEM CHANGED. They were authored
    // when GuiScale made one unit one FRAMEBUFFER pixel, so on a dpr-2 phone a
    // 280-unit ring drew at 140 CSS px - about a third of a 430 px screen, which
    // is right. Fixing GuiScale (one unit is now one CSS pixel, so the fight
    // stopped rendering at half size) doubled every one of them: the same ring
    // became 280 CSS px, 65% of the screen, sitting on top of the thing it
    // steers. The constants were never wrong; the ground moved under them.
    //
    // So the stick is a FRACTION OF THE SHORT EDGE now, clamped, and every other
    // number is a ratio of the ring. A future change to the scale cannot
    // re-break it, because there is no longer a length in here that assumes one.
    // The ratios are the ones that were measured by hand on the live page on
    // 2026-09-10 and are deliberately unchanged: knob 0.27 of the ring, rest
    // 0.62, and TRAVEL IS THE RING'S RADIUS so a push to the drawn edge is full
    // lock (it used to be 0.62 of it, which read as a stick that would not
    // commit - a push to the edge gave 60% throttle and 16 m in ten seconds).
    public const float RING_OF_SHORT_EDGE = 0.33f, RING_MIN = 120f, RING_MAX = 200f;
    public const float KNOB_OF_RING = 0.27f, REST_OF_RING = 0.62f;

    /// <summary>The ring's diameter, in the units this class draws in — which
    /// are CSS pixels on the web and points elsewhere, because that is what
    /// GuiScale now means.</summary>
    public static float RING
    {
        get
        {
            float s = Mathf.Max(0.01f, BuilderManager.GuiScale);
            float shortEdge = Mathf.Min(Screen.width, Screen.height) / s;
            if (shortEdge < 1f) return RING_MIN;
            return Mathf.Clamp(shortEdge * RING_OF_SHORT_EDGE, RING_MIN, RING_MAX);
        }
    }
    public static float RANGE { get { return RING * 0.5f; } }         // full deflection = the ring's radius
    public static float KNOB { get { return RING * KNOB_OF_RING; } }
    public static float KNOB_TRAVEL { get { return RANGE; } }         // the knob reaches the drawn edge
    public static float REST { get { return RING * REST_OF_RING; } }  // resting centre from the bottom-left
    public static bool MouseAccepted = true;
    /// <summary>Has this player driven, by any means, since the page loaded?
    /// Set at the one site that already asks the question - PumpRoam's
    /// `StickNow() != Vector2.zero`, which is also what raises the `moved`
    /// beacon - so the hint and the telemetry can never disagree about what
    /// counts as driving. Static on purpose: once you have driven you know how,
    /// and coming out of a fight must not re-teach you.</summary>
    public static bool everDriven;
    public static void TestResetDriven() { everDriven = false; }
    /// <summary>Should the WASD cluster be on screen? Named rather than left
    /// inline in OnGUI so a bench measures THE EXPRESSION THE DRAW ACTUALLY
    /// USES - the WATCH-button lesson (CLAUDE.md): a seam that restates a
    /// predicate proves only that the restatement is correct.
    /// `stickHeld` is deliberately NOT in here: that is a frame-by-frame draw
    /// suppression while a mouse drags the ring, not a statement about whether
    /// this player needs the hint.</summary>
    public static bool KeyHintWanted
    {
        get { return !everDriven && !MobileBuilderUI.HasCoarsePointer; }
    }
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

    static GUIStyle keyLbl, keyCap;

    /// <summary>A WASD cluster drawn where the stick rests, captioned. Sized off
    /// RING so it tracks the stick at every screen size instead of carrying its
    /// own literals - the 640x480 label sweep exists because of literals.</summary>
    void DrawKeyHint(Vector2 a)
    {
        if (keyLbl == null)
        {
            keyLbl = new GUIStyle(GUI.skin.label);
            keyLbl.alignment = TextAnchor.MiddleCenter;
            keyLbl.fontStyle = FontStyle.Bold;
        }
        if (keyCap == null)
        {
            keyCap = new GUIStyle(GUI.skin.label);
            keyCap.alignment = TextAnchor.MiddleCenter;
            keyCap.fontStyle = FontStyle.Bold;
        }
        float K = Mathf.Max(26f, RING * 0.30f);          // one key cap
        float G = K * 0.13f;                              // the gap between caps
        keyLbl.fontSize = Mathf.RoundToInt(K * 0.46f);
        keyCap.fontSize = Mathf.RoundToInt(K * 0.38f);
        float cy = a.y - (K + G) * 0.5f;                  // two rows, centred on the ring
        Color saved = GUI.color;
        float pulse = 0.72f + 0.20f * Mathf.Sin(Time.unscaledTime * 2.6f);
        Cap(new Rect(a.x - K * 0.5f,     cy - K - G, K, K), "W", pulse);
        Cap(new Rect(a.x - K * 1.5f - G, cy,         K, K), "A", pulse);
        Cap(new Rect(a.x - K * 0.5f,     cy,         K, K), "S", pulse);
        Cap(new Rect(a.x + K * 0.5f + G, cy,         K, K), "D", pulse);
        keyCap.normal.textColor = new Color(1f, 1f, 1f, 0.78f);
        GUI.Label(new Rect(a.x - RING, cy + K + G * 2f, RING * 2f, K), "or ARROWS to drive", keyCap);
        GUI.color = saved;
    }

    void Cap(Rect r, string letter, float pulse)
    {
        GUI.color = new Color(0.86f, 0.92f, 1f, pulse);            // the cap edge
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        float b = Mathf.Max(2f, r.width * 0.075f);
        GUI.color = new Color(0.05f, 0.07f, 0.11f, 0.90f);         // the well
        GUI.DrawTexture(new Rect(r.x + b, r.y + b, r.width - b * 2f, r.height - b * 2f),
                        Texture2D.whiteTexture);
        GUI.color = Color.white;
        keyLbl.normal.textColor = new Color(0.94f, 0.97f, 1f, Mathf.Min(1f, pulse + 0.18f));
        GUI.Label(r, letter, keyLbl);
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

        // A MOUSE IS NOT A FINGER. On a coarse pointer the ring above is the
        // whole story; on a fine one it is a phone affordance the player has no
        // reason to grab, and WASD - which has always worked - was advertised
        // nowhere at all. Show the keys until the player drives once.
        if (KeyHintWanted && !stickHeld) DrawKeyHint(a);

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
