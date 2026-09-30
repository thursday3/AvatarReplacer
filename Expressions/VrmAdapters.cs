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
        bool HasClip(string key);
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

        public bool HasClip(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (_proxy.BlendShapeAvatar == null || _proxy.BlendShapeAvatar.Clips == null) return false;
            return _proxy.BlendShapeAvatar.Clips.Any(c =>
                string.Equals(c.BlendShapeName, key, StringComparison.OrdinalIgnoreCase));
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
            if (key == null) return;

            if (HasExactKey(key.Value))
            {
                _instance.Runtime.Expression.SetWeight(key.Value, value);
                return;
            }

            // Some VRM 1.0 exporters store standard expressions ("happy",
            // "blink", ...) under the *custom* preset. Fall back to the
            // custom-named key when the standard one does not exist, otherwise
            // those expressions silently never apply.
            var customKey = new ExpressionKey(ExpressionPreset.custom, key.Value.Name);
            if (HasExactKey(customKey))
                _instance.Runtime.Expression.SetWeight(customKey, value);
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

        public bool HasClip(string key)
        {
            if (_instance?.Runtime?.Expression?.ExpressionKeys == null || string.IsNullOrEmpty(key)) return false;

            var presetKey = MapStringToPresetKey(key);
            if (presetKey != null)
            {
                return _instance.Runtime.Expression.ExpressionKeys.Any(k =>
                    k.Preset == presetKey.Value.Preset &&
                    string.Equals(k.Name, presetKey.Value.Name, StringComparison.OrdinalIgnoreCase));
            }

            return _instance.Runtime.Expression.ExpressionKeys.Any(k =>
                k.Preset == ExpressionPreset.custom &&
                string.Equals(k.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        private ExpressionKey? MapStringToPresetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var k = key.Trim().ToLowerInvariant();
            switch (k)
            {
                // VRM 0.x-style preset names that UniVRM10 exposes as standard keys.
                // Without these, a clip name like "happy" would be treated as a
                // *custom* key and silently miss the avatar's happy preset.
                case "neutral": return new ExpressionKey(ExpressionPreset.neutral);
                case "happy": return new ExpressionKey(ExpressionPreset.happy);
                case "angry": return new ExpressionKey(ExpressionPreset.angry);
                case "sad": return new ExpressionKey(ExpressionPreset.sad);
                case "relaxed": return new ExpressionKey(ExpressionPreset.relaxed);
                case "aa": return new ExpressionKey(ExpressionPreset.aa);
                case "ih": return new ExpressionKey(ExpressionPreset.ih);
                case "ou": return new ExpressionKey(ExpressionPreset.ou);
                case "ee": return new ExpressionKey(ExpressionPreset.ee);
                case "oh": return new ExpressionKey(ExpressionPreset.oh);
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
                (k.Preset == key.Value.Preset || k.Preset == ExpressionPreset.custom) &&
                string.Equals(k.Name, key.Value.Name, StringComparison.OrdinalIgnoreCase));
        }

        private bool HasExactKey(ExpressionKey key)
        {
            if (_instance?.Runtime?.Expression?.ExpressionKeys == null) return false;
            return _instance.Runtime.Expression.ExpressionKeys.Any(k =>
                k.Preset == key.Preset && string.Equals(k.Name, key.Name, StringComparison.OrdinalIgnoreCase));
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


        public CompositeVrmExpressionAdapter(IVrmExpressionAdapter primary, VrmMeshBlendshapeAdapter meshFallback, VrmMeshExpressionApplicator meshApplicator = null)
        {
            _primary = primary;
            _meshFallback = meshFallback;
            _meshApplicator = meshApplicator;
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

        public bool HasClip(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return (_primary != null && _primary.HasClip(key)) ||
                   (_meshFallback != null && _meshFallback.HasClip(key));
        }

        public void ImmediatelySetValue(BlendShapePreset preset, float value)
        {
            // Route to exactly ONE backend. Previously the VRM expression clip AND
            // the raw mesh blendshape(s) it drives were both written every frame;
            // the two writes race (last writer wins), producing stacked, flickering
            // or plainly wrong faces. Only fall through to the mesh when the
            // primary backend cannot represent this expression.
            bool primaryHas = _primary != null && _primary.HasClip(preset);
            if (primaryHas)
            {
                _primary.ImmediatelySetValue(preset, value);
                return;
            }

            if (_meshApplicator != null && _meshFallback != null)
                _meshApplicator.Queue(PresetToStandardKey(preset), value);
            else if (_meshFallback != null)
                _meshFallback.ImmediatelySetValue(PresetToStandardKey(preset), value);
        }

        public void ImmediatelySetValue(string key, float value)
        {
            if (string.IsNullOrEmpty(key)) return;

            bool primaryHas = _primary != null && _primary.HasClip(key);
            if (primaryHas)
            {
                _primary.ImmediatelySetValue(key, value);
                return;
            }

            if (_meshFallback != null)
            {
                if (_meshApplicator != null)
                    _meshApplicator.Queue(NormalizeKey(key), value);
                else
                    _meshFallback.ImmediatelySetValue(key, value);
            }
            else if (_primary != null)
            {
                _primary.ImmediatelySetValue(key, value);
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
