using System.Collections.Generic;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     What a lobby's class says when more than one registry node measured the same game port: two nodes are two
///     destinations, so their agreement is the measurement a single node cannot make, and their disagreement is a fact
///     about the router rather than a disagreement to average away.
/// </summary>
[TestFixture]
public class SecondVantageTests
{
	private static ReachabilityReport Local(int observedPort, int secondObservedPort = 0, bool coldAnswered = false, string observedIp = "203.0.113.2") =>
		new("local", ReachabilityStates.Answered, true, observedIp, observedPort, "203.0.113.2", 8999, "203.0.113.1", 1, "ipv4", coldAnswered, 1, string.Empty, secondObservedPort)
		{
			SourcesAnswered = 1
		};

	private static ReachabilityReport Peer(int observedPort, bool coldAnswered = false, bool answered = true) =>
		new("peer", answered ? ReachabilityStates.Answered : ReachabilityStates.Silent, answered, "203.0.113.2", observedPort, "203.0.113.2", 8999, "203.0.113.1", 1, "ipv4", coldAnswered, 1);

	[Test]
	public void APeerThatSawTheSameOutsidePortMeasuresOneMappingForEveryDestination()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(10000), new List<ReachabilityReport> { Peer(10000) });

		Assert.That(MappingBehaviours.FromMeasurement(merged.ObservedPort, merged.SecondObservedPort), Is.EqualTo(MappingBehaviours.Reused));
		Assert.That(merged.SourcesChecked, Is.EqualTo(2));
		Assert.That(merged.SourcesAnswered, Is.EqualTo(2));
		Assert.That(merged.ObservedIp, Is.EqualTo("203.0.113.2"), "a peer's view never replaces the endpoint this node measured");
	}

	[Test]
	public void APeerThatSawADifferentOutsidePortProvesTheMappingIsMadePerDestination()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(10000), new List<ReachabilityReport> { Peer(10001) });

		Assert.That(MappingBehaviours.FromMeasurement(merged.ObservedPort, merged.SecondObservedPort), Is.EqualTo(MappingBehaviours.PerDestination));
	}

	[Test]
	public void ASecondObservationThisNodeMadeItselfIsNotReplacedByAPeer()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(10000, secondObservedPort: 10000), new List<ReachabilityReport> { Peer(10001) });

		Assert.That(merged.SecondObservedPort, Is.EqualTo(10000));
		Assert.That(merged.SourcesAnswered, Is.EqualTo(2));
	}

	[Test]
	public void APortThatOnlyOneNodeReachedColdIsNotOpenForEverybody()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(8999, coldAnswered: true), new List<ReachabilityReport> { Peer(8999, coldAnswered: false) });

		Assert.That(merged.ColdAnswered, Is.False);
		Assert.That(ReachabilityClasses.FromReport(merged, MappingBehaviours.Reused), Is.EqualTo(ReachabilityClasses.PunchOnly));
	}

	[Test]
	public void APeerThatNeverAnsweredLeavesTheClaimThisNodeMeasured()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(8999, coldAnswered: true), new List<ReachabilityReport> { Peer(0, answered: false) });

		Assert.That(merged.ColdAnswered, Is.True);
		Assert.That(merged.SecondObservedPort, Is.EqualTo(0));
		Assert.That(merged.SourcesChecked, Is.EqualTo(2));
		Assert.That(merged.SourcesAnswered, Is.EqualTo(1));
		Assert.That(ReachabilityClasses.FromReport(merged, MappingBehaviours.FromMeasurement(merged.ObservedPort, merged.SecondObservedPort)), Is.EqualTo(ReachabilityClasses.Open));
	}

	[Test]
	public void ANodeWithNoPeerStatesOneSource()
	{
		ReachabilityReport merged = SecondVantage.Merge(Local(8999), new List<ReachabilityReport>());

		Assert.That(merged.SourcesChecked, Is.EqualTo(1));
		Assert.That(merged.SourcesAnswered, Is.EqualTo(1));
		Assert.That(ReachabilityClasses.FromReport(merged, MappingBehaviours.FromMeasurement(merged.ObservedPort, merged.SecondObservedPort)), Is.EqualTo(ReachabilityClasses.PunchOnly), "the report this node measured is unchanged");
	}
}
