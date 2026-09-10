using System;
using System.IO;
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
            {
                string json = File.ReadAllText(configPath);
                var profile = JsonUtility.FromJson<VrmAvatarProfile>(json);
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

                string json = JsonUtility.ToJson(template, true);
                File.WriteAllText(templatePath, json);
                ModLog.Debug($"Created template profile at: {templatePath}");
            }
            catch (Exception e)
            {
                ModLog.Info($"[WARN] Failed to create template profile: {e.Message}");
            }
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
