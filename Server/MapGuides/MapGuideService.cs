using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.MapGuides;

[Injectable(InjectionType.Singleton)]
public class MapGuideService(ISptLogger<MapGuideService> logger, ModHelper modHelper, JsonUtil jsonUtil)
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
    private readonly object _lock = new();
    private MapGuideConfig _config = new();
    private DateTime _loadedStamp = DateTime.MinValue;
    private string _folder = "";

    private string ConfigPath => System.IO.Path.Combine(_folder, "config", "mapguides.json");
    private string ImageFolder => System.IO.Path.Combine(_folder, "mapguides");

    private void Refresh()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_folder))
            {
                _folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            }
            try
            {
                System.IO.Directory.CreateDirectory(ImageFolder);
                if (!System.IO.File.Exists(ConfigPath))
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConfigPath)!);
                    System.IO.File.WriteAllText(ConfigPath, jsonUtil.Serialize(MapGuideDefaults.Create(), true) ?? "{}");
                    logger.Info("[Re-Classic] Wrote the default map guides to config/mapguides.json.");
                }
                DateTime stamp = System.IO.File.GetLastWriteTimeUtc(ConfigPath);
                if (stamp == _loadedStamp)
                {
                    return;
                }
                _config = jsonUtil.Deserialize<MapGuideConfig>(System.IO.File.ReadAllText(ConfigPath)) ?? new MapGuideConfig();
                _loadedStamp = stamp;
                logger.Info($"[Re-Classic] Loaded map guides for {_config.Maps.Count} maps.");
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read config/mapguides.json, keeping the last good copy: {e.Message}");
                _loadedStamp = DateTime.MinValue;
            }
        }
    }

    public MapGuideConfig List()
    {
        Refresh();
        return _config;
    }

    public MapGuideImageResponse Image(string name)
    {
        Refresh();
        var response = new MapGuideImageResponse { Name = name ?? "" };
        string file = System.IO.Path.GetFileName(name ?? "");
        if (string.IsNullOrWhiteSpace(file) || !ImageExtensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
        {
            return response;
        }
        string path = System.IO.Path.Combine(ImageFolder, file);
        try
        {
            if (System.IO.File.Exists(path))
            {
                response.Data = Convert.ToBase64String(System.IO.File.ReadAllBytes(path));
            }
            else
            {
                logger.Warning($"[Re-Classic] Map guide image {file} is missing from the mapguides folder.");
            }
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not read map guide image {file}: {e.Message}");
        }
        return response;
    }
}
