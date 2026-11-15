using Realm.Shared.Reachability;

namespace Realm.AdminServer.Models;

public class LobbyInfo
{
	public required string LobbyId { get; set; }
	public required string Map { get; set; }
	public required string HostIP { get; set; }
	public required int HostPort { get; set; }
	public required string NatType { get; set; }
	public required string PasswordHash { get; set; }
	public required int MaxPlayers { get; set; }
	public required int SlotsUsed { get; set; }
	public string? GameVersion { get; set; }
	public string? MapVersion { get; set; }
	public bool IsGreenlit { get; set; }
	public string? MapCreator { get; set; }
	public double Latitude { get; set; }
	public double Longitude { get; set; }
	public DateTime LastHeartbeat { get; set; }
	public string? OriginServerUri { get; set; }
	public string? HostToken { get; set; }
	public int HostPingBaseline { get; set; }
	public string? LocalIP { get; set; }
	public int? LocalPort { get; set; }
	public long MapSizeBytes { get; set; }
	public bool IsGameInProgress { get; set; }

	/// <summary>How reachable the lobby's game port was shown to be by the node that measured it: open, punch-only or unverified.</summary>
	public string? ReachabilityClass { get; set; }

	/// <summary>Whether the host's router reused one external port for every destination or made one per destination.</summary>
	public string? MappingBehaviour { get; set; }

	/// <summary>The external port the measuring node saw the game port answer from, which is not the port a player is sent to.</summary>
	public int ObservedPort { get; set; }

	/// <summary>
	///     Every endpoint this lobby is reachable at, one per address family it was published in, each carrying the result
	///     of the check this node made of it. A lobby registered by a client that states no endpoint list has the single
	///     endpoint this node measured in its place, so a listing always has the same shape.
	/// </summary>
	public List<LobbyEndpointInfo> Endpoints { get; set; } = new();

	/// <summary>
	///     How many times this lobby has been handed to a new host, starting at one for the screen that registered it. Every
	///     host message states the epoch it was written under, so a host the lobby has already left cannot publish, admit a
	///     player or close a lobby somebody else now owns.
	/// </summary>
	public int OwnershipEpoch { get; set; } = 1;

	/// <summary>Copies this lobby so one field can be replaced atomically, without a reader seeing half a handover.</summary>
	/// <returns>A copy that shares nothing mutable with this lobby.</returns>
	public LobbyInfo Clone() => new()
	{
		LobbyId = LobbyId,
		Map = Map,
		HostIP = HostIP,
		HostPort = HostPort,
		NatType = NatType,
		PasswordHash = PasswordHash,
		MaxPlayers = MaxPlayers,
		SlotsUsed = SlotsUsed,
		GameVersion = GameVersion,
		MapVersion = MapVersion,
		IsGreenlit = IsGreenlit,
		MapCreator = MapCreator,
		Latitude = Latitude,
		Longitude = Longitude,
		LastHeartbeat = LastHeartbeat,
		OriginServerUri = OriginServerUri,
		HostToken = HostToken,
		HostPingBaseline = HostPingBaseline,
		LocalIP = LocalIP,
		LocalPort = LocalPort,
		MapSizeBytes = MapSizeBytes,
		IsGameInProgress = IsGameInProgress,
		ReachabilityClass = ReachabilityClass,
		MappingBehaviour = MappingBehaviour,
		ObservedPort = ObservedPort,
		Endpoints = new List<LobbyEndpointInfo>(Endpoints),
		OwnershipEpoch = OwnershipEpoch
	};
}