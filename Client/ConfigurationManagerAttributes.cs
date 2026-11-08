using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

namespace mizmii.ReClassic
{
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public int? Order;
        public bool? Browsable;
        public string DispName;
    }

    internal enum FeatureCategory
    {
        Gameplay,
        Combat,
        Environment,
        VisualsAndAudio
    }

    internal sealed class SettingInfo
    {
        public FeatureCategory Category;
        public string Group;
        public string Section;
        public ConfigEntryBase Entry;
    }

    internal static class ConfigHelper
    {
        public const string MainSection = "Main Features";

        public static readonly List<SettingInfo> Settings = new List<SettingInfo>();

        private static readonly Dictionary<ConfigEntryBase, string> Marks = new Dictionary<ConfigEntryBase, string>();

        public static void Experimental(ConfigEntryBase entry)
        {
            Mark(entry, "EXPERIMENTAL");
        }

        public static void PerformanceHeavy(ConfigEntryBase entry)
        {
            Mark(entry, "PERFORMANCE HEAVY");
        }

        private static void Mark(ConfigEntryBase entry, string mark)
        {
            Marks[entry] = Marks.TryGetValue(entry, out string existing) ? existing + ", " + mark : mark;
            if (entry.Description.Tags.Length > 0 && entry.Description.Tags[0] is ConfigurationManagerAttributes attributes)
            {
                attributes.DispName = entry.Definition.Key + " (" + Marks[entry] + ")";
            }
        }

        private static readonly Dictionary<ConfigEntryBase, List<ConfigEntry<bool>>> Required = new Dictionary<ConfigEntryBase, List<ConfigEntry<bool>>>();

        public static void Requires(ConfigEntry<bool> parent, params ConfigEntryBase[] entries)
        {
            foreach (ConfigEntryBase entry in entries)
            {
                if (entry == null || parent == null || entry == parent)
                {
                    continue;
                }
                if (!Required.TryGetValue(entry, out List<ConfigEntry<bool>> parents))
                {
                    parents = new List<ConfigEntry<bool>>();
                    Required[entry] = parents;
                }
                if (!parents.Contains(parent))
                {
                    parents.Add(parent);
                }
            }
        }

        public static ConfigEntry<bool> MissingRequirement(ConfigEntryBase entry)
        {
            return Missing(entry, new HashSet<ConfigEntryBase>());
        }

        private static ConfigEntry<bool> Missing(ConfigEntryBase entry, HashSet<ConfigEntryBase> seen)
        {
            if (!seen.Add(entry))
            {
                return null;
            }
            foreach (ConfigEntry<bool> parent in Parents(entry))
            {
                ConfigEntry<bool> deeper = Missing(parent, seen);
                if (deeper != null)
                {
                    return deeper;
                }
                if (!parent.Value)
                {
                    return parent;
                }
            }
            return null;
        }

        private static IEnumerable<ConfigEntry<bool>> Parents(ConfigEntryBase entry)
        {
            if (Required.TryGetValue(entry, out List<ConfigEntry<bool>> parents))
            {
                return parents;
            }
            ConfigEntry<bool> owner = OwnerFeature(entry);
            return owner != null ? new[] { owner } : new ConfigEntry<bool>[0];
        }

        private static ConfigEntry<bool> OwnerFeature(ConfigEntryBase entry)
        {
            string section = entry.Definition.Section;
            if (section == MainSection)
            {
                return null;
            }
            string best = null;
            ConfigEntry<bool> owner = null;
            bool shared = false;
            foreach (SettingInfo setting in Settings)
            {
                if (!(setting.Entry is ConfigEntry<bool> feature) || setting.Entry.Definition.Section != MainSection || string.IsNullOrEmpty(setting.Section))
                {
                    continue;
                }
                if (section != setting.Section && !section.StartsWith(setting.Section + " - ", System.StringComparison.Ordinal))
                {
                    continue;
                }
                if (best == null || setting.Section.Length > best.Length)
                {
                    best = setting.Section;
                    owner = feature;
                    shared = false;
                }
                else if (setting.Section == best && feature != owner)
                {
                    shared = true;
                }
            }
            return shared ? null : owner;
        }

        public static string Label(ConfigEntryBase entry, bool upper)
        {
            string key = upper ? entry.Definition.Key.ToUpperInvariant() : entry.Definition.Key;
            return Marks.TryGetValue(entry, out string mark) ? "<color=#ff8c8c>" + key + " (" + mark + ")</color>" : key;
        }

        private static readonly PropertyInfo OrphanedEntries = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries");

        public static ConfigDescription Advanced(string description, AcceptableValueBase acceptableValues = null)
        {
            return new ConfigDescription(description, acceptableValues, new ConfigurationManagerAttributes { IsAdvanced = true });
        }

        public static ConfigEntry<bool> Feature(ConfigFile config, FeatureCategory category, string group, string name, bool defaultValue, string description, string oldSection, string oldKey)
        {
            var attributes = new ConfigurationManagerAttributes { Order = 1000 - ((int)category * 100 + Settings.Count) };
            ConfigEntry<bool> entry = config.Bind(MainSection, name, defaultValue, new ConfigDescription(description, null, attributes));
            Migrate(config, entry, new ConfigDefinition(oldSection, oldKey));
            Settings.Add(new SettingInfo { Category = category, Group = group, Section = oldSection, Entry = entry });
            return entry;
        }

        public static void Option(FeatureCategory category, string group, ConfigEntryBase entry)
        {
            Settings.Add(new SettingInfo { Category = category, Group = group, Section = entry.Definition.Section, Entry = entry });
        }

        public static FeatureCategory? CategoryOf(ConfigEntryBase entry)
        {
            foreach (SettingInfo setting in Settings)
            {
                if (setting.Entry == entry)
                {
                    return setting.Category;
                }
            }
            string section = entry.Definition.Section;
            var seen = new HashSet<string>();
            SettingInfo owner = null;
            foreach (SettingInfo setting in Settings)
            {
                if (string.IsNullOrEmpty(setting.Section) || !seen.Add(setting.Section))
                {
                    continue;
                }
                bool matches = section == setting.Section || section.StartsWith(setting.Section + " - ", System.StringComparison.Ordinal);
                if (matches && (owner == null || setting.Section.Length > owner.Section.Length))
                {
                    owner = setting;
                }
            }
            return owner?.Category;
        }

        private static void Migrate(ConfigFile config, ConfigEntryBase entry, ConfigDefinition old)
        {
            if (!(OrphanedEntries?.GetValue(config) is Dictionary<ConfigDefinition, string> orphans) || !orphans.TryGetValue(old, out string value))
            {
                return;
            }
            orphans.Remove(old);
            try
            {
                entry.SetSerializedValue(value);
            }
            catch
            {
            }
            config.Save();
        }
    }
}
