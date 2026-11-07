using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.Repairs;

[Injectable]
public class RepairStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, RepairQueueService repairs) : StaticRouter(jsonUtil, new List<RouteAction>
{
    new RouteAction<RepairListRequest>("/reclassic/repairs/list", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(repairs.List(sessionId))))
});

[Injectable]
public class RepairItemEventRouter(RepairQueueService repairs) : ItemEventRouter(new List<ItemRouteAction>
{
    new ItemRouteAction<RepairSendRequest>("ReClassicRepairSend", (url, pmc, body, sessionId, output, token) =>
        ValueTask.FromResult(repairs.Send(pmc, body, sessionId)))
});

[Injectable(InjectionType.Singleton)]
public class RepairUpdater(RepairQueueService repairs) : IOnUpdate
{
    public Task<bool> OnUpdateAsync(long secondsSinceLastRun, CancellationToken cancellationToken)
    {
        if (secondsSinceLastRun < 5)
        {
            return Task.FromResult(false);
        }
        repairs.Update();
        return Task.FromResult(true);
    }
}
