using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Realm.Shared.Reachability;

namespace Realm.Network.Tests;

/// <summary>
///     Covers the inbound allowance a hosted lobby asks Windows for. The rules that matter are that the command names the
///     port the lobby listens on, that a refusal is read as a refusal rather than as a permanent answer, and that every
///     attempt to host asks again.
/// </summary>
[TestFixture]
public class WindowsFirewallRuleTests
{
	[Test]
	public void TheAllowanceNamesTheLobbyPort()
	{
		string command = WindowsFirewallRules.BuildAddCommand(8999, null);

		Assert.That(command, Does.Contain($"name=\"{WindowsFirewallRules.RuleName(8999)}\""));
		Assert.That(command, Does.Contain("dir=in"));
		Assert.That(command, Does.Contain("action=allow"));
		Assert.That(command, Does.Contain("protocol=UDP"));
		Assert.That(command, Does.Contain("localport=8999"));
		Assert.That(command, Does.Not.Contain("program="));
	}

	[Test]
	public void TheAllowanceNamesTheAuthorityWhenThereIsOneToName()
	{
		string command = WindowsFirewallRules.BuildAddCommand(9000, @"C:\Games\Realm\Realm.ListenServerHost\Realm.ListenServerHost.exe");
		Assert.That(command, Does.Contain(@"program=""C:\Games\Realm\Realm.ListenServerHost\Realm.ListenServerHost.exe"""));
	}

	[Test]
	public void TheShowReportOfAnAllowanceReadsAsOpen()
	{
		const string output = "Rule Name:                            Realm RTS Lobby UDP 8999\n" +
		                      "Enabled:                              Yes\n" +
		                      "Direction:                            In\n" +
		                      "Action:                               Allow\n";

		Assert.That(WindowsFirewallRules.ParseShowOutput(output), Is.EqualTo(WindowsFirewallRuleState.Allowed));
		Assert.That(WindowsFirewallRules.IsOpen(WindowsFirewallRules.ParseShowOutput(output)), Is.True);
	}

	[Test]
	public void TheShowReportOfABlockReadsAsBlocked()
	{
		const string output = "Rule Name:                            Realm RTS Lobby UDP 8999\n" +
		                      "Enabled:                              Yes\n" +
		                      "Action:                               Block\n";

		Assert.That(WindowsFirewallRules.ParseShowOutput(output), Is.EqualTo(WindowsFirewallRuleState.Blocked));
		Assert.That(WindowsFirewallRules.IsOpen(WindowsFirewallRules.ParseShowOutput(output)), Is.False);
	}

	[Test]
	public void TheShowReportOfASwitchedOffRuleReadsAsDisabled()
	{
		const string output = "Rule Name:                            Realm RTS Lobby UDP 8999\n" +
		                      "Enabled:                              No\n" +
		                      "Action:                               Allow\n";

		Assert.That(WindowsFirewallRules.ParseShowOutput(output), Is.EqualTo(WindowsFirewallRuleState.Disabled));
	}

	[Test]
	public void TheSingleWordAnswerOfTheRuleQueryReadsAsItsState()
	{
		Assert.That(WindowsFirewallRules.ParseStateToken("allowed"), Is.EqualTo(WindowsFirewallRuleState.Allowed));
		Assert.That(WindowsFirewallRules.ParseStateToken("blocked"), Is.EqualTo(WindowsFirewallRuleState.Blocked));
		Assert.That(WindowsFirewallRules.ParseStateToken("disabled"), Is.EqualTo(WindowsFirewallRuleState.Disabled));
		Assert.That(WindowsFirewallRules.ParseStateToken("absent"), Is.EqualTo(WindowsFirewallRuleState.Absent));
		Assert.That(WindowsFirewallRules.ParseStateToken("Get-NetFirewallRule : Access is denied"), Is.EqualTo(WindowsFirewallRuleState.Unknown));
	}

	[Test]
	public void AReportThatMatchesNoRuleReadsAsAbsent()
	{
		Assert.That(WindowsFirewallRules.ParseShowOutput("No rules match the specified criteria."), Is.EqualTo(WindowsFirewallRuleState.Absent));
		Assert.That(WindowsFirewallRules.ParseShowOutput(string.Empty), Is.EqualTo(WindowsFirewallRuleState.Absent));
	}

	[Test]
	public async Task AnAllowanceAlreadyInPlaceIsNotAskedForAgain()
	{
		var runner = new ScriptedFirewallRunner(WindowsFirewallRuleState.Allowed);
		var requester = new WindowsFirewallRuleRequester(runner);

		WindowsFirewallGrantResult result = await requester.RequestInboundUdpForPortAsync(8999);

		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.AlreadyOpen));
		Assert.That(runner.ElevatedCommands, Is.Empty);
	}

	[Test]
	public async Task ADismissedPromptIsAskedAgainTheNextTimeThePlayerHosts()
	{
		var runner = new ScriptedFirewallRunner(WindowsFirewallRuleState.Absent) { ElevationAnswer = WindowsFirewallGrant.Declined };
		var requester = new WindowsFirewallRuleRequester(runner);

		WindowsFirewallGrantResult first = await requester.RequestInboundUdpForPortAsync(8999);
		WindowsFirewallGrantResult second = await requester.RequestInboundUdpForPortAsync(8999);
		WindowsFirewallGrantResult third = await requester.RequestInboundUdpForPortAsync(8999);

		Assert.That(first.Grant, Is.EqualTo(WindowsFirewallGrant.Declined));
		Assert.That(second.Grant, Is.EqualTo(WindowsFirewallGrant.Declined));
		Assert.That(third.Grant, Is.EqualTo(WindowsFirewallGrant.Declined));
		Assert.That(runner.ElevatedCommands.Count, Is.EqualTo(3), "each attempt to host must ask again rather than remembering one dismissal");
	}

	[Test]
	public async Task ARuleThatBlocksThePortIsReplacedRatherThanLeftInPlace()
	{
		var runner = new ScriptedFirewallRunner(WindowsFirewallRuleState.Blocked) { ElevationAnswer = WindowsFirewallGrant.Granted };
		var requester = new WindowsFirewallRuleRequester(runner);

		WindowsFirewallGrantResult result = await requester.RequestInboundUdpForPortAsync(8999);

		Assert.That(runner.ElevatedCommands.Count, Is.EqualTo(2), "a blocking rule must be removed before the allowance is added");
		Assert.That(runner.ElevatedCommands[0], Is.EqualTo(WindowsFirewallRules.BuildDeleteCommand(8999)));
		Assert.That(runner.ElevatedCommands[1], Does.Contain("add rule"));
		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.Failed), "a grant has to be confirmed by reading the rule back, and this fake never changes its answer");
	}

	[Test]
	public async Task AGrantedAllowanceIsConfirmedByReadingItBack()
	{
		var runner = new ScriptedFirewallRunner(WindowsFirewallRuleState.Absent) { ElevationAnswer = WindowsFirewallGrant.Granted, BecomesOpenAfterGrant = true };
		var requester = new WindowsFirewallRuleRequester(runner);

		WindowsFirewallGrantResult result = await requester.RequestInboundUdpForPortAsync(8999);

		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.Granted));
		Assert.That(runner.ElevatedCommands.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task AnUnusablePortIsNotAskedFor()
	{
		var runner = new ScriptedFirewallRunner(WindowsFirewallRuleState.Absent);
		var requester = new WindowsFirewallRuleRequester(runner);

		WindowsFirewallGrantResult result = await requester.RequestInboundUdpForPortAsync(0);

		Assert.That(result.Grant, Is.EqualTo(WindowsFirewallGrant.Failed));
		Assert.That(runner.ElevatedCommands, Is.Empty);
	}

	private static string RuleReport(string action, bool enabled) =>
		$"Rule Name:                            Realm RTS Lobby UDP 8999\nEnabled:                              {(enabled ? "Yes" : "No")}\nAction:                               {action}\n";

	private class ScriptedFirewallRunner : IWindowsFirewallCommandRunner
	{
		private readonly WindowsFirewallRuleState _state;

		public ScriptedFirewallRunner(WindowsFirewallRuleState state)
		{
			_state = state;
		}

		public string AuthorityExecutablePath => string.Empty;

		public WindowsFirewallGrant ElevationAnswer { get; init; } = WindowsFirewallGrant.Granted;

		public bool BecomesOpenAfterGrant { get; init; }

		public List<string> ElevatedCommands { get; } = new();

		public Task<WindowsFirewallRuleState> ReadRuleStateAsync(int port, CancellationToken cancellationToken = default)
		{
			if (BecomesOpenAfterGrant && ElevatedCommands.Count > 0) return Task.FromResult(WindowsFirewallRuleState.Allowed);
			return Task.FromResult(_state);
		}

		public Task<WindowsFirewallGrant> RunElevatedAsync(string arguments, CancellationToken cancellationToken = default)
		{
			ElevatedCommands.Add(arguments);
			return Task.FromResult(ElevationAnswer);
		}
	}
}
