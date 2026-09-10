using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace AvatarReplacer
{
    /// <summary>
    /// Runtime shader catalog. DLLs such as MToon.dll contain the material/importer
    /// code, but the actual Unity Shader assets are separate. A player build must
    /// already contain the shader or load it from an AssetBundle before Shader.Find
    /// can resolve it. This class handles both cases and reports the exact state.
    /// </summary>
    internal static class VrmShaderCatalog
    {
        public static readonly string[] KnownVrmShaders =
        {
            // VRM 0.x / built-in render pipeline
            "VRM/MToon",
            "VRM/UnlitTexture",
            "VRM/UnlitCutout",
            "VRM/UnlitTransparent",
            "VRM/UnlitTransparentZWrite",
            "Standard",
            "UniGLTF/UniUnlit",
            "VRM_USE_GLTFSHADER",

            // VRM 1.0 / MToon10
            "VRM10/MToon10",
            "VRM10/Universal Render Pipeline/MToon10",

            // Known game shader currently used successfully by the mod
            "KiriToon/Giantess"
        };

        private static readonly Dictionary<string, Shader> loaded = new Dictionary<string, Shader>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<AssetBundle> bundles = new List<AssetBundle>();
        private static bool bundleAttempted;
        private static readonly string EmbeddedBundleResourceName = "AvatarReplacer.Resources.liltoonshader";

        public static void Initialize()
        {
            LogRenderPipeline();
            LoadConfiguredBundle();
            Refresh();
        }

        private static bool pipelineLogged;

        /// <summary>
        /// Logs which render pipeline the game is actually running (Built-in vs URP/HDRP).
        /// This matters because VRM/MToon (0.x) has no official URP variant, while
        /// VRM10/Universal Render Pipeline/MToon10 does. Only logged once per session.
        /// </summary>
        public static void LogRenderPipeline()
        {
            if (pipelineLogged) return;
            pipelineLogged = true;

            try
            {
                var pipelineAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                string pipelineName = pipelineAsset != null ? pipelineAsset.GetType().Name : "None (Built-in Render Pipeline)";
                ModLog.Info($"[ShaderCatalog] Active render pipeline: {pipelineName}");

                if (pipelineAsset != null && pipelineName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                    ModLog.Info("[ShaderCatalog] URP detected: VRM/MToon (0.x) has no official URP shader. Prefer VRM10/Universal Render Pipeline/MToon10 (requires a VRM1.0 file).");
            }
            catch (Exception ex)
            {
                ModLog.Debug($"[ShaderCatalog] Render pipeline detection failed: {ex.Message}");
            }
        }

        public static void Refresh()
        {
            // Shader.Find can resolve shaders that are included in the player build.
            foreach (string name in KnownVrmShaders)
            {
                try
                {
                    Shader shader = Shader.Find(name);
                    if (shader != null) loaded[name] = shader;
                }
                catch { }
            }

            // Include shader objects that are loaded but not registered for Shader.Find.
            try
            {
                foreach (Shader shader in Resources.FindObjectsOfTypeAll<Shader>())
                    Add(shader);
            }
            catch (Exception ex)
            {
                ModLog.Debug($"[ShaderCatalog] Shader object scan failed: {ex.Message}");
            }

            // Include every shader currently used by a live renderer. This is how
            // KiriToon/Giantess is discovered in LethalFantasy's player build.
            try
            {
                foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
                {
                    if (renderer == null || renderer.sharedMaterials == null) continue;
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null) Add(material.shader);
                }
            }
            catch (Exception ex)
            {
                ModLog.Debug($"[ShaderCatalog] Renderer shader scan failed: {ex.Message}");
            }
        }

        public static Shader Find(string shaderName)
        {
            if (string.IsNullOrWhiteSpace(shaderName)) return null;
            string wanted = shaderName.Trim();

            if (loaded.TryGetValue(wanted, out Shader cached) && cached != null)
                return cached;

            try
            {
                Shader shader = Shader.Find(wanted);
                if (shader != null)
                {
                    loaded[wanted] = shader;
                    return shader;
                }
            }
            catch { }

            Refresh();
            return loaded.TryGetValue(wanted, out Shader result) ? result : null;
        }

        public static List<Shader> GetLoadedShaders()
        {
            Refresh();
            return loaded.Values
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.name))
                .GroupBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void LogDiagnostics()
        {
            Refresh();
            ModLog.Info("[ShaderCatalog] ===== VRM SHADER AVAILABILITY =====");
            foreach (string name in KnownVrmShaders)
            {
                Shader shader = Find(name);
                ModLog.Info($"[ShaderCatalog] {name} = {(shader != null ? "LOADED" : "NOT LOADED")}");
            }

            var shaders = GetLoadedShaders();
            ModLog.Info($"[ShaderCatalog] Total loaded Shader objects discovered: {shaders.Count}");
            foreach (Shader shader in shaders)
                ModLog.Debug($"[ShaderCatalog] LOADED: {shader.name}");
            ModLog.Info("[ShaderCatalog] =====================================");
        }

        private static void Add(Shader shader)
        {
            if (shader == null || string.IsNullOrWhiteSpace(shader.name)) return;
            if (!loaded.ContainsKey(shader.name)) loaded.Add(shader.name, shader);
        }

        private static void LoadConfiguredBundle()
        {
            if (bundleAttempted) return;
            bundleAttempted = true;

            // Preferred distribution path: the working lilToon AssetBundle is embedded
            // in AvatarReplacer.dll. This removes the external shaders/ directory.
            if (TryLoadEmbeddedBundle())
                return;

            // Legacy/development fallback: allow an external bundle when explicitly configured.
            string configured = AvatarReplacerPlugin.ShaderBundlePathValue;
            if (string.IsNullOrWhiteSpace(configured))
            {
                ModLog.Debug("[ShaderCatalog] No embedded lilToon bundle and no ShaderBundlePath configured. Using shaders already present in the game.");
                return;
            }

            string root = Path.Combine(Paths.PluginPath, "AvatarReplacer");
            string path = configured.Trim();
            if (!Path.IsPathRooted(path)) path = Path.Combine(root, path);
            path = Path.GetFullPath(path);

            if (!File.Exists(path))
            {
                ModLog.Warning($"[ShaderCatalog] Shader bundle not found: {path}");
                return;
            }

            try
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    ModLog.Warning($"[ShaderCatalog] Failed to load shader bundle: {path}");
                    return;
                }

                bundles.Add(bundle); // Keep it loaded for the lifetime of the plugin.
                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                int added = 0;
                foreach (Shader shader in shaders)
                {
                    if (shader == null) continue;
                    int before = loaded.Count;
                    Add(shader);
                    bool isNew = loaded.Count > before;
                    if (isNew) added++;
                    ModLog.Info($"[ShaderCatalog] Bundle shader: '{shader.name}' ({(isNew ? "added" : "duplicate, already known")})");
                }

                if (shaders.Length == 0)
                    ModLog.Warning($"[ShaderCatalog] Bundle '{Path.GetFileName(path)}' loaded but contains zero Shader assets. Check the AssetBundle tag/name used when exporting from Unity.");

                ModLog.Info($"[ShaderCatalog] Loaded shader AssetBundle: {Path.GetFileName(path)} (Shader assets={shaders.Length}, new={added})");
            }
            catch (Exception ex)
            {
                ModLog.Warning($"[ShaderCatalog] Shader bundle load failed: {ex.Message}");
            }
        }

        private static bool TryLoadEmbeddedBundle()
        {
            try
            {
                Assembly assembly = typeof(VrmShaderCatalog).Assembly;
                string[] resources = assembly.GetManifestResourceNames();
                bool present = resources.Any(r => string.Equals(r, EmbeddedBundleResourceName, StringComparison.OrdinalIgnoreCase));

                ModLog.Info($"[ShaderCatalog] Embedded lilToon resource: {(present ? "FOUND" : "NOT FOUND")}");
                if (!present) return false;

                using (Stream stream = assembly.GetManifestResourceStream(EmbeddedBundleResourceName))
                {
                    if (stream == null)
                    {
                        ModLog.Error("[ShaderCatalog] Embedded lilToon resource was listed but could not be opened.");
                        return false;
                    }

                    byte[] bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) throw new EndOfStreamException("Unexpected end of embedded shader bundle stream.");
                        offset += read;
                    }

                    ModLog.Info($"[ShaderCatalog] Embedded lilToon bundle size={bytes.Length:N0} bytes. Loading from memory...");
                    AssetBundle bundle = AssetBundle.LoadFromMemory(bytes);
                    if (bundle == null)
                    {
                        ModLog.Error("[ShaderCatalog] Embedded lilToon AssetBundle failed to load from memory.");
                        return false;
                    }

                    bundles.Add(bundle);
                    Shader[] shaders = bundle.LoadAllAssets<Shader>();
                    int added = 0;
                    foreach (Shader shader in shaders)
                    {
                        int before = loaded.Count;
                        Add(shader);
                        if (loaded.Count > before) added++;
                        if (shader != null)
                            ModLog.Info($"[ShaderCatalog] Embedded shader: '{shader.name}' ({(loaded.ContainsKey(shader.name) ? "available" : "duplicate")})");
                    }

                    ModLog.Info($"[ShaderCatalog] Embedded AssetBundle loaded. Shader assets={shaders.Length}, new={added}. Bundle retained.");
                    if (shaders.Length == 0)
                    {
                        ModLog.Error("[ShaderCatalog] Embedded lilToon bundle contains zero Shader assets. Rebuild the Unity AssetBundle as a normal asset bundle, not a streamed-scene bundle.");
                        return false;
                    }

                    ModLog.Info("[ShaderCatalog] EMBEDDED SHADER INJECTION CONFIRMED.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error($"[ShaderCatalog] Embedded lilToon load failed: {ex.GetType().Name}: {ex.Message}");
                if (AvatarReplacerPlugin.IsDebugEnabled) ModLog.ErrorException(ex);
                return false;
            }
        }
    }
}