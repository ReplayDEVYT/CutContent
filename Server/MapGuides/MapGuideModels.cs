using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Utils;

namespace ReClassic.MapGuides;

public class MapGuidePage
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";
}

public class MapGuide
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("pages")]
    public List<MapGuidePage> Pages { get; set; } = new();
}

public class MapGuideConfig
{
    [JsonPropertyName("help")]
    public string Help { get; set; } = "Each key in maps is a location id (bigmap, factory4_day, factory4_night, Woods, Shoreline, Interchange, RezervBase, laboratory, Lighthouse, TarkovStreets, Sandbox, Sandbox_high, Labyrinth). Pages are shown in order. text supports TextMeshPro rich text like <b>, <i> and <color=#c5c3b2>. image is a .png or .jpg file name in the mapguides folder next to this mod's DLL, or empty for a text-only page. Changes are picked up without restarting the server.";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("maps")]
    public Dictionary<string, MapGuide> Maps { get; set; } = new();
}

public record MapGuideListRequest : IRequestData
{
}

public record MapGuideImageRequest : IRequestData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class MapGuideImageResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("data")]
    public string Data { get; set; } = "";
}
