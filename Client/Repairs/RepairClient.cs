#pragma warning disable CS0649
using EFT;
using EFT.Communications;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Repairs
{
    internal sealed class RepairJobView
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("traderId")] public string TraderId;
        [JsonProperty("template")] public string Template;
        [JsonProperty("durability")] public double Durability;
        [JsonProperty("maxDurability")] public double MaxDurability;
        [JsonProperty("startTime")] public long StartTime;
        [JsonProperty("endTime")] public long EndTime;
    }

    internal sealed class RepairListResponse
    {
        [JsonProperty("enabled")] public bool Enabled;
        [JsonProperty("serverTime")] public long ServerTime;
        [JsonProperty("baseMinutes")] public double BaseMinutes = 10;
        [JsonProperty("minutesPerPoint")] public double MinutesPerPoint = 1;
        [JsonProperty("qualityExponent")] public double QualityExponent = 2;
        [JsonProperty("minimumMinutes")] public double MinimumMinutes = 5;
        [JsonProperty("maximumHours")] public double MaximumHours = 24;
        [JsonProperty("jobs")] public List<RepairJobView> Jobs = new List<RepairJobView>();
    }

    internal sealed class RepairSendOperation
    {
        public string Action = "ReClassicRepairSend";
        public string itemId;
        public string traderId;
        public double amount;
        public double durability;
        public double maxDurability;
    }

    internal static class RepairClient
    {
        private static RepairListResponse _info;
        private static long _serverOffset;
        private static bool _loading;

        public static event Action Updated;

        public static bool Available => _info != null && _info.Enabled;

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _serverOffset;

        public static IEnumerable<RepairJobView> JobsFor(string traderId)
        {
            if (_info?.Jobs == null)
            {
                return Enumerable.Empty<RepairJobView>();
            }
            return _info.Jobs.Where(j => j.TraderId == traderId).OrderBy(j => j.EndTime);
        }

        public static long DurationSeconds(double amount, double quality)
        {
            RepairListResponse info = _info ?? new RepairListResponse();
            if (quality <= 0)
            {
                quality = 1;
            }
            double minutes = (info.BaseMinutes + info.MinutesPerPoint * Math.Max(0, amount)) * Math.Pow(1 / Math.Max(0.1, quality), info.QualityExponent);
            minutes = Math.Min(Math.Max(minutes, info.MinimumMinutes), Math.Max(info.MinimumMinutes, info.MaximumHours * 60));
            return (long)Math.Round(minutes * 60);
        }

        public static async void Refresh()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/repairs/list", "{}");
                RepairListResponse response = JsonConvert.DeserializeObject<RepairListResponse>(json);
                if (response != null)
                {
                    _info = response;
                    _serverOffset = response.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Repair Time] Could not load repairs from the server: {e.Message}");
            }
            finally
            {
                _loading = false;
            }
            Updated?.Invoke();
        }

        public static void Send(RepairSendOperation operation, Action<bool> done)
        {
            IEftSession session = PatchConstants.BackEndSession;
            if (session == null)
            {
                done?.Invoke(false);
                return;
            }
            session.SendOperationRightNow(operation, result =>
            {
                bool ok = !result.Failed;
                if (!ok && !string.IsNullOrEmpty(result.Error))
                {
                    NotificationManager.DisplayWarningNotification(result.Error.Localized());
                }
                done?.Invoke(ok);
                Refresh();
            });
        }

        public static string Duration(long seconds)
        {
            seconds = Math.Max(0, seconds);
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            }
            if (span.TotalMinutes >= 1)
            {
                return $"{span.Minutes}m {span.Seconds:00}s";
            }
            return $"{span.Seconds}s";
        }

        public static string SpeedName(double quality)
        {
            if (quality <= 0.75)
            {
                return "Slow";
            }
            if (quality < 1.1)
            {
                return "Average";
            }
            return "Fast";
        }

        public static string ItemName(string template)
        {
            if (string.IsNullOrEmpty(template))
            {
                return "Item";
            }
            string name = (template + " Name").Localized();
            return string.IsNullOrEmpty(name) || name == template + " Name" ? template : name;
        }

        public static float Progress(RepairJobView job)
        {
            long total = Math.Max(1, job.EndTime - job.StartTime);
            return Mathf.Clamp01((float)(Now - job.StartTime) / total);
        }
    }
}
