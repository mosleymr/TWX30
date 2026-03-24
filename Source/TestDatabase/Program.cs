// Test the Database module
using TWXProxy.Core;

Console.WriteLine("TWX Database Module Test");
Console.WriteLine("========================\n");

// Test TWX26 database import
Console.WriteLine("=== TWX26 Database Import Test ===\n");

// Path to the TWX26 database file
string dbPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "..",
    "wcc_i.xdb.twx"
);

// Try multiple possible paths
if (!File.Exists(dbPath))
{
    dbPath = Path.Combine(
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
        "..", "..", "..", "..",
        "wcc_i.xdb.twx"
    );
}

if (!File.Exists(dbPath))
{
    dbPath = "../wcc_i.xdb.twx";
}

if (!File.Exists(dbPath))
{
    Console.WriteLine($"ERROR: Database file 'wcc_i.xdb.twx' not found");
    Console.WriteLine("Please ensure wcc_i.xdb.twx is in the Source directory");
    Environment.Exit(1);
}

Console.WriteLine($"Loading database from: {dbPath}\n");

try
{
    // Create database instance and load TWX26 file
    var database = new ModDatabase();
    database.LoadFromTWX26(dbPath);

    // Display header information
    var header = database.DBHeader;

    Console.WriteLine("=== DATABASE HEADER ===");
    Console.WriteLine($"Program Name:     {header.ProgramName}");
    Console.WriteLine($"Version:          {header.Version}");
    Console.WriteLine($"Sectors:          {header.Sectors}");
    Console.WriteLine($"StarDock:         {header.StarDock}");
    Console.WriteLine($"AlphaCentauri:    {header.AlphaCentauri}");
    Console.WriteLine($"Rylos:            {header.Rylos}");
    Console.WriteLine($"Address:          {header.Address}");
    Console.WriteLine($"Description:      {header.Description}");
    Console.WriteLine($"Server Port:      {header.ServerPort}");
    Console.WriteLine($"Listen Port:      {header.ListenPort}");
    Console.WriteLine($"Login Name:       {header.LoginName}");
    Console.WriteLine($"Game:             {header.Game}");
    Console.WriteLine($"Icon File:        {header.IconFile}");
    Console.WriteLine($"Use RLogin:       {header.UseRLogin}");
    Console.WriteLine($"Use Login:        {header.UseLogin}");
    Console.WriteLine($"Rob Factor:       {header.RobFactor}");
    Console.WriteLine($"Steal Factor:     {header.StealFactor}");
    Console.WriteLine($"Last Port CIM:    {header.LastPortCIM}");
    Console.WriteLine();

    // Display some sample sector data
    Console.WriteLine("=== SAMPLE SECTOR DATA ===");
    DisplaySectorInfo(database, 1);

    if (header.StarDock > 0 && header.StarDock <= header.Sectors)
    {
        Console.WriteLine();
        DisplaySectorInfo(database, header.StarDock);
    }

    if (header.AlphaCentauri > 0 && header.AlphaCentauri <= header.Sectors)
    {
        Console.WriteLine();
        DisplaySectorInfo(database, header.AlphaCentauri);
    }

    Console.WriteLine();
    Console.WriteLine("✓ Database loaded successfully!");
    Console.WriteLine($"Total sectors loaded: {database.SectorCount}");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ ERROR: Failed to load database");
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    Environment.Exit(1);
}

static void DisplaySectorInfo(ModDatabase database, int sectorNum)
{
    var sector = database.GetSector(sectorNum);

    if (sector == null)
    {
        Console.WriteLine($"Sector {sectorNum}: Not found");
        return;
    }

    Console.WriteLine($"Sector {sectorNum}:");
    Console.Write("  Warps: ");
    if (sector.Warp != null)
    {
        for (int i = 0; i < sector.Warp.Length; i++)
        {
            if (sector.Warp[i] > 0)
                Console.Write($"{sector.Warp[i]} ");
        }
    }
    Console.WriteLine();

    if (sector.SectorPort != null)
    {
        Console.WriteLine($"  Port: {sector.SectorPort.Name}");
        Console.WriteLine($"    Class: {sector.SectorPort.ClassIndex}");
        Console.WriteLine($"    Dead: {sector.SectorPort.Dead}");
    }

    if (!string.IsNullOrEmpty(sector.Beacon))
        Console.WriteLine($"  Beacon: {sector.Beacon}");

    if (!string.IsNullOrEmpty(sector.Constellation))
        Console.WriteLine($"  Constellation: {sector.Constellation}");

    if (sector.Fighters?.Quantity > 0)
        Console.WriteLine($"  Fighters: {sector.Fighters.Quantity} ({sector.Fighters.Owner})");

    Console.WriteLine($"  NavHaz: {sector.NavHaz}%");
    Console.WriteLine($"  Explored: {sector.Explored}");
}
