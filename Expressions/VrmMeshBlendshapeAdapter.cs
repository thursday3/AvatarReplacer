using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM;

namespace AvatarReplacer
{

    public enum BlendshapeCategory { Unknown, Eye, Mouth, Emotion }

    public class VrmMeshBlendshapeAdapter : IVrmExpressionAdapter
    {

        private readonly Dictionary<string, List<(SkinnedMeshRenderer renderer, int index)>> _nameToTargets =
            new Dictionary<string, List<(SkinnedMeshRenderer, int)>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> _clipNames = new List<string>();

        private readonly Dictionary<string, BlendshapeCategory> _nameToCategory =
            new Dictionary<string, BlendshapeCategory>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> _appliedThisFrame = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, float> _lastAppliedWeights = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private BlendshapeCategory _activeCategory = BlendshapeCategory.Unknown;
        private float _activeCategoryWeight = 0f;

        private static readonly string[] EyeKeywords = { "eye", "blink", "look", "lid", "iris", "pupil", "gaze" };
        private static readonly string[] MouthKeywords = { "mouth", "lip", "jaw", "tongue", "teeth", "chin" };
        private static readonly string[] EmotionKeywords = { "happy", "angry", "sad", "joy", "smile", "frown", "sorrow", "fun", "rage", "surprised" };
        private static readonly string[] VowelPatterns = { "_a", "_i", "_u", "_e", "_o", "aa", "ih", "ou", "ee", "oh", "vowel" };

        public VrmMeshBlendshapeAdapter(Transform vrmRoot)
        {
            if (vrmRoot == null) return;

            foreach (var smr in vrmRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;

                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (string.IsNullOrEmpty(name)) continue;

                    string simpleName = name;
                    int underscore = name.IndexOf('_');
                    if (underscore >= 0)
                        simpleName = name.Substring(underscore + 1);

                    RegisterBlendshape(simpleName, smr, i);
                    if (name != simpleName)
                        RegisterBlendshape(name, smr, i);
                }
            }

            if (_clipNames.Count > 0)
            {
                int eyeCount = _nameToCategory.Count(kv => kv.Value == BlendshapeCategory.Eye);
                int mouthCount = _nameToCategory.Count(kv => kv.Value == BlendshapeCategory.Mouth);
                int emotionCount = _nameToCategory.Count(kv => kv.Value == BlendshapeCategory.Emotion);
                ModLog.Info($"VrmMeshBlendshapeAdapter: Discovered {_clipNames.Count} mesh blendshapes (Eye:{eyeCount}, Mouth:{mouthCount}, Emotion:{emotionCount})");
            }
        }

        private void RegisterBlendshape(string name, SkinnedMeshRenderer smr, int index)
        {
            if (!_nameToTargets.ContainsKey(name))
            {
                _nameToTargets[name] = new List<(SkinnedMeshRenderer, int)>();
                _clipNames.Add(name);
                _nameToCategory[name] = ClassifyBlendshape(name);
            }
            _nameToTargets[name].Add((smr, index));
        }

        private BlendshapeCategory ClassifyBlendshape(string name)
        {
            string lower = name.ToLowerInvariant();

            foreach (var kw in EmotionKeywords)
                if (lower.Contains(kw)) return BlendshapeCategory.Emotion;

            foreach (var pattern in VowelPatterns)
                if (lower.Contains(pattern)) return BlendshapeCategory.Mouth;

            foreach (var kw in EyeKeywords)
                if (lower.Contains(kw)) return BlendshapeCategory.Eye;

            foreach (var kw in MouthKeywords)
                if (lower.Contains(kw)) return BlendshapeCategory.Mouth;

            return BlendshapeCategory.Unknown;
        }

        public IEnumerable<string> GetClipNames() => _clipNames;

        public bool HasClip(BlendShapePreset preset)
        {
            string key = PresetToStandardKey(preset);
            if (string.IsNullOrEmpty(key)) return false;
            BlendshapeCategory category = ClassifyStandardKey(key);
            string normalized = NormalizeKey(key);
            foreach (var clip in _clipNames)
            {
                string clipNormalized = NormalizeKey(clip);
                if (IsStrictMatch(normalized, clipNormalized, category)) return true;
            }
            return false;
        }

        public void ImmediatelySetValue(BlendShapePreset preset, float value)
        {
            ApplyWithStandardMapping(PresetToStandardKey(preset), value);
        }

        public void ImmediatelySetValue(string key, float value)
        {
            if (string.IsNullOrEmpty(key)) return;
            ApplyBlendshapeWeight(key, value);
        }

        public bool HasClip(string key)
        {
            return !string.IsNullOrEmpty(key) && _nameToTargets.ContainsKey(key);
        }

        public void BeginFrame()
        {
            _appliedThisFrame.Clear();
            _activeCategory = BlendshapeCategory.Unknown;
            _activeCategoryWeight = 0f;
        }

        public void EndFrame()
        {

            var keysToRemove = new List<string>();

            foreach (var key in _lastAppliedWeights.Keys.ToList())
            {
                float val = _lastAppliedWeights[key];
                if (!_appliedThisFrame.Contains(key) && val > 0.001f)
                {

                    float decayed = Mathf.MoveTowards(val, 0f, Time.deltaTime * 8f);
                    SetBlendshapeDirectly(key, decayed);

                    if (decayed <= 0.001f)
                        keysToRemove.Add(key);
                    else
                        _lastAppliedWeights[key] = decayed;
                }
            }

            foreach (var key in keysToRemove)
                _lastAppliedWeights.Remove(key);
        }

        public void ApplyWithPriority(string standardKey, float value, BlendshapeCategory requestedCategory)
        {
            if (string.IsNullOrEmpty(standardKey) || _clipNames.Count == 0 || value < 0.01f) return;

            if (requestedCategory == BlendshapeCategory.Emotion && value > _activeCategoryWeight)
            {
                _activeCategory = BlendshapeCategory.Emotion;
                _activeCategoryWeight = value;
            }

            if (requestedCategory == BlendshapeCategory.Eye &&
                _activeCategory == BlendshapeCategory.Emotion &&
                _activeCategoryWeight > 0.5f)
            {
                return;
            }

            string k = NormalizeKey(standardKey);

            foreach (var clipName in _clipNames)
            {

                if (_nameToCategory.TryGetValue(clipName, out var clipCategory))
                {
                    if (requestedCategory != BlendshapeCategory.Unknown &&
                        clipCategory != BlendshapeCategory.Unknown &&
                        clipCategory != requestedCategory)
                        continue;
                }

                string n = NormalizeKey(clipName);
                if (IsStrictMatch(k, n, requestedCategory))
                {
                    ApplyBlendshapeWeight(clipName, value);
                }
            }
        }

        public void ApplyWithStandardMapping(string standardKey, float value)
        {
            var category = ClassifyStandardKey(standardKey);
            ApplyWithPriority(standardKey, value, category);
        }

        private BlendshapeCategory ClassifyStandardKey(string key)
        {
            string k = key.ToLowerInvariant();

            if (k.Contains("blink") || k.Contains("look")) return BlendshapeCategory.Eye;

            if (k == "a" || k == "aa" || k == "i" || k == "ih" ||
                k == "u" || k == "ou" || k == "e" || k == "ee" ||
                k == "o" || k == "oh") return BlendshapeCategory.Mouth;

            if (k.Contains("happy") || k.Contains("angry") || k.Contains("sad") ||
                k.Contains("smile") || k.Contains("joy")) return BlendshapeCategory.Emotion;

            return BlendshapeCategory.Unknown;
        }

        private string NormalizeKey(string key)
        {
            return key.ToLowerInvariant()
                .Replace("_", "")
                .Replace(".", "")
                .Replace(" ", "")
                .Replace("-", "");
        }

        private bool IsStrictMatch(string standardKey, string clipName, BlendshapeCategory category)
        {

            if (standardKey == clipName) return true;

            switch (category)
            {
                case BlendshapeCategory.Eye:

                    if (standardKey.Contains("blink"))
                    {
                        if (!clipName.Contains("blink") && !clipName.Contains("eyeclose")) return false;

                        if (standardKey.Contains("left") && !clipName.Contains("left") && !clipName.Contains("l")) return false;
                        if (standardKey.Contains("right") && !clipName.Contains("right") && !clipName.Contains("r")) return false;
                        if (!standardKey.Contains("left") && !standardKey.Contains("right"))
                            return !clipName.Contains("left") && !clipName.Contains("right") && !clipName.EndsWith("l") && !clipName.EndsWith("r");
                        return true;
                    }

                    if (standardKey.Contains("look"))
                    {
                        return clipName.Contains("look") &&
                               (standardKey.Contains("left") == clipName.Contains("left")) &&
                               (standardKey.Contains("right") == clipName.Contains("right")) &&
                               (standardKey.Contains("up") == clipName.Contains("up")) &&
                               (standardKey.Contains("down") == clipName.Contains("down"));
                    }
                    break;

                case BlendshapeCategory.Mouth:

                    if (standardKey == "a" || standardKey == "aa")
                        return clipName == "aa" || clipName.EndsWith("moutha") || clipName == "mtha" || clipName.Contains("vowela");
                    if (standardKey == "i" || standardKey == "ih")
                        return clipName == "ih" || clipName.EndsWith("mouthi") || clipName == "mthi" || clipName.Contains("voweli");
                    if (standardKey == "u" || standardKey == "ou")
                        return clipName == "ou" || clipName.EndsWith("mouthu") || clipName == "mthu" || clipName.Contains("vowelu");
                    if (standardKey == "e" || standardKey == "ee")
                        return clipName == "ee" || clipName.EndsWith("mouthe") || clipName == "mthe" || clipName.Contains("vowele");
                    if (standardKey == "o" || standardKey == "oh")
                        return clipName == "oh" || clipName.EndsWith("moutho") || clipName == "mtho" || clipName.Contains("vowelo");
                    break;

                case BlendshapeCategory.Emotion:

                    if (standardKey.Contains("happy") && (clipName.Contains("happy") || clipName.Contains("smile") || clipName.Contains("joy")))
                        return true;
                    if (standardKey.Contains("angry") && clipName.Contains("angry"))
                        return true;
                    if (standardKey.Contains("sad") && (clipName.Contains("sad") || clipName.Contains("sorrow")))
                        return true;
                    break;
            }

            return clipName.Contains(standardKey) || standardKey.Contains(clipName);
        }

        private static string PresetToStandardKey(BlendShapePreset preset)
        {
            switch (preset)
            {
                case BlendShapePreset.LookLeft: return "lookleft";
                case BlendShapePreset.LookRight: return "lookright";
                case BlendShapePreset.LookUp: return "lookup";
                case BlendShapePreset.LookDown: return "lookdown";
                case BlendShapePreset.A: return "aa";
                case BlendShapePreset.I: return "ih";
                case BlendShapePreset.U: return "ou";
                case BlendShapePreset.E: return "ee";
                case BlendShapePreset.O: return "oh";
                case BlendShapePreset.Blink: return "blink";
                case BlendShapePreset.Blink_L: return "blinkleft";
                case BlendShapePreset.Blink_R: return "blinkright";
                case BlendShapePreset.Joy: return "happy";
                case BlendShapePreset.Angry: return "angry";
                case BlendShapePreset.Sorrow: return "sad";
                case BlendShapePreset.Fun: return "relaxed";
                case BlendShapePreset.Neutral: return "neutral";
                default: return preset.ToString().ToLowerInvariant();
            }
        }

        private void ApplyBlendshapeWeight(string name, float weight)
        {
            _appliedThisFrame.Add(name);
            _lastAppliedWeights[name] = weight;
            SetBlendshapeDirectly(name, weight);
        }

        private void SetBlendshapeDirectly(string name, float weight)
        {
            if (!_nameToTargets.TryGetValue(name, out var list)) return;

            float w = Mathf.Clamp01(weight) * 100f;
            foreach (var (renderer, index) in list)
                if (renderer != null)
                    renderer.SetBlendShapeWeight(index, w);
        }

        public int DiscoveredClipCount => _clipNames.Count;
    }
}
