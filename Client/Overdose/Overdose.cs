using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Overdose
{
    internal static class Overdose
    {
        private const string Section = "Overdose";

        public const string Antidote = "5fca138c2a7b221b2852a5c6";
        public const EHealthFactorType DrugAttribute = (EHealthFactorType)51;
        public const EHealthFactorType StressAttribute = (EHealthFactorType)52;

        public static readonly Dictionary<string, float> DrugLevels = new Dictionary<string, float>
        {
            ["5c0e530286f7747fa1419862"] = 15f,
            ["5c0e531286f7747fa54205c2"] = 15f,
            ["5c0e531d86f7747fa23f4d42"] = 25f,
            ["5c0e534186f7747fa1419867"] = 20f,
            ["5c10c8fd86f7743d7d706df3"] = 20f,
            ["5ed515c8d380ab312177c0fa"] = 20f,
            ["5ed515e03a40a50460332579"] = 25f,
            ["5ed515ece452db0eb56fc028"] = 15f,
            ["5ed5160a87bb8443d10680b5"] = 10f,
            ["5ed51652f6c34d2cc26336a1"] = 15f,
            ["5ed5166ad380ab312177c100"] = 35f,
            ["5fca13ca637ee0341a484f46"] = 15f,
            ["637b60c3b7afa97bfc3d7001"] = 40f,
            ["637b612fb7afa97bfc3d7005"] = 25f,
            ["637b6179104668754b72f8f5"] = 30f,
            ["637b620db7afa97bfc3d7009"] = 25f,
            ["66507eabf5ddb0818b085b68"] = 20f,
            ["544fb3f34bdc2d03748b456a"] = 30f,
            ["544fb37f4bdc2dee738b4567"] = 6f,
            ["5af0548586f7743a532b7e99"] = 5f,
            ["590c695186f7741e566b64a2"] = 8f,
            ["5751a89d24597722aa0e8db0"] = 3f,
            ["5755383e24597772cb798966"] = 2f
        };

        public static readonly Dictionary<string, float> StressRelief = new Dictionary<string, float>
        {
            ["5d40407c86f774318526545a"] = 20f,
            ["614451b71e5874611e2c7ae5"] = 15f,
            ["5d403f9186f7743cac3f229b"] = 20f,
            ["5d1b376e86f774252519444e"] = 30f,
            ["544fb3f34bdc2d03748b456a"] = 30f,
            ["637b620db7afa97bfc3d7009"] = 25f,
            ["5751a89d24597722aa0e8db0"] = 10f,
            ["5ed5166ad380ab312177c100"] = 20f,
            ["637b60c3b7afa97bfc3d7001"] = 20f
        };

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<KeyboardShortcut> LogDrugs;

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Health", "Overdose", true,
                "Stimulants and painkillers carry a Drug Level, shown on the item. Every dose adds to a hidden meter that slowly wears off. At 50 you Overdose: you lose consciousness fast, your thorax takes damage, your arm and leg stamina is capped at 50% and you breathe heavily. Past 80 your whole body starts taking damage, and you also wheeze like you're dying, limp even on painkillers, move slower, shake badly, lose your peripheral vision, feel pain and slump over. At 100 your heart stops. The xTG-12 antidote injector clears an overdose quickly and gives you Adrenaline, but sends your blood pressure and temperature up and empties your stamina. Some drinks and drugs also take the edge off Stress.",
                Section, "Enabled");
            DamageScale = config.Bind(Section, "Damage Scale", 1f, new ConfigDescription("How much an overdose hurts your thorax. 0 turns the damage off.", new AcceptableValueRange<float>(0f, 3f)));
            LogDrugs = ConfigHelper.DebugKey(config, Section, "Log Drug Level", "Prints your hidden drug level to the BepInEx log.");
            ConfigHelper.Requires(Enabled, DamageScale, LogDrugs);

            Effects.AttributeIcons.Register(DrugAttribute, () => Effects.CustomEffects.Icon("effect_overpk.png"));
            Effects.AttributeIcons.Register(StressAttribute, () => Effects.CustomEffects.Icon("effect_stress.png"));
            new GameStartedPatch().Enable();
            new UsePatch().Enable();
            new AttributePatch().Enable();
            new DyingBreathPatch().Enable();
        }

        private class GameStartedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
            }

            [PatchPostfix]
            private static void Postfix(GameWorld __instance)
            {
                try
                {
                    string location = __instance.LocationId;
                    if (string.IsNullOrEmpty(location) || location.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return;
                    }
                    if (__instance.GetComponent<OverdoseRunner>() == null)
                    {
                        __instance.gameObject.AddComponent<OverdoseRunner>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Overdose] Could not start the overdose tracking: {e}");
                }
            }
        }

        private class UsePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoMedEffect));
            }

            [PatchPostfix]
            private static void Postfix(ActiveHealthController __instance, Item item, IHealthEffect __result)
            {
                try
                {
                    if (item == null || (__result == null && item.TemplateId.ToString() != Antidote))
                    {
                        return;
                    }
                    Player local = Singleton<GameWorld>.Instance?.MainPlayer;
                    if (local == null || !ReferenceEquals(local.ActiveHealthController, __instance))
                    {
                        return;
                    }
                    string id = item.TemplateId.ToString();
                    if (Enabled.Value)
                    {
                        if (id == Antidote)
                        {
                            OverdoseRunner.Instance?.Antidote(local);
                        }
                        else if (DrugLevels.TryGetValue(id, out float level))
                        {
                            OverdoseRunner.Instance?.Dose(level);
                        }
                    }
                    if (StressRelief.TryGetValue(id, out float relief) && Stress.Stress.Enabled.Value)
                    {
                        Stress.StressRunner.Instance?.Relieve(relief);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Overdose] Could not apply {item?.ShortName}: {e.Message}");
                }
            }
        }

        private class AttributePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Constructor(typeof(HealthEffectsComponent), new[] { typeof(Item), typeof(IHealthEffectsComponentTemplate) });
            }

            [PatchPostfix]
            private static void Postfix(Item item)
            {
                try
                {
                    if (item == null)
                    {
                        return;
                    }
                    string id = item.TemplateId.ToString();
                    if (Enabled.Value && id == Antidote)
                    {
                        Add(item, DrugAttribute, "Drug level", "<color=#54C1FF>Clears overdose</color>", 0f, false);
                    }
                    else if (Enabled.Value && DrugLevels.TryGetValue(id, out float level))
                    {
                        Add(item, DrugAttribute, "Drug level", level.ToString("0"), level, true);
                    }
                    if (Stress.Stress.Enabled != null && Stress.Stress.Enabled.Value && StressRelief.TryGetValue(id, out float relief))
                    {
                        Add(item, StressAttribute, "Stress", "-" + relief.ToString("0"), -relief, true);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Overdose] Could not add the drug details to an item: {e.Message}");
                }
            }

            private static void Add(Item item, EHealthFactorType id, string name, string value, float baseValue, bool colored)
            {
                item.Attributes.Add(new ItemAttribute(id)
                {
                    Name = name,
                    DisplayNameFunc = () => name,
                    Base = () => baseValue,
                    StringValue = () => value,
                    FullStringValue = () => name + " " + value,
                    DisplayType = () => EItemAttributeDisplayType.Compact,
                    LabelVariations = colored ? EItemAttributeLabelVariations.Colored : EItemAttributeLabelVariations.None,
                    LessIsGood = true
                });
            }
        }

        private class DyingBreathPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.UpdateBreathStatus));
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance)
            {
                if (!OverdoseRunner.Dying || !__instance.IsYourPlayer || __instance.Speaker == null)
                {
                    return true;
                }
                if (!__instance.HeavyBreath)
                {
                    __instance.Speaker.Play(EPhraseTrigger.OnBreath, ETagStatus.Dying | ETagStatus.Aware, true);
                }
                __instance.HeavyBreath = true;
                return false;
            }
        }
    }
}
