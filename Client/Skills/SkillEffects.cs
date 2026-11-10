using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mizmii.ReClassic.Skills
{
    internal static class SkillEffects
    {
        private sealed class Bonus
        {
            public EBuffId Id;
            public ESkillId Skill;
            public float PerLevel;
            public EBuffId IconFrom;
            public string Text;
        }

        private static readonly Bonus[] Bonuses =
        {
            new Bonus { Id = (EBuffId)9401, Skill = ESkillId.Health, PerLevel = 0.01f, IconFrom = EBuffId.HealthEnergy, Text = "Stimulants change your blood pressure [{0}] less" },
            new Bonus { Id = (EBuffId)9402, Skill = ESkillId.Health, PerLevel = 0.02f, IconFrom = EBuffId.HealthHydration, Text = "Radiation and toxicity leave your body [{0}] faster" },
            new Bonus { Id = (EBuffId)9403, Skill = ESkillId.StressResistance, PerLevel = 0.01f, IconFrom = EBuffId.StressPainChance, Text = "Dizziness builds up [{0}] slower" },
            new Bonus { Id = (EBuffId)9404, Skill = ESkillId.StressResistance, PerLevel = 0.01f, IconFrom = EBuffId.StressTremor, Text = "Injuries take [{0}] longer to turn into Agony" },
            new Bonus { Id = (EBuffId)9405, Skill = ESkillId.StressResistance, PerLevel = 0.012f, IconFrom = EBuffId.StressPainChance, Text = "Stress builds up [{0}] slower and is less severe" },
            new Bonus { Id = (EBuffId)9406, Skill = ESkillId.Endurance, PerLevel = 0.02f, IconFrom = EBuffId.EnduranceBuffRestorationTimeRed, Text = "Body temperature returns to normal [{0}] faster" }
        };

        public const EBuffId StimPressure = (EBuffId)9401;
        public const EBuffId Purge = (EBuffId)9402;
        public const EBuffId Dizziness = (EBuffId)9403;
        public const EBuffId Agony = (EBuffId)9404;
        public const EBuffId StressBuild = (EBuffId)9405;
        public const EBuffId Temperature = (EBuffId)9406;

        private static readonly AccessTools.FieldRef<Skill, SkillManager.Buff[]> BuffsOf = AccessTools.FieldRefAccess<Skill, SkillManager.Buff[]>("Buffs");
        private static readonly Dictionary<EBuffId, Bonus> ById = new Dictionary<EBuffId, Bonus>();

        public static void Enable()
        {
            foreach (Bonus bonus in Bonuses)
            {
                ById[bonus.Id] = bonus;
            }
            new SkillSetupPatch().Enable();
            new DescriptionPatch().Enable();
            new IconPatch().Enable();
        }

        public static float Value(EBuffId id)
        {
            if (!ById.TryGetValue(id, out Bonus bonus))
            {
                return 0f;
            }
            SkillManager skills = Singleton<GameWorld>.Instance?.MainPlayer?.Skills;
            Skill skill = skills != null ? Find(skills, bonus.Skill) : null;
            if (skill == null)
            {
                return 0f;
            }
            return bonus.PerLevel * Mathf.Min(skill.SummaryLevel, 50);
        }

        private static Skill Find(SkillManager skills, ESkillId id)
        {
            switch (id)
            {
                case ESkillId.Health: return skills.Health;
                case ESkillId.StressResistance: return skills.StressResistance;
                case ESkillId.Endurance: return skills.Endurance;
                default: return null;
            }
        }

        private class SkillSetupPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(SkillManager), "method_3");
            }

            [PatchPostfix]
            private static void Postfix(SkillManager __instance)
            {
                try
                {
                    foreach (Bonus bonus in Bonuses)
                    {
                        Skill skill = Find(__instance, bonus.Skill);
                        if (skill == null)
                        {
                            continue;
                        }
                        SkillManager.Buff[] buffs = BuffsOf(skill);
                        bool present = false;
                        foreach (SkillManager.Buff buff in buffs)
                        {
                            present |= buff.Id == bonus.Id;
                        }
                        if (present)
                        {
                            continue;
                        }
                        var added = new SkillManager.FloatBuff { Id = bonus.Id }.PerLevel(bonus.PerLevel);
                        var list = new List<SkillManager.Buff>(buffs) { added };
                        BuffsOf(skill) = list.ToArray();
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogError($"[Skills] Could not add the new skill effects: {e}");
                }
            }
        }

        private class DescriptionPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BuffIcon), nameof(BuffIcon.GetBuffDescription));
            }

            [PatchPrefix]
            private static bool Prefix(SkillManager.Buff buff, ref string __result)
            {
                if (buff == null || !ById.TryGetValue(buff.Id, out Bonus bonus))
                {
                    return true;
                }
                float value = buff.ValueObj is float f ? f : 0f;
                __result = bonus.Text.Replace("{0}", (value * 100f).ToString("0.#") + "%").Replace("[", "<color=#54c1ff>").Replace("]", "</color>");
                return false;
            }
        }

        private class IconPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BuffIcon), nameof(BuffIcon.LoadBuffIcon));
            }

            [PatchPrefix]
            private static bool Prefix(SkillManager.Buff buff, ref Sprite __result)
            {
                if (buff == null || !ById.TryGetValue(buff.Id, out Bonus bonus))
                {
                    return true;
                }
                Dictionary<EBuffId, Sprite> sprites = EFTHardSettings.Instance?.StaticIcons?.BuffIdSprites;
                __result = sprites != null && sprites.TryGetValue(bonus.IconFrom, out Sprite sprite) ? sprite : null;
                return false;
            }
        }
    }
}
