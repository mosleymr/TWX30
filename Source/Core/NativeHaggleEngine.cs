using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TWXProxy.Core;

public sealed class NativeHaggleEngine
{
    private sealed class Candidate
    {
        public int Mcic { get; set; }
        public int BaseVar { get; set; }
        public double Variance { get; set; }
        public int Productivity { get; set; }
        public double ExactPrice { get; set; }
    }

    private sealed class SessionState
    {
        public int Sector { get; set; }
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
        public int MaxProductivity { get; set; }
        public int McicMin { get; set; }
        public int McicMax { get; set; }
        public int BidNumber { get; set; }
        public long LastCounter { get; set; }
        public bool FinalOffer { get; set; }
        public List<Candidate> Candidates { get; } = new();
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

    private readonly ShipInfoParser _shipInfoParser = new();
    private ShipStatus _shipStatus = new();
    private SessionState? _session;
    private string? _pendingProductKey;
    private ProductType _pendingProductType;
    private string? _pendingBuySell;
    private long _lastKnownCredits;
    private int _lastKnownExperience = 1000;

    public NativeHaggleEngine()
    {
        _shipInfoParser.Updated += status =>
        {
            _shipStatus = CloneStatus(status);
            if (_shipStatus.Credits > 0)
                _lastKnownCredits = _shipStatus.Credits;
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

    public static bool IsNegotiationLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        return RxHoldPrompt.IsMatch(line) ||
               RxAgreed.IsMatch(line) ||
               RxSellOffer.IsMatch(line) ||
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
            return HandleOffer(ParseLong(initialSell.Groups[1].Value), "SELLING", finalOffer: false);

        Match initialBuy = RxBuyOffer.Match(line);
        if (initialBuy.Success)
            return HandleOffer(ParseLong(initialBuy.Groups[1].Value), "BUYING", finalOffer: false);

        Match finalMatch = RxFinalOffer.Match(line);
        if (finalMatch.Success)
            return HandleOffer(ParseLong(finalMatch.Groups[1].Value), _session?.BuySell ?? string.Empty, finalOffer: true);

        return null;
    }

    private void UpdatePassiveState(string line)
    {
        Match creditsMatch = RxCredits.Match(line);
        if (creditsMatch.Success)
        {
            _lastKnownCredits = ParseLong(creditsMatch.Groups[1].Value);
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
        _session.TradeQty = tradeQty;
        _session.PortQty = port.ProductAmount.GetValueOrDefault(_pendingProductType);
        _session.Percent = port.ProductPercent.GetValueOrDefault(_pendingProductType);
        _session.Experience = ResolveExperience();
        _session.BidNumber = 0;
        _session.LastCounter = 0;
        _session.FinalOffer = false;
        _session.Candidates.Clear();

        ConfigureProductConstants(_session);
        if (!PrepareRanges(_session, db))
        {
            Reset("unable-to-prepare");
            return;
        }

        GlobalModules.DebugLog(
            $"[NativeHaggle] Armed sector={_session.Sector} product={_session.ProductKey} buysell={_session.BuySell} qty={_session.TradeQty} portQty={_session.PortQty} percent={_session.Percent} exp={_session.Experience} weekday={_session.Weekday}\n");
    }

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
        if (_session.BidNumber == 0)
        {
            if (!DeriveCandidates(_session, offer))
            {
                GlobalModules.DebugLog($"[NativeHaggle] Derive failed for sector={_session.Sector} product={_session.ProductKey}, manual haggle required.\n");
                Reset("derive-failed");
                return null;
            }
        }
        else
        {
            if (!FilterCandidates(_session, offer))
            {
                GlobalModules.DebugLog($"[NativeHaggle] Candidate filter failed for sector={_session.Sector} product={_session.ProductKey}, manual haggle required.\n");
                Reset("filter-failed");
                return null;
            }
        }

        long bid = ComputeBid(_session);
        _session.BidNumber++;
        _session.LastCounter = bid;

        GlobalModules.DebugLog(
            $"[NativeHaggle] offer={offer} final={finalOffer} bidNumber={_session.BidNumber} candidates={_session.Candidates.Count} bid={bid}\n");
        return bid.ToString(CultureInfo.InvariantCulture);
    }

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

        if (session.Percent == 100)
        {
            int productivity = (int)PascalRoundInt(session.PortQty / 10.0, 0);
            session.MaxProductivity = productivity;
            session.LowProductivity = productivity;
            session.HighProductivity = productivity;
        }
        else if (session.Percent == 0)
        {
            session.LowProductivity = ReadInt(db, session.Sector, session.ProductKey + "L");
            session.HighProductivity = ReadInt(db, session.Sector, session.ProductKey + "H");
            session.MaxProductivity = session.HighProductivity;
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
            session.LowProductivity = minProductivity;
            session.HighProductivity = maxProductivity;
        }

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
            session.McicMin = sign * defaultMin;
            session.McicMax = sign * defaultMax;
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
        session.Candidates.Clear();

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
                    long lowBound = PascalRoundInt(((mcicFactor - 0.003) * exactPrice) - 0.5001, 0);
                    long highBound = PascalRoundInt(((mcicFactor + 0.003) * exactPrice) + 0.5001, 0);
                    if (offer < lowBound || offer > highBound)
                        continue;

                    for (double variance = -0.003; variance <= 0.0030001; variance += 0.001)
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

        PersistDerivedRanges(session);
        return session.Candidates.Count > 0;
    }

    private static bool FilterCandidates(SessionState session, long offer)
    {
        if (session.Candidates.Count == 0)
            return false;

        var next = new List<Candidate>();
        foreach (Candidate candidate in session.Candidates)
        {
            double exactCounter = ((session.LastCounter - candidate.ExactPrice) * 0.3) + candidate.ExactPrice;
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
        return session.Candidates.Count > 0;
    }

    private static long ComputeBid(SessionState session)
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

    private void Reset(string reason)
    {
        if (_session != null)
        {
            GlobalModules.DebugLog($"[NativeHaggle] Reset reason='{reason}' sector={_session.Sector} product={_session.ProductKey}\n");
        }
        _session = null;
        _pendingProductKey = null;
        _pendingBuySell = null;
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

    private static long PascalRoundInt(double value, int precision)
    {
        return (long)Math.Truncate(PascalRoundValue(value, precision));
    }

    private static double PascalRoundValue(double value, int precision)
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
