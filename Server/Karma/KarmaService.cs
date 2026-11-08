using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace ReClassic.Karma;

[Injectable(InjectionType.Singleton)]
public class KarmaService(ISptLogger<KarmaService> logger, ModHelper modHelper, FileUtil fileUtil, JsonUtil jsonUtil, TimeUtil timeUtil)
{
    private const int HistoryLength = 50;
    private readonly object _lock = new();
    private KarmaState _state = new();
    private KarmaConfig _config = new();
    private string _folder = "";
    private bool _loaded;

    public static KarmaService? Instance { get; private set; }

    public KarmaConfig Config
    {
        get
        {
            EnsureLoaded();
            return _config;
        }
    }

    public void EnsureLoaded()
    {
        lock (_lock)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            Instance = this;
            _folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string configPath = System.IO.Path.Combine(_folder, "config", "karma.json");
            try
            {
                if (fileUtil.FileExists(configPath))
                {
                    _config = jsonUtil.Deserialize<KarmaConfig>(fileUtil.ReadFile(configPath)) ?? new KarmaConfig();
                }
                foreach (var pair in new KarmaConfig().Events)
                {
                    _config.Events.TryAdd(pair.Key, pair.Value);
                }
                fileUtil.WriteFile(configPath, jsonUtil.Serialize(_config, true) ?? "{}");
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read the karma config, using defaults: {e.Message}");
                _config = new KarmaConfig();
            }
            try
            {
                if (fileUtil.FileExists(StatePath()))
                {
                    _state = jsonUtil.Deserialize<KarmaState>(fileUtil.ReadFile(StatePath())) ?? new KarmaState();
                }
            }
            catch (Exception e)
            {
                logger.Error($"[Re-Classic] Could not read saved karma, starting fresh: {e.Message}");
                _state = new KarmaState();
            }
        }
    }

    private string StatePath()
    {
        return System.IO.Path.Combine(_folder, "data", "karma.json");
    }

    private void Save()
    {
        try
        {
            fileUtil.WriteFile(StatePath(), jsonUtil.Serialize(_state) ?? "{}");
        }
        catch (Exception e)
        {
            logger.Error($"[Re-Classic] Could not save karma: {e.Message}");
        }
    }

    public double Get(string profileId)
    {
        EnsureLoaded();
        if (!_config.Enabled || string.IsNullOrEmpty(profileId))
        {
            return 0;
        }
        lock (_lock)
        {
            return _state.Profiles.TryGetValue(profileId, out KarmaEntry? entry) ? entry.Value : 0;
        }
    }

    public KarmaInfo Info(string profileId)
    {
        EnsureLoaded();
        double karma = Get(profileId);
        return new KarmaInfo
        {
            Enabled = _config.Enabled,
            ProfileId = profileId,
            Karma = karma,
            Minimum = _config.Minimum,
            Maximum = _config.Maximum,
            Tier = Tier(karma),
            TraderPrice = TraderPrice(karma),
            FencePrice = FencePrice(karma),
            FenceSell = FenceSell(karma),
            FleaTax = FleaTax(karma),
            QuestExperience = QuestExperience(karma),
            Events = new Dictionary<string, double>(_config.Events)
        };
    }

    public KarmaManyResponse Many(IEnumerable<string> profileIds)
    {
        var response = new KarmaManyResponse();
        foreach (string id in profileIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(id))
            {
                response.Values[id] = Get(id);
            }
        }
        return response;
    }

    public KarmaInfo Add(string profileId, string eventName, int count)
    {
        EnsureLoaded();
        if (!_config.Enabled || string.IsNullOrEmpty(profileId) || string.IsNullOrEmpty(eventName) || !_config.Events.TryGetValue(eventName, out double points))
        {
            return Info(profileId);
        }
        double amount = points * Math.Clamp(count, 1, 50);
        double applied;
        lock (_lock)
        {
            if (!_state.Profiles.TryGetValue(profileId, out KarmaEntry? entry))
            {
                entry = new KarmaEntry();
                _state.Profiles[profileId] = entry;
            }
            double before = entry.Value;
            entry.Value = Math.Clamp(before + amount, _config.Minimum, _config.Maximum);
            applied = entry.Value - before;
            entry.History.Add(new KarmaChange { Time = timeUtil.GetTimeStamp(), Event = eventName, Amount = applied });
            if (entry.History.Count > HistoryLength)
            {
                entry.History.RemoveRange(0, entry.History.Count - HistoryLength);
            }
            Save();
        }
        logger.Info($"[Re-Classic] Karma {(applied >= 0 ? "+" : "")}{applied:0.#} for {profileId} ({eventName}), now {Get(profileId):0.#}.");
        KarmaInfo info = Info(profileId);
        info.Applied = applied;
        return info;
    }

    public string Tier(double karma)
    {
        if (karma <= -_config.ExtremeThreshold)
        {
            return "Very Low";
        }
        if (karma < -_config.NeutralBand)
        {
            return "Low";
        }
        if (karma <= _config.NeutralBand)
        {
            return "Neutral";
        }
        return karma < _config.ExtremeThreshold ? "High" : "Very High";
    }

    private double Low(double karma)
    {
        double span = Math.Max(1, -_config.Minimum - _config.NeutralBand);
        return Math.Clamp((-karma - _config.NeutralBand) / span, 0, 1);
    }

    private double High(double karma)
    {
        double span = Math.Max(1, _config.Maximum - _config.NeutralBand);
        return Math.Clamp((karma - _config.NeutralBand) / span, 0, 1);
    }

    public double TraderPrice(double karma)
    {
        return 1 + Low(karma) * _config.TraderPriceIncrease - High(karma) * _config.TraderPriceDiscount;
    }

    public double FencePrice(double karma)
    {
        return 1 - Low(karma) * _config.FencePriceDiscount;
    }

    public double FenceSell(double karma)
    {
        return 1 + Low(karma) * _config.FenceSellBonus - High(karma) * _config.FenceSellPenalty;
    }

    public double FleaTax(double karma)
    {
        return 1 + Low(karma) * _config.FleaTaxIncrease - High(karma) * _config.FleaTaxDiscount;
    }

    public double QuestExperience(double karma)
    {
        return 1 - Low(karma) * _config.QuestPenalty + High(karma) * _config.QuestBonus;
    }

    public double BuyMultiplier(string profileId, MongoId traderId)
    {
        double karma = Get(profileId);
        return traderId == Traders.FENCE ? FencePrice(karma) : TraderPrice(karma);
    }
}
