using CommonAssets.Scripts.ArtilleryShelling;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using mizmii.ReClassic.Consciousness;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace mizmii.ReClassic.Raid
{
    internal static class Mortars
    {
        private static readonly string[] EffectNames = { "Artillery_mine", "big_round_impact", "Grenade_new2", "Grenade_new" };
        private static ArtilleryProjectileExplosiveItem _shell;
        private static int _mask;

        public static IEnumerator Barrage(float minDelay, float maxDelay)
        {
            while (Singleton<GameWorld>.Instance != null && RaidTimers.Running)
            {
                if (Coop.IsServer)
                {
                    int salvo = UnityEngine.Random.value < 0.3f ? UnityEngine.Random.Range(2, 4) : 1;
                    for (int i = 0; i < salvo; i++)
                    {
                        if (TryPickTarget(out Vector3 point))
                        {
                            Coop.Send(CoopMessage.Mortar, string.Empty, values: new[] { point.x, point.y, point.z });
                            Explode(point);
                        }
                        yield return new WaitForSeconds(UnityEngine.Random.Range(0.4f, 1.2f));
                    }
                }
                yield return new WaitForSeconds(UnityEngine.Random.Range(minDelay, maxDelay));
            }
        }

        private static int Mask()
        {
            if (_mask == 0)
            {
                _mask = LayersMaskController.PlayerStaticCollisionsMask.value | LayersMaskController.TerrainLowPoly.value;
            }
            return _mask;
        }

        private static bool TryPickTarget(out Vector3 point)
        {
            point = default;
            GameWorld world = Singleton<GameWorld>.Instance;
            List<Player> humans = world.AllAlivePlayersList.Where(p => p != null && !p.IsAI && p.HealthController.IsAlive).ToList();
            if (humans.Count == 0)
            {
                return false;
            }
            Player target = humans[UnityEngine.Random.Range(0, humans.Count)];
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(8f, 60f);
                Vector3 above = target.Position + new Vector3(offset.x, 150f, offset.y);
                if (Physics.Raycast(above, Vector3.down, out RaycastHit hit, 400f, Mask(), QueryTriggerInteraction.Ignore))
                {
                    point = hit.point + hit.normal * 0.05f;
                    return true;
                }
            }
            return false;
        }

        public static void Explode(Vector3 point)
        {
            try
            {
                Systems.Effects.Effects effects = Singleton<Systems.Effects.Effects>.Instance;
                if (effects != null)
                {
                    var names = Traverse.Create(effects).Field("_names").GetValue<Dictionary<string, Systems.Effects.Effects.Effect>>();
                    string name = names == null ? EffectNames[0] : EffectNames.FirstOrDefault(names.ContainsKey);
                    if (name != null)
                    {
                        effects.EmitGrenade(name, point, Vector3.up, 1f);
                    }
                }

                ArtilleryProjectileExplosiveItem shell = Shell();
                IBallisticsCalculator calculator = Singleton<GameWorld>.Instance?.SharedBallisticsCalculator;
                if (calculator != null)
                {
                    shell.Explosion(point, null, calculator, null, () => new DamageInfo
                    {
                        DamageType = EDamageType.Artillery,
                        ArmorDamage = shell.ArmorDamage,
                        StaminaBurnRate = shell.StaminaBurnRate,
                        PenetrationPower = shell.PenetrationPower,
                        Direction = Vector3.zero,
                        Player = null,
                        IsForwardHit = true
                    }, 0f, 0f, null, false);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Raid Timers] Mortar shell failed: {e}");
            }
        }

        private static ArtilleryProjectileExplosiveItem Shell()
        {
            if (_shell != null)
            {
                return _shell;
            }
            _shell = new ArtilleryProjectileExplosiveItem();
            try
            {
                GlobalConfiguration.ArtilleryProjectileExplosionParams settings = Singleton<GlobalConfiguration>.Instance?.ArtilleryShelling?.ProjectileExplosionParams;
                if (settings != null)
                {
                    _shell.SetExplosiveItemParams(settings);
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Raid Timers] Using default mortar values: {e.Message}");
            }
            return _shell;
        }
    }
}
