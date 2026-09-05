// ===========================================================================
// RookieGuide.cs — the guided first improvement, with the ghost hand.
// docs/Rookie_Warmup_Design_2026-09-03.md §4C, animation ambition per owen's
// 2026-09-03 decision: "invest in richer animation now".
//
// Runs ONCE per career, after the first fight settles, in the workshop. Five
// steps, each ONE tap, each completed by GAME STATE (never an OK button), the
// whole thing cancelled forever by SKIP TIPS. The ghost hand is procedural —
// a soft-circle sprite that GLIDES from the thing to tap toward where the tap
// lands, taps (ripple), and loops until the player acts. No assets.
//
// TARGET PART: the doc's table says wedge; shipped as plate-first WHEN the
// rescue crate has granted the steel ballast (the crate hint and the debrief
// button both coach "heavy plate LOW", so the guide must demonstrate the SAME
// move, not a different one) and wedge otherwise (first fight WON — nothing
// was granted, the wedge is the kit's spare weapon).
//
// WEB ONLY for now, like the rest of the warm-up (owen: web proves first).
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
    public class RookieGuide : MonoBehaviour
    {
        public static RookieGuide active;

        /// <summary>Called by MobileBuilderUI.Update once per frame; spawns the
        /// guide exactly when it should exist. Cheap no-op everywhere else.</summary>
        public static void Tick(MobileBuilderUI ui, BuilderManager bm)
        {
#if !UNITY_EDITOR   // device builds (iOS + web); never under the editor benches
            if (active != null || ui == null || bm == null) return;
            if (RewardBox.active != null) return;            // the box has the floor
            if (!Career.active || Career.Data == null) return;
            if (Career.Data.guideDone) return;
            if (FightManager.current != null) return;        // wait for the workshop
            // Spawns at BOOT now (fights == 0): the pre-fight phase walks a
            // brand-new player to their first fight (LEAGUE -> AUTONOMY), then
            // the same guide flows into the post-fight build lesson. owen,
            // 2026-09-03: a fresh player saw no animation because the guide
            // only started AFTER fight one.
            var go = new GameObject("RookieGuide");
            active = go.AddComponent<RookieGuide>();
            active.ui = ui; active.bm = bm;
#endif
        }

        /// <summary>SKIP TIPS also ends the guide, permanently — one skip
        /// gesture, everything scripted stops (design principle 6).</summary>
        public static void Cancel()
        {
            if (Career.active && Career.Data != null && !Career.Data.guideDone)
            { Career.Data.guideDone = true; if (Career.autosave) Career.Save(); }
            if (active != null) { Destroy(active.gameObject); active = null; }
        }

        MobileBuilderUI ui;
        BuilderManager bm;
        const int STEP_LEAGUE = -2;   // pre-fight: point at the LEAGUE tab
        const int STEP_AUTO   = -1;   // pre-fight: point at AUTONOMY FIGHT
        int step;                    // <1 pre-fight, 1..5 post-fight build lesson
        int startParts;
        float lastSayAt = -99f;
        float step5At = -1f;
        int lastTab = -1;
        string targetPartId;
        int targetTile = -1;

        // ---- the hand ----
        Canvas canvas;
        RectTransform hand;
        Image handImg;
        float animT;
        const float GLIDE = 0.9f, HOLD = 0.45f;
        static Sprite circle;

        void Start()
        {
            // The lesson part is chosen when the FIGHT is behind us, not here:
            // since the guide spawns at boot (pre-fight phase), choosing in
            // Start() froze "wedge" before the crate could exist - a draw then
            // coached the wedge while the crate's steel plate sat on the shelf
            // (seen 2026-09-04). PickLesson() runs at the crossing instead.
            if (Career.Data.fights >= 1 && !PickLesson()) { Cancel(); return; }

            var cgo = new GameObject("RookieGuideCanvas", typeof(Canvas));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;

            var hgo = new GameObject("hand", typeof(Image));
            hgo.transform.SetParent(cgo.transform, false);
            handImg = hgo.GetComponent<Image>();
            handImg.sprite = Circle();
            handImg.raycastTarget = false;      // the hand must never EAT the tap it teaches
            hand = hgo.GetComponent<RectTransform>();
            hand.sizeDelta = new Vector2(46f, 46f);
            // A refresh after the fight lands HERE, not at the crossing below
            // - so the already-on check has to decide the first step too
            // (owen's phone, 2026-09-05: the fix only covered the crossing).
            step = Career.Data.fights < 1 ? STEP_LEAGUE : (alreadyOn ? 5 : 1);
            Say();
        }

        void OnDestroy() { if (active == this) active = null; }

        /// <summary>The crate's ballast decides the lesson: a loss/draw coaches
        /// the STEEL PLATE it just handed over, a win coaches the WEDGE the
        /// first-bout box handed over. Returns false if the tile is missing.
        /// The steel chip is set to match, or the coached tile would dispense
        /// an ALUMINUM plate while the words said "steel" (QA 2026-09-03).</summary>
        bool PickLesson()
        {
            targetPartId = Career.Data.rescueGranted ? "plate" : "wedge";
            targetTile = -1;
            for (int i = 0; i < bm.PaletteCount; i++)
                if (bm.PartId(i) == targetPartId) { targetTile = i; break; }
            if (targetTile < 0) return false;
            startParts = bm.PlacedCount;
            if (targetPartId == "plate") bm.ActiveMatKey = "Steel";
            // ALREADY DONE (owen's phone, 2026-09-05): the guide said "tap the
            // glowing WEDGE" over a tile reading "0 free" while a wedge sat
            // bolted to the nose. The mounting lesson is moot once the part is
            // on the machine - go straight to the last step and say why.
            alreadyOn = false;
            foreach (var pp in bm.placed) if (pp.def != null && pp.def.id == targetPartId) { alreadyOn = true; break; }
            if (alreadyOn) return true;
            // Nothing free in the chip's material but stock in another: switch
            // the chip, or the coached tile dispenses nothing (the "0 free ·
            // 1 in Steel" case).
            if (bm.CareerRemaining(targetTile) < 1)
                foreach (var mat in new[] { "Aluminum", "Steel", "ABS", "Titanium", "Tungsten", "CarbonFiber", "Rubber" })
                    if (bm.CareerRemainingMat(targetTile, mat) >= 1) { bm.ActiveMatKey = mat; break; }
            return true;
        }
        bool alreadyOn;

        void Update()
        {
            if (bm == null || ui == null || Career.Data == null) { Destroy(gameObject); return; }
            if (Career.Data.guideDone) { Destroy(gameObject); return; }
            if (FightManager.current != null) { Hide(); return; }   // any fight: sleep
            if (RewardBox.active != null) { Hide(); return; }       // a reward box has the floor

            // ---- PRE-FIGHT: lead them to their first fight ----------------
            if (Career.Data.fights < 1)
            {
                int want = ui.CurrentTab == 1 ? STEP_AUTO : STEP_LEAGUE;
                if (step != want) { step = want; Say(); lastSayAt = Time.unscaledTime; }
                Animate();
                if (Time.unscaledTime - lastSayAt > 5f) { Say(); lastSayAt = Time.unscaledTime; }
                return;
            }
            // crossing into the build lesson: the fight is done, so the crate
            // (or not) is known and the lesson part can be chosen truthfully
            if (step < 1)
            {
                if (!PickLesson()) { Cancel(); return; }
                step = alreadyOn ? 5 : 1; Say(); lastSayAt = Time.unscaledTime;
            }

            // ---- POST-FIGHT: advance on state, tolerating out-of-order play ----
            switch (step)
            {
                case 1:
                    if (bm.SelectedPart == targetTile) { step = 2; Say(); }
                    if (bm.PlacedCount > startParts) { step = 4; Say(); }   // they raced ahead
                    break;
                case 2:
                    if (bm.GhostLive) { step = 3; Say(); }
                    if (bm.PlacedCount > startParts) { step = 4; Say(); }
                    if (bm.SelectedPart != targetTile && bm.SelectedPart >= 0) { }  // any part is fine — the gesture is the lesson
                    if (bm.SelectedPart < 0) { step = 1; Say(); }
                    break;
                case 3:
                    if (bm.PlacedCount > startParts) { step = 4; Say(); }
                    if (bm.SelectedPart < 0 && !bm.GhostLive) { step = 1; Say(); }
                    break;
                case 4:
                    if (!bm.ActiveEditDirty()) { step = 5; Say(); }
                    break;
                case 5:
                    if (Career.Data.fights >= 2 || FightManager.current != null)
                    {
                        Career.Data.guideDone = true;
                        if (Career.autosave) Career.Save();
                        RBTelemetry.Once("guide", "&s=done");
                        Destroy(gameObject);
                        return;
                    }
                    // Do not haunt: QA watched the hand sit on the LEAGUE tab
                    // for an entire post-guide session. If the player has not
                    // rematched within a minute of reaching the last step, the
                    // lesson is over - end quietly and permanently.
                    if (step5At < 0f) step5At = Time.unscaledTime;
                    if (Time.unscaledTime - step5At > 60f)
                    {
                        Career.Data.guideDone = true;
                        if (Career.autosave) Career.Save();
                        Destroy(gameObject);
                        return;
                    }
                    break;
            }
            // Speak IMMEDIATELY when the player arrives on the tab the lesson
            // lives on (regression pass: the step-1 line fired once on SHOP,
            // was rightly suppressed there, and then never re-issued - the win
            // path finished on hand-signals alone).
            if (ui.CurrentTab != lastTab)
            {
                lastTab = ui.CurrentTab;
                if (ui.CurrentTab == 0 || step == 5) { Say(); lastSayAt = Time.unscaledTime; }
            }
            // RE-COACH (warm-up QA: the instruction toast was overwritten by
            // the first error and never came back - the player finished on
            // hand-signals alone). Re-assert every few seconds, with a
            // dedicated line when the ghost is sitting red: the escape routes
            // exist but nothing advertised them.
            if (Time.unscaledTime - lastSayAt > 5f)
            {
                if ((step == 2 || step == 3) && bm.SelectedPart >= 0 && !bm.GhostLive)
                    bm.Coach("Red means it will not fit there - try another spot, or ROTATE turns the part");
                else
                    Say();
                ui.RepumpMessage();   // identical text must still re-light the bar
                lastSayAt = Time.unscaledTime;
            }
            Animate();
        }

        void Say()
        {
            // Pre-fight lines (step < 1) speak on their own tabs; post-fight
            // steps 1-4 coach BUILD-tab controls, so they stay quiet elsewhere
            // (the win path lands on SHOP where the wedge does not exist).
            if (step >= 1 && step < 5 && ui != null && ui.CurrentTab != 0) return;
            RBTelemetry.Once("guide", "&s=" + step);
            switch (step)
            {
                case STEP_LEAGUE: bm.Coach("Tap LEAGUE to fight your first contest"); break;
                case STEP_AUTO:   bm.Coach("Tap AUTONOMY FIGHT - your robot's program does the driving"); break;
                case 1: bm.Coach(targetPartId == "plate"
                            ? "Let's make it tougher - tap the glowing STEEL PLATE below"
                            : "Let's improve it - tap the glowing WEDGE below"); break;
                case 2: bm.Coach("Now tap the ROBOT where you want it - low is strong"); break;
                case 3: bm.Coach("Tap AGAIN to bolt it on - first tap aims, second commits"); break;
                case 4: bm.Coach("SAVE keeps the change on " + (bm.PlacedCount > startParts ? "your machine" : "it")); break;
                case 5: bm.Coach(ui != null && ui.CurrentTab == 1
                            ? "AUTONOMY FIGHT - your program does the driving"
                            : alreadyOn
                            ? "Your " + (targetPartId == "plate" ? "STEEL PLATE" : "WEDGE") + " is already on. Back to the LEAGUE - see what it is worth"
                            : "Back to the LEAGUE - see what the change is worth"); break;
            }
        }

        void Hide() { if (handImg != null) handImg.enabled = false; }

        // ---- the glide-tap-ripple loop -------------------------------------
        void Animate()
        {
            if (handImg == null) return;
            Vector2 from, to;
            if (!Targets(out from, out to)) { handImg.enabled = false; return; }
            handImg.enabled = true;

            animT += Time.unscaledDeltaTime;
            float cycle = GLIDE + HOLD;
            float t = animT % cycle;
            if (t < GLIDE)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / GLIDE);
                hand.position = Vector2.Lerp(from, to, k);
                handImg.color = new Color(1f, 1f, 1f, 0.75f);
                hand.localScale = Vector3.one;
            }
            else
            {
                // the TAP: squash + a ripple ring expanding out of it
                float k = (t - GLIDE) / HOLD;
                hand.position = to;
                hand.localScale = Vector3.one * Mathf.Lerp(0.72f, 1f, k);
                handImg.color = new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.75f, k));
                if (t - Time.unscaledDeltaTime < GLIDE) Ripple(to);
            }
        }

        /// <summary>Where the hand travels FROM and TO for the current step.
        /// Single-point steps glide in place (from == to) and just tap.</summary>
        bool Targets(out Vector2 from, out Vector2 to)
        {
            from = to = Vector2.zero;
            switch (step)
            {
                case STEP_LEAGUE: { var r = Find("tab1"); if (r == null) return false; from = to = r.position; return true; }
                case STEP_AUTO:   { var b = FindByLabel("AUTONOMY FIGHT"); if (b == null) return false; from = to = b.position; return true; }
                case 1: { var r = Find("part_" + targetTile); if (r == null) return false; from = to = r.position; return true; }
                case 2: { var r = Find("part_" + targetTile); var w = RobotOnScreen();
                          from = r != null ? (Vector2)r.position : w; to = w; return true; }
                case 3: { to = from = RobotOnScreen(); return true; }
                case 4: { var r = FindByLabel("SAVE"); if (r == null) return false; from = to = r.position; return true; }
                case 5:
                {
                    // On the league tab already? Then the lesson's last word is
                    // the button that starts the rematch, not the tab.
                    if (ui != null && ui.CurrentTab == 1)
                    {
                        var b = FindByLabel("AUTONOMY FIGHT");
                        if (b != null) { from = to = b.position; return true; }
                    }
                    var r = Find("tab1"); if (r == null) return false; from = to = r.position; return true;
                }
            }
            return false;
        }

        Vector2 RobotOnScreen()
        {
            var cam = Camera.main;
            if (cam == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.55f);
            // the build robot lives around the origin; aim slightly high-front
            return cam.WorldToScreenPoint(new Vector3(0f, 0.75f, 0.30f));
        }

        RectTransform Find(string name)
        {
            var go = GameObject.Find(name);
            if (go == null || !go.activeInHierarchy) return null;
            return go.GetComponent<RectTransform>();
        }

        RectTransform FindByLabel(string label)
        {
            // TOPMOST match on screen, deterministically - FindObjectsByType
            // order pointed the hand at TIPPER's AUTONOMY FIGHT instead of
            // SCOUT's (regression pass 2026-09-03). The coached rematch is the
            // first row, and the first row is the highest one.
            RectTransform best = null; float bestY = float.NegativeInfinity;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var t = b.GetComponentInChildren<Text>();
                if (t == null || t.text != label) continue;
                var rt = b.GetComponent<RectTransform>();
                if (rt != null && rt.position.y > bestY) { bestY = rt.position.y; best = rt; }
            }
            return best;
        }

        void Ripple(Vector2 at)
        {
            var rgo = new GameObject("ripple", typeof(Image));
            rgo.transform.SetParent(canvas.transform, false);
            var img = rgo.GetComponent<Image>();
            img.sprite = Circle(); img.raycastTarget = false;
            var rt = rgo.GetComponent<RectTransform>();
            rt.position = at; rt.sizeDelta = new Vector2(46f, 46f);
            rgo.AddComponent<RippleFade>();
        }

        class RippleFade : MonoBehaviour
        {
            float t;
            void Update()
            {
                t += Time.unscaledDeltaTime / 0.45f;
                if (t >= 1f) { Destroy(gameObject); return; }
                transform.localScale = Vector3.one * Mathf.Lerp(1f, 2.4f, t);
                var img = GetComponent<Image>();
                img.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.55f, 0f, t));
            }
        }

        /// <summary>A soft filled circle, generated once — no asset pipeline.</summary>
        static Sprite Circle()
        {
            if (circle != null) return circle;
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            float c = (S - 1) * 0.5f;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    byte a = (byte)(255f * Mathf.Clamp01(1f - Mathf.InverseLerp(0.82f, 1f, d)));
                    px[y * S + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply();
            circle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            return circle;
        }
    }
}
