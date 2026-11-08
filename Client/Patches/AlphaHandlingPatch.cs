using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class AlphaHandling
    {
        private const int AverageSamples = 8;
        private const float RotationInputClamp = 300f;
        private const float MaxOverlapAt = 0.45f;
        private const float WallSmoothTime = 6f;

        private struct MouseVal
        {
            public readonly int From;
            public readonly int Component;
            public readonly float Intensity;

            public MouseVal(int from, int component, float intensity)
            {
                From = from;
                Component = component;
                Intensity = intensity;
            }
        }

        private static readonly MouseVal[] AlphaMouse =
        {
            new MouseVal(0, 2, 0.15f),
            new MouseVal(2, 0, -0.1f),
            new MouseVal(1, 2, -1.3f),
            new MouseVal(3, 0, 0.1f),
            new MouseVal(0, 1, 0.2f),
        };

        private class PoseVal
        {
            public int Component;
            public float Intensity;
            public float AltIntensity;
            public AnimationCurve Curve;
        }

        private static PoseVal Val(int component, float intensity, float alt, float t0, float v0, float in0, float out0, float t1, float v1, float in1, float out1)
        {
            return new PoseVal
            {
                Component = component,
                Intensity = intensity,
                AltIntensity = alt,
                Curve = new AnimationCurve(new Keyframe(t0, v0, in0, out0), new Keyframe(t1, v1, in1, out1)),
            };
        }

        private static readonly PoseVal[] RiflePos =
        {
            Val(0, 0.03f, 0.08f, 0.25f, 0f, 1.333f, 1.333f, 1f, 1f, -0.077f, 1f),
            Val(1, 0.05f, 0.05f, 0f, 0f, 3.333f, 3.333f, 0.3f, 1f, 3.069f, 1f),
            Val(2, -0.25f, 0.05f, 0.25f, 0f, 1.333f, 1.333f, 1f, 1f, -0.077f, 1f),
        };

        private static readonly PoseVal[] RifleRot =
        {
            Val(0, -57.5f, 15f, 0.25f, 0f, 1.333f, 1.502f, 1f, 1f, -0.077f, 1f),
            Val(1, -0.7f, -25.33f, 0.25f, 0f, 1.333f, 1.333f, 1f, 1f, -0.077f, 1f),
            Val(2, -31.56f, -52.5f, 0.25f, 0f, 1.333f, 1.333f, 1f, 1f, -0.077f, 1f),
        };

        private static readonly PoseVal[] PistolPos =
        {
            Val(0, 0.033f, -0.07f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 0.831f, 1.667f),
            Val(1, 0.05f, 0.1f, 0f, 0f, 2.222f, 2.222f, 0.45f, 1f, 2.222f, 2.222f),
            Val(2, -0.3f, 0.1f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 0.697f, 1.667f),
        };

        private static readonly PoseVal[] PistolRot =
        {
            Val(0, -70f, 35f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 0.939f, 1.667f),
            Val(1, -6.8f, -37.98f, 0.405f, 0f, 1.681f, 1.681f, 1f, 1f, 0.933f, 1.681f),
            Val(2, -8.26f, -25f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 0.72f, 1.667f),
        };

        private static readonly PoseVal[] RifleTpPos =
        {
            Val(0, 0.03f, 0.12f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
            Val(1, 0.1f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
            Val(2, -0.15f, -0.15f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
        };

        private static readonly PoseVal[] RifleTpRot =
        {
            Val(0, -60f, 57.27f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
            Val(1, 7f, 38.01f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
            Val(2, 0f, -18.8f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f),
        };

        private static readonly PoseVal[] PistolTpPos =
        {
            Val(0, 0f, -0.1f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 1.667f, 1.667f),
            Val(1, 0.15f, 0.1f, 0f, 0f, 2.222f, 2.222f, 0.45f, 1f, 2.222f, 2.222f),
            Val(2, 0f, -0.2f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 1.667f, 1.667f),
        };

        private static readonly PoseVal[] PistolTpRot =
        {
            Val(0, -80f, 63.25f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 1.667f, 1.667f),
            Val(1, -27.2f, 23.1f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 1.667f, 1.667f),
            Val(2, 0f, 0f, 0.4f, 0f, 1.667f, 1.667f, 1f, 1f, 1.667f, 1.667f),
        };

        private class WallState
        {
            public float WeightUpDown;
            public float WeightTarget = 1f;
        }

        private static readonly ConditionalWeakTable<TurnAwayEffector, WallState> WallStates = new ConditionalWeakTable<TurnAwayEffector, WallState>();
        private static ProceduralWeaponAnimation _processing;

        private static ConfigEntry<bool> _alphaSway;
        private static ConfigEntry<bool> _wallPushOthers;
        private static ConfigEntry<bool> _alphaWallPush;
        private static ConfigEntry<float> _swayMultiplier;

        private class SwayState
        {
            public Quaternion LastLook;
            public bool HasLook;
            public float LastTime;
            public Vector2 LastRotationVelocity;
            public readonly Vector2[] VelocitySamples = new Vector2[AverageSamples];
            public readonly Vector2[] AccelerationSamples = new Vector2[AverageSamples];
            public int SampleIndex;
            public int SampleCount;
        }

        private static readonly ConditionalWeakTable<MotionEffector, SwayState> SwayStates = new ConditionalWeakTable<MotionEffector, SwayState>();

        public static bool AlphaSwayEnabled => _alphaSway.Value;

        public static void Enable(ConfigFile config, ConfigEntry<float> swayMultiplier)
        {
            const string section = "Combat - Weapon Handling";
            _swayMultiplier = swayMultiplier;
            _alphaSway = ConfigHelper.Feature(config, FeatureCategory.Combat, "Weapon Handling", "Alpha Sway Springs", true, "Mouse sway uses the alpha's springs and values: turning swings the weapon on the hand rotation spring instead of the newer critically damped sway spring, and weapon weight no longer adds sway, lag or aim drift.", section, "Alpha Sway Springs");
            _alphaWallPush = ConfigHelper.Feature(config, FeatureCategory.Combat, "Weapon Handling", "Alpha Wall Push", true, "When a weapon runs into a wall it tips up (or down when looking down) and pulls in like the alpha, instead of sliding straight back.", section, "Alpha Wall Push");

            _wallPushOthers = config.Bind(section, "Alpha Wall Push For Others", true, "Bots and other players (including Fika players) also tip their weapons up against walls like the alpha.");
            ConfigHelper.Requires(_alphaWallPush, _wallPushOthers);

            new MotionPatch().Enable();
            new EffectorsPatch().Enable();
            new TurnAwayPatch().Enable();
        }

        private static ProceduralWeaponAnimation AnimationOf(MotionEffector effector)
        {
            if (_processing != null && _processing.MotionReact == effector)
            {
                return _processing;
            }
            Player local = Singleton<GameWorld>.Instance?.MainPlayer;
            ProceduralWeaponAnimation animation = local != null ? local.ProceduralWeaponAnimation : null;
            return animation != null && animation.MotionReact == effector ? animation : null;
        }

        private static Vector2 Average(Vector2[] samples, int count)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                sum += samples[i];
            }
            return count > 0 ? sum / count : Vector2.zero;
        }

        private class MotionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MotionEffector), nameof(MotionEffector.Process));
            }

            [PatchPrefix]
            private static bool Prefix(MotionEffector __instance, float deltaTime)
            {
                if (!_alphaSway.Value || deltaTime <= 0f || __instance.Transform == null)
                {
                    return true;
                }
                ProceduralWeaponAnimation animation = AnimationOf(__instance);
                if (animation == null || animation.HandsContainer == null || !PlayerKind.IsHuman(animation))
                {
                    return true;
                }
                Player owner = PlayerKind.Owner(animation);
                if (owner == null)
                {
                    return true;
                }
                SwayState state = SwayStates.GetValue(__instance, e => new SwayState());
                Vector2 rotation = owner.Rotation;
                Quaternion look = Quaternion.Euler(rotation.y, rotation.x, 0f);
                if (!state.HasLook || Time.time - state.LastTime > 0.1f)
                {
                    state.LastLook = look;
                    state.HasLook = true;
                }
                Vector3 last = Quaternion.Inverse(look) * (state.LastLook * Vector3.forward);
                Vector2 gyro = new Vector2(-Mathf.Atan2(last.x, last.z) * Mathf.Rad2Deg, Mathf.Atan2(last.z, last.y) * Mathf.Rad2Deg - 90f);
                state.LastLook = look;
                state.LastTime = Time.time;
                Vector2 velocity = gyro / deltaTime;
                velocity.x = Mathf.Clamp(velocity.x, -RotationInputClamp, RotationInputClamp);
                velocity.y = Mathf.Clamp(velocity.y, -RotationInputClamp, RotationInputClamp);
                Vector2 acceleration = velocity - state.LastRotationVelocity;
                state.LastRotationVelocity = velocity;
                state.VelocitySamples[state.SampleIndex] = velocity;
                state.AccelerationSamples[state.SampleIndex] = acceleration;
                state.SampleIndex = (state.SampleIndex + 1) % AverageSamples;
                state.SampleCount = Mathf.Min(state.SampleCount + 1, AverageSamples);
                Vector2 averageVelocity = Average(state.VelocitySamples, state.SampleCount);
                Vector2 averageAcceleration = Average(state.AccelerationSamples, state.SampleCount);

                float bipod = (!__instance._isMounted || !__instance._isBipodUsed) ? 1f : __instance.BipodModifier;
                float step = deltaTime * __instance.Intensity * bipod;
                ValProcessor[] movement = __instance._movementProcessors;
                if (movement != null)
                {
                    Vector2 trackedVelocity = __instance.RotationVelocity;
                    Vector2 trackedAcceleration = __instance.RotationAcceleration;
                    __instance.RotationVelocity = averageVelocity;
                    __instance.RotationAcceleration = averageAcceleration;
                    try
                    {
                        for (int i = 0; i < movement.Length; i++)
                        {
                            movement[i].Process(__instance, step);
                        }
                    }
                    finally
                    {
                        __instance.RotationVelocity = trackedVelocity;
                        __instance.RotationAcceleration = trackedAcceleration;
                    }
                }

                Spring hands = animation.HandsContainer.HandsRotation;
                float mouseStep = step * _swayMultiplier.Value;
                for (int i = 0; i < AlphaMouse.Length; i++)
                {
                    MouseVal val = AlphaMouse[i];
                    float input;
                    switch (val.From)
                    {
                        case 0: input = averageVelocity.x; break;
                        case 1: input = averageAcceleration.x; break;
                        case 2: input = averageVelocity.y; break;
                        default: input = averageAcceleration.y; break;
                    }
                    hands.AddAcceleration(val.Component, input * val.Intensity * mouseStep);
                }
                return false;
            }
        }

        private class EffectorsPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.ProcessEffectors));
            }

            [PatchPrefix]
            private static void Prefix(ProceduralWeaponAnimation __instance)
            {
                _processing = __instance;
            }

            [PatchPostfix]
            private static void Postfix()
            {
                _processing = null;
            }
        }

        private class TurnAwayPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TurnAwayEffector), nameof(TurnAwayEffector.Process));
            }

            [PatchPrefix]
            private static bool Prefix(TurnAwayEffector __instance, float deltaTime)
            {
                if (!_alphaWallPush.Value)
                {
                    return true;
                }
                ProceduralWeaponAnimation animation = _processing;
                if (animation == null || animation.TurnAway != __instance)
                {
                    return true;
                }
                Player player = PlayerKind.Owner(animation);
                if (player == null || (!player.IsYourPlayer && !_wallPushOthers.Value))
                {
                    return true;
                }
                Player.FirearmController firearm = player.HandsController as Player.FirearmController;
                Transform weaponRoot = player.PlayerBones?.WeaponRoot?.Original;
                if (firearm == null || weaponRoot == null || firearm.WeaponLn <= 0f)
                {
                    return true;
                }

                WallState state = WallStates.GetValue(__instance, e => new WallState());
                float overlap = Mathf.Clamp01(__instance.OverlapDepth / firearm.WeaponLn);
                Vector3 direction = -weaponRoot.up;
                if (overlap > 0.015f)
                {
                    float crossY = 0f;
                    Transform parent = animation.HandsContainer.WeaponRootAnim != null ? animation.HandsContainer.WeaponRootAnim.parent : null;
                    if (parent != null)
                    {
                        Vector3 origin = parent.TransformPoint(animation.HandsContainer.HandsPosition.Zero);
                        if (Physics.Raycast(origin, direction, out RaycastHit hit, firearm.WeaponLn, EFTHardSettings.Instance.WEAPON_OCCLUSION_LAYERS))
                        {
                            crossY = Vector3.Cross(direction, hit.normal).y;
                        }
                    }
                    float bias = crossY > 0f ? 0.2f : 0.4f;
                    float side = (direction.y + 1f + bias) / 2f;
                    if (Mathf.Abs(state.WeightUpDown - side) > 0.55f)
                    {
                        state.WeightTarget = (Mathf.Sign(direction.y + bias) + 1f) / 2f;
                    }
                    state.WeightUpDown = Mathf.Lerp(state.WeightUpDown, state.WeightTarget, 10f * deltaTime);
                }

                float target = Mathf.Clamp01(overlap / MaxOverlapAt);
                __instance.OverlapValue = Mathf.Lerp(__instance.OverlapValue, target, target > __instance.OverlapValue ? WallSmoothTime * deltaTime : WallSmoothTime / 2f * deltaTime);

                bool pistol = __instance._isPistol;
                bool firstPerson = player.PointOfView == EPointOfView.FirstPerson;
                PoseVal[] pos = firstPerson ? (pistol ? PistolPos : RiflePos) : (pistol ? PistolTpPos : RifleTpPos);
                PoseVal[] rot = firstPerson ? (pistol ? PistolRot : RifleRot) : (pistol ? PistolTpRot : RifleTpRot);
                __instance._position = Evaluate(pos, __instance.OverlapValue, state.WeightUpDown);
                __instance._rotation = Evaluate(rot, __instance.OverlapValue, state.WeightUpDown);
                __instance._lElbowShift = Vector3.zero;
                __instance._rElbowShift = Vector3.zero;
                __instance._cameraShift = Vector3.zero;
                return false;
            }

            private static Vector3 Evaluate(PoseVal[] pose, float value, float weight)
            {
                Vector3 result = Vector3.zero;
                for (int i = 0; i < pose.Length; i++)
                {
                    PoseVal val = pose[i];
                    result[val.Component] = val.Curve.Evaluate(value) * Mathf.Lerp(val.AltIntensity, val.Intensity, weight);
                }
                return result;
            }
        }
    }
}
