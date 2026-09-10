using System;
using System.Collections.Generic;
using UnityEngine;

namespace AvatarReplacer
{
    /// <summary>
    /// Diagnostic-only inspection of materials created by UniVRM.
    /// Never changes the avatar's materials or shaders.
    /// </summary>
    internal static class VrmMaterialDiagnostics
    {
        public static void Log(GameObject root)
        {
            if (root == null || !AvatarReplacerPlugin.IsDebugEnabled)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var shaderCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int materialCount = 0;

            ModLog.Debug("VRM materials: inspecting UniVRM-created materials (no shader replacement).");

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null) continue;

                    materialCount++;
                    string shaderName = material.shader != null ? material.shader.name : "<missing shader>";

                    if (!shaderCounts.TryGetValue(shaderName, out int count))
                        count = 0;
                    shaderCounts[shaderName] = count + 1;

                    ModLog.Debug($"VRM material: renderer={renderer.name}, material={material.name}, shader={shaderName}");
                }
            }

            ModLog.Debug($"VRM materials: renderers={renderers.Length}, materials={materialCount}, uniqueShaders={shaderCounts.Count}");
            foreach (var pair in shaderCounts)
                ModLog.Debug($"VRM shader usage: {pair.Key} x{pair.Value}");
        }
    }
}
