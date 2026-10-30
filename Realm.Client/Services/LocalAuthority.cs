using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public readonly record struct LocalAuthorityReply(bool Started, string Failure, int Port, string MapHash, string WireTableHash, string LogPath, string ConsoleLogPath)
{
	public static LocalAuthorityReply Failed(string failure, string logPath) => new(false, failure, 0, string.Empty, string.Empty, logPath, logPath.Length == 0 ? string.Empty : ServerConsoleCapture.ResolvePath(logPath));
}

/// <summary>
///     The authority this screen owns. It is brought up once, while the game is starting, and stays up for the whole
///     session: every match this screen hosts is put on it, and leaving a match or joining somebody else's lobby pauses it
///     rather than ending the process that holds it.
/// </summary>
public class LocalAuthority : IDisposable
{
	public const string LogFileName = "local_authority.listen_server.log";

	private readonly SemaphoreSlim _launchGate = new(1, 1);

	private ResidentAuthority? _resident;
	private ServerLaunchTarget? _target;
	private string _logPath = string.Empty;
	private string _dataFolderPath = string.Empty;
	private bool _disposed;

	public bool IsRunning => Resident is { IsRunning: true };

	public int ProcessId => Resident?.ProcessId ?? 0;

	public string LogPath => Resident?.LogPath ?? _logPath;

	public string LastLaunchFailure { get; private set; } = string.Empty;

	private ResidentAuthority? Resident => Volatile.Read(ref _resident);

	/// <summary>
	///     Names where the authority's own log belongs and the target it is started from. A screen configures it once, from
	///     its main thread, and then asks for it to come up while it is still in its menus.
	/// </summary>
	public void Configure(ServerLaunchTarget target, string logDirectory, string dataFolderPath)
	{
		_target = target;
		_logPath = Path.Combine(logDirectory, LogFileName);
		_dataFolderPath = dataFolderPath;
	}

	public async Task<bool> EnsureStartedAsync()
	{
		if (IsRunning) return true;

		await _launchGate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (IsRunning) return true;
			if (_disposed) return false;

			ServerLaunchTarget? target = _target;
			if (target == null)
			{
				LastLaunchFailure = "this build has no authority to run its matches";
				return false;
			}

			ResidentAuthority? previous = Resident;
			if (previous != null)
			{
				Volatile.Write(ref _resident, null);
				previous.Dispose();
			}

			var request = new ResidentAuthorityLaunchRequest(target, _logPath, _dataFolderPath);
			ResidentAuthorityLaunchAttempt attempt = await ResidentAuthority.LaunchAsync(request).ConfigureAwait(false);
			if (!attempt.Started || attempt.Authority == null)
			{
				LastLaunchFailure = attempt.Failure;
				return false;
			}

			LastLaunchFailure = string.Empty;
			Volatile.Write(ref _resident, attempt.Authority);
			return true;
		}
		finally
		{
			_launchGate.Release();
		}
	}

	/// <summary>
	///     Puts a match on the authority. Whatever it was holding is given up first, because one authority holds one match
	///     and a held match is still sitting on the port the next one may ask for.
	/// </summary>
	public async Task<LocalAuthorityReply> StartMatchAsync(ServerControlCommand command)
	{
		string logPath = LogPath;

		if (!await EnsureStartedAsync().ConfigureAwait(false))
		{
			return LocalAuthorityReply.Failed(LastLaunchFailure, logPath);
		}

		ResidentAuthority? resident = Resident;
		if (resident == null) return LocalAuthorityReply.Failed("this build has no authority to run its matches", logPath);

		ServerControlReply reply = await resident.SendAsync(command).ConfigureAwait(false);
		if (!reply.Accepted) return LocalAuthorityReply.Failed(reply.Failure, resident.LogPath);

		return new LocalAuthorityReply(true, string.Empty, reply.Port, reply.MapHash, reply.WireTableHash, resident.LogPath, resident.ConsoleLogPath);
	}

	/// <summary>
	///     Parks the authority. The match it holds is given up, and with it the socket that match was listening on, so a
	///     screen that leaves a hosted lobby can reach somebody else's on the port it hosted with. The process stays up,
	///     which is the whole point of it: nothing is started or stopped between sessions.
	/// </summary>
	public void Pause()
	{
		ResidentAuthority? resident = Resident;
		if (resident == null) return;

		_ = resident.SendAsync(new ServerControlCommand { Kind = ServerControlKind.Pause });
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		ResidentAuthority? resident = Resident;
		Volatile.Write(ref _resident, null);
		resident?.Dispose();
	}
}
