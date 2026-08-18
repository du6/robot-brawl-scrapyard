// ===========================================================================
// ExportPartTable.cs — dump the game's OWN part geometry and material colours
// to JSON for the website's 3D robot viewer (owen, 2026-08-17).
//
// WHY THIS EXISTS RATHER THAN A HAND-WRITTEN TABLE IN JAVASCRIPT: the web
// viewer rebuilds robots out of the same build text the game uses, and a
// hand-copied size table is a second source of truth that drifts silently —
// the model on the site would slowly stop being the robot in the game, and
// nothing would fail. This reads P1PartDef.Palette() and MatDB directly, so
// a part resized in the game is a part resized on the website at the next
// export, and a part ADDED to the game shows up here instead of rendering as
// a mystery cube.
//
//   from a live editor:
//     RobotBrawl.Editor.ExportPartTable.Export();
//   from a terminal, against a CLONE:
//     Unity -quit -batchmode -nographics -projectPath <clone> \
//           -executeMethod RobotBrawl.Editor.ExportPartTable.Export -logFile -
//
// Writes website/data/parts.json. Committed, because the website build must
// not need Unity.
// ===========================================================================
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using RobotBrawl.Phase0;

namespace RobotBrawl.Editor
{
    public static class ExportPartTable
    {
        const string OUT = "website/data/parts.json";

        static string F(float v)
        { return v.ToString("0.####", CultureInfo.InvariantCulture); }

        static string Hex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        /// <summary>Wheels and the spinner disc are ROUND: the builder reads
        /// their x/z as a diameter and y as a width (P1PartDef.size's own
        /// comment). The viewer needs to know which primitive to raise, and
        /// guessing from the id prefix is exactly the string-matching this
        /// project bans elsewhere — so it is derived from the shape instead:
        /// a part whose x and z match and whose y differs is a disc.</summary>
        static bool IsDisc(P1PartDef d)
        {
            bool square = Mathf.Abs(d.size.x - d.size.z) < 0.001f;
            return square && Mathf.Abs(d.size.y - d.size.x) > 0.001f
                   && (d.id.Contains("wheel") || d.id.Contains("spinner")
                       || d.id.Contains("saw") || d.id.Contains("spindle"));
        }

        /// <summary>CAPTURE THE GAME'S OWN PART VISUAL, not an approximation.
        ///
        /// A part in this game is NOT a box. PartVisualFactory.BuildPart
        /// assembles each one out of several primitives — a body, end caps,
        /// rows of bolt cylinders, hazard ribs, socket dots, glowing accents —
        /// across a dozen fixed materials (DarkSteel, HubMetal, SocketDark,
        /// CoreYellow, AmberGlow, Copper). That layered detail is what makes
        /// the machines read as machinery instead of as blocks, and the website
        /// was drawing ONE flat-coloured box per part, throwing all of it away.
        ///
        /// Rather than reimplement the recipes in JavaScript — a second source
        /// of truth that would drift the moment a part is restyled — this RUNS
        /// BuildPart and records what it produced. Whatever the game draws is
        /// what the site draws.
        ///
        /// ⚠ WHICH SUB-PARTS FOLLOW THE PLAYER'S MATERIAL? The body does; the
        /// functional accents deliberately do not ("those are what say 'this is
        /// the sharp end'", BuildPart's own note). Rather than encode that
        /// judgement here, build each part TWICE under two wildly different
        /// fallback colours: anything whose colour moved is body-tinted and is
        /// emitted as "body", anything that stayed is emitted as its literal
        /// colour. The classification is measured, not guessed.
        ///
        /// ⚠ MUST RUN IN PLAY MODE. Deco() calls Object.Destroy on the
        /// primitive's collider, which is illegal from edit mode.</summary>
        /// <summary>⚠ SIX OF THESE PARTS ARE DRAWN DIFFERENTLY DEPENDING ON
        /// WHICH WAY THEY WERE BOLTED, AND THE ROTATION IS NOT IN THE
        /// TRANSFORM. For pivot/spindle/ram/blade/wedge/hook the builder does
        /// NOT rotate the GameObject — it passes
        /// `VisualId(def, part.DriveAxis())`, i.e. the id with an axis code
        /// appended ("pivotYP", "wedgeXN"), and BuildPart's ParseAxis turns
        /// that suffix back into the direction the part points. So the
        /// orientation lives in the VISUAL, and a consumer that only has the
        /// recorded transform cannot recover it.
        ///
        /// Exporting the bare id gave every one of them its DEFAULT axis, so
        /// the website drew a robot whose weapons all pointed the fallback way.
        /// It shows up worst on the machine with the most varied loadout —
        /// owen spotted it on LIGHT (Hammer: pivot, spindle, blade and two
        /// wedges) before any other class.
        ///
        /// Spike and spinner are Weapon category too and are NOT here on
        /// purpose: the builder gives those a real transform rotation
        /// (FromToRotation on the mount normal) through their own branches, so
        /// the recorded transform already carries their orientation.</summary>
        static readonly string[] AXIS_CODES = { "XP", "XN", "YP", "YN", "ZP", "ZN" };
        static bool NeedsAxisVariants(P1PartDef d)
        {
            return d.id == "pivot" || d.id == "spindle" || d.id == "ram"
                || d.id == "blade" || d.id == "wedge" || d.id == "hook";
        }

        /// <summary>⚠ YAW IS NOT A ROTATION, IT IS A DIFFERENT SHAPE. A
        /// transcription of PlacedPart.Half(): the builder passes
        /// `part.Half() * 2` as the size, so a yawed part reaches BuildPart
        /// with its components SWAPPED and the visual is regenerated around
        /// the new long axis. Nothing is rotated, which is why the yaw cannot
        /// be recovered from the recorded transform, and why each yaw is a
        /// swap about a DIFFERENT axis rather than one turn about Y:
        ///   90  -> (z,y,x)  long axis to X
        ///   180 -> (x,z,y)  stood up, y/z
        ///   270 -> (y,x,z)  stood up, x/y
        /// spike, Mobility and spinner* never reach this — Half() returns for
        /// them earlier through their own axis-aware branches.</summary>
        static Vector3 YawSize(Vector3 s, int yaw)
        {
            if (yaw == 90) return new Vector3(s.z, s.y, s.x);
            if (yaw == 180) return new Vector3(s.x, s.z, s.y);
            if (yaw == 270) return new Vector3(s.y, s.x, s.z);
            return s;
        }

        static readonly int[] YAWS = { 90, 180, 270 };

        /// <summary>Every drawing of this part that is not the default one,
        /// keyed "<axis><yaw>" — "XN", "y90", "XNy90". Variants identical to
        /// the base are DROPPED, which is most of them: a cube is the same
        /// shape at every yaw, a beam with x==y is unchanged at 270, and a
        /// weapon on its default axis is unchanged too. Without that the table
        /// is 24 recipes per weapon part and several hundred KB on a page that
        /// has to open on a phone.</summary>
        static string VisVariants(P1PartDef d, string baseRecipe)
        {
            bool axisPart = NeedsAxisVariants(d);
            bool yawPart = d.id != "spike" && !d.id.StartsWith("spinner")
                           && d.category != P1Category.Mobility;
            var map = new List<string>();
            var codes = new List<string>();
            if (axisPart) codes.AddRange(AXIS_CODES); else codes.Add(null);
            foreach (var code in codes)
            {
                string vid = code == null ? null : d.id + code;
                foreach (var yaw in new[] { 0, 90, 180, 270 })
                {
                    if (yaw != 0 && !yawPart) continue;
                    if (code == null && yaw == 0) continue;      // that is the base
                    string rec = VisRecipe(d, vid, YawSize(d.size, yaw));
                    if (rec == baseRecipe) continue;             // nothing new to say
                    string key = (code ?? "") + (yaw == 0 ? "" : "y" + yaw);
                    if (key.Length == 0) continue;
                    map.Add("\"" + key + "\":" + rec);
                }
            }
            if (map.Count == 0) return "";
            return ",\"visVar\":{" + string.Join(",", map) + "}";
        }

        static string VisRecipe(P1PartDef d) { return VisRecipe(d, null, d.size); }

        static string VisRecipe(P1PartDef d, string idOverride, Vector3 size)
        {
            var a = CaptureVis(d, new Color(1f, 0f, 0f), idOverride, size);
            var b = CaptureVis(d, new Color(0f, 1f, 0f), idOverride, size);
            if (a.Count == 0) return "[]";

            var sb = new StringBuilder("[");
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                bool tinted = i < b.Count && b[i].color != p.color;
                if (i > 0) sb.Append(',');
                sb.Append("{\"t\":\"").Append(p.prim).Append("\"")
                  .Append(",\"p\":[").Append(F(p.pos.x)).Append(',').Append(F(p.pos.y)).Append(',').Append(F(p.pos.z)).Append(']')
                  .Append(",\"r\":[").Append(F(p.euler.x)).Append(',').Append(F(p.euler.y)).Append(',').Append(F(p.euler.z)).Append(']')
                  .Append(",\"s\":[").Append(F(p.scale.x)).Append(',').Append(F(p.scale.y)).Append(',').Append(F(p.scale.z)).Append(']')
                  .Append(",\"c\":").Append(tinted ? "\"body\"" : "\"" + Hex(p.color) + "\"")
                  .Append(",\"me\":").Append(F(p.metallic))
                  .Append(",\"sm\":").Append(F(p.smoothness));
                if (p.emissive) sb.Append(",\"glow\":true");
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        struct VisPiece
        {
            public string prim;
            public Vector3 pos, euler, scale;
            public Color color;
            public float metallic, smoothness;
            public bool emissive;
        }

        static List<VisPiece> CaptureVis(P1PartDef d, Color fallback, string idOverride, Vector3 size)
        {
            var list = new List<VisPiece>();
            var host = new GameObject("vis_probe_" + d.id);
            try
            {
                var mat = MatDB.Get(d.matName);
                // ⚠ BuildPart IS NOT THE ONLY ENTRY POINT, AND ASSUMING IT WAS
                // SHIPPED SQUARE WHEELS. BuildPart has no Mobility branch at
                // all — a wheel falls through to its final `else`, which draws
                // a CUBE — because the builder never asks it for one: it calls
                // BuildWheel directly (BuilderManager.cs:619) with the radius
                // and width unpacked from the part size. So the export was a
                // faithful recording of a path the game does not use for these
                // parts, and the website drew every wheel as a rubber brick.
                // Dispatch on CATEGORY exactly as the builder does — matching
                // on an id prefix is the thing this project bans, and it is
                // also what would break the moment a wheel is renamed.
                // There are FOUR of these in BuilderManager.AddPart, not one,
                // and the other three are just as real as the wheel: a spinner
                // and a spike each have their own builder entry too. BuildPart
                // does answer for those two — but with the ARENA variants
                // (SpinnerArena/SpikeArena), which are a different drawing of
                // the same part. Mirror the builder's dispatch, in its order.
                if (d.category == P1Category.Mobility)
                    PartVisualFactory.BuildWheel(host.transform, d.size.x * 0.5f, d.size.y, -1);
                else if (d.id.StartsWith("spinner"))
                    PartVisualFactory.BuildSpinner(host.transform, d.size.x * 0.5f, d.size.y, -1);
                else if (d.id == "spike")
                    PartVisualFactory.BuildSpike(host.transform, d.size);
                else
                    PartVisualFactory.BuildPart(idOverride ?? d.id, host.transform, size, false, fallback,
                                                mat != null ? mat.metallic : 0.8f,
                                                mat != null ? mat.smoothness : 0.6f);
                foreach (var r in host.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    string prim = mf != null && mf.sharedMesh != null
                        ? mf.sharedMesh.name.ToLowerInvariant() : "cube";
                    if (prim.Contains("cylinder")) prim = "cyl";
                    else if (prim.Contains("sphere")) prim = "sph";
                    else if (prim.Contains("capsule")) prim = "cap";
                    else prim = "cube";
                    var t = r.transform;
                    var m = r.sharedMaterial;
                    // Positions are relative to the PART root, not the piece's
                    // immediate parent — Beam() nests details under a child to
                    // handle yaw, and a nested local position is meaningless to
                    // a renderer that instantiates the list flat.
                    list.Add(new VisPiece
                    {
                        prim = prim,
                        pos = host.transform.InverseTransformPoint(t.position),
                        euler = (Quaternion.Inverse(host.transform.rotation) * t.rotation).eulerAngles,
                        scale = t.lossyScale,
                        color = m != null ? m.color : fallback,
                        metallic = m != null && m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0.5f,
                        smoothness = m != null && m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.5f,
                        emissive = m != null && m.IsKeywordEnabled("_EMISSION"),
                    });
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[PartTable] " + d.id + " visual capture failed: " + e.Message);
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(host); else Object.DestroyImmediate(host);
            }
            return list;
        }

        public static void Export()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"generated\": \"by ExportPartTable from P1PartDef.Palette() + MatDB\",\n");

            sb.Append("  \"parts\": {\n");
            var defs = P1PartDef.Palette();
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                string baseRec = VisRecipe(d);
                sb.Append("    \"").Append(d.id).Append("\": {")
                  .Append("\"sx\":").Append(F(d.size.x))
                  .Append(",\"sy\":").Append(F(d.size.y))
                  .Append(",\"sz\":").Append(F(d.size.z))
                  .Append(",\"cat\":\"").Append(d.category).Append("\"")
                  .Append(",\"disc\":").Append(IsDisc(d) ? "true" : "false")
                  .Append(",\"edge\":").Append(F(d.edgeHardness))
                  .Append(",\"label\":\"").Append(d.label.Replace("\"", "'")).Append("\"")
                  .Append(",\"vis\":").Append(baseRec)
                  .Append(VisVariants(d, baseRec))
                  .Append("}");
                if (i < defs.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  },\n");

            sb.Append("  \"materials\": {\n");
            // ⚠ MatDB.Order IS THE PLAYER-SELECTABLE LIST, NOT EVERY MATERIAL.
            // Materials pinned to a part (P1PartDef.materialChoice == false)
            // never appear in it — RUBBER is the live example: every wheel in
            // the game is Rubber, Order does not mention it, so the first
            // export omitted it and the website rendered all six of a
            // champion's wheels through its unknown-material fallback, as pale
            // blue-grey semi-metal drums. Export the palette PLUS whatever the
            // parts actually reference, or the table silently under-describes
            // the game it is generated from.
            var mats = new List<string>(MatDB.Order);
            foreach (var d in P1PartDef.Palette())
                if (!string.IsNullOrEmpty(d.matName) && !mats.Contains(d.matName)
                    && MatDB.Has(d.matName))
                    mats.Add(d.matName);
            for (int i = 0; i < mats.Count; i++)
            {
                var m = MatDB.Get(mats[i]);
                sb.Append("    \"").Append(mats[i]).Append("\": {")
                  .Append("\"color\":\"").Append(Hex(m.color)).Append("\"")
                  .Append(",\"metallic\":").Append(F(m.metallic))
                  .Append(",\"smoothness\":").Append(F(m.smoothness))
                  .Append("}");
                if (i < mats.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  }\n}\n");

            string path = Path.Combine(Directory.GetCurrentDirectory(), OUT);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[PartTable] wrote " + path + " — " + defs.Length
                      + " parts, " + mats.Count + " materials");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
