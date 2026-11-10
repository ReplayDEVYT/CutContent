using BepInEx.Configuration;
using EFT.Communications;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace mizmii.ReClassic.Sync
{
    internal static class SettingsSync
    {
        public const string LockedNote = "Locked to the match host's setting until the match ends.";
        public const string NextRaidNote = "Only takes effect next raid, so it can't be changed until the match ends.";

        private static ConfigFile _config;
        private static Dictionary<ConfigEntryBase, string> _originals;
        private static readonly Dictionary<ConfigEntryBase, string> LockedValues = new Dictionary<ConfigEntryBase, string>();
        private static bool _saveOnSet;
        private static bool _applying;
        private static float _lastNotice;
        private static bool _dirty;
        private static float _dirtyAt;

        public static bool Hosting { get; private set; }
        public static Action Broadcast;

        public static bool Locked { get; private set; }
        public static event Action LockChanged;

        public static void Init(ConfigFile config)
        {
            _config = config;
            config.SettingChanged += OnSettingChanged;
        }

        public static bool IsSynced(ConfigEntryBase entry)
        {
            string section = entry.Definition.Section;
            string key = entry.Definition.Key;
            if (section == "Hidden" || section == "Developer" || section.StartsWith("Prop Physics", StringComparison.Ordinal) || section.EndsWith(" - Debug", StringComparison.Ordinal))
            {
                return false;
            }
            if (section == ConfigHelper.MainSection && (key == "Prop Physics" || key == "Auctions"))
            {
                return false;
            }
            if (ConfigHelper.CategoryOf(entry) == FeatureCategory.VisualsAndAudio)
            {
                return false;
            }
            return entry.SettingType != typeof(KeyboardShortcut);
        }

        public static bool IsLocked(ConfigEntryBase entry)
        {
            if (!IsSynced(entry))
            {
                return false;
            }
            return Locked || (Hosting && ConfigHelper.IsNextRaid(entry));
        }

        public static string NoteFor(ConfigEntryBase entry)
        {
            return Locked ? LockedNote : NextRaidNote;
        }

        public static void Tick()
        {
            if (_dirty && UnityEngine.Time.realtimeSinceStartup - _dirtyAt > 0.3f)
            {
                _dirty = false;
                try
                {
                    Broadcast?.Invoke();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Fika] Could not send the new settings: {e.Message}");
                }
            }
        }

        private static string Key(ConfigEntryBase entry)
        {
            return entry.Definition.Section + "\n" + entry.Definition.Key;
        }

        private static IEnumerable<ConfigEntryBase> Entries()
        {
            return _config == null ? Enumerable.Empty<ConfigEntryBase>() : _config.Select(pair => pair.Value).Where(IsSynced).ToList();
        }

        public static string Snapshot()
        {
            var values = new Dictionary<string, string>();
            foreach (ConfigEntryBase entry in Entries())
            {
                values[Key(entry)] = entry.GetSerializedValue();
            }
            return JsonConvert.SerializeObject(values);
        }

        public static void ApplyHost(string json)
        {
            Dictionary<string, string> values;
            try
            {
                values = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Fika] Could not read the host's settings: {e.Message}");
                return;
            }
            Lock(values);
            Plugin.LogSource.LogInfo($"[Fika] Using the match host's Re-Classic settings ({values?.Count ?? 0} values) until the match ends.");
        }

        public static void LockCurrent()
        {
            if (_config == null)
            {
                return;
            }
            Hosting = true;
            LockedValues.Clear();
            foreach (ConfigEntryBase entry in Entries())
            {
                if (ConfigHelper.IsNextRaid(entry))
                {
                    LockedValues[entry] = entry.GetSerializedValue();
                }
            }
            LockChanged?.Invoke();
            Plugin.LogSource.LogInfo("[Fika] Hosting: your Re-Classic setting changes are sent to the other players as you make them.");
        }

        private static void Lock(Dictionary<string, string> values)
        {
            if (_config == null)
            {
                return;
            }
            if (!Locked)
            {
                _originals = Entries().ToDictionary(e => e, e => e.GetSerializedValue());
                _saveOnSet = _config.SaveOnConfigSet;
                _config.SaveOnConfigSet = false;
            }
            Locked = true;
            LockedValues.Clear();
            _applying = true;
            try
            {
                foreach (ConfigEntryBase entry in Entries())
                {
                    string value = entry.GetSerializedValue();
                    if (values != null && values.TryGetValue(Key(entry), out string hostValue) && hostValue != value)
                    {
                        try
                        {
                            entry.SetSerializedValue(hostValue);
                        }
                        catch (Exception e)
                        {
                            Plugin.LogSource.LogWarning($"[Fika] Could not apply the host's value for {entry.Definition.Key}: {e.Message}");
                        }
                    }
                    LockedValues[entry] = entry.GetSerializedValue();
                }
            }
            finally
            {
                _applying = false;
            }
            LockChanged?.Invoke();
        }

        public static void Unlock()
        {
            if (Hosting)
            {
                Hosting = false;
                _dirty = false;
                LockedValues.Clear();
                LockChanged?.Invoke();
            }
            if (!Locked || _config == null)
            {
                return;
            }
            _applying = true;
            try
            {
                foreach (KeyValuePair<ConfigEntryBase, string> pair in _originals)
                {
                    if (pair.Key.GetSerializedValue() != pair.Value)
                    {
                        pair.Key.SetSerializedValue(pair.Value);
                    }
                }
            }
            finally
            {
                _applying = false;
            }
            _config.SaveOnConfigSet = _saveOnSet;
            _config.Save();
            _originals = null;
            LockedValues.Clear();
            Locked = false;
            LockChanged?.Invoke();
            Plugin.LogSource.LogInfo("[Fika] Match over, your own Re-Classic settings are back.");
        }

        private static void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            if (_applying)
            {
                return;
            }
            ConfigEntryBase entry = args.ChangedSetting;
            if (Hosting && !Locked)
            {
                if (LockedValues.TryGetValue(entry, out string fixedValue))
                {
                    if (entry.GetSerializedValue() != fixedValue)
                    {
                        _applying = true;
                        try
                        {
                            entry.SetSerializedValue(fixedValue);
                        }
                        finally
                        {
                            _applying = false;
                        }
                        Notice(entry.Definition.Key + " only takes effect next raid, so it can't be changed until the match ends.");
                    }
                    return;
                }
                if (IsSynced(entry))
                {
                    _dirty = true;
                    _dirtyAt = UnityEngine.Time.realtimeSinceStartup;
                }
                return;
            }
            if (!Locked)
            {
                return;
            }
            if (!LockedValues.TryGetValue(entry, out string value) || entry.GetSerializedValue() == value)
            {
                return;
            }
            _applying = true;
            try
            {
                entry.SetSerializedValue(value);
            }
            finally
            {
                _applying = false;
            }
            Notice("Re-Classic settings follow the match host and can't be changed until the match ends.");
        }

        private static void Notice(string text)
        {
            if (UnityEngine.Time.realtimeSinceStartup - _lastNotice > 2f)
            {
                _lastNotice = UnityEngine.Time.realtimeSinceStartup;
                NotificationManager.DisplayWarningNotification(text);
            }
        }
    }
}
