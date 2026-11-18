using System;
using Steamworks;

namespace Realm.ListenServerHost;

/// <summary>
///     The authority's own Steamworks context. The authority carries the host's Steam identity: it owns the Steam P2P listen
///     socket every remote player connects to, so it cannot host a Steam lobby without one. It initialises lazily and can be
///     retried, because the authority comes up with the screen and may be running before Steam is ready to answer.
///     <para>
///         Two processes of the same user may both initialise Steamworks — that is a documented, ordinary configuration, and
///         nothing here refuses a second process. What the authority never does is reach its own SteamID over Steam P2P: the
///         host's own seat keeps the loopback IPC it already has, and a self-addressed connection would pay a relay hop to
///         talk to itself.
///     </para>
/// </summary>
public sealed class SteamAuthorityContext : IDisposable
{
	private bool _initialized;

	private bool _disposed;

	/// <summary>Whether Steamworks is running in this process.</summary>
	public bool IsAvailable => _initialized;

	/// <summary>The application this process was launched as, or <c>0</c> while Steamworks is not running.</summary>
	public uint AppId { get; private set; }

	/// <summary>The SteamID64 the authority hosts as, or <c>0</c> while Steamworks is not running.</summary>
	public ulong SteamId { get; private set; }

	/// <summary>What Steamworks said when it last refused to start, for the log and for a support report.</summary>
	public string InitializationMessage { get; private set; } = string.Empty;

	/// <summary>
	///     Tries again to bring Steamworks up, which is what a hosting request does when the process came up before Steam was
	///     ready. It is a no-op once Steamworks is running.
	/// </summary>
	/// <returns>Whether Steamworks is running after the attempt.</returns>
	public bool TryStart()
	{
		if (_initialized || _disposed) return _initialized;

		try
		{
			ESteamAPIInitResult result = SteamAPI.InitEx(out string message);
			if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
			{
				InitializationMessage = string.IsNullOrWhiteSpace(message) ? result.ToString() : message;
				Console.Error.WriteLine($"[authority][steam] Steamworks stays off: {result} {InitializationMessage}");
				return false;
			}

			_initialized = true;
			AppId = SteamUtils.GetAppID().m_AppId;
			SteamId = SteamUser.GetSteamID().m_SteamID;
			InitializationMessage = string.Empty;

			Console.Out.WriteLine($"[authority][steam] running as application {AppId}, hosting as {SteamId}");
			Console.Out.Flush();
			return true;
		}
		catch (Exception exception)
		{
			InitializationMessage = exception.Message;
			Console.Error.WriteLine($"[authority][steam] Steamworks is not usable in this process: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Delivers the Steam callbacks that arrived since the last frame. Steamworks only progresses while this is called, so
	///     it runs on the authority's own main loop and never on a background thread.
	/// </summary>
	public void Pump()
	{
		if (!_initialized || _disposed) return;

		try
		{
			SteamAPI.RunCallbacks();
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"[authority][steam] callbacks stopped: {exception.Message}");
			_initialized = false;
			InitializationMessage = exception.Message;
		}
	}

	/// <summary>
	///     The line the authority log records so a support report states which Steam application and which identity the match
	///     was hosted under, or that it had none.
	/// </summary>
	/// <returns>A one-line description of this process's Steam state.</returns>
	public string Describe()
	{
		if (!_initialized)
		{
			return string.IsNullOrEmpty(InitializationMessage)
				? "no Steam identity: Steamworks is not running in this process"
				: $"no Steam identity: {InitializationMessage}";
		}

		return $"Steam application {AppId}, hosting as {SteamId}";
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (!_initialized) return;

		try
		{
			SteamAPI.Shutdown();
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"[authority][steam] shutdown failed: {exception.Message}");
		}

		_initialized = false;
	}
}
