using EFT;
using EFT.UI.Health;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Radiation
{
    internal sealed class RateReadout : MonoBehaviour
    {
        private static readonly Color Good = new Color(0.349f, 0.667f, 0.835f);
        private static readonly Color Bad = new Color(0.729f, 0f, 0f);

        private Image _up;
        private Image _down;
        private TMP_Text _value;
        private Animator _upAnimator;
        private Animator _downAnimator;
        private float _shown = float.NaN;

        public static RateReadout For(HealthParameterPanel target, BuffableHealthParameterPanel source, Vector2 offset = default)
        {
            if (target == null)
            {
                return null;
            }
            RateReadout readout = target.GetComponent<RateReadout>();
            if (readout != null)
            {
                return readout;
            }
            if (source == null || source._upBuffArrow == null || source._downBuffArrow == null || source._buffValue == null)
            {
                return null;
            }
            Transform up = Existing(source._upBuffArrow.transform, source.transform, target.transform) ?? Copy(source._upBuffArrow.transform, source.transform, target.transform);
            Transform down = Existing(source._downBuffArrow.transform, source.transform, target.transform) ?? Copy(source._downBuffArrow.transform, source.transform, target.transform);
            Transform value = Existing(source._buffValue.transform, source.transform, target.transform) ?? Copy(source._buffValue.transform, source.transform, target.transform);
            if (up == null || down == null || value == null || up.GetComponent<Image>() == null || down.GetComponent<Image>() == null || value.GetComponent<TMP_Text>() == null)
            {
                if (Logged.Add(target.name))
                {
                    Plugin.LogSource.LogWarning($"[Radiation] Could not find the rate on the {target.name} panel. Its children:\n{Hierarchy(target.transform, "")}\nHydration panel:\n{Hierarchy(source.transform, "")}");
                }
                return null;
            }
            readout = target.gameObject.AddComponent<RateReadout>();
            readout._up = up.GetComponent<Image>();
            readout._down = down.GetComponent<Image>();
            readout._value = value.GetComponent<TMP_Text>();
            readout._current = target._currentValue;
            if (offset != Vector2.zero)
            {
                readout._value.rectTransform.anchoredPosition += offset;
                Transform arrows = readout._up.transform.parent != null ? readout._up.transform.parent.parent : null;
                if (arrows is RectTransform arrowRect && arrows != target.transform && !arrows.IsChildOf(readout._value.transform))
                {
                    arrowRect.anchoredPosition += offset;
                }
            }
            readout._upAnimator = up.GetComponent<Animator>();
            readout._downAnimator = down.GetComponent<Animator>();
            readout.Hide();
            return readout;
        }

        private static readonly HashSet<string> Logged = new HashSet<string>();

        private static Transform Existing(Transform element, Transform sourceRoot, Transform targetRoot)
        {
            string path = Path(sourceRoot, element);
            if (!string.IsNullOrEmpty(path))
            {
                Transform match = targetRoot.Find(path);
                if (match != null)
                {
                    return match;
                }
            }
            foreach (Transform child in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child != targetRoot && child.name == element.name)
                {
                    return child;
                }
            }
            return null;
        }

        private static Transform Copy(Transform element, Transform sourceRoot, Transform targetRoot)
        {
            var chain = new List<Transform>();
            for (Transform t = element; t != null && t != sourceRoot; t = t.parent)
            {
                chain.Insert(0, t);
            }
            if (chain.Count == 0 || chain[0].parent != sourceRoot)
            {
                return null;
            }
            Transform current = targetRoot;
            for (int i = 0; i < chain.Count; i++)
            {
                Transform next = current.Find(chain[i].name);
                if (next == null)
                {
                    GameObject copy = Instantiate(chain[i].gameObject, current, false);
                    copy.name = chain[i].name;
                    copy.transform.SetSiblingIndex(Mathf.Min(chain[i].GetSiblingIndex(), current.childCount - 1));
                    next = copy.transform;
                }
                current = next;
            }
            return current;
        }

        private static string Hierarchy(Transform root, string indent)
        {
            string text = indent + root.name + (root.gameObject.activeSelf ? "" : " (off)") + "\n";
            foreach (Transform child in root)
            {
                text += Hierarchy(child, indent + "  ");
            }
            return text;
        }

        private static string Path(Transform root, Transform current)
        {
            string path = "";
            while (current != null && current != root)
            {
                path = path.Length == 0 ? current.name : current.name + "/" + path;
                current = current.parent;
            }
            return current == root ? path : "";
        }

        public void Set(float perSecond)
        {
            float perMinute = perSecond * 60f;
            if (!float.IsNaN(_shown) && Mathf.Abs(perMinute - _shown) < 0.005f)
            {
                return;
            }
            _shown = perMinute;
            if (Mathf.Abs(perMinute) < 0.05f)
            {
                Hide();
                return;
            }
            bool rising = perMinute > 0f;
            _up.gameObject.SetActive(rising);
            _down.gameObject.SetActive(!rising);
            _value.gameObject.SetActive(true);
            _value.SetMonospaceText(perMinute.ToString("0.00"));
            _value.color = rising ? Bad : Good;
            _up.color = Bad;
            _down.color = Good;
            if (_upAnimator != null && _upAnimator.isActiveAndEnabled)
            {
                _upAnimator.SetBool("Active", true);
            }
            if (_downAnimator != null && _downAnimator.isActiveAndEnabled)
            {
                _downAnimator.SetBool("Active", true);
            }
        }

        public void SetPair(float firstPerMinute, float secondPerMinute, bool towardNormal)
        {
            string text = Compact(firstPerMinute) + "/" + Compact(secondPerMinute);
            if (Mathf.Abs(firstPerMinute) < 0.05f && Mathf.Abs(secondPerMinute) < 0.05f)
            {
                if (_pairText != null)
                {
                    _pairText = null;
                    Hide();
                }
                return;
            }
            if (text == _pairText)
            {
                return;
            }
            _pairText = text;
            bool rising = Mathf.Abs(firstPerMinute) >= Mathf.Abs(secondPerMinute) ? firstPerMinute > 0f : secondPerMinute > 0f;
            Color color = towardNormal ? Good : Bad;
            _up.gameObject.SetActive(rising);
            _down.gameObject.SetActive(!rising);
            _value.gameObject.SetActive(true);
            if (!_shrunk)
            {
                _shrunk = true;
                _value.enableAutoSizing = false;
                _value.fontSize *= 0.8f;
            }
            _value.SetMonospaceText(text);
            LiftAbove();
            _value.color = color;
            _up.color = color;
            _down.color = color;
            if (_upAnimator != null && _upAnimator.isActiveAndEnabled)
            {
                _upAnimator.SetBool("Active", true);
            }
            if (_downAnimator != null && _downAnimator.isActiveAndEnabled)
            {
                _downAnimator.SetBool("Active", true);
            }
        }

        private string _pairText;
        private bool _shrunk;
        private TMP_Text _current;

        private void LiftAbove()
        {
            if (_current == null)
            {
                return;
            }
            _current.ForceMeshUpdate();
            _value.ForceMeshUpdate();
            Bounds reading = _current.textBounds;
            Bounds rate = _value.textBounds;
            if (reading.size.x <= 0f || rate.size.x <= 0f)
            {
                return;
            }
            Vector3 corner = _current.transform.TransformPoint(new Vector3(reading.max.x, reading.max.y, 0f));
            Vector3 mine = _value.transform.TransformPoint(new Vector3(rate.max.x, rate.min.y, 0f));
            Transform space = _value.transform.parent != null ? _value.transform.parent : _value.transform;
            Vector3 delta = corner + space.TransformVector(new Vector3(15f, 5f, 0f)) - mine;
            delta.z = 0f;
            if (delta.sqrMagnitude < 0.0001f)
            {
                return;
            }
            _value.transform.position += delta;
            Transform arrows = _up.transform.parent != null ? _up.transform.parent.parent : null;
            if (arrows != null && arrows != transform && !arrows.IsChildOf(_value.transform))
            {
                arrows.position += delta;
            }
        }

        private static string Compact(float value)
        {
            return Mathf.Abs(value) >= 9.95f ? value.ToString("+0;-0;0") : value.ToString("+0.#;-0.#;0");
        }

        private void Hide()
        {
            _up.gameObject.SetActive(false);
            _down.gameObject.SetActive(false);
            _value.gameObject.SetActive(false);
        }
    }
}
