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
        private readonly Dictionary<Renderer, bool> originalRendererEnabledStates = new Dictionary<Renderer, bool>();
        private Component originalVariation;
        private bool originalVariationEnabled = true;
        private readonly Dictionary<Renderer, bool> rendererEnabledStates = new Dictionary<Renderer, bool>();


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
            rendererEnabledStates.Clear();
            foreach (var renderer in cachedRenderers)
            {
                if (renderer != null)
                    rendererEnabledStates[renderer] = renderer.enabled;
            }

            ForceMaterialSync();

            ModLog.Info($"Avatar setup complete for {gameObject.name} with initial scale ratio: {initialScaleRatio}, cached {cachedRenderers.Length} renderers");
        }

        public Giantess BoundGiantess => giantess;

        public void SetVisibility(bool visible)
        {
            foreach (var pair in rendererEnabledStates)
            {
                if (pair.Key != null)
                    pair.Key.enabled = visible && pair.Value;
            }
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

        public void SetDebugOverride(BlendShapePreset preset) => cachedExpressionBridge?.SetDebugOverride(preset);

        public void ClearDebugOverride() => cachedExpressionBridge?.ClearDebugOverride();

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
            originalRenderers = renderers ?? new List<Renderer>();
            originalVariation = variation;
            originalRendererEnabledStates.Clear();
            foreach (var renderer in originalRenderers)
            {
                if (renderer != null)
                    originalRendererEnabledStates[renderer] = renderer.enabled;
            }
            originalVariationEnabled = GetComponentEnabled(variation, true);
        }

        private static bool GetComponentEnabled(Component component, bool fallback)
        {
            if (component == null) return fallback;
            var property = component.GetType().GetProperty("enabled");
            if (property != null && property.CanRead)
            {
                try { return (bool)property.GetValue(component, null); }
                catch { }
            }
            return fallback;
        }

        /// <summary>
        /// Reasserts the original avatar's hidden state when this cached VRM is
        /// made active. The game can re-enable its renderers during gameplay,
        /// so applying this only during the initial load is not sufficient.
        /// </summary>
        public void EnsureOriginalAvatarHidden()
        {
            foreach (var renderer in originalRenderers)
            {
                if (renderer != null)
                    renderer.enabled = false;
            }

            if (originalVariation != null)
            {
                var enabledProp = originalVariation.GetType().GetProperty("enabled");
                if (enabledProp != null && enabledProp.CanWrite)
                    enabledProp.SetValue(originalVariation, false);
            }
        }

        public void RestoreOriginalAvatar()
        {
            foreach (var renderer in originalRenderers)
            {
                if (renderer != null && originalRendererEnabledStates.TryGetValue(renderer, out bool wasEnabled))
                    renderer.enabled = wasEnabled;
            }

            if (originalVariation != null)
            {
                var enabledProp = originalVariation.GetType().GetProperty("enabled");
                if (enabledProp != null && enabledProp.CanWrite)
                    enabledProp.SetValue(originalVariation, originalVariationEnabled);
            }
        }

        public void ToggleAvatarVisibility()
        {
            vrmVisible = !vrmVisible;

            SetVisibility(vrmVisible);

            foreach (var r in originalRenderers)
            {
                if (r != null)
                    r.enabled = !vrmVisible && originalRendererEnabledStates.TryGetValue(r, out bool wasEnabled) && wasEnabled;
            }

            if (originalVariation != null)
            {
                var varType = originalVariation.GetType();
                var enabledProp = varType.GetProperty("enabled");
                if (enabledProp != null)
                    enabledProp.SetValue(originalVariation, !vrmVisible && originalVariationEnabled);
            }

            ModLog.Info($"Avatar toggled - VRM visible: {vrmVisible}, Original visible: {!vrmVisible}");
        }
    }
}
