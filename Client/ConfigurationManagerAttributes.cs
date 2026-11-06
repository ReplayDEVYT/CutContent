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

        private static readonly HashSet<ConfigEntryBase> ExperimentalEntries = new HashSet<ConfigEntryBase>();

        public static void Experimental(ConfigEntryBase entry)
        {
            ExperimentalEntries.Add(entry);
            if (entry.Description.Tags.Length > 0 && entry.Description.Tags[0] is ConfigurationManagerAttributes attributes)
            {
                attributes.DispName = entry.Definition.Key + " (EXPERIMENTAL)";
            }
        }

        public static string Label(ConfigEntryBase entry, bool upper)
        {
            string key = upper ? entry.Definition.Key.ToUpperInvariant() : entry.Definition.Key;
            return ExperimentalEntries.Contains(entry) ? "<color=#ff8c8c>" + key + " (EXPERIMENTAL)</color>" : key;
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
