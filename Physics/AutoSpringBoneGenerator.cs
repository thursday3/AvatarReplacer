using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UniVRM10;

namespace AvatarReplacer
{
    public static class AutoSpringBoneGenerator
    {
        private static readonly string[] DefaultKeywords = new[]
        {
            "hair", "skirt", "tail", "cape", "cloth", "dress", "accessory",
            "ribbon", "sleeve", "belt", "twintail", "frill", "lace", "string"
        };

        private static readonly string[] ExcludedKeywords = new[]
        {
            "body", "bodyskin", "skin", "face", "head", "eye", "eyelash", "eyebrow",
            "teeth", "tongue", "arm", "hand", "leg", "foot", "toe", "finger", "spine", "chest", "hips"
        };

        public static void TryGenerate(
            GameObject vrmRoot,
            string keywordCsv,
            int maxRoots,
            float stiffness,
            float gravityPower,
            float drag,
            bool debug,
            bool allowOnAvatarsWithBones = false)
        {
            if (vrmRoot == null) return;

            // Keyword-guessed springs can grab chains near the head/body and visibly
            // deform an avatar, so generation is opt-in per avatar state. When
            // allowOnAvatarsWithBones is set the user explicitly wants extra chains
            // even on avatars that already have VRM spring bones.
            if (!allowOnAvatarsWithBones)
            {
                foreach (var comp in vrmRoot.GetComponentsInChildren<Component>(true))
                {
                    if (comp != null && comp.GetType().Name.IndexOf("SpringBone", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }
                }

                var vrm10 = vrmRoot.GetComponent<Vrm10Instance>() ?? vrmRoot.GetComponentInChildren<Vrm10Instance>(true);
                if (vrm10 != null && HasVrm10SpringBones(vrm10))
                {
                    return;
                }
            }

            var keywords = ParseKeywords(keywordCsv);
            if (keywords.Count == 0)
            {
                foreach (var k in DefaultKeywords) keywords.Add(k);
            }

            var candidates = new List<Transform>();
            var all = vrmRoot.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t == null) continue;
                var n = t.name.ToLowerInvariant();
                if (IsExcluded(n)) continue;
                if (Matches(n, keywords))
                {
                    candidates.Add(t);
                }
            }

            var roots = new List<Transform>();
            foreach (var t in candidates)
            {
                if (roots.Count >= maxRoots) break;
                if (t == null) continue;
                if (HasMatchingParent(t, keywords)) continue;
                roots.Add(t);
            }

            if (roots.Count == 0)
            {
                if (debug) ModLog.Debug("AutoSpringBone: No candidate bones found.");
                return;
            }

            // Look in the assembly that owns the VRM 0.x runtime type. The
            // embedded library is loaded with its original assembly identity.
            var springType = typeof(VRM.VRMBlendShapeProxy).Assembly.GetType("VRM.VRMSpringBone");
            if (springType == null)
            {
                if (debug) ModLog.Debug("AutoSpringBone: VRMSpringBone type not found.");
                return;
            }

            var spring = vrmRoot.AddComponent(springType);
            if (spring == null)
            {
                if (debug) ModLog.Debug("AutoSpringBone: Failed to add VRMSpringBone.");
                return;
            }

            SetRootBones(spring, roots);
            SetField(spring, "m_stiffnessForce", stiffness);
            SetField(spring, "m_gravityPower", gravityPower);
            SetField(spring, "m_gravityDir", Vector3.down);
            SetField(spring, "m_dragForce", drag);
            SetField(spring, "m_hitRadius", 0.02f);
            SetField(spring, "m_center", vrmRoot.transform);

            if (debug)
            {
                ModLog.Debug($"AutoSpringBone: Created VRMSpringBone with {roots.Count} roots: {string.Join(", ", roots.Select(r => r.name))}");
            }
        }

        private static HashSet<string> ParseKeywords(string csv)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(csv)) return set;
            foreach (var raw in csv.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = raw.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            return set;
        }

        private static bool Matches(string name, HashSet<string> keywords)
        {
            foreach (var k in keywords)
            {
                if (name.Contains(k)) return true;
            }
            return false;
        }

        private static bool IsExcluded(string name)
        {
            foreach (var k in ExcludedKeywords)
            {
                if (name.Contains(k)) return true;
            }
            return false;
        }

        private static bool HasMatchingParent(Transform t, HashSet<string> keywords)
        {
            var p = t.parent;
            while (p != null)
            {
                var n = p.name.ToLowerInvariant();
                if (Matches(n, keywords)) return true;
                p = p.parent;
            }
            return false;
        }

        private static void SetRootBones(Component spring, List<Transform> roots)
        {
            var type = spring.GetType();
            var field = type.GetField("m_rootBones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                        ?? type.GetField("RootBones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                        ?? type.GetField("rootBones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field != null)
            {
                field.SetValue(spring, roots);
                return;
            }
            var prop = type.GetProperty("RootBones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(spring, roots, null);
            }
        }

        private static void SetField(Component spring, string name, object value)
        {
            var field = spring.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field != null) field.SetValue(spring, value);
        }

        private static bool HasVrm10SpringBones(Vrm10Instance inst)
        {
            try
            {
                var runtime = inst.Runtime;
                if (runtime == null) return false;
                var springProp = runtime.GetType().GetProperty("SpringBone");
                var springObj = springProp != null ? springProp.GetValue(runtime, null) : null;
                if (springObj == null) return false;

                string[] candidates = { "Joints", "Springs", "SpringBones", "SpringBoneJoints" };
                foreach (var name in candidates)
                {
                    var prop = springObj.GetType().GetProperty(name);
                    if (prop != null)
                    {
                        var val = prop.GetValue(springObj, null);
                        if (CountEnumerable(val) > 0) return true;
                    }
                    var field = springObj.GetType().GetField(name);
                    if (field != null)
                    {
                        var val = field.GetValue(springObj);
                        if (CountEnumerable(val) > 0) return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        private static int CountEnumerable(object value)
        {
            if (value == null) return 0;
            if (value is System.Collections.IEnumerable e)
            {
                int c = 0;
                foreach (var _ in e) c++;
                return c;
            }
            return 0;
        }
    }
}
