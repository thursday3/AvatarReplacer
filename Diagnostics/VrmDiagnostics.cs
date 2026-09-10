using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AvatarReplacer
{
    public static class VrmDiagnostics
    {
        public static void LogAvatar(GameObject avatar, Giantess giantess, string reason)
        {
            if (avatar == null) return;

            var animators = avatar.GetComponentsInChildren<Animator>(true);
            var humanoid = animators.FirstOrDefault(a => a != null && a.isHuman);
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            int blendShapes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r != null && r.sharedMesh != null)
                .Sum(r => r.sharedMesh.blendShapeCount);
            int springBones = avatar.GetComponentsInChildren<Component>(true)
                .Count(c => c != null && c.GetType().Name.IndexOf("SpringBone", StringComparison.OrdinalIgnoreCase) >= 0);
            var vrm10Type = Type.GetType("UniVRM10.Vrm10Instance, VRM10");
            bool vrm10 = vrm10Type != null && avatar.GetComponentInChildren(vrm10Type, true) != null;

            float height = humanoid != null ? MeasureHumanoidHeight(humanoid) : MeasureBounds(avatar);
            string expressionInfo = GetExpressionInfo(avatar, vrm10);

            ModLog.Debug(
                $"===== VRM DIAGNOSTICS ({reason}) =====\n" +
                $"Avatar={avatar.name}\n" +
                $"Format={(vrm10 ? "VRM1.0" : "VRM0.x/unknown")}\n" +
                $"Humanoid={(humanoid != null)}, HumanScale={(humanoid != null ? humanoid.humanScale.ToString("F3") : "n/a")}\n" +
                $"MeasuredHeight={height:F3}m, LossyScale={avatar.transform.lossyScale}\n" +
                $"Renderers={renderers.Length}, BlendShapes={blendShapes}, SpringBoneComponents={springBones}\n" +
                expressionInfo + "\n" +
                "=====================================");
        }

        private static float MeasureHumanoidHeight(Animator animator)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (head == null) return MeasureBounds(animator.gameObject);
            float feet = footL != null && footR != null ? Mathf.Min(footL.position.y, footR.position.y) : head.position.y;
            return Mathf.Abs(head.position.y - feet);
        }

        private static float MeasureBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size.y;
        }

        private static string GetExpressionInfo(GameObject avatar, bool vrm10)
        {
            if (vrm10)
            {
                try
                {
                    var type = Type.GetType("UniVRM10.Vrm10Instance, VRM10");
                    var instance = avatar.GetComponentInChildren(type, true);
                    var runtime = type.GetProperty("Runtime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance, null);
                    var expression = runtime?.GetType().GetProperty("Expression", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(runtime, null);
                    var keys = expression?.GetType().GetProperty("ExpressionKeys", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(expression, null) as System.Collections.IEnumerable;
                    int count = 0;
                    if (keys != null) foreach (var _ in keys) count++;
                    return $"Expressions=VRM1:{count}";
                }
                catch { return "Expressions=VRM1:unknown"; }
            }

            var proxy = avatar.GetComponentInChildren<VRM.VRMBlendShapeProxy>(true);
            int vrm0 = proxy != null && proxy.BlendShapeAvatar != null && proxy.BlendShapeAvatar.Clips != null
                ? proxy.BlendShapeAvatar.Clips.Count : 0;
            return $"Expressions=VRM0:{vrm0}";
        }
    }
}
