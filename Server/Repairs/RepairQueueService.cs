using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ReClassic.Repairs;

[Injectable(InjectionType.Singleton)]
public class RepairQueueService(
    ISptLogger<RepairQueueService> logger,
    ModHelper modHelper,
    FileUtil fileUtil,
    JsonUtil jsonUtil,
    TimeUtil timeUtil,
    ICloner cloner,
    ItemHelper itemHelper,
    InventoryHelper inventoryHelper,
    TraderHelper traderHelper,
    MailSendService mailSendService,
    EventOutputHolder eventOutputHolder,
    HttpResponseUtil httpResponseUtil)
{
    private readonly object _lock = new();
    private RepairState _state = new();
    private RepairConfig _config = new();
    private string _folder = "";
    private bool _loaded;
    private bool _dirty;

    private void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            _folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string configPath = System.IO.Path.Combine(_folder, "config", "repairs.json");
            try
            {
                if (fileUtil.FileExists(configPath))
                {
                    _config = jsonUtil.Deserialize<RepairConfig>(fileUtil.ReadFile(configPath)) ?? new RepairConfig();
                }
                fileUtil.WriteFile(configPath, jsonUtil.Serialize(_config, true) ?? "{}");
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read the repair config, using defaults: {e.Message}");
                _config = new RepairConfig();
            }
            try
            {
                if (fileUtil.FileExists(StatePath()))
                {
                    _state = jsonUtil.Deserialize<RepairState>(fileUtil.ReadFile(StatePath())) ?? new RepairState();
                }
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read saved repairs, starting fresh: {e.Message}");
                _state = new RepairState();
            }
        }
    }

    private string StatePath()
    {
        return System.IO.Path.Combine(_folder, "data", "repairs.json");
    }

    private void Save()
    {
        if (!_dirty)
        {
            return;
        }
        _dirty = false;
        try
        {
            fileUtil.WriteFile(StatePath(), jsonUtil.Serialize(_state) ?? "{}");
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not save repairs: {e.Message}");
        }
    }

    private double Quality(string traderId, MongoId sessionId)
    {
        double quality = traderHelper.GetTrader(new MongoId(traderId), sessionId)?.Repair?.Quality ?? 1;
        return quality <= 0 ? 1 : quality;
    }

    private long DurationSeconds(double amount, double quality)
    {
        double minutes = (_config.BaseMinutes + _config.MinutesPerPoint * Math.Max(0, amount)) * Math.Pow(1 / Math.Max(0.1, quality), _config.QualityExponent);
        minutes = Math.Clamp(minutes, _config.MinimumMinutes, Math.Max(_config.MinimumMinutes, _config.MaximumHours * 60));
        return (long)Math.Round(minutes * 60);
    }

    public ItemEventRouterResponse Send(PmcData pmc, RepairSendRequest request, MongoId sessionId)
    {
        EnsureLoaded();
        ItemEventRouterResponse output = eventOutputHolder.GetOutput(sessionId);
        lock (_lock)
        {
            if (!_config.Enabled)
            {
                return output;
            }
            if (string.IsNullOrEmpty(request.ItemId) || string.IsNullOrEmpty(request.TraderId))
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Invalid repair request.");
            }
            MongoId itemId = new MongoId(request.ItemId);
            Item? item = pmc.Inventory?.Items?.FirstOrDefault(i => i.Id == itemId);
            if (item == null)
            {
                return httpResponseUtil.AppendErrorToOutput(output, "Could not find that item in your stash.");
            }
            List<Item> items = cloner.Clone(pmc.Inventory!.Items!.GetItemWithChildren(itemId))!;
            Item root = items[0];
            root.ParentId = "hideout";
            root.SlotId = "hideout";
            root.Location = null;
            inventoryHelper.RemoveItem(pmc, itemId, sessionId, output);

            long now = timeUtil.GetTimeStamp();
            var job = new RepairJob
            {
                Id = new MongoId().ToString(),
                SessionId = sessionId.ToString(),
                TraderId = request.TraderId,
                Items = items,
                Durability = request.Durability,
                MaxDurability = request.MaxDurability,
                StartTime = now,
                EndTime = now + DurationSeconds(request.Amount, Quality(request.TraderId, sessionId))
            };
            _state.Jobs.Add(job);
            _dirty = true;
            Save();
            return output;
        }
    }

    public RepairListResponse List(MongoId sessionId)
    {
        EnsureLoaded();
        lock (_lock)
        {
            string session = sessionId.ToString();
            return new RepairListResponse
            {
                Enabled = _config.Enabled,
                ServerTime = timeUtil.GetTimeStamp(),
                BaseMinutes = _config.BaseMinutes,
                MinutesPerPoint = _config.MinutesPerPoint,
                QualityExponent = _config.QualityExponent,
                MinimumMinutes = _config.MinimumMinutes,
                MaximumHours = _config.MaximumHours,
                Jobs = _state.Jobs.Where(j => j.SessionId == session).OrderBy(j => j.EndTime).Select(j => new RepairJobView
                {
                    Id = j.Id,
                    TraderId = j.TraderId,
                    Template = j.Items.Count > 0 ? j.Items[0].Template.ToString() : "",
                    Durability = j.Durability,
                    MaxDurability = j.MaxDurability,
                    StartTime = j.StartTime,
                    EndTime = j.EndTime
                }).ToList()
            };
        }
    }

    public void Update()
    {
        EnsureLoaded();
        lock (_lock)
        {
            long now = timeUtil.GetTimeStamp();
            foreach (RepairJob job in _state.Jobs.Where(j => j.EndTime <= now).ToList())
            {
                job.Attempts++;
                if (Deliver(job))
                {
                    _state.Jobs.Remove(job);
                }
                _dirty = true;
            }
            Save();
        }
    }

    private bool Deliver(RepairJob job)
    {
        try
        {
            List<Item> items = cloner.Clone(job.Items)!.ReplaceIDs().ToList();
            items[0].ParentId = new MongoId().ToString();
            items[0].SlotId = "hideout";
            string name = itemHelper.GetItemName(job.Items[0].Template);
            string message = $"Your {name} has been repaired and is ready for you.";
            mailSendService.SendDirectNpcMessageToPlayer(new MongoId(job.SessionId), job.TraderId, MessageType.MessageWithItems, message, items, timeUtil.GetHoursAsSeconds(_config.MailStorageHours));
            return true;
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not return a repaired item, will retry: {e.Message}");
            return false;
        }
    }
}
