using UnityEngine;

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    bool garageAutoFit = true;
    float garageLastDistance = -1f, garageLastWorkspace = -1f;
    Bounds garageLastBounds;

    /// <summary>Fit against the actual clear band above the dock, not the
    /// full window. The projection shift already centres that band.</summary>
    public static float GarageFitDistance(Bounds bounds, Quaternion view, float verticalFov,
                                          float aspect, float visibleHeight)
    {
        float tanY = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
        float tanX = tanY * Mathf.Max(0.2f, aspect) * 0.84f;
        tanY *= Mathf.Clamp(visibleHeight, 0.08f, 1f) * 0.78f;
        Quaternion inverse = Quaternion.Inverse(view);
        float distance = 0.8f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3((i & 1) == 0 ? -bounds.extents.x : bounds.extents.x,
                                         (i & 2) == 0 ? -bounds.extents.y : bounds.extents.y,
                                         (i & 4) == 0 ? -bounds.extents.z : bounds.extents.z);
            Vector3 p = inverse * corner;
            distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(p.x) / tanX - p.z,
                                 Mathf.Max(Mathf.Abs(p.y) / tanY - p.z, 0.3f - p.z)));
        }
        return distance;
    }

    Bounds GarageBuildBounds()
    {
        Bounds bounds = new Bounds(new Vector3(0f, 0.7f, 0f), Vector3.one * 0.5f);
        bool first = true;
        foreach (var part in placed)
        {
            if (part == null || part.def == null) continue;
            var b = new Bounds(part.pos, part.Half() * 2f);
            if (first) { bounds = b; first = false; }
            else bounds.Encapsulate(b);
        }
        return bounds;
    }

    public void FitGarageView() { garageAutoFit = true; garageLastDistance = -1f; }

    /// <param name="factor">Less than one zooms in; greater than one zooms out.</param>
    public void ZoomGarageView(float factor)
    {
        garageAutoFit = false;
        orbitDist = Mathf.Clamp(orbitDist * factor, 0.8f, 60f);
        garageLastDistance = orbitDist;
    }

    void UpdateGarageCamera()
    {
        if (cam == null) return;
        Bounds bounds = GarageBuildBounds();
        float visible = MobileBuilderUI.Active
            ? 1f - MobileBuilderUI.coverBottom - MobileBuilderUI.coverTop : 1f;
        if (garageLastDistance >= 0f && Mathf.Abs(orbitDist - garageLastDistance) > 0.001f)
            garageAutoFit = false; // Existing touch/pinch and QA distance controls.
        // Reframe when a different build is loaded, a part extends the build,
        // or the dock/viewport changes. Ordinary orbit keeps a fitted view.
        if (Mathf.Abs(visible - garageLastWorkspace) > 0.005f
            || (bounds.center - garageLastBounds.center).sqrMagnitude > 0.0001f
            || (bounds.size - garageLastBounds.size).sqrMagnitude > 0.0001f)
            garageAutoFit = true;
        garageLastWorkspace = visible;
        garageLastBounds = bounds;
        if (!UiTyping())
        {
            orbitYaw += Phase0Input.OrbitAxis() * 80f * Time.deltaTime;
            orbitPitch = Mathf.Clamp(orbitPitch - Phase0Input.Throttle() * 55f * Time.deltaTime, -70f, 80f);
            float scroll = Phase0Input.Scroll();
            bool overUI = UnityEngine.EventSystems.EventSystem.current != null
                       && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            if (!uiPointerBlocked && !overUI && Mathf.Abs(scroll) > 0.001f)
                ZoomGarageView(Mathf.Pow(0.88f, scroll));
        }
        Quaternion view = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        if (garageAutoFit && visible > 0.08f)
            orbitDist = GarageFitDistance(bounds, view, cam.fieldOfView, cam.aspect, visible);
        garageLastDistance = orbitDist;
        cam.transform.SetPositionAndRotation(bounds.center + view * new Vector3(0f, 0f, -orbitDist), view);
    }
}
}
