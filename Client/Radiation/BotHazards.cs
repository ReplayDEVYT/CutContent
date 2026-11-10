using BepInEx.Bootstrap;
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

namespace mizmii.ReClassic.Radiation
{
    internal static class BotHazards
    {
        private const string GasMask = "5b432c305acfc40019478128";
        private const float SickAt = 15f;
        private const float ToxicHarmful = 25f;

        private sealed class State
        {
            public float Radiation;
            public float Toxicity;
        }

        private static readonly Dictionary<Player, State> States = new Dictionary<Player, State>();
        private static float _accumulated;

        public static ConfigEntry<bool> Masks;
        public static ConfigEntry<bool> Avoid;
        public static ConfigEntry<bool> Harm;

        public static void Enable(ConfigFile config, ConfigEntry<bool> parent)
        {
            Masks = config.Bind("Radiation", "Bots Spawn With Gas Masks", true, "Bots that spawn inside a radiation or chemical zone put on a GP-5 gas mask.");
            Avoid = config.Bind("Radiation", "Bots Avoid Zones", true, "Bots without a gas mask turn back from radiation and chemical zones and walk out if they end up inside one. Needs BigBrain.");
            Harm = config.Bind("Radiation", "Zones Hurt Bots", true, "Radiation and chemicals hurt bots the same way they hurt you, and can kill them.");
            ConfigHelper.Requires(parent, Masks, Avoid, Harm);
            new SpawnMaskPatch().Enable();
            if (Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain"))
            {
                try
                {
                    RegisterBrain();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not teach bots to avoid zones: {e.Message}");
                }
            }
        }

        private static void RegisterBrain()
        {
            HazardBrain.Register();
        }

        public static void Reset()
        {
            States.Clear();
            _accumulated = 0f;
        }

        public static float Protection(Player player, Hazard hazard)
        {
            return RadiationZones.Protection(player, hazard);
        }

        public static bool Threatened(Player player, float margin, out Zone zone)
        {
            zone = null;
            if (player == null || !RadiationZones.Enabled.Value)
            {
                return false;
            }
            Vector3 position = player.Position;
            float best = float.MaxValue;
            foreach (Zone candidate in RadiationZones.Active)
            {
                if (Protection(player, candidate.Hazard) >= 0.75f)
                {
                    continue;
                }
                if (Mathf.Abs(position.y - candidate.Center.y) > candidate.Height)
                {
                    continue;
                }
                float dx = position.x - candidate.Center.x;
                float dz = position.z - candidate.Center.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                float reach = candidate.Radius + margin;
                if (distance < reach && distance - reach < best)
                {
                    best = distance - reach;
                    zone = candidate;
                }
            }
            return zone != null;
        }

        public static void Tick(float dt)
        {
            if (!RadiationZones.Enabled.Value || !Harm.Value || RadiationZones.Active.Count == 0)
            {
                return;
            }
            _accumulated += dt;
            if (_accumulated < 1f)
            {
                return;
            }
            float step = _accumulated;
            _accumulated = 0f;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return;
            }
            var seen = new HashSet<Player>();
            foreach (Player player in world.AllAlivePlayersList)
            {
                if (player == null || !player.IsAI || player.AIData?.BotOwner == null || player.ActiveHealthController == null || !player.HealthController.IsAlive)
                {
                    continue;
                }
                seen.Add(player);
                if (!States.TryGetValue(player, out State state))
                {
                    state = new State();
                    States[player] = state;
                }
                Vector3 position = player.Position;
                float radiation = RadiationZones.RateAt(position, Hazard.Radiation) * (1f - Protection(player, Hazard.Radiation));
                float chemical = RadiationZones.RateAt(position, Hazard.Chemical) * (1f - Protection(player, Hazard.Chemical));
                state.Radiation = radiation > 0.01f ? Mathf.Min(100f, state.Radiation + radiation * 0.8f * step) : Mathf.Max(0f, state.Radiation - step * 0.5f / 60f);
                state.Toxicity = chemical > 0.01f ? Mathf.Min(100f, state.Toxicity + chemical * 0.5f * step) : Mathf.Max(0f, state.Toxicity - step * 0.5f / 60f);
                if (state.Radiation >= 100f)
                {
                    player.ActiveHealthController.Kill(EDamageType.RadExposure);
                    continue;
                }
                if (state.Toxicity >= 100f && Toxicity.Enabled.Value)
                {
                    player.ActiveHealthController.Kill(EDamageType.Poison);
                    continue;
                }
                Hurt(player.ActiveHealthController, state, step);
            }
            var gone = new List<Player>();
            foreach (Player player in States.Keys)
            {
                if (!seen.Contains(player))
                {
                    gone.Add(player);
                }
            }
            foreach (Player player in gone)
            {
                States.Remove(player);
            }
        }

        private static void Hurt(ActiveHealthController health, State state, float step)
        {
            if (state.Radiation >= SickAt && RadiationZones.DamageScale.Value > 0f)
            {
                float perPart = ActiveHealthController.Effect.EffectsSettings.RadExposure.Damage * 0.1f * RadiationZones.DamageScale.Value * Mathf.Clamp(state.Radiation / 50f, 0.1f, 2f) * step;
                foreach (EBodyPart part in HealthHelper.RealBodyParts)
                {
                    health.ApplyDamage(part, perPart, DamageHelper.RadiationDamage);
                }
            }
            if (state.Toxicity > ToxicHarmful && Toxicity.Enabled.Value && Toxicity.DamageScale.Value > 0f)
            {
                float severity = Mathf.Clamp01((state.Toxicity - ToxicHarmful) / 75f);
                health.ApplyDamage(EBodyPart.Stomach, Mathf.Lerp(0.1f, 0.4f, severity) * Toxicity.DamageScale.Value * step, DamageHelper.PoisonDamage);
            }
        }

        private static void PutOnMask(Profile profile)
        {
            InventoryEquipment equipment = profile?.Inventory?.Equipment;
            if (equipment == null || !Singleton<ItemFactory>.Instantiated)
            {
                return;
            }
            Slot face = equipment.GetSlot(EquipmentSlot.FaceCover);
            if (face == null)
            {
                return;
            }
            if (face.ContainedItem != null && RadiationZones.WearsFullMask(face.ContainedItem))
            {
                return;
            }
            Item mask = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), GasMask, null);
            if (mask == null)
            {
                return;
            }
            if (face.ContainedItem != null)
            {
                face.RemoveItemWithoutRestrictions();
            }
            Slot head = equipment.GetSlot(EquipmentSlot.Headwear);
            if (head?.ContainedItem?.Template is EquipmentTemplate headTemplate && headTemplate.BlocksFaceCover)
            {
                head.RemoveItemWithoutRestrictions();
            }
            if (mask.Template is EquipmentTemplate maskTemplate)
            {
                Slot eyes = equipment.GetSlot(EquipmentSlot.Eyewear);
                if (maskTemplate.BlocksEyewear && eyes?.ContainedItem != null)
                {
                    eyes.RemoveItemWithoutRestrictions();
                }
                if (maskTemplate.BlocksHeadwear && head?.ContainedItem != null)
                {
                    head.RemoveItemWithoutRestrictions();
                }
            }
            face.AddWithoutRestrictions(mask);
        }

        private class SpawnMaskPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BotCreatorClient), nameof(BotCreatorClient.CreateBot));
            }

            [PatchPrefix]
            private static void Prefix(Profile profile, PositionNote bornInfo)
            {
                try
                {
                    if (!RadiationZones.Enabled.Value || !Masks.Value || profile == null || bornInfo == null)
                    {
                        return;
                    }
                    RadiationZones.EnsureBuilt();
                    Vector3 position = bornInfo.position;
                    if (RadiationZones.RateAt(position, Hazard.Radiation) + RadiationZones.RateAt(position, Hazard.Chemical) <= 0f)
                    {
                        return;
                    }
                    PutOnMask(profile);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not give a bot a gas mask: {e.Message}");
                }
            }
        }
    }
}
