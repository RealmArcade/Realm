namespace Realm.AdminServer.Models;

public record SeederDownloadRequest(
	string MapId,
	string ClientPublicIP,
	int ClientPublicPort
);