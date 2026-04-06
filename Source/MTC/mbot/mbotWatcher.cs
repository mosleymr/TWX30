using System.Collections.Generic;
using Core = TWXProxy.Core;

namespace MTC.mbot;

internal sealed class mbotWatcher
{
    private Core.GameInstance? _gameInstance;
    private Core.ModDatabase? _database;

    public bool IsAttached => _gameInstance != null;

    public IReadOnlyList<string> Responsibilities { get; } = new[]
    {
        "Subspace/channel tracking",
        "Fig, mine, bust, and LRA sector markers",
        "Ship and planet number capture",
        "Planet movement updates",
        "Watcher-style auto-restart/health checks",
        "Emergency reboot handling",
    };

    public void Attach(Core.GameInstance? gameInstance, Core.ModDatabase? database)
    {
        _gameInstance = gameInstance;
        _database = database;
    }

    public void Detach()
    {
        _gameInstance = null;
        _database = null;
    }

    public void ObserveServerLine(string line)
    {
        _ = line;
        _ = _database;
        // Intentionally left as a placeholder for the MTC-local watcher port.
    }
}
