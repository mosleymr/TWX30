using System.Collections.Generic;

namespace MTC.mombot;

internal sealed class mombotConfig
{
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; }
    public bool AcceptSelfCommands { get; set; } = true;
    public bool AcceptSubspaceCommands { get; set; } = true;
    public bool AcceptPrivateCommands { get; set; } = true;
    public bool WatcherEnabled { get; set; } = true;
    public string ScriptRoot { get; set; } = "scripts/mombot";
    public List<string> AuthorizedUsers { get; set; } = new();
}
