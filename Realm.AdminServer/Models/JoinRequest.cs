namespace Realm.AdminServer.Models;

public record JoinRequest(
	string LobbyId,
	string ClientPublicIP,
	int ClientPublicPort,
	IReadOnlyList<string>? ClientEndpointFamilies = null
);