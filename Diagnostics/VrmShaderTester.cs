using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AvatarReplacer
{
    /// <summary>
    /// Runtime shader laboratory for the active VRM.
    /// It discovers shader objects already loaded by the game, then temporarily
    /// assigns them to the VRM materials so the best compatible shader can be found.
    /// No game assets are modified: only the imported VRM materials are changed.
    /// </summary>
    internal static class VrmShaderTester
    {
        private sealed class MaterialState
        {
            public Material Material;
            public Shader OriginalShader;
            public string[] Keywords;
            public int RenderQueue;
            public bool EnableInstancing;
            public bool DoubleSidedGI;
            public Dictionary<string, Texture> Textures = new Dictionary<string, Texture>(StringComparer.Ordinal);
            public Dictionary<string, Vector2> TextureScales = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            public Dictionary<string, Vector2> TextureOffsets = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            public Dictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.Ordinal);
            public Dictionary<string, float> Floats = new Dictionary<string, float>(StringComparer.Ordinal);
        }

        private static readonly string[] CommonTextureProperties =
        {
            "_MainTex", "_BaseMap", "_BaseColorMap", "_ColorMap", "_ShadeTexture",
            "_BumpMap", "_NormalMap", "_EmissionMap", "_MetallicGlossMap", "_OcclusionMap",
            "_OutlineWidthTexture"
        };

        private static readonly string[] CommonColorProperties =
        {
            "_Color", "_BaseColor", "_ShadeColor", "_ShadeColor2", "_EmissionColor",
            "_RimColor", "_OutlineColor"
        };

        private static readonly string[] CommonFloatProperties =
        {
            "_Metallic", "_Smoothness", "_Glossiness", "_Roughness", "_Cutoff",
            "_Alpha", "_BumpScale", "_OcclusionStrength", "_EmissionPower",
            "_OutlineWidth", "_OutlineLightingMix", "_ShadeShift", "_ShadeToony"
        };

        private static GameObject targetRoot;
        private static readonly List<MaterialState> states = new List<MaterialState>();
        private static readonly List<Shader> candidates = new List<Shader>();
        private static int currentIndex = -1;
        private static bool initialized;
        private static string lastAppliedName;

        public static string CurrentShaderName =>
            currentIndex >= 0 && currentIndex < candidates.Count && candidates[currentIndex] != null
                ? candidates[currentIndex].name
                : "<original>";

        public static int CandidateCount => candidates.Count;
        public static int CurrentIndex => currentIndex;

        public static void SetTarget(GameObject root)
        {
            Reset();
            targetRoot = root;

            if (!IsEnabled || root == null)
                return;

            // No override configured: skip the whole capture/candidate lab and its
            // large per-load log dump. ApplyConfiguredOverride -> EnsureReady
            // re-initializes lazily the moment the user sets ShaderOverride.
            if (string.IsNullOrWhiteSpace(AvatarReplacerPlugin.ShaderOverrideValue))
            {
                ModLog.Debug($"[ShaderTest] ShaderOverride is empty; live shader lab idle for {root.name}.");
                return;
            }

            CaptureTargetMaterials(root);
            DiscoverCandidates();
            initialized = true;

            ModLog.Info($"[ShaderTest] Target={root.name}, VRM materials={states.Count}, candidate shaders={candidates.Count}");
            for (int i = 0; i < candidates.Count; i++)
            {
                Shader s = candidates[i];
                ModLog.Debug($"[ShaderTest] [{i + 1}/{candidates.Count}] {s.name}");
            }

            ApplyConfiguredOverride();
        }

        public static void ApplyConfiguredOverride()
        {
            if (!IsEnabled) return;
            if (!EnsureReady()) return;

            string configured = AvatarReplacerPlugin.ShaderOverrideValue;
            if (string.IsNullOrWhiteSpace(configured))
            {
                RestoreOriginalMaterials();
                currentIndex = -1;
                lastAppliedName = null;
                ModLog.Info("[ShaderTest] ShaderOverride is empty; restored original/default VRM material shaders.");
                return;
            }

            Shader shader = VrmShaderCatalog.Find(configured.Trim());
            if (shader == null)
            {
                ModLog.Warning($"[ShaderTest] ShaderOverride '{configured}' is NOT LOADED. No shader change was applied.");
                // Always show the availability table here, not just in debug mode -
                // this is the exact moment the user needs it to diagnose the failure.
                VrmShaderCatalog.LogDiagnostics();
                return;
            }

            ModLog.Info($"[ShaderTest] ShaderOverride '{shader.name}' = LOADED. Applying to {states.Count} material(s).");

            int index = candidates.FindIndex(s => s != null && string.Equals(s.name, shader.name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                candidates.Add(shader);
                index = candidates.Count - 1;
            }
            ApplyIndex(index, true);
        }

        public static void Reset()
        {
            RestoreOriginalMaterials();
            targetRoot = null;
            states.Clear();
            candidates.Clear();
            currentIndex = -1;
            initialized = false;
            lastAppliedName = null;
        }

        private static bool IsEnabled => AvatarReplacerPlugin.ShaderTestEnabledValue;

        private static bool EnsureReady()
        {
            if (!IsEnabled) return false;
            if (initialized && targetRoot != null && states.Count > 0) return true;

            var manager = AvatarReplacerPlugin.ActiveVrmManager;
            if (manager == null || manager.gameObject == null) return false;
            SetTarget(manager.gameObject);
            return initialized;
        }

        private static void CaptureTargetMaterials(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null) continue;
                    if (states.Any(s => s.Material == material)) continue;

                    var state = new MaterialState
                    {
                        Material = material,
                        OriginalShader = material.shader,
                        Keywords = material.shaderKeywords,
                        RenderQueue = material.renderQueue,
                        EnableInstancing = material.enableInstancing,
                        DoubleSidedGI = material.doubleSidedGI
                    };

                    foreach (string property in CommonTextureProperties)
                    {
                        if (!material.HasProperty(property)) continue;
                        state.Textures[property] = material.GetTexture(property);
                        state.TextureScales[property] = material.GetTextureScale(property);
                        state.TextureOffsets[property] = material.GetTextureOffset(property);
                    }

                    foreach (string property in CommonColorProperties)
                    {
                        if (material.HasProperty(property))
                            state.Colors[property] = material.GetColor(property);
                    }

                    foreach (string property in CommonFloatProperties)
                    {
                        if (material.HasProperty(property))
                            state.Floats[property] = material.GetFloat(property);
                    }

                    states.Add(state);
                }
            }
        }

        private static void DiscoverCandidates()
        {
            candidates.Clear();
            var byName = new Dictionary<string, Shader>(StringComparer.OrdinalIgnoreCase);

            foreach (Shader shader in VrmShaderCatalog.GetLoadedShaders())
                AddCandidate(byName, shader);

            // Put the known working giantess shader first.
            if (byName.TryGetValue("KiriToon/Giantess", out Shader giantess))
            {
                candidates.Add(giantess);
                byName.Remove(giantess.name);
            }

            foreach (var pair in byName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                candidates.Add(pair.Value);
        }

        private static void AddCandidate(Dictionary<string, Shader> byName, Shader shader)
        {
            if (shader == null || string.IsNullOrWhiteSpace(shader.name)) return;
            if (shader.name.StartsWith("Hidden/", StringComparison.OrdinalIgnoreCase)) return;
            if (shader.name.StartsWith("Internal/", StringComparison.OrdinalIgnoreCase)) return;
            if (!byName.ContainsKey(shader.name)) byName.Add(shader.name, shader);
        }

        private static void ApplyIndex(int index, bool fromConfig)
        {
            if (index < 0 || index >= candidates.Count) return;
            Shader shader = candidates[index];
            if (shader == null) return;

            RestoreCommonPropertiesBeforeSwitch();

            foreach (var state in states)
            {
                if (state.Material == null) continue;
                try
                {
                    state.Material.shader = shader;
                    RestoreCommonProperties(state, state.Material);
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"[ShaderTest] Failed to apply '{shader.name}' to '{state.Material.name}': {ex.Message}");
                }
            }

            currentIndex = index;
            lastAppliedName = shader.name;
            ModLog.Info($"[ShaderTest] ACTIVE {index + 1}/{candidates.Count}: {shader.name}{(fromConfig ? " (config)" : " (live test)")}");
        }

        private static void RestoreCommonPropertiesBeforeSwitch()
        {
            // Reapply the values captured from the original VRM conversion before every
            // test. This prevents one incompatible shader from poisoning the next test.
            foreach (var state in states)
            {
                if (state.Material == null) continue;
                try { RestoreCommonProperties(state, state.Material); }
                catch { }
            }
        }

        private static void RestoreCommonProperties(MaterialState state, Material material)
        {
            foreach (var pair in state.Textures)
            {
                if (!material.HasProperty(pair.Key)) continue;
                material.SetTexture(pair.Key, pair.Value);
                if (state.TextureScales.TryGetValue(pair.Key, out var scale)) material.SetTextureScale(pair.Key, scale);
                if (state.TextureOffsets.TryGetValue(pair.Key, out var offset)) material.SetTextureOffset(pair.Key, offset);
            }

            foreach (var pair in state.Colors)
                if (material.HasProperty(pair.Key)) material.SetColor(pair.Key, pair.Value);

            foreach (var pair in state.Floats)
                if (material.HasProperty(pair.Key)) material.SetFloat(pair.Key, pair.Value);
        }

        private static void RestoreOriginalMaterials()
        {
            foreach (var state in states)
            {
                if (state.Material == null) continue;
                try
                {
                    state.Material.shader = state.OriginalShader;
                    state.Material.shaderKeywords = state.Keywords;
                    state.Material.renderQueue = state.RenderQueue;
                    state.Material.enableInstancing = state.EnableInstancing;
                    state.Material.doubleSidedGI = state.DoubleSidedGI;
                    RestoreCommonProperties(state, state.Material);
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(lastAppliedName))
                ModLog.Debug($"[ShaderTest] Restored original VRM shaders after testing '{lastAppliedName}'.");
        }
    }
}