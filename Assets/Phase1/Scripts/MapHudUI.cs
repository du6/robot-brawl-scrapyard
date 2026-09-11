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
    public const float ROW = 44f;

    BuilderManager bm; Canvas canvas; RectTransform root, bar, chipsRow, card;
    readonly List<Chip> chips = new List<Chip>();
    Text toast, banner, cardTitle, cardSub;
    Button garage, challenge, board;
    // the sign-in gate (a stranger's machine) and the yard board
    RectTransform signIn, boardPanel; InputField emailIn, passIn, nameIn; Text signStatus, boardTitle, boardBody; bool busy;
    static Font font;
    class Chip { public GameObject go; public Image panel, needleBar, needleTip; public Text label, dist; public RectTransform needleRt; }
    public static readonly Color PANEL_HI = new Color(0.26f, 0.32f, 0.42f, 0.98f);   // the objective's chip
    Text objective;

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
    public bool BoardShown { get { return boardPanel != null && boardPanel.gameObject.activeSelf; } }
    public string BoardText { get { return boardBody != null ? boardBody.text : ""; } }

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
        garage = MkButton("garage", bar, "GARAGE", 18, () => { if (bm != null) bm.LeaveMap(); });
        var grt = garage.GetComponent<RectTransform>();
        grt.anchorMin = new Vector2(1f, 0f); grt.anchorMax = new Vector2(1f, 1f); grt.pivot = new Vector2(1f, 0.5f);
        grt.anchoredPosition = new Vector2(-8f, 0f); grt.sizeDelta = new Vector2(112f, 0f);
        board = MkButton("board", bar, "BOARD", 18, () => ShowBoard());
        var brt0 = board.GetComponent<RectTransform>();
        brt0.anchorMin = new Vector2(1f, 0f); brt0.anchorMax = new Vector2(1f, 1f); brt0.pivot = new Vector2(1f, 0.5f);
        brt0.anchoredPosition = new Vector2(-128f, 0f); brt0.sizeDelta = new Vector2(100f, 0f);
        chipsRow.offsetMax = new Vector2(-240f, 0f);
        BuildSignIn(); BuildBoard();

        // under the bar: the banner (a place) or the toast (a reward)
        banner = MkText("banner", root, "", 17, TextAnchor.MiddleCenter);
        banner.color = BANNER;
        var brt = banner.rectTransform; brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f); brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -8f - ROW - 6f); brt.sizeDelta = new Vector2(-32f, 28f);
        toast = MkText("toast", root, "", 22, TextAnchor.MiddleCenter);
        toast.fontStyle = FontStyle.Bold; toast.color = TOAST;
        var trt = toast.rectTransform; trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -8f - ROW - 6f); trt.sizeDelta = new Vector2(-32f, 32f);
        AddShadow(toast.gameObject); AddShadow(banner.gameObject);
        // the objective: what to do next, right under the bar (the first minute)
        objective = MkText("objective", root, "", 19, TextAnchor.MiddleCenter);
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
        var t = MkText("title", signIn, "SIGN IN TO CHALLENGE", 20, TextAnchor.MiddleCenter); t.fontStyle = FontStyle.Bold;
        Place(t.rectTransform, 0f, -10f, -16f, 28f);
        var sub = MkText("sub", signIn, "another player's machine - points go on the board under your name", 13, TextAnchor.MiddleCenter);
        sub.color = new Color(0.75f, 0.80f, 0.88f); Place(sub.rectTransform, 0f, -38f, -16f, 20f);
        emailIn = MkInput("email", signIn, "email", false); Place(emailIn.GetComponent<RectTransform>(), 0f, -66f, -48f, ROW);
        passIn = MkInput("password", signIn, "password", true); Place(passIn.GetComponent<RectTransform>(), 0f, -66f - ROW - 8f, -48f, ROW);
        nameIn = MkInput("name", signIn, "display name (new accounts)", false); Place(nameIn.GetComponent<RectTransform>(), 0f, -66f - 2f * (ROW + 8f), -48f, ROW);
        signStatus = MkText("status", signIn, "", 13, TextAnchor.MiddleCenter); signStatus.color = AMBER;
        Place(signStatus.rectTransform, 0f, -66f - 3f * (ROW + 8f) + 2f, -16f, 20f);
        var login = MkButton("login", signIn, "SIGN IN", 17, () => DoSignIn(false));
        login.GetComponent<Image>().color = AMBER; login.GetComponentInChildren<Text>().color = new Color(0.12f, 0.08f, 0.02f);
        var lr = login.GetComponent<RectTransform>(); lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 0f); lr.pivot = new Vector2(0f, 0f);
        lr.anchoredPosition = new Vector2(16f, 12f); lr.sizeDelta = new Vector2(120f, ROW);
        var create = MkButton("create", signIn, "CREATE ACCOUNT", 15, () => DoSignIn(true));
        var cr = create.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(0.5f, 0f); cr.anchorMax = new Vector2(0.5f, 0f); cr.pivot = new Vector2(0.5f, 0f);
        cr.anchoredPosition = new Vector2(8f, 12f); cr.sizeDelta = new Vector2(150f, ROW);
        var cancel = MkButton("cancel", signIn, "NOT NOW", 15, () => HideSignIn());
        var xr = cancel.GetComponent<RectTransform>(); xr.anchorMin = new Vector2(1f, 0f); xr.anchorMax = new Vector2(1f, 0f); xr.pivot = new Vector2(1f, 0f);
        xr.anchoredPosition = new Vector2(-16f, 12f); xr.sizeDelta = new Vector2(96f, ROW);
        signIn.gameObject.SetActive(false);
    }
    public void ShowSignIn() { if (signIn == null) return; signStatus.text = ""; busy = false; signIn.gameObject.SetActive(true); }
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
        var close = MkButton("close", boardPanel, "CLOSE", 16, () => HideBoard());
        var xr = close.GetComponent<RectTransform>(); xr.anchorMin = new Vector2(0.5f, 0f); xr.anchorMax = new Vector2(0.5f, 0f); xr.pivot = new Vector2(0.5f, 0f);
        xr.anchoredPosition = new Vector2(0f, 12f); xr.sizeDelta = new Vector2(140f, ROW);
        boardPanel.gameObject.SetActive(false);
    }
    public void ShowBoard()
    {
        if (boardPanel == null || bm == null) return;
        boardPanel.gameObject.SetActive(true);
        boardBody.text = "reading the board...";
        bm.StartCoroutine(LadderClient.YardBoard(20, (rows, me, err) =>
        {
            if (boardBody == null) return;
            if (rows == null) { boardBody.text = "the board is out of reach right now\n(" + err + ")\n\nchallenge a stranger's machine and your points go here"; return; }
            var sb = new System.Text.StringBuilder();
            if (rows.Count == 0) sb.Append("nobody on the board yet - challenge a stranger's machine\n");
            foreach (var r in rows) sb.Append(r.rank.ToString().PadLeft(3)).Append("   ").Append(r.owner).Append("   ").Append(r.points).Append(" pts   ").Append(r.wins).Append("/").Append(r.bouts).Append(" won\n");
            if (me != null) sb.Append("\nyou: rank ").Append(me.rank).Append("  ·  ").Append(me.points).Append(" pts  ·  ").Append(me.wins).Append("/").Append(me.bouts).Append(" won");
            else if (!LadderClient.SignedIn) sb.Append("\nsign in (challenge a stranger) to get on the board");
            boardBody.text = sb.ToString();
        }));
    }
    public void HideBoard() { if (boardPanel != null) boardPanel.gameObject.SetActive(false); }

    static void Place(RectTransform rt, float x, float top, float wDelta, float h)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(x, top); rt.sizeDelta = new Vector2(wDelta, h);
    }
    static InputField MkInput(string name, Transform parent, string placeholder, bool password)
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
        c.dist = MkText("dist", c.go.transform, "", 14, TextAnchor.MiddleLeft); c.dist.color = new Color(0.78f, 0.82f, 0.90f);
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
        // safe-area insets, in canvas units
        float sf = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        var sa = Screen.safeArea;
        root.offsetMin = new Vector2(sa.xMin / sf, sa.yMin / sf);
        root.offsetMax = new Vector2(-(Screen.width - sa.xMax) / sf, -(Screen.height - sa.yMax) / sf);
        float row = Mathf.Max(ROW, ROW / 163f * (Screen.dpi > 0f ? Screen.dpi : 163f) / sf);
        bar.sizeDelta = new Vector2(-16f, row);

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
        }
        bool hasObj = !string.IsNullOrEmpty(m.objective);
        if (objective.gameObject.activeSelf != hasObj) objective.gameObject.SetActive(hasObj);
        if (hasObj) objective.text = m.objective;
        float under = -8f - row - 6f - (hasObj ? 30f : 0f);
        toast.rectTransform.anchoredPosition = new Vector2(0f, under);
        banner.rectTransform.anchoredPosition = new Vector2(0f, under);
        bool hasToast = !string.IsNullOrEmpty(m.toast);
        if (toast.gameObject.activeSelf != hasToast) toast.gameObject.SetActive(hasToast);
        if (hasToast) toast.text = m.toast;
        bool hasBanner = !hasToast && !string.IsNullOrEmpty(m.banner);
        if (banner.gameObject.activeSelf != hasBanner) banner.gameObject.SetActive(hasBanner);
        if (hasBanner) banner.text = m.banner;
        if (card.gameObject.activeSelf != m.card) card.gameObject.SetActive(m.card);
        if (m.card) { cardTitle.text = m.cardTitle; cardSub.text = m.cardSub; }
        // the card's touch row
        var chr = challenge.GetComponent<RectTransform>(); chr.sizeDelta = new Vector2(-48f, row);
        card.sizeDelta = new Vector2(Mathf.Min(420f, root.rect.width - 24f), 70f + row + 10f);
    }

    // ---- the dock's helpers, copied so this file needs nothing private -----
    static GameObject MkPanel(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = c.a > 0.001f;
        return go;
    }
    static Text MkText(string name, Transform parent, string s, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>(); t.font = Fnt(); t.text = s; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
    static Button MkButton(string name, Transform parent, string label, int size, System.Action onClick)
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
