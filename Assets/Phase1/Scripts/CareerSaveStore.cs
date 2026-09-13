using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RobotBrawl.Phase0
{
    /// <summary>Path-explicit persistence, so corruption/recovery tests never
    /// need to read or replace the player's save. Old plain JSON still loads.
    /// A complete staged file and a previous valid backup cover interrupted
    /// writes, including WebGL's filesystem where File.Replace is unavailable.</summary>
    internal static class CareerSaveStore
    {
        internal sealed class LoadResult
        {
            public CareerData data;
            public bool blocked;
            public string notice = "";
        }

        internal static LoadResult Read(string path)
        {
            bool anyFile = false;
            string[] candidates = { path, path + ".tmp", path + ".bak" };
            foreach (string candidate in candidates)
            {
                CareerData data;
                bool exists;
                if (TryRead(candidate, out data, out exists))
                    return new LoadResult { data = data, notice = candidate == path ? "" :
                        "Recovered your progress from a saved copy. Your original save has been preserved." };
                anyFile |= exists;
            }
            return anyFile
                ? new LoadResult { blocked = true, notice = "Your save could not be recovered. Original files are preserved; this session will not save. Reload after restoring a backup." }
                : new LoadResult();
        }

        static bool TryRead(string path, out CareerData data, out bool exists)
        {
            data = null;
            exists = true;
            try { return TryParse(File.ReadAllText(path), out data); }
            catch (FileNotFoundException) { exists = false; return false; }
            catch (DirectoryNotFoundException) { exists = false; return false; }
            catch (Exception) { return false; }
        }

        internal static bool TryParse(string json, out CareerData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            string trimmed = json.Trim();
            if (!trimmed.StartsWith("{") || !trimmed.EndsWith("}")) return false;
            // JsonUtility accepts {} as a new object. Require the old core
            // schema so truncated/foreign valid JSON cannot erase a career.
            if (!HasCoreFields(trimmed)) return false;
            try
            {
                var d = JsonUtility.FromJson<CareerData>(trimmed);
                if (d == null || d.scrap < 0 || d.inventory == null || d.stable == null) return false;
                foreach (var item in d.inventory)
                    if (item == null || item.count < 0 || string.IsNullOrEmpty(item.partId) || string.IsNullOrEmpty(item.mat)) return false;
                foreach (var robot in d.stable) if (robot == null) return false;
                // Additive fields are allowed to be absent in old saves; an
                // explicit null is repaired before any migration iterates it.
                if (d.blueprints == null) d.blueprints = new List<CareerBlueprint>();
                if (d.doneContests == null) d.doneContests = new List<string>();
                if (d.autoDoneContests == null) d.autoDoneContests = new List<string>();
                if (d.txns == null) d.txns = new List<CareerTxn>();
                if (d.pendingClaims == null) d.pendingClaims = new List<CareerClaim>();
                if (d.pendingPurchases == null) d.pendingPurchases = new List<CareerPurchase>();
                if (d.medals == null) d.medals = new List<CareerMedal>();
                if (d.programs == null) d.programs = new List<SavedProgram>();
                if (d.pendingRewards == null) d.pendingRewards = new List<string>();
                if (d.worldOpened == null) d.worldOpened = new List<string>();
                if (d.yardBeaten == null) d.yardBeaten = new List<string>();
                if (d.scrapCurve == null) d.scrapCurve = new List<int>();
                if (!Finite(d.expeditionX) || !Finite(d.expeditionY) || !Finite(d.expeditionZ) || !Finite(d.expeditionYaw))
                {
                    d.expeditionHasPosition = false;
                    d.expeditionX = d.expeditionY = d.expeditionZ = d.expeditionYaw = 0f;
                }
                data = d;
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        static bool HasCoreFields(string json)
        {
            // Look only at root object keys. A snapshot/string or unrelated
            // nested object mentioning "inventory" must not pass validation.
            var containers = new Stack<char>();
            var key = new StringBuilder();
            bool inString = false, escape = false, rootKey = false, expectKey = false;
            int core = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escape) { escape = false; if (rootKey) key.Append(c); continue; }
                    if (c == '\\') { escape = true; continue; }
                    if (c != '"') { if (rootKey) key.Append(c); continue; }
                    inString = false;
                    if (rootKey)
                    {
                        string field = key.ToString();
                        int valueAt = i + 1;
                        while (valueAt < json.Length && char.IsWhiteSpace(json[valueAt])) valueAt++;
                        if (valueAt >= json.Length || json[valueAt++] != ':') return false;
                        while (valueAt < json.Length && char.IsWhiteSpace(json[valueAt])) valueAt++;
                        if (valueAt >= json.Length) return false;
                        if ((field == "inventory" || field == "stable") && json[valueAt] != '[') return false;
                        if (field == "scrap" && json[valueAt] != '-' && !char.IsDigit(json[valueAt])) return false;
                        if (field == "scrap") core |= 1;
                        if (field == "inventory") core |= 2;
                        if (field == "stable") core |= 4;
                        expectKey = false;
                    }
                    continue;
                }
                if (c == '"')
                {
                    inString = true; key.Clear();
                    rootKey = containers.Count == 1 && expectKey;
                }
                else if (c == '{' || c == '[')
                {
                    if (containers.Count == 0 && i != 0) return false;
                    containers.Push(c);
                    if (containers.Count == 1) expectKey = true;
                }
                else if (c == '}' || c == ']')
                {
                    if (containers.Count == 0 || containers.Pop() != (c == '}' ? '{' : '[')) return false;
                    if (containers.Count == 0 && i != json.Length - 1) return false;
                }
                else if (c == ',' && containers.Count == 1) expectKey = true;
            }
            return !inString && containers.Count == 0 && core == 7;
        }

        internal static bool Write(string path, CareerData data, out string error)
        {
            error = "";
            try
            {
                string json = JsonUtility.ToJson(data);
                CareerData validated;
                if (!TryParse(json, out validated)) throw new InvalidDataException("Refusing to write invalid career data.");
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string staged = path + ".tmp";
                bool exists;
                bool previousValid = TryRead(path, out validated, out exists);
                // After interrupted rename recovery, .tmp may be the only
                // complete save. Preserve it before opening it for overwrite;
                // if this backup fails, leave that recovery copy untouched.
                if (!previousValid && TryRead(staged, out validated, out exists))
                    File.Copy(staged, path + ".bak", true);
                File.WriteAllText(staged, json);
                if (!TryRead(staged, out validated, out exists)) throw new IOException("Staged save failed validation.");
                if (!File.Exists(path)) { File.Move(staged, path); return true; }

                if (!previousValid)
                    File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, false);
                string backup = previousValid ? path + ".bak" : null;
#if UNITY_WEBGL && !UNITY_EDITOR
                ReplacePortable(staged, path, backup);
#else
                try { File.Replace(staged, path, backup); }
                catch (PlatformNotSupportedException) { ReplacePortable(staged, path, backup); }
                catch (NotSupportedException) { ReplacePortable(staged, path, backup); }
#endif
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        static void ReplacePortable(string staged, string path, string backup)
        {
            // Every interruption leaves a valid primary, staged file or backup.
            // Unity's template enables autoSyncPersistentDataPath for IDBFS.
            if (backup != null) File.Copy(path, backup, true);
            File.Delete(path);
            File.Move(staged, path);
        }
    }
}
