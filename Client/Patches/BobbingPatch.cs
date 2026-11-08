using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class Bobbing
    {
        private const float AlphaRunStepMin = 0.9f;
        private const float StepMax = 1.2f;
        private const float StopGrace = 0.25f;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _alphaHeadBob;
        private static ConfigEntry<float> _headBobStrength;
        private static bool _inWalk;

        private class State
        {
            public Vector3 LastPosition;
            public bool Walking;
            public float StillTime;
            public bool Initialized;
            public AnimValProcessor[] Normal;
        }

        private static readonly ConditionalWeakTable<WalkEffector, State> States = new ConditionalWeakTable<WalkEffector, State>();

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Movement", "Alpha Bobbing", true, "Weapon and camera bob like the alpha: the bob only advances while you are actually moving and restarts from the first step when you stop, steps follow the alpha's run timing, the camera roll rides on the main step curve again, and there is no extra overweight bob.", "Combat - Weapon Handling", "Alpha Bobbing");
            const string section = "Combat - Weapon Handling";
            _alphaHeadBob = config.Bind(section, "Alpha Head Bob", true, "With Alpha Bobbing on, the camera bobs as hard as the alpha's while walking and running, ignoring the game's Head bobbing setting (which defaults to its lowest value). The alpha had no such setting, so its head bob always ran at full strength, and grew stronger the faster you moved.");
            _headBobStrength = config.Bind(section, "Head Bob Strength", 1f, new ConfigDescription("How strong the alpha head bob is. 1 is the alpha. The game's own Head bobbing setting goes from 0.2 to 1.", new AcceptableValueRange<float>(0.2f, 2f)));
            ConfigHelper.Requires(_enabled, _alphaHeadBob);
            ConfigHelper.Requires(_alphaHeadBob, _headBobStrength);
            new WalkPatch().Enable();
            new HeadBobPatch(false).Enable();
            new HeadBobPatch(true).Enable();
        }

        private static AnimValProcessor[] BuildAlphaNormal(WalkEffector walk, PlayerSpring springs)
        {
            AnimValProcessor[] modern = walk.PresetProcessors[(int)WalkEffector.EWalkPreset.normal];
            if (modern == null || modern.Length != 5 || springs == null)
            {
                return modern;
            }
            AnimValProcessor roll = modern[4];
            if (roll.Values?.Vals == null || roll.Values.Vals.Length != 1 || roll.Values.Vals[0].To != EFT.Animations.Target.CameraRotation || roll.Values.Vals[0].Component != EFT.Animations.ComponentType.Z)
            {
                return modern;
            }
            AnimValProcessor step = modern[0];
            EFT.Animations.ValProcessor rollOnStep = new EFT.Animations.ValProcessor(new EFT.Animations.Val { To = EFT.Animations.Target.CameraRotation, Component = EFT.Animations.ComponentType.Z, Intensity = roll.Values.Vals[0].Intensity });
            rollOnStep.Initialize(springs.CameraRotation, springs.HandsPosition, springs.HandsRotation, true);
            List<EFT.Animations.ValProcessor> processors = new List<EFT.Animations.ValProcessor>(step.Processors) { rollOnStep };

            AnimValProcessor alphaStep = new AnimValProcessor(step.Values)
            {
                Processors = processors.ToArray(),
                _time = step._time,
                _intensity = step._intensity,
                _frequency = step._frequency,
            };
            return new[] { alphaStep, modern[1], modern[2], modern[3] };
        }

        private class HeadBobPatch : ModulePatch
        {
            private readonly bool _raw;

            public HeadBobPatch(bool raw)
            {
                _raw = raw;
            }

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EFT.Animations.ValProcessor), _raw ? nameof(EFT.Animations.ValProcessor.ProcessRaw) : nameof(EFT.Animations.ValProcessor.Process));
            }

            [PatchPrefix]
            private static bool Prefix(EFT.Animations.ValProcessor __instance, float value, MethodBase __originalMethod)
            {
                if (!_inWalk || !__instance._isHeadBobbing || __instance._target == null || __instance._val == null)
                {
                    return true;
                }
                float amount = value * __instance._val.Intensity * __instance.Intensity * _headBobStrength.Value;
                if (__originalMethod.Name == nameof(EFT.Animations.ValProcessor.ProcessRaw))
                {
                    amount /= __instance._target.InputIntensity;
                }
                __instance._target.AddAcceleration(__instance._component, amount);
                return false;
            }
        }

        private class WalkPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(WalkEffector), nameof(WalkEffector.Process));
            }

            [PatchPrefix]
            private static bool Prefix(WalkEffector __instance, float deltaTime)
            {
                if (!_enabled.Value || __instance.PresetProcessors == null || __instance.Transform == null)
                {
                    return true;
                }
                Player player = Singleton<GameWorld>.Instance?.MainPlayer;
                ProceduralWeaponAnimation animation = player != null ? player.ProceduralWeaponAnimation : null;
                if (animation == null || animation.Walk != __instance)
                {
                    return true;
                }

                State state = States.GetValue(__instance, w => new State());
                if (!state.Initialized)
                {
                    state.Initialized = true;
                    state.LastPosition = __instance.Transform.position;
                    state.Normal = BuildAlphaNormal(__instance, animation.HandsContainer);
                }

                float speed = player.MovementContext != null ? player.MovementContext.ClampedSpeed : 1f;
                float frequency = __instance.StepFrequency;
                if (frequency > 0f)
                {
                    float interval = 0.5f / frequency;
                    __instance.StepFrequency = 0.5f / Mathf.Clamp(interval, AlphaRunStepMin - speed / 2f, StepMax);
                }

                Vector3 worldVelocity;
                if (player.MovementContext != null && player.MovementContext.CharacterController != null)
                {
                    worldVelocity = player.MovementContext.Velocity;
                }
                else
                {
                    float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                    worldVelocity = (__instance.Transform.position - state.LastPosition) / dt;
                }
                state.LastPosition = __instance.Transform.position;
                Vector3 moved = __instance.Transform.InverseTransformDirection(worldVelocity);
                moved.z = moved.z > 0f ? moved.z : moved.z * __instance.BackSpeedMultyplyer;
                float magnitude = new Vector2(moved.x * __instance.SideSpeedMultyplyer, moved.z).magnitude;

                int preset = (int)__instance.CurrentWalkPreset;
                AnimValProcessor[] processors = preset == (int)WalkEffector.EWalkPreset.normal && state.Normal != null ? state.Normal : __instance.PresetProcessors[preset];
                bool moving = magnitude > __instance.Treshold;
                if (moving)
                {
                    state.StillTime = 0f;
                }
                else
                {
                    state.StillTime += deltaTime;
                }
                if (moving)
                {
                    state.Walking = true;
                    _inWalk = _alphaHeadBob.Value;
                    try
                    {
                        for (int i = 0; i < processors.Length; i++)
                        {
                            processors[i].ProcessRaw(deltaTime * __instance.StepFrequency, __instance.Intensity);
                        }
                    }
                    finally
                    {
                        _inWalk = false;
                    }
                }
                else if (state.Walking && state.StillTime > StopGrace)
                {
                    state.Walking = false;
                    for (int i = 0; i < processors.Length; i++)
                    {
                        processors[i].SetupParentValues(__instance.Intensity, __instance.StepFrequency);
                        processors[i].ResetTime();
                    }
                }
                return false;
            }
        }
    }
}
