using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;

namespace ReClassic.Repairs;

public class RepairConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("baseMinutes")]
    public double BaseMinutes { get; set; } = 10;

    [JsonPropertyName("minutesPerPoint")]
    public double MinutesPerPoint { get; set; } = 1;

    [JsonPropertyName("qualityExponent")]
    public double QualityExponent { get; set; } = 2;

    [JsonPropertyName("minimumMinutes")]
    public double MinimumMinutes { get; set; } = 5;

    [JsonPropertyName("maximumHours")]
    public double MaximumHours { get; set; } = 24;

    [JsonPropertyName("mailStorageHours")]
    public int MailStorageHours { get; set; } = 168;
}

public class RepairJob
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("traderId")]
    public string TraderId { get; set; } = "";

    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = new();

    [JsonPropertyName("durability")]
    public double Durability { get; set; }

    [JsonPropertyName("maxDurability")]
    public double MaxDurability { get; set; }

    [JsonPropertyName("startTime")]
    public long StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public long EndTime { get; set; }

    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }
}

public class RepairState
{
    [JsonPropertyName("jobs")]
    public List<RepairJob> Jobs { get; set; } = new();
}

public record RepairSendRequest : BaseInteractionRequestData
{
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    [JsonPropertyName("traderId")]
    public string? TraderId { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("durability")]
    public double Durability { get; set; }

    [JsonPropertyName("maxDurability")]
    public double MaxDurability { get; set; }
}

public record RepairListRequest : IRequestData
{
}

public class RepairJobView
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("traderId")]
    public string TraderId { get; set; } = "";

    [JsonPropertyName("template")]
    public string Template { get; set; } = "";

    [JsonPropertyName("durability")]
    public double Durability { get; set; }

    [JsonPropertyName("maxDurability")]
    public double MaxDurability { get; set; }

    [JsonPropertyName("startTime")]
    public long StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public long EndTime { get; set; }
}

public class RepairListResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; set; }

    [JsonPropertyName("baseMinutes")]
    public double BaseMinutes { get; set; }

    [JsonPropertyName("minutesPerPoint")]
    public double MinutesPerPoint { get; set; }

    [JsonPropertyName("qualityExponent")]
    public double QualityExponent { get; set; }

    [JsonPropertyName("minimumMinutes")]
    public double MinimumMinutes { get; set; }

    [JsonPropertyName("maximumHours")]
    public double MaximumHours { get; set; }

    [JsonPropertyName("jobs")]
    public List<RepairJobView> Jobs { get; set; } = new();
}
