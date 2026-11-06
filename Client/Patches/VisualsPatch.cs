using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class Visuals
    {
        private static ConfigEntry<bool> _blood;

        private static readonly AccessTools.FieldRef<EffectsController, BloodOnScreen> ControllerBlood =
            AccessTools.FieldRefAccess<EffectsController, BloodOnScreen>("_bloodOnScreen");
        private static readonly AccessTools.FieldRef<BloodOnScreen, BloodDropsController> BloodDrops =
            AccessTools.FieldRefAccess<BloodOnScreen, BloodDropsController>("_bloodDropsController");
        private static readonly AccessTools.FieldRef<BloodOnScreen, bool> BloodLocked =
            AccessTools.FieldRefAccess<BloodOnScreen, bool>("_locked");
        private static readonly AccessTools.FieldRef<BloodOnScreen, float> BloodLastHit =
            AccessTools.FieldRefAccess<BloodOnScreen, float>("_lastHitTime");

        public static void Enable(ConfigFile config)
        {
            _blood = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Screen Effects", "Blood On Screen", true, "Like the alpha, every hit splashes blood drops onto your view.", "Visuals", "Blood On Screen");
            new BloodPatch().Enable();
        }

        private class BloodPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EffectsController), nameof(EffectsController.OnPlayerDamaged));
            }

            [PatchPostfix]
            private static void Postfix(EffectsController __instance, EDamageType type)
            {
                if (!_blood.Value || type == EDamageType.Undefined || type.IsSelfInflicted() || type.IsBleeding())
                {
                    return;
                }
                if (!__instance.enabled || !__instance.gameObject.activeInHierarchy)
                {
                    return;
                }
                BloodOnScreen blood = ControllerBlood(__instance);
                if (blood == null)
                {
                    return;
                }
                if (!blood.enabled)
                {
                    blood.enabled = true;
                }
                BloodDropsController drops = BloodDrops(blood);
                if (drops == null || BloodLocked(blood))
                {
                    return;
                }
                Vector3 sphere = Random.onUnitSphere;
                Vector3 direction = __instance.transform.InverseTransformDirection(new Vector3(sphere.x, 0f, sphere.z)).normalized;
                BloodLastHit(blood) = Time.time;
                drops.CaptureBloodShot(direction);
                BloodLocked(blood) = true;
            }
        }
    }
}
