using NUnit.Framework;
using Realm.AdminServer.Models;
using Realm.AdminServer.Services;
using Realm.Shared.Network.Relay;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     What the node's volunteer pool admits, offers and forgets. Each rule fails silently when it changes: a volunteer
///     admitted on its own word carries nobody, one that stays pooled after it stopped keeps being handed to lobbies that
///     can then not be joined, and an offer order that moves between two reads gives the two peers of one connection
///     different paths. The expiry of a silent volunteer is not driven here because it is measured in the node's own clock.
/// </summary>
[TestFixture]
public class RelayPoolMembershipTests
{
	private const string PublicAddress = "203.0.113.9";

	[Test]
	public void AVolunteerIsPooledOnlyWhenItsEndpointWasMeasuredOpen()
	{
		var pool = new RelayPool();

		Assert.That(pool.Offer(Offer("carrier", 5000), PublicAddress, ReachabilityClasses.Open).Advertised, Is.True);

		(bool punchOnly, string punchReason) = pool.Offer(Offer("behind-a-punch", 5001), PublicAddress, ReachabilityClasses.PunchOnly);
		Assert.That(punchOnly, Is.False, "a relay has to answer a stranger without a punch");
		Assert.That(punchReason, Does.Contain("punch"));

		Assert.That(pool.Offer(Offer("nothing-reached", 5002), PublicAddress, ReachabilityClasses.Unverified).Advertised, Is.False);
		Assert.That(pool.Offer(Offer("not-measured", 5003), PublicAddress, string.Empty).Advertised, Is.False, "a volunteer is never pooled on a class it stated itself");
		Assert.That(pool.Offer(Offer("at-home", 5004, reportedAddress: "192.168.1.20"), "192.168.1.20", ReachabilityClasses.Open).Advertised, Is.False, "an endpoint a player elsewhere cannot be sent to is not pooled");

		Assert.That(pool.AdvertisedCount(), Is.EqualTo(1));
		Assert.That(pool.Select(RelayOffers.MaximumOffers)[0].RelayId, Is.EqualTo("carrier"));
	}

	[Test]
	public void AnOfferThatCannotCarryAnythingIsRefusedWithAReason()
	{
		var pool = new RelayPool();
		(string Name, RelayRegisterRequest Request)[] unusable =
		[
			("no identifier", Offer(string.Empty, 5000)),
			("no port", Offer("no-port", 0)),
			("no room", Offer("no-room", 5000, maximumConnections: 0)),
			("no budget", Offer("no-budget", 5000, budgetBytesPerSecond: 0)),
			("payload below the envelope", Offer("tiny", 5000, maximumPayloadBytes: RelayEnvelope.HeaderBytes - 1))
		];

		foreach ((string name, RelayRegisterRequest request) in unusable)
		{
			(bool advertised, string reason) = pool.Offer(request, PublicAddress, ReachabilityClasses.Open);
			Assert.That(advertised, Is.False, $"a volunteer with {name} is refused");
			Assert.That(reason, Is.Not.Empty, $"the refusal for {name} states a reason");
		}

		Assert.That(pool.AdvertisedCount(), Is.Zero);
	}

	[Test]
	public void TheOfferIsBoundedStableAndOrderedByRoom()
	{
		var pool = new RelayPool();
		pool.Offer(Offer("room-2", 6002, maximumConnections: 2), PublicAddress, ReachabilityClasses.Open);
		pool.Offer(Offer("room-4", 6004, maximumConnections: 4), PublicAddress, ReachabilityClasses.Open);
		pool.Offer(Offer("room-4-early", 6005, maximumConnections: 4), PublicAddress, ReachabilityClasses.Open);
		pool.Offer(Offer("room-4-late", 6006, maximumConnections: 4), PublicAddress, ReachabilityClasses.Open);

		List<RelayCandidateMessage> first = pool.Select(RelayOffers.MaximumOffers);
		List<RelayCandidateMessage> second = pool.Select(RelayOffers.MaximumOffers);

		Assert.That(Ids(first), Is.EqualTo(Ids(second)), "two reads describe the same paths in the same order");
		Assert.That(Ids(first), Is.EqualTo("room-4,room-4-early,room-4-late"));
		Assert.That(first, Has.Count.EqualTo(RelayOffers.MaximumOffers), "the offer is bounded by the paths one connection may open");
		Assert.That(pool.Select(1)[0].RelayId, Is.EqualTo("room-4"));
		Assert.That(pool.Select(0), Is.Empty);
	}

	[Test]
	public void AFullVolunteerIsOfferedAgainOnceItHasRoom()
	{
		var pool = new RelayPool();
		pool.Offer(Offer("only", 7000, maximumConnections: 2), PublicAddress, ReachabilityClasses.Open);

		Assert.That(pool.Touch(new RelayHeartbeatRequest("only", 2)), Is.True);
		Assert.That(pool.Select(RelayOffers.MaximumOffers), Is.Empty, "a volunteer carrying as much as it stated is not offered");

		Assert.That(pool.Touch(new RelayHeartbeatRequest("only", 1)), Is.True);
		Assert.That(pool.Select(RelayOffers.MaximumOffers), Has.Count.EqualTo(1));

		Assert.That(pool.Touch(new RelayHeartbeatRequest("never-registered", 0)), Is.False);
	}

	[Test]
	public void AVolunteerThatStopsIsOfferedByNobodyAtOnce()
	{
		var pool = new RelayPool();
		pool.Offer(Offer("stops", 8000), PublicAddress, ReachabilityClasses.Open);
		pool.Offer(Offer("stays", 8001), PublicAddress, ReachabilityClasses.Open);

		pool.Remove("stops");

		List<RelayCandidateMessage> offered = pool.Select(RelayOffers.MaximumOffers);
		Assert.That(Ids(offered), Is.EqualTo("stays"));
		Assert.That(pool.Touch(new RelayHeartbeatRequest("stops", 0)), Is.False);
	}

	private static string Ids(List<RelayCandidateMessage> candidates)
	{
		var ids = new List<string>(candidates.Count);
		foreach (RelayCandidateMessage candidate in candidates) ids.Add(candidate.RelayId);
		return string.Join(',', ids);
	}

	private static RelayRegisterRequest Offer(string relayId, int port, string? reportedAddress = PublicAddress, int maximumConnections = 4, int budgetBytesPerSecond = 65536, int maximumPayloadBytes = RelayEnvelope.MaximumEncapsulatedDatagramBytes)
	{
		return new RelayRegisterRequest(relayId, port, reportedAddress, maximumConnections, budgetBytesPerSecond, maximumPayloadBytes, RelayCarrierKind.UdpTunnel.ToString(), ReachabilityClasses.Open);
	}
}
