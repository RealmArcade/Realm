namespace Realm.AdminServer.Models;

public record HeartbeatRequest(
	string LobbyId,
	int SlotsUsed,
	bool? IsGameInProgress = null,
	string? ReachabilityClass = null,
	string? HostToken = null,
	int? OwnershipEpoch = null
);