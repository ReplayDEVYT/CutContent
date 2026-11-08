using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.Karma;

[Injectable]
public class KarmaStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, KarmaService karma) : StaticRouter(jsonUtil, new List<RouteAction>
{
    new RouteAction<KarmaInfoRequest>("/reclassic/karma/info", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(karma.Info(string.IsNullOrEmpty(info?.ProfileId) ? sessionId.ToString() : info.ProfileId)))),
    new RouteAction<KarmaManyRequest>("/reclassic/karma/many", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(karma.Many(info?.ProfileIds ?? new List<string>())))),
    new RouteAction<KarmaAddRequest>("/reclassic/karma/add", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(karma.Add(string.IsNullOrEmpty(info?.ProfileId) ? sessionId.ToString() : info.ProfileId, info?.Event ?? "", info?.Count ?? 1))))
});
