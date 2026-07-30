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
    Text fightInfo; bool wasFighting;   // critic round 2: FIGHT tab context
    GameObject buildPanel, fightPanel, garagePanel;
    readonly List<Button> partButtons = new List<Button>();
    readonly List<Button> matButtons = new List<Button>();
    readonly List<string> matKeys = new List<string>();
    int tab;
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
    public string StatsLine { get { return statsText != null ? statsText.text : ""; } }

    public static bool ShouldActivate()
    {
        if (forceMobileUI) return true;
        if (Application.isMobilePlatform) return true;
#if ENABLE_INPUT_SYSTEM
        return Touchscreen.current != null;
#else
        return Input.touchSupported;
#endif
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
        BuildCanvas();
        ShowTab(0);
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

        // top stats bar
        var bar = MkPanel("stats", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 0.92f));
        var brt = bar.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f); brt.sizeDelta = new Vector2(0f, 46f); brt.anchoredPosition = Vector2.zero;
        statsText = MkText("statsText", bar.transform, "", 20, TextAnchor.MiddleCenter);
        Stretch(statsText.rectTransform);

        // bottom dock
        var dock = MkPanel("dock", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 0.96f));
        var drt = dock.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0f, 0f); drt.anchorMax = new Vector2(1f, 0f);
        drt.pivot = new Vector2(0.5f, 0f); drt.sizeDelta = new Vector2(0f, 210f); drt.anchoredPosition = Vector2.zero;

        // tab buttons across the top of the dock
        string[] names = { "BUILD", "FIGHT", "GARAGE" };
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var tb = MkButton("tab" + i, dock.transform, names[i], 20, () => ShowTab(idx));
            var rt = tb.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(i / 3f, 1f); rt.anchorMax = new Vector2((i + 1) / 3f, 1f);
            rt.pivot = new Vector2(0.5f, 1f); rt.sizeDelta = new Vector2(-4f, 44f); rt.anchoredPosition = new Vector2(0f, 0f);
        }

        // content panels fill the rest of the dock
        buildPanel = MkPanel("build", dock.transform, new Color(0f, 0f, 0f, 0f));
        fightPanel = MkPanel("fight", dock.transform, new Color(0f, 0f, 0f, 0f));
        garagePanel = MkPanel("garage", dock.transform, new Color(0f, 0f, 0f, 0f));
        foreach (var p in new[] { buildPanel, fightPanel, garagePanel })
        {
            var rt = p.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6f, 6f); rt.offsetMax = new Vector2(-6f, -48f);
        }
        BuildBuildTab();
        BuildFightTab();
        BuildGarageTab();
    }

    void BuildBuildTab()
    {
        // material chip row (top)
        var matRow = MkPanel("mats", buildPanel.transform, new Color(0f,0f,0f,0f));
        var mr = matRow.GetComponent<RectTransform>();
        mr.anchorMin = new Vector2(0f,1f); mr.anchorMax = new Vector2(1f,1f); mr.pivot = new Vector2(0.5f,1f);
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
        // parts scroll (middle)
        var scrollGO = MkPanel("partscroll", buildPanel.transform, new Color(0f,0f,0f,0.0f));
        var sr = scrollGO.GetComponent<RectTransform>();
        sr.anchorMin = new Vector2(0f,0f); sr.anchorMax = new Vector2(1f,1f);
        sr.offsetMin = new Vector2(0f,46f); sr.offsetMax = new Vector2(0f,-44f);
        var scroll = scrollGO.AddComponent<ScrollRect>(); scroll.horizontal = true; scroll.vertical = false;
        var viewport = MkPanel("viewport", scrollGO.transform, new Color(0f,0f,0f,0.15f));
        var vp = viewport.GetComponent<RectTransform>(); Stretch(vp);
        viewport.AddComponent<Mask>().showMaskGraphic = true;
        var content = MkPanel("content", viewport.transform, new Color(0f,0f,0f,0f));
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f,0f); crt.anchorMax = new Vector2(0f,1f); crt.pivot = new Vector2(0f,0.5f); crt.anchoredPosition = Vector2.zero;
        var clg = content.AddComponent<HorizontalLayoutGroup>(); clg.spacing = 6f; clg.childForceExpandHeight = true; clg.childForceExpandWidth = false; clg.padding = new RectOffset(4,4,4,4);
        var csf = content.AddComponent<ContentSizeFitter>(); csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vp; scroll.content = crt;
        partButtons.Clear();
        int n = bm != null ? bm.PaletteCount : 0;
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            string lab = bm.PartLabel(i) + "\n" + bm.PartMass(i) + " kg";
            var pb = MkButton("part_"+i, content.transform, lab, 15, () => { bm.SelectPart(idx); RefreshHighlight(); });
            var le = pb.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 128f; le.minWidth = 128f;
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
            img.color = sel ? new Color(0.20f,0.45f,0.65f,1f) : new Color(0.16f,0.18f,0.22f,0.96f);
        }
    }

    void RefreshPartLabels()
    {
        if (bm == null) return;
        for (int i = 0; i < partButtons.Count && i < bm.PaletteCount; i++)
        {
            var t = partButtons[i].GetComponentInChildren<Text>();
            if (t != null) t.text = bm.PartLabel(i) + "\n" + bm.PartMass(i) + " kg";
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
        var v = fightPanel.AddComponent<VerticalLayoutGroup>(); v.spacing = 8f; v.childForceExpandWidth = true; v.childForceExpandHeight = true; v.padding = new RectOffset(4,4,4,4);
        // Critic round 2: a mobile player had no view of scrap, record or the
        // next ladder opponent. One live header line carries all three.
        fightInfo = MkText("fightinfo", fightPanel.transform, "", 16, TextAnchor.MiddleCenter);
        fightInfo.color = new Color(0.80f, 0.88f, 1f);
        MkButton("ladder", fightPanel.transform, "LADDER — next rung", 19, () => { if (bm != null) bm.StartLadderFight(); });
        MkButton("exhib", fightPanel.transform, "EXHIBITION FIGHT", 19, () => { if (bm != null) { Progression.activeRungIndex = -1; Progression.activeChallengeIdx = -1; bm.StartFight(); } });
        MkButton("test", fightPanel.transform, "TEST DRIVE", 19, () => { if (bm != null) bm.StartTest(); });
        RefreshFightInfo();
    }

    /// <summary>Scrap balance, win-loss record and the next ladder opponent,
    /// shown at the top of the FIGHT tab. Refreshed after every match.</summary>
    void RefreshFightInfo()
    {
        if (fightInfo == null) return;
        var d = Progression.Data;
        var r = Progression.CurrentRung();
        string next = r != null
            ? string.Format("NEXT RUNG: {0}   \u00b7   win +{1} scrap", r.label, r.reward)
            : "LADDER COMPLETE \u2014 title defense pays out again";
        fightInfo.text = string.Format("SCRAP {0}   \u00b7   RECORD {1}-{2}\n{3}",
            d.scrap, d.fightsWon, Mathf.Max(0, d.fightsFought - d.fightsWon), next);
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

    void ShowTab(int i)
    {
        tab = i;
        if (buildPanel != null) buildPanel.SetActive(i == 0);
        if (fightPanel != null) fightPanel.SetActive(i == 1);
        if (garagePanel != null) garagePanel.SetActive(i == 2);
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
        float sf = canvas != null ? canvas.scaleFactor : 1f;
        return p.y < 210f * sf || p.y > Screen.height - 46f * sf;
    }

    void Update()
    {
        if (bm == null) { bm = Object.FindFirstObjectByType<BuilderManager>(); if (bm == null) return; }
        bool fighting = Object.FindFirstObjectByType<FightManager>() != null
                     || bm.mode == BuilderManager.Mode.Test;   // fix: dock stayed up over TEST DRIVE
        if (canvas != null) canvas.enabled = !fighting;
        if (fighting) { BuilderManager.uiPointerBlocked = false; wasFighting = true; return; }
        if (wasFighting) { wasFighting = false; RefreshFightInfo(); }
        if (statsText != null)
        {
            string msg = bm.LastMessage;
            if (removeArmed)
            {
                statsText.color = new Color(1f, 0.52f, 0.42f);
                statsText.text = "REMOVE armed \u2014 tap a part on the robot to delete it (parts attached to it go too)";
            }
            else if (!string.IsNullOrEmpty(msg))
            {
                statsText.color = new Color(1f, 0.82f, 0.25f);
                statsText.text = msg;
            }
            else
            {
            statsText.color = Color.white;
            statsText.text = bm.HasSelection
                ? string.Format("HOLDING {0} — tap the robot to place  ·  {1} kg · {2} parts", bm.PartLabel(bm.SelectedPart), bm.BuildMassInt, bm.PlacedCount)
                : string.Format("{0} kg · {1} parts  ·  pick a part below, then tap the robot", bm.BuildMassInt, bm.PlacedCount);
            }
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

    void RefreshRemoveBtn()
    {
        if (removeBtn == null) return;
        removeBtn.GetComponent<Image>().color = removeArmed
            ? new Color(0.62f, 0.16f, 0.14f, 0.98f)
            : new Color(0.16f, 0.18f, 0.22f, 0.96f);
    }

    // ---- uGUI construction helpers ----
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
        var t = go.GetComponent<Text>(); t.font = Fnt(); t.text = s; t.fontSize = size; t.alignment = anchor; t.color = Color.white; t.raycastTarget = false; t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }
    Button MkButton(string name, Transform parent, string label, int size, System.Action onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.16f,0.18f,0.22f,0.96f);
        var t = MkText("t", go.transform, label, size, TextAnchor.MiddleCenter); Stretch(t.rectTransform);
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
        bool builder = Object.FindFirstObjectByType<BuilderManager>() != null;
        if (builder && MobileBuilderUI.inst == null && MobileBuilderUI.ShouldActivate())
            new GameObject("mobile_builder_ui").AddComponent<MobileBuilderUI>();
        else if (!builder && MobileBuilderUI.inst != null)
            Destroy(MobileBuilderUI.inst.gameObject);
    }
}

}
