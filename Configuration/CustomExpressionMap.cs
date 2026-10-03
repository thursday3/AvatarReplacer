using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AvatarReplacer
{
    /// <summary>
    /// One row of the expression map: a giantess expression name and the VRM
    /// blendshape that should be driven by it.
    ///   vrmBlendShape == ""  -> follow the built-in default mapping
    ///   vrmBlendShape != ""  -> override it with this blendshape
    /// To find blendshape names on your model, open the .vrm in Blender
    /// (Object Data Properties -> Shape Keys) and copy the names you want.
    /// </summary>
    [Serializable]
    public class CustomExpressionEntry
    {
        public string gameExpression = "";
        public string vrmBlendShape = "";
    }

    /// <summary>
    /// Minimal per-avatar expression map.
    ///
    ///   BepInEx/plugins/AvatarReplacer/config/expression_map.json            (shared, any avatar)
    ///   BepInEx/plugins/AvatarReplacer/config/expression_map.&lt;avatarname&gt;.json (per-VRM only)
    ///
    /// The file is simply the target VRM name plus one line per giantess
    /// expression, all empty by default:
    ///
    /// {
    ///     "targetVrm": "avatar1",
    ///     "mappings": [
    ///         { "gameExpression": "happy eyes", "vrmBlendShape": "" },
    ///         ...
    ///     ]
    /// }
    ///
    /// Empty vrmBlendShape = default. Filled vrmBlendShape = override.
    /// Filling any row turns the map on; the per-VRM file then replaces the
    /// shared map for that avatar (so avatars with different blendshape names
    /// never conflict). "targetVrm" guards the per-VRM file against being used
    /// for a different avatar; the shared map ignores it.
    /// </summary>
    [Serializable]
    public class CustomExpressionMap
    {
        public string targetVrm = "";
        public List<CustomExpressionEntry> mappings = new List<CustomExpressionEntry>();

        // ---------------------------------------------------------------------------
        // Ground-truth expression names (Lethal Fantasy, GiantessExpressionManager):
        // eye/mouth display names from eyeExpressionEnums/mouthExpressionEnums/
        // eyeDialogueExpressions dictionaries, plus the special expressions accessed
        // by GetExpression("...") in Start()/LateUpdate.
        // Do not rename these keys; they must match the game exactly.
        // ---------------------------------------------------------------------------
        private static readonly string[] TemplateKeys = new[]
        {
            // Eyes
            "happy eyes", "serious eyes", "sad eyes", "angry eyes", "hurt eyes",
            "smug eyes", "shock eyes", "vore eyes", "shy eyes", "embarrased eyes",
            "laugh eyes", "cry eyes", "frown eyes", "screaming eyes", "mad eyes",
            "neutral tilt eyes",
            // Mouths
            "happy mouth", "serious mouth", "sad mouth", "angry mouth", "hurt mouth",
            "smug mouth", "shock mouth", "smile mouth", "a mouth", "i mouth",
            "u mouth", "e mouth", "o mouth", "vore mouth", "shy mouth",
            "embarrased mouth", "laugh mouth", "cry mouth", "frown mouth",
            "screaming mouth", "mad mouth", "neutral tilt mouth",
            // Special expressions
            "Blink", "tilt", "anger small", "anger large", "tears sad", "tears laugh"
        };

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
        /// Loads the map for a specific avatar. Resolution order:
        ///  1. A populated per-VRM file expression_map.&lt;avatar&gt;.json.
        ///  2. The shared expression_map.json when the per-VRM file is absent,
        ///     invalid, or contains no filled mappings.
        /// A populated per-VRM map is selected as a whole; its empty rows do
        /// not inherit individual rows from the shared map.
        /// Returns null when the feature should be OFF (missing file -> template
        /// written first; or no filled rows).
        /// </summary>
        public static CustomExpressionMap LoadForAvatar(string configDir, string vrmPath)
        {
            string perVrm = GetPerVrmPath(configDir, vrmPath);
            if (perVrm != null)
            {
                if (!File.Exists(perVrm))
                {
                    // First use of this avatar: create the template with targetVrm
                    // pre-filled and one empty row per giantess expression.
                    CreateTemplate(perVrm, Path.GetFileNameWithoutExtension(vrmPath));
                }
                else
                {
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
            }

            string shared = GetPath(configDir);
            if (string.IsNullOrEmpty(shared)) return null;
            if (!File.Exists(shared))
            {
                CreateTemplate(shared, null);
                return null;
            }
            return LoadFromFile(shared);
        }

        private static CustomExpressionMap LoadFromFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var map = JsonUtility.FromJson<CustomExpressionMap>(json);
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

        public static void CreateTemplate(string path, string targetVrm)
        {
            try
            {
                if (File.Exists(path)) return; // NEVER overwrite user edits
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var template = new CustomExpressionMap { targetVrm = targetVrm ?? "" };
                foreach (var key in TemplateKeys)
                {
                    template.mappings.Add(new CustomExpressionEntry
                    {
                        gameExpression = key,
                        vrmBlendShape = ""   // empty = follow the default mapping
                    });
                }

                File.WriteAllText(path, JsonUtility.ToJson(template, true));
                ModLog.Info($"Created expression map template at: {path} (empty vrmBlendShape = default; fill one to override it).");
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to create expression map template: {e.Message}");
            }
        }

        /// <summary>
        /// Creates an empty template (one row per giantess expression, all
        /// "vrmBlendShape": "") for every .vrm in the given folder. Existing
        /// files are never touched, so a new model dropped into the folder only
        /// gains its own file and user-edited maps keep their values.
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
                    if (string.IsNullOrEmpty(perVrm) || File.Exists(perVrm))
                        continue; // exists: keep the user's edits untouched
                    CreateTemplate(perVrm, Path.GetFileNameWithoutExtension(vrm));
                    if (File.Exists(perVrm)) created++;
                }
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to generate expression map templates: {e.Message}");
            }

            if (created > 0)
                ModLog.Info($"Generated {created} expression map template(s) in {configDir} (existing files untouched).");
            return created;
        }
    }
}
