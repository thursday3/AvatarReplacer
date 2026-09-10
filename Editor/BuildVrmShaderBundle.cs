#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildVrmShaderBundle
{
    private static readonly HashSet<string> Names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "VRM/MToon",
        "VRM/UnlitTexture",
        "VRM/UnlitCutout",
        "VRM/UnlitTransparent",
        "VRM/UnlitTransparentZWrite",
        "Standard",
        "UniGLTF/UniUnlit",
        "VRM10/MToon10",
        "VRM10/Universal Render Pipeline/MToon10"
    };

    [MenuItem("AvatarReplacer/Build VRM Shader Bundle")]
    public static void Build()
    {
        const string outputDir = "Assets/AvatarReplacerShaderBundle";
        const string bundleName = "vrm_shaders";
        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        var assets = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null && Names.Contains(shader.name))
                assets.Add(path);
        }

        if (assets.Count == 0)
        {
            Debug.LogError("[AvatarReplacer] No UniVRM shader assets were found. Make sure UniVRM 0.131.0 is installed in the Unity project.");
            return;
        }

        var build = new AssetBundleBuild
        {
            assetBundleName = bundleName,
            assetNames = assets.ToArray()
        };

        BuildPipeline.BuildAssetBundles(
            outputDir,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget) == BuildTargetGroup.Unknown
                ? BuildTarget.StandaloneWindows64
                : EditorUserBuildSettings.activeBuildTarget);

        string source = Path.Combine(outputDir, bundleName);
        string destination = Path.Combine(Application.dataPath, "..", "BepInEx", "plugins", "AvatarReplacer", "shaders", "vrm_shaders.bundle");
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, true);
        Debug.Log($"[AvatarReplacer] Built VRM shader bundle with {assets.Count} shader assets: {destination}");
        AssetDatabase.Refresh();
    }
}
#endif
