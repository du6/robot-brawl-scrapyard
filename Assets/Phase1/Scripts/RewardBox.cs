// ===========================================================================
// RewardBox.cs - the ceremony for an earned reward.
//
// owen, 2026-09-04: "add some congrats animations whenever the user completes
// a guided task and give them some rewards in a box. User can open the box
// and see the rewards."
//
// The GRANT is already ledgered by Career before anything is queued (the
// checklist's +10s, the rescue crate) - this is the handover moment, not the
// transaction. So the box can be skipped, reloaded through, or crash without
// losing a single scrap, and it never touches Career.Data.
//
// Everything is procedural UGUI, the RookieGuide pattern: a burst of confetti
// squares, a banner, a crate whose lid swings open on a tap, and the reward
// lines rising out of it. No assets, no animator, no video.
//
// WEB ONLY like the rest of the warm-up: Career.QueueReward compiles to a
// no-op outside the WebGL player, so benches and iOS never see one.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RobotBrawl.Phase0
{
    public class RewardBox : MonoBehaviour
    {
        public static RewardBox active;

        /// <summary>Drain one queued reward into a box. Called from
        /// MobileBuilderUI.Update; no-op while a box is up or a fight is on.</summary>
        public static void Tick()
        {
            if (active != null) return;
            if (Career.rewardQueue.Count == 0) return;
            if (FightManager.current != null) return;
            var r = Career.rewardQueue[0];
            Career.rewardQueue.RemoveAt(0);
            var go = new GameObject("RewardBox");
            active = go.AddComponent<RewardBox>();
            active.pop = r;
        }

        Career.RewardPop pop;
        enum Phase { Appear, Closed, Opening, Revealed, Dismiss }
        Phase phase = Phase.Appear;
        float t;                 // seconds in the current phase
        float k = 1f;            // dpi scale

        Canvas canvas;
        Image backdrop;
        RectTransform boxRt, lidRt, bodyRt;
        Text title, caption, tapHint;
        Button claim;
        readonly List<Text> lines = new List<Text>();
        readonly List<Vector2> lineHome = new List<Vector2>();
        Font font;
        static Sprite white;

        const float APPEAR = 0.45f, OPEN = 0.5f, REVEAL = 0.6f, DISMISS = 0.3f, AUTO = 9f;

        static readonly Color[] PALETTE = {
            new Color(1f, 0.84f, 0.40f), new Color(0.40f, 0.80f, 1f), new Color(1f, 0.45f, 0.45f),
            new Color(0.55f, 1f, 0.60f), new Color(1f, 1f, 1f), new Color(1f, 0.62f, 0.24f) };

        void Start()
        {
            k = Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi / 163f : 1f, 1f, 2.5f);
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var cgo = new GameObject("RewardBoxCanvas", typeof(Canvas), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6000;   // above the guide's 5000

            // Backdrop - dims the game and swallows stray taps.
            backdrop = MkImage("backdrop", cgo.transform, new Color(0f, 0f, 0f, 0f));
            var brt = backdrop.rectTransform;
            brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            backdrop.raycastTarget = true;

            // The crate: a body and a lid, lid pivoting on its bottom edge.
            float bw = 220f * k, bh = 150f * k, lh = 44f * k;
            var boxGo = new GameObject("box", typeof(RectTransform));
            boxGo.transform.SetParent(cgo.transform, false);
            boxRt = boxGo.GetComponent<RectTransform>();
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(bw, bh + lh);
            boxRt.anchoredPosition = new Vector2(0f, -45f * k);
            boxRt.localScale = Vector3.zero;

            var body = MkImage("body", boxRt, new Color(0.55f, 0.36f, 0.16f));
            bodyRt = body.rectTransform;
            bodyRt.anchorMin = bodyRt.anchorMax = new Vector2(0.5f, 0f);
            bodyRt.pivot = new Vector2(0.5f, 0f);
            bodyRt.sizeDelta = new Vector2(bw, bh);
            bodyRt.anchoredPosition = Vector2.zero;
            // a strap and rivets so it reads as a crate, not a brown rect
            var strap = MkImage("strap", bodyRt, new Color(0.30f, 0.20f, 0.09f));
            strap.rectTransform.anchorMin = new Vector2(0f, 0.42f); strap.rectTransform.anchorMax = new Vector2(1f, 0.58f);
            strap.rectTransform.offsetMin = strap.rectTransform.offsetMax = Vector2.zero;
            var plate = MkImage("plate", bodyRt, new Color(0.85f, 0.70f, 0.30f));
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            plate.rectTransform.sizeDelta = new Vector2(46f * k, 34f * k);
            body.raycastTarget = true;               // a Button needs a hit-testable graphic (verified: with it false the box never opened)
            var tapBtn = body.gameObject.AddComponent<Button>();
            tapBtn.onClick.AddListener(Open);

            var lid = MkImage("lid", boxRt, new Color(0.68f, 0.46f, 0.20f));
            lidRt = lid.rectTransform;
            lidRt.anchorMin = lidRt.anchorMax = new Vector2(0.5f, 0f);
            lidRt.pivot = new Vector2(0.5f, 0f);     // hinge on the back edge, visually the bottom
            lidRt.sizeDelta = new Vector2(bw + 12f * k, lh);
            lidRt.anchoredPosition = new Vector2(0f, bh);

            // Words: title above, caption below the title, hint under the box.
            // A short screen (iPhone landscape) cannot hold the tall layout:
            // the title sat on the status line and the reward lines on the
            // tab strip (iOS QA 2026-09-05). Compress every vertical offset so
            // the top of the title stays inside the screen.
            float v = Mathf.Clamp((Screen.height * 0.5f - 24f * k) / (237f * k), 0.45f, 1f);
            title = MkText("title", cgo.transform, "", Mathf.RoundToInt(30f * k * Mathf.Max(0.8f, v)), TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold; title.color = new Color(1f, 0.84f, 0.40f);
            Place(title.rectTransform, 0f, 215f * k * v, 900f * k, 44f * k);
            caption = MkText("caption", cgo.transform, "", Mathf.RoundToInt(15f * k), TextAnchor.MiddleCenter);
            caption.color = new Color(0.86f, 0.90f, 0.98f);
            Place(caption.rectTransform, 0f, 182f * k * v, 900f * k, 26f * k);
            tapHint = MkText("hint", cgo.transform, "TAP THE BOX TO OPEN", Mathf.RoundToInt(15f * k), TextAnchor.MiddleCenter);
            tapHint.color = new Color(1f, 1f, 1f, 0.85f);
            Place(tapHint.rectTransform, 0f, -165f * k * v, 500f * k, 26f * k);

            // Reward lines start INSIDE the box and rise out of it on open.
            for (int i = 0; i < pop.lines.Count; i++)
            {
                var l = MkText("line" + i, cgo.transform, pop.lines[i], Mathf.RoundToInt((i == 0 ? 24f : 17f) * k), TextAnchor.MiddleCenter);
                l.fontStyle = FontStyle.Bold;
                l.color = new Color(1f, 0.92f, 0.55f, 0f);
                Place(l.rectTransform, 0f, 10f * k, 700f * k, 34f * k);
                lines.Add(l);
                // first (biggest) line highest; the rest stack downward toward the lid
                lineHome.Add(new Vector2(0f, (140f - 30f * i) * k * v));
            }

            // CLAIM - appears once revealed.
            var cgoBtn = MkImage("claim", cgo.transform, new Color(0.30f, 0.62f, 0.88f, 0f));
            cgoBtn.raycastTarget = true;
            Place(cgoBtn.rectTransform, 0f, -165f * k, 220f * k, 44f * k);
            claim = cgoBtn.gameObject.AddComponent<Button>();
            claim.onClick.AddListener(() => { if (phase == Phase.Revealed) { phase = Phase.Dismiss; t = 0f; } });
            var ct = MkText("claimtxt", cgoBtn.transform, "CLAIM", Mathf.RoundToInt(17f * k), TextAnchor.MiddleCenter);
            ct.fontStyle = FontStyle.Bold; ct.color = new Color(1f, 1f, 1f, 0f);
            var ctr = ct.rectTransform; ctr.anchorMin = Vector2.zero; ctr.anchorMax = Vector2.one; ctr.offsetMin = ctr.offsetMax = Vector2.zero;
            claim.interactable = false;

            title.text = pop.title; caption.text = pop.caption;
            Burst(26, 0f);
            SfxSynth.Place();
        }

        void OnDestroy()
        {
            if (pop != null) Career.GrantReward(pop.id);   // auto-dismissed or skipped: still paid
            if (active == this) active = null;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            switch (phase)
            {
                case Phase.Appear:
                {
                    float p = Mathf.Clamp01(t / APPEAR);
                    backdrop.color = new Color(0f, 0f, 0f, 0.62f * p);
                    // ease-out-back: overshoots to ~1.1 then settles
                    float s = 1f + 2.7f * Mathf.Pow(p - 1f, 3f) + 1.7f * Mathf.Pow(p - 1f, 2f);
                    boxRt.localScale = Vector3.one * s;
                    if (p >= 1f) { phase = Phase.Closed; t = 0f; }
                    break;
                }
                case Phase.Closed:
                {
                    // a gentle breathing pulse says "this is the thing to tap"
                    float pulse = 1f + 0.04f * Mathf.Sin(t * 5f);
                    boxRt.localScale = Vector3.one * pulse;
                    tapHint.color = new Color(1f, 1f, 1f, 0.55f + 0.35f * Mathf.Abs(Mathf.Sin(t * 3f)));
                    break;
                }
                case Phase.Opening:
                {
                    float p = Mathf.Clamp01(t / OPEN);
                    float e = 1f - Mathf.Pow(1f - p, 3f);
                    // A Z-rotation swept the lid THROUGH the body (seen 2026-09-04).
                    // Flip it over the back edge instead: scale-Y through zero, so
                    // it foreshortens, vanishes edge-on, and reappears inverted as a
                    // short slab behind the rim - an open lid seen from the front.
                    float sy = Mathf.Lerp(1f, -0.45f, e);
                    lidRt.localScale = new Vector3(1f, sy, 1f);
                    var lidImg = lidRt.GetComponent<Image>();
                    if (lidImg != null) lidImg.color = sy >= 0f ? new Color(0.68f, 0.46f, 0.20f) : new Color(0.42f, 0.28f, 0.12f);
                    boxRt.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(p * Mathf.PI));
                    tapHint.color = new Color(1f, 1f, 1f, 0.85f * (1f - p));
                    if (p >= 1f) { phase = Phase.Revealed; t = 0f; Burst(40, 30f * k); SfxSynth.Place(); }
                    break;
                }
                case Phase.Revealed:
                {
                    float p = Mathf.Clamp01(t / REVEAL);
                    float e = 1f - Mathf.Pow(1f - p, 3f);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        float pi = Mathf.Clamp01((t - 0.08f * i) / REVEAL);
                        float ei = 1f - Mathf.Pow(1f - pi, 3f);
                        var rt = lines[i].rectTransform;
                        rt.anchoredPosition = Vector2.Lerp(new Vector2(0f, 10f * k), lineHome[i], ei);
                        lines[i].color = new Color(1f, 0.92f, 0.55f, ei);
                        rt.localScale = Vector3.one * (0.6f + 0.4f * ei);
                    }
                    var ci = claim.GetComponent<Image>();
                    ci.color = new Color(0.30f, 0.62f, 0.88f, e);
                    var ct = claim.GetComponentInChildren<Text>(); if (ct != null) ct.color = new Color(1f, 1f, 1f, e);
                    claim.interactable = p >= 1f;
                    if (t > AUTO) { phase = Phase.Dismiss; t = 0f; }
                    break;
                }
                case Phase.Dismiss:
                {
                    float p = Mathf.Clamp01(t / DISMISS);
                    var cg = GetComponentInChildren<CanvasGroup>();
                    if (cg == null) cg = canvas.gameObject.AddComponent<CanvasGroup>();
                    cg.alpha = 1f - p;
                    if (p >= 1f) { Destroy(gameObject); return; }
                    break;
                }
            }
        }

        void Open()
        {
            if (phase != Phase.Closed) return;
            phase = Phase.Opening; t = 0f;
            if (pop != null) Career.GrantReward(pop.id);   // the reveal IS the grant now
            RBTelemetry.Once("reward", "&o=1");
        }

        // ---- confetti --------------------------------------------------------
        void Burst(int n, float yOffset)
        {
            for (int i = 0; i < n; i++)
            {
                var img = MkImage("confetti", canvas.transform, PALETTE[Random.Range(0, PALETTE.Length)]);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                float sz = Random.Range(6f, 12f) * k;
                rt.sizeDelta = new Vector2(sz, sz * Random.Range(0.5f, 1f));
                rt.anchoredPosition = new Vector2(0f, yOffset);
                var bit = img.gameObject.AddComponent<ConfettiBit>();
                float ang = Random.Range(35f, 145f) * Mathf.Deg2Rad;
                float spd = Random.Range(380f, 720f) * k;
                bit.vel = new Vector2(Mathf.Cos(ang) * spd, Mathf.Sin(ang) * spd);
                bit.spin = Random.Range(-540f, 540f);
                bit.life = Random.Range(1.1f, 1.7f);
                bit.k = k;
            }
        }

        class ConfettiBit : MonoBehaviour
        {
            public Vector2 vel; public float spin, life, k = 1f; float age;
            void Update()
            {
                float dt = Time.unscaledDeltaTime; age += dt;
                if (age >= life) { Destroy(gameObject); return; }
                vel += new Vector2(0f, -1400f * k) * dt;
                vel *= 1f - 1.6f * dt;                       // drag
                var rt = (RectTransform)transform;
                rt.anchoredPosition += vel * dt;
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * age);
                var img = GetComponent<Image>(); var c = img.color;
                c.a = 1f - Mathf.Pow(age / life, 3f); img.color = c;
            }
        }

        // ---- tiny UI kit -----------------------------------------------------
        static Sprite White()
        {
            if (white != null) return white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            return white;
        }
        Image MkImage(string name, Transform parent, Color c)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = White(); img.color = c; img.raycastTarget = false;
            return img;
        }
        Text MkText(string name, Transform parent, string s, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var tx = go.GetComponent<Text>();
            tx.font = font; tx.fontSize = size; tx.alignment = anchor; tx.text = s;
            tx.horizontalOverflow = HorizontalWrapMode.Overflow; tx.verticalOverflow = VerticalWrapMode.Overflow;
            tx.raycastTarget = false;
            return tx;
        }
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }
    }
}
