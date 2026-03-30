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
    public bool   NativeHaggleEnabled { get; set; } = true;

    /// <summary>Variables persisted by <c>savevar</c> / retrieved by <c>loadvar</c>.</summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>Preserves every TWXP GameConfig field not listed above.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}
