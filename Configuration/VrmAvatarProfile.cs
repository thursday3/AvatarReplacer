using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AvatarReplacer
{

    [System.Serializable]
    public class VrmAvatarProfile
    {

        public float scaleOverride = -1f;
        public Vector3Serializable positionOffset = null;
        public Vector3Serializable rotationOffset = null;

        public float springBoneStiffness = -1f;
        public float springBoneGravityPower = -1f;
        public float springBoneDrag = -1f;

        public float scaleMultiplier = 1.0f;

        public float smallScaleThresholdMeters = -1f;
        public float smallScaleStrideFactorMin = -1f;
        public float smallScaleHipSmoothMultiplier = -1f;

        [System.Serializable]
        public class Vector3Serializable
        {
            public float x = 0f;
            public float y = 0f;
            public float z = 0f;

            public Vector3Serializable() { }

            public Vector3Serializable(float x, float y, float z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public Vector3 ToVector3() => new Vector3(x, y, z);

            public static Vector3Serializable FromVector3(Vector3 v) => new Vector3Serializable(v.x, v.y, v.z);
        }

        public static VrmAvatarProfile LoadForAvatar(string vrmFilePath, string configDir)
        {
            if (string.IsNullOrEmpty(vrmFilePath)) return null;

            string avatarName = Path.GetFileNameWithoutExtension(vrmFilePath);
            string configPath = Path.Combine(configDir, $"{avatarName}.json");

            if (!File.Exists(configPath))
            {
                return null;
            }

            try
            {                    string json = File.ReadAllText(configPath);
                    var profile = FromJsonString(json);
                ModLog.Debug($"Loaded per-avatar profile for: {avatarName}");
                return profile;
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to load profile for {avatarName}: {e.Message}");
                return null;
            }
        }

        public static void CreateTemplateProfile(string configDir)
        {
            string templatePath = Path.Combine(configDir, "_template.json");

            if (File.Exists(templatePath)) return;

            try
            {
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var template = new VrmAvatarProfile
                {
                    scaleOverride = -1f,
                    positionOffset = new Vector3Serializable(0, 0, 0),
                    rotationOffset = new Vector3Serializable(0, 0, 0),
                    springBoneStiffness = -1f,
                    springBoneGravityPower = -1f,
                    springBoneDrag = -1f,
                    scaleMultiplier = 1.0f,
                    smallScaleThresholdMeters = -1f,
                    smallScaleStrideFactorMin = -1f,
                    smallScaleHipSmoothMultiplier = -1f
                };

                string json = template.ToJsonString();
                File.WriteAllText(templatePath, json);
                ModLog.Debug($"Created template profile at: {templatePath}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to create template profile: {e.Message}");
            }
        }

        /// <summary>
        /// Serialized by hand instead of via UnityEngine.JsonUtility: in this
        /// game's runtime JsonUtility silently drops fields whose types live
        /// in this plugin assembly — in-game it wrote profiles WITHOUT the
        /// positionOffset / rotationOffset objects. This writer emits every
        /// field, omitting only offsets that are actually null (unset).
        /// </summary>
        public string ToJsonString()
        {
            var parts = new List<string>
            {
                "\"scaleOverride\": " + SimpleJson.Number(scaleOverride)
            };
            if (positionOffset != null)
                parts.Add("\"positionOffset\": " + OffsetJson(positionOffset));
            if (rotationOffset != null)
                parts.Add("\"rotationOffset\": " + OffsetJson(rotationOffset));
            parts.Add("\"springBoneStiffness\": " + SimpleJson.Number(springBoneStiffness));
            parts.Add("\"springBoneGravityPower\": " + SimpleJson.Number(springBoneGravityPower));
            parts.Add("\"springBoneDrag\": " + SimpleJson.Number(springBoneDrag));
            parts.Add("\"scaleMultiplier\": " + SimpleJson.Number(scaleMultiplier));
            parts.Add("\"smallScaleThresholdMeters\": " + SimpleJson.Number(smallScaleThresholdMeters));
            parts.Add("\"smallScaleStrideFactorMin\": " + SimpleJson.Number(smallScaleStrideFactorMin));
            parts.Add("\"smallScaleHipSmoothMultiplier\": " + SimpleJson.Number(smallScaleHipSmoothMultiplier));

            var sb = new StringBuilder(320);
            sb.Append("{\n");
            for (int i = 0; i < parts.Count; i++)
                sb.Append("    ").Append(parts[i]).Append(i + 1 < parts.Count ? ",\n" : "\n");
            sb.Append("}");
            return sb.ToString();
        }

        private static string OffsetJson(Vector3Serializable v)
        {
            return "{ \"x\": " + SimpleJson.Number(v.x) +
                   ", \"y\": " + SimpleJson.Number(v.y) +
                   ", \"z\": " + SimpleJson.Number(v.z) + " }";
        }

        /// <summary>
        /// Tolerant reader: // comments, trailing commas and unknown keys are
        /// accepted (hand-edited files). Fields absent from the file keep the
        /// same defaults Unity's FromJson gave them, so existing profile
        /// files behave identically — with the difference that offsets users
        /// wrote and Unity silently discarded now actually apply.
        /// </summary>
        public static VrmAvatarProfile FromJsonString(string json)
        {
            var root = SimpleJson.ParseObject(json);
            var p = new VrmAvatarProfile();

            p.scaleOverride = SimpleJson.GetFloat(root, "scaleOverride", p.scaleOverride);
            p.positionOffset = ReadOffset(root, "positionOffset");
            p.rotationOffset = ReadOffset(root, "rotationOffset");
            p.springBoneStiffness = SimpleJson.GetFloat(root, "springBoneStiffness", p.springBoneStiffness);
            p.springBoneGravityPower = SimpleJson.GetFloat(root, "springBoneGravityPower", p.springBoneGravityPower);
            p.springBoneDrag = SimpleJson.GetFloat(root, "springBoneDrag", p.springBoneDrag);
            p.scaleMultiplier = SimpleJson.GetFloat(root, "scaleMultiplier", p.scaleMultiplier);
            p.smallScaleThresholdMeters = SimpleJson.GetFloat(root, "smallScaleThresholdMeters", p.smallScaleThresholdMeters);
            p.smallScaleStrideFactorMin = SimpleJson.GetFloat(root, "smallScaleStrideFactorMin", p.smallScaleStrideFactorMin);
            p.smallScaleHipSmoothMultiplier = SimpleJson.GetFloat(root, "smallScaleHipSmoothMultiplier", p.smallScaleHipSmoothMultiplier);
            return p;
        }

        private static Vector3Serializable ReadOffset(Dictionary<string, object> root, string key)
        {
            object v;
            if (!root.TryGetValue(key, out v)) return null; // key absent: unset -> global value
            var obj = v as Dictionary<string, object>;
            if (obj == null) return null;
            return new Vector3Serializable(
                SimpleJson.GetFloat(obj, "x", 0f),
                SimpleJson.GetFloat(obj, "y", 0f),
                SimpleJson.GetFloat(obj, "z", 0f));
        }

        public float GetEffectiveScale(float globalValue)
        {
            return scaleOverride > 0f ? scaleOverride : globalValue;
        }

        public Vector3 GetEffectivePosition(Vector3 globalValue)
        {
            return positionOffset != null ? positionOffset.ToVector3() : globalValue;
        }

        public Vector3 GetEffectiveRotation(Vector3 globalValue)
        {
            return rotationOffset != null ? rotationOffset.ToVector3() : globalValue;
        }

        public float GetEffectiveSpringBoneStiffness(float globalValue)
        {
            return springBoneStiffness > 0f ? springBoneStiffness : globalValue;
        }

        public float GetEffectiveSpringBoneGravityPower(float globalValue)
        {
            return springBoneGravityPower > 0f ? springBoneGravityPower : globalValue;
        }

        public float GetEffectiveSpringBoneDrag(float globalValue)
        {
            return springBoneDrag > 0f ? springBoneDrag : globalValue;
        }

        public float GetEffectiveScaleMultiplier()
        {

            return scaleMultiplier <= 0.001f ? 1.0f : scaleMultiplier;
        }

        public float GetEffectiveSmallScaleThreshold(float globalValue)
        {
            return smallScaleThresholdMeters > 0f ? smallScaleThresholdMeters : globalValue;
        }

        public float GetEffectiveSmallScaleStrideFactorMin(float globalValue)
        {
            return smallScaleStrideFactorMin > 0f ? smallScaleStrideFactorMin : globalValue;
        }

        public float GetEffectiveSmallScaleHipSmoothMultiplier(float globalValue)
        {
            return smallScaleHipSmoothMultiplier > 0f ? smallScaleHipSmoothMultiplier : globalValue;
        }
    }
}
