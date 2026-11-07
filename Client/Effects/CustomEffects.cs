using EFT;
using EFT.HealthSystem;
using EFT.UI;
using EFTEffectManager;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Effects
{
    public interface IAdrenaline : IHealthEffect
    {
    }

    public interface IDizzy : IHealthEffect
    {
    }

    public interface IUnconscious : IHealthEffect
    {
    }

    public interface IAgony : IHealthEffect
    {
    }

    public interface IShock : IHealthEffect
    {
    }

    public class Adrenaline : ActiveHealthController.Effect, IAdrenaline, IEffectTriggersUIPanel
    {
    }

    public class Dizzy : ActiveHealthController.Effect, IDizzy, IEffectTriggersUIPanel
    {
    }

    public class Unconscious : ActiveHealthController.Effect, IUnconscious, IEffectTriggersUIPanel
    {
    }

    public class Agony : ActiveHealthController.Effect, IAgony, IEffectTriggersUIPanel
    {
    }

    public class Shock : ActiveHealthController.Effect, IShock, IEffectTriggersUIPanel
    {
    }

    internal static class CustomEffects
    {
        private static readonly KeyValuePair<Type, Type>[] Icons =
        {
            new KeyValuePair<Type, Type>(typeof(IAdrenaline), typeof(IBerserk)),
            new KeyValuePair<Type, Type>(typeof(IDizzy), typeof(IContusion)),
            new KeyValuePair<Type, Type>(typeof(IUnconscious), typeof(IMisfireEffect)),
            new KeyValuePair<Type, Type>(typeof(IShock), typeof(IMisfireEffect))
        };

        public const EBodyPart Any = EBodyPart.Common;

        private static readonly EBodyPart[] Parts =
        {
            EBodyPart.Head, EBodyPart.Chest, EBodyPart.Stomach, EBodyPart.LeftArm, EBodyPart.RightArm, EBodyPart.LeftLeg, EBodyPart.RightLeg
        };

        public static EBodyPart PartFor(IHealthController health)
        {
            if (health != null)
            {
                foreach (EBodyPart part in Parts)
                {
                    if (!health.IsBodyPartDestroyed(part))
                    {
                        return part;
                    }
                }
            }
            return EBodyPart.Head;
        }

        public static EffectRegistration Adrenaline { get; private set; }
        public static EffectRegistration Dizzy { get; private set; }
        public static EffectRegistration Unconscious { get; private set; }
        public static EffectRegistration Agony { get; private set; }
        public static EffectRegistration Shock { get; private set; }

        public static void Enable()
        {
            Adrenaline = Register<Adrenaline>("Adrenaline", typeof(Network.Adrenaline));
            Dizzy = Register<Dizzy>("Dizzy", typeof(Network.Dizzy));
            Unconscious = Register<Unconscious>("Unconscious", typeof(Network.Unconscious));
            Agony = Register<Agony>("Agony", typeof(Network.Agony), false, LoadSprite("agony.png"));
            Shock = Register<Shock>("Shock", typeof(Network.Shock));
            new IconPatch().Enable();
            new TunnelVisionPatch().Enable();
            CopyIcons(LoadedTable());
        }

        private class TunnelVisionPatch : ModulePatch
        {
            private static readonly AccessTools.FieldRef<EffectsController, List<EffectsController.EffectAccumulator>> Accumulators =
                AccessTools.FieldRefAccess<EffectsController, List<EffectsController.EffectAccumulator>>("_effectAccumulators");

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(EffectsController), nameof(EffectsController.Init));
            }

            [PatchPostfix]
            private static void Postfix(EffectsController __instance)
            {
                try
                {
                    foreach (EffectsController.EffectAccumulator accumulator in Accumulators(__instance))
                    {
                        if (accumulator is EffectsController.CC_FastVignetteAccumulator && accumulator.ValidTypes != null
                            && accumulator.ValidTypes.Contains(typeof(ITunnelVision)) && !accumulator.ValidTypes.Contains(typeof(IDizzy)))
                        {
                            accumulator.ValidTypes = accumulator.ValidTypes.Concat(new[] { typeof(IDizzy) }).ToArray();
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Effects] Could not give Dizzy its tunnel vision: {e}");
                }
            }
        }

        private static EffectRegistration Register<T>(string name, Type network, bool removeOnDestroyed = true, Sprite icon = null) where T : ActiveHealthController.Effect, new()
        {
            return EffectsManager.RegisterEffect<T>(typeof(Plugin).Assembly, new EffectOptions
            {
                DisplayName = name,
                NetworkEffectType = network,
                PersistAcrossRaids = false,
                RemoveOnBodyPartDestroyed = removeOnDestroyed,
                IconSprite = icon
            });
        }

        private static Dictionary<Type, Sprite> LoadedTable()
        {
            var settings = AccessTools.Field(typeof(EFTHardSettings), "_instance")?.GetValue(null) as EFTHardSettings;
            return settings?.StaticIcons?.EffectIcons?.EffectIcons;
        }

        private static void CopyIcons(Dictionary<Type, Sprite> table)
        {
            if (table == null)
            {
                return;
            }
            foreach (KeyValuePair<Type, Type> pair in Icons)
            {
                if (table.TryGetValue(pair.Value, out Sprite sprite) && sprite != null)
                {
                    table[pair.Key] = sprite;
                }
                else
                {
                    Plugin.LogSource.LogWarning($"[Effects] The game has no {pair.Value.Name} icon to reuse for {pair.Key.Name}.");
                }
            }
        }

        private static Sprite LoadSprite(string file)
        {
            try
            {
                using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream("ReClassic.Resources." + file))
                {
                    if (stream == null)
                    {
                        Plugin.LogSource.LogWarning($"[Effects] The icon {file} is missing from the mod.");
                        return null;
                    }
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (!texture.LoadImage(memory.ToArray()))
                        {
                            UnityEngine.Object.Destroy(texture);
                            return null;
                        }
                        texture.wrapMode = TextureWrapMode.Clamp;
                        texture.filterMode = FilterMode.Bilinear;
                        UnityEngine.Object.DontDestroyOnLoad(texture);
                        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = Path.GetFileNameWithoutExtension(file);
                        UnityEngine.Object.DontDestroyOnLoad(sprite);
                        return sprite;
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Effects] Could not load the icon {file}: {e}");
                return null;
            }
        }

        private class IconPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return typeof(StaticIcons.EffectSprites)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(m => m.Name.EndsWith("OnAfterDeserialize", StringComparison.Ordinal));
            }

            [PatchPostfix]
            private static void Postfix(StaticIcons.EffectSprites __instance)
            {
                CopyIcons(__instance.EffectIcons);
            }
        }
    }
}

namespace mizmii.ReClassic.Effects.Network
{
    public class Adrenaline : NetworkHealthController.Effect, IAdrenaline
    {
    }

    public class Dizzy : NetworkHealthController.Effect, IDizzy
    {
    }

    public class Unconscious : NetworkHealthController.Effect, IUnconscious
    {
    }

    public class Agony : NetworkHealthController.Effect, IAgony
    {
    }

    public class Shock : NetworkHealthController.Effect, IShock
    {
    }
}
