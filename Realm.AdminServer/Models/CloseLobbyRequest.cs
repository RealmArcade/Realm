namespace Realm.AdminServer.Models;

public record CloseLobbyRequest(
	string LobbyId,
	string HostToken
);