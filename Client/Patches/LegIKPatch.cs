using BepInEx.Configuration;
using EFT;
using EFT.Animations;
using HarmonyLib;
using RootMotion.FinalIK;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class LegIK
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _cameraFollows;
        private static ConfigEntry<bool> _others;
        private static ConfigEntry<float> _othersRange;

        private static readonly AccessTools.FieldRef<Player, FullBodyBipedIK> PlayerFbbik =
            AccessTools.FieldRefAccess<Player, FullBodyBipedIK>("_fbbik");
        private static readonly AccessTools.FieldRef<Player, float> TurnOffFbbikAt =
            AccessTools.FieldRefAccess<Player, float>("_turnOffFbbikAt");
        private static readonly AccessTools.FieldRef<Grounder, bool> GrounderInitiated =
            AccessTools.FieldRefAccess<Grounder, bool>("initiated");
        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, Vector3> CameraTarget =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Vector3>("_vCameraTarget");

        private class State
        {
            public bool Active;
            public bool Logged;
            public bool CameraOnBody;
            public float OriginalWeight = -1f;
        }

        private static readonly ConditionalWeakTable<Player, State> States = new ConditionalWeakTable<Player, State>();
        private static Player _local;
        private static State _localState;

        public static void Enable(ConfigFile config)
        {
            const string section = "Movement - Leg IK";
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Movement", "Leg IK", true, "Restores the alpha's FinalIK grounding: feet plant on stairs, slopes and uneven ground, and the body lowers onto the lower foot.", section, "Enabled");
            _cameraFollows = config.Bind(section, "Camera Follows Body", true, "Your view rises and dips with your body as the legs adapt to the ground, like the alpha.");
            _others = config.Bind(section, "Other Players", true, "Bots and other players (including Fika players) also get leg grounding.");
            _othersRange = config.Bind(section, "Other Players Range", 30f, ConfigHelper.Advanced("Other players further away than this (metres) skip leg grounding.", new AcceptableValueRange<float>(5f, 100f)));

            new FbbikUpdatePatch().Enable();
            new CameraTargetPatch().Enable();
            if (ObservedFbbikPatch.Target() != null)
            {
                new ObservedFbbikPatch().Enable();
            }
        }

        private static void Deactivate(Player player, State state, FullBodyBipedIK ik)
        {
            if (!state.Active)
            {
                return;
            }
            state.Active = false;
            if (player.Grounder != null)
            {
                player.Grounder.weight = 0f;
            }
            if (ik != null && state.OriginalWeight >= 0f)
            {
                ik.solver.IKPositionWeight = state.OriginalWeight;
            }
        }

        private static bool Activate(Player player, State state, FullBodyBipedIK ik, GrounderFBBIK grounder, bool firstPerson)
        {
            if (grounder.ik == null)
            {
                grounder.ik = ik;
            }
            if (!GrounderInitiated(grounder))
            {
                if (!ik.solver.initiated)
                {
                    return false;
                }
                grounder.solver.quality = firstPerson ? Grounding.Quality.Best : Grounding.Quality.Simple;
                grounder.Initiate();
                grounder.ResetPosition();
            }
            if (!state.Active)
            {
                state.Active = true;
                state.OriginalWeight = ik.solver.IKPositionWeight;
                grounder.ResetPosition();
            }

            MovementContext context = player.MovementContext;
            if (context != null)
            {
                float pose = context.SmoothedPoseLevel;
                player.GrounderBlender.Target = context.CurrentState != null && context.CurrentState.Name != EPlayerState.Idle ? 1f : pose;
                grounder.solver.liftPelvisWeight = 1f - pose;
                grounder.solver.maxStep = Mathf.Lerp(0.25f, 0.4f, pose);
            }
            grounder.solver.lowerPelvisWeight = player.GrounderBlender.Value;
            grounder.weight = 1f;
            ik.solver.IKPositionWeight = 1f;
            return true;
        }

        private static bool IsProne(Player player)
        {
            MovementContext context = player.MovementContext;
            if (context == null)
            {
                return false;
            }
            if (context.IsInPronePose)
            {
                return true;
            }
            switch (context.CurrentState?.Name)
            {
                case EPlayerState.ProneIdle:
                case EPlayerState.ProneMove:
                case EPlayerState.Prone2Stand:
                case EPlayerState.Transit2Prone:
                    return true;
                default:
                    return false;
            }
        }

        private class FbbikUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.FBBIKUpdate));
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance, float distance)
            {
                return Run(__instance, distance);
            }
        }

        private class ObservedFbbikPatch : ModulePatch
        {
            public static MethodBase Target()
            {
                return AccessTools.Method("Fika.Core.Main.Players.ObservedPlayer:ObservedFBBIKUpdate");
            }

            protected override MethodBase GetTargetMethod()
            {
                return Target();
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance, float distance)
            {
                return Run(__instance, distance);
            }
        }

        private static bool Run(Player __instance, float distance)
            {
                FullBodyBipedIK ik = PlayerFbbik(__instance);
                GrounderFBBIK grounder = __instance.Grounder;
                if (ik == null || grounder == null)
                {
                    return true;
                }
                State state = States.GetValue(__instance, p => new State());
                bool firstPerson = __instance.PointOfView == EPointOfView.FirstPerson;
                bool local = __instance.IsYourPlayer;
                bool wanted = _enabled.Value
                    && !IsProne(__instance)
                    && (__instance.EnabledAnimators & Player.EAnimatorMask.FBBIK) != 0
                    && (local ? firstPerson || _others.Value : _others.Value && distance <= _othersRange.Value);

                if (local)
                {
                    _local = __instance;
                    _localState = state;
                    if (!state.Logged)
                    {
                        state.Logged = true;
                        Transform camera = __instance.ProceduralWeaponAnimation?.HandsContainer?.CameraTransform;
                        Transform pelvis = __instance.PlayerBones?.Pelvis?.Original;
                        state.CameraOnBody = camera != null && pelvis != null && camera.IsChildOf(pelvis);
                        Plugin.LogSource.LogInfo($"Leg IK: camera {(state.CameraOnBody ? "rides on the body skeleton" : "is separate from the body, offsetting it manually")}.");
                    }
                }

                if (!wanted)
                {
                    Deactivate(__instance, state, ik);
                    return true;
                }
                if (!Activate(__instance, state, ik, grounder, firstPerson))
                {
                    return true;
                }

                if (firstPerson)
                {
                    ik.solver.iterations = 1;
                }
                else
                {
                    ik.solver.iterations = Mathf.Clamp((int)Mathf.Clamp(15f / Mathf.Max(distance, 0.01f), 0f, 2f), 1, 2);
                    if (!ik.solver.Quick && Time.time > TurnOffFbbikAt(__instance))
                    {
                        ik.solver.Quick = true;
                    }
                }
                ik.solver.Update();
                return false;
            }

        private class CameraTargetPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.CalculateCameraPosition));
            }

            [PatchPostfix]
            private static void Postfix(ProceduralWeaponAnimation __instance)
            {
                Player player = _local;
                State state = _localState;
                if (player == null || state == null || !state.Active || state.CameraOnBody || !_cameraFollows.Value || player.ProceduralWeaponAnimation != __instance || __instance.IsAiming)
                {
                    return;
                }
                if (player.PointOfView != EPointOfView.FirstPerson)
                {
                    return;
                }
                GrounderFBBIK grounder = player.Grounder;
                Transform parent = __instance.HandsContainer.CameraTransform != null ? __instance.HandsContainer.CameraTransform.parent : null;
                if (grounder == null || parent == null || grounder.solver.pelvis == null)
                {
                    return;
                }
                Vector3 offset = grounder.solver.pelvis.IKOffset * grounder.weight;
                CameraTarget(__instance) += parent.InverseTransformVector(offset);
            }
        }
    }
}
