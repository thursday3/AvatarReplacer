using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM;

namespace AvatarReplacer
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class AvatarReplacerPlugin : BaseUnityPlugin
    {
        static AvatarReplacerPlugin()
        {
            // Register before Harmony and UniVRM types are first used. The
            // referenced UniVRM DLLs are embedded in this plugin assembly.
            EmbeddedAssemblyLoader.Initialize();
        }

        public static class PluginInfo
        {
            public const string PLUGIN_GUID = "com.user.avatarreplacer";
            public const string PLUGIN_NAME = "AvatarReplacer";
            public const string PLUGIN_VERSION = "6.0.0";
        }

        private static ConfigEntry<string> VrmFileName;
        private static ConfigEntry<bool> EnableReplacement;
        private static ConfigEntry<bool> HideOriginalMesh;
        private static ConfigEntry<float> PosX, PosY, PosZ;
        private static ConfigEntry<float> RotX, RotY, RotZ;
        private static ConfigEntry<float> ScaleOverride;
        private static ConfigEntry<bool> DebugMode;
        private static ConfigEntry<float> SpringBoneStiffness;
        private static ConfigEntry<float> SpringBoneGravityPower;
        private static ConfigEntry<float> SpringBoneDrag;
        private static ConfigEntry<bool> AutoSpringBone;
        private static ConfigEntry<string> AutoSpringBoneKeywords;
        private static ConfigEntry<int> AutoSpringBoneMaxRoots;
        private static ConfigEntry<bool> AutoSpringBoneOnAvatarsWithBones;
        private static ConfigEntry<bool> SmallScaleLocomotionFixEnabled;
        private static ConfigEntry<float> SmallScaleLocomotionFixThresholdMeters;
        private static ConfigEntry<float> SmallScaleLocomotionFixStrideFactorMin;
        private static ConfigEntry<float> SmallScaleLocomotionFixHipSmoothMultiplier;
        private static ConfigEntry<bool> ShaderTestEnabled;
        private static ConfigEntry<string> ShaderOverride;
        private static ConfigEntry<string> ShaderBundlePath;
        private static ConfigEntry<bool> ShaderDiagnostics;
        private static string lastObservedShaderOverride = null;

        public static ConfigEntry<KeyCode> PreviousAvatarKey;
        public static ConfigEntry<KeyCode> NextAvatarKey;
        public static ConfigEntry<KeyCode> RescanKey;
        public static ConfigEntry<KeyCode> DiagnosticsKey;

        private static string configDir;

        /// <summary>Directory holding per-avatar profiles, expression_map.json etc.</summary>
        public static string ConfigDirectory => configDir;
        private static Dictionary<string, VrmAvatarProfile> profileCache = new Dictionary<string, VrmAvatarProfile>();

        public static float SpringBoneStiffnessValue => SpringBoneStiffness != null && !IsDefaultConfigValue(SpringBoneStiffness) ? SpringBoneStiffness.Value : -1f;
        public static float SpringBoneGravityPowerValue => SpringBoneGravityPower != null && !IsDefaultConfigValue(SpringBoneGravityPower) ? SpringBoneGravityPower.Value : -1f;
        public static float SpringBoneDragValue => SpringBoneDrag != null && !IsDefaultConfigValue(SpringBoneDrag) ? SpringBoneDrag.Value : -1f;

        /// <summary>
        /// Spring bone parameter overrides only apply when the user changed them
        /// from the BepInEx config defaults. Unchanged defaults (5/3/1) previously
        /// clobbered every authored VRM0 spring bone on every avatar, breaking
        /// hair/skirt physics; authored values are respected unless customized.
        /// </summary>
        private static bool IsDefaultConfigValue(ConfigEntry<float> entry)
        {
            try { return entry.Value.Equals(Convert.ToSingle(entry.DefaultValue)); }
            catch { return false; }
        }
        public static bool SmallScaleLocomotionFixEnabledValue => SmallScaleLocomotionFixEnabled == null || SmallScaleLocomotionFixEnabled.Value;
        public static float SmallScaleLocomotionFixThresholdMetersValue => SmallScaleLocomotionFixThresholdMeters != null ? Mathf.Max(0.1f, SmallScaleLocomotionFixThresholdMeters.Value) : 60f;
        public static float SmallScaleLocomotionFixStrideFactorMinValue => SmallScaleLocomotionFixStrideFactorMin != null ? Mathf.Clamp(SmallScaleLocomotionFixStrideFactorMin.Value, 0.1f, 1f) : 0.75f;
        public static float SmallScaleLocomotionFixHipSmoothMultiplierValue => SmallScaleLocomotionFixHipSmoothMultiplier != null ? Mathf.Max(1f, SmallScaleLocomotionFixHipSmoothMultiplier.Value) : 1.25f;
        public static bool SpringBoneDiagnosticsValue => DebugMode != null && DebugMode.Value;
        public static float SpringBoneDiagnosticsIntervalValue => 2.0f;

        private static VrmManager activeVrmManager;
        private static string activeVrmPath = null;
        private static bool loadingVrm = false;

        private static bool loggedProxyOnDestroyError = false;

        /// <summary>
        /// UniVRM's VRMBlendShapeProxy.OnDestroy calls
        /// VRM.BlendShapeMerger.RestoreMaterialInitialValues, which touches
        /// UnityEditor-only APIs. In a player build that throws
        /// FileNotFoundException (UnityEditor.CoreModule) and can spam errors on
        /// every avatar unload. A finalizer swallows exactly that failure.
        /// </summary>
        [HarmonyPatch(typeof(VRM.VRMBlendShapeProxy), "OnDestroy")]
        [HarmonyFinalizer]
        private static System.Exception VRMBlendShapeProxy_OnDestroy_Finalizer(System.Exception __exception)
        {
            if (__exception == null) return null;
            if (!loggedProxyOnDestroyError)
            {
                loggedProxyOnDestroyError = true;
                ModLog.Info($"[AvatarReplacer] Suppressed VRMBlendShapeProxy.OnDestroy error (editor-only cleanup, harmless): {__exception.Message}");
            }
            return null; // swallow
        }

        private static List<string> availableVrmFiles = new List<string>();
        private static int currentVrmIndex = -1;
        private static Giantess currentGiantessTarget;

        private static Dictionary<string, VrmManager> loadedVrmCache = new Dictionary<string, VrmManager>();
        private static List<string> cacheAccessOrder = new List<string>();
        private const int MAX_CACHED_AVATARS = 3;
        private static int loadToken = 0;
        private static string lastSceneName = null;

        public static AvatarReplacerPlugin Instance { get; private set; }

        public static bool IsDebugEnabled => DebugMode != null && DebugMode.Value;
        public static VrmManager ActiveVrmManager => activeVrmManager;

        /// <summary>Path of the VRM currently being set up (for per-VRM config files).</summary>
        public static string ActiveVrmPath => activeVrmPath;
        public static bool ShaderTestEnabledValue => ShaderTestEnabled != null && ShaderTestEnabled.Value;
        public static string ShaderOverrideValue => ShaderOverride != null ? ShaderOverride.Value : string.Empty;
        public static string ShaderBundlePathValue => ShaderBundlePath != null ? ShaderBundlePath.Value : string.Empty;
        public static bool ShaderDiagnosticsValue => ShaderDiagnostics == null || ShaderDiagnostics.Value;

        private void Awake()
        {
            Instance = this;
            ModLog.SetLogger(Logger);

            VrmFileName = Config.Bind("General", "VrmFileName", "avatar.vrm", "Name of the default VRM file.");
            EnableReplacement = Config.Bind("General", "EnableReplacement", true, "Set to FALSE to disable VRM.");
            HideOriginalMesh = Config.Bind("General", "HideOriginalMesh", true, "Hide the original Giantess mesh.");
            DebugMode = Config.Bind("General", "DebugMode", false, "Enable verbose debug logging (saved to log file when on).");

            PreviousAvatarKey = Config.Bind("Controls", "PreviousAvatarKey", KeyCode.PageUp, "Key to switch to previous avatar");
            NextAvatarKey = Config.Bind("Controls", "NextAvatarKey", KeyCode.PageDown, "Key to switch to next avatar");
            RescanKey = Config.Bind("Controls", "RescanKey", KeyCode.F5, "Key to rescan VRM folder for new/removed avatars");
            DiagnosticsKey = Config.Bind("Controls", "DiagnosticsKey", KeyCode.F10, "Key to print a full VRM diagnostic report for the active avatar.");
            ShaderTestEnabled = Config.Bind("ShaderTesting", "Enabled", true, "Enable config-driven live shader testing. Changes to ShaderOverride are applied while the game is running.");
            ShaderOverride = Config.Bind("ShaderTesting", "ShaderOverride", "",
                "Exact shader name to apply live. Empty = restore the original/default game shader. Known names: KiriToon/Giantess; VRM/MToon; VRM/UnlitTexture; VRM/UnlitCutout; VRM/UnlitTransparent; VRM/UnlitTransparentZWrite; Standard; VRM10/MToon10; VRM10/Universal Render Pipeline/MToon10. Only names actually loaded by the game can be used.");
            ShaderBundlePath = Config.Bind("ShaderTesting", "ShaderBundlePath", "",
                "Optional AssetBundle path, relative to BepInEx/plugins/AvatarReplacer, containing VRM shader assets. Example: shaders/vrm_shaders.bundle. Required to make VRM/MToon or VRM10/MToon10 available when the game does not ship them.");
            ShaderDiagnostics = Config.Bind("ShaderTesting", "Diagnostics", true,
                "Log known VRM shader availability and loaded shader names when the VRM is loaded. Useful for testing ShaderOverride.");
            lastObservedShaderOverride = ShaderOverride.Value;

            PosX = Config.Bind("Transform", "PosX", 0f, "VRM Position X Offset");
            PosY = Config.Bind("Transform", "PosY", 0f, "VRM Position Y Offset");
            PosZ = Config.Bind("Transform", "PosZ", 0f, "VRM Position Z Offset");
            RotX = Config.Bind("Transform", "RotX", 0f, "VRM Rotation X Offset");
            RotY = Config.Bind("Transform", "RotY", 0f, "VRM Rotation Y Offset");
            RotZ = Config.Bind("Transform", "RotZ", 0f, "VRM Rotation Z Offset");
            ScaleOverride = Config.Bind("Transform", "ScaleOverride", 0f, "GTS startup scale multiplier (0 = no change, 0.5 = half size, 2.0 = double). Scales the whole giantess (mesh + IK shadow) at spawn. VRM auto-matches as normal.");

            SpringBoneStiffness = Config.Bind("SpringBone", "Stiffness", 5.0f, "VRMSpringBone stiffness force");
            SpringBoneGravityPower = Config.Bind("SpringBone", "GravityPower", 3.0f, "VRMSpringBone gravity power");
            SpringBoneDrag = Config.Bind("SpringBone", "Drag", 1.0f, "VRMSpringBone drag force");

            AutoSpringBone = Config.Bind("SpringBone", "AutoGenerate", false, "Auto-generate SpringBones for avatar bones matched by AutoKeywords. OFF by default: keyword-guessed springs can drag head/body-adjacent chains and visibly deform the avatar. Enable only for avatars with no VRM spring bones.");
            AutoSpringBoneKeywords = Config.Bind("SpringBone", "AutoKeywords", "hair,skirt,tail,cape,cloth,dress,accessory,ribbon,sleeve,belt,twintail,frill,lace,string", "Keywords to find bones for auto SpringBone.");
            AutoSpringBoneMaxRoots = Config.Bind("SpringBone", "AutoMaxRoots", 16, "Max number of auto SpringBone root bones.");
            AutoSpringBoneOnAvatarsWithBones = Config.Bind("SpringBone", "AutoGenerateOnAvatarsWithBones", false, "Also run the auto generator on avatars that already have VRM spring bones (adds extra chains). Leave false unless you know an avatar's own springs are missing.");
            SmallScaleLocomotionFixEnabled = Config.Bind("SmallScaleLocomotionFix", "Enabled", true, "Enable small-scale stride and hip smoothing compensation.");
            SmallScaleLocomotionFixThresholdMeters = Config.Bind("SmallScaleLocomotionFix", "ThresholdMeters", 60f, "Apply compensation at and below this giantess height in meters.");
            SmallScaleLocomotionFixStrideFactorMin = Config.Bind("SmallScaleLocomotionFix", "StrideFactorMin", 0.75f, "Minimum stride armature factor at strongest small-scale compensation.");
            SmallScaleLocomotionFixHipSmoothMultiplier = Config.Bind("SmallScaleLocomotionFix", "HipSmoothMultiplier", 1.25f, "Additional smoothing multiplier for small-scale root/hip motion.");

            Harmony.CreateAndPatchAll(typeof(AvatarReplacerPlugin));
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} v{PluginInfo.PLUGIN_VERSION} loaded!");

            configDir = Path.Combine(Paths.PluginPath, "AvatarReplacer", "config");
            if (!Directory.Exists(configDir))
            {
                try { Directory.CreateDirectory(configDir); }
                catch (Exception e) { ModLog.Error($"Failed to create config dir: {e.Message}"); }
            }
            VrmAvatarProfile.CreateTemplateProfile(configDir);

            ScanForVrms();
        }

        private void Update()
        {
            // Shader testing is intentionally config-driven. Edit ShaderOverride in the
            // BepInEx config while the game is running; the watcher/poll below applies
            // the change to the active VRM without any keyboard test controls.
            if (!loadingVrm)
            {
                string current = ShaderOverrideValue ?? string.Empty;
                if (!string.Equals(current, lastObservedShaderOverride, StringComparison.Ordinal))
                {
                    lastObservedShaderOverride = current;
                    if (ShaderTestEnabledValue)
                        VrmShaderTester.ApplyConfiguredOverride();
                }
            }
        }

        public static void ScanForVrms()
        {
            loadingVrm = true;
            string previousActive = "none";
            int previousCount = availableVrmFiles.Count;

            if (activeVrmManager != null && currentVrmIndex != -1 && currentVrmIndex < availableVrmFiles.Count)
            {
                previousActive = Path.GetFileName(availableVrmFiles[currentVrmIndex]);
            }

            availableVrmFiles.Clear();

            foreach (var kvp in loadedVrmCache)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                {
                    SafeDestroyAvatar(kvp.Value.gameObject);
                }
            }
            loadedVrmCache.Clear();
            cacheAccessOrder.Clear();
            profileCache.Clear();
            activeVrmManager = null;
            activeVrmPath = null;

            string vrmDir = Path.Combine(Paths.PluginPath, "AvatarReplacer", "vrm");
            string legacyDir = Path.Combine(Paths.PluginPath, "AvatarReplacer");

            if (!Directory.Exists(vrmDir))
            {
                try { Directory.CreateDirectory(vrmDir); }
                catch (Exception e) { ModLog.Error($"Failed to create vrm dir: {e.Message}"); }
            }

            if (Directory.Exists(vrmDir))
            {
                availableVrmFiles.AddRange(Directory.GetFiles(vrmDir, "*.vrm"));
            }

            if (Directory.Exists(legacyDir))
            {
                var legacyFiles = Directory.GetFiles(legacyDir, "*.vrm");
                foreach (var f in legacyFiles)
                {
                    if (!availableVrmFiles.Contains(f)) availableVrmFiles.Add(f);
                }
            }

            ModLog.Info($"[AvatarReplacer] Found {availableVrmFiles.Count} VRM files (was {previousCount}).");

            // One expression_map.<avatar>.json per discovered model. Existing
            // files are never overwritten, so adding a new VRM only creates its
            // own template and user-edited maps keep their values.
            CustomExpressionMap.EnsureTemplatesForVrmFolder(vrmDir);

            string defaultName = VrmFileName.Value;
            int foundIndex = -1;

            if (previousActive != "none")
            {
                foundIndex = availableVrmFiles.FindIndex(f => Path.GetFileName(f).Equals(previousActive, StringComparison.OrdinalIgnoreCase));
                if (foundIndex != -1)
                {
                    ModLog.Info($"[AvatarReplacer] Keeping previously active avatar: {previousActive}");
                }
            }

            if (foundIndex == -1)
            {
                foundIndex = availableVrmFiles.FindIndex(f => Path.GetFileName(f).Equals(defaultName, StringComparison.OrdinalIgnoreCase));
            }

            if (foundIndex != -1)
            {
                currentVrmIndex = foundIndex;
            }
            else if (availableVrmFiles.Count > 0)
            {
                currentVrmIndex = 0;
                if (previousActive != "none")
                {
                    ModLog.Warning($"[AvatarReplacer] Previously active avatar '{previousActive}' no longer exists. Switching to: {Path.GetFileName(availableVrmFiles[0])}");
                }
            }
            else
            {
                currentVrmIndex = -1;
                ModLog.Warning("[AvatarReplacer] No VRM files found after rescan.");
            }

            loadingVrm = false;
        }

        private static void ClearCache(string reason)
        {
            VrmShaderTester.Reset();
            ModLog.Info($"[AvatarReplacer] Clearing cache: {reason}");
            foreach (var kvp in loadedVrmCache)
            {
                if (kvp.Value != null && kvp.Value.gameObject != null)
                {
                    SafeDestroyAvatar(kvp.Value.gameObject);
                }
            }
            loadedVrmCache.Clear();
            cacheAccessOrder.Clear();
            profileCache.Clear();
            activeVrmManager = null;
            activeVrmPath = null;
            currentGiantessTarget = null;
        }

        public static void ReloadCurrentAvatar()
        {
            if (currentGiantessTarget == null) return;

            string path = "";
            if (currentVrmIndex != -1 && currentVrmIndex < availableVrmFiles.Count)
            {
                path = availableVrmFiles[currentVrmIndex];
            }
            else
            {

                path = Path.Combine(Paths.PluginPath, "AvatarReplacer", VrmFileName.Value);
            }

            ModLog.Info($"[AvatarReplacer] Reloading avatar: {Path.GetFileName(path)}");
            InitializeVrm(currentGiantessTarget, path);
        }

        public static void SwitchAvatar(int direction)
        {
            if (loadingVrm) return;

            if (currentGiantessTarget == null)
            {
                ModLog.Info("[WARN] SwitchAvatar called but no Giantess target is active yet.");
                return;
            }

            if (availableVrmFiles == null || availableVrmFiles.Count == 0)
            {
                ModLog.Info("[WARN] No VRM files available. Place .vrm files in /BepInEx/plugins/AvatarReplacer/vrm and press the rescan key.");
                return;
            }

            currentVrmIndex += direction;

            if (currentVrmIndex < 0) currentVrmIndex = availableVrmFiles.Count - 1;
            if (currentVrmIndex >= availableVrmFiles.Count) currentVrmIndex = 0;

            string newPath = availableVrmFiles[currentVrmIndex];
            ModLog.Info($"Switching to avatar ({currentVrmIndex + 1}/{availableVrmFiles.Count}): {Path.GetFileName(newPath)}");

            InitializeVrm(currentGiantessTarget, newPath);
        }

        [HarmonyPatch(typeof(Giantess), "Awake")]
        [HarmonyPostfix]
        private static void Giantess_Awake_Postfix(Giantess __instance)
        {
            if (__instance == null || !EnableReplacement.Value) return;

            string sceneName = __instance.gameObject.scene.name;
            if (string.IsNullOrEmpty(lastSceneName))
            {
                lastSceneName = sceneName;
            }
            else if (!string.Equals(lastSceneName, sceneName, StringComparison.Ordinal))
            {
                ClearCache($"Scene changed '{lastSceneName}' -> '{sceneName}'");
                lastSceneName = sceneName;
            }

            string objectName = __instance.gameObject.name.ToLowerInvariant();
            if (objectName.Contains("kiritan"))
            {
                ModLog.Debug($"Skipping companion character: {__instance.gameObject.name}");
                return;
            }

            ModLog.Info($"Targeting giantess: {__instance.gameObject.name}");
            currentGiantessTarget = __instance;

            if (ScaleOverride != null && ScaleOverride.Value > 0.0001f)
            {
                float originalScale = __instance.scale;
                __instance.scale = originalScale * ScaleOverride.Value;
                __instance.UpdateScale();
                ModLog.Info($"ScaleOverride applied: GTS scale {originalScale:F3} * {ScaleOverride.Value:F3} = {__instance.scale:F3}");
            }

            string path = "";
            if (currentVrmIndex != -1 && currentVrmIndex < availableVrmFiles.Count)
            {
                path = availableVrmFiles[currentVrmIndex];
            }
            else
            {
                 path = Path.Combine(Paths.PluginPath, "AvatarReplacer", VrmFileName.Value);
            }

            if (Instance != null && Instance.isActiveAndEnabled)
            {
                Instance.StartCoroutine(InitializeVrmDelayed(__instance, path));
            }
            else
            {

                InitializeVrm(__instance, path);
            }
        }

        private static System.Collections.IEnumerator InitializeVrmDelayed(Giantess giantess, string vrmPath)
        {

            yield return new WaitForEndOfFrame();

            yield return new WaitForEndOfFrame();

            InitializeVrm(giantess, vrmPath);
        }

        private static void InitializeVrm(Giantess giantess, string vrmPath)
        {
            _ = InitializeVrmAsync(giantess, vrmPath);
        }

        private static async System.Threading.Tasks.Task InitializeVrmAsync(Giantess giantess, string vrmPath)
        {
            if (loadingVrm) return;

            int myToken = ++loadToken;

            if (giantess == null)
            {
                ModLog.Info("[WARN] InitializeVrm called with null/destroyed Giantess.");
                return;
            }

            try
            {
                string path = vrmPath;

                if (loadedVrmCache.ContainsKey(path) && loadedVrmCache[path] != null)
                {
                    ModLog.Debug($"Cache Hit! Enabling: {Path.GetFileName(path)}");
                    var cached = loadedVrmCache[path];
                    if (cached.BoundGiantess != giantess)
                    {
                        ModLog.Info($"[AvatarReplacer] Cached avatar bound to different Giantess. Rebuilding for current target.");
                        SafeDestroyAvatar(cached.gameObject);
                        loadedVrmCache.Remove(path);
                    }
                    else
                    {

                        if (activeVrmManager != null)
                        {
                            activeVrmManager.gameObject.SetActive(false);
                        }

                        activeVrmManager = cached;
                        activeVrmPath = path;
                        activeVrmManager.gameObject.SetActive(true);

                        cacheAccessOrder.Remove(path);
                        cacheAccessOrder.Add(path);

                        activeVrmManager.SetVisibility(true);
                        if (HideOriginalMesh.Value)
                            activeVrmManager.EnsureOriginalAvatarHidden();
                        VrmShaderTester.SetTarget(activeVrmManager.gameObject);

                        var cachedProfile = GetOrLoadProfile(path);
                        Vector3 pos = cachedProfile != null
                            ? cachedProfile.GetEffectivePosition(new Vector3(PosX.Value, PosY.Value, PosZ.Value))
                            : new Vector3(PosX.Value, PosY.Value, PosZ.Value);
                        Vector3 rot = cachedProfile != null
                            ? cachedProfile.GetEffectiveRotation(new Vector3(RotX.Value, RotY.Value, RotZ.Value))
                            : new Vector3(RotX.Value, RotY.Value, RotZ.Value);

                        activeVrmManager.ApplyGlobalPosition(pos);
                        activeVrmManager.ApplyGlobalOffset(rot);

                        return;
                    }
                }

                loadingVrm = true;
                ModLog.Info($"Loading VRM from: {path}");

                if (!File.Exists(path))
                {
                    ModLog.Info($"[WARN] File not found: {path}");
                    loadingVrm = false;
                    return;
                }

                GameObject vrmRoot = await VrmLoader.LoadVrmAsync(path);

                if (myToken != loadToken || giantess == null || giantess != currentGiantessTarget)
                {
                    if (vrmRoot != null) SafeDestroyAvatar(vrmRoot);
                    loadingVrm = false;
                    ModLog.Info("[WARN] VRM load became stale; aborting.");
                    return;
                }

                if (vrmRoot == null)
                {
                    loadingVrm = false;
                    return;
                }

                var profile = GetOrLoadProfile(path);
                float configuredStiffness = profile != null ? profile.GetEffectiveSpringBoneStiffness(SpringBoneStiffnessValue) : SpringBoneStiffnessValue;
                float configuredGravity = profile != null ? profile.GetEffectiveSpringBoneGravityPower(SpringBoneGravityPowerValue) : SpringBoneGravityPowerValue;
                float configuredDrag = profile != null ? profile.GetEffectiveSpringBoneDrag(SpringBoneDragValue) : SpringBoneDragValue;

                if (AutoSpringBone.Value)
                {
                    AutoSpringBoneGenerator.TryGenerate(
                        vrmRoot,
                        AutoSpringBoneKeywords.Value,
                        AutoSpringBoneMaxRoots.Value,
                        configuredStiffness,
                        configuredGravity,
                        configuredDrag,
                        DebugMode.Value,
                        AutoSpringBoneOnAvatarsWithBones.Value
                    );
                }

                if (giantess == null)
                {
                    SafeDestroyAvatar(vrmRoot);
                    loadingVrm = false;
                    ModLog.Info("[WARN] Giantess target was destroyed while loading VRM; aborting.");
                    return;
                }

                Animator vrmAnimator = vrmRoot.GetComponent<Animator>() ?? vrmRoot.GetComponentInChildren<Animator>(true);
                if (vrmAnimator == null)
                {
                    ModLog.Error("No Animator on VRM root or children; aborting. Check that the VRM has a valid humanoid rig.");
                    SafeDestroyAvatar(vrmRoot);
                    loadingVrm = false;
                    return;
                }

                float intrinsicRatio = 1f;
                float parentScaleY = giantess.transform.lossyScale.y;

                float vrmBoneHeight = GetBoneHeight(vrmAnimator);
                float vrmBoundsHeight = GetHeight(vrmRoot);
                float vrmBaseHeight = vrmBoneHeight > 0.1f ? vrmBoneHeight : vrmBoundsHeight;

                float sourceBoneHeightWorld = GetBoneHeight(giantess.animator);
                float sourceBoundsHeightWorld = GetHeight(giantess.gameObject);
                float sourceWorldHeight = sourceBoneHeightWorld > 0.1f ? sourceBoneHeightWorld : sourceBoundsHeightWorld;
                float sourceBaseHeight = parentScaleY > 0.01f ? sourceWorldHeight / parentScaleY : sourceWorldHeight;

                if (sourceBaseHeight > 0.1f && vrmBaseHeight > 0.1f)
                {
                    // Actual humanoid height is more reliable than Animator.humanScale
                    // for VRMs with unusual proportions or non-standard import scales.
                    intrinsicRatio = sourceBaseHeight / vrmBaseHeight;
                    ModLog.Debug($"Scale match: SourceHeight={sourceBaseHeight:F3}m, VRMHeight={vrmBaseHeight:F3}m, Ratio={intrinsicRatio:F3}");
                }
                else if (giantess.animator != null && giantess.animator.isHuman && vrmAnimator.isHuman)
                {
                    intrinsicRatio = giantess.animator.humanScale / Mathf.Max(vrmAnimator.humanScale, 0.01f);
                    ModLog.Debug($"Scale fallback HumanScale: Source={giantess.animator.humanScale:F3}, VRM={vrmAnimator.humanScale:F3}, Ratio={intrinsicRatio:F3}");
                }

                float multiplier = profile != null ? profile.GetEffectiveScaleMultiplier() : 1.0f;
                intrinsicRatio *= multiplier;

                float profileAbsOverride = profile != null ? profile.GetEffectiveScale(0f) : 0f;
                float finalScale = profileAbsOverride > 0.01f ? profileAbsOverride : intrinsicRatio;

                vrmRoot.transform.localScale = Vector3.one * finalScale;

                vrmRoot.transform.SetParent(giantess.transform, false);

                vrmRoot.transform.localPosition = Vector3.zero;
                vrmRoot.transform.localRotation = Quaternion.identity;

                if (giantess.scale > 0.01f)
                    ModLog.Debug($"Giantess.scale: {giantess.scale:F3}. VRM will inherit parent scale automatically.");

                activeVrmManager = vrmRoot.AddComponent<VrmManager>();
                activeVrmPath = path;

                foreach (var manager in loadedVrmCache.Values)
                {
                    if (manager != null) manager.gameObject.SetActive(false);
                }

                while (loadedVrmCache.Count >= MAX_CACHED_AVATARS && cacheAccessOrder.Count > 0)
                {
                    string oldestPath = cacheAccessOrder[0];
                    cacheAccessOrder.RemoveAt(0);

                    if (loadedVrmCache.TryGetValue(oldestPath, out var oldManager))
                    {
                        if (oldManager != null && oldManager.gameObject != null)
                        {
                            ModLog.Debug($"LRU Eviction: Destroying {Path.GetFileName(oldestPath)} to free memory");

                            SafeDestroyAvatar(oldManager.gameObject);
                        }
                        loadedVrmCache.Remove(oldestPath);
                    }
                }

                if (!loadedVrmCache.ContainsKey(path))
                {
                    loadedVrmCache.Add(path, activeVrmManager);
                    cacheAccessOrder.Add(path);
                }
                else
                {

                    loadedVrmCache[path] = activeVrmManager;
                    cacheAccessOrder.Remove(path);
                    cacheAccessOrder.Add(path);
                }

                string configPath = Path.Combine(Paths.PluginPath, "AvatarReplacer", "bone_mapping.json");
                var config = BoneMappingConfig.LoadOrCreateDefault(configPath);

                activeVrmManager.Setup(giantess, vrmAnimator, config, profile, DebugMode.Value);
                VrmShaderTester.SetTarget(activeVrmManager.gameObject);

                var firstOrigRenderer = giantess.GetComponentInChildren<Renderer>(true);
                if (firstOrigRenderer != null && firstOrigRenderer.sharedMaterial != null)
                {
                    ModLog.Debug($"Original Giantess shader template: {firstOrigRenderer.sharedMaterial.shader.name}");
                }

                SyncShadowSettings(giantess, vrmRoot);

                List<Renderer> hiddenRenderers = new List<Renderer>();
                Component variation = null;

                if (HideOriginalMesh.Value)
                {
                    (hiddenRenderers, variation) = HideOriginalAvatar(giantess, vrmRoot.transform);
                }

                activeVrmManager.SetOriginalRenderers(hiddenRenderers, variation);

                ModLog.Info("VRM Replacement successful.");
            }
            catch (Exception ex)
            {
                ModLog.Error($"Error during replacement: {ex.Message}");
                ModLog.ErrorException(ex);
            }
            finally
            {
                loadingVrm = false;
            }
        }

        private static float GetBoneHeight(Animator animator)
        {
            if (animator == null) return 1.8f;

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform lFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);

            if (head == null) return 1.8f;

            float footY = (lFoot != null && rFoot != null) ? (lFoot.position.y + rFoot.position.y) / 2f :
                          (lFoot != null ? lFoot.position.y : (rFoot != null ? rFoot.position.y : animator.transform.position.y));

            float worldHeight = Mathf.Abs(head.position.y - footY);

            ModLog.Debug($"Measured world height for {animator.name}: {worldHeight:F2}m (lossyScale.y={animator.transform.lossyScale.y:F2})");

            return worldHeight;
        }

        private static float GetHeight(GameObject obj)
        {

            var renderers = obj.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
            bool found = false;
            foreach (var r in renderers)
            {
                if (r.enabled && r.bounds.size.magnitude > 0)
                {
                    bounds.Encapsulate(r.bounds);
                    found = true;
                }
            }
            return found ? bounds.size.y : 1.8f;
        }

        private static void SyncShadowSettings(Giantess giantess, GameObject vrmRoot)
        {
            var originalRenderers = giantess.GetComponentsInChildren<Renderer>(true);
            var vrmRenderers = vrmRoot.GetComponentsInChildren<Renderer>(true);

            if (originalRenderers.Length == 0) return;

            var template = originalRenderers[0];
            foreach (var r in vrmRenderers)
            {
                r.shadowCastingMode = template.shadowCastingMode;
                r.receiveShadows = template.receiveShadows;

                if (r is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                }
            }
            ModLog.Debug($"Synchronized shadow settings from {template.name}");
        }

        private static (List<Renderer>, Component) HideOriginalAvatar(Giantess giantess, Transform exclude)
        {
            var renderers = giantess.GetComponentsInChildren<Renderer>(true);
            var hiddenRenderers = new List<Renderer>();

            foreach (var r in renderers)
            {
                if (!r.transform.IsChildOf(exclude))
                {
                    hiddenRenderers.Add(r);
                    r.enabled = false;
                }
            }

            Component variation = giantess.variation;
            if (variation != null)
            {
                var varType = variation.GetType();
                var enabledProp = varType.GetProperty("enabled");
                if (enabledProp != null)
                    enabledProp.SetValue(variation, false);
            }

            return (hiddenRenderers, variation);
        }

        private static VrmAvatarProfile GetOrLoadProfile(string vrmPath)
        {
            if (string.IsNullOrEmpty(vrmPath) || string.IsNullOrEmpty(configDir))
                return null;

            if (profileCache.TryGetValue(vrmPath, out var cached))
                return cached;

            var profile = VrmAvatarProfile.LoadForAvatar(vrmPath, configDir);
            profileCache[vrmPath] = profile;
            return profile;
        }

        private static void SafeDestroyAvatar(GameObject avatarRoot)
        {
            if (avatarRoot == null) return;

            var proxy = avatarRoot.GetComponent<VRM.VRMBlendShapeProxy>();
            if (proxy != null)
            {
                try
                {

                    proxy.BlendShapeAvatar = null;
                }
                catch (Exception ex)
                {
                    ModLog.Info($"[WARN] Failed to neutralize proxy: {ex.Message}");
                }
            }

            Destroy(avatarRoot);
        }
    }
}
