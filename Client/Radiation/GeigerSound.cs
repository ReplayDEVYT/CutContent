using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace mizmii.ReClassic.Radiation
{
    internal static class GeigerSound
    {
        private const int SampleRate = 44100;
        private const string Resource = "ReClassic.Resources.geiger_click.ogg";

        private static AudioClip[] _fallback;
        private static AudioClip[] _loaded;
        private static UnityWebRequest _request;
        private static bool _started;

        public static AudioClip[] Clips
        {
            get
            {
                Finish();
                return _loaded ?? _fallback;
            }
        }

        public static void Load()
        {
            if (_fallback == null)
            {
                _fallback = new[] { Synth(1), Synth(2), Synth(3) };
            }
            if (_started)
            {
                return;
            }
            _started = true;
            try
            {
                string path = Extract();
                if (path == null)
                {
                    return;
                }
                _request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.OGGVORBIS);
                _request.SendWebRequest();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Radiation] Could not load the Geiger click, using the placeholder: {e.Message}");
                _request = null;
            }
        }

        private static string Extract()
        {
            using (Stream stream = typeof(GeigerSound).Assembly.GetManifestResourceStream(Resource))
            {
                if (stream == null)
                {
                    Plugin.LogSource.LogWarning("[Radiation] The Geiger click is missing from the mod, using the placeholder.");
                    return null;
                }
                string folder = Path.Combine(BepInEx.Paths.CachePath, "ReClassic");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "geiger_click.ogg");
                using (FileStream file = File.Create(path))
                {
                    stream.CopyTo(file);
                }
                return path;
            }
        }

        private static void Finish()
        {
            if (_request == null || !_request.isDone)
            {
                return;
            }
            try
            {
                if (_request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(_request);
                    if (clip != null)
                    {
                        clip.name = "ReClassic Geiger Click";
                        UnityEngine.Object.DontDestroyOnLoad(clip);
                        _loaded = new[] { clip };
                    }
                }
                else
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not load the Geiger click, using the placeholder: {_request.error}");
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Radiation] Could not load the Geiger click, using the placeholder: {e.Message}");
            }
            finally
            {
                _request.Dispose();
                _request = null;
            }
        }

        public static AudioClip AnalyzerBeep()
        {
            int length = (int)(SampleRate * 0.09f);
            float[] data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float release = Mathf.Clamp01((0.09f - t) / 0.012f);
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 2900f * t)) * 0.35f + Mathf.Sin(2f * Mathf.PI * 2900f * t) * 0.4f;
                data[i] = square * attack * release * 0.8f;
            }
            AudioClip clip = AudioClip.Create("ReClassic Gas Analyzer Beep", length, 1, SampleRate, false);
            clip.SetData(data, 0);
            UnityEngine.Object.DontDestroyOnLoad(clip);
            return clip;
        }

        private static AudioClip Synth(int seed)
        {
            var random = new System.Random(seed * 7919);
            int length = (int)(SampleRate * 0.012f);
            float[] data = new float[length];
            float tone = 3200f + seed * 450f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * 900f);
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                float ring = Mathf.Sin(2f * Mathf.PI * tone * t);
                data[i] = (noise * 0.7f + ring * 0.3f) * envelope * 0.9f;
            }
            AudioClip clip = AudioClip.Create("ReClassic Geiger Click " + seed, length, 1, SampleRate, false);
            clip.SetData(data, 0);
            UnityEngine.Object.DontDestroyOnLoad(clip);
            return clip;
        }
    }
}
