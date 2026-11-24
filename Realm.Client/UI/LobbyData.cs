using Realm.Client.Services;

namespace Realm.Client.UI;

/// <summary>
///     One lobby as the browser shows it. Everything here comes from Steam's own lobby metadata and membership, because
///     Steam is the directory: there is no address, port, region or reachability measurement to carry.
/// </summary>
public struct LobbyData
{
	/// <summary>The Steam lobby id, as a string the browser hands back to the join.</summary>
	public string LobbyId;

	/// <summary>The map's path name.</summary>
	public string Map;

	/// <summary>The map version the host will run.</summary>
	public string MapVersion;

	/// <summary>The match mode.</summary>
	public string Mode;

	/// <summary>The Steam persona name of the lobby's owner, who is the host.</summary>
	public string HostName;

	/// <summary>The host's SteamID64, which is what a joiner connects to.</summary>
	public string HostSteamId;

	/// <summary>Players standing in the lobby, over the seats its map offers.</summary>
	public string Players;

	/// <summary>The game build the host runs.</summary>
	public string GameVersion;

	/// <summary>Whether the lobby is gated by a join code. The code itself is never published.</summary>
	public bool RequiresCode;

	/// <summary>Whether this screen can play the lobby's map, with the reason when it cannot.</summary>
	public bool IsCompatible;

	/// <summary>Why this screen cannot play the lobby's map, or an empty string when it can.</summary>
	public string IncompatibleReason;

	/// <summary>How much Steam vouches for the host's map, as the host stated it.</summary>
	public MapTrustState Trust;
}
