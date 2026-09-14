// ===========================================================================
// MapHudUI.cs — THE MAP'S HUD, IN THE DOCK'S STYLE (Robot Brawl: Scrapyard).
// 2026-09-10, after the CrazyGames rejection ("overall quality"): the IMGUI
// compass strip, GARAGE button, toast and encounter card read as debug text.
// This is the same information on a UGUI canvas built like the dock
// (MobileBuilderUI): 1280x720 reference, LegacyRuntime, the dock's panel
// colour, 44 pt touch rows, safe-area insets. Compass chips carry a NEEDLE
// that rotates to the bearing, not an ASCII arrow. The model comes from
// BuilderManager.HudModel(); the buttons' behaviour lives in their onClick
// (CLAUDE.md: a bench fires the onClick, never the seam).
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
public class MapHudUI : MonoBehaviour
{
    public static MapHudUI inst;
    public static readonly Color PANEL = new Color(0.16f, 0.18f, 0.22f, 0.96f);
    public static readonly Color AMBER = new Color(1f, 0.62f, 0.24f);
    public static readonly Color TOAST = new Color(1f, 0.87f, 0.46f);
    public static readonly Color BANNER = new Color(0.62f, 0.90f, 1f);
    /// <summary>The row at REFERENCE scale (1280x720), and nothing else. The
    /// live row comes from Row(); see ApplyMetrics.</summary>
    // owen's phone, 2026-09-14: "the driver joy stick and font size looks too
    // big on my phone". Fixing the map HUD's type (it had been rendering at
    // 8.6-12.5 CSS px, below Apple's 11 pt floor) overshot: at the old nominal
    // sizes the toast landed at 24.4 CSS px and the objective at 21.1 on a
    // 430 px-tall screen, where mobile HUD chrome usually sits at 12-16.
    // Every nominal size below came down one notch. The hierarchy is kept and
    // the numbers are stated because they are the point: toast 20.4, objective
    // 17.8, buttons 17.1, chip name 14.5, chip distance 14.5 CSS px - all above
    // DesktopFontUnits' 14 px floor, which is itself above the 11 pt minimum.
    //
    // ⚠ The FLOOR now decides the two chip sizes, not their point values. That
    // is deliberate, and it means dropping them further does nothing: to make
    // the chips smaller than this, the floor is what has to move, and it is
    // shared with the dock.
    public const float ROW = 44f;

    BuilderManager bm; Canvas canvas; RectTransform root, bar, chipsRow, card;
    readonly List<Chip> chips = new List<Chip>();
    Text toast, banner, cardTitle, cardSub;
    Button garage, challenge, board;
    // the sign-in gate (a stranger's machine) and the yard board
    RectTransform signIn, boardPanel; InputField emailIn, passIn, nameIn; Text signStatus, boardTitle, boardBody; bool busy;
    // owen, 2026-09-12: "when I click board it asked me to sign in, but where is
    // the sign in button" - there wasn't one. The panel was reachable only by
    // challenging a stranger's machine, so the board asked for something the
    // board could not give. It carries its own SIGN IN now.
    Button boardSignIn, boardClose, signCancel; Text signTitle, signSub, boardMe; bool signFromBoard;
    static Font font;
    class Chip { public GameObject go; public Image panel, needleBar, needleTip; public Text label, dist; public RectTransform needleRt; }
    public static readonly Color PANEL_HI = new Color(0.26f, 0.32f, 0.42f, 0.98f);   // the objective's chip
    Text objective;
    Button signLogin, signCreate;

    // ---- THE LIVE METRICS -------------------------------------------------
    // Every literal in this file - ROW and every fontSize - is a REFERENCE
    // number, at 1280x720. What reaches a player is that number through the
    // dock's rule (Row/FontPt below), re-applied whenever the rule moves.
    // `metricSig` is what "moved" means: the row, the type multiplier and the
    // safe rect. Re-applying on a signature rather than once at construction
    // is the whole of findings 2-4, 2026-09-13.
    float row = ROW;                    // the live touch row, in canvas units
    Vector4 metricSig = Vector4.zero;
    readonly Dictionary<Text, float> typePt = new Dictionary<Text, float>();
    float objH = 30f, toastH = 32f, bannerH = 28f;   // laid out by ApplyMetrics
    float boardCloseW = 140f, boardSignW = 140f;
    /// <summary>How many board rows the panel's body can actually draw,
    /// measured off its rect in ApplyMetrics. ShowBoard asks the server for
    /// exactly this many. 8 is a lower bound, not the value - on a landscape
    /// phone the panel holds about 11 and headless about 13.</summary>
    public int BoardRowsFit { get; private set; } = 8;

    // bench seams
    public Button GarageButton { get { return garage; } }
    public Button ChallengeButton { get { return challenge; } }
    public bool CardShown { get { return card != null && card.gameObject.activeSelf; } }
    public int ChipsShown { get { int n = 0; foreach (var c in chips) if (c.go.activeSelf) n++; return n; } }
    public string ChipText(int i) { return i < chips.Count && chips[i].go.activeSelf ? chips[i].label.text + " " + chips[i].dist.text : ""; }
    public float ChipNeedleDeg(int i) { return i < chips.Count ? chips[i].needleRt.localEulerAngles.z : 0f; }
    public bool ChipHasDrawnNeedle(int i) { return i < chips.Count && chips[i].needleBar != null && chips[i].needleBar.sprite == null && chips[i].needleTip != null; }
    public bool ChipHighlighted(int i) { return i < chips.Count && chips[i].panel.color == PANEL_HI; }
    public string ObjectiveText { get { return objective != null && objective.gameObject.activeSelf ? objective.text : ""; } }
    public string ToastText { get { return toast != null && toast.gameObject.activeSelf ? toast.text : ""; } }
    public string BannerText { get { return banner != null && banner.gameObject.activeSelf ? banner.text : ""; } }
    public string CardTitle { get { return cardTitle != null ? cardTitle.text : ""; } }
    public bool SignInShown { get { return signIn != null && signIn.gameObject.activeSelf; } }
    public Button BoardButton { get { return board; } }
    public Button BoardSignInButton { get { return boardSignIn; } }
    public Button SignInCancelButton { get { return signCancel; } }
    public bool BoardShown { get { return boardPanel != null && boardPanel.gameObject.activeSelf; } }
    public bool BoardSignInShown { get { return boardSignIn != null && boardSignIn.gameObject.activeSelf; } }
    public string SignInTitle { get { return signTitle != null ? signTitle.text : ""; } }
    public string BoardText { get { return boardBody != null ? boardBody.text : ""; } }
    // ---- geometry seams, so MapBench measures rects instead of reflecting ----
    /// <summary>The live touch row in canvas units - the dock's, not ROW.</summary>
    public float RowUnits { get { return row; } }
    /// <summary>The dock's legibility floor in THIS canvas's units: the units
    /// that render at 14 CSS px, which is where DesktopFontUnits clamps. Every
    /// label in this HUD must be at or above it.</summary>
    public int TypeFloorUnits { get { return FontPt(0f); } }
    public RectTransform SafeRoot { get { return root; } }
    public RectTransform BarRect { get { return bar; } }
    public RectTransform ObjectiveRect { get { return objective != null ? objective.rectTransform : null; } }
    public RectTransform SignInPanel { get { return signIn; } }
    public RectTransform BoardPanel { get { return boardPanel; } }
    public RectTransform CardRect { get { return card; } }
    /// <summary>The three map lines' VISIBILITY, separately from their text -
    /// the gate hides them while a modal is up, and a bench that asked only
    /// "do they overlap" would be satisfied by nudging them twenty units.</summary>
    public bool ObjectiveShown { get { return objective != null && objective.gameObject.activeSelf; } }
    public bool ToastShown { get { return toast != null && toast.gameObject.activeSelf; } }
    public bool BannerShown { get { return banner != null && banner.gameObject.activeSelf; } }
    public RectTransform CardSubRect { get { return cardSub != null ? cardSub.rectTransform : null; } }
    /// <summary>The sub-line's real width at its live type, wrapped. Compared
    /// against its own rect, this is finding 5.</summary>
    public float CardSubWidthUnits { get { return cardSub != null ? cardSub.preferredWidth : 0f; } }
    public float CardSubHeightUnits { get { return cardSub != null ? cardSub.preferredHeight : 0f; } }
    public bool CardSubWraps { get { return cardSub != null && cardSub.horizontalOverflow == HorizontalWrapMode.Wrap; } }
    /// <summary>The board list's rendered height at the body's own width.</summary>
    public float BoardBodyHeightUnits { get { return boardBody != null ? boardBody.preferredHeight : 0f; } }
    public Text BoardMeLabel { get { return boardMe; } }
    public bool BoardMeShown { get { return boardMe != null && boardMe.gameObject.activeSelf; } }
    public string BoardMeText { get { return boardMe != null ? boardMe.text : ""; } }
    /// <summary>What ShowBoard last asked the server for. It said 20 while the
    /// panel drew 8; a bench that only measured the panel could not see that.</summary>
    public int BoardRowsAsked { get; private set; }

    public static MapHudUI Ensure(BuilderManager bm)
    {
        if (inst == null) inst = new GameObject("map_hud").AddComponent<MapHudUI>();
        inst.bm = bm;
        if (inst.canvas == null) inst.Build();
        inst.canvas.enabled = true;
        return inst;
    }
    public static void Drop()
    {
        if (inst == null) return;
        Destroy(inst.gameObject); inst = null;
    }
    void OnDestroy() { if (inst == this) inst = null; }

    static Font Fnt()
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    void Build()
    {
        var cgo = new GameObject("map_hud_canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 190;   // under the dock's 200 (the dock stands down on the map anyway)
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;

        root = new GameObject("safe", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(canvas.transform, false);
        Stretch(root);

        // the top bar: chips on the left, GARAGE on the right
        bar = new GameObject("bar", typeof(RectTransform)).GetComponent<RectTransform>();
        bar.SetParent(root, false);
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
        bar.anchoredPosition = new Vector2(0f, -8f); bar.sizeDelta = new Vector2(-16f, ROW);
        chipsRow = new GameObject("chips", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
        chipsRow.SetParent(bar, false);
        chipsRow.anchorMin = new Vector2(0f, 0f); chipsRow.anchorMax = new Vector2(1f, 1f); chipsRow.offsetMin = new Vector2(8f, 0f); chipsRow.offsetMax = new Vector2(-130f, 0f);
        var hl = chipsRow.GetComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f; hl.childAlignment = TextAnchor.MiddleLeft; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true; hl.childControlWidth = true; hl.childControlHeight = true;
        for (int i = 0; i < 4; i++) chips.Add(MkChip(i));
        garage = MkButton("garage", bar, "GARAGE", 15, () => { if (bm != null) bm.LeaveMap(); });
        var grt = garage.GetComponent<RectTransform>();
        grt.anchorMin = new Vector2(1f, 0f); grt.anchorMax = new Vector2(1f, 1f); grt.pivot = new Vector2(1f, 0.5f);
        grt.anchoredPosition = new Vector2(-8f, 0f); grt.sizeDelta = new Vector2(112f, 0f);
        board = MkButton("board", bar, "BOARD", 15, () => ShowBoard());
        if (BuilderManager.PortalBuild) board.gameObject.SetActive(false);   // no board on a portal build (no login there)
        var brt0 = board.GetComponent<RectTransform>();
        brt0.anchorMin = new Vector2(1f, 0f); brt0.anchorMax = new Vector2(1f, 1f); brt0.pivot = new Vector2(1f, 0.5f);
        brt0.anchoredPosition = new Vector2(-128f, 0f); brt0.sizeDelta = new Vector2(100f, 0f);
        chipsRow.offsetMax = new Vector2(-240f, 0f);
        BuildSignIn(); BuildBoard();

        // under the bar: the banner (a place) or the toast (a reward)
        banner = MkText("banner", root, "", 15, TextAnchor.MiddleCenter);
        banner.color = BANNER;
        var brt = banner.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -8f - ROW - 6f); brt.sizeDelta = new Vector2(-32f, 28f);
        toast = MkText("toast", root, "", 18, TextAnchor.MiddleCenter);
        toast.fontStyle = FontStyle.Bold; toast.color = TOAST;
        var trt = toast.rectTransform; trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -8f - ROW - 6f); trt.sizeDelta = new Vector2(-32f, 32f);
        AddShadow(toast.gameObject); AddShadow(banner.gameObject);
        // the objective: what to do next, right under the bar (the first minute)
        objective = MkText("objective", root, "", 16, TextAnchor.MiddleCenter);
        objective.fontStyle = FontStyle.Bold; objective.color = AMBER;
        var ort = objective.rectTransform; ort.anchorMin = new Vector2(0f, 1f); ort.anchorMax = new Vector2(1f, 1f); ort.pivot = new Vector2(0.5f, 1f);
        ort.anchoredPosition = new Vector2(0f, -8f - ROW - 6f); ort.sizeDelta = new Vector2(-32f, 30f);
        AddShadow(objective.gameObject);
        // the banner and the toast sit a row lower when there is an objective
        objective.gameObject.SetActive(false);

        // the encounter card, bottom centre
        card = MkPanel("card", root, PANEL).GetComponent<RectTransform>();
        card.anchorMin = new Vector2(0.5f, 0f); card.anchorMax = new Vector2(0.5f, 0f); card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, 16f); card.sizeDelta = new Vector2(420f, 124f);
        cardTitle = MkText("title", card, "", 20, TextAnchor.MiddleCenter); cardTitle.fontStyle = FontStyle.Bold;
        var ctr = cardTitle.rectTransform; ctr.anchorMin = new Vector2(0f, 1f); ctr.anchorMax = new Vector2(1f, 1f); ctr.pivot = new Vector2(0.5f, 1f);
        ctr.anchoredPosition = new Vector2(0f, -8f); ctr.sizeDelta = new Vector2(-16f, 26f);
        cardSub = MkText("sub", card, "", 13, TextAnchor.MiddleCenter); cardSub.color = new Color(0.75f, 0.80f, 0.88f);
        // ⚠ THE SUB-LINE WRAPS, and MkText's default does not.
        // "another player's machine  ·  30-second bout, you drive  ·  points at
        // the bell" (BuilderManager.Map.cs:892) is 77 characters. At the
        // reference 13 it measured ~485 units in a 404-unit box inside a
        // 420-unit card, centre-aligned - so it spilled ~40 units past each
        // edge of the dark panel onto lit terrain. Scaling the type up for
        // finding 2 makes the same string WIDER, so the wrap is not optional:
        // the two fixes only work together. The card's height is measured from
        // this Text (LayoutCard) rather than a literal, because a longer string
        // is exactly how this comes back.
        cardSub.horizontalOverflow = HorizontalWrapMode.Wrap;
        cardSub.verticalOverflow = VerticalWrapMode.Overflow;
        var csr = cardSub.rectTransform; csr.anchorMin = new Vector2(0f, 1f); csr.anchorMax = new Vector2(1f, 1f); csr.pivot = new Vector2(0.5f, 1f);
        csr.anchoredPosition = new Vector2(0f, -36f); csr.sizeDelta = new Vector2(-16f, 22f);
        challenge = MkButton("challenge", card, "CHALLENGE", 20, () => { if (bm != null) bm.ChallengeParked(); });
        challenge.GetComponent<Image>().color = AMBER;
        challenge.GetComponentInChildren<Text>().color = new Color(0.12f, 0.08f, 0.02f);
        var chr = challenge.GetComponent<RectTransform>(); chr.anchorMin = new Vector2(0f, 0f); chr.anchorMax = new Vector2(1f, 0f); chr.pivot = new Vector2(0.5f, 0f);
        chr.anchoredPosition = new Vector2(0f, 10f); chr.sizeDelta = new Vector2(-48f, ROW);
        card.gameObject.SetActive(false);
        toast.gameObject.SetActive(false); banner.gameObject.SetActive(false);
    }

    // ---- the sign-in gate: the game's only login, and only to challenge a stranger
    void BuildSignIn()
    {
        signIn = MkPanel("signin", root, PANEL).GetComponent<RectTransform>();
        signIn.anchorMin = signIn.anchorMax = new Vector2(0.5f, 0.5f); signIn.pivot = new Vector2(0.5f, 0.5f);
        signIn.sizeDelta = new Vector2(420f, 300f);
        signTitle = MkText("title", signIn, "SIGN IN TO CHALLENGE", 20, TextAnchor.MiddleCenter); signTitle.fontStyle = FontStyle.Bold;
        Place(signTitle.rectTransform, 0f, -10f, -16f, 28f);
        signSub = MkText("sub", signIn, "another player's machine - points go on the board under your name", 13, TextAnchor.MiddleCenter);
        signSub.color = new Color(0.75f, 0.80f, 0.88f); Place(signSub.rectTransform, 0f, -38f, -16f, 20f);
        emailIn = MkInput("email", signIn, "email", false); Place(emailIn.GetComponent<RectTransform>(), 0f, -66f, -48f, ROW);
        passIn = MkInput("password", signIn, "password", true); Place(passIn.GetComponent<RectTransform>(), 0f, -66f - ROW - 8f, -48f, ROW);
        nameIn = MkInput("name", signIn, "display name (new accounts)", false); Place(nameIn.GetComponent<RectTransform>(), 0f, -66f - 2f * (ROW + 8f), -48f, ROW);
        signStatus = MkText("status", signIn, "", 13, TextAnchor.MiddleCenter); signStatus.color = AMBER;
        Place(signStatus.rectTransform, 0f, -66f - 3f * (ROW + 8f) + 2f, -16f, 20f);
        var login = signLogin = MkButton("login", signIn, "SIGN IN", 17, () => DoSignIn(false));
        login.GetComponent<Image>().color = AMBER; login.GetComponentInChildren<Text>().color = new Color(0.12f, 0.08f, 0.02f);
        var lr = login.GetComponent<RectTransform>(); lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 0f); lr.pivot = new Vector2(0f, 0f);
        lr.anchoredPosition = new Vector2(16f, 12f); lr.sizeDelta = new Vector2(120f, ROW);
        var create = signCreate = MkButton("create", signIn, "CREATE ACCOUNT", 15, () => DoSignIn(true));
        // laid out left-to-right from the panel's left edge by ApplyMetrics:
        // three buttons whose widths grow with the type cannot be centred and
        // edge-anchored without meeting in the middle on a big-type phone.
        var cr = create.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(0f, 0f); cr.anchorMax = new Vector2(0f, 0f); cr.pivot = new Vector2(0f, 0f);
        cr.anchoredPosition = new Vector2(148f, 12f); cr.sizeDelta = new Vector2(150f, ROW);
        var cancel = signCancel = MkButton("cancel", signIn, "NOT NOW", 15, () => { HideSignIn(); if (signFromBoard) { signFromBoard = false; ShowBoard(); } });
        var xr = cancel.GetComponent<RectTransform>(); xr.anchorMin = new Vector2(1f, 0f); xr.anchorMax = new Vector2(1f, 0f); xr.pivot = new Vector2(1f, 0f);
        xr.anchoredPosition = new Vector2(-16f, 12f); xr.sizeDelta = new Vector2(96f, ROW);
        signIn.gameObject.SetActive(false);
    }
    public void ShowSignIn() { ShowSignIn(false); }
    /// <summary>fromBoard: opened by the board's own button rather than by a
    /// challenge, so the wording is about the board and NOT NOW goes back to it.</summary>
    public void ShowSignIn(bool fromBoard)
    {
        if (signIn == null) return;
        signFromBoard = fromBoard;
        if (fromBoard) HideBoard();
        if (signTitle != null) signTitle.text = fromBoard ? "SIGN IN TO GET ON THE BOARD" : "SIGN IN TO CHALLENGE";
        if (signSub != null) signSub.text = fromBoard ? "your yard points are posted under your name"
                                                     : "another player's machine - points go on the board under your name";
        signStatus.text = ""; busy = false;
        signIn.gameObject.SetActive(true);
        signIn.SetAsLastSibling();
    }
    public void HideSignIn() { if (signIn != null) signIn.gameObject.SetActive(false); }
    void DoSignIn(bool create)
    {
        if (busy || bm == null) return;
        string email = emailIn.text.Trim(), pass = passIn.text, name = nameIn.text.Trim();
        if (email.Length < 3 || !email.Contains("@")) { signStatus.text = "an email address, please"; return; }
        if (pass.Length < 6) { signStatus.text = "a password of at least 6 characters"; return; }
        if (create && name.Length < 2) { signStatus.text = "a display name of at least 2 characters"; return; }
        busy = true; signStatus.text = create ? "creating..." : "signing in...";
        System.Action<string, string> done = (displayName, err) =>
        {
            busy = false;
            if (err != null) { signStatus.text = err; return; }
            LadderClient.SaveSession(displayName ?? name);
            HideSignIn();
            bm.OnSignedIn(displayName ?? name);
            if (signFromBoard) { signFromBoard = false; ShowBoard(); }
        };
        if (create) bm.StartCoroutine(LadderClient.Register(email, pass, name, done));
        else bm.StartCoroutine(LadderClient.Login(email, pass, done));
    }

    // ---- the board: accounts by yard points
    void BuildBoard()
    {
        boardPanel = MkPanel("board_panel", root, PANEL).GetComponent<RectTransform>();
        boardPanel.anchorMin = boardPanel.anchorMax = new Vector2(0.5f, 0.5f); boardPanel.pivot = new Vector2(0.5f, 0.5f);
        boardPanel.sizeDelta = new Vector2(440f, 400f);
        boardTitle = MkText("title", boardPanel, "YARD BOARD", 20, TextAnchor.MiddleCenter); boardTitle.fontStyle = FontStyle.Bold;
        Place(boardTitle.rectTransform, 0f, -10f, -16f, 28f);
        boardBody = MkText("body", boardPanel, "", 15, TextAnchor.UpperLeft);
        boardBody.horizontalOverflow = HorizontalWrapMode.Wrap; boardBody.verticalOverflow = VerticalWrapMode.Truncate;
        var br = boardBody.rectTransform; br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(1f, 1f);
        br.offsetMin = new Vector2(20f, 12f + ROW + 8f); br.offsetMax = new Vector2(-20f, -44f);
        // ⚠ YOUR OWN STANDING IS A PINNED ROW, NOT THE TAIL OF THE LIST.
        // It used to be appended LAST to the body string, and the body is
        // verticalOverflow Truncate - so on the one screen whose whole purpose
        // is showing a player where they stand, their own line was the FIRST
        // thing cut. Its own rect above CLOSE cannot be truncated by a list.
        boardMe = MkText("board_me", boardPanel, "", 13, TextAnchor.MiddleCenter);
        boardMe.color = AMBER;
        var mr = boardMe.rectTransform; mr.anchorMin = new Vector2(0f, 0f); mr.anchorMax = new Vector2(1f, 0f); mr.pivot = new Vector2(0.5f, 0f);
        mr.anchoredPosition = new Vector2(0f, 12f + ROW + 6f); mr.sizeDelta = new Vector2(-40f, 20f);
        boardClose = MkButton("close", boardPanel, "CLOSE", 16, () => HideBoard());
        var xr = boardClose.GetComponent<RectTransform>(); xr.anchorMin = new Vector2(0.5f, 0f); xr.anchorMax = new Vector2(0.5f, 0f); xr.pivot = new Vector2(0.5f, 0f);
        xr.anchoredPosition = new Vector2(0f, 12f); xr.sizeDelta = new Vector2(140f, ROW);
        boardSignIn = MkButton("board_signin", boardPanel, "SIGN IN", 16, () => ShowSignIn(true));
        boardSignIn.GetComponent<Image>().color = AMBER; boardSignIn.GetComponentInChildren<Text>().color = new Color(0.12f, 0.08f, 0.02f);
        var sr = boardSignIn.GetComponent<RectTransform>(); sr.anchorMin = new Vector2(0.5f, 0f); sr.anchorMax = new Vector2(0.5f, 0f); sr.pivot = new Vector2(0.5f, 0f);
        sr.anchoredPosition = new Vector2(-76f, 12f); sr.sizeDelta = new Vector2(140f, ROW);
        boardSignIn.gameObject.SetActive(false);
        boardPanel.gameObject.SetActive(false);
    }
    public void ShowBoard()
    {
        if (boardPanel == null || bm == null) return;
        boardPanel.gameObject.SetActive(true);
        boardPanel.SetAsLastSibling();
        bool wantSignIn = !LadderClient.SignedIn && !BuilderManager.PortalBuild;
        if (boardSignIn != null) boardSignIn.gameObject.SetActive(wantSignIn);
        LayoutBoardButtons();
        boardBody.text = "reading the board...";
        boardMe.text = ""; boardMe.gameObject.SetActive(false);
        // ASK FOR WHAT THE PANEL CAN SHOW. This said 20 while the body could
        // draw eight, and the body is Truncate, so twelve rows were fetched and
        // silently thrown away - and the count read as a deliberate 8 to anyone
        // who looked at the panel instead of the request. BoardRowsFit is
        // derived from the panel's own measured body height (ApplyMetrics), so
        // the two cannot disagree again.
        BoardRowsAsked = BoardRowsFit;
        bm.StartCoroutine(LadderClient.YardBoard(BoardRowsAsked, (rows, me, err) => RenderBoard(rows, me, err)));
    }

    /// <summary>Draw a board payload. Split out of ShowBoard's lambda so a
    /// bench can hand it rows without a live server - the panel, its buttons
    /// and its sizes all still come from the real ShowBoard path, and this is
    /// the only part a server would have supplied.</summary>
    public void RenderBoard(System.Collections.Generic.List<LadderClient.YardRow> rows, LadderClient.YardRow me, string err)
    {
        if (boardBody == null) return;
        if (rows == null)
        {
            boardBody.text = "the board is out of reach right now\n(" + err + ")\n\nchallenge a stranger's machine and your points go here";
            boardMe.gameObject.SetActive(false);
            return;
        }
        var sb = new System.Text.StringBuilder();
        if (rows.Count == 0) sb.Append("nobody on the board yet - challenge a stranger's machine\n");
        int drawn = Mathf.Min(rows.Count, Mathf.Max(1, BoardRowsFit));
        for (int i = 0; i < drawn; i++)
        {
            var r = rows[i];
            sb.Append(r.rank.ToString().PadLeft(3)).Append("   ").Append(r.owner).Append("   ").Append(r.points).Append(" pts   ").Append(r.wins).Append("/").Append(r.bouts).Append(" won\n");
        }
        boardBody.text = sb.ToString();
        // The pinned row, which a longer list can no longer push off the panel.
        string mine = me != null
            ? "you: rank " + me.rank + "  ·  " + me.points + " pts  ·  " + me.wins + "/" + me.bouts + " won"
            : (!LadderClient.SignedIn ? "sign in below, then beat a stranger's machine, and your name lands here" : "");
        boardMe.text = mine;
        boardMe.gameObject.SetActive(mine.Length > 0);
    }
    public void HideBoard() { if (boardPanel != null) boardPanel.gameObject.SetActive(false); }

    static void Place(RectTransform rt, float x, float top, float wDelta, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, top); rt.sizeDelta = new Vector2(wDelta, h);
    }
    InputField MkInput(string name, Transform parent, string placeholder, bool password)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.13f, 0.14f, 0.18f, 1f);
        var border = MkPanel("border", go.transform, new Color(0.42f, 0.44f, 0.50f, 1f)); border.GetComponent<Image>().raycastTarget = false;
        var brt = border.GetComponent<RectTransform>(); brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = Vector2.zero; brt.sizeDelta = new Vector2(0f, 2f);
        var t = MkText("text", go.transform, "", 16, TextAnchor.MiddleLeft); Stretch(t.rectTransform); t.rectTransform.offsetMin = new Vector2(10f, 0f); t.rectTransform.offsetMax = new Vector2(-10f, 0f);
        t.supportRichText = false;
        var ph = MkText("placeholder", go.transform, placeholder, 16, TextAnchor.MiddleLeft); Stretch(ph.rectTransform); ph.rectTransform.offsetMin = new Vector2(10f, 0f); ph.rectTransform.offsetMax = new Vector2(-10f, 0f);
        ph.color = new Color(0.55f, 0.58f, 0.66f); ph.fontStyle = FontStyle.Italic;
        var f = go.GetComponent<InputField>();
        f.textComponent = t; f.placeholder = ph;
        f.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
        return f;
    }

    Chip MkChip(int i)
    {
        var c = new Chip();
        c.go = MkPanel("chip_" + i, chipsRow, PANEL);
        var le = c.go.AddComponent<LayoutElement>(); le.preferredWidth = 170f; le.minWidth = 120f;
        c.panel = c.go.GetComponent<Image>();
        // the needle is DRAWN - a bar from the centre with a bright tip -
        // rotated to the bearing. A glyph (U+25B2) rendered as nothing in the
        // web build: the font has no such character and WebGL has no OS
        // fallback to borrow one from (live, 2026-09-10).
        var pivot = new GameObject("needle", typeof(RectTransform)).GetComponent<RectTransform>();
        pivot.SetParent(c.go.transform, false);
        pivot.anchorMin = new Vector2(0f, 0.5f); pivot.anchorMax = new Vector2(0f, 0.5f); pivot.pivot = new Vector2(0.5f, 0.5f);
        pivot.anchoredPosition = new Vector2(18f, 0f); pivot.sizeDelta = new Vector2(28f, 28f);
        c.needleRt = pivot;
        var bar = MkPanel("bar", pivot, Color.white); c.needleBar = bar.GetComponent<Image>(); c.needleBar.raycastTarget = false;
        var brt = bar.GetComponent<RectTransform>(); brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = new Vector2(0f, -3f); brt.sizeDelta = new Vector2(3f, 14f);
        var tip = MkPanel("tip", pivot, Color.white); c.needleTip = tip.GetComponent<Image>(); c.needleTip.raycastTarget = false;
        var trt2 = tip.GetComponent<RectTransform>(); trt2.anchorMin = trt2.anchorMax = new Vector2(0.5f, 0.5f); trt2.pivot = new Vector2(0.5f, 0.5f);
        trt2.anchoredPosition = new Vector2(0f, 11f); trt2.sizeDelta = new Vector2(7f, 7f); trt2.localRotation = Quaternion.Euler(0f, 0f, 45f);
        var dot = MkPanel("dot", pivot, new Color(1f, 1f, 1f, 0.35f)); dot.GetComponent<Image>().raycastTarget = false;
        var drt2 = dot.GetComponent<RectTransform>(); drt2.anchorMin = drt2.anchorMax = new Vector2(0.5f, 0.5f); drt2.pivot = new Vector2(0.5f, 0.5f);
        drt2.anchoredPosition = Vector2.zero; drt2.sizeDelta = new Vector2(5f, 5f);
        c.label = MkText("label", c.go.transform, "", 15, TextAnchor.MiddleLeft); c.label.fontStyle = FontStyle.Bold;
        var lr = c.label.rectTransform; lr.anchorMin = new Vector2(0f, 0.5f); lr.anchorMax = new Vector2(1f, 1f); lr.pivot = new Vector2(0f, 1f);
        lr.anchoredPosition = new Vector2(36f, -2f); lr.sizeDelta = new Vector2(-40f, 0f);
        c.label.horizontalOverflow = HorizontalWrapMode.Overflow;
        c.dist = MkText("dist", c.go.transform, "", 12, TextAnchor.MiddleLeft); c.dist.color = new Color(0.78f, 0.82f, 0.90f);
        var dr = c.dist.rectTransform; dr.anchorMin = new Vector2(0f, 0f); dr.anchorMax = new Vector2(1f, 0.5f); dr.pivot = new Vector2(0f, 0f);
        dr.anchoredPosition = new Vector2(36f, 2f); dr.sizeDelta = new Vector2(-40f, 0f);
        c.go.SetActive(false);
        return c;
    }

    void LateUpdate()
    {
        if (bm == null || canvas == null) return;
        bool show = bm.mode == BuilderManager.Mode.Map;
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show) return;
        // Safe-area insets, in canvas units. SafeAreaWeb, NOT Screen.safeArea:
        // Unity reports the whole screen as safe in a WebGL build, and WebGL is
        // the platform this game ships to phones, so this inset computed ZERO
        // on every phone with a notch - a check that could not fail on the
        // platform that ships. Measured by PhoneLayoutBench with a landscape
        // iPhone's insets posed: GARAGE, the only way off the map, reached 55.3
        // units into the notch on the web pass and 62.6 on the iOS pass, and
        // the encounter card's CHALLENGE clipped by 5.8 and 9.1.
        // The insets come back in framebuffer pixels, the same units
        // Screen.safeArea used, so the arithmetic below is unchanged.
        float sf = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        root.offsetMin = new Vector2(SafeAreaWeb.Left / sf, SafeAreaWeb.Bottom / sf);
        root.offsetMax = new Vector2(-SafeAreaWeb.Right / sf, -SafeAreaWeb.Top / sf);
        // Has the sizing rule moved? The row, the type multiplier and the safe
        // rect are the three inputs every box below is built from, and all
        // three move without a script ever running: a browser resize, a
        // rotation, the dock's UI-scale button, a bench forcing phone metrics.
        // Probing FontPt at 100 pt rather than at a real size deliberately
        // clears DesktopFontUnits' 14 CSS px floor, which would otherwise hide
        // a multiplier change behind the clamp.
        var sig = new Vector4(Row(), FontPt(100f), root.rect.width, root.rect.height);
        if (sig != metricSig) { metricSig = sig; ApplyMetrics(); }

        var m = bm.HudModel();
        for (int i = 0; i < chips.Count; i++)
        {
            bool on = i < m.compass.Count;
            if (chips[i].go.activeSelf != on) chips[i].go.SetActive(on);
            if (!on) continue;
            var e = m.compass[i];
            chips[i].label.text = e.label; chips[i].label.color = e.tint;
            chips[i].dist.text = Mathf.RoundToInt(e.dist) + " m";
            chips[i].needleBar.color = e.tint; chips[i].needleTip.color = e.tint;
            chips[i].needleRt.localRotation = Quaternion.Euler(0f, 0f, -e.angle);   // + = to the right = clockwise
            bool hi = i == m.objectiveChip;
            chips[i].panel.color = hi ? PANEL_HI : PANEL;
            // The chip grows with its own text: a 170-unit chip held "TREASURE"
            // at reference type and clipped it at phone type. Measured, not
            // scaled by a guessed factor. minWidth stays at the reference 120
            // so four chips can still squeeze into a narrow bar rather than
            // spilling out of it.
            var le = chips[i].go.GetComponent<LayoutElement>();
            if (le != null)
            {
                float w = 36f + Mathf.Max(chips[i].label.preferredWidth, chips[i].dist.preferredWidth) + 10f;
                le.preferredWidth = Mathf.Max(w, 170f);
                le.minWidth = Mathf.Min(le.preferredWidth, 120f);
            }
        }
        // ---- THE MAP LINES ARE GATED BEHIND THE MODALS ------------------------
        //
        // While the sign-in or the board is up, the objective, the toast and
        // the banner are HIDDEN - not nudged out of the way. Two reasons, and
        // the second is the one that makes this a gate rather than an offset:
        //
        //  1. Coaching someone toward a treasure chest while they are typing a
        //     password is wrong at any row height.
        //  2. An offset can be re-broken by the next metrics change; a gate
        //     cannot. It already happened once: growing the bar's row (the fix
        //     for findings 2-4) pushed these three lines down into the two
        //     centred modals - objective x board_panel overlapped 440 x 10.5
        //     units, objective x signin 524 x 19.4, and with a live toast up
        //     (which Career.SaveNotice force-pushes regardless of what else is
        //     on screen) 440 x 40.2 and 604.9 x 40.2. Both modals call
        //     SetAsLastSibling, so they draw ON TOP and slice the amber line
        //     mid-glyph, which reads as a z-order fault and is not one.
        //
        // The text is kept current while hidden so re-showing a modal-covered
        // line costs no frame of stale copy.
        bool modal = (signIn != null && signIn.gameObject.activeSelf)
                  || (boardPanel != null && boardPanel.gameObject.activeSelf);

        bool hasObj = !string.IsNullOrEmpty(m.objective);
        if (hasObj) objective.text = m.objective;
        bool showObj = hasObj && !modal;
        if (objective.gameObject.activeSelf != showObj) objective.gameObject.SetActive(showObj);
        // Under the bar, under the objective if there is one - both heights
        // live, never the ROW literal. The objective's own Y is in
        // ApplyMetrics; this one changes with the model, not with the metrics.
        // It follows hasObj, not showObj: a modal hides these three together,
        // so the stack never has to re-close a gap it is not showing.
        float under = -8f - row - 6f - (hasObj ? objH + 4f : 0f);
        toast.rectTransform.anchoredPosition = new Vector2(0f, under);
        banner.rectTransform.anchoredPosition = new Vector2(0f, under);
        bool hasToast = !string.IsNullOrEmpty(m.toast);
        if (hasToast) toast.text = m.toast;
        bool showToast = hasToast && !modal;
        if (toast.gameObject.activeSelf != showToast) toast.gameObject.SetActive(showToast);
        bool hasBanner = !hasToast && !string.IsNullOrEmpty(m.banner);
        if (hasBanner) banner.text = m.banner;
        bool showBanner = hasBanner && !modal;
        if (banner.gameObject.activeSelf != showBanner) banner.gameObject.SetActive(showBanner);
        if (card.gameObject.activeSelf != m.card) card.gameObject.SetActive(m.card);
        if (m.card)
        {
            cardTitle.text = m.cardTitle;
            // The sub-line WRAPS, so its height depends on the STRING, not only
            // on the metrics - re-lay the card whenever the text changes.
            if (cardSub.text != m.cardSub) { cardSub.text = m.cardSub; LayoutCard(); }
        }
    }

    // ---- THE DOCK'S RULE, FOR THIS CANVAS ---------------------------------
    //
    // WHY THE RULE IS RE-STATED HERE RATHER THAN CALLED. MobileBuilderUI's
    // TouchRow() and FontUnits() are instance methods bound to the DOCK's
    // canvas, and the two pieces they need - PhysicalTouchSizing and the
    // browser's device pixel ratio - are private to that file, which another
    // session owns. So this takes the one PUBLIC seam it can, the pure
    // DesktopFontUnits, and restates the two short predicates around it. The
    // arithmetic below is the dock's, line for line; if either copy changes,
    // the other has to, and MapBench's type-floor check is what says so.
    //
    // MEASURED, 2026-09-13, on a 932x430 CSS-pt landscape phone: framebuffer
    // 1864x860, canvas 1413x652 units, so scaleFactor 1.3188 at
    // devicePixelRatio 2 and ONE CANVAS UNIT IS 0.659 CSS PX. Before this,
    // ROW = 44 units was 29 CSS px (66% of the 44 pt floor) while every dock
    // control was 52, and the literal fontSizes landed between 8.6 and 12.5
    // CSS px against the dock's 14 px floor and Apple's 11 pt minimum.

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiPixelRatio();
#endif
    /// <summary>The browser's device pixel ratio, by the dock's clamp. 1
    /// everywhere else, which is what the dock's own reader leaves it at.
    /// Cached per frame for the same reason ReadBrowserMetrics is: a metrics
    /// change re-sizes ~30 labels and each one would otherwise be a JS call.</summary>
    static int pxFrame = -1; static float pxCached = 1f;
    static float PixelRatio()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // The same forced-browser-metrics hook the dock honours, so a bench
        // measuring the WEB path gets one pixel ratio for the whole UI rather
        // than a phone-sized dock beside a 1x map HUD.
        if (MobileBuilderUI.forcedPixelRatio.HasValue)
            return Mathf.Clamp(MobileBuilderUI.forcedPixelRatio.Value, 0.25f, 4f);
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        if (pxFrame == Time.frameCount) return pxCached;
        pxFrame = Time.frameCount;
        pxCached = Mathf.Clamp(ScrapyardUiPixelRatio(), 0.25f, 4f);
        return pxCached;
#else
        return 1f;
#endif
    }

    /// <summary>THE DOCK'S ANSWER, NOT A SECOND ONE. This was a copy of
    /// MobileBuilderUI's predicate while that one was private; two canvases
    /// with two rules that must never disagree is how a dock at 130% ends up
    /// beside an unscaled map HUD. It is one seam now.</summary>
    static bool Physical { get { return MobileBuilderUI.SizesByPhysicalDpi; } }

    float Scale() { float s = canvas != null ? canvas.scaleFactor : 0f; return s; }

    /// <summary>The touch row in canvas units - MobileBuilderUI.TouchRow().</summary>
    float Row()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MobileBuilderUI.forcedRowUnits.HasValue) return MobileBuilderUI.forcedRowUnits.Value;
#endif
        float sf = Scale();
        // Before the CanvasScaler's first pass scaleFactor is 0; 44 is the
        // same answer TouchRow() gives when it cannot trust its inputs.
        if (sf < 0.01f) return ROW;
        // The dock's own formula, including the user's interface-size
        // preference - which this canvas used to ignore, so a player at 130%
        // scaled the dock and left the map HUD where it was.
        if (!Physical) return MobileBuilderUI.DesktopRowFor(sf, PixelRatio(), MobileBuilderUI.UserUiScale);
        float dpi = UnityEngine.Device.Screen.dpi;
        if (dpi < 1f) return ROW;
        return Mathf.Clamp((ROW / 163f) * dpi / sf, 34f, 110f);
    }

    /// <summary>A point size in canvas units - MobileBuilderUI.FontUnits().</summary>
    int FontPt(float pt)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (MobileBuilderUI.forcedFontScale.HasValue) return Mathf.Max(8, Mathf.RoundToInt(pt * MobileBuilderUI.forcedFontScale.Value));
#endif
        float sf = Scale();
        // The user's interface-size preference belongs here too: passing 1f
        // left a player at 130% with scaled dock type and unscaled map type.
        if (!Physical) return MobileBuilderUI.DesktopFontUnits(pt, sf < 0.01f ? 1f : sf, PixelRatio(), MobileBuilderUI.UserUiScale);
        float dpi = UnityEngine.Device.Screen.dpi;
        if (sf < 0.01f || dpi < 1f) return Mathf.Max(8, Mathf.RoundToInt(pt));
        return Mathf.Max(8, Mathf.RoundToInt(pt * (dpi / 163f) / sf));
    }

    /// <summary>A text box that holds one line of the given type.
    ///
    /// 1.5 is NOT measured and does not need to be: it is the middle of the
    /// box-to-type ratios this file's own literals already used - 26/20 = 1.30
    /// on the card title, 22/13 = 1.69 on its sub-line, 32/22 = 1.45 on the
    /// toast, 30/19 = 1.58 on the objective - collapsed to one number so every
    /// box scales alike instead of eight of them drifting apart. It is a
    /// LAYOUT ratio, not a threshold; no check passes or fails on it.</summary>
    static float Line(int fontUnits) { return Mathf.Ceil(fontUnits * 1.5f); }

    /// <summary>A button wide enough for its own label, measured rather than
    /// guessed: the literals are the reference-scale widths, and the type
    /// inside them grows with the screen while the box did not, so
    /// "CREATE ACCOUNT" ran out of its 150 units as soon as the type moved.
    /// Text.preferredWidth is the label's real width at its live size.</summary>
    static float FitWidth(Button b, float min)
    {
        if (b == null) return min;
        var t = b.GetComponentInChildren<Text>(true);
        return t == null ? min : Mathf.Max(min, t.preferredWidth + 24f);
    }

    /// <summary>Re-lay the whole HUD at the current metrics. Called from
    /// LateUpdate whenever the signature moves - which is the half that was
    /// missing: the row WAS recomputed for the bar and the CHALLENGE button,
    /// so those two tracked the phone while the sign-in fields, the board and
    /// the objective line stayed at the 1280x720 literals beside them.</summary>
    void ApplyMetrics()
    {
        if (root == null) return;
        row = Row();
        foreach (var kv in typePt) if (kv.Key != null) kv.Key.fontSize = FontPt(kv.Value);

        // ---- the top bar
        bar.sizeDelta = new Vector2(-16f, row);
        float garageW = FitWidth(garage, 112f);
        bool hasBoard = board != null && board.gameObject.activeSelf;   // no board on a portal build
        float boardW = hasBoard ? FitWidth(board, 100f) : 0f;
        var grt = garage.GetComponent<RectTransform>(); grt.sizeDelta = new Vector2(garageW, 0f);
        grt.anchoredPosition = new Vector2(-8f, 0f);
        if (hasBoard)
        {
            var brt0 = board.GetComponent<RectTransform>(); brt0.sizeDelta = new Vector2(boardW, 0f);
            brt0.anchoredPosition = new Vector2(-(8f + garageW + 8f), 0f);
        }
        chipsRow.offsetMax = new Vector2(-(8f + garageW + 8f + (hasBoard ? boardW + 8f : 0f)), 0f);

        // ---- the three lines under the bar
        objH = Line(FontPt(19)); toastH = Line(FontPt(22)); bannerH = Line(FontPt(17));
        objective.rectTransform.anchoredPosition = new Vector2(0f, -8f - row - 6f);
        objective.rectTransform.sizeDelta = new Vector2(-32f, objH);
        toast.rectTransform.sizeDelta = new Vector2(-32f, toastH);
        banner.rectTransform.sizeDelta = new Vector2(-32f, bannerH);

        LayoutCard();

        // ---- the sign-in gate
        float sTitleH = Line(FontPt(20)), sSubH = Line(FontPt(13)), sStatusH = Line(FontPt(13));
        float top0 = 10f + sTitleH + 2f + sSubH + 8f;
        Place(signTitle.rectTransform, 0f, -10f, -16f, sTitleH);
        Place(signSub.rectTransform, 0f, -(10f + sTitleH + 2f), -16f, sSubH);
        Place(emailIn.GetComponent<RectTransform>(), 0f, -top0, -48f, row);
        Place(passIn.GetComponent<RectTransform>(), 0f, -(top0 + row + 8f), -48f, row);
        Place(nameIn.GetComponent<RectTransform>(), 0f, -(top0 + 2f * (row + 8f)), -48f, row);
        Place(signStatus.rectTransform, 0f, -(top0 + 3f * (row + 8f) + 2f), -16f, sStatusH);
        float wLogin = FitWidth(signLogin, 120f), wCreate = FitWidth(signCreate, 150f), wCancel = FitWidth(signCancel, 96f);
        var lr = signLogin.GetComponent<RectTransform>(); lr.sizeDelta = new Vector2(wLogin, row); lr.anchoredPosition = new Vector2(16f, 12f);
        var cr = signCreate.GetComponent<RectTransform>(); cr.sizeDelta = new Vector2(wCreate, row); cr.anchoredPosition = new Vector2(16f + wLogin + 12f, 12f);
        var xr2 = signCancel.GetComponent<RectTransform>(); xr2.sizeDelta = new Vector2(wCancel, row); xr2.anchoredPosition = new Vector2(-16f, 12f);
        signIn.sizeDelta = Fit(Mathf.Max(420f, 16f + wLogin + 12f + wCreate + 12f + wCancel + 16f),
                               top0 + 3f * (row + 8f) + 2f + sStatusH + 10f + row + 12f);

        // ---- the board
        //
        // THE PANEL'S HEIGHT COMES FIRST AND THE ROW COUNT FOLLOWS FROM IT.
        // Written the other way round - a literal row count deciding the
        // height - is what let ShowBoard ask for 20 while the body drew 8. The
        // panel takes what the safe area allows, up to the API's full page of
        // 20, and BoardRowsFit is then simply how many lines the measured body
        // rect holds. The only chosen number here is that 20, the API's page
        // size. There is deliberately no minimum row count: a floor would be
        // the same mistake one order smaller - a number that says we draw more
        // than the box can hold, on the screen too short to hold it.
        float bTitleH = Line(FontPt(20));
        float meH = Line(FontPt(13));
        float bodyRow = Line(FontPt(15));
        // Exactly the rects below, not an estimate of them:
        //   top    = 10 + title + 6          (boardBody.offsetMax)
        //   bottom = 12 + row + 6 + me + 6   (boardBody.offsetMin)
        float chrome = (10f + bTitleH + 6f) + (12f + row + 6f + meH + 6f);
        Place(boardTitle.rectTransform, 0f, -10f, -16f, bTitleH);
        boardCloseW = FitWidth(boardClose, 140f); boardSignW = FitWidth(boardSignIn, 140f);
        boardClose.GetComponent<RectTransform>().sizeDelta = new Vector2(boardCloseW, row);
        boardSignIn.GetComponent<RectTransform>().sizeDelta = new Vector2(boardSignW, row);
        LayoutBoardButtons();
        boardPanel.sizeDelta = Fit(Mathf.Max(440f, boardCloseW + boardSignW + 8f + 40f),
                                   chrome + 20f * bodyRow);
        var mr = boardMe.rectTransform;
        mr.anchoredPosition = new Vector2(0f, 12f + row + 6f); mr.sizeDelta = new Vector2(-40f, meH);
        var br = boardBody.rectTransform;
        br.offsetMin = new Vector2(20f, 12f + row + 6f + meH + 6f); br.offsetMax = new Vector2(-20f, -(10f + bTitleH + 6f));
        BoardRowsFit = Mathf.Max(1, Mathf.FloorToInt(br.rect.height / Mathf.Max(1f, bodyRow)));
    }

    /// <summary>The encounter card, sized around its own wrapped sub-line.
    ///
    /// ORDER MATTERS HERE. Text.preferredHeight for a WRAPPING label is taken
    /// at the label's current rect WIDTH, so the card's width has to be set and
    /// the sub-line placed (which is what fixes its width) BEFORE the height is
    /// read. Measuring first and sizing after reads the previous card's width -
    /// the trap FitPaletteRows fell into twice.
    ///
    /// Called from ApplyMetrics (the type moved) and from LateUpdate (the
    /// string changed). Both matter: a two-line sub-line on one screen is a
    /// one-line sub-line on a wider one.</summary>
    void LayoutCard()
    {
        if (card == null || cardSub == null) return;
        float cTitleH = Line(FontPt(20));
        float cardW = Mathf.Min(420f, Mathf.Max(240f, root.rect.width - 24f));
        card.sizeDelta = new Vector2(cardW, card.sizeDelta.y);
        Place(cardTitle.rectTransform, 0f, -8f, -16f, cTitleH);
        Place(cardSub.rectTransform, 0f, -(8f + cTitleH + 2f), -16f, Line(FontPt(13)));
        float subH = Mathf.Max(Line(FontPt(13)), Mathf.Ceil(cardSub.preferredHeight));
        Place(cardSub.rectTransform, 0f, -(8f + cTitleH + 2f), -16f, subH);
        var chr = challenge.GetComponent<RectTransform>(); chr.sizeDelta = new Vector2(-48f, row);
        card.sizeDelta = new Vector2(cardW, 8f + cTitleH + 2f + subH + 10f + row + 10f);
    }

    /// <summary>A panel, never larger than the safe rect it sits in.</summary>
    Vector2 Fit(float w, float h)
    {
        float maxW = root.rect.width > 40f ? root.rect.width - 16f : w;
        float maxH = root.rect.height > 40f ? root.rect.height - 16f : h;
        return new Vector2(Mathf.Min(w, maxW), Mathf.Min(h, maxH));
    }

    /// <summary>CLOSE alone is centred; with SIGN IN beside it the pair is,
    /// with a gap. Positions come from the measured widths, so they hold at
    /// any type size.</summary>
    void LayoutBoardButtons()
    {
        if (boardClose == null || boardSignIn == null) return;
        bool both = boardSignIn.gameObject.activeSelf;
        var xr = boardClose.GetComponent<RectTransform>();
        var sr = boardSignIn.GetComponent<RectTransform>();
        if (!both) { xr.anchoredPosition = new Vector2(0f, 12f); return; }
        float half = (boardSignW + 8f + boardCloseW) * 0.5f;
        sr.anchoredPosition = new Vector2(-half + boardSignW * 0.5f, 12f);
        xr.anchoredPosition = new Vector2(half - boardCloseW * 0.5f, 12f);
    }

    // ---- the dock's helpers, copied so this file needs nothing private -----
    static GameObject MkPanel(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = c.a > 0.001f;
        return go;
    }
    /// <summary>`size` is a POINT size, the way every literal in
    /// MobileBuilderUI is. It is registered here and converted to canvas units
    /// by ApplyMetrics - writing it straight into fontSize is what made the
    /// board body 9.9 CSS px on a phone.</summary>
    Text MkText(string name, Transform parent, string s, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>(); t.font = Fnt(); t.text = s; t.fontSize = FontPt(size); t.alignment = anchor; t.color = Color.white; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        typePt[t] = size;
        return t;
    }
    Button MkButton(string name, Transform parent, string label, int size, System.Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = PANEL;
        var t = MkText("t", go.transform, label, size, TextAnchor.MiddleCenter); t.fontStyle = FontStyle.Bold; Stretch(t.rectTransform);
        if (onClick != null) go.GetComponent<Button>().onClick.AddListener(() => onClick());
        return go.GetComponent<Button>();
    }
    static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
    static void AddShadow(GameObject go) { var s = go.AddComponent<Shadow>(); s.effectColor = new Color(0f, 0f, 0f, 0.7f); s.effectDistance = new Vector2(1f, -1f); }
}
}
