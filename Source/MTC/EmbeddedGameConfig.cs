using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MTC;

/// <summary>
/// Subset of TWXP's GameConfig JSON schema used by embedded games in MTC.
/// <para>
/// <see cref="JsonExtensionDataAttribute"/> on <see cref="Extra"/> ensures all fields that
/// TWXP writes (ListenPort, CommandChar, AutoConnect, …) are round-tripped transparently,
/// so a file can be shared safely between the standalone TWXP proxy and MTC.
/// </para>
/// </summary>
internal class EmbeddedGameConfig
{
    public string Id      { get; set; } = Guid.NewGuid().ToString();
    public string Name    { get; set; } = string.Empty;
    public string Host    { get; set; } = string.Empty;
    public int    Port    { get; set; } = 23;
    public int    Sectors { get; set; } = 1000;
    public int    ListenPort { get; set; } = 2300;
    public char   CommandChar { get; set; } = '$';
    public bool   AutoReconnect { get; set; }
    public int    ReconnectDelaySeconds { get; set; } = 5;
    public bool   NativeHaggleEnabled { get; set; } = true;
    public bool   UseCache { get; set; } = true;
    public int    BubbleSize { get; set; } = 25;
    public bool   LocalEcho { get; set; } = true;
    public bool   LogEnabled { get; set; } = true;
    public bool   LogAnsi { get; set; }
    public bool   LogBinary { get; set; }
    public bool   NotifyPlayCuts { get; set; } = true;
    public int    MaxPlayDelay { get; set; } = 10000;
    public bool   AcceptExternal { get; set; } = true;
    public bool   AllowLerkers { get; set; } = true;
    public bool   BroadcastMessages { get; set; } = true;
    public string ExternalAddress { get; set; } = string.Empty;
    public string LerkerAddress { get; set; } = string.Empty;
    public bool   StreamingMode { get; set; }
    public bool   UseLogin { get; set; }
    public bool   UseRLogin { get; set; }
    public string LoginScript { get; set; } = "0_Login.cts";
    public string LoginName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string GameLetter { get; set; } = string.Empty;

    /// <summary>Variables persisted by <c>savevar</c> / retrieved by <c>loadvar</c>.</summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>Preserves every TWXP GameConfig field not listed above.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
