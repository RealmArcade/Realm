using NUnit.Framework;
using Realm.Shared.Network;
using Realm.Shared.Network.Relay;

namespace Realm.Network.Tests;

/// <summary>
///     A relayed connection crosses three seams that fail silently when they change: the envelope a volunteer carries
///     without reading, the offer list both peers open the same paths from, and the answer a lobby server gives a machine
///     that offered its bandwidth. Each of them is stated here because a change that broke one would still compile, still
///     pass every other suite, and only show up as a lobby nobody can join or a volunteer that stops offering for a reason
///     nobody can read.
/// </summary>
[TestFixture]
public class RelayWireContractTests
{
	private const string Ticket = "relay-wire-contract-ticket-0001";

	[Test]
	public void TheEnvelopeStatesWhatAVolunteerMayReadInAFixedLayout()
	{
		Assert.That(RelayEnvelope.HeaderBytes, Is.EqualTo(31));
		Assert.That(RelayEnvelope.PathIdOffset, Is.EqualTo(RelayConnectionToken.TextCharacters / 2));
		Assert.That(RelayEnvelope.MessageSequenceOffset, Is.EqualTo(RelayEnvelope.DeliveryOffset + 1));
		Assert.That(RelayEnvelope.TagOffset, Is.EqualTo(RelayEnvelope.PathSequenceOffset + 2));
		Assert.That(RelayEnvelope.HeaderBytes, Is.EqualTo(RelayEnvelope.TagOffset + RelayDatagramCipher.TagBytes));
		Assert.That(RelayEnvelope.MaximumInnerDatagramBytes, Is.GreaterThanOrEqualTo(SnapshotFragment.MaximumDatagramBytes));
		Assert.That(RelayForwarder.MaximumDatagramBytes, Is.EqualTo(RelayEnvelope.MaximumEncapsulatedDatagramBytes));
		Assert.That(RelayEnvelope.MaximumEncapsulatedDatagramBytes + 28, Is.LessThanOrEqualTo(1472), "one relayed datagram plus IP and UDP has to stay inside a 1500-byte path");
	}

	[Test]
	public void ACopyIsDeliveredOnceHoweverManyPathsCarriedIt()
	{
		var window = new RelayDuplicateWindow();
		Assert.That(window.TryAccept(10), Is.True);
		Assert.That(window.TryAccept(10), Is.False);
		Assert.That(window.TryAccept(12), Is.True);
		Assert.That(window.TryAccept(11), Is.True, "a copy that overtook another on a faster path is still new");
		Assert.That(window.TryAccept(11), Is.False);

		var wrapped = new RelayDuplicateWindow();
		Assert.That(wrapped.TryAccept(RelayEnvelope.MessageSequenceMask), Is.True);
		Assert.That(wrapped.TryAccept(0), Is.True);
		Assert.That(wrapped.TryAccept(RelayEnvelope.MessageSequenceMask), Is.False);

		var stale = new RelayDuplicateWindow();
		stale.TryAccept(1000);
		Assert.That(stale.TryAccept(1000 - RelayDuplicateWindow.Capacity), Is.False);
		Assert.That(stale.StaleCount, Is.GreaterThan(0));
	}

	[Test]
	public void APathThatMeasuredSomethingIsPreferredOverOneThatHasNot()
	{
		var measured = new RelayPathStatistics { RoundTripMilliseconds = 40, ProbesSent = 10, ProbesAnswered = 10 };
		var unmeasured = new RelayPathStatistics();

		Assert.That(RelayPathScoring.IsBetterThan(measured, unmeasured), Is.True);
		Assert.That(RelayPathScoring.IsBetterThan(unmeasured, measured), Is.False, "a path that answered nothing scores zero and must not keep a connection from one that answered");
	}

	[Test]
	public void BothPeersOpenTheSamePathsFromOneOfferList()
	{
		var candidates = new List<RelayCandidateMessage>
		{
			Candidate("first", 5001),
			Candidate("no-port", 0),
			Candidate("second", 5002),
			Candidate("third", 5003),
			Candidate("fourth", 5004)
		};

		List<RelayPathOffer> offers = RelayOffers.Build(candidates);

		Assert.That(offers, Has.Count.EqualTo(RelayOffers.MaximumOffers));
		Assert.That(offers[0].Candidate.RelayId, Is.EqualTo("first"));
		Assert.That(offers[1].Candidate.RelayId, Is.EqualTo("second"));
		Assert.That(offers[2].Candidate.RelayId, Is.EqualTo("third"));
		Assert.That(offers.Select(offer => offer.PathId), Is.EqualTo(new byte[] { 1, 2, 3 }), "the order the node stated is the order both peers open");
		Assert.That(RelayOffers.Build(null), Is.Empty);
	}

	[Test]
	public void ThePlanAHostStatesToItsAuthorityReadsBack()
	{
		RelayConnectionPlan plan = RelayConnectionPlan.From(Ticket, [Candidate("first", 5001), Candidate("second", 5002)], duplicationEnabled: true, duplicationBudgetBytesPerSecond: 131072);

		RelayConnectionPlan? read = RelayConnectionPlan.TryParse(plan.Serialize());

		Assert.That(read, Is.Not.Null);
		Assert.That(read!.JoinTicket, Is.EqualTo(Ticket));
		Assert.That(read.Offers.Select(offer => offer.PathId), Is.EqualTo(plan.Offers.Select(offer => offer.PathId)));
		Assert.That(read.Offers[1].Candidate.Port, Is.EqualTo(5002));
		Assert.That(read.DuplicationEnabled, Is.True);
		Assert.That(RelayConnectionPlan.TryParse("not a plan"), Is.Null);
	}

	[Test]
	public void AJoinWithoutAVolunteerCarriesNothing()
	{
		Assert.That(RelayConnectionFactory.TryBuild(null, isAuthority: false, out string noPlan), Is.Null);
		Assert.That(noPlan, Is.Not.Empty);

		RelayConnectionPlan ticketless = RelayConnectionPlan.From("short", [Candidate("reachable", 5001)], duplicationEnabled: false, duplicationBudgetBytesPerSecond: 0);
		Assert.That(ticketless.IsUsable, Is.False);
		Assert.That(RelayConnectionFactory.TryBuild(ticketless, isAuthority: true, out string ticketFailure), Is.Null);
		Assert.That(ticketFailure, Does.Contain("ticket"), "the refusal has to name the ticket rather than blame the volunteer list");
	}

	[Test]
	public void TheVolunteerOfferReadsAPooledAnswerWhicheverCasingTheNodeWrites()
	{
		Assert.That(RelayRegistrationAnswer.TryParse("{\"status\":\"Pooled\",\"advertised\":true,\"reason\":null}", out RelayRegistrationAnswer camelCase), Is.True);
		Assert.That(camelCase.Advertised, Is.True, "the node serializes with its web host's camelCase defaults");

		Assert.That(RelayRegistrationAnswer.TryParse("{\"Advertised\":true,\"Reason\":\"\"}", out RelayRegistrationAnswer stated), Is.True);
		Assert.That(stated.Advertised, Is.True);

		Assert.That(RelayRegistrationAnswer.TryParse("{\"advertised\":false,\"reason\":\"Nothing was pooled.\"}", out RelayRegistrationAnswer refused), Is.True);
		Assert.That(refused.Advertised, Is.False);
		Assert.That(refused.Reason, Is.EqualTo("Nothing was pooled."));

		Assert.That(RelayRegistrationAnswer.TryParse("not json", out RelayRegistrationAnswer unreadable), Is.False);
		Assert.That(unreadable.Advertised, Is.False);
		Assert.That(unreadable.Reason, Is.Not.Empty);
	}

	[Test]
	public void TheTransportFacesOneLoopbackPortPerConnectionAndItNeverMoves()
	{
		RelayConnectionPlan plan = RelayConnectionPlan.From(Ticket, [Candidate("first", 5001), Candidate("second", 5002)], duplicationEnabled: false, duplicationBudgetBytesPerSecond: 0);
		RelayPathMux? firstMux = RelayConnectionFactory.TryBuild(plan, isAuthority: true, out string firstFailure);
		RelayPathMux? secondMux = RelayConnectionFactory.TryBuild(plan, isAuthority: true, out string secondFailure);
		Assert.That(firstMux, Is.Not.Null, firstFailure);
		Assert.That(secondMux, Is.Not.Null, secondFailure);
		if (firstMux == null || secondMux == null) return;

		using var first = new RelayUdpShim(firstMux, 0);
		using var second = new RelayUdpShim(secondMux, 0);
		Assert.That(first.TryOpen(out string firstOpenFailure), Is.True, firstOpenFailure);
		Assert.That(second.TryOpen(out string secondOpenFailure), Is.True, secondOpenFailure);

		int firstPort = first.Port;
		Assert.That(firstPort, Is.GreaterThan(0));
		Assert.That(second.Port, Is.Not.EqualTo(firstPort), "one connection is one peer to the transport, so two connections cannot share the port the transport sees");

		firstMux.RemoveAllPaths();
		Assert.That(firstMux.PathCount, Is.EqualTo(0));
		Assert.That(first.TryOpen(out _), Is.True);
		Assert.That(first.Port, Is.EqualTo(firstPort), "a volunteer going away changes which carrier is used, never what the transport connected to");
	}

	private static RelayCandidateMessage Candidate(string relayId, int port)
	{
		return new RelayCandidateMessage
		{
			RelayId = relayId,
			Address = "203.0.113.9",
			Port = port,
			Carrier = RelayCarrierKind.UdpTunnel.ToString(),
			MaximumPayloadBytes = RelayEnvelope.MaximumEncapsulatedDatagramBytes,
			MaximumConnections = 4,
			ActiveConnections = 0,
			BudgetBytesPerSecond = 65536,
			ReachabilityClass = "Open"
		};
	}
}
