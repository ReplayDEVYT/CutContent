using Comfort.Common;
using EFT;
using EFT.Ballistics;
using mizmii.ReClassic.Consciousness;
using mizmii.ReClassic.Patches;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mizmii.ReClassic.Karma
{
    internal static class KarmaTracker
    {
        private static readonly Dictionary<string, Dictionary<string, bool>> Neutral = new Dictionary<string, Dictionary<string, bool>>();
        private static readonly Dictionary<string, Dictionary<string, bool>> NeutralWhenHit = new Dictionary<string, Dictionary<string, bool>>();
        private static readonly Dictionary<string, int> Raiders = new Dictionary<string, int>();
        private static readonly HashSet<string> Counted = new HashSet<string>();
        private static readonly Dictionary<string, string> Personalities = new Dictionary<string, string>();
        private static readonly HashSet<string> Taunted = new HashSet<string>();
        private static float _nextRefresh;
        private static bool _hooked;

        public static void Hook()
        {
            if (_hooked)
            {
                return;
            }
            _hooked = true;
            Player.OnPlayerDeadStatic += OnDead;
        }

        public static void Reset()
        {
            Neutral.Clear();
            NeutralWhenHit.Clear();
            Raiders.Clear();
            Counted.Clear();
            Personalities.Clear();
            Taunted.Clear();
            _nextRefresh = 0f;
        }

        public static bool IsPmc(Player player)
        {
            if (player?.Profile?.Info == null)
            {
                return false;
            }
            EPlayerSide side = player.Profile.Info.Side;
            return side == EPlayerSide.Usec || side == EPlayerSide.Bear;
        }

        public static bool IsPmcBot(Player bot)
        {
            if (bot == null || !bot.IsAI || bot.Profile?.Info?.Settings == null)
            {
                return false;
            }
            WildSpawnType role = bot.Profile.Info.Settings.Role;
            return role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR;
        }

        public static string PersonalityOf(Player bot)
        {
            if (bot == null)
            {
                return string.Empty;
            }
            if (Personalities.TryGetValue(bot.ProfileId, out string cached))
            {
                return cached;
            }
            string personality = PmcKarma.Personality(bot.AIData?.BotOwner);
            if (!string.IsNullOrEmpty(personality))
            {
                Personalities[bot.ProfileId] = personality;
            }
            return personality;
        }

        public static bool Timid(Player bot)
        {
            string personality = PersonalityOf(bot);
            return personality == "Timmy" || personality == "Coward";
        }

        public static bool LowKarma(Player bot)
        {
            if (!IsPmcBot(bot))
            {
                return false;
            }
            if (PmcKarma.SainAvailable)
            {
                string personality = PersonalityOf(bot);
                return personality == "Chad" || personality == "GigaChad";
            }
            uint hash = 2166136261;
            foreach (char c in bot.ProfileId ?? string.Empty)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash / (double)uint.MaxValue < PmcKarma.LowKarmaChance;
        }

        public static void Tick()
        {
            if (Time.time < _nextRefresh)
            {
                return;
            }
            _nextRefresh = Time.time + 1.5f;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world?.AllAlivePlayersList == null)
            {
                return;
            }
            var humans = new List<Player>();
            foreach (Player player in world.AllAlivePlayersList)
            {
                if (PlayerKind.IsHuman(player) && IsPmc(player))
                {
                    humans.Add(player);
                }
            }
            if (humans.Count == 0)
            {
                return;
            }
            foreach (Player bot in world.AllAlivePlayersList)
            {
                if (bot == null || !bot.IsAI || bot.AIData?.BotOwner?.BotsGroup == null)
                {
                    continue;
                }
                if (!Neutral.TryGetValue(bot.ProfileId, out Dictionary<string, bool> map))
                {
                    map = new Dictionary<string, bool>();
                    Neutral[bot.ProfileId] = map;
                }
                bool lowKarma = LowKarma(bot);
                foreach (Player human in humans)
                {
                    map[human.ProfileId] = ComputeNeutral(bot, human);
                    if (lowKarma)
                    {
                        TryTaunt(bot, human);
                    }
                }
            }
        }

        private static void TryTaunt(Player bot, Player human)
        {
            if (Taunted.Contains(bot.ProfileId) || (bot.Position - human.Position).sqrMagnitude > 70f * 70f)
            {
                return;
            }
            Dictionary<IPlayer, EnemyInfo> enemies = bot.AIData?.BotOwner?.EnemiesController?.EnemyInfos;
            if (enemies == null)
            {
                return;
            }
            foreach (var pair in enemies)
            {
                if (pair.Key == null || pair.Key.ProfileId != human.ProfileId || pair.Value == null || !pair.Value.IsVisible)
                {
                    continue;
                }
                Taunted.Add(bot.ProfileId);
                if (UnityEngine.Random.value < 0.8f)
                {
                    EPhraseTrigger first = UnityEngine.Random.value < 0.7f ? EPhraseTrigger.LowKarmaAttack : EPhraseTrigger.Provocation;
                    BotVoice.Say(bot, first, true, EPhraseTrigger.LowKarmaAttack, EPhraseTrigger.Provocation, EPhraseTrigger.OnFight);
                }
                return;
            }
        }

        private static bool ComputeNeutral(Player bot, Player human)
        {
            try
            {
                BotsGroup group = bot.AIData?.BotOwner?.BotsGroup;
                if (group == null || HelpRequests.HasPact(bot))
                {
                    return false;
                }
                return !group.IsEnemy(human) && !group.IsPlayerEnemy(human);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool WasNeutral(Player bot, Player human)
        {
            if (NeutralWhenHit.TryGetValue(bot.ProfileId, out Dictionary<string, bool> hit) && hit.TryGetValue(human.ProfileId, out bool value))
            {
                return value;
            }
            return IsNeutral(bot, human);
        }

        public static bool IsNeutral(Player bot, Player human)
        {
            if (Neutral.TryGetValue(bot.ProfileId, out Dictionary<string, bool> map) && map.TryGetValue(human.ProfileId, out bool value))
            {
                return value;
            }
            return ComputeNeutral(bot, human);
        }

        public static void OnHit(DamageInfo damageInfo, Player victim)
        {
            try
            {
                if (!Active() || victim == null || !victim.IsAI)
                {
                    return;
                }
                Player attacker = Attacker(damageInfo.Player?.iPlayer);
                if (attacker == null || !PlayerKind.IsHuman(attacker) || !IsPmc(attacker))
                {
                    return;
                }
                if (!NeutralWhenHit.TryGetValue(victim.ProfileId, out Dictionary<string, bool> map))
                {
                    map = new Dictionary<string, bool>();
                    NeutralWhenHit[victim.ProfileId] = map;
                }
                if (!map.ContainsKey(attacker.ProfileId))
                {
                    map[attacker.ProfileId] = IsNeutral(victim, attacker);
                }
                HelpRequests.OnHit(victim, attacker, damageInfo.Damage);
                Fleeing.OnHit(victim, attacker);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not check a hit: {e.Message}");
            }
        }

        private static Player Attacker(IPlayer aggressor)
        {
            if (aggressor == null)
            {
                return null;
            }
            return aggressor as Player ?? Singleton<GameWorld>.Instance?.GetEverExistedPlayerByID(aggressor.ProfileId);
        }

        private static bool Active()
        {
            return PmcKarma.Enabled && Coop.IsServer && Singleton<GameWorld>.Instance != null;
        }

        private static void OnDead(Player victim, IPlayer aggressor, DamageInfo damageInfo, EBodyPart part)
        {
            try
            {
                if (!Active() || victim == null || !Counted.Add(victim.ProfileId))
                {
                    return;
                }
                Player killer = Attacker(aggressor);
                if (killer == null || killer == victim || !PlayerKind.IsHuman(killer) || !IsPmc(killer))
                {
                    return;
                }
                if (!victim.IsAI)
                {
                    if (PlayerKind.IsHuman(victim) && PmcKarma.TeammateKills && Coop.InMatch)
                    {
                        KarmaClient.Add(killer, "teammateKill", "Killed a teammate");
                    }
                    return;
                }
                Judge(killer, victim);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not judge a kill: {e.Message}");
            }
        }

        private static void Judge(Player killer, Player victim)
        {
            switch (HelpRequests.OnKilled(victim, killer))
            {
                case PactKill.Betrayal:
                    KarmaClient.Add(killer, "betrayal", "Betrayed a PMC you agreed to help");
                    return;
                case PactKill.Ignored:
                    return;
            }
            if (WasNeutral(victim, killer))
            {
                KarmaClient.Add(killer, "neutralKill", "Killed a neutral " + RoleName(victim));
                return;
            }
            WildSpawnType role = victim.Profile.Info.Settings.Role;
            if (role.IsBoss() && !role.IsFollower() && role != WildSpawnType.bossPartisan)
            {
                KarmaClient.Add(killer, "bossKill", "Killed a boss");
            }
            else if (role == WildSpawnType.pmcBot)
            {
                Raiders.TryGetValue(killer.ProfileId, out int raiders);
                raiders++;
                Raiders[killer.ProfileId] = raiders;
                if (raiders % 2 == 0)
                {
                    KarmaClient.Add(killer, "raiderPair", "Killed two raiders");
                }
            }
            else if (IsPmcBot(victim))
            {
                if (LowKarma(victim))
                {
                    KarmaClient.Add(killer, "lowKarmaPmcKill", "Killed a low-karma PMC");
                }
                else if (Fleeing.KilledWhileFleeing(victim, killer))
                {
                    KarmaClient.Add(killer, "fleeingBeginnerKill", "Killed a PMC who was running away");
                }
            }
            if (Assisted(victim, killer))
            {
                KarmaClient.Add(killer, "assistedNeutral", "Helped a neutral fighter");
            }
        }

        private static string RoleName(Player bot)
        {
            WildSpawnType role = bot.Profile.Info.Settings.Role;
            if (role.IsBoss())
            {
                return "boss";
            }
            if (role == WildSpawnType.exUsec)
            {
                return "rogue";
            }
            return IsPmcBot(bot) ? "PMC" : "fighter";
        }

        private static bool Assisted(Player victim, Player killer)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            BotsGroup victimGroup = victim.AIData?.BotOwner?.BotsGroup;
            if (world?.AllAlivePlayersList == null)
            {
                return false;
            }
            foreach (Player other in world.AllAlivePlayersList)
            {
                if (other == null || other == victim || !other.IsAI || (other.Position - victim.Position).sqrMagnitude > 100f * 100f)
                {
                    continue;
                }
                if (!IsNeutral(other, killer) && !HelpRequests.IsFriendlyTo(other, killer))
                {
                    continue;
                }
                BotsGroup group = other.AIData?.BotOwner?.BotsGroup;
                bool fighting = (group != null && group.IsEnemy(victim)) || (victimGroup != null && victimGroup.IsEnemy(other));
                if (fighting)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
