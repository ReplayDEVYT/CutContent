using BepInEx.Configuration;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace mizmii.ReClassic.Patches
{
    internal static class WelcomePopup
    {
        private const string Caption = "Re-Classic";
        private const float FallbackWidth = 600f;
        private const float ShowDelay = 1.5f;

        private const string Message =
            "<b>Welcome to Re-Classic!</b>\n\n" +
            "This mod adds back various pieces of cut content from old versions of EFT, specifically the alpha version (0.1). " +
            "This mod also attempts to remake the old weapon handling and combat mechanics by using actual code from old versions of Tarkov.\n" +
            "All of the mod's configs can be found in the game's settings menu under \"RE-CLASSIC\".\n\n" +
            "Enjoy!";

        private const string FikaMessage =
            "<b>Playing with Fika?</b>\n\n" +
            "This mod has full Fika support, which means that car alarms, weapon handling, IK, etc. are all synced properly.\n" +
            "<b>All Re-Classic settings are set to what the match's host has them as, so it is recommended that everyone you're playing with has the same settings in order to keep consistency!</b>";

        private sealed class Page
        {
            public ConfigEntry<bool> Shown;
            public string Message;
            public string Image;
            public Texture2D Texture;
        }

        private static readonly System.Collections.Generic.List<Page> Pages = new System.Collections.Generic.List<Page>();
        private static MenuScreen _menu;
        private static GameObject _runner;
        private static bool _open;
        private const float RetryDelay = 5f;

        private static Page Next => Pages.FirstOrDefault(p => !p.Shown.Value);

        public static void Enable(ConfigFile config)
        {
            Pages.Add(new Page
            {
                Shown = config.Bind("Hidden", "Welcome Shown", false, new ConfigDescription("Set once the welcome message has been closed with OK.", null, new ConfigurationManagerAttributes { Browsable = false })),
                Message = Message,
                Image = "Welcome.png"
            });
            if (Sync.FikaSync.Present)
            {
                Pages.Add(new Page
                {
                    Shown = config.Bind("Hidden", "Fika Welcome Shown", false, new ConfigDescription("Set once the Fika message has been closed with OK.", null, new ConfigurationManagerAttributes { Browsable = false })),
                    Message = FikaMessage,
                    Image = "WelcomeFika.png"
                });
            }
            if (Next == null)
            {
                return;
            }
            new MenuScreenShowPatch().Enable();
        }

        private sealed class Runner : MonoBehaviour
        {
            private float _visibleFor;

            private void Update()
            {
                if (Next == null)
                {
                    Destroy(gameObject);
                    return;
                }
                if (_open || _menu == null || !_menu.isActiveAndEnabled || ItemUiContext.Instance == null)
                {
                    _visibleFor = 0f;
                    return;
                }
                _visibleFor += Time.unscaledDeltaTime;
                if (_visibleFor < ShowDelay)
                {
                    return;
                }
                _visibleFor = ShowDelay - RetryDelay;
                try
                {
                    Show();
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[WelcomePopup] Could not show the welcome message: {e}");
                    Destroy(gameObject);
                }
            }
        }

        private static void Show()
        {
            ItemUiContext context = ItemUiContext.Instance;
            Page page = Next;
            if (page == null)
            {
                return;
            }

            GameObject image = null;
            Action close = () =>
            {
                _open = false;
                if (image != null)
                {
                    Object.Destroy(image);
                }
            };
            DialogWindowContext window = context.ShowMessageWindow(page.Message, () =>
            {
                close();
                page.Shown.Value = true;
                page.Shown.ConfigFile.Save();
            }, null, Caption, 0f, true);
            _open = true;
            window.OnDecline += close;
            window.OnClose += close;

            MessageWindow messageWindow = context._messageWindow;
            if (messageWindow != null && messageWindow._message != null)
            {
                image = AddImage(messageWindow._message, page);
            }
            Plugin.LogSource.LogInfo(image != null ? $"[WelcomePopup] Showed {page.Shown.Definition.Key}." : $"[WelcomePopup] Showed {page.Shown.Definition.Key} without the image.");
        }

        private static GameObject AddImage(TextMeshProUGUI message, Page page)
        {
            Texture2D texture = Texture(page);
            if (texture == null)
            {
                return null;
            }

            Transform anchor = message.transform;
            VerticalLayoutGroup layout = null;
            while (anchor.parent != null)
            {
                layout = anchor.parent.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    break;
                }
                anchor = anchor.parent;
            }
            if (layout == null)
            {
                return null;
            }

            var holder = new GameObject("ReClassic Welcome Image", typeof(RectTransform));
            holder.transform.SetParent(layout.transform, false);
            holder.transform.SetSiblingIndex(anchor.GetSiblingIndex());

            RawImage raw = holder.AddComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            float width = ((RectTransform)layout.transform).rect.width - layout.padding.horizontal;
            if (width <= 1f)
            {
                width = FallbackWidth;
            }
            LayoutElement element = holder.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = width * texture.height / texture.width;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)layout.transform);
            return holder;
        }

        private static Texture2D Texture(Page page)
        {
            if (page.Texture != null)
            {
                return page.Texture;
            }
            Assembly assembly = typeof(WelcomePopup).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + page.Image, StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                return null;
            }
            using (Stream stream = assembly.GetManifestResourceStream(name))
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(memory.ToArray()))
                {
                    Object.Destroy(texture);
                    return null;
                }
                texture.wrapMode = TextureWrapMode.Clamp;
                Object.DontDestroyOnLoad(texture);
                page.Texture = texture;
            }
            return page.Texture;
        }

        private class MenuScreenShowPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.Show), new[] { typeof(Profile), typeof(MatchmakerPlayersController), typeof(ESessionMode) });
            }

            [PatchPostfix]
            private static void Postfix(MenuScreen __instance)
            {
                if (Next == null)
                {
                    return;
                }
                _menu = __instance;
                if (_runner == null)
                {
                    _runner = new GameObject("ReClassic_WelcomePopup");
                    _runner.hideFlags = HideFlags.HideAndDontSave;
                    Object.DontDestroyOnLoad(_runner);
                    _runner.AddComponent<Runner>();
                }
            }
        }
    }
}
