using System.Collections.Generic;
using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>
/// Phase 2B broadcast camera: frames BOTH bots. Round-1 rewrite (critic fix 4
/// — the old version lost the fight against walls and let bots leave frame):
///   · fit-to-pair: chase distance derives from the ACTUAL camera FOV so both
///     bots always project inside the frame with margin; if the arena clamp
///     squeezes the ideal spot, the camera climbs until the fit check passes;
///   · wall guard: checks both robots against static AND moving walls, then
///     climbs above an obstruction without sacrificing the pair's framing;
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
    readonly RaycastHit[] sightHits = new RaycastHit[64];
    readonly HashSet<Transform> robotParts = new HashSet<Transform>();
    Transform cachedA, cachedB;

    void CacheRobotParts()
    {
        if (cachedA == a && cachedB == b) return;
        cachedA = a; cachedB = b;
        robotParts.Clear();
        if (a != null) foreach (var t in a.GetComponentsInChildren<Transform>(true)) robotParts.Add(t);
        if (b != null) foreach (var t in b.GetComponentsInChildren<Transform>(true)) robotParts.Add(t);
    }

    // Robot identity, not the mere presence of a Rigidbody, excludes a hit.
    // Retain child identities after wheels/parts detach; newly spawned shards
    // are in the fight core's existing Spawned registry. Crusher bodies are
    // kinematic, and must still block this camera.
    bool IsOccluder(Collider collider)
    {
        if (collider == null || collider.isTrigger) return false;
        if (robotParts.Contains(collider.transform) || collider.GetComponentInParent<CompoundRobot>() != null) return false;
        var body = collider.attachedRigidbody;
        return body == null || !CompoundRobot.Spawned.Contains(body.gameObject);
    }

    bool Obstructed(Vector3 target, Vector3 lens)
    {
        Vector3 ray = lens - target;
        float distance = ray.magnitude;
        if (distance < 0.01f) return false;
        int count = Physics.SphereCastNonAlloc(target, 0.15f, ray / distance, sightHits, distance,
                                              Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (IsOccluder(sightHits[i].collider)) return true;
        // Saturation is rare, but must not silently omit the wall in debris.
        if (count == sightHits.Length)
            foreach (var hit in Physics.SphereCastAll(target, 0.15f, ray / distance, distance,
                                                      Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (IsOccluder(hit.collider)) return true;
        return false;
    }

    float FitAngle()
    {
        float vertical = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
        float horizontal = Mathf.Atan(Mathf.Tan(vertical) * (cam != null ? cam.aspect : 1.78f));
        return Mathf.Min(vertical, horizontal) * 0.72f;
    }

    bool PairFits(Vector3 lens, Vector3 look, Vector3 pa, Vector3 pb)
    {
        Vector3 forward = (look - lens).normalized;
        float worst = Mathf.Max(Vector3.Angle(forward, pa + Vector3.up * 0.3f - lens),
                                Vector3.Angle(forward, pb + Vector3.up * 0.3f - lens));
        return worst * Mathf.Deg2Rad <= FitAngle();
    }

    bool PairVisible(Vector3 lens, Vector3 pa, Vector3 pb)
    {
        return !Obstructed(pa + Vector3.up * 0.3f, lens) && !Obstructed(pb + Vector3.up * 0.3f, lens);
    }

    Vector3 SafeFraming(Vector3 pos, Vector3 look, Vector3 pa, Vector3 pb)
    {
        // Pulling toward a hit used to turn a two-robot shot into a close-up.
        // Height is unconstrained, so rise over walls and preserve the fit.
        for (int i = 0; i < 16; i++)
        {
            if (PairFits(pos, look, pa, pb) && PairVisible(pos, pa, pb)) break;
            pos.y += Mathf.Max(1.5f, (pos.y - look.y) * 0.25f);
            pos.x = Mathf.Lerp(pos.x, look.x, 0.12f);
            pos.z = Mathf.Lerp(pos.z, look.z, 0.12f);
        }
        return pos;
    }

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
        CacheRobotParts();
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
            return SafeFraming(op, look, pa, pb);
        }

        // Half-angle the frame really offers (the tighter of vertical /
        // horizontal), with a 28% safety margin inside the edges.
        float fit = FitAngle();

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

        return SafeFraming(pos, look, pa, pb);
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
        Vector3 desired = Desired(out look);
        Vector3 eased = Vector3.Lerp(transform.position, desired, smooth * Time.deltaTime);
        Vector3 pa = a != null ? a.position : (b != null ? b.position : Vector3.zero);
        Vector3 pb = b != null ? b.position : pa;
        // A crusher can overtake a slowly eased camera. Safety applies to the
        // rendered position as well as the destination; snap upward if needed.
        transform.position = PairFits(eased, look, pa, pb) && PairVisible(eased, pa, pb) ? eased : desired;
        transform.LookAt(look);   // world up — no roll
    }
}

}