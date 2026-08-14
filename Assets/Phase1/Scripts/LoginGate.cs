using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using System.Collections;

namespace RobotBrawl.Phase0
{
// ============================================================================
// LOGIN GATE — client B of docs/Server_Economy_Design_2026-08-13.md.
//
// Owen, same day: "make the user login/signup page the gate of the game."
// No account, no game. The gate lives ON THE ModeSelect GAMEOBJECT, which is
// the load-bearing placement decision: every bench in the suite self-boots by
// destroying "ModeSelect", so the entire suite bypasses this gate with ZERO
// bench edits. The session persists (LadderClient.SaveSession — called HERE,
// never inside Auth, see the note there), so after the first sign-in the
// career plays offline and the gate never shows again until sign-out.
//
// uGUI, not IMGUI, and that is not a style choice: the Device Simulator
// disables the mouse, and IMGUI screens render but cannot be clicked there —
// the exact trap that killed the old chooser (see ModeSelect's own comments).
// Inputs use the measured ARENA border idiom (2px #6B7080): a fill dark
// enough for white text can never reach 3:1 against a dark panel, so the
// BOUNDARY carries the contrast — 3.89:1 measured. Do not "simplify" it back
// into a fill (docs/, ARENA contrast note).
//
// The buttons' onClick and the Test* seams call the SAME public methods —
// the WATCH-button lesson says behaviour must not live only in onClick, and
// the EnlistUiBench lesson says a UI path only a human can drive is a UI
// path nothing checks.
// ============================================================================
public class LoginGate : MonoBehaviour
{
    public static LoginGate inst;

    InputField email, password, displayName;
    Text status, title;
    GameObject canvasGo;
    bool busy;

    /// <summary>Bench-readable outcome line (mirrors the on-screen status).</summary>
    public string Status { get { return status != null ? status.text : ""; } }
    public bool Busy { get { return busy; } }

    void Start()
    {
        inst = this;
        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }
        Build();
    }

    void OnDestroy() { if (inst == this) inst = null; if (canvasGo != null) Destroy(canvasGo); }

    // ---------------------------------------------------------------- verbs
    public void OnLogin()
    {
        if (busy) return;
        if (!FieldsOk(false)) return;
        busy = true; Say("signing in…");
        StartCoroutine(LadderClient.Login(email.text.Trim(), password.text, Done));
    }

    public void OnCreate()
    {
        if (busy) return;
        if (!FieldsOk(true)) return;
        busy = true; Say("creating your account…");
        StartCoroutine(LadderClient.Register(email.text.Trim(), password.text,
                                             displayName.text.Trim(), Done));
    }

    /// <summary>Editor-only dev door — the sandbox stays one click away, the
    /// same promise the old chooser made. Never built in a player.</summary>
    public void OnDevSkip()
    {
        if (!Application.isEditor) return;
        Proceed();
    }

    // Test seams (bridge refuses reflection; these are the same methods the
    // buttons call, so the seam cannot measure a path the button doesn't take).
    public void TestSetFields(string mail, string pw, string name)
    { email.text = mail; password.text = pw; displayName.text = name; }
    public void TestLogin() { OnLogin(); }
    public void TestCreate() { OnCreate(); }

    bool FieldsOk(bool creating)
    {
        if (string.IsNullOrWhiteSpace(email.text) || !email.text.Contains("@"))
        { Say("a real email is required"); return false; }
        if ((password.text ?? "").Length < 10)
        { Say("password must be at least 10 characters"); return false; }
        if (creating && displayName.text.Trim().Length < 2)
        { Say("pick a display name (2–24 characters)"); return false; }
        return true;
    }

    void Done(string name, string err)
    {
        busy = false;
        if (err != null) { Say(err); return; }
        // The ONE place a session is ever persisted — see LadderClient's
        // session-persistence note for why Auth itself must not.
        LadderClient.SaveSession(name);
        Proceed();
    }

    void Say(string s) { if (status != null) status.text = s; }

    void Proceed()
    {
        var ms = GetComponent<ModeSelect>();
        if (canvasGo != null) { Destroy(canvasGo); canvasGo = null; }
        if (ms != null) ms.GateDone();
    }

    // ------------------------------------------------------------------- UI
    static readonly Color PANEL  = new Color(0.086f, 0.094f, 0.117f, 1f);  // dock family
    static readonly Color FILL   = new Color(0.129f, 0.141f, 0.180f, 1f);  // #21242E
    static readonly Color BORDER = new Color(0.420f, 0.439f, 0.502f, 1f);  // #6B7080 — measured 3.89:1
    static readonly Color INK    = new Color(0.92f, 0.93f, 0.95f, 1f);

    void Build()
    {
        canvasGo = new GameObject("LoginGateCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var cv = canvasGo.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 500;   // above anything that might exist at boot
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1170f, 540f);
        sc.matchWidthOrHeight = 0.5f;

        var back = Panel("back", canvasGo.transform, PANEL);
        Stretch(back, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var card = Panel("card", back.transform, new Color(0.055f, 0.06f, 0.078f, 1f));
        var crt = card.GetComponent<RectTransform>();
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.sizeDelta = new Vector2(460f, 420f);
        var col = card.AddComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(24, 24, 20, 20);
        col.spacing = 10f;
        col.childForceExpandHeight = false; col.childForceExpandWidth = true;

        title = Label("title", card.transform, "ROBOT BRAWL", 26, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        // FOUND ON THE FIRST DEVICE BUILD (2026-08-13, iPhone 17 Pro AND iPad
        // Pro 11 sims): this sentence wraps at both widths and the one-line
        // label CLIPPED its last word — players read "your scrap and parts
        // live on your". Two lines of height, measured against the shots in
        // docs/shots/uxval/.
        var sub = Label("sub", card.transform, "Sign in to build, fight and earn — your scrap and parts live on your account.", 13, TextAnchor.MiddleCenter);
        sub.gameObject.GetComponent<LayoutElement>().minHeight = 44f;

        email       = Input("email", card.transform, "email", InputField.ContentType.EmailAddress);
        password    = Input("password", card.transform, "password (10+ characters)", InputField.ContentType.Password);
        displayName = Input("displayname", card.transform, "display name (for a new account)", InputField.ContentType.Standard);

        Btn("login", card.transform, "LOG IN", OnLogin, new Color(0.20f, 0.45f, 0.65f, 1f));
        Btn("create", card.transform, "CREATE ACCOUNT", OnCreate, new Color(0.24f, 0.52f, 0.32f, 1f));
        if (Application.isEditor)
            Btn("devskip", card.transform, "DEV: SKIP SIGN-IN (sandbox)", OnDevSkip, new Color(0.35f, 0.33f, 0.20f, 1f));

        status = Label("status", card.transform, LadderClient.IsProduction ? "" : "[dev] " + LadderClient.BaseUrl, 12, TextAnchor.MiddleCenter);
        status.color = new Color(0.75f, 0.77f, 0.82f, 1f);

        // The soft-brick escape hatch: with the gate in front of the game, a
        // forgotten password locks a player out of EVERYTHING, and there is
        // no self-serve email reset yet (no mail infrastructure on this
        // budget). Support resets it operator-side (the admin endpoint) —
        // this line is how the player learns that path exists.
        var help = Label("resethelp", card.transform,
            "Forgot your password? Email support and we'll reset it.", 11, TextAnchor.MiddleCenter);
        help.color = new Color(0.55f, 0.57f, 0.62f, 1f);
    }

    static GameObject Panel(string n, Transform parent, Color c)
    {
        var go = new GameObject(n, typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = c;
        return go;
    }

    static void Stretch(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
    }

    static Text Label(string n, Transform parent, string s, int size, TextAnchor anchor)
    {
        var go = new GameObject(n, typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = s; t.fontSize = size; t.alignment = anchor; t.color = INK;
        go.AddComponent<LayoutElement>().minHeight = size + 12f;
        return t;
    }

    static InputField Input(string n, Transform parent, string placeholder, InputField.ContentType type)
    {
        // The ARENA border idiom: the 2px border is the contrast boundary.
        var frame = Panel(n + "_border", parent, BORDER);
        frame.AddComponent<LayoutElement>().minHeight = 48f;
        var fill = Panel(n, frame.transform, FILL);
        Stretch(fill, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));

        var f = fill.AddComponent<InputField>();
        var ph = Label("ph", fill.transform, placeholder, 15, TextAnchor.MiddleLeft);
        ph.color = new Color(0.55f, 0.57f, 0.62f, 1f);
        Stretch(ph.gameObject, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-12f, -2f));
        var tx = Label("text", fill.transform, "", 15, TextAnchor.MiddleLeft);
        Stretch(tx.gameObject, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-12f, -2f));
        tx.supportRichText = false;
        f.textComponent = tx; f.placeholder = ph; f.contentType = type;
        f.targetGraphic = fill.GetComponent<Image>();
        return f;
    }

    static Button Btn(string n, Transform parent, string label, UnityEngine.Events.UnityAction fn, Color c)
    {
        var go = Panel(n, parent, c);
        go.AddComponent<LayoutElement>().minHeight = 48f;   // the 44pt floor with margin
        var b = go.AddComponent<Button>();
        b.onClick.AddListener(fn);
        var t = Label("label", go.transform, label, 16, TextAnchor.MiddleCenter);
        Stretch(t.gameObject, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return b;
    }
}
}
