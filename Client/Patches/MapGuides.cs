#pragma warning disable CS0649
using BepInEx.Configuration;
using EFT.UI;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json;
using SPT.Common.Http;
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

namespace mizmii.ReClassic.Patches
{
    internal static class MapGuides
    {
        private const float MaxImageHeight = 380f;
        private const float FallbackWidth = 600f;

        private sealed class GuidePage
        {
            [JsonProperty("title")] public string Title = "";
            [JsonProperty("text")] public string Text = "";
            [JsonProperty("image")] public string Image = "";
        }

        private sealed class Guide
        {
            [JsonProperty("name")] public string Name = "";
            [JsonProperty("pages")] public List<GuidePage> Pages = new List<GuidePage>();
        }

        private sealed class GuideList
        {
            [JsonProperty("enabled")] public bool Enabled = true;
            [JsonProperty("maps")] public Dictionary<string, Guide> Maps = new Dictionary<string, Guide>();
        }

        private sealed class ImageResponse
        {
            [JsonProperty("name")] public string Name = "";
            [JsonProperty("data")] public string Data = "";
        }

        private static ConfigEntry<bool> _enabled;
        private static GuideList _guides;
        private static float _fetchedAt = -100f;
        private static Task<GuideList> _fetching;
        private static readonly Dictionary<string, Texture2D> Images = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Task<Texture2D>> ImageTasks = new Dictionary<string, Task<Texture2D>>(StringComparer.OrdinalIgnoreCase);

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.VisualsAndAudio, "Interface", "Map Guides", true,
                "Brings back the WATCH INTRO button on the map select screen. It opens a guide for the map with a few pages of info and pictures. The guides are set on the server in config/mapguides.json.",
                "Interface - Map Guides", "Enabled");
            new LocationInfoPatch().Enable();
        }

        private static Task<GuideList> Fetch()
        {
            if (_guides != null && Time.realtimeSinceStartup - _fetchedAt < 15f)
            {
                return Task.FromResult(_guides);
            }
            if (_fetching != null && !_fetching.IsCompleted)
            {
                return _fetching;
            }
            _fetching = FetchNow();
            return _fetching;
        }

        private static async Task<GuideList> FetchNow()
        {
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/mapguides/list", "{}");
                GuideList list = JsonConvert.DeserializeObject<GuideList>(json);
                if (list != null)
                {
                    list.Maps = new Dictionary<string, Guide>(list.Maps ?? new Dictionary<string, Guide>(), StringComparer.OrdinalIgnoreCase);
                    _guides = list;
                    _fetchedAt = Time.realtimeSinceStartup;
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Map Guides] Could not load the map guides from the server: {e.Message}");
            }
            return _guides;
        }

        private static Guide Find(GuideList list, string locationId)
        {
            if (list == null || !list.Enabled || string.IsNullOrEmpty(locationId) || list.Maps == null)
            {
                return null;
            }
            return list.Maps.TryGetValue(locationId, out Guide guide) && guide?.Pages != null && guide.Pages.Count > 0 ? guide : null;
        }

        private static Task<Texture2D> Image(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return Task.FromResult<Texture2D>(null);
            }
            if (Images.TryGetValue(name, out Texture2D cached))
            {
                return Task.FromResult(cached);
            }
            if (!ImageTasks.TryGetValue(name, out Task<Texture2D> task))
            {
                task = LoadImage(name);
                ImageTasks[name] = task;
            }
            return task;
        }

        private static async Task<Texture2D> LoadImage(string name)
        {
            Texture2D texture = null;
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/mapguides/image", JsonConvert.SerializeObject(new { name }));
                ImageResponse response = JsonConvert.DeserializeObject<ImageResponse>(json);
                if (!string.IsNullOrEmpty(response?.Data))
                {
                    texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(Convert.FromBase64String(response.Data)))
                    {
                        texture.wrapMode = TextureWrapMode.Clamp;
                        Object.DontDestroyOnLoad(texture);
                    }
                    else
                    {
                        Object.Destroy(texture);
                        texture = null;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Map Guides] Could not load image {name}: {e.Message}");
            }
            Images[name] = texture;
            ImageTasks.Remove(name);
            return texture;
        }

        private sealed class IntroButton : MonoBehaviour
        {
            public string LocationId;
            public bool Hooked;
        }

        private class LocationInfoPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(LocationInfoPanel), nameof(LocationInfoPanel.Set));
            }

            [PatchPostfix]
            private static void Postfix(LocationInfoPanel __instance, LocationSettings.Location location)
            {
                try
                {
                    GameObject intro = __instance._watchIntro;
                    if (intro == null || location == null || !_enabled.Value)
                    {
                        return;
                    }
                    bool locked = __instance._lockedPanel != null && __instance._lockedPanel.activeSelf;
                    IntroButton marker = intro.GetComponent<IntroButton>() ?? intro.AddComponent<IntroButton>();
                    marker.LocationId = location.Id;
                    Hook(intro, marker);
                    Label(intro);
                    if (locked)
                    {
                        intro.SetActive(false);
                        return;
                    }
                    intro.SetActive(Find(_guides, location.Id) != null);
                    string id = location.Id;
                    Fetch().ContinueWith(task =>
                    {
                        if (intro != null && marker != null && marker.LocationId == id && !locked)
                        {
                            intro.SetActive(Find(task.Result, id) != null);
                        }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Map Guides] Could not set up the guide button: {e}");
                }
            }

            private static void Label(GameObject intro)
            {
                const string text = "OPEN GUIDE";
                DefaultUIButton button = intro.GetComponentInChildren<DefaultUIButton>(true);
                if (button != null)
                {
                    if (button.HeaderText != text)
                    {
                        button.SetRawText(text, button.HeaderSize);
                    }
                    return;
                }
                foreach (TMP_Text label in intro.GetComponentsInChildren<TMP_Text>(true))
                {
                    foreach (LocalizedText localized in label.GetComponents<LocalizedText>())
                    {
                        localized.enabled = false;
                    }
                    label.text = text;
                }
            }

            private static void Hook(GameObject intro, IntroButton marker)
            {
                if (marker.Hooked)
                {
                    return;
                }
                marker.Hooked = true;
                DefaultUIButton button = intro.GetComponentInChildren<DefaultUIButton>(true);
                if (button != null)
                {
                    button.Interactable = true;
                    button.OnClick.AddListener(() => Open(marker.LocationId));
                }
                Button plain = intro.GetComponentInChildren<Button>(true);
                if (plain != null)
                {
                    plain.interactable = true;
                    plain.onClick.AddListener(() => Open(marker.LocationId));
                }

                CanvasGroup group = intro.GetComponent<CanvasGroup>() ?? intro.AddComponent<CanvasGroup>();
                group.interactable = true;
                group.blocksRaycasts = true;
                group.ignoreParentGroups = true;

                RectTransform rect = intro.transform as RectTransform;
                if (rect != null)
                {
                    var area = new GameObject("ReClassic Guide Click Area", typeof(RectTransform));
                    area.transform.SetParent(rect, false);
                    area.transform.SetAsLastSibling();
                    var areaRect = (RectTransform)area.transform;
                    areaRect.anchorMin = Vector2.zero;
                    areaRect.anchorMax = Vector2.one;
                    areaRect.offsetMin = Vector2.zero;
                    areaRect.offsetMax = Vector2.zero;
                    Image hit = area.AddComponent<Image>();
                    hit.color = new Color(0f, 0f, 0f, 0f);
                    hit.raycastTarget = true;
                    LayoutElement ignore = area.AddComponent<LayoutElement>();
                    ignore.ignoreLayout = true;
                    GuideClick click = area.AddComponent<GuideClick>();
                    click.Marker = marker;
                    click.Labels = intro.GetComponentsInChildren<TMP_Text>(true);
                }
            }
        }

        private sealed class GuideClick : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public IntroButton Marker;
            public TMP_Text[] Labels;
            private Color[] _colors;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left && Marker != null)
                {
                    Open(Marker.LocationId);
                }
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Labels == null)
                {
                    return;
                }
                _colors = new Color[Labels.Length];
                for (int i = 0; i < Labels.Length; i++)
                {
                    if (Labels[i] != null)
                    {
                        _colors[i] = Labels[i].color;
                        Labels[i].color = Color.white;
                    }
                }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (Labels == null || _colors == null)
                {
                    return;
                }
                for (int i = 0; i < Labels.Length && i < _colors.Length; i++)
                {
                    if (Labels[i] != null)
                    {
                        Labels[i].color = _colors[i];
                    }
                }
                _colors = null;
            }

            private void OnDisable()
            {
                OnPointerExit(null);
            }
        }

        private static int _openedFrame = -1;

        private static async void Open(string locationId)
        {
            if (_openedFrame == Time.frameCount)
            {
                return;
            }
            _openedFrame = Time.frameCount;
            try
            {
                Guide guide = Find(await Fetch(), locationId);
                if (guide == null)
                {
                    EFT.Communications.NotificationManager.DisplayWarningNotification("There is no guide for this map.");
                    return;
                }
                GuideWindow.Show(guide);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Map Guides] Could not open the guide: {e}");
            }
        }

        private sealed class GuideWindow
        {
            private Guide _guide;
            private int _page;
            private MessageWindow _window;
            private GameObject _imageHolder;
            private RawImage _image;
            private LayoutElement _imageLayout;
            private GameObject _navigation;
            private DefaultUIButton _previous;
            private DefaultUIButton _next;
            private TextMeshProUGUI _counter;
            private VerticalLayoutGroup _layout;
            private bool _closed;
            private int _version;

            public static void Show(Guide guide)
            {
                ItemUiContext context = ItemUiContext.Instance;
                if (context == null)
                {
                    return;
                }
                var window = new GuideWindow { _guide = guide };
                DialogWindowContext dialog = context.ShowMessageWindow(" ", window.Close, null, guide.Name, 0f, true, TextAlignmentOptions.TopLeft);
                dialog.OnDecline += window.Close;
                dialog.OnClose += window.Close;
                window._window = context._messageWindow;
                window.Build();
                window.ShowPage(0);
            }

            private void Build()
            {
                TextMeshProUGUI message = _window?._message;
                if (message == null)
                {
                    return;
                }
                Transform anchor = message.transform;
                while (anchor.parent != null)
                {
                    _layout = anchor.parent.GetComponent<VerticalLayoutGroup>();
                    if (_layout != null)
                    {
                        break;
                    }
                    anchor = anchor.parent;
                }
                if (_layout == null)
                {
                    return;
                }

                _imageHolder = new GameObject("ReClassic Guide Image", typeof(RectTransform));
                _imageHolder.transform.SetParent(_layout.transform, false);
                _imageHolder.transform.SetSiblingIndex(anchor.GetSiblingIndex());
                _image = _imageHolder.AddComponent<RawImage>();
                _image.raycastTarget = false;
                _imageLayout = _imageHolder.AddComponent<LayoutElement>();
                _imageLayout.flexibleWidth = 0f;
                _imageLayout.flexibleHeight = 0f;
                _imageHolder.SetActive(false);

                if (_guide.Pages.Count < 2 || _window._acceptButton == null)
                {
                    return;
                }
                _navigation = new GameObject("ReClassic Guide Pages", typeof(RectTransform));
                _navigation.transform.SetParent(_layout.transform, false);
                _navigation.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
                HorizontalLayoutGroup row = _navigation.AddComponent<HorizontalLayoutGroup>();
                row.childAlignment = TextAnchor.MiddleCenter;
                row.spacing = 16f;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = false;
                row.padding = new RectOffset(0, 0, 8, 4);
                float height = Mathf.Max(32f, ((RectTransform)_window._acceptButton.transform).rect.height);

                _previous = MakeButton("PREVIOUS", height, () => ShowPage(_page - 1));
                _counter = Object.Instantiate(message, _navigation.transform, false);
                _counter.name = "ReClassic Guide Counter";
                _counter.alignment = TextAlignmentOptions.Center;
                _counter.enableWordWrapping = false;
                _counter.fontSize = Mathf.Max(14f, message.fontSize * 0.9f);
                LayoutElement counterLayout = _counter.GetComponent<LayoutElement>() ?? _counter.gameObject.AddComponent<LayoutElement>();
                counterLayout.preferredWidth = 90f;
                counterLayout.flexibleWidth = 0f;
                _next = MakeButton("NEXT", height, () => ShowPage(_page + 1));
                LayoutElement navigationLayout = _navigation.AddComponent<LayoutElement>();
                navigationLayout.minHeight = height + 12f;
            }

            private DefaultUIButton MakeButton(string text, float height, Action onClick)
            {
                GameObject copy = Object.Instantiate(_window._acceptButton.gameObject, _navigation.transform, false);
                copy.name = "ReClassic Guide " + text;
                copy.SetActive(true);
                DefaultUIButton button = copy.GetComponent<DefaultUIButton>();
                button.SetRawText(text, 18);
                button.OnClick.AddListener(() => onClick());
                LayoutElement layout = copy.GetComponent<LayoutElement>() ?? copy.AddComponent<LayoutElement>();
                layout.minWidth = 150f;
                layout.preferredWidth = 150f;
                layout.minHeight = height;
                layout.preferredHeight = height;
                layout.flexibleWidth = 0f;
                return button;
            }

            private void ShowPage(int index)
            {
                if (_closed || _window == null)
                {
                    return;
                }
                _page = Mathf.Clamp(index, 0, _guide.Pages.Count - 1);
                GuidePage page = _guide.Pages[_page];
                int version = ++_version;

                string title = string.IsNullOrWhiteSpace(page.Title) ? string.Empty : "<b><size=120%>" + page.Title.ToUpperInvariant() + "</size></b>\n\n";
                if (_window._message != null)
                {
                    _window._message.text = title + (page.Text ?? string.Empty);
                }
                if (_window.Caption != null)
                {
                    _window.Caption.text = _guide.Name;
                }
                if (_counter != null)
                {
                    _counter.text = $"{_page + 1} / {_guide.Pages.Count}";
                }
                if (_previous != null)
                {
                    _previous.Interactable = _page > 0;
                }
                if (_next != null)
                {
                    _next.Interactable = _page < _guide.Pages.Count - 1;
                }
                SetImage(null);
                Rebuild();

                Image(page.Image).ContinueWith(task =>
                {
                    if (!_closed && version == _version)
                    {
                        SetImage(task.Result);
                        Rebuild();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }

            private void SetImage(Texture2D texture)
            {
                if (_imageHolder == null)
                {
                    return;
                }
                if (texture == null)
                {
                    _imageHolder.SetActive(false);
                    return;
                }
                _image.texture = texture;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_layout.transform);
                float width = ((RectTransform)_layout.transform).rect.width - _layout.padding.horizontal;
                if (width <= 1f)
                {
                    width = FallbackWidth;
                }
                float height = width * texture.height / texture.width;
                if (height > MaxImageHeight)
                {
                    height = MaxImageHeight;
                    width = height * texture.width / texture.height;
                }
                _imageLayout.preferredWidth = width;
                _imageLayout.preferredHeight = height;
                _imageLayout.minHeight = height;
                _imageHolder.SetActive(true);
            }

            private void Rebuild()
            {
                if (_layout != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_layout.transform);
                }
                if (_window != null && _window.WindowTransform != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_window.WindowTransform);
                }
            }

            private void Close()
            {
                if (_closed)
                {
                    return;
                }
                _closed = true;
                if (_imageHolder != null)
                {
                    Object.Destroy(_imageHolder);
                }
                if (_navigation != null)
                {
                    Object.Destroy(_navigation);
                }
            }
        }
    }
}
