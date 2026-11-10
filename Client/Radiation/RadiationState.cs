using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace mizmii.ReClassic.Radiation
{
    internal sealed class Meter
    {
        public const float Maximum = 100f;

        private sealed class Saved
        {
            [JsonProperty("level")]
            public float Level;

            [JsonProperty("pending")]
            public float Pending;

            [JsonProperty("savedUtc")]
            public DateTime SavedUtc;
        }

        private sealed class Flow
        {
            public float PerSecond;
            public float EndTime;
        }

        private readonly string _fileName;
        private readonly float _decayPerSecond;
        private Dictionary<string, Saved> _saved;
        private readonly List<Flow> _flows = new List<Flow>();

        public string ProfileId;
        public float Level;

        public Meter(string name, float decayPerMinute)
        {
            _fileName = Plugin.Guid + "." + name + ".json";
            _decayPerSecond = decayPerMinute / 60f;
        }

        private string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, _fileName);

        private Dictionary<string, Saved> All()
        {
            if (_saved != null)
            {
                return _saved;
            }
            try
            {
                if (File.Exists(FilePath))
                {
                    _saved = JsonConvert.DeserializeObject<Dictionary<string, Saved>>(File.ReadAllText(FilePath));
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Hazards] Could not read {_fileName}: {e.Message}");
            }
            if (_saved == null)
            {
                _saved = new Dictionary<string, Saved>();
            }
            return _saved;
        }

        public float Rate { get; private set; }

        private float _rateLevel = -1f;

        public void MeasureRate(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }
            if (_rateLevel < 0f)
            {
                _rateLevel = Level;
            }
            float instant = (Level - _rateLevel) / dt;
            _rateLevel = Level;
            Rate = Mathf.Lerp(Rate, instant, 1f - Mathf.Exp(-dt / 1.5f));
            if (Mathf.Abs(Rate) < 0.0005f)
            {
                Rate = 0f;
            }
        }

        public float PeekRate(string profileId)
        {
            if (!string.IsNullOrEmpty(profileId) && profileId == ProfileId)
            {
                return Rate;
            }
            return Peek(profileId) > 0f ? -_decayPerSecond : 0f;
        }

        public float Peek(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return 0f;
            }
            if (profileId == ProfileId)
            {
                return Level;
            }
            if (!All().TryGetValue(profileId, out Saved saved))
            {
                return 0f;
            }
            float seconds = (float)Math.Max(0.0, (DateTime.UtcNow - saved.SavedUtc).TotalSeconds);
            return Mathf.Clamp(saved.Level + saved.Pending - seconds * _decayPerSecond, 0f, Maximum);
        }

        public void Begin(string profileId)
        {
            _flows.Clear();
            Rate = 0f;
            _rateLevel = -1f;
            ProfileId = null;
            Level = Peek(profileId);
            ProfileId = profileId;
        }

        public void End(bool died)
        {
            if (string.IsNullOrEmpty(ProfileId))
            {
                return;
            }
            if (died)
            {
                Level = 0f;
                _flows.Clear();
            }
            Save();
            ProfileId = null;
            _flows.Clear();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(ProfileId))
            {
                return;
            }
            try
            {
                float pending = 0f;
                foreach (Flow flow in _flows)
                {
                    if (flow.PerSecond > 0f)
                    {
                        pending += flow.PerSecond * Mathf.Max(0f, flow.EndTime - Time.time);
                    }
                }
                All()[ProfileId] = new Saved { Level = Level, Pending = pending, SavedUtc = DateTime.UtcNow };
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(_saved, Formatting.Indented));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Hazards] Could not save {_fileName}: {e.Message}");
            }
        }

        public void Gain(float amount)
        {
            Level = Mathf.Clamp(Level + amount, 0f, Maximum);
        }

        public void Over(float amount, float seconds)
        {
            float time = Mathf.Max(1f, seconds);
            _flows.Add(new Flow { PerSecond = amount / time, EndTime = Time.time + time });
        }

        public void Clear()
        {
            Level = 0f;
            _flows.RemoveAll(f => f.PerSecond > 0f);
        }

        public bool Rising
        {
            get
            {
                foreach (Flow flow in _flows)
                {
                    if (flow.PerSecond > 0f && Time.time < flow.EndTime)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void Tick(float dt, bool naturalDecay)
        {
            float purge = 1f + Skills.SkillEffects.Value(Skills.SkillEffects.Purge);
            float rate = naturalDecay ? -_decayPerSecond * purge : 0f;
            for (int i = _flows.Count - 1; i >= 0; i--)
            {
                if (Time.time >= _flows[i].EndTime)
                {
                    _flows.RemoveAt(i);
                    continue;
                }
                float flow = _flows[i].PerSecond;
                rate += flow < 0f ? flow * purge : flow;
            }
            Level = Mathf.Clamp(Level + rate * dt, 0f, Maximum);
        }
    }

    internal static class Meters
    {
        public static readonly Meter Radiation = new Meter("Radiation", 0.5f);
        public static readonly Meter Toxicity = new Meter("Toxicity", 0.5f);

        private static readonly Dictionary<string, float> RadiationChunk = new Dictionary<string, float>
        {
            ["60098ad7c2240c0fe85c570a"] = 15f,
            ["590c678286f77426c9660122"] = 10f,
            ["5755356824597772cb798962"] = 12f,
            ["590c657e86f77412b013051d"] = 20f,
            ["5d1b33a686f7742523398398"] = 4f,
            ["5d40407c86f774318526545a"] = 6f,
            ["614451b71e5874611e2c7ae5"] = 3f,
            ["5d1b376e86f774252519444e"] = 8f,
            ["5d403f9186f7743cac3f229b"] = 5f
        };

        private static readonly Dictionary<string, float> RadiationOverTime = new Dictionary<string, float>
        {
            ["637b6251104668754b72f8f9"] = 40f,
            ["5751a89d24597722aa0e8db0"] = 25f
        };

        public const string Antidote = "5fca138c2a7b221b2852a5c6";

        public static void Begin(string profileId)
        {
            Radiation.Begin(profileId);
            Toxicity.Begin(profileId);
        }

        public static void End(bool died)
        {
            Radiation.End(died);
            Toxicity.End(died);
        }

        public static void Save()
        {
            Radiation.Save();
            Toxicity.Save();
        }

        public static void TreatRadiation(string templateId, float duration)
        {
            if (RadiationOverTime.TryGetValue(templateId, out float over) || (duration > 1f && !RadiationChunk.ContainsKey(templateId)))
            {
                Radiation.Over(-(over > 0f ? over : 20f), Mathf.Max(10f, duration));
            }
            else
            {
                Radiation.Gain(-(RadiationChunk.TryGetValue(templateId, out float chunk) ? chunk : 8f));
            }
        }

        private static readonly Dictionary<string, float> KitCost = new Dictionary<string, float>
        {
            ["60098ad7c2240c0fe85c570a"] = 40f,
            ["590c678286f77426c9660122"] = 30f,
            ["5755356824597772cb798962"] = 30f,
            ["590c657e86f77412b013051d"] = 50f
        };

        public static float KitRadiationCost(string templateId, float itemCost)
        {
            if (KitCost.TryGetValue(templateId, out float cost))
            {
                return Mathf.Max(cost, itemCost);
            }
            return Mathf.Max(20f, itemCost);
        }

        public static int TreatRadiationWithKit(string templateId, float itemCost, float resource)
        {
            float chunk = RadiationChunk.TryGetValue(templateId, out float value) ? value : 8f;
            float fullCost = KitRadiationCost(templateId, itemCost);
            float removable = Mathf.Min(chunk, Radiation.Level);
            if (removable <= 0.01f || resource < 1f)
            {
                return 0;
            }
            float affordable = resource / fullCost * chunk;
            float removed = Mathf.Min(removable, affordable);
            int cost = Mathf.Clamp(Mathf.CeilToInt(fullCost * removed / chunk), 1, Mathf.FloorToInt(resource));
            Radiation.Gain(-removed);
            return cost;
        }

        public static string RadiationRelief(string templateId, float duration)
        {
            if (RadiationOverTime.TryGetValue(templateId, out float over) || (duration > 1f && !RadiationChunk.ContainsKey(templateId)))
            {
                return "-" + (over > 0f ? over : 20f).ToString("0") + " over " + Mathf.Max(10f, duration).ToString("0") + "s";
            }
            string relief = "-" + (RadiationChunk.TryGetValue(templateId, out float chunk) ? chunk : 8f).ToString("0");
            return KitCost.TryGetValue(templateId, out float cost) ? relief + " (" + cost.ToString("0") + " HP)" : relief;
        }

        public static string ToxicityRelief(string templateId)
        {
            return templateId == Antidote ? "Clears" : "-10";
        }

        public static void TreatToxicity(string templateId)
        {
            if (templateId == Antidote)
            {
                Toxicity.Clear();
                return;
            }
            Toxicity.Gain(-10f);
        }
    }
}
