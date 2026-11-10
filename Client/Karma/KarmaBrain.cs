using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace mizmii.ReClassic.Karma
{
    internal static class KarmaBrain
    {
        public static void Register()
        {
            var brains = new List<string> { "PmcUsec", "PmcBear", "PMC", "Assault", "CursAssault", "ExUsec", "Knight", "BigPipe", "BirdEye", "BossKilla", "Obdolbs" };
            var roles = new List<WildSpawnType> { WildSpawnType.pmcUSEC, WildSpawnType.pmcBEAR };
            BrainManager.AddCustomLayer(typeof(PactLayer), brains, 26, roles);
        }

        public static bool InCombat(BotOwner owner)
        {
            if (owner?.Memory == null)
            {
                return false;
            }
            if (owner.Memory.IsUnderFire)
            {
                return true;
            }
            EnemyInfo goal = owner.Memory.GoalEnemy;
            return goal != null && (goal.IsVisible || Time.time - goal.PersonalLastSeenTime < 6f);
        }
    }

    internal sealed class PactLayer : CustomLayer
    {
        public PactLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "ReClassicPact";
        }

        public override bool IsActive()
        {
            if (Fleeing.FlightOf(BotOwner) != null)
            {
                return true;
            }
            BrainMode mode = HelpRequests.Mode(BotOwner);
            return mode != BrainMode.None && !KarmaBrain.InCombat(BotOwner);
        }

        private Type Wanted()
        {
            if (Fleeing.FlightOf(BotOwner) != null)
            {
                return typeof(PactFleeLogic);
            }
            return HelpRequests.Mode(BotOwner) == BrainMode.Follow ? typeof(PactFollowLogic) : typeof(PactWaitLogic);
        }

        public override Action GetNextAction()
        {
            Type wanted = Wanted();
            string reason = wanted == typeof(PactFleeLogic) ? "Running away" : wanted == typeof(PactFollowLogic) ? "Following an ally" : "Waiting for help";
            return new Action(wanted, reason);
        }

        public override bool IsCurrentActionEnding()
        {
            return CurrentAction == null || CurrentAction.Type != Wanted();
        }
    }

    internal sealed class PactWaitLogic : CustomLogic
    {
        public PactWaitLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            BotOwner.StopMove();
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            BotOwner.SetPose(1f);
            Player target = HelpRequests.Target(BotOwner);
            if (target != null)
            {
                BotOwner.Steering.LookToPoint(target.Position + Vector3.up * 1.4f);
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("Waiting near a human");
        }
    }

    internal sealed class PactFollowLogic : CustomLogic
    {
        private float _nextPath;

        public PactFollowLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            _nextPath = 0f;
        }

        public override void Stop()
        {
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            Player target = HelpRequests.Target(BotOwner);
            if (target == null)
            {
                BotOwner.StopMove();
                return;
            }
            Vector3 position = target.Position;
            float distance = (position - BotOwner.Position).magnitude;
            bool down = HelpRequests.IsAllyDown(target);
            bool crouch = down || target.MovementContext.PoseLevel < 0.5f;
            BotOwner.SetPose(crouch && (!down || distance < 3f) ? 0.3f : 1f);
            if (distance < (down ? 1.4f : 4f))
            {
                BotOwner.StopMove();
                BotOwner.Sprint(false);
                BotOwner.Steering.LookToPoint(position + Vector3.up * 1.4f);
                return;
            }
            bool sprint = (down || !crouch) && distance > (down ? 6f : 14f) && BotOwner.CanSprintPlayer;
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Sprint(sprint);
            if (Time.time >= _nextPath)
            {
                _nextPath = Time.time + (distance > 20f ? 1f : 0.5f);
                Vector3 behind = position - (position - BotOwner.Position).normalized * (down ? 0.9f : 2.5f);
                if (BotOwner.GoToPoint(behind, true, 1.5f) != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    BotOwner.GoToPoint(position, true, 2.5f, false, false);
                }
            }
            BotOwner.Steering.LookToMovingDirection();
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("Following an ally");
        }
    }

    internal sealed class PactFleeLogic : CustomLogic
    {
        public PactFleeLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            Flight flight = Fleeing.FlightOf(BotOwner);
            if (flight != null)
            {
                flight.NextPlan = 0f;
            }
        }

        public override void Stop()
        {
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            Flight flight = Fleeing.FlightOf(BotOwner);
            if (flight == null)
            {
                BotOwner.StopMove();
                return;
            }
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Sprint(BotOwner.CanSprintPlayer);
            bool arrived = flight.HasDestination && (flight.Destination - BotOwner.Position).sqrMagnitude < 3f * 3f;
            if (Time.time >= flight.NextPlan || arrived)
            {
                flight.NextPlan = Time.time + 3f;
                if ((arrived || !flight.HasDestination) && !Fleeing.Plan(flight))
                {
                    Fleeing.End(flight);
                    return;
                }
                if (BotOwner.GoToPoint(flight.Destination, false, 2f) != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    if (!Fleeing.Plan(flight) || BotOwner.GoToPoint(flight.Destination, false, 2f) != UnityEngine.AI.NavMeshPathStatus.PathComplete)
                    {
                        Fleeing.End(flight);
                        return;
                    }
                }
            }
            BotOwner.Steering.LookToMovingDirection();
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("Running away");
        }
    }
}
