using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Network;
using Realm.Shared.Reachability;

namespace Realm.Client.Services;

/// <summary>
///     The platform half of asking Windows for the inbound allowance a lobby needs: it reads what the rule currently says
///     and runs the commands <see cref="WindowsFirewallRuleRequester" /> asks for, showing the elevation prompt the platform
///     demands for the ones that change the machine. What it never does is remember a refusal, so a host who dismissed the
///     prompt once is asked again the next time they host.
/// </summary>
public class WindowsFirewallService : IWindowsFirewallCommandRunner
{
	private const int ReadCommandTimeoutMilliseconds = 8000;

	private const int AddCommandTimeoutMilliseconds = 120_000;

	private const int ElevationCancelledErrorCode = 1223;

	private const string RuleStateQuery = "$r = Get-NetFirewallRule -DisplayName '{0}' -ErrorAction SilentlyContinue; " +
	                                       "if (-not $r) { 'absent' } elseif ($r.Action -eq 'Block') { 'blocked' } elseif ($r.Enabled -ne 'True') { 'disabled' } else { 'allowed' }";

	private readonly WindowsFirewallRuleRequester _requester;

	public WindowsFirewallService()
	{
		_requester = new WindowsFirewallRuleRequester(this);
	}

	public Task<WindowsFirewallGrantResult> RequestInboundUdpForPortAsync(int port)
	{
		return RequestAndLogAsync(port);
	}

	/// <summary>
	///     The program the allowance names, so the rule covers the process that actually holds the socket rather than every
	///     program on the machine. A build that runs its authority as a project has no separate executable to name.
	/// </summary>
	public string AuthorityExecutablePath
	{
		get
		{
			if (!ListenServerTarget.TryResolve(out ServerLaunchTarget? target, out _) || target == null) return string.Empty;
			if (target.IsProject) return string.Empty;
			return target.ExecutablePath;
		}
	}

	/// <summary>
	///     Reads the rule through PowerShell, whose answers are names rather than sentences, so a Windows installed in any
	///     language answers the same way. netsh is the fallback for a machine where PowerShell cannot be run at all.
	/// </summary>
	public async Task<WindowsFirewallRuleState> ReadRuleStateAsync(int port, CancellationToken cancellationToken = default)
	{
		string query = string.Format(RuleStateQuery, WindowsFirewallRules.RuleName(port).Replace("'", "''"));
		string token = await RunAsync("powershell", $"-NoProfile -NonInteractive -Command \"{query}\"", ReadCommandTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);

		WindowsFirewallRuleState fromPowerShell = WindowsFirewallRules.ParseStateToken(token);
		if (fromPowerShell != WindowsFirewallRuleState.Unknown) return fromPowerShell;

		string show = await RunAsync("netsh", WindowsFirewallRules.BuildShowCommand(port), ReadCommandTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
		return WindowsFirewallRules.ParseShowOutput(show);
	}

	/// <summary>
	///     Runs a command that changes the machine's firewall, which the platform refuses without elevation. ShellExecute with
	///     the runas verb is what shows the prompt, and a dismissed prompt is an ordinary outcome rather than an error.
	/// </summary>
	public async Task<WindowsFirewallGrant> RunElevatedAsync(string arguments, CancellationToken cancellationToken = default)
	{
		try
		{
			var startInfo = new ProcessStartInfo("netsh")
			{
				Arguments = arguments,
				UseShellExecute = true,
				Verb = "runas",
				WindowStyle = ProcessWindowStyle.Hidden
			};

			using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("'netsh' did not start");
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(AddCommandTimeoutMilliseconds);
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
			return process.ExitCode == 0 ? WindowsFirewallGrant.Granted : WindowsFirewallGrant.Failed;
		}
		catch (Win32Exception exception) when (exception.NativeErrorCode == ElevationCancelledErrorCode)
		{
			return WindowsFirewallGrant.Declined;
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException)
		{
			GameLog.LogError($"[Firewall] 'netsh {arguments}' could not be run elevated: {exception.Message}");
			return WindowsFirewallGrant.Failed;
		}
	}

	private async Task<WindowsFirewallGrantResult> RequestAndLogAsync(int port)
	{
		GameLog.LogToConsole($"[Firewall] Checking the inbound allowance for UDP port {port}.");
		WindowsFirewallGrantResult result = await _requester.RequestInboundUdpForPortAsync(port);

		string message = result.Grant switch
		{
			WindowsFirewallGrant.AlreadyOpen => $"UDP port {port} is already allowed in by '{WindowsFirewallRules.RuleName(port)}'.",
			WindowsFirewallGrant.Granted => $"UDP port {port} is now allowed in.",
			WindowsFirewallGrant.Declined => $"The elevation prompt for UDP port {port} was dismissed, so the port stays closed until the next attempt to host asks again.",
			WindowsFirewallGrant.NotApplicable => "This platform has no Windows firewall rule to add.",
			_ => $"The inbound allowance for UDP port {port} could not be granted: {result.Detail}"
		};

		if (result.Grant is WindowsFirewallGrant.Granted or WindowsFirewallGrant.AlreadyOpen or WindowsFirewallGrant.NotApplicable) GameLog.LogToConsole($"[Firewall] {message}");
		else GameLog.LogError($"[Firewall] {message}");

		return result;
	}

	private static async Task<string> RunAsync(string fileName, string arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
	{
		try
		{
			var startInfo = new ProcessStartInfo(fileName)
			{
				Arguments = arguments,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"'{fileName}' did not start");
			Task<string> readOutput = process.StandardOutput.ReadToEndAsync();
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(timeoutMilliseconds);
			await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
			return await readOutput.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or OperationCanceledException or System.IO.IOException)
		{
			GameLog.LogToConsole($"[Firewall] '{fileName} {arguments}' could not be read: {exception.Message}");
			return string.Empty;
		}
	}
}
