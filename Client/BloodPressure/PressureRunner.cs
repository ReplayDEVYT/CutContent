using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFTEffectManager;
using mizmii.ReClassic.Consciousness;
using mizmii.ReClassic.Effects;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace mizmii.ReClassic.Pressure
{
    internal sealed class PressureRunner : MonoBehaviour
    {
        private const float BaseSeconds = 60f;
        private const float HighSystolic = 140f;
        private const float HighDiastolic = 90f;
        private const float SevereHighSystolic = 180f;
        private const float SevereHighDiastolic = 120f;
        private const float LowSystolic = 90f;
        private const float LowDiastolic = 60f;
        private const float SevereLowSystolic = 70f;
        private const float SevereLowDiastolic = 45f;
        private const float StaminaCap = 0.75f;

        public static PressureRunner Instance { get; private set; }

        public float Systolic { get; private set; } = BloodPressure.NormalSystolic;
        public float Diastolic { get; private set; } = BloodPressure.NormalDiastolic;
        public float SystolicRate { get; private set; }
        public float DiastolicRate { get; private set; }
        public bool High { get; private set; }
        public bool Low { get; private set; }
        public bool Severe { get; private set; }
        public bool TowardNormal { get; private set; } = true;

        private Player _player;
        private float _targetSystolic = BloodPressure.NormalSystolic;
        private float _targetDiastolic = BloodPressure.NormalDiastolic;
        private float _nextSample;
        private float _thoraxLoop;
        private float _crashLoop;
        private float _legStamina = -1f;
        private float _armStamina = -1f;
        private ActiveHealthController.Effect _pain;
        private ActiveHealthController.Effect _tremor;
        private readonly List<KeyValuePair<string, Vector2>> _parts = new List<KeyValuePair<string, Vector2>>();

        private Vector2 _spike;

        public static void Spike(Vector2 amount)
        {
            PressureRunner runner = Instance;
            if (runner == null)
            {
                return;
            }
            runner._spike += amount;
            runner.Systolic += amount.x * 0.5f;
            runner.Diastolic += amount.y * 0.5f;
        }

        public static void CrashFor(Player player, float seconds)
        {
            PressureRunner runner = Instance;
            ActiveHealthController health = player?.ActiveHealthController;
            if (runner == null || health == null || !health.IsAlive || seconds <= 0f || !BloodPressure.Enabled.Value)
            {
                return;
            }
            float scale = BloodPressure.DamageScale.Value;
            float severity = runner.Danger;
            if (severity <= 0f || scale <= 0f)
            {
                return;
            }
            float perSecond = Mathf.Lerp(0.15f, 0.9f, severity) * scale;
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                if (!health.IsAlive)
                {
                    break;
                }
                health.ApplyDamage(part, perSecond * seconds * (part == EBodyPart.Head ? 0.3f : 1f), DamageHelper.Existence);
            }
            Plugin.LogSource.LogInfo($"[Blood Pressure] Out for {seconds:0}s at {runner.Systolic:0}/{runner.Diastolic:0}, the body took {perSecond * seconds:0} damage per part.");
        }

        public float Danger
        {
            get
            {
                float high = Mathf.Max((Systolic - SevereHighSystolic + 20f) / 20f, (Diastolic - SevereHighDiastolic + 15f) / 15f);
                float low = Mathf.Max((SevereLowSystolic + 12f - Systolic) / 12f, (SevereLowDiastolic + 8f - Diastolic) / 8f);
                return Mathf.Clamp01(Mathf.Max(high, low));
            }
        }

        public static void PassTime(float seconds)
        {
            PressureRunner runner = Instance;
            if (runner == null || runner._player == null || !BloodPressure.Enabled.Value)
            {
                return;
            }
            float k = 1f - Mathf.Exp(-seconds * Mathf.Max(0.05f, BloodPressure.Speed.Value) / BaseSeconds);
            runner.Systolic += (runner._targetSystolic - runner.Systolic) * k;
            runner.Diastolic += (runner._targetDiastolic - runner.Diastolic) * k;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
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
                Plugin.LogSource.LogWarning($"[Blood Pressure] {e.Message}");
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
                Systolic = BloodPressure.NormalSystolic;
                Diastolic = BloodPressure.NormalDiastolic;
                _nextSample = 0f;
            }
            if (player == null || dt <= 0f)
            {
                return;
            }
            ActiveHealthController health = player.ActiveHealthController;
            if (health == null || !health.IsAlive || !BloodPressure.Enabled.Value)
            {
                Release();
                return;
            }

            bool log = BloodPressure.LogPressure.Value.IsDown();
            if (Time.time >= _nextSample || log)
            {
                _nextSample = Time.time + 0.5f;
                Target(player, health, log);
            }

            float seconds = BaseSeconds / Mathf.Max(0.05f, BloodPressure.Speed.Value);
            float k = 1f - Mathf.Exp(-dt / seconds);
            float oldSystolic = Systolic;
            float oldDiastolic = Diastolic;
            Systolic = Mathf.Clamp(Systolic + (_targetSystolic - Systolic) * k, 50f, 230f);
            Diastolic = Mathf.Clamp(Diastolic + (_targetDiastolic - Diastolic) * k, 30f, 140f);
            float smooth = 1f - Mathf.Exp(-dt / 2f);
            SystolicRate = Mathf.Lerp(SystolicRate, (Systolic - oldSystolic) / dt * 60f, smooth);
            DiastolicRate = Mathf.Lerp(DiastolicRate, (Diastolic - oldDiastolic) / dt * 60f, smooth);
            float distance = Mathf.Abs(Systolic - BloodPressure.NormalSystolic) + Mathf.Abs(Diastolic - BloodPressure.NormalDiastolic);
            float before = Mathf.Abs(oldSystolic - BloodPressure.NormalSystolic) + Mathf.Abs(oldDiastolic - BloodPressure.NormalDiastolic);
            TowardNormal = distance <= before + 0.0001f;

            High = Systolic >= (High ? HighSystolic - 2f : HighSystolic) || Diastolic >= (High ? HighDiastolic - 2f : HighDiastolic);
            Low = !High && (Systolic <= (Low ? LowSystolic + 2f : LowSystolic) || Diastolic <= (Low ? LowDiastolic + 2f : LowDiastolic));
            float highSeverity = Mathf.Clamp01(Mathf.Max((Systolic - HighSystolic) / (SevereHighSystolic - HighSystolic), (Diastolic - HighDiastolic) / (SevereHighDiastolic - HighDiastolic)));
            float lowSeverity = Mathf.Clamp01(Mathf.Max((LowSystolic - Systolic) / (LowSystolic - SevereLowSystolic), (LowDiastolic - Diastolic) / (LowDiastolic - SevereLowDiastolic)));
            Severe = (High && highSeverity >= 1f) || (Low && lowSeverity >= 1f);

            Show(CustomEffects.HighBloodPressure, health, High);
            Show(CustomEffects.LowBloodPressure, health, Low);
            HeadPain(health, High);
            Tremors(health, High);

            float strain = 0f;
            if (Low)
            {
                strain += 2f + 10f * lowSeverity;
            }
            if (health.FindActiveEffect<IPainKiller>() != null)
            {
                strain += 0.3f;
            }
            ConsciousnessController.SetStrain("blood pressure", strain);

            CapStamina(player, High);
            Slump(player, Low);
            Thorax(health, dt, highSeverity);
            Crash(player, health, dt);
        }

        private void Stacked(string name, Vector2 each, int count)
        {
            int used = Mathf.Min(count, 4);
            float resist = 1f - Skills.SkillEffects.Value(Skills.SkillEffects.StimPressure);
            Add(used > 1 ? name + " x" + used : name, each.x * used * resist, each.y * used * resist);
        }

        private void Add(string name, float systolic, float diastolic)
        {
            _parts.Add(new KeyValuePair<string, Vector2>(name, new Vector2(systolic, diastolic)));
        }

        private void Target(Player player, ActiveHealthController health, bool log)
        {
            _parts.Clear();
            int light = 0;
            int heavy = 0;
            int regenerating = 0;
            int temperatureStim = 0;
            Vector2 painkillers = Vector2.zero;
            int painkiller = 0;
            bool adrenaline = false;
            int boosts = 0;
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
                else if (effect is IPainKiller)
                {
                    if (painkiller < 4)
                    {
                        var drug = effect as ActiveHealthController.PainKiller;
                        painkillers += BloodPressure.PainkillerShift(drug?.ItemTemplateId, drug != null ? drug.MaxDuration : 0f);
                    }
                    painkiller++;
                }
                else if (effect is IAdrenaline)
                {
                    adrenaline = true;
                }
                else if (effect is IHealthBoost)
                {
                    regenerating++;
                }
                else if (effect is ActiveHealthController.Stimulator stimulator && stimulator.ActiveBuffs != null)
                {
                    bool regenerates = false;
                    bool heats = false;
                    foreach (IStimulatorBuff buff in stimulator.ActiveBuffs)
                    {
                        if (buff == null || !buff.Active || buff.Settings == null)
                        {
                            continue;
                        }
                        switch (buff.Settings.BuffType)
                        {
                            case EStimulatorBuffType.HealthRate:
                                regenerates |= buff.Settings.Value > 0f;
                                break;
                            case EStimulatorBuffType.BodyTemperature:
                                heats = true;
                                break;
                            case EStimulatorBuffType.MaxStamina:
                            case EStimulatorBuffType.StaminaRate:
                            case EStimulatorBuffType.EnergyRate:
                                if (buff.Settings.Value > 0f)
                                {
                                    boosts++;
                                }
                                break;
                        }
                    }
                    if (regenerates)
                    {
                        regenerating++;
                    }
                    if (heats)
                    {
                        temperatureStim++;
                    }
                }
            }
            if (light + heavy > 0)
            {
                Add("bleeding", -Mathf.Min(40f, light * 6f + heavy * 18f), -Mathf.Min(26f, light * 4f + heavy * 12f));
            }
            float blood = BloodLeft(health);
            if (blood < 0.99f)
            {
                Add("lost health", -(1f - blood) * 40f, -(1f - blood) * 25f);
            }
            if (blood < 0.25f)
            {
                Add("near death", -20f, -12f);
            }
            if (regenerating > 0)
            {
                Stacked("regenerating", BloodPressure.RegenerationShift, regenerating);
            }
            if (temperatureStim > 0)
            {
                Stacked("temperature stimulants", BloodPressure.TemperatureStimShift, temperatureStim);
            }
            if (boosts > 0)
            {
                Stacked("stimulants", BloodPressure.BoostShift, boosts);
            }
            if (painkiller > 0 && painkillers != Vector2.zero)
            {
                Add(painkiller > 1 ? "painkillers x" + Mathf.Min(painkiller, 4) : "painkillers", painkillers.x, painkillers.y);
            }
            if (adrenaline)
            {
                Add("adrenaline", 22f, 12f);
            }
            float temperature = health.Temperature.Current;
            if (temperature >= Temperature.BodyTemperature.OverheatAt)
            {
                Add("overheating", -18f, -12f);
            }
            else if (temperature >= Temperature.BodyTemperature.HotAt)
            {
                Add("heat", -8f, -6f);
            }
            else if (temperature <= Temperature.BodyTemperature.FrostbiteAt)
            {
                Add("freezing", 18f, 10f);
            }
            else if (temperature <= Temperature.BodyTemperature.ColdAt)
            {
                Add("cold", 10f, 6f);
            }
            float stamina = player.Physical?.Stamina != null ? Mathf.Clamp01(player.Physical.Stamina.NormalValue) : 1f;
            if (stamina < 0.95f)
            {
                Add("exertion", 15f * (1f - stamina), 6f * (1f - stamina));
            }
            if (health.Hydration.Current < 20f)
            {
                Add("dehydration", -10f, -6f);
            }

            _spike = Vector2.MoveTowards(_spike, Vector2.zero, 0.5f * _spike.magnitude / 60f + 0.05f);
            if (_spike.sqrMagnitude > 0.25f)
            {
                Add("antidote", _spike.x, _spike.y);
            }
            Vector2 sum = Vector2.zero;
            foreach (var part in _parts)
            {
                sum += part.Value;
            }
            sum *= BloodPressure.Strength.Value;
            _targetSystolic = Mathf.Clamp(BloodPressure.NormalSystolic + sum.x, 50f, 230f);
            _targetDiastolic = Mathf.Clamp(BloodPressure.NormalDiastolic + sum.y, 30f, 140f);

            if (log)
            {
                var text = new StringBuilder();
                foreach (var part in _parts)
                {
                    text.Append($", {part.Key} {part.Value.x:+0;-0}/{part.Value.y:+0;-0}");
                }
                Plugin.LogSource.LogInfo($"[Blood Pressure] {Systolic:0}/{Diastolic:0} heading to {_targetSystolic:0}/{_targetDiastolic:0}{text}.");
            }
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

        private static bool Alive(ActiveHealthController.Effect effect)
        {
            return effect != null && effect.State != EEffectState.Residued && effect.State != EEffectState.Removed;
        }

        private void HeadPain(ActiveHealthController health, bool on)
        {
            if (on && !Alive(_pain))
            {
                _pain = health.AddEffect<ActiveHealthController.Pain>(EBodyPart.Head, 0f);
            }
            else if (!on && Alive(_pain))
            {
                _pain.ForceResidue();
                _pain = null;
            }
        }

        private void Tremors(ActiveHealthController health, bool on)
        {
            if (on && !Alive(_tremor))
            {
                _tremor = health.AddEffect<ActiveHealthController.Tremor>(EBodyPart.Head, 0f);
            }
            else if (!on && Alive(_tremor))
            {
                _tremor.ForceResidue();
                _tremor = null;
            }
        }

        private void CapStamina(Player player, bool on)
        {
            if (!on)
            {
                _legStamina = -1f;
                _armStamina = -1f;
                return;
            }
            _legStamina = Hold(player.Physical?.Stamina, _legStamina);
            _armStamina = Hold(player.Physical?.HandsStamina, _armStamina);
        }

        private static float Hold(Stamina stamina, float last)
        {
            if (stamina == null)
            {
                return -1f;
            }
            float cap = (float)stamina.TotalCapacity * StaminaCap;
            float current = Mathf.Min(stamina.Current, cap);
            if (last >= 0f && current > last)
            {
                current = last;
            }
            if (!Mathf.Approximately(current, stamina.Current))
            {
                stamina.Current = current;
            }
            return current;
        }

        private static void Slump(Player player, bool on)
        {
            MovementContext movement = player.MovementContext;
            if (!on || movement == null || player.IsInPronePose)
            {
                return;
            }
            float step = EFTHardSettings.Instance != null ? EFTHardSettings.Instance.DELTA_LEVEL : 0.1f;
            float top = Mathf.Clamp01(1f - step);
            if (movement.PoseLevel > top + 0.001f)
            {
                movement.SetPoseLevel(top, true);
            }
        }

        private void Thorax(ActiveHealthController health, float dt, float severity)
        {
            float scale = BloodPressure.DamageScale.Value;
            if (!High || scale <= 0f)
            {
                _thoraxLoop = 0f;
                return;
            }
            _thoraxLoop += dt;
            if (_thoraxLoop < 10f)
            {
                return;
            }
            _thoraxLoop -= 10f;
            health.ApplyDamage(EBodyPart.Chest, Mathf.Lerp(0.4f, 1.5f, severity) * scale, DamageHelper.Existence);
        }

        private void Crash(Player player, ActiveHealthController health, float dt)
        {
            float scale = BloodPressure.DamageScale.Value;
            Knockout knockout = Knockout.Active;
            bool out_ = knockout != null && knockout.Player == player;
            float danger = Danger;
            if (!out_ || danger <= 0f || scale <= 0f)
            {
                _crashLoop = 0f;
                return;
            }
            _crashLoop += dt;
            if (_crashLoop < 1f)
            {
                return;
            }
            _crashLoop -= 1f;
            float perSecond = Mathf.Lerp(0.15f, 0.9f, danger) * scale;
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                health.ApplyDamage(part, perSecond * (part == EBodyPart.Head ? 0.3f : 1f), DamageHelper.Existence);
            }
        }

        private void Release()
        {
            ConsciousnessController.SetStrain("blood pressure", 0f);
            ActiveHealthController health = _player?.ActiveHealthController;
            if (health != null && health.IsAlive)
            {
                Show(CustomEffects.HighBloodPressure, health, false);
                Show(CustomEffects.LowBloodPressure, health, false);
                HeadPain(health, false);
                Tremors(health, false);
            }
            _pain = null;
            _tremor = null;
            _legStamina = -1f;
            _armStamina = -1f;
            High = false;
            Low = false;
            Severe = false;
            SystolicRate = 0f;
            DiastolicRate = 0f;
        }
    }
}
