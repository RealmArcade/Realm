namespace Realm.AdminServer.Models;

/// <summary>
///     Asks this node to tell a lobby's host to send toward one joining player, which requires no credentials because the
///     address is one this node already saw that player arrive from.
/// </summary>
/// <param name="LobbyId">The lobby being joined.</param>
/// <param name="ClientIP">The address the joining player was seen at.</param>
/// <param name="ClientPort">The port the joining player was seen at.</param>
public record PunchRequest(
	string LobbyId,
	string ClientIP,
	int ClientPort
);
