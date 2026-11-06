using Audio.ReverbSubsystem;
using Audio.SpatialSystem;
using Audio.SpatialSystem.Utils;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Meta.XR.Acoustics;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class Acoustics
    {
        private enum Space
        {
            SmallRoom,
            LargeRoom,
            Street,
            Open
        }

        private class SpaceSettings
        {
            public ConfigEntry<float> Rt60;
            public ConfigEntry<float> ReverbLevel;
            public ConfigEntry<float> EarlyReflections;
            public ConfigEntry<float> ReverbSend;
        }

        private class SendEntry
        {
            public BaseSpatialAudioSource Spatializer;
            public float BaseReverb;
            public float BaseEarly;
            public float Weight;
        }

        private const float ProbeInterval = 0.4f;
        private const float ProbeRange = 80f;
        private const float ZoneSize = 140f;
        private const float ZoneFade = 15f;
        private const float SendApplyThreshold = 0.25f;
        private const float ZoneApplyThreshold = 0.02f;

        private static readonly float[] Bands = { 250f, 1000f, 4000f };
        private static readonly float[] Rt60BandScale = { 1.15f, 1f, 0.65f };
        private static readonly float[] LevelBandScale = { 1f, 1f, 0.75f };

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _reverbStrength;
        private static ConfigEntry<float> _impactStrength;
        private static ConfigEntry<float> _smoothing;
        private static ConfigEntry<bool> _logSpace;
        private static readonly Dictionary<Space, SpaceSettings> Settings = new Dictionary<Space, SpaceSettings>();

        private static readonly List<SendEntry> Sends = new List<SendEntry>();
        private static readonly Dictionary<BaseSpatialAudioSource, SendEntry> SendLookup = new Dictionary<BaseSpatialAudioSource, SendEntry>();
        private static readonly Vector3[] Directions = BuildDirections();
        private static readonly float[] Weights = new float[4];

        private static float _rt60;
        private static float _level;
        private static float _early;
        private static float _reverbSend;
        private static float _appliedRt60 = float.NaN;
        private static float _appliedLevel = float.NaN;
        private static float _appliedEarly;
        private static float _appliedReverbSend;
        private static IntPtr _zone = IntPtr.Zero;
        private static bool _zoneFailed;
        private static int _mask;

        public static void Enable(ConfigFile config)
        {
            const string section = "Audio - Acoustics";
            _enabled = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Audio", "Acoustics", true, "Makes reverb and reflections react much more strongly to the space you are in, like the older Tarkov audio: long tails in streets and halls, punchy early reflections in small rooms.", section, "Enabled");
            _reverbStrength = config.Bind(section, "Reverb Strength", 1f, new ConfigDescription("Scales the extra reverb tail and level. 0 leaves EFT's reverb untouched.", new AcceptableValueRange<float>(0f, 2f)));
            _impactStrength = config.Bind(section, "Impact Strength", 1f, new ConfigDescription("Scales the extra early reflections and reverb send that make shots hit harder in enclosed spaces.", new AcceptableValueRange<float>(0f, 2f)));
            _smoothing = config.Bind(section, "Transition Time", 0.8f, ConfigHelper.Advanced("Seconds it takes to blend into a new space.", new AcceptableValueRange<float>(0.1f, 5f)));
            _logSpace = config.Bind(section, "Log Space Changes", false, ConfigHelper.Advanced("Logs the detected space and applied values when they change noticeably."));

            BindSpace(config, Space.SmallRoom, "Small Room", 0.25f, 5f, 6f, 2f);
            BindSpace(config, Space.LargeRoom, "Large Interior", 0.9f, 3f, 3f, 4f);
            BindSpace(config, Space.Street, "City Street", 0.8f, 4f, 2f, 4f);
            BindSpace(config, Space.Open, "Open Ground", 0f, 0f, 0f, 0f);

            new SimpleSetPresetPatch().Enable();
            new SuperSetPresetPatch().Enable();

            var host = new GameObject("ReClassic_Acoustics");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<AcousticsDriver>();
        }

        private static void BindSpace(ConfigFile config, Space space, string name, float rt60, float level, float early, float send)
        {
            string section = "Audio - Acoustics - " + name;
            Settings[space] = new SpaceSettings
            {
                Rt60 = config.Bind(section, "Reverb Time Offset", rt60, ConfigHelper.Advanced("Seconds added to the simulated reverb decay (RT60).", new AcceptableValueRange<float>(-1f, 4f))),
                ReverbLevel = config.Bind(section, "Reverb Level Offset", level, ConfigHelper.Advanced("Decibels added to the simulated reverb level.", new AcceptableValueRange<float>(-12f, 12f))),
                EarlyReflections = config.Bind(section, "Early Reflections Boost", early, ConfigHelper.Advanced("Decibels added to every sound's early reflection send. This is what makes shots feel punchy.", new AcceptableValueRange<float>(-12f, 12f))),
                ReverbSend = config.Bind(section, "Reverb Send Boost", send, ConfigHelper.Advanced("Decibels added to every sound's reverb send.", new AcceptableValueRange<float>(-12f, 12f)))
            };
        }

        private static Vector3[] BuildDirections()
        {
            var directions = new List<Vector3>();
            for (int i = 0; i < 16; i++)
            {
                directions.Add(Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward);
            }
            for (int i = 0; i < 8; i++)
            {
                directions.Add(Quaternion.Euler(-40f, i * 45f + 22.5f, 0f) * Vector3.forward);
            }
            directions.Add(Vector3.up);
            return directions.ToArray();
        }

        private static float GroupWeight(BetterAudio.AudioSourceGroupType group)
        {
            switch (group)
            {
                case BetterAudio.AudioSourceGroupType.Gunshots:
                case BetterAudio.AudioSourceGroupType.Grenades:
                case BetterAudio.AudioSourceGroupType.Impacts:
                    return 1f;
                case BetterAudio.AudioSourceGroupType.Weaponry:
                    return 0.7f;
                case BetterAudio.AudioSourceGroupType.Collisions:
                case BetterAudio.AudioSourceGroupType.Environment:
                case BetterAudio.AudioSourceGroupType.InteractiveObjects:
                case BetterAudio.AudioSourceGroupType.Windows:
                case BetterAudio.AudioSourceGroupType.PropagatedExclusive:
                case BetterAudio.AudioSourceGroupType.LightOcclusion:
                    return 0.6f;
                case BetterAudio.AudioSourceGroupType.Character:
                case BetterAudio.AudioSourceGroupType.Speech:
                    return 0.5f;
                case BetterAudio.AudioSourceGroupType.Distant:
                case BetterAudio.AudioSourceGroupType.Lamp:
                case BetterAudio.AudioSourceGroupType.OutEnvironment:
                    return 0.3f;
                default:
                    return 0f;
            }
        }

        private static void Register(BaseSpatialAudioSource spatializer, AudioGroupPreset preset)
        {
            if (spatializer == null || preset == null)
            {
                return;
            }

            float weight = GroupWeight(preset.Type);
            if (!SendLookup.TryGetValue(spatializer, out SendEntry entry))
            {
                entry = new SendEntry { Spatializer = spatializer };
                SendLookup[spatializer] = entry;
                Sends.Add(entry);
            }
            entry.BaseReverb = spatializer.ReverbSendDB;
            entry.BaseEarly = spatializer.EarlyReflectionsSendDB;
            entry.Weight = weight;
            Apply(entry, _appliedEarly, _appliedReverbSend);
        }

        private static void Apply(SendEntry entry, float early, float reverb)
        {
            if (entry.Weight <= 0f)
            {
                return;
            }
            entry.Spatializer.EarlyReflectionsSendDB = entry.BaseEarly + early * entry.Weight;
            entry.Spatializer.ReverbSendDB = entry.BaseReverb + reverb * entry.Weight;
            entry.Spatializer.UpdateParameters();
        }

        private static void ApplySends(float early, float reverb)
        {
            for (int i = Sends.Count - 1; i >= 0; i--)
            {
                SendEntry entry = Sends[i];
                if (entry.Spatializer == null)
                {
                    SendLookup.Remove(entry.Spatializer);
                    Sends.RemoveAt(i);
                    continue;
                }
                Apply(entry, early, reverb);
            }
            _appliedEarly = early;
            _appliedReverbSend = reverb;
        }

        private static Transform Listener()
        {
            if (Singleton<AudioListenerConsistencyManager>.Instantiated)
            {
                return Singleton<AudioListenerConsistencyManager>.Instance.transform;
            }
            Camera camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        private static void Probe(Vector3 origin)
        {
            if (_mask == 0)
            {
                _mask = SoundOcclusionUtils.LowPolyOcclusionMask;
            }

            int horizontalHits = 0;
            float horizontalDistance = 0f;
            int upperHits = 0;
            bool ceiling = false;
            for (int i = 0; i < Directions.Length; i++)
            {
                bool hit = Physics.Raycast(origin, Directions[i], out RaycastHit info, ProbeRange, _mask, QueryTriggerInteraction.Ignore);
                if (i < 16)
                {
                    if (hit)
                    {
                        horizontalHits++;
                        horizontalDistance += info.distance;
                    }
                }
                else if (i < 24)
                {
                    if (hit && info.distance < 25f)
                    {
                        upperHits++;
                    }
                }
                else
                {
                    ceiling = hit && info.distance < 30f;
                }
            }

            float horizontalRatio = horizontalHits / 16f;
            float meanDistance = horizontalHits > 0 ? horizontalDistance / horizontalHits : ProbeRange;
            float enclosure = Mathf.Clamp01((ceiling ? 0.5f : 0f) + 0.5f * (upperHits / 8f));
            float largeness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 12f, meanDistance));
            float streetness = horizontalRatio * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 70f, meanDistance)));

            Weights[(int)Space.SmallRoom] = enclosure * (1f - largeness);
            Weights[(int)Space.LargeRoom] = enclosure * largeness;
            Weights[(int)Space.Street] = (1f - enclosure) * streetness;
            Weights[(int)Space.Open] = Mathf.Max(0f, 1f - Weights[0] - Weights[1] - Weights[2]);
        }

        private static void Blend(float deltaTime)
        {
            float rt60 = 0f, level = 0f, early = 0f, send = 0f;
            foreach (KeyValuePair<Space, SpaceSettings> pair in Settings)
            {
                float w = Weights[(int)pair.Key];
                rt60 += w * pair.Value.Rt60.Value;
                level += w * pair.Value.ReverbLevel.Value;
                early += w * pair.Value.EarlyReflections.Value;
                send += w * pair.Value.ReverbSend.Value;
            }

            float t = 1f - Mathf.Exp(-deltaTime / Mathf.Max(_smoothing.Value, 0.05f));
            _rt60 = Mathf.Lerp(_rt60, rt60 * _reverbStrength.Value, t);
            _level = Mathf.Lerp(_level, level * _reverbStrength.Value, t);
            _early = Mathf.Lerp(_early, early * _impactStrength.Value, t);
            _reverbSend = Mathf.Lerp(_reverbSend, send * _impactStrength.Value, t);
        }

        private static bool EnsureZone()
        {
            if (_zone != IntPtr.Zero)
            {
                return true;
            }
            if (_zoneFailed)
            {
                return false;
            }

            try
            {
                if (MetaXRAcousticNativeInterface.Interface.CreateControlZone(out _zone) != 0 || _zone == IntPtr.Zero)
                {
                    _zone = IntPtr.Zero;
                    _zoneFailed = true;
                    Plugin.LogSource.LogWarning("[Acoustics] Could not create a Meta XR acoustic control zone, extra reverb tails are off. Reflection boosts still work.");
                    return false;
                }
                float half = ZoneSize * 0.5f;
                float fade = ZoneFade / half;
                MetaXRAcousticNativeInterface.Interface.ControlZoneSetBox(_zone, 2f + fade, 2f + fade, 2f + fade);
                MetaXRAcousticNativeInterface.Interface.ControlZoneSetFadeDistance(_zone, fade, fade, fade);
                _appliedRt60 = float.NaN;
                _appliedLevel = float.NaN;
                return true;
            }
            catch (Exception e)
            {
                _zone = IntPtr.Zero;
                _zoneFailed = true;
                Plugin.LogSource.LogWarning($"[Acoustics] Meta XR acoustics are unavailable, extra reverb tails are off: {e.Message}");
                return false;
            }
        }

        private static void UpdateZone(Vector3 position)
        {
            if (!EnsureZone())
            {
                return;
            }

            Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * (ZoneSize * 0.5f));
            MetaXRAcousticNativeInterface.Interface.ControlZoneSetTransform(_zone, in matrix);

            if (float.IsNaN(_appliedRt60) || Mathf.Abs(_rt60 - _appliedRt60) > ZoneApplyThreshold || Mathf.Abs(_level - _appliedLevel) > ZoneApplyThreshold * 10f)
            {
                MetaXRAcousticNativeInterface.Interface.ControlZoneReset(_zone, ControlZoneProperty.RT60);
                MetaXRAcousticNativeInterface.Interface.ControlZoneReset(_zone, ControlZoneProperty.REVERB_LEVEL);
                for (int i = 0; i < Bands.Length; i++)
                {
                    MetaXRAcousticNativeInterface.Interface.ControlZoneSetFrequency(_zone, ControlZoneProperty.RT60, Bands[i], _rt60 * Rt60BandScale[i]);
                    MetaXRAcousticNativeInterface.Interface.ControlZoneSetFrequency(_zone, ControlZoneProperty.REVERB_LEVEL, Bands[i], _level * LevelBandScale[i]);
                }
                _appliedRt60 = _rt60;
                _appliedLevel = _level;
            }
        }

        private static void DestroyZone()
        {
            if (_zone == IntPtr.Zero)
            {
                return;
            }
            try
            {
                MetaXRAcousticNativeInterface.Interface.DestroyControlZone(_zone);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Acoustics] Failed to remove the control zone: {e.Message}");
            }
            _zone = IntPtr.Zero;
        }

        private static void Reset()
        {
            DestroyZone();
            _rt60 = _level = _early = _reverbSend = 0f;
            if (_appliedEarly != 0f || _appliedReverbSend != 0f)
            {
                ApplySends(0f, 0f);
            }
        }

        private class AcousticsDriver : MonoBehaviour
        {
            private float _nextProbe;
            private float _lastProbe;
            private string _lastLogged;

            private void Update()
            {
                try
                {
                    Tick();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Acoustics] Update failed, turning acoustics off for this raid: {e}");
                    Reset();
                    _nextProbe = float.MaxValue;
                }
            }

            private void Tick()
            {
                GameWorld world = Singleton<GameWorld>.Instance;
                bool active = _enabled.Value && world != null && !(world is HideoutGameWorld);
                if (!active)
                {
                    if (_zone != IntPtr.Zero || _appliedEarly != 0f || _appliedReverbSend != 0f)
                    {
                        Reset();
                    }
                    if (world == null)
                    {
                        _nextProbe = 0f;
                    }
                    return;
                }

                if (Time.time < _nextProbe)
                {
                    return;
                }

                Transform listener = Listener();
                if (listener == null)
                {
                    return;
                }

                float delta = _lastProbe > 0f ? Mathf.Min(Time.time - _lastProbe, 1f) : ProbeInterval;
                _lastProbe = Time.time;
                _nextProbe = Time.time + ProbeInterval;

                Probe(listener.position);
                Blend(delta);
                UpdateZone(listener.position);

                if (Mathf.Abs(_early - _appliedEarly) > SendApplyThreshold || Mathf.Abs(_reverbSend - _appliedReverbSend) > SendApplyThreshold)
                {
                    ApplySends(_early, _reverbSend);
                }

                if (_logSpace.Value)
                {
                    string summary = $"small {Weights[0]:F2} large {Weights[1]:F2} street {Weights[2]:F2} open {Weights[3]:F2} | RT60 +{_rt60:F2}s level +{_level:F1}dB ER +{_early:F1}dB send +{_reverbSend:F1}dB";
                    string key = $"{Weights[0]:F1}{Weights[1]:F1}{Weights[2]:F1}{Weights[3]:F1}";
                    if (key != _lastLogged)
                    {
                        _lastLogged = key;
                        Plugin.LogSource.LogInfo("[Acoustics] " + summary);
                    }
                }
            }

            private void OnDestroy()
            {
                Reset();
            }
        }

        private class SimpleSetPresetPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(ReverbSimpleSource), nameof(ReverbSimpleSource.SetPreset));
            }

            [PatchPostfix]
            private static void Postfix(ReverbSimpleSource __instance, AudioGroupPreset preset)
            {
                Register(__instance._spatializer, preset);
            }
        }

        private class SuperSetPresetPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(ReverbSuperSource), nameof(ReverbSuperSource.SetPreset));
            }

            [PatchPostfix]
            private static void Postfix(ReverbSuperSource __instance, AudioGroupPreset preset)
            {
                Register(__instance._spatializerA, preset);
                Register(__instance._spatializerB, preset);
            }
        }
    }
}
