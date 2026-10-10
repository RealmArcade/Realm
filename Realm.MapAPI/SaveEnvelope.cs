namespace Realm.MapAPI;

/// <summary>
/// Represents a structured envelope wrapping a save payload with versioning, player identity, and cryptographic integrity data.
/// </summary>
/// <typeparam name="T">The type of the saved game state payload.</typeparam>
public class SaveEnvelope<T>
{
    /// <summary>
    /// Gets or sets the schema version number of the save payload.
    /// </summary>
    public int DataVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the name of the map script associated with this save file.
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player identifier or username bound to this save file.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the ISO-8601 UTC timestamp string representing when the save file was created or last written.
    /// </summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cryptographic signature string used for integrity verification and anti-tamper checking.
    /// </summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the typed game state payload.
    /// </summary>
    public T? Payload { get; set; }
}