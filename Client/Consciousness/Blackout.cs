using EFT;
using EFT.HealthSystem;
using System;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal static class Blackout
    {
        public static void PassTime(Player player, float seconds)
        {
            ActiveHealthController health = player?.ActiveHealthController;
            if (health == null || !health.IsAlive || seconds <= 1f)
            {
                return;
            }
            float energyBefore = health.Energy.Current;
            float hydrationBefore = health.Hydration.Current;
            float healthBefore = health.GetBodyPartHealth(EBodyPart.Common).Current;

            float energy = Mathf.Min(0f, health._energyRate) / 60f * seconds;
            float hydration = Mathf.Min(0f, health._hydrationRate) / 60f * seconds;
            if (energy < 0f)
            {
                health.ChangeEnergy(energy);
            }
            if (hydration < 0f)
            {
                health.ChangeHydration(hydration);
            }

            var settings = ActiveHealthController.Effect.EffectsSettings;
            foreach (IHealthEffect effect in health.GetAllActiveEffects())
            {
                if (!health.IsAlive)
                {
                    break;
                }
                EffectsSettings.BleedingSettings bleed = effect is IHeavyBleeding ? settings.HeavyBleeding : effect is ILightBleeding ? settings.LightBleeding : null;
                if (bleed == null || bleed.HealthLoopTime <= 0f)
                {
                    continue;
                }
                bool dry = health.Hydration.Current <= 0f;
                float perLoop = dry ? bleed.DamageHealthDehydrated : bleed.DamageHealth;
                float loop = dry && bleed.HealthLoopTimeDehydrated > 0f ? bleed.HealthLoopTimeDehydrated : bleed.HealthLoopTime;
                float damage = perLoop / loop * seconds;
                if (damage > 0f)
                {
                    health.ApplyDamage(effect.BodyPart, damage, effect is IHeavyBleeding ? DamageHelper.HeavyBleedingDamage : DamageHelper.LightBleedingDamage);
                }
            }

            Try(() => Radiation.Meters.Radiation.Tick(seconds, true));
            Try(() => Radiation.Meters.Toxicity.Tick(seconds, !Radiation.Meters.Toxicity.Rising));
            Try(() => Temperature.TemperatureRunner.PassTime(seconds));
            Try(() => Pressure.PressureRunner.CrashFor(player, seconds));
            Try(() => Pressure.PressureRunner.PassTime(seconds));

            float healthAfter = health.IsAlive ? health.GetBodyPartHealth(EBodyPart.Common).Current : 0f;
            Plugin.LogSource.LogInfo($"[Unconsciousness] {seconds / 60f:0.0} raid minutes passed while out: energy {energyBefore:0} -> {health.Energy.Current:0}, hydration {hydrationBefore:0} -> {health.Hydration.Current:0}, health {healthBefore:0} -> {healthAfter:0}.");
        }

        private static void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Unconsciousness] Could not pass time: {e.Message}");
            }
        }
    }
}
