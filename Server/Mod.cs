using SPTarkov.Server.Core.Models.Spt.Mod;

namespace ReClassic;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.mizmii.reclassic";
    public string Name { get; init; } = "Re-Classic";
    public string Author { get; init; } = "mizmii";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("1.3.1");
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.6");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; } = "";
    public string License { get; init; } = "Unlicense";
}
