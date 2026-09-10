using System;
using UnityEngine;
using VRM;

namespace AvatarReplacer
{

    public class VrmBoneExpressionFallback : MonoBehaviour
    {
        private GiantessExpressionManager _expressionManager;
        private Transform _jawBone;
        private Quaternion _jawRestRotation;
        private float _currentJawAngle;
        private const float SmoothingSpeed = 14f;
        private const float MaxJawAngle = 22f;

        private static readonly (string key, float openAmount)[] PhonemeMapping =
        {
            ("a mouth", 0.95f),
            ("o mouth", 0.8f),
            ("u mouth", 0.5f),
            ("e mouth", 0.4f),
            ("i mouth", 0.2f),
            ("vore mouth", 0.9f),
            ("happy mouth", 0.4f),
            ("shock mouth", 0.7f),
            ("screaming mouth", 0.9f),
            ("laugh mouth", 0.6f),
        };

        public void Initialize(Giantess giantess, Animator vrmAnimator, IVrmExpressionAdapter expressionAdapter = null)
        {
            _expressionManager = giantess?.expressionManager;
            _jawBone = vrmAnimator != null ? vrmAnimator.GetBoneTransform(HumanBodyBones.Jaw) : null;

            if (_jawBone == null)
            {
                ModLog.Debug("VrmBoneExpressionFallback: No Jaw bone found - fallback disabled");
                enabled = false;
                return;
            }

            if (expressionAdapter != null &&
                (expressionAdapter.HasClip(BlendShapePreset.A) || expressionAdapter.HasClip(BlendShapePreset.O) ||
                 expressionAdapter.HasClip(BlendShapePreset.U) || expressionAdapter.HasClip(BlendShapePreset.I)))
            {
                ModLog.Debug("VrmBoneExpressionFallback: VRM has mouth expressions; jaw fallback disabled.");
                enabled = false;
                return;
            }

            _jawRestRotation = _jawBone.localRotation;
            _currentJawAngle = 0f;
            ModLog.Debug("VrmBoneExpressionFallback: Jaw bone fallback initialized");
        }

        private void LateUpdate()
        {
            if (_expressionManager == null || _jawBone == null) return;

            float targetOpen = GetTargetJawOpenAmount();
            _currentJawAngle = Mathf.MoveTowards(_currentJawAngle, targetOpen * MaxJawAngle, Time.deltaTime * SmoothingSpeed);

            _jawBone.localRotation = _jawRestRotation * Quaternion.Euler(_currentJawAngle, 0f, 0f);
        }

        private float GetTargetJawOpenAmount()
        {
            float maxWeight = 0f;
            foreach (var (key, openAmount) in PhonemeMapping)
            {
                var expr = _expressionManager.TryGetExpression(key);
                if (expr == null) continue;

                float w = (expr.weight * expr.weightMultiplier) / 100f;
                if (w > 0.01f)
                {
                    float contribution = w * openAmount;
                    if (contribution > maxWeight) maxWeight = contribution;
                }
            }
            return Mathf.Clamp01(maxWeight);
        }
    }
}
