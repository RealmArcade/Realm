using System;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Accounts;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>
///     Owns the one Steamworks context this process ever has. Every account feature works without it, so a build that is not
///     launched by Steam, a machine without the Steam client and a development run that has no native library all continue
///     with <see cref="IsAvailable" /> false instead of failing. While it is false, Steam is not offered as a sign-in provider at
///     all, because a ticket minted by this process is the only identity the authority can verify, and there is no way to mint
///     one without the client. The process never relaunches itself into one fixed application: a single build is shipped as both
///     the demo and the base game, so asking Steam to restart it under either id would send the other product's install to the
///     wrong store page. It initialises against whichever application Steam launched it as instead.
/// </summary>
public class SteamService
{
	private const string NativeLibraryName = "steam_api64";

	private static readonly TimeSpan TicketTimeout = TimeSpan.FromSeconds(15);

	private bool _initialized;

	private bool _stopped;

	private Callback<GetTicketForWebApiResponse_t>? _ticketResponseCallback;

	private HAuthTicket _pendingTicket = HAuthTicket.Invalid;

	private TaskCompletionSource<string>? _pendingTicketResult;

	public bool IsAvailable => _initialized;

	public bool CanOpenStorePage => _initialized && SteamApplicationIds.FullGameAppId != 0;

	/// <summary>
	///     The Steam application this process was launched as, or <c>0</c> when Steamworks is not running. A ticket is minted
	///     for exactly this application, so it is also the id the authority has to accept.
	/// </summary>
	public uint AppId => _initialized ? SteamUtils.GetAppID().m_AppId : 0;

	public void Start()
	{
		if (_initialized || _stopped) return;

		try
		{
			if (!SteamAPI.Init())
			{
				GD.Print("[Steam] the Steam client did not accept this process, so the game continues without Steamworks");
				return;
			}

			_initialized = true;
			GD.Print($"[Steam] running as application {SteamUtils.GetAppID().m_AppId}");
		}
		catch (Exception exception) when (IsMissingNativeLibrary(exception))
		{
			GD.Print($"[Steam] {NativeLibraryName} is not shipped with this build, so Steamworks stays off and Steam is not offered as a sign-in provider");
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] Steamworks is not usable in this build: {exception.Message}");
		}
	}

	/// <summary>
	///     Whether this process failed because the native library is not there, which is an ordinary property of a build that does
	///     not ship Steamworks rather than something that went wrong. The check runs on the call itself instead of on a probe of its
	///     own, because only the loader that the P/Invoke uses knows where it looks.
	/// </summary>
	private static bool IsMissingNativeLibrary(Exception exception)
	{
		return exception is DllNotFoundException
		       || (exception is TypeInitializationException && exception.InnerException is DllNotFoundException);
	}

	public void Pump()
	{
		if (!_initialized || _stopped) return;

		try
		{
			SteamAPI.RunCallbacks();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] callbacks stopped: {exception.Message}");
			_initialized = false;
			CancelPendingTicket(string.Empty);
		}
	}

	/// <summary>
	///     Mints a single-use auth session ticket for the authority to exchange for this player's SteamID64, in the hex form the
	///     Steam Web API accepts. The ticket arrives through a Steamworks callback that <see cref="Pump" /> delivers, so this must
	///     be awaited from a frame that keeps pumping; an empty answer means Steamworks could not mint one, and the player still
	///     has an unverified name to play under.
	/// </summary>
	public async Task<string> RequestWebApiTicketAsync()
	{
		if (!_initialized) return string.Empty;

		_ticketResponseCallback ??= new Callback<GetTicketForWebApiResponse_t>(OnTicketResponse);
		CancelPendingTicket(string.Empty);

		var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pendingTicketResult = pending;

		HAuthTicket ticket;
		try
		{
			ticket = SteamUser.GetAuthTicketForWebApi(SteamAuthTicket.Identity);
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] a sign-in ticket could not be requested: {exception.Message}");
			_pendingTicketResult = null;
			return string.Empty;
		}

		if (ticket == HAuthTicket.Invalid)
		{
			_pendingTicketResult = null;
			return string.Empty;
		}

		_pendingTicket = ticket;

		Task finished = await Task.WhenAny(pending.Task, Task.Delay(TicketTimeout)).ConfigureAwait(true);
		if (finished != pending.Task)
		{
			CancelPendingTicket(string.Empty);
			return string.Empty;
		}

		return await pending.Task.ConfigureAwait(true);
	}

	private void OnTicketResponse(GetTicketForWebApiResponse_t response)
	{
		TaskCompletionSource<string>? pending = _pendingTicketResult;
		if (pending == null) return;
		if (response.m_hAuthTicket != _pendingTicket) return;

		_pendingTicketResult = null;

		if (response.m_eResult != EResult.k_EResultOK || response.m_cubTicket <= 0)
		{
			pending.TrySetResult(string.Empty);
			return;
		}

		pending.TrySetResult(Convert.ToHexString(response.m_rgubTicket, 0, response.m_cubTicket).ToLowerInvariant());
	}

	private void CancelPendingTicket(string result)
	{
		TaskCompletionSource<string>? pending = _pendingTicketResult;
		_pendingTicketResult = null;

		if (_pendingTicket != HAuthTicket.Invalid)
		{
			try
			{
				SteamUser.CancelAuthTicket(_pendingTicket);
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[Steam] a sign-in ticket could not be cancelled: {exception.Message}");
			}

			_pendingTicket = HAuthTicket.Invalid;
		}

		pending?.TrySetResult(result);
	}

	public void Stop()
	{
		if (!_initialized || _stopped) return;

		_stopped = true;
		CancelPendingTicket(string.Empty);

		try
		{
			SteamAPI.Shutdown();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] shutdown failed: {exception.Message}");
		}

		_initialized = false;
	}

	public string GetPersonaName()
	{
		if (!_initialized) return string.Empty;

		try
		{
			return SteamFriends.GetPersonaName();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] the persona name could not be read: {exception.Message}");
			return string.Empty;
		}
	}

	public bool OpenFullGameStorePage()
	{
		if (!CanOpenStorePage) return false;

		try
		{
			SteamFriends.ActivateGameOverlayToStore(new AppId_t(SteamApplicationIds.FullGameAppId), EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
			return true;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[Steam] the store overlay could not be opened: {exception.Message}");
			return false;
		}
	}
}
