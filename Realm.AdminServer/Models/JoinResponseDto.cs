using Realm.Shared.Reachability;

namespace Realm.AdminServer.Models;

public record JoinResponseDto(
	string HostIP,
	int HostPort,
	string? LocalIP = null,
	int? LocalPort = null,
	int? PunchPort = null,
	string? PunchToken = null,
	string? ReachabilityClass = null,
	IReadOnlyList<LobbyEndpointInfo>? Endpoints = null,
	RegistryCapabilities? Capabilities = null
);