using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI.Health;
using HarmonyLib;
using mizmii.ReClassic.Radiation;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Pressure
{
    internal static class BloodPressure
    {
        private const string Section = "Blood Pressure";

        public const float NormalSystolic = 120f;
        public const float NormalDiastolic = 80f;
        public const EHealthFactorType AttributeId = (EHealthFactorType)50;

        private static readonly System.Collections.Generic.Dictionary<string, Vector2> Painkillers = new System.Collections.Generic.Dictionary<string, Vector2>
        {
            ["544fb37f4bdc2dee738b4567"] = new Vector2(-8f, -5f),
            ["544fb3f34bdc2d03748b456a"] = new Vector2(-24f, -15f),
            ["5751a89d24597722aa0e8db0"] = new Vector2(-2f, -1f),
            ["5755383e24597772cb798966"] = Vector2.zero,
            ["590c695186f7741e566b64a2"] = new Vector2(-3f, -2f),
            ["5af0548586f7743a532b7e99"] = new Vector2(5f, 3f),
            ["5c0e530286f7747fa1419862"] = new Vector2(-6f, -4f),
            ["5c10c8fd86f7743d7d706df3"] = Vector2.zero,
            ["5d1b376e86f774252519444e"] = new Vector2(-14f, -9f),
            ["5d403f9186f7743cac3f229b"] = new Vector2(-10f, -6f),
            ["5d40407c86f774318526545a"] = new Vector2(-10f, -6f),
            ["614451b71e5874611e2c7ae5"] = new Vector2(-12f, -8f),
            ["5ed515e03a40a50460332579"] = new Vector2(18f, 11f),
            ["637b620db7afa97bfc3d7009"] = new Vector2(-14f, -9f)
        };

        public static Vector2 PainkillerShift(string templateId, float duration)
        {
            if (!string.IsNullOrEmpty(templateId) && Painkillers.TryGetValue(templateId, out Vector2 known))
            {
                return known;
            }
            float strength = 4f + 10f * Mathf.Clamp01(duration / 300f);
            return new Vector2(-strength, -strength * 0.65f);
        }
        public static Vector2 RegenerationShift = new Vector2(25f, 14f);
        public static Vector2 TemperatureStimShift = new Vector2(-14f, -9f);
        public static Vector2 BoostShift = new Vector2(6f, 4f);


        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> Strength;
        public static ConfigEntry<float> Speed;
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<KeyboardShortcut> LogPressure;

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Health", "Blood Pressure", true,
                "Your blood pressure moves with what happens to your body and shows in the health panel as systolic over diastolic, with how fast each is changing per minute. Bleeding, lost health and painkillers lower it, and painkillers also make you a little drowsy. Regenerating stimulants, adrenaline, hard running and the cold raise it, while heat and stimulants that change your body temperature lower it. High blood pressure (140/90 and up) gives you head pain and shaking hands, slowly damages your thorax, and caps your arm and leg stamina at 75% without letting it recover. Low blood pressure (90/60 and down) narrows your vision, drains your consciousness fast and keeps you from standing fully upright. Passing out while your blood pressure is severely high or low tears your body apart.",
                Section, "Enabled");
            Strength = config.Bind(Section, "Strength", 1f, new ConfigDescription("How hard bleeding, stimulants, painkillers and everything else push your blood pressure. 1 is the default.", new AcceptableValueRange<float>(0.25f, 2f)));
            Speed = config.Bind(Section, "Change Speed", 1f, new ConfigDescription("How fast your blood pressure follows. 1 takes about a minute to settle.", new AcceptableValueRange<float>(0.25f, 4f)));
            DamageScale = config.Bind(Section, "Damage Scale", 1f, new ConfigDescription("How much high blood pressure and passing out with severe blood pressure hurt. 0 turns the damage off.", new AcceptableValueRange<float>(0f, 3f)));
            LogPressure = config.Bind(Section, "Log Blood Pressure", new KeyboardShortcut(UnityEngine.KeyCode.None), "Prints your blood pressure and everything affecting it to the BepInEx log.");
            ConfigHelper.Requires(Enabled, Strength, Speed, DamageScale, LogPressure);

            new GameStartedPatch().Enable();
            new HealthPanelPatch().Enable();
            new AttributePatch().Enable();
            Effects.AttributeIcons.Register(AttributeId, () => Effects.AttributeIcons.GameSprite("health_icon_blood_pressure"));
        }

        private class AttributePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Constructor(typeof(HealthEffectsComponent), new[] { typeof(Item), typeof(IHealthEffectsComponentTemplate) });
            }

            [PatchPostfix]
            private static void Postfix(HealthEffectsComponent __instance, Item item)
            {
                try
                {
                    if (item == null || !Enabled.Value)
                    {
                        return;
                    }
                    Vector2 shift = item.TemplateId.ToString() == Overdose.Overdose.Antidote ? new Vector2(45f, 25f) : Vector2.zero;
                    float seconds = 0f;
                    if (__instance.DamageEffects != null && __instance.DamageEffects.TryGetValue(EDamageEffectType.Pain, out var pain))
                    {
                        Vector2 painShift = PainkillerShift(item.TemplateId.ToString(), pain.Duration);
                        if (painShift != Vector2.zero)
                        {
                            shift += painShift;
                            seconds = Mathf.Max(seconds, pain.Duration);
                        }
                    }
                    var buffs = __instance.BuffSettings;
                    if (buffs != null)
                    {
                        bool regeneration = false;
                        bool temperature = false;
                        int boosts = 0;
                        foreach (var buff in buffs)
                        {
                            if (buff == null)
                            {
                                continue;
                            }
                            switch (buff.BuffType)
                            {
                                case EStimulatorBuffType.HealthRate:
                                    if (buff.Value > 0f)
                                    {
                                        regeneration = true;
                                        seconds = Mathf.Max(seconds, buff.Duration);
                                    }
                                    break;
                                case EStimulatorBuffType.BodyTemperature:
                                    temperature = true;
                                    seconds = Mathf.Max(seconds, buff.Duration);
                                    break;
                                case EStimulatorBuffType.MaxStamina:
                                case EStimulatorBuffType.StaminaRate:
                                case EStimulatorBuffType.EnergyRate:
                                    if (buff.Value > 0f)
                                    {
                                        boosts++;
                                        seconds = Mathf.Max(seconds, buff.Duration);
                                    }
                                    break;
                            }
                        }
                        if (regeneration)
                        {
                            shift += RegenerationShift;
                        }
                        if (temperature)
                        {
                            shift += TemperatureStimShift;
                        }
                        if (boosts > 0)
                        {
                            shift += BoostShift * Mathf.Min(boosts, 4);
                        }
                    }
                    if (Mathf.Abs(shift.x) < 0.5f && Mathf.Abs(shift.y) < 0.5f)
                    {
                        return;
                    }
                    float systolic = Mathf.Round(shift.x);
                    float diastolic = Mathf.Round(shift.y);
                    string value = systolic > 0f
                        ? systolic.ToString("0") + "/" + diastolic.ToString("+0;-0;0")
                        : systolic.ToString("+0;-0;0") + "/" + diastolic.ToString("+0;-0;0");
                    if (seconds > 0f)
                    {
                        value += " (over " + seconds.ToString("0") + "s)";
                    }
                    string full = "Blood pressure " + systolic.ToString("+0;-0;0") + "/" + diastolic.ToString("+0;-0;0") + " mmHg" + (seconds > 0f ? " over " + seconds.ToString("0") + "s" : "");
                    item.Attributes.Add(new ItemAttribute(AttributeId)
                    {
                        Name = "Blood pressure",
                        DisplayNameFunc = () => "Blood pressure",
                        Base = () => systolic,
                        StringValue = () => value,
                        FullStringValue = () => full,
                        DisplayType = () => EItemAttributeDisplayType.Compact,
                        LabelVariations = EItemAttributeLabelVariations.Colored,
                        LessIsGood = true
                    });
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Blood Pressure] Could not add the blood pressure details to an item: {e.Message}");
                }
            }
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
                    if (__instance.GetComponent<PressureRunner>() == null)
                    {
                        __instance.gameObject.AddComponent<PressureRunner>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Blood Pressure] Could not start blood pressure: {e}");
                }
            }
        }

        private class HealthPanelPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(HealthParametersPanel), nameof(HealthParametersPanel.UpdateMainHealthAndStats));
            }

            [PatchPostfix]
            private static void Postfix(HealthParametersPanel __instance)
            {
                try
                {
                    HealthParameterPanel panel = __instance._bloodPressure;
                    if (panel == null || !Enabled.Value)
                    {
                        return;
                    }
                    PressureRunner runner = PressureRunner.Instance;
                    float systolic = runner != null ? runner.Systolic : NormalSystolic;
                    float diastolic = runner != null ? runner.Diastolic : NormalDiastolic;
                    panel.SetParameterValue(new ValueStruct { Current = systolic, Minimum = 0f, Maximum = diastolic }, -1f, 0, false, true);
                    if (panel._maxValue == null && panel._currentValue != null)
                    {
                        panel._currentValue.SetMonospaceText(Mathf.RoundToInt(systolic) + "/" + Mathf.RoundToInt(diastolic));
                    }
                    bool off = runner != null && (runner.High || runner.Low);
                    panel.SetWarningColor(off, false);
                    RateReadout readout = RateReadout.For(panel, __instance._hydration);
                    if (readout != null)
                    {
                        if (runner != null)
                        {
                            readout.SetPair(runner.SystolicRate, runner.DiastolicRate, runner.TowardNormal);
                        }
                        else
                        {
                            readout.SetPair(0f, 0f, true);
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Blood Pressure] Could not show the blood pressure: {e.Message}");
                }
            }
        }
    }
}
