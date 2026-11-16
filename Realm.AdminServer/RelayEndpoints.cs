using System.Text.Json;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared.Network.Relay;

namespace Realm.AdminServer;

/// <summary>Volunteer relay registration, heartbeats and the candidate lists hosts and joining players read.</summary>
public static class RelayEndpoints
{
	public static void MapRelayEndpoints(WebApplication app, RelayPool pool)
	{
		app.MapPost("/relays/register", (RelayRegisterRequest req, HttpContext context) =>
		{
			string sourceAddress = (context.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback).ToString();

			// A volunteer is pooled only when the class the requester states is the one hosting is measured with. Nothing here
			// promotes a claim: the reading is taken as stated and only the open class is admitted, so a machine behind a
			// router that drops strangers is told why it cannot relay instead of registering and failing everyone it carries.
			(bool advertised, string reason) = pool.Offer(req, sourceAddress, req.ReachabilityClass ?? string.Empty);

			if (!advertised) Console.WriteLine($"[Relay] Volunteer {req.RelayId} was not pooled: {reason}");
			else Console.WriteLine($"[Relay] Pooled volunteer {req.RelayId} at {(string.IsNullOrWhiteSpace(req.ReportedIP) ? sourceAddress : req.ReportedIP)}:{req.Port} carrying up to {req.MaximumConnections} connections within {req.BudgetBytesPerSecond}B/s.");

			return Results.Ok(new { Status = advertised ? "Pooled" : "Refused", Advertised = advertised, Reason = reason });
		});

		app.MapPost("/relays/heartbeat", (RelayHeartbeatRequest req, RelayPool pool) =>
		{
			bool known = pool.Touch(req);
			return known ? Results.Ok(new { Status = "Recorded" }) : Results.Ok(new { Status = "Unknown" });
		});

		app.MapPost("/relays/unregister", (RelayUnregisterRequest req, RelayPool pool) =>
		{
			pool.Remove(req.RelayId);
			return Results.Ok(new { Status = "Unregistered" });
		});

		// A host reads the pool before it publishes a lobby, so its own screen can state whether offering relays is worth it.
		app.MapGet("/relays/candidates", (RelayPool pool) =>
		{
			RelayCandidatesAnswer answer = new("Offered", true, null, pool.Select(RelayOffers.MaximumOffers));
			return Results.Ok(answer);
		});
	}

	/// <summary>
	///     States the relay candidates one joining player is offered, in the order both that player and the lobby's host
	///     open them. A node that pools no volunteers answers with an empty list, which every caller reads as the
	///     direct-only behaviour it had before relays existed.
	/// </summary>
	/// <param name="pool">The node's volunteer pool.</param>
	/// <returns>The candidates, already ordered for both peers.</returns>
	public static IReadOnlyList<RelayCandidateMessage> CandidatesForJoin(RelayPool pool) => pool.Select(RelayOffers.MaximumOffers);

	/// <summary>Encodes the candidates a host is told about over its signalling channel.</summary>
	/// <param name="token">The join ticket the connection is named by.</param>
	/// <param name="candidates">The candidates both peers open.</param>
	/// <returns>The message the host's channel carries.</returns>
	public static string HostMessage(string token, IReadOnlyList<RelayCandidateMessage> candidates)
	{
		return JsonSerializer.Serialize(new { Action = "Relays", Token = token, Relays = candidates });
	}
}
