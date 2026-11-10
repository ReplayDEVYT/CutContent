using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
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

    public interface IRadiationSickness : IHealthEffect
    {
    }

    public interface IToxicBuildup : IHealthEffect
    {
    }

    public interface IHot : IHealthEffect
    {
    }

    public interface ICold : IHealthEffect
    {
    }

    public interface IOverheating : IHealthEffect
    {
    }

    public interface IHighBloodPressure : IHealthEffect
    {
    }

    public interface IWet : IHealthEffect
    {
    }

    public interface IStressed : IHealthEffect
    {
    }

    public interface IOverdose : IHealthEffect
    {
    }

    public interface ILowBloodPressure : IHealthEffect
    {
    }

    public class Adrenaline : ActiveHealthController.Effect, IAdrenaline, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Keeps you from passing out", "Clears dizziness");
    }

    public class Dizzy : ActiveHealthController.Effect, IDizzy, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Narrows your vision", "Turns into passing out if it keeps building", "Builds from pain, bleeding, fractures and low health");
    }

    public class Unconscious : ActiveHealthController.Effect, IUnconscious, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "You can't move or act", "Time passes while you're out");
    }

    public class Agony : ActiveHealthController.Effect, IAgony, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Shaking hands", "Normal painkillers don't help this limb", "Builds dizziness");
    }

    public class Shock : ActiveHealthController.Effect, IShock, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Dizziness builds very fast", "Caused by agony in two or more limbs");
    }

    public class RadiationSickness : ActiveHealthController.Effect, IRadiationSickness, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Damages your whole body over time", "Hurts more the higher your radiation is", "Death at 100 radiation", "Ends once radiation drops below 7.5");
    }

    public class ToxicBuildup : ActiveHealthController.Effect, IToxicBuildup, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Above 25: damages your stomach", "Above 50: drains energy and hydration", "Death at 100 toxicity");
    }

    public class Hot : ActiveHealthController.Effect, IHot, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Hydration -0.6/min", "Stamina drains 35% faster", "Fatigue drains energy twice as fast");
    }

    public class Cold : ActiveHealthController.Effect, ICold, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Shaking hands", "Movement speed capped at 85%", "Energy -0.6/min");
    }

    public class Overheating : ActiveHealthController.Effect, IOverheating, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Tunnel vision", "Energy -1.5/min, hydration -1.5/min", "Damages your whole body until you cool down", "Stamina drains 60% faster");
    }

    public class Overdose : ActiveHealthController.Effect, IOverdose, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "You lose consciousness fast", "Damages your thorax", "Arm and leg stamina capped at 50%", "Heavy breathing", "Worse past 80: whole-body damage, limping, slower movement, severe tremors, tunnel vision, pain and slumping", "Fatal at 100", "The xTG-12 antidote clears it");
    }

    public class Stressed : ActiveHealthController.Effect, IStressed, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Shaking hands and heavy breathing", "You may call out when hurt, reloading or when your weapon jams", "You may shout back at enemies taunting you", "Builds up from being shot at, hit or near death in a fight", "Stress Resistance makes it rarer and weaker");
    }

    public class Wet : ActiveHealthController.Effect, IWet, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Keeps cooling you down until you dry off", "Water soaks you through, heavy rain slowly soaks you outside", "Dries faster indoors, by a fire and in the heat");
    }

    public class HighBloodPressure : ActiveHealthController.Effect, IHighBloodPressure, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Head pain and shaking hands", "Slowly damages your thorax", "Arm and leg stamina capped at 75% and stops recovering", "Losing consciousness while it is severe tears your body apart");
    }

    public class LowBloodPressure : ActiveHealthController.Effect, ILowBloodPressure, IEffectTriggersUIPanel
    {
        public override EffectDescription[] DisplayableVariations => CustomEffects.Describe(this, "Tunnel vision", "You lose consciousness quickly", "You can't stand fully upright", "Losing consciousness while it is severe tears your body apart");
    }

    internal static class CustomEffects
    {
        private static readonly List<KeyValuePair<Type, Type>> Icons = new List<KeyValuePair<Type, Type>>
        {
            new KeyValuePair<Type, Type>(typeof(IAdrenaline), typeof(IBerserk)),
            new KeyValuePair<Type, Type>(typeof(IUnconscious), typeof(IMisfireEffect)),
            new KeyValuePair<Type, Type>(typeof(IShock), typeof(IMisfireEffect)),
            new KeyValuePair<Type, Type>(typeof(IRadiationSickness), typeof(ILethalIntoxication)),
            new KeyValuePair<Type, Type>(typeof(IToxicBuildup), typeof(IIntoxication))
        };

        public const EBodyPart Any = EBodyPart.Common;

        public static EffectDescription[] Describe(IHealthEffect effect, params string[] lines)
        {
            var tips = new List<SimpleBuffDescription>
            {
                new BuffDescription("<b>" + HealthHelper.EffectName(effect) + "</b>", () => effect.WholeTime, effect.OverallDuration)
            };
            foreach (string line in lines)
            {
                tips.Add(new SimpleBuffDescription(line));
            }
            return new[] { new EffectDescription(effect, true, tips) };
        }

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
        public static EffectRegistration Radiation { get; private set; }
        public static EffectRegistration Toxicity { get; private set; }
        public static EffectRegistration Hot { get; private set; }
        public static EffectRegistration Cold { get; private set; }
        public static EffectRegistration Overheating { get; private set; }
        public static EffectRegistration HighBloodPressure { get; private set; }
        public static EffectRegistration Wet { get; private set; }
        public static EffectRegistration Stressed { get; private set; }
        public static EffectRegistration Overdose { get; private set; }
        public static EffectRegistration LowBloodPressure { get; private set; }

        public static void Enable()
        {
            Adrenaline = Register<Adrenaline>("Adrenaline", typeof(Network.Adrenaline));
            Dizzy = Register<Dizzy>("Dizzy", typeof(Network.Dizzy), true, LoadSprite("effect_disorientation.png"));
            Unconscious = Register<Unconscious>("Unconscious", typeof(Network.Unconscious));
            Agony = Register<Agony>("Agony", typeof(Network.Agony), false, LoadSprite("agony.png"));
            Shock = Register<Shock>("Shock", typeof(Network.Shock));
            Radiation = Register<RadiationSickness>("Radiation", typeof(Network.RadiationSickness), false);
            Toxicity = Register<ToxicBuildup>("Toxicity", typeof(Network.ToxicBuildup), false);
            Sprite hot = LoadSprite("effect_hot.png");
            Hot = Register<Hot>("Hot", typeof(Network.Hot), false, hot);
            Cold = Register<Cold>("Cold", typeof(Network.Cold), false, LoadSprite("effect_cold.png"));
            Overheating = Register<Overheating>("Overheating", typeof(Network.Overheating), false, LoadSprite("effect_overheating.png") ?? hot);
            HealthHelper._typesOfEffects[typeof(IRadiationSickness)] = EDamageEffectType.RadExposure;
            HealthHelper._typesOfEffects[typeof(IToxicBuildup)] = EDamageEffectType.Intoxication;
            new IconPatch().Enable();
            new TunnelVisionPatch().Enable();
            Stressed = Register<Stressed>("Stressed", typeof(Network.Stressed), false, Icon("effect_stress.png"));
            Overdose = Register<Overdose>("Overdose", typeof(Network.Overdose), false, Icon("effect_overpk.png"));
            Sprite wet = LoadSprite("effect_wet.png");
            Wet = Register<Wet>("Wet", typeof(Network.Wet), false, wet);
            if (wet == null)
            {
                Icons.Add(new KeyValuePair<Type, Type>(typeof(IWet), typeof(IFrostbite)));
            }
            Sprite high = LoadSprite("effect_highpressure.png");
            Sprite low = LoadSprite("effect_lowpressure.png");
            HighBloodPressure = Register<HighBloodPressure>("High Blood Pressure", typeof(Network.HighBloodPressure), false, high);
            LowBloodPressure = Register<LowBloodPressure>("Low Blood Pressure", typeof(Network.LowBloodPressure), false, low);
            if (high == null)
            {
                Icons.Add(new KeyValuePair<Type, Type>(typeof(IHighBloodPressure), typeof(IStimulatorDebuff)));
            }
            if (low == null)
            {
                Icons.Add(new KeyValuePair<Type, Type>(typeof(ILowBloodPressure), typeof(ITunnelVision)));
            }
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
                            && accumulator.ValidTypes.Contains(typeof(ITunnelVision)))
                        {
                            accumulator.ValidTypes = accumulator.ValidTypes.Concat(new[] { typeof(IDizzy), typeof(IOverheating), typeof(ILowBloodPressure) }.Where(t => !accumulator.ValidTypes.Contains(t))).ToArray();
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

        public static bool HasIcon(Type type)
        {
            Dictionary<Type, Sprite> table = LoadedTable();
            return table != null && table.TryGetValue(type, out Sprite sprite) && sprite != null;
        }

        private static Dictionary<Type, Sprite> LoadedTable()
        {
            var settings = AccessTools.Field(typeof(EFTHardSettings), "_instance")?.GetValue(null) as EFTHardSettings;
            return settings?.StaticIcons?.EffectIcons?.EffectIcons;
        }

        public static void EnsureNamedIcons()
        {
            CopyIcons(LoadedTable());
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

        private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

        public static Sprite Icon(string file)
        {
            if (!IconCache.TryGetValue(file, out Sprite sprite) || sprite == null)
            {
                sprite = LoadSprite(file);
                IconCache[file] = sprite;
            }
            return sprite;
        }

        private static Sprite LoadSprite(string file)
        {
            return LoadSprite(file, true);
        }

        private static Sprite LoadSprite(string file, bool warnIfMissing)
        {
            try
            {
                using (Stream stream = typeof(Plugin).Assembly.GetManifestResourceStream("ReClassic.Resources." + file))
                {
                    if (stream == null)
                    {
                        if (warnIfMissing)
                        {
                            Plugin.LogSource.LogWarning($"[Effects] The icon {file} is missing from the mod.");
                        }
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

    public class RadiationSickness : NetworkHealthController.Effect, IRadiationSickness
    {
    }

    public class ToxicBuildup : NetworkHealthController.Effect, IToxicBuildup
    {
    }

    public class Hot : NetworkHealthController.Effect, IHot
    {
    }

    public class Cold : NetworkHealthController.Effect, ICold
    {
    }

    public class Overheating : NetworkHealthController.Effect, IOverheating
    {
    }

    public class Overdose : NetworkHealthController.Effect, IOverdose
    {
    }

    public class Stressed : NetworkHealthController.Effect, IStressed
    {
    }

    public class Wet : NetworkHealthController.Effect, IWet
    {
    }

    public class HighBloodPressure : NetworkHealthController.Effect, IHighBloodPressure
    {
    }

    public class LowBloodPressure : NetworkHealthController.Effect, ILowBloodPressure
    {
    }
}
