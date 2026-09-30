using System.Reflection;
using UnityEngine;
using VRM;
using UniVRM10;

namespace AvatarReplacer
{
    public class VrmLookAtBridge : MonoBehaviour
    {
        private Giantess giantess;
        private IVrmHeadAdapter headAdapter;
        private IVrmExpressionAdapter expressionAdapter;
        private Vrm10Instance vrm10Instance;
        private MethodInfo setLookAtYawPitchMethod;
        private bool vrm10LookAtChecked;

        private float saccadeTimer = 0f;
        private Vector3 currentLookTarget;
        private Vector3 saccadeOffset;
        private float nextSaccadeTime = 0f;

        private bool isGlancingAway = false;
        private float glanceTimer = 0f;
        private Vector3 glanceOffset;

        private float smoothYaw = 0f;
        private float smoothPitch = 0f;

        private BlendShapePreset? lookLeftKey = null;
        private BlendShapePreset? lookRightKey = null;
        private BlendShapePreset? lookUpKey = null;
        private BlendShapePreset? lookDownKey = null;
        private string lookLeftString = null;
        private string lookRightString = null;
        private string lookUpString = null;
        private string lookDownString = null;

        private const float EYE_SMOOTH_SPEED = 12f;
        private const float SACCADE_INTERVAL_MIN = 0.5f;
        private const float SACCADE_INTERVAL_MAX = 2.5f;
        private const float SACCADE_MAGNITUDE = 0.5f;

        private const float GLANCE_CHANCE = 0.0f;
        private const float GLANCE_DURATION_MIN = 0.5f;
        private const float GLANCE_DURATION_MAX = 1.5f;

        private const float MAX_YAW_ANGLE = 60f;
        private const float MAX_PITCH_ANGLE = 45f;

        private const float SENSITIVITY = 2.0f;

        private Transform leftEyeBone;
        private Transform rightEyeBone;
        private Quaternion leftEyeRest;
        private Quaternion rightEyeRest;
        private bool useEyeBoneFallback;
        private Component vrm0LookAt;
        private MethodInfo vrm0SetLookAtYawPitch;

        public void Initialize(Giantess giantess, IVrmHeadAdapter headAdapter, IVrmExpressionAdapter expressionAdapter)
        {
            this.giantess = giantess;
            this.headAdapter = headAdapter;
            this.expressionAdapter = expressionAdapter;
            this.vrm10Instance = GetComponent<Vrm10Instance>() ?? GetComponentInChildren<Vrm10Instance>(true);
            // Legacy VRM 0.x look-at: reflection keeps this version-proof against
            // whatever UniVRM build ships in lib/VRM.dll.
            var vrm0Type = System.Type.GetType("VRM.VRMLookAtHead, VRM") ?? typeof(Vrm10Instance).Assembly.GetType("VRM.VRMLookAtHead");
            if (vrm0Type != null)
            {
                vrm0LookAt = GetComponent(vrm0Type) ?? GetComponentInChildren(vrm0Type, true);
                if (vrm0LookAt != null)
                    vrm0SetLookAtYawPitch = vrm0Type.GetMethod("SetLookAtYawPitch", BindingFlags.Public | BindingFlags.Instance);
            }

            if (expressionAdapter == null)
            {
                ModLog.Error("VrmLookAtBridge: Expression Adapter is null. Eye tracking will fail.");
                return;
            }

            DiscoverEyeBlendShapes();
            DiscoverEyeBoneFallback();

            if (giantess.attentionManager != null && giantess.attentionManager.attentionEyeTransform != null)
            {

                currentLookTarget = giantess.attentionManager.attentionEyeTransform.position;
            }
            else
            {

                 ModLog.Info("[WARN] VrmLookAtBridge: AttentionEyeTransform not found during init. Using default forward.");
                 if (headAdapter != null && headAdapter.Head != null)
                     currentLookTarget = headAdapter.Head.position + headAdapter.Head.forward;
            }

            ModLog.Debug("VrmLookAtBridge: Initialized in PERMANENT MANUAL MODE for lifelike tracking via Adapter.");
        }

        private void DiscoverEyeBlendShapes()
        {
            if (expressionAdapter == null) return;

            if (expressionAdapter.HasClip(BlendShapePreset.LookLeft)) lookLeftKey = BlendShapePreset.LookLeft;
            if (expressionAdapter.HasClip(BlendShapePreset.LookRight)) lookRightKey = BlendShapePreset.LookRight;
            if (expressionAdapter.HasClip(BlendShapePreset.LookUp)) lookUpKey = BlendShapePreset.LookUp;
            if (expressionAdapter.HasClip(BlendShapePreset.LookDown)) lookDownKey = BlendShapePreset.LookDown;

            if (lookLeftKey == null) lookLeftString = FindStringClip("LookLeft", "Lookleft", "look_left", "EyeLeft");
            if (lookRightKey == null) lookRightString = FindStringClip("LookRight", "Lookright", "look_right", "EyeRight");
            if (lookUpKey == null) lookUpString = FindStringClip("LookUp", "Lookup", "look_up", "EyeUp");
            if (lookDownKey == null) lookDownString = FindStringClip("LookDown", "Lookdown", "look_down", "EyeDown");

            int foundCount = (HasLookLeft() ? 1 : 0) + (HasLookRight() ? 1 : 0) +
                           (HasLookUp() ? 1 : 0) + (HasLookDown() ? 1 : 0);
            ModLog.Debug($"VrmLookAtBridge: Discovered {foundCount}/4 eye blend shapes");
        }

        private void DiscoverEyeBoneFallback()
        {
            if (headAdapter == null) return;
            leftEyeBone = headAdapter.GetBone(HumanBodyBones.LeftEye);
            rightEyeBone = headAdapter.GetBone(HumanBodyBones.RightEye);
            useEyeBoneFallback = (leftEyeBone != null && rightEyeBone != null) &&
                !HasLookLeft() && !HasLookRight() && !HasLookUp() && !HasLookDown();
            if (useEyeBoneFallback)
            {
                leftEyeRest = leftEyeBone.localRotation;
                rightEyeRest = rightEyeBone.localRotation;
                ModLog.Debug("VrmLookAtBridge: Using direct eye bone fallback (no look blendshapes)");
            }
        }

        private string FindStringClip(params string[] candidates)
        {
            foreach(var name in expressionAdapter.GetClipNames())
            {
                foreach(var cand in candidates)
                {
                    if (name.Equals(cand, System.StringComparison.OrdinalIgnoreCase)) return name;
                }
            }
            return null;
        }

        private bool HasLookLeft() => lookLeftKey.HasValue || lookLeftString != null;
        private bool HasLookRight() => lookRightKey.HasValue || lookRightString != null;
        private bool HasLookUp() => lookUpKey.HasValue || lookUpString != null;
        private bool HasLookDown() => lookDownKey.HasValue || lookDownString != null;

        private void SetWeight(BlendShapePreset? preset, string key, float weight)
        {
            if (preset.HasValue) expressionAdapter.ImmediatelySetValue(preset.Value, weight);
            else if (key != null) expressionAdapter.ImmediatelySetValue(key, weight);
        }

        void LateUpdate()
        {
            // Note: giantess.attentionManager is frequently still null at Initialize
            // time (game assigns it late), so the target is re-resolved HERE every
            // frame instead of hard-failing when init ran too early.
            if (giantess == null || expressionAdapter == null || headAdapter == null || headAdapter.Head == null) return;

            Vector3 playerTarget;
            if (giantess.attentionManager != null && giantess.attentionManager.attentionEyeTransform != null)
            {
                playerTarget = giantess.attentionManager.attentionEyeTransform.position;
            }
            else if (Camera.main != null)
            {
                playerTarget = Camera.main.transform.position;
            }
            else
            {
                playerTarget = headAdapter.Head.position + headAdapter.Head.forward * 2f;
            }

            UpdateLifelikeBehavior(playerTarget);

            ApplyManualEyeMovement();

            if (AvatarReplacerPlugin.IsDebugEnabled && Time.frameCount % 300 == 0)
            {
                ModLog.Debug($"VrmLookAtBridge: Mode LIFELIKE MANUAL, Yaw={smoothYaw:F2}, Pitch={smoothPitch:F2}, Glancing={isGlancingAway}");
            }
        }

        private void UpdateLifelikeBehavior(Vector3 baseTarget)
        {
            float dt = Time.deltaTime;

            saccadeTimer += dt;
            if (saccadeTimer >= nextSaccadeTime)
            {
                saccadeTimer = 0f;
                nextSaccadeTime = Random.Range(SACCADE_INTERVAL_MIN, SACCADE_INTERVAL_MAX);
                saccadeOffset = Random.insideUnitSphere * SACCADE_MAGNITUDE;
            }

            if (isGlancingAway)
            {
                glanceTimer -= dt;
                if (glanceTimer <= 0)
                {
                    isGlancingAway = false;
                }
            }
            else
            {

                if (Random.value < GLANCE_CHANCE)
                {
                    isGlancingAway = true;
                    glanceTimer = Random.Range(GLANCE_DURATION_MIN, GLANCE_DURATION_MAX);
                    glanceOffset = Random.insideUnitSphere * 5.0f;
                    glanceOffset.y *= 0.5f;
                }
            }

            Vector3 finalTargetPosition = baseTarget + saccadeOffset;

            if (isGlancingAway)
            {
                finalTargetPosition = headAdapter.Head.position + headAdapter.Head.forward * 2f + glanceOffset;
            }

            currentLookTarget = Vector3.Lerp(currentLookTarget, finalTargetPosition, dt * 5f);
        }

        private void TryApplyVrm10LookAt(float yawDeg, float pitchDeg)
        {
            if (vrm10Instance == null) return;
            object lookAt = vrm10Instance.Runtime?.LookAt;
            if (lookAt == null) return;
            if (!vrm10LookAtChecked)
            {
                vrm10LookAtChecked = true;
                var type = lookAt.GetType();
                setLookAtYawPitchMethod = type.GetMethod("SetLookAtYawPitch", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(float), typeof(float) }, null);
                if (setLookAtYawPitchMethod == null)
                    setLookAtYawPitchMethod = type.GetMethod("SetLookAtYawPitch", BindingFlags.Public | BindingFlags.Instance);
            }
            if (setLookAtYawPitchMethod != null)
            {
                try
                {
                    // UniVRM10 accepts DEGREES here and converts internally.
                    // The previous code multiplied by Deg2Rad, feeding degree-valued
                    // radians (max ~1 rad instead of ~60 deg) which collapsed VRM1
                    // gaze to almost zero movement.
                    setLookAtYawPitchMethod.Invoke(lookAt, new object[] { yawDeg, pitchDeg });
                }
                catch (System.Exception) {  }
            }
        }

        private void TryApplyVrm0LookAt(float yawDeg, float pitchDegUp)
        {
            if (vrm0LookAt == null || vrm0SetLookAtYawPitch == null) return;
            try
            {
                // Legacy VRMLookAtHead expects radians and a down-positive pitch
                // (the convention this class originally computed). Our smoothPitch
                // is up-positive, so negate before passing it on.
                vrm0SetLookAtYawPitch.Invoke(vrm0LookAt, new object[] { yawDeg * Mathf.Deg2Rad, -pitchDegUp * Mathf.Deg2Rad });
            }
            catch (System.Exception) { }
        }

        private void ApplyManualEyeMovement()
        {
            Transform head = headAdapter.Head;
            if (head == null) return;

            Vector3 directionToTarget = (currentLookTarget - head.position).normalized;
            Vector3 localDirection = head.InverseTransformDirection(directionToTarget);

            // Conventions used throughout this class:
            //   yaw   positive = target is to the avatar's RIGHT
            //   pitch positive = target is ABOVE the head (up-positive)
            float targetYaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Asin(Mathf.Clamp(localDirection.y, -1f, 1f)) * Mathf.Rad2Deg;

            targetYaw = Mathf.Clamp(targetYaw, -MAX_YAW_ANGLE, MAX_YAW_ANGLE);
            targetPitch = Mathf.Clamp(targetPitch, -MAX_PITCH_ANGLE, MAX_PITCH_ANGLE);

            smoothYaw = Mathf.Lerp(smoothYaw, targetYaw, Time.deltaTime * EYE_SMOOTH_SPEED);
            smoothPitch = Mathf.Lerp(smoothPitch, targetPitch, Time.deltaTime * EYE_SMOOTH_SPEED);

            TryApplyVrm10LookAt(smoothYaw, smoothPitch);
            TryApplyVrm0LookAt(smoothYaw, smoothPitch);

            bool shouldLog = AvatarReplacerPlugin.IsDebugEnabled && (Time.frameCount % 300 == 0);

            if (smoothYaw > 0)
            {
                float weight = Mathf.Clamp01((smoothYaw / MAX_YAW_ANGLE) * SENSITIVITY);
                if (HasLookRight())
                {
                    SetWeight(lookRightKey, lookRightString, weight);
                    if (shouldLog) ModLog.Debug($"VrmLookAtBridge: MANUAL LookRight={weight:F3} (Yaw={smoothYaw:F1})");
                }
                if (HasLookLeft()) SetWeight(lookLeftKey, lookLeftString, 0f);
            }
            else if (smoothYaw < 0)
            {
                float weight = Mathf.Clamp01((-smoothYaw / MAX_YAW_ANGLE) * SENSITIVITY);
                if (HasLookLeft())
                {
                    SetWeight(lookLeftKey, lookLeftString, weight);
                    if (shouldLog) ModLog.Debug($"VrmLookAtBridge: MANUAL LookLeft={weight:F3} (Yaw={smoothYaw:F1})");
                }
                if (HasLookRight()) SetWeight(lookRightKey, lookRightString, 0f);
            }
            else
            {
                if (HasLookLeft()) SetWeight(lookLeftKey, lookLeftString, 0f);
                if (HasLookRight()) SetWeight(lookRightKey, lookRightString, 0f);
            }

            // smoothPitch is up-positive: target above head => LookUp.
            // The previous code mapped positive pitch to LookUp while computing
            // pitch as down-positive, so vertical gaze was fully inverted.
            if (smoothPitch > 0)
            {
                float weight = Mathf.Clamp01((smoothPitch / MAX_PITCH_ANGLE) * SENSITIVITY);
                if (HasLookUp())
                {
                    SetWeight(lookUpKey, lookUpString, weight);
                    if (shouldLog) ModLog.Debug($"VrmLookAtBridge: MANUAL LookUp={weight:F3} (Pitch={smoothPitch:F1})");
                }
                if (HasLookDown()) SetWeight(lookDownKey, lookDownString, 0f);
            }
            else if (smoothPitch < 0)
            {
                float weight = Mathf.Clamp01((-smoothPitch / MAX_PITCH_ANGLE) * SENSITIVITY);
                if (HasLookDown())
                {
                    SetWeight(lookDownKey, lookDownString, weight);
                    if (shouldLog) ModLog.Debug($"VrmLookAtBridge: MANUAL LookDown={weight:F3} (Pitch={smoothPitch:F1})");
                }
                if (HasLookUp()) SetWeight(lookUpKey, lookUpString, 0f);
            }
            else
            {
                if (HasLookUp()) SetWeight(lookUpKey, lookUpString, 0f);
                if (HasLookDown()) SetWeight(lookDownKey, lookDownString, 0f);
            }

            if (useEyeBoneFallback && leftEyeBone != null && rightEyeBone != null)
            {
                // smoothPitch is up-positive; Unity's positive X rotation pitches
                // DOWN, so negate here. Yaw stays as-is (positive Y = turn right).
                Quaternion rot = Quaternion.Euler(-smoothPitch, smoothYaw, 0f);
                leftEyeBone.localRotation = leftEyeRest * rot;
                rightEyeBone.localRotation = rightEyeRest * rot;
            }
        }
    }
}
