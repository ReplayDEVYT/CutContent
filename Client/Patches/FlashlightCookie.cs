using BepInEx.Configuration;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static class FlashlightCookie
    {
        private const string Resource = "ReClassic.Resources.flashlightCookie.png";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _size;
        private static Texture2D _source;
        private static Texture2D _cookie;
        private static bool _loadFailed;
        private static readonly Dictionary<Light, Texture> Originals = new Dictionary<Light, Texture>();
        private static readonly List<Light> Dead = new List<Light>();

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Lighting", "Alpha Flashlight Beam", true,
                "Flashlights and weapon lights use the alpha's beam texture, with its soft rings around a bright center. This is your own setting and isn't changed by the host in co-op.",
                "Visuals - Flashlight Beam", "Enabled");
            _enabled.SettingChanged += (sender, args) => ApplyAll();
            _size = config.Bind("Visuals - Flashlight Beam", "Beam Size", 0.7f,
                new ConfigDescription("How much of the light's cone the alpha beam pattern fills. Lower makes the bright center and rings smaller.", new AcceptableValueRange<float>(0.3f, 1f)));
            _size.SettingChanged += (sender, args) =>
            {
                if (_cookie != null)
                {
                    UnityEngine.Object.Destroy(_cookie);
                    _cookie = null;
                }
                ApplyAll();
            };
            new InitPatch().Enable();
        }

        private static Texture2D Source()
        {
            if (_source != null || _loadFailed)
            {
                return _source;
            }
            try
            {
                using (Stream stream = typeof(FlashlightCookie).Assembly.GetManifestResourceStream(Resource))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.LoadImage(memory.ToArray(), false);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    UnityEngine.Object.DontDestroyOnLoad(texture);
                    _source = texture;
                }
            }
            catch (Exception e)
            {
                _loadFailed = true;
                Plugin.LogSource.LogError($"[Flashlight] Could not load the alpha beam texture: {e}");
            }
            return _source;
        }

        private static Texture2D Cookie()
        {
            if (_cookie != null)
            {
                return _cookie;
            }
            Texture2D source = Source();
            if (source == null)
            {
                return null;
            }
            int width = source.width;
            int height = source.height;
            float scale = Mathf.Clamp(_size.Value, 0.3f, 1f);
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = ((y + 0.5f) / height - 0.5f) / scale + 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float u = ((x + 0.5f) / width - 0.5f) / scale + 0.5f;
                    pixels[y * width + x] = u < 0f || u > 1f || v < 0f || v > 1f ? Color.clear : source.GetPixelBilinear(u, v);
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            texture.SetPixels(pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.name = "ReClassic_FlashlightCookie";
            texture.Apply(true, true);
            UnityEngine.Object.DontDestroyOnLoad(texture);
            _cookie = texture;
            return _cookie;
        }

        private static void Apply(Light light)
        {
            if (light == null || light.type != LightType.Spot)
            {
                return;
            }
            if (!Originals.ContainsKey(light))
            {
                Originals[light] = light.cookie;
            }
            if (_enabled.Value)
            {
                Texture2D cookie = Cookie();
                if (cookie != null)
                {
                    light.cookie = cookie;
                }
            }
            else
            {
                light.cookie = Originals[light];
            }
        }

        private static void ApplyAll()
        {
            Dead.Clear();
            foreach (Light light in Originals.Keys)
            {
                if (light == null)
                {
                    Dead.Add(light);
                }
            }
            foreach (Light light in Dead)
            {
                Originals.Remove(light);
            }
            foreach (Light light in new List<Light>(Originals.Keys))
            {
                Apply(light);
            }
        }

        private class InitPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TacticalComboVisualController), nameof(TacticalComboVisualController.Init));
            }

            [PatchPostfix]
            private static void Postfix(TacticalComboVisualController __instance)
            {
                try
                {
                    if (Originals.Count > 2000)
                    {
                        ApplyAll();
                    }
                    foreach (Light light in __instance.GetComponentsInChildren<Light>(true))
                    {
                        Apply(light);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Flashlight] Could not set the beam texture: {e}");
                }
            }
        }
    }
}
