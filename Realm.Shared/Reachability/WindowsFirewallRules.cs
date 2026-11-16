using System;
using System.Collections.Generic;

namespace Realm.Shared.Reachability;

/// <summary>What the local firewall currently says about the inbound rule a lobby needs.</summary>
public enum WindowsFirewallRuleState
{
	/// <summary>No rule by this name exists, or one exists that does not allow the port.</summary>
	Absent,

	/// <summary>The port is allowed in from the network.</summary>
	Allowed,

	/// <summary>A rule by this name blocks the port.</summary>
	Blocked,

	/// <summary>A rule by this name allows the port but is switched off.</summary>
	Disabled,

	/// <summary>The state could not be read, so nothing can be assumed about the rule.</summary>
	Unknown
}

/// <summary>
///     The one inbound allowance a hosted lobby needs from Windows, and the commands that ask for it. A machine that
///     refuses the prompt once keeps the refusal as a rule of its own, so the allowance is asked for again on every attempt
///     to host rather than being remembered as refused.
/// </summary>
public static class WindowsFirewallRules
{
	/// <summary>
	///     Every name this build's allowances start with. A build that names its program keeps one rule for every port that
	///     program will ever hold, so any rule of ours that names a port is a leftover from a build that could not name one:
	///     it covers the port it was made for and nothing else, and on a machine whose lobby now binds a port the system
	///     chose it covers nothing at all.
	/// </summary>
	public const string RuleNamePrefix = "Realm RTS Lobby";

	private const string PerPortRuleNamePrefix = RuleNamePrefix + " UDP ";

	private const string ProgramRuleName = RuleNamePrefix;

	private static readonly char[] NewLineSeparator = { (char)13, (char)10 };

	/// <summary>
	///     The name the allowance carries, so a later attempt can tell whether it is already in place. An allowance that names
	///     the program it covers is one rule for every port that program will ever hold, which is what a lobby binding a port
	///     the system chooses needs; a build with no executable of its own to name keeps one rule per port.
	/// </summary>
	/// <param name="port">The lobby port the allowance covers when there is no program to name instead.</param>
	/// <param name="executablePath">The program the allowance covers, or an empty string when there is none to name.</param>
	/// <returns>The rule name.</returns>
	public static string RuleName(int port, string? executablePath) => string.IsNullOrWhiteSpace(executablePath) ? $"Realm RTS Lobby UDP {port}" : ProgramRuleName;

	/// <summary>
	///     Whether this rule name is one a build that could not name its program made, which is the shape an allowance has
	///     before it is migrated to the program-scoped rule. Only a name this build produced is recognised: a rule the
	///     player made by hand is not ours to delete.
	/// </summary>
	/// <param name="ruleName">The rule name to judge.</param>
	/// <returns>Whether the name is one of this build's per-port allowances.</returns>
	public static bool IsPerPortRuleName(string? ruleName)
	{
		if (string.IsNullOrWhiteSpace(ruleName)) return false;
		string name = ruleName.Trim();
		if (!name.StartsWith(PerPortRuleNamePrefix, StringComparison.Ordinal)) return false;

		string portText = name[PerPortRuleNamePrefix.Length..];
		return portText.Length > 0 && portText.Length <= 5 && int.TryParse(portText, out int port) && port is > 0 and <= 65535;
	}

	/// <summary>
	///     The PowerShell command that names every allowance this build has ever made on this machine, one per line. It is
	///     asked for before the program-scoped allowance is granted, because a machine that hosted under an older build keeps
	///     the per-port rule that build added and would otherwise be left with two.
	/// </summary>
	/// <returns>The command to run, which needs no elevation.</returns>
	public static string BuildPowerShellListCommand()
	{
		return "$rules = Get-NetFirewallRule -DisplayName '" + RuleNamePrefix + "*' -ErrorAction SilentlyContinue; " +
		       "if ($rules) { $rules | ForEach-Object { $_.DisplayName } }";
	}

	/// <summary>
	///     Reads the rule names the list command printed, keeping the ones this build produced and dropping duplicates. Text
	///     PowerShell could not answer with is read as no rules, so an unreadable machine is never mistaken for a machine
	///     with allowances to clean up.
	/// </summary>
	/// <param name="output">What the list command printed.</param>
	/// <returns>The rule names, without duplicates.</returns>
	public static IReadOnlyList<string> ParseRuleNames(string? output)
	{
		var names = new List<string>(4);
		if (string.IsNullOrWhiteSpace(output)) return names;

		foreach (string rawLine in output.Split(NewLineSeparator))
		{
			string name = rawLine.Trim();
			if (!name.StartsWith(RuleNamePrefix, StringComparison.Ordinal)) continue;
			if (names.Contains(name, StringComparer.Ordinal)) continue;
			names.Add(name);
		}

		return names;
	}

	/// <summary>The command that deletes one allowance by name, used to migrate the per-port rules a machine kept.</summary>
	/// <param name="ruleName">The rule to delete.</param>
	/// <returns>The netsh arguments.</returns>
	public static string BuildDeleteByNameCommand(string ruleName) => $"advfirewall firewall delete rule name=\"{(ruleName ?? string.Empty).Trim()}\"";

	/// <summary>The command that reports the allowance, which needs no elevation to run.</summary>
	public static string BuildShowCommand(int port, string? executablePath) => $"advfirewall firewall show rule name=\"{RuleName(port, executablePath)}\"";

	/// <summary>
	///     Reads the single-word answer the screen asks PowerShell for. A name rather than a sentence is what keeps this
	///     readable on a Windows installed in a language this build does not know.
	/// </summary>
	public static WindowsFirewallRuleState ParseStateToken(string output)
	{
		string token = (output ?? string.Empty).Trim().ToLowerInvariant();
		return token switch
		{
			"allowed" => WindowsFirewallRuleState.Allowed,
			"blocked" => WindowsFirewallRuleState.Blocked,
			"disabled" => WindowsFirewallRuleState.Disabled,
			"absent" => WindowsFirewallRuleState.Absent,
			_ => WindowsFirewallRuleState.Unknown
		};
	}

	/// <summary>
	///     The command that grants the allowance. It needs elevation, which is the point: the screen asks for it with a
	///     prompt each time it hosts, so a refusal is never mistaken for a permanent answer.
	/// </summary>
	public static string BuildAddCommand(int port, string? executablePath)
	{
		if (string.IsNullOrWhiteSpace(executablePath))
		{
			return $"advfirewall firewall add rule name=\"{RuleName(port, null)}\" dir=in action=allow protocol=UDP localport={port} enable=yes profile=any";
		}

		return $"advfirewall firewall add rule name=\"{RuleName(port, executablePath)}\" dir=in action=allow protocol=UDP program=\"{executablePath}\" enable=yes profile=any";
	}

	/// <summary>The command that removes an allowance this build granted, used when a port is reopened and by the harness.</summary>
	public static string BuildDeleteCommand(int port, string? executablePath) => $"advfirewall firewall delete rule name=\"{RuleName(port, executablePath)}\"";

	/// <summary>
	///     The PowerShell command that reports the allowance's state as a single word, so a Windows installed in a language
	///     this build does not know answers the same way netsh would not.
	/// </summary>
	/// <remarks>
	///     The query is assembled by concatenation rather than by formatting on purpose: it carries PowerShell script
	///     blocks, and a format string reads their braces as placeholders, which fails before PowerShell is ever started.
	/// </remarks>
	/// <param name="port">The port the allowance covers when there is no program to name instead.</param>
	/// <param name="executablePath">The program the allowance covers, or an empty string when there is none to name.</param>
	/// <returns>The command to run, which needs no elevation.</returns>
	public static string BuildPowerShellStateQuery(int port, string? executablePath)
	{
		string quotedRuleName = RuleName(port, executablePath).Replace("'", "''");

		return "$r = Get-NetFirewallRule -DisplayName '" + quotedRuleName + "' -ErrorAction SilentlyContinue; " +
		       "if (-not $r) { 'absent' } elseif ($r.Action -eq 'Block') { 'blocked' } elseif ($r.Enabled -ne 'True') { 'disabled' } else { 'allowed' }";
	}

	/// <summary>
	///     Reads what <see cref="BuildShowCommand" /> printed. A rule that blocks the port or that is switched off is not an
	///     allowance, and is reported as such so the caller grants it again.
	/// </summary>
	public static WindowsFirewallRuleState ParseShowOutput(string output)
	{
		if (string.IsNullOrWhiteSpace(output)) return WindowsFirewallRuleState.Absent;

		bool sawRuleBlock = false;
		bool enabled = true;
		string action = string.Empty;

		foreach (string rawLine in output.Split('\n'))
		{
			string line = rawLine.Trim();
			int separatorIndex = line.IndexOf(':');
			if (separatorIndex <= 0) continue;

			string key = line[..separatorIndex].Trim();
			string value = line[(separatorIndex + 1)..].Trim();

			if (key.Equals("Rule Name", StringComparison.OrdinalIgnoreCase))
			{
				sawRuleBlock = true;
				enabled = true;
				action = string.Empty;
				continue;
			}

			if (!sawRuleBlock) continue;

			if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
			{
				enabled = value.Equals("Yes", StringComparison.OrdinalIgnoreCase);
			}
			else if (key.Equals("Action", StringComparison.OrdinalIgnoreCase))
			{
				action = value;
			}
		}

		if (!sawRuleBlock) return WindowsFirewallRuleState.Absent;
		if (action.Equals("Block", StringComparison.OrdinalIgnoreCase)) return WindowsFirewallRuleState.Blocked;
		if (!enabled) return WindowsFirewallRuleState.Disabled;
		if (action.Equals("Allow", StringComparison.OrdinalIgnoreCase)) return WindowsFirewallRuleState.Allowed;
		return WindowsFirewallRuleState.Absent;
	}

	/// <summary>Whether the state is already the allowance a lobby needs, so no prompt is worth showing.</summary>
	public static bool IsOpen(WindowsFirewallRuleState state) => state == WindowsFirewallRuleState.Allowed;
}
