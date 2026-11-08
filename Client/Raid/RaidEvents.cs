using Comfort.Common;
using EFT;
using mizmii.ReClassic.Consciousness;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Raid
{
    internal class RaidEvents : MonoBehaviour
    {
        private static RaidEvents _instance;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        private GameWorld _world;
        private bool _beepFive;
        private bool _beepThree;
        private bool _eventStarted;
        private readonly List<Coroutine> _running = new List<Coroutine>();
        private float _nextCheck;

        private void Awake()
        {
            _instance = this;
        }

        public static void ClockMoved()
        {
            if (_instance == null)
            {
                return;
            }
            double remaining = RaidTimers.Remaining.TotalSeconds;
            _instance._beepFive = remaining <= 300.0;
            _instance._beepThree = remaining <= 180.0;
        }

        private void Update()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world != _world)
            {
                Reset();
                _world = world;
            }
            if (world == null || !RaidTimers.Enabled || !RaidTimers.Running || Time.unscaledTime < _nextCheck)
            {
                return;
            }
            _nextCheck = Time.unscaledTime + 0.25f;

            TimedMap map = RaidTimers.CurrentMap;
            if (map == TimedMap.None)
            {
                return;
            }
            double remaining = RaidTimers.Remaining.TotalSeconds;
            if (!_beepFive && remaining <= 300.0)
            {
                _beepFive = true;
                if (remaining > 290.0)
                {
                    Beep();
                }
            }
            if (!_beepThree && remaining <= 180.0)
            {
                _beepThree = true;
                if (remaining > 170.0)
                {
                    Beep();
                }
            }
            if (_eventStarted || !RaidTimers.EventsEnabled)
            {
                return;
            }
            double start = map == TimedMap.Labs ? 0.0 : 60.0;
            if (remaining <= start)
            {
                _eventStarted = true;
                Plugin.LogSource.LogInfo($"[Raid Timers] Starting the {map} event.");
                switch (map)
                {
                    case TimedMap.Factory:
                        Run(Factory());
                        break;
                    case TimedMap.Reserve:
                        Run(Reserve());
                        break;
                    case TimedMap.Labs:
                        Run(Labs());
                        break;
                }
            }
        }

        private void Reset()
        {
            foreach (Coroutine routine in _running)
            {
                if (routine != null)
                {
                    StopCoroutine(routine);
                }
            }
            _running.Clear();
            _beepFive = false;
            _beepThree = false;
            _eventStarted = false;
            EventBots.Reset();
        }

        private void Run(IEnumerator routine)
        {
            _running.Add(StartCoroutine(Guard(routine)));
        }

        private static IEnumerator Guard(IEnumerator routine)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext())
                    {
                        yield break;
                    }
                    current = routine.Current;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Raid Timers] Map event failed: {e}");
                    yield break;
                }
                yield return current;
            }
        }

        private static bool StillInRaid()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            return world != null && RaidTimers.Running;
        }

        private IEnumerator Factory()
        {
            AudioClip alarm = Clip("factoryAlarm");
            Run(Loop(alarm, RaidTimers.AlarmVolume));
            yield return new WaitForSeconds(6f);
            Run(Mortars.Barrage(2.5f, 8f));
            yield return new WaitForSeconds(4f);
            Run(EventBots.Waves(new[] { WildSpawnType.assault }, 2, 4, 35f, 60f, 10));
        }

        private IEnumerator Reserve()
        {
            AudioClip siren = Clip("reserveAlarm");
            Play(siren, RaidTimers.AlarmVolume);
            yield return new WaitForSeconds(siren != null ? siren.length : 30f);
            Run(Mortars.Barrage(2f, 7f));
            Run(EventBots.Waves(new[] { WildSpawnType.exUsec }, 2, 3, 45f, 75f, 8));
        }

        private IEnumerator Labs()
        {
            AudioClip alarm = Clip("labsDeathAlarm");
            Play(alarm, RaidTimers.AlarmVolume);
            yield return new WaitForSeconds(alarm != null ? alarm.length : 12f);
            Run(Infection());
            Run(EventBots.Waves(new[] { WildSpawnType.infectedLaborant, WildSpawnType.infectedCivil, WildSpawnType.infectedAssault }, 3, 5, 30f, 50f, 12));
        }

        private static IEnumerator Infection()
        {
            var wait = new WaitForSeconds(5f);
            while (StillInRaid())
            {
                foreach (Player player in Singleton<GameWorld>.Instance.AllAlivePlayersList.ToList())
                {
                    try
                    {
                        if (player == null || player.ActiveHealthController == null || !player.HealthController.IsAlive)
                        {
                            continue;
                        }
                        bool local = player.IsYourPlayer || (player.IsAI && Coop.IsServer);
                        if (!local || (player.IsAI && EventBots.IsInfected(player)))
                        {
                            continue;
                        }
                        player.ActiveHealthController.DoZombieInfection(EBodyPart.Head);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning($"[Raid Timers] Could not infect {player?.Profile?.Nickname}: {e.Message}");
                    }
                }
                yield return wait;
            }
        }

        private static IEnumerator Loop(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                yield break;
            }
            while (StillInRaid())
            {
                Play(clip, volume);
                yield return new WaitForSeconds(clip.length);
            }
        }

        private static void Beep()
        {
            Player player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || !player.HealthController.IsAlive)
            {
                return;
            }
            Play(Clip("watchBeep"), RaidTimers.BeepVolume, BetterAudio.AudioSourceGroupType.Character);
        }

        private static void Play(AudioClip clip, float volume, BetterAudio.AudioSourceGroupType group = BetterAudio.AudioSourceGroupType.Environment)
        {
            BetterAudio audio = Singleton<BetterAudio>.Instance;
            if (clip == null || audio == null || volume <= 0f)
            {
                return;
            }
            audio.PlayNonspatial(clip, group, 0f, volume);
        }

        private static AudioClip Clip(string name)
        {
            if (Clips.TryGetValue(name, out AudioClip cached))
            {
                return cached;
            }
            AudioClip clip = null;
            try
            {
                using (Stream stream = typeof(RaidEvents).Assembly.GetManifestResourceStream("ReClassic.Resources." + name + ".wav"))
                using (var memory = new MemoryStream())
                {
                    if (stream != null)
                    {
                        stream.CopyTo(memory);
                        clip = Patches.WorldInteractions.Wav.ToClip(name, memory.ToArray());
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Raid Timers] Could not load {name}: {e.Message}");
            }
            if (clip == null)
            {
                Plugin.LogSource.LogWarning($"[Raid Timers] The {name} sound is missing.");
            }
            Clips[name] = clip;
            return clip;
        }
    }
}
