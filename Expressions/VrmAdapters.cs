using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM;
using UniVRM10;

namespace AvatarReplacer
{

    public interface IVrmExpressionAdapter
    {
        void ImmediatelySetValue(BlendShapePreset preset, float value);
        void ImmediatelySetValue(string key, float value);
        IEnumerable<string> GetClipNames();
        bool HasClip(BlendShapePreset preset);
    }

    public class Vrm0ExpressionAdapter : IVrmExpressionAdapter
    {
        private readonly VRMBlendShapeProxy _proxy;

        public Vrm0ExpressionAdapter(VRMBlendShapeProxy proxy)
        {
            _proxy = proxy;
        }

        public void ImmediatelySetValue(BlendShapePreset preset, float value)
        {
            _proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(preset), value);
        }

        public void ImmediatelySetValue(string key, float value)
        {
            _proxy.ImmediatelySetValue(BlendShapeKey.CreateUnknown(key), value);
        }

        public IEnumerable<string> GetClipNames()
        {
            if (_proxy.BlendShapeAvatar == null || _proxy.BlendShapeAvatar.Clips == null)
                return Enumerable.Empty<string>();
            return _proxy.BlendShapeAvatar.Clips.Select(c => c.BlendShapeName);
        }

        public bool HasClip(BlendShapePreset preset)
        {
             if (_proxy.BlendShapeAvatar == null || _proxy.BlendShapeAvatar.Clips == null) return false;
             return _proxy.BlendShapeAvatar.Clips.Any(c => c.Preset == preset);
        }
    }

    public class Vrm1ExpressionAdapter : IVrmExpressionAdapter
    {
        private readonly Vrm10Instance _instance;

        public Vrm1ExpressionAdapter(Vrm10Instance instance)
        {
            _instance = instance;
        }

        public void ImmediatelySetValue(BlendShapePreset preset, float value)
        {
            if (_instance?.Runtime?.Expression == null) return;

            var key = MapPresetToVrm1Key(preset);
            if (key != null)
            {
                _instance.Runtime.Expression.SetWeight(key.Value, value);
            }
        }

        public void ImmediatelySetValue(string key, float value)
        {
            if (_instance?.Runtime?.Expression == null) return;

            var presetKey = MapStringToPresetKey(key);
            if (presetKey != null)
            {
                _instance.Runtime.Expression.SetWeight(presetKey.Value, value);
                return;
            }
            var expressionKey = new ExpressionKey(ExpressionPreset.custom, key);
            _instance.Runtime.Expression.SetWeight(expressionKey, value);
        }

        private ExpressionKey? MapStringToPresetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var k = key.Trim().ToLowerInvariant();
            switch (k)
            {
                case "lookleft": case "look_left": return new ExpressionKey(ExpressionPreset.lookLeft);
                case "lookright": case "look_right": return new ExpressionKey(ExpressionPreset.lookRight);
                case "lookup": case "look_up": return new ExpressionKey(ExpressionPreset.lookUp);
                case "lookdown": case "look_down": return new ExpressionKey(ExpressionPreset.lookDown);
                case "blink": return new ExpressionKey(ExpressionPreset.blink);
                case "blinkleft": case "blink_left": return new ExpressionKey(ExpressionPreset.blinkLeft);
                case "blinkright": case "blink_right": return new ExpressionKey(ExpressionPreset.blinkRight);
                default: return null;
            }
        }

        public IEnumerable<string> GetClipNames()
        {
            if (_instance?.Runtime?.Expression?.ExpressionKeys == null)
                return Enumerable.Empty<string>();
            return _instance.Runtime.Expression.ExpressionKeys.Select(k =>
                !string.IsNullOrEmpty(k.Name) ? k.Name : k.Preset.ToString());
        }

        public bool HasClip(BlendShapePreset preset)
        {
            if (_instance?.Runtime?.Expression?.ExpressionKeys == null) return false;
            var key = MapPresetToVrm1Key(preset);
            if (key == null) return false;
            return _instance.Runtime.Expression.ExpressionKeys.Any(k =>
                k.Preset == key.Value.Preset && string.Equals(k.Name, key.Value.Name, StringComparison.OrdinalIgnoreCase));
        }

        private ExpressionKey? MapPresetToVrm1Key(BlendShapePreset preset)
        {
            switch (preset)
            {

                case BlendShapePreset.Neutral: return new ExpressionKey(ExpressionPreset.neutral);
                case BlendShapePreset.A: return new ExpressionKey(ExpressionPreset.aa);
                case BlendShapePreset.I: return new ExpressionKey(ExpressionPreset.ih);
                case BlendShapePreset.U: return new ExpressionKey(ExpressionPreset.ou);
                case BlendShapePreset.E: return new ExpressionKey(ExpressionPreset.ee);
                case BlendShapePreset.O: return new ExpressionKey(ExpressionPreset.oh);
                case BlendShapePreset.Blink: return new ExpressionKey(ExpressionPreset.blink);
                case BlendShapePreset.Joy: return new ExpressionKey(ExpressionPreset.happy);
                case BlendShapePreset.Angry: return new ExpressionKey(ExpressionPreset.angry);
                case BlendShapePreset.Sorrow: return new ExpressionKey(ExpressionPreset.sad);
                case BlendShapePreset.Fun: return new ExpressionKey(ExpressionPreset.relaxed);
                case BlendShapePreset.LookUp: return new ExpressionKey(ExpressionPreset.lookUp);
                case BlendShapePreset.LookDown: return new ExpressionKey(ExpressionPreset.lookDown);
                case BlendShapePreset.LookLeft: return new ExpressionKey(ExpressionPreset.lookLeft);
                case BlendShapePreset.LookRight: return new ExpressionKey(ExpressionPreset.lookRight);
                case BlendShapePreset.Blink_L: return new ExpressionKey(ExpressionPreset.blinkLeft);
                case BlendShapePreset.Blink_R: return new ExpressionKey(ExpressionPreset.blinkRight);
                default: return null;
            }
        }
    }

    public class CompositeVrmExpressionAdapter : IVrmExpressionAdapter
    {
        private readonly IVrmExpressionAdapter _primary;
        private readonly VrmMeshBlendshapeAdapter _meshFallback;
        private readonly VrmMeshExpressionApplicator _meshApplicator;
        private readonly bool _primaryHasClips;

        public CompositeVrmExpressionAdapter(IVrmExpressionAdapter primary, VrmMeshBlendshapeAdapter meshFallback, VrmMeshExpressionApplicator meshApplicator = null)
        {
            _primary = primary;
            _meshFallback = meshFallback;
            _meshApplicator = meshApplicator;
            var primaryNames = primary?.GetClipNames().ToList() ?? new List<string>();
            _primaryHasClips = primaryNames.Count > 0;
        }

        public IEnumerable<string> GetClipNames()
        {
            var names = new List<string>();
            if (_primary != null) names.AddRange(_primary.GetClipNames());
            if (_meshFallback != null)
                names.AddRange(_meshFallback.GetClipNames().Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)));
            return names;
        }

        public bool HasClip(BlendShapePreset preset)
        {
            return (_primary != null && _primary.HasClip(preset)) ||
                   (_meshFallback != null && _meshFallback.HasClip(preset));
        }

        public void ImmediatelySetValue(BlendShapePreset preset, float value)
        {
            if (_primary != null && _primary.HasClip(preset))
                _primary.ImmediatelySetValue(preset, value);

            if (_meshApplicator != null && _meshFallback != null)
                _meshApplicator.Queue(PresetToStandardKey(preset), value);
            else if (_meshFallback != null && !_primaryHasClips)
                _meshFallback.ImmediatelySetValue(PresetToStandardKey(preset), value);
        }

        public void ImmediatelySetValue(string key, float value)
        {
            if (_primaryHasClips && _primary != null)
            {
                _primary.ImmediatelySetValue(key, value);
                if (_meshApplicator != null && _meshFallback != null)
                    _meshApplicator.Queue(NormalizeKey(key), value);
                return;
            }
            if (_meshFallback != null)
            {
                if (_meshApplicator != null)
                    _meshApplicator.Queue(NormalizeKey(key), value);
                else
                    _meshFallback.ImmediatelySetValue(key, value);
            }
        }

        private static string PresetToStandardKey(BlendShapePreset preset)
        {
            switch (preset)
            {
                case BlendShapePreset.LookLeft: return "lookLeft";
                case BlendShapePreset.LookRight: return "lookRight";
                case BlendShapePreset.LookUp: return "lookUp";
                case BlendShapePreset.LookDown: return "lookDown";
                case BlendShapePreset.A: return "aa";
                case BlendShapePreset.I: return "ih";
                case BlendShapePreset.U: return "ou";
                case BlendShapePreset.E: return "ee";
                case BlendShapePreset.O: return "oh";
                case BlendShapePreset.Blink: return "blink";
                case BlendShapePreset.Blink_L: return "blinkLeft";
                case BlendShapePreset.Blink_R: return "blinkRight";
                case BlendShapePreset.Joy: return "happy";
                case BlendShapePreset.Angry: return "angry";
                case BlendShapePreset.Sorrow: return "sad";
                case BlendShapePreset.Neutral: return "neutral";
                default: return preset.ToString().ToLowerInvariant();
            }
        }

        private static string NormalizeKey(string key)
        {
            return key?.ToLowerInvariant().Replace("_", "").Replace(".", "").Replace(" ", "") ?? "";
        }
    }

    public interface IVrmHeadAdapter
    {
        Transform Head { get; }
        Transform GetBone(HumanBodyBones bone);
    }

    public class Vrm0HeadAdapter : IVrmHeadAdapter
    {
        private readonly Animator _animator;
        private readonly VRMLookAtHead _lookAt;

        public Vrm0HeadAdapter(Animator animator, VRMLookAtHead lookAt)
        {
            _animator = animator;
            _lookAt = lookAt;
        }

        public Transform Head
        {
            get
            {
                if (_lookAt != null && _lookAt.Head != null) return _lookAt.Head;
                return _animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }

        public Transform GetBone(HumanBodyBones bone)
        {
            return _animator.GetBoneTransform(bone);
        }
    }

    public class Vrm1HeadAdapter : IVrmHeadAdapter
    {
        private readonly Vrm10Instance _instance;

        public Vrm1HeadAdapter(Vrm10Instance instance)
        {
            _instance = instance;
        }

        public Transform Head
        {
            get
            {
                if (_instance?.Humanoid == null) return null;
                return _instance.Humanoid.Head;
            }
        }

        public Transform GetBone(HumanBodyBones bone)
        {
            if (_instance?.Humanoid == null) return null;
            return _instance.Humanoid.GetBoneTransform(bone);
        }
    }
}
