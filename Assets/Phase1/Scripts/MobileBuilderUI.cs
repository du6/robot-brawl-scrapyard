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

/// <summary>Hands a drag that STARTED ON A TEXT FIELD to the list the field
/// sits in — 2026-08-10.
///
/// Unity's InputField implements IBeginDrag/IDrag/IEndDrag for text selection,
/// so it CONSUMES the drag and the enclosing ScrollRect never sees it. In a
/// scrolling panel made mostly of fields that reads as a dead list: the finger
/// happens to land on a field, and nothing moves. Measured in the editor before
/// this existed, same gesture, two start points:
///
///     from the FIELD  top hit=fill   handler=box                content y 0.0 -> 0.0
///     from the PANEL  top hit=viewport handler=arenaaccountscroll  content y 0.0 -> 32.8
///
/// ⚠ ONLY WHEN THE FIELD IS NOT FOCUSED. A focused field keeps its own drag,
/// because that is how you select text in it, and stealing that would trade one
/// broken gesture for another. So this relays the "I meant to scroll the list"
/// case and leaves the "I meant to select in this field" case exactly as Unity
/// shipped it — the same division ProgramDragHandle makes for the program
/// canvas, where a drag on a handle moves structure and a drag anywhere else
/// scrolls.
///
/// The relay is deliberately dumb: it forwards the SAME PointerEventData, so
/// the ScrollRect computes its own delta from the pointer, and there is no
/// second copy of scroll maths to drift.</summary>
public class FieldScrollRelay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public InputField field;
    public ScrollRect target;
    bool relaying;

    bool ShouldRelay { get { return target != null && (field == null || !field.isFocused); } }

    public void OnBeginDrag(PointerEventData e)
    {
        relaying = ShouldRelay;
        if (relaying) ExecuteEvents.Execute(target.gameObject, e, ExecuteEvents.beginDragHandler);
    }
    public void OnDrag(PointerEventData e)
    {
        if (relaying) ExecuteEvents.Execute(target.gameObject, e, ExecuteEvents.dragHandler);
    }
    public void OnEndDrag(PointerEventData e)
    {
        // Ends what it began even if focus changed mid-drag: a ScrollRect that
        // is begun and never ended keeps the content held by the pointer.
        if (relaying) ExecuteEvents.Execute(target.gameObject, e, ExecuteEvents.endDragHandler);
        relaying = false;
    }
}

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
    class ShopPartRow { public int part; public GameObject go; public Text lbl; public bool open;
                        public GameObject desc; public Text descT; public LayoutElement descLe; }
    class ShopMatRow  { public int part; public string mat; public GameObject go; public Text lbl;
                        public Button buy, sell; public Text buyT, sellT; }
    readonly List<ShopPartRow> shopParts = new List<ShopPartRow>();
    readonly List<ShopMatRow> shopMats = new List<ShopMatRow>();
    readonly List<Button> tabBtns = new List<Button>();
    int armSell = -1; string armSellMat = "";
    string shopNote = ""; bool shopNoteBad;

    // ---- ONE SHOP, TWO SHELVES (owen, 2026-08-10) -----------------------
    // There were two shops: this tab, selling PARTS for career SCRAP, and a
    // second one inside ArenaScreen selling COSMETICS for the LADDER WALLET.
    // Two shops is a navigation bug, not a design — but they are NOT the same
    // shop, and merging the currencies would delete a decision: §2.3 makes the
    // ladder wallet one-way into career scrap on purpose. So the LOCATION is
    // consolidated and the two balances are kept apart, with the one-way valve
    // sitting at the seam between them — which is also the only place a player
    // ever sees both numbers at once, and so the only place the valve is
    // legible. It used to be buried in a shop nobody could open.
    int shopSection;                       // 0 = PARTS, 1 = COSMETICS
    GameObject shopPartsRoot, cosmeticsRoot;
    Transform cosmeticsContent;
    Button shopSecParts, shopSecCosmetics;
    LayoutElement shopSecLE;               // re-sized in ApplyTouchSizes, like every other row
    readonly List<Cosmetic> cosmetics = new List<Cosmetic>();
    long ladderBalance = -1;               // -1 = not read yet, never "0"
    string depositText = "";
    // A STABLE idempotency key per deposit ATTEMPT. Regenerating per retry
    // defeats the guarantee: the API uses it to make a dropped response safe
    // to retry, so the retry must carry the SAME key. Rolled only on success.
    string depositKey = System.Guid.NewGuid().ToString();
    bool cosmeticsBusy;
    // ---- C3: career league board ----
    GameObject careerBoard; Transform careerBoardContent;
    Button ladderBtn, exhibBtn;
    // ---- C4: workshop ----
    GameObject robotsPanel;
    Transform robotsContent;
    // ---- MEDALS (2026-08-02, owen): the trophy case ----
    // Was appended as index 5 precisely so nothing had to be renumbered.
    // Removing PARTS (owen, 2026-08-05) is the renumbering that note was
    // avoiding, so it was done in ONE pass across every index-keyed site:
    // the `names` array, LayoutTabs' count, and all three arms of ShowTab.
    //
    // ⚠ THAT PASS MISSED TabHint(), and nobody noticed for five days: its
    // case 4 still answered with the deleted PARTS tab's hint and case 5 with
    // the trophy case's, so both tabs described the wrong screen. Fixed
    // 2026-08-10. When you renumber, grep for the INDEX, not the name — the
    // sites that break are the ones that never mentioned "TROPHIES" at all.
    //
    // ---- ARENA takes index 4 (owen, 2026-08-10) ----
    // The trophy case is gone from the dock and its content moved INTO the
    // LEAGUE board, where the medals are actually earned: a league that has
    // been swept now says so on its own header. Nothing became unreachable,
    // which was the whole risk of removing a tab that owned live content.
    //
    // ARENA has no UGUI panel because ArenaScreen is IMGUI — it draws itself
    // in OnGUI, over everything. The tab therefore toggles the COMPONENT
    // rather than a panel GameObject. Built lazily: the ladder screen hits
    // the network in Start(), and constructing it for a player who never
    // opens the tab would be a request nobody asked for.
    ArenaScreen arenaScreen;
    // ---- the ARENA, ported to UGUI one surface at a time -----------------
    // The BOARD is first: it is the surface the tab opens on, the one every
    // other ARENA surface is reached THROUGH, and the one a player looks at
    // for longest. Whatever is not ported yet still draws in IMGUI —
    // ArenaScreen.SuppressImgui is only set once the dock can render the
    // surface being shown, so nothing is ever simply missing.
    GameObject arenaPanel;
    Transform arenaBoardContent;
    Text arenaStatus;
    readonly List<Button> arenaCatBtns = new List<Button>();
    LayoutElement arenaCatLE;
    int arenaBoardStamp = -1;   // rebuild the rows only when the board changes
    // ⚠ REPAINTING IS NOT REFETCHING — the ARENA's launch blocker, 2026-08-10.
    //
    // Opening the tab did `arenaBoardStamp = -1; RefreshArena();`, which forces
    // a REDRAW of the model ArenaScreen already holds. The only thing that ever
    // asked the server anything was ArenaScreen.Start(), which runs ONCE, on
    // first open. So the board a player saw the first time they opened the
    // ARENA was the board they saw for the rest of the session — on a screen
    // whose entire premise is "enlist, go away, come back and see what
    // happened". Matches settled, ratings moved, challenges arrived, and the
    // dock showed none of it.
    //
    // The gate is elapsed REALTIME, not a frame count or a stamp, because the
    // thing being rationed is a network request and the thing that triggers it
    // is a human hand: flicking BUILD/ARENA/BUILD must not fire three GETs, and
    // coming back from a fight must not show yesterday's board. 20 s is chosen
    // to sit well inside the worker's 5-minute scheduling period — anything
    // that landed server-side while you were away is at least a scheduler
    // period old, so a refetch on any real return is not merely cheap, it is
    // the only way to see it.
    //
    // RefreshNow() already no-ops while a fetch is in flight; there is no
    // second guard here on purpose. Two places deciding whether the ladder is
    // busy is how they come to disagree.
    //
    // The window is PUBLIC so ReturningPlayerBench waits on the product's own
    // number instead of a copy of it. Two constants that have to agree, kept in
    // two files, is the shape of half the bugs in this repo.
    float arenaFetchAt = -999f;
    public const float ARENA_REFETCH_S = 20f;
    // Surface 2: the scouting card and the challenge flow. It replaces the
    // board list rather than floating over it — the dock has one column and a
    // card that covers the thing it came from is how you lose your place.
    GameObject arenaBoardRoot;
    GameObject arenaCardRoot;
    Transform arenaCardContent;
    string arenaCardStamp = "";
    // Surface 3: MY FIGHTS and the replay launcher.
    GameObject arenaInboxRoot, arenaCatRow;
    Transform arenaInboxContent;
    Button arenaSecBoard, arenaSecInbox;
    LayoutElement arenaSecLE;
    string arenaInboxStamp = "";
    // Surfaces 4 and 5: ENLIST, and the account. One root, because they are
    // the same errand seen from either side of being signed in — you cannot
    // enlist without an account, and an account exists in order to enlist.
    GameObject arenaAccountRoot;
    Transform arenaAccountContent;
    string arenaAccountStamp = "";
    Button arenaSecAccount;
    // ---- P3a: the Program Bench canvas (career-only tab, index 5) ----
    GameObject programPanel; ProgramCanvas programCanvas;

    /// <summary>⚠ THE ONE LIST OF TAB PANELS. Every pass that treats "the tab
    /// panels" as a set MUST iterate this and never a hand-written array.
    ///
    /// It exists because there were two such arrays and they disagreed. The
    /// creation loop listed SEVEN panels; the safe-area inset pass in
    /// ApplyTouchSizes listed SIX, omitting programPanel — so the PROGRAM tab
    /// received no notch inset at all and its content started ~70 px left of
    /// every other tab. On a notched iPhone in landscape that put the title,
    /// the hint and two buttons under the Dynamic Island: "PR[#]GRAM",
    /// "[#]GRAMS", and a green button reduced to "[#]HAT" that could not be
    /// identified. Career-only, i.e. every tester in the whole of career mode.
    ///
    /// ⚠ ADDING "programPanel" AS A SEVENTH NAME WOULD HAVE BEEN THE FIX THAT
    /// RECREATES THIS BUG — the next panel would be added to one array and not
    /// the other, exactly as this one was. House rule 1: measure over
    /// EVERYTHING, not over a named list. One field, two consumers.
    ///
    /// (The touch-floor pass in the same method has the SAME disease over a
    /// different named set — see the ApplyTouchSizes font list and #8. That one
    /// is a list of TEXT elements, not panels, so this field does not fix it;
    /// it is the same lesson needing its own application.)</summary>
    GameObject[] tabPanels;
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
        statsRt = brt;
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
        msgBarRt = mrt;
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
        tipBarRt = prt;
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
        string[] names = { "BUILD", "FIGHT", "GARAGE", "SHOP", "ARENA", "PROGRAM" };   // P3a: index 5, career-only
        tabBtns.Clear();
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            // OWEN 2026-08-04, walkthrough: tapping LEAGUE while the dock was
            // collapsed switched the tab and the hint - "pick a contest" - and
            // showed no contests. Every tab except BUILD is nothing BUT its
            // panel, so collapsed is a dead end there, and BUILD's palette is
            // the reason you would tap BUILD too. Tapping a tab means "show me
            // this", so it opens.
            var tb = MkButton("tab" + i, dock.transform, names[i], 20, () =>
            { if (!dockOpen) { tab = idx; SetDockOpen(true); } else ShowTab(idx); });
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
        arenaPanel = MkPanel("arena", dock.transform, new Color(0f, 0f, 0f, 0f));
        programPanel = MkPanel("program", dock.transform, new Color(0f, 0f, 0f, 0f));   // P3a
        tabPanels = new[] { buildPanel, fightPanel, garagePanel, shopPanel,
                            robotsPanel, arenaPanel, programPanel };
        foreach (var p in tabPanels)
        {
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f); rt.offsetMax = new Vector2(-6f, -48f);
        }
        BuildBuildTab();
        BuildScoutCard();
        BuildFightTab();
        // C6.5: the sandbox garage is not merely HIDDEN in career - it is
        // never built. An inactive-but-alive "SAVE A" button under the career
        // UI cost the owner a garage slot in C4 (label-match incident); an
        // empty panel has no buttons to mis-hit.
        if (!Career.active) BuildGarageTab();
        BuildShopTab();
        BuildRobotsTab();
        BuildArenaTab();
        // P3a: career-only, the C6.5 rule (never build career UI the sandbox
        // can mis-hit; the garage is the same rule in reverse).
        if (Career.active) BuildProgramTab();
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
        // R8 (owen 2026-08-04): "fold material row into one chooser".
        //
        // Seven material chips had a permanent 44 pt row of their own, which on
        // a 2.5-inch landscape phone is 11% of the screen spent on a setting
        // you change a few times a build. Folding it into the action row costs
        // NO row at all - the chooser is a sixth button there - and the chips
        // themselves become a sheet that borrows the palette's space while it
        // is open. Dock 58.9% -> 48.1% with nothing made smaller.
        //
        // Over the palette rather than above the dock deliberately: a sheet
        // that floats above the dock would push into the build view, and this
        // whole loop has been about giving that space back. The palette is also
        // the right thing to cover - you are choosing what the NEXT part is
        // made of, so the parts are exactly what you are not reading.
        var matSheet = MkPanel("matsheet", buildPanel.transform, new Color(0.07f,0.08f,0.11f,0.98f));
        matSheetGO = matSheet;
        var msr = matSheet.GetComponent<RectTransform>();
        msr.anchorMin = new Vector2(0f,0f); msr.anchorMax = new Vector2(1f,1f);
        matSheetRt = msr;

        var matRow = MkPanel("mats", matSheet.transform, new Color(0f,0f,0f,0f));
        var mr = matRow.GetComponent<RectTransform>();
        mr.anchorMin = new Vector2(0f,0.5f); mr.anchorMax = new Vector2(1f,0.5f); mr.pivot = new Vector2(0.5f,0.5f);
        mr.sizeDelta = new Vector2(-12f,40f); mr.anchoredPosition = Vector2.zero;
        matRowRt = mr;
        var mh = matRow.AddComponent<HorizontalLayoutGroup>(); mh.spacing = 4f; mh.childForceExpandWidth = true; mh.childForceExpandHeight = true;
        foreach (var key in MatDB.Order)
        {
            string k = key;
            var mb = MkButton("mat_"+k, matRow.transform, MatDB.Get(k).name, 15, () => PickMat(k));
            matButtons.Add(mb); matKeys.Add(k);
        }
        matSheet.SetActive(false);
        // action row (bottom)
        var actRow = MkPanel("acts", buildPanel.transform, new Color(0f,0f,0f,0f));
        var ar = actRow.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(0f,0f); ar.anchorMax = new Vector2(1f,0f); ar.pivot = new Vector2(0.5f,0f);
        ar.sizeDelta = new Vector2(0f,42f); ar.anchoredPosition = Vector2.zero;
        actRowRt = ar;
        var ah = actRow.AddComponent<HorizontalLayoutGroup>(); ah.spacing = 6f; ah.childForceExpandWidth = true; ah.childForceExpandHeight = true;
        // First slot, because it is the only one of these that changes what the
        // NEXT tap does - the rest act on what is already there.
        matBtn = MkButton("matbtn", actRow.transform, "MATERIAL", 17, ToggleMatSheet);
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
        sr.offsetMin = new Vector2(0f,46f); sr.offsetMax = new Vector2(0f,-2f);
        partScrollRt = sr;
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
        // Two rows put all 19 in roughly one screen-width (10 columns).
        //
        // THREE rows now (owen, 2026-08-05). Two was too narrow for the tile
        // text and had been silently eating it: a tile is ONE finger row tall,
        // which is two lines, and the three PINNED parts - Battery, Gyro
        // stabilizer, Wheel - carry four fields (name, mass, pinned material,
        // free stock) because R2 added the material word and R4 added the
        // stock badge. MEASURED on the iPhone: "Gyro stabilizer / 37 kg
        // Aluminum  0 free" wants 44.6 units in a 30-unit box even after
        // best-fit has bottomed out at 8 pt, so the third line was TRUNCATED -
        // the stock count was invisible on exactly the three parts you run out
        // of. Every two-line rewording was measured and every one still
        // overflowed; line two's budget is about 14 characters and "14 kg
        // Rubber" alone is 12.
        //
        // 19 parts over 3 rows is 7 columns, so the cells come out ~40% wider
        // and all four fields fit at full size. It costs one palette row of
        // dock height, which DockH(0) now accounts for.
        //
        // This was found by the C19 legibility sweep, not by eye - and only
        // once builds stopped saving as drafts. Draft mode hides the stock
        // badge (FreeParts), so the tiles fitted for as long as the shared
        // pool was quietly demoting robots to drafts.
        var grid = content.AddComponent<GridLayoutGroup>();
        // 128 was measured wrong the first time: at 2640x1656 the scaler is
        // ~2.178, so the canvas is only ~1212 units wide, the viewport ~1200,
        // and ten 128-wide columns need 1342. Wheel (index 9, top of the last
        // column) stayed off-screen - the exact part the palette had to
        // surface. 112 makes the whole 19-part palette fit with ~18 units to
        // spare, verified by screenshot, not by arithmetic alone.
        grid.cellSize = new Vector2(112f, 50f);
        partGrid = grid;
        grid.spacing = new Vector2(6f, 4f);
        // VERTICAL PADDING ZERO, and that is the whole fix for owen's
        // 2026-08-07 report that the bottom palette row looked cut off.
        //
        // Four rows of 34 plus three 4-unit gaps is 148, which is exactly the
        // viewport. Add 4+4 of vertical padding and the content is 156 into a
        // 148 hole, so the bottom row lost 8 units — the second line of every
        // tile, which is where the free-stock count lives. The scroller is
        // HORIZONTAL-only, so no swipe ever revealed it.
        //
        // The first fix grew the dock by the missing 8 instead. It worked, and
        // it was WRONG: a taller dock covers more of the build area, so parts
        // could no longer be placed where they used to be. CareerSmoke went
        // 127/127 -> 112/127, fifteen failures across buying, selling,
        // placement, enrollment, purses and draft mode. Measured A/B with the
        // correction pinned off. Giving the palette room by taking it from the
        // BUILD AREA is a bad trade; giving it room by dropping cosmetic
        // padding inside its own viewport costs nothing at all.
        //
        // Horizontal padding stays: that axis scrolls, so it cannot clip.
        grid.padding = new RectOffset(4,4,0,0);
        grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        // P1 (2026-08-05): 3 -> 4. The five sensor parts take the catalogue to
        // 24; at three rows that is 8 columns and the viewport-derived cell
        // width lands ~131 units on the iPad — UNDER the 150 the 2026-08-05
        // three-row change measured as the minimum that fits a pinned part's
        // two-line label (29.6 into a 30 box at 150). Four rows is 6 columns
        // → ~176-unit cells, wider than before, still no palette scroll.
        // Costs one more dock row; DockH(0) accounts for it, 0.68 cap
        // unchanged. Flagged for the iPad device pass.
        grid.constraintCount = 4;
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

    GameObject matSheetGO;
    RectTransform matSheetRt;
    Button matBtn;

    // OWEN 2026-08-04: "remove the color box on each material button, since
    // they don't reflect real material color anyway."
    //
    // He is right, and the justification I gave for adding them was the weak
    // part. I argued auditColor was fine because it is CATEGORICAL rather than
    // realistic - but a colour chip sitting immediately left of the word
    // ALUMINUM does not read as a category key, it reads as a claim about what
    // aluminium looks like. Steel as dark blue and titanium as orange do not
    // merely fail to inform; they misinform, and a wrong signal costs more than
    // an absent one. The label already names the material, the caret already
    // says the control opens, and MatDB.auditColor still does its real job in
    // the builder's material VIEW, where it is a legend and nothing is claiming
    // to be a photograph.

    GameObject scoutCard;
    Text scoutTitleTx, scoutStatsTx, scoutBlurbTx;
    Button scoutFight, scoutBack;

    /// <summary>Is the uGUI scouting card the one on screen? BuilderManager
    /// checks this before drawing its IMGUI version, so exactly one of them
    /// draws.</summary>
    public static bool ScoutCardLive
    {
        get { return inst != null && inst.scoutCard != null && inst.scoutCard.activeSelf; }
    }

    /// <summary>Build the scouting card. uGUI, not IMGUI, because IMGUI cannot
    /// be pressed under the Device Simulator (no mouse device) - and scouting
    /// is a dead end without its buttons.</summary>
    void BuildScoutCard()
    {
        scoutCard = MkPanel("scoutcard", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 0.96f));
        var rt = scoutCard.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 150f);
        rt.anchoredPosition = Vector2.zero;

        scoutTitleTx = MkText("scouttitle", scoutCard.transform, "", 22, TextAnchor.UpperLeft);
        scoutStatsTx = MkText("scoutstats", scoutCard.transform, "", 19, TextAnchor.UpperLeft);
        scoutBlurbTx = MkText("scoutblurb", scoutCard.transform, "", 19, TextAnchor.UpperLeft);
        scoutTitleTx.fontStyle = FontStyle.Bold;
        scoutTitleTx.color = new Color(0.82f, 0.90f, 1f);
        scoutStatsTx.color = new Color(0.94f, 0.95f, 0.98f);
        scoutBlurbTx.color = new Color(0.94f, 0.95f, 0.98f);
        foreach (var t in new[] { scoutTitleTx, scoutStatsTx, scoutBlurbTx })
        {
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
        }

        scoutFight = MkButton("scoutfight", scoutCard.transform, "FIGHT \u25b8", 18, () =>
        {
            if (bm == null) return;
            int li = bm.ScoutLeague, ci = bm.ScoutContest;
            bm.EndScout();
            if (li >= 0) bm.StartCareerFight(li, ci);
        });
        scoutBack = MkButton("scoutback", scoutCard.transform, "BACK", 18, () =>
        { if (bm != null) bm.EndScout(); });
        var fi = scoutFight.GetComponent<UnityEngine.UI.Image>();
        if (fi != null) fi.color = new Color(0.72f, 0.45f, 0.17f, 1f);
        scoutCard.SetActive(false);
    }

    /// <summary>Lay the card out inside the safe area and fill it in. Sized
    /// from the text, so a long arena name wraps instead of losing the end -
    /// and the end is where the hazards are, which is what you paid to see.</summary>
    void PumpScoutCard()
    {
        if (scoutCard == null || bm == null) return;
        bool want = bm.Scouting;
        if (scoutCard.activeSelf != want) scoutCard.SetActive(want);
        // The card is the screen while it is up. Leaving the status bar live
        // underneath it let the build status print THROUGH the card - two
        // sentences in the same pixels, neither readable.
        if (statsRt != null && statsRt.gameObject.activeSelf == want)
            statsRt.gameObject.SetActive(!want);
        if (!want) return;

        float R = TouchRow();
        float cw = CanvasW;
        float inner = Mathf.Max(120f, cw - safeL - safeR - 28f);

        scoutTitleTx.text = bm.ScoutTitle;
        scoutStatsTx.text = bm.ScoutStats;
        scoutBlurbTx.text = bm.ScoutBlurb;
        foreach (var t in new[] { scoutTitleTx, scoutStatsTx, scoutBlurbTx })
        {
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(0f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.sizeDelta = new Vector2(inner, t.preferredHeight);
        }
        scoutTitleTx.fontSize = FontUnits(15f);
        scoutStatsTx.fontSize = FontUnits(12f);
        scoutBlurbTx.fontSize = FontUnits(12f);

        float y = -(safeT + 10f);
        float x = safeL + 14f;
        scoutTitleTx.rectTransform.anchoredPosition = new Vector2(x, y);
        y -= scoutTitleTx.preferredHeight + 6f;
        scoutStatsTx.rectTransform.anchoredPosition = new Vector2(x, y);
        y -= scoutStatsTx.preferredHeight + 4f;
        scoutBlurbTx.rectTransform.anchoredPosition = new Vector2(x, y);
        y -= scoutBlurbTx.preferredHeight + 10f;

        float bw2 = Mathf.Max(120f, R * 2.6f);
        foreach (var b in new[] { scoutFight, scoutBack })
        {
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(1f, 1f); brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(1f, 1f);
            brt.sizeDelta = new Vector2(bw2, R);
        }
        scoutBack.GetComponent<RectTransform>().anchoredPosition =
            new Vector2(-(safeR + 14f), y);
        scoutFight.GetComponent<RectTransform>().anchoredPosition =
            new Vector2(-(safeR + 14f + bw2 + 10f), y);
        scoutFight.gameObject.SetActive(bm.ScoutLeague >= 0);

        var crt = scoutCard.GetComponent<RectTransform>();
        float cardH = -y + R + 14f;
        crt.sizeDelta = new Vector2(0f, cardH);

        // Frame the scouted machine in what the card LEAVES, the same way the
        // builder frames yours. Walkthrough finding: the preview sat behind the
        // card with a wheel poking out below it - you pay 50 scrap to look at an
        // opponent and got its tyre. The lens shift already exists and reads
        // these two numbers; scouting just never set them.
        float chS = CanvasW > 1f && canvas != null
                  ? canvas.GetComponent<RectTransform>().rect.height : 0f;
        if (chS > 100f)
        {
            coverBottom = 0f;                          // the dock is down
            coverTop = Mathf.Clamp01(cardH / chS);
        }
    }

    public void ToggleMatSheet() { SetMatSheet(matSheetGO == null || !matSheetGO.activeSelf); }

    /// <summary>Show or hide the material sheet. Public so the suite can reach
    /// the chips, which are no longer on screen by default - a test that can
    /// only see what happens to be visible stops testing the rest.</summary>
    public void SetMatSheet(bool v)
    {
        if (matSheetGO != null)
        {
            // OWEN 2026-08-04: "when I click aluminum I see the materials
            // behind the menu". Draw order. uGUI paints siblings in order, and
            // this sheet is built BEFORE the palette scroll, so every part tile
            // is a later sibling and paints over it - the sheet was only
            // visible through the gaps between tiles.
            //
            // Re-asserted on every open rather than fixed once at build time:
            // sibling order is not private to this method. Anything that
            // rebuilds or re-parents the palette later would silently put the
            // sheet back underneath, and the symptom is subtle enough that it
            // shipped once already.
            if (v) matSheetGO.transform.SetAsLastSibling();
            matSheetGO.SetActive(v);
        }
        RefreshMats();
    }

    /// <summary>Test hook: is the open sheet actually ON TOP of the palette?
    /// C17 asserted the chips EXIST, which stayed true while they were being
    /// painted over - the same shape of gap as testing a button through its
    /// API instead of looking at it.</summary>
    public bool MatSheetDrawsOnTop
    {
        get
        {
            if (matSheetGO == null || partScrollRt == null) return false;
            if (matSheetGO.transform.parent != partScrollRt.parent) return false;
            return matSheetGO.transform.GetSiblingIndex() > partScrollRt.GetSiblingIndex();
        }
    }

    public bool MatSheetOpen { get { return matSheetGO != null && matSheetGO.activeSelf; } }

    /// <summary>Test hook: the canvas scale factor, so the suite can convert a
    /// font size into physical points the same way the UI does.</summary>
    public float CanvasScaleForTest { get { return canvas != null ? canvas.scaleFactor : 0f; } }

    /// <summary>What the chooser currently reads. Test hook - the affordance
    /// IS the label, so the suite has to look at the actual string.</summary>
    public string MatButtonLabel
    {
        get
        {
            if (matBtn == null) return "";
            var t = matBtn.GetComponentInChildren<UnityEngine.UI.Text>();
            return t != null ? t.text : "";
        }
    }

    /// <summary>Size the notice bar to the notice. Clamped to three lines: a
    /// bar that can grow without limit is a bar that can cover the robot.</summary>
    void FitMsgBar()
    {
        if (msgBarRt == null || msgText == null) return;
        float lineish = Mathf.Max(14f, msgText.fontSize * 1.35f);
        float want = msgText.preferredHeight + 12f;
        float h = Mathf.Clamp(want, MSG_H, lineish * 3f + 12f);
        if (Mathf.Abs(msgBarRt.sizeDelta.y - h) > 0.5f)
        {
            msgBarRt.sizeDelta = new Vector2(0f, h);
            if (tipBarRt != null && tipBar != null && tipBar.activeSelf)
                tipBarRt.anchoredPosition = new Vector2(0f, -(BarH + h));
            ApplyDockH();
        }
    }

    /// <summary>Size the tip bar to the tip. Twin of FitMsgBar - the sweep
    /// found this one immediately after the notice bar was fixed, which is the
    /// argument for the sweep: the same defect existed twice and I had looked
    /// straight at both bars several times without seeing either.
    ///
    /// Tips are full sentences and the row also carries two arrows and SKIP
    /// TIPS, so the text gets a narrower box than the bar - it reaches a second
    /// line sooner than the notices do.</summary>
    void FitTipBar()
    {
        if (tipBarRt == null || tipText == null) return;
        float lineish = Mathf.Max(14f, tipText.fontSize * 1.35f);
        float want = tipText.preferredHeight + 12f;
        float h = Mathf.Clamp(want, TIP_H, lineish * 3f + 12f);
        if (Mathf.Abs(tipBarRt.sizeDelta.y - h) > 0.5f)
        {
            tipBarRt.sizeDelta = new Vector2(0f, h);
            ApplyDockH();
        }
    }

    void PickMat(string k)
    {
        if (Progression.MatUnlocked(k)) { if (bm != null) bm.ActiveMatKey = k; }
        else if (Progression.TryBuyMat(k)) { if (bm != null) bm.ActiveMatKey = k; }
        // Close on pick. Leaving it open would mean the sheet covers the
        // palette at the exact moment you go to choose the part it applies to.
        SetMatSheet(false);
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
        if (stock >= 0)
        {
            t += "  " + Mathf.Max(0, stock) + " free";
            // ⚠ "0 free" WAS A LIE FOR A PART YOU OWN IN ANOTHER MATERIAL —
            // 2026-08-10. CareerRemaining is keyed on the material the SELECTOR
            // is set to, so an owned Steel wedge read "Wedge 28 kg 0 free" with
            // the chips on Aluminum — the 28 kg confirming which key was
            // consulted. Measured on device; same effect on the ABS armour
            // plate. This is Career.ResolveMat's documented shape (":129-135",
            // the four wheels a new player owned and could not see) surfacing
            // in the palette BADGE rather than the grant path.
            //
            // The badge is the half that lies, so the badge is the half fixed.
            // The GREY stays: it answers "can I place this right now", and the
            // honest answer in the wrong material is still no. Ungreying would
            // make an unplaceable tile look placeable, which trades one wrong
            // signal for another — and a third intermediate tint is a design
            // decision nobody asked for. The badge now names the remedy instead.
            if (stock == 0 && bm.PartPinnedMat(i) == null)
            {
                string cur = MatDB.Canon(bm.PartMatKey(i));
                foreach (var m in MatDB.Order)
                {
                    if (MatDB.Canon(m) == cur) continue;
                    int other = bm.CareerRemainingMat(i, m);
                    if (other > 0) { t += " · " + other + " in " + MatDB.Get(m).name; break; }
                }
            }
        }
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
        // The chooser has to READ as a chooser: a button labelled "MATERIAL"
        // next to ROTATE and UNDO looks like another verb. Naming the current
        // material is also the only place that state is now visible at all,
        // since the chips it used to live on are behind the sheet.
        //
        // OWEN 2026-08-04: "should we add some indicator to the aluminum button
        // to tell users that this can be expanded to a list of materials?"
        //
        // Yes - and the first version was worse than missing an affordance. It
        // swapped the label to "CLOSE" when open, so the one place the selected
        // material was visible went blank at the exact moment you were changing
        // it, and the button stopped being a readout at all for as long as it
        // mattered. The NAME now stays put in both states and only the caret
        // moves, which is the part that should carry the state.
        //
        // The caret points UP because the sheet opens upward, over the palette.
        // A disclosure arrow that points the wrong way is worse than none: it
        // is a promise about where to look.
        if (matBtn != null)
        {
            var bt = matBtn.GetComponentInChildren<Text>();
            if (bt != null)
                bt.text = MatDB.Get(cur).name.ToUpper()
                        + (MatSheetOpen ? "  \u25be" : "  \u25b4");
            // C19: the label just changed — keep its width honest, or a
            // long material name re-clips until the next scale re-apply.
            if (bt != null)
            {
                var mle = matBtn.GetComponent<LayoutElement>();
                if (mle == null) mle = matBtn.gameObject.AddComponent<LayoutElement>();
                mle.minWidth = bt.preferredWidth + 12f;
                mle.flexibleWidth = 1f;
            }
            var bi = matBtn.GetComponent<Image>();
            if (bi != null)
                bi.color = MatSheetOpen ? new Color(0.20f,0.45f,0.65f,1f)
                                        : new Color(0.16f,0.18f,0.22f,0.96f);
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
        // ⚠ LAST SIBLING, OR THE BOTTOM HALF OF THIS BUTTON IS DEAD — 2026-08-12.
        //
        // careerBoard and its cbviewport are created BELOW this line, so they
        // were later siblings: drawn on top, and hit-tested first. cbviewport is
        // MkPanel(..., alpha 0.15), and MkPanel sets raycastTarget = a > 0.001,
        // so a near-invisible scroll viewport sat over this chip and ate the
        // taps. MEASURED, raycast ladder down the button's own rect:
        //
        //   y  0-50%   top hit 'cbviewport'  -> click handler none   DEAD
        //   y 60-100%  top hit 'test'        -> click handler 'test' live
        //   overlap 109px of 197px = 55%, from the bottom up
        //   live band 88px = 31.1pt, under the 44pt floor
        //
        // The rect was never the problem — it is 69.9pt, comfortably over the
        // floor, which is why the touch-floor check passed it. Nothing in this
        // project measures whether one control is drawn on top of another.
        //
        // SetAsLastSibling is this file's existing treatment for a floating
        // overlay (saveDlg, matSheetGO), not a new idea. It also stops the
        // 0.15 wash rendering over the chip, so the button reads at full
        // opacity again.
        //
        // ⚠ DO NOT "fix" this by clearing cbviewport.raycastTarget. A
        // ScrollRect's viewport must receive drags; that would trade a dead
        // button for a dead board — the #13 lesson running backwards.
        //
        // ⚠ Two consequences, deliberate: the board's rows still scroll BENEATH
        // this rect (input is fixed, the overlap is not), and a drag that starts
        // on this rect now belongs to the button, so a board scroll cannot be
        // begun from this corner. Correct for a button; recorded because it is a
        // real behaviour change.
        //
        // ⚠ THE SetAsLastSibling CALL IS AT THE END OF THIS METHOD, NOT HERE.
        // It was here first and it did NOTHING: careerBoard and cbviewport are
        // created BELOW, so they became later siblings again immediately.
        // Measured — sibling index still 3, live 5 / dead 6, byte-identical to
        // the broken state. "Last" is only meaningful once every sibling exists.
        var trt = tdb.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(1f, 1f); trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(1f, 1f);
        // 30 was the last sub-44 pt control left in the touch UI - measured at
        // 19.1 pt, the smallest tap target in the game, on a button that starts
        // a test drive. It is anchored rather than laid out, so no row height
        // reached it; it needs the physical size stated on its own.
        trt.sizeDelta = new Vector2(160f, TouchRow());
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

        // ⚠ LAST LINE OF THIS METHOD, AND IT MUST STAY LAST. See the TEST DRIVE
        // block above for why the button needs to be the final sibling: the
        // board's viewport is raycastTarget at alpha 0.15 and was covering the
        // chip's bottom 55%, eating the taps that start a test drive.
        //
        // It belongs HERE rather than beside the button because careerBoard and
        // cbviewport are created after it — putting the call at the creation
        // site left the sibling index unchanged and the button just as dead.
        // Anything added to fightPanel below this line re-opens the defect.
        tdb.transform.SetAsLastSibling();
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

    // P4 polish (V2.7): the autonomy mark is DRAWN now. LegacyRuntime.ttf
    // has no gear glyph, which is why the row used to say "[AUTO]" in ASCII -
    // the bench's Contains() check passed while the SCREEN showed nothing.
    // This rasterizes a 6-tooth gear ONCE; every row shares the one sprite.
    static Sprite autoMarkSprite;
    static Sprite AutoMarkSprite()
    {
        if (autoMarkSprite != null) return autoMarkSprite;
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[N * N];
        float c = (N - 1) * 0.5f;
        float aa = 2.2f / N;            // ~2px of edge softening, in 0..1 units
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                // 6 teeth: the rim breathes between 0.66 and 0.96
                float t = (Mathf.Cos(ang * 6f) + 1f) * 0.5f;
                float rim = 0.66f + 0.30f * Mathf.SmoothStep(0f, 1f, t);
                float a = Mathf.Min(Mathf.Clamp01((rim - r) / aa),
                                    Mathf.Clamp01((r - 0.30f) / aa));
                px[y * N + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        autoMarkSprite = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f));
        return autoMarkSprite;
    }

    /// <summary>The gear badge on a contest row: you have won this one with
    /// the robot driving itself. Named automark_&lt;id&gt; so a bench asserts
    /// on the OBJECT, not on label text that may or may not have pixels.</summary>
    GameObject AutoMark(Transform parent, string id)
    {
        var go = new GameObject("automark_" + id, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = AutoMarkSprite();
        img.color = new Color(1f, 0.82f, 0.34f, 1f);
        img.preserveAspect = true;
        img.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        float s = TouchRow() * 0.54f;
        le.minWidth = s; le.preferredWidth = s;
        le.minHeight = s; le.preferredHeight = s;
        return go;
    }

    void RefreshCareerBoard()
    {
        if (careerBoardContent == null || bm == null) return;
        fightGates.Clear();   // the rows below are about to be destroyed
        for (int i = careerBoardContent.childCount - 1; i >= 0; i--)
            Destroy(careerBoardContent.GetChild(i).gameObject);

        // ---- THE TROPHY CASE LIVES HERE NOW (owen, 2026-08-10) -------------
        // It had its own tab until ARENA took index 4. Moving it here rather
        // than deleting it, because a medal is the LEAGUE's reward \u2014 "win
        // every contest in a league and its champion medal lands here" \u2014 and
        // a trophy case on its own tab is a room you visit to be told nothing
        // has changed. On the board it is read on the way past, next to the
        // contests that are still owed.
        int medalsWon = Career.Data.medals.Count;
        var tcase = MkText("trophycase", careerBoardContent,
            medalsWon > 0
                ? "\u2605 TROPHY CASE  \u00b7  " + medalsWon + " of " + CareerDB.Leagues.Length
                  + " league campaigns won"
                : "TROPHY CASE  \u00b7  no medals yet \u2014 sweep every contest in a league "
                  + "to win its champion medal",
            14, TextAnchor.MiddleLeft);
        tcase.color = medalsWon > 0 ? new Color(1f, 0.87f, 0.46f) : new Color(0.72f, 0.78f, 0.88f);
        tcase.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

        for (int li = 0; li < CareerDB.Leagues.Length; li++)
        {
            var lg = CareerDB.Leagues[li];
            bool open = Career.LeagueUnlocked(li);
            // The medal rides on its own league's header. Everything the old
            // trophy row said that is not already on this line \u2014 who won it,
            // their record, when \u2014 goes in the second line, and only when
            // there is a medal to describe.
            var medal = Career.MedalFor(li);
            var hdr = MkText("lg_" + li, careerBoardContent,
                string.Format("{0}{1}{2} \u00b7 {3} \u00b7 cap {4} kg \u00b7 {5}{6}",
                    open ? "" : "[locked] ", medal != null ? "\u2605 " : "",
                    lg.name, lg.arenaName, Mathf.RoundToInt(lg.weightCap),
                    ArenaHazards.Summary(lg.arenaId),
                    medal != null
                        ? "\n    CHAMPION \u00b7 " + medal.robot + " " + medal.wins + "-" + medal.losses
                          + " \u00b7 " + medal.contests + " contest" + (medal.contests == 1 ? "" : "s")
                          + " swept \u00b7 " + medal.when
                        : ""),
                14, TextAnchor.MiddleLeft);
            hdr.color = medal != null ? new Color(1f, 0.87f, 0.46f)
                      : open         ? new Color(0.80f,0.88f,1f)
                                     : new Color(0.55f,0.55f,0.60f);
            hdr.gameObject.AddComponent<LayoutElement>().minHeight = medal != null ? 38f : 20f;
            if (!open) continue;
            for (int ci = 0; ci < lg.contests.Length; ci++)
            {
                int lidx = li, cidx = ci;
                var c = lg.contests[ci];
                bool done = Career.Data.doneContests.Contains(c.id);
                // P4: \u2699 = ever won this contest AUTONOMOUSLY (owen's "shared
                // contests, tracked separately" \u2014 the mark is the record).
                bool autoDone = Career.Data.autoDoneContests.Contains(c.id);
                var row = MkPanel("contest_" + c.id, careerBoardContent, new Color(0.10f,0.11f,0.14f,1f));
                var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
                var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(6,4,2,2);
                if (autoDone) AutoMark(row.transform, c.id);
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
                // P4 (owen 2026-08-06): FIGHT splits into MANUAL FIGHT and
                // AUTONOMY FIGHT — same contest, same purse; autonomy hands
                // the robot to its saved program and kills the keyboard.
                var fb = MkButton("cfight_" + c.id, row.transform, "MANUAL FIGHT", 14, () => { if (bm != null) bm.StartCareerFight(lidx, cidx); });
                fb.gameObject.AddComponent<LayoutElement>().minWidth = 118f;
                fb.GetComponent<Image>().color = FIGHT_OK;
                var ab = MkButton("cauto_" + c.id, row.transform, "AUTONOMY FIGHT", 14, () => { if (bm != null) bm.StartCareerFight(lidx, cidx, true); });
                ab.gameObject.AddComponent<LayoutElement>().minWidth = 132f;
                ab.GetComponent<Image>().color = AUTO_OK;
                fightGates.Add(new FightGate { btn = fb, img = fb.GetComponent<Image>(),
                                               abtn = ab, aimg = ab.GetComponent<Image>(),
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
    float HANDLE_H { get { return TouchRow(); } }
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

    /// <summary>Give the dock back after WATCH closed it.
    ///
    /// ⚠ THIS IS NOT A PLAYER RETURNING TO THE ARENA, and the difference is
    /// one line. They never left — the dock closed itself so the replay was not
    /// played behind an opaque panel — and watching a recording changes nothing
    /// server-side, so this must not spend a refetch.
    ///
    /// It did, and it was measured: a 30-second replay outlasts the 20-second
    /// window, so reopening fired a GET whose "28 ranked" landed on top of
    /// "replay finished" about 200 ms later. Exactly the failure DoEnlist
    /// carries two comments about — a status line with two writers and no
    /// ordering rule loses, every time, the message that mattered.
    ///
    /// Claiming the window rather than adding a suppress flag: the same one
    /// assignment already means "a fetch has just happened, do not chase it".</summary>
    public void ReopenDockAfterReplay()
    {
        arenaFetchAt = Time.realtimeSinceStartup;
        SetDockOpen(true);
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

    RectTransform matRowRt, actRowRt, partScrollRt;
    UnityEngine.UI.GridLayoutGroup partGrid;
    float lastScaleFactor, lastLayoutSig;

    /// <summary>One finger-sized row, in canvas units.
    ///
    /// R7 (owen 2026-08-04, after the collapse landed). Measured on his
    /// landscape iPhone, every tap target in this UI was under Apple's 44 pt
    /// floor: materials 25.4, action row 26.7, tabs 28.0, palette tiles 31.8.
    /// The dock was covering 41% of the screen AND was too small to hit - the
    /// worst of both, and the reason "make the dock smaller" was the wrong
    /// instinct on its own.
    ///
    /// The cause is that every row height in this file is a LITERAL in canvas
    /// units, and canvas units are pixels divided by a scale factor that tracks
    /// resolution, not physical size. A 460-dpi phone therefore shrinks
    /// everything relative to the finger holding it. 44 units meant 44 pt on
    /// whatever screen these numbers were first eyeballed on and nothing in
    /// particular anywhere else.
    ///
    /// So convert properly: 44 pt is 44/163 inch, times the device's real dpi
    /// gives pixels, divided by the live scale factor gives units. Clamped at
    /// both ends because dpi is a value devices lie about, and a lie must not
    /// be able to produce either an untappable row or a dock that fills the
    /// screen.
    ///
    /// UnityEngine.Device.Screen, not Screen - under the simulator the plain
    /// one reports the editor window, which is the wrong physical device.</summary>
    float TouchRow()
    {
        float sf = canvas != null ? canvas.scaleFactor : 0f;
        float dpi = UnityEngine.Device.Screen.dpi;
        if (sf < 0.01f || dpi < 1f) return 44f;         // unknown: previous behaviour
        return Mathf.Clamp((44f / 163f) * dpi / sf, 34f, 110f);
    }

    // OWEN 2026-08-04 walkthrough: SCOUT and FIGHT on the contest rows were
    // visibly smaller than every control in the builder. C16 measures NAMED
    // build controls, so the league list - the screen the whole career runs
    // through - was never checked by anything. Rows that carry a button are tap
    // targets and now take the same physical height as the rest.

    /// <summary>A physical type size, in canvas units.
    ///
    /// OWEN 2026-08-04: "why didn't you capture the aluminum UI bug in your
    /// previous test iterations?"
    ///
    /// Fair, and this is the same answer one layer down. R7 converted every BOX
    /// in this file from canvas units to physical inches and left every FONT
    /// SIZE as a literal - so the targets became finger-sized while the type
    /// inside them stayed pinned to resolution. Measured on his landscape
    /// iPhone after all of that work:
    ///
    ///   palette tile labels   8.3 pt
    ///   action row labels    10.8 pt
    ///
    /// Apple's smallest recommended type is 11 pt and body text is 17. I had
    /// made the buttons easy to hit and left them hard to read, and every check
    /// I had written measured rectangles, so nothing failed.</summary>
    int FontUnits(float pt)
    {
        float sf = canvas != null ? canvas.scaleFactor : 0f;
        float dpi = UnityEngine.Device.Screen.dpi;
        if (sf < 0.01f || dpi < 1f) return Mathf.RoundToInt(pt);
        return Mathf.Max(8, Mathf.RoundToInt(pt * (dpi / 163f) / sf));
    }

    /// <summary>Set every label in a subtree to one physical size.</summary>
    void SetFont(Transform root, float pt)
    {
        if (root == null) return;
        int u = FontUnits(pt);
        var ts = root.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        for (int i = 0; i < ts.Length; i++) ts[i].fontSize = u;
    }

    /// <summary>Push the finger-sized row into every control that is one.
    ///
    /// Re-applied whenever the scale factor moves rather than once at build
    /// time: CanvasScaler sets scaleFactor in its own Update, so anything read
    /// during Awake is a value from before the first layout pass. That is the
    /// same trap the dock's open/closed default fell into an hour earlier, and
    /// it fails the same silent way - correct-looking code, numbers that never
    /// change.</summary>
    RectTransform statsRt;
    float safeL, safeR, safeB, safeT;

    /// <summary>The notch, the rounded corners and the home indicator, in
    /// canvas units.
    ///
    /// R9 (owen 2026-08-04). Measured on his landscape iPhone: the screen is
    /// 2532x1170 but the SAFE area is x=141 w=2250 y=63 - 141 px bitten out of
    /// each side and 63 px off the bottom. The UI ignored all of it and drew
    /// edge to edge, so the outer tabs (BUILD and the last one, TROPHIES at the
    /// time and ARENA since 2026-08-10) ran under the notch
    /// and the rounded corners, and the whole action row - ROTATE through SAVE -
    /// sat in the home-indicator strip, which on iOS also swallows the swipe
    /// that would have hit them.
    ///
    /// Only the UI is inset. The 3D view still runs edge to edge, which is what
    /// you want behind a notch: the arena bleeds off the sides, and nothing you
    /// have to READ or HIT is underneath anything.
    ///
    /// UnityEngine.Device.Screen again - the plain one reports the editor
    /// window, which has no notch at all, so this whole class of defect is
    /// invisible until it ships.</summary>
    void ReadSafeArea()
    {
        safeL = safeR = safeB = safeT = 0f;
        if (canvas == null) return;
        float sf = canvas.scaleFactor;
        if (sf < 0.01f) return;
        var sa = UnityEngine.Device.Screen.safeArea;
        float w = UnityEngine.Device.Screen.width, h = UnityEngine.Device.Screen.height;
        if (w < 1f || h < 1f || sa.width < 1f || sa.height < 1f) return;
        safeL = Mathf.Max(0f, sa.x) / sf;
        safeR = Mathf.Max(0f, w - (sa.x + sa.width)) / sf;
        safeB = Mathf.Max(0f, sa.y) / sf;
        safeT = Mathf.Max(0f, h - (sa.y + sa.height)) / sf;
    }

    /// <summary>Inset a full-width bar so its CONTENT clears the notch. The
    /// panel itself still spans the screen - a dark bar that stops short of the
    /// edge reads as a rendering fault, not as a considered margin.</summary>
    float safeFracL
    {
        get
        {
            float cw = CanvasW;
            return cw > 1f ? Mathf.Clamp01(safeL / cw) : 0f;
        }
    }
    float safeFracR
    {
        get
        {
            float cw = CanvasW;
            return cw > 1f ? Mathf.Clamp01(1f - safeR / cw) : 1f;
        }
    }
    float CanvasW
    {
        get
        {
            if (canvas == null) return 0f;
            var c = canvas.GetComponent<RectTransform>();
            return c != null ? c.rect.width : 0f;
        }
    }

    void InsetBar(RectTransform rt)
    {
        if (rt == null) return;
        var t = rt.GetComponentInChildren<UnityEngine.UI.Text>();
        if (t != null)
        {
            var trt = t.rectTransform;
            // ⚠ COMPOSE WITH THE EXISTING INSET, NEVER OVERWRITE IT — 2026-08-12.
            //
            // These two lines used to ASSIGN, and that silently ate a layout
            // intent. The tip strip reserves 206 units on its right for the
            // < > and SKIP TIPS controls; this pass replaced that with
            // -(10 + safeR) — about -88 on the reference device — so the tip
            // text gained ~118 units and ran underneath its own controls, cut
            // mid-sentence with no ellipsis.
            //
            // Max on the left and Min on the right because these are inset
            // offsets with opposite signs: larger positive and more negative
            // both mean MORE inset. So an existing reservation survives, and a
            // notch bigger than the reservation still wins. Whichever inset is
            // stricter is the one that is correct.
            //
            // SYMMETRIC ON PURPOSE. Only the right edge was reported, because
            // only the right edge had a reservation to destroy — the left had
            // the identical overwrite and nothing to lose yet. Fixing one side
            // would leave the bug armed for the next caller that reserves space.
            //
            // Verified against all three callers rather than assumed: statsText
            // and msgText carry plain symmetric padding and no reservation, so
            // statsText is unchanged, msgText recovers a silent 14→10 padding
            // regression, and the tip bar gets its 206 back.
            trt.offsetMin = new Vector2(Mathf.Max(trt.offsetMin.x,   10f + safeL),  trt.offsetMin.y);
            trt.offsetMax = new Vector2(Mathf.Min(trt.offsetMax.x, -(10f + safeR)), trt.offsetMax.y);
        }
    }

    void ApplyTouchSizes()
    {
        ReadSafeArea();
        float R = TouchRow();
        foreach (var tb in tabBtns)
        {
            if (tb == null) continue;
            var rt = tb.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(-4f, R);
        }
        // tabPanels, never a fresh array — this pass used to list six of the
        // seven and PROGRAM went uninset. See the field's comment.
        foreach (var pn in tabPanels ?? new GameObject[0])
        {
            if (pn == null) continue;
            var rt = pn.GetComponent<RectTransform>();
            if (rt == null) continue;
            rt.offsetMin = new Vector2(6f + safeL, 6f + safeB);
            rt.offsetMax = new Vector2(-(6f + safeR), -(R + 4f));
        }
        InsetBar(statsRt);
        InsetBar(msgBar != null ? msgBar.GetComponent<RectTransform>() : null);
        InsetBar(tipBar != null ? tipBar.GetComponent<RectTransform>() : null);
        LayoutTabs();
        if (matRowRt != null) matRowRt.sizeDelta = new Vector2(-12f, R);
        if (actRowRt != null) actRowRt.sizeDelta = new Vector2(0f, R);
        // The SHOP's PARTS/COSMETICS switch. It lives in a VerticalLayoutGroup
        // so it is held by a LayoutElement rather than a sizeDelta, but it is
        // the same rule: one touch row, tracked against the scale factor.
        if (shopSecLE != null)
        { shopSecLE.flexibleHeight = 0f; shopSecLE.minHeight = R; shopSecLE.preferredHeight = R; }
        // Same rule for the ARENA's weight-class filters and its list switch.
        if (arenaCatLE != null)
        { arenaCatLE.flexibleHeight = 0f; arenaCatLE.minHeight = R; arenaCatLE.preferredHeight = R; }
        if (arenaSecLE != null)
        { arenaSecLE.flexibleHeight = 0f; arenaSecLE.minHeight = R; arenaSecLE.preferredHeight = R; }
        if (partScrollRt != null)
        {
            partScrollRt.offsetMin = new Vector2(0f, R + 4f);
            partScrollRt.offsetMax = new Vector2(0f, -2f);
        }
        if (matSheetRt != null)
        {
            // Exactly the palette's footprint, so the sheet cannot creep over
            // the action row - which is where CLOSE lives.
            matSheetRt.offsetMin = new Vector2(0f, R + 4f);
            matSheetRt.offsetMax = new Vector2(0f, -2f);
        }
        // TYPE, not just boxes. Sizes chosen against what each label has to do:
        // tabs and the action row are single words you read at a glance, the
        // palette tiles carry two lines in a fixed cell so they get the floor
        // rather than the ideal, and the status bar is the one line that must
        // survive being read mid-build.
        FitTabFonts();
        foreach (var mb in matButtons) if (mb != null) SetFont(mb.transform, 13f);
        foreach (var pb in partButtons) if (pb != null) SetFont(pb.transform, 11f);
        if (actRowRt != null) SetFont(actRowRt, 14f);
        // V2.2 C19 repair: the action row split its width EQUALLY, so the one
        // button whose face carries a VARIABLE label — MATERIAL, showing the
        // current material's name — clipped on the sim. Width now follows each
        // label (min-width = text + padding); the group spreads the slack.
        if (actRowRt != null)
            foreach (var ab in actRowRt.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                var lt2 = ab.GetComponentInChildren<UnityEngine.UI.Text>();
                if (lt2 == null) continue;
                var le2 = ab.GetComponent<UnityEngine.UI.LayoutElement>();
                if (le2 == null) le2 = ab.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                le2.minWidth = lt2.preferredWidth + 12f;
                le2.flexibleWidth = 1f;
            }
        if (statsText != null) statsText.fontSize = FontUnits(13f);
        // V2.2 C19 repair (first PROPER-SIM sweep — the 08-06 "environmental"
        // theory was half wrong): the stats bar was a 46-unit constant while
        // its label is FontUnits-scaled, so on the iPad sim the second wrapped
        // line fell outside the bar. Budget two lines; the bars below read
        // BarH so the stack follows.
        if (statsRt != null)
            statsRt.sizeDelta = new Vector2(0f, Mathf.Max(BAR_H, FontUnits(13f) * 2f + 14f));
        if (msgBarRt != null) msgBarRt.anchoredPosition = new Vector2(0f, -BarH);
        // …and the collapse handle was the one label never re-fonted: built at
        // a literal 18 units, it read 9.6 pt on the sim. Same fix as every
        // other control — physical points through SetFont.
        if (dockHandle != null) SetFont(dockHandle.transform, 12f);
        // The notice and tip bars were the two the first sweep never saw,
        // because neither is on screen unless the game has something to say.
        if (msgBar != null) SetFont(msgBar.transform, 12f);
        if (tipBar != null) SetFont(tipBar.transform, 12f);
        FitMsgBar();
        FitTipBar();

        if (partGrid != null)
        {
            // R7b: the cell WIDTH was a literal 112, chosen so ten columns fit
            // the phone's canvas - and the note above it already records that
            // 128 was measured wrong once. Measured again on the iPad: content
            // 1182 units against a 1097 viewport, 85 over, so the last column
            // (Wheel and Hook) hung off the edge. The canvas is NARROWER in
            // units on the bigger device, which is the sort of thing that makes
            // a hand-fitted number a trap rather than a shortcut.
            //
            // Derived from the viewport instead: whatever ten columns is, make
            // it fit. Floored so a freak narrow screen gets a scroll rather
            // than unreadable slivers.
            float vw = partScrollRt != null ? partScrollRt.rect.width : 0f;
            if (vw < 50f && canvas != null)
            {
                var cw0 = canvas.GetComponent<RectTransform>();
                if (cw0 != null) vw = cw0.rect.width - 12f;
            }
            int cols = Mathf.Max(1, Mathf.CeilToInt((bm != null ? bm.PaletteCount : 19)
                                                    / (float)partGrid.constraintCount));
            float cellW = vw > 50f ? (vw - 8f - (cols - 1) * 6f) / cols : 112f;
            partGrid.cellSize = new Vector2(Mathf.Max(84f, cellW), R);

        }
        if (handleRt != null) handleRt.sizeDelta = new Vector2(300f, R);
        ApplyDockH();
        FitPaletteRows(R);
    }

    /// <summary>Size the palette's cell HEIGHT to the viewport it actually
    /// got, and scroll vertically if even that is not enough.
    ///
    /// ⚠ THIS MUST RUN AFTER ApplyDockH(), and that ordering is the whole
    /// reason it is a separate method. The width pass above reads
    /// partScrollRt.rect while the dock still has its PREVIOUS height, which
    /// is harmless for width and useless for height — the first version of
    /// this fix sat in that block, measured a stale viewport, and changed
    /// nothing at all: the bench reported the identical 80.6 units clipped.
    /// The canvas is force-rebuilt first because rect does not settle until
    /// the layout pass runs.
    ///
    /// The bug being fixed: width has been derived from the viewport since R6
    /// ("whatever ten columns is, make it fit") but height stayed pinned to a
    /// touch row, which held only while the rows happened to fit. They stopped
    /// fitting — the sensor palette took the catalogue to 25 parts and the
    /// grid to FOUR rows, DockH(0) asked for six touch rows, and the
    /// 0.68-of-screen cap (which by design has the last word) handed back
    /// less. The grid laid out four rows anyway, so the bottom row hung 80.6
    /// units BELOW a viewport that only scrolls horizontally: four parts a
    /// player could see the tops of and never touch.
    ///
    /// That is R1's failure one axis over ("EVERY wheel and EVERY weapon lived
    /// off-screen"), and it was invisible to every bench that checks data
    /// rather than geometry. TouchSmoke's fixed-axis invariant caught it.</summary>
    void FitPaletteRows(float R)
    {
        if (partGrid == null || partScrollRt == null) return;

        float vh = partScrollRt.rect.height - 10f;      // the scrollbar's lane
        if (vh <= 50f) return;                          // not laid out yet; leave it alone
        int rows = Mathf.Max(1, partGrid.constraintCount);
        float spacing = partGrid.spacing.y;
        // ⚠ THE FLOOR IS R, NOT 44 — and the literal was a UNIT BUG that cost
        // the touch floor for a day (fixed 2026-08-10).
        //
        // R is TouchRow(): a PHYSICAL 44 pt expressed in canvas units,
        // (44/163)*dpi/sf. The literal 44 is 44 CANVAS UNITS, which on owen's
        // phone measures 36.9 pt — comfortably under the 44 pt floor every
        // other control in this dock is held to. Clamping between a
        // canvas-unit lower bound and a physical upper bound is comparing two
        // different quantities, and the low bound won.
        //
        // Measured: CareerSmoke "build controls clear the 44 pt touch floor
        // (too small: part_0=36.9pt)". It went green the moment the palette
        // stopped being allowed to shrink below a finger.
        //
        // So the cell never goes under a fingertip, and when the rows do not
        // fit at that height the VERTICAL SCROLL below absorbs it. That is the
        // honest trade: this method exists because the sensor rows were
        // unreachable, and a row you can see but cannot hit accurately is the
        // same failure wearing a different hat.
        // R was ALREADY the upper bound, so flooring at R collapses this to a
        // constant. Written as one, rather than left as a Clamp(x, R, R) that
        // reads like it still negotiates: the cell is one touch row, always,
        // and the scroll below is what gives when that does not fit.
        float cellH = R;
        if (Mathf.Abs(partGrid.cellSize.y - cellH) > 0.5f)
            partGrid.cellSize = new Vector2(partGrid.cellSize.x, cellH);

        // Last resort, and it has to exist. If even the floored height cannot
        // fit the rows — a shorter screen, or a catalogue that grows again —
        // let the palette scroll VERTICALLY too. A cramped two-axis scroll is
        // worse than a clean one-axis one, and both are enormously better than
        // a part that cannot be reached at all.
        var srr = partScrollRt.GetComponent<UnityEngine.UI.ScrollRect>();
        if (srr != null)
            srr.vertical = (rows * cellH + (rows - 1) * spacing) > vh + 1f;
    }

    /// <summary>Test hook: how tall a named control actually is, in points.
    ///
    /// Named rather than "the smallest button anywhere", because the list tabs
    /// have their own dense rows that this change does not claim to fix -
    /// a global minimum would fail for something it was never measuring and
    /// teach us to lower the bar.</summary>
    public float TapTargetPt(string goName)
    {
        var go = GameObject.Find(goName);
        if (go == null) return -1f;
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) return -1f;
        float dpi = UnityEngine.Device.Screen.dpi;
        if (dpi < 1f) return 99f;
        var c = new Vector3[4];
        rt.GetWorldCorners(c);
        return (c[1].y - c[0].y) / (dpi / 163f);
    }

    float DockH(int t)
    {
        float R = TouchRow();
        // + safeB everywhere: the dock's BACKGROUND still reaches the bottom of
        // the screen, but its contents are pushed above the home indicator.
        // Because OverUI measures the dock rect, the hit band follows for free -
        // the alternative, lifting the dock off the bottom, would have left a
        // live strip underneath it that places parts through the UI.
        if (!dockOpen) return R + 4f + safeB;     // the tab strip, and nothing else
        // BUILD, derived rather than the old literal 272: tab strip + material
        // row + two palette rows + action row + the scrollbar's lane and the
        // padding between them. Capped so a device that overstates its dpi
        // cannot hand back a dock taller than the screen it is sitting on.
        if (t == 0)
        {
            float ch1 = 0f;
            if (canvas != null) { var c1 = canvas.GetComponent<RectTransform>(); if (c1 != null) ch1 = c1.rect.height; }
            // R8 made this four rows, not five: the material chips folded into
            // the action row as a chooser and gave their row back. It is five
            // again (owen, 2026-08-05) because the palette went to THREE rows
            // so its tiles stop truncating their own stock counts - see the
            // GridLayoutGroup note. P1 (same day): SIX, because the sensor
            // palette takes the catalogue to 24 parts and the grid to FOUR
            // rows — tab strip + action row + four palette rows. The 0.68 cap
            // is unchanged and still has the last word, so a device that
            // overstates its dpi cannot turn this into a dock that swallows
            // the screen.
            float want = 6f * R + 38f + safeB;
            return ch1 > 100f ? Mathf.Min(want, ch1 * 0.68f) : want;
        }
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
        float top = BarH
                  + ((msgBar != null && msgBar.activeSelf) ? MsgH : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TipH : 0f);
        // - HANDLE_H: the list tabs used to take EVERY unit down to the top
        // bar, which left the handle nowhere to go. Floating it above the dock
        // put it across the status line; tucking it inside put it across the
        // tab strip. Both were the same mistake - moving a control instead of
        // making room for it. The dock now reserves the handle's height, so
        // there is exactly one rule and nothing overlaps anything.
        return Mathf.Clamp(ch - top - 8f - HANDLE_H, 400f, ch);
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
            // The handle always sits directly above the dock. It has no
            // fallback position and needs none: DockH reserves its height, so
            // the band is never occupied by anything else.
            //
            // Two earlier attempts MOVED the handle instead - above the dock it
            // crossed the status bar, tucked inside it crossed the tab strip.
            // Both were the same mistake, and the second one only looked
            // different. A control with two possible positions has two possible
            // collisions; reserving the space gives it one position and none.
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
        float top = BarH
                  + ((msgBar != null && msgBar.activeSelf) ? MsgH : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TipH : 0f);
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
    /// <summary>V2.2 C19 repair: six career tabs share the safe width and
    /// TROPHIES/PROGRAM overflowed their sixth at 15 pt. Reset every tab to
    /// the standard 15 pt, then shrink any label that overflows its box,
    /// floored at the sweep's own 10.5 pt line. Idempotent, and called from
    /// BOTH re-layout paths — ApplyTouchSizes (scale change) and LayoutTabs
    /// (career flip changes the tab count, which changes every box width
    /// WITHOUT a scale change; fitting only on scale change is how the first
    /// verification run kept the stale 3-tab fit).</summary>
    void FitTabFonts()
    {
        int tabsLive = 0;
        foreach (var tb in tabBtns) if (tb != null && tb.gameObject.activeSelf) tabsLive++;
        float cw2 = CanvasW;
        if (tabsLive == 0 || cw2 <= 1f) return;
        float tw = cw2 * Mathf.Max(0.1f, safeFracR - safeFracL) / tabsLive - 10f;
        if (tw < 20f) return;
        int floorU = FontUnits(10.5f);
        foreach (var tb in tabBtns)
        {
            if (tb == null || !tb.gameObject.activeSelf) continue;
            var lt = tb.GetComponentInChildren<UnityEngine.UI.Text>();
            if (lt == null) continue;
            lt.fontSize = FontUnits(15f);
            int guard = 24;
            while (lt.fontSize > floorU && lt.preferredWidth > tw && guard-- > 0)
                lt.fontSize = lt.fontSize - 1;
        }
    }

    void LayoutTabs()
    {
        // ARENA is career-only, like SHOP - 6 tabs in career mode, still 3
        // in the sandbox. It replaced TROPHIES at index 4 (owen, 2026-08-10);
        // the medals moved onto the LEAGUE board, where they are earned.
        // PARTS was the sixth and is gone (owen, 2026-08-05):
        // ownership is already on the BUILD tiles ("N free") and on every SHOP
        // row ("own N"), so the tab restated two numbers the player had
        // anyway. What it uniquely showed - which robot was holding what - it
        // stopped being able to say when designs stopped holding parts.
        int n = Career.active ? 6 : 3;   // P3a: +PROGRAM in career
        for (int i = 0; i < tabBtns.Count; i++)
        {
            bool show = i < n;
            tabBtns[i].gameObject.SetActive(show);
            if (!show) continue;
            var rt = tabBtns[i].GetComponent<RectTransform>();
            // R9: spread the tabs across the SAFE width, not the screen width.
            // Measured on owen's landscape iPhone, 141 px is bitten out of each
            // side by the notch and the rounded corners - so the first and last
            // tabs — BUILD and whatever is last (TROPHIES then, ARENA now) — were
            // the two sitting underneath it.
            // Dividing 0..1 evenly is only correct on a rectangle.
            rt.anchorMin = new Vector2(Mathf.Lerp(safeFracL, safeFracR, i / (float)n), 1f);
            rt.anchorMax = new Vector2(Mathf.Lerp(safeFracL, safeFracR, (i + 1) / (float)n), 1f);
        }
        // C4: the workshop renames FIGHT/GARAGE in career mode
        var tl1 = tabBtns.Count > 1 ? tabBtns[1].GetComponentInChildren<Text>() : null;
        if (tl1 != null) tl1.text = Career.active ? "LEAGUE" : "FIGHT";
        var tl2 = tabBtns.Count > 2 ? tabBtns[2].GetComponentInChildren<Text>() : null;
        if (tl2 != null) tl2.text = Career.active ? "ROBOTS" : "GARAGE";
        RefreshTabHighlight();   // renaming a tab resets nothing about selection
        FitTabFonts();           // C19: box widths just changed with the tab count
    }

    /// <summary>P3a: the Program Bench canvas. All machinery lives in
    /// ProgramCanvas.cs; this just mounts it on the career-only panel.</summary>
    void BuildProgramTab()
    {
        programCanvas = programPanel.AddComponent<ProgramCanvas>();
        programCanvas.Init(bm);
    }

    void BuildShopTab()
    {
        var v = shopPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false; v.padding = new RectOffset(4,4,4,4);
        shopHeader = MkText("shopheader", shopPanel.transform, "", 15, TextAnchor.MiddleLeft);
        var hle = shopHeader.gameObject.AddComponent<LayoutElement>(); hle.minHeight = 20f; hle.preferredHeight = 20f;

        // The two shelves. A row of two buttons rather than a second tab,
        // because PARTS and COSMETICS are the same errand — spending — and the
        // tab bar is full at six.
        var secRow = MkPanel("shopsections", shopPanel.transform, new Color(0f,0f,0f,0f));
        var secLE = secRow.AddComponent<LayoutElement>();
        var secH = secRow.AddComponent<HorizontalLayoutGroup>();
        secH.spacing = 4f; secH.childForceExpandWidth = true; secH.childForceExpandHeight = true;
        shopSecParts = MkButton("shopsec_parts", secRow.transform, "PARTS", 14, () => ShowShopSection(0));
        shopSecCosmetics = MkButton("shopsec_cos", secRow.transform, "COSMETICS", 14, () => ShowShopSection(1));
        // Sized like every other tap target in this dock, and it has to be:
        // CareerSmoke measures NAMED build controls, and a control invented
        // after that list was written is one nothing checks.
        //
        // ⚠ flexibleHeight MUST BE 0, and the screenshot is why. A
        // LayoutElement leaves it at -1 ("unset"), the VerticalLayoutGroup
        // read that as "take the slack", and the two section buttons came out
        // roughly THREE TIMES the height of the tab row above them — a third
        // of the dock spent on a switch. min/preferred alone do not hold a row
        // down; something has to decline the leftover space.
        //
        // Re-applied in ApplyTouchSizes rather than only here, because
        // TouchRow() depends on the canvas scale factor and this runs before
        // the canvas has one. Sizing a touch target once, at construction, is
        // how it ends up right on the machine that built it and wrong on a
        // phone.
        shopSecLE = secLE;
        secLE.flexibleHeight = 0f;
        secLE.minHeight = TouchRow(); secLE.preferredHeight = TouchRow();

        shopPartsRoot = MkPanel("shoppartsroot", shopPanel.transform, new Color(0f,0f,0f,0f));
        var prle = shopPartsRoot.AddComponent<LayoutElement>(); prle.flexibleHeight = 1f; prle.minHeight = 96f;
        var prv = shopPartsRoot.AddComponent<VerticalLayoutGroup>();
        prv.childForceExpandWidth = true; prv.childForceExpandHeight = true;

        var scrollGO = MkPanel("shopscroll", shopPartsRoot.transform, new Color(0f,0f,0f,0f));
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
            // Taller when open: the header carries the part's DESCRIPTION on a
            // second line (owen, 2026-08-05 - "where do I see the introduction
            // of each part?"). It only costs height on the one part you have
            // open, which is the accordion's whole point.
            var hle2 = head.AddComponent<LayoutElement>(); hle2.minHeight = 36f; hle2.preferredHeight = 36f;
            var hbtn = head.AddComponent<Button>();
            hbtn.targetGraphic = head.GetComponent<Image>();
            hbtn.onClick.AddListener(() => ToggleShopPart(idx));
            var htxt = MkText("lbl", head.transform, "", 14, TextAnchor.MiddleLeft);
            Stretch(htxt.rectTransform);
            htxt.rectTransform.offsetMin = new Vector2(10f, 0f); htxt.rectTransform.offsetMax = new Vector2(-10f, 0f);
            var pr = new ShopPartRow(); pr.part = i; pr.go = head; pr.lbl = htxt; pr.open = (i == 1);
            // The DESCRIPTION gets its own row rather than a second line on the
            // header (owen, 2026-08-05). Two lines in the header label spilled
            // OUT of its panel and drew over the 3D view: the label measured
            // 56 units and reported that it fitted, while the panel behind it
            // rendered at about 28. Rather than keep arguing with the layout,
            // this is the same shape as every other line in this list - one row
            // object, its own height, shown when the part is open - which is
            // both simpler and addressable by name from the suite.
            var drow = MkPanel("shopdesc_" + i, content.transform, new Color(0.10f,0.12f,0.16f,0.95f));
            var dle = drow.AddComponent<LayoutElement>(); dle.minHeight = 34f; dle.preferredHeight = 34f;
            var dtxt = MkText("lbl", drow.transform, "", 13, TextAnchor.MiddleLeft);
            Stretch(dtxt.rectTransform);
            dtxt.rectTransform.offsetMin = new Vector2(16f, 2f);
            dtxt.rectTransform.offsetMax = new Vector2(-10f, -2f);
            dtxt.horizontalOverflow = HorizontalWrapMode.Wrap;
            dtxt.color = new Color(0.72f, 0.80f, 0.92f);
            pr.desc = drow; pr.descT = dtxt; pr.descLe = dle;
            shopParts.Add(pr);

            foreach (var mk in bm.PartLegalMats(i))
            {
                string mat = mk;
                var row = MkPanel("shopmat_" + i + "_" + mk, content.transform, new Color(0.07f,0.08f,0.105f,1f));
                var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
                var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(28,4,2,2);
                var lbl = MkText("lbl", row.transform, "", 13, TextAnchor.MiddleLeft);
                // shop1 capture: a FLEXIBLE label flung the buttons to the far
                // right of a ~1200-unit row, and BUY moved horizontally from row
                // to row as SELL / REWORK appeared and vanished. A fixed label
                // column left-packs the group, so the actions sit in the same
                // place on every line, beside the price they act on. (REWORK
                // itself is gone - owen, 2026-08-05 - but the fixed column is
                // what stops BUY wandering, and that is still worth having.)
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
                shopMats.Add(mr);
            }
        }
        BuildCosmeticsShelf();
        ShowShopSection(0);
        RefreshShop();
    }

    /// <summary>The ARENA tab, in UGUI. Surface 1 of 5: the BOARD.
    ///
    /// Built like every other tab — a header, a row of weight-class filters
    /// sized to TouchRow(), and a scrolled list — so it inherits the dock's
    /// safe-area handling, its touch floor, and the benches that measure both.
    /// The IMGUI ArenaScreen keeps the surfaces that are not ported yet.</summary>
    void BuildArenaTab()
    {
        var v = arenaPanel.AddComponent<VerticalLayoutGroup>();
        v.spacing = 4f; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
        v.padding = new RectOffset(4,4,4,4);

        arenaStatus = MkText("arenastatus", arenaPanel.transform, "", 15, TextAnchor.MiddleLeft);
        var hle = arenaStatus.gameObject.AddComponent<LayoutElement>();
        hle.minHeight = 20f; hle.preferredHeight = 20f; hle.flexibleHeight = 0f;

        // BOARD or MY FIGHTS. Same shape as the SHOP's shelf switch, because
        // it is the same idea: one tab, two lists, and the tab bar is full.
        var secRow = MkPanel("arenasections", arenaPanel.transform, new Color(0f,0f,0f,0f));
        arenaSecLE = secRow.AddComponent<LayoutElement>();
        arenaSecLE.flexibleHeight = 0f;
        arenaSecLE.minHeight = TouchRow(); arenaSecLE.preferredHeight = TouchRow();
        var sh = secRow.AddComponent<HorizontalLayoutGroup>();
        sh.spacing = 4f; sh.childForceExpandWidth = true; sh.childForceExpandHeight = true;
        // ⚠ SIGNED OUT, TWO OF THESE THREE CANNOT WORK — and they used to look
        // exactly like the one that can. RefreshArena's view precedence forces
        // the ACCOUNT surface whenever `!LadderClient.SignedIn`, so a signed-out
        // tap on THE BOARD or MY FIGHTS set the flag, got overruled on the very
        // next frame, and changed NOTHING on screen: a live-looking button that
        // silently ate the tap. The player's reasonable reading is that the tab
        // is broken.
        //
        // owen, 2026-08-03: "whenever a button is disabled, it should show hint
        // to user on why it is disabled." Same answer as the SHOP's SELL — keep
        // the slot, keep the label, read DEAD, and say why when tapped. The
        // saying-why is not optional; a dead button with no explanation is the
        // unexplained gap that rule was written against.
        arenaSecBoard = MkButton("arenasec_board", secRow.transform, "THE BOARD", 14,
            () =>
            {
                if (arenaScreen == null) return;
                if (!LadderClient.SignedIn)
                {
                    // SC_ACCOUNT, because the account panel is the ONLY surface
                    // on screen while signed out and §2.4's renderer shows the
                    // line only on the surface that wrote it. Any other scope
                    // here is a message nobody can read.
                    arenaScreen.SetStatus("sign in first — the board opens once you have an account.",
                                          ArenaScreen.SC_ACCOUNT);
                    return;
                }
                arenaScreen.ShowInbox = false; arenaScreen.CloseCard(); arenaBoardStamp = -1;
            });
        arenaSecInbox = MkButton("arenasec_inbox", secRow.transform, "MY FIGHTS", 14,
            () =>
            {
                if (arenaScreen == null) return;
                if (!LadderClient.SignedIn)
                {
                    arenaScreen.SetStatus("sign in first — your fights are tied to an account.",
                                          ArenaScreen.SC_ACCOUNT);
                    return;
                }
                arenaScreen.ShowInbox = true; arenaScreen.ShowEnlistPanel = false;
                arenaScreen.CloseCard(); arenaInboxStamp = "";
            });
        arenaSecAccount = MkButton("arenasec_account", secRow.transform, "ENLIST", 14,
            () => { if (arenaScreen != null) { arenaScreen.ShowEnlistPanel = true; arenaScreen.ShowInbox = false; arenaScreen.CloseCard(); arenaAccountStamp = ""; } });

        // The weight classes. P4P first — it is the board the tab opens on,
        // and "everyone, pound for pound" is the only view that is never empty.
        var catRow = MkPanel("arenacats", arenaPanel.transform, new Color(0f,0f,0f,0f));
        arenaCatRow = catRow;
        arenaCatLE = catRow.AddComponent<LayoutElement>();
        arenaCatLE.flexibleHeight = 0f;                 // see the SHOP switch: -1 eats the dock
        arenaCatLE.minHeight = TouchRow(); arenaCatLE.preferredHeight = TouchRow();
        var ch = catRow.AddComponent<HorizontalLayoutGroup>();
        ch.spacing = 3f; ch.childForceExpandWidth = true; ch.childForceExpandHeight = true;
        arenaCatBtns.Clear();
        var cats = ArenaScreen.Categories;
        for (int i = 0; i < cats.Length; i++)
        {
            int idx = i;
            string label = cats[i] == "" ? "P4P" : cats[i];
            var b = MkButton("arenacat_" + label, catRow.transform, label, 13,
                             () => { if (arenaScreen != null) arenaScreen.SetCategory(idx); });
            arenaCatBtns.Add(b);
        }

        var scrollGO = MkPanel("arenascroll", arenaPanel.transform, new Color(0f,0f,0f,0f));
        arenaBoardRoot = scrollGO;
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 96f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("arenaviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("arenacontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f);
        crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);
        var clg = content.AddComponent<VerticalLayoutGroup>();
        clg.spacing = 3f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false;
        clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: ARENA
        arenaBoardContent = content.transform;

        // ---- the scouting card, surface 2 ------------------------------
        arenaCardRoot = MkPanel("arenacardroot", arenaPanel.transform, new Color(0f,0f,0f,0f));
        var crle = arenaCardRoot.AddComponent<LayoutElement>(); crle.flexibleHeight = 1f; crle.minHeight = 96f;
        var crv = arenaCardRoot.AddComponent<VerticalLayoutGroup>();
        crv.childForceExpandWidth = true; crv.childForceExpandHeight = true;

        var cScrollGO = MkPanel("arenacardscroll", arenaCardRoot.transform, new Color(0f,0f,0f,0f));
        var cScroll = cScrollGO.AddComponent<ScrollRect>(); cScroll.horizontal = false; cScroll.vertical = true;
        var cVp = MkPanel("arenacardviewport", cScrollGO.transform, new Color(0f,0f,0f,0.15f));
        var cVpRt = cVp.GetComponent<RectTransform>(); Stretch(cVpRt);
        cVp.AddComponent<Mask>().showMaskGraphic = true;
        var cContent = MkPanel("arenacardcontent", cVp.transform, new Color(0f,0f,0f,0f));
        var cCrt = cContent.GetComponent<RectTransform>();
        cCrt.anchorMin = new Vector2(0f,1f); cCrt.anchorMax = new Vector2(1f,1f);
        cCrt.pivot = new Vector2(0.5f,1f); cCrt.anchoredPosition = Vector2.zero;
        cCrt.sizeDelta = new Vector2(0f, 0f);
        var cClg = cContent.AddComponent<VerticalLayoutGroup>();
        cClg.spacing = 4f; cClg.childForceExpandWidth = true; cClg.childForceExpandHeight = false;
        cClg.padding = new RectOffset(4,4,4,4);
        var cCsf = cContent.AddComponent<ContentSizeFitter>(); cCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cScroll.viewport = cVpRt; cScroll.content = cCrt;
        AddListOverflow(cScrollGO, cScroll, cVpRt);
        arenaCardContent = cContent.transform;
        arenaCardRoot.SetActive(false);

        // ---- MY FIGHTS, surface 3 --------------------------------------
        arenaInboxRoot = MkPanel("arenainboxroot", arenaPanel.transform, new Color(0f,0f,0f,0f));
        var irle = arenaInboxRoot.AddComponent<LayoutElement>(); irle.flexibleHeight = 1f; irle.minHeight = 96f;
        var irv = arenaInboxRoot.AddComponent<VerticalLayoutGroup>();
        irv.childForceExpandWidth = true; irv.childForceExpandHeight = true;

        var iScrollGO = MkPanel("arenainboxscroll", arenaInboxRoot.transform, new Color(0f,0f,0f,0f));
        var iScroll = iScrollGO.AddComponent<ScrollRect>(); iScroll.horizontal = false; iScroll.vertical = true;
        var iVp = MkPanel("arenainboxviewport", iScrollGO.transform, new Color(0f,0f,0f,0.15f));
        var iVpRt = iVp.GetComponent<RectTransform>(); Stretch(iVpRt);
        iVp.AddComponent<Mask>().showMaskGraphic = true;
        var iContent = MkPanel("arenainboxcontent", iVp.transform, new Color(0f,0f,0f,0f));
        var iCrt = iContent.GetComponent<RectTransform>();
        iCrt.anchorMin = new Vector2(0f,1f); iCrt.anchorMax = new Vector2(1f,1f);
        iCrt.pivot = new Vector2(0.5f,1f); iCrt.anchoredPosition = Vector2.zero;
        iCrt.sizeDelta = new Vector2(0f, 0f);
        var iClg = iContent.AddComponent<VerticalLayoutGroup>();
        iClg.spacing = 3f; iClg.childForceExpandWidth = true; iClg.childForceExpandHeight = false;
        iClg.padding = new RectOffset(4,4,4,4);
        var iCsf = iContent.AddComponent<ContentSizeFitter>(); iCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        iScroll.viewport = iVpRt; iScroll.content = iCrt;
        AddListOverflow(iScrollGO, iScroll, iVpRt);
        arenaInboxContent = iContent.transform;
        arenaInboxRoot.SetActive(false);

        // ---- ENLIST / the account, surfaces 4 and 5 --------------------
        arenaAccountRoot = MkPanel("arenaaccountroot", arenaPanel.transform, new Color(0f,0f,0f,0f));
        var arle = arenaAccountRoot.AddComponent<LayoutElement>(); arle.flexibleHeight = 1f; arle.minHeight = 96f;
        var arv = arenaAccountRoot.AddComponent<VerticalLayoutGroup>();
        arv.childForceExpandWidth = true; arv.childForceExpandHeight = true;

        var aScrollGO = MkPanel("arenaaccountscroll", arenaAccountRoot.transform, new Color(0f,0f,0f,0f));
        var aScroll = aScrollGO.AddComponent<ScrollRect>(); aScroll.horizontal = false; aScroll.vertical = true;
        var aVp = MkPanel("arenaaccountviewport", aScrollGO.transform, new Color(0f,0f,0f,0.15f));
        var aVpRt = aVp.GetComponent<RectTransform>(); Stretch(aVpRt);
        aVp.AddComponent<Mask>().showMaskGraphic = true;
        var aContent = MkPanel("arenaaccountcontent", aVp.transform, new Color(0f,0f,0f,0f));
        var aCrt = aContent.GetComponent<RectTransform>();
        aCrt.anchorMin = new Vector2(0f,1f); aCrt.anchorMax = new Vector2(1f,1f);
        aCrt.pivot = new Vector2(0.5f,1f); aCrt.anchoredPosition = Vector2.zero;
        aCrt.sizeDelta = new Vector2(0f, 0f);
        var aClg = aContent.AddComponent<VerticalLayoutGroup>();
        aClg.spacing = 4f; aClg.childForceExpandWidth = true; aClg.childForceExpandHeight = false;
        aClg.padding = new RectOffset(4,4,4,4);
        var aCsf = aContent.AddComponent<ContentSizeFitter>(); aCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        aScroll.viewport = aVpRt; aScroll.content = aCrt;
        AddListOverflow(aScrollGO, aScroll, aVpRt);
        arenaAccountContent = aContent.transform;
        arenaAccountRoot.SetActive(false);
    }

    /// <summary>A labelled text field, the dock's way. `secret` switches the
    /// InputField to Password content type, which masks it — and the value is
    /// pushed OUT to the caller and never read back, so nothing here ever
    /// holds what was typed.
    ///
    /// <paramref name="hint"/> is the placeholder, and it is a PARAMETER rather
    /// than a constant for one specific reason: MkInput exists, does exactly
    /// this, and has "robot name…" hard-coded into it. Calling MkInput here
    /// would have shipped "robot name…" inside the PASSWORD box. The lines are
    /// copied; the string is not.
    ///
    /// ⚠ STILL WRITE-ONLY. The placeholder and the border are paint. Nothing
    /// added here reads `fin.text` back, and nothing may: this field carries
    /// the password, ArenaScreen.SetPassword is a one-way push, and a getter
    /// here is one screenshot or one log line away from leaking it.</summary>
    InputField ArenaField(Transform parent, string label, string initial,
                          bool secret, string hint, System.Action<string> onChanged)
    {
        var row = MkPanel("field_" + label, parent, new Color(0f,0f,0f,0f));
        var rle = row.AddComponent<LayoutElement>();
        rle.flexibleHeight = 0f; rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
        var rh = row.AddComponent<HorizontalLayoutGroup>();
        rh.spacing = 6f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false;
        rh.padding = new RectOffset(6,6,2,2);

        var lbl = MkText("lbl", row.transform, label, 13, TextAnchor.MiddleLeft);
        lbl.color = new Color(0.74f, 0.80f, 0.90f);
        lbl.gameObject.AddComponent<LayoutElement>().minWidth = 92f;

        GameObject fill;
        var boxGO = MkFieldBox("box", row.transform, out fill);
        boxGO.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var txt = MkText("lbl", fill.transform, "", 14, TextAnchor.MiddleLeft);
        Stretch(txt.rectTransform);
        txt.rectTransform.offsetMin = new Vector2(8f, 0f); txt.rectTransform.offsetMax = new Vector2(-8f, 0f);
        var ph = MkText("ph", fill.transform, hint ?? "", 14, TextAnchor.MiddleLeft);
        Stretch(ph.rectTransform);
        ph.rectTransform.offsetMin = new Vector2(8f, 0f); ph.rectTransform.offsetMax = new Vector2(-8f, 0f);
        ph.color = new Color(1f, 1f, 1f, 0.35f);
        var fin = boxGO.AddComponent<InputField>();
        // textComponent and placeholder BEFORE text: setting text is what makes
        // InputField decide whether the placeholder shows, so a placeholder
        // assigned afterwards starts out visible under a pre-filled value.
        fin.textComponent = txt;
        fin.placeholder = ph;
        fin.text = initial ?? "";
        if (secret) fin.contentType = InputField.ContentType.Password;
        fin.onValueChanged.AddListener(s => { if (onChanged != null) onChanged(s); });

        // A drag begun on this field would otherwise be eaten by the InputField
        // and the panel it sits in would not move. See FieldScrollRelay.
        // Resolved from the hierarchy rather than passed in: the field does not
        // need to know which list it is in, and a caller cannot forget.
        var sr = boxGO.GetComponentInParent<ScrollRect>();
        if (sr != null)
        {
            var relay = boxGO.AddComponent<FieldScrollRelay>();
            relay.field = fin; relay.target = sr;
        }
        return fin;
    }

    /// <summary>ENLIST when signed in; the account when not. Two faces of one
    /// errand — you cannot enlist without an account, and an account exists in
    /// order to enlist, which is what the sign-in copy has always promised.
    ///
    /// ⚠ THE PASSWORD IS PUSHED, NEVER PULLED. The field writes into
    /// ArenaScreen.SetPassword and nothing reads it back; ArenaScreen clears
    /// it the moment it has been sent. A screen that can hand its password
    /// back is one screenshot, one log line or one careless bench away from
    /// leaking it, and this dock gets photographed on purpose.</summary>
    void RefreshArenaAccount()
    {
        if (arenaScreen == null || arenaAccountContent == null) return;

        // Deliberately NOT keyed on anything derived from the password.
        string stamp = LadderClient.SignedIn + "|" + arenaScreen.Registering
                     + "|" + arenaScreen.MyRobots.Count + "|" + arenaScreen.Who
                     + "|" + (Career.active && Career.Data != null ? Career.Data.activeRobot : -1);
        if (stamp == arenaAccountStamp) return;
        arenaAccountStamp = stamp;

        for (int i = arenaAccountContent.childCount - 1; i >= 0; i--)
            Destroy(arenaAccountContent.GetChild(i).gameObject);

        if (!LadderClient.SignedIn)
        {
            var head = MkText("acchead", arenaAccountContent,
                arenaScreen.Registering ? "CREATE AN ACCOUNT" : "SIGN IN", 16, TextAnchor.MiddleLeft);
            head.color = new Color(0.90f, 0.94f, 1f);
            head.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

            ArenaField(arenaAccountContent, "email", arenaScreen.Email, false,
                       "you@example.com",
                       s => { if (arenaScreen != null) arenaScreen.Email = s; });
            ArenaField(arenaAccountContent, "password", "", true,
                       "password",
                       s => { if (arenaScreen != null) arenaScreen.SetPassword(s); });
            if (arenaScreen.Registering)
                ArenaField(arenaAccountContent, "display name", arenaScreen.DisplayName, false,
                           "what the board calls you",
                           s => { if (arenaScreen != null) arenaScreen.DisplayName = s; });

            var row = MkPanel("accbtns", arenaAccountContent, new Color(0f,0f,0f,0f));
            var rle = row.AddComponent<LayoutElement>();
            rle.flexibleHeight = 0f; rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
            var rh = row.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 4f; rh.childForceExpandWidth = true; rh.childForceExpandHeight = true;

            var go = MkButton("accsubmit", row.transform,
                arenaScreen.Registering ? "CREATE ACCOUNT" : "SIGN IN", 14,
                () => { if (arenaScreen != null) { arenaScreen.SubmitAuth(); arenaAccountStamp = ""; } });
            go.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
            var alt = MkButton("accalt", row.transform,
                arenaScreen.Registering ? "I HAVE AN ACCOUNT" : "CREATE ONE", 14,
                () => { if (arenaScreen != null) { arenaScreen.ToggleRegistering(); arenaAccountStamp = ""; } });
            alt.GetComponent<Image>().color = new Color(0.16f,0.17f,0.21f,1f);

            // ⚠ THIS USED TO SAY "the board is public" AND THE SCREEN DISAGREED
            // WITH IT — 2026-08-10. Signed out, the precedence at the top of
            // RefreshArena forces the ACCOUNT surface and dims THE BOARD, so
            // the first sentence a new player reads promised a thing the very
            // next control refused. A QA pass caught it as a player would:
            // reading the screen, not running a bench.
            //
            // The sentence was the wrong half to keep, but note WHY it was
            // written: GET /v1/leaderboard carries no RequireAuthorization, so
            // the board genuinely IS public server-side and could be shown to a
            // signed-out player. Only the app hides it, deliberately — see the
            // rationale above `onAccount`. Whether that is right is owen's
            // call; making the copy agree with the app is not, and had to
            // happen either way.
            var note = MkText("accnote", arenaAccountContent,
                "signing in is what lets you see the ladder, enlist a robot,\n"
                + "challenge, and spend what you win.", 13, TextAnchor.UpperLeft);
            note.color = new Color(0.70f, 0.76f, 0.86f);
            note.gameObject.AddComponent<LayoutElement>().minHeight = 38f;
            return;
        }

        // ---- signed in: ENLIST -----------------------------------------
        var h2 = MkText("enlisthead", arenaAccountContent, "ENLIST A ROBOT", 16, TextAnchor.MiddleLeft);
        h2.color = new Color(0.90f, 0.94f, 1f);
        h2.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

        CareerRobot ar = null;
        if (Career.active && Career.Data != null)
        {
            int ari = Career.Data.activeRobot;
            if (ari >= 0 && ari < Career.Data.stable.Count) ar = Career.Data.stable[ari];
        }

        if (ar == null || string.IsNullOrEmpty(ar.snapshot))
        {
            var t = MkText("enlistnone", arenaAccountContent,
                "enlisting sends your SAVED career robot to the ladder, where it\n"
                + "fights while you are away.\n\n"
                + "there is no saved robot yet — build one and SAVE it, then come back.",
                14, TextAnchor.UpperLeft);
            t.color = new Color(0.80f, 0.86f, 0.96f);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 76f;
        }
        else
        {
            if (string.IsNullOrEmpty(arenaScreen.EnlistName)) arenaScreen.EnlistName = ar.name ?? "";
            var sending = MkText("enlistsending", arenaAccountContent,
                "sending: " + (ar.name ?? "(unnamed)")
                + (string.IsNullOrEmpty(ar.program) ? "   ·   no program armed" : "   ·   program armed"),
                14, TextAnchor.MiddleLeft);
            sending.color = string.IsNullOrEmpty(ar.program)
                ? new Color(0.90f, 0.78f, 0.60f) : new Color(0.76f, 0.90f, 0.80f);
            sending.gameObject.AddComponent<LayoutElement>().minHeight = 22f;

            ArenaField(arenaAccountContent, "ladder name", arenaScreen.EnlistName, false,
                       ar.name ?? "your robot's name",
                       s => { if (arenaScreen != null) arenaScreen.EnlistName = s; });

            var rule = MkText("enlistrule", arenaAccountContent,
                "re-enlisting under a name you already use replaces that robot's\n"
                + "build and keeps its rating. a new name starts at placement.",
                12, TextAnchor.UpperLeft);
            rule.color = new Color(0.66f, 0.72f, 0.82f);
            rule.gameObject.AddComponent<LayoutElement>().minHeight = 34f;

            var eb = MkButton("enlistgo", arenaAccountContent, "ENLIST", 14,
                () => { if (arenaScreen != null) { arenaScreen.EnlistNow(); arenaAccountStamp = ""; } });
            var ele2 = eb.gameObject.AddComponent<LayoutElement>();
            ele2.flexibleHeight = 0f; ele2.minHeight = TouchRow(); ele2.preferredHeight = TouchRow();
            eb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
        }

        if (arenaScreen.MyRobots.Count > 0)
        {
            var t = MkText("enlistmine", arenaAccountContent, "already on the ladder:", 13, TextAnchor.MiddleLeft);
            t.color = new Color(0.70f, 0.76f, 0.86f);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
            foreach (var m in arenaScreen.MyRobots)
            {
                // StatusText lives on MyRobot so this row and ArenaScreen's
                // OnGUI copy cannot drift. It used to say "waiting to be
                // checked" for a REJECTED robot as well as a pending one,
                // forever, because the server sent nothing that told them apart.
                var r = MkText("mine_" + m.id, arenaAccountContent,
                    "   " + m.name + "   ·   " + m.StatusText,
                    13, TextAnchor.MiddleLeft);
                // Amber for a refusal — it is the one state the player can act
                // on, and it must not read as the same "still waiting" grey it
                // was indistinguishable from until now.
                r.color = m.CanFight  ? new Color(0.82f, 0.88f, 0.96f)
                        : m.Rejected  ? new Color(1.00f, 0.75f, 0.30f)
                                      : new Color(0.72f, 0.74f, 0.66f);
                r.gameObject.AddComponent<LayoutElement>().minHeight = 20f;
            }
        }

        var so = MkButton("accsignout", arenaAccountContent,
            "SIGN OUT" + (string.IsNullOrEmpty(arenaScreen.Who) ? "" : " (" + arenaScreen.Who + ")"), 13,
            () => { if (arenaScreen != null) { arenaScreen.SignOut(); arenaAccountStamp = ""; } });
        var sole = so.gameObject.AddComponent<LayoutElement>();
        sole.flexibleHeight = 0f; sole.minHeight = TouchRow(); sole.preferredHeight = TouchRow();
        so.GetComponent<Image>().color = new Color(0.16f,0.17f,0.21f,1f);
    }

    /// <summary>MY FIGHTS, and the launcher that plays one back.
    ///
    /// ⚠ IT DOES NOT PICK THE URL. ArenaScreen.Watch takes replayUrls[0], and
    /// that index is a CONTRACT: FightWorkerLoop uploads the bout recordings
    /// first and appends the scorecard LAST, precisely so a client can take
    /// the first without inspecting anything. Re-deriving "the playable one"
    /// here would be a second reading of that rule, and the last time nobody
    /// had tried to PLAY a replay, every URL on every match pointed at a
    /// scorecard for a day.</summary>
    void RefreshArenaInbox()
    {
        if (arenaScreen == null || arenaInboxContent == null) return;

        var inbox = arenaScreen.Inbox;
        string stamp = inbox.Count + "|" + arenaScreen.Playing + "|" + LadderClient.SignedIn;
        for (int i = 0; i < inbox.Count && i < 6; i++) stamp += "|" + inbox[i].matchId + inbox[i].outcome;
        if (stamp == arenaInboxStamp) return;
        arenaInboxStamp = stamp;

        for (int i = arenaInboxContent.childCount - 1; i >= 0; i--)
            Destroy(arenaInboxContent.GetChild(i).gameObject);

        if (arenaScreen.Playing)
        {
            var stop = MkButton("arenastopreplay", arenaInboxContent, "STOP REPLAY", 14,
                                () => { if (arenaScreen != null) { arenaScreen.StopReplay(); arenaInboxStamp = ""; } });
            var sle2 = stop.gameObject.AddComponent<LayoutElement>();
            sle2.flexibleHeight = 0f; sle2.minHeight = TouchRow(); sle2.preferredHeight = TouchRow();
            stop.GetComponent<Image>().color = new Color(0.30f,0.18f,0.18f,1f);
        }

        if (!LadderClient.SignedIn)
        {
            var row = MkPanel("inbox_signedout", arenaInboxContent, new Color(0.13f,0.14f,0.18f,0.9f));
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 52f; le.preferredHeight = 52f;
            var t = MkText("lbl", row.transform,
                "sign in to see your fights.", 14, TextAnchor.MiddleLeft);
            t.color = new Color(0.80f, 0.86f, 0.96f);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(10f, 2f); t.rectTransform.offsetMax = new Vector2(-10f, -2f);
            return;
        }

        if (inbox.Count == 0)
        {
            var row = MkPanel("inbox_empty", arenaInboxContent, new Color(0.13f,0.14f,0.18f,0.9f));
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 52f; le.preferredHeight = 52f;
            var t = MkText("lbl", row.transform,
                "no fights yet — SCOUT someone on the board and challenge them.",
                14, TextAnchor.MiddleLeft);
            t.color = new Color(0.78f, 0.82f, 0.90f);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(10f, 2f); t.rectTransform.offsetMax = new Vector2(-10f, -2f);
            return;
        }

        for (int i = 0; i < inbox.Count; i++)
        {
            var m = inbox[i];
            // ⚠ EXACT COMPARISON, AND THE SUBSTRING VERSION WAS WRONG FOR THE
            // WHOLE LIFE OF THIS SCREEN. The server sends "WON" (Program.cs's
            // outcome derivation) and this line tested Contains("WIN") —
            // W-O-N does not contain W-I-N, so `win` was NEVER true and every
            // victory fell through to the PENDING grey below. The loss half
            // passed only by luck: "LOST" does contain "LOS".
            //
            // So defeats glowed red and wins looked unresolved, on a ladder
            // whose whole premise is coming back to see what happened. Nothing
            // failed; a wrong colour is not an exception, and no bench asserts
            // a background. Compare the contract exactly rather than sniffing
            // at it — "WON"/"LOST" is what LadderClientBench pins.
            //
            // DRAW and PENDING deliberately keep falling through to neutral.
            bool win  = m.outcome == "WON";
            bool loss = m.outcome == "LOST";
            var row = MkPanel("inboxrow_" + i, arenaInboxContent,
                win ? new Color(0.12f,0.18f,0.14f,1f)
                    : loss ? new Color(0.18f,0.12f,0.12f,1f)
                           : new Color(0.10f,0.11f,0.14f,1f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
            var rh = row.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false;
            rh.padding = new RectOffset(8,4,2,2);

            var lbl = MkText("lbl", row.transform,
                m.myRobot + "  vs  " + m.opponent + "\n"
                + (string.IsNullOrEmpty(m.outcome) ? m.status : m.outcome)
                + "  ·  " + m.category + (m.gap > 0 ? "  +" + m.gap : ""),
                13, TextAnchor.MiddleLeft);
            lbl.color = win ? new Color(0.76f, 0.94f, 0.80f)
                      : loss ? new Color(0.96f, 0.80f, 0.78f)
                             : new Color(0.86f, 0.90f, 0.96f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (m.replayUrls.Count > 0)
            {
                var mm = m;
                // ⚠ THE DOCK CLOSES BEFORE THE REPLAY STARTS, and the order of
                // these three lines is the whole fix.
                //
                // ArenaScreen.Watch attaches the camera to the fight, so the
                // replay was rendering behind an opaque dock panel: the player
                // pressed WATCH, the status line said "playing X vs Y", and
                // they looked at MY FIGHTS for the length of the bout. Nothing
                // about it read as broken, which is why it survived.
                //
                // CLOSE FIRST, then call. StartCoroutine runs a coroutine's
                // body up to its first yield SYNCHRONOUSLY, so Watch's
                // early-outs — no replay, failed download — fire inside
                // WatchNow. Closing afterwards would slam the dock shut again
                // right after Watch had reopened it, and a failed download
                // would strand the player on an empty arena with no control on
                // screen. Closing first means every one of those exits runs
                // with the dock already shut and reopens it for real.
                //
                // The dock HANDLE stays live while it is closed (it always
                // does), so a long replay is still escapable by hand — which is
                // the only way to reach STOP while the panel is down.
                var wb = MkButton("inboxwatch_" + i, row.transform, "WATCH", 13,
                                  () =>
                                  {
                                      if (arenaScreen == null) return;
                                      SetDockOpen(false);
                                      // Refused (already busy): nothing will
                                      // ever reopen it, so undo our own half.
                                      if (!arenaScreen.WatchNow(mm)) SetDockOpen(true);
                                      arenaInboxStamp = "";
                                  });
                wb.gameObject.AddComponent<LayoutElement>().minWidth = 86f;
                wb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
            }
            else
            {
                // A match with no recording is not a broken button — it is a
                // match whose replay never uploaded. Say which.
                var no = MkText("norep", row.transform, "no replay", 12, TextAnchor.MiddleCenter);
                no.color = new Color(0.55f, 0.58f, 0.64f);
                no.gameObject.AddComponent<LayoutElement>().minWidth = 86f;
            }
        }
    }

    /// <summary>The scouting card, in UGUI. §1.3: everything on it is public
    /// BY DESIGN — the program's contents and the payload url are not here,
    /// and that omission is the rule, not an oversight.
    ///
    /// ⚠ IT ASKS ArenaScreen FOR THE GATE. Eligibility and the three refusals
    /// live in ChallengeBlocker/EligibleFor, because this is the surface that
    /// SPENDS SCRAP and a second copy of "you can punch up, never down" is a
    /// second chance to tell a player the wrong reason.</summary>
    void RefreshArenaCard()
    {
        if (arenaScreen == null || arenaCardContent == null) return;
        var card = arenaScreen.Card;
        if (card == null) return;

        // Rebuild only on a real change: the card, the pick, the confirm step
        // and the wallet are the whole of its state.
        string stamp = card.snapshotId + "|" + arenaScreen.Pick + "|" + arenaScreen.Pending
                     + "|" + arenaScreen.Balance + "|" + arenaScreen.MyRobots.Count;
        if (stamp == arenaCardStamp) return;
        arenaCardStamp = stamp;

        for (int i = arenaCardContent.childCount - 1; i >= 0; i--)
            Destroy(arenaCardContent.GetChild(i).gameObject);

        var head = MkText("cardhead", arenaCardContent,
            card.robotName + "   ·   " + card.category + "   ·   " + card.massKg + " kg",
            16, TextAnchor.MiddleLeft);
        head.color = new Color(0.90f, 0.94f, 1f);
        head.gameObject.AddComponent<LayoutElement>().minHeight = 26f;

        var parts = MkText("cardparts", arenaCardContent,
            card.parts.Count + " parts: " + string.Join(", ", card.parts.ToArray()),
            13, TextAnchor.UpperLeft);
        parts.color = new Color(0.74f, 0.80f, 0.90f);
        var ple = parts.gameObject.AddComponent<LayoutElement>(); ple.minHeight = 34f;

        // WHETHER they have a program, never WHAT it is.
        var prog = MkText("cardprog", arenaCardContent,
            card.hasProgram ? "has a program (contents private)" : "no program — it will not move",
            13, TextAnchor.MiddleLeft);
        prog.color = card.hasProgram ? new Color(0.72f, 0.86f, 0.76f) : new Color(0.86f, 0.78f, 0.62f);
        prog.gameObject.AddComponent<LayoutElement>().minHeight = 20f;

        string blocked = arenaScreen.ChallengeBlocker(card);
        if (blocked != null)
        {
            var b = MkText("cardblocked", arenaCardContent, blocked, 14, TextAnchor.UpperLeft);
            b.color = new Color(0.86f, 0.82f, 0.68f);
            b.gameObject.AddComponent<LayoutElement>().minHeight = 40f;
        }
        else
        {
            var eligible = arenaScreen.EligibleFor(card);
            int stake = arenaScreen.StakeForPick(card);

            // Which of yours answers. One row of buttons, capped at four —
            // past that the row stops being tappable and starts being a list.
            var pickRow = MkPanel("cardpicks", arenaCardContent, new Color(0f,0f,0f,0f));
            var prle = pickRow.AddComponent<LayoutElement>();
            prle.flexibleHeight = 0f; prle.minHeight = TouchRow(); prle.preferredHeight = TouchRow();
            var prh = pickRow.AddComponent<HorizontalLayoutGroup>();
            prh.spacing = 3f; prh.childForceExpandWidth = true; prh.childForceExpandHeight = true;
            for (int i = 0; i < eligible.Count && i < 4; i++)
            {
                int idx = i;
                bool on = i == Mathf.Clamp(arenaScreen.Pick, 0, eligible.Count - 1);
                var pb = MkButton("cardpick_" + i, pickRow.transform, eligible[i].name, 13,
                                  () => { if (arenaScreen != null) { arenaScreen.SetPick(idx); arenaCardStamp = ""; } });
                var pimg = pb.GetComponent<Image>();
                if (pimg != null) pimg.color = on ? new Color(0.20f,0.45f,0.65f,1f) : new Color(0.16f,0.17f,0.21f,1f);
                var pt = pb.GetComponentInChildren<Text>();
                if (pt != null) pt.color = on ? Color.white : new Color(0.72f,0.78f,0.88f);
            }

            if (!arenaScreen.Pending)
            {
                var cb = MkButton("cardchallenge", arenaCardContent,
                                  "CHALLENGE FOR " + stake + " SCRAP", 14,
                                  () => { if (arenaScreen != null) { arenaScreen.ArmChallenge(); arenaCardStamp = ""; } });
                var cle2 = cb.gameObject.AddComponent<LayoutElement>();
                cle2.flexibleHeight = 0f; cle2.minHeight = TouchRow(); cle2.preferredHeight = TouchRow();
                cb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
            }
            else
            {
                // The confirm step spells out what is at risk and what comes
                // back. A stake is the only thing on this screen that can cost
                // the player something, so it never happens on one tap.
                var warn = MkText("cardstake", arenaCardContent,
                    "stake " + stake + " scrap"
                    + (arenaScreen.Balance >= 0 ? " of your " + arenaScreen.Balance : "")
                    + " — returned if you win or draw, lost if you do not.",
                    13, TextAnchor.UpperLeft);
                warn.color = new Color(1f, 0.87f, 0.55f);
                warn.gameObject.AddComponent<LayoutElement>().minHeight = 34f;

                var row = MkPanel("cardconfirmrow", arenaCardContent, new Color(0f,0f,0f,0f));
                var rle2 = row.AddComponent<LayoutElement>();
                rle2.flexibleHeight = 0f; rle2.minHeight = TouchRow(); rle2.preferredHeight = TouchRow();
                var rh = row.AddComponent<HorizontalLayoutGroup>();
                rh.spacing = 4f; rh.childForceExpandWidth = true; rh.childForceExpandHeight = true;
                var ok = MkButton("cardconfirm", row.transform, "CONFIRM", 14,
                                  () => { if (arenaScreen != null) { arenaScreen.ConfirmChallenge(); arenaCardStamp = ""; } });
                ok.GetComponent<Image>().color = new Color(0.18f,0.42f,0.28f,1f);
                var no = MkButton("cardcancel", row.transform, "CANCEL", 14,
                                  () => { if (arenaScreen != null) { arenaScreen.CancelChallenge(); arenaCardStamp = ""; } });
                no.GetComponent<Image>().color = new Color(0.30f,0.18f,0.18f,1f);
            }
        }

        var close = MkButton("cardclose", arenaCardContent, "BACK TO THE BOARD", 13,
                             () => { if (arenaScreen != null) { arenaScreen.CloseCard(); arenaCardStamp = ""; } });
        var xle = close.gameObject.AddComponent<LayoutElement>();
        xle.flexibleHeight = 0f; xle.minHeight = TouchRow(); xle.preferredHeight = TouchRow();
        close.GetComponent<Image>().color = new Color(0.16f,0.17f,0.21f,1f);
    }

    /// <summary>Set the ARENA's status line, and in a DEVELOPMENT BUILD say
    /// WHICH SERVER it is talking to.
    ///
    /// ⚠ THIS EXISTS BECAUSE THE ARTEFACT COULD NOT ANSWER IT. LadderClient
    /// defaults to LOCAL_DEV in the editor and PRODUCTION in a build, and the
    /// il2cpp output PROVES the #if resolved to a single unconditional return
    /// — but the literal it returns is a metadata index, so which one it is
    /// cannot be read out of the Xcode project. Rather than assert it, the
    /// app now says it out loud on the device, where the question is settled
    /// in one glance.
    ///
    /// Debug.isDebugBuild is false in a release build, so a shipped player
    /// never shows a player a URL — that was one of the ARENA's judged
    /// defects (docs/ARENA_Judged_2026-08-10 §2.3) and is not being
    /// reintroduced.</summary>
    void SetArenaStatus(string s)
    {
        if (arenaStatus == null) return;
        arenaStatus.text = Debug.isDebugBuild ? s + "   ·   [dev] " + LadderClient.BaseUrl : s;
        arenaStatus.color = new Color(0.80f, 0.88f, 1f);
    }

    /// <summary>Repaint the ARENA board. Cheap every frame EXCEPT when the
    /// board actually changed — rebuilding a scrolled list under the player's
    /// finger every frame is how a list stops being scrollable.</summary>
    void RefreshArena()
    {
        if (arenaScreen == null || arenaBoardContent == null) return;

        // The card REPLACES the board rather than floating over it. One
        // column, one thing at a time; a card that covers the list it came
        // from is how a player loses their place on a phone.
        // Three views, one column: the CARD wins, then MY FIGHTS, then the
        // BOARD. The card wins because it is the only one you arrive at by
        // choosing something, and dropping a player back to a list they did
        // not ask for is how a screen feels like it fought them.
        // Signed out, the ACCOUNT surface wins outright: every other view is
        // something you can only do with an account, and a board you cannot
        // act on is a worse first screen than the one asking you to sign in.
        bool onCard = arenaScreen.Card != null;
        bool onAccount = !onCard && (arenaScreen.ShowEnlistPanel || !LadderClient.SignedIn);
        bool onInbox = !onCard && !onAccount && arenaScreen.ShowInbox;
        bool onBoard = !onCard && !onAccount && !onInbox;

        if (arenaCardRoot != null && arenaCardRoot.activeSelf != onCard)
        { arenaCardRoot.SetActive(onCard); arenaCardStamp = ""; }
        if (arenaAccountRoot != null && arenaAccountRoot.activeSelf != onAccount)
        { arenaAccountRoot.SetActive(onAccount); arenaAccountStamp = ""; }
        if (arenaInboxRoot != null && arenaInboxRoot.activeSelf != onInbox)
        { arenaInboxRoot.SetActive(onInbox); arenaInboxStamp = ""; }
        // Held as a reference, not walked to. content->viewport->scroll is two
        // hops today and would be a silent mis-toggle the moment a wrapper is
        // added; the first version of this line counted three.
        if (arenaBoardRoot != null && arenaBoardRoot.activeSelf != onBoard)
            arenaBoardRoot.SetActive(onBoard);
        // The weight filters belong to the BOARD. They mean nothing against
        // your own fights or a single card, and a filter row that does nothing
        // is worse than one that is absent.
        if (arenaCatRow != null && arenaCatRow.activeSelf != onBoard)
            arenaCatRow.SetActive(onBoard);

        // Signed out, only the ACCOUNT section is reachable — the other two are
        // overruled by the precedence above no matter what they are set to, so
        // they must not look otherwise. Signed in, all three are live and this
        // is exactly the call it was before.
        bool secLive = LadderClient.SignedIn;
        TintSection(arenaSecBoard, onBoard, secLive);
        TintSection(arenaSecInbox, onInbox, secLive);
        TintSection(arenaSecAccount, onAccount, true);

        if (onCard) { RefreshArenaCard(); return; }
        if (onAccount)
        {
            RefreshArenaAccount();
            if (arenaStatus != null)
            {
                bool own = arenaScreen.StatusScope == ArenaScreen.SC_ACCOUNT
                           && !string.IsNullOrEmpty(arenaScreen.Status);
                // Same correction as the note in RefreshArenaAccount: signed
                // out, this line sat directly above a dimmed THE BOARD while
                // telling the player the board was public.
                SetArenaStatus(own ? arenaScreen.Status
                    : LadderClient.SignedIn ? "signed in" : "sign in to see the ladder and enlist a robot");
            }
            return;
        }
        if (onInbox)
        {
            RefreshArenaInbox();
            if (arenaStatus != null)
            {
                // While a replay plays, the status line carries its progress —
                // the player is looking at the arena, not this dock.
                //
                // ⚠ AND IT ONLY SHOWS A STATUS THIS SURFACE WROTE. The first
                // version printed ArenaScreen.Status unconditionally and the
                // fights list opened reading "8 ranked" — the BOARD's count,
                // inherited. That is the exact leak §2.4 of the ARENA
                // judgement recorded, reproduced in the port within an hour of
                // my having written it down.
                string rl = arenaScreen.ReplayLine;
                bool ownStatus = arenaScreen.StatusScope == ArenaScreen.SC_INBOX
                                 && !string.IsNullOrEmpty(arenaScreen.Status);
                SetArenaStatus(!string.IsNullOrEmpty(rl) ? rl
                               : ownStatus ? arenaScreen.Status
                               : arenaScreen.Inbox.Count + " fight(s)");
            }
            return;
        }

        if (arenaStatus != null)
        {
            // Same rule as the inbox: the board shows only what the BOARD
            // wrote, and otherwise says its own count.
            bool ownStatus = arenaScreen.StatusScope == ArenaScreen.SC_BOARD
                             && !string.IsNullOrEmpty(arenaScreen.Status);
            SetArenaStatus(ownStatus ? arenaScreen.Status
                : arenaScreen.CategoryLabel + " · " + arenaScreen.Board.Count + " ranked");
        }
        for (int i = 0; i < arenaCatBtns.Count; i++)
        {
            var b = arenaCatBtns[i];
            if (b == null) continue;
            bool on = i == arenaScreen.CategoryIndex;
            var img = b.GetComponent<Image>();
            if (img != null) img.color = on ? new Color(0.20f,0.45f,0.65f,1f) : new Color(0.16f,0.17f,0.21f,1f);
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.color = on ? Color.white : new Color(0.72f,0.78f,0.88f);
        }

        // A cheap identity for "the list changed": count, category, and the
        // first row's snapshot. Enough to catch a reload, and it costs nothing.
        var board = arenaScreen.Board;
        int stamp = board.Count * 31 + arenaScreen.CategoryIndex * 7
                  + (board.Count > 0 ? board[0].activeSnapshotId.GetHashCode() : 0);
        if (stamp == arenaBoardStamp) return;
        arenaBoardStamp = stamp;

        for (int i = arenaBoardContent.childCount - 1; i >= 0; i--)
            Destroy(arenaBoardContent.GetChild(i).gameObject);

        if (board.Count == 0)
        {
            // ⚠ "nobody ranked here yet" and "the request failed" look
            // identical in an empty list, and LadderClientBench's header calls
            // that out as the worst failure mode this client has. The status
            // line above carries the error; this says only what an EMPTY board
            // means, and never pretends a failure is an empty ladder.
            var erow = MkPanel("arena_empty", arenaBoardContent, new Color(0.13f,0.14f,0.18f,0.9f));
            var ele = erow.AddComponent<LayoutElement>(); ele.minHeight = 52f; ele.preferredHeight = 52f;
            var et = MkText("lbl", erow.transform,
                "nobody ranked in " + arenaScreen.CategoryLabel + " yet.\n"
                + "ENLIST a robot to be the first.", 14, TextAnchor.MiddleLeft);
            et.color = new Color(0.78f, 0.82f, 0.90f);
            Stretch(et.rectTransform);
            et.rectTransform.offsetMin = new Vector2(10f, 2f); et.rectTransform.offsetMax = new Vector2(-10f, -2f);
            return;
        }

        for (int i = 0; i < board.Count; i++)
        {
            var e = board[i];
            var row = MkPanel("arenarow_" + i, arenaBoardContent, new Color(0.10f,0.11f,0.14f,1f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
            var rh = row.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false;
            rh.padding = new RectOffset(8,4,2,2);

            var rank = MkText("rank", row.transform, e.rank + ".", 14, TextAnchor.MiddleLeft);
            rank.color = new Color(0.62f, 0.68f, 0.78f);
            rank.gameObject.AddComponent<LayoutElement>().minWidth = 34f;

            // Name and owner on one line, the rating and its CONFIDENCE on the
            // next. A 1400 at RD 350 has not earned what a 1400 at RD 60 has,
            // and the board must not flatter the difference away.
            var lbl = MkText("lbl", row.transform,
                e.robotName + "  ·  " + e.owner + "\n"
                + Mathf.RoundToInt(e.rating) + "  "
                + (e.provisional ? "provisional" : "±" + Mathf.RoundToInt(e.deviation))
                + "  ·  " + e.category,
                13, TextAnchor.MiddleLeft);
            lbl.color = e.provisional ? new Color(0.80f, 0.80f, 0.72f) : new Color(0.88f, 0.92f, 1f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (!string.IsNullOrEmpty(e.activeSnapshotId))
            {
                var ee = e;
                var sb = MkButton("arenascout_" + i, row.transform, "SCOUT", 13,
                                  () => { if (arenaScreen != null) arenaScreen.ScoutNow(ee); });
                sb.gameObject.AddComponent<LayoutElement>().minWidth = 86f;
                sb.GetComponent<Image>().color = new Color(0.20f,0.45f,0.65f,1f);
            }
        }
    }

    /// <summary>The COSMETICS shelf: same dock, different currency. Its rows
    /// are built at REFRESH rather than here, because the catalogue comes off
    /// the network and this runs before anyone has signed in.</summary>
    void BuildCosmeticsShelf()
    {
        cosmeticsRoot = MkPanel("shopcosroot", shopPanel.transform, new Color(0f,0f,0f,0f));
        var rle = cosmeticsRoot.AddComponent<LayoutElement>(); rle.flexibleHeight = 1f; rle.minHeight = 96f;
        var rv = cosmeticsRoot.AddComponent<VerticalLayoutGroup>();
        rv.spacing = 4f; rv.childForceExpandWidth = true; rv.childForceExpandHeight = false;

        var scrollGO = MkPanel("cosscroll", cosmeticsRoot.transform, new Color(0f,0f,0f,0f));
        var sle = scrollGO.AddComponent<LayoutElement>(); sle.flexibleHeight = 1f; sle.minHeight = 96f;
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = MkPanel("cosviewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("coscontent", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,1f); crt.anchorMax = new Vector2(1f,1f);
        crt.pivot = new Vector2(0.5f,1f); crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(0f, 0f);
        var clg = content.AddComponent<VerticalLayoutGroup>();
        clg.spacing = 4f; clg.childForceExpandWidth = true; clg.childForceExpandHeight = false;
        clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        AddListOverflow(scrollGO, scroll, vp);   // R2 finding 8: COSMETICS
        cosmeticsContent = content.transform;
    }

    /// <summary>PARTS or COSMETICS. Loads the cosmetics shelf on the way in —
    /// its catalogue and the wallet are both network reads, and a shelf that
    /// shows an empty list while it fetches reads as "you own nothing".</summary>
    void ShowShopSection(int s)
    {
        shopSection = s;
        if (shopPartsRoot != null) shopPartsRoot.SetActive(s == 0);
        if (cosmeticsRoot != null) cosmeticsRoot.SetActive(s == 1);
        TintSection(shopSecParts, s == 0);
        TintSection(shopSecCosmetics, s == 1);
        if (s == 1 && !cosmeticsBusy) StartCoroutine(LoadCosmetics());
        else RefreshShop();
    }

    void TintSection(Button b, bool on) { TintSection(b, on, true); }

    /// <summary>Selected / unselected / DEAD, in one place. `live` is the third
    /// state and it is not the same as "unselected": an unselected section is
    /// somewhere you can go, a dead one is somewhere you cannot, and painting
    /// them the same is what made the signed-out ARENA look functional. Dead
    /// borrows the SHOP's gate palette rather than inventing a fourth grey —
    /// one vocabulary for "you cannot do this yet" across the whole dock.</summary>
    void TintSection(Button b, bool on, bool live)
    {
        if (b == null) return;
        var img = b.GetComponent<Image>();
        if (img != null)
            img.color = !live ? GATE_DEAD
                      : on ? new Color(0.20f,0.45f,0.65f,1f) : new Color(0.16f,0.17f,0.21f,1f);
        var t = b.GetComponentInChildren<Text>();
        if (t != null)
            t.color = !live ? GATE_TXT_DEAD
                    : on ? Color.white : new Color(0.72f,0.78f,0.88f);
    }

    /// <summary>Wallet THEN catalogue, both before drawing. Affordability greys
    /// the BUY buttons, and `ladderBalance` starts at -1 meaning "unknown" — a
    /// shelf drawn without a wallet read shows every item disabled with no
    /// explanation. That exact bug was caught once already by screenshotting
    /// the ARENA shop's deep-link path: six items, six dead buttons.</summary>
    System.Collections.IEnumerator LoadCosmetics()
    {
        cosmeticsBusy = true;
        RefreshShop();                       // paint "loading…" before the wait
        if (LadderClient.SignedIn)
        {
            yield return LadderClient.Wallet((b, err) => { if (err == null) ladderBalance = b; });
            yield return LadderClient.Cosmetics((rows, err) =>
            {
                cosmetics.Clear();
                if (err == null && rows != null) cosmetics.AddRange(rows);
            });
        }
        cosmeticsBusy = false;
        RefreshCosmetics();
        RefreshShop();
    }

    System.Collections.IEnumerator BuyCosmetic(Cosmetic c)
    {
        cosmeticsBusy = true; RefreshShop();
        yield return LadderClient.BuyCosmetic(c.id, (bal, err) =>
        {
            if (err != null) { shopNote = "shop: " + err; shopNoteBad = true; }
            else { ladderBalance = bal; shopNote = "bought " + c.name; shopNoteBad = false; }
        });
        cosmeticsBusy = false;
        yield return LoadCosmetics();
    }

    /// <summary>The one-way valve, §2.3. Ladder winnings move INTO career
    /// scrap and never back.</summary>
    System.Collections.IEnumerator DepositToCareer()
    {
        int amt;
        if (!int.TryParse(depositText, out amt) || amt <= 0)
        { shopNote = "enter a positive amount to move"; shopNoteBad = true; RefreshShop(); yield break; }
        cosmeticsBusy = true; RefreshShop();
        yield return LadderClient.Deposit(amt, depositKey, (bal, err) =>
        {
            if (err != null) { shopNote = "deposit: " + err; shopNoteBad = true; return; }
            ladderBalance = bal;
            depositText = "";
            depositKey = System.Guid.NewGuid().ToString();   // only now is a new key correct
            shopNote = "moved " + amt + " to career scrap; " + bal + " left on the ladder";
            shopNoteBad = false;
        });
        cosmeticsBusy = false;
        RefreshCosmetics();
        RefreshShop();
    }

    void RefreshCosmetics()
    {
        if (cosmeticsContent == null) return;
        for (int i = cosmeticsContent.childCount - 1; i >= 0; i--)
            Destroy(cosmeticsContent.GetChild(i).gameObject);

        if (!LadderClient.SignedIn)
        {
            // Not an error, and it must not look like one. Cosmetics are bought
            // with ladder winnings, and you cannot have any until you have an
            // account — so this says what to do, in the ARENA, by name.
            var row = MkPanel("cos_signedout", cosmeticsContent, new Color(0.13f,0.14f,0.18f,0.9f));
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 64f; le.preferredHeight = 64f;
            var t = MkText("lbl", row.transform,
                "cosmetics are bought with LADDER winnings.\n"
                + "sign in on the ARENA tab, then come back.", 14, TextAnchor.MiddleLeft);
            t.color = new Color(0.80f, 0.86f, 0.96f);
            Stretch(t.rectTransform);
            t.rectTransform.offsetMin = new Vector2(10f, 2f); t.rectTransform.offsetMax = new Vector2(-10f, -2f);
            return;
        }

        // ---- the valve, at the seam ------------------------------------
        // This is the only screen that shows BOTH balances, so it is the only
        // place moving one into the other is legible. It lived in the ARENA
        // shop, which had no entry point, so a player with ladder winnings had
        // no way to discover they could be spent in the career at all.
        var vrow = MkPanel("cos_deposit", cosmeticsContent, new Color(0.10f,0.16f,0.13f,0.95f));
        var vle = vrow.AddComponent<LayoutElement>(); vle.minHeight = TouchRow(); vle.preferredHeight = TouchRow();
        var vh = vrow.AddComponent<HorizontalLayoutGroup>();
        vh.spacing = 6f; vh.childForceExpandHeight = true; vh.childForceExpandWidth = false;
        vh.padding = new RectOffset(8,6,2,2);
        var vlbl = MkText("lbl", vrow.transform, "move ladder scrap to career", 13, TextAnchor.MiddleLeft);
        vlbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // Same box, same reason: this one sat at 1.22:1 against the green
        // deposit row and was the second-hardest field in the dock to see.
        // A player who cannot find the box cannot spend what they won.
        GameObject ffill;
        var fieldGO = MkFieldBox("cos_deposit_field", vrow.transform, out ffill);
        fieldGO.AddComponent<LayoutElement>().minWidth = 90f;
        var ftxt = MkText("lbl", ffill.transform, "", 14, TextAnchor.MiddleCenter);
        Stretch(ftxt.rectTransform);
        var fph = MkText("ph", ffill.transform, "amount", 14, TextAnchor.MiddleCenter);
        Stretch(fph.rectTransform);
        fph.color = new Color(1f, 1f, 1f, 0.35f);
        var fin = fieldGO.AddComponent<InputField>();
        fin.textComponent = ftxt; fin.placeholder = fph; fin.text = depositText ?? "";
        fin.contentType = InputField.ContentType.IntegerNumber;
        fin.onValueChanged.AddListener(s => depositText = s);
        var dbtn = MkButton("cos_deposit_go", vrow.transform, "TO CAREER", 13,
                            () => { if (!cosmeticsBusy) StartCoroutine(DepositToCareer()); });
        dbtn.gameObject.AddComponent<LayoutElement>().minWidth = 110f;
        dbtn.GetComponent<Image>().color = new Color(0.18f,0.42f,0.28f,1f);

        var note = MkText("cos_oneway", cosmeticsContent,
            "one-way: ladder winnings become career scrap, never the other way.",
            12, TextAnchor.MiddleLeft);
        note.color = new Color(0.62f, 0.70f, 0.62f);
        note.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

        if (cosmetics.Count == 0)
        {
            var erow = MkPanel("cos_empty", cosmeticsContent, new Color(0.13f,0.14f,0.18f,0.9f));
            var ele = erow.AddComponent<LayoutElement>(); ele.minHeight = 44f; ele.preferredHeight = 44f;
            var et = MkText("lbl", erow.transform,
                cosmeticsBusy ? "loading the shelf…" : "nothing on the shelf yet.",
                14, TextAnchor.MiddleLeft);
            et.color = new Color(0.78f, 0.82f, 0.90f);
            Stretch(et.rectTransform);
            et.rectTransform.offsetMin = new Vector2(10f, 2f); et.rectTransform.offsetMax = new Vector2(-10f, -2f);
            return;
        }

        for (int i = 0; i < cosmetics.Count; i++)
        {
            var c = cosmetics[i];
            var row = MkPanel("cos_" + c.id, cosmeticsContent,
                c.owned ? new Color(0.14f,0.20f,0.16f,0.95f) : new Color(0.10f,0.11f,0.14f,1f));
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
            var rh = row.AddComponent<HorizontalLayoutGroup>();
            rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false;
            rh.padding = new RectOffset(8,4,2,2);
            var lbl = MkText("lbl", row.transform,
                c.name + "  ·  " + c.kind + (c.owned ? "  ·  owned" : "  ·  " + c.price),
                14, TextAnchor.MiddleLeft);
            lbl.color = c.owned ? new Color(0.72f, 0.92f, 0.78f) : new Color(0.86f, 0.90f, 0.96f);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            if (!c.owned)
            {
                var cc = c;
                bool afford = ladderBalance < 0 || ladderBalance >= c.price;
                var bb = MkButton("cosbuy_" + c.id, row.transform, "BUY", 13,
                                  () => { if (!cosmeticsBusy) StartCoroutine(BuyCosmetic(cc)); });
                bb.gameObject.AddComponent<LayoutElement>().minWidth = 76f;
                bb.GetComponent<Image>().color = afford ? new Color(0.20f,0.45f,0.65f,1f)
                                                        : new Color(0.20f,0.21f,0.25f,1f);
                var bt = bb.GetComponentInChildren<Text>();
                if (bt != null) bt.color = afford ? Color.white : new Color(0.55f,0.58f,0.64f);
            }
        }
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

        // \u26a0 THE HEADER NAMES THE CURRENCY OF THE SHELF YOU ARE ON. Two
        // balances in one tab is only safe while it is never ambiguous which
        // one a price is in \u2014 the cosmetics shelf must never read "SCRAP N"
        // and charge the ladder wallet.
        if (shopSection == 1)
        {
            string bal = !LadderClient.SignedIn ? "not signed in"
                       : ladderBalance < 0      ? "reading\u2026"
                                                : "LADDER " + ladderBalance;
            shopHeader.text = !string.IsNullOrEmpty(shopNote)
                ? (shopNoteBad ? "\u26a0 " : "") + shopNote + "   \u00b7   " + bal
                : bal + (cosmeticsBusy ? "   \u00b7   loading\u2026" : "")
                      + "   \u00b7   cosmetic only \u2014 nothing here touches a fight";
        }
        else
        {
            shopHeader.text = !string.IsNullOrEmpty(shopNote)
                ? (shopNoteBad ? "\u26a0 " : "") + shopNote + "   \u00b7   SCRAP " + Career.Data.scrap
                : "SCRAP " + Career.Data.scrap
                  + "   \u00b7   tap a part to compare its materials   \u00b7   HP/kg is what a weight cap buys"
                  + "   \u00b7   SELL returns 50%; to change a material, SELL and BUY";
        }
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
            if (pr.desc != null)
            {
                if (pr.desc.activeSelf != pr.open) pr.desc.SetActive(pr.open);
                if (pr.open)
                {
                    pr.descT.text = bm.PartDesc(i);
                    // owen, 2026-08-08: "the first time I expand an item the
                    // description bar looks too tall; after I close it and expand
                    // it again the bar size looks normal."
                    //
                    // Text.preferredHeight measures the WRAP at the rect's CURRENT
                    // width. A row that has never been open has never been through
                    // a layout pass, so it is still at Unity's default 100x100 and
                    // its label is 74 units wide: the 87-character beam blurb
                    // measured 134.7 there instead of 14.6, and the row latched
                    // 142.7 instead of 34.0. Nothing re-measured it, because this
                    // refresh only runs on a toggle - so the wrong height survived
                    // until the row was closed and opened again at the real width.
                    //
                    // Borrow the width from the HEADER row: it is a sibling in the
                    // same childForceExpandWidth group, so it always already
                    // carries the width this row is about to be given. Measured:
                    // 74 -> 1044.8 wide, preferredHeight 134.7 -> 14.6, row 34.0.
                    var drt = (RectTransform)pr.desc.transform;
                    var hrt = (RectTransform)pr.go.transform;
                    if (hrt.rect.width > 1f && Mathf.Abs(drt.rect.width - hrt.rect.width) > 1f)
                        drt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, hrt.rect.width);
                    // Height from the wrapped text, so a long description gets
                    // the room it needs instead of being silently truncated -
                    // the failure mode C19 caught on the palette tiles.
                    float wantD = Mathf.Max(34f, pr.descT.preferredHeight + 8f);
                    pr.descLe.minHeight = wantD; pr.descLe.preferredHeight = wantD;
                }
            }
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
            // HP and HP PER KG (owen, 2026-08-05). HP alone would just restate
            // the kg column - both are linear in volume - so the row carries
            // the ratio, which is the only figure that separates the materials
            // and the one a weight cap makes decisive.
            float kgm = pdef != null ? pdef.MassOf(mr.mat) : 0f;
            float hpm = bm.PartHP(mr.part, mr.mat);
            mr.lbl.text = string.Format(
                "{0}{1}   \u00b7   {2} scrap   \u00b7   {3} kg   \u00b7   {4} HP ({5:F1}/kg)   \u00b7   own {6}",
                MatDB.Get(mr.mat).name, fixedMat ? " (fixed)" : "", price,
                Mathf.RoundToInt(kgm), Mathf.RoundToInt(hpm),
                hpm / Mathf.Max(1f, kgm), ownm);
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
            // dead, and says why (own none) on hover
            // or tap. Fixed columns either way, which was the original point.
            bool canSell = ownm > 0;
            mr.sellT.text = canSell ? "SELL " + CareerDB.SellPrice(id, mr.mat) : "SELL";
            mr.sellT.color = canSell ? GATE_TXT_LIVE : GATE_TXT_DEAD;
            mr.sell.GetComponent<Image>().color = canSell ? GATE_LIVE : GATE_DEAD;
        }
    }

    // ---- an input box you can SEE before you touch it --------------------
    //
    // ⚠ THE FILL ALONE DOES NOT DO IT, and the arithmetic is the reason.
    // The ARENA's fields were `MkPanel("box", …, 0.06,0.07,0.09)` sitting on
    // the dock, whose own background is `0.06,0.07,0.09` under a 15%-black
    // viewport — so the box and the space around it were the SAME COLOUR:
    //
    //     field  (0.06,0.07,0.09)              relative luminance 0.00593
    //     behind (0.051,0.0595,0.0765)         relative luminance 0.00480
    //     ratio  (0.00593+0.05)/(0.00480+0.05)                  = 1.02:1
    //
    // which is exactly what the UX review measured. The floor for the boundary
    // of an interactive control is 3:1 (WCAG 1.4.11 non-text contrast).
    //
    // Lifting the fill to MkInput's `0.13,0.14,0.18` — the obvious fix, and the
    // one first prescribed — gets 0.01766, i.e. **1.23:1**. Still nowhere near.
    // It cannot get there: any fill dark enough to read white text on is too
    // dark to separate from a dark dock. So the boundary is carried by a
    // BORDER, which is the thing the 3:1 rule is actually about:
    //
    //     border (0.42,0.44,0.50)              relative luminance 0.16311
    //     vs the dock behind it                                 = 3.89:1  ✓
    //     vs the fill inside it                                 = 3.15:1  ✓
    //     vs the SHOP's green deposit row (0.098,0.155,0.127)   = 3.13:1  ✓
    //
    // Both adjacent colours clear the floor at both sites, which is what the
    // rule asks and what a single lifted fill could not have given. The fill
    // still moves to 0.13,0.14,0.18 so these boxes look like every other input
    // in the dock — that part of the prescription was right, it was just not
    // sufficient on its own.
    //
    // Implemented as two panels rather than an outline sprite because this dock
    // builds everything from code and owns no sprite: the outer panel IS the
    // border and the inner one, inset by 2, is the fill.
    static readonly Color FIELD_FILL   = new Color(0.13f, 0.14f, 0.18f, 1f);
    static readonly Color FIELD_BORDER = new Color(0.42f, 0.44f, 0.50f, 1f);
    const float FIELD_BORDER_PX = 2f;

    /// <summary>The box half of a text field. Returns the OUTER panel — that is
    /// the one to hang the InputField and the LayoutElement on — and hands back
    /// the inner fill through <paramref name="fill"/>, which is what the text
    /// and placeholder must be parented to (UGUI puts the caret under the text
    /// component's parent, so it has to be inside the border).</summary>
    GameObject MkFieldBox(string name, Transform parent, out GameObject fill)
    {
        var border = MkPanel(name, parent, FIELD_BORDER);
        fill = MkPanel("fill", border.transform, FIELD_FILL);
        var frt = fill.GetComponent<RectTransform>();
        Stretch(frt);
        frt.offsetMin = new Vector2(FIELD_BORDER_PX, FIELD_BORDER_PX);
        frt.offsetMax = new Vector2(-FIELD_BORDER_PX, -FIELD_BORDER_PX);
        return border;
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
        var rle0 = row.AddComponent<LayoutElement>(); rle0.minHeight = TouchRow(); rle0.preferredHeight = TouchRow();
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
            var dle = drow.AddComponent<LayoutElement>(); dle.minHeight = TouchRow(); dle.preferredHeight = TouchRow();
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
            var rle = row.AddComponent<LayoutElement>(); rle.minHeight = TouchRow(); rle.preferredHeight = TouchRow();
            var rh = row.AddComponent<HorizontalLayoutGroup>(); rh.spacing = 4f; rh.childForceExpandHeight = true; rh.childForceExpandWidth = false; rh.padding = new RectOffset(6,4,2,2);
            // MEDALS (2026-08-02): titles are real now - one per league
            // campaign swept - so the card names the championship instead of
            // printing a bare star nothing explained. Kept to the existing one
            // -line card; the full §2b redesign is still out of scope.
            string champ = "";
            foreach (var h in rob.leagueHistory)
                if (h.EndsWith(" champion")) champ += (champ.Length > 0 ? " \u00b7 " : "") + h.Substring(0, h.Length - 9);
            // READINESS (owen, 2026-08-05). A saved design no longer holds its
            // parts, so any card here may or may not be buildable out of the
            // box as it stands. Saying so on the card is the point: otherwise
            // "which robot can I field?" is answerable only by tapping EDIT on
            // each one and being refused later at LEAGUE - the same asymmetry
            // the 2026-08-02 FIGHT-button fix existed to remove.
            var lackM = Career.SnapshotShortfall(rob.snapshot);
            string ready = lackM.Count > 0
                         ? "\n\u26a0 needs " + Career.ShortfallText(lackM)
                         : "\n\u2713 ready to field";
            var lbl = MkText("lbl", row.transform, string.Format("{0}{1} \u00b7 {2}-{3}{4}{5}{6}",
                ri == Career.Data.activeRobot ? "\u25b8 " : "", rob.name, rob.wins, rob.losses,
                rob.titles > 0 ? "  \u00b7  \u2605\u00d7" + rob.titles : "",
                champ.Length > 0 ? "\n\u2605 " + champ + " champion" : "",
                ready), 14, TextAnchor.MiddleLeft);
            if (rob.titles > 0) lbl.color = new Color(1f, 0.87f, 0.46f);
            // Amber wins over champion gold: a title you cannot bolt together
            // is still a robot you cannot enter, and that is the actionable
            // fact on this row.
            if (lackM.Count > 0) lbl.color = new Color(1f, 0.82f, 0.25f);
            // Every card is at least two lines now, three with a championship.
            // C19 sweeps visible labels for fit, so the height has to follow
            // the line count rather than a constant that was right yesterday.
            float cardH = champ.Length > 0 ? 68f : 46f;
            rle.minHeight = Mathf.Max(TouchRow(), cardH);
            rle.preferredHeight = Mathf.Max(TouchRow(), cardH);
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            // R5 (critic finding 9 carve-out): LOAD, RENAME and RETIRE were the
            // same grey at the same size, so the irreversible action was styled
            // exactly like the primary one. LOAD reads primary; RETIRE reads
            // destructive and states its own confirm on the button face instead
            // of only in a message bar the player may not be looking at.
            // OWEN 2026-08-02: it already loaded the machine and made it active
            // - it just left you standing on ROBOTS to go find the tools
            // yourself. Loading a robot IS the intent to work on it.
            //
            // OWEN 2026-08-05: "replace EDIT with LOAD". EDIT named a MODE the
            // button does not put you in - there is no edit-vs-view state in
            // this game, only which design is currently bolted together. LOAD
            // names what actually happens, and it is the right word now that
            // designs no longer hold their parts: you are picking which one to
            // build out of the box, and the readiness badge on this same row
            // tells you whether you can.
            var edb = MkButton("stload_" + i2, row.transform, "LOAD", 13, () => { Feedback(bm.StableEdit(ri)); RefreshRobots(); RefreshPartLabels(); RefreshHighlight(); ShowTab(0); });
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

    // BuildPartsTab() and RefreshParts() lived here and are gone with the tab
    // (owen, 2026-08-05: "the PARTS tab looks redundant because we know how
    // many of each part are owned in the SHOP tab and BUILD tab").
    //
    // He is right, and it got more right this morning. The shelf's one unique
    // job was naming WHICH SAVED ROBOT was holding a part - and once designs
    // stopped holding parts, every row had exactly one claimant: the build you
    // have open. At that point it was printing "own N / N in this build /
    // N-M spare" beside a BUILD tile already reading "N free" and a SHOP row
    // already reading "own N". Three surfaces, one fact.


    /// <summary>Test/QA hook: drive the dock exactly as a tap would. The
    /// visual critic loop needs to walk every tab and photograph it; without
    /// this it would have to synthesise pointer events against a moving
    /// layout, which is how UI harnesses end up testing their own maths
    /// instead of the screen.</summary>
    public void TestShowTab(int i) { ShowTab(i); }
    public int TestTab { get { return tab; } }
    /// <summary>The ladder model behind the ARENA tab, or null before the tab
    /// has ever been opened. A seam, not a shortcut: whether re-entering the
    /// tab REFETCHES is a property of this dock's ShowTab and is only visible
    /// as ArenaScreen.TestRefreshes moving. Without it a harness can read the
    /// board's contents and learn nothing — a stale board and a freshly
    /// reloaded identical board look exactly the same.</summary>
    public ArenaScreen TestArenaScreen { get { return arenaScreen; } }
    /// <summary>PARTS (0) or COSMETICS (1) within the one SHOP tab. A seam,
    /// not a shortcut: the cosmetics shelf is a network read behind a section
    /// button, and a harness that cannot reach it is a harness that will keep
    /// reporting the parts shelf as if it were the whole shop.</summary>
    public void TestShowShopSection(int s) { ShowShopSection(s); }
    public int TestShopSection { get { return shopSection; } }
    /// <summary>R1 fix 2 evidence seam. The dock height drives BOTH the panel
    /// layout and the pointer-blocking boundary; if they ever disagree, taps
    /// inside the expanded panel place parts on the robot behind it. These
    /// two read-only peeks let a harness prove they agree per tab instead of
    /// taking it on trust.</summary>
    public float TestDockHeight { get { return dockRt != null ? dockRt.sizeDelta.y : -1f; } }
    public bool TestOverUI(Vector2 screenPoint) { return OverUI(screenPoint); }
    public float TestCanvasScale { get { return canvas != null ? canvas.scaleFactor : -1f; } }

    /// <summary>P3a: public as a harness seam (the TouchSmoke Test* precedent)
    /// — tab switching was only reachable through private button closures.</summary>
    public void ShowTab(int i)
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
        if (programPanel != null) programPanel.SetActive(open && i == 5 && Career.active);   // P3a

        // ---- ARENA, index 4 ------------------------------------------------
        // ArenaScreen is IMGUI, so there is no panel to SetActive; the
        // component itself is the switch, and disabling it stops its OnGUI.
        // Built on first open, never before — Start() hits the ladder API.
        bool wantArena = open && i == 4 && Career.active;

        // ⚠ A REPLAY MUST SURVIVE THE DOCK CLOSING AND MUST NOT SURVIVE A TAB
        // CHANGE, AND THE DISCRIMINATOR IS `i`, NOT `open`.
        //
        // WATCH closes the dock ON PURPOSE so the fight is not played behind an
        // opaque panel, and `wantArena` includes `open` — so the obvious hook,
        // tearing down when arenaScreen is disabled (an OnDisable, or anything
        // keyed on `wantArena`), would kill every replay the instant it started.
        // It would present as a flaky replay rather than as a wrong hook, which
        // is the expensive kind of wrong.
        //
        // Leaving the ARENA tab is the real abandonment: ReplayPlayer.Play is
        // called with attachCamera:true, so the camera is on the arena for the
        // length of the bout. Without this, tabbing to BUILD mid-replay leaves
        // the player in the BUILD tab looking at a fight instead of their robot
        // until the recording runs out — and then a BackToBuild() lands while
        // they are mid-build.
        //
        // StopReplay() only, deliberately: it is null-guarded and safe to call
        // on every tab change, and it is the single owner of Close(). The dock
        // is NOT restored from here — we are already inside ShowTab, and
        // RestoreDock() reopens the dock by calling SetDockOpen(true), which
        // re-enters this method. The path that gets here has opened the dock
        // itself anyway, because a tab BUTTON is what opens it.
        if (arenaScreen != null && i != 4) arenaScreen.StopReplay();

        bool arenaJustBuilt = false;
        if (wantArena && arenaScreen == null)
        {
            var ago = new GameObject("arena_screen");
            ago.transform.SetParent(transform, false);
            arenaScreen = ago.AddComponent<ArenaScreen>();
            arenaJustBuilt = true;
        }
        if (arenaScreen != null)
        {
            arenaScreen.enabled = wantArena;
            // ALL FIVE SURFACES ARE PORTED, so inside the dock the IMGUI never
            // draws: two renderers of one screen is a doubled screen, not a
            // fallback. ArenaScreen keeps its OnGUI for the standalone
            // ArenaScreen.Open() path, which is how this screen is looked at
            // without the mobile dock — the model and the flows are shared, so
            // that path cannot drift from this one.
            arenaScreen.SuppressImgui = wantArena;
        }
        if (arenaPanel != null) arenaPanel.SetActive(wantArena);
        if (wantArena)
        {
            arenaBoardStamp = -1; RefreshArena();      // repaint what we hold…
            // …and ASK THE SERVER, which is a different verb. See arenaFetchAt.
            float now = Time.realtimeSinceStartup;
            if (arenaJustBuilt)
            {
                // ArenaScreen.Start() is already fetching. Claiming the window
                // rather than firing a second identical GET into it.
                arenaFetchAt = now;
            }
            else if (arenaScreen != null && now - arenaFetchAt >= ARENA_REFETCH_S)
            {
                arenaFetchAt = now;
                arenaScreen.RefreshNow();
            }
        }

        if (i == 3) { shopNote = ""; shopNoteBad = false; RefreshShop(); }
        if (robots) RefreshRobots();
        if (i == 5 && programCanvas != null) programCanvas.Refresh();   // P3a
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
        float top = BarH
                  + ((msgBar != null && msgBar.activeSelf) ? MsgH : 0f)
                  + ((tipBar != null && tipBar.activeSelf) ? TipH : 0f);
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
        // The ARENA board arrives from the network, so it lands mid-frame with
        // nobody to tell. Polled rather than pushed: RefreshArena is a no-op
        // unless the list actually changed, and a callback into a UI that may
        // have been torn down by a tab switch is the more expensive mistake.
        if (tab == 4 && dockOpen && arenaPanel != null && arenaPanel.activeSelf) RefreshArena();
        // Watch the canvas SIZE as well as the scale factor. Switching the
        // simulated device changes both, but a rotation changes only the size,
        // and the palette's column width is derived from the viewport width -
        // so keying off scaleFactor alone would leave the tiles fitted to the
        // previous screen.
        if (canvas != null)
        {
            var crt0 = canvas.GetComponent<RectTransform>();
            float sig = canvas.scaleFactor * 1000f
                      + (crt0 != null ? crt0.rect.width + crt0.rect.height * 7f : 0f);
            if (Mathf.Abs(sig - lastLayoutSig) > 0.5f)
            {
                lastLayoutSig = sig;
                lastScaleFactor = canvas.scaleFactor;
                ApplyTouchSizes();
            }
            // …and refit the palette EVERY frame, not just on a signature
            // change. ApplyTouchSizes runs once when the UI is built, and at
            // that moment the dock's new height has not propagated down to the
            // palette's rect yet — so a one-shot fit measures a viewport that
            // does not exist and leaves the cells at their unfitted height.
            // That is exactly what the first two attempts at this fix did:
            // correct arithmetic, run at a moment when the inputs were not
            // there, and the bench reported the identical 80.6 units clipped
            // both times.
            //
            // Cheap enough to do unconditionally: a handful of float ops, and
            // it assigns only when the value actually moved.
            FitPaletteRows(TouchRow());
        }
        bool fighting = FightManager.current != null
                     || bm.mode == BuilderManager.Mode.Test   // fix: dock stayed up over TEST DRIVE
                     || bm.Scouting;                          // C3: scouting overlay owns the screen
        // Scouting keeps the canvas UP now: the card lives on it, and turning
        // the canvas off is what forced the IMGUI version that cannot be
        // tapped. The dock still stands down - the card owns the screen.
        if (canvas != null) canvas.enabled = !fighting || bm.Scouting;
        PumpScoutCard();
        if (bm.Scouting)
        {
            if (dockRt != null) dockRt.gameObject.SetActive(false);
            if (dockHandle != null) dockHandle.gameObject.SetActive(false);
            return;
        }
        if (dockRt != null && !dockRt.gameObject.activeSelf)
        {
            dockRt.gameObject.SetActive(true);
            if (dockHandle != null) dockHandle.gameObject.SetActive(true);
        }
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
            // \u26a0 4 and 5 were BOTH WRONG until 2026-08-10. The PARTS removal on
            // 08-05 renumbered the names array, LayoutTabs and ShowTab and
            // missed this switch, so case 4 answered with the deleted PARTS
            // tab's hint and case 5 with the trophy case's \u2014 every tab from
            // here down described its predecessor. Nothing failed, because a
            // wrong sentence is not an exception.
            case 4:  return "the ladder \u00b7 ENLIST your saved robot, then scout and challenge";
            case 5:  return "your program bench \u00b7 the robot drives itself with this";
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

    /// <summary>The message bar's LIVE height. It grows to fit a long notice.
    ///
    /// R11 (owen 2026-08-04, found by the legibility sweep, not by me):
    /// msgtext wraps and truncates vertically inside a fixed 38-unit bar, so
    /// any notice long enough to reach a second line lost that line. Notices
    /// are how this game explains a refusal - "Needs at least 1 wheel", the
    /// weight-cap messages - so the half that got clipped was routinely the
    /// half that said what to do about it.
    ///
    /// Everything that stacks under the top bars reads this rather than the
    /// MSG_H constant, or the dock and the camera would keep budgeting for a
    /// bar that is no longer 38 tall.</summary>
    /// <summary>The stats bar's LIVE height (V2.2 C19 repair): the bar now
    /// budgets two FontUnits lines, so everything stacking under it must read
    /// this rather than the 46-unit constant — the MsgH pattern exactly.</summary>
    float BarH { get { return statsRt != null ? statsRt.sizeDelta.y : BAR_H; } }
    float MsgH { get { return msgBarRt != null ? msgBarRt.sizeDelta.y : MSG_H; } }
    float TipH { get { return tipBarRt != null ? tipBarRt.sizeDelta.y : TIP_H; } }
    RectTransform msgBarRt, tipBarRt;

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
        FitTipBar();
        float y = -(BarH + ((msgBar != null && msgBar.activeSelf) ? MsgH : 0f));
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
    class FightGate { public Button btn; public Image img; public Button abtn; public Image aimg; public Text lbl; public string baseLabel; public int li, ci; }
    readonly List<FightGate> fightGates = new List<FightGate>();
    float fightGateAt = -99f;
    static readonly Color FIGHT_OK   = new Color(0.40f, 0.22f, 0.09f, 1f);
    static readonly Color FIGHT_DEAD = new Color(0.19f, 0.19f, 0.21f, 1f);
    /// <summary>P4: the AUTONOMY FIGHT button's live color — violet, so the
    /// two ways to fight read as different commitments at a glance.</summary>
    static readonly Color AUTO_OK    = new Color(0.30f, 0.20f, 0.44f, 1f);

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
            // P4: the autonomy button carries its OWN gate on top of the
            // manual one. One AutonomyBlocker call covers all rows (it reads
            // the active robot + bay, not the contest), so hoisting it out of
            // the loop would be nicer \u2014 but the 0.25 s pump timer already
            // bounds the cost and per-row keeps the code shaped like the
            // manual gate beside it.
            // V2.8 (critic loop 7, F2): the autonomy gate now speaks on its
            // OWN terms and is never overwritten by the manual one. The line
            // that used to sit here was `if (blocked) aTag = tag;` — so any
            // bay that also failed the manual gate swallowed the autonomy
            // reason whole. On owen's save that hid "needs a saved program"
            // behind "Needs at least 1 wheel" on all nine rows, and no bench
            // could see it: AutonomyBench asks the blocker directly, and a
            // test that goes through the API cannot notice that nothing on
            // screen says the API exists (critic loop 6's rule).
            string aTag;
            bool aOwn = bm.AutonomyBlocker(out aTag) != null;   // autonomy's OWN refusal
            bool aBlocked = aOwn || blocked;
            string want = g.baseLabel;
            if (blocked) want += "   \u2014   " + tag;
            // V2.8b (critic loop 7 R2, finding 6): ELSE, not a second clause.
            // AutonomyBlocker reads the bay, not the contest, so its sentence
            // is identical on all nine rows; appending it alongside the manual
            // one pushed every label onto two lines in a one-line box and
            // clipped both. The autonomy reason now shows exactly when it is
            // the ONLY thing in the way - which is when it is actionable.
            else if (aOwn) want += "   \u2014   auto: " + aTag;
            if (g.lbl.text != want) g.lbl.text = want;
            // amber for EITHER refusal - a row only autonomy refuses used to
            // print its refusal in ready-white.
            g.lbl.color = (blocked || aOwn) ? new Color(1f, 0.72f, 0.36f) : Color.white;
            if (g.img != null) g.img.color = blocked ? FIGHT_DEAD : FIGHT_OK;
            if (g.aimg != null) g.aimg.color = aBlocked ? FIGHT_DEAD : AUTO_OK;
            var face = g.btn.GetComponentInChildren<Text>();
            // Deliberately still clickable. A dead button that swallows the tap
            // is the same complaint again; tapping a greyed one still pumps the
            // full sentence into the message bar for anyone who wants it.
            if (face != null) face.color = blocked ? new Color(0.52f, 0.52f, 0.56f) : Color.white;
            var aface = g.abtn != null ? g.abtn.GetComponentInChildren<Text>() : null;
            if (aface != null) aface.color = aBlocked ? new Color(0.52f, 0.52f, 0.56f) : Color.white;
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
            FitMsgBar();
            return;
        }
        if (!string.IsNullOrEmpty(msg) && msg != msgSeen) { msgSeen = msg; msgAt = Time.unscaledTime; }
        bool live = !string.IsNullOrEmpty(msgSeen) && Time.unscaledTime - msgAt < MSG_LIFE;
        if (live) { msgText.color = new Color(1f, 0.82f, 0.25f); msgText.text = msgSeen; }
        if (msgBar.activeSelf != live) { msgBar.SetActive(live); ApplyDockH(); }
        // The bar is sized to the NOTICE, so it has to be re-fitted whenever
        // the notice changes - not only when the layout changes. The longest
        // messages in this game are the refusals, which are exactly the ones
        // worth reading in full.
        if (msgBar.activeSelf) FitMsgBar();
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
