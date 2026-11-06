using BepInEx.Configuration;
using Comfort.Common;
using EFT.Weather;
using HarmonyLib;
using Prism.Utils;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace mizmii.ReClassic.Patches
{
    internal static class AlphaLighting
    {
        private const string Section = "Lighting";
        private const string FactoryDayScene = "Factory_Rework_Day_Scripts";

        private static ConfigEntry<bool> _tonemap;
        private static ConfigEntry<bool> _lighting;
        private static ConfigEntry<float> _strength;
        private static ConfigEntry<bool> _keepAdaptation;
        private static ConfigEntry<bool> _grain;
        private static ConfigEntry<bool> _grade;

        private static readonly Vector3 AlphaTone = new Vector3(6.2f, 0.05f, 0.8f);
        private static readonly Vector3 AlphaSecondaryTone = new Vector3(0.004f, 0.06f, 0f);
        private const float AlphaNoise = 0.07f;
        private const float AlphaDirt = 0.679f;
        private const float AlphaVintageAmount = 0.473f;

        private const float AlphaRayleigh = 2.2442f;
        private const float AlphaMie = 0.3004f;
        private const float AlphaDirectionality = 0.9418f;
        private const float AlphaContrast = 1.5f;
        private const float AlphaDayIntensity = 1.1f;
        private const float AlphaNightIntensity = 0.3f;
        private static readonly Color AlphaDayHigh = new Color(1f, 0.953f, 0.918f);
        private static readonly Color AlphaDayLow = new Color(1f, 0.42f, 0f);
        private static readonly Color AlphaNight = new Color(0.098f, 0.157f, 0.255f);
        private static readonly float[] GradientKeys = { 0f, 0.4f, 0.512f, 0.527f, 0.545f, 0.565f, 0.587f, 1f };

        private static readonly Color AlphaFactorySun = new Color(0.97f, 0.739f, 0.485f);
        private const float AlphaFactorySunIntensity = 2f;
        private static readonly Color AlphaFactorySky = new Color(0.115f, 0.172f, 0.206f);
        private static readonly Color AlphaFactoryEquator = new Color(0.144f, 0.187f, 0.213f);
        private static readonly Color AlphaFactoryGround = new Color(0.152f, 0.132f, 0.097f);
        private const float AlphaFactoryAmbient = 0.6f;
        private const int AlphaFillMask = unchecked((int)4294963198u);

        private struct FillLight
        {
            public string Name;
            public Quaternion Rotation;
            public Color Color;
            public float Intensity;
        }

        private static readonly FillLight[] AlphaFills =
        {
            new FillLight { Name = "fake_GI", Rotation = new Quaternion(0.1448f, 0.5986f, 0.6297f, 0.4734f), Color = new Color(1f, 0.842f, 0.559f), Intensity = 0.2f },
            new FillLight { Name = "fake_skylight", Rotation = new Quaternion(0.3612f, -0.4704f, 0.2193f, 0.7747f), Color = new Color(0.817f, 0.91f, 0.941f), Intensity = 0.3f }
        };

        private struct CameraState
        {
            public bool UseTonemap;
            public TonemapType Type;
            public Vector3 Tone;
            public Vector3 SecondaryTone;
            public bool UseExposure;
            public bool UseNoise;
            public float Noise;
            public float Dirt;
            public bool HasVintage;
            public CC_Vintage.Filter VintageFilter;
            public float VintageAmount;
        }

        private static PrismEffects _camera;
        private static CameraState _original;
        private static bool _tonemapApplied;

        private static Gradient _blended;
        private static Gradient _blendSource;
        private static float _blendStrength = -1f;
        private static float _blendTime;

        private static TOD_Sky _sky;
        private static float _skyDay;
        private static float _skyNight;

        private static Type _amandsType;
        private static object _amands;
        private static ConfigEntry<float> _amandsBrightness;

        public static void Enable(ConfigFile config)
        {
            _tonemap = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Lighting", "Alpha Tonemap", false, "The camera uses the alpha's filmic tonemap and colour grade instead of the modern ones. Eye adaptation is turned off like in the alpha. Works with Amand's Graphics: it replaces Amand's tonemap choice while on, but Amand's brightness slider still applies.", Section, "Alpha Tonemap");
            _lighting = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Lighting", "Alpha Lighting", false, "Sunlight, moonlight and sky scattering follow the alpha's time-of-day settings: whiter midday sun that turns deep orange at the horizon, a stronger sun, darker blue nights and a bluer sky. On Factory (day) the alpha's sun, ambient light and two fill lights are restored. Works with HollywoodFX and Amand's Graphics.", Section, "Alpha Lighting");
            _strength = config.Bind(Section, "Alpha Lighting Strength", 1f, new ConfigDescription("How far Alpha Lighting moves the lighting from the modern values to the alpha's. 1 is the alpha's original settings; lower it if nights are too dark for you.", new AcceptableValueRange<float>(0f, 1f)));
            _keepAdaptation = config.Bind(Section, "Keep Eye Adaptation", false, ConfigHelper.Advanced("With Alpha Tonemap on, keep the modern automatic exposure instead of the alpha's fixed exposure."));

            _tonemap.SettingChanged += (sender, args) => ToggleTonemap();
            _grain = config.Bind(Section, "Alpha Film Grain", false, ConfigHelper.Advanced("With Alpha Tonemap on, add the alpha camera's film grain."));
            _grade = config.Bind(Section, "Alpha Colour Grade", true, ConfigHelper.Advanced("With Alpha Tonemap on, use the alpha's colour grade filter. Amand's Graphics turns the game's colour grade off unless its 'Use BSG CC_Vintage' option is on."));

            _keepAdaptation.SettingChanged += (sender, args) => ReapplyTonemap();
            _grain.SettingChanged += (sender, args) => ReapplyTonemap();
            _grade.SettingChanged += (sender, args) => ReapplyTonemap();
            _lighting.SettingChanged += (sender, args) => LightingChanged();
            _strength.SettingChanged += (sender, args) => LightingChanged();

            new PrismEnablePatch().Enable();
            new PrismPresetPatch().Enable();
            new ToDUpdatePatch().Enable();

            _amandsType = AccessTools.TypeByName("AmandsGraphics.AmandsGraphicsClass");
            if (_amandsType != null)
            {
                Type plugin = AccessTools.TypeByName("AmandsGraphics.AmandsGraphicsPlugin");
                _amandsBrightness = plugin != null ? AccessTools.Property(plugin, "Brightness")?.GetValue(null) as ConfigEntry<float> : null;
                new AmandsUpdatePatch().Enable();
                new AmandsResetPatch().Enable();
                Plugin.LogSource.LogInfo("[Lighting] Amand's Graphics found, Alpha Tonemap will apply after it.");
            }

            var runner = new GameObject("ReClassic.AlphaLighting");
            UnityEngine.Object.DontDestroyOnLoad(runner);
            runner.AddComponent<FactoryLighting>();
        }

        private static float Strength => _lighting.Value ? Mathf.Clamp01(_strength.Value) : 0f;

        private static void ReapplyTonemap()
        {
            if (!_tonemap.Value)
            {
                return;
            }
            RestoreTonemap();
            ApplyTonemap();
        }

        private static void Track(PrismEffects prism)
        {
            if (_camera == prism)
            {
                return;
            }
            _camera = prism;
            _tonemapApplied = false;
            Capture();
        }

        private static void Capture()
        {
            if (_camera == null)
            {
                return;
            }
            _original = new CameraState
            {
                UseTonemap = _camera.useTonemap,
                Type = _camera.tonemapType,
                Tone = _camera.toneValues,
                SecondaryTone = _camera.secondaryToneValues,
                UseExposure = _camera.useExposure,
                UseNoise = _camera.useNoise,
                Noise = _camera.noiseIntensity,
                Dirt = _camera.dirtIntensity
            };
            CC_Vintage vintage = _camera.GetComponent<CC_Vintage>();
            if (vintage != null)
            {
                _original.HasVintage = true;
                _original.VintageFilter = vintage.filter;
                _original.VintageAmount = vintage.amount;
            }
        }

        private static bool AmandsActive(out float brightness)
        {
            brightness = 0f;
            if (_amandsType == null)
            {
                return false;
            }
            if (_amands == null)
            {
                Type plugin = AccessTools.TypeByName("AmandsGraphics.AmandsGraphicsPlugin");
                _amands = plugin != null ? AccessTools.Property(plugin, "AmandsGraphicsClass")?.GetValue(null) : null;
                if (_amands == null)
                {
                    return false;
                }
            }
            object mode = AccessTools.Field(_amandsType, "GraphicsMode")?.GetValue(_amands);
            if (!(mode is bool on) || !on)
            {
                return false;
            }
            if (_amandsBrightness != null)
            {
                brightness = _amandsBrightness.Value - 0.5f;
            }
            return true;
        }

        private static void ApplyTonemap()
        {
            if (_camera == null || !_tonemap.Value)
            {
                return;
            }
            AmandsActive(out float brightness);
            _camera.useTonemap = true;
            _camera.tonemapType = TonemapType.Filmic;
            _camera.toneValues = AlphaTone + new Vector3(0f, brightness, 0f);
            _camera.secondaryToneValues = AlphaSecondaryTone;
            _camera.useExposure = _keepAdaptation.Value && _original.UseExposure;
            _camera.useNoise = _grain.Value;
            _camera.noiseIntensity = _grain.Value ? AlphaNoise : _original.Noise;
            _camera.dirtIntensity = AlphaDirt;
            CC_Vintage vintage = _camera.GetComponent<CC_Vintage>();
            if (vintage != null)
            {
                vintage.filter = _grade.Value ? CC_Vintage.Filter.Owl : _original.VintageFilter;
                vintage.amount = _grade.Value ? AlphaVintageAmount : _original.VintageAmount;
            }
            _tonemapApplied = true;
        }

        private static void RestoreTonemap()
        {
            if (_camera == null || !_tonemapApplied)
            {
                return;
            }
            _tonemapApplied = false;
            _camera.useTonemap = _original.UseTonemap;
            _camera.tonemapType = _original.Type;
            _camera.toneValues = _original.Tone;
            _camera.secondaryToneValues = _original.SecondaryTone;
            _camera.useExposure = _original.UseExposure;
            _camera.useNoise = _original.UseNoise;
            _camera.noiseIntensity = _original.Noise;
            _camera.dirtIntensity = _original.Dirt;
            CC_Vintage vintage = _camera.GetComponent<CC_Vintage>();
            if (vintage != null && _original.HasVintage)
            {
                vintage.filter = _original.VintageFilter;
                vintage.amount = _original.VintageAmount;
            }
        }

        private static void ToggleTonemap()
        {
            if (_tonemap.Value)
            {
                ApplyTonemap();
                return;
            }
            RestoreTonemap();
            if (_amandsType == null || _camera == null)
            {
                return;
            }
            try
            {
                AccessTools.Field(_amandsType, "defaulttoneValues")?.SetValue(null, _original.Tone);
                AccessTools.Field(_amandsType, "defaultsecondaryToneValues")?.SetValue(null, _original.SecondaryTone);
                if (AmandsActive(out _))
                {
                    AccessTools.Method(_amandsType, "UpdateAmandsGraphics")?.Invoke(_amands, null);
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning($"[Lighting] Could not hand the tonemap back to Amand's Graphics: {ex.Message}");
            }
        }

        private static void LightingChanged()
        {
            _blendStrength = -1f;
            if (Strength <= 0f)
            {
                RestoreSky();
            }
            FactoryLighting.Instance?.Refresh();
        }

        private static Color AlphaLightColor(float t)
        {
            if (t <= 0.512f)
            {
                return AlphaNight * Mathf.Clamp01((0.512f - t) / 0.112f);
            }
            float elevation = Mathf.Asin(Mathf.Clamp(t * 2f - 1f, -1f, 1f)) * Mathf.Rad2Deg;
            float day = Mathf.InverseLerp(110f, 80f, 90f - elevation);
            Color color = Color.Lerp(AlphaDayHigh, AlphaDayLow, 1f - day);
            return color * Mathf.Clamp01((day - 0.1f) / 0.9f);
        }

        private static Gradient Blend(Gradient source, float strength)
        {
            if (_blended != null && _blendSource == source && Mathf.Approximately(_blendStrength, strength) && Time.unscaledTime - _blendTime < 2f)
            {
                return _blended;
            }
            var keys = new GradientColorKey[GradientKeys.Length];
            for (int i = 0; i < GradientKeys.Length; i++)
            {
                float t = GradientKeys[i];
                Color modern = source != null ? source.Evaluate(t) : Color.black;
                Color color = Color.Lerp(modern, AlphaLightColor(t), strength);
                color.a = 1f;
                keys[i] = new GradientColorKey(color, t);
            }
            if (_blended == null)
            {
                _blended = new Gradient();
            }
            _blended.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            _blendSource = source;
            _blendStrength = strength;
            _blendTime = Time.unscaledTime;
            return _blended;
        }

        private static void ApplySky(float strength)
        {
            TOD_Sky sky = MonoBehaviourSingleton<TOD_Sky>.Instance;
            if (sky == null)
            {
                return;
            }
            TOD_AtmosphereParameters atmosphere = sky.Atmosphere;
            if (atmosphere != null)
            {
                atmosphere.RayleighMultiplier = Mathf.Lerp(atmosphere.RayleighMultiplier, AlphaRayleigh, strength);
                atmosphere.MieMultiplier = Mathf.Lerp(atmosphere.MieMultiplier, AlphaMie, strength);
                atmosphere.Directionality = Mathf.Lerp(atmosphere.Directionality, AlphaDirectionality, strength);
                atmosphere.Contrast = Mathf.Lerp(atmosphere.Contrast, AlphaContrast, strength);
            }
            if (sky != _sky)
            {
                RestoreSky();
                _sky = sky;
                _skyDay = sky.Day.LightIntensity;
                _skyNight = sky.Night.LightIntensity;
            }
            sky.Day.LightIntensity = Mathf.Lerp(_skyDay, AlphaDayIntensity, strength);
            sky.Night.LightIntensity = Mathf.Lerp(_skyNight, AlphaNightIntensity, strength);
        }

        private static void RestoreSky()
        {
            if (_sky == null)
            {
                return;
            }
            _sky.Day.LightIntensity = _skyDay;
            _sky.Night.LightIntensity = _skyNight;
            if (_amandsType != null)
            {
                try
                {
                    AccessTools.Field(_amandsType, "defaultLightIntensity")?.SetValue(null, _skyNight);
                }
                catch
                {
                }
            }
            _sky = null;
        }

        private class PrismEnablePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(PrismEffects), nameof(PrismEffects.OnEnable));
            }

            [PatchPostfix]
            private static void Postfix(PrismEffects __instance)
            {
                if (__instance.gameObject.name != "FPS Camera")
                {
                    return;
                }
                Track(__instance);
                if (_tonemap.Value)
                {
                    ApplyTonemap();
                }
            }
        }

        private class PrismPresetPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(PrismEffects), nameof(PrismEffects.SetPrismPreset), new[] { typeof(PrismPreset) });
            }

            [PatchPrefix]
            private static void Prefix(PrismEffects __instance)
            {
                if (__instance == _camera)
                {
                    RestoreTonemap();
                }
            }

            [PatchPostfix]
            private static void Postfix(PrismEffects __instance)
            {
                if (__instance != _camera)
                {
                    return;
                }
                Capture();
                if (_tonemap.Value)
                {
                    ApplyTonemap();
                }
            }
        }

        private class AmandsUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(_amandsType, "UpdateAmandsGraphics");
            }

            [PatchPostfix]
            private static void Postfix(object __instance)
            {
                _amands = __instance;
                if (_tonemap.Value)
                {
                    ApplyTonemap();
                }
            }
        }

        private class AmandsResetPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(_amandsType, "ResetGraphics");
            }

            [PatchPostfix]
            private static void Postfix(object __instance)
            {
                _amands = __instance;
                if (_tonemap.Value)
                {
                    ApplyTonemap();
                }
            }
        }

        private class ToDUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(ToDController), nameof(ToDController.Update));
            }

            [PatchPrefix]
            private static void Prefix(ToDController __instance, ref Gradient __state)
            {
                __state = null;
                float strength = Strength;
                if (strength <= 0f)
                {
                    return;
                }
                Gradient source = __instance.LightColor == _blended && _blendSource != null ? _blendSource : __instance.LightColor;
                if (source == null)
                {
                    return;
                }
                __state = source;
                __instance.LightColor = Blend(source, strength);
            }

            [PatchPostfix]
            private static void Postfix(ToDController __instance, Gradient __state)
            {
                if (__state == null)
                {
                    return;
                }
                __instance.LightColor = __state;
                ApplySky(Strength);
            }
        }

        private class FactoryLighting : MonoBehaviour
        {
            public static FactoryLighting Instance;

            private Light _sun;
            private Color _sunColor;
            private float _sunIntensity;
            private Color _writtenColor;
            private float _writtenIntensity;
            private GameObject _fills;
            private LevelSettings _settings;
            private AmbientMode _ambientMode;
            private Color _ambientSky;
            private Color _ambientEquator;
            private Color _ambientGround;
            private float _ambientIntensity;
            private float _nextCheck;

            private void Awake()
            {
                Instance = this;
            }

            public void Refresh()
            {
                _nextCheck = 0f;
                if (Strength <= 0f)
                {
                    Cleanup();
                }
                else if (_sun != null)
                {
                    ApplyAmbient();
                    ApplyFills();
                }
            }

            private void Update()
            {
                if (Time.unscaledTime < _nextCheck)
                {
                    return;
                }
                _nextCheck = Time.unscaledTime + 1f;
                if (Strength <= 0f)
                {
                    Cleanup();
                    return;
                }
                Scene scene = SceneManager.GetSceneByName(FactoryDayScene);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    Forget();
                    return;
                }
                if (_sun != null)
                {
                    return;
                }
                Light sun = FindSun(scene);
                if (sun == null)
                {
                    return;
                }
                _sun = sun;
                _sunColor = sun.color;
                _sunIntensity = sun.intensity;
                _writtenColor = sun.color;
                _writtenIntensity = sun.intensity;
                _settings = Singleton<LevelSettings>.Instantiated ? Singleton<LevelSettings>.Instance : null;
                if (_settings != null)
                {
                    _ambientMode = _settings.AmbientMode;
                    _ambientSky = _settings.SkyColor;
                    _ambientEquator = _settings.EquatorColor;
                    _ambientGround = _settings.GroundColor;
                    _ambientIntensity = _settings.AmbientIntensity;
                }
                _fills = new GameObject("ReClassic Alpha Fill Lights");
                SceneManager.MoveGameObjectToScene(_fills, scene);
                foreach (FillLight fill in AlphaFills)
                {
                    var child = new GameObject(fill.Name);
                    child.transform.SetParent(_fills.transform, false);
                    child.transform.rotation = fill.Rotation;
                    Light light = child.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.shadows = LightShadows.None;
                    light.color = fill.Color;
                    light.cullingMask = AlphaFillMask;
                    light.renderMode = LightRenderMode.Auto;
                }
                ApplyAmbient();
                ApplyFills();
                Plugin.LogSource.LogInfo("[Lighting] Applied the alpha Factory lighting.");
            }

            private void LateUpdate()
            {
                if (_sun == null)
                {
                    return;
                }
                float strength = Strength;
                if (strength <= 0f)
                {
                    return;
                }
                if (_sun.color != _writtenColor)
                {
                    _sunColor = _sun.color;
                }
                if (!Mathf.Approximately(_sun.intensity, _writtenIntensity))
                {
                    _sunIntensity = _sun.intensity;
                }
                _writtenColor = Color.Lerp(_sunColor, AlphaFactorySun, strength);
                _writtenIntensity = Mathf.Lerp(_sunIntensity, AlphaFactorySunIntensity, strength);
                _sun.color = _writtenColor;
                _sun.intensity = _writtenIntensity;
            }

            private static Light FindSun(Scene scene)
            {
                Light best = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Light light in root.GetComponentsInChildren<Light>(false))
                    {
                        if (light.type != LightType.Directional || !light.enabled || light.shadows == LightShadows.None)
                        {
                            continue;
                        }
                        if (best == null || light.intensity > best.intensity)
                        {
                            best = light;
                        }
                    }
                }
                return best;
            }

            private void ApplyFills()
            {
                if (_fills == null)
                {
                    return;
                }
                float strength = Strength;
                Light[] lights = _fills.GetComponentsInChildren<Light>(true);
                for (int i = 0; i < lights.Length && i < AlphaFills.Length; i++)
                {
                    lights[i].intensity = AlphaFills[i].Intensity * strength;
                }
            }

            private void ApplyAmbient()
            {
                if (_settings == null)
                {
                    return;
                }
                float strength = Strength;
                _settings.AmbientMode = AmbientMode.Trilight;
                _settings.SkyColor = Color.Lerp(_ambientSky, AlphaFactorySky, strength);
                _settings.EquatorColor = Color.Lerp(_ambientEquator, AlphaFactoryEquator, strength);
                _settings.GroundColor = Color.Lerp(_ambientGround, AlphaFactoryGround, strength);
                _settings.AmbientIntensity = Mathf.Lerp(_ambientIntensity, AlphaFactoryAmbient, strength);
                _settings.ApplySettings();
            }

            private void Cleanup()
            {
                if (_sun != null)
                {
                    _sun.color = _sunColor;
                    _sun.intensity = _sunIntensity;
                }
                if (_settings != null)
                {
                    _settings.AmbientMode = _ambientMode;
                    _settings.SkyColor = _ambientSky;
                    _settings.EquatorColor = _ambientEquator;
                    _settings.GroundColor = _ambientGround;
                    _settings.AmbientIntensity = _ambientIntensity;
                    _settings.ApplySettings();
                }
                if (_fills != null)
                {
                    Destroy(_fills);
                }
                Forget();
            }

            private void Forget()
            {
                _sun = null;
                _settings = null;
                _fills = null;
            }
        }
    }
}
