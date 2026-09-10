using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRM;

namespace AvatarReplacer
{

    public class VrmManager : MonoBehaviour
    {
        private Giantess giantess;
        private VrmAnimationBridge animationBridge;
        private VrmAvatarProfile profile;

        private bool vrmVisible = true;
        private List<Renderer> originalRenderers = new List<Renderer>();
        private Component originalVariation;


        private float initialScaleRatio = 1f;

        private Renderer[] cachedRenderers;
        private MaterialPropertyBlock propBlock;

        private IVrmHeadAdapter headAdapter;
        private IVrmExpressionAdapter expressionAdapter;

        public void Setup(Giantess giantess, Animator target, BoneMappingConfig mappingConfig, VrmAvatarProfile profile, bool debug)
        {
            this.giantess = giantess;
            this.profile = profile;

            initialScaleRatio = transform.localScale.x;

            var vrm10 = GetComponent<UniVRM10.Vrm10Instance>();

            if (vrm10 != null)
            {
                int exprCount = (vrm10.Runtime?.Expression?.ExpressionKeys == null) ? 0 : vrm10.Runtime.Expression.ExpressionKeys.Count();
                ModLog.Debug($"VrmManager: VRM 1.0 loaded: {gameObject.name}, Animator=yes, Humanoid={vrm10.Humanoid != null}, ExpressionKeys={exprCount}");
                headAdapter = new Vrm1HeadAdapter(vrm10);
                var primaryAdapter = new Vrm1ExpressionAdapter(vrm10);
                expressionAdapter = WrapWithMeshFallback(primaryAdapter);
            }
            else
            {
                ModLog.Debug("VrmManager: No VRM 1.0 Instance found. Assuming VRM 0.x. Creating adapters.");

                var lookAt = GetComponent<VRMLookAtHead>();
                headAdapter = new Vrm0HeadAdapter(target, lookAt);

                IVrmExpressionAdapter primaryAdapter = null;
                var proxy = GetComponent<VRM.VRMBlendShapeProxy>();
                if (proxy != null)
                {
                    primaryAdapter = new Vrm0ExpressionAdapter(proxy);
                }
                else
                {
                    ModLog.Info("[WARN] VrmManager: VRMBlendShapeProxy not found on VRM 0.x avatar!");
                }
                expressionAdapter = WrapWithMeshFallback(primaryAdapter);
            }

            IVrmExpressionAdapter WrapWithMeshFallback(IVrmExpressionAdapter primary)
            {
                var meshAdapter = new VrmMeshBlendshapeAdapter(transform);
                VrmMeshExpressionApplicator applicator = null;
                if (meshAdapter.DiscoveredClipCount > 0)
                {
                    applicator = gameObject.AddComponent<VrmMeshExpressionApplicator>();
                    applicator.Initialize(meshAdapter);
                }
                if (meshAdapter.DiscoveredClipCount > 0 || primary != null)
                    return new CompositeVrmExpressionAdapter(primary, meshAdapter.DiscoveredClipCount > 0 ? meshAdapter : null, applicator);
                return primary;
            }

            animationBridge = gameObject.AddComponent<VrmAnimationBridge>();
            bool smallScaleFixEnabled = AvatarReplacerPlugin.SmallScaleLocomotionFixEnabledValue;
            float smallScaleThresholdMeters = AvatarReplacerPlugin.SmallScaleLocomotionFixThresholdMetersValue;
            float smallScaleStrideFactorMin = AvatarReplacerPlugin.SmallScaleLocomotionFixStrideFactorMinValue;
            float smallScaleHipSmoothMultiplier = AvatarReplacerPlugin.SmallScaleLocomotionFixHipSmoothMultiplierValue;

            if (profile != null)
            {
                smallScaleThresholdMeters = profile.GetEffectiveSmallScaleThreshold(smallScaleThresholdMeters);
                smallScaleStrideFactorMin = profile.GetEffectiveSmallScaleStrideFactorMin(smallScaleStrideFactorMin);
                smallScaleHipSmoothMultiplier = profile.GetEffectiveSmallScaleHipSmoothMultiplier(smallScaleHipSmoothMultiplier);
            }

            animationBridge.Initialize(
                giantess,
                target,
                mappingConfig,
                debug,
                smallScaleFixEnabled,
                smallScaleThresholdMeters,
                smallScaleStrideFactorMin,
                smallScaleHipSmoothMultiplier);

            if (expressionAdapter != null)
            {
                var expressionBridge = gameObject.AddComponent<VrmExpressionBridge>();
                cachedExpressionBridge = expressionBridge;
                expressionBridge.Initialize(giantess, expressionAdapter);
                ModLog.Debug("VrmManager: Expression bridge attached for facial sync");

                var jawFallback = gameObject.AddComponent<VrmBoneExpressionFallback>();
                jawFallback.Initialize(giantess, target, expressionAdapter);
            }
            else
            {
                ModLog.Warning("VrmManager: No expression adapter available; expression sync disabled.");
            }

            float springStiffness = AvatarReplacerPlugin.SpringBoneStiffnessValue;
            float springGravity = AvatarReplacerPlugin.SpringBoneGravityPowerValue;
            float springDrag = AvatarReplacerPlugin.SpringBoneDragValue;
            if (profile != null)
            {
                springStiffness = profile.GetEffectiveSpringBoneStiffness(springStiffness);
                springGravity = profile.GetEffectiveSpringBoneGravityPower(springGravity);
                springDrag = profile.GetEffectiveSpringBoneDrag(springDrag);
            }
            gameObject.AddComponent<VrmSpringBoneController>().Initialize(giantess, springStiffness, springGravity, springDrag);
            if (debug) VrmDiagnostics.LogAvatar(gameObject, giantess, "setup");

            if (headAdapter != null && expressionAdapter != null)
            {
                gameObject.AddComponent<VrmLookAtBridge>().Initialize(giantess, headAdapter, expressionAdapter);
                ModLog.Debug("VrmManager: LookAt bridge attached");
            }
            else
            {
                 ModLog.Info("[WARN] VrmManager: Skipping LookAtBridge - missing adapters");
            }

            SetLayerRecursively(gameObject, giantess.gameObject.layer);
            ModLog.Debug($"VrmManager: Forced layer sync to: {giantess.gameObject.layer} ({LayerMask.LayerToName(giantess.gameObject.layer)})");

            cachedRenderers = GetComponentsInChildren<Renderer>(true);
            propBlock = new MaterialPropertyBlock();

            ForceMaterialSync();

            ModLog.Info($"Avatar setup complete for {gameObject.name} with initial scale ratio: {initialScaleRatio}, cached {cachedRenderers.Length} renderers");
        }

        public Giantess BoundGiantess => giantess;

        public void SetVisibility(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.enabled = visible;
        }

        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child != null) SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        public void ApplyGlobalOffset(Vector3 offset)
        {
            if (animationBridge != null)
            {
                animationBridge.SetGlobalRotation(offset);
            }
        }

        public void ApplyGlobalPosition(Vector3 offset)
        {
            if (animationBridge != null)
            {
                animationBridge.SetGlobalPosition(offset);
            }
        }

        public void ResetOffsets()
        {
            if (animationBridge != null)
            {
                animationBridge.SetGlobalRotation(Vector3.zero);
            }
        }

        public void Update()
        {

            if (AvatarReplacerPlugin.PreviousAvatarKey != null)
            {
                if (Input.GetKeyDown(AvatarReplacerPlugin.PreviousAvatarKey.Value) || Input.GetKeyDown(KeyCode.PageUp))
                {
                    ModLog.Info("Previous Avatar Key Detected");
                    AvatarReplacerPlugin.SwitchAvatar(-1);
                }
                else if (Input.GetKeyDown(AvatarReplacerPlugin.NextAvatarKey.Value) || Input.GetKeyDown(KeyCode.PageDown))
                {
                    ModLog.Info("Next Avatar Key Detected");
                    AvatarReplacerPlugin.SwitchAvatar(1);
                }

                if (AvatarReplacerPlugin.RescanKey != null && Input.GetKeyDown(AvatarReplacerPlugin.RescanKey.Value))
                {
                    ModLog.Info($"Manual Rescan ({AvatarReplacerPlugin.RescanKey.Value}) Detected");
                    AvatarReplacerPlugin.ScanForVrms();
                    AvatarReplacerPlugin.ReloadCurrentAvatar();
                }
                if (AvatarReplacerPlugin.DiagnosticsKey != null && Input.GetKeyDown(AvatarReplacerPlugin.DiagnosticsKey.Value))
                {
                    VrmDiagnostics.LogAvatar(gameObject, giantess, "manual");
                }
            }

            if (cachedExpressionBridge != null)
            {
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                if (ctrl)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Neutral); ModLog.Info("Debug Expression: Neutral"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha2)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Joy); ModLog.Info("Debug Expression: Joy"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha3)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Angry); ModLog.Info("Debug Expression: Angry"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha4)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Sorrow); ModLog.Info("Debug Expression: Sorrow"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha5)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Fun); ModLog.Info("Debug Expression: Fun"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha6)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.Blink); ModLog.Info("Debug Expression: Blink"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha7)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.LookLeft); ModLog.Info("Debug Expression: LookLeft"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha8)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.LookRight); ModLog.Info("Debug Expression: LookRight"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha9)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.A); ModLog.Info("Debug Expression: Mouth A"); }
                    else if (Input.GetKeyDown(KeyCode.Alpha0)) { cachedExpressionBridge.SetDebugOverride(BlendShapePreset.O); ModLog.Info("Debug Expression: Mouth O"); }
                }

                if (!ctrl) cachedExpressionBridge.ClearDebugOverride();
            }
        }

        private VrmExpressionBridge cachedExpressionBridge;

        private void ForceMaterialSync()
        {
            if (cachedRenderers == null || cachedRenderers.Length == 0) return;

            float outlineWidth = Mathf.Clamp(giantess.scale * 0.01f, 0.001f, 0.1f);

            for (int i = 0; i < cachedRenderers.Length; i++)
            {
                var r = cachedRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(propBlock);
                propBlock.SetFloat("_OutlineWidth", outlineWidth);
                r.SetPropertyBlock(propBlock);
            }
        }

        public System.Collections.Generic.List<VrmAnimationBridge.BoneLink> GetBoneLinks()
        {
            return animationBridge != null ? animationBridge.GetLinks() : new System.Collections.Generic.List<VrmAnimationBridge.BoneLink>();
        }

        public void SetOriginalRenderers(System.Collections.Generic.List<Renderer> renderers, Component variation)
        {
            originalRenderers = renderers;
            originalVariation = variation;
        }

        public void ToggleAvatarVisibility()
        {
            vrmVisible = !vrmVisible;

            SetVisibility(vrmVisible);

            foreach (var r in originalRenderers)
            {
                if (r != null)
                    r.enabled = !vrmVisible;
            }

            if (originalVariation != null)
            {
                var varType = originalVariation.GetType();
                var enabledProp = varType.GetProperty("enabled");
                if (enabledProp != null)
                    enabledProp.SetValue(originalVariation, !vrmVisible);
            }

            ModLog.Info($"Avatar toggled - VRM visible: {vrmVisible}, Original visible: {!vrmVisible}");
        }
    }
}
