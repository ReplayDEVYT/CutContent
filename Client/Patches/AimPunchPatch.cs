using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class AimPunch
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _hitStrength;
        private static ConfigEntry<float> _explosionStrength;
        private static ConfigEntry<float> _damageToPunch;
        private static ConfigEntry<float> _maxPunch;

        private static int _punchFrame;
        private static float _punchUsed;

        private static readonly AccessTools.FieldRef<EffectsController, Player> EffectsPlayer =
            AccessTools.FieldRefAccess<EffectsController, Player>("player");

        public static void Enable(ConfigFile config)
        {
            const string section = "Combat - Aim Punch";
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Combat, "Damage", "Aim Punch", true, "Restores the alpha's aim punch: taking damage violently knocks your view around, scaled by how hard you were hit, and explosions shake you much harder.", section, "Enabled");
            _hitStrength = config.Bind(section, "Hit Punch Strength", 1f, new ConfigDescription("Scales the view kick from taking damage. 1 is close to the alpha.", new AcceptableValueRange<float>(0f, 3f)));
            _explosionStrength = config.Bind(section, "Explosion Shake Strength", 2f, new ConfigDescription("Multiplies EFT's own explosion camera shake. 1 is the current game.", new AcceptableValueRange<float>(0f, 5f)));
            _damageToPunch = config.Bind(section, "Punch Per Damage", 0.03f, ConfigHelper.Advanced("Punch strength per point of damage, before armor. A full grenade shake is about 1.2.", new AcceptableValueRange<float>(0.005f, 0.2f)));
            _maxPunch = config.Bind(section, "Punch Limit Per Frame", 2.5f, ConfigHelper.Advanced("Upper limit on the total punch in one frame, so many fragments landing at once do not flip the camera.", new AcceptableValueRange<float>(0.5f, 10f)));

            new PlayerDamagedPatch().Enable();
            new GrenadeShakePatch().Enable();
        }

        private static float BodyPartScale(EBodyPart bodyPart)
        {
            switch (bodyPart)
            {
                case EBodyPart.Head:
                    return 1.5f;
                case EBodyPart.LeftArm:
                case EBodyPart.RightArm:
                    return 0.8f;
                case EBodyPart.LeftLeg:
                case EBodyPart.RightLeg:
                    return 0.6f;
                default:
                    return 1f;
            }
        }

        private class PlayerDamagedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EffectsController), nameof(EffectsController.OnPlayerDamaged));
            }

            [PatchPostfix]
            private static void Postfix(EffectsController __instance, float damage, EBodyPart bodyPart, EDamageType type, float damageReducedByArmor)
            {
                if (!_enabled.Value || _hitStrength.Value <= 0f || type.IsSelfInflicted() || type == EDamageType.Undefined)
                {
                    return;
                }
                if (!__instance.enabled || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                Player player = EffectsPlayer(__instance);
                if (player == null || player.ProceduralWeaponAnimation == null || player.ProceduralWeaponAnimation.ForceReact == null)
                {
                    return;
                }

                float total = damage + damageReducedByArmor;
                if (total <= 0f)
                {
                    return;
                }

                if (_punchFrame != Time.frameCount)
                {
                    _punchFrame = Time.frameCount;
                    _punchUsed = 0f;
                }

                float power = Mathf.Min(total * _damageToPunch.Value * BodyPartScale(bodyPart) * _hitStrength.Value, _maxPunch.Value - _punchUsed);
                if (power <= 0.01f)
                {
                    return;
                }
                _punchUsed += power;
                player.ProceduralWeaponAnimation.ForceReact.GrenadeShake(-Random.onUnitSphere, power);
            }
        }

        private class GrenadeShakePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ForceEffector), nameof(ForceEffector.GrenadeShake_CO));
            }

            [PatchPrefix]
            private static void Prefix(ref float strength)
            {
                if (_enabled.Value)
                {
                    strength = Mathf.Min(strength * _explosionStrength.Value, 3f);
                }
            }
        }
    }
}
