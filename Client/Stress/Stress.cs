using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Stress
{
    internal static class Stress
    {
        private const string Section = "Stress";

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> BuildUp;
        public static ConfigEntry<bool> Voice;
        public static ConfigEntry<bool> Breathing;
        public static ConfigEntry<KeyboardShortcut> LogStress;

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Health", "Stress", true,
                "Being shot at, getting hit and being close to death in a fight builds up stress. Once you're Stressed your hands shake, you breathe heavily, and you may call out on your own when you're hurt, reloading or your weapon jams, or shout back at an enemy taunting you nearby. It wears off once the fighting stops. A higher Stress Resistance skill makes it build up slower and hit softer.",
                Section, "Enabled");
            BuildUp = config.Bind(Section, "Build Up", 1f, new ConfigDescription("How fast stress builds up. 1 is the default.", new AcceptableValueRange<float>(0.25f, 3f)));
            Voice = config.Bind(Section, "Call Outs", true, "Let your character call out and shout back at taunts on their own while stressed.");
            Breathing = config.Bind(Section, "Heavy Breathing", true, "Breathe heavily while stressed, even with full stamina.");
            LogStress = config.Bind(Section, "Log Stress", new KeyboardShortcut(KeyCode.None), "Prints your stress level to the BepInEx log.");
            ConfigHelper.Requires(Enabled, BuildUp, Voice, Breathing, LogStress);

            new GameStartedPatch().Enable();
            new ShotPatch().Enable();
            new HurtPatch().Enable();
            new SayPatch().Enable();
            new BreathPatch().Enable();
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
                    if (__instance.GetComponent<StressRunner>() == null)
                    {
                        __instance.gameObject.AddComponent<StressRunner>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Stress] Could not start stress: {e}");
                }
            }
        }

        private class ShotPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BallisticsCalculator), nameof(BallisticsCalculator.CreateShot));
            }

            [PatchPostfix]
            private static void Postfix(Vector3 origin, Vector3 direction, string player)
            {
                if (Enabled.Value)
                {
                    StressRunner.Instance?.Shot(origin, direction, player);
                }
            }
        }

        private class HurtPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));
            }

            [PatchPostfix]
            private static void Postfix(ActiveHealthController __instance, float damage, DamageInfo damageInfo)
            {
                if (!Enabled.Value || damage <= 0f || StressRunner.Instance == null)
                {
                    return;
                }
                Player local = Singleton<GameWorld>.Instance?.MainPlayer;
                if (local == null || !ReferenceEquals(local.ActiveHealthController, __instance))
                {
                    return;
                }
                if (damageInfo.DamageType.IsEnemyDamage() || damageInfo.DamageType == EDamageType.Explosion)
                {
                    StressRunner.Instance.Hurt(damage);
                }
            }
        }

        private class SayPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.Say));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance, EPhraseTrigger phrase)
            {
                if (Enabled.Value && __instance != null && !__instance.IsYourPlayer)
                {
                    StressRunner.Instance?.Heard(__instance, phrase);
                }
            }
        }

        private class BreathPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.PropertyGetter(typeof(PhysicalBase), nameof(PhysicalBase.BreathIsAudible));
            }

            [PatchPostfix]
            private static void Postfix(PhysicalBase __instance, ref bool __result)
            {
                if (__result || !(StressRunner.HeavyBreathing || Overdose.OverdoseRunner.HeavyBreathing) || __instance.HoldingBreath)
                {
                    return;
                }
                Player local = Singleton<GameWorld>.Instance?.MainPlayer;
                if (local != null && ReferenceEquals(local.Physical, __instance))
                {
                    __result = true;
                }
            }
        }
    }
}
