namespace Realm.AdminServer.Models;

public record JoinResponseDto(
	string HostIP,
	int HostPort,
	string? LocalIP = null,
	int? LocalPort = null,
	int? PunchPort = null,
	string? PunchToken = null
);