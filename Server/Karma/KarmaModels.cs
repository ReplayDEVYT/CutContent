using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace ReClassic.Karma;

public class KarmaConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("minimum")]
    public double Minimum { get; set; } = -100;

    [JsonPropertyName("maximum")]
    public double Maximum { get; set; } = 100;

    [JsonPropertyName("neutralBand")]
    public double NeutralBand { get; set; } = 15;

    [JsonPropertyName("extremeThreshold")]
    public double ExtremeThreshold { get; set; } = 60;

    [JsonPropertyName("events")]
    public Dictionary<string, double> Events { get; set; } = new()
    {
        ["bossKill"] = 6,
        ["helpedPmc"] = 8,
        ["helpedOtherFaction"] = 12,
        ["lowKarmaPmcKill"] = 3,
        ["assistedNeutral"] = 2,
        ["raiderPair"] = 1,
        ["neutralKill"] = -5,
        ["betrayal"] = -20,
        ["teammateKill"] = -10,
        ["fleeingBeginnerKill"] = -4
    };

    [JsonPropertyName("traderPriceIncreaseAtLowest")]
    public double TraderPriceIncrease { get; set; } = 0.15;

    [JsonPropertyName("traderPriceDiscountAtHighest")]
    public double TraderPriceDiscount { get; set; } = 0.07;

    [JsonPropertyName("fencePriceDiscountAtLowest")]
    public double FencePriceDiscount { get; set; } = 0.2;

    [JsonPropertyName("fenceSellBonusAtLowest")]
    public double FenceSellBonus { get; set; } = 0.25;

    [JsonPropertyName("fenceSellPenaltyAtHighest")]
    public double FenceSellPenalty { get; set; } = 0.3;

    [JsonPropertyName("questExperiencePenaltyAtLowest")]
    public double QuestPenalty { get; set; } = 0.25;

    [JsonPropertyName("questExperienceBonusAtHighest")]
    public double QuestBonus { get; set; } = 0.2;

    [JsonPropertyName("fleaTaxIncreaseAtLowest")]
    public double FleaTaxIncrease { get; set; } = 0.4;

    [JsonPropertyName("fleaTaxDiscountAtHighest")]
    public double FleaTaxDiscount { get; set; } = 0.25;
}

public class KarmaChange
{
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("amount")]
    public double Amount { get; set; }
}

public class KarmaEntry
{
    [JsonPropertyName("value")]
    public double Value { get; set; }

    [JsonPropertyName("history")]
    public List<KarmaChange> History { get; set; } = new();
}

public class KarmaState
{
    [JsonPropertyName("profiles")]
    public Dictionary<string, KarmaEntry> Profiles { get; set; } = new();
}

public record KarmaInfoRequest : IRequestData
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "";
}

public record KarmaManyRequest : IRequestData
{
    [JsonPropertyName("profileIds")]
    public List<string> ProfileIds { get; set; } = new();
}

public record KarmaAddRequest : IRequestData
{
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "";

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}

public class KarmaInfo
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "";

    [JsonPropertyName("karma")]
    public double Karma { get; set; }

    [JsonPropertyName("applied")]
    public double Applied { get; set; }

    [JsonPropertyName("minimum")]
    public double Minimum { get; set; }

    [JsonPropertyName("maximum")]
    public double Maximum { get; set; }

    [JsonPropertyName("tier")]
    public string Tier { get; set; } = "Neutral";

    [JsonPropertyName("traderPrice")]
    public double TraderPrice { get; set; } = 1;

    [JsonPropertyName("fencePrice")]
    public double FencePrice { get; set; } = 1;

    [JsonPropertyName("fenceSell")]
    public double FenceSell { get; set; } = 1;

    [JsonPropertyName("fleaTax")]
    public double FleaTax { get; set; } = 1;

    [JsonPropertyName("questExperience")]
    public double QuestExperience { get; set; } = 1;

    [JsonPropertyName("events")]
    public Dictionary<string, double> Events { get; set; } = new();
}

public class KarmaManyResponse
{
    [JsonPropertyName("values")]
    public Dictionary<string, double> Values { get; set; } = new();
}
