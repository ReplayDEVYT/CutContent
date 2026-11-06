using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using EFT.UI.Ragfair;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace mizmii.ReClassic.Auctions
{
    internal sealed class AuctionList : MonoBehaviour
    {
        private const float RowHeight = 112f;
        private const float RefreshInterval = 15f;

        private OfferViewList _list;
        private RectTransform _root;
        private RectTransform _content;
        private ScrollRect _scroll;
        private TextMeshProUGUI _empty;
        private CanvasGroup _nativeList;
        private OffersWithNodes _offers;
        private Action _unsubscribe;
        private readonly List<AuctionLotRow> _rows = new List<AuctionLotRow>();
        private List<string> _shownIds = new List<string>();
        private bool _dirty;
        private float _second;
        private float _refresh;

        public static AuctionList Attach(OfferViewList list)
        {
            AuctionList view = list.GetComponent<AuctionList>() ?? list.gameObject.AddComponent<AuctionList>();
            view._list = list;
            view.Build();
            return view;
        }

        private void Build()
        {
            if (_root != null || _list._scroller == null || !AuctionUi.Ready)
            {
                return;
            }
            var scroller = (RectTransform)_list._scroller.transform;
            _root = AuctionUi.Rect("ReClassic Auction List", scroller.parent);
            _root.anchorMin = scroller.anchorMin;
            _root.anchorMax = scroller.anchorMax;
            _root.pivot = scroller.pivot;
            _root.anchoredPosition = scroller.anchoredPosition;
            _root.sizeDelta = scroller.sizeDelta;
            _root.SetSiblingIndex(scroller.GetSiblingIndex() + 1);
            _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            RectTransform viewport = AuctionUi.Rect("Viewport", _root);
            AuctionUi.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            _content = AuctionUi.Rect("Rows", viewport);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup stack = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.childControlWidth = true;
            stack.childForceExpandWidth = true;
            stack.childControlHeight = false;
            stack.childForceExpandHeight = false;
            stack.spacing = 4f;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;

            _empty = AuctionUi.Text("Empty", viewport, "", 16f, AuctionUi.MutedColor, TextAlignmentOptions.Center);
            AuctionUi.Stretch(_empty.rectTransform, 40f, 40f, 40f, 40f);

            _nativeList = _list._scroller.GetComponent<CanvasGroup>() ?? _list._scroller.gameObject.AddComponent<CanvasGroup>();

            AuctionClient.LotsUpdated += OnLotsUpdated;
        }

        private void OnDestroy()
        {
            AuctionClient.LotsUpdated -= OnLotsUpdated;
            _unsubscribe?.Invoke();
        }

        public void Track(OffersWithNodes offers)
        {
            if (_offers == offers)
            {
                return;
            }
            _unsubscribe?.Invoke();
            _offers = offers;
            _unsubscribe = offers?.ItemsChanged.Subscribe(() => _dirty = true);
            _dirty = true;
        }

        public void Apply()
        {
            if (_root == null)
            {
                return;
            }
            bool active = AuctionClient.Active;
            _root.gameObject.SetActive(active);
            if (_nativeList != null)
            {
                _nativeList.alpha = active ? 0f : 1f;
                _nativeList.blocksRaycasts = !active;
                _nativeList.interactable = !active;
            }
            if (active && _list._notFoundObject != null)
            {
                _list._notFoundObject.SetActive(false);
            }
            _dirty = true;
        }

        private void OnLotsUpdated()
        {
            foreach (AuctionLotRow row in _rows)
            {
                row.UpdateLot();
            }
        }

        private void Update()
        {
            if (_root == null || !AuctionClient.Active)
            {
                return;
            }
            if (_list._notFoundObject != null && _list._notFoundObject.activeSelf)
            {
                _list._notFoundObject.SetActive(false);
            }
            Fit();
            if (_dirty)
            {
                _dirty = false;
                Sync();
            }
            _second += Time.unscaledDeltaTime;
            if (_second >= 1f)
            {
                _second = 0f;
                foreach (AuctionLotRow row in _rows)
                {
                    row.UpdateClock();
                }
            }
            _refresh += Time.unscaledDeltaTime;
            if (_refresh >= RefreshInterval && _shownIds.Count > 0)
            {
                _refresh = 0f;
                AuctionClient.RefreshLots(_shownIds);
            }
        }

        private readonly Vector3[] _corners = new Vector3[4];

        private void Fit()
        {
            var scroller = (RectTransform)_list._scroller.transform;
            RectTransform area = _list._scroller._targetArea != null ? _list._scroller._targetArea : scroller;
            var parent = (RectTransform)_root.parent;
            area.GetWorldCorners(_corners);
            Vector2 min = parent.InverseTransformPoint(_corners[0]);
            Vector2 max = parent.InverseTransformPoint(_corners[2]);
            RectOffset padding = _list._scroller._padding;
            float left = padding != null ? padding.left : 0f;
            float right = padding != null ? padding.right : 0f;
            float top = padding != null ? padding.top : 0f;
            float bottom = padding != null ? padding.bottom : 0f;
            Scrollbar bar = _list._scroller._scrollbar;
            if (bar != null)
            {
                var barRect = (RectTransform)bar.transform;
                barRect.GetWorldCorners(_corners);
                float barLeft = parent.InverseTransformPoint(_corners[0]).x;
                if (barLeft > min.x && barLeft < max.x + 1f)
                {
                    right = Mathf.Max(right, max.x - barLeft + 4f);
                }
            }
            Vector2 position = new Vector2(min.x + left, min.y + bottom);
            Vector2 size = new Vector2(Mathf.Max(0f, max.x - min.x - left - right), Mathf.Max(0f, max.y - min.y - top - bottom));
            if (_root.anchorMin != parent.pivot || _root.anchorMax != parent.pivot)
            {
                _root.anchorMin = parent.pivot;
                _root.anchorMax = parent.pivot;
                _root.pivot = Vector2.zero;
            }
            if ((_root.anchoredPosition - position).sqrMagnitude > 0.25f)
            {
                _root.anchoredPosition = position;
            }
            if ((_root.sizeDelta - size).sqrMagnitude > 0.25f)
            {
                _root.sizeDelta = size;
            }
        }

        private void Sync()
        {
            List<Offer> offers = _offers?.Where(o => o != null && !o.NotAvailable).ToList() ?? new List<Offer>();
            List<string> ids = offers.Select(o => o.Id).ToList();
            if (ids.SequenceEqual(_shownIds))
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    _rows[i].Rebind(offers[i]);
                }
                return;
            }

            bool sameSet = ids.Count == _shownIds.Count && !ids.Except(_shownIds).Any();
            _shownIds = ids;
            while (_rows.Count > offers.Count)
            {
                AuctionLotRow last = _rows[_rows.Count - 1];
                _rows.RemoveAt(_rows.Count - 1);
                Destroy(last.gameObject);
            }
            for (int i = 0; i < offers.Count; i++)
            {
                if (i >= _rows.Count)
                {
                    _rows.Add(AuctionLotRow.Create(_content, RowHeight));
                }
                _rows[i].Show(offers[i]);
            }
            _empty.text = offers.Count > 0 ? "" : AuctionClient.View == "mine"
                ? "You have no active auctions. Use ADD OFFER to put an item up for auction."
                : AuctionClient.View == "bids"
                    ? "You have not bid on any active auctions."
                    : AuctionClient.View == "starred"
                        ? "You have not starred any auctions."
                        : "No auctions in this category right now.";
            if (!sameSet)
            {
                _scroll.verticalNormalizedPosition = 1f;
            }
            _refresh = 0f;
            AuctionClient.RefreshLots(ids);
        }
    }

    internal sealed class AuctionLotRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float Pad = 4f;
        private const float CellWidth = 124f;
        private const float BiddersWidth = 250f;
        private const float PriceWidth = 170f;
        private const float ActionWidth = 196f;
        private static readonly Color StarOn = new Color(1f, 0.84f, 0.16f, 1f);
        private static readonly Color StarOff = new Color(0.32f, 0.32f, 0.3f, 1f);

        private Offer _offer;
        private readonly List<Image> _panels = new List<Image>();
        private Image _border;
        private Image _icon;
        private ItemIcon _itemIcon;
        private Action _unsubscribeIcon;
        private TextMeshProUGUI _count;
        private TextMeshProUGUI _name;
        private TextMeshProUGUI _info;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _time;
        private TextMeshProUGUI _bidders;
        private TextMeshProUGUI _priceCaption;
        private TextMeshProUGUI _price;
        private TMP_InputField _input;
        private DefaultUIButton _bid;
        private Image _star;
        private Image _starIcon;
        private TextMeshProUGUI _starText;
        private bool _busy;
        private bool _hover;
        private Color _panelColor = AuctionUi.NativePanel;
        private Color _hoverColor = AuctionUi.NativePanelHover;

        public static AuctionLotRow Create(RectTransform parent, float height)
        {
            RectTransform root = AuctionUi.Rect("Lot", parent);
            root.sizeDelta = new Vector2(0f, height);
            root.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            Image hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0.001f);
            AuctionLotRow row = root.gameObject.AddComponent<AuctionLotRow>();
            row.Build(root);
            return row;
        }

        private Image Block(string name, RectTransform root, float left, float right, bool fromRight, float width)
        {
            Image panel = AuctionUi.Skinned(name, root, AuctionUi.PanelTemplate, AuctionUi.NativePanel);
            RectTransform rect = panel.rectTransform;
            rect.anchorMin = new Vector2(fromRight ? 1f : 0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(fromRight ? -right - width : left, 0f);
            rect.offsetMax = new Vector2(-right, 0f);
            _panels.Add(panel);
            return panel;
        }

        private static TextMeshProUGUI Caption(string name, Transform parent, string text, float y)
        {
            TextMeshProUGUI label = AuctionUi.Text(name, parent, text, 11f, AuctionUi.NativeCaption);
            label.enableWordWrapping = false;
            label.characterSpacing = 2f;
            Top(label.rectTransform, y, 14f, 10f, 10f);
            return label;
        }

        private void Build(RectTransform root)
        {
            const float actionRight = 0f;
            const float priceRight = actionRight + ActionWidth + Pad;
            const float biddersRight = priceRight + PriceWidth + Pad;
            const float infoRight = biddersRight + BiddersWidth + Pad;

            Image cell = Block("ItemCell", root, 0f, 0f, false, 0f);
            cell.rectTransform.anchorMax = new Vector2(0f, 1f);
            cell.rectTransform.offsetMax = new Vector2(CellWidth, 0f);
            Color cellColor = AuctionUi.CellTemplate != null ? AuctionUi.CellTemplate.color : new Color(0f, 0f, 0f, 0.35f);
            Image cellBack = AuctionUi.Skinned("CellBack", cell.transform, AuctionUi.CellTemplate, cellColor);
            AuctionUi.Stretch(cellBack.rectTransform, 6f, 6f, 6f, 6f);
            _icon = AuctionUi.Panel("Icon", cell.transform, Color.white);
            AuctionUi.Stretch(_icon.rectTransform, 12f, 12f, 12f, 12f);
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            _count = AuctionUi.Text("Count", cell.transform, "", 13f, AuctionUi.NativeText, TextAlignmentOptions.BottomRight);
            AuctionUi.Stretch(_count.rectTransform, 8f, 8f, 10f, 8f);

            Image info = Block("Info", root, CellWidth + Pad, infoRight, false, 0f);
            _name = AuctionUi.Text("Name", info.transform, "", 16f, AuctionUi.NativeText);
            Top(_name.rectTransform, 8f, 42f, 10f, 10f);
            _info = AuctionUi.Text("Details", info.transform, "", 12.5f, AuctionUi.NativeCaption);
            _info.enableWordWrapping = false;
            Top(_info.rectTransform, 52f, 18f, 10f, 10f);
            _status = AuctionUi.Text("Status", info.transform, "", 12.5f, AuctionUi.NativeText);
            _status.enableWordWrapping = false;
            _status.characterSpacing = 1f;
            Top(_status.rectTransform, 72f, 18f, 10f, 10f);
            _time = AuctionUi.Text("Time", info.transform, "", 13f, AuctionUi.NativeText, TextAlignmentOptions.BottomRight);
            _time.enableWordWrapping = false;
            AuctionUi.Stretch(_time.rectTransform, 10f, 8f, 10f, 8f);

            Image bidders = Block("Bidders", root, 0f, biddersRight, true, BiddersWidth);
            Caption("BiddersCaption", bidders.transform, "BIDS", 8f);
            _bidders = AuctionUi.Text("Lines", bidders.transform, "", 12.5f, AuctionUi.NativeText);
            AuctionUi.Stretch(_bidders.rectTransform, 10f, 26f, 10f, 6f);
            _bidders.enableWordWrapping = false;
            _bidders.overflowMode = TextOverflowModes.Truncate;

            Image price = Block("Price", root, 0f, priceRight, true, PriceWidth);
            _priceCaption = Caption("Caption", price.transform, "", 8f);
            _priceCaption.alignment = TextAlignmentOptions.Center;
            _price = AuctionUi.Text("Amount", price.transform, "", 22f, AuctionUi.NativeText, TextAlignmentOptions.Center);
            _price.enableWordWrapping = false;
            AuctionUi.Stretch(_price.rectTransform, 8f, 24f, 8f, 8f);

            Image actions = Block("Actions", root, 0f, actionRight, true, ActionWidth);
            Caption("BidCaption", actions.transform, "YOUR BID", 8f);

            _input = AuctionUi.Input("Amount", actions.transform);
            if (_input != null)
            {
                Top((RectTransform)_input.transform, 26f, 30f, 10f, 46f);
                _input.onSubmit.AddListener(_ => Bid());
            }

            _star = AuctionUi.Skinned("Star", actions.transform, AuctionUi.PanelTemplate, new Color(0f, 0f, 0f, 0.45f));
            _star.raycastTarget = true;
            RectTransform starRect = _star.rectTransform;
            starRect.anchorMin = new Vector2(1f, 1f);
            starRect.anchorMax = new Vector2(1f, 1f);
            starRect.pivot = new Vector2(1f, 1f);
            starRect.anchoredPosition = new Vector2(-10f, -26f);
            starRect.sizeDelta = new Vector2(30f, 30f);
            Button starButton = _star.gameObject.AddComponent<Button>();
            starButton.targetGraphic = _star;
            starButton.onClick.AddListener(ToggleStar);
            Sprite starSprite = AuctionUi.StarSprite();
            if (starSprite != null)
            {
                _starIcon = AuctionUi.Panel("Icon", _star.transform, StarOff);
                _starIcon.sprite = starSprite;
                _starIcon.preserveAspect = true;
                _starIcon.raycastTarget = false;
                AuctionUi.Stretch(_starIcon.rectTransform, 5f, 5f, 5f, 5f);
            }
            else
            {
                _starText = AuctionUi.Text("Label", _star.transform, "*", 22f, StarOff, TextAlignmentOptions.Center);
                AuctionUi.Stretch(_starText.rectTransform);
            }

            RectTransform bidSlot = AuctionUi.Rect("BidSlot", actions.transform);
            Top(bidSlot, 64f, 32f, 10f, 10f);
            HorizontalLayoutGroup slotLayout = bidSlot.gameObject.AddComponent<HorizontalLayoutGroup>();
            slotLayout.childControlWidth = true;
            slotLayout.childControlHeight = true;
            slotLayout.childForceExpandWidth = true;
            slotLayout.childForceExpandHeight = true;
            _bid = AuctionUi.Button("Bid", bidSlot, "BID", Bid);

            _border = AuctionUi.Skinned("Border", root, AuctionUi.BorderTemplate, AuctionUi.NativeMineBorder);
            AuctionUi.Stretch(_border.rectTransform);
            if (AuctionUi.BorderTemplate == null)
            {
                _border.color = new Color(0f, 0f, 0f, 0f);
                Outline outline = _border.gameObject.AddComponent<Outline>();
                outline.effectColor = AuctionUi.NativeMineBorder;
                outline.effectDistance = new Vector2(1f, -1f);
            }
            _border.gameObject.SetActive(false);
        }

        private static void Top(RectTransform rect, float y, float height, float left, float right)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -y - height);
            rect.offsetMax = new Vector2(-right, -y);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            Paint();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hover = false;
            Paint();
        }

        private void Paint()
        {
            Color color = _hover ? _hoverColor : _panelColor;
            foreach (Image panel in _panels)
            {
                if (panel.color != color)
                {
                    panel.color = color;
                }
            }
        }

        public void Show(Offer offer)
        {
            bool changed = _offer == null || _offer.Id != offer.Id;
            _offer = offer;
            if (changed)
            {
                if (_input != null)
                {
                    _input.text = string.Empty;
                }
                _unsubscribeIcon?.Invoke();
                _unsubscribeIcon = null;
                _itemIcon = null;
                _icon.sprite = null;
                _icon.color = new Color(1f, 1f, 1f, 0f);
                if (offer.Item != null)
                {
                    _itemIcon = ItemViewFactory.LoadItemIcon(offer.Item);
                    _unsubscribeIcon = _itemIcon.Changed.Subscribe(UpdateIcon);
                    UpdateIcon();
                }
                Item item = offer.Item;
                int stack = item != null ? item.StackObjectsCount : 1;
                string name = item != null ? item.Name.Localized() : offer.Name;
                _name.text = name;
                _count.text = stack > 1 ? stack.ToString(CultureInfo.InvariantCulture) : "";
            }
            UpdateLot();
            UpdateClock();
        }

        public void Rebind(Offer offer)
        {
            _offer = offer;
        }

        private void OnDestroy()
        {
            _unsubscribeIcon?.Invoke();
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

        private AuctionLotView Lot()
        {
            return _offer != null && AuctionClient.Lots.TryGetValue(_offer.Id, out AuctionLotView lot) ? lot : null;
        }

        public void UpdateLot()
        {
            if (_offer == null)
            {
                return;
            }
            AuctionLotView lot = Lot();
            string currency = _offer.Requirements != null && _offer.Requirements.Length > 0 ? _offer.Requirements[0].TemplateId : AuctionClient.Roubles;
            if (lot == null)
            {
                double shown = _offer.Requirements != null && _offer.Requirements.Length > 0 ? _offer.Requirements[0].PreciseCount : 0;
                SetText(_price, AuctionClient.Price(shown, currency));
                SetText(_priceCaption, "STARTING BID");
                SetText(_info, _offer.User != null ? "SELLER  <color=#c5c3b2>" + _offer.User.Nickname + "</color>" : "");
                SetText(_status, "");
                SetText(_bidders, "<color=#8c8c84>Loading…</color>");
                return;
            }

            bool hasBids = lot.BidCount > 0;
            SetText(_price, AuctionClient.Price(hasBids ? lot.CurrentBid : lot.StartPrice, lot.Currency));
            SetText(_priceCaption, hasBids ? "CURRENT BID" : "STARTING BID");

            var info = new StringBuilder();
            info.Append("SELLER  <color=#c5c3b2>").Append(lot.SellerName).Append("</color>");
            if (lot.MarketValue > 0)
            {
                info.Append("     FLEA VALUE  <color=#c5c3b2>~").Append(AuctionClient.Price(lot.MarketValue, lot.Currency)).Append("</color>");
            }
            SetText(_info, info.ToString());
            SetText(_status, lot.IsMine ? "<color=#7fa6d6>YOUR LOT</color>" : lot.YouLead ? "<color=#8ccc73>YOU ARE THE HIGHEST BIDDER</color>" : lot.YouBid ? "<color=#db6b5c>YOU HAVE BEEN OUTBID</color>" : "");

            var bidders = new StringBuilder();
            bidders.Append(hasBids ? $"<color=#8c8c84>BIDDERS ({lot.BidCount})</color>" : "<color=#8c8c84>NO BIDS YET</color>");
            foreach (AuctionBidView bid in lot.Bids.Take(5))
            {
                string name = bid.IsYou ? "<color=#8ccc73>You</color>" : bid.Name;
                bidders.Append('\n').Append(AuctionClient.Price(bid.Amount, lot.Currency)).Append("  ").Append(name);
            }
            if (lot.Bids.Count > 5)
            {
                bidders.Append($"\n<color=#8c8c84>+{lot.Bids.Count - 5} more</color>");
            }
            SetText(_bidders, bidders.ToString());

            _panelColor = lot.Starred ? AuctionUi.NativeStarPanel : lot.IsMine ? AuctionUi.NativeMinePanel : AuctionUi.NativePanel;
            _hoverColor = lot.Starred ? AuctionUi.NativeStarHover : lot.IsMine ? AuctionUi.NativeMineHover : AuctionUi.NativePanelHover;
            Paint();
            bool bordered = lot.Starred || lot.IsMine;
            if (_border.gameObject.activeSelf != bordered)
            {
                _border.gameObject.SetActive(bordered);
            }
            Color borderColor = lot.Starred ? AuctionUi.NativeStarBorder : AuctionUi.NativeMineBorder;
            if (AuctionUi.BorderTemplate != null)
            {
                _border.color = borderColor;
            }
            else if (_border.TryGetComponent(out Outline outline))
            {
                outline.effectColor = borderColor;
            }
            Color star = lot.Starred ? StarOn : StarOff;
            if (_starIcon != null)
            {
                _starIcon.color = star;
            }
            if (_starText != null)
            {
                _starText.color = star;
            }

            bool ended = lot.EndTime <= AuctionClient.Now;
            bool canBid = !lot.IsMine && !lot.YouLead && !ended;
            if (_input != null)
            {
                _input.interactable = canBid && !_busy;
                if (_input.placeholder is TextMeshProUGUI placeholder)
                {
                    SetText(placeholder, lot.IsMine ? "Your lot" : lot.YouLead ? "You are leading" : "Min " + AuctionClient.Price(lot.MinNextBid, lot.Currency));
                }
            }
            string caption = lot.IsMine ? "MANAGE" : "BID";
            if (_bid._text != caption)
            {
                _bid.SetRawText(caption, _bid._fontSize);
            }
            _bid.Interactable = !_busy && !ended && (lot.IsMine || canBid);
        }

        public void UpdateClock()
        {
            if (_offer == null)
            {
                return;
            }
            AuctionLotView lot = Lot();
            long end = lot?.EndTime ?? new DateTimeOffset(DateTime.SpecifyKind(_offer.EndTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long left = end - AuctionClient.Now;
            SetText(_time, "<color=#8c8c84>ENDS IN</color>  " + AuctionClient.TimeLeft(end));
            _time.color = left < 600 ? AuctionUi.BadColor : AuctionUi.NativeText;
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        private void ToggleStar()
        {
            AuctionLotView lot = Lot();
            if (_offer != null)
            {
                AuctionClient.Star(_offer.Id, lot == null || !lot.Starred);
            }
        }

        private void Bid()
        {
            AuctionLotView lot = Lot();
            if (_offer == null || lot == null || _busy)
            {
                return;
            }
            if (lot.IsMine)
            {
                AuctionWindow.Open(_offer);
                return;
            }
            double? amount = null;
            if (_input != null && double.TryParse(_input.text, NumberStyles.Number, CultureInfo.InvariantCulture, out double typed) && typed > 0)
            {
                amount = typed;
            }
            Auctions.PlaceBid(_offer, amount, ok =>
            {
                _busy = false;
                if (ok && _input != null)
                {
                    _input.text = string.Empty;
                }
                UpdateLot();
            }, () =>
            {
                _busy = true;
                UpdateLot();
            });
        }
    }
}
