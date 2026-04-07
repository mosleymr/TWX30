using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TWXProxy.Core;

public static class NativeHaggleModes
{
    public const string Baseline = "baseline";
    public const string BlendHeuristic = "blend-heuristic";
    public const string ClampHeuristic = "clamp-heuristic";
    public const string ServerDerived = "server-derived";
    public const string ExcellentTarget = "excellent-target";
    public const string Default = ClampHeuristic;

    public static string Normalize(string? mode)
    {
        string normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return Default;

        return normalized switch
        {
            "ephaggle" => ClampHeuristic,
            "clamp heuristic" => ClampHeuristic,
            Baseline => Baseline,
            "blend heuristic" => BlendHeuristic,
            BlendHeuristic => BlendHeuristic,
            "enhanced haggle" => ServerDerived,
            "server derived" => ServerDerived,
            ClampHeuristic => ClampHeuristic,
            ServerDerived => ServerDerived,
            ExcellentTarget => ExcellentTarget,
            _ => normalized,
        };
    }

    public static bool IsBuiltIn(string? mode)
    {
        string normalized = Normalize(mode);
        return normalized == ClampHeuristic ||
               normalized == BlendHeuristic ||
               normalized == ServerDerived ||
               normalized == Baseline;
    }

    public static IReadOnlyList<string> All { get; } = new[]
    {
        ClampHeuristic,
        BlendHeuristic,
        ServerDerived,
        Baseline,
    };

    public static IReadOnlyList<NativeHaggleModeInfo> BuiltInModes => NativeHaggleModeCatalog.GetBuiltIns();
}

public sealed class NativeHaggleEngine
{
    internal sealed class Candidate
    {
        public int Mcic { get; set; }
        public int BaseVar { get; set; }
        public double Variance { get; set; }
        public int Productivity { get; set; }
        public double ExactPrice { get; set; }
    }

    internal sealed class SessionState
    {
        public int Sector { get; set; }
        public string RouteKey { get; set; } = string.Empty;
        public string ActiveMode { get; set; } = string.Empty;
        public string Weekday { get; set; } = "Sat";
        public string ProductKey { get; set; } = string.Empty;
        public ProductType ProductType { get; set; }
        public string BuySell { get; set; } = string.Empty; // Port perspective: SELLING or BUYING
        public int PortQty { get; set; }
        public int Percent { get; set; }
        public int TradeQty { get; set; }
        public int Experience { get; set; } = 1000;
        public int PlusMinus { get; set; }
        public int McicStep { get; set; }
        public double BasePrice { get; set; }
        public double ProductFactor { get; set; }
        public int BaseVarMin { get; set; }
        public int BaseVarMax { get; set; }
        public int LowProductivity { get; set; }
        public int HighProductivity { get; set; }
        public int CalculatedLowProductivity { get; set; }
        public int MaxProductivity { get; set; }
        public int DefaultMcicMin { get; set; }
        public int DefaultMcicMax { get; set; }
        public int McicMin { get; set; }
        public int McicMax { get; set; }
        public int DeriveFailures { get; set; }
        public bool UseLowPercentDerive { get; set; }
        public int BidNumber { get; set; }
        public long LastCounter { get; set; }
        public long LastOffer { get; set; }
        public bool FinalOffer { get; set; }
        public bool HeuristicFallback { get; set; }
        public long StartCredits { get; set; }
        public int StartEmptyHolds { get; set; }
        public int StartFuelOre { get; set; }
        public int StartOrganics { get; set; }
        public int StartEquipment { get; set; }
        public int StartProductQty { get; set; }
        public DateTime PortReportUpdate { get; set; }
        public double PortReportAgeDays { get; set; }
        public int PortMaxQty { get; set; }
        public long PendingBid { get; set; }
        public long PendingBidOffer { get; set; }
        public bool PendingBidFinalOffer { get; set; }
        public bool OutcomeRecorded { get; set; }
        public string RewardTier { get; set; } = string.Empty;
        public int RewardExperience { get; set; }
        public int FinalTargetNudgeApplied { get; set; }
        public bool FirstOfferExactHitApplied { get; set; }
        public bool EmpiricalProbeApplied { get; set; }
        public int EmpiricalProbeNudge { get; set; }
        public double HiddenTotalMin { get; set; }
        public double HiddenTotalMax { get; set; }
        public int HiddenTotalAppliedBidNumber { get; set; }
        public List<Candidate> Candidates { get; } = new();

        public bool HasHiddenTotalRange => HiddenTotalMin > 0 && HiddenTotalMax > 0 && HiddenTotalMin <= HiddenTotalMax;
    }

    private sealed class RetryHint
    {
        public int Sector { get; set; }
        public string ProductKey { get; set; } = string.Empty;
        public string BuySell { get; set; } = string.Empty;
    }

    internal enum ServerProbeBranch
    {
        HiddenOverBid,
        BidOverHidden,
        Overlap,
    }

    private static readonly Regex RxCommandPrompt = new(@"command \[tl=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxCommerceReport = new(
        @"^Commerce report for .+?:\s+\d{1,2}:\d{2}:\d{2}\s+(?:AM|PM)\s+([A-Za-z]{3,5})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxCredits = new(
        @"^You have ([\d,]+) credits and (\d+) empty cargo holds\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxExpGain = new(
        @"receive (\d+) experience point",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxTradeSuccess = new(
        @"^For your (good|great|excellent) trading you receive ([\d,]+) experience point",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxHoldPrompt = new(
        @"^How many holds of (Fuel Ore|Organics|Equipment) do you want to (buy|sell) \[(\d+)\]\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxAgreed = new(
        @"^Agreed,\s+([\d,]+)\s+units\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxSellOffer = new(
        @"^We'll sell them for ([\d,]+) credits\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxBuyOffer = new(
        @"^We'll buy them for ([\d,]+) credits\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxFinalOffer = new(
        @"^Our final offer is ([\d,]+) credits\.",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RxYourOfferPrompt = new(
        @"^Your offer \[([\d,]+)\]\s*\?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ShipInfoParser _shipInfoParser = new();
    private ShipStatus _shipStatus = new();
    private SessionState? _session;
    private string? _pendingProductKey;
    private ProductType _pendingProductType;
    private string? _pendingBuySell;
    private long _lastKnownCredits;
    private bool _hasLastKnownCredits;
    private int _lastKnownEmptyHolds;
    private bool _hasLastKnownEmptyHolds;
    private int _lastKnownExperience = 1000;
    private RetryHint? _retryHint;
    private int _completedHaggles;
    private int _successfulHaggles;
    private int _goodRewardCount;
    private int _greatRewardCount;
    private int _excellentRewardCount;
    private string _firstBidMode = NativeHaggleModes.ClampHeuristic;
    private readonly Dictionary<string, NativeHaggleModeExtension> _extensionModes = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastMissingModeId;

    public event Action? StatsChanged;

    public int CompletedHaggles => _completedHaggles;

    public int SuccessfulHaggles => _successfulHaggles;

    public int GoodRewardCount => _goodRewardCount;

    public int GreatRewardCount => _greatRewardCount;

    public int ExcellentRewardCount => _excellentRewardCount;

    public int SuccessRatePercent =>
        _completedHaggles <= 0
            ? 0
            : (int)Math.Round((_successfulHaggles * 100.0) / _completedHaggles, MidpointRounding.AwayFromZero);

    public string FirstBidMode => _firstBidMode;

    public IReadOnlyList<NativeHaggleModeInfo> AvailableModes => NativeHaggleModeCatalog.GetAvailableModes(_extensionModes.Values);

    public NativeHaggleEngine()
    {
        _shipInfoParser.Updated += status =>
        {
            _shipStatus = CloneStatus(status);
            if (_shipStatus.Credits >= 0)
            {
                _lastKnownCredits = _shipStatus.Credits;
                _hasLastKnownCredits = true;
            }
            if (_shipStatus.HoldsEmpty >= 0)
            {
                _lastKnownEmptyHolds = (int)_shipStatus.HoldsEmpty;
                _hasLastKnownEmptyHolds = true;
            }
            if (_shipStatus.Experience > 0)
                _lastKnownExperience = (int)_shipStatus.Experience;
        };
    }

    public bool Enabled { get; private set; }

    public event Action<bool>? EnabledChanged;

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;

        Enabled = enabled;
        if (!enabled)
            Reset("disabled");
        EnabledChanged?.Invoke(enabled);
    }

    public bool Toggle()
    {
        SetEnabled(!Enabled);
        return Enabled;
    }

    public void SetFirstBidMode(string? mode)
    {
        _firstBidMode = NativeHaggleModes.Normalize(mode);
    }

    internal void RegisterMode(NativeHaggleModeExtension mode)
    {
        string modeId = NativeHaggleModes.Normalize(mode.ModeInfo.Id);
        _extensionModes[modeId] = mode;
        if (string.Equals(_lastMissingModeId, modeId, StringComparison.OrdinalIgnoreCase))
            _lastMissingModeId = null;
    }

    internal void UnregisterMode(string? modeId)
    {
        string normalized = NativeHaggleModes.Normalize(modeId);
        _extensionModes.Remove(normalized);
    }

    public static bool IsOfferLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return RxSellOffer.IsMatch(line) ||
               RxBuyOffer.IsMatch(line) ||
               RxFinalOffer.IsMatch(line);
    }

    public string? HandleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        _shipInfoParser.FeedLine(line);
        UpdatePassiveState(line);

        if (RxCommandPrompt.IsMatch(line))
        {
            Reset("command-prompt");
            return null;
        }

        if (!Enabled)
            return null;

        if (line.Equals("<Port>", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Docking...", StringComparison.OrdinalIgnoreCase))
        {
            StartPortSession();
            return null;
        }

        Match commerceMatch = RxCommerceReport.Match(line);
        if (commerceMatch.Success)
        {
            EnsureSession();
            if (_session != null)
                _session.Weekday = NormalizeWeekday(commerceMatch.Groups[1].Value);
            return null;
        }

        Match holdMatch = RxHoldPrompt.Match(line);
        if (holdMatch.Success)
        {
            ParseHoldPrompt(holdMatch);
            return null;
        }

        Match agreedMatch = RxAgreed.Match(line);
        if (agreedMatch.Success)
        {
            ArmSession(ParseInt(agreedMatch.Groups[1].Value));
            return null;
        }

        Match initialSell = RxSellOffer.Match(line);
        if (initialSell.Success)
        {
            GlobalModules.DebugLog($"[NativeHaggle] Offer line SELLING: '{line}'\n");
            return HandleOffer(ParseLong(initialSell.Groups[1].Value), "SELLING", finalOffer: false);
        }

        Match initialBuy = RxBuyOffer.Match(line);
        if (initialBuy.Success)
        {
            GlobalModules.DebugLog($"[NativeHaggle] Offer line BUYING: '{line}'\n");
            return HandleOffer(ParseLong(initialBuy.Groups[1].Value), "BUYING", finalOffer: false);
        }

        Match finalMatch = RxFinalOffer.Match(line);
        if (finalMatch.Success)
        {
            GlobalModules.DebugLog($"[NativeHaggle] Offer line FINAL: '{line}'\n");
            return HandleOffer(ParseLong(finalMatch.Groups[1].Value), _session?.BuySell ?? string.Empty, finalOffer: true);
        }

        Match promptMatch = RxYourOfferPrompt.Match(line);
        if (promptMatch.Success)
        {
            return HandleOfferPrompt(ParseLong(promptMatch.Groups[1].Value));
        }

        return null;
    }

    public void SuppressCurrentTrade(string reason)
    {
        GlobalModules.DebugLog($"[NativeHaggle] SuppressCurrentTrade('{reason}') ignored while suppression is disabled.\n");
    }

    public void ObserveScriptSend(string text)
    {
        // Suppression is temporarily disabled for debugging.
    }

    private void UpdatePassiveState(string line)
    {
        Match tradeSuccessMatch = RxTradeSuccess.Match(line);
        if (tradeSuccessMatch.Success)
        {
            string rewardTier = tradeSuccessMatch.Groups[1].Value.Trim().ToLowerInvariant();
            int rewardExperience = ParseInt(tradeSuccessMatch.Groups[2].Value);
            if (_session != null)
            {
                _session.RewardTier = rewardTier;
                _session.RewardExperience = rewardExperience;
            }

            switch (rewardTier)
            {
                case "excellent":
                    _excellentRewardCount++;
                    break;
                case "great":
                    _greatRewardCount++;
                    break;
                case "good":
                    _goodRewardCount++;
                    break;
            }

            GlobalModules.DebugLog(
                $"[NativeHaggle] Reward line tier='{rewardTier}' exp={rewardExperience} text='{line}'\n");
            RecordOutcome(success: true, $"trade-success-line:{rewardTier}");
        }

        Match creditsMatch = RxCredits.Match(line);
        if (creditsMatch.Success)
        {
            long credits = ParseLong(creditsMatch.Groups[1].Value);
            int emptyHolds = ParseInt(creditsMatch.Groups[2].Value);
            _lastKnownCredits = credits;
            _hasLastKnownCredits = true;
            _lastKnownEmptyHolds = emptyHolds;
            _hasLastKnownEmptyHolds = true;
            ProcessCreditsLine(credits, emptyHolds);
            return;
        }

        Match expMatch = RxExpGain.Match(line);
        if (expMatch.Success)
        {
            _lastKnownExperience += ParseInt(expMatch.Groups[1].Value);
        }
    }

    private void StartPortSession()
    {
        _session = new SessionState
        {
            Sector = GlobalModules.GlobalAutoRecorder.CurrentSector,
            Weekday = "Sat",
        };
        _pendingProductKey = null;
        _pendingBuySell = null;
    }

    private void EnsureSession()
    {
        if (_session == null)
            StartPortSession();
    }

    private void ParseHoldPrompt(Match holdMatch)
    {
        EnsureSession();
        _pendingProductKey = ProductKeyFromPrompt(holdMatch.Groups[1].Value);
        _pendingProductType = ProductTypeFromKey(_pendingProductKey);
        string action = holdMatch.Groups[2].Value.ToUpperInvariant();
        _pendingBuySell = action == "BUY" ? "SELLING" : "BUYING";
    }

    private void ArmSession(int tradeQty)
    {
        EnsureSession();
        if (_session == null || string.IsNullOrEmpty(_pendingProductKey) || string.IsNullOrEmpty(_pendingBuySell))
            return;

        ModDatabase? db = ScriptRef.GetActiveDatabase();
        int sector = GlobalModules.GlobalAutoRecorder.CurrentSector;
        SectorData? sectorData = db?.GetSector(sector);
        Port? port = sectorData?.SectorPort;
        if (port == null)
        {
            GlobalModules.DebugLog($"[NativeHaggle] No port data for sector {sector}, manual haggle required.\n");
            Reset("missing-port-data");
            return;
        }

        _session.Sector = sector;
        _session.ProductKey = _pendingProductKey;
        _session.ProductType = _pendingProductType;
        _session.BuySell = _pendingBuySell;
        _session.RouteKey = BuildRouteKey(sector, _pendingProductKey, _pendingBuySell);
        _session.ActiveMode = GetActiveFirstBidMode();
        _session.TradeQty = tradeQty;
        _session.PortQty = port.ProductAmount.GetValueOrDefault(_pendingProductType);
        _session.Percent = port.ProductPercent.GetValueOrDefault(_pendingProductType);
        _session.PortReportUpdate = port.Update;
        _session.PortReportAgeDays = port.Update == default
            ? 0
            : Math.Max(0, (DateTime.Now - port.Update).TotalDays);
        _session.PortMaxQty = 0;
        _session.Experience = ResolveExperience();
        _session.BidNumber = 0;
        _session.LastCounter = 0;
        _session.LastOffer = 0;
        _session.FinalOffer = false;
        _session.HeuristicFallback = false;
        _session.DeriveFailures = 0;
        _session.PendingBid = 0;
        _session.PendingBidOffer = 0;
        _session.PendingBidFinalOffer = false;
        _session.OutcomeRecorded = false;
        _session.FinalTargetNudgeApplied = 0;
        _session.FirstOfferExactHitApplied = false;
        _session.RewardTier = string.Empty;
        _session.RewardExperience = 0;
        _session.EmpiricalProbeApplied = false;
        _session.EmpiricalProbeNudge = 0;
        _session.HiddenTotalMin = 0;
        _session.HiddenTotalMax = 0;
        _session.HiddenTotalAppliedBidNumber = 0;
        _session.StartCredits = ResolveStartingCredits();
        _session.StartEmptyHolds = ResolveStartingEmptyHolds();
        _session.StartFuelOre = (int)_shipStatus.FuelOre;
        _session.StartOrganics = (int)_shipStatus.Organics;
        _session.StartEquipment = (int)_shipStatus.Equipment;
        _session.StartProductQty = ResolveShipProductQty(_pendingProductType);
        _session.Candidates.Clear();

        PushScriptState(_session.StartCredits, abort: false);

        ConfigureProductConstants(_session);
        if (!PrepareRanges(_session, db))
        {
            Reset("unable-to-prepare");
            return;
        }
        _session.PortMaxQty = Math.Max(_session.PortQty, _session.MaxProductivity * 10);

        if (RetryHintMatches(_session))
        {
            _session.HeuristicFallback = true;
            GlobalModules.DebugLog(
                $"[NativeHaggle] Using heuristic-first retry mode for sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell}\n");
        }
        else if (_retryHint != null)
        {
            _retryHint = null;
        }

        GlobalModules.DebugLog(
            $"[NativeHaggle] Armed sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell} activeMode={_session.ActiveMode} qty={_session.TradeQty} portQty={_session.PortQty} percent={_session.Percent} portMaxQty={_session.PortMaxQty} reportAgeHours={_session.PortReportAgeDays * 24.0:0.00} exp={_session.Experience} weekday={_session.Weekday} lowProd={_session.LowProductivity} highProd={_session.HighProductivity} mcicMin={_session.McicMin} mcicMax={_session.McicMax} {DescribeStartCargoSnapshot(_session)}\n");
    }

    private void ProcessCreditsLine(long credits, int emptyHolds)
    {
        if (_session == null)
            return;

        PushScriptState(credits, abort: false);

        bool attemptedTrade = _session.BidNumber > 0 || _session.LastCounter > 0;
        if (!attemptedTrade)
            return;

        if (_session.OutcomeRecorded)
        {
            if (RetryHintMatches(_session))
                _retryHint = null;
            return;
        }

        bool success = IsSuccessfulTrade(_session, credits, emptyHolds);
        if (success)
        {
            if (RetryHintMatches(_session))
                _retryHint = null;
            return;
        }

        GlobalModules.DebugLog(
            $"[NativeHaggle] No transaction detected sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell} startCredits={_session.StartCredits} endCredits={credits} startEmpty={_session.StartEmptyHolds} endEmpty={emptyHolds} bidNumber={_session.BidNumber} lastOffer={_session.LastOffer} lastCounter={_session.LastCounter} {DescribeStartCargoSnapshot(_session)}\n");
        if (_session.BidNumber <= 1)
        {
            _retryHint = new RetryHint
            {
                Sector = _session.Sector,
                ProductKey = _session.ProductKey,
                BuySell = _session.BuySell,
            };
            GlobalModules.DebugLog(
                $"[NativeHaggle] Recorded retry hint for sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell}\n");
        }
        RecordOutcome(success: false, "credits-no-transaction");
        PushScriptState(credits, abort: true);
    }

    private static bool IsSuccessfulTrade(SessionState session, long credits, int emptyHolds)
    {
        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
        {
            return credits < session.StartCredits || emptyHolds < session.StartEmptyHolds;
        }

        if (string.Equals(session.BuySell, "BUYING", StringComparison.OrdinalIgnoreCase))
        {
            return credits > session.StartCredits || emptyHolds > session.StartEmptyHolds;
        }

        return true;
    }

    private bool RetryHintMatches(SessionState session)
    {
        if (_retryHint == null)
            return false;

        return _retryHint.Sector == session.Sector &&
               string.Equals(_retryHint.ProductKey, session.ProductKey, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(_retryHint.BuySell, session.BuySell, StringComparison.OrdinalIgnoreCase);
    }

    private void PushScriptState(long credits, bool abort)
    {
        ScriptRef.SetVarOnActiveScripts("$HAGGLE~CREDITS", credits.ToString(CultureInfo.InvariantCulture));
        ScriptRef.SetVarOnActiveScripts("$HAGGLE~ABORT", abort ? "1" : "0");
    }

    private long ResolveStartingCredits()
    {
        if (_hasLastKnownCredits)
            return _lastKnownCredits;
        if (_shipStatus.Credits >= 0)
            return _shipStatus.Credits;
        return 0;
    }

    private int ResolveStartingEmptyHolds()
    {
        if (_hasLastKnownEmptyHolds)
            return _lastKnownEmptyHolds;
        if (_shipStatus.HoldsEmpty >= 0)
            return (int)_shipStatus.HoldsEmpty;

        long totalCargo = _shipStatus.FuelOre + _shipStatus.Organics + _shipStatus.Equipment + _shipStatus.Colonists;
        if (_shipStatus.TotalHolds > 0)
        {
            long empty = _shipStatus.TotalHolds - totalCargo;
            return empty < 0 ? 0 : (int)empty;
        }

        return 0;
    }

    private int ResolveShipProductQty(ProductType productType) => productType switch
    {
        ProductType.FuelOre => (int)_shipStatus.FuelOre,
        ProductType.Organics => (int)_shipStatus.Organics,
        _ => (int)_shipStatus.Equipment,
    };

    private string? HandleOffer(long offer, string buySell, bool finalOffer)
    {
        if (_session == null || _session.TradeQty <= 0)
            return null;

        if (!string.IsNullOrEmpty(buySell) &&
            !string.Equals(_session.BuySell, buySell, StringComparison.OrdinalIgnoreCase))
        {
            GlobalModules.DebugLog($"[NativeHaggle] Offer mode mismatch session={_session.BuySell} line={buySell}, resetting.\n");
            Reset("mode-mismatch");
            return null;
        }

        _session.FinalOffer = finalOffer;
        if (_session.HeuristicFallback)
        {
            long heuristicBid = ComputeHeuristicBid(_session, offer);
            StageBid(_session, offer, heuristicBid, finalOffer);

            GlobalModules.DebugLog(
                $"[NativeHaggle] heuristic offer={offer} final={finalOffer} stagedBid={heuristicBid}\n");
            return null;
        }

        if (_session.BidNumber == 0)
        {
            if (!DeriveCandidates(_session, offer))
            {
                if (TryEnableHeuristicFallback(_session, offer, "derive-failed"))
                {
                    long heuristicBid = ComputeHeuristicBid(_session, offer);
                    StageBid(_session, offer, heuristicBid, finalOffer);

                    GlobalModules.DebugLog(
                        $"[NativeHaggle] heuristic offer={offer} final={finalOffer} stagedBid={heuristicBid}\n");
                    return null;
                }

                GlobalModules.DebugLog($"[NativeHaggle] Derive failed for sector={_session.Sector} product={_session.ProductKey}, manual haggle required.\n");
                Reset("derive-failed");
                return null;
            }
        }
        else
        {
            if (!FilterCandidates(_session, offer))
            {
                if (TryEnableHeuristicFallback(_session, offer, "filter-failed"))
                {
                    long heuristicBid = ComputeHeuristicBid(_session, offer);
                    StageBid(_session, offer, heuristicBid, finalOffer);

                    GlobalModules.DebugLog(
                        $"[NativeHaggle] heuristic offer={offer} final={finalOffer} stagedBid={heuristicBid}\n");
                    return null;
                }

                GlobalModules.DebugLog($"[NativeHaggle] Candidate filter failed for sector={_session.Sector} product={_session.ProductKey}, manual haggle required.\n");
                Reset("filter-failed");
                return null;
            }
        }

        UpdateHiddenTotalTracker(_session);

        long bid = ComputeBid(_session, offer, _session.ActiveMode);
        StageBid(_session, offer, bid, finalOffer);

        GlobalModules.DebugLog(
            $"[NativeHaggle] offer={offer} final={finalOffer} candidates={_session.Candidates.Count} stagedBid={bid}\n");
        return null;
    }

    private string? HandleOfferPrompt(long offer)
    {
        if (_session == null || _session.PendingBid <= 0 || _session.PendingBidOffer <= 0)
            return null;

        if (offer != _session.PendingBidOffer)
            return null;

        long bid = _session.PendingBid;
        bool finalOffer = _session.PendingBidFinalOffer;
        _session.PendingBid = 0;
        _session.PendingBidOffer = 0;
        _session.PendingBidFinalOffer = false;

        _session.BidNumber++;
        _session.LastCounter = bid;
        _session.LastOffer = offer;
        string probe = DescribePredictedProbe(_session, bid);

        GlobalModules.DebugLog(
            $"[NativeHaggle] Prompt offer={offer} final={finalOffer} bidNumber={_session.BidNumber} bid={bid} {probe}\n");
        return bid.ToString(CultureInfo.InvariantCulture);
    }

    private static void StageBid(SessionState session, long offer, long bid, bool finalOffer)
    {
        session.PendingBid = bid;
        session.PendingBidOffer = offer;
        session.PendingBidFinalOffer = finalOffer;
    }

    private static void UpdateHiddenTotalTracker(SessionState session)
    {
        ApplyAcceptedBidToHiddenTotalTracker(session);

        if (session.HasHiddenTotalRange)
            return;

        (double hiddenMin, double hiddenMax) = GetHiddenTotalRangeFromCandidates(session);
        if (hiddenMin <= 0 || hiddenMax <= 0 || hiddenMin > hiddenMax)
            return;

        session.HiddenTotalMin = hiddenMin;
        session.HiddenTotalMax = hiddenMax;

        GlobalModules.DebugLog(
            $"[NativeHaggle] Hidden tracker init total={hiddenMin:0.000000}..{hiddenMax:0.000000} candidates={session.Candidates.Count}\n");
    }

    private static void ApplyAcceptedBidToHiddenTotalTracker(SessionState session)
    {
        if (!session.HasHiddenTotalRange || session.BidNumber <= 0 || session.LastCounter <= 0)
            return;

        if (session.HiddenTotalAppliedBidNumber >= session.BidNumber)
            return;

        double bid = session.LastCounter;
        session.HiddenTotalMin = AdvanceServerHiddenTotal(session.HiddenTotalMin, bid);
        session.HiddenTotalMax = AdvanceServerHiddenTotal(session.HiddenTotalMax, bid);
        session.HiddenTotalAppliedBidNumber = session.BidNumber;

        GlobalModules.DebugLog(
            $"[NativeHaggle] Hidden tracker advance bidNumber={session.BidNumber} lastCounter={session.LastCounter} total={session.HiddenTotalMin:0.000000}..{session.HiddenTotalMax:0.000000}\n");
    }

    private static double AdvanceServerHiddenTotal(double priorTotal, double acceptedBid)
    {
        // Mirrors the 0x004594F5 hidden-basis update: 0.7 * priorTotal + 0.3 * acceptedBid.
        return (priorTotal * 0.7) + (acceptedBid * 0.3);
    }

    private static (double MinTotal, double MaxTotal) GetHiddenTotalRangeFromCandidates(SessionState session)
    {
        if (session.Candidates.Count == 0 || session.TradeQty <= 0 || session.Percent <= 0)
            return (0, 0);

        double minTotal = double.MaxValue;
        double maxTotal = double.MinValue;
        double baseCommodity = GetServerCommodityBasePrice(session);

        foreach (Candidate candidate in session.Candidates)
        {
            double signedTrade = candidate.Mcic;
            foreach (int adjustedQty in GetServerSeedQuantityCandidates(session, signedTrade))
            {
                double adjustment = signedTrade < 0
                    ? (((((session.Percent * 10.0) - adjustedQty) * signedTrade) / session.Percent) / 1000.0)
                    : ((((adjustedQty * signedTrade) / session.Percent) / 1000.0));

                double basisPerUnit = (baseCommodity * (1.0 - adjustment)) + 0.5;
                if (basisPerUnit <= 0)
                    continue;

                double hiddenTotal = basisPerUnit * session.TradeQty;
                if (hiddenTotal < minTotal)
                    minTotal = hiddenTotal;
                if (hiddenTotal > maxTotal)
                    maxTotal = hiddenTotal;
            }
        }

        if (minTotal == double.MaxValue || maxTotal == double.MinValue)
            return (0, 0);

        return (minTotal, maxTotal);
    }

    private static IReadOnlyList<int> GetServerSeedQuantityCandidates(SessionState session, double signedTrade)
    {
        List<int> quantities = new(16);
        AddUniqueQuantity(quantities, session.PortQty);

        if (session.Percent <= 0)
            return quantities;

        int capQty = Math.Max(0, session.Percent * 10);
        AddUniqueQuantity(quantities, capQty);

        if (string.Equals(session.ProductKey, "FUEL", StringComparison.OrdinalIgnoreCase))
            return quantities;

        foreach (double factor1 in GetServerSeedFactor1Candidates(session))
        {
            foreach (double factor2 in GetServerSeedFactor2Candidates())
            {
                double deltaBase = session.Percent * factor1 * factor2;
                AddAdjustedSeedQuantityVariants(quantities, session.PortQty, capQty, signedTrade, deltaBase);
            }
        }

        return quantities;
    }

    private static IReadOnlyList<double> GetServerSeedFactor1Candidates(SessionState session)
    {
        double baseFactor = string.Equals(session.ProductKey, "ORGANICS", StringComparison.OrdinalIgnoreCase) ? 0.2 : 0.3;

        return new[]
        {
            baseFactor,
            Math.Max(baseFactor, 0.25),
            Math.Max(baseFactor, 1.0 / 3.0),
            Math.Max(baseFactor, 0.5),
            Math.Max(baseFactor, 2.0 / 3.0),
            Math.Max(baseFactor, 1.0),
        };
    }

    private static IReadOnlyList<double> GetServerSeedFactor2Candidates() => new[]
    {
        0.5,
        0.75,
        1.0,
        1.25,
        1.5,
    };

    private static void AddAdjustedSeedQuantityVariants(List<int> quantities, int rawQty, int capQty, double signedTrade, double deltaBase)
    {
        if (deltaBase < 0)
            return;

        int rounded = Math.Max(0, (int)Math.Round(deltaBase, MidpointRounding.AwayFromZero));
        int floored = Math.Max(0, (int)Math.Floor(deltaBase));
        int ceiled = Math.Max(0, (int)Math.Ceiling(deltaBase));

        AddAdjustedSeedQuantity(quantities, rawQty, capQty, signedTrade, floored);
        AddAdjustedSeedQuantity(quantities, rawQty, capQty, signedTrade, rounded);
        AddAdjustedSeedQuantity(quantities, rawQty, capQty, signedTrade, ceiled);

        if (rounded > 0)
        {
            AddAdjustedSeedQuantity(quantities, rawQty, capQty, signedTrade, rounded - 1);
            AddAdjustedSeedQuantity(quantities, rawQty, capQty, signedTrade, rounded + 1);
        }
    }

    private static void AddAdjustedSeedQuantity(List<int> quantities, int rawQty, int capQty, double signedTrade, int shift)
    {
        int adjustedQty = signedTrade < 0
            ? rawQty - shift
            : rawQty + shift;

        if (adjustedQty < 0)
            adjustedQty = 0;

        if (adjustedQty > capQty)
            adjustedQty = capQty;

        AddUniqueQuantity(quantities, adjustedQty);
    }

    private static void AddUniqueQuantity(List<int> quantities, int quantity)
    {
        if (!quantities.Contains(quantity))
            quantities.Add(quantity);
    }

    private static double GetServerCommodityBasePrice(SessionState session) => session.ProductKey switch
    {
        "FUEL" => 25.0,
        "ORGANICS" => 50.0,
        _ => 90.0,
    };

    private static string DescribeStartCargoSnapshot(SessionState session) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"startFuel={session.StartFuelOre} startOrg={session.StartOrganics} startEqu={session.StartEquipment} startProduct={session.StartProductQty} startEmpty={session.StartEmptyHolds}");

    private static ShipStatus CloneStatus(ShipStatus status) => new()
    {
        TraderName = status.TraderName,
        Experience = status.Experience,
        Alignment = status.Alignment,
        AlignText = status.AlignText,
        CurrentSector = status.CurrentSector,
        Turns = status.Turns,
        Credits = status.Credits,
        Corp = status.Corp,
        ShipName = status.ShipName,
        ShipType = status.ShipType,
        TurnsPerWarp = status.TurnsPerWarp,
        TotalHolds = status.TotalHolds,
        HoldsEmpty = status.HoldsEmpty,
        FuelOre = status.FuelOre,
        Organics = status.Organics,
        Equipment = status.Equipment,
        Colonists = status.Colonists,
        Fighters = status.Fighters,
        Shields = status.Shields,
        ArmidMines = status.ArmidMines,
        LimpetMines = status.LimpetMines,
        Photons = status.Photons,
        GenesisTorps = status.GenesisTorps,
        Cloaks = status.Cloaks,
        Beacons = status.Beacons,
        AtomicDet = status.AtomicDet,
        Corbomite = status.Corbomite,
        EtherProbes = status.EtherProbes,
        MineDisruptors = status.MineDisruptors,
        PsychProbe = status.PsychProbe,
        PlanetScanner = status.PlanetScanner,
        LRSType = status.LRSType,
        TimesBlownUp = status.TimesBlownUp,
        TransWarp1 = status.TransWarp1,
        TransWarp2 = status.TransWarp2,
    };

    private int ResolveExperience()
    {
        if (_shipStatus.Experience > 0)
            return (int)_shipStatus.Experience;
        return _lastKnownExperience;
    }

    private static string ProductKeyFromPrompt(string value) => value.ToUpperInvariant() switch
    {
        "FUEL ORE" => "FUEL",
        "ORGANICS" => "ORGANICS",
        "EQUIPMENT" => "EQUIPMENT",
        _ => string.Empty,
    };

    private static ProductType ProductTypeFromKey(string key) => key switch
    {
        "FUEL" => ProductType.FuelOre,
        "ORGANICS" => ProductType.Organics,
        _ => ProductType.Equipment,
    };

    private static void ConfigureProductConstants(SessionState session)
    {
        switch (session.ProductKey)
        {
            case "FUEL":
                session.BasePrice = 25.5;
                session.ProductFactor = 0.25;
                break;
            case "ORGANICS":
                session.BasePrice = 50.5;
                session.ProductFactor = 0.5;
                break;
            default:
                session.BasePrice = 90.5;
                session.ProductFactor = 0.9;
                break;
        }

        switch (NormalizeWeekday(session.Weekday))
        {
            case "Mon":
                session.BaseVarMin = 0;
                session.BaseVarMax = 5;
                break;
            case "Tue":
                session.BaseVarMin = 7;
                session.BaseVarMax = 7;
                break;
            case "Wed":
                session.BaseVarMin = 10;
                session.BaseVarMax = 15;
                break;
            case "Thu":
                session.BaseVarMin = 9;
                session.BaseVarMax = 9;
                break;
            case "Fri":
                session.BaseVarMin = 11;
                session.BaseVarMax = 12;
                break;
            case "Sat":
                session.BaseVarMin = 11;
                session.BaseVarMax = 18;
                break;
            default:
                session.BaseVarMin = 10;
                session.BaseVarMax = 12;
                break;
        }

        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
        {
            session.PlusMinus = -1;
            session.McicStep = 1;
        }
        else
        {
            session.PlusMinus = 1;
            session.McicStep = -1;
        }
    }

    private static bool PrepareRanges(SessionState session, ModDatabase? db)
    {
        (int defaultMin, int defaultMax) = session.ProductKey switch
        {
            "FUEL" => (40, 90),
            "ORGANICS" => (30, 75),
            _ => (20, 65),
        };
        session.DefaultMcicMin = session.McicStep * defaultMin;
        session.DefaultMcicMax = session.McicStep * defaultMax;

        int savedLowProductivity = ReadInt(db, session.Sector, session.ProductKey + "L");
        int savedHighProductivity = ReadInt(db, session.Sector, session.ProductKey + "H");

        if (session.Percent == 100)
        {
            int productivity = (int)PascalRoundInt(session.PortQty / 10.0, 0);
            session.MaxProductivity = productivity;
            session.LowProductivity = productivity;
            session.HighProductivity = productivity;
            session.CalculatedLowProductivity = productivity;
        }
        else if (session.Percent == 0)
        {
            session.LowProductivity = ReadInt(db, session.Sector, session.ProductKey + "L");
            session.HighProductivity = ReadInt(db, session.Sector, session.ProductKey + "H");
            session.MaxProductivity = session.HighProductivity;
            session.CalculatedLowProductivity = session.LowProductivity;
            if (session.LowProductivity <= 0 || session.HighProductivity <= 0)
                return false;
        }
        else
        {
            int minProductivity = (int)PascalRoundInt((session.PortQty * 10.0) / (session.Percent + 0.9999999999), 0);
            int maxProductivity = (int)PascalRoundInt(((session.PortQty / (double)session.Percent) * 10.0) - 0.4999999999, 0);
            if (maxProductivity > 6553)
                maxProductivity = 6553;
            session.MaxProductivity = maxProductivity;
            session.CalculatedLowProductivity = minProductivity;
            session.LowProductivity = savedLowProductivity > minProductivity ? savedLowProductivity : minProductivity;
            session.HighProductivity = (savedHighProductivity > 0 && savedHighProductivity < maxProductivity)
                ? savedHighProductivity
                : maxProductivity;
        }

        session.UseLowPercentDerive = ((session.HighProductivity - session.LowProductivity) + 1) > 10;

        if (db != null)
        {
            WriteInt(db, session.Sector, session.ProductKey + "L", session.LowProductivity);
            WriteInt(db, session.Sector, session.ProductKey + "H", session.HighProductivity);
        }

        int sign = session.McicStep;
        int storedMin = ReadSignedInt(db, session.Sector, session.ProductKey + "-");
        int storedMax = ReadSignedInt(db, session.Sector, session.ProductKey + "+");
        bool validStored =
            storedMin != int.MinValue &&
            storedMax != int.MinValue &&
            ((storedMin * sign) >= defaultMin) &&
            ((storedMin * sign) <= defaultMax) &&
            ((storedMax * sign) >= defaultMin) &&
            ((storedMax * sign) <= defaultMax);

        if (validStored)
        {
            session.McicMin = storedMin;
            session.McicMax = storedMax;
        }
        else
        {
            session.McicMin = session.DefaultMcicMin;
            session.McicMax = session.DefaultMcicMax;
            if (db != null && storedMin != int.MinValue)
            {
                db.SetSectorVar(session.Sector, session.ProductKey + "-", string.Empty);
                db.SetSectorVar(session.Sector, session.ProductKey + "+", string.Empty);
            }
        }

        return true;
    }

    private static bool DeriveCandidates(SessionState session, long offer)
    {
        while (true)
        {
            session.Candidates.Clear();

            if (session.UseLowPercentDerive && !HasLowPercentAnomalyRisk(session))
                DeriveCandidatesLowPercent(session, offer);
            else
                DeriveCandidatesConventional(session, offer);

            if (session.Candidates.Count > 0)
            {
                PersistDerivedRanges(session);
                LogCandidateSnapshot(session, offer, stage: "derive");
                return true;
            }

            if (!ApplyDeriveRecovery(session))
                return false;
        }
    }

    private static void DeriveCandidatesConventional(SessionState session, long offer)
    {
        double expAdjust = session.Experience > 999
            ? 0
            : session.PlusMinus * ((1000.0 - session.Experience) / 100.0);

        int terminal = session.McicMax + session.McicStep;
        for (int mcic = session.McicMin; mcic != terminal; mcic += session.McicStep)
        {
            double mcicFactor = (mcic / 1000.0) + 1.0;
            double qtyFactor = (mcic * (session.ProductFactor * session.PortQty)) / 10.0;

            for (int productivity = session.LowProductivity; productivity <= session.HighProductivity; productivity++)
            {
                double productivityFactor = qtyFactor / productivity;

                for (int baseVar = session.BaseVarMin; baseVar <= session.BaseVarMax; baseVar++)
                {
                    double priceBase = ((session.PlusMinus * baseVar) + session.BasePrice) - expAdjust - productivityFactor;
                    while (priceBase < 4.0)
                        priceBase += 1.0;

                    double exactPrice = priceBase * session.TradeQty;
                    double lowBound = ((mcicFactor - 0.003) * exactPrice) - 0.5001;
                    double highBound = ((mcicFactor + 0.003) * exactPrice) + 0.5001;
                    if (offer < PascalRoundInt(lowBound, 0) || offer > PascalRoundInt(highBound, 0))
                        continue;

                    for (double variance = -0.003; variance <= 0.0030001; variance = PascalRoundValue(variance + 0.001, 3))
                    {
                        double offeredPrice = (mcicFactor + variance) * exactPrice;
                        long rounded = PascalRoundInt(offeredPrice, 0);
                        bool match = rounded == offer;
                        if (!match)
                        {
                            double roundedDownCheck = PascalRoundValue(offeredPrice - 0.5, 7);
                            double roundedUpCheck = PascalRoundValue(offeredPrice + 0.5, 7);
                            bool roundedDown = Math.Abs(rounded - roundedDownCheck) <= 0.0000001;
                            bool roundedUp = Math.Abs(rounded - roundedUpCheck) <= 0.0000001;
                            if (roundedDown && rounded + 1 == offer)
                                match = true;
                            else if (roundedUp && rounded - 1 == offer)
                                match = true;
                        }

                        if (!match)
                            continue;

                        session.Candidates.Add(new Candidate
                        {
                            Mcic = mcic,
                            BaseVar = baseVar,
                            Variance = PascalRoundValue(variance, 3),
                            Productivity = productivity,
                            ExactPrice = exactPrice,
                        });
                    }
                }
            }
        }
    }

    private static void DeriveCandidatesLowPercent(SessionState session, long offer)
    {
        double expAdjust = session.Experience > 999
            ? 0
            : session.PlusMinus * ((1000.0 - session.Experience) / 100.0);

        int terminal = session.McicMax + session.McicStep;
        for (int mcic = session.McicMin; mcic != terminal; mcic += session.McicStep)
        {
            double mcicFactor = (mcic / 1000.0) + 1.0;

            for (int baseVar = session.BaseVarMin; baseVar <= session.BaseVarMax; baseVar++)
            {
                for (double variance = -0.003; variance <= 0.0030001; variance = PascalRoundValue(variance + 0.001, 3))
                {
                    double divisor = mcicFactor + variance;
                    if (Math.Abs(divisor) < 0.0000001)
                        continue;

                    double lowerExact = (offer - 0.4999999999) / divisor;
                    double upperExact = (offer + 0.4999999999) / divisor;

                    double denom1 = (((session.PlusMinus * baseVar) + session.BasePrice) - expAdjust) - (upperExact / session.TradeQty);
                    double denom2 = (((session.PlusMinus * baseVar) + session.BasePrice) - expAdjust) - (lowerExact / session.TradeQty);
                    if (Math.Abs(denom1) < 0.0000001 || Math.Abs(denom2) < 0.0000001)
                        continue;

                    double prod1 = ((mcic * session.ProductFactor) * session.PortQty) / (10.0 * denom1);
                    double prod2 = ((mcic * session.ProductFactor) * session.PortQty) / (10.0 * denom2);
                    if (prod2 < prod1)
                    {
                        (prod1, prod2) = (prod2, prod1);
                    }

                    int rangeLow = (int)PascalRoundInt(prod1 + 0.4999999999, 0);
                    int rangeHigh = (int)PascalRoundInt(prod2 - 0.4999999999, 0);
                    if (rangeLow > rangeHigh)
                        continue;

                    int low = Math.Max(session.LowProductivity, rangeLow);
                    int high = Math.Min(session.HighProductivity, rangeHigh);
                    if (low > high)
                        continue;

                    for (int productivity = low; productivity <= high; productivity++)
                    {
                        double exactPrice = ((((session.PlusMinus * baseVar) + session.BasePrice)
                            - ((session.PortQty / (productivity * 10.0)) * (mcic * session.ProductFactor)))
                            - expAdjust) * session.TradeQty;

                        session.Candidates.Add(new Candidate
                        {
                            Mcic = mcic,
                            BaseVar = baseVar,
                            Variance = variance,
                            Productivity = productivity,
                            ExactPrice = exactPrice,
                        });
                    }
                }
            }
        }
    }

    private static bool HasLowPercentAnomalyRisk(SessionState session)
    {
        double expAdjust = session.Experience > 999
            ? 0
            : session.PlusMinus * ((1000.0 - session.Experience) / 100.0);

        int terminal = session.McicMax + session.McicStep;
        for (int mcic = session.McicMin; mcic != terminal; mcic += session.McicStep)
        {
            double minValue = (((session.BasePrice + (session.PlusMinus * session.BaseVarMax)) - expAdjust)
                - ((mcic * (session.ProductFactor * session.PortQty)) / (session.LowProductivity * 10.0)));
            minValue = PascalRoundValue(minValue, 3);
            if (minValue < 4.0)
                return true;
        }

        return false;
    }

    private static bool FilterCandidates(SessionState session, long offer)
    {
        if (session.Candidates.Count == 0)
            return false;

        var prior = new List<Candidate>(session.Candidates.Count);
        prior.AddRange(session.Candidates);
        var next = new List<Candidate>();
        foreach (Candidate candidate in session.Candidates)
        {
            double exactCounter = AdvanceServerHiddenTotal(candidate.ExactPrice, session.LastCounter);
            long projected = PascalRoundInt((((candidate.Mcic / 1000.0) + candidate.Variance) + 1.0) * exactCounter, 0);
            if (projected != offer)
                continue;

            next.Add(new Candidate
            {
                Mcic = candidate.Mcic,
                BaseVar = candidate.BaseVar,
                Variance = candidate.Variance,
                Productivity = candidate.Productivity,
                ExactPrice = exactCounter,
            });
        }

        session.Candidates.Clear();
        session.Candidates.AddRange(next);
        PersistDerivedRanges(session);
        if (session.Candidates.Count == 0)
            LogFilterFailure(session, offer, prior);
        else
            LogCandidateSnapshot(session, offer, stage: "filter");
        return session.Candidates.Count > 0;
    }

    private long ComputeBid(SessionState session, long offer, string firstBidMode)
    {
        string mode = NativeHaggleModes.Normalize(firstBidMode);
        if (_extensionModes.TryGetValue(mode, out NativeHaggleModeExtension? extension))
            return extension.ComputeBid(this, session, offer);

        if (mode == NativeHaggleModes.ServerDerived)
            return ComputeServerDerivedBid(session, offer);

        long exactBid = ComputeExactBid(session);
        return ApplyExperimentalFirstBidMode(session, offer, exactBid, mode);
    }

    private static long ComputeExactBid(SessionState session)
    {
        double minCounter = 0;
        double maxCounter = 0;

        foreach (Candidate candidate in session.Candidates)
        {
            double counter;
            if (session.FinalOffer)
            {
                if (string.Equals(session.BuySell, "BUYING", StringComparison.OrdinalIgnoreCase))
                {
                    counter = candidate.ExactPrice - 0.5;
                    if (minCounter == 0 || counter < minCounter)
                        minCounter = counter;
                }
                else
                {
                    counter = candidate.ExactPrice + 0.5;
                    if (counter > maxCounter)
                        maxCounter = counter;
                }

                continue;
            }

            counter = ((((candidate.Mcic * 0.004) / (session.BidNumber + 1.0)) * -1.0) + 1.0) * candidate.ExactPrice;
            if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase) && session.BidNumber == 0)
            {
                double stupidOffer = (candidate.ExactPrice / 1.5) + 0.5;
                if (counter < stupidOffer)
                    counter = stupidOffer + 0.5;
            }

            if (minCounter == 0 || counter < minCounter)
                minCounter = counter;
            if (counter > maxCounter)
                maxCounter = counter;
        }

        double chosen;
        if (string.Equals(session.BuySell, "BUYING", StringComparison.OrdinalIgnoreCase))
        {
            chosen = minCounter;
            if (session.BidNumber > 0 && chosen > session.LastCounter)
                chosen = session.LastCounter;
            if (session.BidNumber == 0 && session.Percent == 100 && chosen != 0)
                chosen -= 1;
        }
        else
        {
            chosen = maxCounter;
            if (session.BidNumber > 0 && chosen < session.LastCounter)
                chosen = session.LastCounter;
            if (session.BidNumber == 0 && session.Percent == 100)
                chosen += 1;
        }

        return PascalRoundInt(chosen, 0);
    }

    private static long ApplyExperimentalFirstBidMode(SessionState session, long offer, long exactBid, string firstBidMode)
    {
        if (session.BidNumber != 0)
            return exactBid;

        string mode = NativeHaggleModes.Normalize(firstBidMode);

        if (mode == NativeHaggleModes.Baseline)
            return exactBid;

        long heuristicBid = ComputeHeuristicBid(session, offer);
        long adjustedBid = mode switch
        {
            NativeHaggleModes.BlendHeuristic => PascalRoundInt((exactBid + heuristicBid) / 2.0, 0),
            NativeHaggleModes.ClampHeuristic => string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase)
                ? Math.Max(exactBid, heuristicBid)
                : Math.Min(exactBid, heuristicBid),
            _ => exactBid,
        };

        adjustedBid = NormalizeBidForDirection(session, offer, adjustedBid);
        if (adjustedBid != exactBid)
        {
            GlobalModules.DebugLog(
                $"[NativeHaggle] Experiment '{mode}' adjusted first bid exact={exactBid} heuristic={heuristicBid} adjusted={adjustedBid} offer={offer} sector={session.Sector} product={session.ProductKey} buysell={session.BuySell}\n");
        }

        return adjustedBid;
    }

    internal static long ComputeServerDerivedBid(SessionState session, long offer)
    {
        return ComputeServerDerivedBid(session, offer, NativeHaggleModes.ClampHeuristic);
    }

    internal static long ComputeServerDerivedBid(SessionState session, long offer, string firstBidMode)
    {
        if (session.Candidates.Count == 0)
            return NormalizeBidForDirection(session, offer, offer);

        long exactBid = ComputeExactBid(session);
        long overlayBid = session.BidNumber == 0
            ? ApplyExperimentalFirstBidMode(session, offer, exactBid, firstBidMode)
            : exactBid;

        int roundNumber = Math.Max(1, session.BidNumber + 1);
        double chosenThreshold = 0;
        bool initialized = false;

        foreach (Candidate candidate in session.Candidates)
        {
            double threshold = candidate.ExactPrice * (1.0 - ((candidate.Mcic / 250.0) / roundNumber));
            if (!initialized)
            {
                chosenThreshold = threshold;
                initialized = true;
                continue;
            }

            if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
                chosenThreshold = Math.Max(chosenThreshold, threshold);
            else
                chosenThreshold = Math.Min(chosenThreshold, threshold);
        }

        long thresholdBid = PascalRoundInt(chosenThreshold, 0);
        long bid = string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(overlayBid, thresholdBid)
            : Math.Min(overlayBid, thresholdBid);

        bid = NormalizeBidForDirection(session, offer, bid);
        GlobalModules.DebugLog(
            $"[NativeHaggle] Server-derived bid round={roundNumber} offer={offer} exact={exactBid} overlay={overlayBid} threshold={chosenThreshold:0.000000} thresholdBid={thresholdBid} bid={bid} candidates={session.Candidates.Count} sector={session.Sector} product={session.ProductKey} buysell={session.BuySell}\n");
        return bid;
    }

    internal static long MoveBidTowardTarget(long baseBid, long targetBid, int maxNudge, bool increaseBid)
    {
        if (maxNudge <= 0)
            return baseBid;

        if (increaseBid)
        {
            if (targetBid <= baseBid)
                return baseBid;

            long candidate = baseBid + maxNudge;
            return Math.Min(targetBid, candidate);
        }

        if (targetBid >= baseBid)
            return baseBid;

        long candidateBid = baseBid - maxNudge;
        return Math.Max(targetBid, candidateBid);
    }

    internal static long MoveBidTowardOffer(SessionState session, long offer, long baseBid, int soften)
    {
        if (soften <= 0)
            return baseBid;

        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
            return Math.Min(offer - 1, baseBid + soften);

        return Math.Max(offer + 1, baseBid - soften);
    }

    internal static long MoveBidTowardExactRange(SessionState session, long baseBid, int nudge)
    {
        if (nudge <= 0 || session.Candidates.Count == 0)
            return baseBid;

        (double minExact, double maxExact, _) = GetTrackedTargetTotalRange(session);
        if (minExact <= 0 || maxExact <= 0)
            return baseBid;

        long lowerTarget = (long)Math.Ceiling(minExact);
        long upperTarget = (long)Math.Floor(maxExact);
        long target = baseBid;

        if (baseBid < lowerTarget)
            target = lowerTarget;
        else if (baseBid > upperTarget)
            target = upperTarget;

        if (target == baseBid)
            return baseBid;

        bool portSelling = string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase);
        return MoveBidTowardTarget(baseBid, target, nudge, portSelling);
    }

    internal static (double MinExact, double MaxExact) GetCandidateExactRange(SessionState session)
    {
        double minExact = 0;
        double maxExact = 0;

        foreach (Candidate candidate in session.Candidates)
        {
            if (minExact == 0 || candidate.ExactPrice < minExact)
                minExact = candidate.ExactPrice;
            if (candidate.ExactPrice > maxExact)
                maxExact = candidate.ExactPrice;
        }

        return (minExact, maxExact);
    }

    internal static bool TryGetCollapsedCandidateExact(SessionState session, out double exact)
    {
        (double minExact, double maxExact) = GetCandidateExactRange(session);
        if (minExact <= 0 || maxExact <= 0 || Math.Abs(maxExact - minExact) > 0.000001)
        {
            exact = 0;
            return false;
        }

        exact = minExact;
        return true;
    }

    internal static bool TryGetFirstOfferExactHitBid(SessionState session, long offer, out long bid, out string reason)
    {
        bid = 0;
        reason = string.Empty;

        if (session.BidNumber != 0 ||
            session.FinalOffer ||
            session.Percent != 100 ||
            session.Candidates.Count == 0 ||
            !TryGetCollapsedCandidateExact(session, out double exact))
        {
            return false;
        }

        long roundedExactBid = NormalizeBidForDirection(session, offer, PascalRoundInt(exact, 0));
        if (roundedExactBid <= 0)
            return false;

        if (!TryGetCollapsedCandidateProbe(session, exact, roundedExactBid, out double serverProbe, out int serverBucket) ||
            serverBucket != 100)
        {
            return false;
        }

        bid = roundedExactBid;
        reason = string.Create(
            CultureInfo.InvariantCulture,
            $"collapsedExact={exact:0.000000} roundedBid={roundedExactBid} serverProbe={serverProbe:0.00} serverBucket={serverBucket}");
        return true;
    }

    internal static bool TryGetCollapsedCandidateProbe(SessionState session, double exact, long bid, out double serverProbe, out int serverBucket)
    {
        serverProbe = 0;
        serverBucket = 0;

        if (exact <= 0 || bid <= 0)
            return false;

        long raw = string.Equals(session.BuySell, "BUYING", StringComparison.OrdinalIgnoreCase)
            ? Math.Max(0L, (long)Math.Truncate((exact * 10000.0) / bid))
            : Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / exact));

        serverProbe = raw / 100.0;
        serverBucket = (int)(raw / 100);
        return true;
    }

    internal static bool TryGetPreferredExactRange(SessionState session, out double minExact, out double maxExact)
    {
        (minExact, maxExact) = GetCandidateExactRange(session);
        if (minExact > 0 && maxExact > 0 && minExact <= maxExact)
            return true;

        if (session.HasHiddenTotalRange)
        {
            minExact = session.HiddenTotalMin;
            maxExact = session.HiddenTotalMax;
            return true;
        }

        minExact = 0;
        maxExact = 0;
        return false;
    }

    internal static (double MinTotal, double MaxTotal, string Source) GetTrackedTargetTotalRange(SessionState session)
    {
        return TryGetTargetExactRange(session, out double minTotal, out double maxTotal, out string source)
            ? (minTotal, maxTotal, source)
            : (0, 0, "n/a");
    }

    internal static (double Threshold, long ThresholdBid) ComputeServerThresholdBid(SessionState session)
    {
        int roundNumber = Math.Max(1, session.BidNumber + 1);
        double chosenThreshold = 0;
        bool initialized = false;

        foreach (Candidate candidate in session.Candidates)
        {
            double threshold = candidate.ExactPrice * (1.0 - ((candidate.Mcic / 250.0) / roundNumber));
            if (!initialized)
            {
                chosenThreshold = threshold;
                initialized = true;
                continue;
            }

            if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
                chosenThreshold = Math.Max(chosenThreshold, threshold);
            else
                chosenThreshold = Math.Min(chosenThreshold, threshold);
        }

        return (chosenThreshold, PascalRoundInt(chosenThreshold, 0));
    }

    private static string BuildRouteKey(int sector, string productKey, string buySell) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{sector}:{productKey}:{buySell}");

    internal static bool TryGetTargetExactRange(SessionState session, out double minExact, out double maxExact, out string source)
    {
        if (TryGetPreferredExactRange(session, out minExact, out maxExact))
        {
            source = session.Candidates.Count > 0 ? "candidates" : "hidden-tracker";
            return true;
        }

        minExact = 0;
        maxExact = 0;
        source = "n/a";
        return false;
    }

    internal static double ComputeCandidateExactPrice(SessionState session, Candidate candidate, int effectiveQty)
    {
        double expAdjust = session.Experience > 999
            ? 0
            : session.PlusMinus * ((1000.0 - session.Experience) / 100.0);

        double priceBase = ((session.PlusMinus * candidate.BaseVar) + session.BasePrice)
            - (((candidate.Mcic * session.ProductFactor) * effectiveQty) / (10.0 * candidate.Productivity))
            - expAdjust;

        while (priceBase < 4.0)
            priceBase += 1.0;

        return priceBase * session.TradeQty;
    }

    // This is still a ratio-only approximation until the real TradeData.+0x6c probe path is modeled.
    internal static bool TryGetServerProbeRange(
        SessionState session,
        long bid,
        out double serverProbeMin,
        out double serverProbeMax,
        out int serverBucketMin,
        out int serverBucketMax)
    {
        serverProbeMin = 0;
        serverProbeMax = 0;
        serverBucketMin = 0;
        serverBucketMax = 0;

        if (bid <= 0)
            return false;

        if (!TryGetTargetExactRange(session, out double minExact, out double maxExact, out _))
            return false;

        ServerProbeBranch branch = GetServerProbeBranch(session, bid);
        long exactOverBidRawMin = Math.Max(0L, (long)Math.Truncate((minExact * 10000.0) / bid));
        long exactOverBidRawMax = Math.Max(0L, (long)Math.Truncate((maxExact * 10000.0) / bid));
        long bidOverExactRawMin = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / maxExact));
        long bidOverExactRawMax = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / minExact));

        if (branch == ServerProbeBranch.BidOverHidden)
        {
            serverProbeMin = bidOverExactRawMin / 100.0;
            serverProbeMax = bidOverExactRawMax / 100.0;
            serverBucketMin = (int)(bidOverExactRawMin / 100);
            serverBucketMax = (int)(bidOverExactRawMax / 100);
            return true;
        }

        if (branch == ServerProbeBranch.HiddenOverBid)
        {
            serverProbeMin = exactOverBidRawMin / 100.0;
            serverProbeMax = exactOverBidRawMax / 100.0;
            serverBucketMin = (int)(exactOverBidRawMin / 100);
            serverBucketMax = (int)(exactOverBidRawMax / 100);
            return true;
        }

        serverProbeMin = Math.Min(exactOverBidRawMin / 100.0, bidOverExactRawMin / 100.0);
        serverProbeMax = Math.Max(exactOverBidRawMax / 100.0, bidOverExactRawMax / 100.0);
        serverBucketMin = (int)Math.Min(exactOverBidRawMin / 100, bidOverExactRawMin / 100);
        serverBucketMax = (int)Math.Max(exactOverBidRawMax / 100, bidOverExactRawMax / 100);
        return true;
    }

    private static bool TryGetRewardTierBucket(string rewardTier, out int bucket)
    {
        switch (rewardTier.Trim().ToUpperInvariant())
        {
            case "GOOD":
                bucket = 98;
                return true;
            case "GREAT":
                bucket = 99;
                return true;
            case "EXCELLENT":
                bucket = 100;
                return true;
            default:
                bucket = 0;
                return false;
        }
    }

    private static bool TryGetRewardTierHiddenRange(
        SessionState session,
        long bid,
        string rewardTier,
        out double impliedHiddenMin,
        out double impliedHiddenMax,
        out int rewardBucket)
    {
        impliedHiddenMin = 0;
        impliedHiddenMax = 0;
        rewardBucket = 0;

        if (bid <= 0 || !TryGetRewardTierBucket(rewardTier, out rewardBucket))
            return false;

        ServerProbeBranch branch = GetServerProbeBranch(session, bid);
        if (branch == ServerProbeBranch.HiddenOverBid)
        {
            impliedHiddenMin = bid * (rewardBucket / 100.0);
            impliedHiddenMax = bid * ((rewardBucket + 1) / 100.0);
            return true;
        }

        if (branch == ServerProbeBranch.BidOverHidden)
        {
            impliedHiddenMin = bid * (100.0 / (rewardBucket + 1.0));
            impliedHiddenMax = bid * (100.0 / rewardBucket);
            return true;
        }

        return false;
    }

    private static string DescribeRewardHiddenComparison(SessionState session)
    {
        if (session.LastCounter <= 0 ||
            !TryGetRewardTierHiddenRange(session, session.LastCounter, session.RewardTier, out double impliedHiddenMin, out double impliedHiddenMax, out int rewardBucket))
        {
            return "rewardHidden=n/a";
        }

        string modelRange = "n/a";
        string modelSource = "n/a";
        double modelMid = 0;
        if (TryGetTargetExactRange(session, out double modelMin, out double modelMax, out modelSource))
        {
            modelRange = string.Create(
                CultureInfo.InvariantCulture,
                $"{modelMin:0.000000}..{modelMax:0.000000}");
            modelMid = (modelMin + modelMax) / 2.0;
        }

        double impliedMid = (impliedHiddenMin + impliedHiddenMax) / 2.0;
        string deltaText = modelMid > 0
            ? (impliedMid - modelMid).ToString("+0.000000;-0.000000;0.000000", CultureInfo.InvariantCulture)
            : "n/a";
        string scaleText = modelMid > 0
            ? (impliedMid / modelMid).ToString("0.000000", CultureInfo.InvariantCulture)
            : "n/a";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"rewardBucket={rewardBucket} impliedHidden={impliedHiddenMin:0.000000}..{impliedHiddenMax:0.000000} modelHidden={modelRange} modelSource={modelSource} hiddenDelta={deltaText} hiddenScale={scaleText}");
    }

    internal static string DescribePredictedProbe(SessionState session, long bid)
    {
        if (bid <= 0)
            return "probe=n/a";

        if (TryDescribeTrackedProbe(session, bid, out string trackedProbe))
            return trackedProbe;

        if (session.Candidates.Count == 0)
            return "probe=n/a";

        double exactOverBidMin = double.MaxValue;
        double exactOverBidMax = double.MinValue;
        double bidOverExactMin = double.MaxValue;
        double bidOverExactMax = double.MinValue;
        int exactOverBidBucketMin = int.MaxValue;
        int exactOverBidBucketMax = int.MinValue;
        int bidOverExactBucketMin = int.MaxValue;
        int bidOverExactBucketMax = int.MinValue;

        foreach (Candidate candidate in session.Candidates)
        {
            if (candidate.ExactPrice <= 0)
                continue;

            long exactOverBidRaw = Math.Max(0L, (long)Math.Truncate((candidate.ExactPrice * 10000.0) / bid));
            long bidOverExactRaw = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / candidate.ExactPrice));

            double exactOverBid = exactOverBidRaw / 100.0;
            double bidOverExact = bidOverExactRaw / 100.0;
            int exactBucket = (int)(exactOverBidRaw / 100);
            int bidBucket = (int)(bidOverExactRaw / 100);

            if (exactOverBid < exactOverBidMin)
                exactOverBidMin = exactOverBid;
            if (exactOverBid > exactOverBidMax)
                exactOverBidMax = exactOverBid;
            if (bidOverExact < bidOverExactMin)
                bidOverExactMin = bidOverExact;
            if (bidOverExact > bidOverExactMax)
                bidOverExactMax = bidOverExact;
            if (exactBucket < exactOverBidBucketMin)
                exactOverBidBucketMin = exactBucket;
            if (exactBucket > exactOverBidBucketMax)
                exactOverBidBucketMax = exactBucket;
            if (bidBucket < bidOverExactBucketMin)
                bidOverExactBucketMin = bidBucket;
            if (bidBucket > bidOverExactBucketMax)
                bidOverExactBucketMax = bidBucket;
        }

        if (exactOverBidMin == double.MaxValue || bidOverExactMin == double.MaxValue)
            return "probe=n/a";

        ServerProbeBranch branch = GetServerProbeBranch(session, bid);
        (double serverProbeMin, double serverProbeMax, int serverBucketMin, int serverBucketMax) = branch switch
        {
            ServerProbeBranch.BidOverHidden => (bidOverExactMin, bidOverExactMax, bidOverExactBucketMin, bidOverExactBucketMax),
            ServerProbeBranch.HiddenOverBid => (exactOverBidMin, exactOverBidMax, exactOverBidBucketMin, exactOverBidBucketMax),
            _ => (
                Math.Min(exactOverBidMin, bidOverExactMin),
                Math.Max(exactOverBidMax, bidOverExactMax),
                Math.Min(exactOverBidBucketMin, bidOverExactBucketMin),
                Math.Max(exactOverBidBucketMax, bidOverExactBucketMax)),
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"probeModel=ratio exact/bid={exactOverBidMin:0.00}..{exactOverBidMax:0.00} bucket={exactOverBidBucketMin}..{exactOverBidBucketMax} bid/exact={bidOverExactMin:0.00}..{bidOverExactMax:0.00} bucket={bidOverExactBucketMin}..{bidOverExactBucketMax} serverBranch={DescribeServerProbeBranch(branch)} serverProbe={serverProbeMin:0.00}..{serverProbeMax:0.00} serverBucket={serverBucketMin}..{serverBucketMax}");
    }

    internal static bool TryDescribeTrackedProbe(SessionState session, long bid, out string description)
    {
        if (bid <= 0 || !TryGetTargetExactRange(session, out double minExact, out double maxExact, out string exactSource))
        {
            description = string.Empty;
            return false;
        }

        long exactOverBidRawMin = Math.Max(0L, (long)Math.Truncate((minExact * 10000.0) / bid));
        long exactOverBidRawMax = Math.Max(0L, (long)Math.Truncate((maxExact * 10000.0) / bid));
        long bidOverExactRawMin = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / maxExact));
        long bidOverExactRawMax = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / minExact));
        ServerProbeBranch branch = GetServerProbeBranch(session, bid);
        (double serverProbeMin, double serverProbeMax, long serverBucketMin, long serverBucketMax) = branch switch
        {
            ServerProbeBranch.BidOverHidden => (bidOverExactRawMin / 100.0, bidOverExactRawMax / 100.0, bidOverExactRawMin / 100, bidOverExactRawMax / 100),
            ServerProbeBranch.HiddenOverBid => (exactOverBidRawMin / 100.0, exactOverBidRawMax / 100.0, exactOverBidRawMin / 100, exactOverBidRawMax / 100),
            _ => (
                Math.Min(exactOverBidRawMin / 100.0, bidOverExactRawMin / 100.0),
                Math.Max(exactOverBidRawMax / 100.0, bidOverExactRawMax / 100.0),
                Math.Min(exactOverBidRawMin / 100, bidOverExactRawMin / 100),
                Math.Max(exactOverBidRawMax / 100, bidOverExactRawMax / 100)),
        };

        description = string.Create(
            CultureInfo.InvariantCulture,
            $"probeModel=ratio exact/bid={exactOverBidRawMin / 100.0:0.00}..{exactOverBidRawMax / 100.0:0.00} bucket={exactOverBidRawMin / 100}..{exactOverBidRawMax / 100} bid/exact={bidOverExactRawMin / 100.0:0.00}..{bidOverExactRawMax / 100.0:0.00} bucket={bidOverExactRawMin / 100}..{bidOverExactRawMax / 100} serverBranch={DescribeServerProbeBranch(branch)} serverProbe={serverProbeMin:0.00}..{serverProbeMax:0.00} serverBucket={serverBucketMin}..{serverBucketMax} exactSource={exactSource}");
        return true;
    }

    internal static ServerProbeBranch GetServerProbeBranch(SessionState session, long bid)
    {
        return FallbackServerProbeBranch(session);
    }

    internal static bool TryGetServerProbeComparisonRange(SessionState session, out double hiddenMin, out double hiddenMax)
    {
        hiddenMin = 0;
        hiddenMax = 0;

        return TryGetTargetExactRange(session, out hiddenMin, out hiddenMax, out _);
    }

    internal static ServerProbeBranch FallbackServerProbeBranch(SessionState session) =>
        string.Equals(session.BuySell, "BUYING", StringComparison.OrdinalIgnoreCase)
            ? ServerProbeBranch.HiddenOverBid
            : ServerProbeBranch.BidOverHidden;

    internal static string DescribeServerProbeBranch(ServerProbeBranch branch) =>
        branch switch
        {
            ServerProbeBranch.BidOverHidden => "bid/hidden",
            ServerProbeBranch.HiddenOverBid => "hidden/bid",
            _ => "overlap",
        };

    private string GetActiveFirstBidMode()
    {
        string? overrideMode = Environment.GetEnvironmentVariable("TWX_HAGGLE_EXPERIMENT");
        string resolved = ResolveConfiguredMode(
            string.IsNullOrWhiteSpace(overrideMode) ? null : overrideMode,
            _firstBidMode);
        if (!string.IsNullOrWhiteSpace(overrideMode))
        {
            GlobalModules.DebugLog(
                $"[NativeHaggle] TWX_HAGGLE_EXPERIMENT override='{overrideMode}' selectedMode='{resolved}' configuredMode='{_firstBidMode}'\n");
        }

        return resolved;
    }

    internal static long NormalizeBidForDirection(SessionState session, long offer, long bid)
    {
        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
        {
            if (bid >= offer)
                bid = offer - 1;
            return Math.Max(1, bid);
        }

        if (bid <= offer)
            bid = offer + 1;
        return Math.Max(offer + 1, bid);
    }

    private string ResolveConfiguredMode(string? preferredMode, string? fallbackMode)
    {
        string preferred = NativeHaggleModes.Normalize(preferredMode);
        if (NativeHaggleModes.IsBuiltIn(preferred) || _extensionModes.ContainsKey(preferred))
        {
            _lastMissingModeId = null;
            return preferred;
        }

        string fallback = NativeHaggleModes.Normalize(fallbackMode);
        if (NativeHaggleModes.IsBuiltIn(fallback) || _extensionModes.ContainsKey(fallback))
        {
            if (!string.IsNullOrWhiteSpace(preferredMode) &&
                !string.Equals(_lastMissingModeId, preferred, StringComparison.OrdinalIgnoreCase))
            {
                _lastMissingModeId = preferred;
                GlobalModules.DebugLog(
                    $"[NativeHaggle] Mode '{preferred}' is unavailable; falling back to '{fallback}'.\n");
            }
            return fallback;
        }

        _lastMissingModeId = null;
        return NativeHaggleModes.Default;
    }

    private static void PersistDerivedRanges(SessionState session)
    {
        if (session.Candidates.Count == 0)
            return;

        int minMcic = 0;
        int maxMcic = 0;
        int minProductivity = 0;
        int maxProductivity = 0;

        foreach (Candidate candidate in session.Candidates)
        {
            if (minMcic == 0 || (candidate.Mcic * session.McicStep) < (minMcic * session.McicStep))
                minMcic = candidate.Mcic;
            if ((candidate.Mcic * session.McicStep) > (maxMcic * session.McicStep))
                maxMcic = candidate.Mcic;
            if (minProductivity == 0 || candidate.Productivity < minProductivity)
                minProductivity = candidate.Productivity;
            if (candidate.Productivity > maxProductivity)
                maxProductivity = candidate.Productivity;
        }

        ModDatabase? db = ScriptRef.GetActiveDatabase();
        if (db == null)
            return;

        WriteInt(db, session.Sector, session.ProductKey + "-", minMcic);
        WriteInt(db, session.Sector, session.ProductKey + "+", maxMcic);
        WriteInt(db, session.Sector, session.ProductKey + "L", minProductivity);
        WriteInt(db, session.Sector, session.ProductKey + "H", maxProductivity);
    }

    private static bool TryEnableHeuristicFallback(SessionState session, long offer, string reason)
    {
        if (session.HeuristicFallback)
            return true;

        if (reason == "filter-failed")
        {
            if (session.BidNumber <= 0 || session.LastOffer <= 0 || session.LastCounter <= 0)
                return false;
        }
        else if (reason == "derive-failed")
        {
            if (offer <= 0)
                return false;
        }
        else
        {
            return false;
        }

        session.HeuristicFallback = true;
        session.Candidates.Clear();

        GlobalModules.DebugLog(
            $"[NativeHaggle] Switching to heuristic fallback ({reason}) for sector={session.Sector} product={session.ProductKey} buysell={session.BuySell} offer={offer} lastOffer={session.LastOffer} lastCounter={session.LastCounter}\n");
        return true;
    }

    private static long ComputeHeuristicBid(SessionState session, long offer)
    {
        long priorOffer = session.LastOffer > 0 ? session.LastOffer : offer;
        long priorCounter = session.LastCounter > 0 ? session.LastCounter : offer;

        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
        {
            return ComputeBuyHeuristicBid(session, offer, priorOffer, priorCounter);
        }

        return ComputeSellHeuristicBid(session, offer, priorOffer, priorCounter);
    }

    private static long ComputeRepeatedPromptBid(SessionState session, long offer)
    {
        if (session.BidNumber <= 1)
        {
            if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
            {
                long retry = (offer * 92) / 100;
                if (retry <= session.LastCounter)
                    retry = session.LastCounter + Math.Max(1, (offer - session.LastCounter) / 2);
                if (retry >= offer)
                    retry = offer - 1;
                return Math.Max(1, retry);
            }

            long opening = (offer * 108) / 100;
            if (opening <= offer)
                opening = offer + 1;
            if (opening >= session.LastCounter && session.LastCounter > offer)
                opening = offer + Math.Max(1, (session.LastCounter - offer) / 2);
            return Math.Max(offer + 1, opening);
        }

        long bid = ComputeHeuristicBid(session, offer);
        if (string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase))
        {
            if (bid <= session.LastCounter)
                bid = session.LastCounter + 1;
            if (bid >= offer)
                bid = offer - 1;
            return Math.Max(1, bid);
        }

        if (bid >= session.LastCounter)
            bid = session.LastCounter - 1;
        return Math.Max(offer + 1, bid);
    }

    private static long ComputeBuyHeuristicBid(SessionState session, long offer, long priorOffer, long priorCounter)
    {
        long counter;
        if (session.BidNumber == 0)
        {
            counter = (offer * 92) / 100;
            if (counter <= 0)
                counter = offer;
        }
        else if (session.FinalOffer)
        {
            long offerChange = offer - priorOffer;
            offerChange -= 1;
            offerChange = (offerChange * 25) / 10;
            counter = priorCounter - offerChange;
            if (counter == priorCounter)
                counter += 1;
            counter += 1;
        }
        else
        {
            long offerPct = (offer * 1000) / Math.Max(1, priorOffer);
            if (offerPct > 990)
                offerPct = 990;

            counter = (priorCounter * 1000) / Math.Max(1, offerPct);
            if (counter <= priorCounter)
                counter += 1;
        }

        if (counter <= 0)
            counter = Math.Max(1, offer);
        return counter;
    }

    private static long ComputeSellHeuristicBid(SessionState session, long offer, long priorOffer, long priorCounter)
    {
        long counter;
        if (session.BidNumber == 0)
        {
            counter = (offer * 108) / 100;
            if (counter <= offer)
                counter = offer + 1;
        }
        else if (session.FinalOffer)
        {
            long offerChange = offer - priorOffer;
            offerChange = (offerChange * 25) / 10;
            counter = priorCounter - offerChange;
            counter -= 3;
        }
        else
        {
            long offerPct = (offer * 1000) / Math.Max(1, priorOffer);
            if (offerPct < 1003)
                offerPct = 1003;

            counter = (priorCounter * 1000) / Math.Max(1, offerPct);
            if (counter >= priorCounter)
                counter -= 1;
        }

        if (counter <= 0)
            counter = Math.Max(1, offer);
        return counter;
    }

    private void Reset(string reason)
    {
        if (_session != null)
        {
            bool attemptedTrade = _session.BidNumber > 0 || _session.LastCounter > 0 || _session.PendingBid > 0;
            if (attemptedTrade && !_session.OutcomeRecorded)
                RecordOutcome(success: false, $"reset:{reason}");

            GlobalModules.DebugLog($"[NativeHaggle] Reset reason='{reason}' sector={_session.Sector} product={_session.ProductKey}\n");
        }
        _session = null;
        _pendingProductKey = null;
        _pendingBuySell = null;
    }

    private void RecordOutcome(bool success, string reason)
    {
        if (_session == null || _session.OutcomeRecorded)
            return;

        _session.OutcomeRecorded = true;
        _completedHaggles++;
        if (success)
            _successfulHaggles++;

        GetActiveModeExtension(_session.ActiveMode)?.OnOutcome(this, _session, success, reason);

        string probe = (_session.LastCounter > 0)
            ? DescribePredictedProbe(_session, _session.LastCounter)
            : "probe=n/a";
        string rewardHidden = DescribeRewardHiddenComparison(_session);
        string rewardTier = string.IsNullOrWhiteSpace(_session.RewardTier) ? "-" : _session.RewardTier;
        string routeState = DescribeModeState(_session);

        GlobalModules.DebugLog(
            $"[NativeHaggle] Outcome recorded success={success} reason='{reason}' rewardTier='{rewardTier}' rewardExp={_session.RewardExperience} completed={_completedHaggles} successful={_successfulHaggles} good={_goodRewardCount} great={_greatRewardCount} excellent={_excellentRewardCount} pct={SuccessRatePercent}% sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell} route={_session.RouteKey} bidNumber={_session.BidNumber} empiricalProbe={_session.EmpiricalProbeApplied} empiricalNudge={_session.EmpiricalProbeNudge} lastOffer={_session.LastOffer} lastCounter={_session.LastCounter} {DescribeStartCargoSnapshot(_session)} {probe} {rewardHidden} {routeState}\n");
        StatsChanged?.Invoke();
    }

    private NativeHaggleModeExtension? GetActiveModeExtension(string? modeId)
    {
        string normalized = NativeHaggleModes.Normalize(modeId);
        return _extensionModes.TryGetValue(normalized, out NativeHaggleModeExtension? extension)
            ? extension
            : null;
    }

    private string DescribeModeState(SessionState session)
    {
        NativeHaggleModeExtension? extension = GetActiveModeExtension(session.ActiveMode);
        return extension?.DescribeState(this, session) ?? "modeState=n/a";
    }

    private static string NormalizeWeekday(string value)
    {
        string day = value.Trim();
        if (day.Length >= 3)
            day = day[..3];
        return day.ToUpperInvariant() switch
        {
            "MON" => "Mon",
            "TUE" => "Tue",
            "WED" => "Wed",
            "THU" => "Thu",
            "FRI" => "Fri",
            "SAT" => "Sat",
            "SUN" => "Sun",
            _ => "Sat",
        };
    }

    private static long ParseLong(string value) =>
        long.Parse(value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int ParseInt(string value) =>
        int.Parse(value.Replace(",", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int ReadInt(ModDatabase? db, int sector, string key)
    {
        int value = ReadSignedInt(db, sector, key);
        return value == int.MinValue ? 0 : value;
    }

    private static int ReadSignedInt(ModDatabase? db, int sector, string key)
    {
        if (db == null)
            return int.MinValue;

        string raw = db.GetSectorVar(sector, key);
        if (string.IsNullOrWhiteSpace(raw))
            return int.MinValue;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : int.MinValue;
    }

    private static void WriteInt(ModDatabase db, int sector, string key, int value)
    {
        db.SetSectorVar(sector, key, value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool ApplyDeriveRecovery(SessionState session)
    {
        ModDatabase? db = ScriptRef.GetActiveDatabase();
        if (session.DeriveFailures == 0)
        {
            session.DeriveFailures = 1;
            session.HighProductivity = session.MaxProductivity;
            if (db != null)
                WriteInt(db, session.Sector, session.ProductKey + "H", session.MaxProductivity);
            GlobalModules.DebugLog(
                $"[NativeHaggle] Derive recovery #1 sector={session.Sector} product={session.ProductKey} highProd->{session.HighProductivity}\n");
            return true;
        }

        if (session.DeriveFailures == 1)
        {
            session.DeriveFailures = 2;
            session.McicMin = session.DefaultMcicMin;
            session.McicMax = session.DefaultMcicMax;
            session.LowProductivity = session.CalculatedLowProductivity;
            session.HighProductivity = session.MaxProductivity;
            session.BaseVarMin = 0;
            session.BaseVarMax = 18;
            if (db != null)
            {
                WriteInt(db, session.Sector, session.ProductKey + "-", session.McicMin);
                WriteInt(db, session.Sector, session.ProductKey + "+", session.McicMax);
                WriteInt(db, session.Sector, session.ProductKey + "L", session.LowProductivity);
                WriteInt(db, session.Sector, session.ProductKey + "H", session.HighProductivity);
            }

            GlobalModules.DebugLog(
                $"[NativeHaggle] Derive recovery #2 sector={session.Sector} product={session.ProductKey} lowProd->{session.LowProductivity} highProd->{session.HighProductivity} mcicMin->{session.McicMin} mcicMax->{session.McicMax} baseVar=0..18\n");
            return true;
        }

        return false;
    }

    private static void LogCandidateSnapshot(SessionState session, long offer, string stage)
    {
        if (session.Candidates.Count == 0)
            return;

        int limit = Math.Min(session.Candidates.Count, 8);
        for (int i = 0; i < limit; i++)
        {
            Candidate candidate = session.Candidates[i];
            GlobalModules.DebugLog(
                $"[NativeHaggle] {stage} cand[{i + 1}/{session.Candidates.Count}] offer={offer} mcic={candidate.Mcic} baseVar={candidate.BaseVar} variance={candidate.Variance.ToString("0.000", CultureInfo.InvariantCulture)} prod={candidate.Productivity} exact={candidate.ExactPrice.ToString("0.000000", CultureInfo.InvariantCulture)}\n");
        }
    }

    private static void LogFilterFailure(SessionState session, long offer, List<Candidate> prior)
    {
        int limit = Math.Min(prior.Count, 8);
        for (int i = 0; i < limit; i++)
        {
            Candidate candidate = prior[i];
            double exactCounter = AdvanceServerHiddenTotal(candidate.ExactPrice, session.LastCounter);
            long projected = PascalRoundInt((((candidate.Mcic / 1000.0) + candidate.Variance) + 1.0) * exactCounter, 0);
            long delta = projected - offer;
            GlobalModules.DebugLog(
                $"[NativeHaggle] filter-fail cand[{i + 1}/{prior.Count}] target={offer} projected={projected} delta={delta} mcic={candidate.Mcic} baseVar={candidate.BaseVar} variance={candidate.Variance.ToString("0.000", CultureInfo.InvariantCulture)} prod={candidate.Productivity} exact={candidate.ExactPrice.ToString("0.000000", CultureInfo.InvariantCulture)} nextExact={exactCounter.ToString("0.000000", CultureInfo.InvariantCulture)} lastCounter={session.LastCounter}\n");
        }
    }

    internal static long PascalRoundInt(double value, int precision)
    {
        return (long)Math.Truncate(PascalRoundValue(value, precision));
    }

    internal static double PascalRoundValue(double value, int precision)
    {
        double factor = Math.Pow(10, precision);
        double scaled = value * factor;
        double integer = Math.Truncate(scaled);
        double fraction = scaled - integer;
        double point5 = 0.5 - 1e-17;
        if (fraction >= point5)
            scaled = integer + 1.0;
        else
            scaled = integer;
        return scaled / factor;
    }
}
