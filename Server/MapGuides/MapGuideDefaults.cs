namespace ReClassic.MapGuides;

public static class MapGuideDefaults
{
    private static MapGuidePage Page(string title, string text, string image = "")
    {
        return new MapGuidePage { Title = title, Text = text, Image = image };
    }

    private static MapGuide Guide(string name, params MapGuidePage[] pages)
    {
        return new MapGuide { Name = name, Pages = pages.ToList() };
    }

    public static MapGuideConfig Create()
    {
        MapGuidePage factoryTimer = Page("Limited Time",
            "The factory is still a hot combat zone, even after the Contract Wars. The length of your stay may decide whether you get out alive or not.");
        MapGuide factory = Guide("Factory",
            Page("Overview",
                "Chemical Plant No. 16 in the Tarkov industrial zone. The TerraGroup-leased plant was used to make chemicals, for reasons that nobody truly knows about.\nAbsolute combat hotspot, with every engagement being purely CQB. There's barely any room the breathe in here."),
            Page("Getting Around",
                "The layout of the map is pretty straightforward. Most engagements occur in the old offices and locker rooms, now filled with corpses of old PMCs."),
            factoryTimer);
        MapGuide factoryNight = Guide("Factory (Night)",
            Page("Overview",
                "Pitch-black. Same as regular Factory, with something else roaming around in the darkness.."),
            factoryTimer);

        return new MapGuideConfig
        {
            Maps = new Dictionary<string, MapGuide>
            {
                ["bigmap"] = Guide("Customs",
                    Page("Overview",
                        "The customs terminal on the edge of Tarkov, with warehouses, the new and old gas stations, the dorms and the construction site, split by the river.\n\nThe dorms are the most crowded spot on the map. Scavs and other PMCs go there for the locked rooms and the loot inside."),
                    Page("Getting Out",
                        "Most exits on this map are on the edges, with some of them needing specific keys or payment. Look around for any significant landmarks in order to find an escape.")),
                ["factory4_day"] = factory,
                ["factory4_night"] = factoryNight,
                ["Woods"] = Guide("Woods",
                    Page("Overview",
                        "The Priozersk natural reserve: thick forest, a lumber mill, an abandoned village and a crashed plane. Long sight lines and lots of cover.\n\nThe sawmill is where the Shturman group camps."),
                    Page("Survival",
                        "Woods is extremely big, which also makes it the calmest map. It's also very easy to get lost. Don't fuck around too much, though. You can still get killed here just as fast as any other map.")),
                ["Shoreline"] = Guide("Shoreline",
                    Page("Overview",
                        "A stretch of coast with a village, a weather station, a power station and the Azure Coast health resort.\n\nThe resort is the map's main combat hotspot. Its east and west wings are full of locked rooms and extremely rare loot, but Sanitar's men guard it."),
                    Page("Survival",
                        "This map is filled with snipers and heavily geared players. It might be beneficial to take the long way around the map, just so you don't run into other players.")),
                ["Interchange"] = Guide("Interchange",
                    Page("Overview",
                        "The ULTRA shopping center on the highway interchange: three big stores and a long mall between them, mostly in the dark.\n\nTagilla's brother, Killa, roams around the stores inside of the mall. He almost never goes outside, always guarding the upper floors of the mall.")),
                ["RezervBase"] = Guide("Reserve",
                    Page("Overview",
                        "A secret State Reserve military base, with barracks, bunkers, hangars and the command building. After the Contract Wars, this place has been filled with raiders.\n\nThe bunkers connect a lot of the map together."),
                    Page("Limited Time",
                        "Of course a military base would be under attack still, especially in a place like Tarkov. Staying here for too long is a guaranteed death wish."),
                        Page("Extracts",
                        "Getting out of this map is a pain in the ass. Every exit needs some sort of special condition, most of them alerting the entire map.")),
                ["laboratory"] = Guide("The Lab",
                    Page("Overview",
                        "TerraGroup's secret underground lab beneath the center of Tarkov. Messy, but cleaner than any other place in the Norvinsk Region.\n\nYou need a keycard to get in. Every exit requires some sort of special condition, whether that be power or something else."),
                    Page("Limited Time",
                        "Rumour has it that Terragroup used to experiment on humans. God knows what they did with them, but you definitely don't want to stick around to find out.")),
                ["Lighthouse"] = Guide("Lighthouse",
                    Page("Overview",
                        "A rocky coastline with a lighthouse on the island, a water treatment plant held by Rogues, and the private road down to it.\n\nThe Rogues at the water treatment plant shoot at anyone who comes close."),
                    Page("Special Characters",
                        "According to locals, the old lighthouse keeper has come back to Tarkov after the Contract Wars.\nGetting in contact with him is practically impossible, unless you have the right connections.")),
                ["TarkovStreets"] = Guide("Streets of Tarkov",
                    Page("Overview",
                        "The city center: wrecked apartment blocks, a cinema, a hotel and offices.\n\nMost buildings can be entered. This map is probably the least special, but it does hold the BTR.")),
                ["Sandbox"] = Guide("Ground Zero",
                    Page("Overview",
                        "The business district where the conflict started. Glass office towers, the TerraGroup headquarters and blocked streets.\n\nA good place to learn the ropes before heading further into Tarkov.")),
                ["Sandbox_high"] = Guide("Ground Zero",
                    Page("Experience",
                        "You know this place by now, but it seems others have learned too. Loot is much better, but so are the people you fight."))
            }
        };
    }
}
