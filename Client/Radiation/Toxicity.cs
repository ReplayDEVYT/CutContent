using BepInEx.Configuration;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace mizmii.ReClassic.Radiation
{
    internal static class Toxicity
    {
        private const string Section = "Toxicity";
        private const float FoodChance = 0.25f;
        public const float ItemAmount = 10f;
        public const float ItemSeconds = 240f;

        private static readonly HashSet<string> Safe = new HashSet<string>
        {
            "590c5f0d86f77413997acfab",
            "590c5d4b86f774784e1b9c45",
            "60098b1705871270cd5352a1",
            "5d1b33a686f7742523398398",
            "575146b724597720a27126d5",
            "544fb62a4bdc2dfb738b4568"
        };

        private static readonly Dictionary<string, float> StimChance = new Dictionary<string, float>
        {
            ["5c0e531286f7747fa54205c2"] = 0.15f,
            ["5c0e531d86f7747fa23f4d42"] = 0.2f,
            ["5c0e534186f7747fa1419867"] = 0.15f,
            ["5ed515c8d380ab312177c0fa"] = 0.2f,
            ["5ed515e03a40a50460332579"] = 0.15f,
            ["5ed515ece452db0eb56fc028"] = 0.2f,
            ["5ed5160a87bb8443d10680b5"] = 0.1f,
            ["5ed51652f6c34d2cc26336a1"] = 0.25f,
            ["5ed5166ad380ab312177c100"] = 0.35f,
            ["5fca13ca637ee0341a484f46"] = 0.2f,
            ["637b60c3b7afa97bfc3d7001"] = 0.35f,
            ["637b612fb7afa97bfc3d7005"] = 0.25f,
            ["637b6179104668754b72f8f5"] = 0.15f,
            ["637b620db7afa97bfc3d7009"] = 0.15f,
            ["66507eabf5ddb0818b085b68"] = 0.2f
        };

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<float> DamageScale;
        public static ConfigEntry<bool> ScreenEffect;

        public static void Enable(ConfigFile config)
        {
            Enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "Health", "Toxicity", true,
                "Brings back the Poisoning value in your health panel. Most food and drink has a chance (shown on its inspect screen, usually 25%) of adding 10 toxicity over 4 minutes, and so do some stimulants. MREs, Iskra rations, EWR, Superwater, milk and army pineapple juice are always safe. Chemical zones raise it too. Over 25 your vision starts to sour and you take slow damage, and at 50 or more you also lose energy and hydration. It drops by 0.5 a minute on its own, medkits that treat poisoning take some off, and the xTG-12 antidote clears it.",
                Section, "Enabled");
            DamageScale = config.Bind(Section, "Damage Scale", 1f, new ConfigDescription("How much high toxicity hurts and drains you. 1 is the default.", new AcceptableValueRange<float>(0f, 3f)));
            ScreenEffect = config.Bind(Section, "Screen Effect", true, "Over 25 toxicity, a light touch of the old Halloween infection effect creeps over your vision.");
            ConfigHelper.Requires(Enabled, DamageScale, ScreenEffect);
            new AttributePatch().Enable();
        }

        public static float ChanceFor(Item item)
        {
            if (item == null)
            {
                return 0f;
            }
            string id = item.TemplateId.ToString();
            if (item is Stimulator)
            {
                return StimChance.TryGetValue(id, out float chance) ? chance : 0f;
            }
            if (item is Food || item is Drink)
            {
                return Safe.Contains(id) ? 0f : FoodChance;
            }
            return 0f;
        }

        public static void Consumed(Item item)
        {
            if (!Enabled.Value)
            {
                return;
            }
            float chance = ChanceFor(item);
            if (chance > 0f && UnityEngine.Random.value < chance)
            {
                Meters.Toxicity.Over(ItemAmount, ItemSeconds);
            }
        }

        private class AttributePatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Constructor(typeof(HealthEffectsComponent), new[] { typeof(Item), typeof(IHealthEffectsComponentTemplate) });
            }

            [PatchPostfix]
            private static void Postfix(HealthEffectsComponent __instance, Item item)
            {
                try
                {
                    if (item == null)
                    {
                        return;
                    }
                    string id = item.TemplateId.ToString();
                    float chance = ChanceFor(item);
                    if (chance > 0f && Enabled.Value)
                    {
                        Add(item, EHealthFactorType.Poisoning, "Toxicity chance", (chance * 100f).ToString("0") + "%", chance, true, true);
                    }
                    var effects = __instance.DamageEffects;
                    if (Enabled.Value && (id == Meters.Antidote || (effects != null && effects.ContainsKey(EDamageEffectType.Intoxication))))
                    {
                        GameRow(item, EDamageEffectType.Intoxication, "REMOVES TOXICATION", Meters.ToxicityRelief(id));
                    }
                    if (RadiationZones.Enabled != null && RadiationZones.Enabled.Value && effects != null && effects.TryGetValue(EDamageEffectType.RadExposure, out var cure))
                    {
                        string relief = Meters.RadiationRelief(id, cure.Duration);
                        GameRow(item, EDamageEffectType.RadExposure, "REMOVES RADIATION EXPOSURE", relief);
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Toxicity] Could not add the toxicity details to an item: {e.Message}");
                }
            }

            private const string Good = "#54C1FF";

            public static void GameRow(Item item, EDamageEffectType id, string name, string value)
            {
                bool signed = value.StartsWith("-", StringComparison.Ordinal);
                string shown = signed ? value : "<color=" + Good + ">" + value + "</color>";
                ItemAttribute row = null;
                foreach (ItemAttribute attribute in item.Attributes)
                {
                    if (attribute.Id is EDamageEffectType existing && existing == id)
                    {
                        row = attribute;
                        break;
                    }
                }
                if (row == null)
                {
                    row = new ItemAttribute(id)
                    {
                        Name = name,
                        DisplayType = () => EItemAttributeDisplayType.Compact
                    };
                    item.Attributes.Add(row);
                }
                row.StringValue = () => shown;
                row.FullStringValue = () => name.Localized() + "\n" + value;
                if (signed)
                {
                    row.Base = () => -1f;
                    row.LessIsGood = true;
                    row.LabelVariations = EItemAttributeLabelVariations.Colored;
                }
            }

            private static void Add(Item item, Enum id, string name, string value, float baseValue, bool lessIsGood, bool colored = false)
            {
                item.Attributes.Add(new ItemAttribute(id)
                {
                    Name = name,
                    DisplayNameFunc = () => name,
                    Base = () => baseValue,
                    StringValue = () => value,
                    FullStringValue = () => name + " " + value,
                    DisplayType = () => EItemAttributeDisplayType.Compact,
                    LessIsGood = lessIsGood,
                    LabelVariations = colored ? EItemAttributeLabelVariations.Colored : EItemAttributeLabelVariations.None
                });
            }
        }
    }
}
