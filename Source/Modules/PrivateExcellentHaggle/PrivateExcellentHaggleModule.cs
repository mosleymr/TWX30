using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TWXProxy.Core;

namespace PrivateExcellentHaggle;

public sealed class PrivateExcellentHaggleModule : IExpansionModule
{
    private ExpansionModuleContext? _context;
    private ExcellentTargetMode? _mode;

    public string Id => "private-excellent-haggle";
    public string DisplayName => "Private Excellent Haggle";
    public ExpansionHostTargets SupportedHosts => ExpansionHostTargets.Any;

    public Task InitializeAsync(ExpansionModuleContext context, CancellationToken cancellationToken)
    {
        _context = context;
        _mode = new ExcellentTargetMode();
        context.GameInstance.RegisterNativeHaggleMode(_mode);
        context.Log($"Registered haggle mode '{_mode.ModeInfo.DisplayName}' ({_mode.ModeInfo.Id}).");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (_context != null && _mode != null)
        {
            _context.GameInstance.UnregisterNativeHaggleMode(_mode.ModeInfo.Id);
            _context.Log($"Unregistered haggle mode '{_mode.ModeInfo.DisplayName}' ({_mode.ModeInfo.Id}).");
        }

        _mode = null;
        _context = null;
        return Task.CompletedTask;
    }
}

internal sealed class ExcellentTargetMode : NativeHaggleModeExtension
{
    private sealed class RouteState
    {
        public int GreatStreak { get; set; }
        public int Cooldown { get; set; }
        public int SuccessfulProbeCount { get; set; }
        public int FailedProbeCount { get; set; }
        public int FirstOfferExactHitFailures { get; set; }
        public int NearExcellentCount { get; set; }
        public int EarlyTowardOfferBias { get; set; }
    }

    private readonly Dictionary<string, RouteState> _routeStates = new(StringComparer.OrdinalIgnoreCase);

    public ExcellentTargetMode()
        : base(NativeHaggleModes.ExcellentTarget, "Excellent Target (Private)")
    {
    }

    public override long ComputeBid(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session, long offer)
    {
        string firstBidMode = ReadFirstBidMode();
        long baseBid = NativeHaggleEngine.ComputeServerDerivedBid(session, offer, firstBidMode);
        if (session.Candidates.Count == 0)
            return baseBid;

        if (!session.FinalOffer)
        {
            if (session.BidNumber == 0)
            {
                if (!string.IsNullOrWhiteSpace(session.RouteKey) &&
                    _routeStates.TryGetValue(session.RouteKey, out RouteState? exactHitRouteState) &&
                    exactHitRouteState.Cooldown > 0)
                {
                    session.FirstOfferExactHitApplied = false;
                }
                else if (ReadFirstExactHitEnabled() &&
                         NativeHaggleEngine.TryGetFirstOfferExactHitBid(session, offer, out long exactHitBid, out string exactHitReason))
                {
                    session.FirstOfferExactHitApplied = true;
                    GlobalModules.DebugLog(
                        $"[NativeHaggle] Excellent-target first-hit round=1 offer={offer} baseBid={baseBid} bid={exactHitBid} reason={exactHitReason} candidates={session.Candidates.Count} sector={session.Sector} product={session.ProductKey} buysell={session.BuySell}\n");
                    return exactHitBid;
                }
                else
                {
                    session.FirstOfferExactHitApplied = false;
                }
            }

            int routeBias = 0;
            if (session.BidNumber == 0 &&
                !string.IsNullOrWhiteSpace(session.RouteKey) &&
                _routeStates.TryGetValue(session.RouteKey, out RouteState? routeState) &&
                routeState.Cooldown <= 0)
            {
                routeBias = routeState.EarlyTowardOfferBias;
            }

            int soften = session.BidNumber == 0
                ? ReadFirstSoften()
                : ReadMidSoften();

            long bid = baseBid;
            if (soften > 0)
                bid = NativeHaggleEngine.MoveBidTowardOffer(session, offer, bid, soften);
            if (routeBias > 0)
                bid = NativeHaggleEngine.MoveBidTowardOffer(session, offer, bid, routeBias);

            int exactNudge = session.BidNumber == 0
                ? ReadFirstExactNudge()
                : ReadMidExactNudge();
            if (exactNudge > 0)
                bid = MoveBidTowardTargetExactRange(session, bid, exactNudge);

            bid = NativeHaggleEngine.NormalizeBidForDirection(session, offer, bid);
            GlobalModules.DebugLog(
                $"[NativeHaggle] Excellent-target early round={Math.Max(1, session.BidNumber + 1)} offer={offer} baseBid={baseBid} soften={soften} routeBias={routeBias} exactNudge={exactNudge} bid={bid} candidates={session.Candidates.Count} sector={session.Sector} product={session.ProductKey} buysell={session.BuySell}\n");
            return bid;
        }

        (double minTarget, double maxTarget, string exactSource) = GetTrackedTargetTotalRange(session);
        bool portSelling = string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase);
        double targetExactTotal = portSelling ? maxTarget : minTarget;
        long targetBid = portSelling
            ? (long)Math.Ceiling(targetExactTotal)
            : (long)Math.Floor(targetExactTotal);

        int maxNudge = ReadFinalNudge();
        if (!string.IsNullOrWhiteSpace(session.RouteKey) &&
            _routeStates.TryGetValue(session.RouteKey, out RouteState? finalRouteState) &&
            finalRouteState.Cooldown > 0)
        {
            if (maxNudge > 0)
            {
                GlobalModules.DebugLog(
                    $"[NativeHaggle] Excellent-target cooldown route={session.RouteKey} cooldown={finalRouteState.Cooldown} suppressedFinalNudge={maxNudge}\n");
            }
            maxNudge = 0;
        }

        long finalBid = NativeHaggleEngine.MoveBidTowardTarget(baseBid, targetBid, maxNudge, portSelling);
        finalBid = NativeHaggleEngine.NormalizeBidForDirection(session, offer, finalBid);
        session.FinalTargetNudgeApplied = (int)(finalBid - baseBid);
        finalBid = ApplyEmpiricalProbe(session, offer, baseBid, finalBid);

        string baseProbe = DescribePredictedProbe(session, baseBid);
        string finalProbe = DescribePredictedProbe(session, finalBid);
        GlobalModules.DebugLog(
            $"[NativeHaggle] Excellent-target bid round={Math.Max(1, session.BidNumber + 1)} offer={offer} targetExact={targetExactTotal:0.000000} exactRange={minTarget:0.000000}..{maxTarget:0.000000} exactSource={exactSource} baseBid={baseBid} targetBid={targetBid} maxNudge={maxNudge} bid={finalBid} baseProbe={baseProbe} finalProbe={finalProbe} candidates={session.Candidates.Count} sector={session.Sector} product={session.ProductKey} buysell={session.BuySell}\n");
        return finalBid;
    }

    public override void OnOutcome(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session, bool success, string reason)
    {
        if (string.IsNullOrWhiteSpace(session.RouteKey))
            return;

        RouteState state = GetRouteState(session.RouteKey);
        double serverProbeMin = 0;
        double serverProbeMax = 0;
        int serverBucketMin = 0;
        int serverBucketMax = 0;
        bool hasServerProbe = session.LastCounter > 0 &&
            TryGetServerProbeRange(session, session.LastCounter, out serverProbeMin, out serverProbeMax, out serverBucketMin, out serverBucketMax);

        if (success)
        {
            if (state.Cooldown > 0)
                state.Cooldown--;

            if (session.EmpiricalProbeApplied)
            {
                if (string.Equals(session.RewardTier, "great", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(session.RewardTier, "excellent", StringComparison.OrdinalIgnoreCase))
                {
                    state.SuccessfulProbeCount++;
                }

                state.GreatStreak = 0;
                state.NearExcellentCount = 0;
                return;
            }

            if (string.Equals(session.RewardTier, "excellent", StringComparison.OrdinalIgnoreCase))
            {
                state.GreatStreak = 0;
                state.NearExcellentCount = 0;
                GlobalModules.DebugLog(
                    $"[NativeHaggle] Excellent-route update route={session.RouteKey} success={success} reason='{reason}' rewardTier='{session.RewardTier}' probeModel=ratio serverProbe={(hasServerProbe ? serverProbeMax.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")} serverBucket={(hasServerProbe ? serverBucketMax.ToString(CultureInfo.InvariantCulture) : "n/a")} greatStreak={state.GreatStreak} nearExcellent={state.NearExcellentCount} bias={state.EarlyTowardOfferBias} cooldown={state.Cooldown} probeWins={state.SuccessfulProbeCount} probeFails={state.FailedProbeCount}\n");
                return;
            }

            if (string.Equals(session.RewardTier, "great", StringComparison.OrdinalIgnoreCase))
            {
                state.GreatStreak++;
                state.NearExcellentCount = 0;
                if (state.Cooldown <= 0)
                    state.EarlyTowardOfferBias = 0;
            }
            else
            {
                state.GreatStreak = 0;
                state.NearExcellentCount = 0;
                if (state.Cooldown <= 0)
                    state.EarlyTowardOfferBias = 0;
            }

            GlobalModules.DebugLog(
                $"[NativeHaggle] Excellent-route update route={session.RouteKey} success={success} reason='{reason}' rewardTier='{session.RewardTier}' probeModel=ratio serverProbe={(hasServerProbe ? serverProbeMax.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")} serverBucket={(hasServerProbe ? serverBucketMax.ToString(CultureInfo.InvariantCulture) : "n/a")} greatStreak={state.GreatStreak} nearExcellent={state.NearExcellentCount} bias={state.EarlyTowardOfferBias} cooldown={state.Cooldown} probeWins={state.SuccessfulProbeCount} probeFails={state.FailedProbeCount}\n");
            return;
        }

        if (session.EmpiricalProbeApplied)
        {
            state.FailedProbeCount++;
            state.Cooldown = 20;
        }

        if (session.FirstOfferExactHitApplied &&
            string.Equals(reason, "credits-no-transaction", StringComparison.OrdinalIgnoreCase))
        {
            state.FailedProbeCount++;
            state.FirstOfferExactHitFailures++;
            state.Cooldown = Math.Max(state.Cooldown, 20);
            GlobalModules.DebugLog(
                $"[NativeHaggle] Excellent-target first-hit backoff route={session.RouteKey} reason='{reason}' cooldown={state.Cooldown} probeFails={state.FailedProbeCount} firstHitFails={state.FirstOfferExactHitFailures}\n");
        }

        if (!session.EmpiricalProbeApplied &&
            session.FinalTargetNudgeApplied != 0 &&
            string.Equals(reason, "credits-no-transaction", StringComparison.OrdinalIgnoreCase))
        {
            state.FailedProbeCount++;
            state.Cooldown = Math.Max(state.Cooldown, 20);
            GlobalModules.DebugLog(
                $"[NativeHaggle] Excellent-target backoff route={session.RouteKey} finalNudge={session.FinalTargetNudgeApplied} reason='{reason}' cooldown={state.Cooldown} probeFails={state.FailedProbeCount}\n");
        }

        if (state.EarlyTowardOfferBias > 0 &&
            string.Equals(reason, "credits-no-transaction", StringComparison.OrdinalIgnoreCase))
        {
            state.Cooldown = Math.Max(state.Cooldown, 12);
            state.EarlyTowardOfferBias = 0;
            state.NearExcellentCount = 0;
        }

        state.GreatStreak = 0;

        GlobalModules.DebugLog(
            $"[NativeHaggle] Excellent-route update route={session.RouteKey} success={success} reason='{reason}' probeModel=ratio serverProbe={(hasServerProbe ? serverProbeMax.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")} serverBucket={(hasServerProbe ? serverBucketMax.ToString(CultureInfo.InvariantCulture) : "n/a")} greatStreak={state.GreatStreak} nearExcellent={state.NearExcellentCount} bias={state.EarlyTowardOfferBias} cooldown={state.Cooldown} probeWins={state.SuccessfulProbeCount} probeFails={state.FailedProbeCount}\n");
    }

    public override string DescribeState(NativeHaggleEngine engine, NativeHaggleEngine.SessionState session)
    {
        if (string.IsNullOrWhiteSpace(session.RouteKey) ||
            !_routeStates.TryGetValue(session.RouteKey, out RouteState? state))
        {
            return "excellentRoute=n/a";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"excellentRoute(greatStreak={state.GreatStreak},nearExcellent={state.NearExcellentCount},bias={state.EarlyTowardOfferBias},cooldown={state.Cooldown},probeWins={state.SuccessfulProbeCount},probeFails={state.FailedProbeCount},firstHitFails={state.FirstOfferExactHitFailures})");
    }

    private RouteState GetRouteState(string routeKey)
    {
        if (!_routeStates.TryGetValue(routeKey, out RouteState? state))
        {
            state = new RouteState();
            _routeStates[routeKey] = state;
        }

        return state;
    }

    private long ApplyEmpiricalProbe(NativeHaggleEngine.SessionState session, long offer, long baseBid, long currentBid)
    {
        session.EmpiricalProbeApplied = false;
        session.EmpiricalProbeNudge = 0;

        if (!ReadEmpiricalProbeEnabled() || !session.FinalOffer || string.IsNullOrWhiteSpace(session.RouteKey))
            return currentBid;

        RouteState state = GetRouteState(session.RouteKey);
        if (state.Cooldown > 0 || state.GreatStreak < 8)
            return currentBid;

        (double _, long thresholdBid) = NativeHaggleEngine.ComputeServerThresholdBid(session);
        bool portSelling = string.Equals(session.BuySell, "SELLING", StringComparison.OrdinalIgnoreCase);
        long candidate = portSelling ? currentBid + 1 : currentBid - 1;

        if (portSelling)
        {
            if (candidate > thresholdBid)
                return currentBid;
        }
        else if (candidate < thresholdBid)
        {
            return currentBid;
        }

        candidate = NativeHaggleEngine.NormalizeBidForDirection(session, offer, candidate);
        if (candidate == currentBid)
            return currentBid;

        session.EmpiricalProbeApplied = true;
        session.EmpiricalProbeNudge = (int)(candidate - baseBid);

        GlobalModules.DebugLog(
            $"[NativeHaggle] Excellent-target empirical probe route={session.RouteKey} greatStreak={state.GreatStreak} cooldown={state.Cooldown} baseBid={baseBid} currentBid={currentBid} thresholdBid={thresholdBid} probeBid={candidate} nudge={session.EmpiricalProbeNudge}\n");
        return candidate;
    }

    private static long MoveBidTowardTargetExactRange(NativeHaggleEngine.SessionState session, long baseBid, int nudge)
    {
        if (nudge <= 0)
            return baseBid;

        if (!TryGetTargetExactRange(session, out double minExact, out double maxExact, out _))
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
        return NativeHaggleEngine.MoveBidTowardTarget(baseBid, target, nudge, portSelling);
    }

    private static (double MinTotal, double MaxTotal, string Source) GetTrackedTargetTotalRange(NativeHaggleEngine.SessionState session)
    {
        return TryGetTargetExactRange(session, out double minTotal, out double maxTotal, out string source)
            ? (minTotal, maxTotal, source)
            : (0, 0, "n/a");
    }

    private static bool TryGetTargetExactRange(NativeHaggleEngine.SessionState session, out double minExact, out double maxExact, out string source)
    {
        if (TryGetExperimentalExactRange(session, out minExact, out maxExact, out source))
            return true;

        return NativeHaggleEngine.TryGetTargetExactRange(session, out minExact, out maxExact, out source);
    }

    private static bool TryGetExperimentalExactRange(NativeHaggleEngine.SessionState session, out double minExact, out double maxExact, out string source)
    {
        minExact = 0;
        maxExact = 0;
        source = string.Empty;

        if (session.Candidates.Count == 0 || !ReadPortApproxEnabled())
            return false;

        int productionRate = ReadPortApproxProductionRate();
        int maxRegen = ReadPortApproxMaxRegen();
        int biasHours = ReadPortApproxBiasHours();
        if (productionRate <= 0 || maxRegen <= 0)
            return false;

        double approxDays = session.PortReportAgeDays + (biasHours / 24.0);
        if (approxDays <= 0.0000001)
            return false;

        double capDays = maxRegen / (double)productionRate;
        if (capDays <= 0.0000001)
            return false;

        double cappedDays = Math.Min(approxDays, capDays);
        int maxQty = Math.Max(session.PortQty, session.PortMaxQty);
        if (maxQty <= session.PortQty)
            return false;

        bool initialized = false;
        int minQty = int.MaxValue;
        int maxQtyUsed = int.MinValue;

        foreach (NativeHaggleEngine.Candidate candidate in session.Candidates)
        {
            if (candidate.Productivity <= 0)
                continue;

            int qtyDelta = (int)NativeHaggleEngine.PascalRoundInt((candidate.Productivity * cappedDays * productionRate) / 10.0, 0);
            int effectiveQty = Math.Min(maxQty, session.PortQty + Math.Max(0, qtyDelta));
            double exactPrice = NativeHaggleEngine.ComputeCandidateExactPrice(session, candidate, effectiveQty);

            if (!initialized || exactPrice < minExact)
                minExact = exactPrice;
            if (!initialized || exactPrice > maxExact)
                maxExact = exactPrice;

            initialized = true;
            if (effectiveQty < minQty)
                minQty = effectiveQty;
            if (effectiveQty > maxQtyUsed)
                maxQtyUsed = effectiveQty;
        }

        if (!initialized || minExact <= 0 || maxExact <= 0 || minExact > maxExact)
        {
            minExact = 0;
            maxExact = 0;
            return false;
        }

        source = string.Create(
            CultureInfo.InvariantCulture,
            $"report-age-approx(ageHours={session.PortReportAgeDays * 24.0:0.00},biasHours={biasHours},cappedDays={cappedDays:0.000},prodRate={productionRate},maxRegen={maxRegen},qty={session.PortQty}->{minQty}..{maxQtyUsed},maxQty={maxQty})");
        return true;
    }

    private static bool TryGetServerProbeRange(
        NativeHaggleEngine.SessionState session,
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

        if (bid <= 0 || !TryGetTargetExactRange(session, out double minExact, out double maxExact, out _))
            return false;

        NativeHaggleEngine.ServerProbeBranch branch = NativeHaggleEngine.GetServerProbeBranch(session, bid);
        long exactOverBidRawMin = Math.Max(0L, (long)Math.Truncate((minExact * 10000.0) / bid));
        long exactOverBidRawMax = Math.Max(0L, (long)Math.Truncate((maxExact * 10000.0) / bid));
        long bidOverExactRawMin = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / maxExact));
        long bidOverExactRawMax = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / minExact));

        if (branch == NativeHaggleEngine.ServerProbeBranch.BidOverHidden)
        {
            serverProbeMin = bidOverExactRawMin / 100.0;
            serverProbeMax = bidOverExactRawMax / 100.0;
            serverBucketMin = (int)(bidOverExactRawMin / 100);
            serverBucketMax = (int)(bidOverExactRawMax / 100);
            return true;
        }

        if (branch == NativeHaggleEngine.ServerProbeBranch.HiddenOverBid)
        {
            serverProbeMin = exactOverBidRawMin / 100.0;
            serverProbeMax = exactOverBidRawMax / 100.0;
            serverBucketMin = (int)(exactOverBidRawMin / 100);
            serverBucketMax = (int)(exactOverBidRawMax / 100);
            return true;
        }

        return false;
    }

    private static string DescribePredictedProbe(NativeHaggleEngine.SessionState session, long bid)
    {
        if (bid <= 0 || !TryGetTargetExactRange(session, out double minExact, out double maxExact, out string exactSource))
            return NativeHaggleEngine.DescribePredictedProbe(session, bid);

        long exactOverBidRawMin = Math.Max(0L, (long)Math.Truncate((minExact * 10000.0) / bid));
        long exactOverBidRawMax = Math.Max(0L, (long)Math.Truncate((maxExact * 10000.0) / bid));
        long bidOverExactRawMin = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / maxExact));
        long bidOverExactRawMax = Math.Max(0L, (long)Math.Truncate((bid * 10000.0) / minExact));
        NativeHaggleEngine.ServerProbeBranch branch = NativeHaggleEngine.GetServerProbeBranch(session, bid);
        (double serverProbeMin, double serverProbeMax, long serverBucketMin, long serverBucketMax) = branch switch
        {
            NativeHaggleEngine.ServerProbeBranch.BidOverHidden => (bidOverExactRawMin / 100.0, bidOverExactRawMax / 100.0, bidOverExactRawMin / 100, bidOverExactRawMax / 100),
            NativeHaggleEngine.ServerProbeBranch.HiddenOverBid => (exactOverBidRawMin / 100.0, exactOverBidRawMax / 100.0, exactOverBidRawMin / 100, exactOverBidRawMax / 100),
            _ => (
                Math.Min(exactOverBidRawMin / 100.0, bidOverExactRawMin / 100.0),
                Math.Max(exactOverBidRawMax / 100.0, bidOverExactRawMax / 100.0),
                Math.Min(exactOverBidRawMin / 100, bidOverExactRawMin / 100),
                Math.Max(exactOverBidRawMax / 100, bidOverExactRawMax / 100)),
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"probeModel=ratio exact/bid={exactOverBidRawMin / 100.0:0.00}..{exactOverBidRawMax / 100.0:0.00} bucket={exactOverBidRawMin / 100}..{exactOverBidRawMax / 100} bid/exact={bidOverExactRawMin / 100.0:0.00}..{bidOverExactRawMax / 100.0:0.00} bucket={bidOverExactRawMin / 100}..{bidOverExactRawMax / 100} serverBranch={NativeHaggleEngine.DescribeServerProbeBranch(branch)} serverProbe={serverProbeMin:0.00}..{serverProbeMax:0.00} serverBucket={serverBucketMin}..{serverBucketMax} exactSource={exactSource}");
    }

    private static string ReadFirstBidMode()
    {
        const string defaultValue = NativeHaggleModes.ClampHeuristic;

        string? raw = Environment.GetEnvironmentVariable("TWX_HAGGLE_EXCELLENT_FIRST_MODE");
        if (string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                const string filePath = "/tmp/twx_haggle_excellent_first_mode.txt";
                if (File.Exists(filePath))
                    raw = File.ReadAllText(filePath).Trim();
            }
            catch
            {
                raw = null;
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (string.Equals(raw, "exact", StringComparison.OrdinalIgnoreCase))
            return NativeHaggleModes.Baseline;
        if (string.Equals(raw, "server", StringComparison.OrdinalIgnoreCase))
            return defaultValue;

        string normalized = NativeHaggleModes.Normalize(raw);
        return normalized == NativeHaggleModes.ServerDerived || normalized == NativeHaggleModes.ExcellentTarget
            ? defaultValue
            : normalized;
    }

    private static bool ReadEmpiricalProbeEnabled() =>
        ReadFlag("TWX_HAGGLE_EXCELLENT_EMPIRICAL", "/tmp/twx_haggle_excellent_empirical.txt", defaultValue: false);

    private static bool ReadPortApproxEnabled() =>
        ReadFlag("TWX_HAGGLE_EXCELLENT_PORT_APPROX", "/tmp/twx_haggle_excellent_port_approx.txt", defaultValue: true);

    private static int ReadPortApproxBiasHours() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_PORT_BIAS_HOURS", "/tmp/twx_haggle_excellent_port_bias_hours.txt", defaultValue: 6, maxValue: 240);

    private static int ReadPortApproxProductionRate() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_PORT_PRODUCTION_RATE", "/tmp/twx_haggle_excellent_port_production_rate.txt", defaultValue: 10, maxValue: 100);

    private static int ReadPortApproxMaxRegen() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_PORT_MAX_REGEN", "/tmp/twx_haggle_excellent_port_max_regen.txt", defaultValue: 100, maxValue: 1000);

    private static int ReadFinalNudge() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_NUDGE", "/tmp/twx_haggle_excellent_nudge.txt", defaultValue: 1, maxValue: 5);

    private static int ReadFirstSoften() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_FIRST_SOFTEN", "/tmp/twx_haggle_excellent_first_soften.txt", defaultValue: 0, maxValue: 3);

    private static bool ReadFirstExactHitEnabled() =>
        ReadFlag("TWX_HAGGLE_EXCELLENT_FIRST_EXACT_HIT", "/tmp/twx_haggle_excellent_first_exact_hit.txt", defaultValue: true);

    private static int ReadMidSoften() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_MID_SOFTEN", "/tmp/twx_haggle_excellent_mid_soften.txt", defaultValue: 0, maxValue: 3);

    private static int ReadFirstExactNudge() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_FIRST_EXACT_NUDGE", "/tmp/twx_haggle_excellent_first_exact_nudge.txt", defaultValue: 0, maxValue: 5);

    private static int ReadMidExactNudge() =>
        ReadSetting("TWX_HAGGLE_EXCELLENT_MID_EXACT_NUDGE", "/tmp/twx_haggle_excellent_mid_exact_nudge.txt", defaultValue: 0, maxValue: 5);

    private static int ReadSetting(string envName, string filePath, int defaultValue, int maxValue)
    {
        string? raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (File.Exists(filePath))
                    raw = File.ReadAllText(filePath).Trim();
            }
            catch
            {
                raw = null;
            }
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            return defaultValue;

        if (parsed < 0)
            return 0;
        if (parsed > maxValue)
            return maxValue;
        return parsed;
    }

    private static bool ReadFlag(string envName, string filePath, bool defaultValue)
    {
        string? raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                if (File.Exists(filePath))
                    raw = File.ReadAllText(filePath).Trim();
            }
            catch
            {
                raw = null;
            }
        }

        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
