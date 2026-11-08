using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using mizmii.ReClassic.Consciousness;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace mizmii.ReClassic.Karma
{
    internal static class PmcKarma
    {
        private const string Section = "PMC Karma";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _requests;
        private static ConfigEntry<float> _requestChance;
        private static ConfigEntry<int> _maxRequests;
        private static ConfigEntry<float> _lowKarmaChance;
        private static ConfigEntry<bool> _flee;
        private static ConfigEntry<float> _fleeChance;
        private static ConfigEntry<bool> _teammateKills;
        private static ConfigEntry<bool> _force;
        private static MethodInfo _personality;

        public static bool Enabled => _enabled != null && _enabled.Value;
        public static bool HelpRequestsEnabled => _requests.Value;
        public static float RequestChance => _requestChance.Value;
        public static int MaxRequests => _maxRequests.Value;
        public static float LowKarmaChance => _lowKarmaChance.Value;
        public static bool FleeEnabled => _flee.Value;
        public static float FleeChance => _fleeChance.Value;
        public static bool SainAvailable => Chainloader.PluginInfos.ContainsKey("me.sol.sain");
        public static bool TeammateKills => _teammateKills.Value;

        public static void Enable(ConfigFile config)
        {
            _enabled = ConfigHelper.Feature(config, FeatureCategory.Gameplay, "PMCs", "PMC Karma", true,
                "Your PMC earns or loses karma for how they treat other PMCs and bots. Low karma makes traders charge more, raises the flea market tax and lowers quest experience, but Fence likes you more. High karma does the opposite. Hover over a trader to see your karma. Needs the server mod.",
                Section, "Enabled");
            _requests = config.Bind(Section, "Help Requests", true,
                "PMCs that hear you may ask for help when they're out of supplies, out of ammo or just scared. Answer with the Cooperation or Roger voiceline or a thumbs up to team up, or with Fight to turn them away. Following you needs BigBrain.");
            _requestChance = config.Bind(Section, "Help Request Chance", 0.06f,
                ConfigHelper.Advanced("Base chance that a PMC who hears you asks for help. Running out of supplies or ammo, and being a Timmy or a Coward in SAIN, make it much more likely.", new AcceptableValueRange<float>(0f, 1f)));
            _maxRequests = config.Bind(Section, "Max Help Requests Per Raid", 2,
                ConfigHelper.Advanced("How many PMCs can ask for help in one raid.", new AcceptableValueRange<int>(0, 10)));
            _lowKarmaChance = config.Bind(Section, "Low Karma PMC Chance", 0.2f,
                ConfigHelper.Advanced("Only used without SAIN. With SAIN, Chad and GigaChad PMCs have low karma. Share of PMC bots that have low karma. They taunt you when they spot you, rarely ask for help, and killing them earns karma.", new AcceptableValueRange<float>(0f, 1f)));
            _flee = config.Bind(Section, "Timid PMCs Run Away", true,
                "Needs SAIN and BigBrain. Timmy and Coward PMCs may run away when you shoot them. You get a warning if you hit one while they're running, and killing them while they run costs a little karma.");
            _fleeChance = config.Bind(Section, "Run Away Chance", 0.35f,
                ConfigHelper.Advanced("Chance that a Timmy or Coward PMC runs away when you first hit them. They roll again once when they drop below half health.", new AcceptableValueRange<float>(0f, 1f)));
            _teammateKills = config.Bind(Section, "Teammate Kill Penalty", false,
                "Fika co-op: killing another player costs karma.");
            _force = config.Bind(Section + " - Debug", "Force Help Request", false,
                ConfigHelper.Advanced("Debug. Turn on during a raid to make the nearest PMC ask for help right away. In co-op only the host can use it. It turns itself back off."));
            _force.SettingChanged += (sender, args) =>
            {
                if (!_force.Value)
                {
                    return;
                }
                _force.Value = false;
                if (Enabled && Singleton<GameWorld>.Instance != null)
                {
                    HelpRequests.ForceRequest();
                }
            };

            HelpRequests.BrainAvailable = false;
            if (Chainloader.PluginInfos.ContainsKey("xyz.drakia.bigbrain"))
            {
                try
                {
                    RegisterBrain();
                    HelpRequests.BrainAvailable = true;
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not add the follow layer to BigBrain, PMCs you help won't follow you: {e.Message}");
                }
            }
            else
            {
                Plugin.LogSource.LogInfo("[PMC Karma] BigBrain isn't installed, PMCs you help won't follow you.");
            }

            KarmaTracker.Hook();
            new AddEnemyPatch().Enable();
            new GesturePatch().Enable();
            new ThrowItemPatch().Enable();
            new FenceSellPatch().Enable();
            new ScavSellPatch().Enable();
            new FleaTaxPatch().Enable();
            new TooltipPatch().Enable();
            new OverallStatsPatch().Enable();

            var driver = new GameObject("ReClassic_PmcKarma");
            UnityEngine.Object.DontDestroyOnLoad(driver);
            driver.hideFlags = HideFlags.HideAndDontSave;
            driver.AddComponent<KarmaDriver>();
        }

        private static void RegisterBrain()
        {
            KarmaBrain.Register();
        }

        public static string Personality(BotOwner owner)
        {
            if (owner == null)
            {
                return string.Empty;
            }
            try
            {
                if (_personality == null)
                {
                    Type type = AccessTools.TypeByName("SAIN.Interop.SAINExternal");
                    _personality = type == null ? null : AccessTools.Method(type, "GetPersonality");
                    if (_personality == null)
                    {
                        return string.Empty;
                    }
                }
                return _personality.Invoke(null, new object[] { owner }) as string ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private class AddEnemyPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.AddEnemy));
            }

            [PatchPrefix]
            private static bool Prefix(BotsGroup __instance, IPlayer person, ref bool __result)
            {
                if (HelpRequests.AllowEnemy(__instance, person))
                {
                    return true;
                }
                __result = false;
                return false;
            }
        }

        private class GesturePatch : ModulePatch
        {
            private static readonly AccessTools.FieldRef<MovementContext, Player> PlayerOf = AccessTools.FieldRefAccess<MovementContext, Player>("_player");

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(MovementContext), nameof(MovementContext.SetInteractInHands));
            }

            [PatchPostfix]
            private static void Postfix(MovementContext __instance, EInteraction interaction)
            {
                if (interaction == EInteraction.None || !Enabled)
                {
                    return;
                }
                HelpRequests.OnGesture(PlayerOf(__instance), interaction);
            }
        }

        private class ThrowItemPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.ThrowItem), new[]
                {
                    typeof(Item), typeof(IPlayer), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Vector3), typeof(bool), typeof(bool), typeof(float)
                });
            }

            [PatchPostfix]
            private static void Postfix(LootItem __result, Item item, IPlayer player, bool syncable)
            {
                if (syncable && Enabled)
                {
                    HelpRequests.OnDrop(__result, item, player);
                }
            }
        }

        private static Trader.ItemPrice? Scale(Trader.ItemPrice? price, double multiplier)
        {
            if (!price.HasValue || Math.Abs(multiplier - 1) < 0.0001)
            {
                return price;
            }
            return new Trader.ItemPrice(price.Value.CurrencyId, Math.Max(1, (int)Math.Floor(price.Value.Amount * multiplier)));
        }

        private class FenceSellPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Trader), nameof(Trader.GetUserItemPrice));
            }

            [PatchPostfix]
            private static void Postfix(Trader __instance, ref Trader.ItemPrice? __result)
            {
                if (__instance.Id == KarmaClient.FenceId)
                {
                    __result = Scale(__result, KarmaClient.FenceSell);
                }
            }
        }

        private class ScavSellPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(Trader), nameof(Trader.GetItemPriceOnScavSell));
            }

            [PatchPostfix]
            private static void Postfix(Trader __instance, ref int __result)
            {
                if (__instance.Id == KarmaClient.FenceId && __result > 0)
                {
                    __result = Math.Max(1, (int)Math.Floor(__result * KarmaClient.FenceSell));
                }
            }
        }

        private class FleaTaxPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(PriceCalculator), nameof(PriceCalculator.CalculateTaxPrice));
            }

            [PatchPostfix]
            private static void Postfix(ref double __result)
            {
                __result *= KarmaClient.FleaTax;
            }
        }

        private class TooltipPatch : ModulePatch
        {
            private const string LineName = "ReClassic Karma";

            protected override MethodBase GetTargetMethod()
            {
                return AccessTools.Method(typeof(TraderTooltip), nameof(TraderTooltip.Show));
            }

            [PatchPostfix]
            private static void Postfix(TraderTooltip __instance, Profile.TraderInfo traderInfo)
            {
                try
                {
                    KarmaClient.RefreshSoon();
                    CustomTextMeshProUGUI money = __instance._moneySpent;
                    if (money == null)
                    {
                        return;
                    }
                    Transform parent = money.transform.parent;
                    Transform existing = parent.Find(LineName);
                    if (!KarmaClient.Available || traderInfo == null || !traderInfo.Available)
                    {
                        existing?.gameObject.SetActive(false);
                        return;
                    }
                    CustomTextMeshProUGUI line;
                    if (existing == null)
                    {
                        line = UnityEngine.Object.Instantiate(money, parent);
                        line.name = LineName;
                        line.transform.SetSiblingIndex(money.transform.GetSiblingIndex() + 1);
                    }
                    else
                    {
                        line = existing.GetComponent<CustomTextMeshProUGUI>();
                    }
                    line.gameObject.SetActive(true);
                    ((TMP_Text)line).text = Text(traderInfo.Id);
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[PMC Karma] Could not show karma on the trader tooltip: {e.Message}");
                }
            }

            private static string Text(string traderId)
            {
                KarmaInfo info = KarmaClient.Info;
                string tier = $"<color={KarmaClient.TierColor(info.Tier)}>{info.Tier} ({info.Karma:0.#})</color>";
                var effects = new List<string>();
                if (traderId == KarmaClient.FenceId)
                {
                    if (Math.Abs(info.FencePrice - 1) > 0.005)
                    {
                        effects.Add("prices " + KarmaClient.Percent(info.FencePrice));
                    }
                    if (Math.Abs(info.FenceSell - 1) > 0.005)
                    {
                        effects.Add("pays " + KarmaClient.Percent(info.FenceSell));
                    }
                }
                else if (Math.Abs(info.TraderPrice - 1) > 0.005)
                {
                    effects.Add("prices " + KarmaClient.Percent(info.TraderPrice));
                }
                string suffix = effects.Count > 0 ? " <color=#747b7e>(" + string.Join(", ", effects) + ")</color>" : string.Empty;
                return "PMC karma: " + tier + suffix;
            }
        }
    }

    internal class OverallStatsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(StatInfoFactory), nameof(StatInfoFactory.AddStandingStats));
        }

        [PatchPostfix]
        private static void Postfix(StatInfoFactory __instance)
        {
            try
            {
                if (__instance._type != StatisticsSpawn.EStatisticsType.Overall || __instance._profile?.Info == null || __instance._statGroups == null)
                {
                    return;
                }
                if (!__instance._profile.Info.Side.CheckSide(EPlayerSideMask.Pmc))
                {
                    return;
                }
                KarmaClient.RefreshSoon();
                KarmaInfo info = KarmaClient.Info;
                if (!KarmaClient.Available || info.ProfileId != __instance._profile.Id)
                {
                    return;
                }
                var group = new EFT.UI.SessionEnd.SessionResultStatistics.StatGroup
                {
                    Caption = "PMC Karma",
                    Icon = __instance._staticIcons?.GetStatGroupIcon(EStatGroupId.StandingStats)
                };
                Add(group, "Karma", $"<color={KarmaClient.TierColor(info.Tier)}>{info.Karma:0.#}</color>");
                Add(group, "Standing", $"<color={KarmaClient.TierColor(info.Tier)}>{info.Tier}</color>");
                Add(group, "Range", $"{info.Minimum:0} to {info.Maximum:0}");
                Add(group, "Trader prices", KarmaClient.Percent(info.TraderPrice));
                Add(group, "Fence prices", KarmaClient.Percent(info.FencePrice));
                Add(group, "Fence pays", KarmaClient.Percent(info.FenceSell));
                Add(group, "Flea market tax", KarmaClient.Percent(info.FleaTax));
                Add(group, "Quest experience", KarmaClient.Percent(info.QuestExperience));
                __instance._statGroups.Add(group);
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] Could not add karma to the Overall tab: {e.Message}");
            }
        }

        private static void Add(EFT.UI.SessionEnd.SessionResultStatistics.StatGroup group, string caption, string value)
        {
            group.StatItems.Add(new ProfileStats.StatInfo { Caption = caption, Type = ProfileStats.EStatType.Number, Value = value });
        }
    }

    internal sealed class KarmaDriver : MonoBehaviour
    {
        private GameWorld _world;
        private GlobalEventDispatcher _dispatcher;
        private float _loadAt;
        private float _nextMenuRefresh;

        private void Update()
        {
            if (!PmcKarma.Enabled)
            {
                return;
            }
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world != _world)
            {
                _world = world;
                KarmaTracker.Reset();
                HelpRequests.Reset();
                Fleeing.Reset();
                Unsubscribe();
                _loadAt = world != null ? Time.time + 5f : 0f;
                if (world == null)
                {
                    _nextMenuRefresh = Time.realtimeSinceStartup + 3f;
                }
            }
            if (world == null)
            {
                if (Time.realtimeSinceStartup > _nextMenuRefresh && PatchConstants.BackEndSession != null)
                {
                    _nextMenuRefresh = Time.realtimeSinceStartup + 60f;
                    KarmaClient.Refresh();
                }
                return;
            }
            if (!Coop.IsServer)
            {
                return;
            }
            Subscribe();
            if (_loadAt > 0f && Time.time > _loadAt)
            {
                _loadAt = 0f;
                KarmaClient.Load(Patches.PlayerKind.Humans().Select(h => h.ProfileId));
            }
            KarmaTracker.Tick();
            HelpRequests.Tick();
            Fleeing.Tick();
        }

        private void Subscribe()
        {
            if (!Singleton<GlobalEventDispatcher>.Instantiated)
            {
                return;
            }
            GlobalEventDispatcher dispatcher = Singleton<GlobalEventDispatcher>.Instance;
            if (dispatcher == _dispatcher)
            {
                return;
            }
            Unsubscribe();
            _dispatcher = dispatcher;
            dispatcher.OnSoundPlayed += OnSound;
            dispatcher.OnPhraseSay += OnPhrase;
            dispatcher.OnBeingHit += OnHit;
        }

        private void Unsubscribe()
        {
            if (_dispatcher == null)
            {
                return;
            }
            try
            {
                _dispatcher.OnSoundPlayed -= OnSound;
                _dispatcher.OnPhraseSay -= OnPhrase;
                _dispatcher.OnBeingHit -= OnHit;
            }
            catch (Exception)
            {
            }
            _dispatcher = null;
        }

        private static void OnSound(IPlayer player, Vector3 position, float power, AISoundType type)
        {
            Fleeing.OnSound(player, type);
            HelpRequests.OnSound(player, position, power);
        }

        private static void OnPhrase(GlobalEventDispatcher.PhraseDelegateInfo info)
        {
            if (info != null)
            {
                HelpRequests.OnPhrase(info.PlayerRequester, info.phrase);
            }
        }

        private static void OnHit(DamageInfo damageInfo, Player victim)
        {
            KarmaTracker.OnHit(damageInfo, victim);
        }
    }
}
