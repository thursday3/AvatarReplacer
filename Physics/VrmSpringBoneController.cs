using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AvatarReplacer
{
    /// <summary>
    /// Keeps UniVRM 0.x/1.0 SpringBone aware of runtime avatar scaling.
    /// This intentionally does not fake physics by changing stiffness, drag,
    /// gravity, or collider radii. UniVRM owns those parameters.
    /// </summary>
    public sealed class VrmSpringBoneController : MonoBehaviour
    {
        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Giantess giantess;
        private float lastScale = -1f;
        private float lastDiagnosticTime = -999f;
        private float lastReconstructedScale = -1f;
        private float lastReconstructTime = -999f;
        private bool scalingSupportConfigured;
        private bool hasVrm10;
        private object vrm10Runtime;
        private object vrm10SpringRuntime;
        private MethodInfo vrm10Process;
        private MethodInfo vrm10SetModelLevel;
        private MethodInfo vrm10Reconstruct;
        private MethodInfo vrm10Restore;
        private Type vrm10ModelLevelType;
        private Component[] vrm0SpringBones = Array.Empty<Component>();
        private float stiffness = -1f;
        private float gravityPower = -1f;
        private float drag = -1f;

        public void Initialize(Giantess owner, float stiffnessOverride = -1f, float gravityPowerOverride = -1f, float dragOverride = -1f)
        {
            giantess = owner;
            // Authored VRM spring bones carry the avatar's own physics. Only apply
            // overrides when the user actually configured values (per-avatar
            // profile or a customized global config); blindly overwriting them
            // made hair/skirts look wrong (e.g. gravity 3.0 on chains authored
            // for ~0). Overrides are per-field: unset ones stay -1 and are
            // skipped by ApplyInitialVrm0Parameters.
            if (stiffnessOverride > 0f || gravityPowerOverride > 0f || dragOverride > 0f)
            {
                stiffness = stiffnessOverride;
                gravityPower = gravityPowerOverride;
                drag = dragOverride;
            }
            vrm0SpringBones = FindVrm0SpringBones();
            ApplyInitialVrm0Parameters();
            DiscoverVrm10Runtime();
            ConfigureScalingSupport();

            ModLog.Info($"SpringBone controller initialized: VRM0={vrm0SpringBones.Length}, VRM1={hasVrm10}, RuntimeScaling={scalingSupportConfigured}, ParamOverrides={(stiffness > 0f || gravityPower > 0f || drag > 0f ? "on" : "off")}");
        }

        public void ProcessAfterAnimationFrame()
        {
            if (giantess == null) return;

            float scale = Mathf.Max(giantess.scale, 0.0001f);
            bool changed = lastScale > 0f && !Mathf.Approximately(scale, lastScale);
            float ratio = changed ? scale / Mathf.Max(lastScale, 0.0001f) : 1f;
            bool largeChange = changed && ShouldReconstruct(scale);

            // UniVRM documents that a model scale change requires a SpringBone
            // reconstruction because initial scale-dependent state must be rebuilt.
            if (changed)
            {
                ConfigureScalingSupport();
                if (largeChange)
                {
                    ReconstructAfterScaleChange(ratio);
                    lastReconstructedScale = scale;
                    lastReconstructTime = Time.time;
                }
            }

            // VRM 1.0 is manually processed by VrmAnimationBridge so that GTS
            // animation/IK is already final before SpringBone writes back.
            ProcessVrm10IfManual();

            if (AvatarReplacerPlugin.SpringBoneDiagnosticsValue &&
                Time.time - lastDiagnosticTime >= AvatarReplacerPlugin.SpringBoneDiagnosticsIntervalValue)
            {
                EmitDiagnostics(scale, ratio, changed, largeChange);
                lastDiagnosticTime = Time.time;
            }

            lastScale = scale;
        }

        private void ApplyInitialVrm0Parameters()
        {
            if (stiffness <= 0f && gravityPower <= 0f && drag <= 0f) return;
            foreach (var spring in vrm0SpringBones)
            {
                try
                {
                    // Per-field guards: only overwrite parameters the user or
                    // profile actually configured. Writing the unset (-1) sentinels
                    // into VRMSpringBone fields produced nonsense physics.
                    if (stiffness > 0f) SetField(spring, "m_stiffnessForce", stiffness);
                    if (gravityPower > 0f) SetField(spring, "m_gravityPower", gravityPower);
                    if (gravityPower > 0f) SetField(spring, "m_gravityDir", Vector3.down);
                    if (drag > 0f) SetField(spring, "m_dragForce", drag);
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"SpringBone: failed to apply initial VRM0 settings: {ex.Message}");
                }
            }
        }

        private static void SetField(Component component, string name, object value)
        {
            if (component == null || value == null) return;
            var field = component.GetType().GetField(name, InstanceFlags);
            if (field != null) field.SetValue(component, value);
        }

        private Component[] FindVrm0SpringBones()
        {
            var list = new List<Component>();
            foreach (var c in GetComponentsInChildren<Component>(true))
            {
                if (c != null && string.Equals(c.GetType().Name, "VRMSpringBone", StringComparison.Ordinal))
                    list.Add(c);
            }
            return list.ToArray();
        }

        private void DiscoverVrm10Runtime()
        {
            // Use the referenced type directly; resolving it loads the embedded
            // VRM10 assembly before the component lookup.
            var type = typeof(UniVRM10.Vrm10Instance);

            var instance = GetComponent(type) ?? GetComponentInChildren(type, true);
            if (instance == null) return;

            try
            {
                var runtimeProp = type.GetProperty("Runtime", InstanceFlags);
                object runtime = runtimeProp?.GetValue(instance, null);
                if (runtime == null) return;
                vrm10Runtime = runtime;
                vrm10Process = runtime.GetType().GetMethod("Process", InstanceFlags, null, Type.EmptyTypes, null);

                var springProp = runtime.GetType().GetProperty("SpringBone", InstanceFlags);
                vrm10SpringRuntime = springProp?.GetValue(runtime, null);
                if (vrm10SpringRuntime == null) return;

                hasVrm10 = true;
                var springType = vrm10SpringRuntime.GetType();
                vrm10SetModelLevel = springType.GetMethod("SetModelLevel", InstanceFlags);
                vrm10Reconstruct = springType.GetMethod("ReconstructSpringBone", InstanceFlags, null, Type.EmptyTypes, null);
                vrm10Restore = springType.GetMethod("RestoreInitialTransform", InstanceFlags, null, Type.EmptyTypes, null);

                if (vrm10SetModelLevel != null)
                {
                    var parameters = vrm10SetModelLevel.GetParameters();
                    if (parameters.Length == 1)
                        vrm10ModelLevelType = parameters[0].ParameterType;
                    else if (parameters.Length == 2)
                        vrm10ModelLevelType = parameters[1].ParameterType;
                }
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SpringBone: VRM1 runtime discovery failed: {ex.Message}");
            }
        }

        private void ConfigureScalingSupport()
        {
            bool configured0 = false;
            foreach (var spring in vrm0SpringBones)
            {
                try
                {
                    var prop = spring.GetType().GetProperty("UseRuntimeScalingSupport", InstanceFlags);
                    if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                    {
                        prop.SetValue(spring, true, null);
                        configured0 = true;
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"SpringBone: failed to enable VRM0 runtime scaling on {spring.name}: {ex.Message}");
                }
            }

            bool configured1 = false;
            if (vrm10SpringRuntime != null && vrm10SetModelLevel != null && vrm10ModelLevelType != null)
            {
                try
                {
                    object level = Activator.CreateInstance(vrm10ModelLevelType);
                    SetMember(level, "SupportsScalingAtRuntime", true);
                    SetMember(level, "StopSpringBoneWriteback", false);
                    SetMember(level, "ExternalForce", Vector3.zero);

                    var parameters = vrm10SetModelLevel.GetParameters();
                    if (parameters.Length == 1)
                        vrm10SetModelLevel.Invoke(vrm10SpringRuntime, new[] { level });
                    else if (parameters.Length == 2)
                        vrm10SetModelLevel.Invoke(vrm10SpringRuntime, new object[] { transform, level });
                    else
                        return;

                    configured1 = true;
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"SpringBone: failed to enable VRM1 runtime scaling: {ex.Message}");
                }
            }

            scalingSupportConfigured = configured0 || configured1;
        }

        private bool ShouldReconstruct(float scale)
        {
            if (lastReconstructedScale <= 0f) return true;
            if (Time.time - lastReconstructTime < 0.15f) return false;
            float ratio = scale / Mathf.Max(lastReconstructedScale, 0.0001f);
            return ratio >= 1.5f || ratio <= 0.6666667f;
        }

        private void ReconstructAfterScaleChange(float ratio)
        {
            bool reconstructed = false;

            foreach (var spring in vrm0SpringBones)
            {
                try
                {
                    var method = spring.GetType().GetMethod("ReconstructSpringBone", InstanceFlags, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        method.Invoke(spring, null);
                        reconstructed = true;
                        continue;
                    }

                    method = spring.GetType().GetMethod("Setup", InstanceFlags, null, new[] { typeof(bool) }, null);
                    if (method != null)
                    {
                        method.Invoke(spring, new object[] { true });
                        reconstructed = true;
                        continue;
                    }

                    method = spring.GetType().GetMethod("Setup", InstanceFlags, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        method.Invoke(spring, null);
                        reconstructed = true;
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"SpringBone: VRM0 scale reconstruction failed: {ex.Message}");
                }
            }

            if (vrm10SpringRuntime != null)
            {
                try
                {
                    if (vrm10Reconstruct != null)
                    {
                        reconstructed |= Convert.ToBoolean(vrm10Reconstruct.Invoke(vrm10SpringRuntime, null));
                    }
                    if (vrm10Restore != null && (ratio >= 3f || ratio <= 0.3333333f))
                    {
                        // Only clear velocity for truly extreme jumps; normal
                        // growth should preserve the current pose.
                        vrm10Restore.Invoke(vrm10SpringRuntime, null);
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warning($"SpringBone: VRM1 scale reconstruction failed: {ex.Message}");
                }
            }

            ModLog.Debug($"SpringBone: runtime scale change rebased (success={reconstructed}, scale={giantess.scale:F3})");
        }

        private void ProcessVrm10IfManual()
        {
            if (vrm10Runtime == null || vrm10Process == null) return;
            try
            {
                vrm10Process.Invoke(vrm10Runtime, null);
            }
            catch (Exception ex)
            {
                ModLog.Warning($"SpringBone: VRM1 Runtime.Process failed: {ex.Message}");
            }
        }

        private void EmitDiagnostics(float scale, float ratio, bool changed, bool largeChange)
        {
            float lossy = transform.lossyScale.x;
            string runtimeType = vrm10SpringRuntime != null ? vrm10SpringRuntime.GetType().Name : "none";
            ModLog.Debug(
                $"SpringBone diagnostics: GTSScale={scale:F3}, AvatarLossyScale={lossy:F3}, " +
                $"DeltaRatio={(changed ? ratio : 1f):F3}, LargeChange={largeChange}, " +
                $"VRM0={vrm0SpringBones.Length}, VRM1={hasVrm10}, Runtime={runtimeType}, " +
                $"RuntimeScaling={scalingSupportConfigured}");
        }

        private static void SetMember(object target, string name, object value)
        {
            if (target == null) return;
            var type = target.GetType();
            var prop = type.GetProperty(name, InstanceFlags);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, value, null);
                return;
            }
            var field = type.GetField(name, InstanceFlags);
            if (field != null)
                field.SetValue(target, value);
        }
    }
}
