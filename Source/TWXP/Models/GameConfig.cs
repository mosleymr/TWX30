using System.Text.Json.Serialization;

namespace TWXP.Models;

public class GameConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 23;
    public int ListenPort { get; set; } = 2300;
    public char CommandChar { get; set; } = '$';
    public int Sectors { get; set; } = 1000;
    public string DatabasePath { get; set; } = string.Empty;
    public string ScriptDirectory { get; set; } = string.Empty;
    public bool AutoConnect { get; set; }
    public bool NativeHaggleEnabled { get; set; } = true;
    
    // Runtime status - not persisted
    [JsonIgnore]
    public GameStatus Status { get; set; } = GameStatus.Stopped;
    
    public DateTime? LastConnected { get; set; }
    
    // Additional settings
    public bool UseEncryption { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Variables persisted via savevar/loadvar for this game.
    /// Stored in the game's GAMENAME.json data file alongside the config.
    /// </summary>
    public Dictionary<string, string> Variables { get; set; } = new();

    /// <summary>
    /// Runtime path to this game's GAMENAME.json data file.
    /// Set when the config is loaded from disk; not serialised.
    /// </summary>
    [JsonIgnore]
    public string GameDataFilePath { get; set; } = string.Empty;
}

public enum GameStatus
{
    Stopped,
    Starting,
    Running,
    Paused,
    Error
}
