using System;
using System.Collections.Generic;

namespace Realm.Shared.Reachability;

/// <summary>What happened when a screen asked Windows to let other players reach its lobby.</summary>
public enum WindowsFirewallGrant
{
	/// <summary>The allowance was already in place, so nothing was asked.</summary>
	AlreadyOpen,

	/// <summary>The player approved the request and the allowance is now in place.</summary>
	Granted,

	/// <summary>The player dismissed the request. The next attempt to host asks again.</summary>
	Declined,

	/// <summary>This build is not running on Windows, which has no such rule to add.</summary>
	NotApplicable,

	/// <summary>The request could not be made, and the detail says why.</summary>
	Failed
}

/// <summary>The detail the lobby help screen needs about the local firewall.</summary>
public readonly record struct WindowsFirewallGrantResult(WindowsFirewallGrant Grant, string Detail);

/// <summary>
///     The two things changing a firewall rule needs from the platform: reading what the rule currently says, and running a
///     command that changes it with the elevation the platform demands.
/// </summary>
public interface IWindowsFirewallCommandRunner
{
	/// <summary>The program the allowance should name, or an empty string when the platform has none to name.</summary>
	string AuthorityExecutablePath { get; }

	/// <summary>What the rule for this port currently says, read without elevation.</summary>
	Task<WindowsFirewallRuleState> ReadRuleStateAsync(int port, CancellationToken cancellationToken = default);

	/// <summary>
	///     The names of every allowance this build has ever made on this machine, read without elevation. It is what tells a
	///     per-port rule an older build left behind from the program-scoped one this build keeps.
	/// </summary>
	Task<IReadOnlyList<string>> ReadRuleNamesAsync(CancellationToken cancellationToken = default);

	/// <summary>Runs a command that changes the rule, with the elevation the platform demands for it.</summary>
	Task<WindowsFirewallGrant> RunElevatedAsync(string arguments, CancellationToken cancellationToken = default);
}

/// <summary>
///     Asks the local firewall for the one inbound allowance a hosted lobby needs. The allowance is asked for on every
///     attempt to host: a dismissed prompt leaves the port closed, and remembering that dismissal would turn one misclick
///     into a machine that can never host again. Nothing is cached here, so a later attempt always asks again.
/// </summary>
public class WindowsFirewallRuleRequester
{
	private readonly IWindowsFirewallCommandRunner _runner;

	public WindowsFirewallRuleRequester(IWindowsFirewallCommandRunner runner)
	{
		_runner = runner;
	}

	/// <summary>
	///     Asks for the allowance and reports what happened. Asking never throws: the firewall is a machine's own business,
	///     and nothing it answers - not a missing shell, not a rule it cannot read, not a prompt that never appears - may
	///     stop a host from publishing the lobby it is trying to open.
	/// </summary>
	/// <param name="port">The lobby port the allowance covers.</param>
	/// <param name="cancellationToken">Cancels the ask.</param>
	/// <returns>What the machine did about the allowance.</returns>
	public async Task<WindowsFirewallGrantResult> RequestInboundUdpForPortAsync(int port, CancellationToken cancellationToken = default)
	{
		if (port <= 0) return new WindowsFirewallGrantResult(WindowsFirewallGrant.Failed, "a lobby port is needed before the firewall can be asked for it");
		if (!OperatingSystem.IsWindows()) return new WindowsFirewallGrantResult(WindowsFirewallGrant.NotApplicable, "this platform has no Windows firewall rule to add");

		try
		{
			return await AskAsync(port, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return new WindowsFirewallGrantResult(WindowsFirewallGrant.Failed, $"the local firewall could not be asked: {exception.GetType().Name}: {exception.Message}");
		}
	}

	private async Task<WindowsFirewallGrantResult> AskAsync(int port, CancellationToken cancellationToken)
	{
		WindowsFirewallRuleState state = await ReadStateAsync(port, cancellationToken).ConfigureAwait(false);
		if (WindowsFirewallRules.IsOpen(state))
		{
			await RemoveOrphanedPerPortRulesAsync(cancellationToken).ConfigureAwait(false);
			return new WindowsFirewallGrantResult(WindowsFirewallGrant.AlreadyOpen, "the port is already allowed in");
		}

		if (state != WindowsFirewallRuleState.Absent)
		{
			await _runner.RunElevatedAsync(WindowsFirewallRules.BuildDeleteCommand(port, _runner.AuthorityExecutablePath), cancellationToken).ConfigureAwait(false);
		}

		string addArguments = WindowsFirewallRules.BuildAddCommand(port, _runner.AuthorityExecutablePath);
		WindowsFirewallGrant grant = await _runner.RunElevatedAsync(addArguments, cancellationToken).ConfigureAwait(false);

		if (grant == WindowsFirewallGrant.Declined) return new WindowsFirewallGrantResult(grant, "the elevation prompt was dismissed");
		if (grant != WindowsFirewallGrant.Granted) return new WindowsFirewallGrantResult(grant, "the firewall command could not be run");

		WindowsFirewallRuleState grantedState = await ReadStateAsync(port, cancellationToken).ConfigureAwait(false);
		if (WindowsFirewallRules.IsOpen(grantedState))
		{
			await RemoveOrphanedPerPortRulesAsync(cancellationToken).ConfigureAwait(false);
			return new WindowsFirewallGrantResult(WindowsFirewallGrant.Granted, "the port is now allowed in");
		}

		return new WindowsFirewallGrantResult(WindowsFirewallGrant.Failed, $"the allowance was requested but reads as {grantedState} afterwards");
	}

	/// <summary>
	///     Removes the allowances a build that could not name its program left behind, so a machine that hosted under an
	///     older build keeps at most the one rule this build maintains. Only names this build produced and no longer uses are
	///     touched: the program-scoped rule is kept, and a rule the player made by hand is not this build's to delete. A
	///     removal the platform refuses is logged and forgotten, because a migration that cannot run must never stop a host
	///     from publishing the lobby it is trying to open.
	/// </summary>
	/// <param name="cancellationToken">Cancels the sweep.</param>
	private async Task RemoveOrphanedPerPortRulesAsync(CancellationToken cancellationToken)
	{
		// A build with no executable to name keeps the per-port shape itself, so there is nothing to migrate it away from.
		if (string.IsNullOrWhiteSpace(_runner.AuthorityExecutablePath)) return;

		string currentRuleName = WindowsFirewallRules.RuleName(0, _runner.AuthorityExecutablePath);

		IReadOnlyList<string> names;
		try
		{
			names = await _runner.ReadRuleNamesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception)
		{
			return;
		}

		for (int index = 0; index < names.Count; index++)
		{
			string name = names[index];
			if (!WindowsFirewallRules.IsPerPortRuleName(name)) continue;
			if (string.Equals(name, currentRuleName, StringComparison.Ordinal)) continue;

			WindowsFirewallGrant deletion = await _runner.RunElevatedAsync(WindowsFirewallRules.BuildDeleteByNameCommand(name), cancellationToken).ConfigureAwait(false);
			if (deletion != WindowsFirewallGrant.Granted) return;
		}
	}

	private Task<WindowsFirewallRuleState> ReadStateAsync(int port, CancellationToken cancellationToken)
	{
		return _runner.ReadRuleStateAsync(port, cancellationToken);
	}
}
