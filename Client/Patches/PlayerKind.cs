using Comfort.Common;
using EFT;
using EFT.Animations;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace mizmii.ReClassic.Patches
{
    internal static class PlayerKind
    {
        private static readonly Dictionary<Type, FieldInfo> ObservedAiFields = new Dictionary<Type, FieldInfo>();
        private static readonly AccessTools.FieldRef<ProceduralWeaponAnimation, Player.FirearmController> FirearmOf =
            AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Player.FirearmController>("_firearmController");
        private static readonly ConditionalWeakTable<ProceduralWeaponAnimation, Player> Owners = new ConditionalWeakTable<ProceduralWeaponAnimation, Player>();

        public static bool IsHuman(Player player)
        {
            if (player == null)
            {
                return false;
            }
            if (player.IsYourPlayer)
            {
                return true;
            }
            if (player.IsAI)
            {
                return false;
            }
            Type type = player.GetType();
            if (!ObservedAiFields.TryGetValue(type, out FieldInfo field))
            {
                field = AccessTools.Field(type, "IsObservedAI");
                ObservedAiFields[type] = field;
            }
            return field == null || !(bool)field.GetValue(player);
        }

        public static Player Owner(ProceduralWeaponAnimation animation)
        {
            if (animation == null)
            {
                return null;
            }
            Player player = FirearmOf(animation)?._player;
            if (player != null)
            {
                return player;
            }
            Player local = Singleton<GameWorld>.Instance?.MainPlayer;
            if (local != null && local.ProceduralWeaponAnimation == animation)
            {
                return local;
            }
            return Owners.GetValue(animation, a => a.GetComponentInParent<Player>());
        }

        public static bool IsHuman(ProceduralWeaponAnimation animation)
        {
            return IsHuman(Owner(animation));
        }

        public static bool IsHuman(Player.FirearmController controller)
        {
            return controller != null && IsHuman(controller._player);
        }

        public static IEnumerable<Player> Humans()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null || world.AllAlivePlayersList == null)
            {
                yield break;
            }
            foreach (Player player in world.AllAlivePlayersList)
            {
                if (IsHuman(player))
                {
                    yield return player;
                }
            }
        }
    }
}
