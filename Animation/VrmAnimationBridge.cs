using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace AvatarReplacer
{

    public class VrmAnimationBridge : MonoBehaviour
    {
        private const float DefaultMetersPerScale = 30f;

        private List<BoneLink> boneLinks = new List<BoneLink>();
        private bool debugMode;
        private Vector3 globalRotationOffset = Vector3.zero;
        private Vector3 globalPositionOffset = Vector3.zero;

        private Transform leftFootVrm;
        private Transform rightFootVrm;
        private Transform leftFootArmature;
        private Transform rightFootArmature;

        private Vector3 leftFootOffset;
        private Quaternion leftFootRotOffset;
        private Vector3 rightFootOffset;
        private Quaternion rightFootRotOffset;
        private float initialVrmScale = 1f;

        private HumanPoseHandler sourcePoseHandler;
        private HumanPoseHandler targetPoseHandler;
        private HumanPose pose = new HumanPose();
        private float[] lastMuscles;
        private bool musclesInitialized = false;
        private Vector3 smoothedSourceFootMid;
        private bool sourceFootMidInitialized = false;
        private Giantess giantess;
        private bool usePoseHandler = true;
        private bool smallScaleFixEnabled = true;
        private float smallScaleThresholdMeters = 60f;
        private float smallScaleStrideFactorMin = 0.75f;
        private float smallScaleHipSmoothMultiplier = 1.25f;

        private Animator sourceAnimator;
        private Animator vrmAnimatorCached;
        private Transform sourceLeftFoot;
        private Transform sourceRightFoot;
        private Transform vrmLeftFoot;
        private Transform vrmRightFoot;
        private BoneLink rootBoneLink;

        private Vrm10Instance vrm10Instance;

        [System.Serializable]
        public class BoneLink
        {
            public Transform source;
            public Transform target;
            public string boneName;
            public Quaternion baseRestPoseOffset;
            public Vector3 baseRestPosOffset;
            public bool isRoot;
        }

        public void Initialize(
            Giantess giantess,
            Animator vrmAnimator,
            BoneMappingConfig config,
            bool debug,
            bool smallScaleFixEnabled,
            float smallScaleThresholdMeters,
            float smallScaleStrideFactorMin,
            float smallScaleHipSmoothMultiplier)
        {
            this.giantess = giantess;
            this.debugMode = debug;
            this.smallScaleFixEnabled = smallScaleFixEnabled;
            this.smallScaleThresholdMeters = Mathf.Max(0.1f, smallScaleThresholdMeters);
            this.smallScaleStrideFactorMin = Mathf.Clamp(smallScaleStrideFactorMin, 0.1f, 1f);
            this.smallScaleHipSmoothMultiplier = Mathf.Max(1f, smallScaleHipSmoothMultiplier);
            Animator source = giantess.animator;

            this.vrm10Instance = vrmAnimator.GetComponent<Vrm10Instance>();
            if (this.vrm10Instance != null)
            {
                ModLog.Debug("VrmAnimationBridge: VRM 1.0 detected. Configuring Vrm10Instance.");

                this.vrm10Instance.UpdateType = Vrm10Instance.UpdateTypes.None;
            }

            boneLinks.Clear();
            MapHumanoidBones(source, vrmAnimator);
            MapCustomBones(source, vrmAnimator, config);

            leftFootVrm = vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFootVrm = vrmAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

            initialVrmScale = vrmAnimator.transform.lossyScale.x;

            vrmAnimator.enabled = true;
            vrmAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            vrmAnimator.updateMode = AnimatorUpdateMode.Normal;

            if (vrmAnimator.runtimeAnimatorController != null)
            {
                ModLog.Debug($"VrmAnimationBridge: Disabling VRM animation controller: {vrmAnimator.runtimeAnimatorController.name}");
                vrmAnimator.runtimeAnimatorController = null;
            }

            if (source.isHuman && vrmAnimator.isHuman)
            {
                sourcePoseHandler = new HumanPoseHandler(source.avatar, source.transform);
                targetPoseHandler = new HumanPoseHandler(vrmAnimator.avatar, vrmAnimator.transform);
                usePoseHandler = true;
                ModLog.Debug($"VrmAnimationBridge: HumanPoseHandler initialized. Source: {source.avatar.name}, Target: {vrmAnimator.avatar?.name ?? "null"}");
            }
            else
            {
                usePoseHandler = false;
                ModLog.Info($"[WARN] VrmAnimationBridge: Failed to init HumanPoseHandler. Source isHuman: {source.isHuman}, Target isHuman: {vrmAnimator.isHuman}");
            }

            sourceAnimator = source;
            vrmAnimatorCached = vrmAnimator;
            sourceLeftFoot = source.GetBoneTransform(HumanBodyBones.LeftFoot);
            sourceRightFoot = source.GetBoneTransform(HumanBodyBones.RightFoot);
            vrmLeftFoot = vrmAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            vrmRightFoot = vrmAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

            var armatures = giantess.GetComponentsInChildren<Armature>(true);
            foreach (var a in armatures)
            {
                if (a.humanBodyBones == HumanBodyBones.LeftFoot && leftFootVrm != null)
                {
                    leftFootArmature = a.transform;
                    Transform sFoot = source.GetBoneTransform(HumanBodyBones.LeftFoot);
                    if (sFoot != null)
                    {
                        leftFootOffset = sFoot.InverseTransformPoint(leftFootArmature.position);
                        leftFootRotOffset = Quaternion.Inverse(sFoot.rotation) * leftFootArmature.rotation;
                    }
                }
                if (a.humanBodyBones == HumanBodyBones.RightFoot && rightFootVrm != null)
                {
                    rightFootArmature = a.transform;
                    Transform sFoot = source.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (sFoot != null)
                    {
                        rightFootOffset = sFoot.InverseTransformPoint(rightFootArmature.position);
                        rightFootRotOffset = Quaternion.Inverse(sFoot.rotation) * rightFootArmature.rotation;
                    }
                }
            }

            rootBoneLink = boneLinks.Find(l => l.isRoot);

            ModLog.Debug(
                $"VrmAnimationBridge: WorldSync Initialized. Links={boneLinks.Count}, RootLink={(rootBoneLink != null ? "found" : "missing")}, " +
                $"SmallScaleFix={(this.smallScaleFixEnabled ? "on" : "off")}, Threshold={this.smallScaleThresholdMeters:F1}m");
        }

        private void OnDestroy()
        {
            sourcePoseHandler?.Dispose();
            targetPoseHandler?.Dispose();
            sourcePoseHandler = null;
            targetPoseHandler = null;
        }

        private float GetCurrentHeightMeters()
        {
            if (giantess == null)
            {
                return 0f;
            }

            float metersPerScale = DefaultMetersPerScale;
            if (LootZoneManager.Instance != null && LootZoneManager.Instance.meterPerScale > 0.001f)
            {
                metersPerScale = LootZoneManager.Instance.meterPerScale;
            }

            return giantess.scale * metersPerScale;
        }

        private float GetSmallScaleStrength(float currentMeters)
        {
            if (!smallScaleFixEnabled)
            {
                return 0f;
            }

            float threshold = Mathf.Max(0.1f, smallScaleThresholdMeters);
            if (currentMeters >= threshold)
            {
                return 0f;
            }

            return Mathf.Clamp01((threshold - currentMeters) / threshold);
        }

        private float GetSmallScaleStrideFactor(float strength)
        {
            return Mathf.Lerp(1f, smallScaleStrideFactorMin, Mathf.Clamp01(strength));
        }

        private float GetSmallScaleSmoothingFactor(float strength)
        {
            return Mathf.Lerp(1f, smallScaleHipSmoothMultiplier, Mathf.Clamp01(strength));
        }

        private void Update()
        {

            SyncFootArmatures();
        }

        private void LateUpdate()
        {
            if (giantess == null || giantess.animator == null) return;

            Animator source = sourceAnimator;
            Animator target = vrmAnimatorCached;

            float giantessScale = giantess.scale;
            float giantessMeters = GetCurrentHeightMeters();
            float smallScaleStrength = GetSmallScaleStrength(giantessMeters);
            float smallScaleSmoothingFactor = GetSmallScaleSmoothingFactor(smallScaleStrength);

            Quaternion globalRot = Quaternion.Euler(globalRotationOffset);

            if (usePoseHandler && sourcePoseHandler != null && targetPoseHandler != null)
            {

                foreach (var link in boneLinks)
                {
                    if (link.isRoot && link.source != null && link.target != null)
                    {

                        Quaternion targetRot = globalRot * link.source.rotation * link.baseRestPoseOffset;
                        float rotSmooth = (giantessScale > 100f) ? 5f : 20f;
                        rotSmooth /= smallScaleSmoothingFactor;
                        link.target.rotation = Quaternion.Slerp(link.target.rotation, targetRot, Time.deltaTime * rotSmooth);

                        float currentScale = giantessScale;

                        if (sourceLeftFoot != null && sourceRightFoot != null && vrmLeftFoot != null && vrmRightFoot != null)
                        {

                            Vector3 sourceFootMid = (sourceLeftFoot.position + sourceRightFoot.position) / 2f;
                            Vector3 vrmFootMid = (vrmLeftFoot.position + vrmRightFoot.position) / 2f;

                            Vector3 vrmHipToFoot = link.target.position - vrmFootMid;

                            link.target.position = sourceFootMid + vrmHipToFoot + globalPositionOffset;
                        }
                        else
                        {

                            Vector3 scaledOffset = link.baseRestPosOffset * (currentScale / Mathf.Max(initialVrmScale, 0.01f));
                            link.target.position = link.source.position - scaledOffset;
                        }
                    }
                }

                sourcePoseHandler.GetHumanPose(ref pose);

                if (!musclesInitialized || lastMuscles == null || lastMuscles.Length != pose.muscles.Length)
                {
                    lastMuscles = new float[pose.muscles.Length];
                    Array.Copy(pose.muscles, lastMuscles, pose.muscles.Length);
                    musclesInitialized = true;
                }
                else
                {

                    float baseSpeed = 40f;
                    float effectiveSmooth = (giantessScale > 50f) ? (baseSpeed * 0.5f) : baseSpeed;

                    for (int i = 0; i < pose.muscles.Length; i++)
                    {
                        lastMuscles[i] = Mathf.Lerp(lastMuscles[i], pose.muscles[i], Time.deltaTime * effectiveSmooth);
                        pose.muscles[i] = lastMuscles[i];
                    }
                }

                targetPoseHandler.SetHumanPose(ref pose);

                foreach (var link in boneLinks)
                {
                    if (link.isRoot && link.source != null && link.target != null)
                    {
                        if (sourceLeftFoot != null && sourceRightFoot != null && vrmLeftFoot != null && vrmRightFoot != null)
                        {

                            Vector3 rawSourceMid = (sourceLeftFoot.position + sourceRightFoot.position) / 2f;

                            if (!sourceFootMidInitialized)
                            {
                                smoothedSourceFootMid = rawSourceMid;
                                sourceFootMidInitialized = true;
                            }
                            else
                            {

                                float posSmoothSpeed = (giantessScale > 50f) ? 10f : 25f;
                                posSmoothSpeed /= smallScaleSmoothingFactor;
                                smoothedSourceFootMid = Vector3.Lerp(smoothedSourceFootMid, rawSourceMid, Time.deltaTime * posSmoothSpeed);
                            }

                            Vector3 vrmFootMid = (vrmLeftFoot.position + vrmRightFoot.position) / 2f;
                            Vector3 vrmHipToFoot = link.target.position - vrmFootMid;

                            link.target.position = smoothedSourceFootMid + vrmHipToFoot + globalPositionOffset;
                        }
                        else
                        {

                            link.target.position = link.source.position + globalPositionOffset;
                        }
                    }
                }
            }
            else
            {

                foreach (var link in boneLinks)
                {
                    if (link.source == null || link.target == null) continue;

                    link.target.rotation = link.source.rotation * link.baseRestPoseOffset;

                    if (link.isRoot)
                    {
                        link.target.rotation = globalRot * link.target.rotation;

                        if (sourceLeftFoot != null && sourceRightFoot != null && vrmLeftFoot != null && vrmRightFoot != null)
                        {
                            Vector3 sourceFootMid = (sourceLeftFoot.position + sourceRightFoot.position) / 2f;
                            Vector3 vrmFootMid = (vrmLeftFoot.position + vrmRightFoot.position) / 2f;
                            Vector3 vrmHipToFoot = link.target.position - vrmFootMid;
                            link.target.position = sourceFootMid + vrmHipToFoot;
                        }
                        else
                        {
                            float currentScale = giantess.scale;
                            Vector3 scaledOffset = link.baseRestPosOffset * (currentScale / Mathf.Max(initialVrmScale, 0.01f));
                            link.target.position = link.source.position - scaledOffset + globalPositionOffset;
                        }
                    }
                }
            }

            var physicsController = GetComponent<VrmSpringBoneController>();
            if (physicsController != null && physicsController.enabled)
            {
                physicsController.ProcessAfterAnimationFrame();
            }
            else if (vrm10Instance != null)
            {

               if (vrm10Instance.UpdateType == Vrm10Instance.UpdateTypes.None)
               {
                   vrm10Instance.Runtime.Process();
               }
            }

            if (debugMode && Time.frameCount % 300 == 0 && rootBoneLink != null && leftFootVrm != null && leftFootArmature != null)
            {
                float currentScale = transform.lossyScale.x;
                float scaleFactor = currentScale / Mathf.Max(initialVrmScale, 0.01f);
                float strideFactor = GetSmallScaleStrideFactor(smallScaleStrength);
                ModLog.Debug(
                    $"VrmAnimationBridge: Active. HipsPos: {rootBoneLink.target.position}, Scale: {transform.lossyScale.x:F3}, FootOffset: {scaleFactor:F3}, " +
                    $"Height={giantessMeters:F2}m, SmallStrength={smallScaleStrength:F2}, StrideFactor={strideFactor:F2}, HipSmooth={smallScaleSmoothingFactor:F2}");
            }
        }

        private void SyncFootArmatures()
        {

            float currentScale = transform.lossyScale.x;
            float scaleFactor = currentScale / Mathf.Max(initialVrmScale, 0.01f);
            float smallScaleStrength = GetSmallScaleStrength(GetCurrentHeightMeters());
            float strideFactor = GetSmallScaleStrideFactor(smallScaleStrength);
            scaleFactor *= strideFactor;

            Transform leftSourceFoot = sourceLeftFoot;
            Transform rightSourceFoot = sourceRightFoot;

            if (leftSourceFoot != null && leftFootArmature != null)
            {

                Vector3 scaledOffset = leftFootOffset * scaleFactor;
                leftFootArmature.position = leftSourceFoot.TransformPoint(scaledOffset);
                leftFootArmature.rotation = leftSourceFoot.rotation * leftFootRotOffset;
            }
            if (rightSourceFoot != null && rightFootArmature != null)
            {

                Vector3 scaledOffset = rightFootOffset * scaleFactor;
                rightFootArmature.position = rightSourceFoot.TransformPoint(scaledOffset);
                rightFootArmature.rotation = rightSourceFoot.rotation * rightFootRotOffset;
            }
        }

        private void MapHumanoidBones(Animator source, Animator target)
        {
            if (target == null)
            {
                ModLog.Error("MapHumanoidBones failed: target VRM animator is null (VRM may not have loaded as humanoid).");
                return;
            }
            if (source == null)
            {
                ModLog.Error("MapHumanoidBones failed: source (game character) animator is null.");
                return;
            }

            if (!target.isHuman)
            {
                 ModLog.Error($"MapHumanoidBones failed: Target VRM '{target.name}' is not a Humanoid. Check import settings or VRM version.");
                 return;
            }

            foreach (HumanBodyBones boneType in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (boneType == HumanBodyBones.LastBone) continue;

                Transform sBone = source.GetBoneTransform(boneType);
                Transform tBone = target.GetBoneTransform(boneType);

                if (sBone != null && tBone != null)
                {
                    AddLink(sBone, tBone, boneType.ToString(), (boneType == HumanBodyBones.Hips));
                }
            }
        }

        private void MapCustomBones(Animator source, Animator target, BoneMappingConfig config)
        {
            var sourceTransforms = source.GetComponentsInChildren<Transform>(true);
            var targetTransforms = target.GetComponentsInChildren<Transform>(true);

            foreach (var map in config.boneMaps)
            {
                Transform sBone = Array.Find(sourceTransforms, t => t.name.Contains(map.originalBoneName));
                Transform tBone = Array.Find(targetTransforms, t => t.name.Contains(map.vrmBoneName));

                if (sBone != null && tBone != null)
                {
                    if (boneLinks.Exists(l => l.target == tBone)) continue;
                    AddLink(sBone, tBone, $"{map.originalBoneName}->{map.vrmBoneName}", map.isHips);
                }
            }
        }

        private void AddLink(Transform source, Transform target, string boneName, bool isRoot)
        {

            Quaternion rotOffset = Quaternion.Inverse(source.rotation) * target.rotation;

            Vector3 posOffset = source.position - target.position;

            boneLinks.Add(new BoneLink
            {
                source = source,
                target = target,
                boneName = boneName,
                baseRestPoseOffset = rotOffset,
                baseRestPosOffset = posOffset,
                isRoot = isRoot
            });
        }

        public void SetGlobalRotation(Vector3 offset)
        {
            this.globalRotationOffset = offset;

        }

        public void SetGlobalPosition(Vector3 offset)
        {
            this.globalPositionOffset = offset;
        }

        public List<BoneLink> GetLinks()
        {
            return boneLinks;
        }
    }
}
