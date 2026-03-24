using System.Text.RegularExpressions;

namespace TWXProxy.Core;

/// <summary>
/// Stateful line-by-line parser that extracts ship/trader status from the
/// two TW2002 display commands:
///
///   "/"  — four single lines, fields delimited by the Latin-1 0xB3 "³" byte.
///   "I"  — multi-line Info block between "&lt;Info&gt;" and the next Command prompt.
///
/// Feed every ANSI-stripped line from the server through <see cref="FeedLine"/>.
/// When a complete update has been parsed the <see cref="Updated"/> event fires
/// with the current <see cref="ShipStatus"/> snapshot.
/// </summary>
public class ShipInfoParser
{
    // ── State ──────────────────────────────────────────────────────────────
    private ShipStatus _s = new();

    // Multi-line "I" block tracking
    private bool _inInfoBlock;

    // "/" one-liner: the server sends 4 lines that each look like
    //   " Sect 12545³Turns 25,000³..."
    // We recognise them by the presence of the ³ separator.
    private const char Sep = '\u00B3';  // Latin-1 0xB3

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired after a complete "/" or "I" response has been parsed.
    /// The argument is the live <see cref="ShipStatus"/> instance (not a copy).
    /// </summary>
    public event Action<ShipStatus>? Updated;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Feed one ANSI-stripped line from the server.
    /// Call this for every line (complete or partial prompt) received.
    /// </summary>
    public void FeedLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        string trimmed = line.Trim();

        // ── "I" block delimiters ──────────────────────────────────────────
        if (trimmed.Equals("<Info>", StringComparison.OrdinalIgnoreCase))
        {
            _inInfoBlock = true;
            return;
        }

        if (_inInfoBlock && trimmed.StartsWith("Command [TL="))
        {
            _inInfoBlock = false;
            Updated?.Invoke(_s);
            return;
        }

        if (_inInfoBlock)
        {
            ParseInfoLine(trimmed);
            return;
        }

        // ── "/" one-liner lines (contain the ³ separator) ─────────────────
        if (line.Contains(Sep))
        {
            bool parsed = ParseSlashLine(line);
            if (parsed)
            {
                // The "/" response is 4 lines; fire after the last one
                // (identified by containing "Aln " and "Exp ")
                if (line.Contains("Aln ") && line.Contains("Exp "))
                    Updated?.Invoke(_s);
            }
            return;
        }

        // ── Fighter-drop confirmation ─────────────────────────────────────
        // "Done. You have 837 fighter(s) in close support."
        {
            var m = _rxDoneFighters.Match(trimmed);
            if (m.Success)
            {
                _s.Fighters = ParseInt(m.Groups[1].Value);
                Updated?.Invoke(_s);
            }
        }
    }

    // ── "/" one-liner parser ───────────────────────────────────────────────

    private bool ParseSlashLine(string line)
    {
        // Split on ³ and parse each "Key value" token
        var tokens = line.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        bool any = false;

        foreach (var raw in tokens)
        {
            string tok = raw.Trim();
            if (tok.Length == 0) continue;

            // Every token is "Label value[,value…]" with a single space separator
            // after the label keyword.  We match by the known keyword prefix.
            any = true;

            if      (TrySlash(tok, "Sect ",   out long sect))   _s.CurrentSector  = (int)sect;
            else if (TrySlash(tok, "Turns ",  out long turns))  _s.Turns          = (int)turns;
            else if (TrySlash(tok, "Creds ",  out long creds))  _s.Credits        = creds;
            else if (TrySlash(tok, "Figs ",   out long figs))   _s.Fighters       = (int)figs;
            else if (TrySlash(tok, "Shlds ",  out long shlds))  _s.Shields        = (int)shlds;
            else if (TrySlash(tok, "Hlds ",   out long hlds))   _s.TotalHolds     = (int)hlds;
            else if (TrySlash(tok, "Ore ",    out long ore))    _s.FuelOre        = (int)ore;
            else if (TrySlash(tok, "Org ",    out long org))    _s.Organics       = (int)org;
            else if (TrySlash(tok, "Equ ",    out long equ))    _s.Equipment      = (int)equ;
            else if (TrySlash(tok, "Col ",    out long col))    _s.Colonists      = (int)col;
            else if (TrySlash(tok, "Phot ",   out long phot))   _s.Photons        = (int)phot;
            else if (TrySlash(tok, "Armd ",   out long armd))   _s.ArmidMines     = (int)armd;
            else if (TrySlash(tok, "Lmpt ",   out long lmpt))   _s.LimpetMines    = (int)lmpt;
            else if (TrySlash(tok, "GTorp ",  out long gtorp))  _s.GenesisTorps   = (int)gtorp;
            else if (TrySlash(tok, "TWarp ",  out long twarp))  _s.TurnsPerWarp   = (int)twarp;
            else if (TrySlash(tok, "Clks ",   out long clks))   _s.Cloaks         = (int)clks;
            else if (TrySlash(tok, "Beacns ", out long beacns)) _s.Beacons        = (int)beacns;
            else if (TrySlash(tok, "AtmDt ",  out long atmdt))  _s.AtomicDet      = (int)atmdt;
            else if (TrySlash(tok, "Crbo ",   out long crbo))   _s.Corbomite      = (int)crbo;
            else if (TrySlash(tok, "EPrb ",   out long eprb))   _s.EtherProbes    = (int)eprb;
            else if (TrySlash(tok, "MDis ",   out long mdis))   _s.MineDisruptors = (int)mdis;
            else if (TrySlash(tok, "PsPrb ",  out string psp))  _s.PsychProbe     = IsBoolYes(psp);
            else if (TrySlash(tok, "PlScn ",  out string plsn)) _s.PlanetScanner  = IsBoolYes(plsn);
            else if (TrySlash(tok, "LRS ",    out string lrs))  _s.LRSType        = lrs.Trim();
            else if (TrySlash(tok, "Aln ",    out long aln))    _s.Alignment      = aln;
            else if (TrySlash(tok, "Exp ",    out long exp))    _s.Experience     = exp;
            else if (TrySlash(tok, "Corp ",   out long corp))   _s.Corp           = (int)corp;
            // "Ship 9 Other" – ship number, ignore or could store in ShipType
        }

        return any;
    }

    // ── "I" multi-line parser ─────────────────────────────────────────────

    // "Done. You have 837 fighter(s) in close support."
    private static readonly Regex _rxDoneFighters = new(
        @"^Done\.\s+You have ([\d,]+) fighter",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _rxRankExp = new(
        @"([\d,]+)\s+points?,\s+Alignment=([\d,]+)",
        RegexOptions.Compiled);

    private static readonly Regex _rxTotalHolds = new(
        @"(\d[\d,]*)\s*-\s*Fuel Ore=([\d,]+)\s+Empty=([\d,]+)",
        RegexOptions.Compiled);

    private static readonly Regex _rxTwoFieldLine = new(
        // Matches two "Label: value" pairs on the same line separated by spaces
        @"^(.+?)\s*:\s*(\S+)\s{3,}(.+?)\s*:\s*(\S+)\s*$",
        RegexOptions.Compiled);

    private void ParseInfoLine(string line)
    {
        // Key: value lines — split on the first ':'
        int colon = line.IndexOf(':');
        if (colon <= 0) { CheckTransWarpHeader(line); return; }

        string key = line[..colon].Trim();
        string val = line[(colon + 1)..].Trim();

        switch (key)
        {
            case "Trader Name":
            {
                // val is "Rank Name" — we only want the last word (the name, not the rank)
                var parts = val.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                _s.TraderName = parts.Length > 0 ? parts[^1] : val;
                break;
            }

            case "Rank and Exp":
            {
                var m = _rxRankExp.Match(val);
                if (m.Success)
                {
                    _s.Experience = ParseLong(m.Groups[1].Value);
                    _s.Alignment  = ParseLong(m.Groups[2].Value);
                    // Optional alignment text after the number
                    string rest = val[(m.Index + m.Length)..].Trim();
                    if (!string.IsNullOrEmpty(rest)) _s.AlignText = rest;
                }
                break;
            }

            case "Times Blown Up":
                _s.TimesBlownUp = ParseInt(val);
                break;

            // "Corp           # 2," — colon split gives key="Corp           # 2,"
            // …but actually the format is "Corp           # 2," with '#' not ':'
            // We catch it below in the '#' branch.

            case "Ship Name":
                _s.ShipName = val;
                break;

            case "Ship Info":
                _s.ShipType = val;
                break;

            case "Turns to Warp":
                _s.TurnsPerWarp = ParseInt(val);
                break;

            case "Current Sector":
                _s.CurrentSector = ParseInt(val);
                break;

            case "Turns left":
                _s.Turns = ParseInt(val);
                break;

            case "Total Holds":
            {
                var m = _rxTotalHolds.Match(val);
                if (m.Success)
                {
                    _s.TotalHolds = ParseInt(m.Groups[1].Value);
                    _s.FuelOre    = ParseInt(m.Groups[2].Value);
                    _s.HoldsEmpty = ParseInt(m.Groups[3].Value);
                }
                else
                {
                    _s.TotalHolds = ParseInt(val.Split('-')[0].Trim());
                }
                break;
            }

            case "Fighters":
                _s.Fighters = ParseInt(val);
                break;

            case "Shield points":
                _s.Shields = ParseInt(val.Split('+')[0]); // strip "+X regeneration"
                break;

            // Two-value lines — handled below after the switch
            case var k when k.StartsWith("Armid Mines"):
                _s.ArmidMines  = ParseFirstNum(val);
                _s.LimpetMines = ParseSecondField(line, "Limpet Mines");
                break;

            case var k when k.StartsWith("Photon Missiles"):
                _s.Photons      = ParseFirstNum(val);
                _s.GenesisTorps = ParseSecondField(line, "Genesis Torps");
                break;

            case var k when k.StartsWith("Atomic Detn"):
                _s.AtomicDet  = ParseFirstNum(val);
                _s.Corbomite  = ParseSecondField(line, "Corbomite Level");
                break;

            case var k when k.StartsWith("Cloaking Device"):
                _s.Cloaks      = ParseFirstNum(val);
                _s.EtherProbes = ParseSecondField(line, "Ether Probes");
                break;

            case "Mine Disruptors":
                _s.MineDisruptors = ParseInt(val);
                break;

            case var k when k.StartsWith("Navigation Beacons") || k.Equals("Beacons"):
                _s.Beacons = ParseFirstNum(val);
                break;

            case "Planet Scanner":
                _s.PlanetScanner = IsBoolYes(val);
                break;

            case "LongRange Scan":
                // "Holographic Scanner" → "Holo", "Standard" → "Standard"
                _s.LRSType = val.Contains("Holo", StringComparison.OrdinalIgnoreCase)
                           ? "Holo" : val;
                break;

            case var k when k.Contains("Type 1 Jump"):
                _s.TransWarp1 = ParseInt(val.Split(' ')[0]);
                break;

            case var k when k.Contains("Type 2 Jump"):
                _s.TransWarp2 = ParseInt(val.Split(' ')[0]);
                break;

            case var k when k.StartsWith("Interdictor"):
                _s.Interdictor = IsBoolYes(val);
                break;

            case "Credits":
                _s.Credits = ParseLong(val);
                break;
        }

        // "Corp           # 2," — note the '#' separator instead of ':'
        if (line.Contains("Corp") && line.Contains('#'))
        {
            int hash = line.IndexOf('#');
            string corpVal = line[(hash + 1)..].Trim().TrimEnd(',');
            if (int.TryParse(corpVal, out int cn)) _s.Corp = cn;
        }
    }

    private void CheckTransWarpHeader(string line)
    {
        _ = line; // may be used in future for additional header detection
    }

    /// <summary>
    /// Extracts a labelled value from lines that pack two fields,
    /// e.g. "Armid Mines  T1: 1                       Limpet Mines T2: 250"
    /// Returns 0 if the label is not found.
    /// </summary>
    private static int ParseSecondField(string line, string label)
    {
        int idx = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0;
        int c = line.IndexOf(':', idx);
        if (c < 0) return 0;
        string rest = line[(c + 1)..].Trim().Split(' ')[0];
        return ParseInt(rest);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool TrySlash(string token, string prefix, out long value)
    {
        value = 0;
        if (!token.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string rest = token[prefix.Length..].Replace(",", "").Trim();
        return long.TryParse(rest, out value);
    }

    private static bool TrySlash(string token, string prefix, out string value)
    {
        value = string.Empty;
        if (!token.StartsWith(prefix, StringComparison.Ordinal)) return false;
        value = token[prefix.Length..].Trim();
        return true;
    }

    private static bool IsBoolYes(string s) =>
        s.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("Y",   StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string s)
    {
        if (int.TryParse(s.Replace(",", "").Trim(), out int v)) return v;
        return 0;
    }

    /// <summary>Extracts the first numeric (optionally comma-separated) token from a string.
    /// Used for compound lines like "5     Limpet Mines T2:      10" where we want just 5.</summary>
    private static int ParseFirstNum(string s)
    {
        var first = s.TrimStart().Split(new char[]{' ', '\t'}, 2, StringSplitOptions.RemoveEmptyEntries);
        return first.Length > 0 ? ParseInt(first[0]) : 0;
    }

    private static long ParseLong(string s)
    {
        if (long.TryParse(s.Replace(",", "").Trim(), out long v)) return v;
        return 0;
    }
}
