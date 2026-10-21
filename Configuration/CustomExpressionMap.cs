using System;
using System.Collections.Generic;
using System.IO;

namespace AvatarReplacer
{
    /// <summary>
    /// One row of the expression map — a fully visible mapping line:
    ///
    ///   gameExpression : the giantess expression (game side)
    ///   defaultVrm     : what the mod uses when you don't override (reference)
    ///   vrmBlendShape  : what is actually driven; empty = use defaultVrm
    ///
    /// To override a row, put a blendshape name in vrmBlendShape. Empty rows
    /// and never-listed expressions alike are shown so the player can see —
    /// and change — the complete behaviour of the mod on their avatar.
    /// </summary>
    [Serializable]
    public class CustomExpressionEntry
    {
        public string gameExpression = "";
        public string defaultVrm = "";
        public string vrmBlendShape = "";
    }

    /// <summary>
    /// Per-avatar expression map, readable as a visible map of how the mod
    /// drives the VRM:
    ///
    ///   BepInEx/plugins/AvatarReplacer/config/expression_map.&lt;avatarname&gt;.json
    ///
    /// A generated file lists EVERY game expression the mod can drive —
    /// including empty and unused ones — with its default VRM preset filled
    /// in, so editing is simply changing the "vrmBlendShape" column.
    ///
    /// Templates are regenerated ONLY when the file is missing or is an empty
    /// skeleton (zero mapping rows — the older generator's broken output).
    /// A file that contains rows is never rewritten, byte for byte.
    /// gameExpression keys the mod can't find on this avatar are still listed
    /// (bad rows are logged and skipped at load; they never break the map).
    /// </summary>
    [Serializable]
    public class CustomExpressionMap
    {
        public string targetVrm = "";
        public List<CustomExpressionEntry> mappings = new List<CustomExpressionEntry>();

        /// <summary>The map is active when at least one row names a blendshape.</summary>
        public bool HasActiveMappings
        {
            get
            {
                if (mappings == null) return false;
                foreach (var m in mappings)
                {
                    if (m != null && !string.IsNullOrWhiteSpace(m.vrmBlendShape))
                        return true;
                }
                return false;
            }
        }

        public IEnumerable<CustomExpressionEntry> ActiveMappings()
        {
            if (mappings == null) yield break;
            foreach (var m in mappings)
            {
                if (m != null && !string.IsNullOrWhiteSpace(m.vrmBlendShape))
                    yield return m;
            }
        }

        public static string GetPath(string configDir)
        {
            if (string.IsNullOrEmpty(configDir)) return null;
            return Path.Combine(configDir, "expression_map.json");
        }

        /// <summary>
        /// Per-VRM map path: expression_map.&lt;avatarName&gt;.json
        /// (avatarName = VRM file name without extension, sanitized).
        /// </summary>
        public static string GetPerVrmPath(string configDir, string vrmPath)
        {
            if (string.IsNullOrEmpty(configDir) || string.IsNullOrEmpty(vrmPath)) return null;
            string name = Path.GetFileNameWithoutExtension(vrmPath);
            if (string.IsNullOrWhiteSpace(name)) return null;

            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool ok = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
                sb.Append(ok ? c : '_');
            }
            return Path.Combine(configDir, $"expression_map.{sb}.json");
        }

        /// <summary>
        /// True when this map is intended for the given avatar:
        ///  - empty targetVrm -> shared map, applies to any avatar;
        ///  - otherwise compare against the VRM file name (with and without
        ///    extension), case-insensitive.
        /// </summary>
        public bool MatchesAvatar(string vrmPath)
        {
            if (string.IsNullOrWhiteSpace(targetVrm)) return true;
            if (string.IsNullOrEmpty(vrmPath)) return false;

            string file = Path.GetFileName(vrmPath);
            string fileNoExt = Path.GetFileNameWithoutExtension(vrmPath);
            string target = targetVrm.Trim();

            return string.Equals(target, file, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(target, fileNoExt, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Serialized by hand instead of via UnityEngine.JsonUtility: in this
        /// game's runtime JsonUtility silently drops fields whose types live
        /// in this plugin assembly — in-game it wrote { "targetVrm": "x" }
        /// skeletons with the whole mappings list missing. This writer emits
        /// exactly the documented format (4-space indent, one row object per
        /// expression, in table order).
        /// </summary>
        public string ToJsonString()
        {
            var sb = new System.Text.StringBuilder(64 + (mappings?.Count ?? 0) * 110);
            sb.Append("{\n    \"targetVrm\": ").Append(SimpleJson.Quote(targetVrm ?? "")).Append(",\n");
            sb.Append("    \"mappings\": [");

            bool first = true;
            if (mappings != null)
            {
                foreach (var m in mappings)
                {
                    if (m == null) continue;
                    sb.Append(first ? "\n" : ",\n");
                    first = false;
                    sb.Append("        {\n");
                    sb.Append("            \"gameExpression\": ").Append(SimpleJson.Quote(m.gameExpression)).Append(",\n");
                    sb.Append("            \"defaultVrm\": ").Append(SimpleJson.Quote(m.defaultVrm)).Append(",\n");
                    sb.Append("            \"vrmBlendShape\": ").Append(SimpleJson.Quote(m.vrmBlendShape)).Append('\n');
                    sb.Append("        }");
                }
            }
            sb.Append(first ? "]\n}" : "\n    ]\n}");
            return sb.ToString();
        }

        /// <summary>
        /// Tolerant reader for hand-edited files: // and /* */ comments,
        /// trailing commas and unknown keys are accepted; malformed JSON
        /// throws (callers then leave the file untouched rather than
        /// destroying the player's edits).
        /// </summary>
        public static CustomExpressionMap FromJsonString(string json)
        {
            var root = SimpleJson.ParseObject(json);
            var map = new CustomExpressionMap
            {
                targetVrm = SimpleJson.GetString(root, "targetVrm") ?? ""
            };

            var rows = SimpleJson.GetArray(root, "mappings");
            if (rows != null)
            {
                foreach (var item in rows)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null) continue;
                    map.mappings.Add(new CustomExpressionEntry
                    {
                        gameExpression = SimpleJson.GetString(row, "gameExpression") ?? "",
                        defaultVrm = SimpleJson.GetString(row, "defaultVrm") ?? "",
                        vrmBlendShape = SimpleJson.GetString(row, "vrmBlendShape") ?? ""
                    });
                }
            }
            return map;
        }

        /// <summary>
        /// Loads the map for a specific avatar, upgrading stale files on the
        /// way. Resolution order:
        ///  1. per-VRM file expression_map.&lt;avatar&gt;.json (if present and usable)
        ///  2. shared expression_map.json
        /// Upgrades performed before the decision (both no-clobber for files
        /// with real content):
        ///  - a zero-row skeleton is regenerated as a full visible template;
        ///  - game expressions missing from the file are appended as new rows;
        ///  - legacy rows without defaultVrm get it filled in.
        /// Returns null when the feature should be OFF (no filled rows).
        /// </summary>
        public static CustomExpressionMap LoadForAvatar(string configDir, string vrmPath)
        {
            string perVrm = GetPerVrmPath(configDir, vrmPath);
            if (perVrm != null)
            {
                if (File.Exists(perVrm))
                {
                    if (UpgradeFileInPlace(perVrm, Path.GetFileNameWithoutExtension(vrmPath)))
                    {
                        ModLog.Info($"Expression map {Path.GetFileName(perVrm)} was auto-upgraded (visible defaults / appended rows) — check 'defaultVrm' and your overrides are intact.");
                    }

                    var map = LoadFromFile(perVrm);
                    if (map != null)
                    {
                        if (!map.MatchesAvatar(vrmPath))
                        {
                            ModLog.Info($"Custom map {Path.GetFileName(perVrm)} targets '{map.targetVrm}', not '{Path.GetFileName(vrmPath)}'; ignored.");
                            return null;
                        }
                        ModLog.Info($"Using per-VRM expression map: {Path.GetFileName(perVrm)}");
                        return map;
                    }
                    // Empty template: fall through to the shared map.
                }
                else
                {
                    // First use of this avatar: full visible template with
                    // targetVrm pre-filled and every expression listed.
                    CreateTemplate(perVrm, Path.GetFileNameWithoutExtension(vrmPath));
                }
            }

            string shared = GetPath(configDir);
            if (string.IsNullOrEmpty(shared)) return null;
            if (File.Exists(shared))
            {
                UpgradeFileInPlace(shared, null);
                return LoadFromFile(shared);
            }

            CreateTemplate(shared, null);
            return null;
        }

        /// <summary>
        /// In-place freshness pass over one map file. NEVER rewrites a file
        /// that already lists every known game expression with defaults — i.e.
        /// any file with real user content. Handles exactly these shapes:
        ///  - missing / unreadable: nothing written by this method;
        ///  - zero-row skeleton (old generator output): regenerated in full;
        ///  - valid file missing some game expressions: rows appended (and
        ///    legacy rows get their default column filled from the table);
        ///  - valid file whose expression list is complete: untouched.
        /// Returns true when the file was changed on disk.
        /// </summary>
        public static bool UpgradeFileInPlace(string path, string targetVrm)
        {
            try
            {
                bool exists = File.Exists(path);
                if (!exists) return false; // creation handled by CreateTemplate

                CustomExpressionMap map = null;
                bool parsed = false;
                string parseError = null;
                try
                {
                    map = FromJsonString(File.ReadAllText(path));
                    parsed = map != null;
                }
                catch (Exception pe)
                {
                    parseError = pe.Message;
                    parsed = false;
                }

                if (!parsed)
                {
                    // The file exists but is not readable JSON. It may be a
                    // hand-edited map with a typo — NEVER destroy user edits:
                    // leave the bytes alone and say what to fix. (Raw skeletons
                    // like { "targetVrm": "x" } parse fine and hit the
                    // zero-row path below instead.)
                    ModLog.Info($"[WARN] Expression map {Path.GetFileName(path)} is not valid JSON ({parseError}); file left untouched. Fix the syntax or delete the file to regenerate it.");
                    return false;
                }

                if (map.mappings == null) map.mappings = new List<CustomExpressionEntry>();
                if (map.mappings.Count == 0)
                {
                    // Zero-row skeleton: regenerate completely.
                    CreateTemplate(path, targetVrm, force: true);
                    ModLog.Info($"Expression map {Path.GetFileName(path)} was an empty skeleton; regenerated with the full visible map.");
                    return true;
                }

                // Valid file with rows: append any game expressions the mod
                // knows now that the file doesn't list yet. Keep user bytes
                // untouched otherwise (in particular: do not rewrite the file
                // when nothing is appended).
                var listed = new HashSet<string>();
                bool backfilled = false;
                var defaultsByKey = new Dictionary<string, string>();
                foreach (var def in ExpressionDefaults.All)
                    defaultsByKey[def.GameExpression] = def.DefaultPreset ?? "";

                foreach (var row in map.mappings)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.gameExpression)) continue;
                    listed.Add(row.gameExpression.Trim());
                    // Legacy rows (pre visible-map format) have an empty
                    // reference column; fill it from the table so the file
                    // shows what the mod actually does. Overrides are never
                    // touched by this pass.
                    if (string.IsNullOrEmpty(row.defaultVrm) &&
                        defaultsByKey.TryGetValue(row.gameExpression.Trim(), out var def0) &&
                        !string.IsNullOrEmpty(def0))
                    {
                        row.defaultVrm = def0;
                        backfilled = true;
                    }
                }

                bool appended = false;
                foreach (var def in ExpressionDefaults.All)
                {
                    if (!listed.Contains(def.GameExpression))
                    {
                        map.mappings.Add(new CustomExpressionEntry
                        {
                            gameExpression = def.GameExpression,
                            defaultVrm = def.DefaultPreset ?? "",
                            vrmBlendShape = ""
                        });
                        appended = true;
                    }
                }
                if (appended || backfilled)
                {
                    File.WriteAllText(path, map.ToJsonString());
                    ModLog.Info($"Expression map {Path.GetFileName(path)} upgraded: {(appended ? "appended missing rows" : "filled defaults")}; overrides kept.");
                }
                return appended || backfilled;
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to refresh expression map {Path.GetFileName(path)}: {e.Message}");
                return false;
            }
        }

        private static CustomExpressionMap LoadFromFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var map = FromJsonString(json);
                if (map == null)
                {
                    ModLog.Info($"[WARN] {Path.GetFileName(path)} could not be parsed; map ignored.");
                    return null;
                }
                if (map.mappings == null) map.mappings = new List<CustomExpressionEntry>();
                if (!map.HasActiveMappings)
                {
                    ModLog.Info($"Custom map {Path.GetFileName(path)} has no filled mappings; using auto-mapping.");
                    return null;
                }
                return map;
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to load {Path.GetFileName(path)}: {e.Message}; map ignored.");
                return null;
            }
        }

        /// <summary>
        /// Writes a FULL visible template: every expression known to the mod
        /// (including empty and unused ones), each with its default preset
        /// shown in 'defaultVrm' so the player can see what empty means.
        ///
        /// With force:false the file is created only when missing (NEVER
        /// overwrites user edits). With force:true an existing file is
        /// replaced — used exclusively to upgrade zero-row skeletons.
        /// Default mappings come from ExpressionDefaults (single source of
        /// truth used by the runtime too).
        /// </summary>
        public static void CreateTemplate(string path, string targetVrm, bool force = false)
        {
            try
            {
                if (File.Exists(path) && !force) return; // NEVER overwrite user edits

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var template = new CustomExpressionMap { targetVrm = targetVrm ?? "" };
                foreach (var def in ExpressionDefaults.All)
                {
                    template.mappings.Add(new CustomExpressionEntry
                    {
                        gameExpression = def.GameExpression,
                        defaultVrm = def.DefaultPreset ?? "",
                        vrmBlendShape = ""   // empty = use the default shown beside it
                    });
                }

                File.WriteAllText(path, template.ToJsonString());
                ModLog.Info($"Created expression map template at: {path} (defaultVrm = what an empty vrmBlendShape uses; fill vrmBlendShape to override).");
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to create expression map template: {e.Message}");
            }
        }

        /// <summary>
        /// Creates a full visible template for every .vrm in the given folder.
        /// Zero-row skeleton files from older builds are regenerated; files
        /// with real rows are never touched, so a new model only gains its own
        /// file and user-edited maps keep every value.
        /// </summary>
        public static int EnsureTemplatesForVrmFolder(string vrmDirectory)
        {
            if (string.IsNullOrEmpty(vrmDirectory) || !Directory.Exists(vrmDirectory))
                return 0;

            string configDir = AvatarReplacerPlugin.ConfigDirectory;
            if (string.IsNullOrEmpty(configDir)) return 0;

            int created = 0;
            try
            {
                foreach (var vrm in Directory.GetFiles(vrmDirectory, "*.vrm"))
                {
                    string perVrm = GetPerVrmPath(configDir, vrm);
                    if (string.IsNullOrEmpty(perVrm))
                        continue;
                    int before = created;
                    if (File.Exists(perVrm))
                    {
                        // Upgrade stale skeletons in place; no-op for files
                        // with rows. This is what keeps "never clobber the
                        // player's edits" true across even this format change.
                        if (UpgradeFileInPlace(perVrm, Path.GetFileNameWithoutExtension(vrm)))
                            created++;
                    }
                    else
                    {
                        CreateTemplate(perVrm, Path.GetFileNameWithoutExtension(vrm));
                        if (File.Exists(perVrm)) created++;
                    }
                    if (created > before)
                        ModLog.Info($"Expression map for {Path.GetFileName(vrm)}: generated fresh full visible map.");
                }
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to generate expression map templates: {e.Message}");
            }

            if (created > 0)
                ModLog.Info($"Expression maps written/updated: {created} in {configDir} (files with existing rows untouched).");
            return created;
        }
    }
}
