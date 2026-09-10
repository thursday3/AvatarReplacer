using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UniVRM10;
using VRM;
using UniGLTF;

namespace AvatarReplacer
{
    /// <summary>
    /// Runtime VRM loader.
    ///
    /// Important: UniVRM owns material creation. We deliberately do not replace
    /// every VRM material with a single generic Unity shader. This preserves
    /// MToon/MToon10, Unlit and glTF PBR materials authored by the VRM creator.
    /// </summary>
    public static class VrmLoader
    {
        public static async Task<GameObject> LoadVrmAsync(string vrmPath)
        {
            if (!File.Exists(vrmPath))
            {
                ModLog.Error($"VRM file not found: {vrmPath}");
                return null;
            }

            var detection = VrmDetector.Detect(vrmPath);
            if (!string.IsNullOrEmpty(detection.Error))
                ModLog.Warning($"VRM detector: {detection.Error} Falling back to loader probing.");

            ModLog.Info($"VRM detector: format={detection.Format}, glb={detection.IsGlb}, jsonBytes={detection.JsonChunkBytes}");

            // Unity player builds may strip shaders from Shader.Find(). The original
            // game character already has the correct runtime shader loaded, so use a
            // live renderer shader as the material-conversion fallback.
            VrmShaderCatalog.Initialize();
            if (AvatarReplacerPlugin.ShaderDiagnosticsValue)
                VrmShaderCatalog.LogDiagnostics();

            Shader shader = FindGameRuntimeShader();
            if (shader == null)
            {
                ModLog.Error("No runtime shader is available for VRM material conversion. Could not find a live game renderer shader.");
                return null;
            }

            ModLog.Debug($"VRM material conversion shader: {shader.name} (taken from a live game renderer)");

            GameObject root = null;
            bool tried10 = false;
            bool tried0 = false;

            // Convert VRM/glTF PBR data onto the game shader that is already loaded at runtime.
            if (detection.Format == VrmFormat.Vrm10 || detection.Format == VrmFormat.Unknown)
            {
                tried10 = true;
                root = await TryLoadVrm10(vrmPath, shader);
            }

            if (root == null && (detection.Format == VrmFormat.Vrm0X || detection.Format == VrmFormat.Unknown || tried10))
            {
                tried0 = true;
                root = await TryLoadVrm0X(vrmPath, shader);
            }

            if (root == null && !tried10)
            {
                tried10 = true;
                root = await TryLoadVrm10(vrmPath, shader);
            }

            if (root == null)
            {
                ModLog.Error($"VRM load failed. Tried VRM1={tried10}, VRM0={tried0}.");
                return null;
            }

            VrmMaterialDiagnostics.Log(root);
            return root;
        }

        private static async Task<GameObject> TryLoadVrm10(string path, Shader shader)
        {
            try
            {
                ModLog.Debug($"Loader: trying VRM 1.0: {Path.GetFileName(path)}");

                var instance = await UniVRM10.Vrm10.LoadPathAsync(
                    path,
                    canLoadVrm0X: false,
                    showMeshes: true,
                    awaitCaller: new ImmediateCaller(),
                    materialGenerator: new SimpleMaterialGenerator(shader));

                if (instance == null) return null;

                ModLog.Info("VRM 1.0 loaded successfully using game runtime shader material conversion.");
                return instance.gameObject;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"VRM 1.0 load failed: {ex.Message}");
                if (AvatarReplacerPlugin.IsDebugEnabled) ModLog.ErrorException(ex);
                return null;
            }
        }

        private static async Task<GameObject> TryLoadVrm0X(string path, Shader shader)
        {
            try
            {
                ModLog.Debug($"Loader: trying VRM 0.x: {Path.GetFileName(path)}");

                RuntimeGltfInstance instance = await VrmUtility.LoadAsync(
                    path,
                    awaitCaller: new ImmediateCaller(),
                    materialGeneratorCallback: vrm => new SimpleMaterialGenerator(shader));

                if (instance == null) return null;

                instance.ShowMeshes();
                instance.EnableUpdateWhenOffscreen();

                ModLog.Info("VRM 0.x loaded successfully using game runtime shader material conversion.");
                return instance.Root;
            }
            catch (Exception ex)
            {
                ModLog.Warning($"VRM 0.x load failed: {ex.Message}");
                if (AvatarReplacerPlugin.IsDebugEnabled) ModLog.ErrorException(ex);
                return null;
            }
        }

        /// <summary>
        /// Finds a shader that is already loaded by the game. Unity player builds can
        /// strip shaders, making Shader.Find("Standard") and Shader.Find("VRM/MToon")
        /// return null even though the game is rendering characters with those shader
        /// assets. The live giantess renderer is therefore the safest runtime source.
        /// </summary>
        private static Shader FindGameRuntimeShader()
        {
            string configured = AvatarReplacerPlugin.ShaderOverrideValue;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                Shader configuredShader = VrmShaderCatalog.Find(configured);
                if (configuredShader != null)
                {
                    ModLog.Info($"Using configured VRM shader: {configuredShader.name}");
                    return configuredShader;
                }

                ModLog.Warning($"Configured VRM shader '{configured}' is not loaded. Falling back to the game's default working shader.");
            }

            Shader shader = VrmShaderCatalog.Find("KiriToon/Giantess");
            if (shader != null) return shader;

            foreach (Shader candidate in VrmShaderCatalog.GetLoadedShaders())
            {
                if (candidate.name.IndexOf("KiriToon", StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            }

            // Last resort: any shader used by a live skinned mesh.
            foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!(renderer is SkinnedMeshRenderer) || renderer.sharedMaterials == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && material.shader != null) return material.shader;
            }

            return null;
        }

    }

    /// <summary>
    /// Runtime-compatible material descriptor generator.
    ///
    /// The game does not expose its shaders through Shader.Find in the player build
    /// (the diagnostic reports all of the usual names as unavailable). We therefore
    /// feed UniGLTF the shader object taken from a live game character renderer.
    /// This is the same strategy that made the original loader work, but without
    /// hard-coding Standard or requiring a VRM shader bundle.
    /// </summary>
    public sealed class SimpleMaterialGenerator : IMaterialDescriptorGenerator
    {
        private readonly Shader shader;

        public SimpleMaterialGenerator(Shader shader)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            this.shader = shader;
        }

        public MaterialDescriptor Get(GltfData data, int materialIndex)
        {
            if (data == null || data.GLTF == null || materialIndex < 0 || materialIndex >= data.GLTF.materials.Count)
                return GetGltfDefault();

            var src = data.GLTF.materials[materialIndex];
            string name = src.name ?? $"Material_{materialIndex}";
            var textures = new Dictionary<string, TextureDescriptor>();
            var floats = new Dictionary<string, float>();
            var colors = new Dictionary<string, Color>();
            var vectors = new Dictionary<string, Vector4>();
            var actions = new List<Action<Material>>();

            if (src.pbrMetallicRoughness != null)
            {
                float[] values = src.pbrMetallicRoughness.baseColorFactor;
                if (values != null && values.Length >= 4)
                {
                    Color baseColor = new Color(values[0], values[1], values[2], values[3]);
                    colors["_Color"] = baseColor;
                    colors["_BaseColor"] = baseColor;
                }
                else
                {
                    colors["_Color"] = Color.white;
                    colors["_BaseColor"] = Color.white;
                }

                floats["_Metallic"] = src.pbrMetallicRoughness.metallicFactor;
                floats["_Smoothness"] = 1f - src.pbrMetallicRoughness.roughnessFactor;

                var tex = src.pbrMetallicRoughness.baseColorTexture;
                if (tex != null && tex.index >= 0 &&
                    GltfPbrTextureImporter.TryBaseColorTexture(data, src, out _, out var descriptor))
                {
                    // Support both Built-in Standard-style and common custom-game
                    // texture property names. UniGLTF ignores properties the shader
                    // does not expose.
                    textures["_MainTex"] = descriptor;
                    textures["_BaseMap"] = descriptor;
                }
            }
            else
            {
                colors["_Color"] = Color.white;
                colors["_BaseColor"] = Color.white;
            }

            // Preserve the shader's existing defaults. Do not disable outlines,
            // emission, skin colour, or any other VRM/game material feature here.
            return new MaterialDescriptor(
                name,
                shader,
                null,
                textures,
                floats,
                colors,
                vectors,
                actions);
        }

        public MaterialDescriptor GetGltfDefault(string materialName = null)
        {
            return new MaterialDescriptor(
                materialName ?? "Default",
                shader,
                null,
                new Dictionary<string, TextureDescriptor>(),
                new Dictionary<string, float>(),
                new Dictionary<string, Color>
                {
                    { "_Color", Color.white },
                    { "_BaseColor", Color.white }
                },
                new Dictionary<string, Vector4>(),
                new List<Action<Material>>());
        }
    }

}