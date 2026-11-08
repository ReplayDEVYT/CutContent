using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using JsonType;
using mizmii.ReClassic.Consciousness;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;

namespace mizmii.ReClassic.Raid
{
    internal enum TimedMap
    {
        None,
        Factory,
        Reserve,
        Labs
    }

    internal static class RaidTimers
    {
        private const string Section = "Raid Timers";
        private static readonly TimeSpan Extra = TimeSpan.FromHours(100);

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _events;
        private static ConfigEntry<float> _beepVolume;
        private static ConfigEntry<float> _alarmVolume;
        private static ConfigEntry<bool> _skip;

        private static TimeSpan? _limit;
        private static readonly StringBuilder Builder = new StringBuilder(16);

        public static bool Enabled => _enabled.Value;
        public static bool EventsEnabled => _events.Value;
        public static float BeepVolume => _beepVolume.Value;
        public static float AlarmVolume => _alarmVolume.Value;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Raid", "Raid Timers", true,
                "No map kicks you out when its timer runs out. Factory, Reserve and Labs keep their timer: your watch beeps 5 and 3 minutes before the end, and staying past it sets off something bad on each map. On other maps the timer shows how long you've been in the raid. Takes effect next raid.",
                Section, "Enabled");
            _events = config.Bind(Section, "Map Events", true,
                "Factory, Reserve and Labs set off their own event in the last minute: sirens, mortars, and scavs, rogues or the infected coming for you.");
            _beepVolume = config.Bind(Section, "Watch Beep Volume", 0.8f,
                ConfigHelper.Advanced("Volume of the watch beeps 5 and 3 minutes before the end.", new AcceptableValueRange<float>(0f, 1f)));
            _alarmVolume = config.Bind(Section, "Siren Volume", 0.7f,
                ConfigHelper.Advanced("Volume of the map sirens and alarms.", new AcceptableValueRange<float>(0f, 1f)));
            _skip = config.Bind(Section + " - Debug", "Skip To Last 2 Minutes", false,
                ConfigHelper.Advanced("Debug. Turn on during a raid to jump the raid clock to 2 minutes before the end. In co-op only the host can use it, and everyone jumps with them. It turns itself back off."));
            _skip.SettingChanged += (sender, args) =>
            {
                if (!_skip.Value)
                {
                    return;
                }
                SkipTo(TimeSpan.FromMinutes(2));
                _skip.Value = false;
            };

            new TimerCreatedPatch().Enable();
            new ChangeSessionTimePatch().Enable();
            new MainTimerUpdatePatch().Enable();
            new TimerTextPatch().Enable();
            new LocationInfoPatch().Enable();

            var driver = new GameObject("ReClassic_RaidTimers");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.hideFlags = HideFlags.HideAndDontSave;
            driver.AddComponent<RaidEvents>();
        }

        public static TimedMap MapOf(string locationId)
        {
            string id = (locationId ?? string.Empty).ToLowerInvariant();
            if (id.StartsWith("factory4"))
            {
                return TimedMap.Factory;
            }
            if (id == "rezervbase")
            {
                return TimedMap.Reserve;
            }
            if (id == "laboratory")
            {
                return TimedMap.Labs;
            }
            return TimedMap.None;
        }

        public static TimedMap CurrentMap
        {
            get
            {
                GameWorld world = Singleton<GameWorld>.Instance;
                return world == null ? TimedMap.None : MapOf(world.LocationId);
            }
        }

        public static GameTimer Timer => Singleton<AbstractGame>.Instance?.GameTimer;

        public static bool Running
        {
            get
            {
                GameTimer timer = Timer;
                return _limit.HasValue && timer != null && timer.StartDateTime.HasValue && timer.Status == GameTimer.EGameTimerStatus.Started;
            }
        }

        public static TimeSpan Elapsed
        {
            get
            {
                GameTimer timer = Timer;
                if (timer?.StartDateTime == null)
                {
                    return TimeSpan.Zero;
                }
                DateTime end = timer.Status == GameTimer.EGameTimerStatus.Stopped ? timer.StopDateTime : DateTimeExtensions.UtcNow;
                TimeSpan elapsed = end - timer.StartDateTime.Value;
                return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
            }
        }

        public static TimeSpan Remaining => (_limit ?? TimeSpan.Zero) - Elapsed;

        private static TimeSpan Extend(TimeSpan sessionTime)
        {
            if (sessionTime >= Extra)
            {
                _limit = sessionTime - Extra;
                return sessionTime;
            }
            _limit = sessionTime;
            return sessionTime + Extra;
        }

        public static void SkipTo(TimeSpan remaining)
        {
            GameTimer timer = Timer;
            if (!Running || timer == null)
            {
                Plugin.LogSource.LogWarning("[Raid Timers] Can only skip the clock during a raid.");
                return;
            }
            if (Coop.InMatch && !Coop.IsServer)
            {
                Plugin.LogSource.LogWarning("[Raid Timers] Only the host can skip the raid clock.");
                return;
            }
            DateTime start = DateTimeExtensions.UtcNow - (_limit.Value - remaining);
            SetStart(start);
            Coop.Send(CoopMessage.RaidClock, string.Empty, extra: start.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Plugin.LogSource.LogInfo($"[Raid Timers] Raid clock moved to {remaining} before the end.");
        }

        public static void SetStart(DateTime start)
        {
            GameTimer timer = Timer;
            if (timer == null || !timer.StartDateTime.HasValue)
            {
                return;
            }
            if (start > DateTimeExtensions.UtcNow)
            {
                start = DateTimeExtensions.UtcNow;
            }
            timer._startDateTime = start;
            timer._escapeDateTime = start + timer.SessionTime;
            RaidEvents.ClockMoved();
        }

        public static void OnRemoteClock(string extra)
        {
            if (long.TryParse(extra, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long ticks))
            {
                SetStart(new DateTime(ticks, DateTimeKind.Utc));
            }
        }

        private static string Format(TimeSpan span)
        {
            bool negative = span < TimeSpan.Zero;
            if (negative)
            {
                span = span.Negate();
            }
            Builder.Length = 0;
            if (negative)
            {
                Builder.Append('-');
            }
            Builder.Append((int)span.TotalHours);
            Builder.Append(':');
            Builder.Append(span.Minutes.ToString("00"));
            Builder.Append(':');
            Builder.Append(span.Seconds.ToString("00"));
            return Builder.ToString();
        }

        private static string DisplayText()
        {
            return CurrentMap == TimedMap.None ? Format(Elapsed) : Format(Remaining);
        }

        private class TimerCreatedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Constructor(typeof(GameTimer), new[] { typeof(TimeSpan?) });
            }

            [PatchPostfix]
            private static void Postfix(GameTimer __instance)
            {
                if (!_enabled.Value)
                {
                    _limit = null;
                    return;
                }
                if (__instance.SessionTime.HasValue)
                {
                    __instance.SessionTime = Extend(__instance.SessionTime.Value);
                }
            }
        }

        private class ChangeSessionTimePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GameTimer), nameof(GameTimer.ChangeSessionTime));
            }

            [PatchPrefix]
            private static void Prefix(ref TimeSpan sessionTime)
            {
                if (!_enabled.Value)
                {
                    _limit = null;
                    return;
                }
                sessionTime = Extend(sessionTime);
            }
        }

        private class MainTimerUpdatePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MainTimerPanel), nameof(MainTimerPanel.UpdateTimer));
            }

            [PatchPrefix]
            private static bool Prefix(MainTimerPanel __instance)
            {
                if (!Running)
                {
                    return true;
                }
                bool warning = CurrentMap != TimedMap.None && Remaining.TotalSeconds < 600.0;
                __instance._canvasGroup.ignoreParentGroups = warning;
                __instance._canvasGroup.blocksRaycasts = !warning;
                if (warning && !__instance.ForcePull)
                {
                    __instance.ForcePull = true;
                    __instance._canvasGroup.alpha = 1f;
                    if (__instance.TimerText.color != __instance._warningColor)
                    {
                        __instance.TimerText.color = __instance._warningColor;
                    }
                }
                if (__instance.DisplayTime)
                {
                    __instance.SetTimerText(TimeSpan.Zero);
                }
                return false;
            }
        }

        private class TimerTextPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TimerPanel), nameof(TimerPanel.SetTimerText));
            }

            [PatchPrefix]
            private static bool Prefix(TimerPanel __instance)
            {
                if (!(__instance is MainTimerPanel) || !Running)
                {
                    return true;
                }
                __instance.TimerText.SetMonospaceText(DisplayText());
                return false;
            }
        }

        private class LocationInfoPatch : ModulePatch
        {
            private static readonly Dictionary<TMP_Text, string> OriginalHeaders = new Dictionary<TMP_Text, string>();

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(LocationInfoPanel), nameof(LocationInfoPanel.Set));
            }

            [PatchPostfix]
            private static void Postfix(LocationInfoPanel __instance, LocationSettings.Location location)
            {
                try
                {
                    LocationWarningPanel panel = __instance._warningPanel;
                    if (panel == null || location == null)
                    {
                        return;
                    }
                    TMP_Text description = panel._description;
                    TMP_Text header = panel.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t != description);
                    bool timed = _enabled.Value && MapOf(location.Id) != TimedMap.None;
                    if (!timed)
                    {
                        panel.GetComponent<WarningFitter>()?.Stop();
                        if (header != null && OriginalHeaders.TryGetValue(header, out string original))
                        {
                            OriginalHeaders.Remove(header);
                            header.text = original;
                            foreach (LocalizedText localized in header.GetComponents<LocalizedText>())
                            {
                                localized.enabled = true;
                            }
                        }
                        return;
                    }
                    panel.gameObject.SetActive(true);
                    if (header != null)
                    {
                        if (!OriginalHeaders.ContainsKey(header))
                        {
                            OriginalHeaders[header] = header.text;
                        }
                        foreach (LocalizedText localized in header.GetComponents<LocalizedText>())
                        {
                            localized.enabled = false;
                        }
                        header.text = "LIMITED TIME";
                    }
                    description.text = "This map has an extract timer. You will not be booted out of the map, but staying past the timer may cause severe consequences..";
                    WarningFitter fitter = panel.GetComponent<WarningFitter>() ?? panel.gameObject.AddComponent<WarningFitter>();
                    fitter.Begin(__instance.transform as RectTransform, panel.transform as RectTransform, header, description);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Raid Timers] Could not show the map warning: {e}");
                }
            }
        }
    }
}
