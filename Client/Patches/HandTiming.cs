using Newtonsoft.Json;
using System;
using System.IO;

namespace mizmii.ReClassic.Patches
{
    internal sealed class HandTiming
    {
        private const string FileName = Plugin.Guid + ".HandTiming.json";

        [JsonProperty("reachSeconds")] public float ReachSeconds = 0.5f;
        [JsonProperty("turnSeconds")] public float TurnSeconds = 0f;
        [JsonProperty("minTurnSeconds")] public float MinTurnSeconds = 0.15f;
        [JsonProperty("maxTurnSeconds")] public float MaxTurnSeconds = 0.45f;
        [JsonProperty("pushSeconds")] public float PushSeconds = 0.3f;
        [JsonProperty("releaseSeconds")] public float ReleaseSeconds = 0.45f;
        [JsonProperty("gripAt")] public float GripAt = 0.32f;
        [JsonProperty("turnEnd")] public float TurnEnd = 0.48f;
        [JsonProperty("letGoAt")] public float LetGoAt = 0.62f;
        [JsonProperty("fadeIn")] public float FadeIn = 0.15f;
        [JsonProperty("fadeOut")] public float FadeOut = 0.2f;
        [JsonProperty("openLead")] public float OpenLead = 0.5f;

        public static HandTiming Current = new HandTiming();

        public static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, FileName);

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Current = JsonConvert.DeserializeObject<HandTiming>(File.ReadAllText(FilePath)) ?? new HandTiming();
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Full Interactions] Could not read the hand timing file, using the defaults: {e.Message}");
                Current = new HandTiming();
            }
        }

        public static void Save()
        {
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
        }

        public static void Reset()
        {
            Current = new HandTiming();
        }
    }
}
