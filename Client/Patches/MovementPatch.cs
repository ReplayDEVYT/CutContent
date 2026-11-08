using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class Movement
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _inertiaScale;
        private static ConfigEntry<float> _landingSlowdown;
        private static ConfigEntry<bool> _keepSensitivity;
        private static ConfigEntry<bool> _slideMomentum;
        private static ConfigEntry<float> _slideFriction;
        private static ConfigEntry<float> _slideSteering;
        private static ConfigEntry<float> _slideBrake;
        private static ConfigEntry<float> _diveMomentum;
        private static ConfigEntry<float> _diveFriction;
        private static ConfigEntry<float> _diveSteering;
        private static ConfigEntry<bool> _alphaTwist;
        private static ConfigEntry<bool> _alphaMovement;
        private static ConfigEntry<float> _alphaWalkSpeed;
        private static ConfigEntry<float> _alphaSprintSpeed;

        private static float _originalTwist = -1f;
        private static float _originalTurn = -1f;

        private static float _slideRequestTime = -10f;
        private static bool _isSlide;
        private static float _lastSlideTime = -10f;
        private static bool _carrying;
        private static Vector2 _slideInput;
        private static Vector3 _diveVelocity;
        private static Vector2 _alphaTarget;
        private static Vector2 _alphaFrom;
        private static float _alphaDistance;
        private static float _alphaTime;
        private static bool _curveChecked;
        private static bool _curveUsable;

        public static void Enable(ConfigFile config)
        {
            const string section = "Movement";
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Movement", "Movement", true, "Alpha-style movement: no weight inertia, no dead stops, short slides and dives that carry you into cover, and no control or sensitivity loss while doing them.", section, "Enabled");
            _inertiaScale = config.Bind(section, "Weight Inertia", 0f, new ConfigDescription("How much carried weight turns into movement inertia (slow starts, wide turns, heavy stops). 0 is the alpha, which had no inertia system. 1 is the current game.", new AcceptableValueRange<float>(0f, 1f)));
            _landingSlowdown = config.Bind(section, "Landing Slowdown", 0.4f, new ConfigDescription("How strongly landing from a jump or fall slows you down. 1 is the current game.", new AcceptableValueRange<float>(0f, 1f)));
            _alphaMovement = config.Bind(section, "Alpha Movement", true, "Uses the alpha's movement model: walking direction eases along the alpha direction curve, speed, stance and leaning ease at the alpha rates with no stamina or weight penalties, stopping uses only the animation, and there is no dead stop when reversing direction.");
            _alphaWalkSpeed = config.Bind(section, "Alpha Walk Speed", 0.85f, new ConfigDescription("With Alpha Movement on, scales how fast everyone walks and runs, including bots. 1 is the current game's speed.", new AcceptableValueRange<float>(0.5f, 1f)));
            _alphaSprintSpeed = config.Bind(section, "Alpha Sprint Speed", 0.85f, new ConfigDescription("With Alpha Movement on, scales how fast everyone sprints, including bots. 1 is the current game's speed.", new AcceptableValueRange<float>(0.5f, 1f)));
            _keepSensitivity = config.Bind(section, "Keep Look Sensitivity", true, "Sliding, diving, landing and getting up from prone no longer lower or lock mouse sensitivity.");
            _slideMomentum = config.Bind(section, "Slide Momentum", true, "Crouching while sprinting slides you a short way instead of stopping dead. A and D nudge the slide and S brakes.");
            _diveMomentum = config.Bind(section, "Dive Carry", 0.35f, new ConfigDescription("How much sprint speed a prone dive keeps. 0 is the current game, where the dive stops you dead. A and D nudge the dive.", new AcceptableValueRange<float>(0f, 1f)));
            _slideFriction = config.Bind(section, "Slide Grip", 7f, ConfigHelper.Advanced("How quickly a slide loses speed. Lower slides further.", new AcceptableValueRange<float>(1f, 20f)));
            _slideSteering = config.Bind(section, "Slide Nudge", 3f, ConfigHelper.Advanced("How hard A and D redirect a slide.", new AcceptableValueRange<float>(0f, 20f)));
            _slideBrake = config.Bind(section, "Slide Brake", 3f, ConfigHelper.Advanced("Extra friction while holding S during a slide.", new AcceptableValueRange<float>(1f, 8f)));
            _diveFriction = config.Bind(section, "Dive Grip", 6f, ConfigHelper.Advanced("How quickly a dive loses speed. Lower dives further.", new AcceptableValueRange<float>(1f, 20f)));
            _diveSteering = config.Bind(section, "Dive Nudge", 2f, ConfigHelper.Advanced("How hard A and D redirect a dive.", new AcceptableValueRange<float>(0f, 20f)));

            _alphaTwist = config.Bind(section, "Alpha Body Twist", true, "Restores the alpha's 55 degree upper body twist (45 now), so you can watch an angle further before your legs turn and shift your position.");
            ConfigHelper.Requires(_alphaMovement, _alphaWalkSpeed, _alphaSprintSpeed);
            ConfigHelper.Requires(_slideMomentum, _slideFriction, _slideSteering, _slideBrake);

            _enabled.SettingChanged += (s, e) => { RefreshInertia(); ApplyHardSettings(); };
            _alphaTwist.SettingChanged += (s, e) => ApplyHardSettings();
            ApplyHardSettings();
            _inertiaScale.SettingChanged += (s, e) => RefreshInertia();

            new InertiaPatch().Enable();
            new LandingPatch().Enable();
            new SensitivityPatch().Enable();
            new SprintCrouchPatch().Enable();
            new TransitionEnterPatch().Enable();
            new TransitionMovePatch().Enable();
            new TransitionBlendPatch().Enable();
            new IdleBlendPatch().Enable();
            new DiveEnterPatch().Enable();
            new DiveMotionPatch().Enable();
            new RunEnterPatch().Enable();
            new DirectionPatch().Enable();
            new PosePatch().Enable();
            new LeanPatch().Enable();
            new SpeedScalePatch().Enable();
        }

        private class SpeedScalePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.DirectApplyMotion));
            }

            [PatchPrefix]
            private static void Prefix(MovementContext __instance, ref Vector3 motion)
            {
                if (!_enabled.Value || !_alphaMovement.Value || __instance == null)
                {
                    return;
                }
                EPlayerState? state = __instance.CurrentState?.Name;
                float scale;
                if (state == EPlayerState.Sprint)
                {
                    scale = _alphaSprintSpeed.Value;
                }
                else if (state == EPlayerState.Run)
                {
                    scale = _alphaWalkSpeed.Value;
                }
                else
                {
                    return;
                }
                motion.x *= scale;
                motion.z *= scale;
            }
        }

        private static void RefreshInertia()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            player?.Physical?.OnWeightUpdated();
        }

        private static void ApplyHardSettings()
        {
            EFTHardSettings settings = EFTHardSettings.Instance;
            if (settings == null)
            {
                return;
            }
            if (_originalTwist < 0f)
            {
                _originalTwist = settings.HANDS_TO_BODY_MAX_ANGLE;
                _originalTurn = settings.TURN_ANGLE;
            }
            bool alpha = _enabled.Value && _alphaTwist.Value;
            settings.HANDS_TO_BODY_MAX_ANGLE = alpha ? 55f : _originalTwist;
            settings.TURN_ANGLE = alpha ? 55f : _originalTurn;
        }

        private static bool IsLocal(MovementContext context)
        {
            return _enabled.Value && context != null && !context.IsAI;
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        private static Vector3 FlatRight(MovementContext context)
        {
            Vector3 right = Flat(context.TransformVector(Vector3.right));
            return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.zero;
        }

        private static Vector3 Steer(MovementContext context, Vector3 velocity, float input, float strength, float deltaTime)
        {
            if (input == 0f || strength <= 0f)
            {
                return velocity;
            }
            float speed = velocity.magnitude;
            Vector3 steered = velocity + FlatRight(context) * Math.Sign(input) * strength * deltaTime;
            return Vector3.ClampMagnitude(steered, speed);
        }

        private static Vector3 CarryMotion(MovementContext context, Vector3 motion, float deltaTime, Vector2 input)
        {
            Vector3 root = motion / deltaTime;
            Vector3 previous = Flat(context.LastBlendMotionDelta);
            float friction = _slideFriction.Value * (input.y < -0.1f ? _slideBrake.Value : 1f);
            float keep = Mathf.Exp(-friction * deltaTime);
            Vector3 velocity = Vector3.Lerp(Flat(root), previous, keep);
            velocity = Steer(context, velocity, input.x, _slideSteering.Value, deltaTime);
            velocity.y = root.y;
            return velocity * deltaTime;
        }

        private class InertiaPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(PhysicalBase), nameof(PhysicalBase.CalculateValue));
            }

            [PatchPostfix]
            private static void Postfix(PhysicalBase __instance, Vector3 dependency, ref float __result)
            {
                if (!_enabled.Value || dependency != __instance.BaseInertiaLimits)
                {
                    return;
                }
                if (__instance._playerBridge?.iPlayer == null || __instance._playerBridge.iPlayer.IsAI)
                {
                    return;
                }
                __result *= _inertiaScale.Value;
            }
        }

        private class LandingPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.ChangeSpeedLimit), new[] { typeof(float), typeof(Player.ESpeedLimit), typeof(float) });
            }

            [PatchPrefix]
            private static void Prefix(MovementContext __instance, ref float speedDelta, Player.ESpeedLimit cause, ref float duration)
            {
                if (cause != Player.ESpeedLimit.Fall || !IsLocal(__instance))
                {
                    return;
                }
                float scale = _landingSlowdown.Value;
                speedDelta = Mathf.Lerp(__instance.MaxSpeed, speedDelta, scale);
                duration *= scale;
            }
        }

        private class SensitivityPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementState), nameof(MovementState.ClampRotation));
            }

            [PatchPrefix]
            private static bool Prefix(MovementState __instance, Vector3 deltaRotation, ref Vector3 __result)
            {
                if (!_keepSensitivity.Value || !IsLocal(__instance.MovementContext))
                {
                    return true;
                }
                if (!(__instance is SprintToIdleTransitionState || __instance is Transit2ProneState || __instance is Prone2StandState || __instance is JumpLandingState))
                {
                    return true;
                }
                Vector2 sensed = __instance.MovementContext.ApplyExternalSense(new Vector2(deltaRotation.x, deltaRotation.y));
                __result = new Vector3(sensed.x, sensed.y, 0f);
                return false;
            }
        }

        private class SprintCrouchPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SprintPlayerState), nameof(SprintPlayerState.ChangePose));
            }

            [PatchPostfix]
            private static void Postfix(SprintPlayerState __instance, float poseDelta)
            {
                if (poseDelta < 0f && IsLocal(__instance.MovementContext))
                {
                    _slideRequestTime = Time.time;
                }
            }
        }

        private class TransitionEnterPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SprintToIdleTransitionState), nameof(SprintToIdleTransitionState.Enter));
            }

            [PatchPostfix]
            private static void Postfix(SprintToIdleTransitionState __instance)
            {
                if (!IsLocal(__instance.MovementContext))
                {
                    return;
                }
                _isSlide = _slideMomentum.Value && Time.time - _slideRequestTime < 0.5f;
                _slideInput = Vector2.zero;
            }
        }

        private class TransitionMovePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementState), nameof(MovementState.Move));
            }

            [PatchPrefix]
            private static void Prefix(MovementState __instance, Vector2 direction)
            {
                if (!(__instance is SprintToIdleTransitionState))
                {
                    return;
                }
                MovementContext context = __instance.MovementContext;
                if (!IsLocal(context))
                {
                    return;
                }
                _slideInput = direction;
            }
        }

        private class TransitionBlendPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SprintToIdleTransitionState), nameof(SprintToIdleTransitionState.BlendMotion));
            }

            [PatchPrefix]
            private static bool Prefix(SprintToIdleTransitionState __instance, ref Vector3 motion, float deltaTime)
            {
                MovementContext context = __instance.MovementContext;
                if (!IsLocal(context) || deltaTime <= 0f)
                {
                    return true;
                }
                if (!_isSlide)
                {
                    return !_alphaMovement.Value;
                }
                _lastSlideTime = Time.time;
                _carrying = true;
                motion = CarryMotion(context, motion, deltaTime, _slideInput);
                return false;
            }
        }

        private class IdleBlendPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(IdlePlayerState), nameof(IdlePlayerState.BlendMotion));
            }

            [PatchPrefix]
            private static bool Prefix(IdlePlayerState __instance, ref Vector3 motion, float deltaTime)
            {
                MovementContext context = __instance.MovementContext;
                if (!IsLocal(context) || deltaTime <= 0f)
                {
                    return true;
                }
                if (!_carrying || __instance.GetType() != typeof(IdlePlayerState) || Time.time - _lastSlideTime > 1.5f || Flat(context.LastBlendMotionDelta).magnitude < 0.3f)
                {
                    _carrying = false;
                    return !_alphaMovement.Value;
                }
                motion = CarryMotion(context, motion, deltaTime, Vector2.zero);
                return false;
            }
        }

        private class DiveEnterPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Transit2ProneState), nameof(Transit2ProneState.Enter));
            }

            [PatchPostfix]
            private static void Postfix(Transit2ProneState __instance, bool isFromSameState)
            {
                MovementContext context = __instance.MovementContext;
                if (isFromSameState || !IsLocal(context))
                {
                    return;
                }
                Vector3 velocity = Flat(context.LastBlendMotionDelta);
                _diveVelocity = velocity.magnitude > 2.5f ? velocity * _diveMomentum.Value : Vector3.zero;
            }
        }

        private class DiveMotionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Transit2ProneState), nameof(Transit2ProneState.ProcessAnimatorMovement));
            }

            [PatchPrefix]
            private static bool Prefix(Transit2ProneState __instance, float deltaTime)
            {
                MovementContext context = __instance.MovementContext;
                if (!IsLocal(context) || _diveVelocity.sqrMagnitude < 0.01f)
                {
                    return true;
                }

                Vector3 motion = context.PlayerAnimatorDeltaPosition;
                __instance.ApplyGravity(ref motion, deltaTime);
                Vector3 forward = context.ForwardProjectionOnSurface;
                if (context.OverlapOrHasNoGround(0.75f, forward, 0f, 4f, 0.2f))
                {
                    _diveVelocity = Vector3.zero;
                    context.RestorePreviousYaw();
                    Vector3 local = context.InverseTransformVector(motion);
                    __instance._depenetration = Mathf.Lerp(__instance._depenetration, Mathf.Max(0.01f, context.GetPenetrationDepth(forward, 0.35f)), deltaTime * 10f);
                    local.z = -__instance._depenetration;
                    motion = context.TransformVector(local);
                }
                else if (context.OverlapOrHasNoGround(0.75f, -forward, 0.25f, 4f, 0.15f))
                {
                    context.RestorePreviousYaw();
                    Vector3 local = context.InverseTransformVector(motion);
                    __instance._depenetration = Mathf.Lerp(__instance._depenetration, Mathf.Max(0.01f, context.GetPenetrationDepth(-forward, 0.4f)), deltaTime * 7f);
                    local.z = __instance._depenetration;
                    motion = context.TransformVector(local);
                }
                else
                {
                    _diveVelocity *= Mathf.Exp(-_diveFriction.Value * deltaTime);
                    _diveVelocity = Steer(context, _diveVelocity, context.MovementDirection.x, _diveSteering.Value, deltaTime);
                    motion += _diveVelocity * deltaTime;
                }
                context.ApplyMotion(motion, deltaTime);
                context.ApplyRotation(context.TransformRotation * context.AnimatorDeltaRotation);
                return false;
            }
        }

        private static bool DirectionCurveUsable()
        {
            if (_curveChecked)
            {
                return _curveUsable;
            }
            _curveChecked = true;
            AnimationCurve curve = EFTHardSettings.Instance.DIRECTION_CURVE;
            string keys = "";
            if (curve != null)
            {
                for (int i = 0; i < curve.length; i++)
                {
                    keys += $" ({curve[i].time:0.###}, {curve[i].value:0.###})";
                }
            }
            _curveUsable = curve != null && curve.length > 1
                && curve[curve.length - 1].value >= 0.99f
                && curve[curve.length - 1].value - curve[0].value > 0.5f;
            Plugin.LogSource.LogInfo(_curveUsable
                ? "Alpha Movement: using the alpha direction curve" + keys
                : "Alpha Movement: direction curve is a leftover placeholder" + keys + ", using the alpha direction lerp instead.");
            return _curveUsable;
        }

        private static bool UseAlphaModel(MovementContext context)
        {
            return _alphaMovement.Value && IsLocal(context) && !context.IsInPronePose;
        }

        private class RunEnterPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovePlayerState), nameof(MovePlayerState.Enter));
            }

            [PatchPostfix]
            private static void Postfix(MovePlayerState __instance)
            {
                if (UseAlphaModel(__instance.MovementContext))
                {
                    _alphaTarget = Vector2.zero;
                    _alphaTime = 0f;
                }
            }
        }

        private class DirectionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovePlayerState), nameof(MovePlayerState.UpdateMovementDirection));
            }

            [PatchPrefix]
            private static bool Prefix(MovePlayerState __instance, float deltaTime)
            {
                MovementContext context = __instance.MovementContext;
                if (!UseAlphaModel(context))
                {
                    return true;
                }

                __instance.moveTime = 0f;
                Vector2 input = __instance.Direction;
                float targetSpeed = Mathf.Clamp(context.ClampedSpeed, 0.01f, 1f);
                if (input.sqrMagnitude > 1E-10f)
                {
                    if (input != _alphaTarget)
                    {
                        _alphaTime = 0f;
                        _alphaFrom = context.MovementDirection;
                        _alphaDistance = (input - _alphaFrom).magnitude;
                        _alphaTarget = input;
                    }
                    _alphaTime += deltaTime;
                    Vector2 direction;
                    if (_alphaDistance < 0.001f)
                    {
                        direction = input;
                    }
                    else if (DirectionCurveUsable())
                    {
                        direction = Vector2.Lerp(_alphaFrom, input, Mathf.Clamp01(EFTHardSettings.Instance.DIRECTION_CURVE.Evaluate(_alphaTime / _alphaDistance)));
                    }
                    else
                    {
                        direction = Vector2.Lerp(context.MovementDirection, input, Mathf.Clamp01(deltaTime * EFTHardSettings.Instance.DIRECTION_LERP_SPEED));
                        if ((direction - input).sqrMagnitude < 0.0001f)
                        {
                            direction = input;
                        }
                    }
                    context.MovementDirection = direction;
                    if (Mathf.Abs(input.x - direction.x) < 0.1f && Mathf.Abs(input.y - direction.y) < 0.1f)
                    {
                        context.SetUpDiscreteDirection(MovementDirectionExtension.ConvertToMovementDirection(input));
                    }
                    __instance.LastNonZeroDirectionInput = input;
                    __instance._speed = input.normalized * targetSpeed;
                    __instance._timeWithoutInput = 0f;
                }
                else
                {
                    __instance._speed = Vector2.zero;
                    __instance._timeWithoutInput += deltaTime;
                }
                __instance.LastDirectionInput = input;
                __instance.InertiaDirection = __instance.ConvertDirectionFromLocalToGlobal(context.MovementDirection);
                __instance.prevDirection = Vector2.zero;
                __instance._previousSpeed = targetSpeed;

                if (!context.IsSprintEnabled)
                {
                    float current = context.SmoothedCharacterMovementSpeed;
                    float difference = Mathf.Abs(current - targetSpeed);
                    if (difference > float.Epsilon)
                    {
                        context.SmoothedCharacterMovementSpeed = difference > 0.001f
                            ? Mathf.Lerp(current, targetSpeed, deltaTime * EFTHardSettings.Instance.CHARACTER_SPEED_CHANGING_SPEED)
                            : targetSpeed;
                    }
                }

                if (__instance._moveCalled)
                {
                    __instance._timeWithOutExternalMoveCall = 0f;
                }
                else
                {
                    __instance._timeWithOutExternalMoveCall += deltaTime;
                }
                __instance._moveCalled = false;
                if (__instance._timeWithOutExternalMoveCall > MovePlayerState.MAX_WITHOUT_MOVE_CALL)
                {
                    __instance.Direction = Vector2.zero;
                }
                return false;
            }
        }

        private class PosePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.SmoothPoseLevel));
            }

            [PatchPrefix]
            private static bool Prefix(MovementContext __instance, float deltaTime)
            {
                if (!_alphaMovement.Value || !IsLocal(__instance))
                {
                    return true;
                }
                float target = __instance._poseLevel;
                float difference = Mathf.Abs(__instance.SmoothedPoseLevel - target);
                if (difference < float.Epsilon)
                {
                    return false;
                }
                __instance.SmoothedPoseLevel = difference > 0.001f
                    ? Mathf.Lerp(__instance.SmoothedPoseLevel, target, deltaTime * EFTHardSettings.Instance.POSE_CHANGING_SPEED)
                    : target;
                return false;
            }
        }

        private class LeanPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.InertiaSmoothTilt));
            }

            [PatchPrefix]
            private static bool Prefix(MovementContext __instance, float smoothDiff, float deltaTime)
            {
                if (!_alphaMovement.Value || !IsLocal(__instance))
                {
                    return true;
                }
                __instance.LegacySmoothTilt(smoothDiff, deltaTime);
                return false;
            }
        }
    }
}
