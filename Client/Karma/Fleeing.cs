using Comfort.Common;
using EFT;
using mizmii.ReClassic.Consciousness;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace mizmii.ReClassic.Karma
{
    internal sealed class Flight
    {
        public Player Bot;
        public Player Threat;
        public float Until;
        public float NextPlan;
        public bool HasDestination;
        public Vector3 Destination;

        public string Name => Bot?.Profile?.Nickname ?? "A PMC";
    }

    internal static class Fleeing
    {
        private const float Duration = 30f;
        private const float GiveUpDistance = 110f;

        private static readonly Dictionary<string, Flight> Flights = new Dictionary<string, Flight>();
        private static readonly HashSet<string> RolledFirst = new HashSet<string>();
        private static readonly HashSet<string> RolledHurt = new HashSet<string>();
        private static readonly Dictionary<string, HashSet<string>> Warned = new Dictionary<string, HashSet<string>>();
        private static readonly Dictionary<string, float> LastShot = new Dictionary<string, float>();
        private static readonly Dictionary<string, Dictionary<string, float>> RunningHits = new Dictionary<string, Dictionary<string, float>>();
        private static readonly float[] Angles = { 0f, 35f, -35f, 70f, -70f, 110f, -110f };
        private static readonly float[] Distances = { 45f, 30f, 18f };

        public static void Reset()
        {
            Flights.Clear();
            RolledFirst.Clear();
            RolledHurt.Clear();
            Warned.Clear();
            LastShot.Clear();
            RunningHits.Clear();
        }

        private static bool Active => PmcKarma.Enabled && PmcKarma.FleeEnabled && HelpRequests.BrainAvailable && Coop.IsServer && Singleton<GameWorld>.Instance != null;

        public static Flight FlightOf(BotOwner owner)
        {
            Player bot = owner?.GetPlayer;
            return bot != null && Flights.TryGetValue(bot.ProfileId, out Flight flight) ? flight : null;
        }

        public static bool IsFleeing(Player bot)
        {
            return bot != null && Flights.ContainsKey(bot.ProfileId);
        }

        public static void OnSound(IPlayer source, AISoundType type)
        {
            if (source != null && source.IsAI && type != AISoundType.step)
            {
                LastShot[source.ProfileId] = Time.time;
            }
        }

        public static bool RunningFrom(Player bot, Player human)
        {
            if (bot == null || human == null || !IsFleeing(bot))
            {
                return false;
            }
            if (LastShot.TryGetValue(bot.ProfileId, out float shot) && Time.time - shot < 2f)
            {
                return false;
            }
            if (bot.HandsController != null && bot.HandsController.IsAiming)
            {
                return false;
            }
            Vector3 away = bot.Position - human.Position;
            away.y = 0f;
            Vector3 velocity = bot.Velocity;
            velocity.y = 0f;
            if (away.sqrMagnitude < 0.01f || velocity.magnitude < 1.2f)
            {
                return false;
            }
            return Vector3.Dot(velocity.normalized, away.normalized) > 0.2f;
        }

        public static void OnHit(Player victim, Player attacker)
        {
            if (!Active || !KarmaTracker.IsPmcBot(victim))
            {
                return;
            }
            if (IsFleeing(victim))
            {
                if (!RunningFrom(victim, attacker))
                {
                    return;
                }
                if (!RunningHits.TryGetValue(victim.ProfileId, out Dictionary<string, float> hits))
                {
                    hits = new Dictionary<string, float>();
                    RunningHits[victim.ProfileId] = hits;
                }
                hits[attacker.ProfileId] = Time.time;
                if (!Warned.TryGetValue(victim.ProfileId, out HashSet<string> warned))
                {
                    warned = new HashSet<string>();
                    Warned[victim.ProfileId] = warned;
                }
                if (warned.Add(attacker.ProfileId))
                {
                    KarmaClient.Notice(attacker, $"{Flights[victim.ProfileId].Name} is running away from you. Killing them while they flee will cost karma.");
                }
                return;
            }
            if (HelpRequests.HasPact(victim) || !KarmaTracker.Timid(victim))
            {
                return;
            }
            bool roll = RolledFirst.Add(victim.ProfileId);
            if (!roll && Health(victim) < 0.5f)
            {
                roll = RolledHurt.Add(victim.ProfileId);
            }
            if (roll && UnityEngine.Random.value < PmcKarma.FleeChance)
            {
                Start(victim, attacker);
            }
        }

        private static float Health(Player bot)
        {
            try
            {
                var common = bot.HealthController.GetBodyPartHealth(EBodyPart.Common);
                return common.Maximum > 0f ? common.Current / common.Maximum : 1f;
            }
            catch (Exception)
            {
                return 1f;
            }
        }

        private static void Start(Player bot, Player threat)
        {
            var flight = new Flight { Bot = bot, Threat = threat, Until = Time.time + Duration };
            if (!Plan(flight))
            {
                return;
            }
            Flights[bot.ProfileId] = flight;
            BotVoice.Say(bot, EPhraseTrigger.NeedHelp, true, EPhraseTrigger.CoverMe, EPhraseTrigger.GetBack);
            Plugin.LogSource.LogInfo($"[PMC Karma] {flight.Name} is running away from {threat.Profile?.Nickname}.");
        }

        public static void End(Flight flight)
        {
            if (flight?.Bot != null)
            {
                Flights.Remove(flight.Bot.ProfileId);
            }
        }

        public static bool KilledWhileFleeing(Player victim, Player killer)
        {
            bool fleeing = false;
            if (RunningHits.TryGetValue(victim.ProfileId, out Dictionary<string, float> hits) && hits.TryGetValue(killer.ProfileId, out float time) && Time.time - time < 2f)
            {
                fleeing = true;
            }
            else if (RunningFrom(victim, killer))
            {
                fleeing = true;
            }
            Flights.Remove(victim.ProfileId);
            return fleeing;
        }

        public static void Tick()
        {
            if (Flights.Count == 0)
            {
                return;
            }
            foreach (Flight flight in Flights.Values.ToList())
            {
                if (!Active || !HelpRequests.Alive(flight.Bot) || !HelpRequests.Alive(flight.Threat) || Time.time > flight.Until
                    || (flight.Threat.Position - flight.Bot.Position).sqrMagnitude > GiveUpDistance * GiveUpDistance)
                {
                    End(flight);
                }
            }
        }

        public static bool Plan(Flight flight)
        {
            Vector3 me = flight.Bot.Position;
            if (NavMesh.SamplePosition(me, out NavMeshHit start, 3f, NavMesh.AllAreas))
            {
                me = start.position;
            }
            Vector3 from = flight.Threat.Position;
            Vector3 away = me - from;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f)
            {
                away = -flight.Bot.Transform.forward;
                away.y = 0f;
            }
            away.Normalize();
            float current = (me - from).sqrMagnitude;
            var path = new NavMeshPath();
            foreach (float distance in Distances)
            {
                foreach (float angle in Angles)
                {
                    Vector3 target = me + Quaternion.Euler(0f, angle, 0f) * away * distance;
                    if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    {
                        continue;
                    }
                    if ((hit.position - from).sqrMagnitude < current + 100f)
                    {
                        continue;
                    }
                    if (!NavMesh.CalculatePath(me, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete || path.corners.Length < 2)
                    {
                        continue;
                    }
                    Vector3 first = path.corners[1] - me;
                    first.y = 0f;
                    if (first.sqrMagnitude > 0.01f && Vector3.Dot(first.normalized, away) < -0.2f)
                    {
                        continue;
                    }
                    flight.Destination = hit.position;
                    flight.HasDestination = true;
                    return true;
                }
            }
            return false;
        }
    }
}
