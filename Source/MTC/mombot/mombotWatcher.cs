using System;
using System.Collections.Generic;
using Core = TWXProxy.Core;

namespace MTC.mombot;

internal sealed class mombotWatcher
{
    private const string UnderAttackLead = "Shipboard Computers";
    private const string UnderAttackTail = "is powering up weapons systems!";

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

    public bool ObserveServerLine(string line)
    {
        _ = _database;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (line.Contains(UnderAttackLead, StringComparison.Ordinal) ||
            line.Contains(UnderAttackTail, StringComparison.Ordinal))
        {
            SetRedAlertVars("TRUE");
            return true;
        }

        return false;
    }

    private static void SetRedAlertVars(string value)
    {
        PersistCurrentGameVar("$BOT~REDALERT", value);
        PersistCurrentGameVar("$BOT~redalert", value);
        PersistCurrentGameVar("$bot~redalert", value);
        PersistCurrentGameVar("$redalert", value);
    }

    private static void PersistCurrentGameVar(string name, string value)
    {
        Core.ScriptRef.SetCurrentGameVar(name, value);
        Core.ScriptRef.OnVariableSaved?.Invoke(name, value);
    }
}
