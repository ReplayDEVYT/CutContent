using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace CutContent;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.mizmii.CutContent";
    public string Name { get; init; } = "CutContent";
    public string Author { get; init; } = "mizmii";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "";
    public string License { get; init; } = "Unlicense";
}

// Database tables are ready at Preload. Make item changes before profiles load.
[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public class CutContentMod(
    ISptLogger<CutContentMod> logger,
    GlobalTable globalTable,
    HideoutTable hideoutTable,
    BotTable botTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        EditGlobals();
        EditHideout();
        EditBots();
        logger.Success("CutContent finished editing the database!");
        return Task.CompletedTask;
    }

    private void EditGlobals()
    {
        logger.Info($"Scav cooldown: {globalTable.Configuration.SavagePlayCooldown}");
        // globalTable.Configuration.SavagePlayCooldown = 1;
        // TODO: Edit global game settings here
    }

    private void EditHideout()
    {
        logger.Info($"Hideout areas: {hideoutTable.Areas.Count}");
        // var areas = hideoutTable.Areas;
        // TODO: Edit hideout areas/stages here
    }

    private void EditBots()
    {
        logger.Info($"Bot types: {botTable.Types.Count}");
        // var botTypes = botTable.Types;
        // TODO: Edit bot settings here
    }
}
