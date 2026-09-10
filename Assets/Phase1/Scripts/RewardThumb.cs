// ===========================================================================
// RewardThumb.cs — a PICTURE of a reward, with no image assets (Robot Brawl:
// Scrapyard; owen, 2026-09-10: "when a part is acquired, we should display
// the image of the part in addition to text. for scraps we should show
// something like coins").
//
// The part is built with the same PartVisualFactory the builder uses for a
// ghost, in a studio 500 m under the world, photographed once by a
// throw-away camera into a small texture, and torn down. Scrap is a stack of
// gold coins built the same way. Headless (-nographics) there is no GPU to
// photograph with: Render() then returns a texture of the right size that
// may be blank, and never throws.
// ===========================================================================
using UnityEngine;

namespace RobotBrawl.Phase0
{
    public static class RewardThumb
    {
        static readonly Vector3 STUDIO = new Vector3(0f, -500f, 0f);

        public static P1PartDef Def(string partId)
        {
            foreach (var d in P1PartDef.Palette()) if (d.id == partId) return d;
            return null;
        }

        /// <summary>Build the visual of one part (or, with partId "coins", a coin
        /// stack) under `parent`, roughly one metre across.</summary>
        public static GameObject BuildModel(Transform parent, string partId, string mat)
        {
            var root = new GameObject("thumb_" + partId);
            root.transform.SetParent(parent, false);
            if (partId == "coins")
            {
                var gold = PartVisualFactory.Emissive(new Color(0.95f, 0.72f, 0.20f), new Color(0.9f, 0.6f, 0.1f) * 0.6f, 0.9f, 0.7f);
                for (int i = 0; i < 6; i++)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    c.transform.SetParent(root.transform, false);
                    c.transform.localPosition = new Vector3(i % 2 == 0 ? 0f : 0.06f, 0.05f + i * 0.09f, i % 3 == 0 ? 0f : -0.04f);
                    c.transform.localRotation = Quaternion.Euler(0f, i * 23f, 0f);
                    c.transform.localScale = new Vector3(0.6f, 0.04f, 0.6f);
                    c.GetComponent<Renderer>().sharedMaterial = gold;
                    var col = c.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
                }
                var lean = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                lean.transform.SetParent(root.transform, false);
                lean.transform.localPosition = new Vector3(0.42f, 0.28f, 0.1f);
                lean.transform.localRotation = Quaternion.Euler(78f, 0f, 20f);
                lean.transform.localScale = new Vector3(0.6f, 0.04f, 0.6f);
                lean.GetComponent<Renderer>().sharedMaterial = gold;
                var lc = lean.GetComponent<Collider>(); if (lc != null) Object.Destroy(lc);
                return root;
            }
            var def = Def(partId);
            if (def == null) return root;
            if (string.IsNullOrEmpty(mat)) mat = def.matName;
            var probe = new BuilderManager.PlacedPart { def = def, yaw = 0, wheelAxis = Vector3.right };
            if (def.category == P1Category.Mobility)
                PartVisualFactory.BuildWheel(root.transform, def.size.x * 0.5f, def.size.y, -1);
            else if (def.id.StartsWith("spinner"))
                PartVisualFactory.BuildSpinner(root.transform, def.size.x * 0.5f, def.size.y, -1);
            else if (def.id == "spike")
                PartVisualFactory.BuildSpike(root.transform, def.size);
            else
            {
                var md = MatDB.Get(mat);
                PartVisualFactory.BuildPart(BuilderManager.VisualId(def, probe.DriveAxis()), root.transform, probe.Half() * 2f,
                    def.category == P1Category.Control, md.color, md.metallic, md.smoothness);
            }
            foreach (var col in root.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            return root;
        }

        /// <summary>Photograph a part (or "coins") into a square texture with a
        /// transparent background. Null only if a camera could not be made.</summary>
        public static Texture2D Render(string partId, string mat, int px)
        {
            px = Mathf.Clamp(px, 16, 512);
            GameObject studio = null; RenderTexture rt = null; Texture2D tex = null;
            var prevActive = RenderTexture.active;
            try
            {
                studio = new GameObject("reward_studio");
                studio.transform.position = STUDIO;
                var model = BuildModel(studio.transform, partId, mat);
                // frame it: the renderers' bounds
                var bounds = new Bounds(studio.transform.position, Vector3.one * 0.2f);
                bool any = false;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                { if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds); }
                float radius = Mathf.Max(0.25f, bounds.extents.magnitude);
                var camGo = new GameObject("reward_camera");
                camGo.transform.SetParent(studio.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.fieldOfView = 28f;
                cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
                cam.allowHDR = false; cam.allowMSAA = false;
                Vector3 dir = new Vector3(0.55f, 0.45f, -0.70f).normalized;
                float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
                cam.transform.position = bounds.center + dir * dist;
                cam.transform.LookAt(bounds.center);
                rt = new RenderTexture(px, px, 16, RenderTextureFormat.ARGB32);
                rt.Create();
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
                tex.Apply();
                cam.targetTexture = null;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RewardThumb] no picture for " + partId + ": " + e.Message);
                if (tex == null) { tex = new Texture2D(px, px, TextureFormat.RGBA32, false); }
            }
            finally
            {
                RenderTexture.active = prevActive;
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                if (studio != null) Object.DestroyImmediate(studio);   // gone THIS frame: nothing of the studio ever renders in a real camera
            }
            return tex;
        }
    }
}
