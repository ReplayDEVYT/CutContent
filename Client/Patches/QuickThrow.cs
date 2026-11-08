using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace mizmii.ReClassic.Patches
{
    internal static class QuickThrow
    {
        private const string IconResource = "ReClassic.Resources.quickthrow.png";

        private static ConfigEntry<bool> _enabled;
        private static bool _fromGrenadeKey;
        private static ThrowWeap _selected;
        private static float _selectedAt;
        private static Sprite _icon;
        private static bool _iconFailed;

        private static readonly AccessTools.FieldRef<QuickSlotView, InventoryController> SlotInventory = AccessTools.FieldRefAccess<QuickSlotView, InventoryController>("InventoryController");

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Combat, "Grenades", "Grenade Quick Throw", true,
                "Grenades carried in your rig are thrown straight away with the old quick throw when picked with the grenade key, instead of being drawn so you pull the pin and throw by hand. Grenades in your pockets are still drawn. The grenade slot on the hotbar shows an arrow when the grenade will be quick thrown.",
                "Combat - Grenade Quick Throw", "Enabled");
            new GrenadeKeyPatch().Enable();
            new SelectorResultPatch().Enable();
            new SetItemInHandsPatch().Enable();
            new SetGrenadeInHandsPatch().Enable();
            new GrenadeSlotPatch().Enable();
            new SelectorViewPatch().Enable();
        }

        private static CompoundItem Rig(InventoryController inventory)
        {
            return inventory?.Inventory?.Equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem;
        }

        private static bool IsIn(Item item, Item container)
        {
            Item current = item?.Parent?.Container?.ParentItem;
            for (int depth = 0; current != null && depth < 8; depth++)
            {
                if (current == container)
                {
                    return true;
                }
                current = current.Parent?.Container?.ParentItem;
            }
            return false;
        }

        public static ThrowWeap RigGrenade(InventoryController inventory, ThrowWeap wanted)
        {
            if (!_enabled.Value || inventory == null || wanted == null)
            {
                return null;
            }
            CompoundItem rig = Rig(inventory);
            if (rig == null)
            {
                return null;
            }
            if (IsIn(wanted, rig))
            {
                return wanted;
            }
            return rig.GetAllItems().OfType<ThrowWeap>().FirstOrDefault(g => g.TemplateId == wanted.TemplateId && g != wanted && inventory.Examined(g));
        }

        private static bool TryQuickThrow(Player player, ThrowWeap grenade)
        {
            if (player == null || !player.IsYourPlayer || grenade == null || player.HandsController is IQuickGrenadeThrowController || player.HandsController?.Item is ThrowWeap)
            {
                return false;
            }
            ThrowWeap rigGrenade = RigGrenade(player.InventoryController, grenade);
            if (rigGrenade == null)
            {
                return false;
            }
            try
            {
                player.SetInHandsForQuickUse(rigGrenade, result => { });
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Quick Throw] Could not quick throw: {e}");
                return false;
            }
        }

        private static Sprite Icon()
        {
            if (_icon != null || _iconFailed)
            {
                return _icon;
            }
            try
            {
                using (Stream stream = typeof(QuickThrow).Assembly.GetManifestResourceStream(IconResource))
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    texture.LoadImage(memory.ToArray());
                    texture.filterMode = FilterMode.Bilinear;
                    UnityEngine.Object.DontDestroyOnLoad(texture);
                    _icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    UnityEngine.Object.DontDestroyOnLoad(_icon);
                }
            }
            catch (Exception e)
            {
                _iconFailed = true;
                Plugin.LogSource.LogError($"[Quick Throw] Could not load the hotbar icon: {e}");
            }
            return _icon;
        }

        private static void AddBadge(Transform parent, Func<bool> shown, float size)
        {
            if (parent == null || Icon() == null)
            {
                return;
            }
            Transform existing = parent.Find("ReClassic_QuickThrow");
            QuickThrowBadge badge;
            if (existing != null)
            {
                badge = existing.GetComponent<QuickThrowBadge>();
            }
            else
            {
                var go = new GameObject("ReClassic_QuickThrow", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-3f, -3f);
                rect.sizeDelta = new Vector2(size, size);
                Image image = go.AddComponent<Image>();
                image.sprite = Icon();
                image.raycastTarget = false;
                image.preserveAspect = true;
                image.color = new Color(0.94f, 0.82f, 0.45f, 1f);
                badge = go.AddComponent<QuickThrowBadge>();
                badge.Image = image;
            }
            if (badge != null)
            {
                badge.Shown = shown;
                badge.Refresh();
            }
            existing = badge != null ? badge.transform : null;
            existing?.SetAsLastSibling();
        }

        private class QuickThrowBadge : MonoBehaviour
        {
            public Image Image;
            public Func<bool> Shown;
            private float _next;

            public void Refresh()
            {
                bool visible = false;
                try
                {
                    visible = Shown != null && Shown();
                }
                catch (Exception)
                {
                }
                if (Image != null && Image.enabled != visible)
                {
                    Image.enabled = visible;
                }
            }

            private void Update()
            {
                if (Time.unscaledTime >= _next)
                {
                    _next = Time.unscaledTime + 0.5f;
                    Refresh();
                }
            }
        }

        private class GrenadeKeyPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(PlayerInputTranslator), nameof(PlayerInputTranslator.vmethod_1));
            }

            [PatchPrefix]
            private static void Prefix()
            {
                _fromGrenadeKey = true;
            }

            [PatchFinalizer]
            private static void Finalizer()
            {
                _fromGrenadeKey = false;
            }
        }

        private class SelectorResultPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GrenadeSelector), nameof(GrenadeSelector.SetResult));
            }

            [PatchPrefix]
            private static void Prefix(ThrowWeap result)
            {
                if (result != null)
                {
                    _selected = result;
                    _selectedAt = Time.unscaledTime;
                }
            }
        }

        private class SetItemInHandsPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.SetItemInHands));
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance, Item item)
            {
                if (!_fromGrenadeKey || !(item is ThrowWeap grenade))
                {
                    return true;
                }
                _fromGrenadeKey = false;
                return !TryQuickThrow(__instance, grenade);
            }
        }

        private class SetGrenadeInHandsPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Player), nameof(Player.SetInHands), new[] { typeof(ThrowWeap), typeof(Callback<IGrenadeController>) });
            }

            [PatchPrefix]
            private static bool Prefix(Player __instance, ThrowWeap throwWeap)
            {
                if (_selected == null || _selected != throwWeap || Time.unscaledTime - _selectedAt > 5f)
                {
                    return true;
                }
                _selected = null;
                return !TryQuickThrow(__instance, throwWeap);
            }
        }

        private class GrenadeSlotPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(QuickSlotView), nameof(QuickSlotView.SetItem));
            }

            [PatchPostfix]
            private static void Postfix(QuickSlotView __instance)
            {
                if (!(__instance is FastAccessGrenadeItemView view))
                {
                    return;
                }
                try
                {
                    AddBadge(view.transform, () =>
                    {
                        InventoryController inventory = SlotInventory(view);
                        ThrowWeap top = inventory?.Inventory?.Equipment?.TopPriorityGrenade;
                        return top != null && RigGrenade(inventory, top) != null;
                    }, 22f);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Quick Throw] Could not add the hotbar icon: {e}");
                }
            }
        }

        private class SelectorViewPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(FastAccessGrenadeGridItemView), nameof(FastAccessGrenadeGridItemView.Create));
            }

            [PatchPostfix]
            private static void Postfix(FastAccessGrenadeGridItemView __result, Item item, ItemController itemController)
            {
                if (__result == null || !(item is ThrowWeap grenade) || !(itemController is InventoryController inventory))
                {
                    return;
                }
                try
                {
                    AddBadge(__result.transform, () => RigGrenade(inventory, grenade) != null, 20f);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Quick Throw] Could not add the selector icon: {e}");
                }
            }
        }
    }
}
