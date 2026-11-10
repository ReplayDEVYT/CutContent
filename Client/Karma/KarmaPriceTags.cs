using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Ragfair;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using TMPro;

namespace mizmii.ReClassic.Karma
{
    internal static class KarmaPriceTags
    {
        private const string Marker = "<size=75%><color=";
        private const string Good = "#54c1ff";
        private const string Bad = "#c40000";
        private const string Neutral = "#747b7e";

        private static readonly AccessTools.FieldRef<TradingItemView, ETradeMode> CellMode = AccessTools.FieldRefAccess<TradingItemView, ETradeMode>("_tradeMode");
        private static readonly PropertyInfo CellTrader = AccessTools.Property(typeof(TradingItemView), "Trader");
        private static readonly AccessTools.FieldRef<TraderDealScreen, Trader> DealTrader = AccessTools.FieldRefAccess<TraderDealScreen, Trader>("_trader");
        private static readonly AccessTools.FieldRef<TraderDealScreen, TMP_Text[]> DealSums = AccessTools.FieldRefAccess<TraderDealScreen, TMP_Text[]>("_equivalentSumValue");

        public static void Enable()
        {
            new CellPatch().Enable();
            new DealPatch().Enable();
            new TaxPatch().Enable();
            new QuestExperiencePatch().Enable();
        }

        private static bool Affected(Trader trader, ETradeMode mode, out double multiplier, out bool higherIsGood)
        {
            multiplier = 1;
            higherIsGood = false;
            KarmaInfo info = KarmaClient.Info;
            if (!KarmaClient.Available || trader == null || info == null)
            {
                return false;
            }
            bool fence = trader.Id == KarmaClient.FenceId;
            if (mode == ETradeMode.Purchase)
            {
                multiplier = fence ? info.FencePrice : info.TraderPrice;
                return true;
            }
            if (fence)
            {
                multiplier = info.FenceSell;
                higherIsGood = true;
                return true;
            }
            return false;
        }

        internal static string Color(double multiplier, bool higherIsGood)
        {
            double percent = (multiplier - 1) * 100;
            if (Math.Abs(percent) < 0.5)
            {
                return Neutral;
            }
            return (percent > 0) == higherIsGood ? Good : Bad;
        }

        private static void Tag(TMP_Text text, double multiplier, bool higherIsGood)
        {
            if (text == null || !text.gameObject.activeSelf || string.IsNullOrEmpty(text.text) || text.text.Contains(Marker))
            {
                return;
            }
            text.text += " " + Marker + Color(multiplier, higherIsGood) + ">(" + KarmaClient.Percent(multiplier) + ")</color></size>";
        }

        private static void Tag(TMP_Text text, Trader trader, ETradeMode mode)
        {
            if (Affected(trader, mode, out double multiplier, out bool higherIsGood))
            {
                Tag(text, multiplier, higherIsGood);
            }
        }

        private class CellPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TradingItemView), nameof(TradingItemView.SetPrice));
            }

            [PatchPostfix]
            private static void Postfix(TradingItemView __instance, Trader.ItemPrice? price)
            {
                try
                {
                    if (!price.HasValue || price.Value.Amount < 1)
                    {
                        return;
                    }
                    Tag(__instance._price, CellTrader?.GetValue(__instance) as Trader, CellMode(__instance));
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Karma] Could not tag a price: {e.Message}");
                }
            }
        }

        private class DealPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TraderDealScreen), nameof(TraderDealScreen.OnPriceChanged));
            }

            [PatchPostfix]
            private static void Postfix(TraderDealScreen __instance)
            {
                try
                {
                    TMP_Text[] sums = DealSums(__instance);
                    if (sums == null)
                    {
                        return;
                    }
                    Trader trader = DealTrader(__instance);
                    foreach (TMP_Text sum in sums)
                    {
                        Tag(sum, trader, __instance.TradeMode);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Karma] Could not tag the deal price: {e.Message}");
                }
            }
        }

        private class TaxPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(AddOfferWindow), nameof(AddOfferWindow.SetPrices));
            }

            [PatchPostfix]
            private static void Postfix(AddOfferWindow __instance)
            {
                try
                {
                    if (KarmaClient.Available && KarmaClient.Info != null)
                    {
                        Tag(__instance._taxPrice, KarmaClient.Info.FleaTax, false);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Karma] Could not tag the flea market tax: {e.Message}");
                }
            }
        }

        private class QuestExperiencePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(StatView), nameof(StatView.Show));
            }

            [PatchPostfix]
            private static void Postfix(StatView __instance, QuestReward reward)
            {
                try
                {
                    if (reward == null || reward.type != ERewardType.Experience || reward.value <= 0f || !KarmaClient.Available || KarmaClient.Info == null)
                    {
                        return;
                    }
                    Tag(__instance._value, KarmaClient.Info.QuestExperience, true);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Karma] Could not tag the quest experience: {e.Message}");
                }
            }
        }
    }
}
