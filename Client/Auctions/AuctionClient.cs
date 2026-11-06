#pragma warning disable CS0649
using Comfort.Common;
using EFT;
using JsonType;
using EFT.Communications;
using EFT.UI.Ragfair;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;

namespace mizmii.ReClassic.Auctions
{
    internal sealed class AuctionBidView
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("amount")] public double Amount;
        [JsonProperty("time")] public long Time;
        [JsonProperty("isYou")] public bool IsYou;
    }

    internal sealed class AuctionLotView
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("currency")] public string Currency;
        [JsonProperty("startPrice")] public double StartPrice;
        [JsonProperty("currentBid")] public double CurrentBid;
        [JsonProperty("minNextBid")] public double MinNextBid;
        [JsonProperty("marketValue")] public double MarketValue;
        [JsonProperty("bidCount")] public int BidCount;
        [JsonProperty("leaderName")] public string LeaderName;
        [JsonProperty("sellerName")] public string SellerName;
        [JsonProperty("endTime")] public long EndTime;
        [JsonProperty("isMine")] public bool IsMine;
        [JsonProperty("youLead")] public bool YouLead;
        [JsonProperty("youBid")] public bool YouBid;
        [JsonProperty("canExtend")] public bool CanExtend;
        [JsonProperty("canCancel")] public bool CanCancel;
        [JsonProperty("starred")] public bool Starred;
        [JsonProperty("extendCost")] public double ExtendCost;
        [JsonProperty("extendHours")] public double ExtendHours;
        [JsonProperty("bids")] public List<AuctionBidView> Bids = new List<AuctionBidView>();
    }

    internal sealed class AuctionLotsResponse
    {
        [JsonProperty("serverTime")] public long ServerTime;
        [JsonProperty("lots")] public List<AuctionLotView> Lots = new List<AuctionLotView>();
    }

    internal sealed class AuctionInfo
    {
        [JsonProperty("enabled")] public bool Enabled;
        [JsonProperty("averageDurationHours")] public double AverageDurationHours;
        [JsonProperty("minBidIncrementPercent")] public double MinBidIncrementPercent;
        [JsonProperty("listingTaxMultiplier")] public double ListingTaxMultiplier;
        [JsonProperty("minLevel")] public int MinLevel = 10;
        [JsonProperty("activeLots")] public int ActiveLots;
        [JsonProperty("maxActiveLots")] public int MaxActiveLots;
        [JsonProperty("serverTime")] public long ServerTime;
    }

    internal sealed class StarRequest
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("starred")] public bool Starred;
    }

    internal sealed class LotsRequest
    {
        [JsonProperty("ids")] public List<string> Ids;
    }

    internal sealed class AddOperation
    {
        public string Action = "ReClassicAuctionAdd";
        public string[] items;
        public bool sellInOnePiece;
        public string currency;
        public double startPrice;
    }

    internal sealed class BidOperation
    {
        public string Action = "ReClassicAuctionBid";
        public string lotId;
        public double amount;
    }

    internal sealed class LotOperation
    {
        public string Action;
        public string lotId;
    }

    internal static class AuctionClient
    {
        public const string Roubles = "5449016a4bdc2d6f028b456f";
        public const string Dollars = "5696686a4bdc2da3298b456a";
        public const string Euros = "569668774bdc2da2298b4568";

        public static bool Active;
        public static string View = "all";
        public static AuctionInfo Info;
        public static readonly Dictionary<string, AuctionLotView> Lots = new Dictionary<string, AuctionLotView>();
        public static event Action LotsUpdated;
        public static event Action ModeChanged;
        public static event Action ViewChanged;

        private static long _serverOffset;
        private static int _lotsRequest;

        private static readonly MethodInfo SendMethod = AccessTools.Method(typeof(Backend), "method_5").MakeGenericMethod(typeof(OffersList));
        private static readonly FieldInfo UrlsField = AccessTools.Field(typeof(ClientBackendSession), "_backendUrls");

        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + _serverOffset;

        public static void SetActive(bool active)
        {
            if (Active == active)
            {
                return;
            }
            Active = active;
            View = "all";
            ModeChanged?.Invoke();
        }

        public static void SetView(string view)
        {
            if (View == view)
            {
                return;
            }
            View = view;
            ViewChanged?.Invoke();
        }

        public static Task<Result<OffersList>> Find(EftClientBackendSession session, int page, int limit, int sortType, bool direction, int currency, int priceFrom, int priceTo, int quantityFrom, int quantityTo, int conditionFrom, int conditionTo, bool oneHourExpiration, bool removeBartering, int offerOwnerType, bool onlyFunctional, string handbookId, string linkedSearchId, string neededSearchId, Dictionary<string, int> buildItems, int buildCount, bool updateOfferCount)
        {
            var completion = new TaskCompletionSource<Result<OffersList>>();
            var parameters = new Dictionary<string, object>
            {
                ["page"] = page,
                ["limit"] = limit,
                ["sortType"] = sortType,
                ["sortDirection"] = direction ? 1 : 0,
                ["currency"] = currency,
                ["priceFrom"] = priceFrom,
                ["priceTo"] = priceTo,
                ["quantityFrom"] = quantityFrom,
                ["quantityTo"] = quantityTo,
                ["conditionFrom"] = conditionFrom,
                ["conditionTo"] = conditionTo,
                ["oneHourExpiration"] = oneHourExpiration,
                ["removeBartering"] = removeBartering,
                ["offerOwnerType"] = offerOwnerType,
                ["onlyFunctional"] = onlyFunctional,
                ["updateOfferCount"] = updateOfferCount,
                ["handbookId"] = handbookId,
                ["linkedSearchId"] = linkedSearchId,
                ["neededSearchId"] = neededSearchId,
                ["buildItems"] = buildItems,
                ["buildCount"] = buildCount,
                ["tm"] = 18,
                ["reload"] = 11,
                ["auctionView"] = View
            };
            var urls = (BackendUrls)UrlsField.GetValue(session);
            var request = new SendRequest
            {
                Url = urls.RagFair + "/reclassic/auctions/find",
                ParseInBackground = true,
                Params = parameters,
                Retries = 1
            };
            Callback<OffersList> callback = result =>
            {
                completion.TrySetResult(result);
                RefreshLots(result.Value?.offers?.Select(o => o.Id));
            };
            SendMethod.Invoke(session, new object[] { request, callback });
            return completion.Task;
        }

        public static async void RefreshLots(IEnumerable<string> ids)
        {
            List<string> list = ids?.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList() ?? new List<string>();
            if (list.Count == 0)
            {
                return;
            }
            int request = ++_lotsRequest;
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/auctions/lots", JsonConvert.SerializeObject(new LotsRequest { Ids = list }));
                var response = JsonConvert.DeserializeObject<AuctionLotsResponse>(json);
                if (response == null || request != _lotsRequest && list.Count > 1)
                {
                    return;
                }
                _serverOffset = response.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                foreach (AuctionLotView lot in response.Lots)
                {
                    Lots[lot.Id] = lot;
                }
                LotsUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Auctions] Could not load auction details: {e.Message}");
            }
        }

        public static async void Star(string id, bool starred)
        {
            if (Lots.TryGetValue(id, out AuctionLotView cached))
            {
                cached.Starred = starred;
                LotsUpdated?.Invoke();
            }
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/auctions/star", JsonConvert.SerializeObject(new StarRequest { Id = id, Starred = starred }));
                var response = JsonConvert.DeserializeObject<AuctionLotsResponse>(json);
                if (response != null)
                {
                    foreach (AuctionLotView lot in response.Lots)
                    {
                        Lots[lot.Id] = lot;
                    }
                }
                LotsUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Auctions] Could not star the lot: {e.Message}");
            }
        }

        public static async void RefreshInfo()
        {
            try
            {
                string json = await RequestHandler.PostJsonAsync("/reclassic/auctions/info", "{}");
                Info = JsonConvert.DeserializeObject<AuctionInfo>(json);
                if (Info != null)
                {
                    _serverOffset = Info.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }
                LotsUpdated?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Auctions] Could not load auction info: {e.Message}");
            }
        }

        public static void Send(object operation, Action<bool> done)
        {
            IEftSession session = PatchConstants.BackEndSession;
            if (session == null)
            {
                done?.Invoke(false);
                return;
            }
            session.SendOperationRightNow(operation, result =>
            {
                bool ok = !result.Failed;
                if (!ok && !string.IsNullOrEmpty(result.Error))
                {
                    NotificationManager.DisplayWarningNotification(result.Error.Localized());
                }
                done?.Invoke(ok);
            });
        }

        public static void Bid(string lotId, double amount, Action<bool> done)
        {
            Send(new BidOperation { lotId = lotId, amount = Math.Round(amount) }, done);
        }

        public static void Extend(string lotId, Action<bool> done)
        {
            Send(new LotOperation { Action = "ReClassicAuctionExtend", lotId = lotId }, done);
        }

        public static void Cancel(string lotId, Action<bool> done)
        {
            Send(new LotOperation { Action = "ReClassicAuctionCancel", lotId = lotId }, done);
        }

        public static string Symbol(string currency)
        {
            switch (currency)
            {
                case Dollars:
                    return "$";
                case Euros:
                    return "€";
                default:
                    return "₽";
            }
        }

        public static string Price(double amount, string currency)
        {
            return amount.ToString("N0") + " " + Symbol(currency);
        }

        public static string TimeLeft(long endTime)
        {
            long seconds = Math.Max(0, endTime - Now);
            if (seconds <= 0)
            {
                return "ENDED";
            }
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            }
            return $"{span.Minutes}m {span.Seconds:00}s";
        }
    }
}
