using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Ragfair;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace mizmii.ReClassic.Auctions
{
    internal sealed class AuctionWindow : MonoBehaviour
    {
        private const float Width = 600f;
        private const float Height = 540f;

        private static AuctionWindow _instance;

        private Offer _offer;
        private Image _icon;
        private ItemIcon _itemIcon;
        private Action _unsubscribeIcon;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _details;
        private TextMeshProUGUI _history;
        private TextMeshProUGUI _hint;
        private TMP_InputField _input;
        private DefaultUIButton _bidButton;
        private DefaultUIButton _minButton;
        private DefaultUIButton _extendButton;
        private DefaultUIButton _cancelButton;
        private float _timer;
        private bool _busy;

        public static void Open(Offer offer)
        {
            if (_instance == null)
            {
                _instance = Create();
            }
            if (_instance == null)
            {
                return;
            }
            _instance.Show(offer);
        }

        public static void CloseAll()
        {
            if (_instance != null)
            {
                _instance.Close();
            }
        }

        private static AuctionWindow Create()
        {
            GameObject template = AuctionUi.WindowTemplate;
            if (template == null || !AuctionUi.Ready)
            {
                return null;
            }
            GameObject root = Object.Instantiate(template, template.transform.parent, false);
            root.name = "ReClassic Auction Window";
            foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
            {
                if (behaviour is RenewOfferWindow || behaviour is UIElement)
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
            Transform inner = root.transform.Find("Inner");
            if (inner == null)
            {
                Object.Destroy(root);
                return null;
            }
            for (int i = inner.childCount - 1; i >= 0; i--)
            {
                Transform child = inner.GetChild(i);
                if (child.name != "Caption Panel" && child.name != "Border")
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
            foreach (LocalizedText localized in root.GetComponentsInChildren<LocalizedText>(true))
            {
                Object.DestroyImmediate(localized);
            }

            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(Width, Height);
            var innerRect = (RectTransform)inner;
            innerRect.sizeDelta = new Vector2(Width, Height);
            innerRect.anchoredPosition = new Vector2(Width / 2f, -Height / 2f);

            Image blocker = AuctionUi.Panel("Blocker", root.transform, new Color(0f, 0f, 0f, 0.45f));
            blocker.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            blocker.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            blocker.rectTransform.sizeDelta = new Vector2(10000f, 10000f);
            blocker.transform.SetAsFirstSibling();

            AuctionWindow window = root.AddComponent<AuctionWindow>();
            blocker.gameObject.AddComponent<Button>().onClick.AddListener(window.Close);

            Transform caption = inner.Find("Caption Panel");
            if (caption != null)
            {
                foreach (TextMeshProUGUI label in caption.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    label.text = "AUCTION";
                }
                foreach (Button button in caption.GetComponentsInChildren<Button>(true))
                {
                    button.onClick.RemoveAllListeners();
                    button.onClick.AddListener(window.Close);
                }
            }
            window.BuildContent(inner);
            root.SetActive(false);
            return window;
        }

        private void BuildContent(Transform inner)
        {
            Image iconBack = AuctionUi.Panel("IconBack", inner, AuctionUi.IconBackColor, true);
            AuctionUi.Place(iconBack.rectTransform, 20f, 36f, 170f, 150f);
            _icon = AuctionUi.Panel("Icon", iconBack.transform, Color.white);
            AuctionUi.Stretch(_icon.rectTransform, 8f, 8f, 8f, 8f);
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;

            _name = AuctionUi.Text("Name", inner, "", 18f, AuctionUi.TextColor);
            AuctionUi.Place(_name.rectTransform, 205f, 34f, 375f, 46f);

            _details = AuctionUi.Text("Details", inner, "", 13.5f, AuctionUi.TextColor);
            AuctionUi.Place(_details.rectTransform, 205f, 82f, 375f, 110f);
            _details.overflowMode = TextOverflowModes.Truncate;

            TextMeshProUGUI historyCaption = AuctionUi.Text("HistoryCaption", inner, "BID HISTORY", 12f, AuctionUi.MutedColor);
            AuctionUi.Place(historyCaption.rectTransform, 20f, 198f, 560f, 18f);

            Image historyBack = AuctionUi.Panel("History", inner, new Color(0f, 0f, 0f, 0.45f), true);
            AuctionUi.Place(historyBack.rectTransform, 20f, 218f, 560f, 196f);
            _history = AuctionUi.Text("Lines", historyBack.transform, "", 13f, AuctionUi.TextColor);
            AuctionUi.Stretch(_history.rectTransform, 10f, 8f, 10f, 8f);
            _history.overflowMode = TextOverflowModes.Truncate;
            _history.enableWordWrapping = false;

            _hint = AuctionUi.Text("Hint", inner, "", 12.5f, AuctionUi.MutedColor);
            AuctionUi.Place(_hint.rectTransform, 20f, 422f, 560f, 34f);

            _input = AuctionUi.Input("Amount", inner);
            if (_input != null)
            {
                var rect = (RectTransform)_input.transform;
                AuctionUi.Place(rect, 20f, 462f, 220f, 32f);
                _input.onSubmit.AddListener(_ => PlaceBid());
            }

            _minButton = AuctionUi.Button("Min", inner, "MIN", FillMinimum);
            Anchor(_minButton, 252f, 466f);
            _bidButton = AuctionUi.Button("Bid", inner, "PLACE BID", PlaceBid);
            Anchor(_bidButton, 340f, 466f);
            _extendButton = AuctionUi.Button("Extend", inner, "EXTEND", Extend);
            Anchor(_extendButton, 20f, 466f);
            _cancelButton = AuctionUi.Button("Cancel", inner, "CANCEL AUCTION", CancelLot);
            Anchor(_cancelButton, 250f, 466f);
        }

        private static void Anchor(DefaultUIButton button, float x, float y)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private void Show(Offer offer)
        {
            _offer = offer;
            _unsubscribeIcon?.Invoke();
            _unsubscribeIcon = null;
            _icon.sprite = null;
            _icon.color = new Color(1f, 1f, 1f, 0f);
            if (offer.Item != null)
            {
                _itemIcon = ItemViewFactory.LoadItemIcon(offer.Item);
                _unsubscribeIcon = _itemIcon.Changed.Subscribe(UpdateIcon);
                UpdateIcon();
            }
            if (_input != null)
            {
                _input.text = string.Empty;
            }
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            AuctionClient.LotsUpdated -= Refresh;
            AuctionClient.LotsUpdated += Refresh;
            Refresh();
            AuctionClient.RefreshLots(new[] { offer.Id });
        }

        public void Close()
        {
            AuctionClient.LotsUpdated -= Refresh;
            _unsubscribeIcon?.Invoke();
            _unsubscribeIcon = null;
            _offer = null;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            AuctionClient.LotsUpdated -= Refresh;
            _unsubscribeIcon?.Invoke();
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void UpdateIcon()
        {
            if (_itemIcon == null)
            {
                return;
            }
            _icon.sprite = _itemIcon.Sprite;
            _icon.color = _itemIcon.Sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        private void Update()
        {
            if (_offer == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            _timer += Time.unscaledDeltaTime;
            if (_timer >= 1f)
            {
                _timer = 0f;
                Refresh();
            }
        }

        private AuctionLotView Lot()
        {
            return _offer != null && AuctionClient.Lots.TryGetValue(_offer.Id, out AuctionLotView lot) ? lot : null;
        }

        private void Refresh()
        {
            if (_offer == null)
            {
                return;
            }
            Item item = _offer.Item;
            int stack = item != null ? item.StackObjectsCount : 1;
            string itemName = item != null ? item.Name.Localized() : _offer.Name;
            _name.text = stack > 1 ? $"{itemName} <color=#9a9a8f>x{stack}</color>" : itemName;

            AuctionLotView lot = Lot();
            if (lot == null)
            {
                _details.text = "Loading auction…";
                _history.text = "";
                SetMode(false, false);
                return;
            }

            var details = new StringBuilder();
            details.AppendLine($"<color=#8c8c84>Seller</color>   {lot.SellerName}");
            if (item != null)
            {
                string condition = Condition(item);
                if (!string.IsNullOrEmpty(condition))
                {
                    details.AppendLine($"<color=#8c8c84>Condition</color>   {condition}");
                }
            }
            details.AppendLine($"<color=#8c8c84>Flea value</color>   ~{AuctionClient.Price(lot.MarketValue, lot.Currency)}");
            details.AppendLine($"<color=#8c8c84>Starting bid</color>   {AuctionClient.Price(lot.StartPrice, lot.Currency)}");
            details.AppendLine(lot.BidCount > 0
                ? $"<color=#8c8c84>Current bid</color>   <color=#dbcc8c>{AuctionClient.Price(lot.CurrentBid, lot.Currency)}</color>  ({lot.LeaderName})"
                : "<color=#8c8c84>Current bid</color>   none yet");
            long left = lot.EndTime - AuctionClient.Now;
            details.Append($"<color=#8c8c84>Time left</color>   <color={(left < 600 ? "#db6b5c" : "#c4c2b3")}>{AuctionClient.TimeLeft(lot.EndTime)}</color>");
            _details.text = details.ToString();

            if (lot.Bids.Count == 0)
            {
                _history.text = "<color=#8c8c84>No bids yet.</color>";
            }
            else
            {
                var lines = new StringBuilder();
                foreach (AuctionBidView bid in lot.Bids.Take(11))
                {
                    string ago = Ago(bid.Time);
                    string bidder = bid.IsYou ? "<color=#8ccc73>You</color>" : bid.Name;
                    lines.AppendLine($"{AuctionClient.Price(bid.Amount, lot.Currency),-18}  {bidder}  <color=#8c8c84>{ago}</color>");
                }
                _history.text = lines.ToString();
            }

            bool ended = left <= 0;
            SetMode(lot.IsMine, !ended);
            if (lot.IsMine)
            {
                _hint.text = lot.CanExtend
                    ? $"Extend this auction by {lot.ExtendHours:0.#}h for {AuctionClient.Price(lot.ExtendCost, AuctionClient.Roubles)}. Auctions without bids can be cancelled and the item is sent back to you."
                    : "This auction cannot be extended any further.";
                AuctionUi.SetButton(_extendButton, $"EXTEND +{lot.ExtendHours:0.#}H", lot.CanExtend && !ended && !_busy);
                AuctionUi.SetButton(_cancelButton, "CANCEL AUCTION", lot.CanCancel && !ended && !_busy);
            }
            else
            {
                _hint.text = lot.YouLead
                    ? "You are the highest bidder. If someone outbids you, your money is sent back to you by message."
                    : $"Minimum bid: <color=#dbcc8c>{AuctionClient.Price(lot.MinNextBid, lot.Currency)}</color>. The money is taken when you bid and returned by message if you are outbid.";
                AuctionUi.SetButton(_bidButton, "PLACE BID", !lot.YouLead && !ended && !_busy);
                AuctionUi.SetButton(_minButton, "MIN", !lot.YouLead && !ended && !_busy);
            }
        }

        private void SetMode(bool owner, bool open)
        {
            if (_input != null)
            {
                _input.gameObject.SetActive(!owner);
                _input.interactable = open;
            }
            _bidButton.gameObject.SetActive(!owner);
            _minButton.gameObject.SetActive(!owner);
            _extendButton.gameObject.SetActive(owner);
            _cancelButton.gameObject.SetActive(owner);
        }

        private static string Condition(Item item)
        {
            RepairableComponent repairable = item.GetItemComponent<RepairableComponent>();
            if (repairable != null && repairable.TemplateDurability > 0)
            {
                return $"{repairable.Durability:0}/{repairable.MaxDurability:0} ({repairable.TemplateDurability:0})";
            }
            ResourceComponent resource = item.GetItemComponent<ResourceComponent>();
            if (resource != null && resource.MaxResource > 0)
            {
                return $"{resource.Value:0}/{resource.MaxResource:0}";
            }
            MedKitComponent medKit = item.GetItemComponent<MedKitComponent>();
            if (medKit != null && medKit.MaxHpResource > 0)
            {
                return $"{medKit.HpResource:0}/{medKit.MaxHpResource:0}";
            }
            return null;
        }

        private static string Ago(long time)
        {
            long seconds = Math.Max(0, AuctionClient.Now - time);
            if (seconds < 60)
            {
                return "just now";
            }
            if (seconds < 3600)
            {
                return $"{seconds / 60}m ago";
            }
            return $"{seconds / 3600}h {seconds % 3600 / 60}m ago";
        }

        private void FillMinimum()
        {
            AuctionLotView lot = Lot();
            if (lot != null && _input != null)
            {
                _input.text = Math.Ceiling(lot.MinNextBid).ToString(CultureInfo.InvariantCulture);
            }
        }

        private void PlaceBid()
        {
            AuctionLotView lot = Lot();
            if (lot == null || _busy || lot.IsMine || lot.YouLead)
            {
                return;
            }
            string text = _input != null ? _input.text : string.Empty;
            if (!double.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out double amount) || amount <= 0)
            {
                amount = lot.MinNextBid;
            }
            if (amount < lot.MinNextBid)
            {
                EFT.Communications.NotificationManager.DisplayWarningNotification($"Your bid must be at least {AuctionClient.Price(lot.MinNextBid, lot.Currency)}.");
                return;
            }
            string lotId = lot.Id;
            string message = $"Bid <b>{AuctionClient.Price(amount, lot.Currency)}</b> on {_name.text}?\n\nThe money is taken from your stash now. If you are outbid it is returned to you by message.";
            ItemUiContext context = ItemUiContext.Instance;
            if (context == null)
            {
                Submit(lotId, amount);
                return;
            }
            context.ShowMessageWindow(message, () => Submit(lotId, amount), () => { }, "PLACE BID");
        }

        private void Submit(string lotId, double amount)
        {
            _busy = true;
            Refresh();
            AuctionClient.Bid(lotId, amount, ok =>
            {
                _busy = false;
                if (ok)
                {
                    EFT.Communications.NotificationManager.DisplayMessageNotification("Bid placed.");
                    if (_input != null)
                    {
                        _input.text = string.Empty;
                    }
                }
                AuctionClient.RefreshLots(new[] { lotId });
                AuctionClient.RefreshInfo();
                Auctions.RefreshList();
            });
        }

        private void Extend()
        {
            AuctionLotView lot = Lot();
            if (lot == null || _busy || !lot.CanExtend)
            {
                return;
            }
            string lotId = lot.Id;
            _busy = true;
            Refresh();
            AuctionClient.Extend(lotId, ok =>
            {
                _busy = false;
                AuctionClient.RefreshLots(new[] { lotId });
                Auctions.RefreshList();
            });
        }

        private void CancelLot()
        {
            AuctionLotView lot = Lot();
            if (lot == null || _busy || !lot.CanCancel)
            {
                return;
            }
            string lotId = lot.Id;
            ItemUiContext context = ItemUiContext.Instance;
            Action cancel = () =>
            {
                _busy = true;
                Refresh();
                AuctionClient.Cancel(lotId, ok =>
                {
                    _busy = false;
                    if (ok)
                    {
                        AuctionClient.Lots.Remove(lotId);
                        Close();
                    }
                    AuctionClient.RefreshInfo();
                    Auctions.RefreshList();
                });
            };
            if (context == null)
            {
                cancel();
                return;
            }
            context.ShowMessageWindow("Cancel this auction? The item will be sent back to you by message. The listing fee is not refunded.", cancel, () => { }, "CANCEL AUCTION");
        }
    }
}
