using Comfort.Common;
using EFT;
using mizmii.ReClassic.Consciousness;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Raid
{
    internal static class EventBots
    {
        private static readonly HashSet<WildSpawnType> Roles = new HashSet<WildSpawnType>();
        private static readonly List<BotOwner> Pending = new List<BotOwner>();
        private static BotSpawner _spawner;

        public static void Reset()
        {
            Roles.Clear();
            Pending.Clear();
            if (_spawner != null)
            {
                _spawner.OnBotCreated -= OnBotCreated;
                _spawner = null;
            }
        }

        public static bool IsInfected(Player player)
        {
            WildSpawnType role = player.Profile.Info.Settings.Role;
            return role == WildSpawnType.infectedAssault || role == WildSpawnType.infectedCivil || role == WildSpawnType.infectedLaborant || role == WildSpawnType.infectedPmc || role == WildSpawnType.infectedTagilla;
        }

        public static IEnumerator Waves(WildSpawnType[] roles, int minCount, int maxCount, float minDelay, float maxDelay, int cap)
        {
            if (!Coop.IsServer)
            {
                yield break;
            }
            foreach (WildSpawnType role in roles)
            {
                Roles.Add(role);
            }
            Hook();
            while (Singleton<GameWorld>.Instance != null && RaidTimers.Running)
            {
                Hunt();
                int alive = Alive();
                if (alive < cap)
                {
                    int count = Mathf.Min(UnityEngine.Random.Range(minCount, maxCount + 1), cap - alive);
                    Spawn(roles[UnityEngine.Random.Range(0, roles.Length)], count);
                }
                float wait = UnityEngine.Random.Range(minDelay, maxDelay);
                float until = Time.time + wait;
                while (Time.time < until)
                {
                    yield return new WaitForSeconds(2f);
                    Hunt();
                }
            }
        }

        private static void Hook()
        {
            BotSpawner spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
            if (spawner == null || spawner == _spawner)
            {
                return;
            }
            if (_spawner != null)
            {
                _spawner.OnBotCreated -= OnBotCreated;
            }
            _spawner = spawner;
            _spawner.OnBotCreated += OnBotCreated;
        }

        private static void OnBotCreated(BotOwner bot)
        {
            if (bot != null && Roles.Contains(bot.Profile.Info.Settings.Role))
            {
                Pending.Add(bot);
            }
        }

        private static int Alive()
        {
            return Singleton<GameWorld>.Instance.AllAlivePlayersList.Count(p => p != null && p.IsAI && Roles.Contains(p.Profile.Info.Settings.Role));
        }

        private static void Spawn(WildSpawnType role, int count)
        {
            BotsController controller = Singleton<IBotGame>.Instance?.BotsController;
            if (controller == null || count <= 0)
            {
                return;
            }
            try
            {
                controller.ActivateBotsWithoutWave(count, new GetProfileDataParams(EPlayerSide.Savage, role, BotDifficulty.normal, 0f));
                Plugin.LogSource.LogInfo($"[Raid Timers] Sending in {count} {role}.");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Raid Timers] Could not spawn {role}: {e}");
            }
        }

        private static void Hunt()
        {
            if (Pending.Count == 0)
            {
                return;
            }
            List<Player> humans = Singleton<GameWorld>.Instance.AllAlivePlayersList.Where(p => p != null && !p.IsAI && p.HealthController.IsAlive && !Coop.IsDown(p)).ToList();
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                BotOwner bot = Pending[i];
                if (bot == null || bot.IsDead)
                {
                    Pending.RemoveAt(i);
                    continue;
                }
                if (bot.BotsGroup == null || bot.BotState != EBotState.Active)
                {
                    continue;
                }
                foreach (Player human in humans)
                {
                    try
                    {
                        bot.BotsGroup.AddEnemy(human, EBotEnemyCause.initial);
                    }
                    catch (Exception e)
                    {
                        Plugin.LogSource.LogWarning($"[Raid Timers] Could not point a bot at {human.Profile.Nickname}: {e.Message}");
                    }
                }
                Pending.RemoveAt(i);
            }
        }
    }
}
