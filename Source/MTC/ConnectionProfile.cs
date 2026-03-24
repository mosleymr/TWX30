using System.Xml.Linq;

namespace MTC;

/// <summary>
/// Transport protocol for the game connection.
/// </summary>
public enum TwProtocol { Telnet, Rlogin }

/// <summary>
/// All settings needed to establish and configure a game connection,
/// including the last-known ship state so the sidebar is pre-populated
/// when the file is re-opened.
/// Serialised to/from a UTF-8 XML file with the ".mtc" extension.
/// </summary>
public class ConnectionProfile
{
    // ── Connection ─────────────────────────────────────────────────────────
    public string     Server          { get; set; } = string.Empty;
    public int        Port            { get; set; } = 2002;
    public TwProtocol Protocol        { get; set; } = TwProtocol.Telnet;

    // ── TWXProxy integration ───────────────────────────────────────────────
    public bool       LocalTwxProxy   { get; set; } = true;
    public string     TwxProxyDbPath  { get; set; } = string.Empty;

    // ── Embedded proxy (native script engine inside MTC) ───────────────────
    /// <summary>When true MTC runs the TWX proxy engine in-process instead of using a bare telnet connection.</summary>
    public bool       EmbeddedProxy   { get; set; } = false;
    /// <summary>Universe size in sectors — used to pre-size the database when the embedded proxy creates it for the first time.</summary>
    public int        Sectors         { get; set; } = 1000;
    /// <summary>When true the embedded proxy automatically reconnects to the server after a disconnect.</summary>
    public bool       AutoReconnect   { get; set; } = false;

    // ── Terminal settings ──────────────────────────────────────────────────
    /// <summary>Maximum number of lines retained in the off-screen scrollback buffer.</summary>
    public int        ScrollbackLines { get; set; } = 2000;

    // ── Trader info (last known) ───────────────────────────────────────────
    public string     TraderName   { get; set; } = string.Empty;
    public int        Sector       { get; set; } = 0;
    public int        Turns        { get; set; } = 0;
    public int        Experience   { get; set; } = 0;
    public string     Alignment    { get; set; } = "0";
    public long       Credits      { get; set; } = 0;
    public int        Corp         { get; set; } = 0;

    // ── Ship info (last known) ─────────────────────────────────────────────
    public string     ShipName     { get; set; } = string.Empty;
    public int        HoldsTotal   { get; set; } = 0;
    public int        FuelOre      { get; set; } = 0;
    public int        Organics     { get; set; } = 0;
    public int        Equipment    { get; set; } = 0;
    public int        Colonists    { get; set; } = 0;
    public int        HoldsEmpty   { get; set; } = 0;
    public int        Fighters     { get; set; } = 0;
    public int        Shields      { get; set; } = 0;
    public int        TurnsPerWarp { get; set; } = 0;

    // ── Combat items (last known) ──────────────────────────────────────────
    public int        Etheral      { get; set; } = 0;
    public int        Beacon       { get; set; } = 0;
    public int        Disruptor    { get; set; } = 0;
    public int        Photon       { get; set; } = 0;
    public int        Armor        { get; set; } = 0;
    public int        Limpet       { get; set; } = 0;
    public int        Genesis      { get; set; } = 0;
    public int        Atomic       { get; set; } = 0;
    public int        Corbomite    { get; set; } = 0;
    public int        Cloak        { get; set; } = 0;
    public int        TranswarpDrive1 { get; set; } = 0;
    public int        TranswarpDrive2 { get; set; } = 0;
    public bool       ScannerD     { get; set; } = false;
    public bool       ScannerH     { get; set; } = false;
    public bool       ScannerP     { get; set; } = false;

    // ── Serialisation ──────────────────────────────────────────────────────

    /// <summary>Save this profile to an XML ".mtc" file.</summary>
    public void SaveXml(string path)
    {
        var doc = new XDocument(
            new XElement("MtcConnection",
                // Connection
                new XElement("Server",          Server),
                new XElement("Port",            Port),
                new XElement("Protocol",        Protocol.ToString()),
                new XElement("LocalTwxProxy",   LocalTwxProxy),
                new XElement("TwxProxyDbPath",  TwxProxyDbPath),
                new XElement("EmbeddedProxy",   EmbeddedProxy),
                new XElement("AutoReconnect",   AutoReconnect),
                new XElement("ScrollbackLines", ScrollbackLines),
                // Trader info
                new XElement("TraderName",      TraderName),
                new XElement("Sector",          Sector),
                new XElement("Turns",           Turns),
                new XElement("Experience",      Experience),
                new XElement("Alignment",       Alignment),
                new XElement("Credits",         Credits),
                new XElement("Corp",            Corp),
                // Ship
                new XElement("ShipName",        ShipName),
                new XElement("HoldsTotal",      HoldsTotal),
                new XElement("FuelOre",         FuelOre),
                new XElement("Organics",        Organics),
                new XElement("Equipment",       Equipment),
                new XElement("Colonists",       Colonists),
                new XElement("HoldsEmpty",      HoldsEmpty),
                new XElement("Fighters",        Fighters),
                new XElement("Shields",         Shields),
                new XElement("TurnsPerWarp",    TurnsPerWarp),
                // Combat
                new XElement("Etheral",         Etheral),
                new XElement("Beacon",          Beacon),
                new XElement("Disruptor",       Disruptor),
                new XElement("Photon",          Photon),
                new XElement("Armor",           Armor),
                new XElement("Limpet",          Limpet),
                new XElement("Genesis",         Genesis),
                new XElement("Atomic",          Atomic),
                new XElement("Corbomite",       Corbomite),
                new XElement("Cloak",           Cloak),
                new XElement("TranswarpDrive1", TranswarpDrive1),
                new XElement("TranswarpDrive2", TranswarpDrive2),
                new XElement("ScannerD",        ScannerD),
                new XElement("ScannerH",        ScannerH),
                new XElement("ScannerP",        ScannerP)
            )
        );
        doc.Save(path);
    }

    /// <summary>Load a profile from an XML ".mtc" file.</summary>
    public static ConnectionProfile LoadXml(string path)
    {
        var root = XDocument.Load(path).Root
                   ?? throw new InvalidDataException($"Empty or invalid MTC file: {path}");

        int I(string name, int def = 0)   => (int?)   root.Element(name) ?? def;
        long L(string name, long def = 0) => (long?)  root.Element(name) ?? def;
        bool B(string name, bool def = false) => (bool?)root.Element(name) ?? def;
        string S(string name, string def = "") => (string?)root.Element(name) ?? def;

        var p = new ConnectionProfile();
        // Connection
        p.Server          = S("Server");
        p.Port            = I("Port", 2002);
        p.Protocol        = Enum.TryParse<TwProtocol>(S("Protocol"), out var proto)
                            ? proto : TwProtocol.Telnet;
        p.LocalTwxProxy   = B("LocalTwxProxy", true);
        p.TwxProxyDbPath  = S("TwxProxyDbPath");
        p.EmbeddedProxy   = B("EmbeddedProxy", false);
        p.AutoReconnect   = B("AutoReconnect",  false);
        p.ScrollbackLines = I("ScrollbackLines", 2000);
        // Trader
        p.TraderName    = S("TraderName");
        p.Sector        = I("Sector");
        p.Turns         = I("Turns");
        p.Experience    = I("Experience");
        p.Alignment     = S("Alignment", "0");
        p.Credits       = L("Credits");
        p.Corp          = I("Corp");
        // Ship
        p.ShipName      = S("ShipName");
        p.HoldsTotal    = I("HoldsTotal");
        p.FuelOre       = I("FuelOre");
        p.Organics      = I("Organics");
        p.Equipment     = I("Equipment");
        p.Colonists     = I("Colonists");
        p.HoldsEmpty    = I("HoldsEmpty");
        p.Fighters      = I("Fighters");
        p.Shields       = I("Shields");
        p.TurnsPerWarp  = I("TurnsPerWarp");
        // Combat
        p.Etheral       = I("Etheral");
        p.Beacon        = I("Beacon");
        p.Disruptor     = I("Disruptor");
        p.Photon        = I("Photon");
        p.Armor         = I("Armor");
        p.Limpet        = I("Limpet");
        p.Genesis       = I("Genesis");
        p.Atomic        = I("Atomic");
        p.Corbomite     = I("Corbomite");
        p.Cloak         = I("Cloak");
        p.TranswarpDrive1 = I("TranswarpDrive1");
        p.TranswarpDrive2 = I("TranswarpDrive2");
        p.ScannerD      = B("ScannerD");
        p.ScannerH      = B("ScannerH");
        p.ScannerP      = B("ScannerP");
        return p;
    }
}
