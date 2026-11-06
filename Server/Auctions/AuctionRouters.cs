using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.Auctions;

[Injectable]
public class AuctionStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, AuctionService auctions) : StaticRouter(jsonUtil, new List<RouteAction>
{
    new RouteAction<AuctionSearchRequest>("/reclassic/auctions/find", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.GetBody(auctions.Find(sessionId, info)))),
    new RouteAction<AuctionLotsRequest>("/reclassic/auctions/lots", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(auctions.Lots(sessionId, info)))),
    new RouteAction<AuctionStarRequest>("/reclassic/auctions/star", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(auctions.Star(sessionId, info)))),
    new RouteAction<AuctionLotsRequest>("/reclassic/auctions/info", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(auctions.Info(sessionId))))
});

[Injectable]
public class AuctionItemEventRouter(AuctionService auctions) : ItemEventRouter(new List<ItemRouteAction>
{
    new ItemRouteAction<AuctionAddRequest>("ReClassicAuctionAdd", (url, pmc, body, sessionId, output, token) =>
        ValueTask.FromResult(auctions.AddLot(pmc, body, sessionId))),
    new ItemRouteAction<AuctionBidRequest>("ReClassicAuctionBid", (url, pmc, body, sessionId, output, token) =>
        ValueTask.FromResult(auctions.Bid(pmc, body, sessionId))),
    new ItemRouteAction<AuctionLotActionRequest>("ReClassicAuctionExtend", (url, pmc, body, sessionId, output, token) =>
        ValueTask.FromResult(auctions.Extend(pmc, body, sessionId))),
    new ItemRouteAction<AuctionLotActionRequest>("ReClassicAuctionCancel", (url, pmc, body, sessionId, output, token) =>
        ValueTask.FromResult(auctions.Cancel(pmc, body, sessionId)))
});

[Injectable(InjectionType.Singleton)]
public class AuctionUpdater(AuctionService auctions) : IOnUpdate
{
    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        if (secondsSinceLastRun < 10)
        {
            return Task.FromResult(false);
        }
        auctions.Update();
        return Task.FromResult(true);
    }
}
