using BepInEx.Configuration;
using EFT;
using EFT.Animations;
using EFT.Animations.NewRecoil;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class WeaponHandling
    {
        private const float AlphaReferenceWeight = 3f;
        private static bool _warned;
        private const float AlphaForceScale = 7f / 60f;
        private static readonly Vector2 AlphaRotationRange = new Vector2(0.85f, 1.15f);
        private static readonly Vector2 AlphaPositionRange = new Vector2(0.65f, 1.05f);
        private static bool _logged;
        private static bool _springCaptured;
        private static float _springDamping;
        private static ConfigEntry<float> _oldRecoilStrength;
        private static ConfigEntry<float> _oldRecoilReturn;

        private const float AlphaUpRatio = 1f;
        private const float AlphaBackRatio = 0.85f;
        private const float AlphaCameraRatio = 5f;

        private struct AlphaRecoil
        {
            public readonly float Up;
            public readonly float Back;
            public readonly float Camera;

            public AlphaRecoil(float up, float back, float camera)
            {
                Up = up;
                Back = back;
                Camera = camera;
            }
        }

        private static readonly Dictionary<string, AlphaRecoil> AlphaWeapons = new Dictionary<string, AlphaRecoil>
        {
            { "5644bd2b4bdc2d3b4c8b4572", new AlphaRecoil(100f, 300f, 0.2f) },
            { "56e0598dd2720bb5668b45a6", new AlphaRecoil(350f, 200f, 0.05f) },
            { "5447a9cd4bdc2dbd208b4567", new AlphaRecoil(85f, 290f, 0.2f) },
            { "5448bd6b4bdc2dfc2f8b4569", new AlphaRecoil(500f, 250f, 0.05f) },
            { "54491c4f4bdc2db1078b4568", new AlphaRecoil(400f, 500f, 0.3f) },
            { "571a12c42459771f627b58a0", new AlphaRecoil(500f, 250f, 0.03f) },
            { "55801eed4bdc2d89578b4588", new AlphaRecoil(100f, 500f, 0.4f) },
            { "5580223e4bdc2d1c128b457f", new AlphaRecoil(60f, 660f, 0.5f) },
            { "56d59856d2720bd8418b456a", new AlphaRecoil(400f, 250f, 0.05f) },
            { "56dee2bdd2720bc8328b4567", new AlphaRecoil(350f, 450f, 0.3f) },
            { "576165642459773c7a400233", new AlphaRecoil(300f, 500f, 0.4f) },
            { "576a581d2459771e7b1bc4f1", new AlphaRecoil(400f, 250f, 0.05f) },
            { "57838ad32459774a17445cd2", new AlphaRecoil(75f, 150f, 0.2f) },
            { "579204f224597773d619e051", new AlphaRecoil(500f, 250f, 0.05f) },
            { "57c44b372459772d2b39b8ce", new AlphaRecoil(75f, 150f, 0.2f) },
            { "57d14d2524597714373db789", new AlphaRecoil(55f, 105f, 0.2f) },
            { "57dc2fa62459775949412633", new AlphaRecoil(150f, 350f, 0.2f) },
            { "57f3c6bd24597738e730fa2f", new AlphaRecoil(55f, 105f, 0.2f) },
            { "57f4c844245977379d5c14d1", new AlphaRecoil(65f, 115f, 0.2f) },
        };

        private static ConfigEntry<bool> _alphaRecoil;
        private static ConfigEntry<float> _recoilMultiplier;
        private static ConfigEntry<float> _cameraRecoilMultiplier;
        private static ConfigEntry<float> _swayMultiplier;
        private static ConfigEntry<float> _swayWeightInfluence;
        private static ConfigEntry<float> _displacementMultiplier;
        private static ConfigEntry<float> _aimSwayMultiplier;

        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, float> SwayStrength =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_swayStrength");
        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, float> DisplacementStrength =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_displacementStr");
        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, float> AimSwayStrength =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, float>("_aimSwayStrength");

        public static void Enable(ConfigFile config)
        {
            const string section = "Combat - Weapon Handling";
            _alphaRecoil = ConfigHelper.Feature(config, FeatureCategory.Combat, "Weapon Handling", "Alpha Recoil Springs", true, "Switches your weapons back to the old spring recoil pipeline that the alpha used: each shot kicks the weapon and camera springs, which settle back on their own. No soft first shots, no laser-like stabilisation in full auto, and no camera dragging along the recoil path. Applies the next time you draw a weapon.", section, "Alpha Recoil Springs");
            _recoilMultiplier = config.Bind(section, "Recoil Multiplier", 1f, new ConfigDescription("Scales the force of every shot.", new AcceptableValueRange<float>(0.25f, 3f)));
            _oldRecoilStrength = config.Bind(section, "Alpha Recoil Scale", 1f, new ConfigDescription("With Alpha Recoil Springs on, weapons use their original alpha recoil values (weapons that were not in the alpha are converted from their current values), scaled by this. Applies the next time you draw a weapon.", new AcceptableValueRange<float>(0.25f, 2f)));
            _oldRecoilReturn = config.Bind(section, "Alpha Recoil Recovery", 0.25f, new ConfigDescription("How fast the weapon settles back after each shot, as a share of the weapon's own return speed. Lower is slower. Applies the next time you draw a weapon.", new AcceptableValueRange<float>(0.05f, 1f)));
            _cameraRecoilMultiplier = config.Bind(section, "Camera Recoil Multiplier", 1f, new ConfigDescription("Scales how much each shot shakes the camera. Applies the next time you draw a weapon.", new AcceptableValueRange<float>(0f, 4f)));
            _swayMultiplier = config.Bind(section, "Sway Multiplier", 1f, new ConfigDescription("Scales weapon sway when turning and moving.", new AcceptableValueRange<float>(0f, 3f)));
            _swayWeightInfluence = config.Bind(section, "Sway Weight Influence", 1f, ConfigHelper.Advanced("How much weapon weight adds to sway when Alpha Sway is off. 0 makes every weapon sway like a light one.", new AcceptableValueRange<float>(0f, 1f)));
            _displacementMultiplier = config.Bind(section, "Weapon Lag Multiplier", 1f, ConfigHelper.Advanced("Scales how far the weapon lags behind your movement when Alpha Sway is off.", new AcceptableValueRange<float>(0f, 2f)));
            _aimSwayMultiplier = config.Bind(section, "Aim Drift Multiplier", 1f, ConfigHelper.Advanced("Scales the drift of heavy, low-ergonomics weapons while aiming when Alpha Sway is off.", new AcceptableValueRange<float>(0f, 2f)));
            ConfigHelper.Requires(_alphaRecoil, _oldRecoilStrength, _oldRecoilReturn);

            AlphaHandling.Enable(config, _swayMultiplier);
            _swayMultiplier.SettingChanged += (s, e) => RefreshWeapon();
            _swayWeightInfluence.SettingChanged += (s, e) => RefreshWeapon();
            _displacementMultiplier.SettingChanged += (s, e) => RefreshWeapon();
            _aimSwayMultiplier.SettingChanged += (s, e) => RefreshWeapon();

            new PipelinePatch().Enable();
            new SpringRestorePatch().Enable();
            new CameraFollowPatch().Enable();
            new RecoilForcePatch().Enable();
            new RecoilWeaponPatch().Enable();
            new SwayFactorsPatch().Enable();
            new WeaponVariablesPatch().Enable();
        }

        private static void RefreshWeapon()
        {
            foreach (Player player in PlayerKind.Humans())
            {
                player.ProceduralWeaponAnimation?.UpdateWeaponVariables();
            }
        }

        private static bool IsLocal(ProceduralWeaponAnimation animation)
        {
            return PlayerKind.IsHuman(animation);
        }

        private static string CurveKeys(AnimationCurve curve)
        {
            if (curve == null)
            {
                return " (none)";
            }
            string keys = "";
            for (int i = 0; i < curve.length; i++)
            {
                keys += $" ({curve[i].time:0.###}, {curve[i].value:0.###})";
            }
            return keys;
        }

        private static bool IsLocal(Player.FirearmController controller)
        {
            return PlayerKind.IsHuman(controller);
        }

        private static bool OldPipelineUsable(ShotEffector effector)
        {
            OldRecoilShotEffect old = effector.OldShotRecoil;
            return old != null
                && old.ShotRecoilProcessValues != null && old.ShotRecoilProcessValues.Length >= 5
                && old.HandRotationRecoil != null && old.HandRotationRecoil.ReturnSpeedCurve != null && old.HandRotationRecoil.ReturnSpeedCurve.length > 0
                && old.HandPositionRecoil != null && old.CameraRotationRecoil != null;
        }

        private class PipelinePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ShotEffector), nameof(ShotEffector.SubscribeToWeapon));
            }

            [PatchPrefix]
            private static void Prefix(ShotEffector __instance, Player.FirearmController firearms)
            {
                if (!IsLocal(firearms))
                {
                    return;
                }
                bool isOld = __instance.CurrentRecoilEffect is OldRecoilShotEffect;
                if (_alphaRecoil.Value && !isOld)
                {
                    if (!OldPipelineUsable(__instance))
                    {
                        if (!_warned)
                        {
                            _warned = true;
                            Plugin.LogSource.LogWarning("Alpha Recoil: the old recoil pipeline has no data on this weapon rig, keeping modern recoil.");
                        }
                        return;
                    }
                    OldRecoilShotEffect old = __instance.OldShotRecoil;
                    if (!_springCaptured)
                    {
                        _springCaptured = true;
                        Plugin.LogSource.LogInfo("Alpha Recoil: old return curve" + CurveKeys(old.HandRotationRecoil.ReturnSpeedCurve) + ", new return curve" + CurveKeys(__instance.NewShotRecoil.HandRotationRecoil.ReturnSpeedCurve));
                        _springDamping = old.HandRotationRecoil.Damping;
                    }
                    if (old.WeaponRecoil == null)
                    {
                        old.WeaponRecoil = new WeaponRecoil(false);
                    }
                    old.UseOffset = __instance.NewShotRecoil.UseOffset;
                    __instance.SetOldRecoilMode();
                    if (!_logged)
                    {
                        _logged = true;
                        Plugin.LogSource.LogInfo($"Alpha Recoil on. Hand rotation spring: return {old.HandRotationRecoil.ReturnSpeed}, damping {old.HandRotationRecoil.Damping}, intensity {old.HandRotationRecoil.Intensity}. Hand position: return {old.HandPositionRecoil.ReturnSpeed}, damping {old.HandPositionRecoil.Damping}. Camera: return {old.CameraRotationRecoil.ReturnSpeed}, damping {old.CameraRotationRecoil.Damping}.");
                    }
                }
                else if (!_alphaRecoil.Value && isOld)
                {
                    __instance.SetNewRecoilMode();
                }
            }
        }

        private class CameraFollowPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.AddHandRecoilRotateToCamera));
            }

            [PatchPrefix]
            private static bool Prefix(ProceduralWeaponAnimation __instance)
            {
                return __instance.Shootingg.CurrentRecoilEffect.HandRotationRecoilEffect is NewRotationRecoilProcess;
            }
        }

        private class RecoilForcePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ShotEffector), nameof(ShotEffector.Process));
            }

            [PatchPrefix]
            private static void Prefix(ShotEffector __instance, ref float str)
            {
                if (IsLocal(__instance._firearmController))
                {
                    str *= _recoilMultiplier.Value;
                }
            }
        }

        private class RecoilWeaponPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ShotEffector), nameof(ShotEffector.OnWeaponParametersChanged));
            }

            [PatchPostfix]
            private static void Postfix(ShotEffector __instance)
            {
                if (!IsLocal(__instance._firearmController))
                {
                    return;
                }
                ShotEffector.RecoilShotVal[] values = __instance.CurrentRecoilEffect?.RecoilProcessValues;
                if (values == null || values.Length < 5)
                {
                    return;
                }
                if (__instance.CurrentRecoilEffect is OldRecoilShotEffect old && __instance._weapon != null)
                {
                    WeaponTemplate template = __instance._weapon.WeaponTemplate;
                    AlphaRecoil alpha;
                    if (!AlphaWeapons.TryGetValue(__instance._weapon.Item.TemplateId.ToString(), out alpha))
                    {
                        alpha = new AlphaRecoil(template.RecoilForceUp * AlphaUpRatio, template.RecoilForceBack * AlphaBackRatio, template.RecoilCamera * AlphaCameraRatio);
                    }
                    Vector2 suppression = __instance._buffs != null ? __instance._buffs.RecoilSupression : Vector2.zero;
                    float factor = Mathf.Max(0f, (1f + __instance.RecoilDelta()) * (1f - suppression.x - suppression.y * ShotEffector.RECOIL_SUPPRESSION_FACTOR));
                    float scale = AlphaForceScale * _oldRecoilStrength.Value;
                    old.BasicPlayerRecoilRotationStrength = AlphaRotationRange * (alpha.Up * factor * scale);
                    old.BasicPlayerRecoilPositionStrength = AlphaPositionRange * (alpha.Back * factor * scale);
                    values[3].IntensityMultiplicator = alpha.Camera;
                    values[4].IntensityMultiplicator = -alpha.Camera;
                }
                float multiplier = _cameraRecoilMultiplier.Value;
                values[3].IntensityMultiplicator *= multiplier;
                values[4].IntensityMultiplicator *= multiplier;
            }
        }

        private class SpringRestorePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.InitWeaponData));
            }

            [PatchPostfix]
            private static void Postfix(ProceduralWeaponAnimation __instance, IWeapon weapon)
            {
                if (!_springCaptured || !IsLocal(__instance))
                {
                    return;
                }
                if (__instance.Shootingg.CurrentRecoilEffect.HandRotationRecoilEffect is OldRotationRecoilProcess spring)
                {
                    spring.ReturnSpeed = weapon.WeaponTemplate.RecoilReturnSpeedHandRotation * __instance.Aiming.RecoilConvergenceMult * _oldRecoilReturn.Value;
                    spring.Damping = _springDamping;
                    __instance.CameraSmoothRecoil = __instance.CameraSmoothSteady;
                }
            }
        }

        private class SwayFactorsPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateSwayFactors));
            }

            [PatchPostfix]
            private static void Postfix(ProceduralWeaponAnimation __instance)
            {
                if (!IsLocal(__instance))
                {
                    return;
                }
                float influence = AlphaHandling.AlphaSwayEnabled ? 0f : _swayWeightInfluence.Value;
                float sway = SwayStrength(__instance);
                if (influence < 1f)
                {
                    float baseSway = EFTHardSettings.Instance.SWAY_STRENGTH_PER_KG.Evaluate(AlphaReferenceWeight);
                    sway = Mathf.Lerp(baseSway, sway, influence);
                }
                sway *= _swayMultiplier.Value;
                SwayStrength(__instance) = sway;
                __instance.MotionReact.SwayFactors = new Vector3(sway, __instance.IsAiming ? sway * 0.3f : sway, sway) * __instance.IntensityByAiming;
                DisplacementStrength(__instance) *= AlphaHandling.AlphaSwayEnabled ? 0f : _displacementMultiplier.Value;
            }
        }

        private class WeaponVariablesPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateWeaponVariables));
            }

            [PatchPostfix]
            private static void Postfix(ProceduralWeaponAnimation __instance)
            {
                if (IsLocal(__instance))
                {
                    AimSwayStrength(__instance) *= AlphaHandling.AlphaSwayEnabled ? 0f : _aimSwayMultiplier.Value;
                }
            }
        }
    }
}
