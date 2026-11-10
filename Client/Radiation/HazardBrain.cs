using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace mizmii.ReClassic.Radiation
{
    internal static class HazardBrain
    {
        public static void Register()
        {
            var brains = new List<string> { "PmcUsec", "PmcBear", "PMC", "Assault", "CursAssault", "ExUsec", "Knight", "BigPipe", "BirdEye", "Obdolbs", "BossKilla", "BossTagilla", "BossBully", "BossGluhar", "BossSanitar", "BossKojaniy", "BossKolontay", "BossBoar", "BossPartisan", "Tagilla", "Killa", "FollowerBully", "FollowerGluharAssault", "FollowerGluharProtect", "FollowerGluharScout", "FollowerSanitar", "FollowerKojaniy", "FollowerBoar", "Fl_Kolontay", "ArenaFighter", "Boar", "Gluhar", "Kojaniy", "SectantPriest", "SectantWarrior" };
            BrainManager.AddCustomLayer(typeof(HazardLayer), brains, 95);
        }
    }

    internal sealed class HazardLayer : CustomLayer
    {
        private const float Margin = 4f;

        public HazardLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "ReClassicHazard";
        }

        public override bool IsActive()
        {
            if (!BotHazards.Avoid.Value || BotOwner?.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }
            return BotHazards.Threatened(BotOwner.GetPlayer, Margin, out _);
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(HazardEscapeLogic), "Leaving a radiation zone");
        }

        public override bool IsCurrentActionEnding()
        {
            return CurrentAction == null || CurrentAction.Type != typeof(HazardEscapeLogic);
        }
    }

    internal sealed class HazardEscapeLogic : CustomLogic
    {
        private float _nextPath;

        public HazardEscapeLogic(BotOwner botOwner) : base(botOwner)
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
            Player player = BotOwner.GetPlayer;
            if (!BotHazards.Threatened(player, 4f, out Zone zone))
            {
                BotOwner.StopMove();
                return;
            }
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.Sprint(BotOwner.CanSprintPlayer);
            if (Time.time >= _nextPath)
            {
                _nextPath = Time.time + 1.5f;
                Vector3 away = player.Position - zone.Center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f)
                {
                    away = player.Transform.forward;
                }
                away.Normalize();
                bool moving = false;
                for (int i = 0; i < 6 && !moving; i++)
                {
                    Vector3 direction = Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * 35f * ((i + 1) / 2), 0f) * away;
                    Vector3 target = zone.Center + direction * (zone.Radius + 8f);
                    target.y = player.Position.y;
                    if (NavMesh.SamplePosition(target, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                    {
                        moving = BotOwner.GoToPoint(hit.position, true, 1.5f) == NavMeshPathStatus.PathComplete;
                    }
                }
            }
            BotOwner.Steering.LookToMovingDirection();
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("Leaving a radiation zone");
        }
    }
}
