// ===========================================================================
// BuilderManager.WorldLook.cs — HOW THE PLANET LOOKS (Robot Brawl:
// Scrapyard). owen, 2026-09-10: "The map looks ugly. It should look like
// some planets where robots live with modern designs. Driving the robot
// around and exploring the planet itself should be an enjoyable experience."
//
// Everything here is code and primitives — the web budget (§5) allows no
// art assets. The look comes from: a sky with atmosphere and a banded
// sister planet and a moon on the horizon; fog that matches the horizon
// and hides the edge of the loaded world; a low, warm sun with shadows;
// ground coloured per vertex by biome, height band and slope, with a faint
// grid etched into the flats (Assets/Shaders/PlanetGround.shader); and six
// kinds of structure in one design language - graphite bodies, cyan and
// amber light - instead of grey boxes.
//
// Every render setting touched at EnterMap is restored at LeaveMap; the
// garage and the fights look as they always did.
// ===========================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;   // AmbientMode, ShadowCastingMode

namespace RobotBrawl.Phase0
{
public partial class BuilderManager
{
    // ------------------------------------------------------------ palette
    static readonly Color GRAPHITE = new Color(0.16f, 0.17f, 0.20f);
    static readonly Color GRAPHITE_LIGHT = new Color(0.30f, 0.32f, 0.37f);
    static readonly Color PANEL_BLUE = new Color(0.10f, 0.14f, 0.24f);
    static readonly Color CYAN = new Color(0.25f, 0.92f, 1.0f);
    static readonly Color AMBER = new Color(1.0f, 0.62f, 0.18f);
    static readonly Color VIOLET = new Color(0.62f, 0.40f, 1.0f);

    Material matBody, matBodyLight, matPanel, matCyan, matAmber, matViolet, matPod, matGround;
    Material savedSkybox; Material planetSky;
    bool savedFog; FogMode savedFogMode; Color savedFogColor; float savedFogStart, savedFogEnd;
    AmbientMode savedAmbientMode; Color savedAmbientSky, savedAmbientEq, savedAmbientGround; float savedAmbientIntensity;
    Light sun; Quaternion savedSunRot; Color savedSunColor; float savedSunIntensity; bool sunTouched;
    float savedFarClip; bool farClipTouched;
    GameObject sisterPlanet, moon;
    Vector3 planetDir = new Vector3(0.55f, 0.28f, 0.79f).normalized, moonDir = new Vector3(-0.72f, 0.42f, 0.55f).normalized;

    /// <summary>The ground shader, or null in a build that lost it (then the
    /// flat biome materials draw instead - uglier, never broken).</summary>
    public Material GroundMaterial { get { return matGround; } }
    public bool PlanetLookOn { get { return planetSky != null && RenderSettings.skybox == planetSky; } }

    void EnsureLookMats()
    {
        if (matBody != null) return;
        matBody = PartVisualFactory.Mat(GRAPHITE, 0.55f, 0.45f);
        matBodyLight = PartVisualFactory.Mat(GRAPHITE_LIGHT, 0.6f, 0.5f);
        matPanel = PartVisualFactory.Mat(PANEL_BLUE, 0.7f, 0.8f);
        matCyan = PartVisualFactory.Emissive(CYAN * 0.5f, CYAN * 2.2f, 0.1f, 0.6f);
        matAmber = PartVisualFactory.Emissive(AMBER * 0.6f, AMBER * 2.0f, 0.1f, 0.6f);
        matViolet = PartVisualFactory.Emissive(VIOLET * 0.5f, VIOLET * 1.8f, 0.1f, 0.6f);
        matPod = PartVisualFactory.Mat(new Color(0.20f, 0.22f, 0.27f), 0.6f, 0.55f);
        var sh = Shader.Find("Scrapyard/PlanetGround");
        if (sh != null)
        {
            matGround = new Material(sh);
            matGround.SetColor("_GridColor", CYAN);
            matGround.SetFloat("_GridStrength", 0.30f);
        }
    }

    // ------------------------------------------------------------ the sky
    void SetupPlanetLook()
    {
        EnsureLookMats();
        // sky: a copy of the scene's procedural skybox, tinted for another world
        savedSkybox = RenderSettings.skybox;
        if (savedSkybox != null && planetSky == null)
        {
            planetSky = new Material(savedSkybox);
            if (planetSky.HasProperty("_SkyTint")) planetSky.SetColor("_SkyTint", new Color(0.30f, 0.52f, 0.78f));
            if (planetSky.HasProperty("_GroundColor")) planetSky.SetColor("_GroundColor", new Color(0.36f, 0.26f, 0.30f));
            if (planetSky.HasProperty("_AtmosphereThickness")) planetSky.SetFloat("_AtmosphereThickness", 1.35f);
            if (planetSky.HasProperty("_Exposure")) planetSky.SetFloat("_Exposure", 1.25f);
            if (planetSky.HasProperty("_SunSize")) planetSky.SetFloat("_SunSize", 0.05f);
        }
        if (planetSky != null) RenderSettings.skybox = planetSky;

        // fog: the horizon's colour, ending where the loaded world does
        savedFog = RenderSettings.fog; savedFogMode = RenderSettings.fogMode; savedFogColor = RenderSettings.fogColor;
        savedFogStart = RenderSettings.fogStartDistance; savedFogEnd = RenderSettings.fogEndDistance;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.66f, 0.70f);
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = CHUNK * (VIEW_CHUNKS + 0.5f) - 4f;   // 116 m: the last loaded chunk fades out

        // ambient: sky-lit from above, warm from the ground
        savedAmbientMode = RenderSettings.ambientMode; savedAmbientSky = RenderSettings.ambientSkyColor;
        savedAmbientEq = RenderSettings.ambientEquatorColor; savedAmbientGround = RenderSettings.ambientGroundColor;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.80f);
        RenderSettings.ambientEquatorColor = new Color(0.58f, 0.46f, 0.48f);
        RenderSettings.ambientGroundColor = new Color(0.26f, 0.20f, 0.22f);

        // the sun: low and warm, long shadows
        sun = null; sunTouched = false;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun != null)
        {
            savedSunRot = sun.transform.rotation; savedSunColor = sun.color; savedSunIntensity = sun.intensity;
            sun.transform.rotation = Quaternion.Euler(26f, 38f, 0f);
            sun.color = new Color(1.0f, 0.86f, 0.70f);
            sun.intensity = 1.2f;
            sunTouched = true;
            RenderSettings.sun = sun;
        }

        // the sky bodies live 800 m out: the camera must see that far
        if (cam != null) { savedFarClip = cam.farClipPlane; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1600f); farClipTouched = true; }
        var sbShader = Shader.Find("Scrapyard/SkyBody");
        if (sbShader != null)
        {
            sisterPlanet = SkyBody("sister_planet", sbShader, 300f, new Color(0.88f, 0.58f, 0.40f), new Color(0.52f, 0.30f, 0.46f), 7f);
            moon = SkyBody("moon", sbShader, 70f, new Color(0.80f, 0.82f, 0.88f), new Color(0.55f, 0.56f, 0.62f), 3f);
            PlaceSkyBodies();
        }
    }

    GameObject SkyBody(string name, Shader sh, float size, Color a, Color b, float bands)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = name;
        var c = g.GetComponent<Collider>(); if (c != null) Object.Destroy(c);
        g.transform.localScale = Vector3.one * size;
        var m = new Material(sh);
        m.SetColor("_ColorA", a); m.SetColor("_ColorB", b); m.SetFloat("_Bands", bands);
        m.SetVector("_SunDir", sun != null ? -(Vector4)sun.transform.forward : new Vector4(0.4f, 0.5f, -0.7f, 0f));
        g.GetComponent<Renderer>().sharedMaterial = m;
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (worldRoot != null) g.transform.SetParent(worldRoot.transform, true);
        return g;
    }

    /// <summary>They follow the camera at a fixed bearing, so they never get
    /// nearer: the horizon of a world with no edge.</summary>
    void PlaceSkyBodies()
    {
        if (cam == null) return;
        if (sisterPlanet != null) sisterPlanet.transform.position = cam.transform.position + planetDir * 800f;
        if (moon != null) moon.transform.position = cam.transform.position + moonDir * 900f;
    }

    void RestoreLook()
    {
        if (savedSkybox != null || planetSky != null) RenderSettings.skybox = savedSkybox;
        RenderSettings.fog = savedFog; RenderSettings.fogMode = savedFogMode; RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogStartDistance = savedFogStart; RenderSettings.fogEndDistance = savedFogEnd;
        RenderSettings.ambientMode = savedAmbientMode; RenderSettings.ambientSkyColor = savedAmbientSky;
        RenderSettings.ambientEquatorColor = savedAmbientEq; RenderSettings.ambientGroundColor = savedAmbientGround;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
        if (sunTouched && sun != null) { sun.transform.rotation = savedSunRot; sun.color = savedSunColor; sun.intensity = savedSunIntensity; }
        sunTouched = false;
        if (farClipTouched && cam != null) cam.farClipPlane = savedFarClip;
        farClipTouched = false;
        sisterPlanet = null; moon = null;   // children of the world root; swept with it
    }

    // ------------------------------------------------------------ ground colour
    /// <summary>The colour of a ground vertex: biome palette, height band, and
    /// slope turning to bare rock; a lighter plaza around home.</summary>
    Color GroundColor(float x, float z, float h, Vector3 n)
    {
        float b = Biome(x, z);
        Color lo, hi;
        if (b < 0.42f)      { lo = new Color(0.50f, 0.30f, 0.20f); hi = new Color(0.74f, 0.54f, 0.38f); }   // rust flats
        else if (b < 0.66f) { lo = new Color(0.26f, 0.38f, 0.30f); hi = new Color(0.52f, 0.60f, 0.42f); }   // scrap steppe
        else                { lo = new Color(0.20f, 0.20f, 0.27f); hi = new Color(0.42f, 0.42f, 0.52f); }   // ash fields
        // soften the biome border: blend toward the neighbour palette near the thresholds
        float band = Mathf.Clamp01((h + 3f) / 10f);
        Color c = Color.Lerp(lo, hi, band);
        float slope = Mathf.Clamp01((0.90f - n.y) / 0.25f);
        c = Color.Lerp(c, new Color(0.19f, 0.17f, 0.19f), slope);
        float dHome = Vector2.Distance(new Vector2(x, z), new Vector2(homePos.x, homePos.z));
        float plaza = 1f - Mathf.Clamp01((dHome - 10f) / 6f);
        c = Color.Lerp(c, new Color(0.36f, 0.40f, 0.48f), plaza);
        return c;
    }

    // ------------------------------------------------------------ structures
    /// <summary>Six kinds, one language. Every piece is a primitive with its
    /// collider; the accents are emissive. Sized to be driven around, not
    /// over.</summary>
    void SpawnStructure(Transform parent, int kind, float x, float z, float gy, System.Random rng)
    {
        float yaw = (float)rng.NextDouble() * 360f;
        var root = new GameObject("structure_" + kind);
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(x, gy, z);
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform t = root.transform;
        switch (kind)
        {
            case 0:   // MONOLITH: a leaning slab with a light seam
            {
                float hgt = 5f + (float)rng.NextDouble() * 4f;
                var slab = Prim(PrimitiveType.Cube, t, new Vector3(0f, hgt * 0.5f, 0f), new Vector3(1.8f, hgt, 0.7f), Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 6f - 3f), matBody);
                Prim(PrimitiveType.Cube, slab.transform, new Vector3(0f, 0.02f, 0.5f), new Vector3(0.05f, 0.82f, 0.03f), Quaternion.identity, rng.Next(2) == 0 ? matCyan : matAmber, false);
                break;
            }
            case 1:   // ANTENNA MAST: a spire, three rings, an amber lamp
            {
                float hgt = 6f + (float)rng.NextDouble() * 5f;
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, hgt * 0.5f, 0f), new Vector3(0.28f, hgt * 0.5f, 0.28f), Quaternion.identity, matBodyLight);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.3f, 0f), new Vector3(1.6f, 0.3f, 1.6f), Quaternion.identity, matBody);
                for (int r = 1; r <= 3; r++)
                    Prim(PrimitiveType.Cylinder, t, new Vector3(0f, hgt * r / 3.6f, 0f), new Vector3(0.9f - r * 0.15f, 0.04f, 0.9f - r * 0.15f), Quaternion.identity, matCyan, false);
                Prim(PrimitiveType.Sphere, t, new Vector3(0f, hgt + 0.3f, 0f), Vector3.one * 0.7f, Quaternion.identity, matAmber, false);
                break;
            }
            case 2:   // CRYSTAL CLUSTER: violet shards out of the rock
            {
                int n = 3 + rng.Next(3);
                for (int i = 0; i < n; i++)
                {
                    float a = (float)rng.NextDouble() * 360f;
                    float len = 1.6f + (float)rng.NextDouble() * 2.4f;
                    Vector3 off = new Vector3(Mathf.Cos(a) * (0.4f + (float)rng.NextDouble()), len * 0.35f, Mathf.Sin(a) * (0.4f + (float)rng.NextDouble()));
                    Prim(PrimitiveType.Cube, t, off, new Vector3(0.5f + (float)rng.NextDouble() * 0.4f, len, 0.5f + (float)rng.NextDouble() * 0.4f),
                         Quaternion.Euler((float)rng.NextDouble() * 30f - 15f, a, (float)rng.NextDouble() * 30f - 15f), i == 0 ? matViolet : (rng.Next(3) == 0 ? matCyan : matViolet));
                }
                break;
            }
            case 3:   // SOLAR ARRAY: a row of tilted panels on posts
            {
                int n = 4 + rng.Next(4);
                for (int i = 0; i < n; i++)
                {
                    float px = (i - (n - 1) * 0.5f) * 2.9f;
                    Prim(PrimitiveType.Cylinder, t, new Vector3(px, 0.6f, 0f), new Vector3(0.12f, 0.6f, 0.12f), Quaternion.identity, matBodyLight, false);
                    Prim(PrimitiveType.Cube, t, new Vector3(px, 1.35f, 0f), new Vector3(2.6f, 0.06f, 1.7f), Quaternion.Euler(-32f, 0f, 0f), matPanel);
                }
                Prim(PrimitiveType.Cube, t, new Vector3(0f, 0.12f, 1.4f), new Vector3(n * 2.9f, 0.08f, 0.12f), Quaternion.identity, matCyan, false);
                break;
            }
            case 4:   // RING BEACON: a ring of blocks around a lit column
            {
                int n = 12; float rad = 2.6f;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Prim(PrimitiveType.Cube, t, new Vector3(Mathf.Cos(a) * rad, 0.45f, Mathf.Sin(a) * rad), new Vector3(0.9f, 0.9f, 0.5f),
                         Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), i % 3 == 0 ? matAmber : matBody, i % 3 != 0);
                }
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 2.0f, 0f), new Vector3(0.5f, 2.0f, 0.5f), Quaternion.identity, matBodyLight);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 4.1f, 0f), new Vector3(0.9f, 0.08f, 0.9f), Quaternion.identity, matCyan, false);
                break;
            }
            default:  // RELAY HUB: a low drum with a half-buried dome and a lit rim
            {
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.45f, 0f), new Vector3(3.6f, 0.45f, 3.6f), Quaternion.identity, matBody);
                Prim(PrimitiveType.Cylinder, t, new Vector3(0f, 0.92f, 0f), new Vector3(3.7f, 0.04f, 3.7f), Quaternion.identity, matCyan, false);
                Prim(PrimitiveType.Sphere, t, new Vector3(0f, 0.9f, 0f), Vector3.one * 2.6f, Quaternion.identity, matBodyLight);
                Prim(PrimitiveType.Cube, t, new Vector3(0f, 2.3f, 0f), new Vector3(0.12f, 1.2f, 0.12f), Quaternion.identity, matAmber, false);
                break;
            }
        }
    }

    static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Quaternion localRot, Material m, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(type);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = localRot;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) { var c = g.GetComponent<Collider>(); if (c != null) Object.Destroy(c); }
        return g;
    }

    /// <summary>The home plaza: a relay hub behind you, two monoliths framing
    /// the way out, so the first frame has a place in it.</summary>
    void SpawnHomePlaza(Transform parent, System.Random rng)
    {
        SpawnStructure(parent, 5, homePos.x, homePos.z - 9f, TerrainHeight(homePos.x, homePos.z - 9f), rng);
        SpawnStructure(parent, 0, homePos.x - 7f, homePos.z + 4f, TerrainHeight(homePos.x - 7f, homePos.z + 4f), rng);
        SpawnStructure(parent, 0, homePos.x + 7f, homePos.z + 4f, TerrainHeight(homePos.x + 7f, homePos.z + 4f), rng);
    }

    /// <summary>A crate is a supply pod: a dark shell with an amber band and a
    /// beam you can see from a way off.</summary>
    GameObject MakePod(Transform parent, float x, float gy, float z, string key)
    {
        var pod = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pod.name = "crate " + key;
        pod.transform.SetParent(parent, false);
        pod.transform.position = new Vector3(x, gy + 0.4f, z);
        pod.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        pod.transform.localScale = new Vector3(1.0f, 0.8f, 0.8f);
        pod.GetComponent<Renderer>().sharedMaterial = matPod;
        Object.Destroy(pod.GetComponent<Collider>());     // you drive INTO it
        Prim(PrimitiveType.Cube, pod.transform, Vector3.zero, new Vector3(1.04f, 0.16f, 0.84f), Quaternion.identity, matAmber, false);
        Prim(PrimitiveType.Cylinder, pod.transform, new Vector3(0f, 3.0f, 0f), new Vector3(0.10f, 2.4f, 0.12f), Quaternion.identity, matAmber, false);
        return pod;
    }
}
}
