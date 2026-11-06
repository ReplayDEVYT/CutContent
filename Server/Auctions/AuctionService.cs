using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Ragfair;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Ragfair;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Services.Ragfair;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ReClassic.Auctions;

[Injectable(InjectionType.Singleton)]
public class AuctionService(
    ISptLogger<AuctionService> logger,
    ModHelper modHelper,
    FileUtil fileUtil,
    JsonUtil jsonUtil,
    TimeUtil timeUtil,
    RandomUtil randomUtil,
    ICloner cloner,
    GlobalTable globalTable,
    ItemHelper itemHelper,
    HandbookHelper handbookHelper,
    PaymentHelper paymentHelper,
    PaymentService paymentService,
    InventoryHelper inventoryHelper,
    ProfileHelper profileHelper,
    BotHelper botHelper,
    MailSendService mailSendService,
    RagfairHelper ragfairHelper,
    RagfairServerHelper ragfairServerHelper,
    RagfairPriceService ragfairPriceService,
    RagfairTaxService ragfairTaxService,
    RagfairCategoriesService ragfairCategoriesService,
    RagfairAssortGenerator ragfairAssortGenerator,
    RagfairController ragfairController,
    EventOutputHolder eventOutputHolder,
    HttpResponseUtil httpResponseUtil,
    BotConfig botConfig)
{
    private readonly object _lock = new();
    private AuctionState _state = new();
    private AuctionConfig _config = new();
    private string _folder = "";
    private List<List<Item>>? _pool;
    private bool _loaded;
    private bool _dirty;

    public AuctionConfig Config
    {
        get
        {
            EnsureLoaded();
            return _config;
        }
    }

    public void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            _folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string configPath = System.IO.Path.Combine(_folder, "config", "auctions.json");
            try
            {
                if (fileUtil.FileExists(configPath))
                {
                    _config = jsonUtil.Deserialize<AuctionConfig>(fileUtil.ReadFile(configPath)) ?? new AuctionConfig();
                }
                fileUtil.WriteFile(configPath, jsonUtil.Serialize(_config, true) ?? "{}");
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read the auction config, using defaults: {e.Message}");
                _config = new AuctionConfig();
            }

            string statePath = StatePath();
            try
            {
                if (fileUtil.FileExists(statePath))
                {
                    _state = jsonUtil.Deserialize<AuctionState>(fileUtil.ReadFile(statePath)) ?? new AuctionState();
                }
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read saved auctions, starting fresh: {e.Message}");
                _state = new AuctionState();
            }
        }
    }

    private string StatePath()
    {
        return System.IO.Path.Combine(_folder, "data", "auctions.json");
    }

    private void Save()
    {
        if (!_dirty)
        {
            return;
        }
        _dirty = false;
        try
        {
            fileUtil.WriteFile(StatePath(), jsonUtil.Serialize(_state) ?? "{}");
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not save auctions: {e.Message}");
        }
    }

    public void Update()
    {
        EnsureLoaded();
        if (!_config.Enabled)
        {
            return;
        }
        lock (_lock)
        {
            long now = timeUtil.GetTimeStamp();
            foreach (AuctionLot lot in _state.Lots.ToList())
            {
                RunAiBidders(lot, now);
                if (lot.EndTime <= now)
                {
                    Settle(lot);
                    _state.Lots.Remove(lot);
                    _dirty = true;
                }
            }
            FillAiLots(now);
            DeliverPending();
            Save();
        }
    }

    private List<List<Item>> Pool()
    {
        if (_pool != null)
        {
            return _pool;
        }
        _pool = new List<List<Item>>();
        foreach (List<Item> items in ragfairAssortGenerator.GenerateRagfairAssortItems())
        {
            if (items.Count == 0)
            {
                continue;
            }
            KeyValuePair<bool, TemplateItem?> template = itemHelper.GetItem(items[0].Template);
            if (!ragfairServerHelper.IsItemValidRagfairItem(template) || paymentHelper.IsMoneyTpl(items[0].Template))
            {
                continue;
            }
            _pool.Add(items);
        }
        return _pool;
    }

    private void FillAiLots(long now)
    {
        int aiLots = _state.Lots.Count(l => !l.SellerIsPlayer);
        if (aiLots >= _config.MinAiLots)
        {
            if (aiLots >= _config.MaxAiLots || randomUtil.GetDouble(0, 1) > 0.02)
            {
                return;
            }
        }
        List<List<Item>> pool = Pool();
        if (pool.Count == 0)
        {
            return;
        }
        int target = Math.Max(_config.MinAiLots, Math.Min(_config.MaxAiLots, aiLots + 1));
        int attempts = 0;
        while (aiLots < target && attempts++ < target * 4)
        {
            if (CreateAiLot(pool, now, aiLots < _config.MinAiLots && _state.Lots.Count == 0))
            {
                aiLots++;
            }
        }
    }

    private bool CreateAiLot(List<List<Item>> pool, long now, bool staggered)
    {
        List<Item> source = randomUtil.GetRandomElement(pool);
        List<Item> items = cloner.Clone(source)!.ReplaceIDs().ToList();
        Item root = items[0];
        root.ParentId = "hideout";
        root.SlotId = "hideout";
        root.Upd ??= new Upd();
        bool preset = items.Count > 1;
        int stack = ragfairServerHelper.CalculateDynamicStackCount(root.Template, preset);
        root.Upd.StackObjectsCount = Math.Max(1, stack);
        root.Upd.SpawnedInSession = true;
        root.Upd.UnlimitedCount = null;

        string currency = PickCurrency();
        double valueRub = MarketValueRub(items);
        if (valueRub < 1000)
        {
            return false;
        }
        double value = FromRub(valueRub, currency);
        double start = Math.Max(MinimumBid(items, currency), RoundPrice(value * randomUtil.GetDouble(_config.AiStartPriceMin, _config.AiStartPriceMax), currency));

        long duration = RandomDuration();
        long startTime = staggered ? now - (long)(duration * randomUtil.GetDouble(0, 0.8)) : now;

        var lot = new AuctionLot
        {
            Id = new MongoId().ToString(),
            InternalId = _state.NextInternalId++,
            Items = items,
            SellerId = new MongoId().ToString(),
            SellerName = botHelper.GetPmcNicknameOfMaxLength(botConfig.BotNameLengthLimit),
            SellerIsPlayer = false,
            SellerRating = Math.Round(randomUtil.GetDouble(0.2, 3), 2),
            Currency = currency,
            StartPrice = start,
            MarketValue = Math.Round(value),
            StartTime = startTime,
            EndTime = startTime + duration
        };
        CreateAiBidders(lot, now);
        _state.Lots.Add(lot);
        _dirty = true;
        return true;
    }

    private void CreateAiBidders(AuctionLot lot, long now)
    {
        int rolled = randomUtil.GetInt(_config.AiBiddersMin, Math.Max(_config.AiBiddersMin, _config.AiBiddersMax));
        double interest = Interest(lot, lot.StartPrice);
        int count = (int)Math.Round(rolled * (0.2 + 1.1 * interest));
        long span = Math.Max(60, lot.EndTime - now);
        for (int i = 0; i < count; i++)
        {
            bool sniper = randomUtil.GetChance100(15 + 35 * interest);
            lot.AiBidders.Add(NewAiBidder(lot, now + (long)(span * randomUtil.GetDouble(0.02, 0.6)), sniper));
        }
    }

    private void RunAiBidders(AuctionLot lot, long now)
    {
        if (lot.EndTime <= now)
        {
            return;
        }
        long remaining = lot.EndTime - now;
        long endgame = (long)(Math.Max(0, _config.EndgameMinutes) * 60);
        if (remaining <= endgame)
        {
            if (!lot.LateBiddersAdded)
            {
                AddLateBidders(lot, now, remaining);
            }
            double interest = Interest(lot, MinNextBid(lot));
            string? leaderId = lot.Leader?.Bidder;
            foreach (AiBidder bidder in lot.AiBidders)
            {
                if (bidder.Sniper || bidder.Id == leaderId || bidder.MaxBid < MinNextBid(lot))
                {
                    continue;
                }
                long soon = now + randomUtil.GetInt(10, 30 + (int)(240 * (1 - interest)));
                if (bidder.NextAction > soon && randomUtil.GetChance100(25 + 70 * interest))
                {
                    bidder.NextAction = soon;
                }
            }
        }
        if (lot.AiBidders.Count == 0)
        {
            return;
        }
        foreach (AiBidder bidder in lot.AiBidders.OrderBy(b => b.NextAction))
        {
            if (bidder.NextAction > now)
            {
                continue;
            }
            if (bidder.Sniper && remaining > Math.Max(180, _config.SnipeProtectionSeconds * 2))
            {
                bidder.NextAction = lot.EndTime - randomUtil.GetInt(30, Math.Max(31, Math.Max(180, _config.SnipeProtectionSeconds * 2)));
                continue;
            }
            bidder.NextAction = now + (long)Math.Max(30, remaining * randomUtil.GetDouble(0.05, 0.35));
            AuctionBid? leader = lot.Leader;
            if (leader != null && leader.Bidder == bidder.Id)
            {
                continue;
            }
            double needed = MinNextBid(lot);
            if (needed > bidder.MaxBid)
            {
                continue;
            }
            PlaceBid(lot, bidder.Id, bidder.Name, false, needed, now);
            remaining = lot.EndTime - now;
        }
    }

    private void PlaceBid(AuctionLot lot, string bidderId, string name, bool isPlayer, double amount, long now)
    {
        AuctionBid? previous = lot.Leader;
        lot.Bids.Add(new AuctionBid
        {
            Bidder = bidderId,
            BidderName = name,
            IsPlayer = isPlayer,
            Amount = amount,
            Time = now
        });
        if (previous != null && previous.IsPlayer && previous.Bidder != bidderId)
        {
            Refund(lot, previous);
        }
        if (_config.SnipeProtectionSeconds > 0 && lot.EndTime - now < _config.SnipeProtectionSeconds)
        {
            lot.EndTime = now + _config.SnipeProtectionSeconds;
        }
        bool late = lot.EndTime - now <= (long)(Math.Max(0, _config.EndgameMinutes) * 60);
        double interest = Interest(lot, MinNextBid(lot));
        foreach (AiBidder bidder in lot.AiBidders)
        {
            if (bidder.Id == bidderId || (bidder.Sniper && !late))
            {
                continue;
            }
            if (!randomUtil.GetChance100(20 + 80 * interest))
            {
                continue;
            }
            int delay = late ? randomUtil.GetInt(5, 20 + (int)(100 * (1 - interest))) : randomUtil.GetInt(20, 300);
            bidder.NextAction = Math.Min(bidder.NextAction, now + delay);
        }
        _dirty = true;
    }

    private void Refund(AuctionLot lot, AuctionBid bid)
    {
        string name = ItemName(lot);
        Send(bid.Bidder, $"You were outbid on the auction for {name}. Your bid of {FormatPrice(bid.Amount, lot.Currency)} has been returned.", MoneyItems(lot.Currency, bid.Amount));
    }

    private void Settle(AuctionLot lot)
    {
        string name = ItemName(lot);
        AuctionBid? winner = lot.Leader;
        if (winner == null)
        {
            if (lot.SellerIsPlayer)
            {
                Send(lot.SellerId, $"Your auction for {name} ended without any bids. The item has been returned.", FreshItems(lot.Items));
            }
            return;
        }
        if (winner.IsPlayer)
        {
            Send(winner.Bidder, $"You won the auction for {name} with a bid of {FormatPrice(winner.Amount, lot.Currency)}.", FreshItems(lot.Items));
        }
        if (lot.SellerIsPlayer)
        {
            Send(lot.SellerId, $"Your auction for {name} sold to {winner.BidderName} for {FormatPrice(winner.Amount, lot.Currency)}.", MoneyItems(lot.Currency, winner.Amount));
        }
    }

    private void Send(string sessionId, string message, List<Item> items)
    {
        if (TrySend(sessionId, message, items, out string error))
        {
            return;
        }
        logger.Error($"[Re-Classic] Could not send an auction message, will retry: {error}");
        _state.PendingMail.Add(new PendingMail { SessionId = sessionId, Message = message, Items = cloner.Clone(items)! });
        _dirty = true;
    }

    private bool TrySend(string sessionId, string message, List<Item> items, out string error)
    {
        error = "";
        try
        {
            mailSendService.SendDirectNpcMessageToPlayer(new MongoId(sessionId), Traders.RAGMAN, MessageType.MessageWithItems, message, items, timeUtil.GetHoursAsSeconds(_config.MailStorageHours));
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private double Interest(AuctionLot lot, double price)
    {
        if (price <= 0 || lot.MarketValue <= 0)
        {
            return 0;
        }
        double ratio = lot.MarketValue / price;
        double full = Math.Max(1.05, _config.DealRatioForMaxInterest);
        return Math.Clamp((ratio - 0.85) / (full - 0.85), 0, 1);
    }

    private AiBidder NewAiBidder(AuctionLot lot, long nextAction, bool sniper)
    {
        bool eager = randomUtil.GetChance100(_config.EagerBidderChance);
        double fraction = eager
            ? randomUtil.GetDouble(_config.AiValueMax, Math.Max(_config.AiValueMax, _config.EagerValueMax))
            : randomUtil.GetDouble(_config.AiValueMin, _config.AiValueMax);
        return new AiBidder
        {
            Id = new MongoId().ToString(),
            Name = botHelper.GetPmcNicknameOfMaxLength(botConfig.BotNameLengthLimit),
            MaxBid = RoundPrice(lot.MarketValue * fraction, lot.Currency),
            Sniper = sniper,
            NextAction = nextAction
        };
    }

    private void AddLateBidders(AuctionLot lot, long now, long remaining)
    {
        lot.LateBiddersAdded = true;
        double interest = Interest(lot, MinNextBid(lot));
        double expected = interest * interest * Math.Max(0, _config.EndgameMaxLateBidders);
        int count = (int)Math.Floor(expected * randomUtil.GetDouble(0.6, 1.3) + randomUtil.GetDouble(0, 1));
        if (interest < 0.1)
        {
            count = randomUtil.GetChance100(interest * 100) ? 1 : 0;
        }
        for (int i = 0; i < count; i++)
        {
            long at = now + (long)(remaining * randomUtil.GetDouble(0.15, 0.95));
            lot.AiBidders.Add(NewAiBidder(lot, at, false));
        }
        if (count > 0)
        {
            _dirty = true;
        }
    }

    private List<Item> FreshItems(List<Item> items)
    {
        List<Item> copy = cloner.Clone(items)!.ReplaceIDs().ToList();
        if (copy[0].Upd != null)
        {
            copy[0].Upd.UnlimitedCount = null;
        }
        copy[0].ParentId = new MongoId().ToString();
        copy[0].SlotId = "hideout";
        if (copy.Count == 1 && (copy[0].Upd?.StackObjectsCount ?? 1) > 1)
        {
            List<Item> stacks = itemHelper.SplitStack(copy[0]);
            foreach (Item stack in stacks)
            {
                stack.ParentId = copy[0].ParentId;
                stack.SlotId = "hideout";
            }
            return stacks;
        }
        return copy;
    }

    private void DeliverPending()
    {
        if (_state.PendingMail.Count == 0)
        {
            return;
        }
        foreach (PendingMail mail in _state.PendingMail.ToList())
        {
            mail.Attempts++;
            if (TrySend(mail.SessionId, mail.Message, FreshItems(mail.Items), out _) || mail.Attempts >= 20)
            {
                _state.PendingMail.Remove(mail);
                _dirty = true;
            }
        }
    }

    private List<Item> MoneyItems(string currency, double amount)
    {
        var stack = new Item
        {
            Id = new MongoId(),
            Template = new MongoId(currency),
            Upd = new Upd { StackObjectsCount = Math.Round(amount) }
        };
        return itemHelper.SplitStack(stack);
    }

    private string PickCurrency()
    {
        double total = _config.CurrencyWeights.Values.Where(w => w > 0).Sum();
        double roll = randomUtil.GetDouble(0, Math.Max(total, 0.0001));
        foreach (KeyValuePair<string, double> pair in _config.CurrencyWeights)
        {
            if (pair.Value <= 0)
            {
                continue;
            }
            roll -= pair.Value;
            if (roll <= 0)
            {
                return CurrencyTpl(pair.Key);
            }
        }
        return SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString();
    }

    private static string CurrencyTpl(string code)
    {
        return code.ToUpperInvariant() switch
        {
            "USD" => SPTarkov.Server.Core.Models.Enums.Money.DOLLARS.ToString(),
            "EUR" => SPTarkov.Server.Core.Models.Enums.Money.EUROS.ToString(),
            _ => SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString()
        };
    }

    private static string CurrencySymbol(string tpl)
    {
        if (tpl == SPTarkov.Server.Core.Models.Enums.Money.DOLLARS.ToString())
        {
            return "$";
        }
        if (tpl == SPTarkov.Server.Core.Models.Enums.Money.EUROS.ToString())
        {
            return "€";
        }
        return "₽";
    }

    private static string FormatPrice(double amount, string currency)
    {
        return $"{amount:N0} {CurrencySymbol(currency)}";
    }

    private long RandomDuration()
    {
        double hours = randomUtil.GetNormallyDistributedRandomNumber(_config.AverageDurationHours, Math.Max(0.01, _config.DurationVarianceHours));
        hours = Math.Max(_config.MinimumDurationHours, hours);
        return (long)(hours * 3600);
    }

    private double MarketValueRub(List<Item> items)
    {
        Item root = items[0];
        double stack = root.Upd?.StackObjectsCount ?? 1;
        double value;
        if (items.Count > 1)
        {
            value = ragfairPriceService.GetDynamicOfferPriceForOffer(items, SPTarkov.Server.Core.Models.Enums.Money.ROUBLES, false);
        }
        else
        {
            value = ragfairPriceService.GetFleaPriceForItem(root.Template) * stack;
        }
        return value * Math.Max(0.1, itemHelper.GetItemQualityModifierForItems(items, true));
    }

    private double FleaMinimumRub(List<Item> items)
    {
        Item root = items[0];
        double stack = root.Upd?.StackObjectsCount ?? 1;
        GetItemPriceResult prices = ragfairController.GetItemMinAvgMaxFleaPriceValues(new GetMarketPriceRequestData { TemplateId = root.Template });
        double min = prices.Min;
        if (min <= 0 || min >= int.MaxValue)
        {
            min = ragfairPriceService.GetFleaPriceForItem(root.Template);
        }
        double total = min * stack;
        if (items.Count > 1)
        {
            total = Math.Min(total, MarketValueRub(items));
            total = Math.Max(total, MarketValueRub(items) * 0.5);
        }
        return total;
    }

    private double MinimumBid(List<Item> items, string currency)
    {
        return RoundPrice(FromRub(FleaMinimumRub(items) * _config.MinimumBidOfFleaMinimum, currency), currency);
    }

    private double FromRub(double rub, string currency)
    {
        if (currency == SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString())
        {
            return rub;
        }
        return handbookHelper.FromRoubles(rub, new MongoId(currency));
    }

    private double ToRub(double amount, string currency)
    {
        if (currency == SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString())
        {
            return amount;
        }
        return handbookHelper.InRoubles(amount, new MongoId(currency));
    }

    private static double RoundPrice(double value, string currency)
    {
        double step = currency == SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString() ? 100 : 1;
        if (value > 100000 * step)
        {
            step *= 10;
        }
        return Math.Max(step, Math.Ceiling(value / step) * step);
    }

    public double MinNextBid(AuctionLot lot)
    {
        AuctionBid? leader = lot.Leader;
        if (leader == null)
        {
            return lot.StartPrice;
        }
        double increment = leader.Amount * _config.MinBidIncrementPercent / 100.0;
        double minimum = lot.Currency == SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString() ? 100 : 1;
        return RoundPrice(leader.Amount + Math.Max(minimum, increment), lot.Currency);
    }

    private double ExtendCostRub(AuctionLot lot)
    {
        double value = ToRub(Math.Max(lot.StartPrice, lot.Leader?.Amount ?? 0), lot.Currency);
        double cost = value * _config.ExtendCostPercentPerHour / 100.0 * _config.ExtendHours;
        return Math.Ceiling(Math.Max(_config.ExtendCostMinimumRub, cost));
    }

    private string ItemName(AuctionLot lot)
    {
        Item root = lot.Items[0];
        string name = itemHelper.GetItemName(root.Template);
        double stack = root.Upd?.StackObjectsCount ?? 1;
        return stack > 1 ? $"{name} x{stack:N0}" : name;
    }

    public GetOffersResult Find(MongoId sessionId, AuctionSearchRequest request)
    {
        EnsureLoaded();
        lock (_lock)
        {
            string session = sessionId.ToString();
            IEnumerable<AuctionLot> lots = _state.Lots;
            switch (request.AuctionView)
            {
                case "mine":
                    lots = lots.Where(l => l.SellerId == session);
                    break;
                case "starred":
                    HashSet<string> stars = StarsFor(session);
                    lots = lots.Where(l => stars.Contains(l.Id));
                    break;
                case "bids":
                    lots = lots.Where(l => l.Bids.Any(b => b.Bidder == session));
                    break;
            }

            List<RagfairOffer> all = lots.Select(l => ToOffer(l)).ToList();
            HashSet<MongoId> filter = ragfairHelper.FilterCategories(sessionId, request).ToHashSet();
            List<RagfairOffer> offers = filter.Count > 0 || (request.HandbookId.HasValue && !request.HandbookId.Value.IsEmpty)
                ? all.Where(o => filter.Contains(o.Items![0].Template)).ToList()
                : all;

            if (request.Currency is > 0)
            {
                string tag = ragfairHelper.GetCurrencyTag(request.Currency.Value);
                offers = offers.Where(o => ragfairHelper.GetCurrencyTag(o.Requirements!.First().TemplateId) == tag).ToList();
            }
            if (request.PriceFrom is > 0)
            {
                offers = offers.Where(o => o.RequirementsCost >= request.PriceFrom).ToList();
            }
            if (request.PriceTo is > 0)
            {
                offers = offers.Where(o => o.RequirementsCost <= request.PriceTo).ToList();
            }

            bool descending = request.SortDirection == 1;
            offers = request.SortType switch
            {
                RagfairSort.PRICE => descending ? offers.OrderByDescending(o => o.RequirementsCost).ToList() : offers.OrderBy(o => o.RequirementsCost).ToList(),
                RagfairSort.ID => descending ? offers.OrderByDescending(o => o.InternalId).ToList() : offers.OrderBy(o => o.InternalId).ToList(),
                _ => descending ? offers.OrderByDescending(o => o.EndTime).ToList() : offers.OrderBy(o => o.EndTime).ToList()
            };

            var result = new GetOffersResult
            {
                Offers = offers,
                OffersCount = offers.Count,
                SelectedCategory = request.HandbookId.HasValue ? request.HandbookId.Value.ToString() : null,
                Categories = ragfairCategoriesService.GetCategoriesFromOffers(all, request, true)
            };

            return result;
        }
    }

    private RagfairOffer ToOffer(AuctionLot lot)
    {
        double price = lot.Leader?.Amount ?? lot.StartPrice;
        return new RagfairOffer
        {
            Id = new MongoId(lot.Id),
            InternalId = lot.InternalId,
            Items = lot.Items,
            Root = lot.Items[0].Id,
            Requirements = new List<OfferRequirement>
            {
                new OfferRequirement { TemplateId = new MongoId(lot.Currency), Count = price, OnlyFunctional = false }
            },
            ItemsCost = Math.Round(ToRub(lot.MarketValue, lot.Currency)),
            RequirementsCost = Math.Round(ToRub(price, lot.Currency)),
            SummaryCost = Math.Round(ToRub(price, lot.Currency)),
            StartTime = lot.StartTime,
            EndTime = lot.EndTime,
            SellInOnePiece = true,
            UnlimitedCount = false,
            LoyaltyLevel = 1,
            Locked = false,
            Quantity = 1,
            User = new RagfairOfferUser
            {
                Id = new MongoId(lot.SellerId),
                Nickname = lot.SellerName,
                Rating = lot.SellerRating,
                MemberType = MemberCategory.Default,
                SelectedMemberCategory = MemberCategory.Default,
                IsRatingGrowing = true
            }
        };
    }

    public AuctionLotsResponse Lots(MongoId sessionId, AuctionLotsRequest request)
    {
        EnsureLoaded();
        lock (_lock)
        {
            string session = sessionId.ToString();
            var response = new AuctionLotsResponse { ServerTime = timeUtil.GetTimeStamp() };
            IEnumerable<AuctionLot> lots = request.Ids is { Count: > 0 }
                ? _state.Lots.Where(l => request.Ids.Contains(l.Id))
                : _state.Lots.Where(l => l.SellerId == session || l.Bids.Any(b => b.Bidder == session));
            foreach (AuctionLot lot in lots)
            {
                response.Lots.Add(View(lot, session));
            }
            return response;
        }
    }

    private AuctionLotView View(AuctionLot lot, string session)
    {
        AuctionBid? leader = lot.Leader;
        bool mine = lot.SellerId == session;
        return new AuctionLotView
        {
            Id = lot.Id,
            Currency = lot.Currency,
            StartPrice = lot.StartPrice,
            CurrentBid = leader?.Amount ?? 0,
            MinNextBid = MinNextBid(lot),
            MarketValue = lot.MarketValue,
            BidCount = lot.Bids.Count,
            LeaderName = leader == null ? "" : leader.Bidder == session ? "You" : leader.BidderName,
            SellerName = mine ? "You" : lot.SellerName,
            EndTime = lot.EndTime,
            IsMine = mine,
            YouLead = leader != null && leader.Bidder == session,
            YouBid = lot.Bids.Any(b => b.Bidder == session),
            CanExtend = mine && lot.Extensions < _config.MaxExtensionsPerLot,
            CanCancel = mine && lot.Bids.Count == 0,
            Starred = StarsFor(session).Contains(lot.Id),
            ExtendCost = mine ? ExtendCostRub(lot) : 0,
            ExtendHours = _config.ExtendHours,
            Bids = lot.Bids.AsEnumerable().Reverse().Select(b => new AuctionBidView
            {
                Name = b.Bidder == session ? "You" : b.BidderName,
                Amount = b.Amount,
                Time = b.Time,
                IsYou = b.Bidder == session
            }).ToList()
        };
    }

    private HashSet<string> StarsFor(string session)
    {
        return _state.Stars.TryGetValue(session, out List<string>? list) ? list.ToHashSet() : new HashSet<string>();
    }

    public AuctionLotsResponse Star(MongoId sessionId, AuctionStarRequest request)
    {
        EnsureLoaded();
        lock (_lock)
        {
            string session = sessionId.ToString();
            if (!_state.Stars.TryGetValue(session, out List<string>? list))
            {
                list = new List<string>();
                _state.Stars[session] = list;
            }
            list.RemoveAll(id => _state.Lots.All(l => l.Id != id));
            if (!string.IsNullOrEmpty(request.Id))
            {
                list.Remove(request.Id);
                if (request.Starred)
                {
                    list.Add(request.Id);
                }
            }
            _dirty = true;
            Save();
            var response = new AuctionLotsResponse { ServerTime = timeUtil.GetTimeStamp() };
            AuctionLot? lot = _state.Lots.FirstOrDefault(l => l.Id == request.Id);
            if (lot != null)
            {
                response.Lots.Add(View(lot, session));
            }
            return response;
        }
    }

    public AuctionInfoResponse Info(MongoId sessionId)
    {
        EnsureLoaded();
        lock (_lock)
        {
            PmcData? pmc = profileHelper.GetPmcProfile(sessionId);
            string session = sessionId.ToString();
            return new AuctionInfoResponse
            {
                Enabled = _config.Enabled,
                AverageDurationHours = _config.AverageDurationHours,
                MinBidIncrementPercent = _config.MinBidIncrementPercent,
                ListingTaxMultiplier = _config.ListingTaxMultiplier,
                MinLevel = _config.MinLevel,
                ActiveLots = _state.Lots.Count(l => l.SellerId == session),
                MaxActiveLots = pmc == null ? 0 : MaxLots(pmc),
                ServerTime = timeUtil.GetTimeStamp()
            };
        }
    }

    private int MaxLots(PmcData pmc)
    {
        double rating = pmc.RagfairInfo?.Rating ?? 0;
        foreach (var limit in globalTable.Configuration.RagFair.MaxActiveOfferCount)
        {
            if (rating >= limit.From && rating < limit.To)
            {
                return (int)limit.Count;
            }
        }
        return 1;
    }

    public ItemEventRouterResponse AddLot(PmcData pmc, AuctionAddRequest request, MongoId sessionId)
    {
        EnsureLoaded();
        ItemEventRouterResponse output = eventOutputHolder.GetOutput(sessionId);
        lock (_lock)
        {
            if (!_config.Enabled)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Auctions are turned off on this server.");
            }
            if ((pmc.Info?.Level ?? 0) < _config.MinLevel)
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"Auctions unlock at level {_config.MinLevel}.");
            }
            string session = sessionId.ToString();
            if (_state.Lots.Count(l => l.SellerId == session) >= MaxLots(pmc))
            {
                return httpResponseUtil.AppendErrorToOutput(output, "You have reached your limit of active auctions.");
            }
            if (request.Items == null || request.Items.Count == 0 || request.StartPrice is not > 0)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Invalid auction request.");
            }
            string currency = string.IsNullOrEmpty(request.Currency) ? SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString() : request.Currency;
            if (!paymentHelper.IsMoneyTpl(new MongoId(currency)))
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Auctions must start at a cash price.");
            }

            var stacks = new List<List<Item>>();
            foreach (MongoId id in request.Items)
            {
                Item? item = pmc.Inventory?.Items?.FirstOrDefault(i => i.Id == id);
                if (item == null)
                {
                    return httpResponseUtil.AppendErrorToOutput(output, "Could not find that item in your stash.");
                }
                KeyValuePair<bool, TemplateItem?> template = itemHelper.GetItem(item.Template);
                if (!ragfairServerHelper.IsItemValidRagfairItem(template))
                {
                    return httpResponseUtil.AppendErrorToOutput(output, "That item cannot be sold on the flea market, so it cannot be auctioned.");
                }
                stacks.Add(pmc.Inventory!.Items!.GetItemWithChildren(id));
            }
            if (stacks.Select(s => s[0].Template).Distinct().Count() > 1)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "An auction can only contain one kind of item.");
            }
            if (stacks.Count > 1 && (itemHelper.GetItem(stacks[0][0].Template).Value?.Properties?.StackMaxSize ?? 1) <= 1)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Only one of these items can be auctioned at a time.");
            }

            List<Item> items = cloner.Clone(stacks[0])!;
            Item root = items[0];
            root.Upd ??= new Upd();
            root.Upd.StackObjectsCount = stacks.Sum(s => s[0].Upd?.StackObjectsCount ?? 1);
            root.ParentId = "hideout";
            root.SlotId = "hideout";
            root.Location = null;

            double minimum = MinimumBid(items, currency);
            double start = RoundPrice(request.StartPrice.Value, currency);
            if (start < minimum)
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"The starting price must be at least {FormatPrice(minimum, currency)} (half the lowest flea price).");
            }

            int count = (int)(root.Upd.StackObjectsCount ?? 1);
            double tax = Math.Round(ragfairTaxService.CalculateTax(root, pmc, ToRub(start, currency), count, true) * _config.ListingTaxMultiplier);
            if (tax > 0)
            {
                paymentService.PayMoney(pmc, BuyRequest(SPTarkov.Server.Core.Models.Enums.Money.ROUBLES, tax, pmc.Id!.Value), sessionId, output);
                if (output.Warnings is { Count: > 0 })
                {
                    return httpResponseUtil.AppendErrorToOutput(output, $"You cannot afford the {FormatPrice(tax, SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString())} listing fee.");
                }
            }

            foreach (MongoId id in request.Items)
            {
                inventoryHelper.RemoveItem(pmc, id, sessionId, output);
            }

            long now = timeUtil.GetTimeStamp();
            var lot = new AuctionLot
            {
                Id = new MongoId().ToString(),
                InternalId = _state.NextInternalId++,
                Items = items.ReplaceIDs().ToList(),
                SellerId = session,
                SellerName = pmc.Info?.Nickname ?? "You",
                SellerIsPlayer = true,
                SellerRating = pmc.RagfairInfo?.Rating ?? 0,
                Currency = currency,
                StartPrice = start,
                MarketValue = Math.Round(FromRub(MarketValueRub(items), currency)),
                StartTime = now,
                EndTime = now + (long)(Math.Max(_config.MinimumDurationHours, _config.AverageDurationHours) * 3600)
            };
            CreateAiBidders(lot, now);
            _state.Lots.Add(lot);
            _dirty = true;
            Save();
            return output;
        }
    }

    public ItemEventRouterResponse Bid(PmcData pmc, AuctionBidRequest request, MongoId sessionId)
    {
        EnsureLoaded();
        ItemEventRouterResponse output = eventOutputHolder.GetOutput(sessionId);
        lock (_lock)
        {
            if ((pmc.Info?.Level ?? 0) < _config.MinLevel)
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"Auctions unlock at level {_config.MinLevel}.");
            }
            AuctionLot? lot = _state.Lots.FirstOrDefault(l => l.Id == request.LotId);
            long now = timeUtil.GetTimeStamp();
            if (lot == null || lot.EndTime <= now)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "This auction has already ended.");
            }
            string session = sessionId.ToString();
            if (lot.SellerId == session)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "You cannot bid on your own auction.");
            }
            if (lot.Leader?.Bidder == session)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "You are already the highest bidder.");
            }
            double minimum = MinNextBid(lot);
            double amount = Math.Round(request.Amount ?? 0);
            if (amount < minimum)
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"Your bid must be at least {FormatPrice(minimum, lot.Currency)}.");
            }

            paymentService.PayMoney(pmc, BuyRequest(new MongoId(lot.Currency), amount, pmc.Id!.Value), sessionId, output);
            if (output.Warnings is { Count: > 0 })
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"You do not have {FormatPrice(amount, lot.Currency)} in your stash.");
            }

            PlaceBid(lot, session, pmc.Info?.Nickname ?? "You", true, amount, now);
            Save();
            return output;
        }
    }

    public ItemEventRouterResponse Extend(PmcData pmc, AuctionLotActionRequest request, MongoId sessionId)
    {
        EnsureLoaded();
        ItemEventRouterResponse output = eventOutputHolder.GetOutput(sessionId);
        lock (_lock)
        {
            AuctionLot? lot = _state.Lots.FirstOrDefault(l => l.Id == request.LotId);
            if (lot == null || lot.SellerId != sessionId.ToString())
            {
                return httpResponseUtil.AppendErrorToOutput(output, "You can only extend your own auctions.");
            }
            if (lot.Extensions >= _config.MaxExtensionsPerLot)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "This auction cannot be extended any further.");
            }
            double cost = ExtendCostRub(lot);
            paymentService.PayMoney(pmc, BuyRequest(SPTarkov.Server.Core.Models.Enums.Money.ROUBLES, cost, pmc.Id!.Value), sessionId, output);
            if (output.Warnings is { Count: > 0 })
            {
                return httpResponseUtil.AppendErrorToOutput(output, $"You need {FormatPrice(cost, SPTarkov.Server.Core.Models.Enums.Money.ROUBLES.ToString())} to extend this auction.");
            }
            lot.EndTime += (long)(_config.ExtendHours * 3600);
            lot.Extensions++;
            _dirty = true;
            Save();
            return output;
        }
    }

    public ItemEventRouterResponse Cancel(PmcData pmc, AuctionLotActionRequest request, MongoId sessionId)
    {
        EnsureLoaded();
        ItemEventRouterResponse output = eventOutputHolder.GetOutput(sessionId);
        lock (_lock)
        {
            AuctionLot? lot = _state.Lots.FirstOrDefault(l => l.Id == request.LotId);
            if (lot == null || lot.SellerId != sessionId.ToString())
            {
                return httpResponseUtil.AppendErrorToOutput(output, "You can only cancel your own auctions.");
            }
            if (lot.Bids.Count > 0)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "An auction with bids cannot be cancelled.");
            }
            _state.Lots.Remove(lot);
            Send(lot.SellerId, $"Your auction for {ItemName(lot)} was cancelled. The item has been returned.", FreshItems(lot.Items));
            _dirty = true;
            Save();
            return output;
        }
    }

    private static ProcessBuyTradeRequestData BuyRequest(MongoId currency, double amount, MongoId pmcId)
    {
        return new ProcessBuyTradeRequestData
        {
            TransactionId = pmcId,
            Action = "TradingConfirm",
            SchemeItems = new List<IdWithCount> { new IdWithCount { Id = currency, Count = Math.Round(amount) } },
            Type = string.Empty,
            ItemId = MongoId.Empty(),
            Count = 0,
            SchemeId = 0
        };
    }
}
