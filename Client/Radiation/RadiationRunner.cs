using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFTEffectManager;
using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mizmii.ReClassic.Radiation
{
    internal sealed class RadiationRunner : MonoBehaviour
    {
        private const float GainPerSecond = 0.8f;
        private const float SickAt = 15f;
        private const float ToxicGainPerSecond = 0.5f;
        private const float ToxicHarmful = 25f;
        private const float ToxicDraining = 50f;
        private const float ToxicShown = 5f;
        private const float MaxInfection = 0.3f;
        private const float MaxVignette = 0.45f;

        private static readonly AccessTools.FieldRef<EffectsController, List<EffectsController.EffectAccumulator>> Accumulators =
            AccessTools.FieldRefAccess<EffectsController, List<EffectsController.EffectAccumulator>>("_effectAccumulators");

        private static RadiationRunner _instance;

        private float _visual;
        private float _infection;
        private float _toxicLoop;
        private float _radiationLoop;
        private bool _infectionOwned;
        private float _nextGearCheck;
        private float _nextSave;
        private bool _hasGeiger;
        private bool _hasAnalyzer;
        private float _nextBeep;
        private AudioClip _beep;
        private bool _maskOn;
        private bool _fullMaskOn;
        private bool _maskDirty = true;
        private bool _glassOwned;
        private bool _vignetteOwned;
        private AudioSource _geigerSource;
        private Player _player;
        private CC_FastVignette _vignette;
        private EffectsController.EffectAccumulator _vignetteAccumulator;

        public static void MaskSettingsChanged()
        {
            if (_instance != null)
            {
                _instance._maskDirty = true;
            }
        }

        private void Awake()
        {
            _instance = this;
            GeigerSound.Load();
            _geigerSource = gameObject.AddComponent<AudioSource>();
            _geigerSource.spatialBlend = 0f;
            _geigerSource.playOnAwake = false;
            _geigerSource.loop = false;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            Meters.End(_player != null && !_player.HealthController.IsAlive);
            SetVignette(0f);
            SetInfection(0f);
            ReleaseGlass();
            MaskOverlay.Hide();
        }

        private void Update()
        {
            try
            {
                RadiationZones.ShareSeed();
                Tick(Time.deltaTime);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Radiation] {e.Message}");
                enabled = false;
            }
        }

        private void Tick(float dt)
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player != _player)
            {
                if (_player != null)
                {
                    Meters.End(!_player.HealthController.IsAlive);
                }
                _player = player;
                _maskDirty = true;
                _vignette = null;
                _vignetteAccumulator = null;
                if (player != null)
                {
                    Meters.Begin(player.ProfileId);
                }
            }
            BotHazards.Tick(dt);
            if (player == null || !player.HealthController.IsAlive)
            {
                FadeVisuals(dt, 0f);
                FadeInfection(dt, 0f, false);
                ReleaseGlass();
                MaskOverlay.Hide();
                return;
            }

            if (Time.time >= _nextGearCheck)
            {
                _nextGearCheck = Time.time + 0.5f;
                _hasGeiger = RadiationZones.HasGeiger(player);
                _hasAnalyzer = RadiationZones.HasAnalyzer(player);
                bool maskOn = RadiationZones.WearsMask(player);
                bool fullMaskOn = RadiationZones.WearsFullMask(player);
#if DEBUG
                maskOn |= Dev.DevTools.ForceMask;
                fullMaskOn |= Dev.DevTools.ForceMask;
#endif
                if (maskOn != _maskOn || fullMaskOn != _fullMaskOn)
                {
                    _maskOn = maskOn;
                    _fullMaskOn = fullMaskOn;
                    _maskDirty = true;
                }
            }
            if (_maskDirty)
            {
                _maskDirty = false;
                ApplyMask(player);
            }
            MaskOverlay.Maintain();

            bool zonesOn = RadiationZones.Enabled.Value && RadiationZones.Active.Count > 0;
            float radiation = 0f;
            float chemical = 0f;
            if (zonesOn)
            {
                Vector3 position = player.Position;
                radiation = RadiationZones.RateAt(position, Hazard.Radiation);
                chemical = RadiationZones.RateAt(position, Hazard.Chemical);
            }
            float takenRadiation = radiation * (1f - RadiationZones.Protection(player, Hazard.Radiation));
            float takenChemical = chemical * (1f - RadiationZones.Protection(player, Hazard.Chemical));
            if (ConfigHelper.Pressed(RadiationZones.LogPosition))
            {
                Vector3 p = player.Position;
                Plugin.LogSource.LogInfo($"[Radiation] {Singleton<GameWorld>.Instance.LocationId} position x {p.x:0.0} y {p.y:0.0} z {p.z:0.0}, radiation {radiation:0.00} (taking {takenRadiation:0.00}), chemicals {chemical:0.00} (taking {takenChemical:0.00}), radiation level {Meters.Radiation.Level:0.0}, toxicity {Meters.Toxicity.Level:0.0}.");
            }

            bool irradiated = takenRadiation > 0.01f;
            if (irradiated)
            {
                Meters.Radiation.Gain(takenRadiation * GainPerSecond * dt);
            }
            Meters.Radiation.Tick(dt, !irradiated);
            bool poisoned = takenChemical > 0.01f && Toxicity.Enabled.Value;
            if (poisoned)
            {
                Meters.Toxicity.Gain(takenChemical * ToxicGainPerSecond * dt);
            }
            Meters.Toxicity.Tick(dt, !poisoned && !Meters.Toxicity.Rising);

            Meters.Radiation.MeasureRate(dt);
            Meters.Toxicity.MeasureRate(dt);

            ActiveHealthController health = player.ActiveHealthController;
            if (health != null)
            {
                if (RadiationZones.Enabled.Value && Meters.Radiation.Level >= Meter.Maximum - 0.01f)
                {
                    Plugin.LogSource.LogInfo("[Radiation] Radiation reached 100, you died.");
                    health.Kill(EDamageType.RadExposure);
                    return;
                }
                if (Toxicity.Enabled.Value && Meters.Toxicity.Level >= Meter.Maximum - 0.01f)
                {
                    Plugin.LogSource.LogInfo("[Toxicity] Toxicity reached 100, you died.");
                    health.Kill(EDamageType.Poison);
                    return;
                }
                RadiationSickness(health, dt);
                ToxicityHarm(health, dt);
            }

            float target = 0f;
            if (RadiationZones.Enabled.Value && RadiationZones.ScreenEffect.Value)
            {
                float exposure = Mathf.Clamp01(takenRadiation * 0.4f + takenChemical * 0.5f);
                float sickness = Mathf.Clamp01((Meters.Radiation.Level - SickAt * 0.5f) / 70f);
                target = Mathf.Clamp01(Mathf.Max(exposure * 0.6f, sickness)) * RadiationZones.ScreenStrength.Value;
            }
            FadeVisuals(dt, target);
            bool zombie = health != null && health.FindActiveEffect<IZombieInfection>() != null;
            float toxic = Meters.Toxicity.Level;
            float infection = Toxicity.Enabled.Value && Toxicity.ScreenEffect.Value && toxic > ToxicHarmful ? Mathf.Lerp(0.08f, MaxInfection, (toxic - ToxicHarmful) / (Meter.Maximum - ToxicHarmful)) : 0f;
            FadeInfection(dt, infection, zombie);

            if (zonesOn && _hasGeiger)
            {
                Click(radiation, dt);
            }
            if (zonesOn && _hasAnalyzer)
            {
                Beep(chemical);
            }
            else
            {
                _nextBeep = 0f;
            }

            if (Time.time >= _nextSave)
            {
                _nextSave = Time.time + 30f;
                Meters.Save();
            }
        }

        private void RadiationSickness(ActiveHealthController health, float dt)
        {
            if (health.FindActiveEffect<ActiveHealthController.RadExposure>(EBodyPart.Stomach) != null)
            {
                health.ResidueEffect<ActiveHealthController.RadExposure>(EBodyPart.Stomach);
            }
            EffectRegistration effect = Effects.CustomEffects.Radiation;
            if (effect == null)
            {
                return;
            }
            float level = Meters.Radiation.Level;
            bool sick = effect.FindExisting(health, EBodyPart.Head) != null;
            bool hurts = RadiationZones.DamageScale.Value > 0f;
            if (!sick && hurts && level >= SickAt)
            {
                sick = effect.Add(health, EBodyPart.Head);
            }
            else if (sick && (level < SickAt * 0.5f || !hurts))
            {
                effect.Remove(health, EBodyPart.Head);
                sick = false;
            }
            if (!sick)
            {
                _radiationLoop = 0f;
                return;
            }
            float loop = Mathf.Max(0.5f, ActiveHealthController.Effect.EffectsSettings.RadExposure.DamageLoopTime);
            _radiationLoop += dt;
            if (_radiationLoop < loop)
            {
                return;
            }
            _radiationLoop -= loop;
            float damage = ActiveHealthController.Effect.EffectsSettings.RadExposure.Damage * 0.2f * RadiationZones.DamageScale.Value * Mathf.Clamp(level / 50f, 0.1f, 2f);
            if (damage <= 0f)
            {
                return;
            }
            foreach (EBodyPart part in HealthHelper.RealBodyParts)
            {
                health.ApplyDamage(part, damage, DamageHelper.RadiationDamage);
            }
        }

        private void ToxicityHarm(ActiveHealthController health, float dt)
        {
            float toxic = Meters.Toxicity.Level;
            float scale = Toxicity.DamageScale.Value;
            EffectRegistration effect = Effects.CustomEffects.Toxicity;
            if (effect != null)
            {
                bool shown = effect.FindExisting(health, EBodyPart.Head) != null;
                bool show = Toxicity.Enabled.Value && toxic >= ToxicShown;
                if (show && !shown)
                {
                    effect.Add(health, EBodyPart.Head);
                }
                else if (!show && shown)
                {
                    effect.Remove(health, EBodyPart.Head);
                }
            }
            if (!Toxicity.Enabled.Value || toxic <= ToxicHarmful || scale <= 0f)
            {
                _toxicLoop = 0f;
                return;
            }
            _toxicLoop += dt;
            if (_toxicLoop < 5f)
            {
                return;
            }
            _toxicLoop -= 5f;
            float severity = Mathf.Clamp01((toxic - ToxicHarmful) / (Meter.Maximum - ToxicHarmful));
            health.ApplyDamage(EBodyPart.Stomach, Mathf.Lerp(0.5f, 2f, severity) * scale, DamageHelper.PoisonDamage);
            if (toxic >= ToxicDraining)
            {
                float drain = Mathf.Lerp(0.4f, 1.2f, (toxic - ToxicDraining) / (Meter.Maximum - ToxicDraining)) * scale;
                health.ChangeEnergy(-drain);
                health.ChangeHydration(-drain);
            }
        }

        private void FadeInfection(float dt, float target, bool zombie)
        {
            _infection = Mathf.MoveTowards(_infection, target, dt * 0.05f);
            if (zombie)
            {
                _infectionOwned = false;
                return;
            }
            SetInfection(_infection);
        }

        private void SetInfection(float value)
        {
            if (!_infectionOwned && value <= 0.001f)
            {
                return;
            }
            Camera camera = CameraManager.Instance?.Camera;
            InfectionEffect effect = camera != null ? camera.GetComponent<InfectionEffect>() : null;
            if (effect == null)
            {
                return;
            }
            effect.EffectValue = value;
            effect.enabled = value > 0.001f;
            _infectionOwned = value > 0.001f;
        }

        private void Click(float radiation, float dt)
        {
            AudioClip[] clips = GeigerSound.Clips;
            if (clips == null || clips.Length == 0 || RadiationZones.GeigerVolume.Value <= 0f || radiation <= 0.005f)
            {
                return;
            }
            float rate = radiation * 35f;
            float chance = rate * dt;
            int guard = 0;
            while (UnityEngine.Random.value < chance && guard++ < 4)
            {
                chance -= 1f;
                AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
                _geigerSource.pitch = UnityEngine.Random.Range(0.96f, 1.02f);
                _geigerSource.PlayOneShot(clip, RadiationZones.GeigerVolume.Value);
            }
        }

        private void Beep(float chemical)
        {
            if (chemical <= 0.005f || RadiationZones.AnalyzerVolume.Value <= 0f)
            {
                _nextBeep = 0f;
                return;
            }
            if (Time.time < _nextBeep)
            {
                return;
            }
            if (_beep == null)
            {
                _beep = GeigerSound.AnalyzerBeep();
            }
            float strength = Mathf.Clamp01(chemical);
            _nextBeep = Time.time + Mathf.Lerp(1.6f, 0.22f, strength);
            _geigerSource.pitch = 1f;
            _geigerSource.PlayOneShot(_beep, RadiationZones.AnalyzerVolume.Value);
        }

        private void FadeVisuals(float dt, float target)
        {
            _visual = Mathf.MoveTowards(_visual, target, dt * (target > _visual ? 0.3f : 0.12f));
            SetVignette(_visual);
        }

        private bool FindVignette()
        {
            if (_vignette != null)
            {
                return true;
            }
            Camera camera = CameraManager.Instance?.Camera;
            if (camera == null)
            {
                return false;
            }
            EffectsController controller = camera.GetComponent<EffectsController>();
            if (controller != null)
            {
                foreach (EffectsController.EffectAccumulator accumulator in Accumulators(controller))
                {
                    if (accumulator is EffectsController.CC_FastVignetteAccumulator vignette)
                    {
                        _vignetteAccumulator = vignette;
                        _vignette = vignette.cc_FastVignette_0;
                        break;
                    }
                }
            }
            if (_vignette == null)
            {
                _vignette = camera.GetComponent<CC_FastVignette>();
            }
            return _vignette != null;
        }

        private void SetVignette(float value)
        {
            if (!_vignetteOwned && value <= 0.001f)
            {
                return;
            }
            if (!FindVignette())
            {
                return;
            }
            if (_vignetteAccumulator != null && _vignetteAccumulator.Enabled)
            {
                _vignetteOwned = false;
                return;
            }
            if (value > 0.001f)
            {
                float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 1.7f) * Mathf.Sin(Time.time * 0.63f);
                _vignette.darkness = value * MaxVignette * pulse;
                ((Behaviour)_vignette).enabled = true;
                _vignetteOwned = true;
            }
            else
            {
                _vignette.darkness = 0f;
                ((Behaviour)_vignette).enabled = false;
                _vignetteOwned = false;
            }
        }

        private static bool FaceShieldDown(Player player)
        {
            FaceShieldComponent shield = player.FaceShieldObserver?.Component ?? player.FaceCoverObserver?.Component;
            return shield != null && (shield.Togglable == null || shield.Togglable.On);
        }

        public static void FaceShieldChanged()
        {
            if (_instance != null)
            {
                _instance._glassOwned = false;
                _instance._maskDirty = true;
            }
        }

        private void ApplyMask(Player player)
        {
            bool effects = RadiationZones.MaskEffects.Value;
            bool full = effects && _fullMaskOn;
            bool overlay = full && RadiationZones.MaskOverlay.Value;
            bool glass = full && RadiationZones.MaskGlass.Value;

            if (overlay)
            {
                MaskOverlay.Show();
            }
            else
            {
                MaskOverlay.Hide();
            }
            if (glass && !FaceShieldDown(player))
            {
                TakeGlass();
            }
            else
            {
                ReleaseGlass();
            }

            FaceShieldComponent shield = player.FaceShieldObserver?.Component;
            EquipmentSlot slot = EquipmentSlot.Headwear;
            if (shield == null)
            {
                shield = player.FaceCoverObserver?.Component;
                slot = EquipmentSlot.FaceCover;
            }
            player.SetupSoundsFromFaceShieldComponent(shield, slot);
        }

        private void TakeGlass()
        {
            CameraManager cameras = CameraManager.Instance;
            if (cameras?.VisorEffect == null || cameras.VisorSwitcher == null)
            {
                return;
            }
            cameras.VisorEffect.SetMask(VisorEffect.EMask.NoMask);
            if (!_glassOwned)
            {
                cameras.VisorSwitcher.Set(true, false);
                _glassOwned = true;
            }
        }

        private void ReleaseGlass()
        {
            if (!_glassOwned)
            {
                return;
            }
            _glassOwned = false;
            CameraManager cameras = CameraManager.Instance;
            if (cameras?.VisorSwitcher == null)
            {
                return;
            }
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || !FaceShieldDown(player))
            {
                cameras.VisorEffect?.SetMask(VisorEffect.EMask.NoMask);
                cameras.VisorSwitcher.Set(false, false);
            }
        }
    }
}
