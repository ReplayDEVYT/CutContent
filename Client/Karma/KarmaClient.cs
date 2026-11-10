#pragma warning disable CS0649
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI;
using mizmii.ReClassic.Consciousness;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mizmii.ReClassic.Karma
{
    internal sealed class KarmaInfo
    {
        [JsonProperty("enabled")] public bool Enabled;
        [JsonProperty("profileId")] public string ProfileId;
        [JsonProperty("karma")] public double Karma;
        [JsonProperty("applied")] public double Applied;
        [JsonProperty("minimum")] public double Minimum = -100;
        [JsonProperty("maximum")] public double Maximum = 100;
        [JsonProperty("tier")] public string Tier = "Neutral";
        [JsonProperty("traderPrice")] public double TraderPrice = 1;
        [JsonProperty("fencePrice")] public double FencePrice = 1;
        [JsonProperty("fenceSell")] public double FenceSell = 1;
        [JsonProperty("fleaTax")] public double FleaTax = 1;
        [JsonProperty("questExperience")] public double QuestExperience = 1;
        [JsonProperty("events")] public Dictionary<string, double> Events = new Dictionary<string, double>();
    }

    internal sealed class KarmaManyResponse
    {
        [JsonProperty("values")] public Dictionary<string, double> Values = new Dictionary<string, double>();
    }

    internal static class KarmaClient
    {
        public const string FenceId = "579dc571d53a0658a154fbec";

        private static KarmaInfo _info;
        private static float _lastRefresh = -100f;
        private static bool _loading;
        private static readonly Dictionary<string, double> Others = new Dictionary<string, double>();

        public static KarmaInfo Info => _info;

        public static void Override(KarmaInfo info)
        {
            _info = info;
        }

        public static bool Available => _info != null && _info.Enabled && PmcKarma.Enabled;

        public static double FenceSell => Available ? _info.FenceSell : 1;

        public static double FleaTax => Available ? _info.FleaTax : 1;

        public static double Of(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return 0;
            }
            if (_info != null && _info.ProfileId == profileId)
            {
                return _info.Karma;
            }
            return Others.TryGetValue(profileId, out double value) ? value : 0;
        }

        public static double Points(string eventName)
        {
            if (_info?.Events != null && _info.Events.TryGetValue(eventName, out double points))
            {
                return points;
            }
            return 0;
        }

        public static void RefreshSoon()
        {
            if (Time.realtimeSinceStartup - _lastRefresh > 10f)
            {
                Refresh();
            }
        }

        public static async void Refresh()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            _lastRefresh = Time.realtimeSinceStartup;
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/karma/info", "{}");
                KarmaInfo info = JsonConvert.DeserializeObject<KarmaInfo>(json);
                if (info != null)
                {
                    _info = info;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not load karma from the server: {e.Message}");
            }
            finally
            {
                _loading = false;
            }
        }

        public static async void Load(IEnumerable<string> profileIds)
        {
            try
            {
                string body = JsonConvert.SerializeObject(new Dictionary<string, object> { ["profileIds"] = new List<string>(profileIds) });
                string json = await RequestHandler.PostJsonAsync("/reclassic/karma/many", body);
                KarmaManyResponse response = JsonConvert.DeserializeObject<KarmaManyResponse>(json);
                if (response?.Values == null)
                {
                    return;
                }
                foreach (var pair in response.Values)
                {
                    Others[pair.Key] = pair.Value;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not load karma for the raid: {e.Message}");
            }
        }

        public static async void Add(Player player, string eventName, string reason, int count = 1)
        {
            if (player == null || !PmcKarma.Enabled)
            {
                return;
            }
            string profileId = player.ProfileId;
            try
            {
                string body = JsonConvert.SerializeObject(new Dictionary<string, object> { ["profileId"] = profileId, ["event"] = eventName, ["count"] = count });
                string json = await RequestHandler.PostJsonAsync("/reclassic/karma/add", body);
                KarmaInfo info = JsonConvert.DeserializeObject<KarmaInfo>(json);
                if (info == null || !info.Enabled)
                {
                    return;
                }
                Others[profileId] = info.Karma;
                if (_info != null && _info.ProfileId == profileId)
                {
                    _info = info;
                }
                if (Math.Abs(info.Applied) < 0.01)
                {
                    return;
                }
                if (player.IsYourPlayer)
                {
                    Show((float)info.Applied, (float)info.Karma, reason);
                }
                else
                {
                    Coop.Send(CoopMessage.Karma, profileId, string.Empty, new[] { (float)info.Applied, (float)info.Karma }, reason);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not send a karma change to the server: {e.Message}");
            }
        }

        public static void OnRemoteKarma(string target, float[] values, string reason)
        {
            Player local = Coop.LocalPlayer;
            if (local == null || local.ProfileId != target || values == null || values.Length < 2)
            {
                return;
            }
            if (_info != null)
            {
                _info.Karma = values[1];
            }
            Show(values[0], values[1], reason);
        }

        public static void OnRemoteNotice(string target, string text)
        {
            Player local = Coop.LocalPlayer;
            if (local == null || local.ProfileId != target)
            {
                return;
            }
            Notice(text);
        }

        public static void Notice(Player player, string text)
        {
            if (player == null)
            {
                return;
            }
            if (player.IsYourPlayer)
            {
                Notice(text);
            }
            else
            {
                Coop.Send(CoopMessage.KarmaNotice, player.ProfileId, string.Empty, null, text);
            }
        }

        private static void Notice(string text)
        {
            NotificationManager.DisplayMessageNotification(text, ENotificationDurationType.Long, ENotificationIconType.Friend);
        }

        private static void Show(float applied, float total, string reason)
        {
            bool positive = applied > 0;
            string color = positive ? "#54c1ff" : "#c40000";
            string text = $"PMC Karma <color={color}>{(positive ? "+" : "")}{applied:0.#}</color> ({reason}). Now {total:0.#}.";
            try
            {
                NotificationManager.DisplayMessageNotification(text, ENotificationDurationType.Default, positive ? ENotificationIconType.Quest : ENotificationIconType.Alert);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not show the karma notification: {e.Message}");
            }
            try
            {
                Singleton<GUISounds>.Instance?.PlayKarmaSound(positive);
            }
            catch (Exception)
            {
            }
        }

        public static string Percent(double multiplier)
        {
            double percent = (multiplier - 1) * 100;
            return (percent >= 0 ? "+" : "") + percent.ToString("0") + "%";
        }

        public static string TierColor(string tier)
        {
            switch (tier)
            {
                case "Very Low":
                case "Low":
                    return "#c40000";
                case "High":
                case "Very High":
                    return "#54c1ff";
                default:
                    return "#747b7e";
            }
        }
    }
}
