using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Consciousness
{
    internal sealed class BlackoutScreen : MonoBehaviour
    {
        private static Sprite _ring;

        private Image _black;
        private CanvasGroup _status;
        private RectTransform _spinner;
        private float _blackTarget;
        private float _blackSpeed = 1f;
        private float _statusTarget;

        private GameObject _blackRoot;
        private TextMeshProUGUI _countdown;
        private float _wakeAt = -1f;

        public static BlackoutScreen Create()
        {
            var root = new GameObject("ReClassicBlackout");
            DontDestroyOnLoad(root);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -50;
            AddScaler(root);
            BlackoutScreen screen = root.AddComponent<BlackoutScreen>();
            screen.BuildBlack();
            screen.Build(root.transform);
            return screen;
        }

        private static void AddScaler(GameObject root)
        {
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
        }

        private void BuildBlack()
        {
            _blackRoot = new GameObject("ReClassicBlackoutFade");
            DontDestroyOnLoad(_blackRoot);
            Canvas canvas = _blackRoot.AddComponent<Canvas>();
            Camera world = null;
            try
            {
                world = EFT.CameraControl.CameraManager.Instance.Camera;
            }
            catch (System.Exception)
            {
                world = null;
            }
            if (world != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = world;
                canvas.planeDistance = world.nearClipPlane + 0.02f;
                canvas.sortingOrder = 32000;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -100;
            }
            AddScaler(_blackRoot);
            var blackObject = new GameObject("Black", typeof(RectTransform));
            blackObject.transform.SetParent(_blackRoot.transform, false);
            Stretch(blackObject.GetComponent<RectTransform>());
            _black = blackObject.AddComponent<Image>();
            _black.color = new Color(0f, 0f, 0f, 0f);
            _black.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (_blackRoot != null)
            {
                Destroy(_blackRoot);
            }
        }

        private void Build(Transform root)
        {

            var statusObject = new GameObject("Status", typeof(RectTransform));
            statusObject.transform.SetParent(root, false);
            RectTransform status = statusObject.GetComponent<RectTransform>();
            status.anchorMin = new Vector2(0.5f, 0f);
            status.anchorMax = new Vector2(0.5f, 0f);
            status.pivot = new Vector2(0.5f, 0f);
            status.anchoredPosition = new Vector2(0f, 70f);
            status.sizeDelta = new Vector2(400f, 110f);
            _status = statusObject.AddComponent<CanvasGroup>();
            _status.alpha = 0f;
            _status.interactable = false;
            _status.blocksRaycasts = false;

            var textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(status, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.sizeDelta = new Vector2(0f, 40f);
            TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
            label.text = "Unconscious..";
            label.fontSize = 26f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color32(197, 195, 178, 255);
            label.raycastTarget = false;
            TMP_FontAsset font = GameFont();
            if (font != null)
            {
                label.font = font;
            }

            var spinnerObject = new GameObject("Spinner", typeof(RectTransform));
            spinnerObject.transform.SetParent(status, false);
            _spinner = spinnerObject.GetComponent<RectTransform>();
            _spinner.anchorMin = new Vector2(0.5f, 0f);
            _spinner.anchorMax = new Vector2(0.5f, 0f);
            _spinner.pivot = new Vector2(0.5f, 0.5f);
            _spinner.anchoredPosition = new Vector2(0f, 28f);
            _spinner.sizeDelta = new Vector2(44f, 44f);
            Image ring = spinnerObject.AddComponent<Image>();
            Sprite native = NativeSpinner();
            ring.sprite = native != null ? native : Ring();
            ring.color = native != null ? Color.white : new Color32(197, 195, 178, 255);
            ring.preserveAspect = true;
            ring.raycastTarget = false;

            var countdownObject = new GameObject("Countdown", typeof(RectTransform));
            countdownObject.transform.SetParent(status, false);
            RectTransform countdownRect = countdownObject.GetComponent<RectTransform>();
            countdownRect.anchorMin = new Vector2(0f, 0f);
            countdownRect.anchorMax = new Vector2(1f, 0f);
            countdownRect.pivot = new Vector2(0.5f, 1f);
            countdownRect.anchoredPosition = new Vector2(0f, -2f);
            countdownRect.sizeDelta = new Vector2(0f, 26f);
            _countdown = countdownObject.AddComponent<TextMeshProUGUI>();
            _countdown.fontSize = 18f;
            _countdown.alignment = TextAlignmentOptions.Center;
            _countdown.color = new Color32(140, 140, 132, 255);
            _countdown.raycastTarget = false;
            _countdown.text = string.Empty;
            if (font != null)
            {
                _countdown.font = font;
            }
        }

        public void SetCountdown(float realSeconds)
        {
            _wakeAt = realSeconds > 0f ? Time.realtimeSinceStartup + realSeconds : -1f;
            if (_wakeAt < 0f && _countdown != null)
            {
                _countdown.text = string.Empty;
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TMP_FontAsset GameFont()
        {
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            return fonts.FirstOrDefault(f => f.name.IndexOf("bender", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ?? FindObjectsOfType<TextMeshProUGUI>().Select(t => t.font).FirstOrDefault(f => f != null)
                ?? fonts.FirstOrDefault();
        }

        private static Sprite NativeSpinner()
        {
            try
            {
                return Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s != null && s.name == "spinner_big");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static Sprite Ring()
        {
            if (_ring != null)
            {
                return _ring;
            }
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(distance - 27f) / 3.5f);
                    float angle = Mathf.Repeat(Mathf.Atan2(dy, dx) / (Mathf.PI * 2f), 1f);
                    float tail = Mathf.Clamp01(angle / 0.8f);
                    float alpha = angle > 0.8f ? 0f : edge * tail;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            _ring = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return _ring;
        }

        public void FadeBlack(float alpha, float seconds)
        {
            _blackTarget = alpha;
            _blackSpeed = seconds <= 0f ? 1000f : 1f / seconds;
        }

        public void ShowStatus(bool show)
        {
            _statusTarget = show ? 1f : 0f;
        }

        public bool Idle => _black.color.a <= 0.001f && _blackTarget <= 0f && _status.alpha <= 0.001f;

        private void Update()
        {
            Color color = _black.color;
            color.a = Mathf.MoveTowards(color.a, _blackTarget, _blackSpeed * Time.unscaledDeltaTime);
            _black.color = color;
            _status.alpha = Mathf.MoveTowards(_status.alpha, _statusTarget, 2f * Time.unscaledDeltaTime);
            _spinner.Rotate(0f, 0f, -300f * Time.unscaledDeltaTime);
            if (_wakeAt >= 0f && _countdown != null)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(_wakeAt - Time.realtimeSinceStartup));
                _countdown.text = $"Waking up in {left / 60}:{left % 60:00}";
            }
        }
    }
}
