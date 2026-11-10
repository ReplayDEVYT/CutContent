using BepInEx.Configuration;
using EFT.UI.Health;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace mizmii.ReClassic.Dev
{
    internal sealed class UiTuner : MonoBehaviour
    {
        private sealed class Edit
        {
            public string Row;
            public string Path;
            public Vector2 Offset;
            public float Scale = 1f;
            public float Font;
            public bool Hidden;
            [JsonIgnore] public Vector2 Applied;
            [JsonIgnore] public float AppliedScale = 1f;
            [JsonIgnore] public float AppliedFont;
            [JsonIgnore] public RectTransform Target;
        }

        private sealed class Node
        {
            public RectTransform Rect;
            public string Path;
            public int Depth;
        }

        public static ConfigEntry<KeyboardShortcut> Key;
        public static ConfigEntry<bool> Show;

        private static readonly string FilePath = Path.Combine(BepInEx.Paths.BepInExRootPath, "ReClassic-ui-tuning.json");

        private readonly Dictionary<string, Edit> _edits = new Dictionary<string, Edit>();
        private readonly List<Node> _nodes = new List<Node>();
        private HealthParametersPanel _panel;
        private string _row = "Blood Pressure";
        private Node _selected;
        private float _nextSearch;
        private Rect _window = new Rect(480f, 80f, 460f, 680f);
        private Vector2 _treeScroll;
        private float _step = 2f;
        private string _status = "";

        public static void Bind(ConfigFile config, string section)
        {
            Key = config.Bind(section, "UI Tuner", new KeyboardShortcut(KeyCode.None), "Open the health panel UI tuner.");
            Show = config.Bind(section, "Show UI Tuner", false, "Show the health panel UI tuner. Open the inventory health screen, pick an element, move or resize it, then press Save and send BepInEx/ReClassic-ui-tuning.json.");
        }

        private static Dictionary<string, HealthParameterPanel> Rows(HealthParametersPanel panel)
        {
            var rows = new Dictionary<string, HealthParameterPanel>();
            void Add(string name, HealthParameterPanel row)
            {
                if (row != null)
                {
                    rows[name] = row;
                }
            }
            Add("Health", panel._overallHealth);
            Add("Temperature", panel._temperature);
            Add("Poisoning", panel._poisoning);
            Add("Radiation", panel._radiation);
            Add("Blood Pressure", panel._bloodPressure);
            Add("Hydration", panel._hydration);
            Add("Energy", panel._energy);
            return rows;
        }

        private void Update()
        {
            if (Key.Value.MainKey != KeyCode.None && Key.Value.IsDown())
            {
                Show.Value = !Show.Value;
            }
            if (!Show.Value)
            {
                return;
            }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            if ((_panel == null || !_panel.isActiveAndEnabled) && Time.unscaledTime >= _nextSearch)
            {
                _nextSearch = Time.unscaledTime + 1f;
                _panel = FindObjectsOfType<HealthParametersPanel>().FirstOrDefault(p => p.isActiveAndEnabled);
                _selected = null;
                Rebuild();
            }
        }

        private void LateUpdate()
        {
            foreach (Edit edit in _edits.Values)
            {
                Apply(edit);
            }
        }

        private void Apply(Edit edit)
        {
            RectTransform rect = edit.Target;
            if (rect == null)
            {
                return;
            }
            Vector2 delta = edit.Offset - edit.Applied;
            if (delta.sqrMagnitude > 0f)
            {
                rect.anchoredPosition += delta;
                edit.Applied = edit.Offset;
            }
            if (!Mathf.Approximately(edit.Scale, edit.AppliedScale))
            {
                rect.localScale = rect.localScale / edit.AppliedScale * edit.Scale;
                edit.AppliedScale = edit.Scale;
            }
            TMP_Text text = rect.GetComponent<TMP_Text>();
            if (text != null && !Mathf.Approximately(edit.Font, edit.AppliedFont))
            {
                text.enableAutoSizing = false;
                text.fontSize += edit.Font - edit.AppliedFont;
                edit.AppliedFont = edit.Font;
            }
            if (rect.gameObject.activeSelf == edit.Hidden && edit.Hidden)
            {
                rect.gameObject.SetActive(false);
            }
        }

        private void Rebuild()
        {
            _nodes.Clear();
            if (_panel == null || !Rows(_panel).TryGetValue(_row, out HealthParameterPanel row))
            {
                return;
            }
            Walk(row.transform as RectTransform, "", 0);
            foreach (Edit edit in _edits.Values)
            {
                if (edit.Row == _row)
                {
                    edit.Target = _nodes.FirstOrDefault(n => n.Path == edit.Path)?.Rect ?? edit.Target;
                }
            }
        }

        private void Walk(RectTransform rect, string path, int depth)
        {
            if (rect == null)
            {
                return;
            }
            _nodes.Add(new Node { Rect = rect, Path = path.Length == 0 ? rect.name : path, Depth = depth });
            foreach (Transform child in rect)
            {
                Walk(child as RectTransform, (path.Length == 0 ? rect.name : path) + "/" + child.name, depth + 1);
            }
        }

        private Edit EditFor(Node node)
        {
            string key = _row + "|" + node.Path;
            if (!_edits.TryGetValue(key, out Edit edit))
            {
                edit = new Edit { Row = _row, Path = node.Path, Target = node.Rect };
                TMP_Text text = node.Rect.GetComponent<TMP_Text>();
                _edits[key] = edit;
            }
            edit.Target = node.Rect;
            return edit;
        }

        private void OnGUI()
        {
            if (!Show.Value)
            {
                return;
            }
            GUI.depth = -1000;
            if (_selected?.Rect != null)
            {
                Outline(_selected.Rect);
            }
            _window = GUILayout.Window(83018, _window, Draw, "Re-Classic health UI tuner", GUILayout.MinWidth(460f));
        }

        private static void Outline(RectTransform rect)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 corner in corners)
            {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, corner);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            var box = new Rect(min.x, Screen.height - max.y, max.x - min.x, max.y - min.y);
            Color old = GUI.color;
            GUI.color = Color.yellow;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 2f, box.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y, 2f, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMax - 2f, box.y, 2f, box.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void Draw(int id)
        {
            if (_panel == null)
            {
                GUILayout.Label("Open the health screen in the inventory to start.");
                GUI.DragWindow();
                return;
            }
            GUILayout.BeginHorizontal();
            int column = 0;
            foreach (string name in Rows(_panel).Keys)
            {
                if (GUILayout.Toggle(_row == name, name, "Button") && _row != name)
                {
                    _row = name;
                    _selected = null;
                    Rebuild();
                }
                if (++column % 4 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Refresh list"))
            {
                Rebuild();
            }

            _treeScroll = GUILayout.BeginScrollView(_treeScroll, GUILayout.Height(220f));
            foreach (Node node in _nodes)
            {
                if (node.Rect == null)
                {
                    continue;
                }
                string label = new string(' ', node.Depth * 3) + node.Rect.name + (node.Rect.gameObject.activeSelf ? "" : " (off)");
                if (_edits.ContainsKey(_row + "|" + node.Path))
                {
                    label += "  *";
                }
                if (GUILayout.Toggle(_selected == node, label, "Button"))
                {
                    _selected = node;
                }
            }
            GUILayout.EndScrollView();

            if (_selected?.Rect != null)
            {
                RectTransform rect = _selected.Rect;
                TMP_Text text = rect.GetComponent<TMP_Text>();
                GUILayout.Label(_selected.Path);
                GUILayout.Label($"Position {rect.anchoredPosition.x:0.0}, {rect.anchoredPosition.y:0.0}   Size {rect.rect.width:0}x{rect.rect.height:0}   Scale {rect.localScale.x:0.00}" + (text != null ? $"   Font {text.fontSize:0.0}" : ""));
                if (text != null)
                {
                    GUILayout.Label("Text: " + text.text);
                }
                Edit edit = _edits.TryGetValue(_row + "|" + _selected.Path, out Edit existing) ? existing : null;
                GUILayout.BeginHorizontal();
                GUILayout.Label("Step", GUILayout.Width(40f));
                foreach (float step in new[] { 0.5f, 1f, 2f, 5f, 10f })
                {
                    if (GUILayout.Toggle(Mathf.Approximately(_step, step), step.ToString("0.#"), "Button"))
                    {
                        _step = step;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Left"))
                {
                    (edit = EditFor(_selected)).Offset.x -= _step;
                }
                if (GUILayout.Button("Right"))
                {
                    (edit = EditFor(_selected)).Offset.x += _step;
                }
                if (GUILayout.Button("Up"))
                {
                    (edit = EditFor(_selected)).Offset.y += _step;
                }
                if (GUILayout.Button("Down"))
                {
                    (edit = EditFor(_selected)).Offset.y -= _step;
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Smaller"))
                {
                    edit = EditFor(_selected);
                    edit.Scale = Mathf.Max(0.1f, edit.Scale - 0.05f);
                }
                if (GUILayout.Button("Bigger"))
                {
                    edit = EditFor(_selected);
                    edit.Scale += 0.05f;
                }
                if (text != null && GUILayout.Button("Font -"))
                {
                    (edit = EditFor(_selected)).Font -= 1f;
                }
                if (text != null && GUILayout.Button("Font +"))
                {
                    (edit = EditFor(_selected)).Font += 1f;
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(rect.gameObject.activeSelf ? "Hide" : "Show"))
                {
                    edit = EditFor(_selected);
                    edit.Hidden = rect.gameObject.activeSelf;
                    rect.gameObject.SetActive(!edit.Hidden);
                }
                if (edit != null && GUILayout.Button("Undo this element"))
                {
                    edit.Offset = Vector2.zero;
                    edit.Scale = 1f;
                    edit.Font = 0f;
                    edit.Hidden = false;
                    Apply(edit);
                    _edits.Remove(_row + "|" + _selected.Path);
                }
                GUILayout.EndHorizontal();
                if (edit != null)
                {
                    GUILayout.Label($"Change: move {edit.Offset.x:+0.0;-0.0;0}, {edit.Offset.y:+0.0;-0.0;0}   scale x{edit.Scale:0.00}   font {edit.Font:+0;-0;0}" + (edit.Hidden ? "   hidden" : ""));
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("Note for me (what's wrong, what you want):");
            _note = GUILayout.TextArea(_note, GUILayout.Height(50f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save for Claude"))
            {
                Save();
            }
            if (GUILayout.Button("Undo everything"))
            {
                foreach (Edit edit in _edits.Values)
                {
                    edit.Offset = Vector2.zero;
                    edit.Scale = 1f;
                    edit.Font = 0f;
                    edit.Hidden = false;
                    Apply(edit);
                    if (edit.Target != null)
                    {
                        edit.Target.gameObject.SetActive(true);
                    }
                }
                _edits.Clear();
                _status = "Everything is back to how the mod draws it.";
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(_status);
            GUI.DragWindow();
        }

        private string _note = "";

        private void Save()
        {
            try
            {
                var rows = new Dictionary<string, List<string>>();
                foreach (var pair in Rows(_panel))
                {
                    var lines = new List<string>();
                    Dump(pair.Value.transform as RectTransform, "", lines);
                    rows[pair.Key] = lines;
                }
                var report = new
                {
                    Note = _note,
                    Screen = $"{Screen.width}x{Screen.height}",
                    Edits = _edits.Values.Select(e => new
                    {
                        e.Row,
                        e.Path,
                        Move = new[] { e.Offset.x, e.Offset.y },
                        e.Scale,
                        FontChange = e.Font,
                        e.Hidden,
                        Now = e.Target == null ? null : Describe(e.Target)
                    }).ToList(),
                    Rows = rows
                };
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(report, Formatting.Indented));
                _status = "Saved to " + FilePath + ". Tell Claude it's ready.";
                Plugin.LogSource.LogInfo("[Dev] UI tuning saved to " + FilePath);
            }
            catch (Exception e)
            {
                _status = "Could not save: " + e.Message;
            }
        }

        private static string Describe(RectTransform rect)
        {
            TMP_Text text = rect.GetComponent<TMP_Text>();
            return $"pos {rect.anchoredPosition.x:0.0},{rect.anchoredPosition.y:0.0} size {rect.rect.width:0.0}x{rect.rect.height:0.0} anchors {rect.anchorMin.x:0.##},{rect.anchorMin.y:0.##}-{rect.anchorMax.x:0.##},{rect.anchorMax.y:0.##} pivot {rect.pivot.x:0.##},{rect.pivot.y:0.##} scale {rect.localScale.x:0.##}"
                + (text != null ? $" font {text.fontSize:0.#} align {text.alignment} text '{text.text}'" : "")
                + (rect.gameObject.activeSelf ? "" : " (off)");
        }

        private static void Dump(RectTransform rect, string indent, List<string> lines)
        {
            if (rect == null)
            {
                return;
            }
            lines.Add(indent + rect.name + ": " + Describe(rect));
            foreach (Transform child in rect)
            {
                Dump(child as RectTransform, indent + "  ", lines);
            }
        }
    }
}
