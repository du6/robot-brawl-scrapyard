using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
public partial class MobileBuilderUI
{
    const string UI_SCALE_KEY = "scrapyard_ui_scale";
    float userUiScale = 1f;
    readonly Dictionary<Text, int> desktopTypeSizes = new Dictionary<Text, int>();
    RectTransform garageTools;
    Button garageFit, garageZoomOut, garageZoomIn, uiScaleButton;
    static int browserMetricsFrame = -1;
    static float browserPixelRatio = 1f;
    static bool browserCoarsePointer;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float ScrapyardUiPixelRatio();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern int ScrapyardUiCoarsePointer();
#endif

    static void ReadBrowserMetrics()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (browserMetricsFrame == Time.frameCount) return;
        browserMetricsFrame = Time.frameCount;
        browserPixelRatio = Mathf.Clamp(ScrapyardUiPixelRatio(), 0.25f, 4f);
        browserCoarsePointer = ScrapyardUiCoarsePointer() != 0;
#endif
    }

    static bool PhysicalTouchSizing
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false; // Browsers expose CSS pixels; reported DPI is not physical DPI.
#else
            return UnityEngine.Device.Application.isMobilePlatform
                || UnityEngine.Device.SystemInfo.deviceType == DeviceType.Handheld;
#endif
        }
    }

    // Public pure sizing seam: a framebuffer minimum is not a readable CSS
    // minimum on a Retina browser. Round UP so the rendered size keeps the floor.
    public static int DesktopFontUnits(float points, float canvasScale, float pixelRatio, float uiScale)
    {
        float pixels = Mathf.Max(14f, points * 1.1f) * Mathf.Clamp(uiScale, 1f, 1.3f)
                     * Mathf.Max(0.25f, pixelRatio);
        return Mathf.Max(1, Mathf.CeilToInt(pixels / Mathf.Max(0.01f, canvasScale)));
    }

    float DesktopRow()
    {
        ReadBrowserMetrics();
        return 52f * browserPixelRatio * userUiScale / Mathf.Max(0.01f, canvas != null ? canvas.scaleFactor : 1f);
    }

    void RegisterDesktopType(Text text, int nominalSize)
    {
        if (PhysicalTouchSizing) return;
        desktopTypeSizes[text] = nominalSize;
        text.fontSize = FontUnits(nominalSize);
    }

    void ApplyDesktopType()
    {
        if (PhysicalTouchSizing) return;
        var dead = new List<Text>();
        foreach (var entry in desktopTypeSizes)
        {
            if (entry.Key == null) { dead.Add(entry.Key); continue; }
            entry.Key.fontSize = FontUnits(entry.Value);
            entry.Key.resizeTextForBestFit = false;
        }
        foreach (var text in dead) desktopTypeSizes.Remove(text);
    }

    void BuildGarageTools()
    {
        userUiScale = Mathf.Clamp(PlayerPrefs.GetFloat(UI_SCALE_KEY, 1f), 1f, 1.3f);
        var go = MkPanel("garage_view_tools", canvas.transform, new Color(0.06f, 0.07f, 0.09f, 0.96f));
        garageTools = go.GetComponent<RectTransform>();
        garageTools.anchorMin = garageTools.anchorMax = new Vector2(1f, 0f);
        garageTools.pivot = new Vector2(1f, 0f);
        var group = go.AddComponent<HorizontalLayoutGroup>();
        group.padding = new RectOffset(4, 4, 2, 2);
        group.spacing = 4f;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = true;
        garageFit = MkButton("garage_fit", go.transform, "FIT", 14, () => { if (bm != null) bm.FitGarageView(); });
        garageZoomOut = MkButton("garage_zoom_out", go.transform, "-", 18, () => { if (bm != null) bm.ZoomGarageView(1.18f); });
        garageZoomIn = MkButton("garage_zoom_in", go.transform, "+", 18, () => { if (bm != null) bm.ZoomGarageView(1f / 1.18f); });
        uiScaleButton = MkButton("garage_ui_scale", go.transform, "", 14, () =>
        {
            userUiScale = userUiScale < 1.1f ? 1.15f : userUiScale < 1.25f ? 1.3f : 1f;
            PlayerPrefs.SetFloat(UI_SCALE_KEY, userUiScale);
            PlayerPrefs.Save();
            ApplyTouchSizes();
            RefreshShop();
            if (bm != null) bm.FitGarageView();
        });
        HoverHint(garageFit, () => "Frame the whole robot in the workspace.");
        HoverHint(garageZoomOut, () => "Zoom out (also: scroll or pinch).");
        HoverHint(garageZoomIn, () => "Zoom in (also: scroll or pinch).");
        HoverHint(uiScaleButton, () => "Interface size: 100%, 115%, or 130%.");
    }

    void LayoutGarageTools()
    {
        if (garageTools == null || canvas == null) return;
        bool build = tab == 0;
        garageFit.gameObject.SetActive(build);
        garageZoomOut.gameObject.SetActive(build);
        garageZoomIn.gameObject.SetActive(build);
        uiScaleButton.gameObject.SetActive(!PhysicalTouchSizing && !browserCoarsePointer);
        bool visible = bm != null && bm.mode == BuilderManager.Mode.Build && !bm.Scouting
                    && (build || uiScaleButton.gameObject.activeSelf);
        garageTools.gameObject.SetActive(visible);
        if (!visible) return;
        float row = TouchRow();
        float px = !PhysicalTouchSizing ? browserPixelRatio * userUiScale / Mathf.Max(0.01f, canvas.scaleFactor) : row / 44f;
        float width = (build ? 164f : 0f) + (uiScaleButton.gameObject.activeSelf ? 120f : 0f);
        garageTools.sizeDelta = new Vector2(width * px, row);
        garageTools.anchoredPosition = new Vector2(-(safeR + 8f), dockRt.sizeDelta.y);
        uiScaleButton.GetComponentInChildren<Text>().text = "UI " + Mathf.RoundToInt(userUiScale * 100f) + "%";
        SetFont(garageTools, 14f);
        if (handleRt != null)
        {
            // A short phone can still fit Fit / − / + beside the collapse handle.
            float safeWidth = Mathf.Max(100f, CanvasW - safeL - safeR);
            float available = safeWidth - garageTools.sizeDelta.x - 24f;
            float handleWidth = Mathf.Min(300f, Mathf.Max(120f, available));
            handleRt.sizeDelta = new Vector2(handleWidth, row);
            handleRt.anchoredPosition = new Vector2(-Mathf.Max(0f,
                garageTools.sizeDelta.x + handleWidth * 0.5f + 16f - safeWidth * 0.5f), dockRt.sizeDelta.y);
        }
    }

    bool GarageToolsHit(Vector2 point)
    {
        return garageTools != null && garageTools.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(garageTools, point, null);
    }

    void PumpGarageGuidance()
    {
        if (tipBar == null || tipText == null) return;
        string guidance = bm != null ? bm.GarageGuidance : "";
        bool saveNotice = Career.active && !string.IsNullOrEmpty(Career.SaveNotice);
        if (saveNotice) guidance = Career.SaveNotice + (string.IsNullOrEmpty(guidance) ? "" : "\n" + guidance);
        bool show = !string.IsNullOrEmpty(guidance);
        if (tipPrev != null) tipPrev.gameObject.SetActive(false);
        if (tipNext != null) tipNext.gameObject.SetActive(false);
        var skip = tipBar.transform.Find("tipskip");
        if (skip != null) skip.gameObject.SetActive(false);
        tipText.rectTransform.offsetMin = new Vector2(14f + safeL, 0f);
        tipText.rectTransform.offsetMax = new Vector2(-(14f + safeR), 0f);
        tipText.text = guidance;
        tipText.fontSize = FontUnits(14f);
        tipText.color = saveNotice ? new Color(1f, 0.84f, 0.44f) : new Color(0.70f, 0.91f, 1f);
        if (tipBar.activeSelf != show) { tipBar.SetActive(show); ApplyDockH(); }
        if (!show) return;
        if (saveNotice)
        {
            // A persistent save failure must remain readable on narrow screens;
            // it must neither expire with a toast nor displace action feedback.
            float height = Mathf.Max(TIP_H, tipText.preferredHeight + 12f);
            if (Mathf.Abs(tipBarRt.sizeDelta.y - height) > 0.5f)
            { tipBarRt.sizeDelta = new Vector2(0f, height); ApplyDockH(); }
        }
        else FitTipBar();
        tipBarRt.anchoredPosition = new Vector2(0f, -(BarH + ((msgBar != null && msgBar.activeSelf) ? MsgH : 0f)));
    }

    class ShopSuggestion
    {
        public int part, score;
        public string mat, reason;
        public bool owned;
    }
    Transform shopSuggestions;
    readonly List<Button> suggestionButtons = new List<Button>();
    readonly Dictionary<string, int> shopSeenInventory = new Dictionary<string, int>();
    readonly HashSet<string> newlyAcquiredParts = new HashSet<string>();
    bool seenFirstInventory;
    int observedInventorySeq = -1;
    CareerData observedCareer;

    void ObserveNewParts()
    {
        if (Career.Data == null || Career.Data.inventory == null) return;
        if (observedCareer != Career.Data)
        {
            observedCareer = Career.Data;
            observedInventorySeq = -1;
            seenFirstInventory = false;
            shopSeenInventory.Clear(); newlyAcquiredParts.Clear();
        }
        if (observedInventorySeq == Career.inventorySeq) return;
        observedInventorySeq = Career.inventorySeq;
        foreach (var item in Career.Data.inventory)
        {
            string key = item.partId + "|" + item.mat;
            int old;
            shopSeenInventory.TryGetValue(key, out old);
            if (seenFirstInventory && item.count > old) newlyAcquiredParts.Add(key);
            shopSeenInventory[key] = item.count;
        }
        seenFirstInventory = true;
    }

    void BuildShopSuggestions(Transform content)
    {
        var root = MkPanel("shop_suggestions", content, new Color(0f, 0f, 0f, 0f));
        shopSuggestions = root.transform;
        var layout = root.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        var heading = MkText("shop_suggestions_title", root.transform, "START HERE · ready to fit and useful upgrades", 15, TextAnchor.MiddleLeft);
        heading.gameObject.AddComponent<LayoutElement>().minHeight = 28f;
        for (int i = 0; i < 3; i++)
        {
            var button = MkButton("shop_suggestion_" + i, root.transform, "", 14, null);
            button.gameObject.AddComponent<LayoutElement>();
            button.GetComponentInChildren<Text>().horizontalOverflow = HorizontalWrapMode.Wrap;
            suggestionButtons.Add(button);
        }
        var catalog = MkText("shop_catalog_title", content, "ALL PARTS · tap to compare materials and prices", 15, TextAnchor.MiddleLeft);
        catalog.gameObject.AddComponent<LayoutElement>().minHeight = 30f;
    }

    void RefreshShopSuggestions()
    {
        if (shopSuggestions == null || bm == null || !Career.active) return;
        ObserveNewParts();
        var suggestions = new List<ShopSuggestion>();
        foreach (var part in shopParts)
        {
            int i = part.part;
            string id = bm.PartId(i);
            var def = CareerDB.Def(id);
            if (def == null) continue;
            int utility = id == "wedge" ? 80 : id == "gusset" ? 70 : id == "wheel" ? 60
                        : id == "spike" ? 50 : def.category == P1Category.Power ? 35
                        : def.category == P1Category.Weapon && !def.actuator ? 30 : 10;
            ShopSuggestion best = null;
            foreach (string mat in bm.PartLegalMats(i))
            {
                int free = bm.CareerRemainingMat(i, mat);
                int price = CareerDB.PartPrice(id, mat);
                bool owned = free > 0;
                if (!owned && price > Career.Data.scrap) continue;
                // Advanced sensors/actuators stay in the full catalog until the
                // player owns them. The first shelf should enable a simple change.
                if (!owned && (def.sensor || def.actuator)) continue;
                bool fresh = owned && newlyAcquiredParts.Contains(id + "|" + mat);
                int score = (fresh ? 4000 : owned ? 3000 : 1000) + utility * 10 - Mathf.Min(price, 500);
                var pick = new ShopSuggestion { part = i, mat = mat, owned = owned, score = score,
                    reason = fresh ? "NEW · ready to fit" : owned ? free + " spare · ready to fit" : price + " scrap" };
                if (best == null || pick.score > best.score) best = pick;
            }
            if (best != null) suggestions.Add(best);
        }
        suggestions.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.part.CompareTo(b.part));
        for (int k = 0; k < suggestionButtons.Count; k++)
        {
            var button = suggestionButtons[k];
            button.gameObject.SetActive(k < suggestions.Count);
            if (k >= suggestions.Count) continue;
            var pick = suggestions[k];
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = FontUnits(14f);
            label.text = (pick.owned ? "FIT " : "VIEW ") + bm.PartLabel(pick.part) + " · " + MatDB.Get(pick.mat).name
                       + " · " + pick.reason;
            button.GetComponent<Image>().color = pick.owned ? new Color(0.12f, 0.32f, 0.29f, 1f)
                                                          : new Color(0.22f, 0.25f, 0.32f, 1f);
            var le = button.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = TouchRow();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (pick.owned)
                {
                    if (bm.CareerRemainingMat(pick.part, pick.mat) <= 0)
                    { RefreshShop(); return; }
                    bm.ActiveMatKey = pick.mat;
                    if (bm.HasSelection && bm.SelectedPart == pick.part) bm.SelectPart(pick.part);
                    bm.SelectPart(pick.part);
                    moveArmed = removeArmed = false;
                    RefreshMoveBtn(); RefreshRemoveBtn();
                    tab = 0; SetDockOpen(!PanelAutoHidesOnPick());
                    bm.FitGarageView();
                    Feedback(bm.PartLabel(pick.part) + ": " + bm.PartDesc(pick.part));
                }
                else
                {
                    foreach (var pr in shopParts) pr.open = false;
                    ToggleShopPart(pick.part);
                    // Scroll the expanded part into view after its layout settles.
                    StartCoroutine(ShowSuggestedShopPart(pick.part));
                }
            });
        }
        shopSuggestions.gameObject.SetActive(suggestions.Count > 0);
        FitShopRows();
    }

    System.Collections.IEnumerator ShowSuggestedShopPart(int part)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        var row = shopParts.Find(p => p.part == part);
        if (row == null) yield break;
        var scroll = row.go.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) yield break;
        float overflow = scroll.content.rect.height - scroll.viewport.rect.height;
        if (overflow <= 0f) yield break;
        var rect = row.go.GetComponent<RectTransform>();
        float y = -rect.anchoredPosition.y - rect.rect.height * rect.pivot.y;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - y / overflow);
    }

    void FitShopRows()
    {
        if (shopHeader == null || shopPanel == null || !shopPanel.activeInHierarchy) return;
        shopHeader.fontSize = FontUnits(14f);
        var headLayout = shopHeader.GetComponent<LayoutElement>();
        headLayout.minHeight = headLayout.preferredHeight = Mathf.Max(TouchRow(), shopHeader.preferredHeight + 8f);
        foreach (var row in shopParts)
        {
            row.lbl.fontSize = FontUnits(14f);
            row.descT.fontSize = FontUnits(14f);
            var le = row.go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = Mathf.Max(TouchRow(), row.lbl.preferredHeight + 8f);
        }
        foreach (var row in shopMats)
        {
            if (!row.go.activeSelf) continue;
            row.lbl.fontSize = FontUnits(14f);
            row.buyT.fontSize = FontUnits(14f);
            var labelLayout = row.lbl.GetComponent<LayoutElement>();
            labelLayout.minWidth = 140f; labelLayout.preferredWidth = 540f; labelLayout.flexibleWidth = 1f;
            var buyLayout = row.buy.GetComponent<LayoutElement>();
            buyLayout.minWidth = buyLayout.preferredWidth = Mathf.Max(70f, row.buyT.preferredWidth + 24f);
            var le = row.go.GetComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = Mathf.Max(TouchRow(), row.lbl.preferredHeight + 8f);
        }
    }
}
}
