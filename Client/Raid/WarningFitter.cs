using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Raid
{
    internal class WarningFitter : MonoBehaviour
    {
        private class Saved
        {
            public RectTransform Rect;
            public Vector2 Size;
            public LayoutElement Element;
            public bool AddedElement;
            public float MinHeight;
            public float PreferredHeight;
        }

        private static bool _logged;

        private readonly List<Saved> _saved = new List<Saved>();
        private RectTransform _root;
        private RectTransform _panel;
        private TMP_Text _header;
        private TMP_Text _description;
        private bool _headerAutoSize;
        private float _headerMin;
        private float _headerMax;
        private bool _descriptionWrap;
        private bool _active;
        private string _fittedText;
        private float _fittedWidth = -1f;

        public void Begin(RectTransform root, RectTransform panel, TMP_Text header, TMP_Text description)
        {
            Stop();
            _root = root;
            _panel = panel;
            _header = header;
            _description = description;
            if (_header != null)
            {
                _headerAutoSize = _header.enableAutoSizing;
                _headerMin = _header.fontSizeMin;
                _headerMax = _header.fontSizeMax;
                _header.fontSizeMax = _header.fontSize;
                _header.fontSizeMin = Mathf.Min(10f, _header.fontSize);
                _header.enableAutoSizing = true;
            }
            if (_description != null)
            {
                _descriptionWrap = _description.enableWordWrapping;
                _description.enableWordWrapping = true;
            }
            _active = true;
            _fittedText = null;
            _fittedWidth = -1f;
            LogLayout();
        }

        public void Stop()
        {
            if (!_active)
            {
                return;
            }
            _active = false;
            RestoreSizes();
            if (_header != null)
            {
                _header.enableAutoSizing = _headerAutoSize;
                _header.fontSizeMin = _headerMin;
                _header.fontSizeMax = _headerMax;
            }
            if (_description != null)
            {
                _description.enableWordWrapping = _descriptionWrap;
            }
        }

        private void OnDisable()
        {
            _fittedWidth = -1f;
        }

        private void LateUpdate()
        {
            if (!_active || _description == null || _panel == null)
            {
                return;
            }
            float width = _description.rectTransform.rect.width;
            if (width <= 1f || (_fittedText == _description.text && Mathf.Abs(_fittedWidth - width) < 0.5f))
            {
                return;
            }
            RestoreSizes();
            Fit(width);
            _fittedText = _description.text;
            _fittedWidth = _description.rectTransform.rect.width;
        }

        private void RestoreSizes()
        {
            foreach (Saved saved in _saved)
            {
                if (saved.Rect != null)
                {
                    saved.Rect.sizeDelta = saved.Size;
                }
                if (saved.Element != null)
                {
                    if (saved.AddedElement)
                    {
                        Destroy(saved.Element);
                    }
                    else
                    {
                        saved.Element.minHeight = saved.MinHeight;
                        saved.Element.preferredHeight = saved.PreferredHeight;
                    }
                }
            }
            _saved.Clear();
            if (_panel != null)
            {
                Rebuild();
            }
        }

        private void Fit(float width)
        {
            RectTransform text = _description.rectTransform;
            float needed = _description.GetPreferredValues(_description.text, width, 0f).y + 4f;
            float delta = needed - text.rect.height;
            if (delta <= 0.5f)
            {
                return;
            }
            Grow(text, delta);
            Transform current = text.parent;
            while (current != null)
            {
                if (current is RectTransform rect)
                {
                    Grow(rect, delta);
                }
                if (current == _panel)
                {
                    break;
                }
                current = current.parent;
            }
            Rebuild();
        }

        private void Grow(RectTransform rect, float delta)
        {
            var saved = new Saved { Rect = rect, Size = rect.sizeDelta };
            bool stretched = !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y);
            if (!stretched)
            {
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y + delta);
            }
            bool inLayout = rect.parent != null && rect.parent.GetComponent<LayoutGroup>() != null;
            LayoutElement element = rect.GetComponent<LayoutElement>();
            if (element != null || inLayout)
            {
                float height = rect.rect.height + (stretched ? delta : 0f);
                if (element == null)
                {
                    element = rect.gameObject.AddComponent<LayoutElement>();
                    saved.AddedElement = true;
                }
                saved.Element = element;
                saved.MinHeight = element.minHeight;
                saved.PreferredHeight = element.preferredHeight;
                element.minHeight = Mathf.Max(element.minHeight, height);
                if (element.preferredHeight >= 0f || saved.AddedElement)
                {
                    element.preferredHeight = Mathf.Max(element.preferredHeight, height);
                }
            }
            _saved.Add(saved);
        }

        private void Rebuild()
        {
            Transform top = _root != null ? _root : _panel.parent;
            if (top is RectTransform rect)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            }
        }

        private void LogLayout()
        {
            if (_logged || _panel == null)
            {
                return;
            }
            _logged = true;
            var builder = new StringBuilder("[Raid Timers] Map warning layout:");
            Transform start = _panel.parent != null ? _panel.parent : _panel;
            Dump(builder, start, 0, 4);
            Plugin.LogSource.LogInfo(builder.ToString());
        }

        private static void Dump(StringBuilder builder, Transform node, int depth, int maxDepth)
        {
            builder.Append('\n').Append(' ', depth * 2).Append(node.name);
            if (node is RectTransform rect)
            {
                builder.Append($" a[{rect.anchorMin}-{rect.anchorMax}] size{rect.sizeDelta} rect{rect.rect.size}");
            }
            foreach (Component component in node.GetComponents<Component>())
            {
                if (component is LayoutGroup || component is LayoutElement || component is ContentSizeFitter)
                {
                    builder.Append(' ').Append(component.GetType().Name);
                }
            }
            if (depth >= maxDepth)
            {
                return;
            }
            foreach (Transform child in node)
            {
                Dump(builder, child, depth + 1, maxDepth);
            }
        }
    }
}
