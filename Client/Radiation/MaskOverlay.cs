using BSG.CameraEffects;
using EFT.CameraControl;
using System;
using System.IO;
using UnityEngine;

namespace mizmii.ReClassic.Radiation
{
    internal sealed class MaskOverlay : ITextureMaskHolder
    {
        private const string Resource = "ReClassic.Resources.gasmask_overlay.png";

        private static readonly MaskOverlay Holder = new MaskOverlay();

        private static bool _wanted;
        private static bool _shown;
        private static TextureMask _textureMask;
        private static Texture _own;
        private static bool _ownLoaded;
        private static Texture2D _built;
        private static Color _savedColor;
        private static bool _colorSaved;

        public static string SourceName { get; private set; } = "none";

        public static Texture Texture()
        {
            Texture own = Own();
            if (own != null)
            {
                SourceName = own.name;
                return own;
            }
            Texture built = Build();
            SourceName = built != null ? built.name : "none";
            return built;
        }

        private static float Soft(float distance, float radius, float edge)
        {
            float t = Mathf.Clamp01((distance - (radius - edge)) / (edge * 2f));
            return 1f - t * t * (3f - 2f * t);
        }

        private static Texture2D Build()
        {
            if (_built != null)
            {
                return _built;
            }
            const int width = 512;
            const int height = 256;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dy = y - 128f;
                    float left = Soft(Mathf.Sqrt((x - 128f) * (x - 128f) + dy * dy), 80f, 24f);
                    float right = Soft(Mathf.Sqrt((x - 384f) * (x - 384f) + dy * dy), 80f, 24f);
                    float bx = (x - 256f) / 70f;
                    float by = dy / 40f;
                    float bridge = Soft(Mathf.Sqrt(bx * bx + by * by) * 40f, 40f, 20f);
                    float see = Mathf.Max(Mathf.Max(left, right), bridge);
                    byte dark = (byte)Mathf.RoundToInt((1f - see) * 255f);
                    pixels[y * width + x] = new Color32(0, 0, 0, dark);
                }
            }
            _built = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "ReClassic Gas Mask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            _built.SetPixels32(pixels);
            _built.Apply(false, true);
            return _built;
        }

        public static void Show()
        {
            _wanted = true;
            Maintain();
        }

        public static void Hide()
        {
            _wanted = false;
            if (!_shown)
            {
                return;
            }
            _shown = false;
            try
            {
                if (_textureMask != null)
                {
                    _textureMask.TryToEnable(Holder, false);
                    if (_colorSaved)
                    {
                        _textureMask.Color = _savedColor;
                        _colorSaved = false;
                    }
                    NightVision vision = CameraManager.Instance?.NightVision;
                    if (vision != null && vision.TextureMask == _textureMask)
                    {
                        vision.ApplySettings();
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Gas Masks] Could not take the mask overlay off: {e.Message}");
            }
            _textureMask = null;
        }

        public static void Maintain()
        {
            if (!_wanted)
            {
                return;
            }
            CameraManager cameras = CameraManager.Instance;
            NightVision vision = cameras?.NightVision;
            TextureMask mask = vision?.TextureMask;
            if (mask == null && cameras?.Camera != null)
            {
                mask = cameras.Camera.GetComponent<TextureMask>();
            }
            if (mask == null)
            {
                return;
            }
            if (vision != null && vision.On)
            {
                return;
            }
            Texture texture = Texture();
            if (texture == null)
            {
                return;
            }
            float size = RadiationZones.MaskOverlaySize.Value;
            if (_shown && _textureMask == mask && mask.Mask == texture && mask.enabled && Mathf.Approximately(mask.Size, size))
            {
                return;
            }
            try
            {
                if (_textureMask != null && _textureMask != mask)
                {
                    _textureMask.TryToEnable(Holder, false);
                }
                if (!_colorSaved)
                {
                    _savedColor = mask.Color;
                    _colorSaved = true;
                }
                mask.Color = Color.black;
                mask.Mask = texture;
                mask.Size = size;
                mask.ApplySettings();
                mask.TryToEnable(Holder, true);
                bool first = !_shown;
                _textureMask = mask;
                _shown = true;
                if (first)
                {
                    Plugin.LogSource.LogInfo($"[Gas Masks] Mask overlay on, drawing {texture.name} through the camera's {mask.name} texture mask (size {mask.Size:0.00}, night vision uses {(vision != null ? vision.MaskSize : 0f):0.00}).");
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Gas Masks] Could not put the mask overlay on: {e.Message}");
                _wanted = false;
            }
        }

        private static Texture Own()
        {
            if (_ownLoaded)
            {
                return _own;
            }
            _ownLoaded = true;
            try
            {
                using (Stream stream = typeof(MaskOverlay).Assembly.GetManifestResourceStream(Resource))
                {
                    if (stream == null)
                    {
                        return null;
                    }
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                        if (!texture.LoadImage(memory.ToArray()))
                        {
                            UnityEngine.Object.Destroy(texture);
                            return null;
                        }
                        texture.name = "gasmask_overlay.png";
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                        UnityEngine.Object.DontDestroyOnLoad(texture);
                        _own = texture;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Gas Masks] Could not load gasmask_overlay.png: {e.Message}");
            }
            return _own;
        }
    }
}
