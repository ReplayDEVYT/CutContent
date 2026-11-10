using BepInEx.Configuration;
using EFT.UI;
using EFT.UI.Settings;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace mizmii.ReClassic.Patches
{
    internal static class FeatureSettingsTab
    {
        private const string TabCaption = "Re-Classic";
        private const string RowsPath = "Image/Scroll View/Viewport/Other Settings/";
        private const float WindowWidth = 900f;
        private const float WindowHeight = 760f;
        private const float MenuButtonWidth = 420f;

        private static ConfigEntry<bool> _showAdvanced;

        private static bool ShowAdvanced => _showAdvanced != null && _showAdvanced.Value;

        private static void BindAdvanced()
        {
            ConfigFile config = Config();
            if (_showAdvanced != null || config == null)
            {
                return;
            }
            _showAdvanced = config.Bind("Hidden", "Show Advanced Settings", false, new ConfigDescription("Show advanced settings in the Re-Classic settings windows.", null, new ConfigurationManagerAttributes { Browsable = false }));
        }

        private static bool IsAdvanced(ConfigEntryBase entry)
        {
            object[] tags = entry.Description?.Tags;
            return tags != null && tags.OfType<ConfigurationManagerAttributes>().Any(a => a.IsAdvanced == true);
        }

        private static readonly FeatureCategory[] Categories = (FeatureCategory[])Enum.GetValues(typeof(FeatureCategory));

        private static readonly AccessTools.FieldRef<SettingsScreen, SettingsTab> CurrentTab =
            AccessTools.FieldRefAccess<SettingsScreen, SettingsTab>("_currentTab");

        public static void Enable()
        {
            new AwakePatch().Enable();
            new ShowScreenPatch().Enable();
            new ClosePatch().Enable();
        }

        private sealed class Kit
        {
            public GameObject Toggle;
            public GameObject Dropdown;
            public GameObject Slider;
            public GameObject Separator;
            public GameObject Window;
            public DefaultUIButton Button;
            public TMP_FontAsset Font;
            public Material FontMaterial;
            public Color TextColor = new Color(0.77f, 0.76f, 0.7f, 1f);
        }

        private sealed class Group
        {
            public string Name;
            public readonly List<ConfigEntryBase> Features = new List<ConfigEntryBase>();
            public readonly List<ConfigEntryBase> Options = new List<ConfigEntryBase>();
        }

        private sealed class FeatureTab : MonoBehaviour
        {
            public SettingsScreen Screen;
            public GameObject Panel;
            public Kit Kit;
            public TextMeshProUGUI LockNotice;
            public readonly List<Action> MenuRefreshers = new List<Action>();
            public CategoryWindow Window;

            public void Open()
            {
                SettingsTab current = CurrentTab(Screen);
                if (current != null)
                {
                    current.gameObject.SetActive(false);
                }
                Refresh();
                Panel.SetActive(true);
            }

            public void Hide()
            {
                if (Window != null)
                {
                    Window.Close();
                }
                if (Panel != null && Panel.activeSelf)
                {
                    Panel.SetActive(false);
                }
            }

            public void Refresh()
            {
                foreach (Action refresh in MenuRefreshers)
                {
                    refresh();
                }
                if (LockNotice != null)
                {
                    LockNotice.gameObject.SetActive(Sync.SettingsSync.Locked);
                }
            }

            public void ShowCategory(FeatureCategory category)
            {
                if (Window == null)
                {
                    Window = CategoryWindow.Create(this);
                }
                if (Window != null)
                {
                    Window.Show(category);
                }
            }

            private void OnEnable()
            {
                Sync.SettingsSync.LockChanged += Refresh;
            }

            private void OnDisable()
            {
                Sync.SettingsSync.LockChanged -= Refresh;
            }
        }

        private sealed class CategoryWindow : MonoBehaviour
        {
            private FeatureTab _tab;
            private TextMeshProUGUI _caption;
            private RectTransform _content;
            private ScrollRect _scroll;
            private readonly List<Action> _refreshers = new List<Action>();
            private readonly List<Action> _stateRefreshers = new List<Action>();
            private readonly List<DropDownBox> _dropdowns = new List<DropDownBox>();

            public static CategoryWindow Create(FeatureTab tab)
            {
                GameObject template = tab.Kit.Window;
                if (template == null)
                {
                    return null;
                }
                GameObject root = Object.Instantiate(template, tab.Screen.transform, false);
                root.name = "ReClassic Settings Window";
                foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                {
                    Object.DestroyImmediate(behaviour);
                }
                foreach (LocalizedText localized in root.GetComponentsInChildren<LocalizedText>(true))
                {
                    Object.DestroyImmediate(localized);
                }

                Transform frame = root.transform.Find("Window") ?? root.transform;
                foreach (LayoutGroup layout in frame.GetComponents<LayoutGroup>())
                {
                    Object.DestroyImmediate(layout);
                }
                foreach (ContentSizeFitter fitter in frame.GetComponents<ContentSizeFitter>())
                {
                    Object.DestroyImmediate(fitter);
                }
                foreach (Transform child in Children(frame))
                {
                    if (child.name != "Caption Panel" && child.name != "Background" && child.name != "Border")
                    {
                        Object.DestroyImmediate(child.gameObject);
                    }
                }

                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.offsetMin = Vector2.zero;
                rootRect.offsetMax = Vector2.zero;
                rootRect.SetAsLastSibling();

                var frameRect = (RectTransform)frame;
                frameRect.anchorMin = new Vector2(0.5f, 0.5f);
                frameRect.anchorMax = new Vector2(0.5f, 0.5f);
                frameRect.pivot = new Vector2(0.5f, 0.5f);
                frameRect.anchoredPosition = Vector2.zero;
                frameRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);

                CategoryWindow window = root.AddComponent<CategoryWindow>();
                window._tab = tab;

                Transform caption = frame.Find("Caption Panel");
                if (caption != null)
                {
                    var captionRect = (RectTransform)caption;
                    captionRect.anchorMin = new Vector2(0f, 1f);
                    captionRect.anchorMax = new Vector2(1f, 1f);
                    captionRect.pivot = new Vector2(0.5f, 1f);
                    captionRect.anchoredPosition = new Vector2(0f, -2f);
                    captionRect.sizeDelta = new Vector2(-4f, Mathf.Max(captionRect.sizeDelta.y, 22f));
                    window._caption = caption.GetComponentInChildren<TextMeshProUGUI>(true);
                    foreach (Button button in caption.GetComponentsInChildren<Button>(true))
                    {
                        button.onClick.RemoveAllListeners();
                        button.onClick.AddListener(window.Close);
                    }
                }

                var viewport = new GameObject("Viewport", typeof(RectTransform));
                var viewportRect = (RectTransform)viewport.transform;
                viewportRect.SetParent(frame, false);
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(12f, 12f);
                viewportRect.offsetMax = new Vector2(-12f, -34f);
                viewport.AddComponent<RectMask2D>();
                viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
                if (caption != null)
                {
                    caption.SetAsLastSibling();
                }

                var content = new GameObject("Content", typeof(RectTransform));
                window._content = (RectTransform)content.transform;
                window._content.SetParent(viewportRect, false);
                window._content.anchorMin = new Vector2(0f, 1f);
                window._content.anchorMax = new Vector2(1f, 1f);
                window._content.pivot = new Vector2(0.5f, 1f);
                window._content.anchoredPosition = Vector2.zero;
                window._content.sizeDelta = Vector2.zero;
                VerticalLayoutGroup stack = content.AddComponent<VerticalLayoutGroup>();
                stack.childAlignment = TextAnchor.UpperCenter;
                stack.childControlWidth = false;
                stack.childControlHeight = false;
                stack.childForceExpandWidth = false;
                stack.childForceExpandHeight = false;
                stack.spacing = 6f;
                stack.padding = new RectOffset(0, 0, 8, 16);
                content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                window._scroll = viewport.AddComponent<ScrollRect>();
                window._scroll.viewport = viewportRect;
                window._scroll.content = window._content;
                window._scroll.horizontal = false;
                window._scroll.movementType = ScrollRect.MovementType.Clamped;
                window._scroll.scrollSensitivity = 40f;

                root.SetActive(false);
                return window;
            }

            public void Show(FeatureCategory category)
            {
                Clear();
                if (_caption != null)
                {
                    _caption.text = CategoryName(category);
                }
                foreach (Group group in Groups(category))
                {
                    Header(group.Name);
                    foreach (ConfigEntryBase entry in group.Features)
                    {
                        AddRow(entry, true);
                    }
                    foreach (ConfigEntryBase entry in group.Options)
                    {
                        AddRow(entry, false);
                    }
                }
                Refresh();
                gameObject.SetActive(true);
                transform.SetAsLastSibling();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                _scroll.verticalNormalizedPosition = 1f;
            }

            public void Close()
            {
                foreach (DropDownBox dropdown in _dropdowns)
                {
                    if (dropdown != null)
                    {
                        dropdown.Close();
                    }
                }
                if (gameObject.activeSelf)
                {
                    gameObject.SetActive(false);
                    if (_tab != null)
                    {
                        _tab.Refresh();
                    }
                }
            }

            private void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    Close();
                }
            }

            private void OnEnable()
            {
                Sync.SettingsSync.LockChanged += Refresh;
                ConfigFile config = Config();
                if (config != null)
                {
                    config.SettingChanged += OnSettingChanged;
                }
            }

            private void OnDisable()
            {
                Sync.SettingsSync.LockChanged -= Refresh;
                ConfigFile config = Config();
                if (config != null)
                {
                    config.SettingChanged -= OnSettingChanged;
                }
            }

            private void Refresh()
            {
                foreach (Action refresh in _refreshers)
                {
                    refresh();
                }
            }

            private void Clear()
            {
                foreach (DropDownBox dropdown in _dropdowns)
                {
                    if (dropdown != null)
                    {
                        dropdown.Close();
                    }
                }
                _dropdowns.Clear();
                _refreshers.Clear();
                _stateRefreshers.Clear();
                foreach (Transform child in Children(_content))
                {
                    child.gameObject.SetActive(false);
                    Object.Destroy(child.gameObject);
                }
            }

            private void Header(string name)
            {
                Kit kit = _tab.Kit;
                var holder = new GameObject("Header " + name, typeof(RectTransform));
                var rect = (RectTransform)holder.transform;
                rect.SetParent(_content, false);
                rect.sizeDelta = new Vector2(WindowWidth - 80f, 44f);

                TextMeshProUGUI label = MakeText(kit, rect, name.ToUpperInvariant(), 22f);
                label.alignment = TextAlignmentOptions.BottomLeft;
                var labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(4f, 8f);
                labelRect.offsetMax = Vector2.zero;

                if (kit.Separator != null)
                {
                    GameObject line = Object.Instantiate(kit.Separator, rect, false);
                    line.SetActive(true);
                    var lineRect = (RectTransform)line.transform;
                    lineRect.anchorMin = new Vector2(0f, 0f);
                    lineRect.anchorMax = new Vector2(1f, 0f);
                    lineRect.pivot = new Vector2(0.5f, 0f);
                    lineRect.anchoredPosition = Vector2.zero;
                    lineRect.sizeDelta = new Vector2(0f, Mathf.Max(lineRect.sizeDelta.y, 2f));
                }
            }

            private void AddRow(ConfigEntryBase entry, bool feature)
            {
                GameObject row = null;
                try
                {
                    if (entry is ConfigEntry<bool> toggle)
                    {
                        row = ToggleRow(toggle, feature);
                    }
                    else if (entry.SettingType.IsEnum)
                    {
                        row = DropdownRow(entry);
                    }
                    else if (Range(entry, out _, out _) && IsNumber(entry.SettingType))
                    {
                        row = SliderRow(entry);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[FeatureSettingsTab] Could not draw {entry.Definition.Key}: {e.Message}");
                    if (row != null)
                    {
                        Object.Destroy(row);
                    }
                    row = null;
                }
                if (row != null)
                {
                    Lockable(row, entry);
                }
            }

            private GameObject ToggleRow(ConfigEntry<bool> entry, bool feature)
            {
                GameObject row = Instantiate(_tab.Kit.Toggle, entry);
                TextMeshProUGUI label = row.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    label.richText = true;
                    label.text = ConfigHelper.Label(entry, feature);
                }
                UpdatableToggle toggle = row.GetComponent<UpdatableToggle>() ?? row.GetComponentInChildren<UpdatableToggle>(true);
                toggle.UpdateValue(entry.Value, false);
                toggle.Bind(value =>
                {
                    if (entry.Value != value)
                    {
                        entry.Value = value;
                    }
                });
                _refreshers.Add(() => toggle.UpdateValue(entry.Value, false));
                EventHandler changed = null;
                changed = (sender, args) =>
                {
                    if (toggle == null)
                    {
                        entry.SettingChanged -= changed;
                        return;
                    }
                    toggle.UpdateValue(entry.Value, false);
                };
                entry.SettingChanged += changed;
                return row;
            }

            private GameObject DropdownRow(ConfigEntryBase entry)
            {
                GameObject row = Instantiate(_tab.Kit.Dropdown, entry);
                TextMeshProUGUI label = row.transform.Find("Text")?.GetComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = entry.Definition.Key;
                }
                DropDownBox dropdown = row.GetComponentInChildren<DropDownBox>(true);
                Array values = Enum.GetValues(entry.SettingType);
                dropdown.Show(values.Cast<object>().Select(v => Spaced(v.ToString()).ToUpperInvariant()));
                Action refresh = () => dropdown.UpdateValue(Mathf.Max(Array.IndexOf(values, entry.BoxedValue), 0), false);
                refresh();
                dropdown.OnValueChanged.Subscribe(index =>
                {
                    object value = values.GetValue(index);
                    if (!Equals(entry.BoxedValue, value))
                    {
                        entry.BoxedValue = value;
                    }
                });
                _refreshers.Add(refresh);
                _dropdowns.Add(dropdown);
                return row;
            }

            private GameObject SliderRow(ConfigEntryBase entry)
            {
                Range(entry, out float min, out float max);
                bool whole = entry.SettingType == typeof(int);
                string format = whole ? "0" : max - min <= 2f ? "0.00" : "0.0";

                GameObject row = Instantiate(_tab.Kit.Slider, entry);
                TextMeshProUGUI caption = row.transform.Find("Caption")?.GetComponent<TextMeshProUGUI>();
                if (caption != null)
                {
                    caption.text = entry.Definition.Key;
                }
                NumberSlider slider = row.GetComponentInChildren<NumberSlider>(true);
                slider._slider.minValue = Mathf.Min(min, slider._slider.minValue);
                slider._slider.maxValue = Mathf.Max(max, slider._slider.maxValue);
                slider._slider.minValue = min;
                slider._slider.maxValue = max;
                slider._slider.wholeNumbers = whole;
                slider.Show(min, max, format);
                Action refresh = () => slider.UpdateValue(Convert.ToSingle(entry.BoxedValue, CultureInfo.InvariantCulture), false, min, max);
                refresh();
                slider.Bind(value =>
                {
                    object boxed = whole ? (object)Mathf.RoundToInt(value) : Mathf.Clamp(value, min, max);
                    if (!Equals(entry.BoxedValue, boxed))
                    {
                        entry.BoxedValue = boxed;
                    }
                });
                _refreshers.Add(refresh);
                return row;
            }

            private GameObject Instantiate(GameObject template, ConfigEntryBase entry)
            {
                GameObject row = Object.Instantiate(template, _content, false);
                row.name = "ReClassic " + entry.Definition.Key;
                row.SetActive(true);
                return row;
            }

            private void Lockable(GameObject row, ConfigEntryBase entry)
            {
                string description = entry.Description?.Description ?? string.Empty;
                HoverTooltipArea tooltip = row.GetComponent<HoverTooltipArea>() ?? row.AddComponent<HoverTooltipArea>();
                CanvasGroup group = row.GetComponent<CanvasGroup>() ?? row.AddComponent<CanvasGroup>();
                bool synced = Sync.SettingsSync.IsSynced(entry);
                Action refresh = () =>
                {
                    bool locked = synced && Sync.SettingsSync.IsLocked(entry);
                    ConfigEntry<bool> missing = ConfigHelper.MissingRequirement(entry);
                    ConfigEntry<bool> conflict = ConfigHelper.ActiveConflict(entry);
                    bool blocked = missing != null || conflict != null;
                    group.interactable = !locked && !blocked;
                    group.alpha = locked || blocked ? 0.4f : 1f;
                    string text = description;
                    if (missing != null)
                    {
                        text += "\n\n<color=#9a9a9a>Requires " + missing.Definition.Key + " to be on.</color>";
                    }
                    if (conflict != null)
                    {
                        text += "\n\n<color=#9a9a9a>Does not work while " + conflict.Definition.Key + " is on.</color>";
                    }
                    if (locked)
                    {
                        text += "\n\n<color=#d6b85a>" + Sync.SettingsSync.NoteFor(entry) + "</color>";
                    }
                    tooltip.SetMessageText(text.Trim(), true);
                };
                refresh();
                _refreshers.Add(refresh);
                _stateRefreshers.Add(refresh);
            }

            private void OnSettingChanged(object sender, SettingChangedEventArgs args)
            {
                foreach (Action refresh in _stateRefreshers)
                {
                    refresh();
                }
            }
        }

        private static bool Range(ConfigEntryBase entry, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            object range = entry.Description?.AcceptableValues;
            if (range == null || !range.GetType().IsGenericType || range.GetType().GetGenericTypeDefinition() != typeof(AcceptableValueRange<>))
            {
                return false;
            }
            min = Convert.ToSingle(range.GetType().GetProperty("MinValue").GetValue(range), CultureInfo.InvariantCulture);
            max = Convert.ToSingle(range.GetType().GetProperty("MaxValue").GetValue(range), CultureInfo.InvariantCulture);
            return max > min;
        }

        private static bool IsNumber(Type type)
        {
            return type == typeof(float) || type == typeof(int) || type == typeof(double);
        }

        private static string Spaced(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }
                builder.Append(name[i]);
            }
            return builder.ToString();
        }

        private static ConfigFile Config()
        {
            return ConfigHelper.Settings.Count > 0 ? ConfigHelper.Settings[0].Entry.ConfigFile : null;
        }

        private static List<Group> Groups(FeatureCategory category)
        {
            var groups = new List<Group>();
            var listed = new HashSet<ConfigEntryBase>();
            var sectionOwner = new Dictionary<string, Group>();

            foreach (SettingInfo setting in ConfigHelper.Settings.Where(s => s.Category == category))
            {
                Group group = groups.FirstOrDefault(g => g.Name == setting.Group);
                if (group == null)
                {
                    group = new Group { Name = setting.Group };
                    groups.Add(group);
                }
                if (setting.Entry.Definition.Section == ConfigHelper.MainSection)
                {
                    group.Features.Add(setting.Entry);
                }
                else
                {
                    group.Options.Add(setting.Entry);
                }
                listed.Add(setting.Entry);
            }
            var claimed = new HashSet<string>();
            foreach (SettingInfo setting in ConfigHelper.Settings)
            {
                if (string.IsNullOrEmpty(setting.Section) || !claimed.Add(setting.Section) || setting.Category != category)
                {
                    continue;
                }
                sectionOwner[setting.Section] = groups.First(g => g.Name == setting.Group);
            }
            var foreign = new HashSet<string>(claimed.Where(section => !sectionOwner.ContainsKey(section)));

            ConfigFile config = Config();
            if (config == null)
            {
                return groups;
            }
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
            {
                ConfigEntryBase entry = pair.Value;
                string section = entry.Definition.Section;
                if (listed.Contains(entry) || section == ConfigHelper.MainSection || section == "Hidden" || (!ShowAdvanced && IsAdvanced(entry)))
                {
                    continue;
                }
                Group owner = null;
                string best = null;
                foreach (string claim in claimed)
                {
                    if ((section == claim || section.StartsWith(claim + " - ", StringComparison.Ordinal)) && (best == null || claim.Length > best.Length))
                    {
                        best = claim;
                    }
                }
                if (best != null && !foreign.Contains(best))
                {
                    owner = sectionOwner[best];
                }
                if (owner == null)
                {
                    continue;
                }
                owner.Options.Add(entry);
                listed.Add(entry);
            }
            return groups;
        }

        private static string CategoryName(FeatureCategory category)
        {
            switch (category)
            {
                case FeatureCategory.Gameplay:
                    return "GAMEPLAY";
                case FeatureCategory.Combat:
                    return "COMBAT";
                case FeatureCategory.Environment:
                    return "ENVIRONMENT";
                case FeatureCategory.VisualsAndAudio:
                    return "VISUALS & AUDIO";
                default:
                    return category.ToString().ToUpperInvariant();
            }
        }

        private static string CategoryBlurb(FeatureCategory category)
        {
            switch (category)
            {
                case FeatureCategory.Gameplay:
                    return "Movement, Leg IK, bobbing, interaction focus and auctions";
                case FeatureCategory.Combat:
                    return "Recoil and sway springs, wall push, aim punch and fragments";
                case FeatureCategory.Environment:
                    return "Prop physics, car alarms, car shake and weight tilt";
                case FeatureCategory.VisualsAndAudio:
                    return "Blood on screen, scope glass and reticles, acoustics";
                default:
                    return string.Empty;
            }
        }

        private static TextMeshProUGUI MakeText(Kit kit, Transform parent, string text, float size)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            if (kit.Font != null)
            {
                label.font = kit.Font;
                if (kit.FontMaterial != null)
                {
                    label.fontSharedMaterial = kit.FontMaterial;
                }
            }
            label.fontSize = size;
            label.color = kit.TextColor;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.richText = true;
            label.text = text;
            return label;
        }

        private static Kit CollectKit(SettingsScreen screen)
        {
            Transform game = screen._gameSettingsScreen.transform;
            var holder = new GameObject("ReClassic Templates", typeof(RectTransform));
            holder.SetActive(false);
            holder.transform.SetParent(screen.transform, false);

            var kit = new Kit
            {
                Toggle = Template(game, RowsPath + "TogglesOneColumn/Clear RAM", holder.transform),
                Dropdown = Template(game, RowsPath + "Common/InterfaceLanguage", holder.transform),
                Slider = Template(game, RowsPath + "Scrolls/FOV", holder.transform),
                Separator = Template(game, RowsPath + "Common/Separator", holder.transform),
                Button = screen._defaultButton
            };
            if (kit.Toggle == null || kit.Dropdown == null)
            {
                Object.Destroy(holder);
                return null;
            }
            TextMeshProUGUI sample = kit.Toggle.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (sample != null)
            {
                kit.Font = sample.font;
                kit.FontMaterial = sample.fontSharedMaterial;
                kit.TextColor = sample.color;
            }
            ItemUiContext context = ItemUiContext.Instance;
            if (context != null && context._messageWindow != null)
            {
                kit.Window = context._messageWindow.gameObject;
            }
            return kit;
        }

        private static GameObject Template(Transform game, string path, Transform holder)
        {
            Transform source = game.Find(path);
            if (source == null)
            {
                return null;
            }
            GameObject copy = Object.Instantiate(source.gameObject, holder, false);
            copy.name = source.name;
            copy.SetActive(true);
            foreach (LocalizedText localized in copy.GetComponentsInChildren<LocalizedText>(true))
            {
                Object.DestroyImmediate(localized);
            }
            foreach (HoverTooltipArea tooltip in copy.GetComponentsInChildren<HoverTooltipArea>(true))
            {
                Object.DestroyImmediate(tooltip);
            }
            foreach (HoverTrigger trigger in copy.GetComponentsInChildren<HoverTrigger>(true))
            {
                Object.DestroyImmediate(trigger);
            }
            return copy;
        }

        private static void Build(SettingsScreen screen)
        {
            if (screen.GetComponent<FeatureTab>() != null || ConfigHelper.Settings.Count == 0)
            {
                return;
            }
            BindAdvanced();
            Kit kit = CollectKit(screen);
            if (kit == null || kit.Button == null)
            {
                Plugin.LogSource.LogWarning("[FeatureSettingsTab] The game settings layout changed, the Re-Classic tab was not added.");
                return;
            }

            Transform source = screen._gameSettingsScreen.transform;
            GameObject panel = Object.Instantiate(source.gameObject, source.parent, false);
            panel.name = "ReClassic Settings";
            panel.SetActive(false);
            foreach (MonoBehaviour behaviour in panel.GetComponents<MonoBehaviour>())
            {
                if (behaviour is UIInputNode || behaviour is SettingsTab)
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
            Transform image = panel.transform.Find("Image") ?? panel.transform;
            foreach (Transform child in Children(image))
            {
                Object.DestroyImmediate(child.gameObject);
            }
            panel.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);

            var tab = screen.gameObject.AddComponent<FeatureTab>();
            tab.Screen = screen;
            tab.Panel = panel;
            tab.Kit = kit;
            BuildMenu(tab, image);

            if (!BuildTabButton(screen, tab))
            {
                Object.Destroy(panel);
                Object.Destroy(tab);
            }
        }

        private static void BuildMenu(FeatureTab tab, Transform parent)
        {
            Kit kit = tab.Kit;
            var column = new GameObject("ReClassic Menu", typeof(RectTransform));
            var rect = (RectTransform)column.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(MenuButtonWidth + 200f, 600f);
            VerticalLayoutGroup stack = column.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.MiddleCenter;
            stack.childControlWidth = false;
            stack.childControlHeight = false;
            stack.childForceExpandWidth = false;
            stack.childForceExpandHeight = false;
            stack.spacing = 10f;

            TextMeshProUGUI title = MakeText(kit, rect, "RE-CLASSIC", 34f);
            title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.sizeDelta = new Vector2(MenuButtonWidth + 200f, 46f);

            TextMeshProUGUI subtitle = MakeText(kit, rect, "Restored and cut content from the Tarkov alpha. Pick a category to change its settings.", 15f);
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.color = new Color(kit.TextColor.r, kit.TextColor.g, kit.TextColor.b, 0.7f);
            subtitle.rectTransform.sizeDelta = new Vector2(MenuButtonWidth + 200f, 30f);

            tab.LockNotice = MakeText(kit, rect, "<color=#d6b85a>Settings follow the match host until the match ends. Visuals & Audio stay your own.</color>", 15f);
            tab.LockNotice.alignment = TextAlignmentOptions.Center;
            tab.LockNotice.rectTransform.sizeDelta = new Vector2(MenuButtonWidth + 200f, 24f);
            tab.LockNotice.gameObject.SetActive(false);

            var spacer = new GameObject("Spacer", typeof(RectTransform));
            ((RectTransform)spacer.transform).SetParent(rect, false);
            ((RectTransform)spacer.transform).sizeDelta = new Vector2(10f, 10f);

            foreach (FeatureCategory category in Categories)
            {
                if (!ConfigHelper.Settings.Any(s => s.Category == category))
                {
                    continue;
                }
                FeatureCategory captured = category;
                var entry = new GameObject("Category " + category, typeof(RectTransform));
                var entryRect = (RectTransform)entry.transform;
                entryRect.SetParent(rect, false);
                entryRect.sizeDelta = new Vector2(MenuButtonWidth, 74f);

                DefaultUIButton button = Object.Instantiate(kit.Button, entryRect, false);
                button.name = "Open " + category;
                button.OnClick.RemoveAllListeners();
                button.OnClick.AddListener(() => tab.ShowCategory(captured));
                button.SetRawText(CategoryName(category), 26);
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = new Vector2(0.5f, 1f);
                buttonRect.anchorMax = new Vector2(0.5f, 1f);
                buttonRect.pivot = new Vector2(0.5f, 1f);
                buttonRect.anchoredPosition = Vector2.zero;
                LayoutElement width = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                width.minWidth = MenuButtonWidth;
                width.preferredWidth = MenuButtonWidth;

                TextMeshProUGUI blurb = MakeText(kit, entryRect, "", 13f);
                blurb.alignment = TextAlignmentOptions.Bottom;
                blurb.color = new Color(kit.TextColor.r, kit.TextColor.g, kit.TextColor.b, 0.65f);
                var blurbRect = blurb.rectTransform;
                blurbRect.anchorMin = new Vector2(0f, 0f);
                blurbRect.anchorMax = new Vector2(1f, 0f);
                blurbRect.pivot = new Vector2(0.5f, 0f);
                blurbRect.anchoredPosition = Vector2.zero;
                blurbRect.sizeDelta = new Vector2(0f, 20f);

                List<ConfigEntry<bool>> features = ConfigHelper.Settings
                    .Where(s => s.Category == category && s.Entry.Definition.Section == ConfigHelper.MainSection)
                    .Select(s => s.Entry).OfType<ConfigEntry<bool>>().ToList();
                tab.MenuRefreshers.Add(() =>
                {
                    int on = features.Count(f => f.Value);
                    blurb.text = $"{CategoryBlurb(captured)}   ·   {on}/{features.Count} on";
                });
            }

            if (_showAdvanced != null && kit.Toggle != null)
            {
                var spacing = new GameObject("Spacer", typeof(RectTransform));
                ((RectTransform)spacing.transform).SetParent(rect, false);
                ((RectTransform)spacing.transform).sizeDelta = new Vector2(10f, 6f);

                GameObject row = Object.Instantiate(kit.Toggle, rect, false);
                row.name = "ReClassic Show Advanced";
                row.SetActive(true);
                TextMeshProUGUI label = row.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    label.text = "SHOW ADVANCED SETTINGS";
                }
                UpdatableToggle toggle = row.GetComponent<UpdatableToggle>() ?? row.GetComponentInChildren<UpdatableToggle>(true);
                if (toggle != null)
                {
                    toggle.UpdateValue(_showAdvanced.Value, false);
                    toggle.Bind(value =>
                    {
                        if (_showAdvanced.Value != value)
                        {
                            _showAdvanced.Value = value;
                        }
                    });
                    tab.MenuRefreshers.Add(() => toggle.UpdateValue(_showAdvanced.Value, false));
                }
                HoverTooltipArea tooltip = row.AddComponent<HoverTooltipArea>();
                tooltip.SetMessageText("Also list fine-tuning settings (strengths, ranges, timings) in the category windows.", true);
            }
        }

        private static bool BuildTabButton(SettingsScreen screen, FeatureTab tab)
        {
            UIAnimatedToggleSpawner source = screen._controlsButton;
            if (source == null)
            {
                return false;
            }
            GameObject buttonObject = Object.Instantiate(source.gameObject, source.transform.parent, false);
            buttonObject.name = "ReClassicToggleSpawner";
            buttonObject.transform.SetAsLastSibling();
            var spawner = buttonObject.GetComponent<UIAnimatedToggleSpawner>();
            spawner._headerCaption = TabCaption;
            spawner._unavailable = false;

            AnimatedToggle toggle = spawner.SpawnedObject;
            if (toggle == null)
            {
                Object.Destroy(buttonObject);
                return false;
            }
            toggle.isOn = false;
            toggle.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    tab.Open();
                }
            });
            return true;
        }

        private static List<Transform> Children(Transform parent)
        {
            var children = new List<Transform>();
            foreach (Transform child in parent)
            {
                children.Add(child);
            }
            return children;
        }

        private static void Hide(SettingsScreen screen)
        {
            FeatureTab tab = screen != null ? screen.GetComponent<FeatureTab>() : null;
            if (tab != null)
            {
                tab.Hide();
            }
        }

        private class AwakePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SettingsScreen), nameof(SettingsScreen.Awake));
            }

            [PatchPostfix]
            private static void Postfix(SettingsScreen __instance)
            {
                try
                {
                    Build(__instance);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[FeatureSettingsTab] Could not build the Re-Classic settings tab: {e}");
                }
            }
        }

        private class ShowScreenPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SettingsScreen), nameof(SettingsScreen.ShowScreen));
            }

            [PatchPostfix]
            private static void Postfix(SettingsScreen __instance)
            {
                Hide(__instance);
            }
        }

        private class ClosePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SettingsScreen), nameof(SettingsScreen.Close));
            }

            [PatchPostfix]
            private static void Postfix(SettingsScreen __instance)
            {
                Hide(__instance);
            }
        }
    }
}
