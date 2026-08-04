using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
using System.Collections.Generic;

namespace RobotBrawl.Phase0
{

/// <summary>Phase 5 touch-native builder (uGUI). A runtime ScreenSpaceOverlay
/// canvas with a CanvasScaler (density/DPI handled natively), a bottom tab dock
/// (BUILD / FIGHT / GARAGE), big tap targets, and touch placement + camera that
/// drive BuilderManager's existing pipeline via the Phase0Input.debugPointer
/// seam. Activates only on touchscreens (or forceMobileUI in the editor);
/// desktop keeps the IMGUI panel.</summary>
public class MobileBuilderUI : MonoBehaviour
{
    public static bool forceMobileUI;
    public static MobileBuilderUI inst;
    static Font font;

    BuilderManager bm;
    Canvas canvas;
    Text statsText;
    GameObject msgBar; Text msgText;          // R2 finding 4: transient channel, separated
    GameObject tipBar; Text tipText;          // R4 finding 4: onboarding channel, separated
    string msgSeen = ""; float msgAt = -99f;
    const float MSG_LIFE = 7f;
    Text fightInfo; bool wasFighting;   // critic round 2: FIGHT tab context
    GameObject buildPanel, fightPanel, garagePanel;
    // ---- C2: shop. Rebuilt 2026-08-01 as a MATERIAL CATALOG (owen: "how to
    // shop parts with different materials?" - he tried and could not work it
    // out). It used to be one row per part priced in whatever GLOBAL material
    // the chips on the BUILD tab happened to be set to, so buying a titanium
    // beam meant leaving the shop, flipping a mode, and coming back. Now every
    // part expands to its legal materials with price, mass and owned count side
    // by side - design doc 7 ("one catalog: every palette part x every legal
    // material") and 3b (weight caps make strength-per-kilogram the whole
    // question, so the comparison has to be AT the counter). ----
    GameObject shopPanel;
    Text shopHeader;
    class ShopPartRow { public int part; public GameObject go; public Text lbl; public bool open; }
    class ShopMatRow  { public int part; public string mat; public GameObject go; public Text lbl;
                        public Button buy, sell, swap; public Text buyT, sellT, swapT; }
    readonly List<ShopPartRow> shopParts = new List<ShopPartRow>();
    readonly List<ShopMatRow> shopMats = new List<ShopMatRow>();
    readonly List<Button> tabBtns = new List<Button>();
    int armSell = -1; string armSellMat = "";
    string shopNote = ""; bool shopNoteBad;
    // ---- C3: career league board ----
    GameObject careerBoard; Transform careerBoardContent;
    Button ladderBtn, exhibBtn;
    // ---- C4: workshop ----
    GameObject robotsPanel, partsPanel;
    Transform robotsContent, partsContent;
    // ---- MEDALS (2026-08-02, owen): the trophy case ----
    // APPENDED as tab index 5 and never inserted earlier. TouchSmoke asserts
    // Btn("BUILD") != null, CareerSmoke taps tabs by label, and ShowTab /
    // DockH / OverUI all key off indices 0-4; appending keeps every one of
    // those stable.
    GameObject trophyPanel; Transform trophyContent; Text trophyHeader;
    InputField nameInput;
    int retireArmM = -1;
    int bpDelArmM = -1;   // OWEN: blueprint armed for deletion
    readonly List<Button> partButtons = new List<Button>();
    readonly List<Button> matButtons = new List<Button>();
    readonly List<string> matKeys = new List<string>();
    int tab;
    // R1 fix 2: the dock is no longer a fixed 210 - both the layout and the
    // hit test read this ONE RectTransform so they can never disagree.
    RectTransform dockRt;
    GameObject scrim;          // dims the 3D view behind an expanded dock
    // OWEN 2026-08-02 save dialog: SAVE on BUILD no longer needs a robot to
    // already exist - it asks for a name and makes one.
    GameObject saveDlg;
    RectTransform saveDlgCard;
    Text saveDlgTitle;
    Button saveDlgOver, saveDlgNew;
    bool saveDlgConfirm;
    InputField saveDlgName;
    Text saveDlgNote, saveDlgErr;
    Button saveDlgOk;
    ScrollRect partScroll; GameObject partEdge;   // palette overflow affordance
    bool removeArmed; int clickHold; Button removeBtn;
    bool dragging; Vector2 lastP, downP; float moved; float pinchPrev = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("mobile_ui_watch");
        go.AddComponent<MobileBuilderWatch>();
        DontDestroyOnLoad(go);
    }

    public static bool Active { get { return inst != null; } }

    // Test seams (TouchSmoke): read-only state peeks, no behavior.
    public bool RemoveArmed { get { return removeArmed; } }
    /// <summary>R2: the status line and the message channel are two Texts now,
    /// so this joins them - anything that greps this for a message still finds it.</summary>
    public string StatsLine
    {
        get
        {
            string m = (msgBar != null && msgBar.activeSelf && msgText != null) ? msgText.text + "   \u00b7   " : "";
            return m + (statsText != null ? statsText.text : "");
        }
    }

    /// <summary>OWEN 2026-08-03: "can we detect the device type and skip the
    /// first screen and automatically land user to their device option?"
    ///
    /// This is the whole rule, in one place, because the boot mode and the UI
    /// attach MUST agree. They did not have to before - the chooser asked, and
    /// then ShouldActivate ignored the answer, which is why "START CAREER -
    /// Desktop" on an iPad handed you the touch UI anyway. A question the game
    /// overrules is worse than no question.
    ///
    /// HANDHELD, not "has a touchscreen" (owen's call). A Surface or a touch
    /// laptop has a keyboard and a mouse, and the builder's shortcuts - R
    /// rotate, Z undo, Q/E orbit - are worth more there than fat tap targets.
    /// This deliberately drops the old Touchscreen.current / Input.touchSupported
    /// test, which classified those machines as phones.</summary>
    public static bool DeviceWantsTouch()
    {
        // UnityEngine.Device.*, NOT the plain Application/SystemInfo. Measured
        // 2026-08-04 with owen's Device Simulator set to an iPad Pro:
        //
        //     REAL   : isMobilePlatform=False deviceType=Desktop  model=Mac15,13
        //     DEVICE : isMobilePlatform=False deviceType=Handheld model=Generic iPad Pro
        //
        // The plain API answers "what is this Mac", which is true and useless -
        // the whole point of the simulator is to be asked what it is EMULATING.
        // Reading the wrong one put "(detected)" next to the Desktop button on
        // a simulated iPad and, worse, handed the desktop builder to a window
        // that cannot click it (see ModeSelect.ShouldAutoBoot).
        //
        // Outside the simulator UnityEngine.Device.* forwards to the real
        // values, so this is the same answer everywhere else.
        return UnityEngine.Device.Application.isMobilePlatform
            || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
    }

    public static bool ShouldActivate()
    {
        if (forceMobileUI) return true;
        return DeviceWantsTouch();
    }

    static Font Fnt()
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    void Awake()
    {
        inst = this;
        bm = Object.FindFirstObjectByType<BuilderManager>();
        EnsureEventSystem();
        dockOpen = ScreenIsTallEnoughForAnOpenDock();
        BuildCanvas();
        ShowTab(0);
        if (!dockOpen) SetDockOpen(false);   // ShowTab just switched a panel on
    }

    void OnDestroy()
    {
        if (inst == this) inst = null;
        BuilderManager.uiPointerBlocked = false;
        Phase0Input.debugPointer = false;
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildCanvas()
    {
        var cgo = new GameObject("mobile_builder_canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1280f, 720f);
        sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight = 0.5f;

        // R1 fix 2: backdrop dimmer. Built FIRST so it is sibling 0 and
        // therefore draws behind the stats bar and the dock. On the list
        // tabs the build room is decoration, not content - dimming it stops
        // an empty floor competing with the thing the player came to read.
        // raycastTarget MUST stay false: OverUI() owns pointer blocking, and
        // a full-screen raycast target here would swallow every camera drag.
        scrim = MkPanel("scrim", canvas.transform, new Color(0.04f, 0.05f, 0.07f, 0.55f));
        scrim.GetComponent<Image>().raycastTarget = false;
        Stretch(scrim.GetComponent<RectTransform>());
        scrim.SetActive(false);

        // top stats bar
        var bar = MkPanel("stats", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 0.92f));
        var brt = bar.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f); brt.sizeDelta = new Vector2(0f, 46f); brt.anchoredPosition = Vector2.zero;
        statsText = MkText("statsText", bar.transform, "", 20, TextAnchor.MiddleCenter);
        Stretch(statsText.rectTransform);
        statsText.rectTransform.offsetMin = new Vector2(10f, 0f);
        statsText.rectTransform.offsetMax = new Vector2(-10f, 0f);
        // R2 (critic finding 4): the label had no clipping rect and no wrapping,
        // so a long message ran off BOTH screen edges at once - the first word a
        // player could read was "nge". No text in this game should be able to do
        // that.
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow = VerticalWrapMode.Truncate;
        bar.AddComponent<RectMask2D>();

        // R2 (critic finding 4): one Text was doing five jobs and whichever job
        // spoke last deleted the other four. A LoadSnapshot migration notice -
        // written once, never cleared - occupied all five tabs for a whole
        // session, and while it was there the player had no robot name, no
        // mass, no part count and no weight budget at all. Notices get their own
        // bar, which expires; the status line beneath is now never overwritten.
        msgBar = MkPanel("msgbar", canvas.transform, new Color(0.10f, 0.09f, 0.05f, 0.94f));
        var mrt = msgBar.GetComponent<RectTransform>();
        mrt.anchorMin = new Vector2(0f, 1f); mrt.anchorMax = new Vector2(1f, 1f);
        mrt.pivot = new Vector2(0.5f, 1f); mrt.sizeDelta = new Vector2(0f, 38f);
        mrt.anchoredPosition = new Vector2(0f, -46f);
        msgBar.GetComponent<Image>().raycastTarget = false;
        msgText = MkText("msgtext", msgBar.transform, "", 18, TextAnchor.MiddleCenter);
        Stretch(msgText.rectTransform);
        msgText.rectTransform.offsetMin = new Vector2(14f, 0f);
        msgText.rectTransform.offsetMax = new Vector2(-14f, 0f);
        msgText.horizontalOverflow = HorizontalWrapMode.Wrap;
        msgText.verticalOverflow = VerticalWrapMode.Truncate;
        msgText.raycastTarget = false;
        msgBar.AddComponent<RectMask2D>();
        msgBar.SetActive(false);

        // R4 (critic finding 4): the onboarding tip used to REPLACE statsText
        // wholesale on all five tabs, which deleted the robot name, the mass,
        // the part count, every tab hint and section 3b's live weight budget -
        // for every new career, i.e. for every new player, for their whole
        // first session. Onboarding now owns its own row and composes with the
        // status instead of overwriting it, and SKIP TIPS lives IN that row so
        // it is reachable from whichever tab the player is on (it used to be
        // drawn only on ROBOTS, which the player had to guess to visit).
        tipBar = MkPanel("tipbar", canvas.transform, new Color(0.05f, 0.11f, 0.18f, 0.95f));
        var prt = tipBar.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0f, 1f); prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(0.5f, 1f); prt.sizeDelta = new Vector2(0f, TIP_H);
        prt.anchoredPosition = new Vector2(0f, -BAR_H);
        tipText = MkText("tiptext", tipBar.transform, "", 18, TextAnchor.MiddleLeft);
        Stretch(tipText.rectTransform);
        tipText.rectTransform.offsetMin = new Vector2(14f, 0f);
        tipText.rectTransform.offsetMax = new Vector2(-206f, 0f);   // room for < > + SKIP TIPS
        tipText.color = new Color(0.62f, 0.84f, 1f);
        tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
        tipText.verticalOverflow = VerticalWrapMode.Truncate;
        var tskip = MkButton("tipskip", tipBar.transform, "SKIP TIPS", 14, () =>
        {
            // Was tutorialStep = 3, which silenced the row by CLAIMING you had
            // finished onboarding - and now that the tips run past step 3 it
            // would not even have silenced it. A skip should turn tips off,
            // not lie about progress.
            Career.Data.tipsOff = true;
            if (Career.autosave) Career.Save();
            RefreshRobots();
            PumpTip();
        });
        var tsrt = tskip.GetComponent<RectTransform>();
        tsrt.anchorMin = new Vector2(1f, 0.5f); tsrt.anchorMax = new Vector2(1f, 0.5f);
        tsrt.pivot = new Vector2(1f, 0.5f); tsrt.sizeDelta = new Vector2(116f, 26f);
        tsrt.anchoredPosition = new Vector2(-8f, 0f);
        // OWEN 2026-08-02: "Add back/next arrows".
        //
        // These move a VIEW index, never Career.Data.tutorialStep. That
        // distinction is the whole design: tutorialStep is progress, and it is
        // earned - 0->1 by founding a stable, 1->2 by saving a build that
        // validates, 2->3 by settling a contest. If the arrows wrote to it,
        // tapping "next" twice would mark the tutorial complete without the
        // player having done any of it, and tapping it once at step 2 would
        // silently claim a contest had been fought. Reading ahead must not
        // count as doing.
        tipPrev = MkButton("tipprev", tipBar.transform, "\u2039", 20, () => { TipStep(-1); });
        var tprt = tipPrev.GetComponent<RectTransform>();
        tprt.anchorMin = new Vector2(1f, 0.5f); tprt.anchorMax = new Vector2(1f, 0.5f);
        tprt.pivot = new Vector2(1f, 0.5f); tprt.sizeDelta = new Vector2(32f, 26f);
        tprt.anchoredPosition = new Vector2(-166f, 0f);
        tipNext = MkButton("tipnext", tipBar.transform, "\u203a", 20, () => { TipStep(1); });
        var tnrt = tipNext.GetComponent<RectTransform>();
        tnrt.anchorMin = new Vector2(1f, 0.5f); tnrt.anchorMax = new Vector2(1f, 0.5f);
        tnrt.pivot = new Vector2(1f, 0.5f); tnrt.sizeDelta = new Vector2(32f, 26f);
        tnrt.anchoredPosition = new Vector2(-130f, 0f);
        HoverHint(tipPrev, () => tipViewNow > 0 ? null : TIP_FIRST);
        HoverHint(tipNext, () => tipViewNow < BuilderManager.TIP_COUNT - 1 ? null : TIP_LAST);
        tipBar.AddComponent<RectMask2D>();
        tipBar.SetActive(false);

        // bottom dock
        var dock = MkPanel("dock", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 1f));
        var drt = dock.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0f, 0f); drt.anchorMax = new Vector2(1f, 0f);
        drt.pivot = new Vector2(0.5f, 0f); drt.sizeDelta = new Vector2(0f, DockH(0)); drt.anchoredPosition = Vector2.zero;
        dockRt = drt;

        // R6 (owen 2026-08-04): the collapse handle. Sits ABOVE the dock rather
        // than inside it, because a control that hides a panel cannot live in
        // the panel it hides.
        //
        // 300 x 44 units - deliberately the widest thing in the UI for its
        // height. Measured on owen's phone, 44 units is 28 pt tall, which is
        // under Apple's 44 pt floor and would be a bad tap target for a small
        // button; a 190 pt wide bar at that height is not, and the whole point
        // of this control is that it must never be the thing you miss.
        dockHandle = MkButton("dockhandle", canvas.transform, "", 18, ToggleDock);
        handleRt = dockHandle.GetComponent<RectTransform>();
        handleRt.anchorMin = new Vector2(0.5f, 0f);
        handleRt.anchorMax = new Vector2(0.5f, 0f);
        handleRt.pivot = new Vector2(0.5f, 0f);
        handleRt.sizeDelta = new Vector2(300f, HANDLE_H);
        var hImg = dockHandle.GetComponent<UnityEngine.UI.Image>();
        if (hImg != null) hImg.color = new Color(0.11f, 0.12f, 0.15f, 0.96f);

        // tab buttons across the top of the dock. SHOP (C2) exists only in
        // career mode; LayoutTabs re-anchors whenever the career switch flips.
        string[] names = { "BUILD", "FIGHT", "GARAGE", "SHOP", "PARTS", "TROPHIES" };
        tabBtns.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            var tb = MkButton("tab" + i, dock.transform, names[i], 20, () => ShowTab(idx));
            var rt = tb.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 1f); rt.sizeDelta = new Vector2(-4f, 44f); rt.anchoredPosition = new Vector2(0f, 0f);
            tabBtns.Add(tb);
        }
        LayoutTabs();

        // content panels fill the rest of the dock
        buildPanel = MkPanel("build", dock.transform, new Color(0f, 0f, 0f, 0f));
        fightPanel = MkPanel("fight", dock.transform, new Color(0f, 0f, 0f, 0f));
        garagePanel = MkPanel("garage", dock.transform, new Color(0f, 0f, 0f, 0f));
        shopPanel = MkPanel("shop", dock.transform, new Color(0f, 0f, 0f, 0f));
        robotsPanel = MkPanel("robots", dock.transform, new Color(0f, 0f, 0f, 0f));
        partsPanel = MkPanel("parts", dock.transform, new Color(0f, 0f, 0f, 0f));
        trophyPanel = MkPanel("trophies", dock.transform, new Color(0f, 0f, 0f, 0f));
        foreach (var p in new[] { buildPanel, fightPanel, garagePanel, shopPanel, robotsPanel, partsPanel, trophyPanel })
        {
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f); rt.offsetMax = new Vector2(-6f, -48f);
        }
        BuildBuildTab();
        BuildFightTab();
        // C6.5: the sandbox garage is not merely HIDDEN in career - it is
        // never built. An inactive-but-alive "SAVE A" button under the career
        // UI cost the owner a garage slot in C4 (label-match incident); an
        // empty panel has no buttons to mis-hit.
        if (!Career.active) BuildGarageTab();
        BuildShopTab();
        BuildRobotsTab();
        BuildPartsTab();
        BuildTrophiesTab();
        // LAST, so it is the final sibling and therefore draws over every
        // panel above. A modal that renders under the dock is not a modal.
        BuildSaveDialog();
    }

    /// <summary>The naming window SAVE opens when there is nothing to commit
    /// to. It reports which of the two things it is about to make - robot or
    /// draft - and why, so the distinction is a consequence of what you built
    /// rather than a quiz you have to pass before you can save.</summary>
    void BuildSaveDialog()
    {
        saveDlg = MkPanel("savedlg", canvas.transform, new Color(0.03f, 0.04f, 0.06f, 0.72f));
        Stretch(saveDlg.GetComponent<RectTransform>());
        // raycastTarget TRUE here (unlike the scrim): this one IS meant to
        // swallow every tap behind it. OverUI() short-circuits to match.
        saveDlg.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        // Sibling order alone did NOT put this above the dock - the first
        // capture showed the tab row painting over the card's bottom edge and
        // the dock left undimmed, which is the tell: something re-parents or
        // re-orders the dock after this is built. An overriding sub-canvas
        // settles it by declaration instead of by construction order, which is
        // the only version of this that stays true when the dock changes again.
        var dlgCv = saveDlg.AddComponent<Canvas>();
        dlgCv.overrideSorting = true;
        dlgCv.sortingOrder = 320;            // canvas itself is 200
        saveDlg.AddComponent<GraphicRaycaster>();   // a sub-canvas needs its own

        var card = MkPanel("savedlg_card", saveDlg.transform, new Color(0.10f, 0.12f, 0.15f, 1f));
        var crt = saveDlgCard = card.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f); crt.sizeDelta = new Vector2(560f, 250f);
        crt.anchoredPosition = Vector2.zero;

        var title = saveDlgTitle = MkText("savedlg_title", card.transform, "NAME THIS BUILD", 22, TextAnchor.MiddleLeft);
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f); trt.sizeDelta = new Vector2(-40f, 34f);
        trt.anchoredPosition = new Vector2(0f, -16f);

        saveDlgName = MkInput("savedlg_name", card.transform);
        // MkInput's placeholder says "robot name" - but this window makes a
        // draft just as often, and the note right under it may be saying so.
        var phT = saveDlgName.placeholder as Text;
        if (phT != null) phT.text = "name this build\u2026";
        var nrt = saveDlgName.GetComponent<RectTransform>();
        nrt.anchorMin = new Vector2(0f, 1f); nrt.anchorMax = new Vector2(1f, 1f);
        nrt.pivot = new Vector2(0.5f, 1f); nrt.sizeDelta = new Vector2(-40f, 40f);
        nrt.anchoredPosition = new Vector2(0f, -58f);
        saveDlgName.onValueChanged.AddListener(delegate { RefreshSaveDlgGate(); });

        saveDlgNote = MkText("savedlg_note", card.transform, "", 15, TextAnchor.UpperLeft);
        var ort = saveDlgNote.rectTransform;
        ort.anchorMin = new Vector2(0f, 1f); ort.anchorMax = new Vector2(1f, 1f);
        ort.pivot = new Vector2(0.5f, 1f); ort.sizeDelta = new Vector2(-40f, 58f);
        ort.anchoredPosition = new Vector2(0f, -106f);
        saveDlgNote.color = new Color(0.72f, 0.78f, 0.86f);

        saveDlgErr = MkText("savedlg_err", card.transform, "", 15, TextAnchor.UpperLeft);
        var ert = saveDlgErr.rectTransform;
        ert.anchorMin = new Vector2(0f, 1f); ert.anchorMax = new Vector2(1f, 1f);
        ert.pivot = new Vector2(0.5f, 1f); ert.sizeDelta = new Vector2(-40f, 36f);
        ert.anchoredPosition = new Vector2(0f, -166f);
        saveDlgErr.color = new Color(0.95f, 0.72f, 0.30f);

        var cancel = MkButton("savedlg_cancel", card.transform, "CANCEL", 16, () => CloseSaveDialog());
        var carrt = cancel.GetComponent<RectTransform>();
        carrt.anchorMin = new Vector2(1f, 0f); carrt.anchorMax = new Vector2(1f, 0f);
        carrt.pivot = new Vector2(1f, 0f); carrt.sizeDelta = new Vector2(120f, 42f);
        carrt.anchoredPosition = new Vector2(-152f, 16f);

        saveDlgOk = MkButton("savedlg_ok", card.transform, "SAVE", 16, () => ConfirmSaveDialog());
        var okrt = saveDlgOk.GetComponent<RectTransform>();
        okrt.anchorMin = new Vector2(1f, 0f); okrt.anchorMax = new Vector2(1f, 0f);
        okrt.pivot = new Vector2(1f, 0f); okrt.sizeDelta = new Vector2(132f, 42f);
        okrt.anchoredPosition = new Vector2(-20f, 16f);
        // Styled dead but LEFT LIVE, same as NEW ROBOT / NEW DRAFT:
        // interactable = false swallows the pointer, and a button that cannot
        // explain why it refused is worse than one that refuses out loud.
        var trig = saveDlgOk.gameObject.AddComponent<EventTrigger>();
        var en = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        en.callback.AddListener(delegate { if (SaveDlgNameEmpty()) SetSaveDlgErr("Type a name first."); });
        trig.triggers.Add(en);

        // OWEN 2026-08-03: "pop up a confirmation window when clicking save to
        // confirm overwrite vs save a new robot". Same card, second face - two
        // dialogs would be two places for the rules to drift apart, and this
        // one already owns the naming half of the answer.
        saveDlgOver = MkButton("savedlg_over", card.transform, "OVERWRITE", 16, () => {
            if (bm == null) return;
            Feedback(bm.SaveActive());
            CloseSaveDialog();
            RefreshRobots();
            PumpDirty(true);
        });
        var ovrt = saveDlgOver.GetComponent<RectTransform>();
        ovrt.anchorMin = new Vector2(0f, 0f); ovrt.anchorMax = new Vector2(0f, 0f);
        ovrt.pivot = new Vector2(0f, 0f); ovrt.sizeDelta = new Vector2(250f, 42f);
        ovrt.anchoredPosition = new Vector2(20f, 70f);

        saveDlgNew = MkButton("savedlg_new", card.transform, "SAVE AS NEW\u2026", 16,
                              () => OpenSaveDialog("SAVE AS \u2014 NEW COPY"));
        var nwrt = saveDlgNew.GetComponent<RectTransform>();
        nwrt.anchorMin = new Vector2(0f, 0f); nwrt.anchorMax = new Vector2(0f, 0f);
        nwrt.pivot = new Vector2(0f, 0f); nwrt.sizeDelta = new Vector2(250f, 42f);
        nwrt.anchoredPosition = new Vector2(20f, 20f);

        saveDlg.SetActive(false);
    }

    /// <summary>The confirm face: which of the two things did you mean?
    /// Only reached when something IS open - with nothing open there is
    /// nothing to overwrite and SAVE goes straight to naming.</summary>
    public void OpenSaveConfirm()
    {
        if (saveDlg == null || bm == null) return;
        string open = bm.ActiveEditName();
        if (open == null) { OpenSaveDialog(); return; }
        saveDlgConfirm = true;
        if (saveDlgTitle != null) saveDlgTitle.text = "SAVE";
        if (saveDlgNote != null)
            saveDlgNote.text = "OVERWRITE replaces " + open + " with what is on the bench now. "
                             + "SAVE AS NEW keeps " + open + " as it was and starts a copy.";
        SetSaveDlgErr("");
        if (saveDlgOver != null)
        {
            var ot = saveDlgOver.GetComponentInChildren<Text>();
            if (ot != null) ot.text = "OVERWRITE " + open;
        }
        ApplySaveDlgFace();
        saveDlg.SetActive(true);
        saveDlg.transform.SetAsLastSibling();
        CentreSaveDlgCard();
    }

    /// <summary>One place decides which controls belong to which face, so a
    /// third face later cannot leave a stray button live on the wrong one.</summary>
    void ApplySaveDlgFace()
    {
        if (saveDlgName != null) saveDlgName.gameObject.SetActive(!saveDlgConfirm);
        if (saveDlgOk != null) saveDlgOk.gameObject.SetActive(!saveDlgConfirm);
        if (saveDlgOver != null) saveDlgOver.gameObject.SetActive(saveDlgConfirm);
        if (saveDlgNew != null) saveDlgNew.gameObject.SetActive(saveDlgConfirm);
    }

    void CentreSaveDlgCard()
    {
        if (saveDlgCard == null) return;
        float dh = dockRt != null ? dockRt.sizeDelta.y : 0f;
        saveDlgCard.anchoredPosition = new Vector2(0f, dh * 0.5f);
    }

    bool SaveDlgNameEmpty()
    {
        return saveDlgName == null || saveDlgName.text == null || saveDlgName.text.Trim().Length == 0;
    }
    void SetSaveDlgErr(string e) { if (saveDlgErr != null) saveDlgErr.text = e == null ? "" : e; }
    void RefreshSaveDlgGate()
    {
        if (saveDlgOk == null) return;
        bool empty = SaveDlgNameEmpty();
        var im = saveDlgOk.GetComponent<UnityEngine.UI.Image>();
        if (im != null) im.color = empty ? GATE_DEAD : GATE_LIVE;
        var t = saveDlgOk.GetComponentInChildren<Text>();
        if (t != null) t.color = empty ? new Color(0.44f, 0.46f, 0.50f) : Color.white;
        if (!empty) SetSaveDlgErr("");
    }
    public void OpenSaveDialog() { OpenSaveDialog("NAME THIS BUILD"); }

    /// <summary>OWEN 2026-08-03: "add a SAVE AS option in the build tab to
    /// allow users save different robots."
    ///
    /// Same window as the empty-case one, reached on purpose rather than
    /// because there was nothing to commit to. The heading changes because the
    /// two are genuinely different acts: one names a build that has no object
    /// behind it, the other forks a copy and leaves the original where it was.</summary>
    public void OpenSaveDialog(string heading)
    {
        if (saveDlg == null || bm == null) return;
        saveDlgConfirm = false;
        ApplySaveDlgFace();
        if (saveDlgTitle != null) saveDlgTitle.text = heading;
        if (saveDlgName != null) saveDlgName.text = "";
        SetSaveDlgErr("");
        if (saveDlgNote != null) saveDlgNote.text = bm.SaveAsNewNote();
        saveDlg.SetActive(true);
        saveDlg.transform.SetAsLastSibling();
        // Centre in the space the player can actually SEE, not in the canvas.
        // Dead centre put the card's bottom 18.5px under the dock (measured) -
        // and relying on paint order to win that argument is the wrong fix
        // twice over: it leaves the buttons crowded against the tab row even
        // when it works, and the dock's height is per-tab, so the number to
        // beat changes underneath you.
        if (saveDlgCard != null)
        {
            float dh = dockRt != null ? dockRt.sizeDelta.y : 0f;
            saveDlgCard.anchoredPosition = new Vector2(0f, dh * 0.5f);
        }
        RefreshSaveDlgGate();
    }
    public void CloseSaveDialog() { if (saveDlg != null) saveDlg.SetActive(false); }
    public bool SaveDialogOpen { get { return saveDlg != null && saveDlg.activeSelf; } }
    void ConfirmSaveDialog()
    {
        if (bm == null) return;
        string err = bm.SaveAsNew(saveDlgName != null ? saveDlgName.text : "");
        // A refusal keeps the window OPEN with the reason in it. Closing on
        // failure would drop the name they typed and hide why it did not take.
        if (err != null) { SetSaveDlgErr(err); SfxSynth.Deny(); return; }
        CloseSaveDialog();
        RefreshRobots();
        PumpDirty(true);
    }

    void BuildBuildTab()
    {
        // material chip row (top)
        var matRow = MkPanel("mats", buildPanel.transform, new Color(0f,0f,0f,0f));
        var mr = matRow.GetComponent<RectTransform>();
        mr.anchorMin = new Vector2(0f,1f); mr.anchorMax = new Vector2(1f,1f); mr.pivot = new Vector2(0.5f,1f);
        mr.sizeDelta = new Vector2(0f, mr.sizeDelta.y);   // R1 critic: default 100px sizeDelta survived the stretch anchors
        mr.sizeDelta = new Vector2(0f,40f); mr.anchoredPosition = Vector2.zero;
        var mh = matRow.AddComponent<HorizontalLayoutGroup>(); mh.spacing = 4f; mh.childForceExpandWidth = true; mh.childForceExpandHeight = true;
        foreach (var key in MatDB.Order)
        {
            string k = key;
            var mb = MkButton("mat_"+k, matRow.transform, MatDB.Get(k).name, 15, () => PickMat(k));
            matButtons.Add(mb); matKeys.Add(k);
        }
        // action row (bottom)
        var actRow = MkPanel("acts", buildPanel.transform, new Color(0f,0f,0f,0f));
        var ar = actRow.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(0f,0f); ar.anchorMax = new Vector2(1f,0f); ar.pivot = new Vector2(0.5f,0f);
        ar.sizeDelta = new Vector2(0f,42f); ar.anchoredPosition = Vector2.zero;
        var ah = actRow.AddComponent<HorizontalLayoutGroup>(); ah.spacing = 6f; ah.childForceExpandWidth = true; ah.childForceExpandHeight = true;
        MkButton("rot", actRow.transform, "ROTATE", 17, () => Phase0Input.DebugRotate());
        MkButton("undo", actRow.transform, "UNDO", 17, () => Phase0Input.DebugUndo());
        removeBtn = MkButton("del", actRow.transform, "REMOVE", 17, () => { removeArmed = !removeArmed; if (removeArmed && bm != null && bm.HasSelection) bm.SelectPart(bm.SelectedPart); RefreshRemoveBtn(); });
        MkButton("desel", actRow.transform, "DONE", 17, () => { if (bm != null && bm.HasSelection) bm.SelectPart(bm.SelectedPart); Phase0Input.debugPointer = false; RefreshHighlight(); });
        // OWEN 2026-08-02: SAVE belongs where the work happens. DONE, one slot
        // to the left, only puts down the held part - it was never a commit,
        // and its name invited exactly that reading. The commit now sits beside
        // it instead of two tab-taps away on ROBOTS.
        saveBtn = MkButton("bsave", actRow.transform, "SAVE", 17, () => {
            if (bm == null) return;
            // OWEN 2026-08-02: "allow user to save directly under BUILD tab,
            // and pop up a window to let user name the saved robot." With
            // nothing open SAVE used to refuse and point at another tab -
            // so the very first build a player makes was the one SAVE would
            // not take. Now it asks for the name itself.
            // OWEN 2026-08-03: "pop up a confirmation window when clicking
            // save to confirm overwrite vs save a new robot." Nothing open ->
            // there is nothing to overwrite, so that case still goes straight
            // to naming rather than asking a question with one answer.
            if (bm.NothingOpen) { OpenSaveDialog(); return; }
            OpenSaveConfirm();
        });
        // parts scroll (middle)
        var scrollGO = MkPanel("partscroll", buildPanel.transform, new Color(0f,0f,0f,0.0f));
        var sr = scrollGO.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0f,0f); sr.anchorMax = new Vector2(1f,1f);
        sr.offsetMin = new Vector2(0f,46f); sr.offsetMax = new Vector2(0f,-44f);
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = true; scroll.vertical = false;
        var viewport = MkPanel("viewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        vp.offsetMin = new Vector2(0f, 10f);      // leave the scrollbar its lane
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("content", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(0f,1f); crt.pivot = new Vector2(0f,1f); crt.anchoredPosition = Vector2.zero;
        // R1 fix 1 (CRITICAL). The palette was a SINGLE horizontal row: nine
        // of the 19 parts fitted, the row ended flush with the screen edge,
        // and there was no scrollbar, no fade and no half-tile to say
        // otherwise. Index 9 - Wheel - was the first one hidden, so EVERY
        // wheel and EVERY weapon lived off-screen. A new player builds from
        // what they can see, presses FIGHT and gets "Needs at least 1 wheel."
        // with nothing on screen suggesting a horizontal swipe.
        //
        // Two rows puts all 19 in roughly one screen-width (10 columns), and
        // the tiles were taller than their two lines of text needed anyway,
        // so this costs no extra dock height beyond DockH(0)'s 272.
        var grid = content.AddComponent<GridLayoutGroup>();
        // 128 was measured wrong the first time: at 2640x1656 the scaler is
        // ~2.178, so the canvas is only ~1212 units wide, the viewport ~1200,
        // and ten 128-wide columns need 1342. Wheel (index 9, top of the last
        // column) stayed off-screen - the exact part the palette had to
        // surface. 112 makes the whole 19-part palette fit with ~18 units to
        // spare, verified by screenshot, not by arithmetic alone.
        grid.cellSize = new Vector2(112f, 50f);
        grid.spacing = new Vector2(6f, 4f);
        grid.padding = new RectOffset(4,4,4,4);
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        grid.constraintCount = 2;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;   // top shelf 0-9, bottom shelf 10-18
        var csf = content.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        // ...and whatever overflow is left is now ANNOUNCED, twice: a
        // permanent scrollbar under the tiles and a chevron at the edge the
        // content runs off. An invisible scroll is the same as no scroll.
        var barGO = MkPanel("partbar", scrollGO.transform, new Color(0.10f,0.11f,0.14f,0.9f));
        var brt2 = barGO.GetComponent<RectTransform>();
        brt2.anchorMin = new Vector2(0f,0f); brt2.anchorMax = new Vector2(1f,0f); brt2.pivot = new Vector2(0.5f,0f);
        brt2.sizeDelta = new Vector2(0f, 8f); brt2.anchoredPosition = Vector2.zero;
        var handleGO = MkPanel("handle", barGO.transform, new Color(0.35f,0.62f,0.85f,1f));
        var hrt2 = handleGO.GetComponent<RectTransform>(); Stretch(hrt2);
        var sbar = barGO.AddComponent<Scrollbar>();
        sbar.direction = Scrollbar.Direction.LeftToRight;
        sbar.handleRect = hrt2;
        sbar.targetGraphic = handleGO.GetComponent<Image>();
        scroll.horizontalScrollbar = sbar;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var edge = MkPanel("scrollhint", scrollGO.transform, new Color(0.03f,0.04f,0.06f,0.60f));
        edge.GetComponent<Image>().raycastTarget = false;
        var ert2 = edge.GetComponent<RectTransform>();
        ert2.anchorMin = new Vector2(1f,0f); ert2.anchorMax = new Vector2(1f,1f); ert2.pivot = new Vector2(1f,0.5f);
        ert2.sizeDelta = new Vector2(26f, -10f); ert2.anchoredPosition = new Vector2(0f, 5f);
        var chev = MkText("chev", edge.transform, "\u203a", 26, TextAnchor.MiddleCenter);
        Stretch(chev.rectTransform);
        chev.color = new Color(0.55f, 0.75f, 0.95f, 1f);
        partScroll = scroll; partEdge = edge; edge.SetActive(false);
        partButtons.Clear();
        int n = bm != null ? bm.PaletteCount : 0;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            string lab = PartTileText(i);
            var pb = MkButton("part_"+i, content.transform, lab, 13, () => { bm.SelectPart(idx); RefreshHighlight(); });
            // R4: MkText defaults to HorizontalWrapMode.Overflow, so the longest
            // tiles ("Gyro stabilizer / 47 kg Aluminum / 1 free") ran straight
            // over their neighbour instead of being clipped or wrapped - two
            // tiles' worth of text sharing one tile's pixels. Wrap plus best-fit
            // shrinks only the two or three tiles that actually need it and
            // leaves the rest at 13, and the inset stops a wrapped line from
            // touching the tile edge.
            var pt = pb.GetComponentInChildren<Text>();
            if (pt != null)
            {
                pt.horizontalOverflow = HorizontalWrapMode.Wrap;
                pt.resizeTextForBestFit = true;
                pt.resizeTextMinSize = 8; pt.resizeTextMaxSize = 13;
                pt.rectTransform.offsetMin = new Vector2(4f, 2f);
                pt.rectTransform.offsetMax = new Vector2(-4f, -2f);
            }
            partButtons.Add(pb);
        }
        RefreshMats(); RefreshPartLabels();
    }

    void PickMat(string k)
    {
        if (Progression.MatUnlocked(k)) { if (bm != null) bm.ActiveMatKey = k; }
        else if (Progression.TryBuyMat(k)) { if (bm != null) bm.ActiveMatKey = k; }
        RefreshHighlight(); RefreshMats(); RefreshPartLabels();
    }

    void RefreshHighlight()
    {
        for (int i = 0; i < partButtons.Count; i++)
        {
            var img = partButtons[i].GetComponent<Image>();
            bool sel = bm != null && bm.SelectedPart == i;
            int stock = bm != null ? bm.CareerRemaining(i) : -1;
            // R5 (critic finding 7): out of stock used to REPLACE the category
            // tint with neutral grey, so a weapon you own none of was pixel
            // identical to a structural part - the shelf lost the one signal
            // R1 added to answer "where are the weapons" exactly when the
            // player most needs it. Darken the hue, never delete it.
            var tint = CatTint(i);
            img.color = sel ? new Color(0.20f,0.45f,0.65f,1f)
                      : stock == 0 ? new Color(tint.r * 0.45f, tint.g * 0.45f, tint.b * 0.45f, 0.96f)
                      : tint;
            // ...and say "you have none" in the type colour too, instead of
            // rendering "0 free" in the same full white as "12 free".
            var tt = partButtons[i].GetComponentInChildren<Text>();
            if (tt != null) tt.color = (stock == 0 && !sel) ? new Color(0.45f,0.45f,0.50f,1f) : Color.white;
        }
    }

    /// <summary>R1 fix 1, second half: "where are the weapons" needs an
    /// answer that survives the tiles scrolling. A per-category base tint on
    /// the tile makes the shelf legible at a glance without spending the two
    /// lines of tile text on a category name.</summary>
    Color CatTint(int i)
    {
        switch (bm != null ? bm.PartCategory(i) : "")
        {
            case "Mobility": return new Color(0.13f, 0.25f, 0.21f, 0.96f);
            case "Weapon":   return new Color(0.30f, 0.15f, 0.15f, 0.96f);
            case "Power":    return new Color(0.26f, 0.22f, 0.12f, 0.96f);
            case "Control":  return new Color(0.17f, 0.16f, 0.28f, 0.96f);
            default:         return new Color(0.16f, 0.18f, 0.22f, 0.96f);
        }
    }

    // ---- C1: stock badges. Remaining = owned minus used, straight from
    // BuilderManager's derived accounting; -1 means unlimited (career off).
    int careerSeenPlaced = -1;
    string careerSeenMat = "";
    bool careerSeenActive;
    string PartTileText(int i)
    {
        // R2 (critic finding 2): a pinned part's material is not on the chip
        // row, so the chips visibly do not apply to it. Saying so on the tile is
        // what stops "Wheel ×0" reading as a bug when the Aluminum chip is lit.
        string pin = bm.PartPinnedMat(i);
        string t = bm.PartLabel(i) + "\n" + bm.PartMass(i) + " kg";
        if (pin != null) t += " " + MatDB.Get(pin).name;
        // R4 (critic finding 5): this badge is FREE STOCK - owned minus what
        // the whole stable is already holding - while the SHOP two taps away
        // says `own N` for the total. Two different numbers in the same bare
        // ownership language read as a bug, so this one now says what it is.
        int stock = bm.CareerRemaining(i);
        if (stock >= 0) t += "  " + Mathf.Max(0, stock) + " free";
        return t;
    }

    void RefreshPartLabels()
    {
        if (bm == null) return;
        for (int i = 0; i < partButtons.Count && i < bm.PaletteCount; i++)
        {
            var t = partButtons[i].GetComponentInChildren<Text>();
            if (t != null) t.text = PartTileText(i);
        }
    }

    void RefreshMats()
    {
        if (bm == null) return;
        string cur = MatDB.Canon(bm.ActiveMatKey);
        for (int i = 0; i < matButtons.Count; i++)
        {
            bool active = MatDB.Canon(matKeys[i]) == cur;
            bool unlocked = Progression.MatUnlocked(matKeys[i]);
            var img = matButtons[i].GetComponent<Image>();
            img.color = active ? new Color(0.20f,0.45f,0.65f,1f)
                      : unlocked ? new Color(0.16f,0.18f,0.22f,0.96f)
                      : new Color(0.10f,0.10f,0.12f,0.96f);
            var t = matButtons[i].GetComponentInChildren<Text>();
            if (t != null) t.color = unlocked ? Color.white : new Color(0.55f,0.55f,0.6f,1f);
        }
    }

    void BuildFightTab()
    {
        // R1 fix 2 follow-up: childForceExpandHeight split the dock's new
        // height EQUALLY between the header, TEST DRIVE and the contest board,
        // so a 470-unit dock produced a ~180-unit TEST DRIVE button and a
        // still-clipped league list. Fixed rows keep their own height and the
        // board (flexibleHeight = 1) takes everything left over.
        var v = fightPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 8f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        // Critic round 2: a mobile player had no view of scrap, record or the
        // next ladder opponent. One live header line carries all three.
        fightInfo = MkText("fightinfo", fightPanel.transform, "", 16, TextAnchor.MiddleCenter);
        fightInfo.color = new Color(0.80f, 0.88f, 1f);
        // R3 critic: TEST DRIVE's fixed height squeezed this two-line header
        // until it clipped - reserve its own two lines.
        var fil = fightInfo.gameObject.AddComponent<LayoutElement>(); fil.minHeight = 44f; fil.preferredHeight = 44f;
        ladderBtn = MkButton("ladder", fightPanel.transform, "LADDER — next rung", 19, () => { if (bm != null) bm.StartLadderFight(); });
        // with childForceExpandHeight off, a plain Button reports no preferred
        // height at all and would collapse to nothing in the sandbox.
        var lbl2 = ladderBtn.gameObject.AddComponent<LayoutElement>(); lbl2.minHeight = 40f; lbl2.preferredHeight = 40f;
        exhibBtn = MkButton("exhib", fightPanel.transform, "EXHIBITION FIGHT", 19, () => { if (bm != null) { Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1; bm.StartFight(); } });
        var ebl2 = exhibBtn.gameObject.AddComponent<LayoutElement>(); ebl2.minHeight = 40f; ebl2.preferredHeight = 40f;
        // R2 (critic finding 8): TEST DRIVE was a full-width bar and the single
        // brightest element on the campaign screen - a sandbox action outranking
        // the mode's core loop. It is now a small right-aligned secondary chip
        // in its own row, so the league board is the loudest thing on LEAGUE.
        // Three attempts at doing this with a layout row produced a chip whose
        // rect had not settled at capture time - once a 132x300 grey slab, once
        // a stray "DRIVE" bleeding off the LEFT screen edge while the chip drew
        // on the right. A layout group cannot leave a stale rect if it never
        // manages the object at all: ignoreLayout takes the chip out of the
        // vertical flow and it is anchored to the panel's top-right corner
        // directly. Deterministic - and it is where the critic asked for it,
        // beside the CAREER header rather than above the league board.
        var tdb = MkButton("test", fightPanel.transform, "TEST DRIVE", 14, () => { if (bm != null) bm.StartTest(); });
        var tdl = tdb.gameObject.AddComponent<LayoutElement>();
        tdl.ignoreLayout = true;
        var trt = tdb.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(1f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(1f, 1f);
        trt.sizeDelta = new Vector2(132f, 30f);
        trt.anchoredPosition = new Vector2(-8f, -7f);
        tdb.GetComponent<Image>().color = new Color(0.13f, 0.15f, 0.19f, 0.96f);
        var tdt = tdb.GetComponentInChildren<Text>();
        if (tdt != null) tdt.color = new Color(0.68f, 0.72f, 0.79f, 1f);
        // C3: the career league board replaces LADDER/EXHIBITION in career mode
        careerBoard = MkPanel("careerboard", fightPanel.transform, new Color(0f,0f,0f,0f));
        var cbl = careerBoard.AddComponent<LayoutElement>(); cbl.flexibleHeight = 1f; cbl.minHeight = 90f;
        var cbs = careerBoard.AddComponent<ScrollRect>(); cbs.horizontal = false; cbs.vertical = true;
        var cbvp = MkPanel("cbviewport", careerBoard.transform, new Color(0f,0f,0f,0.15f));
        var cbvprt = cbvp.GetComponent<RectTransform>(); Stretch(cbvprt);
        cbvp.AddComponent<Mask>().showMaskGraphic = true;
        var cbc = MkPanel("cbcontent", cbvp.transform, new Color(0f,0f,0f,0f));
        var cbcrt = cbc.GetComponent<RectTransform>();
        cbcrt.anchorMin = new Vector2(0f,1f); cbcrt.anchorMax = new Vector2(1f,1f); cbcrt.pivot = new Vector2(0.5f,1f); cbcrt.anchoredPosition = Vector2.zero;
        cbcrt.sizeDelta = new Vector2(0f, 0f);   // R1 critic: rows clipped both screen edges
        var cbclg = cbc.AddComponent<VerticalLayoutGroup>(); cbclg.spacing = 4f; cbclg.childForceExpandWidth = true; cbclg.childForceExpandHeight = false; cbclg.padding = new RectOffset(4,4,4,4);
        var cbcsf = cbc.AddComponent<ContentSizeFitter>(); cbcsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cbs.viewport = cbvprt; cbs.content = cbcrt;
        AddListOverflow(careerBoard, cbs, cbvprt);   // R2 finding 8: LEAGUE
        careerBoardContent = cbc.transform;
        RefreshFightTab();
        RefreshFightInfo();
    }

    /// <summary>Scrap balance, win-loss record and the next ladder opponent,
    /// shown at the top of the FIGHT tab. Refreshed after every match.</summary>
    void RefreshFightInfo()
    {
        if (fightInfo == null) return;
        if (Career.active)
        {
            fightInfo.text = string.Format("CAREER \u00b7 SCRAP {0} \u00b7 RECORD {1}-{2}\n{3}",
                Career.Data.scrap, Career.Data.fightWins,
                Mathf.Max(0, Career.Data.fights - Career.Data.fightWins),
                string.IsNullOrEmpty(Career.lastResultLine) ? "pick a contest below \u00b7 SCOUT shows the opponent" : Career.lastResultLine);
            return;
        }
        var d = Progression.Data;
        var r = Progression.CurrentRung();
        string next = r != null
            ? string.Format("NEXT RUNG: {0}   \u00b7   win +{1} scrap", r.label, r.reward)
            : "LADDER COMPLETE \u2014 title defense pays out again";
        fightInfo.text = string.Format("SCRAP {0}   \u00b7   RECORD {1}-{2}\n{3}",
            d.scrap, d.fightsWon, Mathf.Max(0, d.fightsFought - d.fightsWon), next);
    }

    /// <summary>C3: sandbox shows LADDER/EXHIBITION; career shows the league
    /// board. TEST DRIVE stays in both.</summary>
    void RefreshFightTab()
    {
        bool car = Career.active;
        if (ladderBtn != null) ladderBtn.gameObject.SetActive(!car);
        if (exhibBtn != null) exhibBtn.gameObject.SetActive(!car);
        if (careerBoard != null) careerBoard.SetActive(car);
        if (car) RefreshCareerBoard();
    }

    void RefreshCareerBoard()
    {
        if (careerBoardContent == null || bm == null) return;
        fightGates.Clear();   // the rows below are about to be destroyed
        for (int i = careerBoardContent.childCount - 1; i >= 0; i--)
            Destroy(careerBoardContent.GetChild(i).gameObject);
        for (int li = 0; li < CareerDB.Leagues.Length; li++)
        {
            var lg = CareerDB.Leagues[li];
            bool open = Career.LeagueUnlocked(li);
            var hdr = MkText("lg_" + li, careerBoardContent,
                string.Format("{0}{1} \u00b7 {2} \u00b7 cap {3} kg \u00b7 {4}",
                    open ? "" : "[locked] ", lg.name, lg.arenaName, Mathf.RoundToInt(lg.weightCap),
                    ArenaHazards.Summary(lg.arenaId)),
                14, TextAnchor.MiddleLeft);
            hdr.color = open ? new Color(0.80f,0.88f,1f) : new Color(0.55f,0.55f,0.60f);
            hdr.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
            if (!open) continue;
            for (int ci = 0; ci < lg.contests.Length; ci++)
            {
                int lidx = li, cidx = ci;
                var c = lg.contests[ci];
                bool done = Career.Data.doneContests.Contains(c.id);
                var row = MkPanel("contest_" + c.id, careerBoardContent, new Color(0.10f,0.11f,0.14f,1f));
                var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 34f; rle.preferredHeight = 34f;
                var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(6,4,2,2);
                var lbl = MkText("lbl", row.transform,
                    string.Format("{0}{1} ({2}) \u00b7 {3} scrap{4}{5}",
                        done ? "\u2713 " : "", EnemyRoster.Find(c.oppId).label, c.tier,
                        done ? Mathf.RoundToInt(c.purse * 0.4f) : c.purse,
                        done ? " (re-entry)" : "",
                        c.entryFee > 0 ? " \u00b7 fee " + c.entryFee + " scrap" : ""),
                    14, TextAnchor.MiddleLeft);
                lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                // R5 (critic finding 8): the header says "SCOUT shows the
                // opponent", the status bar says "SCOUT first, then FIGHT", and
                // the row used to lay the buttons out FIGHT | SCOUT in matching
                // grey. Free and reversible reads first and primary; the one
                // that spends an entry fee reads second and committed.
                var scb = MkButton("cscout_" + c.id, row.transform, "SCOUT", 14, () => { if (bm != null) bm.StartScout(lidx, cidx); });
                scb.gameObject.AddComponent<LayoutElement>().minWidth = 72f;
                scb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
                var fb = MkButton("cfight_" + c.id, row.transform, "FIGHT", 14, () => { if (bm != null) bm.StartCareerFight(lidx, cidx); });
                fb.gameObject.AddComponent<LayoutElement>().minWidth = 72f;
                fb.GetComponent<Image>().color = FIGHT_OK;
                fightGates.Add(new FightGate { btn = fb, img = fb.GetComponent<Image>(),
                                               lbl = lbl, baseLabel = lbl.text, li = lidx, ci = cidx });
            }
        }
    }

    void BuildGarageTab()
    {
        var v = garagePanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 6f; v.childForceExpandWidth = true; v.childForceExpandHeight = true; v.padding = new RectOffset(4,4,4,4);
        var saveRow = MkPanel("saverow", garagePanel.transform, new Color(0f,0f,0f,0f));
        var sh = saveRow.AddComponent<HorizontalLayoutGroup>(); sh.spacing = 6f; sh.childForceExpandWidth = true; sh.childForceExpandHeight = true;
        for (int i = 0; i < 3; i++) { int gi = i; MkButton("save"+i, saveRow.transform, "SAVE "+(char)('A'+i), 17, () => { if (bm != null) { Progression.Data.garage[gi].snapshot = bm.SnapshotString(); Progression.Save(); } }); }
        var loadRow = MkPanel("loadrow", garagePanel.transform, new Color(0f,0f,0f,0f));
        var lh = loadRow.AddComponent<HorizontalLayoutGroup>(); lh.spacing = 6f; lh.childForceExpandWidth = true; lh.childForceExpandHeight = true;
        for (int i = 0; i < 3; i++) { int gi = i; MkButton("load"+i, loadRow.transform, "LOAD "+(char)('A'+i), 17, () => { if (bm != null) { var s = Progression.Data.garage[gi].snapshot; if (!string.IsNullOrEmpty(s)) bm.LoadSnapshot(s); } }); }
    }

    /// <summary>R1 fix 2: dock height, per tab.
    ///
    /// It used to be a flat 210 canvas units everywhere. At 2640x1656 the
    /// scaler lands on ~2.18, so 210 is ~458 px of dock and, after the 48-unit
    /// tab strip, ~340 px of list - three of 19 shop rows, three of 11 parts
    /// rows, one of 12 contests - while the top ~73% of every one of the five
    /// tabs rendered the SAME empty build-room floor. BUILD really does need
    /// to see the robot it is editing; LEAGUE / ROBOTS / SHOP / PARTS do not.
    ///
    /// TRAP, and the reason this is a function and not two literals: the dock
    /// height is also the hit-test boundary in OverUI(). When those two
    /// numbers disagree, taps in the newly exposed strip fall THROUGH the
    /// panel and place parts on the robot behind it. OverUI() now measures
    /// dockRt, so there is exactly one source of truth.</summary>
    /// <summary>R6, owen 2026-08-04: "Looks like the menu partially blocks the
    /// building area."
    ///
    /// Measured on his landscape iPhone: dock 488 px of 1191, 41.7% of the
    /// screen. The camera reframe stops the dock covering the ROBOT, but it
    /// cannot give back the space - a phone in landscape is about 415 pt tall
    /// in total, and no honest layout fits a tab strip, a material row, two
    /// palette rows and an action row into that while leaving room to work.
    ///
    /// So the dock closes. Collapsed keeps the tab strip - navigation is not
    /// the thing that was in the way - and drops the content panel, taking the
    /// dock from 41.7% to about 14% including the handle.
    ///
    /// The DEFAULT is measured, not assumed: if the open dock would cover more
    /// than a third of the screen it starts closed. A phone starts closed, an
    /// iPad starts open, and a device I have never seen decides for itself
    /// rather than matching whichever one I happened to hard-code.</summary>
    const float HANDLE_H = 44f;
    const float COLLAPSED_H = 48f;      // the tab strip, and nothing else
    bool dockOpen = true;
    UnityEngine.UI.Button dockHandle;
    RectTransform handleRt;

    public void ToggleDock() { SetDockOpen(!dockOpen); }

    /// <summary>Can this screen afford to show the dock by default?
    ///
    /// Asked in INCHES, off the device, before anything is laid out. Two
    /// earlier versions of this rule got it wrong in ways worth recording:
    ///
    ///   - "canvas height in units" needed a laid-out canvas, and every
    ///     ApplyDockH call during Awake happens BEFORE the first layout pass.
    ///     The rule locked in "open" from a transient rect and the phone came
    ///     up at 41% anyway. It reported no error; the dock was simply still
    ///     there. Measured dockOpen=True three times before I stopped trusting
    ///     the reasoning and printed the number.
    ///   - "points", via dpi/163, invents a scale factor Apple does not use.
    ///     It happens to separate phones from tablets, but it is a made-up
    ///     unit dressed up as a real one.
    ///
    /// Inches are the actual question. owen's iPhone in landscape is 2.5 in
    /// tall; an iPad is about 7.8. Nothing between 4 and 7 inches tall exists
    /// in landscape, so the threshold is not near anything real.</summary>
    static bool ScreenIsTallEnoughForAnOpenDock()
    {
        float dpi = UnityEngine.Device.Screen.dpi;
        if (dpi < 1f) return true;                       // unknown: behave as before
        return UnityEngine.Device.Screen.height / dpi >= 4f;
    }

    /// <summary>Open or close the dock. Public so the smoke suite can pin it
    /// rather than inherit whatever this device decided - a suite whose
    /// assertions depend on a screen-size heuristic is a suite that passes or
    /// fails by which simulator was last selected.</summary>
    public void SetDockOpen(bool v)
    {
        dockOpen = v;
        // One path either way: ShowTab now honours dockOpen, so it deactivates
        // everything when closed and restores exactly the right panel when
        // open. Two branches here is how the two ideas drift apart.
        ShowTab(tab);
    }

    /// <summary>True when the content panel is showing. Read by the suite.</summary>
    public bool DockOpen { get { return dockOpen; } }

    /// <summary>Test hooks. DockHeightForTest reads the LIVE rect rather than
    /// DockH(tab) - the two disagreeing is the bug worth catching, not a
    /// detail.</summary>
    public float DockHeightForTest { get { return dockRt != null ? dockRt.sizeDelta.y : 0f; } }

    /// <summary>Does OverUI actually claim the handle? Asked through the real
    /// hit test, at the handle's real centre, so it stays true if the handle
    /// moves.</summary>
    public bool HandleIsUiForTest
    {
        get
        {
            if (handleRt == null) return false;
            var c = new Vector3[4];
            handleRt.GetWorldCorners(c);
            return OverUI(new Vector2((c[0].x + c[2].x) * 0.5f, (c[0].y + c[1].y) * 0.5f));
        }
    }

    float DockH(int t)
    {
        if (!dockOpen) return COLLAPSED_H;
        if (t == 0) return 272f;                  // BUILD: two palette rows fit
        if (!Career.active) return 210f;
        // R5 (critic finding 4, filed in R2/R3/R4/R5): a content-sized dock left
        // 37-53% of LEAGUE / ROBOTS / SHOP / PARTS as dimmed build room with the
        // yellow platform ring showing through - it read as a panel that failed
        // to draw. The four list tabs now take EVERY unit from the bottom of the
        // top bar stack to the bottom of the screen. Measured, not a literal, so
        // it is right at any aspect ratio.
        float ch = 0f;
        if (canvas != null) { var crt = canvas.GetComponent<RectTransform>(); if (crt != null) ch = crt.rect.height; }
        if (ch < 100f) return 470f;               // canvas not laid out yet
        float top = BAR_H
                  + ((msgBar != null && msgBar.activeSelf) ? MSG_H : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TIP_H : 0f);
        return Mathf.Clamp(ch - top - 2f, 470f, ch);
    }

    /// <summary>R5: the dock height depends on which of the top bars are live,
    /// and those toggle at runtime - re-apply whenever that changes so the dock
    /// never overlaps the tip row and never leaves a gap under it.</summary>
    void ApplyDockH()
    {
        if (dockRt == null) return;
        float dh = DockH(tab);
        if (Mathf.Abs(dockRt.sizeDelta.y - dh) > 0.5f) dockRt.sizeDelta = new Vector2(0f, dh);
        if (scrim != null) scrim.SetActive(dh > 300f);

        if (handleRt != null)
        {
            handleRt.anchoredPosition = new Vector2(0f, dh);
            var ht = dockHandle.GetComponentInChildren<UnityEngine.UI.Text>();
            if (ht != null)
                ht.text = dockOpen ? "\u25bc  HIDE PANEL" : "\u25b2  SHOW PANEL";
        }

        PublishCover(dh + (handleRt != null ? HANDLE_H : 0f));
    }

    /// <summary>What fraction of the screen this UI is sitting on top of, at the
    /// bottom and at the top. 0..1 each.
    ///
    /// OWEN 2026-08-04, on a landscape iPhone: "Looks like the menu partially
    /// blocks the building area."
    ///
    /// It did, and measurement said worse than it looked: dock 488 px of a
    /// 1191-px screen, 41%. But shrinking the dock alone would not have fixed
    /// it, because the build camera framed the robot against the WHOLE
    /// viewport and therefore aimed it squarely into the covered half. Any dock
    /// at all would have clipped the machine.
    ///
    /// So the UI publishes what it covers and the camera reads it. Measured
    /// from the live rects rather than from the constants that produced them -
    /// the numbers drift (this dock is already per-tab, and now collapsible),
    /// and a camera working from a stale literal is the same defect one layer
    /// down.</summary>
    public static float coverBottom, coverTop;

    void PublishCover(float dockH)
    {
        float ch = 0f;
        if (canvas != null) { var crt = canvas.GetComponent<RectTransform>(); if (crt != null) ch = crt.rect.height; }
        if (ch < 100f) { coverBottom = coverTop = 0f; return; }
        float top = BAR_H
                  + ((msgBar != null && msgBar.activeSelf) ? MSG_H : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TIP_H : 0f);
        coverBottom = Mathf.Clamp01(dockH / ch);
        coverTop = Mathf.Clamp01(top / ch);
    }

    /// <summary>R1 fix 6: five screens shared one tab strip in which no tab
    /// ever looked selected - the cheapest orientation cue in the UI, absent.
    /// Selected = the same blue the material chips and the held part already
    /// use, plus bold; unselected steps back a notch in both value and
    /// contrast so the strip reads as one control, not five buttons.</summary>
    void RefreshTabHighlight()
    {
        for (int i = 0; i < tabBtns.Count; i++)
        {
            bool sel = i == tab;
            var img = tabBtns[i].GetComponent<Image>();
            if (img != null)
                img.color = sel ? new Color(0.20f, 0.45f, 0.65f, 1f)
                                : new Color(0.11f, 0.12f, 0.15f, 0.96f);
            var t = tabBtns[i].GetComponentInChildren<Text>();
            if (t != null)
            {
                t.fontStyle = sel ? FontStyle.Bold : FontStyle.Normal;
                t.color = sel ? Color.white : new Color(0.62f, 0.67f, 0.76f, 1f);
            }
        }
    }

    /// <summary>C2: which tabs exist follows the career switch - 3 tabs in
    /// the sandbox, 4 with SHOP in career mode.</summary>
    void LayoutTabs()
    {
        // MEDALS: TROPHIES is career-only, like SHOP and PARTS - 6 tabs in
        // career mode, still 3 in the sandbox.
        int n = Career.active ? 6 : 3;
        for (int i = 0; i < tabBtns.Count; i++)
        {
            bool show = i < n;
            tabBtns[i].gameObject.SetActive(show);
            if (!show) continue;
            var rt = tabBtns[i].GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(i / (float)n, 1f);
            rt.anchorMax = new Vector2((i + 1) / (float)n, 1f);
        }
        // C4: the workshop renames FIGHT/GARAGE in career mode
        var tl1 = tabBtns.Count > 1 ? tabBtns[1].GetComponentInChildren<Text>() : null;
        if (tl1 != null) tl1.text = Career.active ? "LEAGUE" : "FIGHT";
        var tl2 = tabBtns.Count > 2 ? tabBtns[2].GetComponentInChildren<Text>() : null;
        if (tl2 != null) tl2.text = Career.active ? "ROBOTS" : "GARAGE";
        RefreshTabHighlight();   // renaming a tab resets nothing about selection
    }

    void BuildShopTab()
    {
        var v = shopPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        shopHeader = MkText("shopheader", shopPanel.transform, "", 15, TextAnchor.MiddleLeft);
        var hle = shopHeader.gameObject.AddComponent<LayoutElement>(); hle.minHeight = 20f; hle.preferredHeight = 20f;
        var scrollGO = MkPanel("shopscroll", shopPanel.transform, new Color(0f,0f,0f,0f));
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 96f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("shopviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("shopcontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f); crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);   // R1 critic: rows clipped both screen edges
        var clg = content.AddComponent<VerticalLayoutGroup>(); clg.spacing = 4f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false; clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: SHOP
        shopParts.Clear(); shopMats.Clear();
        int n = bm != null ? bm.PaletteCount : 0;
        for (int i = 1; i < n; i++)   // 0 = core: not for sale
        {
            int idx = i;
            // The part HEADER is itself the disclosure control: tapping it opens
            // that part's materials. One accordion, so the 18-part shelf stays
            // scannable and the six prices you are comparing are on one screen.
            var head = MkPanel("shophead_" + i, content.transform, new Color(0.13f,0.16f,0.21f,0.95f));
            var hle2 = head.AddComponent<LayoutElement>(); hle2.minHeight = 36f; hle2.preferredHeight = 36f;
            var hbtn = head.AddComponent<Button>();
            hbtn.targetGraphic = head.GetComponent<Image>();
            hbtn.onClick.AddListener(() => ToggleShopPart(idx));
            var htxt = MkText("lbl", head.transform, "", 14, TextAnchor.MiddleLeft);
            Stretch(htxt.rectTransform);
            htxt.rectTransform.offsetMin = new Vector2(10f, 0f); htxt.rectTransform.offsetMax = new Vector2(-10f, 0f);
            var pr = new ShopPartRow(); pr.part = i; pr.go = head; pr.lbl = htxt; pr.open = (i == 1);
            shopParts.Add(pr);

            foreach (var mk in bm.PartLegalMats(i))
            {
                string mat = mk;
                var row = MkPanel("shopmat_" + i + "_" + mk, content.transform, new Color(0.07f,0.08f,0.105f,1f));
                var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 32f; rle.preferredHeight = 32f;
                var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(28,4,2,2);
                var lbl = MkText("lbl", row.transform, "", 13, TextAnchor.MiddleLeft);
                // shop1 capture: a FLEXIBLE label flung the buttons to the far
                // right of a ~1200-unit row, and BUY moved horizontally from row
                // to row as SELL / REWORK appeared and vanished. A fixed label
                // column left-packs the group, so the three actions sit in the
                // same place on every line, beside the price they act on.
                var mle = lbl.gameObject.AddComponent<LayoutElement>();
                mle.preferredWidth = 400f; mle.minWidth = 400f; mle.flexibleWidth = 0f;
                var mr = new ShopMatRow(); mr.part = i; mr.mat = mat; mr.go = row; mr.lbl = lbl;
                // OWEN 2026-08-02: named by CONTENT, not position. These were
                // "buy"/"sell"/"swap" on every row, so a harness could only
                // reach the first one; CareerSmoke's shop section had been dead
                // since the per-material catalog landed and nobody noticed
                // because the names it used ("shopbuy_1") simply found nothing.
                // partId rather than palette index, so a reordered palette does
                // not silently retarget the test.
                mr.buy = MkButton("buy_" + bm.PartId(i) + "_" + mat, row.transform, "BUY", 13, () => ShopBuy(idx, mat));
                mr.buy.gameObject.AddComponent<LayoutElement>().minWidth = 62f;
                mr.buyT = mr.buy.GetComponentInChildren<Text>();
                mr.sell = MkButton("sell_" + bm.PartId(i) + "_" + mat, row.transform, "SELL", 13, () => ShopSell(idx, mat));
                mr.sell.gameObject.AddComponent<LayoutElement>().minWidth = 78f;
                mr.sellT = mr.sell.GetComponentInChildren<Text>();
                mr.swap = MkButton("swap_" + bm.PartId(i) + "_" + mat, row.transform, "REWORK", 13, () => ShopSwap(idx, mat));
                mr.swap.gameObject.AddComponent<LayoutElement>().minWidth = 104f;
                mr.swapT = mr.swap.GetComponentInChildren<Text>();
                // OWEN 2026-08-03. RefreshShop owns these colours, so these are
                // hint-only gates - two writers on one Image is how a button
                // ends up flickering between two people's ideas of "dead".
                HoverHint(mr.buy, () => {
                    if (bm == null || !Career.active || Career.Data == null) return null;
                    int pr = CareerDB.PartPrice(bm.PartId(idx), mat);
                    return Career.Data.scrap >= pr ? null
                         : "Not enough scrap \u2014 " + MatDB.Get(mat).name + " " + bm.PartLabel(idx)
                           + " costs " + pr + ", you hold " + Career.Data.scrap + ".";
                });
                HoverHint(mr.sell, () => {
                    if (bm == null || !Career.active || Career.Data == null) return null;
                    return Career.CountOf(bm.PartId(idx), mat) > 0 ? null
                         : "You own no " + MatDB.Get(mat).name + " " + bm.PartLabel(idx) + " to sell.";
                });
                HoverHint(mr.swap, () => {
                    if (bm == null || !Career.active || Career.Data == null) return null;
                    if (bm.PartMatFixed(idx))
                        return bm.PartLabel(idx) + " is always " + MatDB.Get(mat).name
                             + " \u2014 there is nothing to rework it from.";
                    return bm.SwapSourceFor(idx, mat) != null ? null
                         : "You own no " + bm.PartLabel(idx) + " in another material to rework into "
                           + MatDB.Get(mat).name + ".";
                });
                shopMats.Add(mr);
            }
        }
        RefreshShop();
    }

    /// <summary>Accordion: open this part, close the others. Re-tapping the
    /// open part collapses it back to the one-line price range.</summary>
    void ToggleShopPart(int i)
    {
        bool wasOpen = false;
        for (int k = 0; k < shopParts.Count; k++) if (shopParts[k].part == i) wasOpen = shopParts[k].open;
        for (int k = 0; k < shopParts.Count; k++) shopParts[k].open = (!wasOpen && shopParts[k].part == i);
        armSell = -1; armSellMat = "";
        shopNote = ""; shopNoteBad = false;
        SfxSynth.Place();
        RefreshShop();
    }

    // Every action now names the material it acts on. Nothing in the shop
    // reads bm.ActiveMatKey any more, which is the whole point: the BUILD tab's
    // chips choose what you BUILD with, the shop chooses what you BUY.
    void ShopBuy(int i, string mat)
    {
        if (bm == null) return;
        armSell = -1; armSellMat = "";
        string id = bm.PartId(i);
        if (Career.TryBuy(id, mat))
            ShopFeedback(MatDB.Get(mat).name + " " + bm.PartLabel(i) + " bought \u2014 " + Career.CountOf(id, mat) + " owned.", false);
        else ShopFeedback(Career.shopMsg, true);
    }

    void ShopSell(int i, string mat)
    {
        if (bm == null) return;
        string id = bm.PartId(i);
        // Guard rail: every owned unit IN THIS MATERIAL is bolted to the current
        // build - selling one needs a second, explicit tap (armed, like REMOVE).
        // The arm now carries the material too, or one tap would arm all six of
        // a part's rows at once.
        bool inUse = Career.CountOf(id, mat) > 0 && bm.CareerRemainingMat(i, mat) == 0;
        if (inUse && (armSell != i || armSellMat != mat))
        {
            armSell = i; armSellMat = mat;
            ShopFeedback("Every " + MatDB.Get(mat).name + " " + bm.PartLabel(i) + " is in this build \u2014 tap SELL again to sell anyway.", true);
            return;
        }
        armSell = -1; armSellMat = "";
        int back = CareerDB.SellPrice(id, mat);
        if (Career.TrySell(id, mat))
            ShopFeedback(MatDB.Get(mat).name + " " + bm.PartLabel(i) + " sold \u2014 " + back + " scrap back.", false);
        else ShopFeedback(Career.shopMsg, true);
    }

    /// <summary>Doc 7: a material swap on an OWNED part costs the price
    /// difference plus a 10% workshop fee. Reworks INTO the row's material from
    /// the cheapest other material owned.</summary>
    void ShopSwap(int i, string mat)
    {
        if (bm == null) return;
        armSell = -1; armSellMat = "";
        string from = bm.SwapSourceFor(i, mat);
        if (from == null) { ShopFeedback("No " + bm.PartLabel(i) + " owned in another material to rework.", true); return; }
        int cost = Career.SwapCost(bm.PartId(i), from, mat);
        if (Career.TrySwap(bm.PartId(i), from, mat))
            ShopFeedback(bm.PartLabel(i) + ": " + MatDB.Get(from).name + " \u2192 " + MatDB.Get(mat).name + " for " + cost + ".", false);
        else ShopFeedback(Career.shopMsg, true);
    }

    void ShopFeedback(string msg, bool bad)
    {
        shopNote = msg; shopNoteBad = bad;
        if (bad) SfxSynth.Deny(); else SfxSynth.Place();
        RefreshShop(); RefreshPartLabels(); RefreshHighlight();
    }

    /// <summary>One catalog. Prices no longer ride the BUILD tab's chips: each
    /// part header states the price RANGE across its legal materials and how
    /// many you own in total, and opening it lists every legal material with
    /// price, mass and owned count so strength-per-kilogram can be judged at
    /// the counter (doc 3b). Pinned parts state their one material as a fact
    /// (R2's "Rubber (fixed)" label, kept) rather than offering a choice.</summary>
    public void RefreshShop()
    {
        if (bm == null || shopHeader == null) return;
        shopHeader.text = !string.IsNullOrEmpty(shopNote)
            ? (shopNoteBad ? "\u26a0 " : "") + shopNote + "   \u00b7   SCRAP " + Career.Data.scrap
            : "SCRAP " + Career.Data.scrap
              + "   \u00b7   tap a part to compare its materials   \u00b7   SELL returns 50%   \u00b7   REWORK = price difference + 10%";
        shopHeader.color = shopNoteBad ? new Color(1f, 0.82f, 0.25f) : new Color(0.80f, 0.88f, 1f);

        for (int k = 0; k < shopParts.Count; k++)
        {
            var pr = shopParts[k];
            int i = pr.part;
            string id = bm.PartId(i);
            var mats = bm.PartLegalMats(i);
            int lo = int.MaxValue, hi = 0, own = 0;
            foreach (var mk in mats)
            {
                int pc = CareerDB.PartPrice(id, mk);
                if (pc < lo) lo = pc;
                if (pc > hi) hi = pc;
                own += Career.CountOf(id, mk);
            }
            if (lo == int.MaxValue) lo = 0;
            string range = bm.PartMatFixed(i)
                ? MatDB.Get(mats[0]).name + " (fixed) \u00b7 " + hi + " scrap"
                : mats.Length + " materials \u00b7 " + lo + "\u2013" + hi + " scrap";
            pr.lbl.text = (pr.open ? "\u25be  " : "\u25b8  ") + bm.PartLabel(i)
                        + "   \u00b7   " + range + "   \u00b7   own " + own;
            pr.lbl.color = Color.white;
            pr.lbl.fontStyle = pr.open ? FontStyle.Bold : FontStyle.Normal;
            pr.go.GetComponent<Image>().color = pr.open ? new Color(0.20f,0.45f,0.65f,1f)
                                                        : new Color(0.13f,0.16f,0.21f,0.95f);
        }

        for (int k = 0; k < shopMats.Count; k++)
        {
            var mr = shopMats[k];
            bool open = false;
            for (int q = 0; q < shopParts.Count; q++) if (shopParts[q].part == mr.part) open = shopParts[q].open;
            if (mr.go.activeSelf != open) mr.go.SetActive(open);
            if (!open) continue;
            string id = bm.PartId(mr.part);
            var pdef = CareerDB.Def(id);
            int price = CareerDB.PartPrice(id, mr.mat);
            int ownm = Career.CountOf(id, mr.mat);
            bool fixedMat = bm.PartMatFixed(mr.part);
            bool afford = Career.Data.scrap >= price;
            mr.lbl.text = string.Format("{0}{1}   \u00b7   {2} scrap   \u00b7   {3} kg   \u00b7   own {4}",
                MatDB.Get(mr.mat).name, fixedMat ? " (fixed)" : "", price,
                pdef != null ? Mathf.RoundToInt(pdef.MassOf(mr.mat)) : 0, ownm);
            mr.lbl.color = afford ? new Color(0.92f,0.95f,1f) : new Color(0.70f,0.65f,0.60f);
            mr.buyT.text = "BUY";
            mr.buyT.color = afford ? GATE_TXT_LIVE : GATE_TXT_DEAD;
            mr.buy.GetComponent<Image>().color = afford ? new Color(0.17f,0.33f,0.24f,0.98f)
                                                        : new Color(0.13f,0.14f,0.17f,0.96f);
            // OWEN 2026-08-03: "whenever a button is disabled, it should show
            // hint to user on why it is disabled."
            //
            // These used to hold their SLOT but go fully transparent and
            // non-interactive - so an unavailable SELL was not a disabled
            // button, it was an unexplained gap. You could not hover it, click
            // it, or even see it. Now it stays visible, keeps its label, reads
            // dead, and says why (own none / nothing to rework from) on hover
            // or tap. Fixed columns either way, which was the original point.
            bool canSell = ownm > 0;
            mr.sellT.text = canSell ? "SELL " + CareerDB.SellPrice(id, mr.mat) : "SELL";
            mr.sellT.color = canSell ? GATE_TXT_LIVE : GATE_TXT_DEAD;
            mr.sell.GetComponent<Image>().color = canSell ? GATE_LIVE : GATE_DEAD;
            string src = fixedMat ? null : bm.SwapSourceFor(mr.part, mr.mat);
            mr.swapT.text = src != null ? "REWORK " + Career.SwapCost(id, src, mr.mat) : "REWORK";
            mr.swapT.color = src != null ? GATE_TXT_LIVE : GATE_TXT_DEAD;
            mr.swap.GetComponent<Image>().color = src != null ? GATE_LIVE : GATE_DEAD;
        }
    }

    InputField MkInput(string name, Transform parent)
    {
        var go = MkPanel(name, parent, new Color(0.13f,0.14f,0.18f,1f));
        var inp = go.AddComponent<InputField>();
        var txt = MkText("txt", go.transform, "", 15, TextAnchor.MiddleLeft);
        Stretch(txt.rectTransform);
        txt.rectTransform.offsetMin = new Vector2(8f, 2f); txt.rectTransform.offsetMax = new Vector2(-8f, -2f);
        txt.supportRichText = false;
        var ph = MkText("ph", go.transform, "robot name\u2026", 15, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform);
        ph.rectTransform.offsetMin = new Vector2(8f, 2f); ph.rectTransform.offsetMax = new Vector2(-8f, -2f);
        ph.color = new Color(1f, 1f, 1f, 0.35f);
        inp.textComponent = txt; inp.placeholder = ph;
        return inp;
    }

    void Feedback(string err) { if (err != null && bm != null) bm.Toast(err); }

    void BuildRobotsTab()
    {
        var v = robotsPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        var row = MkPanel("stablerow", robotsPanel.transform, new Color(0f,0f,0f,0f));
        var rle0 = row.AddComponent<LayoutElement>(); rle0.minHeight = 34f; rle0.preferredHeight = 34f;
        // The row's own HorizontalLayoutGroup reports flexibleHeight = 1
        // (childForceExpandHeight), and LayoutElement leaves flexibleHeight
        // unset by default, so the row competed with the stable list for the
        // dock's new height and ate ~180 units as a giant name box. Say zero.
        rle0.flexibleHeight = 0f;
        var rh0 = row.AddComponent<HorizontalLayoutGroup>(); rh0.spacing = 4f; rh0.childForceExpandHeight = true; rh0.childForceExpandWidth = false;
        nameInput = MkInput("namein", row.transform);
        nameInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        nameInput.onValueChanged.AddListener(delegate { RefreshNameGates(); });
        var newBtn = MkButton("stnew", row.transform, "NEW ROBOT", 14, () => { if (bm == null) return; Feedback(bm.StableCreate(nameInput.text)); nameInput.text = ""; RefreshRobots(); });
        newBtn.gameObject.AddComponent<LayoutElement>().minWidth = 100f;
        RegisterNameGated(newBtn);
        // OWEN 2026-08-02: SAVE has moved to the build bar, where the work is.
        // What is left here are the two CREATE actions, side by side, plus the
        // drafting-table toggle - renamed because "DRAFT" next to "NEW DRAFT"
        // read as two flavours of the same thing when one is a mode and the
        // other makes an object.
        // OWEN 2026-08-02: gated identically to NEW ROBOT beside it. Two
        // adjacent CREATE buttons behaving differently is the kind of
        // inconsistency you feel before you can name it.
        var newBpBtn = MkButton("stnewbp", row.transform, "NEW DRAFT", 14, () => { if (bm == null) return; Feedback(bm.BlueprintSave(nameInput != null ? nameInput.text : "")); if (nameInput != null) nameInput.text = ""; RefreshRobots(); });
        newBpBtn.gameObject.AddComponent<LayoutElement>().minWidth = 106f;
        RegisterNameGated(newBpBtn);
        // OWEN 2026-08-02: the DRAFT MODE toggle is gone. Drafting is derived
        // from whether a design is open, so NEW DRAFT / OPEN are the way in and
        // NEW ROBOT / EDIT / CONVERT are the ways out - there is no longer a
        // switch that can disagree with what you are actually editing.
        var scrollGO = MkPanel("stscroll", robotsPanel.transform, new Color(0f,0f,0f,0f));
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 80f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("stviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("stcontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f); crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);   // R1 critic: rows clipped both screen edges
        var clg = content.AddComponent<VerticalLayoutGroup>(); clg.spacing = 4f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false; clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: ROBOTS
        robotsContent = content.transform;
        RefreshRobots();
    }

    // OWEN 2026-08-02: "if the name text field is empty, disable the
    // corresponding buttons and give user a hint when hovering or clicking the
    // disabled buttons."
    //
    // Styled dead, but deliberately NOT Button.interactable = false. A truly
    // disabled uGUI button swallows the pointer outright - no click, no hover,
    // no EventTrigger - so it could never deliver the hint that was asked for.
    // It stays live and refuses with a sentence, which is the same choice the
    // FIGHT gate makes two screens over, for the same reason.
    readonly List<Button> nameGated = new List<Button>();
    const string NAME_HINT = "Type a name in the box on the left first.";
    static readonly Color GATE_LIVE = new Color(0.16f, 0.18f, 0.22f, 0.96f);
    static readonly Color GATE_DEAD = new Color(0.12f, 0.13f, 0.16f, 0.96f);
    static readonly Color GATE_TXT_LIVE = Color.white;
    static readonly Color GATE_TXT_DEAD = new Color(0.44f, 0.46f, 0.50f);

    bool NameEmpty()
    { return nameInput == null || nameInput.text == null || nameInput.text.Trim().Length == 0; }

    /// <summary>OWEN 2026-08-03: "whenever a button is disabled, it should show
    /// hint to user on why it is disabled when hovering or being clicked."
    ///
    /// Generalised from the name gate, because there were THREE bespoke
    /// versions of this idea in this file - nameGated, fightGates and SetArrow -
    /// and only the first two could talk. Three mechanisms is how the fourth
    /// one gets written without a hint and nobody notices.
    ///
    /// A gate is a button plus a function that returns WHY it is unavailable,
    /// or null when it is not. That single function drives all three
    /// behaviours: the dimming, the hover hint, and the refusal on click. They
    /// cannot drift apart because there is nothing to keep in sync.</summary>
    class BtnGate
    {
        public Button btn;
        public Image img;
        public Text txt;
        public System.Func<string> why;
        public Color live, dead, txtLive, txtDead;
        public bool paint;
    }
    readonly List<BtnGate> gates = new List<BtnGate>();
    float gateAt = -99f;

    /// <summary>Dim it, hint on hover, refuse on click.</summary>
    Button Gated(Button b, System.Func<string> why)
    { return Gated(b, why, GATE_LIVE, GATE_DEAD, GATE_TXT_LIVE, GATE_TXT_DEAD, true); }

    /// <summary>Hint only - for buttons whose colour is already owned by a
    /// refresh pass (the shop rows), so the two do not fight over the Image.</summary>
    Button HoverHint(Button b, System.Func<string> why)
    { return Gated(b, why, GATE_LIVE, GATE_DEAD, GATE_TXT_LIVE, GATE_TXT_DEAD, false); }

    Button Gated(Button b, System.Func<string> why, Color live, Color dead, Color tl, Color td, bool paint)
    {
        if (b == null || why == null) return b;
        gates.Add(new BtnGate { btn = b, img = b.GetComponent<Image>(), txt = b.GetComponentInChildren<Text>(),
                                why = why, live = live, dead = dead, txtLive = tl, txtDead = td, paint = paint });
        // NEVER Button.interactable = false on a gated button. A truly disabled
        // uGUI button swallows the pointer outright - no click, no hover, no
        // EventTrigger - so it can never deliver the hint that was asked for.
        // Every gate here stays live and refuses with a sentence.
        b.interactable = true;
        var trig = b.gameObject.GetComponent<EventTrigger>();
        if (trig == null) trig = b.gameObject.AddComponent<EventTrigger>();
        var en = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        en.callback.AddListener(delegate { string r = why(); if (r != null) Feedback(r); });
        trig.triggers.Add(en);
        return b;
    }

    void PumpGates()
    {
        if (Time.unscaledTime - gateAt < 0.25f) return;
        gateAt = Time.unscaledTime;
        for (int i = gates.Count - 1; i >= 0; i--)
        {
            var g = gates[i];
            if (g.btn == null) { gates.RemoveAt(i); continue; }   // its row was rebuilt
            if (!g.paint) continue;
            bool blocked = g.why() != null;
            if (g.img != null) g.img.color = blocked ? g.dead : g.live;
            if (g.txt != null) g.txt.color = blocked ? g.txtDead : g.txtLive;
        }
    }

    void RegisterNameGated(Button b)
    {
        if (b == null) return;
        nameGated.Add(b);
        Gated(b, () => NameEmpty() ? NAME_HINT : null);
    }

    /// <summary>Kept alongside the 0.25 s gate pump because typing wants an
    /// answer on the same frame as the keystroke, not a quarter second later.</summary>
    void RefreshNameGates()
    {
        bool empty = NameEmpty();
        for (int i = nameGated.Count - 1; i >= 0; i--)
        {
            var b = nameGated[i];
            if (b == null) { nameGated.RemoveAt(i); continue; }   // its row was rebuilt
            var im = b.GetComponent<Image>();
            if (im != null) im.color = empty ? GATE_DEAD : GATE_LIVE;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = empty ? GATE_TXT_DEAD : GATE_TXT_LIVE;
        }
    }

    void RefreshRobots()
    {
        if (robotsContent == null || bm == null) return;
        for (int i = robotsContent.childCount - 1; i >= 0; i--) Destroy(robotsContent.GetChild(i).gameObject);
        // R5 (critic finding 6): the per-tab SKIP TIPS button that used to live
        // here is GONE. R4 moved SKIP TIPS into the tip row (BuildCanvas, the
        // "tipskip" button) but left this one behind, so a new career drew TWO
        // identical SKIP TIPS buttons six rows apart on the ROBOTS tab. One
        // button, one place.
        if (Career.Data.stable.Count == 0 && Career.Data.blueprints.Count == 0 && !Career.Drafting)
        {
            // R2 critic: a new player's ROBOTS tab was an input row over a void.
            var erow = MkPanel("emptyrow", robotsContent, new Color(0f,0f,0f,0f));
            var ele = erow.AddComponent<LayoutElement>(); ele.minHeight = 30f; ele.preferredHeight = 30f;
            var et = MkText("lbl", erow.transform, "Your stable is empty \u2014 type a name above, then tap NEW ROBOT.", 14, TextAnchor.MiddleLeft);
            et.color = new Color(0.65f, 0.75f, 0.9f);
            var ert = et.rectTransform; ert.anchorMin = Vector2.zero; ert.anchorMax = Vector2.one; ert.offsetMin = new Vector2(8f, 0f); ert.offsetMax = Vector2.zero;
        }
        if (Career.Drafting)
        {
            var drow = MkPanel("draftrow", robotsContent, new Color(0.25f,0.16f,0.05f,0.9f));
            var dle = drow.AddComponent<LayoutElement>(); dle.minHeight = 34f; dle.preferredHeight = 34f;
            var dh = drow.AddComponent<HorizontalLayoutGroup>(); dh.spacing = 4f; dh.childForceExpandHeight = true; dh.childForceExpandWidth = false; dh.padding = new RectOffset(6,4,2,2);
            var dl = MkText("lbl", drow.transform, "DRAFT MODE \u2014 everything unlocked", 13, TextAnchor.MiddleLeft);
            dl.color = new Color(1f, 0.82f, 0.25f);
            dl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            MkButton("bpconv", drow.transform, "CONVERT " + bm.ConvertQuote() + " scrap", 13, () => { Feedback(bm.ConvertDraft()); RefreshRobots(); RefreshPartLabels(); RefreshHighlight(); })
                .gameObject.AddComponent<LayoutElement>().minWidth = 128f;
            // SAVE BP used to sit here as a second commit button. NEW DRAFT in
            // the row above now owns creating one, and the build bar's SAVE
            // updates the draft already open, so this row is just CONVERT.
        }
        for (int i2 = 0; i2 < Career.Data.stable.Count; i2++)
        {
            int ri = i2;
            var rob = Career.Data.stable[i2];
            var row = MkPanel("robot_" + i2, robotsContent, new Color(0.10f,0.11f,0.14f,1f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 34f; rle.preferredHeight = 34f;
            var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(6,4,2,2);
            // MEDALS (2026-08-02): titles are real now - one per league
            // campaign swept - so the card names the championship instead of
            // printing a bare star nothing explained. Kept to the existing one
            // -line card; the full §2b redesign is still out of scope.
            string champ = "";
            foreach (var h in rob.leagueHistory)
                if (h.EndsWith(" champion")) champ += (champ.Length > 0 ? " \u00b7 " : "") + h.Substring(0, h.Length - 9);
            var lbl = MkText("lbl", row.transform, string.Format("{0}{1} \u00b7 {2}-{3}{4}{5}",
                ri == Career.Data.activeRobot ? "\u25b8 " : "", rob.name, rob.wins, rob.losses,
                rob.titles > 0 ? "  \u00b7  \u2605\u00d7" + rob.titles : "",
                champ.Length > 0 ? "\n\u2605 " + champ + " champion" : ""), 14, TextAnchor.MiddleLeft);
            if (rob.titles > 0) lbl.color = new Color(1f, 0.87f, 0.46f);
            // a two-line card needs the height, or the champion line clips
            if (champ.Length > 0) { rle.minHeight = 46f; rle.preferredHeight = 46f; }
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // R5 (critic finding 9 carve-out): EDIT, RENAME and RETIRE were the
            // same grey at the same size, so the irreversible action was styled
            // exactly like the primary one. EDIT reads primary; RETIRE reads
            // destructive and states its own confirm on the button face instead
            // of only in a message bar the player may not be looking at.
            // OWEN 2026-08-02: EDIT already loaded the machine and made it
            // active - it just left you standing on ROBOTS to go find the tools
            // yourself. Loading a robot IS the intent to work on it.
            var edb = MkButton("stedit_" + i2, row.transform, "EDIT", 13, () => { Feedback(bm.StableEdit(ri)); RefreshRobots(); RefreshPartLabels(); RefreshHighlight(); ShowTab(0); });
            edb.gameObject.AddComponent<LayoutElement>().minWidth = 56f;
            edb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
            var renBtn = MkButton("stren_" + i2, row.transform, "RENAME", 13, () => { Feedback(bm.StableRename(ri, nameInput != null ? nameInput.text : "")); if (nameInput != null) nameInput.text = ""; RefreshRobots(); });
            renBtn.gameObject.AddComponent<LayoutElement>().minWidth = 74f;
            RegisterNameGated(renBtn);
            bool armedR = retireArmM == ri;
            var rtb = MkButton("stret_" + i2, row.transform, armedR ? "CONFIRM \u2715" : "RETIRE", 13, () => {
                if (retireArmM == ri) { retireArmM = -1; Feedback(bm.StableRetire(ri)); RefreshRobots(); }
                else { retireArmM = ri; Feedback("Retire " + rob.name + "? This cannot be undone \u2014 tap CONFIRM."); RefreshRobots(); }
            });
            rtb.gameObject.AddComponent<LayoutElement>().minWidth = 86f;
            rtb.GetComponent<Image>().color = armedR ? new Color(0.78f,0.18f,0.13f,1f) : new Color(0.34f,0.13f,0.12f,1f);
            var rtt = rtb.GetComponentInChildren<Text>(); if (rtt != null) rtt.color = armedR ? Color.white : new Color(1f,0.68f,0.62f,1f);
        }
        for (int i3 = 0; i3 < Career.Data.blueprints.Count; i3++)
        {
            int bi = i3;
            var bp = Career.Data.blueprints[i3];
            var row = MkPanel("bp_" + i3, robotsContent, new Color(0.12f,0.10f,0.16f,1f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 30f; rle.preferredHeight = 30f;
            var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(6,4,2,2);
            var lbl = MkText("lbl", row.transform, "\u270e " + bp.name + " (blueprint)", 13, TextAnchor.MiddleLeft);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // OWEN 2026-08-02: "what does 'Draft it' button mean?" - it read as
            // "turn this into a draft", but the thing already IS a draft. It
            // OPENS the blueprint with everything unlocked. The row already says
            // "(blueprint)" and draft mode now announces itself in the status
            // line the moment you land, so the button only has to name the verb.
            // It also lands you on BUILD, for the same reason EDIT does.
            MkButton("bpedit_" + i3, row.transform, "OPEN", 13, () => { Feedback(bm.BlueprintEdit(bi)); RefreshRobots(); RefreshPartLabels(); RefreshHighlight(); ShowTab(0); })
                .gameObject.AddComponent<LayoutElement>().minWidth = 86f;
            // OWEN 2026-08-02: "How do I delete drafts". Same arm-then-confirm
            // as RETIRE one loop up, and styled the same destructive red, so
            // the only two irreversible actions on this tab look and behave
            // alike instead of one of them simply not existing.
            bool armedB = bpDelArmM == i3;
            var bdb = MkButton("bpdel_" + i3, row.transform, armedB ? "CONFIRM \u2715" : "DELETE", 13, () => {
                if (bpDelArmM == bi) { bpDelArmM = -1; Feedback(bm.BlueprintDelete(bi)); RefreshRobots(); }
                else { bpDelArmM = bi; Feedback("Delete blueprint " + bp.name + "? This cannot be undone \u2014 tap CONFIRM."); RefreshRobots(); }
            });
            bdb.gameObject.AddComponent<LayoutElement>().minWidth = 86f;
            bdb.GetComponent<Image>().color = armedB ? new Color(0.78f,0.18f,0.13f,1f) : new Color(0.34f,0.13f,0.12f,1f);
            var bdt = bdb.GetComponentInChildren<Text>();
            if (bdt != null) bdt.color = armedB ? Color.white : new Color(1f,0.68f,0.62f,1f);
        }
        // The RENAME buttons above were just created; style them to match the
        // name box's current state instead of waiting for the next keystroke.
        RefreshNameGates();
    }

    void BuildPartsTab()
    {
        var v = partsPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        var scrollGO = MkPanel("shscroll", partsPanel.transform, new Color(0f,0f,0f,0f));
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 100f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("shviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("shcontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f); crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);   // R1 critic: rows clipped both screen edges
        var clg = content.AddComponent<VerticalLayoutGroup>(); clg.spacing = 4f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false; clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: PARTS
        partsContent = content.transform;
    }

    /// <summary>C4 PARTS shelf: owned counts plus who is borrowing what -
    /// every stable robot's snapshot and the build being edited.</summary>
    /// <summary>R4 (critic finding 5). Doc section 7 makes the inventory a
    /// SHARED POOL: parts are owned, builds borrow them, and two saved builds
    /// cannot hold the same six beams at once. This shelf used to list every
    /// stable robot AND the live edit as independent exclusive claims on a pool
    /// that nothing ever allocated, so it printed `own 1` over
    /// `Rustbucket x1 . Ledger x1 . editing x1` - 3 claimed against 1 owned.
    ///
    /// Two separate bugs made that number. (a) Saved robots consumed nothing at
    /// all - fixed in Career.CommittedUsage / BuilderManager.CareerUsed. (b) The
    /// robot open in the editor was counted TWICE here, once from its saved
    /// snapshot and once as "editing"; that slot is now skipped and the live row
    /// is labelled with the robot's name so it is obvious which is which.
    ///
    /// Every row now balances - owned = in use + spare - and the one case that
    /// cannot balance (an old save, or parts sold out from under a build) says
    /// SHORT in amber instead of quietly printing an impossibility.</summary>
    void RefreshParts()
    {
        if (partsContent == null || bm == null) return;
        for (int i = partsContent.childCount - 1; i >= 0; i--) Destroy(partsContent.GetChild(i).gameObject);
        int act = Career.Data.activeRobot;
        string editing = act >= 0 && act < Career.Data.stable.Count
                       ? "editing \u25b8 " + Career.Data.stable[act].name : "editing (unsaved)";
        var keys = new List<string>();
        foreach (var it in Career.Data.inventory)
            if (it.count > 0) { string k = it.partId + "|" + MatDB.Canon(it.mat); if (!keys.Contains(k)) keys.Add(k); }
        var userNames = new List<string>();
        var userMaps = new List<Dictionary<string, int>>();
        for (int r = 0; r < Career.Data.stable.Count; r++)
        {
            if (r == act) continue;   // superseded by the live build below
            userNames.Add(Career.Data.stable[r].name);
            userMaps.Add(Career.SnapshotUsage(Career.Data.stable[r].snapshot));
        }
        userNames.Add(editing);
        userMaps.Add(Career.SnapshotUsage(bm.SnapshotString()));
        foreach (var m2 in userMaps)
            foreach (var k in m2.Keys)
            {
                var f2 = k.Split('|');
                string ck = f2[0] + "|" + MatDB.Canon(f2[1]);
                if (!keys.Contains(ck)) keys.Add(ck);
            }
        foreach (var key in keys)
        {
            var f2 = key.Split('|');
            string id = f2[0], mat = f2[1];
            var def = CareerDB.Def(id);
            if (def == null) continue;
            int own = Career.CountOf(id, mat);
            int used = 0;
            string use = "";
            for (int u2 = 0; u2 < userMaps.Count; u2++)
            {
                int n2 = 0;
                foreach (var kv in userMaps[u2])
                {
                    var f3 = kv.Key.Split('|');
                    if (f3[0] == id && MatDB.Canon(f3[1]) == mat) n2 += kv.Value;
                }
                if (n2 > 0) { used += n2; use += (use.Length > 0 ? " \u00b7 " : "") + userNames[u2] + " \u00d7" + n2; }
            }
            int spare = own - used;
            bool over = spare < 0;
            var row = MkPanel("shelf_" + key, partsContent,
                over ? new Color(0.22f,0.13f,0.05f,0.92f) : new Color(0.10f,0.11f,0.14f,0.85f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 40f; rle.preferredHeight = 40f;
            var lbl = MkText("lbl", row.transform, string.Format("{0} \u00b7 {1} \u2014 {2} owned \u00b7 {3} in use \u00b7 {4}\n{5}",
                def.label, MatDB.Get(mat).name, own, used,
                over ? "SHORT " + (-spare) + " \u2014 buy more or retire a robot" : spare + " spare",
                use.Length > 0 ? "fitted to: " + use : "unused \u2014 all " + own + " spare"), 12, TextAnchor.MiddleLeft);
            if (over) lbl.color = new Color(1f, 0.82f, 0.25f);
            Stretch(lbl.rectTransform);
            lbl.rectTransform.offsetMin = new Vector2(8f, 2f); lbl.rectTransform.offsetMax = new Vector2(-8f, -2f);
        }
    }

    /// <summary>MEDALS (2026-08-02, owen: "user should be able to check their
    /// medals in history"). Built exactly like the other four list tabs -
    /// scroll rect, masked viewport, ContentSizeFitter, and AddListOverflow so
    /// it gets the same scrollbar and the same "more below" chevron. It also
    /// inherits the full-height opaque dock from DockH(), which returns the
    /// list-tab height for every career tab that is not BUILD, so tab 5 needed
    /// no change there - the opaque dock panel IS the background.</summary>
    void BuildTrophiesTab()
    {
        var v = trophyPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        trophyHeader = MkText("trophyheader", trophyPanel.transform, "", 15, TextAnchor.MiddleLeft);
        var hle = trophyHeader.gameObject.AddComponent<LayoutElement>(); hle.minHeight = 22f; hle.preferredHeight = 22f;
        var scrollGO = MkPanel("trscroll", trophyPanel.transform, new Color(0f,0f,0f,0f));
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 100f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("trviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("trcontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f); crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);
        var clg = content.AddComponent<VerticalLayoutGroup>(); clg.spacing = 4f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false; clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: TROPHIES
        trophyContent = content.transform;
    }

    /// <summary>Every league is listed, won or not. A trophy case showing only
    /// what you already hold is a receipt; showing the gaps is what makes it a
    /// goal - so an unwon league draws as an explicit empty socket saying how
    /// many contests are left, and a locked one says so rather than vanishing.
    ///
    /// Gold here is the same celebration gold the results ribbon uses, and is
    /// kept off the warning amber (1, 0.82, 0.25) that PARTS uses for SHORT and
    /// the builder uses for over-cap.</summary>
    void RefreshTrophies()
    {
        if (trophyContent == null) return;
        for (int i = trophyContent.childCount - 1; i >= 0; i--) Destroy(trophyContent.GetChild(i).gameObject);
        int won = Career.Data.medals.Count;
        int total = CareerDB.Leagues.Length;
        if (trophyHeader != null)
        {
            trophyHeader.text = "TROPHY CASE  \u00b7  " + won + " of " + total + " league campaigns won";
            trophyHeader.color = won > 0 ? new Color(1f, 0.87f, 0.46f) : new Color(0.72f, 0.78f, 0.88f);
        }
        if (won == 0)
        {
            // Empty state must read as "not yet", never as "failed to load".
            var erow = MkPanel("tremptyhead", trophyContent, new Color(0.13f,0.12f,0.09f,0.9f));
            var ele = erow.AddComponent<LayoutElement>(); ele.minHeight = 46f; ele.preferredHeight = 46f;
            var et = MkText("lbl", erow.transform,
                "No medals yet \u2014 win EVERY contest in a league and its champion medal lands here.",
                14, TextAnchor.MiddleLeft);
            et.color = new Color(0.88f, 0.82f, 0.66f);
            Stretch(et.rectTransform);
            et.rectTransform.offsetMin = new Vector2(10f, 2f); et.rectTransform.offsetMax = new Vector2(-10f, -2f);
        }
        for (int li = 0; li < total; li++)
        {
            var lg = CareerDB.Leagues[li];
            var m = Career.MedalFor(li);
            bool open = Career.LeagueUnlocked(li);
            int done = 0;
            foreach (var cc in lg.contests) if (Career.Data.doneContests.Contains(cc.id)) done++;
            var row = MkPanel("medal_" + li, trophyContent,
                m != null ? new Color(0.22f,0.17f,0.05f,0.96f) : new Color(0.10f,0.11f,0.14f,0.9f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = 52f; rle.preferredHeight = 52f;
            string head, sub;
            if (m != null)
            {
                head = "\u2605  " + m.leagueName.ToUpper() + " CHAMPION";
                sub  = m.arenaName + "  \u00b7  " + m.robot + "  " + m.wins + "-" + m.losses
                     + "  \u00b7  " + m.contests + " contest" + (m.contests == 1 ? "" : "s")
                     + " swept  \u00b7  " + m.when;
            }
            else if (!open)
            {
                head = "\u25cb  " + lg.name.ToUpper() + "  \u2014  LOCKED";
                sub  = lg.arenaName + "  \u00b7  win the " + CareerDB.Leagues[li - 1].name + " first";
            }
            else
            {
                head = "\u25cb  " + lg.name.ToUpper() + "  \u2014  NOT YET WON";
                sub  = lg.arenaName + "  \u00b7  " + done + " of " + lg.contests.Length
                     + " contests beaten  \u00b7  sweep them all for the medal";
            }
            var lbl = MkText("lbl", row.transform, head + "\n" + sub, 13, TextAnchor.MiddleLeft);
            lbl.color = m != null ? new Color(1f, 0.87f, 0.46f)
                      : open     ? new Color(0.70f, 0.76f, 0.86f)
                                 : new Color(0.48f, 0.52f, 0.60f);
            Stretch(lbl.rectTransform);
            lbl.rectTransform.offsetMin = new Vector2(10f, 2f); lbl.rectTransform.offsetMax = new Vector2(-10f, -2f);
        }
    }

    /// <summary>Test/QA hook: drive the dock exactly as a tap would. The
    /// visual critic loop needs to walk every tab and photograph it; without
    /// this it would have to synthesise pointer events against a moving
    /// layout, which is how UI harnesses end up testing their own maths
    /// instead of the screen.</summary>
    public void TestShowTab(int i) { ShowTab(i); }
    public int TestTab { get { return tab; } }
    /// <summary>R1 fix 2 evidence seam. The dock height drives BOTH the panel
    /// layout and the pointer-blocking boundary; if they ever disagree, taps
    /// inside the expanded panel place parts on the robot behind it. These
    /// two read-only peeks let a harness prove they agree per tab instead of
    /// taking it on trust.</summary>
    public float TestDockHeight { get { return dockRt != null ? dockRt.sizeDelta.y : -1f; } }
    public bool TestOverUI(Vector2 screenPoint) { return OverUI(screenPoint); }
    public float TestCanvasScale { get { return canvas != null ? canvas.scaleFactor : -1f; } }

    void ShowTab(int i)
    {
        if (i >= 3 && !Career.active) i = 0;   // SHOP/PARTS are career-only
        tab = i;
        armSell = -1;
        retireArmM = -1;
        bool robots = i == 2 && Career.active;
        // R6: `open` gates every one of these. ShowTab is not only called by the
        // tab buttons - Update calls it whenever career state moves, and that
        // path re-activated the panel behind the collapsed dock's back. The
        // panel then laid out inside a 48-unit dock, which gives an INVERTED
        // rect: measured `build` at y 11..0 with its material row hanging at
        // y -72, off the bottom of the screen. Invisible, still live, still
        // taking taps. Collapsing has to mean collapsed no matter who asks.
        bool open = dockOpen;
        if (buildPanel != null) buildPanel.SetActive(open && i == 0);
        if (fightPanel != null) fightPanel.SetActive(open && i == 1);
        if (garagePanel != null) garagePanel.SetActive(open && i == 2 && !Career.active);
        if (robotsPanel != null) robotsPanel.SetActive(open && robots);
        if (shopPanel != null) shopPanel.SetActive(open && i == 3);
        if (partsPanel != null) partsPanel.SetActive(open && i == 4);
        if (trophyPanel != null) trophyPanel.SetActive(open && i == 5 && Career.active);
        if (i == 3) { shopNote = ""; shopNoteBad = false; RefreshShop(); }
        if (robots) RefreshRobots();
        if (i == 4) RefreshParts();
        if (i == 5) RefreshTrophies();
        // R1 fix 2 + 6: geometry and selection are both per-tab now.
        ApplyDockH();
        RefreshTabHighlight();
    }

    static readonly List<Vector2> P = new List<Vector2>();
    void Pointers()
    {
        P.Clear();
#if ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts != null) foreach (var t in ts.touches) if (t.press.isPressed) P.Add(t.position.ReadValue());
        if (P.Count == 0) { var m = Mouse.current; if (m != null && m.leftButton.isPressed) P.Add(m.position.ReadValue()); }
#else
        for (int i = 0; i < Input.touchCount; i++) P.Add(Input.GetTouch(i).position);
        if (P.Count == 0 && Input.GetMouseButton(0)) P.Add(Input.mousePosition);
#endif
    }

    bool OverUI(Vector2 p)
    {
        // The save dialog is modal: while it is up the entire screen is UI, or
        // a tap that misses the card places a part in the build room behind it.
        if (saveDlg != null && saveDlg.activeSelf) return true;
        float sf = canvas != null ? canvas.scaleFactor : 1f;
        // R1 fix 2: NEVER re-introduce a literal here. The dock height is
        // per-tab; a stale 210 lets taps in the expanded panel fall through
        // into the builder and place parts behind the UI.
        float dh = dockRt != null ? dockRt.sizeDelta.y : 210f;
        // R4: the top band is now up to three stacked bars, and SKIP TIPS is a
        // real button in the third one - a tap there must not also drag the
        // build camera.
        float top = BAR_H
                  + ((msgBar != null && msgBar.activeSelf) ? MSG_H : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TIP_H : 0f);
        // The handle floats ABOVE the dock, so the dock's height does not cover
        // it. Without this, tapping SHOW PANEL would also drop a part on the
        // robot behind it - the exact class of fall-through the R1 note above
        // warns about, reintroduced by a control that sits outside the band.
        if (dockHandle != null && dockHandle.gameObject.activeInHierarchy && handleRt != null
            && RectTransformUtility.RectangleContainsScreenPoint(handleRt, p, null))
            return true;
        return p.y < dh * sf || p.y > Screen.height - top * sf;
    }

    void Update()
    {
        if (bm == null) { bm = Object.FindFirstObjectByType<BuilderManager>(); if (bm == null) return; }
        bool fighting = Object.FindFirstObjectByType<FightManager>() != null
                     || bm.mode == BuilderManager.Mode.Test   // fix: dock stayed up over TEST DRIVE
                     || bm.Scouting;                          // C3: scouting overlay owns the screen
        if (canvas != null) canvas.enabled = !fighting;
        if (fighting) { BuilderManager.uiPointerBlocked = false; wasFighting = true; return; }
        if (wasFighting) { wasFighting = false; RefreshFightInfo(); if (Career.active) RefreshCareerBoard(); }
        // C1: stock badges follow the build, the material chip and the career
        // switch live; refresh only when one of them changed.
        if (Career.active != careerSeenActive
            || (Career.active && (careerSeenPlaced != bm.PlacedCount || careerSeenMat != bm.ActiveMatKey)))
        {
            careerSeenActive = Career.active;
            careerSeenPlaced = bm.PlacedCount;
            careerSeenMat = bm.ActiveMatKey;
            RefreshPartLabels(); RefreshHighlight();
            LayoutTabs();
            RefreshFightTab();
            RefreshFightInfo();
            if (Career.active) RefreshShop();
            ShowTab(tab);   // C4: re-evaluate which panel tab 2+ shows
        }
        if (statsText != null)
        {
            // R2 (critic finding 4): notices go to their own bar and expire;
            // the status below is rebuilt every frame no matter what they do.
            PumpMessage(bm.LastMessage);
            PumpFightGates();   // OWEN: the contest rows answer before the tap
            PumpGates();        // OWEN 2026-08-03: every gated button, one rule
            PumpDirty(false);   // OWEN: does the active robot owe a save?
            PumpTip();          // R4 finding 4: onboarding is a THIRD channel
            {
            statsText.color = Color.white;
            statsText.text = bm.HasSelection
                ? string.Format("HOLDING {0} — tap the robot to place  ·  {1} kg · {2} part{3}", bm.PartLabel(bm.SelectedPart), bm.BuildMassInt, bm.PlacedCount, bm.PlacedCount == 1 ? "" : "s")
                : string.Format("{0} kg · {1} part{2}  ·  {3}", bm.BuildMassInt, bm.PlacedCount, bm.PlacedCount == 1 ? "" : "s", TabHint());
            // C3: live weight-cap readout against the targeted league
            if (Career.active)
            {
                string rn = bm.ActiveEditName();
                // The dot is the whole point of an explicit SAVE: without it
                // you cannot tell a saved robot from an unsaved one, and the
                // button becomes something you tap superstitiously.
                if (rn != null)
                    statsText.text = "[" + (bm.ActiveEditIsDraft ? "draft: " : "") + rn
                                   + (buildDirty ? " \u25cf" : "") + "]  " + statsText.text;
                var tlg = CareerDB.Leagues[Mathf.Clamp(Career.targetLeagueIdx, 0, CareerDB.Leagues.Length - 1)];
                bool over = bm.BuildMassInt > tlg.weightCap;
                statsText.text += string.Format("  \u00b7  {0}/{1} kg {2}{3}",
                    bm.BuildMassInt, Mathf.RoundToInt(tlg.weightCap), tlg.name, over ? " OVER" : "");
                // OWEN 2026-08-03: the size box readout lived here for one
                // commit. He asked why the rule existed at all, saw the
                // numbers, and cut it - so there is one budget on this bar
                // again, and it is mass.
                if (over) statsText.color = new Color(1f, 0.82f, 0.25f);
                // C4 onboarding used to overwrite everything above this line.
                // It now lives in tipBar (PumpTip) - see R4 finding 4. The
                // weight budget is a spec requirement and must NEVER again be
                // conditional on onboarding state.
            }
            // OWEN 2026-08-02: mode goes FIRST, and is written LAST - after the
            // career block, so nothing above can overwrite it, and prefixed so
            // it reads before the robot name. This replaces the floating IMGUI
            // banner that used to land on top of the tip bar.
            string modeTag = ModeTag();
            if (modeTag.Length > 0)
            {
                statsText.text = modeTag + statsText.text;
                statsText.color = Career.active ? new Color(1f, 0.82f, 0.25f)
                                                : new Color(1f, 0.45f, 0.38f);
            }
            }
        }
        // The edge chevron is a promise that there is more to the right. It
        // must therefore only appear when that is TRUE - at the review
        // resolution the whole palette now fits, on a narrower screen it will
        // not, and the affordance has to follow the geometry either way.
        if (partScroll != null && partEdge != null && partScroll.content != null && partScroll.viewport != null)
        {
            bool over = partScroll.content.rect.width > partScroll.viewport.rect.width + 1f;
            if (partEdge.activeSelf != over) partEdge.SetActive(over);
        }
        // R2 (critic finding 8): the same promise, on the four vertical lists.
        // It must only be made when it is TRUE, and withdrawn once the player
        // has actually reached the bottom.
        for (int li = 0; li < listScrolls.Count; li++)
        {
            var lsc = listScrolls[li]; var lfd = listFades[li];
            if (lsc == null || lfd == null || lsc.content == null || lsc.viewport == null) continue;
            bool lover = lsc.content.rect.height > lsc.viewport.rect.height + 1f;
            bool want = lover && lsc.verticalNormalizedPosition > 0.02f;
            if (lfd.activeSelf != want) lfd.SetActive(want);
        }
        Pointers();
        if (P.Count >= 2)
        {
            float dist = (P[0] - P[1]).magnitude;
            if (pinchPrev > 0f) bm.TestOrbitDist -= (dist - pinchPrev) * 0.02f;
            pinchPrev = dist;
            Vector2 avg = (P[0] + P[1]) * 0.5f;
            if (dragging) { Vector2 d = avg - lastP; bm.TestOrbitYaw += d.x * 0.3f; bm.TestOrbitPitch = Mathf.Clamp(bm.TestOrbitPitch - d.y * 0.3f, -70f, 80f); }
            dragging = true; lastP = avg; Phase0Input.debugPointer = false; BuilderManager.uiPointerBlocked = false;
            return;
        }
        pinchPrev = -1f;
        if (removeArmed && bm.HasSelection) { removeArmed = false; RefreshRemoveBtn(); }
        if (P.Count == 1)
        {
            Vector2 p = P[0];
            bool overUI = OverUI(p);
            BuilderManager.uiPointerBlocked = overUI;
            if (overUI) { dragging = false; Phase0Input.debugPointer = bm.HasSelection || removeArmed || clickHold > 0; return; }
            if (bm.HasSelection || removeArmed)
            {
                Phase0Input.debugPointer = true;
                Phase0Input.debugMousePos = new Vector3(p.x, p.y, 0f);
                if (!dragging) { dragging = true; downP = p; moved = 0f; lastP = p; }
                else { moved += (p - lastP).magnitude; lastP = p; }
            }
            else
            {
                Phase0Input.debugPointer = false;
                if (!dragging) { dragging = true; lastP = p; }
                Vector2 d = p - lastP; lastP = p;
                bm.TestOrbitYaw += d.x * 0.3f; bm.TestOrbitPitch = Mathf.Clamp(bm.TestOrbitPitch - d.y * 0.3f, -70f, 80f);
            }
            return;
        }
        // no pointers — release edge
        if (dragging && !OverUI(lastP))
        {
            if (removeArmed)
            {
                Phase0Input.debugPointer = true;
                Phase0Input.debugMousePos = new Vector3(lastP.x, lastP.y, 0f);
                Phase0Input.DebugClick(1);
                removeArmed = false; clickHold = 4;
                RefreshRemoveBtn();
            }
            else if (bm.HasSelection) Phase0Input.DebugClick(0);
        }
        dragging = false;
        BuilderManager.uiPointerBlocked = false;
        if (clickHold > 0) { clickHold--; Phase0Input.debugPointer = true; }
        else Phase0Input.debugPointer = bm.HasSelection || removeArmed;
    }

    /// <summary>R1 fix 8: this clause used to be written unconditionally, so
    /// ROBOTS, SHOP and LEAGUE all told the player to "pick a part below,
    /// then tap the robot" over a panel containing no parts - and LEAGUE's
    /// own header contradicted it two lines lower. The status prefix (robot
    /// name, mass, part count, weight budget) stays on every tab because it
    /// is status; only the instruction follows the tab.</summary>
    string TabHint()
    {
        if (!Career.active)
            return tab == 1 ? "pick a fight below"
                 : tab == 2 ? "save or load a build below"
                 : "pick a part below, then tap the robot";
        switch (tab)
        {
            case 1:  return "pick a contest \u00b7 SCOUT first, then FIGHT";
            case 2:  return "your stable \u00b7 EDIT loads a robot into the builder";
            case 3:  return "tap a part to see every material and price \u00b7 then BUY";
            case 4:  return "your parts, and who is using them";
            case 5:  return "your trophy case \u00b7 one medal per league campaign won";
            default: return "pick a part below, then tap the robot";
        }
    }

    void RefreshRemoveBtn()
    {
        if (removeBtn == null) return;
        removeBtn.GetComponent<Image>().color = removeArmed
            ? new Color(0.62f, 0.16f, 0.14f, 0.98f)
            : new Color(0.16f, 0.18f, 0.22f, 0.96f);
    }

    // ---- uGUI construction helpers ----
    /// <summary>R2 (critic finding 4). A notice is shown for MSG_LIFE seconds
    /// from the moment its TEXT changes and then stops existing, so a one-off
    /// message can no longer occupy every tab for a whole session. REMOVE-armed
    /// is a MODE, not a notice, so it holds until it is disarmed.</summary>
    const float BAR_H = 46f, MSG_H = 38f, TIP_H = 34f;

    /// <summary>R4 (critic finding 4). The stored counter is a FLOOR, not the
    /// truth. The old tip keyed off tutorialStep alone and checked no state, so
    /// it told a player with two robots in the stable to "found your stable".
    /// State wins: a non-empty stable is past step 0, a saved snapshot is past
    /// step 1.</summary>
    /// <summary>OWEN 2026-08-03: both of these now defer to BuilderManager,
    /// which owns the one tip list. They used to be the only copy, and the
    /// desktop strip was a second, older, career-blind one.</summary>
    int TutorialStep()
    {
        return bm == null ? BuilderManager.TIP_COUNT : bm.CareerTipStep();
    }

    /// <summary>The tip names the tab it is about, and says something different
    /// once you are standing on it - it used to print the identical ROBOTS
    /// sentence on BUILD, LEAGUE, SHOP and PARTS.</summary>
    string TutorialTip(int ts)
    {
        if (bm == null) return "";
        return bm.CareerTip(ts, tab == 0 ? "build" : tab == 1 ? "league" : "other");
    }

    /// <summary>R4 (critic finding 4): onboarding gets its own row, stacked
    /// under the notice bar when one is live so the two can never eat each
    /// other, and never touches statsText.</summary>
    Button tipPrev, tipNext;
    /// <summary>Which tip is being READ. -1 = follow real progress, which is
    /// the default and the state it returns to the moment progress moves.</summary>
    int tipView = -1;
    int tipStepSeen = -1;

    void TipStep(int d)
    {
        int cur = tipView < 0 ? TutorialStep() : tipView;
        int next = Mathf.Clamp(cur + d, 0, BuilderManager.TIP_COUNT - 1);
        // Touch has no hover, so the CLICK path has to carry the reason or the
        // hint never reaches a player on a tablet - which is most of them.
        if (next == cur) { Feedback(d < 0 ? TIP_FIRST : TIP_LAST); return; }
        tipView = next;
        PumpTip();
    }

    void PumpTip()
    {
        if (tipBar == null || tipText == null) return;
        int ts = TutorialStep();
        // Real progress moved - stop previewing and show what to do NOW. This
        // is what keeps the arrows from stranding a player on a tip they have
        // already completed.
        if (ts != tipStepSeen) { tipStepSeen = ts; tipView = -1; }
        // CareerTipStep returns TIP_COUNT for "nothing left to say", which
        // covers career-off, skipped, and finished in one test.
        bool show = ts < BuilderManager.TIP_COUNT;
        int view = tipView < 0 ? ts : Mathf.Clamp(tipView, 0, BuilderManager.TIP_COUNT - 1);
        if (show)
        {
            // Reading ahead or back is marked, so a previewed tip is never
            // mistaken for the thing the game is currently waiting on.
            tipText.text = TutorialTip(view)
                         + (view == ts ? "" : "   \u00b7   (reading ahead \u2014 you are on " + (ts + 1) + "/" + BuilderManager.TIP_COUNT + ")");
            tipText.color = view == ts ? new Color(0.62f, 0.84f, 1f) : new Color(0.72f, 0.72f, 0.80f);
            tipViewNow = view;
            SetArrow(tipPrev, view > 0);
            SetArrow(tipNext, view < BuilderManager.TIP_COUNT - 1);
        }
        if (tipBar.activeSelf != show) { tipBar.SetActive(show); ApplyDockH(); }
        if (!show) return;
        var prt = tipBar.GetComponent<RectTransform>();
        float y = -(BAR_H + ((msgBar != null && msgBar.activeSelf) ? MSG_H : 0f));
        if (Mathf.Abs(prt.anchoredPosition.y - y) > 0.5f) prt.anchoredPosition = new Vector2(0f, y);
    }

    /// <summary>An arrow at the end of the run reads dead instead of vanishing:
    /// a control that disappears moves everything next to it, and the row would
    /// reflow under the thumb mid-tap.</summary>
    int tipViewNow;
    const string TIP_FIRST = "This is the first tip \u2014 there is nothing before it.";
    const string TIP_LAST  = "This is the last tip \u2014 SKIP TIPS clears the row.";

    static void SetArrow(Button b, bool live)
    {
        if (b == null) return;
        // NOT interactable = false any more. A dead arrow that swallows the tap
        // is a button that cannot tell you it is the end of the run - which is
        // the whole of what owen asked for on 2026-08-03.
        var im = b.GetComponent<Image>();
        if (im != null) im.color = live ? new Color(0.16f,0.28f,0.40f,0.96f) : new Color(0.11f,0.13f,0.16f,0.96f);
        var t = b.GetComponentInChildren<Text>();
        if (t != null) t.color = live ? new Color(0.75f,0.90f,1f) : new Color(0.34f,0.36f,0.40f);
    }

    // OWEN 2026-08-02: "why clicking fight doesn't trigger anything in this
    // view". It did - it was refused, and the explanation lived in a bar at the
    // top of the screen for 7 s while his thumb was on a button at the bottom
    // right. The row now carries the reason itself, and the FIGHT button reads
    // unavailable, so the question is answered before the tap instead of after.
    //
    // Recomputed on a timer rather than baked at build time: RefreshCareerBoard
    // only rebuilds when the PART COUNT or material changes, so a shop purchase
    // (which changes what you own but not what is placed) would have left a
    // stale "needs 1x Engine" sitting under a row that had become legal.
    class FightGate { public Button btn; public Image img; public Text lbl; public string baseLabel; public int li, ci; }
    readonly List<FightGate> fightGates = new List<FightGate>();
    float fightGateAt = -99f;
    static readonly Color FIGHT_OK   = new Color(0.40f, 0.22f, 0.09f, 1f);
    static readonly Color FIGHT_DEAD = new Color(0.19f, 0.19f, 0.21f, 1f);

    // OWEN 2026-08-02: recomputed on a timer, not per frame - ActiveRobotDirty
    // builds a full snapshot string to compare, which is fine four times a
    // second and wasteful sixty.
    Button saveBtn;
    bool buildDirty;
    float dirtyAt = -99f;
    static readonly Color SAVE_DIRTY = new Color(0.20f, 0.45f, 0.65f, 1f);
    static readonly Color SAVE_CLEAN = new Color(0.16f, 0.18f, 0.22f, 0.96f);

    /// <summary>OWEN 2026-08-02: the mode banner used to float over this screen
    /// as an IMGUI box at a guessed fraction of screen height, and landed on the
    /// tip bar. A screen that owns its layout should say this IN its layout.
    /// Present on every tab, and impossible to overlap by construction.</summary>
    static string ModeTag()
    {
        // Short on purpose. The two CONSEQUENCES are each stated where they
        // actually bite - the ROBOTS tab carries "DRAFT MODE - everything
        // unlocked" with the CONVERT quote, and the LEAGUE rows now say
        // "draft - CONVERT to enroll" on the button that refuses. This tag only
        // has to answer "which mode am I in", on every tab, without wrapping
        // the status line.
        if (!Career.active) return "DEV SANDBOX  \u00b7  ";
        if (Career.Drafting) return "DRAFT  \u00b7  ";
        return "";
    }

    void PumpDirty(bool force)
    {
        if (bm == null) return;
        if (!force && Time.unscaledTime - dirtyAt < 0.25f) return;
        dirtyAt = Time.unscaledTime;
        buildDirty = bm.ActiveEditDirty();
        if (saveBtn != null)
        {
            var im = saveBtn.GetComponent<Image>();
            if (im != null) im.color = buildDirty ? SAVE_DIRTY : SAVE_CLEAN;
            var t = saveBtn.GetComponentInChildren<Text>();
            if (t != null) t.color = buildDirty ? Color.white : new Color(0.62f, 0.64f, 0.68f);
        }
    }

    void PumpFightGates()
    {
        if (bm == null || !Career.active || fightGates.Count == 0) return;
        if (Time.unscaledTime - fightGateAt < 0.25f) return;
        fightGateAt = Time.unscaledTime;
        for (int i = 0; i < fightGates.Count; i++)
        {
            var g = fightGates[i];
            if (g.btn == null || g.lbl == null) continue;
            string tag;
            bool blocked = bm.CareerFightBlocker(g.li, g.ci, out tag) != null;
            string want = blocked ? g.baseLabel + "   \u2014   " + tag : g.baseLabel;
            if (g.lbl.text != want) g.lbl.text = want;
            g.lbl.color = blocked ? new Color(1f, 0.72f, 0.36f) : Color.white;
            if (g.img != null) g.img.color = blocked ? FIGHT_DEAD : FIGHT_OK;
            var face = g.btn.GetComponentInChildren<Text>();
            // Deliberately still clickable. A dead button that swallows the tap
            // is the same complaint again; tapping a greyed one still pumps the
            // full sentence into the message bar for anyone who wants it.
            if (face != null) face.color = blocked ? new Color(0.52f, 0.52f, 0.56f) : Color.white;
        }
    }

    void PumpMessage(string msg)
    {
        if (msgBar == null || msgText == null) return;
        if (removeArmed)
        {
            msgText.color = new Color(1f, 0.52f, 0.42f);
            msgText.text = "REMOVE armed \u2014 tap a part on the robot to delete it (parts attached to it go too)";
            if (!msgBar.activeSelf) { msgBar.SetActive(true); ApplyDockH(); }
            return;
        }
        if (!string.IsNullOrEmpty(msg) && msg != msgSeen) { msgSeen = msg; msgAt = Time.unscaledTime; }
        bool live = !string.IsNullOrEmpty(msgSeen) && Time.unscaledTime - msgAt < MSG_LIFE;
        if (live) { msgText.color = new Color(1f, 0.82f, 0.25f); msgText.text = msgSeen; }
        if (msgBar.activeSelf != live) { msgBar.SetActive(live); ApplyDockH(); }
    }

    // R2 (critic finding 8): every vertical list ended flush against the bottom
    // of the screen on a full-width row - no scrollbar, no fade, no chevron, no
    // partial row peeking. LEAGUE showed 6 of 12 contests with the seventh row
    // sliced through the middle of its glyphs, SHOP 10 of 19, PARTS 9 of 11.
    // That is exactly the cue-less cliff round 1 fixed on the palette, so it
    // gets the same two-channel answer, applied to all four lists at once.
    readonly List<ScrollRect> listScrolls = new List<ScrollRect>();
    readonly List<GameObject> listFades = new List<GameObject>();
    void AddListOverflow(GameObject scrollGO, ScrollRect scroll, RectTransform vp)
    {
        var barGO = MkPanel("listbar", scrollGO.transform, new Color(0.10f,0.11f,0.14f,0.9f));
        var lbrt = barGO.GetComponent<RectTransform>();
        lbrt.anchorMin = new Vector2(1f,0f); lbrt.anchorMax = new Vector2(1f,1f); lbrt.pivot = new Vector2(1f,0.5f);
        lbrt.sizeDelta = new Vector2(8f, 0f); lbrt.anchoredPosition = Vector2.zero;
        var handleGO = MkPanel("handle", barGO.transform, new Color(0.35f,0.62f,0.85f,1f));
        var hrt = handleGO.GetComponent<RectTransform>(); Stretch(hrt);
        var sbar = barGO.AddComponent<Scrollbar>();
        sbar.direction = Scrollbar.Direction.BottomToTop;
        sbar.handleRect = hrt;
        sbar.targetGraphic = handleGO.GetComponent<Image>();
        scroll.verticalScrollbar = sbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        // rows must stop short of the bar or they run underneath it
        vp.offsetMax = new Vector2(-10f, vp.offsetMax.y);
        var fade = MkPanel("listfade", scrollGO.transform, new Color(0.05f,0.06f,0.08f,0.72f));
        fade.GetComponent<Image>().raycastTarget = false;
        var frt = fade.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0f,0f); frt.anchorMax = new Vector2(1f,0f); frt.pivot = new Vector2(0.5f,0f);
        frt.sizeDelta = new Vector2(-10f, 22f); frt.anchoredPosition = Vector2.zero;
        var chev = MkText("chev", fade.transform, "\u2304  more below", 15, TextAnchor.MiddleCenter);
        Stretch(chev.rectTransform);
        chev.color = new Color(0.62f, 0.80f, 0.98f, 1f);
        chev.raycastTarget = false;
        fade.SetActive(false);
        listScrolls.Add(scroll); listFades.Add(fade);
    }

    GameObject MkPanel(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = c.a > 0.001f;
        return go;
    }
    Text MkText(string name, Transform parent, string s, int size, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>(); t.font = Fnt(); t.text = s; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;   // R5: wrap is the safe default for list rows; the four callers that need Truncate override it, and MkButton forces Overflow on button faces
    }
    Button MkButton(string name, Transform parent, string label, int size, System.Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.16f,0.18f,0.22f,0.96f);
        var t = MkText("t", go.transform, label, size, TextAnchor.MiddleCenter); Stretch(t.rectTransform);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;   // R5: a button face must never wrap mid-label
        if (onClick != null) go.GetComponent<Button>().onClick.AddListener(() => onClick());
        return go.GetComponent<Button>();
    }
    void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }
}

/// <summary>Watches for a BuilderManager and attaches/removes the touch UI.</summary>
public class MobileBuilderWatch : MonoBehaviour
{
    void Update()
    {
        var wbm = Object.FindFirstObjectByType<BuilderManager>();
        bool builder = wbm != null;
        if (builder && wbm.PaletteCount > 0 && MobileBuilderUI.inst == null && MobileBuilderUI.ShouldActivate())
            new GameObject("mobile_builder_ui").AddComponent<MobileBuilderUI>();
        else if (!builder && MobileBuilderUI.inst != null)
            Destroy(MobileBuilderUI.inst.gameObject);
    }
}

/// <summary>C3: slow turntable for the scouting display bot.</summary>
public class ScoutSpin : MonoBehaviour
{
    void Update() { transform.Rotate(0f, 40f * Time.deltaTime, 0f); }
}

}
