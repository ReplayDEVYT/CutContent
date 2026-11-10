using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Effects
{
    internal static class AttributeIcons
    {
        private static readonly Dictionary<EHealthFactorType, Func<Sprite>> Sources = new Dictionary<EHealthFactorType, Func<Sprite>>();
        private static readonly Dictionary<EHealthFactorType, Sprite> Found = new Dictionary<EHealthFactorType, Sprite>();
        private static readonly Dictionary<EHealthFactorType, int> Tries = new Dictionary<EHealthFactorType, int>();
        private static bool _patched;

        public static void Register(EHealthFactorType id, Func<Sprite> source)
        {
            Sources[id] = source;
            if (!_patched)
            {
                _patched = true;
                new IconPatch().Enable();
            }
        }

        public static Sprite GameSprite(string name)
        {
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite != null && sprite.name == name)
                {
                    return sprite;
                }
            }
            return null;
        }

        private class IconPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(StaticIcons), nameof(StaticIcons.GetAttributeIcon));
            }

            [PatchPrefix]
            private static bool Prefix(Enum id, ref Sprite __result)
            {
                if (!(id is EHealthFactorType type) || !Sources.TryGetValue(type, out Func<Sprite> source))
                {
                    return true;
                }
                if (!Found.TryGetValue(type, out Sprite sprite) || sprite == null)
                {
                    Tries.TryGetValue(type, out int tries);
                    if (tries < 5)
                    {
                        Tries[type] = tries + 1;
                        sprite = source();
                        Found[type] = sprite;
                    }
                }
                __result = sprite;
                return false;
            }
        }
    }
}
