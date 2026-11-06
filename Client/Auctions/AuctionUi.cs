using EFT.UI;
using EFT.UI.Ragfair;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace mizmii.ReClassic.Auctions
{
    internal static class AuctionUi
    {
        public static readonly Color TextColor = new Color(0.77f, 0.76f, 0.7f, 1f);
        public static readonly Color MutedColor = new Color(0.55f, 0.55f, 0.52f, 1f);
        public static readonly Color PriceColor = new Color(0.86f, 0.8f, 0.55f, 1f);
        public static readonly Color GoodColor = new Color(0.55f, 0.8f, 0.45f, 1f);
        public static readonly Color BadColor = new Color(0.86f, 0.42f, 0.36f, 1f);
        public static readonly Color CardColor = new Color(0.07f, 0.075f, 0.075f, 0.94f);
        public static readonly Color CardHoverColor = new Color(0.11f, 0.115f, 0.11f, 0.96f);
        public static readonly Color IconBackColor = new Color(0.13f, 0.14f, 0.14f, 0.9f);
        public static readonly Color OutlineColor = new Color(0.33f, 0.33f, 0.3f, 0.6f);

        public static TMP_FontAsset Font;
        public static Material FontMaterial;
        public static DefaultUIButton ButtonTemplate;
        public static TMP_InputField InputTemplate;
        public static GameObject WindowTemplate;
        public static Image PanelTemplate;
        public static Image BorderTemplate;
        public static Image CellTemplate;

        public static readonly Color NativeText = new Color32(197, 195, 178, 255);
        public static readonly Color NativePanel = new Color32(80, 80, 80, 65);
        public static readonly Color NativePanelHover = new Color32(80, 80, 80, 85);
        public static readonly Color NativeMinePanel = new Color32(60, 147, 192, 30);
        public static readonly Color NativeMineHover = new Color32(60, 147, 192, 60);
        public static readonly Color NativeMineBorder = new Color32(11, 106, 130, 255);
        public static readonly Color NativeStarPanel = new Color32(192, 160, 40, 34);
        public static readonly Color NativeStarHover = new Color32(192, 160, 40, 64);
        public static readonly Color NativeStarBorder = new Color32(150, 118, 20, 255);
        public static readonly Color NativeCaption = new Color32(140, 140, 132, 255);

        public static Image Skinned(string name, Transform parent, Image template, Color color)
        {
            Image image = Panel(name, parent, color);
            if (template != null && template.sprite != null)
            {
                image.sprite = template.sprite;
                image.type = template.type;
                image.pixelsPerUnitMultiplier = template.pixelsPerUnitMultiplier;
                image.fillCenter = template.fillCenter;
            }
            image.raycastTarget = false;
            return image;
        }

        public static bool Ready => Font != null && ButtonTemplate != null;

        private static Sprite _starSprite;
        private static bool _starSearched;

        public static Sprite StarSprite()
        {
            if (_starSearched)
            {
                return _starSprite;
            }
            _starSearched = true;
            Sprite best = null;
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null)
                {
                    continue;
                }
                string name = sprite.name.ToLowerInvariant();
                if (!name.Contains("star") || name.Contains("start"))
                {
                    continue;
                }
                if (best == null || name.Contains("fav") || name.Length < best.name.Length)
                {
                    best = sprite;
                }
            }
            _starSprite = best;
            Plugin.LogSource.LogInfo(best != null ? $"[Auctions] Star icon: {best.name}" : "[Auctions] No star icon sprite found, using text.");
            return best;
        }


        public static void Capture(RagfairScreen screen, OfferViewList list)
        {
            if (list != null && list._cellViewPrefab is OfferView offerView)
            {
                if (offerView._purchaseButton != null)
                {
                    ButtonTemplate = offerView._purchaseButton;
                }
                if (offerView._backgroundImages != null)
                {
                    foreach (Image image in offerView._backgroundImages)
                    {
                        if (image != null && image.sprite != null)
                        {
                            PanelTemplate = image;
                            break;
                        }
                    }
                }
                if (offerView._borderImage != null && offerView._borderImage.sprite != null)
                {
                    BorderTemplate = offerView._borderImage;
                }
                if (offerView._descriptionExpanded != null && offerView._descriptionExpanded._offerItemView != null)
                {
                    foreach (Image image in offerView._descriptionExpanded._offerItemView.GetComponentsInChildren<Image>(true))
                    {
                        if (image != null && image.sprite != null && image.name.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            CellTemplate = image;
                            break;
                        }
                    }
                }
                TextMeshProUGUI label = offerView._descriptionExpanded != null ? offerView._descriptionExpanded._offerItemName : null;
                if (label != null)
                {
                    Font = label.font;
                    FontMaterial = label.fontSharedMaterial;
                }
            }
            if (Font == null && screen._myRagfairRating != null)
            {
                Font = screen._myRagfairRating.font;
                FontMaterial = screen._myRagfairRating.fontSharedMaterial;
            }
            if (screen._renewOfferWindow != null)
            {
                WindowTemplate = screen._renewOfferWindow.gameObject;
            }
            ItemUiContext context = ItemUiContext.Instance;
            if (InputTemplate == null && context != null && context._addOfferWindow != null)
            {
                RequirementView[] views = context._addOfferWindow._requirementViews;
                if (views != null && views.Length > 0 && views[0] != null)
                {
                    InputTemplate = views[0]._inputField;
                }
            }
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        public static void Stretch(RectTransform rect, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        public static Image Panel(string name, Transform parent, Color color, bool outline = false)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (outline)
            {
                Outline line = rect.gameObject.AddComponent<Outline>();
                line.effectColor = OutlineColor;
                line.effectDistance = new Vector2(1f, -1f);
            }
            return image;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            RectTransform rect = Rect(name, parent);
            TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font != null)
            {
                label.font = Font;
                if (FontMaterial != null)
                {
                    label.fontSharedMaterial = FontMaterial;
                }
            }
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            label.richText = true;
            label.text = text;
            return label;
        }

        public static DefaultUIButton Button(string name, Transform parent, string caption, UnityAction onClick)
        {
            DefaultUIButton button = Object.Instantiate(ButtonTemplate, parent, false);
            button.name = name;
            button.gameObject.SetActive(true);
            foreach (HoverTooltipArea tooltip in button.GetComponentsInChildren<HoverTooltipArea>(true))
            {
                Object.Destroy(tooltip);
            }
            button.SetHeaderText(caption);
            button.OnClick.AddListener(onClick);
            button.Interactable = true;
            return button;
        }

        public static void SetButton(DefaultUIButton button, string caption, bool interactable)
        {
            if (button == null)
            {
                return;
            }
            button.SetHeaderText(caption);
            button.Interactable = interactable;
        }

        public static TMP_InputField Input(string name, Transform parent)
        {
            if (InputTemplate == null)
            {
                return null;
            }
            TMP_InputField field = Object.Instantiate(InputTemplate, parent, false);
            field.name = name;
            field.gameObject.SetActive(true);
            field.onValueChanged.RemoveAllListeners();
            field.onEndEdit.RemoveAllListeners();
            field.onSubmit.RemoveAllListeners();
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.characterLimit = 12;
            field.text = string.Empty;
            return field;
        }
    }
}
