using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AvatarReplacer
{
    [System.Serializable]
    public class BoneMappingConfig
    {
        [System.Serializable]
        public class BoneMap
        {
            public string originalBoneName;
            public string vrmBoneName;
            public Vector3 rotationOffset = Vector3.zero;
            public bool copyPosition = false;
            public bool isHips = false;
        }

        public List<BoneMap> boneMaps = new List<BoneMap>();
        public Vector3 globalRotationOffset = Vector3.zero;
        public bool useHumanoidMapping = true;

        public static BoneMappingConfig LoadOrCreateDefault(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    return JsonUtility.FromJson<BoneMappingConfig>(json);
                }
                catch (Exception e)
                {
                    ModLog.Error($"Failed to load bone mapping: {e.Message}");
                }
            }

            var config = new BoneMappingConfig();

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "Hips",
                vrmBoneName = "J_Bip_C_Hips",
                isHips = true
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "Spine",
                vrmBoneName = "J_Bip_C_Spine"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "Chest",
                vrmBoneName = "J_Bip_C_Chest"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftUpperArm",
                vrmBoneName = "J_Bip_L_UpperArm",
                rotationOffset = new Vector3(0, 0, 0)
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftLowerArm",
                vrmBoneName = "J_Bip_L_LowerArm"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftHand",
                vrmBoneName = "J_Bip_L_Hand"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftUpperLeg",
                vrmBoneName = "J_Bip_L_UpperLeg",
                rotationOffset = new Vector3(0, 0, 0)
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftLowerLeg",
                vrmBoneName = "J_Bip_L_LowerLeg"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "LeftFoot",
                vrmBoneName = "J_Bip_L_Foot"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightUpperArm",
                vrmBoneName = "J_Bip_R_UpperArm"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightLowerArm",
                vrmBoneName = "J_Bip_R_LowerArm"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightHand",
                vrmBoneName = "J_Bip_R_Hand"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightUpperLeg",
                vrmBoneName = "J_Bip_R_UpperLeg"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightLowerLeg",
                vrmBoneName = "J_Bip_R_LowerLeg"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "RightFoot",
                vrmBoneName = "J_Bip_R_Foot"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "Neck",
                vrmBoneName = "J_Bip_C_Neck"
            });

            config.boneMaps.Add(new BoneMap {
                originalBoneName = "Head",
                vrmBoneName = "J_Bip_C_Head"
            });

            return config;
        }

        public void Save(string path)
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                ModLog.Error($"Failed to save bone mapping: {e.Message}");
            }
        }
    }
}
