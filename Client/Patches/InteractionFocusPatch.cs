using BepInEx.Configuration;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class InteractionFocus
    {
        private const float FocusDuration = 0.7f;
        private const float ReturnMaxDuration = 1.0f;
        private const float FocusTurnSpeed = 8f;
        private const float ReturnTurnSpeed = 6f;
        private const float ReturnSettleAngle = 0.5f;
        private const float MoveSpeedLimit = 0.15f;
        private const float MaxDistance = 3f;
        private const float LookInputCancelThreshold = 0.01f;
        private const Player.ESpeedLimit SpeedLimitCause = (Player.ESpeedLimit)100;

        private enum Phase
        {
            Focus,
            Return
        }

        private static Player _player;
        private static Vector3 _target;
        private static Vector2 _originalRotation;
        private static Phase _phase;
        private static float _phaseEndTime;
        private static bool _returnAfterFocus;
        private static ConfigEntry<bool> _enabled;

        public static ConfigEntry<bool> Toggle => _enabled;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Interaction", "Interaction Focus", true, "Opening a door or container turns your view toward it and slows you for a moment, then your view returns to where you were looking. Moving the mouse cancels the return.", "Interaction Focus", "Enabled");
            new ExecuteInteractionPatch().Enable();
            new TranslateAxesPatch().Enable();
        }

        private static bool IsLocked(Player player)
        {
            return _player != null && player != null && player == _player;
        }

        private static void Begin(Player player, WorldInteractiveObject interactive)
        {
            if (_player != null && _player != player)
            {
                End();
            }

            if (_player == null)
            {
                player.MovementContext.AddStateSpeedLimit(MoveSpeedLimit, SpeedLimitCause);
                _originalRotation = new Vector2(player.MovementContext.Yaw, player.MovementContext.Pitch);
            }

            _player = player;
            _target = GetFocusPoint(interactive);
            _returnAfterFocus = interactive is not LootableContainer;
            _phase = Phase.Focus;
            _phaseEndTime = Time.time + FocusDuration;
        }

        private static void End()
        {
            if (_player != null)
            {
                _player.MovementContext.RemoveStateSpeedLimit(SpeedLimitCause);
            }
            _player = null;
        }

        private static void Tick(Player player)
        {
            if (!IsLocked(player))
            {
                return;
            }

            if (!player.HealthController.IsAlive || Vector3.Distance(player.Position, _target) > MaxDistance)
            {
                End();
                return;
            }

            if (player.IsSprintEnabled)
            {
                player.EnableSprint(false);
            }

            if (_phase == Phase.Focus)
            {
                if (Time.time < _phaseEndTime)
                {
                    TurnTowards(player, GetFocusRotation(player), FocusTurnSpeed);
                    return;
                }

                if (!_returnAfterFocus)
                {
                    End();
                    return;
                }

                _phase = Phase.Return;
                _phaseEndTime = Time.time + ReturnMaxDuration;
            }

            if (Time.time >= _phaseEndTime || TurnTowards(player, _originalRotation, ReturnTurnSpeed) < ReturnSettleAngle)
            {
                End();
            }
        }

        private static Vector2 GetFocusRotation(Player player)
        {
            Vector3 eye = player.CameraPosition != null
                ? player.CameraPosition.position
                : player.Position + Vector3.up * 1.6f;
            Vector3 direction = _target - eye;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return new Vector2(player.MovementContext.Yaw, player.MovementContext.Pitch);
            }

            Vector3 euler = Quaternion.LookRotation(direction).eulerAngles;
            return new Vector2(euler.y, euler.x);
        }

        private static float TurnTowards(Player player, Vector2 targetRotation, float speed)
        {
            float yawDelta = Mathf.DeltaAngle(player.MovementContext.Yaw, targetRotation.x);
            float pitchDelta = Mathf.DeltaAngle(player.MovementContext.Pitch, targetRotation.y);
            float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
            player.Rotate(new Vector2(yawDelta * t, pitchDelta * t));
            return Mathf.Max(Mathf.Abs(yawDelta), Mathf.Abs(pitchDelta));
        }

        private static Vector3 GetFocusPoint(WorldInteractiveObject interactive)
        {
            if (interactive._handle != null)
            {
                return interactive.Handle.position;
            }
            if (interactive.Collider != null)
            {
                return interactive.Collider.bounds.center;
            }
            return interactive.transform.position;
        }

        private class ExecuteInteractionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.ExecuteInteraction));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance, WorldInteractiveObject door)
            {
                if (_enabled.Value && !FullInteractions.Active && door != null && __instance.IsYourPlayer)
                {
                    Begin(__instance, door);
                }
            }
        }

        private class TranslateAxesPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MoveInputTranslator), nameof(MoveInputTranslator.TranslateAxes));
            }

            [PatchPrefix]
            private static void Prefix(MoveInputTranslator __instance, ref float[] axes)
            {
                if (!IsLocked(__instance._controller) || axes == null || axes.Length < 6)
                {
                    return;
                }

                if (_phase == Phase.Return)
                {
                    if (Mathf.Abs(axes[2]) > LookInputCancelThreshold || Mathf.Abs(axes[3]) > LookInputCancelThreshold)
                    {
                        End();
                    }
                    return;
                }

                axes[2] = 0f;
                axes[3] = 0f;
                axes[4] = 0f;
                axes[5] = 0f;
            }

            [PatchPostfix]
            private static void Postfix(MoveInputTranslator __instance)
            {
                Tick(__instance._controller);
            }
        }
    }
}
