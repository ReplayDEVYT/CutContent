using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFTEffectManager;
using HarmonyLib;
using mizmii.ReClassic.Effects;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Consciousness
{
    internal sealed class AgonyController : MonoBehaviour
    {
        private const float BleedingWithBreakSeconds = 30f;
        private const float UntreatedFractureSeconds = 120f;
        private const float BlackedLimbDamage = 40f;
        private const float TremorStrength = 3f;

        public static readonly HashSet<string> HeavyPainkillers = new HashSet<string>
        {
            "544fb3f34bdc2d03748b456a",
            "5c0e530286f7747fa1419862"
        };

        private static readonly EBodyPart[] Limbs =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        public static AgonyController Current { get; private set; }

        private readonly Dictionary<EBodyPart, float> _bleedingWithBreak = new Dictionary<EBodyPart, float>();
        private readonly Dictionary<EBodyPart, float> _untreatedFracture = new Dictionary<EBodyPart, float>();
        private readonly Dictionary<EBodyPart, float> _blackedDamage = new Dictionary<EBodyPart, float>();
        private Player _player;
        private ActiveHealthController.Tremor _tremor;
        private BreathEffector _breath;
        private Vector3 _tremorAmplitude;
        private bool _tremorBoosted;
        private float _nextTick;

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
            RestoreTremor();
        }

        private ActiveHealthController Health => _player != null ? _player.HealthController as ActiveHealthController : null;

        private bool Active => Consciousness.Enabled && Health != null && Health.IsAlive;

        public static bool HasAgony(IHealthController health, EBodyPart part)
        {
            return health != null && health.FindExistingEffect<IAgony>(part) != null;
        }

        public int AgonyCount()
        {
            ActiveHealthController health = Health;
            return health == null ? 0 : health.GetAllEffects().OfType<IAgony>().Count();
        }

        public bool InShock()
        {
            ActiveHealthController health = Health;
            return health != null && health.FindExistingEffect<IShock>(CustomEffects.Any) != null;
        }

        public void OnHit(HitInfo hit)
        {
            if (hit == null || hit.Player != _player || !Active || hit.BodyPart == EBodyPart.Common)
            {
                return;
            }
            if (!EffectsManager.IsPartDestroyed(Health, hit.BodyPart))
            {
                return;
            }
            _blackedDamage.TryGetValue(hit.BodyPart, out float total);
            _blackedDamage[hit.BodyPart] = total + hit.Damage;
        }

        public void HeavyPainkiller()
        {
            ActiveHealthController health = Health;
            if (health == null)
            {
                return;
            }
            foreach (EBodyPart part in Limbs)
            {
                CustomEffects.Agony?.Remove(health, part);
            }
            CustomEffects.Shock?.Remove(health, CustomEffects.Any);
            _bleedingWithBreak.Clear();
            _untreatedFracture.Clear();
            _blackedDamage.Clear();
            UpdateTremor(health, false);
        }

        private void Update()
        {
            if (Time.time < _nextTick)
            {
                return;
            }
            const float step = 0.5f;
            _nextTick = Time.time + step;
            if (!Active)
            {
                return;
            }
            try
            {
                Tick(step);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Agony] Update failed: {e}");
                _nextTick = Time.time + 10f;
            }
        }

        private void Tick(float delta)
        {
            ActiveHealthController health = Health;
            bool painkiller = EffectsManager.HasPainKiller(health);
            foreach (EBodyPart part in Limbs)
            {
                bool destroyed = health.IsBodyPartDestroyed(part);
                bool bleeding = health.FindExistingEffect<IBleeding>(part) != null;
                bool fractured = health.FindExistingEffect<IFracture>(part) != null;
                bool pain = health.FindExistingEffect<IPain>(part) != null;
                bool agony = HasAgony(health, part);

                if (!destroyed)
                {
                    _blackedDamage.Remove(part);
                }

                if (agony)
                {
                    ValueStruct hp = health.GetBodyPartHealth(part);
                    if (!destroyed && !bleeding && !fractured && hp.Maximum > 0f && hp.Current >= hp.Maximum * 0.5f)
                    {
                        CustomEffects.Agony?.Remove(health, part);
                        _bleedingWithBreak.Remove(part);
                        _untreatedFracture.Remove(part);
                        continue;
                    }
                    if (pain)
                    {
                        health.FindExistingEffect<ActiveHealthController.Pain>(part)?.ForceRemove();
                    }
                    continue;
                }

                bool trigger = false;
                trigger |= Accumulate(_bleedingWithBreak, part, bleeding && (fractured || destroyed), delta, BleedingWithBreakSeconds);
                trigger |= Accumulate(_untreatedFracture, part, fractured && pain && !painkiller, delta, UntreatedFractureSeconds);
                trigger |= _blackedDamage.TryGetValue(part, out float shot) && shot >= BlackedLimbDamage;
                if (trigger)
                {
                    StartAgony(health, part);
                }
            }

            int count = health.GetAllEffects().OfType<IAgony>().Count();
            if (count >= 2 && !InShock())
            {
                CustomEffects.Shock?.Add(health, CustomEffects.PartFor(health));
            }
            UpdateTremor(health, count > 0);
        }

        private static bool Accumulate(Dictionary<EBodyPart, float> timers, EBodyPart part, bool condition, float delta, float limit)
        {
            if (!condition)
            {
                timers.Remove(part);
                return false;
            }
            timers.TryGetValue(part, out float time);
            time += delta;
            timers[part] = time;
            return time >= limit;
        }

        private void StartAgony(ActiveHealthController health, EBodyPart part)
        {
            health.FindExistingEffect<ActiveHealthController.Pain>(part)?.ForceRemove();
            CustomEffects.Agony?.Add(health, part);
            _bleedingWithBreak.Remove(part);
            _untreatedFracture.Remove(part);
            _blackedDamage.Remove(part);
        }

        private void UpdateTremor(ActiveHealthController health, bool wanted)
        {
            if (wanted)
            {
                if (_tremor == null || !_tremor.Existing)
                {
                    _tremor = health.AddEffect<ActiveHealthController.Tremor>(CustomEffects.PartFor(health), 0f);
                }
                BoostTremor();
            }
            else
            {
                if (_tremor != null && _tremor.Existing)
                {
                    _tremor.ForceRemove();
                }
                _tremor = null;
                RestoreTremor();
            }
        }

        private void BoostTremor()
        {
            BreathEffector breath = _player.ProceduralWeaponAnimation?.Breath;
            if (breath == null || breath.TremorXRandom == null)
            {
                return;
            }
            if (_tremorBoosted && ReferenceEquals(breath, _breath))
            {
                return;
            }
            RestoreTremor();
            _breath = breath;
            _tremorAmplitude = new Vector3(breath.TremorXRandom.Amplitude, breath.TremorYRandom.Amplitude, breath.TremorZRandom.Amplitude);
            breath.TremorXRandom.Amplitude *= TremorStrength;
            breath.TremorYRandom.Amplitude *= TremorStrength;
            breath.TremorZRandom.Amplitude *= TremorStrength;
            _tremorBoosted = true;
        }

        private void RestoreTremor()
        {
            if (_tremorBoosted && _breath != null)
            {
                _breath.TremorXRandom.Amplitude = _tremorAmplitude.x;
                _breath.TremorYRandom.Amplitude = _tremorAmplitude.y;
                _breath.TremorZRandom.Amplitude = _tremorAmplitude.z;
            }
            _tremorBoosted = false;
            _breath = null;
        }
    }

    internal class AgonyPainPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoPain));
        }

        [PatchPrefix]
        private static bool Prefix(ActiveHealthController __instance, EBodyPart bodyPart)
        {
            return !AgonyController.HasAgony(__instance, bodyPart);
        }
    }

    internal class HeavyPainkillerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoMedEffect));
        }

        [PatchPostfix]
        private static void Postfix(ActiveHealthController __instance, Item item)
        {
            try
            {
                if (item != null && __instance.Player != null && __instance.Player.IsYourPlayer
                    && AgonyController.HeavyPainkillers.Contains(item.TemplateId.ToString()))
                {
                    AgonyController.Current?.HeavyPainkiller();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Agony] Could not apply a heavy painkiller: {e}");
            }
        }
    }
}
