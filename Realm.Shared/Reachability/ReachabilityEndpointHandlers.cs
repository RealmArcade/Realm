using System.Collections.Generic;
using System.Net;

namespace Realm.Shared.Reachability;

/// <summary>What a route hands back: a status code and a body a registry node serializes.</summary>
public readonly record struct ReachabilityEndpointReply(int StatusCode, object? Body);

/// <summary>
///     The work behind a registry node's two reachability routes, kept apart from the web framework so the same decisions are
///     made by the node that serves them and by anything that exercises them.
/// </summary>
public static class ReachabilityEndpointHandlers
{
	/// <summary>
	///     What this node can check. It starts the probe sockets, because the answer is exactly which families it holds a
	///     socket in and starting them here is what makes the answer true rather than intended.
	/// </summary>
	/// <param name="probe">The node's probe service.</param>
	/// <param name="allowsLocalCallers">Whether this node also checks a host that reaches it from inside its own network.</param>
	/// <returns>The capabilities reply.</returns>
	public static ReachabilityEndpointReply Capabilities(UdpReachabilityProbeService probe, bool allowsLocalCallers = false)
	{
		if (!probe.TryStart(out string failure)) return new ReachabilityEndpointReply(503, new { Message = failure });

		var families = new List<string>(2);
		IReadOnlyList<LobbyAddressFamily> available = probe.AvailableFamilies;
		for (int index = 0; index < available.Count; index++) families.Add(LobbyAddresses.FamilyName(available[index]));

		return new ReachabilityEndpointReply(200, new RegistryCapabilities(RegistryCapabilities.EndpointListProtocolVersion, families)
		{
			AllowsLocalCallers = allowsLocalCallers,
			RelaysJoinPunch = true,
			ReachabilityClasses = ReachabilityClasses.All
		});
	}

	/// <summary>
	///     Starts a check of the game port a host is about to advertise, unless the caller reaches this node from inside its
	///     own network, in which case no check is started at all.
	/// </summary>
	/// <param name="probe">The node's probe service.</param>
	/// <param name="request">What the host asked to have checked.</param>
	/// <param name="requestSourceAddress">The address the request arrived from, as the node sees it.</param>
	/// <param name="requestSourcePort">The port the request arrived from.</param>
	/// <param name="allowSameNetworkCaller">
	///     Whether a caller on this node's own network may be judged at all. It is false for every node that faces the
	///     public internet, because a check run from inside the caller's own network can only ever prove that the lobby is
	///     reachable from there; it is only ever true for a harness that has no second network to probe from.
	/// </param>
	/// <returns>The ticket the host rehearses its join with, or the refusal that says why no check was started.</returns>
	public static ReachabilityEndpointReply Begin(UdpReachabilityProbeService probe, ReachabilityBeginRequest? request, string requestSourceAddress, int requestSourcePort, bool allowSameNetworkCaller = false)
	{
		if (request == null) return new ReachabilityEndpointReply(400, new { Message = "A reachability check needs the endpoint a lobby believes other players will use." });
		if (request.AdvertisedPort <= 0 || request.AdvertisedPort > 65535) return new ReachabilityEndpointReply(400, new { Message = "A reachability check needs the UDP port the lobby listens on." });

		if (!allowSameNetworkCaller && !ReachabilityAddresses.IsPubliclyRoutable(requestSourceAddress))
		{
			return new ReachabilityEndpointReply(ReachabilityStatusCodes.CallerOnOwnNetwork, new
			{
				Message = $"This node is reached from '{requestSourceAddress}', which is inside its own network, so anything it measured would only describe that network. Publish through a lobby server on the public internet."
			});
		}

		IPAddress sourceAddress = IPAddress.TryParse(requestSourceAddress ?? string.Empty, out IPAddress? parsedSource) ? parsedSource : IPAddress.Loopback;
		IPAddress advertisedAddress = IPAddress.TryParse(request.AdvertisedIp ?? string.Empty, out IPAddress? parsedAdvertised) ? parsedAdvertised : IPAddress.Any;

		try
		{
			ReachabilityTicket ticket = probe.Begin(advertisedAddress, request.AdvertisedPort, sourceAddress, requestSourcePort, request.WindowMilliseconds);
			return new ReachabilityEndpointReply(200, ticket);
		}
		catch (InvalidOperationException exception)
		{
			return new ReachabilityEndpointReply(503, new { Message = exception.Message });
		}
	}

	public static ReachabilityEndpointReply Read(UdpReachabilityProbeService probe, string probeId)
	{
		ReachabilityReport? report = probe.Read(probeId);
		if (report == null) return new ReachabilityEndpointReply(404, new { Message = "No reachability check by that name is running." });
		return new ReachabilityEndpointReply(200, report);
	}

	/// <summary>
	///     Whether a lobby may be published: only a check this node ran, and whose game port answered it, counts. The report it
	///     returns carries the endpoint the node measured, which is what peers must use.
	/// </summary>
	public static bool IsLobbyVerified(UdpReachabilityProbeService probe, string? probeId, out ReachabilityReport report)
	{
		return probe.TryReadVerified(probeId ?? string.Empty, out report);
	}

	/// <summary>
	///     The refusal a lobby receives when it arrives without a check this node answered.
	/// </summary>
	/// <returns>The refusal body.</returns>
	public static object NotVerifiedRefusal() => new
	{
		Code = ReachabilityErrorCodes.NotVerified,
		Message = "Lobby creation rejected: this node could not reach the lobby's game port, so the lobby was not registered."
	};

	/// <summary>
	///     The refusal a lobby receives when the only endpoints it offers are IPv4 ones behind a router that makes one
	///     outside port per destination. A check can open a way for the one destination it was aimed at, so such a
	///     lobby would be published as reachable by a measurement that says nothing about any other player.
	/// </summary>
	/// <returns>The refusal body.</returns>
	public static object MappingPerDestinationRefusal() => new
	{
		Code = ReachabilityErrorCodes.NotVerified,
		Message = "Lobby creation rejected: this router gives every destination a different outside port, so a check can only ever open the way for itself. Forward the game port, or publish over an address family no router translates."
	};

	/// <summary>
	///     The refusal a lobby receives when it offers only endpoints this node has no probe socket for, which is a node
	///     that cannot measure what the lobby is reachable at rather than a lobby that cannot be reached.
	/// </summary>
	/// <param name="families">The families the lobby offered, as the lobby protocol names them.</param>
	/// <returns>The refusal body.</returns>
	public static object FamilyNotCheckedRefusal(string families) => new
	{
		Code = ReachabilityRefusalCodes.FamilyNotChecked,
		Message = $"Lobby creation rejected: this node has no probe socket to check {families} endpoints from, so the lobby was not registered."
	};
}
