using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2B broadcast camera: frames BOTH bots. Round-1 rewrite (critic fix 4
/// — the old version lost the fight against walls and let bots leave frame):
///   · fit-to-pair: chase distance derives from the ACTUAL camera FOV so both
///     bots always project inside the frame with margin; if the arena clamp
///     squeezes the ideal spot, the camera climbs until the fit check passes;
///   · wall guard: a ray from the look target to the lens pulls the camera in
///     front of any static geometry it would sit inside/behind;
///   · zero roll: LookAt with world up only.
/// If one bot dies the midpoint degrades gracefully to the survivor.
/// </summary>
public class FightCamera : MonoBehaviour
{
    public Transform a, b;
    public Vector3 sideDir = new Vector3(1f, 0f, 0f); // world side to shoot from
    public float clampHalf = 6.2f;
    public float smooth = 3f;
    /// <summary>Round-2 fix 3: match end cuts to a fixed elevated 3/4 overview
    /// (arena center biased toward the wrecks) — the chase framing could end a
    /// count-out wedged against a wall corner and render the results screen
    /// into wall geometry.</summary>
    public bool overview;

    Camera cam;

    /// <summary>Called by FightManager.End: switch to the safe overview and
    /// snap immediately so the results screen never opens inside a wall.</summary>
    public void EndOverview()
    {
        overview = true;
        SnapNow();
    }

    Vector3 Desired(out Vector3 look)
    {
        if (cam == null) cam = GetComponent<Camera>();
        Vector3 pa = a != null ? a.position : (b != null ? b.position : Vector3.zero);
        Vector3 pb = b != null ? b.position : pa;
        Vector3 mid = (pa + pb) * 0.5f;
        look = mid + Vector3.up * 0.4f;

        if (overview)
        {
            // Elevated 3/4 view from well inside the arena: position biased a
            // third of the way from center toward the wrecks, 8 m up, offset
            // to the shoot side; xz clamped to ±4.5 (walls are at ±7).
            Vector3 side0 = sideDir; side0.y = 0f;
            if (side0.sqrMagnitude < 0.01f) side0 = new Vector3(1f, 0f, 0f);
            side0.Normalize();
            Vector3 op = mid * 0.35f + side0 * 3.0f + Vector3.up * 8.0f;
            op.x = Mathf.Clamp(op.x, -4.5f, 4.5f);
            op.z = Mathf.Clamp(op.z, -4.5f, 4.5f);
            return op;
        }

        // Half-angle the frame really offers (the tighter of vertical /
        // horizontal), with a 28% safety margin inside the edges.
        float vHalf = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * (cam != null ? cam.aspect : 1.78f));
        float fit = Mathf.Min(vHalf, hHalf) * 0.72f;

        float sep = Vector3.Distance(pa, pb);
        float d = Mathf.Clamp((sep * 0.5f) / Mathf.Tan(fit) + 2.2f, 6f, 15f);

        Vector3 side = sideDir;
        side.y = 0f;
        if (side.sqrMagnitude < 0.01f) side = new Vector3(1f, 0f, 0f);
        side.Normalize();

        Vector3 pos = mid;
        for (int it = 0; it < 4; it++)
        {
            pos = mid + side * (d * 0.80f) + Vector3.up * (d * 0.55f);
            if (clampHalf > 0.01f)
            {
                pos.x = Mathf.Clamp(pos.x, -clampHalf, clampHalf);
                pos.z = Mathf.Clamp(pos.z, -clampHalf, clampHalf);
            }
            if (pos.y < 2.6f) pos.y = 2.6f;

            // Hard both-in-frame constraint: worst bot angle off the view
            // axis must sit inside the fit cone; otherwise back off/climb
            // (height is never clamped, so this always converges).
            Vector3 fwd = (look - pos).normalized;
            float worst = Mathf.Max(
                Vector3.Angle(fwd, (pa + Vector3.up * 0.3f - pos).normalized),
                Vector3.Angle(fwd, (pb + Vector3.up * 0.3f - pos).normalized)) * Mathf.Deg2Rad;
            if (worst <= fit) break;
            d = Mathf.Min(d * 1.25f, 22f);
        }

        // Wall guard: never let static geometry sit between the fight and the
        // lens (or contain the lens). Robots/debris (rigidbodies) don't count.
        Vector3 dir = pos - look;
        float L = dir.magnitude;
        if (L > 0.01f)
        {
            dir /= L;
            float nearest = float.MaxValue;
            bool blocked = false;
            foreach (var h in Physics.RaycastAll(look, dir, L))
            {
                if (h.collider.isTrigger || h.rigidbody != null) continue;
                if (h.distance < nearest) { nearest = h.distance; blocked = true; }
            }
            if (blocked) pos = look + dir * Mathf.Max(2.0f, nearest - 0.4f);
        }
        return pos;
    }

    public void SnapNow()
    {
        Vector3 look;
        transform.position = Desired(out look);
        transform.LookAt(look);   // world up — no roll
    }

    void LateUpdate()
    {
        Vector3 look;
        transform.position = Vector3.Lerp(transform.position, Desired(out look), smooth * Time.deltaTime);
        transform.LookAt(look);   // world up — no roll
    }
}

}