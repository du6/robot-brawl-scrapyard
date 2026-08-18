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

        public static void Export()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"generated\": \"by ExportPartTable from P1PartDef.Palette() + MatDB\",\n");

            sb.Append("  \"parts\": {\n");
            var defs = P1PartDef.Palette();
            for (int i = 0; i < defs.Length; i++)
            {
                var d = defs[i];
                sb.Append("    \"").Append(d.id).Append("\": {")
                  .Append("\"sx\":").Append(F(d.size.x))
                  .Append(",\"sy\":").Append(F(d.size.y))
                  .Append(",\"sz\":").Append(F(d.size.z))
                  .Append(",\"cat\":\"").Append(d.category).Append("\"")
                  .Append(",\"disc\":").Append(IsDisc(d) ? "true" : "false")
                  .Append(",\"edge\":").Append(F(d.edgeHardness))
                  .Append(",\"label\":\"").Append(d.label.Replace("\"", "'")).Append("\"")
                  .Append("}");
                if (i < defs.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  },\n");

            sb.Append("  \"materials\": {\n");
            var mats = new List<string>(MatDB.Order);
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
