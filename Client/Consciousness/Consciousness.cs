using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFTEffectManager;
using HarmonyLib;
using mizmii.ReClassic.Effects;
using mizmii.ReClassic.Patches;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal static class Consciousness
    {
        public const string AdrenalineInjector = "5c10c8fd86f7743d7d706df3";
        private const string Section = "Gameplay - Unconsciousness";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _dizzinessRate;
        private static ConfigEntry<float> _burstDamage;
        private static ConfigEntry<float> _explosionDamage;
        private static ConfigEntry<float> _minDuration;
        private static ConfigEntry<float> _maxDuration;
        private static ConfigEntry<float> _minSkipMinutes;
        private static ConfigEntry<float> _maxSkipMinutes;
        private static ConfigEntry<float> _despawnRadius;
        private static ConfigEntry<float> _longBlackoutMinutes;
        private static ConfigEntry<float> _simulationSpeed;
        private static ConfigEntry<float> _followerRadius;
        private static ConfigEntry<float> _dropChance;

        public static bool Enabled => _enabled.Value;
        public static float DizzinessRate => _dizzinessRate.Value;
        public static float BurstDamage => _burstDamage.Value;
        public static float ExplosionDamage => _explosionDamage.Value;
        public static float RobberyRadius => _despawnRadius.Value;
        public static float LongBlackoutMinutes => _longBlackoutMinutes.Value;
        public static float SimulationSpeed => _simulationSpeed.Value;
        public static float FollowerRadius => _followerRadius.Value;
        public static float DropChance => _dropChance.Value;
        public static float Cooldown => _cooldown.Value;
        public static float RandomStagger()
        {
            float min = Mathf.Min(_staggerMin.Value, _staggerMax.Value);
            return UnityEngine.Random.Range(min, Mathf.Max(min, _staggerMax.Value));
        }

        public static float CoopWaitSeconds()
        {
            float min = Mathf.Min(_coopMinMinutes.Value, _coopMaxMinutes.Value);
            return UnityEngine.Random.Range(min, Mathf.Max(min, _coopMaxMinutes.Value)) * 60f;
        }
        public static float HandWakeTime => _handWakeTime.Value;

        private static ConfigEntry<float> _cooldown;
        private static ConfigEntry<float> _staggerMin;
        private static ConfigEntry<float> _staggerMax;
        private static ConfigEntry<float> _coopMinMinutes;
        private static ConfigEntry<float> _coopMaxMinutes;
        private static ConfigEntry<float> _handWakeTime;

        public static float RandomDuration()
        {
            float min = Mathf.Min(_minDuration.Value, _maxDuration.Value);
            return UnityEngine.Random.Range(min, Mathf.Max(min, _maxDuration.Value));
        }

        public static float RandomSkipMinutes()
        {
            float min = Mathf.Min(_minSkipMinutes.Value, _maxSkipMinutes.Value);
            return UnityEngine.Random.Range(min, Mathf.Max(min, _maxSkipMinutes.Value));
        }

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Health", "Unconsciousness", true,
                "Pain, untreated bleeding and heavy injuries make you dizzy, and dizziness ends in passing out. A big burst of damage, like losing both legs or a nearby explosion, knocks you out at once. The Adrenaline injector keeps you conscious. While you're out, nearby bots may rob you and time passes.",
                Section, "Unconsciousness");
            _dizzinessRate = config.Bind(Section, "Dizziness Build Up", 1f,
                ConfigHelper.Advanced("How fast pain, bleeding and injuries make you dizzy and pass out.", new AcceptableValueRange<float>(0.1f, 5f)));
            _burstDamage = config.Bind(Section, "Knockout Damage", 80f,
                ConfigHelper.Advanced("Damage taken within two seconds that knocks you out instantly.", new AcceptableValueRange<float>(20f, 300f)));
            _explosionDamage = config.Bind(Section, "Knockout Explosion Damage", 45f,
                ConfigHelper.Advanced("Explosion and fragment damage taken within two seconds that knocks you out instantly.", new AcceptableValueRange<float>(10f, 300f)));
            _minDuration = config.Bind(Section, "Shortest Blackout", 5f,
                ConfigHelper.Advanced("Shortest time in seconds you stay out.", new AcceptableValueRange<float>(2f, 60f)));
            _maxDuration = config.Bind(Section, "Longest Blackout", 15f,
                ConfigHelper.Advanced("Longest time in seconds you stay out.", new AcceptableValueRange<float>(2f, 60f)));
            _minSkipMinutes = config.Bind(Section, "Shortest Time Skip", 10f,
                ConfigHelper.Advanced("Fewest in-game minutes that pass while you're out.", new AcceptableValueRange<float>(0f, 180f)));
            _maxSkipMinutes = config.Bind(Section, "Longest Time Skip", 40f,
                ConfigHelper.Advanced("Most in-game minutes that pass while you're out.", new AcceptableValueRange<float>(0f, 180f)));
            _longBlackoutMinutes = config.Bind(Section, "Long Blackout Minutes", 30f,
                ConfigHelper.Advanced("If you're out for more in-game minutes than this, whoever knocked you out and the bots with them are gone when you wake up. Shorter blackouts let the raid play out around you.", new AcceptableValueRange<float>(0f, 180f)));
            _simulationSpeed = config.Bind(Section, "Blackout Simulation Speed", 8f,
                ConfigHelper.Advanced("How much faster the raid runs while you're out on a short blackout, so bots keep moving around the map. Higher is quicker but heavier on performance.", new AcceptableValueRange<float>(1f, 16f)));
            _followerRadius = config.Bind(Section, "Killer Group Radius", 30f,
                ConfigHelper.Advanced("On a long blackout, bots this many meters from whoever knocked you out leave with them.", new AcceptableValueRange<float>(0f, 150f)));
            _dropChance = config.Bind(Section, "Gear Dropped Chance", 0.35f,
                ConfigHelper.Advanced("Chance that bots searching you leave some of your gear lying on the ground next to you.", new AcceptableValueRange<float>(0f, 1f)));
            _despawnRadius = config.Bind(Section, "Robbery Radius", 60f,
                ConfigHelper.Advanced("Bots within this many meters when you go down search you and may take your gear.", new AcceptableValueRange<float>(0f, 300f)));

            EffectsManager.OnHit += hit =>
            {
                AgonyController.Current?.OnHit(hit);
                ConsciousnessController.Current?.OnHit(hit);
            };
            EffectsManager.LocalBodyPartDestroyed += (player, part, type) => ConsciousnessController.Current?.OnPartDestroyed(part);
            EffectsManager.StimApplied += OnStimApplied;

            _cooldown = config.Bind(Section, "Knockout Cooldown", 120f,
                ConfigHelper.Advanced("Seconds after waking up before you can pass out again. You can still get dizzy.", new AcceptableValueRange<float>(0f, 600f)));
            _coopMinMinutes = config.Bind(Section, "Co-op Shortest Wait", 10f,
                ConfigHelper.Advanced("In Fika, the fewest real minutes you stay out if no teammate wakes you.", new AcceptableValueRange<float>(1f, 60f)));
            _coopMaxMinutes = config.Bind(Section, "Co-op Longest Wait", 20f,
                ConfigHelper.Advanced("In Fika, the most real minutes you stay out if no teammate wakes you.", new AcceptableValueRange<float>(1f, 60f)));
            _staggerMin = config.Bind(Section, "Co-op Next Wake Shortest", 30f,
                ConfigHelper.Advanced("In Fika, when everyone is down, the fewest seconds after the first player wakes before each other player wakes on their own.", new AcceptableValueRange<float>(0f, 900f)));
            _staggerMax = config.Bind(Section, "Co-op Next Wake Longest", 240f,
                ConfigHelper.Advanced("In Fika, when everyone is down, the most seconds after the first player wakes before each other player wakes on their own.", new AcceptableValueRange<float>(0f, 900f)));
            _handWakeTime = config.Bind(Section, "Wake By Hand Time", 5f,
                ConfigHelper.Advanced("In Fika, seconds it takes to wake a teammate by hand.", new AcceptableValueRange<float>(1f, 30f)));

            Robbery.Enable();
            new RaidStartPatch().Enable();
            new DownedInteractionPatch().Enable();
            new AgonyPainPatch().Enable();
            new HeavyPainkillerPatch().Enable();
            new BotAddEnemyPatch().Enable();
            new BotDamagePatch().Enable();
            new FreezeUpdatePatch().Enable();
            new FreezeLateUpdatePatch().Enable();
        }

        private class FreezeUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.ComplexUpdate));
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance, EUpdateQueue queue, float deltaTime)
            {
                if (LiveRagdoll.Frozen.Count == 0 || !LiveRagdoll.Frozen.Contains(__instance))
                {
                    return true;
                }
                if (__instance.UpdateQueue == queue)
                {
                    try
                    {
                        __instance.HealthControllerUpdate(deltaTime / Mathf.Max(1f, Time.timeScale));
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning($"[Unconsciousness] Health update failed while unconscious: {e.Message}");
                    }
                }
                return false;
            }
        }

        private class FreezeLateUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.ComplexLateUpdate));
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance)
            {
                return LiveRagdoll.Frozen.Count == 0 || !LiveRagdoll.Frozen.Contains(__instance);
            }
        }

        public static bool MultiplayerMatch()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return false;
            }
            return world.AllPlayersEverExisted.Count(p => p != null && PlayerKind.IsHuman(p)) > 1;
        }

        private static void OnStimApplied(ActiveHealthController controller, StimUsage usage)
        {
            if (usage != null && AgonyController.HeavyPainkillers.Contains(usage.TemplateId ?? string.Empty))
            {
                AgonyController.Current?.HeavyPainkiller();
            }
            if (usage == null || usage.TemplateId != AdrenalineInjector)
            {
                return;
            }
            float duration = 60f;
            if (usage.Buffs != null && usage.Buffs.Length > 0)
            {
                duration = Mathf.Max(duration, usage.Buffs.Max(b => b.Duration));
            }
            ConsciousnessController.Current?.GiveAdrenaline(controller, duration);
        }

        private class RaidStartPatch : ModulePatch
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
                    Coop.Reset();
                    CoopDriver.Ensure(__instance);
                    Player player = __instance.MainPlayer;
                    if (player != null && player.GetComponent<ConsciousnessController>() == null)
                    {
                        player.gameObject.AddComponent<ConsciousnessController>();
                        player.gameObject.AddComponent<AgonyController>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Unconsciousness] Could not attach to the player: {e}");
                }
            }
        }

        private class BotAddEnemyPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.AddEnemy), new[] { typeof(IPlayer), typeof(EBotEnemyCause) });
            }

            [PatchPrefix]
            private static bool Prefix(IPlayer person, ref bool __result)
            {
                if (person == null)
                {
                    return true;
                }
                Knockout knockout = Knockout.Active;
                bool local = knockout != null && knockout.Player != null && person.ProfileId == knockout.Player.ProfileId;
                if (!local && !(Coop.Down.Count > 0 && Coop.Down.Contains(person.ProfileId)))
                {
                    return true;
                }
                __result = false;
                return false;
            }
        }

        private class BotDamagePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));
            }

            [PatchPrefix]
            private static bool Prefix(ActiveHealthController __instance, ref float __result)
            {
                bool godMode = (Knockout.Active != null && !Knockout.Active.Cooperative && !Knockout.Active.Simulating) || Coop.SleepGodMode;
                if (!godMode || __instance.Player == null || !__instance.Player.IsAI)
                {
                    return true;
                }
                __result = 0f;
                return false;
            }
        }
    }

    internal sealed class ConsciousnessController : MonoBehaviour
    {
        private const float DizzyAt = 40f;
        private const float ClearAt = 25f;
        private const float OutAt = 100f;
        private const float BurstWindow = 2f;

        private float _cooldownUntil;

        public static ConsciousnessController Current { get; private set; }

        private struct Hit
        {
            public float Time;
            public float Damage;
            public bool Explosive;
        }

        private readonly List<Hit> _hits = new List<Hit>();
        private readonly Dictionary<EBodyPart, float> _lostParts = new Dictionary<EBodyPart, float>();
        private Player _player;
        private float _meter;
        private float _nextTick;

        public float Meter => _meter;

        private void Awake()
        {
            _player = GetComponent<Player>();
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
            Knockout.Active?.Abort();
        }

        private bool CanAct()
        {
            return Consciousness.Enabled && _player != null && _player.HealthController != null && _player.HealthController.IsAlive
                && (!Consciousness.MultiplayerMatch() || Coop.Available);
        }

        private bool HasAdrenaline()
        {
            return CustomEffects.Adrenaline?.FindExisting(_player.HealthController, CustomEffects.Any) != null;
        }

        private bool IsDizzy()
        {
            return CustomEffects.Dizzy?.FindExisting(_player.HealthController, CustomEffects.Any) != null;
        }

        public void GiveAdrenaline(ActiveHealthController controller, float duration)
        {
            try
            {
                controller.FindExistingEffect<Adrenaline>(CustomEffects.Any)?.ForceRemove();
                controller.AddEffect<Adrenaline>(CustomEffects.PartFor(controller), null, duration);
                CustomEffects.Dizzy?.Remove(controller, CustomEffects.Any);
                _meter = Mathf.Min(_meter, ClearAt - 1f);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Could not give adrenaline: {e}");
            }
        }

        public void OnHit(HitInfo hit)
        {
            if (hit == null || hit.Player != _player || !CanAct() || Knockout.Active != null || IsOngoing(hit.DamageType))
            {
                return;
            }
            bool explosive = IsExplosive(hit.DamageType);
            if (explosive || !IsVital(hit.BodyPart))
            {
                _hits.Add(new Hit { Time = Time.time, Damage = hit.Damage, Explosive = explosive });
            }
            if (HasAdrenaline())
            {
                return;
            }
            _meter += hit.Damage * 0.35f * Consciousness.DizzinessRate;
            _hits.RemoveAll(h => Time.time - h.Time > BurstWindow);
            float total = _hits.Sum(h => h.Damage);
            float blast = _hits.Where(h => h.Explosive).Sum(h => h.Damage);
            if ((total >= Consciousness.BurstDamage || blast >= Consciousness.ExplosionDamage) && !VitalsCritical())
            {
                PassOut();
            }
        }

        private static bool IsVital(EBodyPart part)
        {
            return part == EBodyPart.Head || part == EBodyPart.Chest || part == EBodyPart.Stomach;
        }

        private bool VitalsCritical()
        {
            IHealthController health = _player.HealthController;
            return EffectsManager.GetHealthFraction(health, EBodyPart.Head) < 0.25f || EffectsManager.GetHealthFraction(health, EBodyPart.Chest) < 0.25f;
        }

        public void OnPartDestroyed(EBodyPart part)
        {
            if (!CanAct() || Knockout.Active != null)
            {
                return;
            }
            _lostParts[part] = Time.time;
            bool legs = _lostParts.TryGetValue(EBodyPart.LeftLeg, out float left) && _lostParts.TryGetValue(EBodyPart.RightLeg, out float right)
                && Mathf.Abs(left - right) <= BurstWindow && Time.time - Mathf.Max(left, right) <= BurstWindow;
            if (legs && !HasAdrenaline() && !VitalsCritical())
            {
                PassOut();
            }
        }

        private static bool IsOngoing(EDamageType type)
        {
            const EDamageType ongoing = EDamageType.LightBleeding | EDamageType.HeavyBleeding | EDamageType.Dehydration | EDamageType.Exhaustion
                | EDamageType.RadExposure | EDamageType.Medicine | EDamageType.Stimulator | EDamageType.Poison | EDamageType.LethalToxin | EDamageType.Existence;
            return (type & ongoing) != 0;
        }

        private static bool IsExplosive(EDamageType type)
        {
            const EDamageType explosive = EDamageType.Explosion | EDamageType.GrenadeFragment | EDamageType.Landmine | EDamageType.Artillery | EDamageType.ThermobaricExplosion;
            return (type & explosive) != 0;
        }

        private void Update()
        {
            if (Time.time < _nextTick)
            {
                return;
            }
            _nextTick = Time.time + 0.5f;
            if (!CanAct() || Knockout.Active != null)
            {
                return;
            }
            try
            {
                Tick(0.5f);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Unconsciousness] Update failed: {e}");
                _nextTick = Time.time + 10f;
            }
        }

        private void Tick(float delta)
        {
            IHealthController health = _player.HealthController;
            if (HasAdrenaline())
            {
                _meter = Mathf.Max(0f, _meter - 6f * delta);
                if (IsDizzy())
                {
                    CustomEffects.Dizzy.Remove(health, CustomEffects.Any);
                }
                return;
            }

            float gain = Strain(health) * Consciousness.DizzinessRate;
            if (gain <= 0.01f)
            {
                _meter = Mathf.Max(0f, _meter - 2f * delta);
            }
            else
            {
                _meter += gain * delta;
            }

            bool dizzy = IsDizzy();
            if (!dizzy && _meter >= DizzyAt)
            {
                CustomEffects.Dizzy?.Add(health, CustomEffects.PartFor(health));
            }
            else if (dizzy && _meter < ClearAt)
            {
                CustomEffects.Dizzy.Remove(health, CustomEffects.Any);
            }

            _meter = Mathf.Min(_meter, OutAt);
            if (_meter >= OutAt)
            {
                PassOut();
            }
        }

        private static float Strain(IHealthController health)
        {
            bool painkiller = EffectsManager.HasPainKiller(health);
            float gain = 0f;
            foreach (IHealthEffect effect in health.GetAllEffects())
            {
                if (effect is IHeavyBleeding)
                {
                    gain += 1.6f;
                }
                else if (effect is ILightBleeding)
                {
                    gain += 0.5f;
                }
                else if (effect is IShock)
                {
                    gain += 9f;
                }
                else if (effect is IAgony)
                {
                    gain += 0.6f;
                }
                else if (!painkiller && (effect is IPain || effect is IFracture))
                {
                    gain += 0.35f;
                }
            }
            float whole = EffectsManager.GetHealthFraction(health);
            if (whole < 0.4f)
            {
                gain += (0.4f - whole) * 12f;
            }
            return gain;
        }

        private bool OnCooldown => Time.time < _cooldownUntil;

        private void PassOut()
        {
            if (Knockout.Active != null || !CanAct() || OnCooldown)
            {
                return;
            }
            _hits.Clear();
            _lostParts.Clear();
            _meter = 0f;
            CustomEffects.Dizzy?.Remove(_player.HealthController, CustomEffects.Any);
            Knockout.Begin(_player, () =>
            {
                _meter = 0f;
                _cooldownUntil = Time.time + Consciousness.Cooldown;
                CustomEffects.Dizzy?.Remove(_player.HealthController, CustomEffects.Any);
            });
        }
    }
}
