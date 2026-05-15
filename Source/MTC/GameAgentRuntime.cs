using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core = TWXProxy.Core;

namespace MTC;

internal enum GameAgentEventKind
{
    ServerLine,
    ServerPrompt,
    ClientInput,
    Connected,
    Disconnected,
    CurrentSectorChanged,
    ShipStatus,
    System
}

internal sealed class GameAgentEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string GameName { get; init; } = string.Empty;
    public GameAgentEventKind Kind { get; init; }
    public string PlainText { get; init; } = string.Empty;
    public string AnsiText { get; init; } = string.Empty;
    public int CurrentSector { get; init; }
    public string PromptSurface { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

internal sealed class GameAgentContextSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string GameName { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string TraderName { get; init; } = string.Empty;
    public int Corp { get; init; }
    public int CurrentSector { get; init; }
    public long Credits { get; init; }
    public int Fighters { get; init; }
    public int Shields { get; init; }
    public int HoldsEmpty { get; init; }
    public int HoldsTotal { get; init; }
    public string CurrentPrompt { get; init; } = string.Empty;
    public string EventLogPath { get; init; } = string.Empty;
    public GameAgentSectorSnapshot? CurrentSectorDetails { get; init; }
    public IReadOnlyList<GameAgentSectorSnapshot> AdjacentSectors { get; init; } = [];
    public IReadOnlyList<GameAgentEvent> RecentEvents { get; init; } = [];
}

internal sealed class GameAgentSectorSnapshot
{
    public int Number { get; init; }
    public string Explored { get; init; } = string.Empty;
    public string Constellation { get; init; } = string.Empty;
    public string Beacon { get; init; } = string.Empty;
    public int NavHaz { get; init; }
    public bool Anomaly { get; init; }
    public int Density { get; init; }
    public IReadOnlyList<int> WarpsOut { get; init; } = [];
    public IReadOnlyList<int> WarpsIn { get; init; } = [];
    public string Port { get; init; } = string.Empty;
    public IReadOnlyList<string> Planets { get; init; } = [];
    public IReadOnlyList<string> Traders { get; init; } = [];
    public IReadOnlyList<string> Ships { get; init; } = [];
    public string Fighters { get; init; } = string.Empty;
    public string ArmidMines { get; init; } = string.Empty;
    public string LimpetMines { get; init; } = string.Empty;
}

internal sealed class GameAgentReplaySnapshot
{
    public string SourcePath { get; init; } = string.Empty;
    public int EventIndex { get; init; }
    public int EventCount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string GameName { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public int CurrentSector { get; init; }
    public string CurrentPrompt { get; init; } = string.Empty;
    public long Credits { get; init; }
    public int Fighters { get; init; }
    public int Shields { get; init; }
    public int HoldsEmpty { get; init; }
    public int HoldsTotal { get; init; }
    public IReadOnlyList<GameAgentEvent> RecentEvents { get; init; } = [];
}

internal sealed class GameAgentRuntime : IDisposable
{
    private const int MaxRecentEvents = 700;
    private const int MaxQueuedEvents = 10000;

    private readonly object _sync = new();
    private readonly Queue<GameAgentEvent> _recentEvents = new();
    private readonly BlockingCollection<GameAgentEvent> _writeQueue = new(MaxQueuedEvents);
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
    };

    private string _gameName = "game";
    private string _eventLogPath = string.Empty;
    private StreamWriter? _writer;
    private bool _disposed;

    public GameAgentRuntime()
    {
        _writerTask = Task.Run(WriterLoop);
    }

    public string EventLogPath
    {
        get
        {
            lock (_sync)
                return _eventLogPath;
        }
    }

    public event Action<GameAgentEvent>? EventRecorded;

    public void SetGameName(string gameName)
    {
        string normalized = NormalizeGameName(gameName);
        lock (_sync)
        {
            if (string.Equals(_gameName, normalized, StringComparison.Ordinal))
                return;

            _gameName = normalized;
            CloseWriterUnderLock();
            _eventLogPath = string.Empty;
        }
    }

    public void Record(GameAgentEvent evt)
    {
        if (_disposed)
            return;

        GameAgentEvent normalized = NormalizeEvent(evt);
        lock (_sync)
        {
            _recentEvents.Enqueue(normalized);
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.Dequeue();
        }

        if (!_writeQueue.TryAdd(normalized))
        {
            RecordInMemoryOnly(new GameAgentEvent
            {
                GameName = normalized.GameName,
                Kind = GameAgentEventKind.System,
                PlainText = "Game agent event queue overflow; dropped an event.",
                CurrentSector = normalized.CurrentSector,
                Metadata = new Dictionary<string, string>
                {
                    ["droppedKind"] = normalized.Kind.ToString(),
                },
            });
        }

        EventRecorded?.Invoke(normalized);
    }

    public IReadOnlyList<GameAgentEvent> GetRecentEvents(int count = 120)
    {
        lock (_sync)
        {
            return _recentEvents
                .Reverse()
                .Take(Math.Max(1, count))
                .Reverse()
                .ToArray();
        }
    }

    public GameAgentContextSnapshot BuildContextSnapshot(GameState state, Core.ModDatabase? database, int recentEventCount = 80)
    {
        string gameName = NormalizeGameName(state.GameName);
        if (string.Equals(gameName, "game", StringComparison.OrdinalIgnoreCase))
            gameName = _gameName;

        string prompt = ResolvePromptSurface();
        return new GameAgentContextSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GameName = gameName,
            Connected = state.Connected,
            Host = state.Host,
            Port = state.Port,
            TraderName = state.TraderName,
            Corp = state.Corp,
            CurrentSector = state.Sector,
            Credits = state.Credits,
            Fighters = state.Fighters,
            Shields = state.Shields,
            HoldsEmpty = state.HoldsEmpty,
            HoldsTotal = state.HoldsTotal,
            CurrentPrompt = prompt,
            EventLogPath = EventLogPath,
            CurrentSectorDetails = BuildSectorSnapshot(database, state.Sector),
            AdjacentSectors = BuildAdjacentSectorSnapshots(database, state.Sector),
            RecentEvents = GetRecentEvents(recentEventCount),
        };
    }

    public static GameAgentReplaySnapshot BuildReplaySnapshot(string sourcePath, IReadOnlyList<GameAgentEvent> events, int index, int recentEventCount = 40)
    {
        if (events.Count == 0)
        {
            return new GameAgentReplaySnapshot
            {
                SourcePath = sourcePath,
                EventIndex = 0,
                EventCount = 0,
            };
        }

        int safeIndex = Math.Clamp(index, 0, events.Count - 1);
        bool connected = false;
        int currentSector = 0;
        string currentPrompt = string.Empty;
        long credits = 0;
        int fighters = 0;
        int shields = 0;
        int holdsEmpty = 0;
        int holdsTotal = 0;

        for (int i = 0; i <= safeIndex; i++)
        {
            GameAgentEvent evt = events[i];
            if (evt.CurrentSector > 0)
                currentSector = evt.CurrentSector;

            switch (evt.Kind)
            {
                case GameAgentEventKind.Connected:
                    connected = true;
                    break;
                case GameAgentEventKind.Disconnected:
                    connected = false;
                    break;
                case GameAgentEventKind.ServerPrompt:
                    currentPrompt = !string.IsNullOrWhiteSpace(evt.PromptSurface) ? evt.PromptSurface : evt.PlainText;
                    break;
                case GameAgentEventKind.CurrentSectorChanged:
                    if (evt.CurrentSector > 0)
                        currentSector = evt.CurrentSector;
                    break;
                case GameAgentEventKind.ShipStatus:
                    credits = ReadLongMetadata(evt, "credits", credits);
                    fighters = ReadIntMetadata(evt, "fighters", fighters);
                    shields = ReadIntMetadata(evt, "shields", shields);
                    holdsEmpty = ReadIntMetadata(evt, "holdsEmpty", holdsEmpty);
                    holdsTotal = ReadIntMetadata(evt, "holdsTotal", holdsTotal);
                    break;
            }
        }

        GameAgentEvent current = events[safeIndex];
        return new GameAgentReplaySnapshot
        {
            SourcePath = sourcePath,
            EventIndex = safeIndex,
            EventCount = events.Count,
            Timestamp = current.Timestamp,
            GameName = current.GameName,
            Connected = connected,
            CurrentSector = currentSector,
            CurrentPrompt = currentPrompt,
            Credits = credits,
            Fighters = fighters,
            Shields = shields,
            HoldsEmpty = holdsEmpty,
            HoldsTotal = holdsTotal,
            RecentEvents = events
                .Take(safeIndex + 1)
                .Reverse()
                .Take(Math.Max(1, recentEventCount))
                .Reverse()
                .ToArray(),
        };
    }

    public static IEnumerable<GameAgentEvent> ReadEvents(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            GameAgentEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<GameAgentEvent>(line);
            }
            catch
            {
                // Skip malformed lines so a partial write does not poison replay.
            }

            if (evt != null)
                yield return evt;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _writeQueue.CompleteAdding();
        try { _writerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        lock (_sync)
            CloseWriterUnderLock();
        _writeQueue.Dispose();
    }

    private GameAgentEvent NormalizeEvent(GameAgentEvent evt)
    {
        string gameName = NormalizeGameName(string.IsNullOrWhiteSpace(evt.GameName) ? _gameName : evt.GameName);
        return new GameAgentEvent
        {
            Timestamp = evt.Timestamp == default ? DateTimeOffset.UtcNow : evt.Timestamp,
            GameName = gameName,
            Kind = evt.Kind,
            PlainText = evt.PlainText ?? string.Empty,
            AnsiText = evt.AnsiText ?? string.Empty,
            CurrentSector = evt.CurrentSector,
            PromptSurface = string.IsNullOrWhiteSpace(evt.PromptSurface) ? ResolvePromptSurface() : evt.PromptSurface,
            Metadata = evt.Metadata ?? [],
        };
    }

    private void RecordInMemoryOnly(GameAgentEvent evt)
    {
        lock (_sync)
        {
            _recentEvents.Enqueue(NormalizeEvent(evt));
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.Dequeue();
        }
    }

    private void WriterLoop()
    {
        DateTime lastFlushUtc = DateTime.UtcNow;
        foreach (GameAgentEvent evt in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
                StreamWriter writer = EnsureWriter(evt.GameName);
                string json = JsonSerializer.Serialize(evt, _jsonOptions);
                writer.WriteLine(json);
                DateTime now = DateTime.UtcNow;
                if ((now - lastFlushUtc).TotalMilliseconds >= 750)
                {
                    writer.Flush();
                    lastFlushUtc = now;
                }
            }
            catch (Exception ex)
            {
                RecordInMemoryOnly(new GameAgentEvent
                {
                    GameName = evt.GameName,
                    Kind = GameAgentEventKind.System,
                    PlainText = $"Game agent event write failed: {ex.Message}",
                    CurrentSector = evt.CurrentSector,
                });
            }
        }

        lock (_sync)
            _writer?.Flush();
    }

    private StreamWriter EnsureWriter(string gameName)
    {
        string path = BuildEventLogPath(gameName);
        lock (_sync)
        {
            if (_writer != null && string.Equals(_eventLogPath, path, StringComparison.Ordinal))
                return _writer;

            CloseWriterUnderLock();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = false,
            };
            _eventLogPath = path;
            return _writer;
        }
    }

    private static string BuildEventLogPath(string gameName)
    {
        string safeGameName = NormalizeGameName(gameName);
        string dir = Path.Combine(AppPaths.TwxproxyGamesDir, safeGameName, "agent");
        return Path.Combine(dir, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    private void CloseWriterUnderLock()
    {
        try { _writer?.Flush(); } catch { }
        try { _writer?.Dispose(); } catch { }
        _writer = null;
    }

    private static string NormalizeGameName(string? gameName)
    {
        string safe = Core.SharedPaths.SanitizeFileComponent(gameName ?? string.Empty);
        return string.IsNullOrWhiteSpace(safe) ? "game" : safe;
    }

    private static string ResolvePromptSurface()
    {
        string currentLine = Core.ScriptRef.GetCurrentLine();
        if (string.IsNullOrWhiteSpace(currentLine))
            return string.Empty;

        string trimmed = Core.AnsiCodes.NormalizeTerminalText(currentLine).Trim();
        int marker = trimmed.IndexOf(" [TL=", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
            return trimmed[..marker].Trim();

        marker = trimmed.IndexOf(" command", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
            return trimmed[..marker].Trim();

        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static IReadOnlyList<GameAgentSectorSnapshot> BuildAdjacentSectorSnapshots(Core.ModDatabase? database, int currentSector)
    {
        Core.SectorData? sector = database?.GetSector(currentSector);
        if (database == null || sector == null)
            return [];

        return sector.Warp
            .Where(warp => warp > 0)
            .Distinct()
            .Take(8)
            .Select(warp => BuildSectorSnapshot(database, warp))
            .Where(snapshot => snapshot != null)
            .Cast<GameAgentSectorSnapshot>()
            .ToArray();
    }

    private static GameAgentSectorSnapshot? BuildSectorSnapshot(Core.ModDatabase? database, int sectorNumber)
    {
        if (database == null || sectorNumber <= 0)
            return null;

        Core.SectorData? sector = database.GetSector(sectorNumber);
        if (sector == null)
            return new GameAgentSectorSnapshot { Number = sectorNumber, Explored = "Unknown" };

        return new GameAgentSectorSnapshot
        {
            Number = sectorNumber,
            Explored = sector.Explored.ToString(),
            Constellation = sector.Constellation ?? string.Empty,
            Beacon = sector.Beacon ?? string.Empty,
            NavHaz = sector.NavHaz,
            Anomaly = sector.Anomaly,
            Density = sector.Density,
            WarpsOut = sector.Warp.Where(warp => warp > 0).Select(warp => (int)warp).ToArray(),
            WarpsIn = sector.WarpsIn.Where(warp => warp > 0).Select(warp => (int)warp).OrderBy(warp => warp).ToArray(),
            Port = FormatPort(sector.SectorPort),
            Planets = database.GetPlanetNamesInSector(sectorNumber).Where(name => !string.IsNullOrWhiteSpace(name)).Take(12).ToArray(),
            Traders = sector.Traders.Select(FormatTrader).Where(value => value.Length > 0).Take(12).ToArray(),
            Ships = sector.Ships.Select(FormatShip).Where(value => value.Length > 0).Take(12).ToArray(),
            Fighters = FormatSpaceObject(sector.Fighters, includeType: true),
            ArmidMines = FormatSpaceObject(sector.MinesArmid, includeType: false),
            LimpetMines = FormatSpaceObject(sector.MinesLimpet, includeType: false),
        };
    }

    private static string FormatPort(Core.Port? port)
    {
        if (port == null || port.Dead || string.IsNullOrWhiteSpace(port.Name))
            return string.Empty;

        return $"{port.Name.Trim()} class {port.ClassIndex} {FormatPortProducts(port)}".TrimEnd();
    }

    private static string FormatPortProducts(Core.Port port)
    {
        static char Product(Core.Port p, Core.ProductType type)
            => p.BuyProduct.TryGetValue(type, out bool buys) && buys ? 'B' : 'S';

        if (port.ClassIndex is 0 or 9)
            return "(special)";

        return $"({Product(port, Core.ProductType.FuelOre)}{Product(port, Core.ProductType.Organics)}{Product(port, Core.ProductType.Equipment)})";
    }

    private static string FormatTrader(Core.Trader trader)
    {
        string name = (trader.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        string ship = string.IsNullOrWhiteSpace(trader.ShipType) ? string.Empty : $" in {trader.ShipType.Trim()}";
        string fighters = trader.Fighters > 0 ? $" with {trader.Fighters:N0} figs" : string.Empty;
        return $"{name}{ship}{fighters}";
    }

    private static string FormatShip(Core.Ship ship)
    {
        string name = (ship.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        string owner = string.IsNullOrWhiteSpace(ship.Owner) ? string.Empty : $" owned by {ship.Owner.Trim()}";
        string fighters = ship.Fighters > 0 ? $" with {ship.Fighters:N0} figs" : string.Empty;
        return $"{name}{owner}{fighters}";
    }

    private static string FormatSpaceObject(Core.SpaceObject? obj, bool includeType)
    {
        if (obj == null || obj.Quantity <= 0)
            return string.Empty;

        string owner = string.IsNullOrWhiteSpace(obj.Owner) ? string.Empty : $" ({obj.Owner.Trim()})";
        string type = includeType ? $" {obj.FigType}" : string.Empty;
        return $"{obj.Quantity:N0}{type}{owner}".Trim();
    }

    private static int ReadIntMetadata(GameAgentEvent evt, string key, int fallback)
        => evt.Metadata.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : fallback;

    private static long ReadLongMetadata(GameAgentEvent evt, string key, long fallback)
        => evt.Metadata.TryGetValue(key, out string? value) && long.TryParse(value, out long parsed) ? parsed : fallback;
}
