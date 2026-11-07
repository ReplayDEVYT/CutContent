using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Repairing;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;

namespace mizmii.ReClassic.Repairs
{
    internal static class RepairTime
    {
        private const string Section = "Gameplay - Repair Time";
        private const string Colorized = "<color=#c5c3b4>{0}</color> {1}";

        private static ConfigEntry<bool> _enabled;

        private static readonly AccessTools.FieldRef<RepairWindow, IRepairStrategy> Strategy = AccessTools.FieldRefAccess<RepairWindow, IRepairStrategy>("_repairStrategy");
        private static readonly AccessTools.FieldRef<RepairWindow, Item> WindowItem = AccessTools.FieldRefAccess<RepairWindow, Item>("_item");
        private static readonly AccessTools.FieldRef<RepairWindow, bool> Busy = AccessTools.FieldRefAccess<RepairWindow, bool>("_unableToRepair");

        public static bool Active => _enabled.Value && RepairClient.Available;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Traders", "Repair Time", true,
                "Trader repairs take time. You hand the item over, the trader keeps it while they work on it, and it comes back by message when the repair is done. Better repairers take longer. Pending repairs are listed on the trader's page. Needs the Re-Classic server mod.",
                Section, "Repair Time");
            new TraderSelectedPatch().Enable();
            new TraderLabelsPatch().Enable();
            new RepairItemPatch().Enable();
            new RepairWindowShowPatch().Enable();
            RepairList.Enable();
            RepairClient.Refresh();
        }

        public static double QualityOf(Trader trader)
        {
            double quality = trader?.Settings?.Repair?.Quality ?? 1f;
            return quality <= 0 ? 1 : quality;
        }

        private static void UpdateLabels(RepairerParametersPanel panel)
        {
            if (!Active || !(panel.CurrentRepairer is Trader trader))
            {
                return;
            }
            double quality = QualityOf(trader);
            if (panel._repairSpeed != null)
            {
                panel._repairSpeed.text = string.Format(Colorized, RepairClient.SpeedName(quality).Localized(), "repair speed".Localized());
            }
            if (panel._repairTime != null)
            {
                float amount = panel.RepairAmount;
                panel._repairTime.text = amount > 0.01f ? RepairClient.Duration(RepairClient.DurationSeconds(amount, quality)) : "0";
            }
        }

        private static void Confirm(RepairWindow window)
        {
            IRepairStrategy strategy = Strategy(window);
            Item item = WindowItem(window);
            if (strategy == null || item == null || !(strategy.CurrentRepairer is Trader trader))
            {
                return;
            }
            float amount = window._repairerParameters.RepairAmount;
            if (amount <= 0f)
            {
                return;
            }
            long seconds = RepairClient.DurationSeconds(amount, QualityOf(trader));
            string name = item.Name.Localized();
            string message = $"Hand your {name} over to {trader.LocalizedName} for repair?\n\nThe repair takes about <b>{RepairClient.Duration(seconds)}</b>. {trader.LocalizedName} keeps the item until it's done, then sends it back to you by message.";
            ItemUiContext context = ItemUiContext.Instance;
            if (context == null)
            {
                return;
            }
            context.ShowMessageWindow(message, () => Launch(window, strategy, item, trader, amount), () => { }, "REPAIR", 0f, true);
        }

        private static async void Launch(RepairWindow window, IRepairStrategy strategy, Item item, Trader trader, float amount)
        {
            try
            {
                await Run(window, strategy, item, trader, amount);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Repair Time] Could not send the item for repair: {e}");
            }
        }

        private static async Task Run(RepairWindow window, IRepairStrategy strategy, Item item, Trader trader, float amount)
        {
            if (Busy(window))
            {
                return;
            }
            Busy(window) = true;
            if (window._repairProcess != null)
            {
                window._repairProcess.SetActive(true);
            }
            float target = window._repairerParameters.RealCurrentDurability + amount;
            float max = strategy.MaxDurability();
            IResult result;
            try
            {
                result = await strategy.RepairItem(amount, null);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Repair Time] The repair failed: {e}");
                result = null;
            }
            if (result == null || !result.Succeed)
            {
                Busy(window) = false;
                if (window != null && window._repairProcess != null)
                {
                    window._repairProcess.SetActive(false);
                }
                return;
            }
            string name = item.Name.Localized();
            var operation = new RepairSendOperation
            {
                itemId = item.Id,
                traderId = trader.Id,
                amount = amount,
                durability = Math.Min(target, max),
                maxDurability = max
            };
            RepairClient.Send(operation, ok =>
            {
                if (window != null)
                {
                    Busy(window) = false;
                    if (window._repairProcess != null)
                    {
                        window._repairProcess.SetActive(false);
                    }
                }
                if (!ok)
                {
                    return;
                }
                Singleton<GUISounds>.Instance?.PlayUISound(EUISoundType.TradeOperationComplete);
                NotificationManager.DisplayMessageNotification($"{trader.LocalizedName} is repairing your {name}. It will be sent back to you in about {RepairClient.Duration(RepairClient.DurationSeconds(amount, QualityOf(trader)))}.");
                if (window != null && window.gameObject.activeInHierarchy)
                {
                    window.Close();
                }
            });
        }

        private class TraderSelectedPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(RepairerParametersPanel), nameof(RepairerParametersPanel.TraderSelected));
            }

            [PatchPostfix]
            private static void Postfix(RepairerParametersPanel __instance)
            {
                UpdateLabels(__instance);
            }
        }

        private class TraderLabelsPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(RepairerParametersPanel), nameof(RepairerParametersPanel.UpdateTraderLabels));
            }

            [PatchPostfix]
            private static void Postfix(RepairerParametersPanel __instance)
            {
                UpdateLabels(__instance);
            }
        }

        private class RepairWindowShowPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(RepairWindow), nameof(RepairWindow.Show));
            }

            [PatchPostfix]
            private static void Postfix()
            {
                RepairClient.Refresh();
            }
        }

        private class RepairItemPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(RepairWindow), nameof(RepairWindow.RepairItem));
            }

            [PatchPrefix]
            private static bool Prefix(RepairWindow __instance, ref Task __result)
            {
                if (!Active || !(Strategy(__instance)?.CurrentRepairer is Trader))
                {
                    return true;
                }
                __result = Task.CompletedTask;
                if (Busy(__instance))
                {
                    return false;
                }
                try
                {
                    Confirm(__instance);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Repair Time] Could not start the repair: {e}");
                }
                return false;
            }
        }
    }
}
