using BepInEx.Configuration;
using EFT;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace mizmii.ReClassic.Patches
{
    internal class NoContainerInventoryPatch : ModulePatch
    {
        private static ConfigEntry<bool> _enabled;

        public static void Bind(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Interaction", "Open Containers Without Searching", true, "Closed containers offer Open instead of Search, so opening one only swings the lid or door open instead of jumping straight into the loot window.", "Containers", "Enabled");
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionContextHelper), nameof(InteractionContextHelper.GetAvailableActions),
                new[] { typeof(GamePlayerOwner), typeof(LootableContainer) });
        }

        [PatchPostfix]
        private static void Postfix(AvailableInteractionState __result, GamePlayerOwner owner, LootableContainer container)
        {
            if (!_enabled.Value || __result?.Actions == null || container.DoorState != EDoorState.Shut)
            {
                return;
            }

            var actions = __result.Actions;
            int searchIndex = actions.FindIndex(a => a.Name == "Search");
            if (searchIndex >= 0)
            {
                actions[searchIndex] = new InteractionAction
                {
                    Name = "OpenDoor",
                    Disabled = actions[searchIndex].Disabled,
                    Action = () => Open(owner, container)
                };
            }
        }

        private static void Open(GamePlayerOwner owner, LootableContainer container)
        {
            owner.Player.SaveInteractionRayInfo();
            owner.Player.ExecuteInteraction(container, new InteractionResult(EInteractionType.Open));
        }
    }
}
