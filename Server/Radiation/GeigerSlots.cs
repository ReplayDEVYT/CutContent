using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ReClassic.Radiation;

[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class GeigerSlots(ISptLogger<GeigerSlots> logger, TemplateTable templates) : IOnLoad
{
    private const string Geiger = "5672cb724bdc2dc2088b456b";
    private const string Analyzer = "590a3efd86f77437d351a25b";
    private const string SpecialSlotMarker = "6655e35b6bc645cb7b059912";

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            MongoId geiger = new MongoId(Geiger);
            MongoId analyzer = new MongoId(Analyzer);
            MongoId marker = new MongoId(SpecialSlotMarker);
            int slots = 0;
            foreach (TemplateItem item in templates.Items.Values)
            {
                if (item.Properties?.Slots == null)
                {
                    continue;
                }
                foreach (Slot slot in item.Properties.Slots)
                {
                    if (slot.Properties?.Filters == null)
                    {
                        continue;
                    }
                    foreach (SlotFilter filter in slot.Properties.Filters)
                    {
                        if (filter.Filter != null && filter.Filter.Contains(marker))
                        {
                            if (filter.Filter.Add(geiger))
                            {
                                slots++;
                            }
                            filter.Filter.Add(analyzer);
                        }
                    }
                }
            }
            logger.Info($"[Re-Classic] The Geiger-Muller counter and Gas analyzer now fit in {slots} special slots.");
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not add the Geiger-Muller counter and Gas analyzer to the special slots: {e.Message}");
        }
        return Task.CompletedTask;
    }
}
