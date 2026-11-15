using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using NUnit.Framework;
using Realm.AdminServer.Services;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     What a check's answer becomes as a class a listing states, and the bytes the punching half of a join agrees on. Both
///     are contracts a refactor can break without any compiler complaint and without any test failing, and both break
///     quietly in the same way: the lobby is either listed as reachable when nobody can join it, or every join reports a
///     host that never sent toward it.
/// </summary>
[TestFixture]
public class ColdProbeClassificationTests
{
	private static ReachabilityReport Report(bool answered = true, bool coldAnswered = false, int observedPort = 8999, int advertisedPort = 8999) =>
		new("probe", answered ? ReachabilityStates.Answered : ReachabilityStates.Silent, answered, "203.0.113.2", observedPort, "203.0.113.2", advertisedPort, "203.0.113.1", 1, "ipv4", coldAnswered, 1);

	[Test]
	public void NothingMeasuredIsUnverified()
	{
		Assert.That(ReachabilityClasses.FromReport(null), Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(ReachabilityClasses.FromReport(Report(answered: false), MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.Unverified));
	}

	[Test]
	public void APortThatAnsweredAnUnpunchedSocketIsOpen()
	{
		Assert.That(ReachabilityClasses.FromReport(Report(coldAnswered: true), MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.Open));
		Assert.That(ReachabilityClasses.FromReport(Report(coldAnswered: true), MappingBehaviours.Unknown), Is.EqualTo(ReachabilityClasses.Open));
	}

	[Test]
	public void APortThatAnsweredOnlyAPunchedSocketIsPunchOnlyWhateverItsOutsidePortIs()
	{
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 8999), MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.PunchOnly), "a router that keeps the port");
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 10000), MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.PunchOnly), "a router that translates it and keeps one mapping for everybody");
	}

	[Test]
	public void ATranslatedOutsidePortMeasuredAtTwoDestinationsIsPunchOnly()
	{
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 10000, advertisedPort: 8999), MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.PunchOnly), "one mapping for every destination is the ordinary home-router case a punch exists for; the rig admits a guest at this shape when the router's translation is present, and the join failure once recorded here came from a scenario that deleted the translation itself");
	}

	[Test]
	public void ARouterThatMakesAPortPerDestinationIsUnverifiedEvenWhenTheCheckAnswered()
	{
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 10000), MappingBehaviours.PerDestination), Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(ReachabilityClasses.FromReport(Report(coldAnswered: true, observedPort: 10000), MappingBehaviours.PerDestination), Is.EqualTo(ReachabilityClasses.Unverified), "a per-destination port open to one destination is not open to a player");
	}

	[Test]
	public void ATranslatedPortNobodyMeasuredIsNotListed()
	{
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 10000), MappingBehaviours.Unknown), Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(ReachabilityClasses.FromReport(Report(observedPort: 10000, advertisedPort: 10000), MappingBehaviours.Unknown), Is.EqualTo(ReachabilityClasses.PunchOnly), "the outside port is the one the lobby listens on, so a player dials the port the host holds");
	}

	[Test]
	public void OnlyOpenAndPunchOnlyArePublishable()
	{
		Assert.That(ReachabilityClasses.IsPublishable(ReachabilityClasses.Open), Is.True);
		Assert.That(ReachabilityClasses.IsPublishable(ReachabilityClasses.PunchOnly), Is.True);
		Assert.That(ReachabilityClasses.IsPublishable(ReachabilityClasses.Unverified), Is.False);
		Assert.That(ReachabilityClasses.IsPublishable("Simulated"), Is.False);
		Assert.That(ReachabilityClasses.IsPublishable(string.Empty), Is.False);
		Assert.That(ReachabilityClasses.IsPublishable(null), Is.False);
	}

	[Test]
	public void AClassOnlyEverMovesDown()
	{
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.PunchOnly, ReachabilityClasses.Open), Is.True);
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.Unverified, ReachabilityClasses.PunchOnly), Is.True);
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.Unverified, ReachabilityClasses.Open), Is.True);

		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.Open, ReachabilityClasses.PunchOnly), Is.False, "a later check never promotes a lobby");
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.PunchOnly, ReachabilityClasses.Unverified), Is.False, "a weaker claim is never applied to a listing that already states it");
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.Open, ReachabilityClasses.Open), Is.False, "the same class is not a change");
		Assert.That(ReachabilityClasses.IsWeaker(ReachabilityClasses.PunchOnly, ReachabilityClasses.PunchOnly), Is.False);
		Assert.That(ReachabilityClasses.IsWeaker("Simulated", ReachabilityClasses.Open), Is.False, "a class this build does not know never moves a listing");
		Assert.That(ReachabilityClasses.IsWeaker(string.Empty, ReachabilityClasses.Open), Is.False);
	}

	[Test]
	public void AClassAJoinCannotReachIsTheEndOfTheLine()
	{
		Assert.That(ReachabilityClasses.Downgrade(ReachabilityClasses.Open), Is.EqualTo(ReachabilityClasses.PunchOnly));
		Assert.That(ReachabilityClasses.Downgrade(ReachabilityClasses.PunchOnly), Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(ReachabilityClasses.Downgrade(ReachabilityClasses.Unverified), Is.EqualTo(ReachabilityClasses.Unverified));
		Assert.That(ReachabilityClasses.Downgrade(null), Is.EqualTo(ReachabilityClasses.Unverified));
	}

	[Test]
	public void TheClaimARecordMakesIsRankedInOneOrder()
	{
		Assert.That(ReachabilityClasses.Rank(ReachabilityClasses.Open), Is.LessThan(ReachabilityClasses.Rank(ReachabilityClasses.PunchOnly)));
		Assert.That(ReachabilityClasses.Rank(ReachabilityClasses.PunchOnly), Is.LessThan(ReachabilityClasses.Rank(ReachabilityClasses.Unverified)));
		Assert.That(ReachabilityClasses.Rank(null), Is.EqualTo(ReachabilityClasses.Rank(ReachabilityClasses.Unverified)), "an unstated class claims nothing, like an unverified one");
	}
}

/// <summary>
///     The bytes both halves of a punch agree on: one datagram shape for the packet that opens a router, one for the report
///     that tells a node where a joining screen is, and a token that names one join and no other.
/// </summary>
[TestFixture]
public class RelayFramingTests
{
	[Test]
	public void APunchNamedForACheckRoundTripsThroughItsBytes()
	{
		Span<byte> buffer = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes];
		const string probeId = "6f1b3c2d4e5f60718293a4b5c6d7e8f9";

		int written = ReachabilityPunch.TryWrite(buffer, probeId);

		Assert.That(written, Is.EqualTo(6 + probeId.Length));
		Assert.That(ReachabilityPunch.TryReadProbeId(buffer[..written], out string read), Is.True);
		Assert.That(read, Is.EqualTo(probeId));
	}

	[Test]
	public void AnUnnamedPunchIsNotReadAsANamedOne()
	{
		Assert.That(ReachabilityPunch.TryReadProbeId(ReachabilityPunch.Datagram, out string read), Is.False);
		Assert.That(read, Is.Empty);
	}

	[Test]
	public void AJoinerReportRoundTripsThroughItsBytes()
	{
		Span<byte> buffer = stackalloc byte[JoinerPunch.MaximumDatagramBytes];
		const string token = "aaaabbbbccccddddeeeeffff00001111";

		int written = JoinerPunch.TryWrite(buffer, token);

		Assert.That(written, Is.EqualTo(5 + token.Length));
		Assert.That(JoinerPunch.TryReadToken(buffer[..written], out string read), Is.True);
		Assert.That(read, Is.EqualTo(token));
	}

	[Test]
	public void APunchIsNeverReadAsAJoinerReport()
	{
		Span<byte> buffer = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes];
		int written = ReachabilityPunch.TryWrite(buffer, "6f1b3c2d4e5f60718293a4b5c6d7e8f9");

		Assert.That(JoinerPunch.TryReadToken(buffer[..written], out string report), Is.False);
		Assert.That(report, Is.Empty);
	}

	[Test]
	public void AJoinerReportIsNeverReadAsAPunch()
	{
		Span<byte> buffer = stackalloc byte[JoinerPunch.MaximumDatagramBytes];
		int written = JoinerPunch.TryWrite(buffer, "aaaabbbbccccddddeeeeffff00001111");

		Assert.That(ReachabilityPunch.TryReadProbeId(buffer[..written], out string probeId), Is.False);
		Assert.That(probeId, Is.Empty);
	}

	[Test]
	public void ANamesLengthIsTheOnlyLengthAccepted()
	{
		Span<byte> punch = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes + 8];
		Span<byte> report = stackalloc byte[JoinerPunch.MaximumDatagramBytes + 8];

		Assert.That(ReachabilityPunch.TryWrite(punch, new string('a', ReachabilityPunch.MaximumProbeIdBytes)), Is.EqualTo(6 + ReachabilityPunch.MaximumProbeIdBytes), "the longest name a ticket states fits");
		Assert.That(ReachabilityPunch.TryWrite(punch, new string('a', ReachabilityPunch.MaximumProbeIdBytes + 1)), Is.EqualTo(0));
		Assert.That(ReachabilityPunch.TryWrite(punch, ReadOnlySpan<char>.Empty), Is.EqualTo(0));

		Assert.That(JoinerPunch.TryWrite(report, new string('a', JoinerPunch.MaximumTokenBytes)), Is.EqualTo(5 + JoinerPunch.MaximumTokenBytes));
		Assert.That(JoinerPunch.TryWrite(report, new string('a', JoinerPunch.MaximumTokenBytes + 1)), Is.EqualTo(0));
		Assert.That(JoinerPunch.TryWrite(report, ReadOnlySpan<char>.Empty), Is.EqualTo(0));
	}

	[Test]
	public void ABufferThatCannotHoldTheNameIsRefusedRatherThanFilledHalfway()
	{
		Span<byte> punch = stackalloc byte[8];
		Span<byte> report = stackalloc byte[8];

		Assert.That(ReachabilityPunch.TryWrite(punch, "0123456789"), Is.EqualTo(0));
		Assert.That(JoinerPunch.TryWrite(report, "0123456789"), Is.EqualTo(0));
	}

	[Test]
	public void ANameThisBuildCannotWriteIsRefused()
	{
		Span<byte> punch = stackalloc byte[ReachabilityPunch.MaximumDatagramBytes];
		Span<byte> report = stackalloc byte[JoinerPunch.MaximumDatagramBytes];

		Assert.That(ReachabilityPunch.TryWrite(punch, "punch\u00e9"), Is.EqualTo(0));
		Assert.That(JoinerPunch.TryWrite(report, "join\u00e9"), Is.EqualTo(0));
	}

	[Test]
	public void ADatagramLongerThanTheLargestOneIsNotAName()
	{
		byte[] tooLong = new byte[ReachabilityPunch.MaximumDatagramBytes + 1];
		"PUNCH:"u8.CopyTo(tooLong);
		for (int index = 6; index < tooLong.Length; index++) tooLong[index] = (byte)'a';

		Assert.That(ReachabilityPunch.TryReadProbeId(tooLong, out string probeId), Is.False);
		Assert.That(probeId, Is.Empty);
	}

	[Test]
	public void ATicketIsUsableOnlyWhenItNamesWhereAndWhichJoin()
	{
		Assert.That(new JoinerPunchTicket("203.0.113.1", 8998, "token").IsUsable, Is.True);
		Assert.That(new JoinerPunchTicket(string.Empty, 8998, "token").IsUsable, Is.False);
		Assert.That(new JoinerPunchTicket("203.0.113.1", 0, "token").IsUsable, Is.False);
		Assert.That(new JoinerPunchTicket("203.0.113.1", 8998, string.Empty).IsUsable, Is.False);
	}
}

/// <summary>
///     The token a registry node gives one join: it names that join and no other, it states nothing until things happen, and
///     it stops existing when its lobby does. Two joins in one lobby must not share it, which is what makes a punch and a
///     report answer the right player.
/// </summary>
[TestFixture]
public class JoinerPunchTokenTests
{
	private JoinerPunchRelay CreateRelay()
	{
		return new JoinerPunchRelay(new ConcurrentDictionary<string, WebSocket>(), new LobbyRegistry(), _ => { });
	}

	[Test]
	public void EveryJoinGetsATokenOfItsOwn()
	{
		using JoinerPunchRelay relay = CreateRelay();

		JoinerPunchTicket first = relay.Register("lobby-1");
		JoinerPunchTicket second = relay.Register("lobby-1");

		Assert.That(first.Token, Is.Not.Empty);
		Assert.That(second.Token, Is.Not.Empty);
		Assert.That(first.Token, Is.Not.EqualTo(second.Token), "two players joining one lobby must not be punched as each other");
		Assert.That(first.Port, Is.GreaterThan(0));
	}

	[Test]
	public void AJoinStatesNothingUntilItsReportAndItsPunchArrive()
	{
		using JoinerPunchRelay relay = CreateRelay();
		JoinerPunchTicket ticket = relay.Register("lobby-1");

		Assert.That(relay.ReadState(ticket.Token), Is.EqualTo(new JoinerPunchRelay.JoinPunchState(false, false)));

		Assert.That(relay.AcknowledgePunch(ticket.Token), Is.True);
		Assert.That(relay.ReadState(ticket.Token).Punched, Is.True);
		Assert.That(relay.ReadState(ticket.Token).Reported, Is.False, "a punch is not a report");
	}

	[Test]
	public void AnUnknownTokenStatesNothingRatherThanThrowing()
	{
		using JoinerPunchRelay relay = CreateRelay();

		Assert.That(relay.ReadState("not-a-token"), Is.EqualTo(new JoinerPunchRelay.JoinPunchState(false, false)));
		Assert.That(relay.ReadState(string.Empty), Is.EqualTo(new JoinerPunchRelay.JoinPunchState(false, false)));
		Assert.That(relay.AcknowledgePunch("not-a-token"), Is.False);
		Assert.That(relay.AcknowledgePunch(string.Empty), Is.False);
	}

	[Test]
	public void ClosingALobbyForgetsEveryTokenItHandedOut()
	{
		using JoinerPunchRelay relay = CreateRelay();
		JoinerPunchTicket closed = relay.Register("lobby-1");
		JoinerPunchTicket other = relay.Register("lobby-2");

		relay.Forget("lobby-1");

		Assert.That(relay.ReadState(closed.Token), Is.EqualTo(new JoinerPunchRelay.JoinPunchState(false, false)));
		Assert.That(relay.AcknowledgePunch(closed.Token), Is.False);
		Assert.That(relay.AcknowledgePunch(other.Token), Is.True, "another lobby's join keeps its token");
	}
}
