using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.MapGuides;

[Injectable]
public class MapGuideStaticRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil, MapGuideService guides) : StaticRouter(jsonUtil, new List<RouteAction>
{
    new RouteAction<MapGuideListRequest>("/reclassic/mapguides/list", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(guides.List()))),
    new RouteAction<MapGuideImageRequest>("/reclassic/mapguides/image", (url, info, sessionId, output, token) =>
        ValueTask.FromResult(httpResponseUtil.NoBody(guides.Image(info.Name))))
});
