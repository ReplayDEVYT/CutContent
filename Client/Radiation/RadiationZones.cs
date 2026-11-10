using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Radiation
{
    internal enum Hazard
    {
        Radiation,
        Chemical
    }

    internal sealed class MaskInfo
    {
        public float Radiation;
        public float Chemical;
        public bool FullFace;
    }

    internal sealed class ZoneDefinition
    {
        [JsonProperty("name")]
        public string Name = "";

        [JsonProperty("type")]
        public string Type = "radiation";

        [JsonProperty("exfil", NullValueHandling = NullValueHandling.Ignore)]
        public string Exfil;

        [JsonProperty("x")]
        public float X;

        [JsonProperty("y")]
        public float Y;

        [JsonProperty("z")]
        public float Z;

        [JsonProperty("radius")]
        public float Radius = 25f;

        [JsonProperty("height")]
        public float Height = 12f;

        [JsonProperty("intensity")]
        public float Intensity = 1f;
    }

    internal sealed class ZoneFile
    {
        [JsonProperty("_help")]
        public string Help = "Radiation zones per map. Keys are location ids (bigmap, Woods, RezervBase, laboratory, factory4_day, Shoreline, Interchange, Lighthouse, TarkovStreets, Sandbox). type is radiation or chemical. Each zone is centered on x/y/z, or on the exfil named in exfil if it is set. radius is how far it reaches sideways in meters, height how far up and down, and intensity the dose per second at its center (1 is a strong zone). The radiation fades out toward the edge. Press the Log Position key in raid to print where you are standing. Changes are read at the start of each raid.";

        [JsonProperty("maps")]
        public Dictionary<string, List<ZoneDefinition>> Maps = new Dictionary<string, List<ZoneDefinition>>();
    }

    internal sealed class Zone
    {
        public string Name;
        public Vector3 Center;
        public float Radius;
        public float Height;
        public float Intensity;
        public bool Random;
        public Hazard Hazard;

        public float RateAt(Vector3 position)
        {
            float vertical = Mathf.Abs(position.y - Center.y);
            if (vertical > Height)
            {
                return 0f;
            }
            float dx = position.x - Center.x;
            float dz = position.z - Center.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            if (distance >= Radius)
            {
                return 0f;
            }
            float k = 1f - distance / Radius;
            float fade = 1f - Mathf.Clamp01((vertical - Height * 0.6f) / (Height * 0.4f));
            return Intensity * k * k * (3f - 2f * k) * fade;
        }
    }

    internal static class RadiationZones
    {
        public const string GeigerId = "5672cb724bdc2dc2088b456b";
        public const string AnalyzerId = "590a3efd86f77437d351a25b";
        private const string Section = "Radiation";
        private const string MaskSection = "Gas Masks";
        private const string FileName = Plugin.Guid + ".RadiationZones.json";

        private static readonly Dictionary<string, MaskInfo> Masks = new Dictionary<string, MaskInfo>
        {
            ["5b432c305acfc40019478128"] = new MaskInfo { Radiation = 0.8f, Chemical = 0.95f, FullFace = true },
            ["60363c0c92ec1c31037959f5"] = new MaskInfo { Radiation = 0.9f, Chemical = 1f, FullFace = true },
            ["59e7715586f7742ee5789605"] = new MaskInfo { Radiation = 0.6f, Chemical = 0.2f, FullFace = false }
        };

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> RandomZones;
        public static ConfigEntry<int> RandomCount;
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<bool> ScreenEffect;
        public static ConfigEntry<float> ScreenStrength;
        public static ConfigEntry<float> GeigerVolume;
        public static ConfigEntry<float> AnalyzerVolume;
        public static ConfigEntry<KeyboardShortcut> LogPosition;
        public static ConfigEntry<bool> MaskEffects;
        public static ConfigEntry<bool> MaskOverlay;
        public static ConfigEntry<float> MaskOverlaySize;
        public static ConfigEntry<bool> MaskGlass;
        public static ConfigEntry<bool> MaskBreathing;

        public static readonly List<Zone> Active = new List<Zone>();

        private static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, FileName);

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Hazards", "Radiation Zones", true,
                "Parts of some maps are radioactive or full of chemicals: the Labs basement and storage, the chemical plant on Customs, the factory gate on Woods and the D-2 bunker on Reserve. Radiation raises the Radiation value in your health panel. Past 15 you get Radiation Exposure, which hurts every part of your body harder the higher it climbs. On its own it only drops by 0.5 a minute, raid or not. Anything that treats Radiation Exposure cures the effect and brings the value down: medkits and drinks take a chunk off, Perfotoran and Golden Star balm keep lowering it while they work. Chemicals poison you until you take an antidote. Masks protect differently: the GP-7 stops almost everything, the GP-5 a little less, and the 3M respirator filters some radioactive dust but barely any chemicals. The Geiger-Muller counter clicks faster the closer you get to radiation, from anywhere on you or a special slot.",
                Section, "Enabled");
            RandomZones = config.Bind(Section, "Random Zones", true, "Also put a few smaller radiation spots of random size and strength somewhere on the map each raid. In co-op everyone gets the same spots.");
            RandomCount = config.Bind(Section, "Random Zone Count", 3, new ConfigDescription("How many random spots to put down each raid.", new AcceptableValueRange<int>(1, 8)));
            DamageScale = config.Bind(Section, "Damage Scale", 1f, new ConfigDescription("How much a radiation dose hurts. 1 is the default.", new AcceptableValueRange<float>(0f, 3f)));
            ScreenEffect = config.Bind(Section, "Screen Effect", true, "Your vision closes in a little while you are being irradiated or poisoned, and stays that way while your radiation is high.");
            ScreenStrength = config.Bind(Section, "Screen Effect Strength", 0.7f, new ConfigDescription("How strong the tunnel vision gets at its worst.", new AcceptableValueRange<float>(0.1f, 1f)));
            GeigerVolume = config.Bind(Section, "Geiger Counter Volume", 0.6f, new ConfigDescription("How loud the Geiger-Muller counter clicks while you carry one in a radiation zone.", new AcceptableValueRange<float>(0f, 1f)));
            LogPosition = ConfigHelper.DebugKey(config, Section, "Log Position", "Prints where you are standing and the radiation there to the BepInEx log, to help place zones in " + FileName + ".");
            AnalyzerVolume = config.Bind(Section, "Gas Analyzer Volume", 0.5f, new ConfigDescription("How loud the Gas analyzer beeps while you carry one in a chemical zone. It beeps faster the stronger the chemicals are.", new AcceptableValueRange<float>(0f, 1f)));
            ConfigHelper.Requires(Enabled, RandomZones, RandomCount, DamageScale, ScreenEffect, ScreenStrength, GeigerVolume, AnalyzerVolume, LogPosition);
            ConfigHelper.NextRaid(Enabled, RandomZones, RandomCount);
            ConfigHelper.Requires(RandomZones, RandomCount);
            ConfigHelper.Requires(ScreenEffect, ScreenStrength);

            MaskEffects = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Visuals", "Gas Mask Effects", true,
                "Wearing a full gas mask narrows your view to its two eyepieces and puts glass in front of your eyes like a lowered faceshield. Any mask, the half-face respirator included, plays the faceshield breathing.",
                MaskSection, "Enabled");
            MaskOverlay = config.Bind(MaskSection, "Eyepiece Overlay", true, "See the inside of the mask around the eyepieces, the same way night vision goggles draw theirs. Night vision takes over while it is on.");
            MaskOverlaySize = config.Bind(MaskSection, "Overlay Size", 1f, new ConfigDescription("How big the eyepieces are on screen. Smaller shows more of the mask.", new AcceptableValueRange<float>(0.5f, 2f)));
            MaskGlass = config.Bind(MaskSection, "Visor Glass", true, "Add the faceshield glass blur and distortion to the eyepieces.");
            MaskBreathing = config.Bind(MaskSection, "Breathing", true, "Play the breathing you hear behind a lowered faceshield, and muffle your voice.");
            ConfigHelper.Requires(MaskEffects, MaskOverlay, MaskOverlaySize, MaskGlass, MaskBreathing);
            ConfigHelper.Requires(MaskOverlay, MaskOverlaySize);
            MaskEffects.SettingChanged += (s, e) => RadiationRunner.MaskSettingsChanged();
            MaskOverlay.SettingChanged += (s, e) => RadiationRunner.MaskSettingsChanged();
            MaskGlass.SettingChanged += (s, e) => RadiationRunner.MaskSettingsChanged();
            MaskBreathing.SettingChanged += (s, e) => RadiationRunner.MaskSettingsChanged();

            BotHazards.Enable(config, Enabled);
            LoadFile();
            new GameStartedPatch().Enable();
            new BreathingPatch().Enable();
            new FaceShieldPatch().Enable();
            new MedTreatmentPatch().Enable();
            new HealthPanelPatch().Enable();
        }

        public static MaskInfo Mask(Player player)
        {
            return Mask(player?.Equipment?.GetSlot(EquipmentSlot.FaceCover)?.ContainedItem);
        }

        public static MaskInfo Mask(Item mask)
        {
            if (mask != null && Masks.TryGetValue(mask.TemplateId.ToString(), out MaskInfo info))
            {
                return info;
            }
            return null;
        }

        public static bool WearsFullMask(Item mask)
        {
            MaskInfo info = Mask(mask);
            return info != null && info.FullFace;
        }

        public static float Protection(Player player, Hazard hazard)
        {
            MaskInfo mask = Mask(player);
            if (mask == null)
            {
                return 0f;
            }
            return hazard == Hazard.Chemical ? mask.Chemical : mask.Radiation;
        }

        public static bool WearsMask(Player player)
        {
            return Mask(player) != null;
        }

        public static bool WearsFullMask(Player player)
        {
            MaskInfo mask = Mask(player);
            return mask != null && mask.FullFace;
        }

        public static bool HasGeiger(Player player)
        {
            return Carries(player, GeigerId);
        }

        public static bool HasAnalyzer(Player player)
        {
            return Carries(player, AnalyzerId);
        }

        private static bool Carries(Player player, string templateId)
        {
            InventoryEquipment equipment = player?.Equipment;
            if (equipment == null)
            {
                return false;
            }
            foreach (Item item in equipment.GetAllItems())
            {
                if (item.TemplateId.ToString() == templateId)
                {
                    return true;
                }
            }
            return false;
        }

        public static float RateAt(Vector3 position, Hazard hazard)
        {
            float rate = 0f;
            foreach (Zone zone in Active)
            {
                if (zone.Hazard == hazard)
                {
                    rate += zone.RateAt(position);
                }
            }
            return rate;
        }

        private static ZoneFile Defaults()
        {
            var file = new ZoneFile();
            file.Maps["bigmap"] = new List<ZoneDefinition>
            {
                new ZoneDefinition { Name = "Chemical plant yard", X = 597f, Y = 1.2f, Z = -96f, Radius = 55f, Height = 14f, Intensity = 1.1f },
                new ZoneDefinition { Name = "Chemical plant warehouses", Type = "chemical", X = 393f, Y = 1.6f, Z = -89f, Radius = 40f, Height = 14f, Intensity = 0.7f }
            };
            file.Maps["Woods"] = new List<ZoneDefinition>
            {
                new ZoneDefinition { Name = "Factory gate", Exfil = "Factory Gate", Radius = 45f, Height = 16f, Intensity = 1f }
            };
            file.Maps["RezervBase"] = new List<ZoneDefinition>
            {
                new ZoneDefinition { Name = "D-2 bunker", Exfil = "EXFIL_Bunker_D2", Radius = 35f, Height = 8f, Intensity = 1.4f }
            };
            file.Maps["laboratory"] = new List<ZoneDefinition>
            {
                new ZoneDefinition { Name = "Basement", X = -166f, Y = -4f, Z = -384f, Radius = 40f, Height = 3.5f, Intensity = 1f },
                new ZoneDefinition { Name = "Under storage", Exfil = "lab_Under_Storage_Collector", Radius = 25f, Height = 4f, Intensity = 1.5f },
                new ZoneDefinition { Name = "Second floor labs", Type = "chemical", X = -189f, Y = 4.2f, Z = -335f, Radius = 22f, Height = 2.5f, Intensity = 0.5f }
            };
            return file;
        }

        private static ZoneFile _file;

        private static void LoadFile()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    _file = Defaults();
                    File.WriteAllText(FilePath, JsonConvert.SerializeObject(_file, Formatting.Indented));
                    return;
                }
                _file = JsonConvert.DeserializeObject<ZoneFile>(File.ReadAllText(FilePath)) ?? Defaults();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Radiation] Could not read {FileName}, using the default zones: {e.Message}");
                _file = Defaults();
            }
        }

        private static List<ZoneDefinition> ZonesFor(string location)
        {
            if (_file?.Maps == null || string.IsNullOrEmpty(location))
            {
                return new List<ZoneDefinition>();
            }
            foreach (var pair in _file.Maps)
            {
                if (string.Equals(pair.Key, location, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value ?? new List<ZoneDefinition>();
                }
            }
            return new List<ZoneDefinition>();
        }

        private static GameWorld _builtFor;

        public static void EnsureBuilt()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null || world == _builtFor)
            {
                return;
            }
            _builtFor = world;
            Build(world, false);
        }

        private static void Build(GameWorld world, bool random)
        {
            Active.Clear();
            LoadFile();
            string location = world.LocationId;
            if (string.IsNullOrEmpty(location) || location.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }
            ExfiltrationPoint[] exfils = UnityEngine.Object.FindObjectsOfType<ExfiltrationPoint>(true);
            foreach (ZoneDefinition definition in ZonesFor(location))
            {
                Vector3 center = new Vector3(definition.X, definition.Y, definition.Z);
                if (!string.IsNullOrEmpty(definition.Exfil))
                {
                    ExfiltrationPoint exfil = exfils.FirstOrDefault(e => e != null && e.Settings != null && string.Equals(e.Settings.Name, definition.Exfil, StringComparison.OrdinalIgnoreCase));
                    if (exfil == null)
                    {
                        Plugin.LogSource.LogWarning($"[Radiation] No exfil named {definition.Exfil} on {location}, skipping the {definition.Name} zone.");
                        continue;
                    }
                    center = exfil.transform.position;
                }
                Active.Add(new Zone { Name = definition.Name, Center = center, Radius = Mathf.Max(1f, definition.Radius), Height = Mathf.Max(1f, definition.Height), Intensity = Mathf.Max(0f, definition.Intensity), Hazard = string.Equals(definition.Type, "chemical", StringComparison.OrdinalIgnoreCase) ? Hazard.Chemical : Hazard.Radiation });
            }
            if (random && RandomZones.Value)
            {
                AddRandom(world);
            }
            foreach (Zone zone in Active)
            {
                Plugin.LogSource.LogInfo($"[Radiation] {(zone.Random ? "Random zone" : zone.Name)} ({zone.Hazard}) at {zone.Center}, radius {zone.Radius:0} m, intensity {zone.Intensity:0.00}.");
            }
        }

        private static int Seed(GameWorld world)
        {
            uint hash = 2166136261;
            foreach (char c in world.LocationId ?? "")
            {
                hash = (hash ^ c) * 16777619;
            }
            uint mix = 0;
            foreach (LootItem loot in world.LootItems._iteration)
            {
                if (loot == null || string.IsNullOrEmpty(loot.ItemId))
                {
                    continue;
                }
                uint h = 2166136261;
                foreach (char c in loot.ItemId)
                {
                    h = (h ^ c) * 16777619;
                }
                mix ^= h;
            }
            return (int)(hash ^ mix);
        }

        private static void AddRandom(GameWorld world)
        {
            List<Vector3> spots = UnityEngine.Object.FindObjectsOfType<LootableContainer>()
                .Where(c => c != null && c.gameObject.activeInHierarchy)
                .Select(c => c.transform.position)
                .OrderBy(p => Mathf.Round(p.x * 10f)).ThenBy(p => Mathf.Round(p.z * 10f)).ThenBy(p => Mathf.Round(p.y * 10f))
                .ToList();
            if (spots.Count == 0)
            {
                return;
            }
            var random = new System.Random(Seed(world));
            int wanted = RandomCount.Value;
            for (int attempt = 0; attempt < wanted * 6 && Active.Count(z => z.Random) < wanted; attempt++)
            {
                Vector3 spot = spots[random.Next(spots.Count)];
                if (Active.Any(z => Vector3.Distance(z.Center, spot) < z.Radius + 20f))
                {
                    continue;
                }
                float size = (float)random.NextDouble();
                Active.Add(new Zone
                {
                    Name = "Random",
                    Center = spot,
                    Radius = Mathf.Lerp(6f, 22f, size),
                    Height = Mathf.Lerp(3f, 8f, size),
                    Intensity = Mathf.Lerp(0.3f, 1.2f, (float)random.NextDouble()),
                    Hazard = random.Next(3) == 0 ? Hazard.Chemical : Hazard.Radiation,
                    Random = true
                });
            }
        }

        private class GameStartedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
            }

            [PatchPostfix]
            private static void Postfix(GameWorld __instance)
            {
                try
                {
                    Active.Clear();
                    BotHazards.Reset();
                    Effects.CustomEffects.EnsureNamedIcons();
                    _builtFor = __instance;
                    if (Enabled.Value)
                    {
                        Build(__instance, true);
                    }
                    if (__instance.GetComponent<RadiationRunner>() == null)
                    {
                        __instance.gameObject.AddComponent<RadiationRunner>();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Radiation] Could not set up the zones: {e}");
                }
            }
        }

        private class MedTreatmentPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.Residue));
            }

            [PatchPrefix]
            private static void Prefix(ActiveHealthController.MedEffect __instance, out int __state)
            {
                __state = 0;
                try
                {
                    Player local = Singleton<GameWorld>.Instance?.MainPlayer;
                    if (local == null || __instance._interrupted || !ReferenceEquals(__instance.HealthController, local.ActiveHealthController))
                    {
                        return;
                    }
                    Item item = __instance.MedItem;
                    string templateId = item.TemplateId.ToString();
                    ActiveHealthController health = __instance.HealthController;
                    MedKitComponent kit = __instance._medKit;
                    float resource = kit != null ? kit.HpResource : float.MaxValue;
                    Toxicity.Consumed(item);
                    if (templateId == Meters.Antidote)
                    {
                        Meters.TreatToxicity(templateId);
                    }
                    HealthEffectsComponent effects = __instance._healthEffects;
                    if (effects?.DamageEffects == null)
                    {
                        return;
                    }
                    if (effects.DamageEffects.TryGetValue(EDamageEffectType.Intoxication, out JsonType.DamageEffectSpecification antidote) && templateId != Meters.Antidote && resource >= antidote.Cost)
                    {
                        if (Effects.CustomEffects.Toxicity?.FindExisting(health, EBodyPart.Head) != null && health.FindActiveEffect<ActiveHealthController.Intoxication>(ActiveHealthController.Intoxication.DefaultBodyPart) == null)
                        {
                            __state += antidote.Cost;
                            resource -= antidote.Cost;
                        }
                        Meters.TreatToxicity(templateId);
                    }
                    if (!effects.DamageEffects.TryGetValue(EDamageEffectType.RadExposure, out JsonType.DamageEffectSpecification cure))
                    {
                        return;
                    }
                    if (kit != null && kit.MaxHpResource > 0)
                    {
                        __state += Meters.TreatRadiationWithKit(templateId, cure.Cost, resource);
                        return;
                    }
                    if (resource < cure.Cost)
                    {
                        return;
                    }
                    Meters.TreatRadiation(templateId, cure.Duration);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not apply the radiation treatment: {e.Message}");
                }
            }

            [PatchPostfix]
            private static void Postfix(ActiveHealthController.MedEffect __instance, int __state)
            {
                if (__state > 0 && __instance._medKit != null)
                {
                    MedKitComponent kit = __instance._medKit;
                    kit.HpResource = Mathf.Max(0f, kit.HpResource - __state);
                    kit.Item.RaiseRefreshEvent();
                    __instance.HealthController.NetworkSyncEffectMedResource(__instance, kit.HpResource);
                }
            }
        }

        private class HealthPanelPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EFT.UI.Health.HealthParametersPanel), nameof(EFT.UI.Health.HealthParametersPanel.UpdateMainHealthAndStats));
            }

            [PatchPostfix]
            private static void Postfix(EFT.UI.Health.HealthParametersPanel __instance)
            {
                try
                {
                    string profile = Singleton<GameWorld>.Instance?.MainPlayer?.ProfileId ?? SPT.Reflection.Utils.PatchConstants.BackEndSession?.Profile?.Id;
                    if (__instance._radiation != null && Enabled.Value)
                    {
                        float level = Meters.Radiation.Peek(profile);
                        __instance._radiation.SetParameterValue(new ValueStruct { Current = level, Minimum = 0f, Maximum = Meter.Maximum }, 15f, 0, true, true);
                        RateReadout.For(__instance._radiation, __instance._hydration, new Vector2(10f, 5f))?.Set(Meters.Radiation.PeekRate(profile));
                    }
                    if (__instance._poisoning != null && Toxicity.Enabled.Value)
                    {
                        float level = Meters.Toxicity.Peek(profile);
                        __instance._poisoning.SetParameterValue(new ValueStruct { Current = level, Minimum = 0f, Maximum = Meter.Maximum }, 25f, 0, true, true);
                        RateReadout.For(__instance._poisoning, __instance._hydration, new Vector2(10f, 10f))?.Set(Meters.Toxicity.PeekRate(profile));
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not show the radiation value: {e.Message}");
                }
            }
        }

        private class FaceShieldPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EFT.CameraControl.PlayerCameraController), nameof(EFT.CameraControl.PlayerCameraController.OnFaceShieldComponentChanged));
            }

            [PatchPostfix]
            private static void Postfix()
            {
                RadiationRunner.FaceShieldChanged();
            }
        }

        private class BreathingPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.SetupSoundsFromFaceShieldComponent));
            }

            [PatchPostfix]
            private static void Postfix(Player __instance)
            {
                try
                {
                    if (!__instance.IsYourPlayer || __instance.Muffled || !MaskEffects.Value || !MaskBreathing.Value || !WearsMask(__instance))
                    {
                        return;
                    }
                    __instance.Muffled = true;
                    if (__instance.FirstPersonPointOfView)
                    {
                        __instance.UpdateBreathStatus();
                    }
                    __instance.UpdateMuffledState();
                    __instance.SendVoiceMuffledState(true);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Gas Masks] Could not start the mask breathing: {e.Message}");
                }
            }
        }
    }
}
