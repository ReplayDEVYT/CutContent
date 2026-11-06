using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using EFT.UI.Screens;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace mizmii.ReClassic.Auctions
{
    internal static class Auctions
    {
        private static ConfigEntry<bool> _enabled;
        private static RagfairScreen _screen;
        private static OfferViewList _list;
        private static FilterRule? _fleaRule;
        private static FilterRule? _auctionRule;
        private static DefaultUIButton _myLotsButton;
        private static DefaultUIButton _myBidsButton;
        private static DefaultUIButton _starredButton;
        private static RagFair _ragfair;

        private static readonly AccessTools.FieldRef<MenuTaskBar, Action<EMenuType, bool>> TaskBarPressed =
            AccessTools.FieldRefAccess<MenuTaskBar, Action<EMenuType, bool>>("OnTaskBarButtonPressed");

        private static readonly AccessTools.FieldRef<RagfairScreen, RagFair> ScreenRagfair =
            AccessTools.FieldRefAccess<RagfairScreen, RagFair>("_ragfair");

        public static bool Enabled => _enabled.Value && AuctionClient.Info != null && AuctionClient.Info.Enabled;

        public static int MinLevel => AuctionClient.Info != null ? AuctionClient.Info.MinLevel : 10;

        public static bool Unlocked => (SPT.Reflection.Utils.PatchConstants.BackEndSession?.Profile?.Info?.Level ?? 0) >= MinLevel;

        private static void UpdateStatus(RagFair ragfair)
        {
            if (ragfair == null)
            {
                return;
            }
            RagFair.ERagFairStatus status = ragfair.Status;
            if (AuctionClient.Active && status == RagFair.ERagFairStatus.LevelCapped && Unlocked)
            {
                status = RagFair.ERagFairStatus.Available;
            }
            else if (!AuctionClient.Active && status == RagFair.ERagFairStatus.Available)
            {
                ragfair.UpdateRagfairStatus();
                return;
            }
            if (ragfair.Status != status)
            {
                ragfair.Status = status;
                ragfair.OnStatusChanged?.Invoke(status);
            }
        }

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Auctions", "Auctions", true,
                "Adds the cut AUCTIONS button next to the flea market. Put items up for auction and bid against AI traders who try to win lots for as little as possible. Needs the Re-Classic server mod.",
                "Auctions", "Enabled");
            _enabled.SettingChanged += (sender, args) =>
            {
                if (!_enabled.Value)
                {
                    AuctionClient.SetActive(false);
                }
            };

            AuctionClient.ModeChanged += OnModeChanged;
            AuctionClient.ViewChanged += () =>
            {
                UpdateViewButtons();
                RefreshList();
            };

            new TaskBarAwakePatch().Enable();
            new GetOffersPatch().Enable();
            new AddOfferPatch().Enable();
            new RagfairShowPatch().Enable();
            new RagfairClosePatch().Enable();
            new ManageOffersPatch().Enable();
            new RagfairStatusPatch().Enable();
        }

        private class RagfairStatusPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(RagFair), nameof(RagFair.UpdateRagfairStatus));
            }

            [PatchPostfix]
            private static void Postfix(RagFair __instance)
            {
                _ragfair = __instance;
                if (AuctionClient.Active)
                {
                    UpdateStatus(__instance);
                }
            }
        }

        public static void PlaceBid(Offer offer, double? entered, Action<bool> done, Action started)
        {
            if (offer == null || !AuctionClient.Lots.TryGetValue(offer.Id, out AuctionLotView lot))
            {
                return;
            }
            if (lot.YouLead)
            {
                EFT.Communications.NotificationManager.DisplayMessageNotification("You are already the highest bidder.");
                return;
            }
            double amount = entered ?? lot.MinNextBid;
            if (amount < lot.MinNextBid)
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification($"Your bid must be at least {AuctionClient.Price(lot.MinNextBid, lot.Currency)}.");
                return;
            }
            string lotId = lot.Id;
            string name = offer.Item != null ? offer.Item.Name.Localized() : offer.Name;
            Action submit = () =>
            {
                started?.Invoke();
                AuctionClient.Bid(lotId, amount, ok =>
                {
                    if (ok)
                    {
                        EFT.Communications.NotificationManager.DisplayMessageNotification("Bid placed.");
                    }
                    done?.Invoke(ok);
                    AuctionClient.RefreshLots(new[] { lotId });
                    AuctionClient.RefreshInfo();
                });
            };
            ItemUiContext context = ItemUiContext.Instance;
            if (context == null)
            {
                submit();
                return;
            }
            context.ShowMessageWindow($"Bid <b>{AuctionClient.Price(amount, lot.Currency)}</b> on {name}?\n\nThe money is taken from your stash now. If you are outbid it is returned to you by message.", submit, () => { }, "PLACE BID");
        }

        public static void RefreshList()
        {
            if (_screen == null || !_screen.isActiveAndEnabled)
            {
                return;
            }
            RagFair ragfair = ScreenRagfair(_screen);
            if (ragfair != null)
            {
                ragfair.ClearCachedInfo();
                ragfair.ForceNextUpdate = true;
            }
            if (_list != null && _list.isActiveAndEnabled)
            {
                _list.UpdateOffersForced();
            }
        }

        private static void OnModeChanged()
        {
            UpdateStatus(_ragfair ?? (_screen != null ? ScreenRagfair(_screen) : null));
            if (!AuctionClient.Active)
            {
                AuctionWindow.CloseAll();
            }
            if (_screen == null || !_screen.isActiveAndEnabled)
            {
                return;
            }
            try
            {
                SwapRules(_screen, AuctionClient.Active);
                ApplyScreen(_screen);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Auctions] Could not switch market: {e}");
            }
        }

        private static FilterRule Fresh(FilterRule rule)
        {
            rule.Page = 0;
            rule.FilterSearchId = string.Empty;
            rule.LinkedSearchId = string.Empty;
            rule.NeededSearchId = string.Empty;
            rule.HandbookId = Singleton<GlobalConfiguration>.Instance.Handbook.defaultCategory;
            return rule;
        }

        private static void SwapRules(RagfairScreen screen, bool toAuctions)
        {
            RagFair ragfair = ScreenRagfair(screen);
            if (ragfair == null)
            {
                return;
            }
            FilterRule current = ragfair.FilterRule;
            FilterRule next;
            if (toAuctions)
            {
                _fleaRule = current;
                next = _auctionRule ?? Fresh(current);
            }
            else
            {
                _auctionRule = current;
                next = _fleaRule ?? Fresh(current);
            }
            next.ViewListType = EViewListType.AllOffers;
            ragfair.CancellableFilters.Clear();
            ragfair.ClearCachedInfo();
            ragfair.ForceNextUpdate = true;
            screen.SelectAllOffersTab();
            ragfair.SetFilterRule(next, true, true, RagFair.ESetFilterSource.FilterWindow);
        }

        private static void BuildViewButtons(RagfairScreen screen)
        {
            if (_myLotsButton != null || screen._addOfferButton == null || screen._wishListToggle == null || screen._myOffersToggle == null)
            {
                return;
            }
            _myLotsButton = ViewButton(screen, screen._wishListToggle.transform, "mine");
            _myBidsButton = ViewButton(screen, screen._myOffersToggle.transform, "bids");
            _starredButton = ViewButton(screen, screen._myOffersToggle.transform, "starred");
            var rect = (RectTransform)_starredButton.transform;
            rect.anchoredPosition += new Vector2(((RectTransform)screen._myOffersToggle.transform).rect.width + 10f, 0f);
        }

        private static DefaultUIButton ViewButton(RagfairScreen screen, Transform spot, string view)
        {
            DefaultUIButton button = Object.Instantiate(screen._addOfferButton, spot.parent, false);
            button.name = "ReClassic View " + view;
            foreach (HoverTooltipArea tooltip in button.GetComponentsInChildren<HoverTooltipArea>(true))
            {
                Object.Destroy(tooltip);
            }
            button.OnClick.RemoveAllListeners();
            button.OnClick.AddListener(() => AuctionClient.SetView(AuctionClient.View == view ? "all" : view));
            var rect = (RectTransform)button.transform;
            var target = (RectTransform)spot;
            rect.anchorMin = target.anchorMin;
            rect.anchorMax = target.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = target.anchoredPosition;
            button.gameObject.SetActive(false);
            return button;
        }

        private static void UpdateViewButtons()
        {
            if (_myLotsButton != null)
            {
                _myLotsButton.SetRawText(AuctionClient.View == "mine" ? "ALL LOTS" : "MY LOTS", 18);
            }
            if (_myBidsButton != null)
            {
                _myBidsButton.SetRawText(AuctionClient.View == "bids" ? "ALL LOTS" : "MY BIDS", 18);
            }
            if (_starredButton != null)
            {
                _starredButton.SetRawText(AuctionClient.View == "starred" ? "ALL LOTS" : "STARRED", 18);
            }
        }


        public static bool OnFleaScreen()
        {
            EftScreenManager manager = EftScreenManager.Instance;
            return manager != null && manager.CurrentScreenController != null && manager.CurrentScreenController.ScreenType == EEftScreenType.FleaMarket;
        }

        private static void ApplyScreen(RagfairScreen screen)
        {
            if (screen == null)
            {
                return;
            }
            bool active = AuctionClient.Active;
            SetVisible(screen._wishListToggle, !active);
            SetVisible(screen._myOffersToggle, !active);
            if (screen._myOffersCount != null)
            {
                screen._myOffersCount.SetActive(!active);
            }
            if (active)
            {
                BuildViewButtons(screen);
                AuctionClient.RefreshInfo();
            }
            SetVisible(_myLotsButton, active);
            SetVisible(_myBidsButton, active);
            SetVisible(_starredButton, active);
            UpdateViewButtons();
            foreach (AuctionList list in screen.GetComponentsInChildren<AuctionList>(true))
            {
                list.Apply();
            }
        }

        private static void SetVisible(Component component, bool visible)
        {
            if (component != null && component.gameObject.activeSelf != visible)
            {
                component.gameObject.SetActive(visible);
            }
        }

        private sealed class AuctionButton : MonoBehaviour
        {
            public MenuTaskBar TaskBar;
            public AnimatedToggle Flea;
            public AnimatedToggle Toggle;
            public GameObject Root;
            private float _infoTimer;
            private bool? _unlocked;

            private void Update()
            {
                if (Toggle == null || Root == null)
                {
                    return;
                }
                if (AuctionClient.Info == null)
                {
                    _infoTimer += Time.unscaledDeltaTime;
                    if (_infoTimer > 10f)
                    {
                        _infoTimer = 0f;
                        AuctionClient.RefreshInfo();
                    }
                }
                bool show = Enabled && !InGameStatus.InRaid && Flea != null && Flea.transform.parent != null && Flea.transform.parent.gameObject.activeSelf;
                if (Root.activeSelf != show)
                {
                    Root.SetActive(show);
                }
                bool selected = show && AuctionClient.Active && OnFleaScreen();
                if (Toggle.isOn != selected)
                {
                    Toggle.ToggleSilent(selected);
                }
                if (selected && Flea.isOn)
                {
                    Flea.ToggleSilent(false);
                }
                bool unlocked = Unlocked;
                if (_unlocked != unlocked)
                {
                    _unlocked = unlocked;
                    foreach (HoverTooltipArea tooltip in Root.GetComponentsInChildren<HoverTooltipArea>(true))
                    {
                        tooltip.SetMessageText(unlocked ? "Bid on lots and put your own items up for auction." : $"Unlocked at character LVL {MinLevel}", true);
                        tooltip.SetUnlockStatus(unlocked);
                    }
                }
                Toggle.interactable = Flea.interactable && unlocked;
            }

            public void Pressed(bool isOn)
            {
                if (!isOn)
                {
                    return;
                }
                bool alreadyOpen = OnFleaScreen();
                AuctionClient.SetActive(true);
                if (alreadyOpen)
                {
                    return;
                }
                if (!Flea.isOn)
                {
                    Flea.isOn = true;
                }
                else
                {
                    TaskBarPressed(TaskBar)?.Invoke(EMenuType.RagFair, true);
                }
            }
        }

        private sealed class FleaClickWatcher : MonoBehaviour, IPointerClickHandler
        {
            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left)
                {
                    AuctionClient.SetActive(false);
                }
            }
        }

        private class TaskBarAwakePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(MenuTaskBar), nameof(MenuTaskBar.Awake));
            }

            [PatchPostfix]
            private static void Postfix(MenuTaskBar __instance)
            {
                try
                {
                    if (!__instance._toggleButtons.TryGetValue(EMenuType.RagFair, out AnimatedToggle flea) || flea == null)
                    {
                        return;
                    }
                    if (flea.GetComponent<FleaClickWatcher>() == null)
                    {
                        flea.gameObject.AddComponent<FleaClickWatcher>();
                    }

                    Transform container = flea.transform.parent;
                    if (container == null || container.parent == null)
                    {
                        return;
                    }

                    GameObject clone = Object.Instantiate(container.gameObject, container.parent, false);
                    clone.name = "ReClassicAuctions";
                    clone.transform.SetSiblingIndex(container.GetSiblingIndex() + 1);
                    foreach (FleaClickWatcher watcher in clone.GetComponentsInChildren<FleaClickWatcher>(true))
                    {
                        Object.DestroyImmediate(watcher);
                    }
                    foreach (LocalizedText text in clone.GetComponentsInChildren<LocalizedText>(true))
                    {
                        text.LocalizationKey = "";
                        text.SetLabelText("AUCTIONS");
                    }
                    foreach (HoverTooltipArea tooltip in clone.GetComponentsInChildren<HoverTooltipArea>(true))
                    {
                        tooltip.SetMessageText("Bid on lots and put your own items up for auction.", true);
                    }

                    AnimatedToggle toggle = clone.GetComponentInChildren<AnimatedToggle>(true);
                    if (toggle == null)
                    {
                        Object.Destroy(clone);
                        return;
                    }
                    ToggleGroup group = clone.GetComponent<ToggleGroup>() ?? clone.AddComponent<ToggleGroup>();
                    group.allowSwitchOff = true;
                    toggle.group = group;
                    toggle.onValueChanged.RemoveAllListeners();
                    toggle.ToggleSilent(false);

                    AuctionButton button = __instance.gameObject.AddComponent<AuctionButton>();
                    button.TaskBar = __instance;
                    button.Flea = flea;
                    button.Toggle = toggle;
                    button.Root = clone;
                    toggle.onValueChanged.AddListener(button.Pressed);
                    clone.SetActive(false);

                    AuctionClient.RefreshInfo();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Auctions] Could not add the AUCTIONS button: {e}");
                }
            }
        }

        private class GetOffersPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(EftClientBackendSession), nameof(EftClientBackendSession.GetOffers), new[]
                {
                    typeof(int), typeof(int), typeof(int), typeof(bool), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
                    typeof(bool), typeof(bool), typeof(int), typeof(bool), typeof(string), typeof(string), typeof(string), typeof(Dictionary<string, int>), typeof(int), typeof(bool)
                });
            }

            [PatchPrefix]
            private static bool Prefix(EftClientBackendSession __instance, ref Task<Result<OffersList>> __result, int page, int limit, int sortType, bool direction, int currency, int priceFrom, int priceTo, int quantityFrom, int quantityTo, int conditionFrom, int conditionTo, bool oneHourExpiration, bool removeBartering, int offerOwnerType, bool onlyFunctional, string handbookId, string linkedSearchId, string neededSearchId, Dictionary<string, int> buildItems, int buildCount, bool updateOfferCount)
            {
                if (!AuctionClient.Active)
                {
                    return true;
                }
                __result = AuctionClient.Find(__instance, page, limit, sortType, direction, currency, priceFrom, priceTo, quantityFrom, quantityTo, conditionFrom, conditionTo, oneHourExpiration, removeBartering, offerOwnerType, onlyFunctional, handbookId, linkedSearchId, neededSearchId, buildItems, buildCount, updateOfferCount);
                return false;
            }
        }

        private class AddOfferPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(EftClientBackendSession), nameof(EftClientBackendSession.RagfairAddOffer), new[] { typeof(bool), typeof(string[]), typeof(BarterTemplate[]), typeof(Callback) });
            }

            [PatchPrefix]
            private static bool Prefix(EftClientBackendSession __instance, bool sellInOnePiece, string[] items, BarterTemplate[] requirements, Callback callback)
            {
                if (!AuctionClient.Active)
                {
                    return true;
                }
                BarterTemplate requirement = requirements != null && requirements.Length > 0 ? requirements[0] : null;
                var operation = new AddOperation
                {
                    items = items,
                    sellInOnePiece = true,
                    currency = requirement != null ? requirement._tpl : AuctionClient.Roubles,
                    startPrice = requirement != null ? requirement.count : 0
                };
                __instance.SendOperationRightNow(operation, result =>
                {
                    callback?.Invoke(result);
                    AuctionClient.RefreshInfo();
                    RefreshList();
                });
                return false;
            }
        }

        private class RagfairShowPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(RagfairScreen), nameof(RagfairScreen.Show), new[] { typeof(Profile), typeof(InventoryController), typeof(CompoundItem[]), typeof(IHealthController), typeof(IEftSession), typeof(RagfairSearch) });
            }

            [PatchPostfix]
            private static void Postfix(RagfairScreen __instance)
            {
                _screen = __instance;
                try
                {
                    if (AuctionClient.Active)
                    {
                        SwapRules(__instance, true);
                    }
                    ApplyScreen(__instance);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Auctions] Could not set up the auction screen: {e}");
                }
            }
        }

        private class RagfairClosePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(RagfairScreen), nameof(RagfairScreen.Close));
            }

            [PatchPrefix]
            private static void Prefix(RagfairScreen __instance)
            {
                AuctionWindow.CloseAll();
                if (!AuctionClient.Active)
                {
                    return;
                }
                RagFair ragfair = ScreenRagfair(__instance);
                if (ragfair != null)
                {
                    _auctionRule = ragfair.FilterRule;
                    if (_fleaRule.HasValue)
                    {
                        ragfair.FilterRule = _fleaRule.Value;
                    }
                    ragfair.CancellableFilters.Clear();
                    ragfair.ClearCachedInfo();
                    ragfair.ForceNextUpdate = true;
                }
                AuctionClient.Active = false;
                AuctionClient.View = "all";
                ApplyScreen(__instance);
            }
        }

        private class ManageOffersPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.DeclaredMethod(typeof(OfferViewList), nameof(OfferViewList.ManageOffers));
            }

            [PatchPostfix]
            private static void Postfix(OfferViewList __instance, OffersWithNodes offers)
            {
                try
                {
                    RagfairScreen screen = __instance.GetComponentInParent<RagfairScreen>();
                    if (screen == null)
                    {
                        return;
                    }
                    _list = __instance;
                    AuctionUi.Capture(screen, __instance);
                    AuctionList list = AuctionList.Attach(__instance);
                    list.Track(offers);
                    list.Apply();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Auctions] Could not prepare the auction screen: {e}");
                }
            }
        }
    }
}
