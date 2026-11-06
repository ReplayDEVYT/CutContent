using BepInEx.Configuration;
using Comfort.Common;
using CommonAssets.Scripts.ArtilleryShelling;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace mizmii.ReClassic.Patches
{
    internal static class FragmentDamage
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _multiplier;
        private static HashSet<string> _fragmentIds;

        public static void Enable(ConfigFile config)
        {
            const string section = "Combat - Grenade Fragments";
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Combat, "Damage", "Fix Fragment Damage", true,
                "Bots' intentional-miss logic was throwing away grenade fragment hits, so fragments from bot grenades only damaged armor. With this on, every fragment hit damages the limb it strikes.", section, "Fix Fragment Damage");
            _multiplier = config.Bind(section, "Fragment Damage Multiplier", 1f,
                ConfigHelper.Advanced("Scales the damage of every grenade, launcher and mine fragment hit.", new AcceptableValueRange<float>(0f, 3f)));

            new MissControllerPatch().Enable();
            new ApplyShotPatch().Enable();
        }

        private static bool IsFragment(DamageInfo damage)
        {
            if (damage.DamageType == EDamageType.GrenadeFragment || damage.Weapon is ThrowWeap)
            {
                return true;
            }
            if (string.IsNullOrEmpty(damage.SourceId))
            {
                return false;
            }
            if (damage.SourceId == ArtilleryProjectileExplosiveItem.ARTILLERY_FRAGMENT_TEMPLATE_ID)
            {
                return true;
            }
            if (_fragmentIds == null && !TryBuildFragmentIds())
            {
                return false;
            }
            return _fragmentIds.Contains(damage.SourceId);
        }

        private static bool TryBuildFragmentIds()
        {
            if (!Singleton<ItemFactory>.Instantiated)
            {
                return false;
            }

            var ids = new HashSet<string>();
            foreach (ItemTemplate template in Singleton<ItemFactory>.Instance.ItemTemplates.Values)
            {
                if (template is ThrowWeapTemplate throwable && !string.IsNullOrEmpty(throwable.FragmentType))
                {
                    ids.Add(throwable.FragmentType);
                }
                else if (template is AmmoTemplate ammo && ammo.FragmentsCount > 0 && !string.IsNullOrEmpty(ammo.FragmentType))
                {
                    ids.Add(ammo.FragmentType);
                }
            }
            _fragmentIds = ids;
            return true;
        }

        private class MissControllerPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BotEnemiesMissController), nameof(BotEnemiesMissController.ShouldApplyDamage));
            }

            [PatchPrefix]
            private static bool Prefix(DamageInfo damage, ref bool __result)
            {
                if (!_enabled.Value || !IsFragment(damage))
                {
                    return true;
                }
                __result = true;
                return false;
            }
        }

        private class ApplyShotPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(Player), nameof(Player.ApplyShot));
            }

            [PatchPrefix]
            private static void Prefix(ref DamageInfo damageInfo)
            {
                if (_multiplier.Value != 1f && IsFragment(damageInfo))
                {
                    damageInfo.Damage *= _multiplier.Value;
                }
            }
        }
    }
}
