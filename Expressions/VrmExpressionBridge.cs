using System;
using System.Collections.Generic;
using UnityEngine;
using VRM;
using System.Linq;

namespace AvatarReplacer
{
    public class VrmExpressionBridge : MonoBehaviour
    {
        private GiantessExpressionManager sourceExpressionManager;
        private IVrmExpressionAdapter vrmAdapter;

        private Dictionary<BlendShapePreset, float> currentEyeWeights = new Dictionary<BlendShapePreset, float>();
        private Dictionary<BlendShapePreset, float> currentMouthWeights = new Dictionary<BlendShapePreset, float>();
        private float currentBlinkWeight = 0f;

        private const float SmoothingSpeed = 15f;
        private const float BlinkSmoothingSpeed = 35f;

        private Dictionary<string, BlendShapePreset> eyeExpressionMap = new Dictionary<string, BlendShapePreset>();
        private Dictionary<string, BlendShapePreset> mouthExpressionMap = new Dictionary<string, BlendShapePreset>();

        private Dictionary<string, string> eyeCustomMap = new Dictionary<string, string>();
        private Dictionary<string, string> mouthCustomMap = new Dictionary<string, string>();
        private Dictionary<string, float> currentCustomWeights = new Dictionary<string, float>();


        private Dictionary<string, string> normalizedMouthKeys = new Dictionary<string, string>();
        private Dictionary<string, string> normalizedCustomValues = new Dictionary<string, string>();
        private string blinkStringClip;

        private CustomExpressionMap customMap;
        private string customBlinkClip;

        public void Initialize(Giantess giantess, IVrmExpressionAdapter adapter)
        {
            sourceExpressionManager = giantess.expressionManager;
            vrmAdapter = adapter;

            SetupDefaultMappings();

            AutoDiscoverAndMapExpressions();

            ApplyCustomMap();

            ModLog.Info($"Expression sync initialized. Mapped {eyeExpressionMap.Count + eyeCustomMap.Count} eyes and {mouthExpressionMap.Count + mouthCustomMap.Count} mouths.");

            InitializeWeights();

            currentBlinkWeight = 0f;

            blinkStringClip = FindBlinkClipName();

            foreach (var kvp in mouthExpressionMap)
            {
                normalizedMouthKeys[kvp.Key] = Normalize(kvp.Key);
            }
            foreach (var kvp in mouthCustomMap)
            {
                normalizedMouthKeys[kvp.Key] = Normalize(kvp.Key);
                normalizedCustomValues[kvp.Value] = Normalize(kvp.Value);
            }
        }

        private void AutoDiscoverAndMapExpressions()
        {
            if (sourceExpressionManager == null || vrmAdapter == null) return;

            var vrmNames = vrmAdapter.GetClipNames().ToList();
            if (vrmNames.Count == 0) return;

            var normalize = new Func<string, string>(s => s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", ""));

            ModLog.Debug($"VrmExpressionBridge: VRM has {vrmNames.Count} blendshapes: {string.Join(", ", vrmNames)}");

            Dictionary<string, string> normalizedVrmNames = new Dictionary<string, string>();
            foreach(var vn in vrmNames)
            {
                string norm = normalize(vn);
                if (!normalizedVrmNames.ContainsKey(norm)) normalizedVrmNames[norm] = vn;
            }

            HashSet<string> allGameKeys = new HashSet<string>();
            try
            {

                var fields = sourceExpressionManager.GetType().GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                foreach(var f in fields)
                {
                    object value = null;
                    try { value = f.GetValue(sourceExpressionManager); } catch {}
                    if (value == null) continue;

                    if (f.FieldType.Name.Contains("Dictionary") && f.FieldType.GenericTypeArguments.Length > 0 && f.FieldType.GenericTypeArguments[0] == typeof(string))
                    {
                        var dict = value as System.Collections.IDictionary;
                        if (dict != null)
                        {
                            foreach(var key in dict.Keys) if (key is string s) allGameKeys.Add(s);
                        }
                    }

                    else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(string))
                    {
                        var enumerable = value as System.Collections.IEnumerable;
                        if (enumerable != null)
                        {
                            foreach(var item in enumerable)
                            {
                                TryExtractName(item, allGameKeys);
                            }
                        }
                    }

                    else
                    {
                        TryExtractName(value, allGameKeys);
                    }
                }

                var sortedKeys = allGameKeys.OrderBy(k => k).ToList();
                ModLog.Debug($"VrmExpressionBridge: Discovered {allGameKeys.Count} unique expression keys: {string.Join(", ", sortedKeys)}");
            }
            catch(Exception e) { ModLog.Error($"VrmExpressionBridge: Error reflecting expressions: {e.Message}"); }

            foreach(var gameKey in allGameKeys)
            {

                bool isEye = gameKey.ToLower().Contains("eyes") || gameKey.ToLower().Contains("look");
                bool isMouth = gameKey.ToLower().Contains("mouth");

                if (isEye && (eyeExpressionMap.ContainsKey(gameKey) || eyeCustomMap.ContainsKey(gameKey))) continue;
                if (isMouth && (mouthExpressionMap.ContainsKey(gameKey) || mouthCustomMap.ContainsKey(gameKey))) continue;

                string cleanGameKey = normalize(gameKey.Replace("eyes", "").Replace("mouth", ""));

                if (normalizedVrmNames.TryGetValue(cleanGameKey, out string vrmMatch))
                {

                    // Skip look presets AND any standard VRM preset name: those are
                    // already driven through SetupDefaultMappings/preset adapters,
                    // and a second custom mapping would double-drive the same
                    // expression with a competing weight each frame.
                    if (IsStandardPresetName(vrmMatch)) {
                         ModLog.Debug($"VrmExpressionBridge: Skipping standard preset match: '{gameKey}' -> '{vrmMatch}'");
                         continue;
                    }

                    if (isEye) eyeCustomMap[gameKey] = vrmMatch;
                    else if (isMouth) mouthCustomMap[gameKey] = vrmMatch;
                    else eyeCustomMap[gameKey] = vrmMatch;

                    ModLog.Debug($"VrmExpressionBridge: Auto-Mapped '{gameKey}' -> '{vrmMatch}'");
                    continue;
                }

                foreach(var vn in vrmNames)
                {
                    string cleanVrm = normalize(vn);

                    if (cleanVrm.Length < 3) continue;

                    if (cleanVrm.Contains(cleanGameKey) || cleanGameKey.Contains(cleanVrm))
                    {

                         if (IsStandardPresetName(vn)) continue;

                         if (isEye) eyeCustomMap[gameKey] = vn;
                         else if (isMouth) mouthCustomMap[gameKey] = vn;
                         else eyeCustomMap[gameKey] = vn;

                         ModLog.Debug($"VrmExpressionBridge: Fuzzy-Mapped '{gameKey}' -> '{vn}'");
                         break;
                    }
                }
            }
        }

        /// <summary>
        /// Applies the user-editable template map (config/expression_map.json).
        /// Every filled row REPLACES whatever default/auto mapping exists for
        /// that game expression, so users can fix or completely rewire a
        /// mapping per avatar without touching code. Rows with an empty
        /// 'vrmBlendShape' are ignored (that is what keeps the feature off).
        /// </summary>
        private void ApplyCustomMap()
        {
            customMap = null;
            customBlinkClip = null;

            if (vrmAdapter == null) return;

            // Avatar-aware resolution: per-VRM map file first, then the shared map.
            var map = CustomExpressionMap.LoadForAvatar(
                AvatarReplacerPlugin.ConfigDirectory, AvatarReplacerPlugin.ActiveVrmPath);
            if (map == null) return;

            customMap = map;
            int applied = 0;
            foreach (var entry in map.ActiveMappings())
            {
                string game = entry.gameExpression;
                string vrm = entry.vrmBlendShape.Trim();
                if (string.IsNullOrWhiteSpace(game)) continue;

                // Resolve the target: accept either a raw clip name that exists
                // on the avatar, or a standard preset name (happy, blink, aa...).
                string target = null;
                if (vrmAdapter.HasClip(vrm))
                {
                    target = vrm;
                }
                else
                {
                    foreach (var name in vrmAdapter.GetClipNames())
                    {
                        if (string.Equals(Normalize(name), Normalize(vrm), StringComparison.Ordinal))
                        {
                            target = name;
                            break;
                        }
                    }
                }

                if (target == null)
                {
                    ModLog.Info($"[WARN] Custom map: VRM has no clip '{vrm}' for game expression '{game}'. Row ignored.");
                    continue;
                }

                if (game.Equals("Blink", StringComparison.OrdinalIgnoreCase))
                {
                    customBlinkClip = target;
                }
                else
                {
                    // An override REPLACES the default preset mapping and any
                    // auto-discovered mapping for this game expression; leaving
                    // them in place would drive the old target in parallel.
                    eyeExpressionMap.Remove(game);
                    mouthExpressionMap.Remove(game);
                    eyeCustomMap.Remove(game);
                    mouthCustomMap.Remove(game);

                    // Route by key type, mirroring auto-discovery: eye keys go
                    // through the eye path (no lip-sync suppression), mouth keys
                    // through the mouth path (respects lip-sync priority).
                    bool isEye = game.ToLowerInvariant().Contains("eyes") || game.ToLowerInvariant().Contains("look");
                    bool isMouth = game.ToLowerInvariant().Contains("mouth");
                    if (isMouth) mouthCustomMap[game] = target;
                    else eyeCustomMap[game] = target;
                }

                applied++;
                ModLog.Info($"Custom map: '{game}' -> '{target}'");
            }

            if (applied > 0)
                ModLog.Info($"Custom expression map: {applied} override(s) applied over auto-mapping.");
        }

        private bool IsLookPresetName(string name)
        {
            string lowerness = name.ToLower();
            return lowerness.Contains("lookup") || lowerness.Contains("lookdown") ||
                   lowerness.Contains("lookleft") || lowerness.Contains("lookright");
        }

        /// <summary>
        /// True for names that normalize to a standard VRM 0.x/1.0 preset
        /// (neutral, vowels, blink, emotions, look directions). Auto-discovery
        /// must not create custom mappings to these; they are owned by the
        /// preset adapter paths and double-driving causes wrong/stacked faces.
        /// </summary>
        private static readonly HashSet<string> StandardPresetNames = new HashSet<string>
        {
            "neutral", "a", "i", "u", "e", "o", "aa", "ih", "ou", "ee", "oh",
            "blink", "blinkl", "blinkr", "blinkleft", "blinkright",
            "joy", "angry", "sorrow", "fun", "happy", "sad", "relaxed",
            "lookup", "lookdown", "lookleft", "lookright"
        };

        private bool IsStandardPresetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = Normalize(name);
            if (StandardPresetNames.Contains(n)) return true;
            return IsLookPresetName(name);
        }

        private void TryExtractName(object item, HashSet<string> keys)
        {
            if (item == null) return;
            try
            {
                var type = item.GetType();

                var displayField = type.GetField("displayName");
                if (displayField != null)
                {
                    var val = displayField.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(val)) { keys.Add(val); return; }
                }

                var bnField = type.GetField("blendShapeName");
                if (bnField != null)
                {
                    var val = bnField.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(val)) { keys.Add(val); return; }
                }

                var nameProp = type.GetProperty("name");
                if (nameProp != null && nameProp.CanRead)
                {
                    var val = nameProp.GetValue(item) as string;
                    if (!string.IsNullOrEmpty(val)) keys.Add(val);
                }
            }
            catch {}
        }

        private void SetupDefaultMappings()
        {

            void MapEye(string k, BlendShapePreset v) { eyeExpressionMap[k] = v; }
            void MapMouth(string k, BlendShapePreset v) { mouthExpressionMap[k] = v; }

            MapEye("happy eyes", BlendShapePreset.Neutral);
            MapEye("serious eyes", BlendShapePreset.Neutral);
            MapEye("sad eyes", BlendShapePreset.Sorrow);
            MapEye("angry eyes", BlendShapePreset.Angry);
            MapEye("hurt eyes", BlendShapePreset.Sorrow);
            MapEye("smug eyes", BlendShapePreset.Fun);
            MapEye("shock eyes", BlendShapePreset.Fun);
            MapEye("vore eyes", BlendShapePreset.Neutral);
            MapEye("laugh eyes", BlendShapePreset.Neutral);
            MapEye("cry eyes", BlendShapePreset.Sorrow);
            MapEye("frown eyes", BlendShapePreset.Sorrow);
            MapEye("screaming eyes", BlendShapePreset.Fun);
            MapEye("mad eyes", BlendShapePreset.Angry);
            MapEye("shy eyes", BlendShapePreset.Neutral);
            MapEye("embarrased eyes", BlendShapePreset.Neutral);
            MapEye("neutral tilt eyes", BlendShapePreset.Neutral);

            MapEye("anger large", BlendShapePreset.Angry);
            MapEye("anger small", BlendShapePreset.Angry);
            MapEye("tears sad", BlendShapePreset.Sorrow);
            MapEye("tears laugh", BlendShapePreset.Joy);
            MapEye("tilt", BlendShapePreset.Neutral);

            MapMouth("happy mouth", BlendShapePreset.Joy);
            MapMouth("serious mouth", BlendShapePreset.Neutral);
            MapMouth("sad mouth", BlendShapePreset.Sorrow);
            MapMouth("angry mouth", BlendShapePreset.Angry);
            MapMouth("hurt mouth", BlendShapePreset.Sorrow);
            MapMouth("smug mouth", BlendShapePreset.Fun);
            MapMouth("shock mouth", BlendShapePreset.O);

            MapMouth("smile mouth", BlendShapePreset.Neutral);

            MapMouth("a mouth", BlendShapePreset.A);
            MapMouth("i mouth", BlendShapePreset.I);
            MapMouth("u mouth", BlendShapePreset.U);
            MapMouth("e mouth", BlendShapePreset.E);
            MapMouth("o mouth", BlendShapePreset.O);
            MapMouth("vore mouth", BlendShapePreset.A);

            MapMouth("laugh mouth", BlendShapePreset.Joy);
            MapMouth("cry mouth", BlendShapePreset.Sorrow);
            MapMouth("frown mouth", BlendShapePreset.Sorrow);
            MapMouth("screaming mouth", BlendShapePreset.O);
            MapMouth("mad mouth", BlendShapePreset.Angry);
            MapMouth("shy mouth", BlendShapePreset.Neutral);
            MapMouth("embarrased mouth", BlendShapePreset.Neutral);
            MapMouth("neutral tilt mouth", BlendShapePreset.Neutral);

            MapMouth("Mouth Happy Alt", BlendShapePreset.Joy);
            MapMouth("Mouth Sad 3", BlendShapePreset.Sorrow);
            MapMouth("Mouth Screaming", BlendShapePreset.O);
            MapMouth("Mouth Smug", BlendShapePreset.Fun);
            MapMouth("Mouth Disgust", BlendShapePreset.Angry);
            MapMouth("Mouth Empty", BlendShapePreset.Neutral);

            MapEye("Eyes Bored", BlendShapePreset.Neutral);
            MapEye("Eyes Disgust", BlendShapePreset.Angry);
            MapEye("Eyes Empty", BlendShapePreset.Neutral);
            MapEye("Eyes Sad 3", BlendShapePreset.Sorrow);
            MapEye("Eyes Screaming", BlendShapePreset.Fun);
            MapEye("Eyes Screaming Alt 2", BlendShapePreset.Fun);
            MapEye("Eyes Sleepy", BlendShapePreset.Neutral);

            ModLog.Debug("VrmExpressionBridge: Applied default hardcoded mappings (Extended).");
        }

        private void InitializeWeights()
        {

            foreach (var preset in Enum.GetValues(typeof(BlendShapePreset)))
            {
                BlendShapePreset p = (BlendShapePreset)preset;
                if (!currentEyeWeights.ContainsKey(p)) currentEyeWeights[p] = 0f;
                if (!currentMouthWeights.ContainsKey(p)) currentMouthWeights[p] = 0f;
            }
        }

        private BlendShapePreset? debugOverridePreset = null;
        private string debugOverrideKey = null;

        public void SetDebugOverride(BlendShapePreset preset)
        {
            debugOverridePreset = preset;
            debugOverrideKey = null;
        }

        public void SetDebugOverride(string customKey)
        {
            debugOverrideKey = customKey;
            debugOverridePreset = null;
        }

        public void ClearDebugOverride()
        {
            debugOverridePreset = null;
            debugOverrideKey = null;
        }

        void LateUpdate()
        {
            if (sourceExpressionManager == null || vrmAdapter == null) return;

            if (debugOverridePreset != null || debugOverrideKey != null)
            {
                ApplyDebugOverride();
            }
            else
            {
                SyncExpressions();
                SyncBlink();
            }
        }

        private void ApplyDebugOverride()
        {

            frameTargets.Clear();
            foreach (var key in currentEyeWeights.Keys) frameTargets[key] = 0f;
            foreach (var key in currentMouthWeights.Keys) frameTargets[key] = 0f;

            customFrameTargets.Clear();
            foreach (var key in activeCustomKeys) customFrameTargets[key] = 0f;

            if (debugOverridePreset != null)
            {
                float target = 1.0f;

                if (debugOverridePreset.Value == BlendShapePreset.Joy)
                    target = 0.4f;

                frameTargets[debugOverridePreset.Value] = target;
            }

            if (debugOverrideKey != null)
            {
                customFrameTargets[debugOverrideKey] = 1.0f;
            }

            foreach (var kvp in frameTargets)
            {
                if (currentEyeWeights.ContainsKey(kvp.Key))
                    UpdateWeight(kvp.Key, kvp.Value, currentEyeWeights);
                else if (currentMouthWeights.ContainsKey(kvp.Key))
                     UpdateWeight(kvp.Key, kvp.Value, currentMouthWeights);
            }
            UpdateCustomWeights();
        }

        private Dictionary<BlendShapePreset, float> frameTargets = new Dictionary<BlendShapePreset, float>();
        private Dictionary<string, float> customFrameTargets = new Dictionary<string, float>();
        private static readonly HashSet<string> lipSyncKeywords = new HashSet<string> { "a", "i", "u", "e", "o", "amouth", "imouth", "umouth", "emouth", "omouth" };

        private HashSet<string> activeCustomKeys = new HashSet<string>();

        private void SyncExpressions()
        {
            if (sourceExpressionManager == null) return;

            frameTargets.Clear();
            foreach (var key in currentEyeWeights.Keys) frameTargets[key] = 0f;
            foreach (var key in currentMouthWeights.Keys) frameTargets[key] = 0f;

            customFrameTargets.Clear();
            foreach (var key in activeCustomKeys) customFrameTargets[key] = 0f;

            float GetWeight(string key) => GetGameExpressionWeight(key);

            float maxLipSyncWeight = 0f;

            foreach (var kvp in mouthExpressionMap)
            {
                float w = GetWeight(kvp.Key);
                if (w > 0.01f)
                {
                    string k = normalizedMouthKeys.TryGetValue(kvp.Key, out var nk) ? nk : Normalize(kvp.Key);
                    if (lipSyncKeywords.Contains(k) || k.EndsWith("mouth") && k.Length < 7)
                    {
                        if (w > maxLipSyncWeight) maxLipSyncWeight = w;
                    }
                }
            }
            foreach (var kvp in mouthCustomMap)
            {
                float w = GetWeight(kvp.Key);
                if (w > 0.01f)
                {
                     string normVal = normalizedCustomValues.TryGetValue(kvp.Value, out var nv) ? nv : Normalize(kvp.Value);
                     // Recognize both single vowels and VRM standard vowel keys so
                     // custom rows like "a mouth" -> "aa" count as lip sync.
                     if (normVal == "a" || normVal == "i" || normVal == "u" || normVal == "e" || normVal == "o" ||
                         normVal == "aa" || normVal == "ih" || normVal == "ou" || normVal == "ee" || normVal == "oh")
                     {
                         if (w > maxLipSyncWeight) maxLipSyncWeight = w;
                     }
                }
            }

            foreach (var kvp in eyeExpressionMap)
            {
                float w = GetWeight(kvp.Key);
                if (w > 0.01f)
                {

                    if (!frameTargets.ContainsKey(kvp.Value)) frameTargets[kvp.Value] = 0f;
                    frameTargets[kvp.Value] = Mathf.Max(frameTargets[kvp.Value], w);
                }
            }
            foreach (var kvp in eyeCustomMap)
            {
                float w = GetWeight(kvp.Key);
                if (w > 0.01f)
                {
                    if (!customFrameTargets.ContainsKey(kvp.Value)) customFrameTargets[kvp.Value] = 0f;
                    customFrameTargets[kvp.Value] = Mathf.Max(customFrameTargets[kvp.Value], w);
                }
            }

            foreach (var kvp in mouthExpressionMap)
            {
                float w = GetWeight(kvp.Key);
                if (w <= 0.01f) continue;

                BlendShapePreset preset = kvp.Value;

                bool isLipSync = preset == BlendShapePreset.A || preset == BlendShapePreset.I ||
                                 preset == BlendShapePreset.U || preset == BlendShapePreset.E ||
                                 preset == BlendShapePreset.O;
                if (!isLipSync)
                    w *= 1f - Mathf.Clamp01(maxLipSyncWeight);
                if (w <= 0.01f) continue;

                if (preset == BlendShapePreset.Joy)
                {
                    w *= 0.4f;
                }

                if (!frameTargets.ContainsKey(preset)) frameTargets[preset] = 0f;
                frameTargets[preset] = Mathf.Max(frameTargets[preset], w);
            }

            foreach (var kvp in mouthCustomMap)
            {
                float w = GetWeight(kvp.Key);
                if (w <= 0.01f) continue;

                string v = normalizedCustomValues.TryGetValue(kvp.Value, out var nv) ? nv : Normalize(kvp.Value);
                bool isLipSync = v == "a" || v == "i" || v == "u" || v == "e" || v == "o" ||
                                 v == "aa" || v == "ih" || v == "ou" || v == "ee" || v == "oh";
                if (!isLipSync)
                    w *= 1f - Mathf.Clamp01(maxLipSyncWeight);
                if (w <= 0.01f) continue;

                if (!customFrameTargets.ContainsKey(kvp.Value)) customFrameTargets[kvp.Value] = 0f;
                customFrameTargets[kvp.Value] = Mathf.Max(customFrameTargets[kvp.Value], w);
            }

            foreach (var kvp in frameTargets)
            {

                if (currentEyeWeights.ContainsKey(kvp.Key))
                    UpdateWeight(kvp.Key, kvp.Value, currentEyeWeights);
                else if (currentMouthWeights.ContainsKey(kvp.Key))
                     UpdateWeight(kvp.Key, kvp.Value, currentMouthWeights);
            }

            UpdateCustomWeights();
        }

        private void UpdateCustomWeights()
        {

             foreach (var kvp in customFrameTargets)
             {
                 UpdateCustomWeight(kvp.Key, kvp.Value);
             }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        }

        private float GetGameExpressionWeight(string gameKey)
        {
            if (sourceExpressionManager == null) return 0f;
            var expr = sourceExpressionManager.TryGetExpression(gameKey);
            if (expr != null)
            {
                return Mathf.Clamp01((expr.weight * expr.weightMultiplier) / 100f);
            }
            return 0f;
        }

        private void UpdateCustomWeight(string blendShapeName, float target)
        {

            if (!currentCustomWeights.ContainsKey(blendShapeName)) currentCustomWeights[blendShapeName] = 0f;

            float current = currentCustomWeights[blendShapeName];
            float next = Mathf.MoveTowards(current, target, Time.deltaTime * SmoothingSpeed);
            currentCustomWeights[blendShapeName] = next;

            const float eps = 0.001f;
            if (next <= eps && target <= eps)
            {
                // Fully idle: write one final zero when clearing, then stop
                // touching the blendshape every frame (previously re-wrote 0
                // for every tracked expression on every frame).
                if (current > eps)
                {
                    vrmAdapter.ImmediatelySetValue(blendShapeName, 0f);
                }
                activeCustomKeys.Remove(blendShapeName);
                return;
            }

            vrmAdapter.ImmediatelySetValue(blendShapeName, next);
            activeCustomKeys.Add(blendShapeName);
        }

        private void UpdateWeight(BlendShapePreset preset, float target, Dictionary<BlendShapePreset, float> weights)
        {
            if (vrmAdapter.HasClip(preset))
            {
                float current = weights[preset];
                float next = Mathf.MoveTowards(current, target, Time.deltaTime * SmoothingSpeed);
                weights[preset] = next;

                const float eps = 0.001f;
                if (next <= eps && target <= eps)
                {
                    // Idle: final clear write once, then go quiet.
                    if (current > eps)
                    {
                        vrmAdapter.ImmediatelySetValue(preset, 0f);
                    }
                    return;
                }

                vrmAdapter.ImmediatelySetValue(preset, next);
            }
        }

        private string FindBlinkClipName()
        {
            if (vrmAdapter == null) return null;
            var candidates = new[] { "blink", "eyeclose", "closeeye" };
            foreach (var n in vrmAdapter.GetClipNames())
            {
                if (string.IsNullOrEmpty(n)) continue;
                var lower = n.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
                foreach (var c in candidates)
                    if (lower == c || lower.Contains(c)) return n;
            }
            return null;
        }

        private void SyncBlink()
        {
            // Use TryGetExpression: GetExpression throws KeyNotFoundException
            // when the key is absent (verified in game ExpressionManager).
            var blinkExpr = sourceExpressionManager.TryGetExpression("Blink");
            float targetBlinkWeight = (blinkExpr != null) ? ((blinkExpr.weight * blinkExpr.weightMultiplier) / 100f) : 0f;
            currentBlinkWeight = Mathf.MoveTowards(currentBlinkWeight, targetBlinkWeight, Time.deltaTime * BlinkSmoothingSpeed);

            // Template-map override: route blink through the mapped clip.
            if (!string.IsNullOrEmpty(customBlinkClip))
            {
                vrmAdapter.ImmediatelySetValue(customBlinkClip, currentBlinkWeight);
                return;
            }

            bool hasBlink = vrmAdapter.HasClip(BlendShapePreset.Blink);
            bool hasBlinkL = vrmAdapter.HasClip(BlendShapePreset.Blink_L);
            bool hasBlinkR = vrmAdapter.HasClip(BlendShapePreset.Blink_R);

            if (hasBlink)
                vrmAdapter.ImmediatelySetValue(BlendShapePreset.Blink, currentBlinkWeight);
            else if (hasBlinkL && hasBlinkR)
            {
                vrmAdapter.ImmediatelySetValue(BlendShapePreset.Blink_L, currentBlinkWeight);
                vrmAdapter.ImmediatelySetValue(BlendShapePreset.Blink_R, currentBlinkWeight);
            }
            else if (!string.IsNullOrEmpty(blinkStringClip))
                vrmAdapter.ImmediatelySetValue(blinkStringClip, currentBlinkWeight);
        }
    }
}
