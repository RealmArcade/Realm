using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Steam;

/// <summary>
///     What a Workshop item states about the map it carries, written into the item's own metadata so a browser can read a
///     map's identity, version and engine generation without downloading it. Steam knows the item; this is the part only
///     Realm can say.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public class WorkshopMapMetadata
{
	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
	};

	/// <summary>The map's folder name, which is what a lobby states and a join matches on.</summary>
	public string MapName { get; set; } = string.Empty;

	/// <summary>The version the author published.</summary>
	public string Version { get; set; } = string.Empty;

	/// <summary>The game build the map was authored against, which decides whether it can be played at all.</summary>
	public string GameBuildNumber { get; set; } = string.Empty;

	/// <summary>The hash of the packaged map content, which is what two peers compare before a match starts.</summary>
	public string ContentHash { get; set; } = string.Empty;

	/// <summary>The SteamID64 of the author who published this item.</summary>
	public ulong AuthorSteamId { get; set; }

	/// <summary>The Workshop ids of the maps and asset packs this map was built against.</summary>
	public ulong[] Dependencies { get; set; } = Array.Empty<ulong>();

	/// <summary>Whether this item is the shared editor art pack rather than a playable map.</summary>
	public bool IsAssetPack { get; set; }

	/// <summary>
	///     Serializes the identity for an item's metadata field.
	/// </summary>
	/// <returns>The metadata text Steam stores with the item.</returns>
	public string ToJson()
	{
		return JsonSerializer.Serialize(this, SerializerOptions);
	}

	/// <summary>
	///     Reads the identity an item states. An item published before this field existed, or one written by hand, states
	///     nothing usable, and that is answered as an empty identity rather than as a failure: the item still exists and
	///     can still be subscribed to.
	/// </summary>
	/// <param name="json">The item's metadata text.</param>
	/// <returns>The identity the text states, or an empty one.</returns>
	public static WorkshopMapMetadata FromJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json)) return new WorkshopMapMetadata();

		try
		{
			return JsonSerializer.Deserialize<WorkshopMapMetadata>(json, SerializerOptions) ?? new WorkshopMapMetadata();
		}
		catch (JsonException)
		{
			return new WorkshopMapMetadata();
		}
	}
}
