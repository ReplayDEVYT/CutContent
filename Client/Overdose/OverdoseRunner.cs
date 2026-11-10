using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFTEffectManager;
using mizmii.ReClassic.Consciousness;
using mizmii.ReClassic.Effects;
using System;
using UnityEngine;

namespace mizmii.ReClassic.Overdose
{
    internal sealed class OverdoseRunner : MonoBehaviour
    {
        private const float OverdoseAt = 50f;
        private const float SevereAt = 80f;
        private const float Fatal = 100f;
        private const float WearOffPerSecond = 0.1f;
        private const float AbsorbSeconds = 20f;
        private const float SevereSpeed = 0.85f;
        private const Player.ESpeedLimit SevereLimit = (Player.ESpeedLimit)72;

        public static OverdoseRunner Instance { get; private set; }
        public static bool HeavyBreathing { get; private set; }
        public static bool Dying { get; private set; }

        public float Level { get; private set; }

        private Player _player;
        private float _pending;
        private float _clearing;
        private float _thoraxLoop;
        private float _nextHold;
        private bool _overdosed;
        private bool _severe;
        private bool _limping;
        private bool _slowed;
        private ActiveHealthController.Effect _tremor;
        private ActiveHealthController.Effect _tremor2;
        private ActiveHealthController.Effect _tunnel;
        private ActiveHealthController.Effect _pain;

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

        public void Dose(float level)
        {
            _pending += level;
            Plugin.LogSource.LogInfo($"[Overdose] Took a drug level {level:0} dose, heading to {Level + _pending:0}.");
        }

        public void Antidote(Player player)
        {
            _pending = 0f;
            _clearing = 6f;
            ActiveHealthController health = player.ActiveHealthController;
            ConsciousnessController.Current?.GiveAdrenaline(health, 60f);
            Pressure.PressureRunner.Spike(new Vector2(45f, 25f));
            if (Temperature.BodyTemperature.Enabled != null && Temperature.BodyTemperature.Enabled.Value)
            {
                health.ChangeTemperature(0.8f);
            }
            if (player.Physical != null)
            {
                if (player.Physical.Stamina != null)
                {
                    player.Physical.Stamina.Current = 0f;
                }
                if (player.Physical.HandsStamina != null)
                {
                    player.Physical.HandsStamina.Current = 0f;
                }
            }
            Plugin.LogSource.LogInfo($"[Overdose] Antidote at drug level {Level:0}.");
        }

        private void Update()
        {
            try
            {
                Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Overdose] {e.Message}");
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
                Level = 0f;
                _pending = 0f;
            }
            ActiveHealthController health = player?.ActiveHealthController;
            if (health == null || !health.IsAlive || !Overdose.Enabled.Value || dt <= 0f)
            {
                Release();
                return;
            }

            if (_pending > 0f)
            {
                float absorbed = Mathf.Min(_pending, Mathf.Max(_pending, 5f) * dt / AbsorbSeconds * 2f);
                _pending -= absorbed;
                Level += absorbed;
            }
            if (_clearing > 0f)
            {
                _clearing -= dt;
                Level = Mathf.Max(0f, Level - 20f * dt);
            }
            Level = Mathf.Max(0f, Level - WearOffPerSecond * dt);

            if (Overdose.LogDrugs.Value.IsDown())
            {
                Plugin.LogSource.LogInfo($"[Overdose] Drug level {Level:0.0}, still absorbing {_pending:0.0}.");
            }

            if (Level >= Fatal)
            {
                Plugin.LogSource.LogInfo("[Overdose] Drug level reached 100, your heart stopped.");
                health.Kill(EDamageType.Stimulator);
                return;
            }

            _overdosed = Level >= (_overdosed ? OverdoseAt - 2f : OverdoseAt);
            _severe = _overdosed && Level >= (_severe ? SevereAt - 2f : SevereAt);
            float severity = _overdosed ? Mathf.Clamp01((Level - OverdoseAt) / (Fatal - OverdoseAt)) : 0f;

            Show(health, _overdosed);
            ConsciousnessController.SetStrain("overdose", _overdosed ? 1.5f + 8f * severity : 0f);
            SetBreathing(player, _overdosed, _severe);
            Thorax(health, dt, severity);
            if (_overdosed)
            {
                Cap(player.Physical?.Stamina, 0.5f);
                Cap(player.Physical?.HandsStamina, 0.5f);
            }
            Hold(ref _tremor, () => health.AddEffect<ActiveHealthController.Tremor>(EBodyPart.Head, 0f), _severe);
            Hold(ref _tremor2, () => health.AddEffect<ActiveHealthController.Tremor>(EBodyPart.Chest, 0f), _severe);
            Hold(ref _tunnel, () => health.AddEffect<ActiveHealthController.TunnelVision>(EBodyPart.Head, 0f), _severe);
            Hold(ref _pain, () => health.AddEffect<ActiveHealthController.Pain>(EBodyPart.Chest, 0f), _severe);
            if (_severe)
            {
                Slump(player);
            }
            SlowDown(player, _severe);
            if (Time.time >= _nextHold)
            {
                _nextHold = Time.time + 0.25f;
                Limp(player, health, _severe);
            }
        }

        private static void Show(ActiveHealthController health, bool on)
        {
            EffectRegistration effect = CustomEffects.Overdose;
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

        private static void Hold(ref ActiveHealthController.Effect effect, Func<ActiveHealthController.Effect> add, bool on)
        {
            bool alive = effect != null && effect.State != EEffectState.Residued && effect.State != EEffectState.Removed;
            if (on && !alive)
            {
                effect = add();
            }
            else if (!on && alive)
            {
                effect.ForceResidue();
                effect = null;
            }
        }

        private static void Cap(Stamina stamina, float fraction)
        {
            if (stamina == null)
            {
                return;
            }
            float cap = (float)stamina.TotalCapacity * fraction;
            if (stamina.Current > cap)
            {
                stamina.Current = cap;
            }
        }

        private void SetBreathing(Player player, bool heavy, bool dying)
        {
            if (heavy == HeavyBreathing && dying == Dying)
            {
                return;
            }
            HeavyBreathing = heavy;
            Dying = dying;
            try
            {
                player.HeavyBreath = false;
                player.method_36();
                player.UpdateBreathStatus();
            }
            catch (Exception)
            {
            }
        }

        private void Thorax(ActiveHealthController health, float dt, float severity)
        {
            float scale = Overdose.DamageScale.Value;
            if (!_overdosed || scale <= 0f)
            {
                _thoraxLoop = 0f;
                return;
            }
            _thoraxLoop += dt;
            if (_thoraxLoop < 5f)
            {
                return;
            }
            _thoraxLoop -= 5f;
            health.ApplyDamage(EBodyPart.Chest, Mathf.Lerp(0.5f, 2.5f, severity) * scale, DamageHelper.Existence);
            if (_severe)
            {
                float whole = Mathf.Lerp(0.6f, 1.6f, Mathf.InverseLerp(SevereAt, Fatal, Level)) * scale;
                foreach (EBodyPart part in HealthHelper.RealBodyParts)
                {
                    if (part != EBodyPart.Chest && health.IsAlive)
                    {
                        health.ApplyDamage(part, part == EBodyPart.Head ? whole * 0.3f : whole, DamageHelper.Existence);
                    }
                }
            }
        }

        private static void Slump(Player player)
        {
            MovementContext movement = player.MovementContext;
            if (movement == null || player.IsInPronePose)
            {
                return;
            }
            float step = EFTHardSettings.Instance != null ? EFTHardSettings.Instance.DELTA_LEVEL : 0.1f;
            float top = Mathf.Clamp01(1f - step * 2f);
            if (movement.PoseLevel > top + 0.001f)
            {
                movement.SetPoseLevel(top, true);
            }
        }

        private void SlowDown(Player player, bool on)
        {
            MovementContext movement = player?.MovementContext;
            if (movement == null || on == _slowed)
            {
                return;
            }
            movement.RemoveStateSpeedLimit(SevereLimit);
            if (on)
            {
                movement.AddStateSpeedLimit(SevereSpeed, SevereLimit);
            }
            _slowed = on;
        }

        private void Limp(Player player, ActiveHealthController health, bool on)
        {
            MovementContext movement = player.MovementContext;
            if (movement == null)
            {
                return;
            }
            if (on)
            {
                movement.SetPhysicalCondition(EPhysicalCondition.RightLegDamaged, true);
                movement.SetPhysicalCondition(EPhysicalCondition.OnPainkillers, false);
                _limping = true;
            }
            else if (_limping)
            {
                _limping = false;
                bool broken = health.IsBodyPartBroken(EBodyPart.RightLeg) || health.IsBodyPartDestroyed(EBodyPart.RightLeg);
                movement.SetPhysicalCondition(EPhysicalCondition.RightLegDamaged, broken);
                movement.SetPhysicalCondition(EPhysicalCondition.OnPainkillers, health.FindActiveEffect<IPainKiller>() != null);
            }
        }

        private void Release()
        {
            ConsciousnessController.SetStrain("overdose", 0f);
            SlowDown(_player, false);
            ActiveHealthController health = _player?.ActiveHealthController;
            if (health != null && health.IsAlive)
            {
                Show(health, false);
                Hold(ref _tremor, null, false);
                Hold(ref _tremor2, null, false);
                Hold(ref _tunnel, null, false);
                Hold(ref _pain, null, false);
                Limp(_player, health, false);
            }
            _tremor = _tremor2 = _tunnel = _pain = null;
            _overdosed = false;
            _severe = false;
            if (HeavyBreathing || Dying)
            {
                HeavyBreathing = false;
                Dying = false;
                try
                {
                    if (_player != null)
                    {
                        _player.HeavyBreath = false;
                        _player.method_36();
                        _player.UpdateBreathStatus();
                    }
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
