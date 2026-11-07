using BepInEx.Configuration;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace mizmii.ReClassic.Patches
{
    internal static class UsingBar
    {
        private static ConfigEntry<bool> _enabled;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Interface", "Item Usage Bar", true,
                "Using a medical item, food or drink shows the old USING panel with the item's name and a progress bar that fills over the item's use time. This is your own setting and isn't changed by the host in co-op.",
                "Gameplay - Item Usage Bar", "Item Usage Bar");
            new EffectStartedPatch().Enable();
        }

        private class EffectStartedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(UsingPanel), nameof(UsingPanel.OnEffectStarted));
            }

            [PatchPrefix]
            private static void Prefix(IHealthEffect effect)
            {
                if (effect is IMedEffect)
                {
                    EFTHardSettings settings = EFTHardSettings.Instance;
                    if (settings != null)
                    {
                        settings.MED_EFFECT_USING_PANEL = _enabled.Value;
                    }
                }
            }
        }
    }
}
