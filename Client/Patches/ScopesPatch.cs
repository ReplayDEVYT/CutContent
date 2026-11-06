using BepInEx.Configuration;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Rendering;

namespace mizmii.ReClassic.Patches
{
    internal enum ScopeGlareMode
    {
        Light,
        Realistic,
        Classic
    }

    internal static class Scopes
    {
        private const string GlassShader = "Custom/OpticGlass";

        public const string ModeDescription =
            "How strong the alpha scope glass and reticle effects are.\n" +
            "Light: halfway between the current game and the alpha. Softer glints, faint reflections and a gentle shimmer on reticles.\n" +
            "Realistic: the alpha's own glass and reticle values, with lens reflections at a natural strength. Glass catches the sky and bright surfaces without washing out.\n" +
            "Classic: tuned to look like the alpha did on screen. Extra strong reflections and glints, the alpha's round milky glare across the lens at full strength while aiming, and brighter, blooming, fuzzy reticles at their alpha size and colour. The current renderer shows the alpha values much dimmer, so this pushes them further.";

        private static ConfigEntry<bool> _glass;
        private static ConfigEntry<bool> _reticleNoise;
        private static ConfigEntry<ScopeGlareMode> _mode;
        private static ConfigEntry<float> _aimShine;

        private static readonly Dictionary<int, Tracked> _tracked = new Dictionary<int, Tracked>();
        private static Texture2D _noise;
        private static bool _noiseSearched;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int SpecColorId = Shader.PropertyToID("_SpecColor");
        private static readonly int ReflectColorId = Shader.PropertyToID("_ReflectColor");
        private static readonly int ShininessId = Shader.PropertyToID("_Shininess");
        private static readonly int SpecPowerId = Shader.PropertyToID("_SpecPower");
        private static readonly int FresPowId = Shader.PropertyToID("_FresPow");
        private static readonly int FresAddId = Shader.PropertyToID("_FresAdd");
        private static readonly int NormalHidenessId = Shader.PropertyToID("_NormalHideness");
        private static readonly int ReflectionStrengthId = Shader.PropertyToID("_GlobalReflectionStrength");
        private static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
        private static readonly int HdrId = Shader.PropertyToID("_HDR");
        private static readonly int MarkScaleId = Shader.PropertyToID("_MarkScale");
        private static readonly int SpecTexId = Shader.PropertyToID("_SpecTex");
        private static readonly int AimMultiplierId = Shader.PropertyToID("_SwitchToSightMultiplier");
        private static readonly Dictionary<string, Texture2D> _specMasks = new Dictionary<string, Texture2D>();

        private static readonly int[] GlassFloats = { ShininessId, SpecPowerId, FresPowId, FresAddId, NormalHidenessId, ReflectionStrengthId, AimMultiplierId };
        private static readonly int[] GlassColors = { ColorId, SpecColorId, ReflectColorId };
        private static readonly int[] ReticleFloats = { HdrId, MarkScaleId };
        private static readonly int[] ReticleColors = { ColorId };

        private struct ReticleValues
        {
            public readonly Color Color;
            public readonly float Hdr;
            public readonly float MarkScale;

            public ReticleValues(Color color, float hdr, float markScale)
            {
                Color = color;
                Hdr = hdr;
                MarkScale = markScale;
            }
        }

        private sealed class Tracked
        {
            public Material Material;
            public bool Glass;
            public bool Optic;
            public string Name;
            public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
            public readonly Dictionary<int, Color> Colors = new Dictionary<int, Color>();
            public Texture Noise;
            public bool HasNoise;
            public Texture SpecTex;
            public bool HasSpecTex;
        }

        private static readonly Dictionary<string, ReticleValues> AlphaReticles = new Dictionary<string, ReticleValues>
        {
            { "scope_all_aimpoint_micro_h1_high_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0588f, 0.0588f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_belomo_pk_06_lod0_linza", new ReticleValues(new Color(1.0000f, 0.1324f, 0.1324f, 0.0000f), 4.0000f, 0.6000f) },
            { "scope_all_ekb_okp7_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_eotech_553_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_eotech_exps3_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_leupold_deltapoint_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_leupold_mark4_hamr_lod0_linza2", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_all_vomz_pilad_p1x42_weaver_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 2.0000f, 0.5000f) },
            { "scope_all_vomz_pilad_p1x42_weaver_lod0_linza_dot", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 2.0000f, 1.0000f) },
            { "scope_all_vomz_pilad_p1x42_weaver_lod0_linza_penek", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 2.0000f, 1.0000f) },
            { "scope_all_vomz_pilad_p1x42_weaver_lod0_linza_s", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 2.0000f, 1.0000f) },
            { "scope_all_walther_mrs_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 3.0000f, 1.0000f) },
            { "scope_base_burris_fast_fire_3_lod0_linza", new ReticleValues(new Color(1.0000f, 0.0000f, 0.0000f, 1.0000f), 4.0000f, 1.0000f) },
            { "scope_dovetail_ekb_okp7_dovetail_lod0_linza", new ReticleValues(new Color(0.0000f, 1.0000f, 0.2552f, 1.0000f), 2.0000f, 0.8000f) },
        };

        private struct GlassValues
        {
            public readonly Color Color;
            public readonly Color SpecColor;
            public readonly Color ReflectColor;
            public readonly float Shininess;
            public readonly float SpecPower;
            public readonly float FresPow;
            public readonly float FresAdd;
            public readonly float NormalHideness;

            public GlassValues(Color color, Color specColor, Color reflectColor, float shininess, float specPower, float fresPow, float fresAdd, float normalHideness)
            {
                Color = color;
                SpecColor = specColor;
                ReflectColor = reflectColor;
                Shininess = shininess;
                SpecPower = specPower;
                FresPow = fresPow;
                FresAdd = fresAdd;
                NormalHideness = normalHideness;
            }
        }

        private static readonly Dictionary<string, GlassValues> AlphaGlass = new Dictionary<string, GlassValues>
        {
            { "scope_30mm_march_tactical_3-24x42_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.5074f, 0.5074f, 0.5074f, 0.5000f), 1.2900f, 1.0000f, 1.2500f, 0.4000f, 6.0000f) },
            { "scope_all_aimpoint_micro_h1_high_lod0_glass", new GlassValues(new Color(0.7721f, 0.7721f, 0.7721f, 1.0000f), new Color(0.2132f, 0.2132f, 0.2132f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 4.0000f, 0.8500f, 1.0500f, 0.1650f, 6.0000f) },
            { "scope_all_elcan_specter_dr_1-4_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.5294f, 0.5294f, 0.5294f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 0.2400f, 0.5300f, 1.0180f, 0.0000f, 9.7700f) },
            { "scope_all_elcan_specter_os4x_lod0_glass", new GlassValues(new Color(0.5880f, 0.5880f, 0.5880f, 1.0000f), new Color(0.2059f, 0.2059f, 0.2059f, 1.0000f), new Color(0.4485f, 0.4485f, 0.4485f, 0.5000f), 0.0781f, 1.0000f, 0.8800f, 0.4400f, 6.0000f) },
            { "scope_all_eotech_553_lod0_glass", new GlassValues(new Color(0.0000f, 0.0000f, 0.0000f, 0.1280f), new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), 0.1880f, 1.8800f, 0.7930f, 0.6440f, 0.5000f) },
            { "scope_all_eotech_exps3_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.1271f, 0.1556f, 0.1838f, 1.0000f), new Color(0.5441f, 0.5441f, 0.5441f, 1.0000f), 4.0000f, 0.3500f, 0.5000f, 0.8730f, 0.5000f) },
            { "scope_all_leupold_deltapoint_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.2206f, 0.2206f, 0.2206f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 4.0000f, 0.2000f, 0.7210f, 0.7470f, 0.5000f) },
            { "scope_all_leupold_mark4_hamr_lod0_glass", new GlassValues(new Color(0.5880f, 0.5880f, 0.5880f, 1.0000f), new Color(0.1324f, 0.1324f, 0.1324f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 4.0000f, 0.8400f, 0.7210f, 0.4750f, 0.5000f) },
            { "scope_all_sig_bravo4_4x30_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.5294f, 0.5294f, 0.5294f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 0.2400f, 0.5300f, 1.9400f, 0.1300f, 9.7700f) },
            { "scope_all_vomz_pilad_p1x42_weaver_lod0_glass", new GlassValues(new Color(0.6912f, 0.6912f, 0.6912f, 1.0000f), new Color(0.3653f, 0.7132f, 0.0315f, 1.0000f), new Color(0.3456f, 0.6209f, 1.0000f, 0.5000f), 0.0781f, 1.0000f, 0.7360f, 0.0000f, 6.0000f) },
            { "scope_all_walther_mrs_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(1.0000f, 0.8829f, 0.7574f, 0.5000f), 4.0000f, 0.5500f, 0.7740f, 0.0000f, 0.5000f) },
            { "scope_base_burris_fast_fire_3_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.5441f, 0.5441f, 0.5441f, 1.0000f), new Color(1.0000f, 1.0000f, 1.0000f, 0.5000f), 4.0000f, 1.3900f, 0.7400f, 0.0000f, 0.5000f) },
            { "scope_dovetail_ekb_okp7_dovetail_lod0_glass", new GlassValues(new Color(1.0000f, 1.0000f, 1.0000f, 1.0000f), new Color(0.1618f, 0.1618f, 0.1618f, 1.0000f), new Color(0.3704f, 0.2868f, 1.0000f, 0.5000f), 0.0781f, 1.0000f, 0.8180f, 0.0000f, 6.0000f) },
        };

        private static readonly GlassValues OpticFallback = AlphaGlass["scope_all_leupold_mark4_hamr_lod0_glass"];
        private static readonly GlassValues CollimatorFallback = AlphaGlass["scope_all_eotech_553_lod0_glass"];

        public static void Enable(ConfigFile config)
        {
            const string section = "Visuals";
            _glass = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Scopes", "Alpha Scope Glass", true, "Scope and sight glass uses the alpha's shine: stronger fresnel edge glow, specular glints and reflections. The strength follows Scope Glare Mode.", section, "Alpha Scope Glass");
            _reticleNoise = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Scopes", "Alpha Reticle Fuzziness", true, "Red dot and holographic reticles get the alpha's noise texture back, so they glow and shimmer instead of looking perfectly clean. The strength follows Scope Glare Mode.", section, "Alpha Reticle Fuzziness");
            _mode = config.Bind(section, "Scope Glare Mode", ScopeGlareMode.Realistic, ModeDescription);
            _aimShine = config.Bind(section, "Shine While Aiming", 0.5f, new ConfigDescription("How much glass shine and glare stays on a scope's lens while you aim through it. The game normally cuts it to a tenth when aiming. Scales with Scope Glare Mode; Classic doubles it, so 0.5 already gives the alpha's full glare. 0 keeps the game's behaviour.", new AcceptableValueRange<float>(0f, 1f)));
            ConfigHelper.Option(FeatureCategory.VisualsAndAudio, "Scopes", _mode);

            _glass.SettingChanged += (sender, args) => ReapplyAll();
            _reticleNoise.SettingChanged += (sender, args) => ReapplyAll();
            _mode.SettingChanged += (sender, args) => ReapplyAll();
            _aimShine.SettingChanged += (sender, args) => ReapplyAll();

            new OpticSightPatch().Enable();
            new CollimatorSightPatch().Enable();
            new LensRedrawPatch(nameof(SSAAPropagator.FillCommandBuffer)).Enable();
            new LensRedrawPatch("OnRenderImage").Enable();
        }

        private static readonly AccessTools.FieldRef<SSAAPropagator, RenderTexture[]> PropagatorTargets =
            AccessTools.FieldRefAccess<SSAAPropagator, RenderTexture[]>("_resampledColorTargetHDR");
        private static readonly Dictionary<int, List<KeyValuePair<Renderer, int>>> _sightGlass = new Dictionary<int, List<KeyValuePair<Renderer, int>>>();

        private static List<KeyValuePair<Renderer, int>> SightGlass(OpticSight sight)
        {
            int id = sight.GetInstanceID();
            if (_sightGlass.TryGetValue(id, out List<KeyValuePair<Renderer, int>> list) && list.All(pair => pair.Key != null))
            {
                return list;
            }
            list = new List<KeyValuePair<Renderer, int>>();
            foreach (Renderer renderer in ScopeRoot(sight.transform).GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == sight.LensRenderer)
                {
                    continue;
                }
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && materials[i].shader != null && materials[i].shader.name == GlassShader)
                    {
                        list.Add(new KeyValuePair<Renderer, int>(renderer, i));
                    }
                }
            }
            _sightGlass[id] = list;
            return list;
        }

        public static void DrawLensAndGlass(CommandBuffer buffer, Renderer renderer, Material material, SSAAPropagator propagator)
        {
            buffer.DrawRenderer(renderer, material);
            if (!_glass.Value || AimShine() <= 0f)
            {
                return;
            }
            try
            {
                OpticSight sight = CameraManager.Instance?.OpticCameraManager?.CurrentOpticSight;
                RenderTexture[] targets = PropagatorTargets(propagator);
                if (sight == null || targets == null || targets.Length == 0 || targets[0] == null)
                {
                    return;
                }
                bool drawn = false;
                foreach (KeyValuePair<Renderer, int> pair in SightGlass(sight))
                {
                    if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    Material[] materials = pair.Key.sharedMaterials;
                    if (pair.Value >= materials.Length || materials[pair.Value] == null)
                    {
                        continue;
                    }
                    if (!drawn)
                    {
                        buffer.SetRenderTarget(targets[0]);
                        drawn = true;
                    }
                    buffer.DrawRenderer(pair.Key, materials[pair.Value], pair.Value, 0);
                }
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogWarning($"Alpha Scope Glass: could not draw the glare while aiming: {ex.Message}");
            }
        }

        private class LensRedrawPatch : ModulePatch
        {
            private readonly string _method;

            public LensRedrawPatch(string method)
            {
                _method = method;
            }

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(SSAAPropagator), _method);
            }

            [PatchTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                FieldInfo lensMaterial = AccessTools.Field(typeof(SSAAPropagator), "_opticLensMaterial");
                MethodInfo draw = AccessTools.Method(typeof(CommandBuffer), nameof(CommandBuffer.DrawRenderer), new[] { typeof(Renderer), typeof(Material) });
                MethodInfo helper = AccessTools.Method(typeof(Scopes), nameof(DrawLensAndGlass));
                List<CodeInstruction> codes = instructions.ToList();
                int patched = 0;
                for (int i = 0; i < codes.Count; i++)
                {
                    if (!codes[i].Calls(draw))
                    {
                        continue;
                    }
                    bool lens = false;
                    for (int back = i - 1; back >= 0 && back >= i - 4; back--)
                    {
                        if (codes[back].LoadsField(lensMaterial))
                        {
                            lens = true;
                            break;
                        }
                    }
                    if (!lens)
                    {
                        continue;
                    }
                    codes[i].opcode = OpCodes.Call;
                    codes[i].operand = helper;
                    codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                    i++;
                    patched++;
                }
                if (patched == 0)
                {
                    Plugin.LogSource.LogWarning("Alpha Scope Glass: could not find where the game redraws the scope lens; glare will not show while aiming.");
                }
                return codes;
            }
        }

        private static Texture2D Noise()
        {
            if (_noise != null || _noiseSearched)
            {
                return _noise;
            }
            _noiseSearched = true;
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (texture != null && texture.name == "noise3")
                {
                    _noise = texture;
                    return _noise;
                }
            }
            AssetBundle bundle = null;
            foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (loaded != null && loaded.name.Replace('\\', '/').EndsWith("systems/effects/collimator.bundle"))
                {
                    bundle = loaded;
                    break;
                }
            }
            if (bundle == null)
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Windows/assets/systems/effects/collimator.bundle");
                if (File.Exists(path))
                {
                    bundle = AssetBundle.LoadFromFile(path);
                }
            }
            if (bundle != null)
            {
                foreach (Texture2D texture in bundle.LoadAllAssets<Texture2D>())
                {
                    if (texture.name == "noise3")
                    {
                        _noise = texture;
                        break;
                    }
                }
            }
            Plugin.LogSource.LogInfo(_noise != null ? "Alpha Reticle Fuzziness: loaded the alpha reticle noise." : "Alpha Reticle Fuzziness: could not find the alpha reticle noise texture.");
            return _noise;
        }

        private static Transform ScopeRoot(Transform start)
        {
            for (Transform t = start; t != null; t = t.parent)
            {
                if (t.name.StartsWith("scope_") || t.GetComponent<LODGroup>() != null)
                {
                    return t;
                }
                if (t.parent != null && (t.parent.name.StartsWith("mod_") || t.parent.name.StartsWith("weapon")))
                {
                    return t;
                }
            }
            return start.parent != null ? start.parent : start;
        }

        private static Tracked Track(Material material, bool glass, bool optic)
        {
            int id = material.GetInstanceID();
            if (_tracked.TryGetValue(id, out Tracked tracked))
            {
                if (tracked.Material != null)
                {
                    return tracked;
                }
                _tracked.Remove(id);
            }

            tracked = new Tracked
            {
                Material = material,
                Glass = glass,
                Optic = optic,
                Name = material.name.Replace(" (Instance)", "").ToLowerInvariant()
            };
            foreach (int property in glass ? GlassFloats : ReticleFloats)
            {
                if (material.HasProperty(property))
                {
                    tracked.Floats[property] = material.GetFloat(property);
                }
            }
            foreach (int property in glass ? GlassColors : ReticleColors)
            {
                if (material.HasProperty(property))
                {
                    tracked.Colors[property] = material.GetColor(property);
                }
            }
            if (glass && material.HasProperty(SpecTexId))
            {
                tracked.HasSpecTex = true;
                tracked.SpecTex = material.GetTexture(SpecTexId);
            }
            if (!glass && material.HasProperty(NoiseTexId))
            {
                tracked.HasNoise = true;
                tracked.Noise = material.GetTexture(NoiseTexId);
            }
            _tracked[id] = tracked;
            return tracked;
        }

        private static void ReapplyAll()
        {
            var dead = new List<int>();
            foreach (KeyValuePair<int, Tracked> pair in _tracked)
            {
                if (pair.Value.Material == null)
                {
                    dead.Add(pair.Key);
                    continue;
                }
                Apply(pair.Value);
            }
            foreach (int id in dead)
            {
                _tracked.Remove(id);
            }
        }

        private static void Restore(Tracked tracked)
        {
            Material material = tracked.Material;
            foreach (KeyValuePair<int, float> pair in tracked.Floats)
            {
                material.SetFloat(pair.Key, pair.Value);
            }
            foreach (KeyValuePair<int, Color> pair in tracked.Colors)
            {
                material.SetColor(pair.Key, pair.Value);
            }
            if (tracked.HasNoise)
            {
                material.SetTexture(NoiseTexId, tracked.Noise);
            }
            if (tracked.HasSpecTex)
            {
                material.SetTexture(SpecTexId, tracked.SpecTex);
            }
        }

        private static void Apply(Tracked tracked)
        {
            Restore(tracked);
            if (tracked.Glass)
            {
                if (_glass.Value)
                {
                    ApplyGlass(tracked);
                }
            }
            else if (_reticleNoise.Value)
            {
                ApplyReticle(tracked);
            }
        }

        private static float OriginalFloat(Tracked tracked, int property, float fallback)
        {
            return tracked.Floats.TryGetValue(property, out float value) ? value : fallback;
        }

        private static Color OriginalColor(Tracked tracked, int property, Color fallback)
        {
            return tracked.Colors.TryGetValue(property, out Color value) ? value : fallback;
        }

        private static void SetFloat(Tracked tracked, int property, float value)
        {
            if (tracked.Floats.ContainsKey(property))
            {
                tracked.Material.SetFloat(property, value);
            }
        }

        private static void SetColor(Tracked tracked, int property, Color value)
        {
            if (tracked.Colors.ContainsKey(property))
            {
                tracked.Material.SetColor(property, value);
            }
        }

        private static void ApplyGlass(Tracked tracked)
        {
            if (!AlphaGlass.TryGetValue(tracked.Name, out GlassValues values))
            {
                values = tracked.Optic ? OpticFallback : CollimatorFallback;
            }

            ScopeGlareMode mode = _mode.Value;
            float blend = mode == ScopeGlareMode.Light ? 0.5f : 1f;
            float glint = mode == ScopeGlareMode.Classic ? 2f : 1f;
            float edge = mode == ScopeGlareMode.Classic ? 0.2f : 0f;
            float reflection = mode == ScopeGlareMode.Light ? 0.25f : mode == ScopeGlareMode.Realistic ? 0.6f : 1.25f;

            Color spec = Color.Lerp(OriginalColor(tracked, SpecColorId, values.SpecColor), values.SpecColor, blend) * glint;
            spec.a = values.SpecColor.a;
            Color reflect = Color.Lerp(OriginalColor(tracked, ReflectColorId, values.ReflectColor), values.ReflectColor, blend);

            SetColor(tracked, ColorId, Color.Lerp(OriginalColor(tracked, ColorId, values.Color), values.Color, blend));
            SetColor(tracked, SpecColorId, spec);
            SetColor(tracked, ReflectColorId, reflect);
            SetFloat(tracked, ShininessId, Mathf.Lerp(OriginalFloat(tracked, ShininessId, values.Shininess), values.Shininess, blend));
            SetFloat(tracked, SpecPowerId, Mathf.Lerp(OriginalFloat(tracked, SpecPowerId, values.SpecPower), values.SpecPower, blend) * glint);
            SetFloat(tracked, FresPowId, Mathf.Lerp(OriginalFloat(tracked, FresPowId, values.FresPow), values.FresPow, blend));
            SetFloat(tracked, FresAddId, Mathf.Min(Mathf.Lerp(OriginalFloat(tracked, FresAddId, values.FresAdd), values.FresAdd, blend) + edge, 1f));
            SetFloat(tracked, NormalHidenessId, Mathf.Lerp(OriginalFloat(tracked, NormalHidenessId, values.NormalHideness), values.NormalHideness, blend));
            SetFloat(tracked, ReflectionStrengthId, Mathf.Max(OriginalFloat(tracked, ReflectionStrengthId, 0f), reflection));
            SetFloat(tracked, AimMultiplierId, Mathf.Lerp(OriginalFloat(tracked, AimMultiplierId, 0.1f), 1f, AimShine()));

            if (tracked.HasSpecTex && tracked.SpecTex == null)
            {
                Texture2D mask = SpecMask(tracked.Optic ? "round_spec_mask" : "eotech_spec_mask") ?? SpecMask("round_spec_mask");
                if (mask != null)
                {
                    tracked.Material.SetTexture(SpecTexId, mask);
                }
            }
        }

        private static Texture2D SpecMask(string name)
        {
            if (_specMasks.TryGetValue(name, out Texture2D cached))
            {
                return cached;
            }
            Texture2D found = null;
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (texture != null && texture.name == name)
                {
                    found = texture;
                    break;
                }
            }
            if (found == null)
            {
                string relative = "assets/content/textures/holemanager/" + name + ".bundle";
                AssetBundle bundle = null;
                bool ours = false;
                foreach (AssetBundle loaded in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (loaded != null && loaded.name.Replace('\\', '/').EndsWith(relative))
                    {
                        bundle = loaded;
                        break;
                    }
                }
                if (bundle == null)
                {
                    string path = Path.Combine(Application.streamingAssetsPath, "Windows/" + relative);
                    if (File.Exists(path))
                    {
                        bundle = AssetBundle.LoadFromFile(path);
                        ours = bundle != null;
                    }
                }
                if (bundle != null)
                {
                    foreach (Texture2D texture in bundle.LoadAllAssets<Texture2D>())
                    {
                        if (texture.name == name)
                        {
                            found = texture;
                            break;
                        }
                    }
                    if (ours)
                    {
                        bundle.Unload(false);
                    }
                }
            }
            if (found != null)
            {
                found.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            }
            _specMasks[name] = found;
            Plugin.LogSource.LogInfo(found != null ? $"Alpha Scope Glass: loaded the alpha glare mask {name}." : $"Alpha Scope Glass: could not find the alpha glare mask {name}.");
            return found;
        }

        private static void ApplyReticle(Tracked tracked)
        {
            Material material = tracked.Material;
            if (tracked.HasNoise && tracked.Noise == null)
            {
                Texture2D noise = Noise();
                if (noise != null)
                {
                    material.SetTexture(NoiseTexId, noise);
                }
            }

            ScopeGlareMode mode = _mode.Value;
            if (mode == ScopeGlareMode.Light)
            {
                return;
            }

            bool known = AlphaReticles.TryGetValue(tracked.Name, out ReticleValues alpha);
            float hdr = known ? alpha.Hdr : OriginalFloat(tracked, HdrId, 3f);
            if (known)
            {
                Color color = alpha.Color;
                color.a = OriginalColor(tracked, ColorId, color).a;
                SetColor(tracked, ColorId, color);
            }
            if (mode == ScopeGlareMode.Classic)
            {
                hdr *= 1.5f;
                if (known)
                {
                    SetFloat(tracked, MarkScaleId, alpha.MarkScale);
                }
            }
            SetFloat(tracked, HdrId, hdr);
        }

        private static void TrackGlass(Transform root, bool optic)
        {
            if (root == null)
            {
                return;
            }
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || material.shader.name != GlassShader)
                    {
                        continue;
                    }
                    bool fresh = !_tracked.ContainsKey(material.GetInstanceID());
                    Tracked tracked = Track(material, true, optic);
                    if (fresh)
                    {
                        Apply(tracked);
                    }
                }
            }
        }

        private class OpticSightPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(OpticSight), nameof(OpticSight.Awake));
            }

            [PatchPostfix]
            private static void Postfix(OpticSight __instance)
            {
                TrackGlass(ScopeRoot(__instance.transform), true);
            }
        }

        private static float AimShine()
        {
            return Mathf.Clamp01(Mathf.Clamp01(_aimShine.Value) * ModeScale());
        }

        private static float ModeScale()
        {
            float scale;
            switch (_mode.Value)
            {
                case ScopeGlareMode.Light:
                    scale = 0.4f;
                    break;
                case ScopeGlareMode.Realistic:
                    scale = 0.7f;
                    break;
                default:
                    scale = 2f;
                    break;
            }
            return scale;
        }

        private class CollimatorSightPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(CollimatorSight), nameof(CollimatorSight.Awake));
            }

            [PatchPostfix]
            private static void Postfix(CollimatorSight __instance)
            {
                Transform root = ScopeRoot(__instance.transform);
                TrackGlass(root, root.GetComponentInChildren<OpticSight>(true) != null);
                Material material = __instance.CollimatorMaterial;
                if (material == null)
                {
                    return;
                }
                bool fresh = !_tracked.ContainsKey(material.GetInstanceID());
                Tracked tracked = Track(material, false, false);
                if (fresh)
                {
                    Apply(tracked);
                }
            }
        }
    }
}
