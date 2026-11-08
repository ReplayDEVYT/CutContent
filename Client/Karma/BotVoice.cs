using EFT;
using System;

namespace mizmii.ReClassic.Karma
{
    internal static class BotVoice
    {
        public static bool Say(Player bot, EPhraseTrigger phrase, bool combat = false, params EPhraseTrigger[] fallbacks)
        {
            if (bot?.Speaker == null || bot.HealthController == null || !bot.HealthController.IsAlive)
            {
                return false;
            }
            if (!bot.IsAI)
            {
                bot.Say(phrase, true);
                return true;
            }
            try
            {
                if (phrase == EPhraseTrigger.Cooperation)
                {
                    bot.ShowGesture(EInteraction.FriendlyGesture);
                }
            }
            catch (Exception)
            {
            }
            int members = bot.AIData?.BotOwner?.BotsGroup?.MembersCount ?? 1;
            ETagStatus mask = bot.HealthStatus | (members > 1 ? ETagStatus.Coop : ETagStatus.Solo) | (combat ? ETagStatus.Combat : ETagStatus.Unaware);
            if (Play(bot, phrase, mask))
            {
                return true;
            }
            if (fallbacks != null)
            {
                foreach (EPhraseTrigger fallback in fallbacks)
                {
                    if (Play(bot, fallback, mask))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool Play(Player bot, EPhraseTrigger phrase, ETagStatus mask)
        {
            try
            {
                return bot.Speaker.Play(phrase, mask, true) != null;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[PMC Karma] {bot.Profile?.Nickname} could not say {phrase}: {e.Message}");
                return false;
            }
        }
    }
}
