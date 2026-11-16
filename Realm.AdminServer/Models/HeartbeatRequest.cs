namespace Realm.AdminServer.Models;

public record HeartbeatRequest(
	string LobbyId,
	string HostToken,
	int SlotsUsed,
	bool? IsGameInProgress = null,
	string? ReachabilityClass = null,
	bool? Listed = null
);