using CommonAssets.Scripts.Game;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using mizmii.ReClassic.Consciousness;
using mizmii.ReClassic.Patches;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Karma
{
    internal enum PactState
    {
        Requesting,
        Allied,
        Done,
        Leaving
    }

    internal enum HelpTask
    {
        Escort,
        Ammo,
        Meds,
        Food
    }

    internal enum PactKill
    {
        None,
        Betrayal,
        Ignored
    }

    internal enum BrainMode
    {
        None,
        Wait,
        Follow
    }

    internal sealed class Pact
    {
        public Player Bot;
        public PactState State;
        public Player Ally;
        public float Since;
        public float NextPhrase;
        public HelpTask Task;
        public string Caliber;
        public bool OtherFaction;
        public bool Holding;
        public float LeaveAt;
        public LootItem Pending;
        public float ConsumeAt;
        public float ReviveStart;
        public readonly Dictionary<string, float> Damage = new Dictionary<string, float>();

        public BotOwner Owner => Bot?.AIData?.BotOwner;
        public BotsGroup Group => Owner?.BotsGroup;
        public string Name => Bot?.Profile?.Nickname ?? "A PMC";
        public string Faction => Bot?.Profile?.Info?.Side == EPlayerSide.Bear ? "BEAR" : "USEC";
    }

    internal static class HelpRequests
    {
        private const float RequestTimeout = 150f;
        private const float AllyTimeout = 1200f;
        private const float AnswerRange = 30f;
        private const float GestureRange = 25f;
        private const float ReviveRange = 2.2f;
        private const float ReviveSeconds = 5f;
        private const string FireteamGuid = "xyz.pit.fireteam";

        private static Func<string, bool> _isFireteamFollower;
        private static bool _fireteamChecked;

        private static readonly Dictionary<string, Pact> Pacts = new Dictionary<string, Pact>();
        private static readonly Dictionary<string, HashSet<string>> Agreed = new Dictionary<string, HashSet<string>>();
        private static readonly HashSet<string> Rolled = new HashSet<string>();
        private static readonly HashSet<string> HitByHuman = new HashSet<string>();
        private static readonly Dictionary<string, float> LastHeard = new Dictionary<string, float>();
        private static int _requests;
        private static float _nextTick;

        public static bool BrainAvailable { get; set; }

        public static void Reset()
        {
            Pacts.Clear();
            Agreed.Clear();
            Rolled.Clear();
            HitByHuman.Clear();
            LastHeard.Clear();
            _requests = 0;
            _nextTick = 0f;
        }

        private static bool Active => PmcKarma.Enabled && PmcKarma.HelpRequestsEnabled && Coop.IsServer && Singleton<GameWorld>.Instance != null;

        private static Pact PactOf(Player bot)
        {
            return bot != null && Pacts.TryGetValue(bot.ProfileId, out Pact pact) ? pact : null;
        }

        public static bool HasPact(Player bot)
        {
            return PactOf(bot) != null;
        }

        public static bool IsFriendlyTo(Player bot, Player human)
        {
            Pact pact = PactOf(bot);
            return pact != null && pact.State != PactState.Requesting;
        }

        public static BrainMode Mode(BotOwner owner)
        {
            Pact pact = PactOf(owner?.GetPlayer);
            if (pact == null)
            {
                return BrainMode.None;
            }
            switch (pact.State)
            {
                case PactState.Requesting:
                case PactState.Leaving:
                    return BrainMode.Wait;
                case PactState.Allied:
                    if (pact.Ally != null && Alive(pact.Ally) && IsAllyDown(pact.Ally))
                    {
                        return BrainMode.Follow;
                    }
                    return pact.Holding || pact.Ally == null || !Alive(pact.Ally) ? BrainMode.Wait : BrainMode.Follow;
                default:
                    return BrainMode.None;
            }
        }

        public static Player Target(BotOwner owner)
        {
            Pact pact = PactOf(owner?.GetPlayer);
            if (pact == null)
            {
                return null;
            }
            if (pact.Ally != null && Alive(pact.Ally))
            {
                return pact.Ally;
            }
            return NearestHuman(pact.Bot.Position, 60f);
        }

        public static bool AllowEnemy(BotsGroup group, IPlayer person)
        {
            if (Pacts.Count == 0 || group == null || person == null)
            {
                return true;
            }
            foreach (Pact pact in Pacts.Values)
            {
                if (pact.Group == group && (!person.IsAI || IsFireteamFollower(person)))
                {
                    return false;
                }
            }
            if (person.IsAI && Pacts.ContainsKey(person.ProfileId) && IsFireteamGroup(group))
            {
                return false;
            }
            return true;
        }

        public static bool IsAllyDown(Player player)
        {
            if (player == null)
            {
                return false;
            }
            return Coop.IsDown(player) || (player.IsYourPlayer && Knockout.Active != null);
        }

        public static bool IsFireteamFollower(IPlayer person)
        {
            if (person == null || !person.IsAI)
            {
                return false;
            }
            if (!_fireteamChecked)
            {
                _fireteamChecked = true;
                try
                {
                    if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(FireteamGuid))
                    {
                        Type type = HarmonyLib.AccessTools.TypeByName("pitTeam.Modules.BossPlayers");
                        MethodInfo method = type != null ? HarmonyLib.AccessTools.Method(type, "IsFollowerProfileId", new[] { typeof(string) }) : null;
                        if (method != null && method.IsStatic)
                        {
                            _isFireteamFollower = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), method);
                            Plugin.LogSource.LogInfo("[PMC Karma] PIT Fire Team found, its squadmates will leave PMCs you're helping alone.");
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not link up with PIT Fire Team: {e.Message}");
                }
            }
            if (_isFireteamFollower == null)
            {
                return false;
            }
            try
            {
                return _isFireteamFollower(person.ProfileId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsFireteamGroup(BotsGroup group)
        {
            if (group == null)
            {
                return false;
            }
            for (int i = 0; i < group.MembersCount; i++)
            {
                BotOwner member = group.Member(i);
                if (member != null && IsFireteamFollower(member.GetPlayer))
                {
                    return true;
                }
            }
            return false;
        }

        private static void MakePeace(Pact pact)
        {
            BotsGroup group = pact.Group;
            foreach (Player human in PlayerKind.Humans())
            {
                if (!Alive(human))
                {
                    continue;
                }
                try
                {
                    group?.RemoveEnemy(human);
                    if (pact.State == PactState.Allied)
                    {
                        group?.AddAlly(human);
                    }
                    else
                    {
                        group?.AddNeutral(human);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not calm {pact.Name} down: {e.Message}");
                }
            }
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null)
            {
                return;
            }
            foreach (Player other in world.AllAlivePlayersList)
            {
                if (other == null || other == pact.Bot || !IsFireteamFollower(other))
                {
                    continue;
                }
                try
                {
                    other.AIData?.BotOwner?.BotsGroup?.RemoveEnemy(pact.Bot);
                    group?.RemoveEnemy(other);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not keep the squad off {pact.Name}: {e.Message}");
                }
            }
        }

        public static bool Alive(Player player)
        {
            return player != null && player.HealthController != null && player.HealthController.IsAlive;
        }

        private static Player NearestHuman(Vector3 position, float range)
        {
            Player best = null;
            float bestDistance = range * range;
            foreach (Player human in PlayerKind.Humans())
            {
                float distance = (human.Position - position).sqrMagnitude;
                if (distance < bestDistance && Alive(human))
                {
                    best = human;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static IEnumerable<Player> Humans()
        {
            return PlayerKind.Humans().Where(h => Alive(h) && KarmaTracker.IsPmc(h));
        }

        public static void OnSound(IPlayer source, Vector3 position, float power)
        {
            try
            {
                if (!Active || source == null || source.IsAI)
                {
                    return;
                }
                Player human = source as Player ?? Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(source.ProfileId);
                if (human == null || !PlayerKind.IsHuman(human) || !KarmaTracker.IsPmc(human))
                {
                    return;
                }
                if (LastHeard.TryGetValue(human.ProfileId, out float last) && Time.time - last < 0.5f)
                {
                    return;
                }
                LastHeard[human.ProfileId] = Time.time;
                Heard(human, position, Mathf.Clamp(power, 15f, 60f));
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not check who heard a sound: {e.Message}");
            }
        }

        private static void Heard(Player human, Vector3 position, float range)
        {
            if (_requests >= PmcKarma.MaxRequests || Pacts.Values.Any(p => p.State == PactState.Requesting))
            {
                return;
            }
            GameWorld world = Singleton<GameWorld>.Instance;
            foreach (Player bot in world.AllAlivePlayersList)
            {
                if (!KarmaTracker.IsPmcBot(bot) || Rolled.Contains(bot.ProfileId) || (bot.Position - position).sqrMagnitude > range * range)
                {
                    continue;
                }
                if (!CanAsk(bot))
                {
                    continue;
                }
                Rolled.Add(bot.ProfileId);
                float chance = Chance(bot, human);
                if (UnityEngine.Random.value < chance)
                {
                    Request(bot);
                    return;
                }
            }
        }

        public static bool InSquad(Player bot)
        {
            BotOwner owner = bot?.AIData?.BotOwner;
            BotsGroup group = owner?.BotsGroup;
            if (owner == null || group == null)
            {
                return false;
            }
            try
            {
                if (group.MembersCount > 1)
                {
                    return true;
                }
                if (owner.Boss != null && owner.Boss.HaveFollowers())
                {
                    return true;
                }
                if (owner.BotFollower != null && owner.BotFollower.HaveBoss)
                {
                    return true;
                }
                foreach (IPlayer ally in group.Allies)
                {
                    if (ally is Player mate && mate != bot && mate.IsAI && Alive(mate))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                return true;
            }
            return false;
        }

        private static bool CanAsk(Player bot)
        {
            BotOwner owner = bot.AIData?.BotOwner;
            if (owner?.BotsGroup == null || InSquad(bot) || HitByHuman.Contains(bot.ProfileId) || PactOf(bot) != null)
            {
                return false;
            }
            EnemyInfo goal = owner.Memory?.GoalEnemy;
            if (goal != null && (goal.IsVisible || Time.time - goal.PersonalLastSeenTime < 10f))
            {
                return false;
            }
            return BrainAvailable || Needs(bot).Count > 0;
        }

        private static float Chance(Player bot, Player human)
        {
            List<HelpTask> needs = Needs(bot);
            bool lowKarma = KarmaTracker.LowKarma(bot);
            float chance = lowKarma ? 0f : PmcKarma.RequestChance;
            if (needs.Contains(HelpTask.Ammo))
            {
                chance += 0.3f;
            }
            if (needs.Contains(HelpTask.Meds))
            {
                chance += 0.3f;
            }
            if (needs.Contains(HelpTask.Food))
            {
                chance += 0.2f;
            }
            if (KarmaTracker.Timid(bot))
            {
                chance = chance * 2f + 0.08f;
            }
            if (lowKarma)
            {
                chance *= 0.5f;
            }
            double karma = KarmaClient.Of(human.ProfileId);
            if (karma < -15)
            {
                chance *= 0.5f;
            }
            else if (karma > 15)
            {
                chance *= 1.3f;
            }
            return Mathf.Clamp(chance, 0f, 0.9f);
        }

        public static List<HelpTask> Needs(Player bot)
        {
            var needs = new List<HelpTask>();
            try
            {
                List<Item> items = bot.Inventory.GetPlayerItems(EPlayerItems.Equipment).ToList();
                Weapon weapon = bot.HandsController?.Item as Weapon ?? bot.AIData?.BotOwner?.WeaponManager?.CurrentWeapon;
                if (weapon != null)
                {
                    string caliber = weapon.AmmoCaliber;
                    int rounds = items.OfType<Ammo>().Where(a => a.Caliber == caliber).Sum(a => a.StackObjectsCount);
                    int capacity = weapon.GetCurrentMagazine()?.MaxCount ?? 30;
                    if (rounds < Mathf.Max(10, capacity))
                    {
                        needs.Add(HelpTask.Ammo);
                    }
                }
                var health = bot.HealthController.GetBodyPartHealth(EBodyPart.Common);
                if (health.Maximum > 0f && health.Current / health.Maximum < 0.6f && !items.Any(i => i is MedKit))
                {
                    needs.Add(HelpTask.Meds);
                }
                var energy = bot.HealthController.Energy;
                var hydration = bot.HealthController.Hydration;
                bool hungry = (energy.Maximum > 0f && energy.Current / energy.Maximum < 0.25f) || (hydration.Maximum > 0f && hydration.Current / hydration.Maximum < 0.25f);
                if (hungry && !items.Any(i => i is FoodDrink))
                {
                    needs.Add(HelpTask.Food);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not check what {bot.Profile?.Nickname} needs: {e.Message}");
            }
            return needs;
        }

        public static void ForceRequest()
        {
            if (!Coop.IsServer)
            {
                KarmaClient.Notice(Coop.LocalPlayer, "Only the host can force a help request.");
                return;
            }
            Player local = Coop.LocalPlayer;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (local == null || world == null)
            {
                return;
            }
            Player best = world.AllAlivePlayersList
                .Where(p => KarmaTracker.IsPmcBot(p) && PactOf(p) == null && !InSquad(p))
                .OrderBy(p => (p.Position - local.Position).sqrMagnitude)
                .FirstOrDefault();
            if (best == null)
            {
                KarmaClient.Notice(local, "There are no PMCs left to ask for help.");
                return;
            }
            Rolled.Add(best.ProfileId);
            Request(best);
        }

        private static void Request(Player bot)
        {
            BotsGroup group = bot.AIData?.BotOwner?.BotsGroup;
            if (group == null)
            {
                return;
            }
            var pact = new Pact { Bot = bot, State = PactState.Requesting, Since = Time.time, NextPhrase = Time.time + 25f };
            Pacts[bot.ProfileId] = pact;
            _requests++;
            MakePeace(pact);
            BotVoice.Say(bot, EPhraseTrigger.Cooperation, false, EPhraseTrigger.NeedHelp);
            Plugin.LogSource.LogInfo($"[PMC Karma] {pact.Name} ({pact.Faction}) is asking for help.");
            foreach (Player human in Humans())
            {
                if ((human.Position - bot.Position).sqrMagnitude < 90f * 90f)
                {
                    KarmaClient.Notice(human, $"{pact.Name} ({pact.Faction}) is asking for help. Gesture, speak, or use \"Cooperate\" on them to team up. You can still engage in combat with them.");
                }
            }
        }

        public static void OnPhrase(IPlayer speaker, EPhraseTrigger phrase)
        {
            try
            {
                if (!Active || speaker == null || speaker.IsAI || Pacts.Count == 0)
                {
                    return;
                }
                Player human = speaker as Player ?? Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(speaker.ProfileId);
                if (human == null || !PlayerKind.IsHuman(human) || !KarmaTracker.IsPmc(human))
                {
                    return;
                }
                switch (phrase)
                {
                    case EPhraseTrigger.Cooperation:
                    case EPhraseTrigger.Roger:
                        Accept(Nearest(human, PactState.Requesting, AnswerRange), human);
                        break;
                    case EPhraseTrigger.OnFight:
                    case EPhraseTrigger.Provocation:
                    case EPhraseTrigger.LowKarmaAttack:
                        Refuse(Nearest(human, PactState.Requesting, AnswerRange), human, phrase);
                        break;
                    case EPhraseTrigger.HoldPosition:
                    case EPhraseTrigger.Stop:
                        SetHolding(human, true);
                        break;
                    case EPhraseTrigger.FollowMe:
                    case EPhraseTrigger.Gogogo:
                    case EPhraseTrigger.Regroup:
                        SetHolding(human, false);
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not handle a voiceline: {e.Message}");
            }
        }

        public static void OnGesture(Player player, EInteraction gesture)
        {
            try
            {
                if (!Active || player == null || Pacts.Count == 0 || !PlayerKind.IsHuman(player) || !KarmaTracker.IsPmc(player))
                {
                    return;
                }
                switch (gesture)
                {
                    case EInteraction.OkGesture:
                    case EInteraction.AllRightGesture:
                    case EInteraction.FriendlyGesture:
                        Accept(Nearest(player, PactState.Requesting, GestureRange), player);
                        break;
                    case EInteraction.HoldGesture:
                        SetHolding(player, true);
                        break;
                    case EInteraction.ComeWithMeGesture:
                        SetHolding(player, false);
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not handle a gesture: {e.Message}");
            }
        }

        private static Pact Nearest(Player human, PactState state, float range)
        {
            Pact best = null;
            float bestDistance = range * range;
            foreach (Pact pact in Pacts.Values)
            {
                if (pact.State != state || !Alive(pact.Bot))
                {
                    continue;
                }
                float distance = (pact.Bot.Position - human.Position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = pact;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static void SetHolding(Player human, bool holding)
        {
            foreach (Pact pact in Pacts.Values)
            {
                if (pact.State == PactState.Allied && pact.Ally == human && pact.Holding != holding && (pact.Bot.Position - human.Position).sqrMagnitude < 40f * 40f)
                {
                    pact.Holding = holding;
                    BotVoice.Say(pact.Bot, EPhraseTrigger.Roger);
                }
            }
        }

        private static void Accept(Pact pact, Player human)
        {
            if (pact == null || pact.State != PactState.Requesting)
            {
                return;
            }
            pact.State = PactState.Allied;
            pact.Ally = human;
            pact.Since = Time.time;
            pact.OtherFaction = pact.Bot.Profile.Info.Side != human.Profile.Info.Side;
            if (!Agreed.TryGetValue(pact.Bot.ProfileId, out HashSet<string> agreed))
            {
                agreed = new HashSet<string>();
                Agreed[pact.Bot.ProfileId] = agreed;
            }
            agreed.Add(human.ProfileId);
            MakePeace(pact);
            List<HelpTask> needs = Needs(pact.Bot);
            pact.Task = needs.Count > 0 ? needs[0] : (BrainAvailable ? HelpTask.Escort : HelpTask.Ammo);
            Weapon weapon = pact.Bot.HandsController?.Item as Weapon ?? pact.Owner?.WeaponManager?.CurrentWeapon;
            pact.Caliber = weapon?.AmmoCaliber;
            BotVoice.Say(pact.Bot, EPhraseTrigger.Roger);
            Plugin.LogSource.LogInfo($"[PMC Karma] {pact.Name} teamed up with {human.Profile.Nickname} ({pact.Task}).");
            KarmaClient.Notice(human, $"{pact.Name} teamed up with you. {TaskText(pact)}");
        }

        private static string TaskText(Pact pact)
        {
            string follow = BrainAvailable ? " They'll follow you while you look. Say Hold Position to make them wait, Follow Me to bring them along again." : string.Empty;
            switch (pact.Task)
            {
                case HelpTask.Ammo:
                    string caliber = string.IsNullOrEmpty(pact.Caliber) ? "their gun" : ("Caliber" + pact.Caliber).Localized();
                    return $"They're out of ammo. Find some {caliber} ammo and drop it next to them." + follow;
                case HelpTask.Meds:
                    return "They're badly hurt. Find a medkit and drop it next to them." + follow;
                case HelpTask.Food:
                    return "They're starving and thirsty. Find some food or a drink and drop it next to them." + follow;
                default:
                    return "They want to get out of here. Lead them to an extract, they'll follow you. Say Hold Position to make them wait, Follow Me to bring them along again.";
            }
        }

        private static void Refuse(Pact pact, Player human, EPhraseTrigger phrase)
        {
            if (pact == null)
            {
                return;
            }
            BotVoice.Say(pact.Bot, phrase, true, EPhraseTrigger.OnFight, EPhraseTrigger.Provocation);
            Turn(pact, human);
            KarmaClient.Notice(human, $"{pact.Name} won't be asking you for help again.");
        }

        private static void Turn(Pact pact, Player against)
        {
            Pacts.Remove(pact.Bot.ProfileId);
            BotsGroup group = pact.Group;
            if (group == null)
            {
                return;
            }
            foreach (Player human in PlayerKind.Humans())
            {
                group.Allies.Remove(human);
                group.Neutrals.Remove(human);
            }
            if (against != null && Alive(against))
            {
                try
                {
                    group.AddEnemy(against, EBotEnemyCause.addPlayer);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not turn {pact.Name} hostile: {e.Message}");
                }
            }
        }

        private static void Release(Pact pact)
        {
            Pacts.Remove(pact.Bot.ProfileId);
            BotsGroup group = pact.Group;
            if (group == null)
            {
                return;
            }
            foreach (Player human in PlayerKind.Humans())
            {
                group.Neutrals.Remove(human);
            }
        }

        public static void OnHit(Player victim, Player attacker, float damage)
        {
            HitByHuman.Add(victim.ProfileId);
            Pact pact = PactOf(victim);
            if (pact == null || !Active)
            {
                return;
            }
            if (pact.State == PactState.Requesting)
            {
                Turn(pact, attacker);
                KarmaClient.Notice(attacker, $"You shot {pact.Name}. They're fighting back.");
                return;
            }
            pact.Damage.TryGetValue(attacker.ProfileId, out float total);
            total += Mathf.Max(1f, damage);
            pact.Damage[attacker.ProfileId] = total;
            if (total > 40f)
            {
                Turn(pact, attacker);
                KarmaClient.Notice(attacker, $"{pact.Name} has had enough of your friendly fire and turned on you.");
            }
            else
            {
                BotVoice.Say(victim, EPhraseTrigger.FriendlyFire);
            }
        }

        public static PactKill OnKilled(Player victim, Player killer)
        {
            Pact pact = PactOf(victim);
            if (pact != null)
            {
                Pacts.Remove(victim.ProfileId);
            }
            if (Agreed.TryGetValue(victim.ProfileId, out HashSet<string> agreed) && agreed.Contains(killer.ProfileId))
            {
                return PactKill.Betrayal;
            }
            return pact != null && pact.State == PactState.Requesting ? PactKill.Ignored : PactKill.None;
        }

        public static void OnDrop(LootItem loot, Item item, IPlayer player)
        {
            try
            {
                if (!Active || loot == null || item == null || player == null || player.IsAI || Pacts.Count == 0)
                {
                    return;
                }
                foreach (Pact pact in Pacts.Values)
                {
                    if (pact.State != PactState.Allied || pact.Task == HelpTask.Escort || pact.Pending != null || !Alive(pact.Bot))
                    {
                        continue;
                    }
                    if ((pact.Bot.Position - player.Position).sqrMagnitude > 6f * 6f || !Matches(pact, item))
                    {
                        continue;
                    }
                    pact.Pending = loot;
                    pact.ConsumeAt = Time.time + 1.5f;
                    return;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not check a dropped item: {e.Message}");
            }
        }

        private static bool Matches(Pact pact, Item item)
        {
            switch (pact.Task)
            {
                case HelpTask.Ammo:
                    return item.GetAllItems().OfType<Ammo>().Any(a => string.IsNullOrEmpty(pact.Caliber) || a.Caliber == pact.Caliber);
                case HelpTask.Meds:
                    return item is Meds;
                case HelpTask.Food:
                    return item is FoodDrink;
                default:
                    return false;
            }
        }

        private static void Consume(Pact pact)
        {
            LootItem loot = pact.Pending;
            pact.Pending = null;
            if (loot == null || loot.gameObject == null || (loot.transform.position - pact.Bot.Position).sqrMagnitude > 10f * 10f)
            {
                return;
            }
            try
            {
                Singleton<GameWorld>.Instance.DestroyLoot(loot.ItemId);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not hand over the item: {e.Message}");
            }
            try
            {
                switch (pact.Task)
                {
                    case HelpTask.Ammo:
                        pact.Owner?.WeaponManager?.Reload?.TryFillMagazines();
                        break;
                    case HelpTask.Meds:
                        pact.Bot.ActiveHealthController?.RestoreFullHealth();
                        break;
                    case HelpTask.Food:
                        pact.Bot.ActiveHealthController?.ChangeEnergy(60f);
                        pact.Bot.ActiveHealthController?.ChangeHydration(60f);
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not use the supplies on {pact.Name}: {e.Message}");
            }
            Complete(pact);
        }

        private static void Complete(Pact pact)
        {
            Player ally = pact.Ally;
            BotVoice.Say(pact.Bot, EPhraseTrigger.GoodWork, false, EPhraseTrigger.OnGoodWork, EPhraseTrigger.Roger);
            if (ally != null)
            {
                if (pact.OtherFaction)
                {
                    KarmaClient.Add(ally, "helpedOtherFaction", "Helped a PMC of the other faction");
                }
                else
                {
                    KarmaClient.Add(ally, "helpedPmc", "Helped a PMC");
                }
            }
            if (pact.Task == HelpTask.Escort)
            {
                pact.State = PactState.Leaving;
                pact.LeaveAt = Time.time + 4f;
                KarmaClient.Notice(ally, $"{pact.Name} made it out.");
            }
            else
            {
                pact.State = PactState.Done;
                KarmaClient.Notice(ally, $"{pact.Name} thanks you and heads off on their own.");
            }
        }

        public static void Tick()
        {
            if (!Active || Pacts.Count == 0 || Time.time < _nextTick)
            {
                return;
            }
            _nextTick = Time.time + 0.25f;
            foreach (Pact pact in Pacts.Values.ToList())
            {
                try
                {
                    Update(pact);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not update {pact.Name}: {e.Message}");
                    Pacts.Remove(pact.Bot?.ProfileId ?? string.Empty);
                }
            }
        }

        private static void Update(Pact pact)
        {
            if (!Alive(pact.Bot))
            {
                Pacts.Remove(pact.Bot.ProfileId);
                return;
            }
            switch (pact.State)
            {
                case PactState.Requesting:
                    if (Time.time - pact.Since > RequestTimeout)
                    {
                        Release(pact);
                        Player near = NearestHuman(pact.Bot.Position, 120f);
                        KarmaClient.Notice(near, $"{pact.Name} stopped waiting for help.");
                        return;
                    }
                    if (Time.time > pact.NextPhrase)
                    {
                        pact.NextPhrase = Time.time + 25f;
                        if (NearestHuman(pact.Bot.Position, 60f) != null)
                        {
                            BotVoice.Say(pact.Bot, EPhraseTrigger.Cooperation, false, EPhraseTrigger.NeedHelp);
                        }
                    }
                    break;
                case PactState.Allied:
                    if (!Alive(pact.Ally) || Time.time - pact.Since > AllyTimeout)
                    {
                        pact.State = PactState.Done;
                        return;
                    }
                    if (pact.Pending != null && Time.time >= pact.ConsumeAt)
                    {
                        Consume(pact);
                        return;
                    }
                    if (IsAllyDown(pact.Ally))
                    {
                        Revive(pact);
                        return;
                    }
                    pact.ReviveStart = 0f;
                    if (pact.Task == HelpTask.Escort && AtExtract(pact))
                    {
                        Complete(pact);
                    }
                    break;
                case PactState.Leaving:
                    if (Time.time >= pact.LeaveAt)
                    {
                        Pacts.Remove(pact.Bot.ProfileId);
                        try
                        {
                            pact.Owner?.LeaveData?.RemoveFromMap();
                        }
                        catch (Exception e)
                        {
                            Plugin.LogSource.LogWarning($"[PMC Karma] Could not take {pact.Name} out of the raid: {e.Message}");
                        }
                    }
                    break;
            }
        }

        private static void Revive(Pact pact)
        {
            Player ally = pact.Ally;
            if ((ally.Position - pact.Bot.Position).sqrMagnitude > ReviveRange * ReviveRange)
            {
                pact.ReviveStart = 0f;
                return;
            }
            if (pact.ReviveStart <= 0f)
            {
                pact.ReviveStart = Time.time;
                BotVoice.Say(pact.Bot, EPhraseTrigger.Cooperation, false, EPhraseTrigger.Roger);
                if (ally.IsYourPlayer)
                {
                    NotificationManager.DisplayMessageNotification($"{pact.Name} is waking you up...");
                }
                else
                {
                    Coop.Send(CoopMessage.Reviving, ally.ProfileId, pact.Bot.ProfileId, new[] { 1f }, pact.Name);
                }
                return;
            }
            if (Time.time - pact.ReviveStart < ReviveSeconds)
            {
                return;
            }
            pact.ReviveStart = 0f;
            Plugin.LogSource.LogInfo($"[PMC Karma] {pact.Name} woke {ally.Profile.Nickname} up.");
            if (ally.IsYourPlayer)
            {
                Knockout.Active?.Revive(false, pact.Name);
            }
            else
            {
                Coop.Send(CoopMessage.Revive, ally.ProfileId, pact.Bot.ProfileId, new[] { 0f }, pact.Name);
            }
        }

        private static bool AtExtract(Pact pact)
        {
            Player ally = pact.Ally;
            if (ally == null || (ally.Position - pact.Bot.Position).sqrMagnitude > 25f * 25f)
            {
                return false;
            }
            ExfiltrationPoint[] points = ExfiltrationController.Instance?.ExfiltrationPoints;
            if (points == null)
            {
                return false;
            }
            foreach (ExfiltrationPoint point in points)
            {
                if (point == null || point.Status == EExfiltrationStatus.NotPresent || point.Status == EExfiltrationStatus.Hidden)
                {
                    continue;
                }
                if ((point.transform.position - ally.Position).sqrMagnitude < 16f * 16f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
