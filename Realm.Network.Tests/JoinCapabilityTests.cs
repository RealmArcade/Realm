using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     What a lobby server states about itself, and what a client does with it rather than waiting out a timeout, plus the
///     firewall allowance migration that keeps one rule on a machine that hosted under an older build.
/// </summary>
[TestFixture]
public class JoinCapabilityTests
{
	private static RegistryCapabilities Capabilities(bool relaysJoinPunch, bool statesClasses = true) => new(RegistryCapabilities.EndpointListProtocolVersion, new[] { "ipv4" })
	{
		RelaysJoinPunch = relaysJoinPunch,
		ReachabilityClasses = statesClasses ? ReachabilityClasses.All : System.Array.Empty<string>()
	};

	private static LobbyJoinAnswer Answer(RegistryCapabilities? capabilities, int punchPort = 0, string punchToken = "") =>
		new("203.0.113.2", 8999, null, new List<LobbyEndpointInfo>(), punchPort, punchToken, capabilities);

	[Test]
	public void ANeedForAPunchIsAnsweredByThePunchTheNodeStated()
	{
		Assert.That(LobbyJoinPreconditions.DescribeBlock(Answer(Capabilities(true), 8998, "ticket"), needsPunch: true), Is.EqualTo(LobbyJoinBlock.None));
	}

	[Test]
	public void ALobbyThatNeedsNoPunchNeedsNothingFromTheNode()
	{
		Assert.That(LobbyJoinPreconditions.DescribeBlock(Answer(Capabilities(false)), needsPunch: false), Is.EqualTo(LobbyJoinBlock.None));
	}

	[Test]
	public void ANodeThatSaysItDoesNotRelayPunchesIsNamedRatherThanWaitedOut()
	{
		Assert.That(LobbyJoinPreconditions.DescribeBlock(Answer(Capabilities(false)), needsPunch: true), Is.EqualTo(LobbyJoinBlock.NodeDoesNotRelayPunches));
	}

	[Test]
	public void ANodeOlderThanTheRelayStatesNothingAndIsNamedAsSuch()
	{
		Assert.That(LobbyJoinPreconditions.DescribeBlock(Answer(null), needsPunch: true), Is.EqualTo(LobbyJoinBlock.NodeStatedNoCapabilities));
	}

	[Test]
	public void ANodeThatRelaysButNamedNoCheckIsNamedAsUnusable()
	{
		Assert.That(LobbyJoinPreconditions.DescribeBlock(Answer(Capabilities(true)), needsPunch: true), Is.EqualTo(LobbyJoinBlock.NodeNamedNoCheck));
	}

	[Test]
	public void WhatTheNodeStatedIsStatedBackInTheFailure()
	{
		string stated = LobbyJoinPreconditions.Describe(Capabilities(false));

		Assert.That(stated, Does.Contain("endpoint families ipv4"));
		Assert.That(stated, Does.Contain("join punch relay no"));
		Assert.That(stated, Does.Contain(ReachabilityClasses.Open));
		Assert.That(LobbyJoinPreconditions.Describe(null), Is.Empty, "a node that stated nothing is not described");
	}

	[Test]
	public void AJoinAnswerCarriesWhatTheNodeStatedItCanDo()
	{
		const string json = "{\"hostIP\":\"203.0.113.2\",\"hostPort\":8999,\"punchPort\":8998,\"punchToken\":\"ticket\"," +
		                    "\"capabilities\":{\"protocolVersion\":2,\"endpointFamilies\":[\"ipv4\",\"ipv6\"],\"allowsLocalCallers\":false," +
		                    "\"relaysJoinPunch\":true,\"reachabilityClasses\":[\"Open\",\"PunchOnly\",\"Unverified\"]}}";

		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse(json);

		Assert.That(answer, Is.Not.Null);
		Assert.That(answer!.Value.Capabilities, Is.Not.Null);
		Assert.That(answer.Value.Capabilities!.RelaysJoinPunch, Is.True);
		Assert.That(answer.Value.Capabilities.ChecksEndpointList, Is.True);
		Assert.That(answer.Value.Capabilities.ReachabilityClasses, Is.EquivalentTo(ReachabilityClasses.All));
	}

	[Test]
	public void AnAnswerFromANodeThatStatesNothingIsStillAUsableAnswer()
	{
		LobbyJoinAnswer? answer = LobbyJoinAnswers.TryParse("{\"hostIP\":\"203.0.113.2\",\"hostPort\":8999}");

		Assert.That(answer, Is.Not.Null);
		Assert.That(answer!.Value.Capabilities, Is.Null);
		Assert.That(answer.Value.HostPort, Is.EqualTo(8999));
	}

	[Test]
	public void OnlyThePerPortAllowancesThisBuildMadeAreRecognisedAsLeftovers()
	{
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Realm RTS Lobby UDP 8999"), Is.True);
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Realm RTS Lobby"), Is.False, "the program-scoped rule is not a leftover");
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Realm RTS Lobby UDP"), Is.False);
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Realm RTS Lobby UDP 0"), Is.False);
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Realm RTS Lobby UDP 70000"), Is.False);
		Assert.That(WindowsFirewallRules.IsPerPortRuleName("Somebody Else's Rule"), Is.False);
	}

	[Test]
	public void TheRuleListKeepsOnlyThisBuildsRulesWithoutDuplicates()
	{
		IReadOnlyList<string> names = WindowsFirewallRules.ParseRuleNames("Realm RTS Lobby\r\nRealm RTS Lobby UDP 8999\r\nRealm RTS Lobby UDP 8999\r\nOther\r\n");

		Assert.That(names, Is.EqualTo(new[] { "Realm RTS Lobby", "Realm RTS Lobby UDP 8999" }));
	}

	[Test]
	public async Task AMachineThatHostedUnderAnOlderBuildEndsWithOneAllowance()
	{
		var runner = new RecordingFirewallRunner { AuthorityExecutablePath = @"C:\Program Files\Realm\Realm.ListenServerHost.exe" };
		runner.Rules["Realm RTS Lobby UDP 8999"] = WindowsFirewallRuleState.Allowed;
		runner.Rules["Realm RTS Lobby UDP 52293"] = WindowsFirewallRuleState.Allowed;

		var requester = new WindowsFirewallRuleRequester(runner);
		WindowsFirewallGrantResult result = await requester.RequestInboundUdpForPortAsync(8999);

		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.Granted));
		Assert.That(runner.Rules.Keys, Is.EqualTo(new[] { "Realm RTS Lobby" }), "the per-port leftovers are gone and only the program rule remains");
		Assert.That(runner.ElevatedCommands, Has.Some.Contains("program="), "the rule this build keeps is the program-scoped one");
		Assert.That(runner.Rules.ContainsKey("Realm RTS Lobby"), Is.True);
	}

	[Test]
	public async Task AMachineThatAlreadyHasTheProgramRuleLosesOnlyItsLeftovers()
	{
		var runner = new RecordingFirewallRunner { AuthorityExecutablePath = @"C:\Program Files\Realm\Realm.ListenServerHost.exe" };
		runner.Rules["Realm RTS Lobby"] = WindowsFirewallRuleState.Allowed;
		runner.Rules["Realm RTS Lobby UDP 8999"] = WindowsFirewallRuleState.Allowed;

		WindowsFirewallGrantResult result = await new WindowsFirewallRuleRequester(runner).RequestInboundUdpForPortAsync(8999);

		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.AlreadyOpen));
		Assert.That(runner.Rules.Keys, Is.EqualTo(new[] { "Realm RTS Lobby" }));
		Assert.That(runner.ElevatedCommands, Has.Exactly(1).Items, "one removal, and no second prompt for a rule that already exists");
	}

	[Test]
	public void ASeederBehindARouterIsKeptButNeverOffered()
	{
		var registry = new Realm.AdminServer.Services.SeederRegistry();
		registry.Register(new Realm.AdminServer.Models.SeederInfo { SeederId = "lan", IP = "10.10.0.2", Port = 9002, MapIds = new List<string> { "map_a" }, LanOnly = true });
		registry.Register(new Realm.AdminServer.Models.SeederInfo { SeederId = "public", IP = "203.0.113.9", Port = 9001, MapIds = new List<string> { "map_a" } });

		Assert.That(registry.GetAdvertised().Select(seeder => seeder.SeederId), Is.EqualTo(new[] { "public" }));
		Assert.That(registry.GetSeedersForMap("map_a").Select(seeder => seeder.SeederId), Is.EqualTo(new[] { "public" }), "a download is never offered an endpoint nothing confirmed");
		Assert.That(registry.Get("lan"), Is.Not.Null, "a LAN-only seeder stays registered for the players its own network serves");
	}

	[Test]
	public void AnEndpointIsAdvertisedOnlyWhenItIsPublicAndConfirmed()
	{
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsAdvertisable("203.0.113.9", confirmed: true), Is.True);
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsAdvertisable("203.0.113.9", confirmed: false), Is.False);
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsAdvertisable("10.10.0.2", confirmed: true), Is.False);
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsConfirmationFresh(System.DateTime.UtcNow.AddSeconds(-59), System.DateTime.UtcNow), Is.True);
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsConfirmationFresh(System.DateTime.UtcNow.AddSeconds(-61), System.DateTime.UtcNow), Is.False);
		Assert.That(Realm.Shared.Distribution.SeederAdvertisement.IsConfirmationFresh(null, System.DateTime.UtcNow), Is.False);
	}

	private sealed class RecordingFirewallRunner : IWindowsFirewallCommandRunner
	{
		public Dictionary<string, WindowsFirewallRuleState> Rules { get; } = new();

		public List<string> ElevatedCommands { get; } = new();

		public string AuthorityExecutablePath { get; set; } = string.Empty;

		public Task<WindowsFirewallRuleState> ReadRuleStateAsync(int port, CancellationToken cancellationToken = default)
		{
			string name = WindowsFirewallRules.RuleName(port, AuthorityExecutablePath);
			return Task.FromResult(Rules.TryGetValue(name, out WindowsFirewallRuleState state) ? state : WindowsFirewallRuleState.Absent);
		}

		public Task<IReadOnlyList<string>> ReadRuleNamesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<string>>(Rules.Keys.ToList());
		}

		public Task<WindowsFirewallGrant> RunElevatedAsync(string arguments, CancellationToken cancellationToken = default)
		{
			ElevatedCommands.Add(arguments);

			if (arguments.Contains("delete rule name="))
			{
				string name = Between(arguments, "name=\"", "\"");
				Rules.Remove(name);
				return Task.FromResult(WindowsFirewallGrant.Granted);
			}

			if (arguments.Contains("add rule name="))
			{
				string name = Between(arguments, "name=\"", "\"");
				Rules[name] = WindowsFirewallRuleState.Allowed;
				return Task.FromResult(WindowsFirewallGrant.Granted);
			}

			return Task.FromResult(WindowsFirewallGrant.Failed);
		}

		private static string Between(string text, string after, string before)
		{
			int start = text.IndexOf(after, System.StringComparison.Ordinal);
			if (start < 0) return string.Empty;
			start += after.Length;
			int end = text.IndexOf(before, start, System.StringComparison.Ordinal);
			return end < 0 ? string.Empty : text[start..end];
		}
	}
}
