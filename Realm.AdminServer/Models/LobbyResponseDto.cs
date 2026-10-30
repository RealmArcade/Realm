namespace Realm.AdminServer.Models;

public record LobbyResponseDto(
	string LobbyId,
	string Map,
	string HostIP,
	int HostPort,
	string NatType,
	int SlotsUsed,
	int MaxPlayers,
	double Latitude,
	double Longitude,
	double DistanceKm,
	int EstimatedPingMs,
	string? OriginServerUri,
	int HostPingBaseline,
	string? GameVersion = null,
	string? LocalIP = null,
	string? MapVersion = null,
	long MapSizeBytes = 0,
	bool IsGreenlit = false,
	string? MapCreator = null
);