namespace Realm.AdminServer.Models;

public record JoinResponseDto(
	string HostIP,
	int HostPort,
	string? LocalIP = null
);