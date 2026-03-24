/*
 * AutoRecorder.cs
 * Parses incoming game text and updates the sector database in real-time.
 * This replicates the "Auto Recorder" functionality of the original Pascal TWX Proxy.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TWXProxy.Core
{
    /// <summary>
    /// Stateful parser that reads lines of game output and keeps the sector database
    /// up to date.  Called for every complete (and partial) line received from the
    /// game server before text triggers are fired so that getSector / sysconst calls
    /// always see current data.
    /// </summary>
    public class AutoRecorder
    {
        // ── State ──────────────────────────────────────────────────────────────
        private int  _lastSector;        // Most recently seen "Sector  : NNNN" number
        private bool _inDensityScan;     // Inside a Relative Density Scan block
        private bool _inHoloScan;        // Inside a Holo Scan block
        private int  _currentSector;     // Sector extracted from most recent Command prompt

        // Density-scan warp collection: all sectors listed in an active density scan
        // are adjacent to the player's current sector — so we collect them and write
        // them as Warp[] for the origin sector when the scan finishes.
        private int        _densityFromSector;  // _currentSector snapshot at scan start
        private readonly List<int> _densityScanSectors = new();

        // Sector-display position: tracks which continuation-line type we're inside
        private enum SectorPos { None, Mines, Planets }
        private SectorPos _sectorPos;

        // Planet land-list / detail parsing state
        private bool _inLandList;     // True after "Registry# and Planet Name" header
        private int  _landListSector; // Sector the land list belongs to (_lastSector at entry)

        // Warp-lane (Frontier Map) parsing state
        // Pascal: TModExtractor.ProcessWarpLine / FCurrentDisplay = dWarpLane
        // Activated by "The shortest path (" or "  TO > " lines from the f command.
        private bool _inWarpLane;        // Currently inside an FM path response
        private int  _lastWarpLaneSect;  // Last sector parsed in the current path (0 = none yet)

        // Commerce / port report parsing state
        // Pascal: Process.ParsePortReport — triggered by "Commerce report for X:"
        private bool _inPortReport;       // Inside a commerce report block
        private int  _portReportSector;   // Sector whose port is being updated
        private bool _portReportHasFuel;  // Fuel Ore line was received
        private bool _portReportHasOrg;   // Organics line was received

        // CIM (Computer Information Menu) download state.
        // Pascal: TDisplay dCIM → dPortCIM or dWarpCIM.
        // Triggered by the ": " prompt sent by the game after ^R.
        // The first data line identifies which kind of CIM is coming:
        //   port CIM → line ends with '%'
        //   warp CIM → line ends with a number
        private bool _inCIM;              // Waiting to identify first data line
        private bool _inPortCIM;          // Processing port CIM lines
        private bool _inWarpCIM;          // Processing warp CIM lines

        // ── Empty-density-scan detection ──────────────────────────────────────
        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// True while holo-scan sector lines are being received.  Used to track
        /// whether "Sector  : NNNN" lines are neighbours (holo scan) or the current
        /// sector (D command / movement display).
        /// </summary>
        public bool InHoloScan => _inHoloScan;

        /// <summary>
        /// The sector number most recently seen in a <c>Command [TL=...]:[N]</c> prompt
        /// or a <c>Sector  : N</c> display line (outside of holo scan).  Zero if not yet known.
        /// </summary>
        public int CurrentSector => _currentSector;

        /// <summary>
        /// Raised on the thread-pool (same thread as <see cref="RecordLine"/>) whenever
        /// the current sector changes — i.e. each time a new sector number is extracted
        /// from a <c>Command [TL=...]:[N]</c> prompt.  Argument is the new sector number.
        /// </summary>
        public event Action<int>? CurrentSectorChanged;

        // ── Compiled Regex ─────────────────────────────────────────────────────
        // "Sector  : 3942 in The Crucible" — sector display header (D command, holo scan)
        // Group 1 = sector number, Group 2 = optional constellation name after " in "
        private static readonly Regex _rxSector = new(
            @"^Sector\s{1,4}:\s*(\d+)(?:\s+in\s+(.+))?", RegexOptions.Compiled);

        // "Warps to Sector(s) :  4497 - 5489 - 6477 - 15024 - 19702"
        // Parenthesised entries like "(3583)" are unexplored sectors and are still valid
        // outbound warps — the parentheses are stripped in ParseWarpsLine.
        // Leading optional whitespace handles indented holo-scan display format.
        private static readonly Regex _rxWarps = new(
            @"^\s*Warps to Sector\(s\)\s*:\s*(.+)", RegexOptions.Compiled);

        // "Ports   : Leander Minor, Class 3 (SBB)"
        // Group 1 = name, Group 2 = class number, Group 3 = optional SBB notation
        // Leading optional whitespace handles indented holo-scan display format.
        private static readonly Regex _rxPort = new(
            @"^\s*Ports\s+:\s+(.+),\s+Class\s+(\d+)(?:\s*\(([BSbs]{3})\))?", RegexOptions.Compiled);

        // "Beacon  : FedSpace, FedLaw Enforced"
        private static readonly Regex _rxBeacon = new(
            @"^\s*Beacon\s+:\s+(.+)", RegexOptions.Compiled);

        // "Command [TL=00:00:00]:[4497]" — extracts current sector from game prompt
        private static readonly Regex _rxCommandSector = new(
            @"Command \[TL=.*?\]:\[(\d+)\]", RegexOptions.Compiled);

        // "Commerce report for Howdah Primus:"
        private static readonly Regex _rxCommerceReport = new(
            @"^Commerce report for (.+?):", RegexOptions.Compiled);

        // "Fuel Ore   Selling   1600    100%       0"
        // "Organics   Buying    1630    100%       0"
        // "Equipment  Buying    2310    100%       0"
        private static readonly Regex _rxProductLine = new(
            @"^(Fuel Ore|Organics|Equipment)\s+(Selling|Buying)\s+([\d,]+)\s+(\d+)%",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Density scan data line:
        // "Sector   4497  ==>              0  Warps : 3    NavHaz :     0%    Anom : No"
        private static readonly Regex _rxDensityLine = new(
            @"^\s*Sector\s+(\d+)\s+==>\s+(\d+)\s+Warps\s*:\s*(\d+)\s+NavHaz\s*:\s*(\d+)%\s+Anom\s*:\s*(Yes|No)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "Fighters: 2,316,238 (belong to your Corp) [Defensive]"
        // "Fighters: 50 (offensive) owned by yours"
        // Quantity may contain commas.  Owner is the text inside the first ( ).
        // FigType comes from an optional trailing [Toll]/[Defensive]/[Offensive] bracket.
        private static readonly Regex _rxFighters = new(
            @"^Fighters\s*:\s*([\d,]+)\s+\((.+?)\)(?:\s+\[(.+?)\])?",
            RegexOptions.Compiled);

        // "NavHaz  :  15%" — NavHaz from sector display
        private static readonly Regex _rxNavHaz = new(
            @"^NavHaz\s+:\s+(\d+)%", RegexOptions.Compiled);

        // "Mines   : 3 (Type 1 Armid) (belong to your Corp)"
        // "Mines   : 3 (Type 2 Limpet) owned by yours"
        private static readonly Regex _rxMines = new(
            @"^Mines\s+:\s+(\d+)\s+\(Type\s+\d+\s+(Armid|Limpet)\)\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Continuation mine line indented with spaces:
        // "        : 3 (Type 2 Limpet) (belong to your Corp)"
        private static readonly Regex _rxMinesCont = new(
            @"^\s+:\s+(\d+)\s+\(Type\s+\d+\s+(Armid|Limpet)\)\s*(.*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // "Planets : <<<< (L) Romulus >>>> (Shielded)"  — first planet on the line
        private static readonly Regex _rxPlanets = new(
            @"^Planets?\s+:\s+(.*)", RegexOptions.Compiled);

        // Planet continuation line: indented spaces + planet decoration
        // "          <<<< (L) Vulcan >>>> (Shielded)"
        private static readonly Regex _rxPlanetCont = new(
            @"^\s{2,}(<<<<.*>>>>.*)", RegexOptions.Compiled);

        // Land-list entry (from the L command at a sector):
        // "   <   8> Romulus                            Level 6   0%     10M     15%    L"
        private static readonly Regex _rxLandEntry = new(
            @"^\s+<\s*(\d+)>\s+(\S[^<]*?)\s{3,}", RegexOptions.Compiled);

        // Planet detail header (inside a planet):
        // "Planet #55 in sector 12545: ."
        private static readonly Regex _rxPlanetDetail = new(
            @"^Planet\s+#(\d+)\s+in\s+sector\s+(\d+)\s*:\s*(.*)", RegexOptions.Compiled);

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Feed one (stripped) line of game output into the recorder.
        /// </summary>
        public void RecordLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // Log any line that looks warp-related so we can trace what the game sends
            if (line.IndexOf("Warp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Long Range", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Holo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                line.IndexOf("Sector", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                GlobalModules.DebugLog($"[AutoRecorder] RAW inHolo={_inHoloScan} inWarpLane={_inWarpLane} inPortRpt={_inPortReport} lastSect={_lastSector}: '{line}'\n");
            }

            var db = ScriptRef.ActiveDatabase;
            if (db == null)
            {
                GlobalModules.DebugLog($"[AutoRecorder] SKIPPED (db==null): '{line}'\n");
                return;
            }

            // ── Warp-lane trigger — checked on the RAW line before Trim() ────
            // Pascal: Copy(Line, 1, 19) = 'The shortest path (' or Copy(Line, 1, 7) = '  TO > '
            // Pascal never trims, so '  TO > ' carries its leading spaces.  We must check
            // before calling Trim() or the spaces are lost and the match always fails.
            if (line.StartsWith("The shortest path (") || line.StartsWith("  TO > "))
            {
                GlobalModules.DebugLog($"[AutoRecorder] WarpLane STARTED: '{line}'\n");
                _inWarpLane       = true;
                _lastWarpLaneSect = 0;
                return;
            }

            line = line.Trim();

            // ── Command prompt always clears warp-lane mode ───────────────────
            // Safety net: if a Command prompt appears while _inWarpLane is set, clear it.
            if (_inWarpLane && line.StartsWith("Command [TL="))
            {
                GlobalModules.DebugLog($"[AutoRecorder] WarpLane cleared by Command prompt\n");
                _inWarpLane       = false;
                _lastWarpLaneSect = 0;
                // Fall through to the normal Command [TL=] handler below.
            }

            // ── Warp-lane continuation lines ──────────────────────────────────
            if (_inWarpLane)
            {
                // ': ' is the ZTM re-query prompt — it signals end of the current route.
                if (line == ":")
                {
                    _inWarpLane       = false;
                    _lastWarpLaneSect = 0;
                    return;
                }
                ProcessWarpLaneLine(db, line);
                return;
            }

            // ── CIM prompt: ": " sent by game after ^R ────────────────────────
            // Pascal: Copy(Line, 1, 2) = ': ' → FCurrentDisplay := dCIM
            // After Trim(), this is simply ":". The CIM prompt is always a lone colon
            // with no other content on the line.
            if (line == ":")
            {
                _inCIM     = true;
                _inPortCIM = false;
                _inWarpCIM = false;
                return;
            }

            // ── CIM type identification (first data line) ──────────────────────
            if (_inCIM)
            {
                if (line.Length > 2)
                {
                    _inCIM = false;
                    if (line.EndsWith("%"))
                    {
                        _inPortCIM = true;
                        ParsePortCIMLine(db, line);
                    }
                    else
                    {
                        _inWarpCIM = true;
                        ParseWarpCIMLine(db, line);
                    }
                }
                return;
            }

            // ── CIM data lines ─────────────────────────────────────────────────
            if (_inPortCIM)
            {
                ParsePortCIMLine(db, line);
                return;
            }
            if (_inWarpCIM)
            {
                ParseWarpCIMLine(db, line);
                return;
            }

            // ── Planet land-list (L command) ───────────────────────────────────
            // Header line triggers the mode; entries supply planet ID + name.
            if (line.StartsWith("Registry# and Planet Name", StringComparison.OrdinalIgnoreCase))
            {
                _inLandList   = true;
                _landListSector = _currentSector;
                return;
            }
            if (_inLandList)
            {
                if (line.StartsWith("---")) return;  // separator
                var le = _rxLandEntry.Match(line);
                if (le.Success && int.TryParse(le.Groups[1].Value, out int planetId))
                {
                    string pname = le.Groups[2].Value.Trim();
                    db.SavePlanet(new Planet { Id = planetId, Name = pname, LastSector = _landListSector });
                    return;
                }
                // Blank or non-entry line ends the list
                if (string.IsNullOrWhiteSpace(line)) { _inLandList = false; return; }
                return;  // skip "Owned by:" continuation lines etc.
            }

            // ── Planet detail page ─────────────────────────────────────────────
            // "Planet #55 in sector 12545: ."
            {
                var pd = _rxPlanetDetail.Match(line);
                if (pd.Success
                    && int.TryParse(pd.Groups[1].Value, out int pid)
                    && int.TryParse(pd.Groups[2].Value, out int psector))
                {
                    string pname = pd.Groups[3].Value.Trim();
                    db.SavePlanet(new Planet { Id = pid, Name = pname, LastSector = psector });
                    return;
                }
            }

            // ── Mode switching ─────────────────────────────────────────────────
            if (line.StartsWith("Relative Density Scan", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("                          Relative Density Scan", StringComparison.OrdinalIgnoreCase))
            {
                _inWarpLane    = false;
                _inDensityScan = true;
                _inHoloScan    = false;
                _densityFromSector = _currentSector;
                _densityScanSectors.Clear();
                return;
            }

            if (line.StartsWith("Long Range Scan", StringComparison.OrdinalIgnoreCase))
            {
                // We are NOW inside a holo scan.  Set the flag immediately so that
                // the "Sector  : NNNN" lines in the scan body do NOT update
                // _currentSector (those sectors are neighbors, NOT the player's
                // current position).
                _inWarpLane    = false;
                _inHoloScan    = true;
                _inDensityScan = false;
                GlobalModules.DebugLog($"[AutoRecorder] HoloScan started, currentSector={_currentSector}\n");
                return;
            }

            if (line.StartsWith("Select (H)olo Scan", StringComparison.OrdinalIgnoreCase))
                return;

            // Separator / decoration lines
            if (line.StartsWith("---") || line.StartsWith("==="))
                return;

            // Command prompt resets scan state
            if (line.StartsWith("Command [TL="))
            {
                _inWarpLane = false;

                // Commit density-scan sectors as Warp[] for the origin sector.
                if (_densityFromSector > 0 && _densityScanSectors.Count > 0)
                    CommitDensityWarps(db, _densityFromSector, _densityScanSectors);
                _densityFromSector = 0;
                _densityScanSectors.Clear();

                _inDensityScan = false;
                _inHoloScan    = false;
                _inPortReport  = false;
                _inCIM         = false;
                _inPortCIM     = false;
                _inWarpCIM     = false;

                // Track current sector from the prompt ("Command [TL=...]:[$N]")
                var mc = _rxCommandSector.Match(line);
                if (mc.Success && int.TryParse(mc.Groups[1].Value, out int csn))
                {
                    _currentSector = csn;
                    CurrentSectorChanged?.Invoke(csn);
                    GlobalModules.DebugLog($"[AutoRecorder] Current sector set to {csn} from prompt\n");
                }
                return;
            }

            // ── Density scan lines ────────────────────────────────────────────
            if (_inDensityScan)
            {
                var m = _rxDensityLine.Match(line);
                if (m.Success)
                {
                    int sn = ParseDensityLine(db, m);
                    if (sn > 0)
                        _densityScanSectors.Add(sn);
                }
                return;
            }

            // ── Sector display header ─────────────────────────────────────────
            {
                var m = _rxSector.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int sn))
                {
                    GlobalModules.DebugLog($"[AutoRecorder] lastSector {_lastSector}→{sn} inHolo={_inHoloScan} inWarpLane={_inWarpLane} inPortRpt={_inPortReport}\n");
                    // A sector display definitively ends any warp-lane sequence.
                    // For 1-hop FM transwarp paths the ':' re-query prompt never arrives,
                    // so _inWarpLane must be cleared here or subsequent sector data gets consumed.
                    if (_inWarpLane)
                    {
                        GlobalModules.DebugLog($"[AutoRecorder] WarpLane cleared by Sector display\n");
                        _inWarpLane       = false;
                        _lastWarpLaneSect = 0;
                    }
                    _lastSector = sn;
                    _sectorPos  = SectorPos.None;

                    // If we were NOT already inside a holo scan, this "Sector  : NNNN" line
                    // is from a D-command display or an autopilot interim stop — update
                    // _currentSector so it stays accurate for density-scan tracking.
                    if (!_inHoloScan)
                    {
                        _currentSector = sn;
                        GlobalModules.DebugLog($"[AutoRecorder] Current sector set to {sn} from sector display\n");

                        // Clear volatile sector data to be re-populated from following lines.
                        var sec = GetOrCreate(db, sn);
                        if (sec != null)
                        {
                            sec.Fighters    = new SpaceObject();
                            sec.MinesArmid  = new SpaceObject();
                            sec.MinesLimpet = new SpaceObject();
                            sec.PlanetNames.Clear();
                            // Don't null the port here — it is only overwritten when a Ports line arrives.
                        }
                    }

                    // Always capture constellation name when present (works for both
                    // normal sector display AND holo scan: "Sector  : 9363  in  The Crucible")
                    if (m.Groups[2].Success)
                    {
                        string constName = m.Groups[2].Value.Trim();
                        if (!string.IsNullOrEmpty(constName))
                        {
                            var sec = GetOrCreate(db, sn);
                            if (sec != null)
                            {
                                sec.Constellation = constName;
                                db.SaveSector(sec);
                                GlobalModules.DebugLog($"[AutoRecorder] Sector {sn} constellation = {constName}\n");
                            }
                        }
                    }

                    _inHoloScan = true;   // Any "Sector  : NNNN" marks start of a display block
                    return;
                }
            }

            // ── Commerce report detection ──────────────────────────────────────
            {
                var m = _rxCommerceReport.Match(line);
                if (m.Success)
                {
                    _inPortReport      = true;
                    _portReportSector  = _currentSector;
                    _portReportHasFuel = false;
                    _portReportHasOrg  = false;
                    // Set port name now; product lines will fill BuyProduct/Amount/Percent
                    var sec = GetOrCreate(db, _portReportSector);
                    if (sec != null)
                    {
                        sec.SectorPort ??= new Port();
                        string reportName = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(reportName))
                            sec.SectorPort.Name = reportName;
                        db.SaveSector(sec);
                    }
                    GlobalModules.DebugLog($"[AutoRecorder] Commerce report for sector {_portReportSector}\n");
                    return;
                }
            }
            if (_inPortReport)
            {
                var pm = _rxProductLine.Match(line);
                if (pm.Success)
                {
                    ParseProductLine(db, pm);
                    return;
                }
                // Skip other lines inside the report (date/time etc.) and fall through
                return;
            }

            // ── Data lines that belong to _lastSector ─────────────────────────
            if (_lastSector <= 0)
            {
                if (line.IndexOf("Warps to Sector", StringComparison.OrdinalIgnoreCase) >= 0)
                    GlobalModules.DebugLog($"[AutoRecorder] DROPPED warp line (_lastSector=0): '{line}'\n");
                return;
            }

            // Warps
            {
                var m = _rxWarps.Match(line);
                if (m.Success)
                {
                    ParseWarpsLine(db, _lastSector, m.Groups[1].Value);
                    return;
                }
                // Log if line looks like it should match but didn't
                if (line.IndexOf("Warps to Sector", StringComparison.OrdinalIgnoreCase) >= 0)
                    GlobalModules.DebugLog($"[AutoRecorder] WARN: warp line not matched by regex for sect={_lastSector}: '{line}'\n");
            }

            // Port
            {
                var m = _rxPort.Match(line);
                if (m.Success)
                {
                    ParsePortLine(db, _lastSector, m);
                    _sectorPos = SectorPos.None;
                    return;
                }
                // Warn if a line looks like a port line but the regex didn't match
                if (line.StartsWith("Port", StringComparison.OrdinalIgnoreCase))
                    GlobalModules.DebugLog($"[AutoRecorder] WARN: port-like line not matched for sector {_lastSector}: '{line}'\n");
            }

            // NavHaz  :  15%
            {
                var m = _rxNavHaz.Match(line);
                if (m.Success)
                {
                    var sector = GetOrCreate(db, _lastSector);
                    if (sector != null)
                    {
                        if (byte.TryParse(m.Groups[1].Value, out byte nh))
                            sector.NavHaz = nh;
                        db.SaveSector(sector);
                    }
                    _sectorPos = SectorPos.None;
                    return;
                }
            }

            // Beacon (federation space etc.)
            {
                var m = _rxBeacon.Match(line);
                if (m.Success)
                {
                    var sector = GetOrCreate(db, _lastSector);
                    if (sector != null)
                    {
                        sector.Beacon = m.Groups[1].Value.Trim();
                        db.SaveSector(sector);
                    }
                    _sectorPos = SectorPos.None;
                    return;
                }
            }

            // Fighters
            {
                var m = _rxFighters.Match(line);
                if (m.Success)
                {
                    ParseFightersLine(db, _lastSector, m);
                    _sectorPos = SectorPos.None;
                    return;
                }
            }

            // Mines (first line)
            {
                var m = _rxMines.Match(line);
                if (m.Success)
                {
                    ParseMinesLine(db, _lastSector, m);
                    _sectorPos = SectorPos.Mines;
                    return;
                }
            }

            // Mines continuation line: "        : 3 (Type 2 Limpet) ..."
            if (_sectorPos == SectorPos.Mines)
            {
                var m = _rxMinesCont.Match(line);
                if (m.Success)
                {
                    ParseMinesLine(db, _lastSector, m);
                    return;
                }
            }

            // Planets (first line): record name in PlanetNames on the sector
            {
                var m = _rxPlanets.Match(line);
                if (m.Success)
                {
                    var sector = GetOrCreate(db, _lastSector);
                    if (sector != null)
                    {
                        string name = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(name))
                            sector.PlanetNames.Add(name);
                        db.SaveSector(sector);
                    }
                    _sectorPos = SectorPos.Planets;
                    return;
                }
            }

            // Planet continuation lines (indented <<<< ... >>>>
            if (_sectorPos == SectorPos.Planets)
            {
                var m = _rxPlanetCont.Match(line);
                if (m.Success)
                {
                    var sector = GetOrCreate(db, _lastSector);
                    if (sector != null)
                    {
                        sector.PlanetNames.Add(m.Groups[1].Value.Trim());
                        db.SaveSector(sector);
                    }
                    return;
                }
                // Any non-planet-looking line ends the planets block
                _sectorPos = SectorPos.None;
            }
        }

        // ── Private helpers ────────────────────────────────────────────────────

        // Returns the sector number on success (so caller can collect it), 0 on failure.
        private static int ParseDensityLine(ModDatabase db, Match m)
        {
            if (!int.TryParse(m.Groups[1].Value, out int sn)) return 0;
            var sector = GetOrCreate(db, sn);
            if (sector == null) return 0;

            // Density value (Pascal: GetParameter(X,4))
            if (int.TryParse(m.Groups[2].Value, out int density))
                sector.Density = density;

            // Warp count (Pascal: GetParameter(X,7)) — count only, not the Warp[] array
            if (byte.TryParse(m.Groups[3].Value, out byte wc))
                sector.WarpCount = wc;

            // NavHaz % (Pascal: GetParameter(X,10), stripped of trailing '%')
            if (byte.TryParse(m.Groups[4].Value, out byte navhaz))
                sector.NavHaz = navhaz;

            // Anomaly flag (Pascal: GetParameter(X,13) = 'Yes')
            sector.Anomaly = m.Groups[5].Value.Equals("Yes", StringComparison.OrdinalIgnoreCase);

            // Pascal: if Sect.Explored in [etNo, etCalc] then set Constellation/Explored/Update
            if (sector.Explored == ExploreType.No || sector.Explored == ExploreType.Calc)
            {
                sector.Constellation = "??? (Density only)";
                sector.Explored = ExploreType.Density;
                sector.Update = DateTime.Now;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] density sector={sn} density={sector.Density} warps={wc} navhaz={navhaz} anom={sector.Anomaly}\n");
            return sn;
        }

        // Write the sectors listed in a density scan as Warp[] for the origin sector.
        // Because a density scan from sector A enumerates all sectors directly adjacent
        // to A, those sector numbers ARE A's warps.
        private static void CommitDensityWarps(ModDatabase db, int fromSector, List<int> sectors)
        {
            var sec = GetOrCreate(db, fromSector);
            if (sec == null) return;

            for (int i = 0; i < 6; i++) sec.Warp[i] = 0;
            int idx = 0;
            foreach (int sn in sectors)
            {
                if (idx >= 6) break;
                sec.Warp[idx++] = (ushort)sn;
            }

            db.SaveSector(sec);
            GlobalModules.DebugLog($"[AutoRecorder] Sector {fromSector} warps from density scan: {string.Join(", ", sectors)}\n");
        }

        private static void ParseWarpsLine(ModDatabase db, int sectorNum, string warpsPart)
        {
            var sector = GetOrCreate(db, sectorNum);
            if (sector == null) return;

            // Clear existing warps
            for (int i = 0; i < 6; i++) sector.Warp[i] = 0;

            // Parse "4497 - 5489 - 6477 - 15024 - 19702"
            // Also handles parenthesised unexplored sectors: "(3583) - 4497 - (6198)"
            int idx = 0;
            foreach (var token in warpsPart.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (idx >= 6) break;
                // Strip surrounding parentheses from unexplored-sector notation
                var clean = token.Trim().Trim('(', ')');
                if (ushort.TryParse(clean, out ushort w))
                    sector.Warp[idx++] = w;
            }

            // Pascal SectorCompleted() sets etHolo (the maximum explored level) whenever
            // a full sector display finishes — this covers both direct visits and holo scans.
            // Only upgrade, never downgrade (density-only will already be ExploreType.Density).
            if (sector.Explored != ExploreType.Yes)
            {
                sector.Explored = ExploreType.Yes;
                sector.Update = DateTime.Now;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] ParseWarpsLine sect={sectorNum} stored=[{string.Join(",", sector.Warp)}]\n");
        }

        private static void ParsePortLine(ModDatabase db, int sectorNum, Match m)
        {
            var sector = GetOrCreate(db, sectorNum);
            if (sector == null) return;

            sector.SectorPort ??= new Port();
            sector.SectorPort.Name = m.Groups[1].Value.Trim();

            if (byte.TryParse(m.Groups[2].Value, out byte cls))
                sector.SectorPort.ClassIndex = cls;

            // Parse S/B notation from optional group 3, e.g. "(SBB)"
            // B = port Buys this product (player sells to port)
            // S = port Sells this product (player buys from port)
            if (m.Groups[3].Success)
            {
                string notation = m.Groups[3].Value.ToUpperInvariant();
                if (notation.Length == 3)
                {
                    sector.SectorPort.BuyProduct[ProductType.FuelOre]   = notation[0] == 'B';
                    sector.SectorPort.BuyProduct[ProductType.Organics]  = notation[1] == 'B';
                    sector.SectorPort.BuyProduct[ProductType.Equipment] = notation[2] == 'B';
                }
            }

            // A Ports line means we have a real sector scan (D command or holo scan).
            // Upgrade explore level so the sector doesn't stay stuck at Unknown/Calc,
            // which can happen for dead-end sectors that never emit a Warps line.
            if (sector.Explored != ExploreType.Yes)
            {
                sector.Explored = ExploreType.Yes;
                if (sector.Update == default) sector.Update = DateTime.Now;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Sector {sectorNum} port={sector.SectorPort.Name} class={cls}\n");
        }

        /// <summary>
        /// Parses a product line from a commerce report and updates the port's
        /// BuyProduct, ProductAmount, and ProductPercent data.
        /// Pascal equivalent: Process.pas commerce report block.
        /// </summary>
        private void ParseProductLine(ModDatabase db, Match m)
        {
            if (_portReportSector <= 0) return;
            var sector = GetOrCreate(db, _portReportSector);
            if (sector == null) return;

            sector.SectorPort ??= new Port();

            string productName = m.Groups[1].Value;
            string status      = m.Groups[2].Value;
            string amtStr      = m.Groups[3].Value.Replace(",", "");
            string pctStr      = m.Groups[4].Value;

            // "Buying" → port buys (players sell here), "Selling" → port sells (players buy here)
            bool buys = status.Equals("Buying", StringComparison.OrdinalIgnoreCase);

            ushort amt = 0;
            if (!ushort.TryParse(amtStr, out amt))
            {
                if (uint.TryParse(amtStr, out uint bigAmt))
                    amt = bigAmt > ushort.MaxValue ? ushort.MaxValue : (ushort)bigAmt;
            }
            if (!byte.TryParse(pctStr, out byte pct)) pct = 0;

            ProductType pt;
            if (productName.Equals("Fuel Ore", StringComparison.OrdinalIgnoreCase))
            {
                pt = ProductType.FuelOre;
                _portReportHasFuel = true;
            }
            else if (productName.Equals("Organics", StringComparison.OrdinalIgnoreCase))
            {
                pt = ProductType.Organics;
                _portReportHasOrg = true;
            }
            else
            {
                pt = ProductType.Equipment;
            }

            sector.SectorPort.BuyProduct[pt]     = buys;
            sector.SectorPort.ProductAmount[pt]  = amt;
            sector.SectorPort.ProductPercent[pt] = pct;

            // After Equipment line: derive ClassIndex from buy pattern if not already set,
            // then timestamp and close the report block.
            if (pt == ProductType.Equipment && _portReportHasFuel && _portReportHasOrg)
            {
                if (sector.SectorPort.ClassIndex == 0)
                    sector.SectorPort.ClassIndex = DeriveClassIndex(
                        sector.SectorPort.BuyProduct[ProductType.FuelOre],
                        sector.SectorPort.BuyProduct[ProductType.Organics],
                        sector.SectorPort.BuyProduct[ProductType.Equipment]);
                sector.SectorPort.Update = DateTime.Now;
                _inPortReport = false;
                GlobalModules.DebugLog($"[AutoRecorder] Port report complete: sector={_portReportSector} class={sector.SectorPort.ClassIndex}\n");
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Port product: {productName} {status} amt={amt} pct={pct}% sector={_portReportSector}\n");
        }

        /// <summary>
        /// Derives the TWX port class index from the buy/sell pattern.
        /// Pascal mapping: BBS=1, BSB=2, SBB=3, SSB=4, SBS=5, BSS=6, SSS=7, BBB=8.
        /// (B=port buys, S=port sells; order = FuelOre, Organics, Equipment)
        /// </summary>
        private static byte DeriveClassIndex(bool buyFuel, bool buyOrg, bool buyEquip)
        {
            if  ( buyFuel &&  buyOrg && !buyEquip) return 1; // BBS
            if  ( buyFuel && !buyOrg &&  buyEquip) return 2; // BSB
            if  (!buyFuel &&  buyOrg &&  buyEquip) return 3; // SBB
            if  (!buyFuel && !buyOrg &&  buyEquip) return 4; // SSB
            if  (!buyFuel &&  buyOrg && !buyEquip) return 5; // SBS
            if  ( buyFuel && !buyOrg && !buyEquip) return 6; // BSS
            if  (!buyFuel && !buyOrg && !buyEquip) return 7; // SSS
            if  ( buyFuel &&  buyOrg &&  buyEquip) return 8; // BBB
            return 0;
        }

        private static void ParseFightersLine(ModDatabase db, int sectorNum, Match m)
        {
            var sector = GetOrCreate(db, sectorNum);
            if (sector == null) return;

            // Quantity: strip commas (Pascal: StripChar(S, ','))
            string qtyStr = m.Groups[1].Value.Replace(",", "");
            if (int.TryParse(qtyStr, out int qty))
                sector.Fighters.Quantity = qty;

            // Owner: text between the first ( and ) — Pascal: Copy(Line, I, Pos(')'...)
            sector.Fighters.Owner = m.Groups[2].Value.Trim();

            // FigType: from optional trailing [Toll]/[Defensive]/[Offensive] bracket
            // Pascal: check last 6 chars for [Toll], last 11 for [Defensive], else Offensive
            string bracket = m.Groups[3].Success ? m.Groups[3].Value.Trim() : string.Empty;
            sector.Fighters.FigType = bracket.Equals("Toll", StringComparison.OrdinalIgnoreCase)
                ? FighterType.Toll
                : bracket.Equals("Defensive", StringComparison.OrdinalIgnoreCase)
                    ? FighterType.Defensive
                    : FighterType.Offensive;

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Sector {sectorNum} fighters={qty} owner={sector.Fighters.Owner} type={sector.Fighters.FigType}\n");
        }

        // "Mines   : 3 (Type 1 Armid) (belong to your Corp)"  — or continuation variant.
        // Groups: 1=quantity, 2=Armid|Limpet, 3=owner text
        private static void ParseMinesLine(ModDatabase db, int sectorNum, Match m)
        {
            var sector = GetOrCreate(db, sectorNum);
            if (sector == null) return;

            if (!int.TryParse(m.Groups[1].Value, out int qty)) return;
            string mineType = m.Groups[2].Value;
            string owner    = m.Groups[3].Value.Trim();

            if (mineType.Equals("Armid", StringComparison.OrdinalIgnoreCase))
            {
                sector.MinesArmid.Quantity = qty;
                sector.MinesArmid.Owner    = owner;
            }
            else
            {
                sector.MinesLimpet.Quantity = qty;
                sector.MinesLimpet.Owner    = owner;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Sector {sectorNum} mines {mineType}={qty} owner={owner}\n");
        }

        // ── Warp-lane helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Pascal: TModExtractor.ProcessWarpLine.
        /// Parses a line of sector numbers separated by " >" and writes each
        /// consecutive pair as a warp connection (one direction only, matching Pascal).
        /// </summary>
        private void ProcessWarpLaneLine(ModDatabase db, string line)
        {
            // Pascal: StripChar(Line, ')'); StripChar(Line, '(');
            string stripped = line.Replace("(", "").Replace(")", "");

            // Pascal: Split(Line, Sectors, ' >') — split on the two-char delimiter " >"
            string[] tokens = stripped.Split(new[] { " >" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string token in tokens)
            {
                // Pascal: CurSect := StrToIntSafe(Sectors[I]); if CurSect < 1 then exit
                // Use continue (not return) so trailing empty tokens from " >" endings
                // don't abort processing of earlier valid sectors on the same line.
                // Pascal: StrToIntSafe + range check then 'exit' (not continue) — abort
                // the whole line if any token is out of range, matching Pascal behaviour.
                if (!int.TryParse(token.Trim(), out int curSect)) return;
                if (curSect < 1 || (db.SectorCount > 0 && curSect > db.SectorCount)) return;

                if (_lastWarpLaneSect > 0)
                    AddWarpToSector(db, _lastWarpLaneSect, curSect);

                _lastWarpLaneSect = curSect;
            }
        }

        /// <summary>
        /// Pascal: TModExtractor.AddWarp.
        /// Inserts <paramref name="warp"/> into the Warp[] array of sector
        /// <paramref name="sectorNum"/> in sorted ascending order, ignoring duplicates.
        /// Sets Explored=Calc and Constellation="??? (warp calc only)" if the sector
        /// was previously unexplored (etNo).
        /// </summary>
        private static void AddWarpToSector(ModDatabase db, int sectorNum, int warp)
        {
            var sec = GetOrCreate(db, sectorNum);
            if (sec == null) return;

            // Skip if already present
            for (int i = 0; i < 6; i++)
                if (sec.Warp[i] == warp) return;

            // Find sorted insertion position (ascending; stop at first slot that is
            // empty (0) or holds a value larger than warp).
            int pos = 6; // default: all 6 slots are full with smaller values — no room
            for (int i = 0; i < 6; i++)
            {
                if (sec.Warp[i] == 0 || sec.Warp[i] > warp)
                {
                    pos = i;
                    break;
                }
            }

            if (pos < 6)
            {
                // Shift everything from pos+1 onward up by one to make room
                for (int i = 5; i > pos; i--)
                    sec.Warp[i] = sec.Warp[i - 1];
                sec.Warp[pos] = (ushort)warp;
            }

            // Pascal: if S.Explored = etNo then set Constellation/Explored/Update
            if (sec.Explored == ExploreType.No)
            {
                sec.Constellation = "??? (warp calc only)";
                sec.Explored      = ExploreType.Calc;
                sec.Update        = DateTime.Now;
            }

            db.SaveSector(sec);
            GlobalModules.DebugLog($"[AutoRecorder] AddWarp sector={sectorNum} warp={warp} (FM lane)\n");
        }

        private static SectorData? GetOrCreate(ModDatabase db, int sectorNum)
        {
            // SectorCount == 0 means universe size is unknown (live capture, no .twx loaded) — allow any positive number.
            if (sectorNum <= 0 || (db.SectorCount > 0 && sectorNum > db.SectorCount)) return null;

            var sector = db.GetSector(sectorNum);
            if (sector != null) return sector;

            // Sector not yet in dictionary — create a blank entry and save it.
            sector = new SectorData { Number = sectorNum };
            try { db.SaveSector(sector); } catch { /* ignore range errors for unknown universes */ }
            return sector;
        }

        // ── CIM helpers ────────────────────────────────────────────────────────

        /// <summary>
        /// Parses a port CIM line and updates the port in the sector database.
        /// Pascal: TModExtractor.ProcessCIMLine (dPortCIM branch).
        ///
        /// Format:  "  SSSS [-] AMOUNT PCT% [-] AMOUNT PCT% [-] AMOUNT PCT%"
        /// where the optional '-' before each product amount indicates the port
        /// buys that product (player can sell here).
        ///
        /// Detection: strip '-' and '%', then whitespace-split.
        /// Tokens: sect, ore, pOre, org, pOrg, equip, pEquip
        /// Buy indicators: scan the original token list for '-' before each amount.
        /// </summary>
        private void ParsePortCIMLine(ModDatabase db, string line)
        {
            // Strip '-' and '%' for numeric extraction (Pascal: StringReplace)
            string stripped = line.Replace("-", " ").Replace("%", " ");
            var nums = stripped.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (nums.Length < 7)
            {
                _inPortCIM = false;
                return;
            }

            if (!int.TryParse(nums[0], out int sect) || sect <= 0
                || (db.SectorCount > 0 && sect > db.SectorCount))
            {
                _inPortCIM = false;
                return;
            }

            if (!int.TryParse(nums[1], out int ore)   || !int.TryParse(nums[2], out int pOre)
             || !int.TryParse(nums[3], out int org)   || !int.TryParse(nums[4], out int pOrg)
             || !int.TryParse(nums[5], out int equip) || !int.TryParse(nums[6], out int pEquip))
            {
                _inPortCIM = false;
                return;
            }

            // Validate percent ranges (Pascal: if < 0 or > 100 → dNone; exit)
            if (ore < 0 || org < 0 || equip < 0
             || pOre < 0 || pOre > 100 || pOrg < 0 || pOrg > 100 || pEquip < 0 || pEquip > 100)
            {
                _inPortCIM = false;
                return;
            }

            var sector = GetOrCreate(db, sect);
            if (sector == null) return;

            sector.SectorPort ??= new Port();

            sector.SectorPort.ProductAmount[ProductType.FuelOre]   = (ushort)Math.Min(ore,   ushort.MaxValue);
            sector.SectorPort.ProductAmount[ProductType.Organics]  = (ushort)Math.Min(org,   ushort.MaxValue);
            sector.SectorPort.ProductAmount[ProductType.Equipment] = (ushort)Math.Min(equip, ushort.MaxValue);
            sector.SectorPort.ProductPercent[ProductType.FuelOre]   = (byte)pOre;
            sector.SectorPort.ProductPercent[ProductType.Organics]  = (byte)pOrg;
            sector.SectorPort.ProductPercent[ProductType.Equipment] = (byte)pEquip;
            sector.SectorPort.Update = DateTime.Now;

            // Buy/sell detection: only update when the port hasn't been named yet.
            // Pascal: if (S.SPort.Name = '') then ... detect from line positions.
            // We detect via token scanning: '-' token before each product amount.
            if (string.IsNullOrEmpty(sector.SectorPort.Name))
            {
                var origTokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                bool buyFuel = false, buyOrg = false, buyEquip = false;

                int ti = 1; // skip sector number token
                if (ti < origTokens.Length && origTokens[ti] == "-") { buyFuel = true; ti++; }
                ti += 2; // skip amount + percent% tokens
                if (ti < origTokens.Length && origTokens[ti] == "-") { buyOrg = true; ti++; }
                ti += 2; // skip amount + percent%
                if (ti < origTokens.Length && origTokens[ti] == "-") { buyEquip = true; }

                sector.SectorPort.BuyProduct[ProductType.FuelOre]   = buyFuel;
                sector.SectorPort.BuyProduct[ProductType.Organics]  = buyOrg;
                sector.SectorPort.BuyProduct[ProductType.Equipment] = buyEquip;
                sector.SectorPort.ClassIndex = DeriveClassIndex(buyFuel, buyOrg, buyEquip);
                sector.SectorPort.Name = "???";
            }

            // Pascal: if (S.Explored = etNo) → mark as calc-explored
            if (sector.Explored == ExploreType.No)
            {
                sector.Constellation = "??? (port data/calc only)";
                sector.Explored = ExploreType.Calc;
                sector.Update = DateTime.Now;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Port CIM: sector={sect} ore={ore}/{pOre}% org={org}/{pOrg}% equip={equip}/{pEquip}%\n");
        }

        /// <summary>
        /// Parses a warp CIM line and updates the sector's Warp[] array.
        /// Pascal: TModExtractor.ProcessCIMLine (dWarpCIM branch).
        ///
        /// Format: "SSSS W1 W2 W3 W4 W5 W6"   (up to 6 warp destinations, 0 = none)
        /// </summary>
        private void ParseWarpCIMLine(ModDatabase db, string line)
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length < 1) { _inWarpCIM = false; return; }

            if (!int.TryParse(tokens[0], out int sect) || sect <= 0
                || (db.SectorCount > 0 && sect > db.SectorCount))
            {
                _inWarpCIM = false;
                return;
            }

            var sector = GetOrCreate(db, sect);
            if (sector == null) { _inWarpCIM = false; return; }

            for (int i = 0; i < 6; i++)
            {
                if (i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int w))
                {
                    // Pascal: if X < 0 or X > Sectors → dNone; exit
                    if (w < 0 || (db.SectorCount > 0 && w > db.SectorCount))
                    {
                        _inWarpCIM = false;
                        return;
                    }
                    sector.Warp[i] = (ushort)w;
                }
                else
                {
                    sector.Warp[i] = 0;
                }
            }

            if (sector.Explored == ExploreType.No)
            {
                sector.Constellation = "??? (warp calc only)";
                sector.Explored = ExploreType.Calc;
                sector.Update = DateTime.Now;
            }

            db.SaveSector(sector);
            GlobalModules.DebugLog($"[AutoRecorder] Warp CIM: sector={sect} warps={string.Join(",", sector.Warp.Where(w => w > 0))}\n");
        }
    }
}