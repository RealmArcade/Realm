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
	/// <summary>The name the allowance carries, so a later attempt can tell whether it is already in place.</summary>
	public static string RuleName(int port) => $"Realm RTS Lobby UDP {port}";

	/// <summary>The command that reports the allowance, which needs no elevation to run.</summary>
	public static string BuildShowCommand(int port) => $"advfirewall firewall show rule name=\"{RuleName(port)}\"";

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
		string rule = $"advfirewall firewall add rule name=\"{RuleName(port)}\" dir=in action=allow protocol=UDP localport={port} enable=yes profile=any";
		return string.IsNullOrWhiteSpace(executablePath) ? rule : $"{rule} program=\"{executablePath}\"";
	}

	/// <summary>The command that removes an allowance this build granted, used when a port is reopened and by the harness.</summary>
	public static string BuildDeleteCommand(int port) => $"advfirewall firewall delete rule name=\"{RuleName(port)}\"";

	/// <summary>
	///     The PowerShell command that reports the allowance's state as a single word, so a Windows installed in a language
	///     this build does not know answers the same way netsh would not.
	/// </summary>
	/// <remarks>
	///     The query is assembled by concatenation rather than by formatting on purpose: it carries PowerShell script
	///     blocks, and a format string reads their braces as placeholders, which fails before PowerShell is ever started.
	/// </remarks>
	/// <param name="port">The port the allowance covers.</param>
	/// <returns>The command to run, which needs no elevation.</returns>
	public static string BuildPowerShellStateQuery(int port)
	{
		string quotedRuleName = RuleName(port).Replace("'", "''");

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
