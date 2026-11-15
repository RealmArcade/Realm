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
	public long MapSizeBytes { get; set; }
	public bool IsGameInProgress { get; set; }

	/// <summary>
	///     Every endpoint this lobby is reachable at, one per address family it was published in, each carrying the result
	///     of the check this node made of it. A lobby registered by a client that states no endpoint list has the single
	///     endpoint this node measured in its place, so a listing always has the same shape.
	/// </summary>
	public List<LobbyEndpointInfo> Endpoints { get; set; } = new();
}