using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Communications;
using EFT.Interactive;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace mizmii.ReClassic.Patches
{
    internal static partial class PropPhysics
    {
        private class FilterRule
        {
            public string Name;
            public string Profile;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public float? Mass = null;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public float? MaxSize = null;
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string Sound = null;
        }

        private class FilterFile
        {
            [JsonProperty("_AvailableProfiles")]
            public string[] AvailableProfiles;
            [JsonProperty("_AvailableSounds")]
            public string[] AvailableSounds;
            public List<FilterRule> Include = new List<FilterRule>();
            public List<string> Exclude = new List<string>();
        }

        private const string FilterFileName = Plugin.Guid + ".PropFilters.json";
        private const float HotkeyRange = 15f;

        private static ConfigEntry<KeyboardShortcut> _addKey;
        private static ConfigEntry<KeyboardShortcut> _excludeKey;
        private static ConfigEntry<string> _hotkeyProfile;

        private static FilterFile _filters = new FilterFile();
        private static readonly List<KeyValuePair<Regex, FilterRule>> IncludeMatchers = new List<KeyValuePair<Regex, FilterRule>>();
        private static readonly List<Regex> ExcludeMatchers = new List<Regex>();
        private static readonly Dictionary<FilterRule, Profile> ResolvedRules = new Dictionary<FilterRule, Profile>();

        private static string FilterPath => Path.Combine(BepInEx.Paths.ConfigPath, FilterFileName);

        private static void BindFilters(ConfigFile config)
        {
            const string section = "Prop Physics - Custom Filters";
            string[] profileNames = Profiles.Select(p => p.Name).ToArray();

            _addKey = config.Bind(section, "Add Looked-At Prop", new KeyboardShortcut(KeyCode.Insert, KeyCode.LeftControl),
                $"Adds the prop under your crosshair to the include list in BepInEx/config/{FilterFileName} and makes it physical right away.");
            _excludeKey = config.Bind(section, "Exclude Looked-At Prop", new KeyboardShortcut(KeyCode.Delete, KeyCode.LeftControl),
                $"Adds the prop under your crosshair to the exclude list in BepInEx/config/{FilterFileName} and freezes it if this mod made it physical.");
            _hotkeyProfile = config.Bind(section, "Hotkey Profile", "MediumGeneric",
                new ConfigDescription("Category given to props added with the hotkey. Change it per prop in the JSON file afterwards if needed.", new AcceptableValueList<string>(profileNames)));

            LoadFilters();

            var host = new GameObject("ReClassic_PropPhysicsRuntime");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<FilterHotkeys>();
            host.AddComponent<BodySleeper>();
        }

        private static void LoadFilters()
        {
            FilterFile loaded = null;
            try
            {
                if (File.Exists(FilterPath))
                {
                    loaded = JsonConvert.DeserializeObject<FilterFile>(File.ReadAllText(FilterPath));
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[PropPhysics] Could not read {FilterFileName}, custom filters are off until it is fixed: {e.Message}");
                _filters = new FilterFile();
                RebuildMatchers();
                return;
            }

            bool create = loaded == null;
            _filters = loaded ?? new FilterFile();
            _filters.Include = _filters.Include ?? new List<FilterRule>();
            _filters.Exclude = _filters.Exclude ?? new List<string>();
            _filters.Include.RemoveAll(r => r == null || string.IsNullOrWhiteSpace(r.Name));
            _filters.Exclude.RemoveAll(string.IsNullOrWhiteSpace);

            foreach (FilterRule rule in _filters.Include)
            {
                if (FindProfile(rule.Profile) == null)
                {
                    Plugin.LogSource.LogWarning($"[PropPhysics] Custom filter '{rule.Name}' uses unknown profile '{rule.Profile}', falling back to MediumGeneric.");
                }
            }

            RebuildMatchers();
            if (create)
            {
                SaveFilters();
            }
        }

        private static void SaveFilters()
        {
            try
            {
                _filters.AvailableProfiles = Profiles.Select(p => p.Name).ToArray();
                _filters.AvailableSounds = Enum.GetNames(typeof(SmallPhysicsObject.EPhysicsObjectType));
                File.WriteAllText(FilterPath, JsonConvert.SerializeObject(_filters, Formatting.Indented));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[PropPhysics] Could not save {FilterFileName}: {e.Message}");
            }
        }

        private static void RebuildMatchers()
        {
            IncludeMatchers.Clear();
            ExcludeMatchers.Clear();
            ResolvedRules.Clear();
            foreach (FilterRule rule in _filters.Include)
            {
                IncludeMatchers.Add(new KeyValuePair<Regex, FilterRule>(ToMatcher(rule.Name), rule));
            }
            foreach (string name in _filters.Exclude)
            {
                ExcludeMatchers.Add(ToMatcher(name));
            }
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            name = name.Replace("(Clone)", string.Empty);
            name = Regex.Replace(name, @"\s*\(\d+\)\s*$", string.Empty);
            return name.Trim();
        }

        private static Regex ToMatcher(string pattern)
        {
            string escaped = Regex.Escape(NormalizeName(pattern)).Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool IsCustomExcluded(IEnumerable<string> names)
        {
            if (ExcludeMatchers.Count == 0)
            {
                return false;
            }
            foreach (string name in names)
            {
                string normalized = NormalizeName(name);
                if (ExcludeMatchers.Any(m => m.IsMatch(normalized)))
                {
                    return true;
                }
            }
            return false;
        }

        private static Profile FindCustomProfile(IEnumerable<string> names)
        {
            if (IncludeMatchers.Count == 0)
            {
                return null;
            }
            foreach (string name in names)
            {
                string normalized = NormalizeName(name);
                foreach (KeyValuePair<Regex, FilterRule> matcher in IncludeMatchers)
                {
                    if (matcher.Key.IsMatch(normalized))
                    {
                        return ResolveRule(matcher.Value);
                    }
                }
            }
            return null;
        }

        private static Profile FindProfile(string name)
        {
            return Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static Profile ResolveRule(FilterRule rule)
        {
            if (ResolvedRules.TryGetValue(rule, out Profile resolved))
            {
                return resolved;
            }

            Profile source = FindProfile(rule.Profile) ?? FindProfile("MediumGeneric") ?? Profiles[Profiles.Length - 1];
            resolved = new Profile
            {
                Name = source.Name,
                Sound = source.Sound,
                SmallSound = source.SmallSound,
                SmallSoundSize = source.SmallSoundSize,
                Material = source.Material,
                EffectiveDensity = source.EffectiveDensity,
                MinMass = source.MinMass,
                MaxMass = source.MaxMass,
                MaxSize = source.MaxSize,
                Drag = source.Drag,
                AngularDrag = source.AngularDrag,
                Keywords = source.Keywords
            };

            if (rule.Mass.HasValue && rule.Mass.Value > 0f)
            {
                resolved.MinMass = rule.Mass.Value;
                resolved.MaxMass = rule.Mass.Value;
            }
            if (rule.MaxSize.HasValue && rule.MaxSize.Value > 0f)
            {
                resolved.MaxSize = rule.MaxSize.Value;
            }
            else
            {
                resolved.IgnoreSizeLimit = true;
            }
            if (!string.IsNullOrEmpty(rule.Sound))
            {
                if (Enum.TryParse(rule.Sound, true, out SmallPhysicsObject.EPhysicsObjectType sound))
                {
                    resolved.Sound = sound;
                    resolved.SmallSound = null;
                }
                else
                {
                    Plugin.LogSource.LogWarning($"[PropPhysics] Custom filter '{rule.Name}' uses unknown sound '{rule.Sound}'.");
                }
            }

            ResolvedRules[rule] = resolved;
            return resolved;
        }

        private static void HandleHotkey(bool include)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            Player player = world != null ? world.MainPlayer : null;
            if (player == null || world is HideoutGameWorld)
            {
                return;
            }

            if (!TryGetLookedAtRoot(player, out Transform root, out LODGroup lodGroup))
            {
                Notify("No prop under the crosshair.");
                return;
            }

            string name = NormalizeName(root.name);
            if (string.IsNullOrEmpty(name))
            {
                Notify("That object has no usable name.");
                return;
            }

            if (include)
            {
                IncludeLookedAt(root, lodGroup, name);
            }
            else
            {
                ExcludeLookedAt(root, name);
            }
        }

        private static void IncludeLookedAt(Transform root, LODGroup lodGroup, string name)
        {
            _filters.Exclude.RemoveAll(n => string.Equals(NormalizeName(n), name, StringComparison.OrdinalIgnoreCase));
            FilterRule rule = _filters.Include.FirstOrDefault(r => string.Equals(NormalizeName(r.Name), name, StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                rule = new FilterRule { Name = name };
                _filters.Include.Add(rule);
            }
            rule.Profile = _hotkeyProfile.Value;
            RebuildMatchers();
            SaveFilters();

            if (root.GetComponentInParent<SmallPhysicsObject>() != null || root.GetComponentInChildren<SmallPhysicsObject>(true) != null)
            {
                Notify($"Added '{name}' as {rule.Profile}. It is already physical.");
                return;
            }

            if (root.GetComponentsInChildren<Renderer>(true).Any(r => r.isPartOfStaticBatch))
            {
                Notify($"Added '{name}' as {rule.Profile}, but it is static batched and cannot move.");
                return;
            }

            Profile profile = ResolveRule(rule);
            Renderer[] measured = GetMeasuredRenderers(root, lodGroup);
            bool converted = false;
            float mass = 0f;
            try
            {
                converted = measured.Length > 0 && TryConvert(root, measured, profile, out mass, out _);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PropPhysics] Failed to convert '{GetPath(root)}': {e.Message}");
            }

            Notify(converted
                ? $"Added '{name}' as {rule.Profile} ({mass:F1} kg)."
                : $"Added '{name}' as {rule.Profile}, but it could not be made physical (near fire, part of something interactive, made of too many parts, or outside its MaxSize).");
        }

        private static void ExcludeLookedAt(Transform root, string name)
        {
            int removed = _filters.Include.RemoveAll(r => string.Equals(NormalizeName(r.Name), name, StringComparison.OrdinalIgnoreCase));
            if (!_filters.Exclude.Any(n => string.Equals(NormalizeName(n), name, StringComparison.OrdinalIgnoreCase)))
            {
                _filters.Exclude.Add(name);
            }
            RebuildMatchers();
            SaveFilters();

            SmallPhysicsObject spo = root.GetComponentInParent<SmallPhysicsObject>() ?? root.GetComponentInChildren<SmallPhysicsObject>(true);
            if (spo != null && Added.Contains(spo))
            {
                Revert(spo);
                Notify($"Excluded '{name}' and froze it.");
            }
            else if (spo != null)
            {
                Notify($"Excluded '{name}'. It is one of BSG's own physics props, so it stays physical.");
            }
            else
            {
                Notify(removed > 0 ? $"Excluded '{name}' and removed its include rule." : $"Excluded '{name}'.");
            }
        }

        private static void Revert(SmallPhysicsObject spo)
        {
            SmallPhysicsSystem system = SystemInstance();
            if (system != null)
            {
                system.RemoveObject(spo);
            }
            foreach (BallisticCollider collider in spo.BallisticColliders)
            {
                if (collider != null)
                {
                    collider.OnHitAction -= spo.OnHit;
                }
            }
            Added.Remove(spo);

            foreach (Rigidbody body in spo.GetComponentsInChildren<Rigidbody>(true))
            {
                UnityEngine.Object.Destroy(body);
            }
            foreach (BoxCollider box in spo.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.name == "ReClassic_PhysicsSupport")
                {
                    UnityEngine.Object.Destroy(box.gameObject);
                }
            }
            UnityEngine.Object.Destroy(spo);
        }

        private static bool TryGetLookedAtRoot(Player player, out Transform root, out LODGroup lodGroup)
        {
            root = null;
            lodGroup = null;

            Camera camera = Camera.main;
            Ray ray;
            if (camera != null)
            {
                ray = new Ray(camera.transform.position, camera.transform.forward);
            }
            else if (player.CameraPosition != null)
            {
                ray = new Ray(player.CameraPosition.position, player.CameraPosition.forward);
            }
            else
            {
                return false;
            }

            RaycastHit[] hits = Physics.RaycastAll(ray, HotkeyRange, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.GetComponentInParent<Player>() != null)
                {
                    continue;
                }

                Transform hitTransform = hit.collider.transform;
                SmallPhysicsObject spo = hitTransform.GetComponentInParent<SmallPhysicsObject>();
                if (spo != null)
                {
                    root = spo.transform;
                    lodGroup = root.GetComponent<LODGroup>();
                    return true;
                }

                lodGroup = hitTransform.GetComponentInParent<LODGroup>();
                if (lodGroup != null)
                {
                    root = lodGroup.transform;
                    return true;
                }

                root = hitTransform;
                for (int i = 0; i < 2 && root.parent != null; i++)
                {
                    List<string> tokens = Tokenize(root.name).ToList();
                    if (tokens.Count > 0 && !tokens.Any(HelperTokens.Contains) && !tokens.All(GenericTokens.Contains))
                    {
                        break;
                    }
                    root = root.parent;
                }
                return true;
            }
            return false;
        }

        private static void Notify(string message)
        {
            Plugin.LogSource.LogInfo("[PropPhysics] " + message);
            try
            {
                NotificationManager.DisplayMessageNotification(message);
            }
            catch
            {
            }
        }

        private class FilterHotkeys : MonoBehaviour
        {
            private void Update()
            {
                if (_addKey == null || !_enabled.Value)
                {
                    return;
                }

                try
                {
                    if (_addKey.Value.IsDown())
                    {
                        HandleHotkey(true);
                    }
                    else if (_excludeKey.Value.IsDown())
                    {
                        HandleHotkey(false);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[PropPhysics] Filter hotkey failed: {e}");
                }
            }
        }
    }
}
