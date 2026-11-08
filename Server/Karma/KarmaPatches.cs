using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Ragfair;

namespace ReClassic.Karma;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class KarmaPatches(ISptLogger<KarmaPatches> logger, KarmaService karma) : IOnLoad
{
    private static readonly HashSet<MongoId> Currencies = new() { Money.ROUBLES, Money.DOLLARS, Money.EUROS };
    private static bool _patched;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        karma.EnsureLoaded();
        if (_patched)
        {
            return Task.CompletedTask;
        }
        _patched = true;
        var harmony = new Harmony("com.mizmii.reclassic.karma");
        Patch(harmony, AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort)), null, nameof(AssortPostfix));
        Patch(harmony, AccessTools.Method(typeof(RagfairTaxService), nameof(RagfairTaxService.CalculateTax)), null, nameof(TaxPostfix));
        Patch(harmony, AccessTools.Method(typeof(RewardHelper), nameof(RewardHelper.ApplyRewards)), nameof(RewardsPrefix), null);
        return Task.CompletedTask;
    }

    private void Patch(Harmony harmony, System.Reflection.MethodInfo? target, string? prefix, string? postfix)
    {
        if (target == null)
        {
            logger.Error($"[Re-Classic] Karma could not find a method to patch ({prefix ?? postfix}).");
            return;
        }
        try
        {
            harmony.Patch(target,
                prefix == null ? null : new HarmonyMethod(typeof(KarmaPatches), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(KarmaPatches), postfix));
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Karma could not patch {target.DeclaringType?.Name}.{target.Name}: {e.Message}");
        }
    }

    private static void AssortPostfix(MongoId sessionId, MongoId traderId, TraderAssort __result)
    {
        KarmaService? service = KarmaService.Instance;
        if (service == null || __result?.BarterScheme == null || !service.Config.Enabled)
        {
            return;
        }
        double multiplier = service.BuyMultiplier(sessionId.ToString(), traderId);
        if (Math.Abs(multiplier - 1) < 0.0001)
        {
            return;
        }
        foreach (List<List<BarterScheme>> options in __result.BarterScheme.Values)
        {
            if (options == null)
            {
                continue;
            }
            foreach (List<BarterScheme> option in options)
            {
                if (option == null)
                {
                    continue;
                }
                for (int i = 0; i < option.Count; i++)
                {
                    BarterScheme scheme = option[i];
                    if (scheme?.Count == null || !Currencies.Contains(scheme.Template))
                    {
                        continue;
                    }
                    option[i] = scheme with { Count = Math.Max(1, Math.Round(scheme.Count.Value * multiplier)) };
                }
            }
        }
    }

    private static void TaxPostfix(PmcData pmcData, ref double __result)
    {
        KarmaService? service = KarmaService.Instance;
        if (service == null || pmcData == null || !service.Config.Enabled)
        {
            return;
        }
        string id = (pmcData.SessionId ?? pmcData.Id)?.ToString() ?? "";
        __result *= service.FleaTax(service.Get(id));
    }

    private static void RewardsPrefix(ref IEnumerable<Reward> rewards, string rewardSource, SptProfile fullProfile)
    {
        KarmaService? service = KarmaService.Instance;
        if (service == null || rewards == null || rewardSource != "unlockedInGame" || !service.Config.Enabled)
        {
            return;
        }
        string id = fullProfile?.ProfileInfo?.ProfileId?.ToString() ?? "";
        double multiplier = service.QuestExperience(service.Get(id));
        if (Math.Abs(multiplier - 1) < 0.0001)
        {
            return;
        }
        rewards = rewards.Select(reward =>
            reward != null && reward.Value.HasValue && (reward.Type == RewardType.Experience || reward.Type == RewardType.Skill)
                ? reward with { Value = Math.Round(reward.Value.Value * multiplier) }
                : reward).ToList();
    }
}
