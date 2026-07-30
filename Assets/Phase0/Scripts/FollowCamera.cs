using UnityEngine;

namespace RobotBrawl.Phase0
{
/// <summary>Simple smoothed chase camera for the Phase 0 sandbox.</summary>
public class FollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 forwardHint = Vector3.forward; // body-space drive direction to chase behind
    public float distance = 7f;
    public float height = 3.5f;
    public float smooth = 4f;
    // Arena bound: when > 0, the camera is kept inside |x|,|z| <= clampHalf so
    // it can never sit outside the walls (which occlude the bot); the height is
    // raised by the clamped amount so the view stays clear over the target.
    public float clampHalf = 0f;

    /// <summary>The chase position LateUpdate lerps toward (clamp included).</summary>
    public Vector3 DesiredPos()
    {
        Vector3 fwd = target.rotation * forwardHint;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();

        float d = distance;
        float h = height;
        if (clampHalf > 0.01f)
        {
            // Prefer pulling the camera IN toward the bot over climbing
            // (round-3 fix: the height-raise showed out-of-world void from an
            // extreme top-down angle at walls). Shrink the chase distance so
            // the camera point stays inside |x|,|z| <= clampHalf.
            for (int a = 0; a < 2; a++)
            {
                float f = a == 0 ? fwd.x : fwd.z;
                float c = a == 0 ? target.position.x : target.position.z;
                if (f > 0.0001f) d = Mathf.Min(d, (clampHalf + c) / f);
                else if (f < -0.0001f) d = Mathf.Min(d, (c - clampHalf) / f);
            }
            d = Mathf.Clamp(d, 2.2f, distance);
            // Keep a pleasant angle: lower the camera as it pulls in.
            h = Mathf.Lerp(1.9f, height, Mathf.InverseLerp(2.2f, distance, d));
        }

        Vector3 wanted = target.position - fwd * d + Vector3.up * h;
        if (clampHalf > 0.01f)
        {
            // Residual clamp (bot cornered facing outward): small CAPPED raise
            // only — never the old unbounded climb.
            float cx = Mathf.Clamp(wanted.x, -clampHalf, clampHalf);
            float cz = Mathf.Clamp(wanted.z, -clampHalf, clampHalf);
            float pushed = Mathf.Abs(wanted.x - cx) + Mathf.Abs(wanted.z - cz);
            wanted = new Vector3(cx, wanted.y + Mathf.Min(pushed * 0.6f, 1.2f), cz);
        }
        return wanted;
    }

    /// <summary>Jump straight to the chase position (mode-switch snap — the
    /// smoothing lerp takes ~1 s to cross a room, which reads as "camera left
    /// behind at the arena edge").</summary>
    public void SnapNow()
    {
        if (target == null) return;
        transform.position = DesiredPos();
        transform.LookAt(target.position + Vector3.up * 0.5f);
    }

    void LateUpdate()
    {
        if (target == null) return;
        transform.position = Vector3.Lerp(transform.position, DesiredPos(), smooth * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 0.5f);
    }
}

}