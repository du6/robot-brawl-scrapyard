using UnityEngine;

namespace RobotBrawl.Phase0
{
// ============================================================================
// GussetWeldFx — a brief "weld flash" when a gusset is applied to a face
// (owen, 2026-08-15: "add some visual effect when gusset is applied"). The
// gold plate RefreshGussetFaces draws is the PERMANENT mark; this is the
// momentary spark that says the weld just happened.
//
// Self-contained and self-destroying, the FloatingDamage precedent: a spawned
// GameObject that animates itself for a fraction of a second and cleans up, so
// nothing has to own or tick it. Unlit + transparent (Sprites/Default, always
// included, honours material.color incl. alpha) so it reads as a glow rather
// than a shaded ball, and it carries NO collider — visible, never occupying,
// exactly like the gusset plate.
//
// Driven by unscaledDeltaTime so it looks the same regardless of any build-mode
// time scaling, and gated to the BUILD flow by its one caller (ApplyGussetFace);
// gussets are never applied mid-fight, so it never lands in a match frame.
// ============================================================================
public class GussetWeldFx : MonoBehaviour
{
    const float LIFE = 0.42f;

    public static void Spawn(Vector3 worldPos, float faceSize)
    {
        var go = new GameObject("gusset_weld_fx");
        go.transform.position = worldPos;
        go.AddComponent<GussetWeldFx>().size = Mathf.Clamp(faceSize, 0.08f, 0.6f);
    }

    float size = 0.2f;
    float t;
    Renderer glow, core;

    static Material Unlit(Color c)
    {
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = c;
        m.renderQueue = 4000;   // over the opaque build, like a highlight
        return m;
    }

    static Renderer Blob(Transform parent)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(s.GetComponent<Collider>());   // visible, never occupying
        s.transform.SetParent(parent, false);
        return s.GetComponent<Renderer>();
    }

    void Start()
    {
        // The expanding gold shell — the weld's warmth spreading along the seam.
        glow = Blob(transform);
        glow.material = Unlit(new Color(1f, 0.82f, 0.25f, 0.85f));   // the career gold
        // The white core — the instant of the spark, gone fastest.
        core = Blob(transform);
        core.material = Unlit(new Color(1f, 0.97f, 0.85f, 1f));
        Destroy(gameObject, LIFE + 0.05f);
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(t / LIFE);

        // Ease-out expansion for the shell, fading to nothing.
        float e = 1f - (1f - k) * (1f - k);
        float gs = Mathf.Lerp(0.04f, size * 1.7f, e);
        glow.transform.localScale = Vector3.one * gs;
        var gc = glow.material.color; gc.a = 0.85f * (1f - k); glow.material.color = gc;

        // The core snaps out fast (first ~third of the life) and is brightest.
        float ck = Mathf.Clamp01(t / (LIFE * 0.35f));
        float cs = Mathf.Lerp(0.02f, size * 0.9f, ck);
        core.transform.localScale = Vector3.one * cs;
        var cc = core.material.color; cc.a = 1f - ck; core.material.color = cc;
    }
}
}
