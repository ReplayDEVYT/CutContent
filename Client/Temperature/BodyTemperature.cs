using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Temperature
{

    internal static class BodyTemperature
    {
        private const string Section = "Body Temperature";

        public const float Normal = 36.7f;
        public const float HotAt = 37.8f;
        public const float ColdAt = 34.5f;
        public const float OverheatAt = 39.3f;
        public const float FrostbiteAt = 32f;

        private static readonly string[] FireNames = { "fire", "bonfire", "campfire", "flame", "brazier", "burn", "stove", "furnace" };
        private static readonly string[] NotFire = { "firework", "fireext", "fire_ext", "extinguisher", "firearm", "fire_alarm", "firealarm", "fire_hose", "firehose", "fireplace_off", "fire_escape", "fireescape" };

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> Strength;
        public static ConfigEntry<float> Speed;
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<KeyboardShortcut> LogTemperature;

        public static readonly List<Vector3> Fires = new List<Vector3>();
        public static readonly List<Collider> Flames = new List<Collider>();

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Hazards", "Body Temperature", true,
                "Your body temperature moves with the world around you instead of sitting at 36.6. Cold seasons, night, rain, snow, wind and blood loss cool you down; summer and spring, direct sunlight, fires, hard running, long fatigue and regenerative stimulants warm you up. It always drifts back toward 36.7 and never jumps: it slowly heads for whatever your surroundings push it to. Being indoors slows how fast you lose heat, standing by a fire warms you, and drinking, resting, taking off heavy gear, wading through water and being wet all help you cool off. Heavy rain and water leave you Wet, which keeps cooling you until you dry off. At 37.8 or above you get Hot: you get thirsty and out of breath faster, and Fatigue hits twice as hard. At 34.5 or below you get Cold: your hands shake, you can't move as fast and you get hungry faster. Past that you get Overheating, which narrows your vision, drains you and keeps hurting your whole body until you cool down or die, or Frostbite, which makes the cold worse. Your temperature also shows on thermal sights.",
                Section, "Enabled");
            Strength = config.Bind(Section, "Environment Strength", 1f, new ConfigDescription("How hard the weather, time of day and everything else push your temperature away from normal. 1 is the default.", new AcceptableValueRange<float>(0.25f, 2f)));
            Speed = config.Bind(Section, "Change Speed", 1f, new ConfigDescription("How fast your temperature moves. 1 takes several minutes to get cold in a winter night.", new AcceptableValueRange<float>(0.25f, 4f)));
            DamageScale = config.Bind(Section, "Overheating Damage", 1f, new ConfigDescription("How much Overheating hurts. 0 turns the damage off.", new AcceptableValueRange<float>(0f, 3f)));
            LogTemperature = config.Bind(Section, "Log Temperature", new KeyboardShortcut(KeyCode.None), "Prints your temperature and everything affecting it to the BepInEx log.");
            ConfigHelper.Requires(Enabled, Strength, Speed, DamageScale, LogTemperature);

            new GameStartedPatch().Enable();
            new DrinkPatch().Enable();
            new AttributePatch().Enable();
        }

        public static bool IsLocal(ActiveHealthController health)
        {
            Player local = Singleton<GameWorld>.Instance?.MainPlayer;
            return local != null && ReferenceEquals(local.ActiveHealthController, health);
        }

        private static bool LooksLikeFire(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            string lower = name.ToLowerInvariant();
            foreach (string skip in NotFire)
            {
                if (lower.Contains(skip))
                {
                    return false;
                }
            }
            foreach (string fire in FireNames)
            {
                if (lower.Contains(fire))
                {
                    return true;
                }
            }
            return false;
        }

        private static void FindFires()
        {
            Fires.Clear();
            Flames.Clear();
            foreach (FlameDamageTrigger trigger in UnityEngine.Object.FindObjectsOfType<FlameDamageTrigger>())
            {
                if (trigger == null)
                {
                    continue;
                }
                Collider collider = trigger.GetComponent<Collider>();
                if (collider != null)
                {
                    Flames.Add(collider);
                }
                else
                {
                    Fires.Add(trigger.transform.position);
                }
            }
            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light == null || light.type != LightType.Point || !light.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Transform t = light.transform;
                if (LooksLikeFire(t.name) || (t.parent != null && LooksLikeFire(t.parent.name)))
                {
                    Vector3 position = t.position;
                    bool near = false;
                    foreach (Vector3 fire in Fires)
                    {
                        if ((fire - position).sqrMagnitude < 4f)
                        {
                            near = true;
                            break;
                        }
                    }
                    if (!near)
                    {
                        Fires.Add(position);
                    }
                }
            }
            Plugin.LogSource.LogInfo($"[Temperature] Found {Flames.Count} burning areas and {Fires.Count} fires to warm up at.");
        }

        public static float FireHeat(Vector3 position)
        {
            float heat = 0f;
            foreach (Collider flame in Flames)
            {
                if (flame == null)
                {
                    continue;
                }
                float distance = Vector3.Distance(flame.ClosestPoint(position), position);
                heat += Mathf.Clamp01(1f - distance / 6f);
            }
            foreach (Vector3 fire in Fires)
            {
                float distance = Vector3.Distance(fire, position);
                if (distance < 5f)
                {
                    float k = 1f - distance / 5f;
                    heat += k * k;
                }
            }
            return Mathf.Clamp01(heat);
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
                    FindFires();
                    if (__instance.GetComponent<TemperatureRunner>() == null)
                    {
                        __instance.gameObject.AddComponent<TemperatureRunner>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Temperature] Could not start body temperature: {e}");
                }
            }
        }

        public static float DrinkCooling(float hydration)
        {
            return Mathf.Min(2.5f, hydration * 0.03f);
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
                    if (item.TemplateId.ToString() == Overdose.Overdose.Antidote)
                    {
                        Add(item, 0.8f, "0.8°C");
                    }
                    if (__instance.HealthEffects != null && __instance.HealthEffects.TryGetValue(EHealthFactorType.Hydration, out var water) && water.Value > 0.5f)
                    {
                        float cooling = DrinkCooling(water.Value);
                        Add(item, -cooling, cooling.ToString("0.0") + "°C");
                    }
                    float seconds = 0f;
                    var buffs = __instance.BuffSettings;
                    if (buffs != null)
                    {
                        foreach (var buff in buffs)
                        {
                            if (buff != null && buff.BuffType == EStimulatorBuffType.HealthRate && buff.Value > 0f)
                            {
                                seconds = Mathf.Max(seconds, buff.Duration);
                            }
                        }
                    }
                    if (seconds > 0f)
                    {
                        float warming = TemperatureRunner.RegenHeatPerMinute * seconds / 60f;
                        Add(item, warming, warming.ToString("0.0") + "°C / " + "Dur.".Localized() + " " + seconds.ToString("0") + "sec".Localized());
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Temperature] Could not add the temperature details to an item: {e.Message}");
                }
            }

            private static void Add(Item item, float change, string value)
            {
                item.AddOrReplaceAttribute(new ItemAttribute(EHealthFactorType.Temperature)
                {
                    Name = EHealthFactorType.Temperature.ToString(),
                    Base = () => change,
                    StringValue = () => value,
                    FullStringValue = () => EHealthFactorType.Temperature.ToString().Localized() + "\n" + (change > 0f ? "increase".Localized() : "decrease".Localized()) + " " + Mathf.Abs(change).ToString("0.0") + "°C",
                    DisplayType = () => EItemAttributeDisplayType.Compact,
                    LabelVariations = EItemAttributeLabelVariations.Colored,
                    LessIsGood = true
                });
            }
        }

        private class DrinkPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHydration));
            }

            [PatchPrefix]
            private static void Prefix(ActiveHealthController __instance, float value)
            {
                if (value > 0.05f && Enabled.Value && IsLocal(__instance))
                {
                    TemperatureRunner.Drank(value);
                }
            }
        }
    }
}
