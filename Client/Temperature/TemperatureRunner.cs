using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Weather;
using EFTEffectManager;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mizmii.ReClassic.Temperature
{
    internal sealed class TemperatureRunner : MonoBehaviour
    {
        private const Player.ESpeedLimit ColdLimit = (Player.ESpeedLimit)71;
        private const float BaseSeconds = 480f;
        private const float MaxPerSecond = 0.02f;

        private static TemperatureRunner _instance;

        private Player _player;
        private float _target = BodyTemperature.Normal;
        private float _rate;
        private float _pending;
        private float _nextSample;
        private float _fatigueSeconds;
        private float _drinkCooling;
        private float _wetness;
        private float _rain;
        private float _air = 15f;
        private bool _wading;
        private bool _wet;
        private float _lastStamina = -1f;
        private float _drainLoop;
        private float _damageLoop;
        private float _energyDebt;
        private float _hydrationDebt;
        private float _shownTemperatureRate;
        private float _shownEnergyRate;
        private float _shownHydrationRate;
        private float _speedLimit = 1f;
        private bool _indoor;
        private bool _regenerating;
        public const float RegenHeatPerMinute = 0.4f;
        private bool _warmingByFire;
        private bool _hot;
        private bool _cold;
        private bool _overheating;
        private bool _frostbite;
        private ActiveHealthController.Effect _tremor;
        private ActiveHealthController.Effect _frostbiteEffect;
        private readonly Dictionary<string, float> _parts = new Dictionary<string, float>();

        public static void PassTime(float seconds)
        {
            TemperatureRunner runner = _instance;
            ActiveHealthController health = runner?._player?.ActiveHealthController;
            if (runner == null || health == null || !health.IsAlive || !BodyTemperature.Enabled.Value)
            {
                return;
            }
            float current = health.Temperature.Current;
            float k = 1f - Mathf.Exp(-seconds * Mathf.Max(0.05f, BodyTemperature.Speed.Value) / BaseSeconds);
            float change = (runner._target - current) * k;
            if (Mathf.Abs(change) > 0.005f)
            {
                health.ChangeTemperature(change);
            }
        }

        public static void Drank(float hydration)
        {
            if (_instance != null)
            {
                _instance._drinkCooling = Mathf.Min(2.5f, _instance._drinkCooling + BodyTemperature.DrinkCooling(hydration));
            }
        }

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            Release();
        }

        private void Update()
        {
            try
            {
                Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Temperature] {e.Message}");
                Release();
                enabled = false;
            }
        }

        private void Tick(float dt)
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player != _player)
            {
                Release();
                _player = player;
                _lastStamina = -1f;
                _nextSample = 0f;
            }
            if (player == null || dt <= 0f)
            {
                return;
            }
            ActiveHealthController health = player.ActiveHealthController;
            if (health == null || !health.IsAlive || !BodyTemperature.Enabled.Value)
            {
                Release();
                return;
            }

            if (health.FindActiveEffect<IChronicStaminaFatigue>() != null)
            {
                _fatigueSeconds += dt;
            }
            else
            {
                _fatigueSeconds = 0f;
            }
            _drinkCooling = Mathf.Max(0f, _drinkCooling - dt * _drinkCooling / 170f - dt * 0.001f);
            Soak(health, dt);

            if (Time.time >= _nextSample)
            {
                _nextSample = Time.time + 0.5f;
                _target = Target(player, health, BodyTemperature.LogTemperature.Value.IsDown());
            }
            else if (BodyTemperature.LogTemperature.Value.IsDown())
            {
                Target(player, health, true);
            }

            float current = health.Temperature.Current;
            float seconds = BaseSeconds / Mathf.Max(0.05f, BodyTemperature.Speed.Value);
            if (_target < current && _indoor)
            {
                seconds *= 1.6f;
            }
            if ((BodyTemperature.Normal - current) * (_target - current) > 0f)
            {
                seconds /= 1f + Skills.SkillEffects.Value(Skills.SkillEffects.Temperature);
            }
            if (_target > current && _warmingByFire)
            {
                seconds *= 0.6f;
            }
            float max = MaxPerSecond * BodyTemperature.Speed.Value;
            float perSecond = Mathf.Clamp((_target - current) / seconds, -max, max);
            if (_regenerating && current < 40f)
            {
                perSecond += RegenHeatPerMinute / 60f * BodyTemperature.Speed.Value;
            }
            _rate = Mathf.Lerp(_rate, perSecond, 1f - Mathf.Exp(-dt / 2f));
            _pending += perSecond * dt;
            if (Mathf.Abs(_pending) >= 0.01f)
            {
                health.ChangeTemperature(_pending);
                _pending = 0f;
            }
            ShowRate(health, ref _shownTemperatureRate, Mathf.Abs(_rate) < 0.0002f ? 0f : _rate * 60f, 2);

            float temperature = health.Temperature.Current;
            _hot = temperature >= (_hot ? BodyTemperature.HotAt - 0.1f : BodyTemperature.HotAt);
            _cold = temperature <= (_cold ? BodyTemperature.ColdAt + 0.1f : BodyTemperature.ColdAt);
            _overheating = temperature >= (_overheating ? BodyTemperature.OverheatAt - 0.1f : BodyTemperature.OverheatAt);
            _frostbite = temperature <= (_frostbite ? BodyTemperature.FrostbiteAt + 0.1f : BodyTemperature.FrostbiteAt);

            Show(Effects.CustomEffects.Hot, health, _hot);
            Show(Effects.CustomEffects.Cold, health, _cold);
            Show(Effects.CustomEffects.Overheating, health, _overheating);
            _wet = _wetness >= (_wet ? 0.05f : 0.1f);
            Show(Effects.CustomEffects.Wet, health, _wet);
            Tremors(health, _cold);
            Frostbite(health, _frostbite);
            SlowDown(player, _frostbite ? 0.7f : _cold ? 0.85f : 1f);

            float energyPerMinute = 0f;
            float hydrationPerMinute = 0f;
            if (_cold)
            {
                energyPerMinute += _frostbite ? 1.2f : 0.6f;
            }
            if (_hot)
            {
                hydrationPerMinute += 0.6f;
                ActiveHealthController.ChronicStaminaFatigue fatigue = health.FindActiveEffect<ActiveHealthController.ChronicStaminaFatigue>();
                if (fatigue != null)
                {
                    energyPerMinute += fatigue.Rate * 60f;
                }
            }
            if (_overheating)
            {
                energyPerMinute += 1.5f;
                hydrationPerMinute += 1.5f;
            }
            Drain(health, dt, energyPerMinute, hydrationPerMinute);
            DrainStamina(player, _hot);
            Overheat(health, dt, temperature);
        }

        private float Target(Player player, ActiveHealthController health, bool log)
        {
            float strength = BodyTemperature.Strength.Value;
            GameWorld world = Singleton<GameWorld>.Instance;
            WeatherController weather = WeatherController.Instance;
            IWeatherCurve curve = weather?.WeatherCurve;
            ESeason season = world?.SeasonsController?.Season ?? ESeason.Summer;
            _indoor = player.Environment == EnvironmentType.Indoor;
            float exposure = _indoor ? 0.35f : 1f;

            float air = curve != null ? curve.Temperature : SeasonAir(season);
            float rain = curve != null ? Mathf.Clamp01(curve.Rain) : 0f;
            float cloud = curve != null ? Mathf.Clamp01((curve.Cloudiness + 1f) * 0.5f) : 0.3f;
            float wind = curve != null ? curve.Wind.magnitude : 0f;
            float sunHeight = weather != null ? weather.SunHeight : 0.5f;

            _parts.Clear();
            float airOffset = air < 18f ? (air - 18f) * 0.11f * exposure : air > 27f ? (air - 27f) * 0.12f * (_indoor ? 0.6f : 1f) : 0f;
            _parts["air " + air.ToString("0.0") + "C"] = airOffset;
            _parts["season " + season] = SeasonOffset(season) * (_indoor ? 0.5f : 1f);
            float night = Mathf.Clamp01(-sunHeight * 4f);
            _parts["night"] = -0.8f * night * exposure;
            if (!_indoor && sunHeight > 0f)
            {
                float sun = Mathf.Clamp01(sunHeight * 2f) * (1f - 0.75f * cloud) * (1f - rain);
                if (sun > 0.01f && Shaded(player))
                {
                    sun *= 0.15f;
                }
                _parts["sunlight"] = 1.1f * sun;
            }
            if (!_indoor && rain > 0.01f)
            {
                _parts[air <= 0f ? "snow" : "rain"] = -(air <= 0f ? 0.8f : 1f) * rain;
            }
            if (!_indoor && wind > 3f)
            {
                _parts["wind " + wind.ToString("0.0")] = -1.2f * Mathf.Clamp01((wind - 3f) / 7f) * (air < 20f ? 1f : 0.4f);
            }
            float fire = BodyTemperature.FireHeat(player.Position);
            _warmingByFire = fire > 0.05f;
            if (_warmingByFire)
            {
                _parts["fire"] = 4f * fire;
            }
            if (_fatigueSeconds > 120f)
            {
                _parts["fatigue"] = 0.9f;
            }
            float stamina = player.Physical?.Stamina != null ? Mathf.Clamp01(player.Physical.Stamina.NormalValue) : 1f;
            if (stamina < 0.95f)
            {
                _parts["exertion"] = 1f * (1f - stamina);
            }
            int light = 0;
            int heavy = 0;
            bool regenerating = false;
            foreach (IHealthEffect effect in health.GetAllActiveEffects())
            {
                if (effect is IHeavyBleeding)
                {
                    heavy++;
                }
                else if (effect is ILightBleeding)
                {
                    light++;
                }
                else if (effect is IHealthBoost)
                {
                    regenerating = true;
                }
                else if (effect is ActiveHealthController.Stimulator stimulator && stimulator.ActiveBuffs != null)
                {
                    foreach (IStimulatorBuff buff in stimulator.ActiveBuffs)
                    {
                        if (buff != null && buff.Active && buff.Settings != null && buff.Settings.BuffType == EStimulatorBuffType.HealthRate && buff.Settings.Value > 0f)
                        {
                            regenerating = true;
                        }
                    }
                }
            }
            _regenerating = regenerating;
            if (regenerating)
            {
                _parts["regenerating"] = 1.5f;
            }
            if (light + heavy > 0)
            {
                _parts["bleeding"] = -Mathf.Min(2.5f, light * 0.5f + heavy * 1.2f);
            }
            float blood = BloodLeft(health);
            if (blood < 0.98f)
            {
                _parts["blood loss"] = -1.5f * (1f - blood);
            }

            float raw = 0f;
            foreach (float value in _parts.Values)
            {
                raw += value;
            }
            float gear = Insulation(player);
            if (gear > 0.01f)
            {
                _parts["gear"] = raw > 0f ? 0.9f * gear : -raw * 0.3f * gear;
            }
            _rain = _indoor ? 0f : rain;
            _air = air;
            _wading = Wading(player);
            if (_wading)
            {
                _parts["water"] = -1.2f;
            }
            if (_wetness > 0.01f)
            {
                _parts["wet " + (_wetness * 100f).ToString("0") + "%"] = -1.4f * _wetness;
            }
            if (raw > 0f && stamina > 0.9f && player.Velocity.magnitude < 0.4f)
            {
                _parts["resting"] = -Mathf.Min(raw, 0.5f);
            }
            float sum = 0f;
            foreach (float value in _parts.Values)
            {
                sum += value;
            }
            sum *= strength;
            if (sum > 0f && _drinkCooling > 0f)
            {
                _parts["drink"] = -Mathf.Min(sum, _drinkCooling);
                sum = Mathf.Max(0f, sum - _drinkCooling);
            }
            float target = Mathf.Clamp(BodyTemperature.Normal + sum, 29f, 40.5f);

            if (log)
            {
                var text = new System.Text.StringBuilder();
                foreach (var pair in _parts)
                {
                    if (Mathf.Abs(pair.Value) > 0.005f)
                    {
                        text.Append($", {pair.Key} {pair.Value:+0.00;-0.00}");
                    }
                }
                Plugin.LogSource.LogInfo($"[Temperature] {health.Temperature.Current:0.00}C heading to {target:0.00}C ({(_indoor ? "indoors" : "outdoors")}, sun height {sunHeight:0.00}, rain {rain:0.00}, cloud {cloud:0.00}, wind {wind:0.0}){text}.");
            }
            return target;
        }

        public float Wetness => _wetness;

        private void Soak(ActiveHealthController health, float dt)
        {
            if (_wading)
            {
                _wetness = Mathf.MoveTowards(_wetness, 1f, dt / 4f);
                return;
            }
            if (_rain > 0.5f)
            {
                _wetness = Mathf.MoveTowards(_wetness, _rain, dt / 90f);
                return;
            }
            if (_rain > 0.05f || _wetness <= 0f)
            {
                return;
            }
            float speed = 1f / 360f;
            if (_indoor)
            {
                speed *= 1.5f;
            }
            if (_warmingByFire)
            {
                speed *= 4f;
            }
            if (health.Temperature.Current >= BodyTemperature.HotAt)
            {
                speed *= 1.5f;
            }
            if (_air > 20f)
            {
                speed *= 1.5f;
            }
            else if (_air < 0f)
            {
                speed *= 0.5f;
            }
            _wetness = Mathf.Max(0f, _wetness - speed * dt);
        }

        private static bool Wading(Player player)
        {
            var obstacles = player.MovementContext?._enteredObstacles;
            if (obstacles == null)
            {
                return false;
            }
            foreach (var obstacle in obstacles)
            {
                if (obstacle != null && obstacle.HasSwampSpeedLimit)
                {
                    return true;
                }
            }
            return false;
        }

        private static float Insulation(Player player)
        {
            InventoryEquipment equipment = player.Equipment;
            if (equipment == null)
            {
                return 0f;
            }
            float total = 0f;
            Item armor = equipment.GetSlot(EquipmentSlot.ArmorVest)?.ContainedItem;
            if (armor != null)
            {
                total += 0.45f;
            }
            Item rig = equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
            if (rig != null)
            {
                total += rig.GetItemComponent<ArmorComponent>() != null ? 0.35f : 0.15f;
            }
            if (equipment.GetSlot(EquipmentSlot.Headwear)?.ContainedItem != null)
            {
                total += 0.2f;
            }
            if (equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem != null)
            {
                total += 0.15f;
            }
            if (equipment.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem != null)
            {
                total += 0.1f;
            }
            return Mathf.Clamp01(total);
        }

        private static float SeasonAir(ESeason season)
        {
            switch (season)
            {
                case ESeason.Summer: return 22f;
                case ESeason.Spring: return 14f;
                case ESeason.SpringEarly: return 6f;
                case ESeason.Autumn: return 10f;
                case ESeason.AutumnLate: return 3f;
                case ESeason.Winter: return -8f;
                default: return 15f;
            }
        }

        private static float SeasonOffset(ESeason season)
        {
            switch (season)
            {
                case ESeason.Summer: return 0.3f;
                case ESeason.Spring: return 0.15f;
                case ESeason.SpringEarly: return 0.05f;
                case ESeason.Autumn: return -0.3f;
                case ESeason.AutumnLate: return -0.45f;
                case ESeason.Winter: return -0.6f;
                default: return 0f;
            }
        }

        private static bool Shaded(Player player)
        {
            if (!TODSkyProvider.IsAvailable || TODSkyProvider.Instance == null)
            {
                return false;
            }
            Vector3 direction = TODSkyProvider.Instance.SunDirection;
            if (direction.y <= 0f)
            {
                return true;
            }
            Vector3 from = player.Position + Vector3.up * 1.6f + direction * 0.3f;
            return Physics.Raycast(from, direction, 400f, LayersMaskController.HighPolyWithTerrainMask);
        }

        private static float BloodLeft(ActiveHealthController health)
        {
            float current = 0f;
            float maximum = 0f;
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                ValueStruct value = health.GetBodyPartHealth(part);
                current += value.Current;
                maximum += value.Maximum;
            }
            return maximum > 0f ? Mathf.Clamp01(current / maximum) : 1f;
        }

        private static void Show(EffectRegistration effect, ActiveHealthController health, bool on)
        {
            if (effect == null)
            {
                return;
            }
            bool shown = effect.FindExisting(health, EBodyPart.Head) != null;
            if (on && !shown)
            {
                effect.Add(health, EBodyPart.Head);
            }
            else if (!on && shown)
            {
                effect.Remove(health, EBodyPart.Head);
            }
        }

        private void Tremors(ActiveHealthController health, bool on)
        {
            bool alive = _tremor != null && _tremor.State != EEffectState.Residued && _tremor.State != EEffectState.Removed;
            if (on && !alive)
            {
                _tremor = health.AddEffect<ActiveHealthController.Tremor>(EBodyPart.Head, 0f);
            }
            else if (!on && alive)
            {
                _tremor.ForceResidue();
                _tremor = null;
            }
        }

        private void Frostbite(ActiveHealthController health, bool on)
        {
            bool alive = _frostbiteEffect != null && _frostbiteEffect.State != EEffectState.Residued && _frostbiteEffect.State != EEffectState.Removed;
            if (on && !alive)
            {
                _frostbiteEffect = health.AddEffect<ActiveHealthController.Frostbite>(EBodyPart.Head, 0f);
            }
            else if (!on && alive)
            {
                _frostbiteEffect.ForceResidue();
                _frostbiteEffect = null;
            }
        }

        private void SlowDown(Player player, float limit)
        {
            MovementContext movement = player?.MovementContext;
            if (movement == null || Mathf.Approximately(limit, _speedLimit))
            {
                return;
            }
            movement.RemoveStateSpeedLimit(ColdLimit);
            if (limit < 1f)
            {
                movement.AddStateSpeedLimit(limit, ColdLimit);
            }
            _speedLimit = limit;
        }

        private void Drain(ActiveHealthController health, float dt, float energyPerMinute, float hydrationPerMinute)
        {
            ShowRate(health, ref _shownEnergyRate, -energyPerMinute, 0);
            ShowRate(health, ref _shownHydrationRate, -hydrationPerMinute, 1);
            _energyDebt += energyPerMinute / 60f * dt;
            _hydrationDebt += hydrationPerMinute / 60f * dt;
            _drainLoop += dt;
            if (_drainLoop < 1f)
            {
                return;
            }
            _drainLoop = 0f;
            if (_energyDebt > 0f)
            {
                health.ChangeEnergy(-_energyDebt);
            }
            if (_hydrationDebt > 0f)
            {
                health.ChangeHydration(-_hydrationDebt);
            }
            _energyDebt = 0f;
            _hydrationDebt = 0f;
        }

        private void DrainStamina(Player player, bool hot)
        {
            Stamina stamina = player.Physical?.Stamina;
            if (stamina == null)
            {
                return;
            }
            float current = stamina.Current;
            if (hot && _lastStamina >= 0f && current < _lastStamina)
            {
                float extra = (_lastStamina - current) * (_overheating ? 0.6f : 0.35f);
                stamina.Current = Mathf.Max(0f, current - extra);
            }
            _lastStamina = stamina.Current;
        }

        private void Overheat(ActiveHealthController health, float dt, float temperature)
        {
            float scale = BodyTemperature.DamageScale.Value;
            if (!_overheating || scale <= 0f)
            {
                _damageLoop = 0f;
                return;
            }
            _damageLoop += dt;
            if (_damageLoop < 5f)
            {
                return;
            }
            _damageLoop -= 5f;
            float severity = Mathf.Clamp(1f + (temperature - BodyTemperature.OverheatAt) / 0.6f, 1f, 2.5f);
            float damage = 0.35f * severity * scale;
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                health.ApplyDamage(part, damage, DamageHelper.Existence);
            }
        }

        private static void ShowRate(ActiveHealthController health, ref float shown, float perMinute, int which)
        {
            if (Mathf.Abs(perMinute - shown) < 0.0001f)
            {
                return;
            }
            float delta = perMinute - shown;
            shown = perMinute;
            switch (which)
            {
                case 0:
                    health._energyRate += delta;
                    break;
                case 1:
                    health._hydrationRate += delta;
                    break;
                default:
                    health._temperatureRate += delta;
                    break;
            }
        }

        private void Release()
        {
            ActiveHealthController health = _player?.ActiveHealthController;
            if (health != null)
            {
                ShowRate(health, ref _shownEnergyRate, 0f, 0);
                ShowRate(health, ref _shownHydrationRate, 0f, 1);
                ShowRate(health, ref _shownTemperatureRate, 0f, 2);
                if (health.IsAlive)
                {
                    Show(Effects.CustomEffects.Hot, health, false);
                    Show(Effects.CustomEffects.Cold, health, false);
                    Show(Effects.CustomEffects.Overheating, health, false);
                    Show(Effects.CustomEffects.Wet, health, false);
                    Tremors(health, false);
                    Frostbite(health, false);
                }
            }
            else
            {
                _shownEnergyRate = 0f;
                _shownHydrationRate = 0f;
                _shownTemperatureRate = 0f;
            }
            if (_player != null)
            {
                SlowDown(_player, 1f);
            }
            _tremor = null;
            _frostbiteEffect = null;
            _hot = _cold = _overheating = _frostbite = _wet = false;
            _pending = 0f;
            _rate = 0f;
        }
    }
}
