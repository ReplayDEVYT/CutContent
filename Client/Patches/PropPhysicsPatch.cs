using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static partial class PropPhysics
    {
        private class Profile
        {
            public string Name;
            public SmallPhysicsObject.EPhysicsObjectType Sound;
            public SmallPhysicsObject.EPhysicsObjectType? SmallSound;
            public float SmallSoundSize;
            public MaterialType Material;
            public float EffectiveDensity;
            public float MinMass;
            public float MaxMass;
            public float MaxSize;
            public float Drag = 0.05f;
            public float AngularDrag = 0.1f;
            public string[] Keywords;
            public string[] Qualifiers;
            public MaterialType[] QualifierMaterials;
            public MaterialType[] AllowedMaterials;
            public string[] Excludes;
            public bool IgnoreSizeLimit;
        }

        private static readonly Profile[] Profiles =
        {
            new Profile
            {
                Name = "Monitor", Sound = SmallPhysicsObject.EPhysicsObjectType.Monitor, Material = MaterialType.Plastic,
                EffectiveDensity = 160f, MinMass = 2f, MaxMass = 25f, MaxSize = 0.8f,
                Keywords = new[] { "monitor", "crt", "lcd", "television", "tv" },
                Excludes = new[] { "stand", "mount", "holder", "bracket" }
            },
            new Profile
            {
                Name = "CoolerBottle", Sound = SmallPhysicsObject.EPhysicsObjectType.CoolerBottle, Material = MaterialType.Plastic,
                EffectiveDensity = 25f, MinMass = 0.5f, MaxMass = 1.5f, MaxSize = 0.6f,
                Keywords = new[] { "cooler", "kuler" },
                Qualifiers = new[] { "bottle", "jug" }
            },
            new Profile
            {
                Name = "Battery", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.MetalThin,
                EffectiveDensity = 1350f, MinMass = 5f, MaxMass = 25f, MaxSize = 0.5f,
                Keywords = new[] { "battery", "akb", "accumulator" }
            },
            new Profile
            {
                Name = "HardHat", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 27f, MinMass = 0.3f, MaxMass = 0.7f, MaxSize = 0.45f,
                Keywords = new[] { "helmet", "hardhat" },
                Excludes = new[] { "military", "armor" }
            },
            new Profile
            {
                Name = "PlasticJerrycan", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 35f, MinMass = 0.6f, MaxMass = 2.5f, MaxSize = 0.6f,
                Keywords = new[] { "jerrycan", "canister", "kanistra", "bidon" },
                Qualifiers = new[] { "plastic" },
                QualifierMaterials = new[] { MaterialType.Plastic }
            },
            new Profile
            {
                Name = "Jerrycan", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.MetalThin,
                EffectiveDensity = 140f, MinMass = 2f, MaxMass = 6f, MaxSize = 0.6f,
                Keywords = new[] { "jerrycan", "canister", "kanistra", "bidon" }
            },
            new Profile
            {
                Name = "GasCylinder", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.MetalThick,
                EffectiveDensity = 750f, MinMass = 4f, MaxMass = 70f, MaxSize = 1.7f, Drag = 0.02f,
                Keywords = new[] { "cylinder", "balloon", "propane", "oxygen" },
                Excludes = new[] { "hydraulic", "engine" }
            },
            new Profile
            {
                Name = "PlasticBarrel", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 80f, MinMass = 2f, MaxMass = 15f, MaxSize = 1.1f,
                Keywords = new[] { "barrel", "drum" },
                Qualifiers = new[] { "plastic" },
                QualifierMaterials = new[] { MaterialType.Plastic }
            },
            new Profile
            {
                Name = "Barrel", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.MetalThick,
                EffectiveDensity = 550f, MinMass = 4f, MaxMass = 70f, MaxSize = 1.7f, Drag = 0.02f,
                Keywords = new[] { "barrel", "drum" },
                Excludes = new[] { "fire", "flame", "burn", "burning", "campfire", "bonfire", "koster", "smoke", "brazier", "stove" }
            },
            new Profile
            {
                Name = "PlasticBucket", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 15f, MinMass = 0.2f, MaxMass = 1.5f, MaxSize = 0.5f,
                Keywords = new[] { "bucket" },
                Qualifiers = new[] { "plastic" },
                QualifierMaterials = new[] { MaterialType.Plastic }
            },
            new Profile
            {
                Name = "Bucket", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.MetalThin,
                EffectiveDensity = 25f, MinMass = 0.3f, MaxMass = 3f, MaxSize = 0.5f,
                Keywords = new[] { "bucket" }
            },
            new Profile
            {
                Name = "Extinguisher", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.MetalThin,
                EffectiveDensity = 700f, MinMass = 2f, MaxMass = 12f, MaxSize = 0.8f,
                Keywords = new[] { "extinguisher" }
            },
            new Profile
            {
                Name = "PlasticBottle", Sound = SmallPhysicsObject.EPhysicsObjectType.SmallPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 40f, MinMass = 0.02f, MaxMass = 1.2f, MaxSize = 0.4f, Drag = 0.1f,
                Keywords = new[] { "bottle" },
                Qualifiers = new[] { "plastic", "water", "pet", "shampoo" },
                QualifierMaterials = new[] { MaterialType.Plastic }
            },
            new Profile
            {
                Name = "GlassBottle", Sound = SmallPhysicsObject.EPhysicsObjectType.GlassBottle, Material = MaterialType.Glass,
                EffectiveDensity = 230f, MinMass = 0.15f, MaxMass = 1.6f, MaxSize = 0.45f, AngularDrag = 0.05f,
                Keywords = new[] { "bottle", "jar", "flask", "wine", "vodka", "beer", "whiskey", "whisky", "champagne", "liquor", "cognac", "brandy", "moonshine", "baltika", "heineken" }
            },
            new Profile
            {
                Name = "SmallPlastic", Sound = SmallPhysicsObject.EPhysicsObjectType.SmallPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 60f, MinMass = 0.02f, MaxMass = 1.2f, MaxSize = 0.4f, Drag = 0.1f,
                Keywords = new[] { "shampoo", "spray", "aerosol", "detergent", "soap" }
            },
            new Profile
            {
                Name = "TinCan", Sound = SmallPhysicsObject.EPhysicsObjectType.TincanEmpty, Material = MaterialType.MetalThin,
                EffectiveDensity = 40f, MinMass = 0.01f, MaxMass = 0.5f, MaxSize = 0.25f, Drag = 0.1f, AngularDrag = 0.05f,
                Keywords = new[] { "can", "tincan", "soda", "cola", "tin", "beercan" }
            },
            new Profile
            {
                Name = "PaperCup", Sound = SmallPhysicsObject.EPhysicsObjectType.SmallPaper, Material = MaterialType.GarbagePaper,
                EffectiveDensity = 20f, MinMass = 0.005f, MaxMass = 0.03f, MaxSize = 0.2f, Drag = 0.5f, AngularDrag = 0.3f,
                Keywords = new[] { "cup" },
                Qualifiers = new[] { "paper" }
            },
            new Profile
            {
                Name = "Cup", Sound = SmallPhysicsObject.EPhysicsObjectType.Cup, Material = MaterialType.GenericHard,
                EffectiveDensity = 400f, MinMass = 0.01f, MaxMass = 0.5f, MaxSize = 0.2f, AngularDrag = 0.05f,
                Keywords = new[] { "cup", "mug", "teacup" },
                Excludes = new[] { "segment", "cupboard" }
            },
            new Profile
            {
                Name = "Books", Sound = SmallPhysicsObject.EPhysicsObjectType.Books, Material = MaterialType.Cardboard,
                EffectiveDensity = 500f, MinMass = 0.1f, MaxMass = 3f, MaxSize = 0.5f, Drag = 0.1f, AngularDrag = 0.2f,
                Keywords = new[] { "book", "books", "magazine", "binder", "folder", "journal" },
                Excludes = new[] { "notebook", "bookshelf", "bookcase" }
            },
            new Profile
            {
                Name = "SmallPaper", Sound = SmallPhysicsObject.EPhysicsObjectType.SmallPaper, Material = MaterialType.GarbagePaper,
                EffectiveDensity = 40f, MinMass = 0.005f, MaxMass = 0.3f, MaxSize = 0.5f, Drag = 1.5f, AngularDrag = 1f,
                Keywords = new[] { "paper", "papers", "newspaper", "document", "documents", "envelope" }
            },
            new Profile
            {
                Name = "SmallBox", Sound = SmallPhysicsObject.EPhysicsObjectType.SmallBox, Material = MaterialType.Cardboard,
                EffectiveDensity = 150f, MinMass = 0.02f, MaxMass = 1.5f, MaxSize = 0.35f, Drag = 0.1f,
                Keywords = new[] { "matchbox", "shoebox", "cigarette", "cigarettes", "cigs", "cereal", "juice", "milk", "pills" }
            },
            new Profile
            {
                Name = "CardBoard", Sound = SmallPhysicsObject.EPhysicsObjectType.CardBoard, Material = MaterialType.Cardboard,
                SmallSound = SmallPhysicsObject.EPhysicsObjectType.SmallBox, SmallSoundSize = 0.3f,
                EffectiveDensity = 15f, MinMass = 0.1f, MaxMass = 6f, MaxSize = 0.9f, Drag = 0.2f, AngularDrag = 0.2f,
                Keywords = new[] { "cardboard", "carton", "box", "boxes", "package", "parcel", "card" },
                AllowedMaterials = new[] { MaterialType.Cardboard, MaterialType.GarbagePaper, MaterialType.GenericSoft, MaterialType.Fabric },
                Excludes = new[] { "toolbox", "fusebox", "mailbox", "icebox", "firebox", "jackbox", "electric", "distrib", "switch", "switchbox", "breaker", "metal", "metall", "wood", "wooden", "woodbox", "ammo", "ammobox", "weapon", "gun", "safe", "military", "scontainer", "plastic", "fire" }
            },
            new Profile
            {
                Name = "WoodenCrate", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.WoodThick,
                EffectiveDensity = 120f, MinMass = 2f, MaxMass = 30f, MaxSize = 1.0f,
                Keywords = new[] { "box", "crate", "woodbox" },
                Qualifiers = new[] { "wood", "wooden", "woodbox" },
                QualifierMaterials = new[] { MaterialType.WoodThin, MaterialType.WoodThick },
                Excludes = new[] { "ammo", "ammobox", "weapon", "military", "scontainer", "electric", "fire" }
            },
            new Profile
            {
                Name = "MetalCandlestick", Sound = SmallPhysicsObject.EPhysicsObjectType.MetalCandlestick, Material = MaterialType.MetalThin,
                EffectiveDensity = 250f, MinMass = 0.2f, MaxMass = 3f, MaxSize = 0.5f,
                Keywords = new[] { "candlestick", "candleholder", "candelabra", "statuette", "figurine" }
            },
            new Profile
            {
                Name = "HardPlastic", Sound = SmallPhysicsObject.EPhysicsObjectType.HardPlastic, Material = MaterialType.Plastic,
                EffectiveDensity = 120f, MinMass = 0.1f, MaxMass = 5f, MaxSize = 0.6f,
                Keywords = new[] { "keyboard", "phone", "telephone", "radio", "kettle", "tray", "plastic", "toy", "remote", "calculator" }
            },
            new Profile
            {
                Name = "MediumGeneric", Sound = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric, Material = MaterialType.GenericHard,
                EffectiveDensity = 200f, MinMass = 0.2f, MaxMass = 6f, MaxSize = 0.6f,
                Keywords = new[] { "vase", "pot", "pan", "teapot", "saucepan", "ball" },
                Excludes = new[] { "flowerpot", "plant" }
            }
        };

        private static readonly HashSet<string> GlobalExcludes = new HashSet<string>
        {
            "shelf", "rack", "table", "desk", "cabinet", "cupboard", "pallet", "pile", "stack", "heap", "group", "grp",
            "wall", "floor", "door", "window", "decal", "collider", "colider", "shadow", "container", "truck", "car",
            "sign", "poster", "big", "large", "huge", "fence", "combined", "occluder", "trigger", "ventilation", "pipe",
            "pipeline", "valve", "hatch", "fittings", "cistern", "ladder", "crane", "debris", "sandbox", "puddle", "scatter",
            "bag", "bags", "chair", "suitcase", "sand"
        };

        private static readonly HashSet<string> HelperTokens = new HashSet<string>
        {
            "shadow", "decal", "collider", "colider", "ballistic"
        };

        private static readonly HashSet<string> GenericTokens = new HashSet<string>
        {
            "model", "mesh", "geo", "geometry", "lod", "object", "obj", "default", "polysurface"
        };

        private const string LoadingStatus = "Building physics props...";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _fixBuiltInProps;
        private static ConfigEntry<bool> _logConverted;
        private static ConfigEntry<bool> _logUnmatched;
        private static ConfigEntry<float> _minPropSize;
        private static ConfigEntry<float> _sizeLimitMultiplier;
        private static ConfigEntry<int> _maxPropsPerRaid;
        private static ConfigEntry<int> _objectsPerFrame;
        private static ConfigEntry<float> _massMultiplier;
        private static ConfigEntry<float> _touchVelocity;
        private static ConfigEntry<float> _bulletMomentumTransfer;
        private static ConfigEntry<float> _massResponse;
        private static ConfigEntry<float> _maxHitVelocity;
        private static ConfigEntry<float> _explosionForceMultiplier;
        private static ConfigEntry<float> _maxExplosionVelocity;
        private static ConfigEntry<float> _explosionUpwardBias;
        private static ConfigEntry<bool> _explosionLineOfSight;
        private static ConfigEntry<float> _spinFactor;

        private const float ChargeKgPerStrength = 0.0015f;
        private const float BlastImpulseConstant = 260f;
        private const float MinBlastDistance = 0.3f;
        private const float HitThrottle = 0.05f;
        private const float SleepThreshold = 0.05f;
        private const float RestingSpeed = 0.5f;
        private const float RestingSpin = 1f;

        private static readonly HashSet<SmallPhysicsObject> Added = new HashSet<SmallPhysicsObject>();
        private static readonly Dictionary<MaterialType, BallisticCollider> BallisticTemplates = new Dictionary<MaterialType, BallisticCollider>();
        private static readonly char[] NameSeparators = { '_', '-', ' ', '.', '(', ')', '[', ']' };
        private static readonly List<Bounds> FireZones = new List<Bounds>();
        private static readonly string[] FireTokens = { "fire", "flame", "burn", "burning", "campfire", "bonfire", "koster", "smoke", "ember", "embers" };
        private const float FireZoneHeight = 1.5f;

        private static readonly AccessTools.FieldRef<SmallPhysicsSystem, List<SmallPhysicsObject>> SystemObjects =
            AccessTools.FieldRefAccess<SmallPhysicsSystem, List<SmallPhysicsObject>>("_smallPhysicsObjects");
        private static readonly AccessTools.FieldRef<SmallPhysicsSystem> SystemInstance =
            AccessTools.StaticFieldRefAccess<SmallPhysicsSystem>(AccessTools.Field(typeof(SmallPhysicsSystem), "_instance"));
        private static readonly AccessTools.FieldRef<SmallPhysicsObject, SoundBank> CollisionSoundBank =
            AccessTools.FieldRefAccess<SmallPhysicsObject, SoundBank>("_collisionSoundBank");
        private static readonly AccessTools.FieldRef<SmallPhysicsObject, float> LastHitTime =
            AccessTools.FieldRefAccess<SmallPhysicsObject, float>("_lastHitTime");
        private static readonly AccessTools.FieldRef<SmallPhysicsObject, Coroutine> DestroyCoroutine =
            AccessTools.FieldRefAccess<SmallPhysicsObject, Coroutine>("_destroyCoroutine");

        private static readonly Stack<float> ShotMomentum = new Stack<float>();
        private static SmallPhysicsObject _touching;

        private class ScanStats
        {
            public int Candidates;
            public int Matched;
            public int StaticBatched;
            public int Rejected;
            public int Converted;
            public int Capped;
            public int Custom;
            public readonly Dictionary<string, int> PerCategory = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Unmatched = new Dictionary<string, int>();
        }

        public static void Enable(ConfigFile config)
        {
            const string general = "Prop Physics";
            const string detection = "Prop Physics - Detection";
            const string forces = "Prop Physics - Forces";

            _enabled = ConfigHelper.Feature(config, FeatureCategory.Environment, "Props", "Prop Physics", false, "Off by default because it costs performance. Adds EFT's SmallPhysicsObject component to props detected by name and mesh. Takes effect next raid.", general, "Enabled");
            ConfigHelper.Experimental(_enabled);
            _fixBuiltInProps = config.Bind(general, "Fix Built-In Props", true, ConfigHelper.Advanced("Also use the corrected mass-based impulses and explosion handling on props BSG already made physical."));
            _logConverted = config.Bind(general, "Log Converted Props", false, ConfigHelper.Advanced("Log every converted prop with its category, mass and size."));
            _logUnmatched = config.Bind(general, "Log Unmatched Names", false, ConfigHelper.Advanced("Log the most common prop names that matched no category, to help tune detection."));

            _minPropSize = config.Bind(detection, "Min Prop Size", 0.04f, ConfigHelper.Advanced("Smallest prop dimension in meters.", new AcceptableValueRange<float>(0.01f, 0.5f)));
            _sizeLimitMultiplier = config.Bind(detection, "Size Limit Multiplier", 1f, ConfigHelper.Advanced("Scales every category's maximum prop size.", new AcceptableValueRange<float>(0.25f, 3f)));
            _maxPropsPerRaid = config.Bind(detection, "Max Props Per Raid", 1500, new ConfigDescription("Upper limit on converted props per raid.", new AcceptableValueRange<int>(0, 10000)));
            _objectsPerFrame = config.Bind(detection, "Objects Per Frame", 1500, ConfigHelper.Advanced("How many objects the loading step checks per frame.", new AcceptableValueRange<int>(100, 20000)));

            _massMultiplier = config.Bind(forces, "Mass Multiplier", 1f, ConfigHelper.Advanced("Scales every estimated prop mass.", new AcceptableValueRange<float>(0.1f, 5f)));
            _touchVelocity = config.Bind(forces, "Player Touch Velocity", 1.0f, ConfigHelper.Advanced("Speed in m/s a prop gets when a player walks into it.", new AcceptableValueRange<float>(0f, 5f)));
            _bulletMomentumTransfer = config.Bind(forces, "Hit Momentum Transfer", 0.75f, ConfigHelper.Advanced("Fraction of a bullet's momentum (mass x impact speed) given to the prop.", new AcceptableValueRange<float>(0f, 1f)));
            _massResponse = config.Bind(forces, "Mass Response", 0.5f, ConfigHelper.Advanced("How strongly mass resists hits and blasts. 1 is physically exact (heavy props barely move), lower values let heavy props react more.", new AcceptableValueRange<float>(0.2f, 1f)));
            _maxHitVelocity = config.Bind(forces, "Max Hit Velocity", 15f, ConfigHelper.Advanced("Cap on the speed change from a single bullet or fragment hit, in m/s.", new AcceptableValueRange<float>(1f, 50f)));
            _explosionForceMultiplier = config.Bind(forces, "Explosion Force Multiplier", 1f, ConfigHelper.Advanced("Scales the blast impulse. 1 is the physical estimate.", new AcceptableValueRange<float>(0f, 10f)));
            _maxExplosionVelocity = config.Bind(forces, "Max Explosion Velocity", 25f, ConfigHelper.Advanced("Cap on the speed change from one explosion, in m/s.", new AcceptableValueRange<float>(1f, 60f)));
            _explosionUpwardBias = config.Bind(forces, "Explosion Upward Bias", 0.25f, ConfigHelper.Advanced("How much the blast direction is tilted upward.", new AcceptableValueRange<float>(0f, 1f)));
            _explosionLineOfSight = config.Bind(forces, "Explosion Line Of Sight", true, ConfigHelper.Advanced("Only push props the blast has a clear line to."));
            _spinFactor = config.Bind(forces, "Spin Factor", 0.15f, ConfigHelper.Advanced("Extra random spin added on impact, relative to the impulse.", new AcceptableValueRange<float>(0f, 1f)));

            BindFilters(config);

            new SpawnLootPatch().Enable();
            new ShotDelegatePatch(typeof(GameWorld)).Enable();
            new ShotDelegatePatch(typeof(ClientGameWorld)).Enable();
            new OnHitPatch().Enable();
            new CheckPlayerCollisionPatch().Enable();
            new PlayCollisionPatch().Enable();
            new ExplosionPatch().Enable();
            new AwakePatch().Enable();
            new CollisionSoundPatch().Enable();
            new SupportRigidbodyPatch().Enable();
        }

        private static bool ShouldFix(SmallPhysicsObject spo)
        {
            return Added.Contains(spo) || _fixBuiltInProps.Value;
        }

        private static async Task BuildAfter(Task original, AbstractGame game)
        {
            await original;

            if (!_enabled.Value || Singleton<GameWorld>.Instance == null || Singleton<GameWorld>.Instance is HideoutGameWorld)
            {
                return;
            }

            try
            {
                await Build(game);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[PropPhysics] Building physics props failed: {e}");
            }
        }

        private static async Task Build(AbstractGame game)
        {
            game.SetMatchmakerStatus(LoadingStatus, 0f);
            await Task.Yield();

            Added.RemoveWhere(x => x == null);
            BallisticTemplates.Clear();
            foreach (BallisticCollider bc in UnityEngine.Object.FindObjectsOfType<BallisticCollider>())
            {
                if (bc.GetType() == typeof(BallisticCollider) && !bc.IsUnsetup() && !BallisticTemplates.ContainsKey(bc.TypeOfMaterial))
                {
                    BallisticTemplates[bc.TypeOfMaterial] = bc;
                }
            }

            CollectFireZones();
            LoadFilters();

            LODGroup[] lodGroups = UnityEngine.Object.FindObjectsOfType<LODGroup>();
            MeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            int total = lodGroups.Length + renderers.Length;
            int perFrame = _objectsPerFrame.Value;
            var visited = new HashSet<Transform>();
            var stats = new ScanStats();
            int processed = 0;

            foreach (LODGroup lodGroup in lodGroups)
            {
                if (++processed % perFrame == 0)
                {
                    game.SetMatchmakerStatus(LoadingStatus, (float)processed / total);
                    await Task.Yield();
                }
                if (lodGroup == null || !visited.Add(lodGroup.transform))
                {
                    continue;
                }
                Process(lodGroup.transform, lodGroup, stats);
            }

            foreach (MeshRenderer renderer in renderers)
            {
                if (++processed % perFrame == 0)
                {
                    game.SetMatchmakerStatus(LoadingStatus, (float)processed / total);
                    await Task.Yield();
                }
                if (renderer == null || !renderer.enabled || renderer.GetComponentInParent<LODGroup>() != null)
                {
                    continue;
                }

                List<string> tokens = Tokenize(renderer.name).ToList();
                if (tokens.Any(HelperTokens.Contains))
                {
                    continue;
                }

                Transform root = renderer.transform;
                if (tokens.Count == 0 || tokens.All(GenericTokens.Contains))
                {
                    if (root.parent == null)
                    {
                        continue;
                    }
                    root = root.parent;
                }

                if (!visited.Add(root))
                {
                    continue;
                }
                Process(root, null, stats);
            }

            game.SetMatchmakerStatus(LoadingStatus, 1f);
            LogSummary(stats);
        }

        private static void Process(Transform root, LODGroup lodGroup, ScanStats stats)
        {
            stats.Candidates++;

            Renderer[] measured = GetMeasuredRenderers(root, lodGroup);
            if (measured.Length == 0)
            {
                return;
            }

            var names = new List<string> { root.name };
            foreach (Renderer renderer in measured)
            {
                if (Tokenize(renderer.name).Any(HelperTokens.Contains))
                {
                    continue;
                }
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    names.Add(filter.sharedMesh.name);
                }
            }

            if (IsCustomExcluded(names))
            {
                return;
            }

            Profile custom = FindCustomProfile(names);
            if (custom == null && stats.Converted - stats.Custom >= _maxPropsPerRaid.Value)
            {
                stats.Capped++;
                return;
            }

            var materials = new HashSet<MaterialType>(root.GetComponentsInChildren<BallisticCollider>(true).Select(b => b.TypeOfMaterial).Where(m => m != MaterialType.None));
            Profile profile = custom ?? Classify(names, materials);
            if (profile == null)
            {
                if (_logUnmatched.Value)
                {
                    string key = string.Join("_", Tokenize(root.name));
                    stats.Unmatched.TryGetValue(key, out int count);
                    stats.Unmatched[key] = count + 1;
                }
                return;
            }

            stats.Matched++;

            if (root.GetComponentsInChildren<Renderer>(true).Any(r => r.isPartOfStaticBatch))
            {
                stats.StaticBatched++;
                return;
            }

            try
            {
                if (!TryConvert(root, measured, profile, out float mass, out Vector3 size))
                {
                    stats.Rejected++;
                    return;
                }

                stats.Converted++;
                if (custom != null)
                {
                    stats.Custom++;
                }
                stats.PerCategory.TryGetValue(profile.Name, out int count);
                stats.PerCategory[profile.Name] = count + 1;

                if (_logConverted.Value)
                {
                    Plugin.LogSource.LogInfo($"[PropPhysics] {profile.Name} '{GetPath(root)}' mass {mass:F2}kg size {size.x:F2}x{size.y:F2}x{size.z:F2}m");
                }
            }
            catch (Exception e)
            {
                stats.Rejected++;
                Plugin.LogSource.LogWarning($"[PropPhysics] Failed to convert '{GetPath(root)}': {e.Message}");
            }
        }

        private static void LogSummary(ScanStats stats)
        {
            var summary = new StringBuilder();
            summary.Append($"[PropPhysics] Checked {stats.Candidates} props. Name matches: {stats.Matched}, static batched: {stats.StaticBatched}, rejected: {stats.Rejected}, converted: {stats.Converted} ({stats.Custom} from custom filters).");
            foreach (var pair in stats.PerCategory.OrderByDescending(p => p.Value))
            {
                summary.Append($" {pair.Key}={pair.Value}");
            }
            Plugin.LogSource.LogInfo(summary.ToString());

            if (stats.Capped > 0)
            {
                Plugin.LogSource.LogWarning($"[PropPhysics] Max Props Per Raid ({_maxPropsPerRaid.Value}) was reached, {stats.Capped} props were skipped. Custom includes ignore the limit. Raise the limit in the config to convert the rest.");
            }

            if (_logUnmatched.Value && stats.Unmatched.Count > 0)
            {
                Plugin.LogSource.LogInfo("[PropPhysics] Most common unmatched names: " +
                    string.Join(", ", stats.Unmatched.OrderByDescending(p => p.Value).Take(150).Select(p => $"{p.Key} ({p.Value})")));
            }
        }

        private static Profile Classify(IEnumerable<string> names, HashSet<MaterialType> materials)
        {
            var tokens = new HashSet<string>();
            var joined = new StringBuilder();
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                foreach (string token in Tokenize(name))
                {
                    tokens.Add(token);
                    joined.Append(token).Append(' ');
                }
            }

            if (tokens.Count == 0 || tokens.Any(GlobalExcludes.Contains))
            {
                return null;
            }

            string all = joined.ToString();
            foreach (Profile profile in Profiles)
            {
                if (!profile.Keywords.Any(k => Matches(k, tokens, all)))
                {
                    continue;
                }
                if (profile.Excludes != null && profile.Excludes.Any(k => Matches(k, tokens, all)))
                {
                    continue;
                }
                if (profile.Qualifiers != null || profile.QualifierMaterials != null)
                {
                    bool byToken = profile.Qualifiers != null && profile.Qualifiers.Any(k => Matches(k, tokens, all));
                    bool byMaterial = profile.QualifierMaterials != null && profile.QualifierMaterials.Any(materials.Contains);
                    if (!byToken && !byMaterial)
                    {
                        continue;
                    }
                }
                if (profile.AllowedMaterials != null && materials.Count > 0 && !profile.AllowedMaterials.Any(materials.Contains))
                {
                    continue;
                }
                return profile;
            }
            return null;
        }

        private static bool Matches(string keyword, HashSet<string> tokens, string all)
        {
            if (tokens.Contains(keyword))
            {
                return true;
            }
            return keyword.Length >= 5 && all.Contains(keyword);
        }

        private static IEnumerable<string> Tokenize(string name)
        {
            var current = new StringBuilder();
            char previous = '\0';
            foreach (char c in name)
            {
                bool boundary = Array.IndexOf(NameSeparators, c) >= 0 || char.IsDigit(c);
                bool camel = char.IsUpper(c) && char.IsLower(previous);
                if ((boundary || camel) && current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (!boundary)
                {
                    current.Append(char.ToLowerInvariant(c));
                }
                previous = c;
            }
            if (current.Length > 0)
            {
                yield return current.ToString();
            }
        }

        private static void CollectFireZones()
        {
            FireZones.Clear();

            foreach (FlameDamageTrigger trigger in UnityEngine.Object.FindObjectsOfType<FlameDamageTrigger>())
            {
                Collider collider = trigger.GetComponent<Collider>();
                FireZones.Add(collider != null ? collider.bounds : new Bounds(trigger.transform.position, Vector3.one * 0.5f));
            }

            foreach (ParticleSystem particles in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
            {
                if (IsFireNamed(particles.transform))
                {
                    FireZones.Add(new Bounds(particles.transform.position, Vector3.one * 0.3f));
                }
            }

            foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (light.type == LightType.Point && IsFireNamed(light.transform))
                {
                    FireZones.Add(new Bounds(light.transform.position, Vector3.one * 0.3f));
                }
            }
        }

        private static bool IsFireNamed(Transform transform)
        {
            for (int depth = 0; transform != null && depth < 4; depth++, transform = transform.parent)
            {
                if (Tokenize(transform.name).Any(t => FireTokens.Contains(t)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNearFire(Bounds propBounds)
        {
            if (FireZones.Count == 0)
            {
                return false;
            }

            Bounds zone = propBounds;
            zone.Encapsulate(propBounds.max + Vector3.up * FireZoneHeight);
            zone.Expand(0.1f);
            foreach (Bounds fire in FireZones)
            {
                if (zone.Intersects(fire))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasBlockingComponent(Transform root)
        {
            return root.GetComponentInParent<Rigidbody>() != null
                || root.GetComponentInChildren<Rigidbody>(true) != null
                || root.GetComponentInParent<SmallPhysicsObject>() != null
                || root.GetComponentInChildren<SmallPhysicsObject>(true) != null
                || root.GetComponentInParent<InteractableObject>() != null
                || root.GetComponentInChildren<InteractableObject>(true) != null
                || root.GetComponentInParent<Turnable>() != null
                || root.GetComponentInChildren<Turnable>(true) != null
                || root.GetComponentInParent<WindowBreaker>() != null
                || root.GetComponentInChildren<WindowBreaker>(true) != null
                || root.GetComponentInChildren<ParticleSystem>(true) != null
                || root.GetComponentInChildren<Light>(true) != null
                || root.GetComponentInChildren<FlameDamageTrigger>(true) != null;
        }

        private static bool TryConvert(Transform root, Renderer[] measured, Profile profile, out float mass, out Vector3 size)
        {
            mass = 0f;
            size = Vector3.zero;

            if (measured.Length > 12 || HasBlockingComponent(root))
            {
                return false;
            }

            Bounds localBounds = GetLocalBounds(root, measured);
            size = Vector3.Scale(localBounds.size, Abs(root.lossyScale));
            float maxDim = Mathf.Max(size.x, size.y, size.z);
            float minDim = Mathf.Min(size.x, size.y, size.z);
            bool tooBig = !profile.IgnoreSizeLimit && maxDim > profile.MaxSize * _sizeLimitMultiplier.Value;
            bool tooSmall = !profile.IgnoreSizeLimit && maxDim < _minPropSize.Value;
            if (tooBig || tooSmall || minDim <= 0.001f)
            {
                return false;
            }

            Bounds worldBounds = measured[0].bounds;
            foreach (Renderer renderer in measured)
            {
                worldBounds.Encapsulate(renderer.bounds);
            }
            if (IsNearFire(worldBounds))
            {
                return false;
            }

            float volume = size.x * size.y * size.z;
            mass =Mathf.Clamp(volume * profile.EffectiveDensity, profile.MinMass, profile.MaxMass) * _massMultiplier.Value;

            foreach (MeshCollider meshCollider in root.GetComponentsInChildren<MeshCollider>(true))
            {
                MakeDynamicSafe(meshCollider);
            }
            var colliders = root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList();

            int lootLayer = LayerMask.NameToLayer("Loot");
            int highPolyLayer = LayerMask.NameToLayer("HighPolyCollider");
            Vector3 boxSize = localBounds.size * 0.98f;

            AddBox(root, "ReClassic_PhysicsSupport", lootLayer, localBounds.center, boxSize);

            if (root.GetComponentsInChildren<BallisticCollider>(true).Length == 0)
            {
                Collider target = colliders.FirstOrDefault(c => c.gameObject.layer == highPolyLayer)
                    ?? AddBox(root, "ReClassic_BallisticCollider", highPolyLayer, localBounds.center, boxSize);

                BallisticCollider bc = target.gameObject.AddComponent<BallisticCollider>();
                if (BallisticTemplates.TryGetValue(profile.Material, out BallisticCollider template) && template != null)
                {
                    bc.TakeSettingsFrom(template);
                }
                else
                {
                    bc.TypeOfMaterial = profile.Material;
                }
                bc.Awake();
            }

            Rigidbody rb = root.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.drag = profile.Drag;
            rb.angularDrag = profile.AngularDrag;
            rb.useGravity = true;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.sleepThreshold = Mathf.Max(rb.sleepThreshold, SleepThreshold);

            SmallPhysicsObject spo = root.gameObject.AddComponent<SmallPhysicsObject>();
            spo.SoundType = profile.SmallSound.HasValue && maxDim <= profile.SmallSoundSize ? profile.SmallSound.Value : profile.Sound;
            spo.OnHitForceMultiplier = 1f;
            spo.OnTouchForceMultiplier = 1f;
            spo.OnBlowUpForceMultiplier = 1f;
            spo.TimeOfLife = -1f;

            if (spo.Collider == null || spo.Rigidbodies.Count == 0)
            {
                UnityEngine.Object.Destroy(spo);
                return false;
            }

            Added.Add(spo);
            return true;
        }

        private static void MakeDynamicSafe(MeshCollider meshCollider)
        {
            if (meshCollider.convex)
            {
                return;
            }

            Mesh mesh = meshCollider.sharedMesh;
            if (mesh != null && mesh.isReadable)
            {
                meshCollider.convex = true;
                return;
            }

            GameObject go = meshCollider.gameObject;
            bool enabled = meshCollider.enabled;
            bool isTrigger = meshCollider.isTrigger;
            PhysicMaterial material = meshCollider.sharedMaterial;
            UnityEngine.Object.DestroyImmediate(meshCollider);

            if (mesh == null)
            {
                return;
            }

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = mesh.bounds.size;
            box.sharedMaterial = material;
            box.isTrigger = isTrigger;
            box.enabled = enabled;
        }

        private static void SleepIfResting(SmallPhysicsObject spo)
        {
            if (spo.Rigidbodies == null)
            {
                return;
            }
            foreach (Rigidbody body in spo.Rigidbodies)
            {
                if (body == null || body.isKinematic || body.IsSleeping())
                {
                    continue;
                }
                if (body.velocity.sqrMagnitude < RestingSpeed * RestingSpeed && body.angularVelocity.sqrMagnitude < RestingSpin * RestingSpin)
                {
                    body.Sleep();
                }
            }
        }

        private static Vector3 ScaleForMass(Vector3 impulse, SmallPhysicsObject spo)
        {
            float total = TotalMass(spo);
            if (total <= 0f)
            {
                return impulse;
            }
            return impulse * Mathf.Pow(total, 1f - _massResponse.Value);
        }

        private static Renderer[] GetMeasuredRenderers(Transform root, LODGroup lodGroup)
        {
            if (lodGroup != null)
            {
                LOD[] lods = lodGroup.GetLODs();
                if (lods.Length > 0)
                {
                    Renderer[] lod0 = lods[0].renderers.Where(r => r != null && r.GetComponent<MeshFilter>() != null).ToArray();
                    if (lod0.Length > 0)
                    {
                        return lod0;
                    }
                }
            }
            return root.GetComponentsInChildren<MeshRenderer>(false).Cast<Renderer>().ToArray();
        }

        private static Bounds GetLocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = default;
            foreach (Renderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    continue;
                }

                Bounds meshBounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = root.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return result;
        }

        private static BoxCollider AddBox(Transform root, string name, int layer, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(root, false);
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            return box;
        }

        private static Vector3 Abs(Vector3 v)
        {
            return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        }

        private static string GetPath(Transform t)
        {
            return t.parent != null ? GetPath(t.parent) + "/" + t.name : t.name;
        }

        private static float TotalMass(SmallPhysicsObject spo)
        {
            float total = 0f;
            foreach (Rigidbody rb in spo.Rigidbodies)
            {
                if (rb != null && rb.gameObject.activeInHierarchy)
                {
                    total += rb.mass;
                }
            }
            return total;
        }

        private static Bounds GetBounds(SmallPhysicsObject spo)
        {
            return spo.Collider != null ? spo.Collider.bounds : new Bounds(spo.transform.position, Vector3.one * 0.1f);
        }

        private static void Apply(SmallPhysicsObject spo, Vector3 impulse, Vector3 point, float maxVelocity, bool onlySleeping)
        {
            if (spo.SolidObject != null)
            {
                spo.SolidObject.SetActive(false);
            }

            bool fragmented = false;
            if (spo.FragmentedObject != null && !spo.FragmentedObject.activeInHierarchy)
            {
                spo.FragmentedObject.SetActive(true);
                fragmented = true;
            }

            float total = TotalMass(spo);
            if (total <= 0f)
            {
                return;
            }

            float maxImpulse = total * maxVelocity;
            if (impulse.magnitude > maxImpulse)
            {
                impulse = impulse.normalized * maxImpulse;
            }

            Bounds bounds = GetBounds(spo);
            Vector3 at = bounds.ClosestPoint(point);
            float lever = Mathf.Max(bounds.extents.magnitude, 0.05f);
            bool applied = false;

            foreach (Rigidbody rb in spo.Rigidbodies)
            {
                if (rb == null || !rb.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (onlySleeping && !fragmented && !rb.isKinematic && !rb.IsSleeping())
                {
                    continue;
                }

                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                }

                Vector3 share = impulse * (rb.mass / total);
                rb.AddForceAtPosition(share, at, ForceMode.Impulse);
                rb.AddTorque(UnityEngine.Random.onUnitSphere * share.magnitude * lever * _spinFactor.Value, ForceMode.Impulse);
                PhysicsExtensions.UpdateController.SupportRigidbody(rb, 0f);
                applied = true;
            }

            if (!applied)
            {
                return;
            }

            spo.StartPartciles(impulse);

            if (fragmented)
            {
                spo.enabled = false;
            }

            if (spo.TimeOfLife > 0f && DestroyCoroutine(spo) == null)
            {
                DestroyCoroutine(spo) = StaticManager.BeginCoroutine(spo.TimeOfLifeCountDown());
            }
        }

        private static bool IsBlastBlocked(Vector3 origin, Vector3 target, SmallPhysicsObject spo)
        {
            if (!Physics.Linecast(origin, target, out RaycastHit hit, LayersMaskController.GrenadeObstaclesColliderMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }
            return !hit.transform.IsChildOf(spo.transform);
        }

        private static float CrossSection(Bounds bounds)
        {
            Vector3 s = bounds.size;
            return (s.x * s.y + s.y * s.z + s.x * s.z) / 3f;
        }

        private class SpawnLootPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), nameof(BaseLocalGame<EftGamePlayerOwner>.SpawnLoot));
            }

            [PatchPostfix]
            private static void Postfix(BaseLocalGame<EftGamePlayerOwner> __instance, ref Task __result)
            {
                __result = BuildAfter(__result, __instance);
            }
        }

        private class ShotDelegatePatch : ModulePatch
        {
            private readonly Type _type;

            public ShotDelegatePatch(Type type)
            {
                _type = type;
            }

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(_type, nameof(GameWorld.ShotDelegate));
            }

            [PatchPrefix]
            private static void Prefix(Shot shotResult)
            {
                float speed = shotResult.VelocityMagnitude > 0f ? shotResult.VelocityMagnitude : shotResult.Speed;
                ShotMomentum.Push(shotResult.BulletMassGram * 0.001f * speed);
            }

            [PatchFinalizer]
            private static void Finalizer()
            {
                if (ShotMomentum.Count > 0)
                {
                    ShotMomentum.Pop();
                }
            }
        }

        private class OnHitPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SmallPhysicsObject), nameof(SmallPhysicsObject.OnHit));
            }

            [PatchPrefix]
            private static bool Prefix(SmallPhysicsObject __instance, DamageInfo damageInfo)
            {
                if (!ShouldFix(__instance))
                {
                    return true;
                }

                if (__instance.OnHitForceMultiplier < 0f || Time.time - LastHitTime(__instance) < HitThrottle)
                {
                    return false;
                }
                LastHitTime(__instance) = Time.time;

                float momentum = ShotMomentum.Count > 0 ? ShotMomentum.Peek() : 0f;
                float impulse = momentum > 0f
                    ? momentum * _bulletMomentumTransfer.Value
                    : Mathf.Clamp(damageInfo.Damage * 0.02f, 0.2f, 3f);

                Vector3 direction = damageInfo.Direction.sqrMagnitude > 0f ? damageInfo.Direction.normalized : Vector3.down;
                Apply(__instance, ScaleForMass(direction * impulse, __instance), damageInfo.HitPoint, _maxHitVelocity.Value, false);
                return false;
            }
        }

        private class CheckPlayerCollisionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SmallPhysicsObject), nameof(SmallPhysicsObject.CheckPlayerCollision));
            }

            [PatchPrefix]
            private static void Prefix(SmallPhysicsObject __instance)
            {
                _touching = __instance;
            }

            [PatchFinalizer]
            private static void Finalizer()
            {
                _touching = null;
            }
        }

        private class PlayCollisionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SmallPhysicsObject), nameof(SmallPhysicsObject.PlayCollision));
            }

            [PatchPrefix]
            private static bool Prefix(SmallPhysicsObject __instance, Vector3 force, Vector3 position)
            {
                SleepIfResting(__instance);

                if (_touching != __instance || !ShouldFix(__instance))
                {
                    return true;
                }

                Vector3 direction = force;
                direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
                direction = (direction + Vector3.up * 0.15f).normalized;

                float total = TotalMass(__instance);
                float speed = _touchVelocity.Value;
                Apply(__instance, direction * total * speed, position + Vector3.up * 0.25f, speed, true);
                return false;
            }
        }

        private class AwakePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SmallPhysicsObject), nameof(SmallPhysicsObject.Awake));
            }

            [PatchPostfix]
            private static void Postfix(SmallPhysicsObject __instance)
            {
                if (__instance.Rigidbodies == null)
                {
                    return;
                }
                foreach (Rigidbody body in __instance.Rigidbodies)
                {
                    if (body != null)
                    {
                        body.sleepThreshold = Mathf.Max(body.sleepThreshold, SleepThreshold);
                    }
                }
            }
        }

        private class CollisionSoundPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SmallPhysicsObject), nameof(SmallPhysicsObject.OnCollisionHandler));
            }

            [PatchPrefix]
            private static bool Prefix(SmallPhysicsObject __instance, bool resetSound)
            {
                if (Singleton<BetterAudio>.Instance == null)
                {
                    return false;
                }
                if (CollisionSoundBank(__instance) != null && !resetSound)
                {
                    return true;
                }

                try
                {
                    __instance.SetupSoundBank();
                }
                catch (Exception)
                {
                    CollisionSoundBank(__instance) = null;
                }

                if (CollisionSoundBank(__instance) == null && __instance.SoundType != SmallPhysicsObject.EPhysicsObjectType.MediumGeneric)
                {
                    __instance.SoundType = SmallPhysicsObject.EPhysicsObjectType.MediumGeneric;
                    try
                    {
                        __instance.SetupSoundBank();
                    }
                    catch (Exception)
                    {
                        CollisionSoundBank(__instance) = null;
                    }
                }

                return CollisionSoundBank(__instance) != null;
            }
        }

        private class ExplosionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(ExplosionSharedMethods), nameof(ExplosionSharedMethods.Explosion));
            }

            [PatchPostfix]
            private static void Postfix(IExplosiveItem grenadeItem, Vector3 explosionPosition)
            {
                if (grenadeItem == null || grenadeItem.IsDummy || grenadeItem.GetStrength <= 0f)
                {
                    return;
                }

                SmallPhysicsSystem system = SystemInstance();
                if (system == null)
                {
                    return;
                }

                float radius = Mathf.Max(grenadeItem.MaxExplosionDistance * 2f, 1f);
                float charge = grenadeItem.GetStrength * ChargeKgPerStrength;
                float chargeFactor = Mathf.Pow(charge, 2f / 3f);

                foreach (SmallPhysicsObject spo in SystemObjects(system).ToArray())
                {
                    if (spo == null || !spo.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Bounds bounds = GetBounds(spo);
                    Vector3 offset = bounds.center - explosionPosition;
                    float distance = offset.magnitude;
                    if (distance > radius)
                    {
                        continue;
                    }

                    if (_explosionLineOfSight.Value && IsBlastBlocked(explosionPosition, bounds.center, spo))
                    {
                        continue;
                    }

                    try
                    {
                        if (!ShouldFix(spo))
                        {
                            SleepIfResting(spo);
                            spo.OnBlowUp(explosionPosition);
                            continue;
                        }

                        float effectiveDistance = Mathf.Max(distance, MinBlastDistance);
                        float specificImpulse = BlastImpulseConstant * chargeFactor / effectiveDistance;
                        float impulse = specificImpulse * CrossSection(bounds) * _explosionForceMultiplier.Value;

                        Vector3 direction = distance > 0.001f ? offset / distance : Vector3.up;
                        direction = (direction + Vector3.up * _explosionUpwardBias.Value).normalized;

                        Apply(spo, ScaleForMass(direction * impulse, spo), bounds.ClosestPoint(explosionPosition), _maxExplosionVelocity.Value, false);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning($"[PropPhysics] Explosion push failed on '{spo.name}': {e.Message}");
                    }
                }
            }
        }
    }
}
