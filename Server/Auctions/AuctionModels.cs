using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Utils;

namespace ReClassic.Auctions;

public class AuctionConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("minLevel")]
    public int MinLevel { get; set; } = 10;

    [JsonPropertyName("averageDurationHours")]
    public double AverageDurationHours { get; set; } = 4;

    [JsonPropertyName("durationVarianceHours")]
    public double DurationVarianceHours { get; set; } = 1.5;

    [JsonPropertyName("minimumDurationHours")]
    public double MinimumDurationHours { get; set; } = 1;

    [JsonPropertyName("minAiLots")]
    public int MinAiLots { get; set; } = 40;

    [JsonPropertyName("maxAiLots")]
    public int MaxAiLots { get; set; } = 80;

    [JsonPropertyName("aiStartPriceMin")]
    public double AiStartPriceMin { get; set; } = 0.35;

    [JsonPropertyName("aiStartPriceMax")]
    public double AiStartPriceMax { get; set; } = 0.65;

    [JsonPropertyName("aiBiddersMin")]
    public int AiBiddersMin { get; set; } = 0;

    [JsonPropertyName("aiBiddersMax")]
    public int AiBiddersMax { get; set; } = 5;

    [JsonPropertyName("aiValueMin")]
    public double AiValueMin { get; set; } = 0.55;

    [JsonPropertyName("aiValueMax")]
    public double AiValueMax { get; set; } = 0.95;

    [JsonPropertyName("eagerBidderChance")]
    public double EagerBidderChance { get; set; } = 15;

    [JsonPropertyName("eagerValueMax")]
    public double EagerValueMax { get; set; } = 1.2;

    [JsonPropertyName("minBidIncrementPercent")]
    public double MinBidIncrementPercent { get; set; } = 5;

    [JsonPropertyName("minimumBidOfFleaMinimum")]
    public double MinimumBidOfFleaMinimum { get; set; } = 0.5;

    [JsonPropertyName("snipeProtectionSeconds")]
    public int SnipeProtectionSeconds { get; set; } = 120;

    [JsonPropertyName("listingTaxMultiplier")]
    public double ListingTaxMultiplier { get; set; } = 0.5;

    [JsonPropertyName("extendHours")]
    public double ExtendHours { get; set; } = 1;

    [JsonPropertyName("extendCostPercentPerHour")]
    public double ExtendCostPercentPerHour { get; set; } = 2;

    [JsonPropertyName("extendCostMinimumRub")]
    public double ExtendCostMinimumRub { get; set; } = 1000;

    [JsonPropertyName("maxExtensionsPerLot")]
    public int MaxExtensionsPerLot { get; set; } = 6;

    [JsonPropertyName("currencyWeights")]
    public Dictionary<string, double> CurrencyWeights { get; set; } = new()
    {
        ["RUB"] = 80,
        ["USD"] = 12,
        ["EUR"] = 8
    };

    [JsonPropertyName("mailStorageHours")]
    public int MailStorageHours { get; set; } = 72;

    [JsonPropertyName("endgameMinutes")]
    public double EndgameMinutes { get; set; } = 15;

    [JsonPropertyName("endgameMaxLateBidders")]
    public int EndgameMaxLateBidders { get; set; } = 8;

    [JsonPropertyName("dealRatioForMaxInterest")]
    public double DealRatioForMaxInterest { get; set; } = 2;
}

public class PendingMail
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = new();

    [JsonPropertyName("attempts")]
    public int Attempts { get; set; }
}

public class AuctionBid
{
    [JsonPropertyName("bidder")]
    public string Bidder { get; set; } = "";

    [JsonPropertyName("bidderName")]
    public string BidderName { get; set; } = "";

    [JsonPropertyName("isPlayer")]
    public bool IsPlayer { get; set; }

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }
}

public class AiBidder
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("maxBid")]
    public double MaxBid { get; set; }

    [JsonPropertyName("nextAction")]
    public long NextAction { get; set; }

    [JsonPropertyName("sniper")]
    public bool Sniper { get; set; }
}

public class AuctionLot
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("intId")]
    public int InternalId { get; set; }

    [JsonPropertyName("items")]
    public List<Item> Items { get; set; } = new();

    [JsonPropertyName("sellerId")]
    public string SellerId { get; set; } = "";

    [JsonPropertyName("sellerName")]
    public string SellerName { get; set; } = "";

    [JsonPropertyName("sellerIsPlayer")]
    public bool SellerIsPlayer { get; set; }

    [JsonPropertyName("sellerRating")]
    public double SellerRating { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("startPrice")]
    public double StartPrice { get; set; }

    [JsonPropertyName("marketValue")]
    public double MarketValue { get; set; }

    [JsonPropertyName("startTime")]
    public long StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public long EndTime { get; set; }

    [JsonPropertyName("extensions")]
    public int Extensions { get; set; }

    [JsonPropertyName("bids")]
    public List<AuctionBid> Bids { get; set; } = new();

    [JsonPropertyName("aiBidders")]
    public List<AiBidder> AiBidders { get; set; } = new();

    [JsonPropertyName("lateBiddersAdded")]
    public bool LateBiddersAdded { get; set; }

    [JsonIgnore]
    public AuctionBid? Leader => Bids.Count > 0 ? Bids[^1] : null;
}

public class AuctionState
{
    [JsonPropertyName("nextIntId")]
    public int NextInternalId { get; set; } = 1;

    [JsonPropertyName("lots")]
    public List<AuctionLot> Lots { get; set; } = new();

    [JsonPropertyName("stars")]
    public Dictionary<string, List<string>> Stars { get; set; } = new();

    [JsonPropertyName("pendingMail")]
    public List<PendingMail> PendingMail { get; set; } = new();
}

public record AuctionSearchRequest : SearchRequestData
{
    [JsonPropertyName("auctionView")]
    public string? AuctionView { get; set; }
}

public record AuctionLotsRequest : IRequestData
{
    [JsonPropertyName("ids")]
    public List<string>? Ids { get; set; }
}

public record AuctionStarRequest : IRequestData
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("starred")]
    public bool Starred { get; set; }
}

public record AuctionAddRequest : BaseInteractionRequestData
{
    [JsonPropertyName("items")]
    public List<MongoId>? Items { get; set; }

    [JsonPropertyName("sellInOnePiece")]
    public bool? SellInOnePiece { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("startPrice")]
    public double? StartPrice { get; set; }
}

public record AuctionBidRequest : BaseInteractionRequestData
{
    [JsonPropertyName("lotId")]
    public string? LotId { get; set; }

    [JsonPropertyName("amount")]
    public double? Amount { get; set; }
}

public record AuctionLotActionRequest : BaseInteractionRequestData
{
    [JsonPropertyName("lotId")]
    public string? LotId { get; set; }
}

public class AuctionBidView
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("amount")]
    public double Amount { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("isYou")]
    public bool IsYou { get; set; }
}

public class AuctionLotView
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    [JsonPropertyName("startPrice")]
    public double StartPrice { get; set; }

    [JsonPropertyName("currentBid")]
    public double CurrentBid { get; set; }

    [JsonPropertyName("minNextBid")]
    public double MinNextBid { get; set; }

    [JsonPropertyName("marketValue")]
    public double MarketValue { get; set; }

    [JsonPropertyName("bidCount")]
    public int BidCount { get; set; }

    [JsonPropertyName("leaderName")]
    public string LeaderName { get; set; } = "";

    [JsonPropertyName("sellerName")]
    public string SellerName { get; set; } = "";

    [JsonPropertyName("endTime")]
    public long EndTime { get; set; }

    [JsonPropertyName("isMine")]
    public bool IsMine { get; set; }

    [JsonPropertyName("youLead")]
    public bool YouLead { get; set; }

    [JsonPropertyName("youBid")]
    public bool YouBid { get; set; }

    [JsonPropertyName("canExtend")]
    public bool CanExtend { get; set; }

    [JsonPropertyName("canCancel")]
    public bool CanCancel { get; set; }

    [JsonPropertyName("starred")]
    public bool Starred { get; set; }

    [JsonPropertyName("extendCost")]
    public double ExtendCost { get; set; }

    [JsonPropertyName("extendHours")]
    public double ExtendHours { get; set; }

    [JsonPropertyName("bids")]
    public List<AuctionBidView> Bids { get; set; } = new();
}

public class AuctionLotsResponse
{
    [JsonPropertyName("serverTime")]
    public long ServerTime { get; set; }

    [JsonPropertyName("lots")]
    public List<AuctionLotView> Lots { get; set; } = new();
}

public class AuctionInfoResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("averageDurationHours")]
    public double AverageDurationHours { get; set; }

    [JsonPropertyName("minBidIncrementPercent")]
    public double MinBidIncrementPercent { get; set; }

    [JsonPropertyName("listingTaxMultiplier")]
    public double ListingTaxMultiplier { get; set; }

    [JsonPropertyName("minLevel")]
    public int MinLevel { get; set; }

    [JsonPropertyName("activeLots")]
    public int ActiveLots { get; set; }

    [JsonPropertyName("maxActiveLots")]
    public int MaxActiveLots { get; set; }

    [JsonPropertyName("serverTime")]
    public long ServerTime { get; set; }
}
