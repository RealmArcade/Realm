using System;
using System.Collections.Generic;
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

	private const string GameLobbyIdMetadataKey = "realm_game_lobby_id";

	/// <summary>
	///     The overlay dialog Steam opens for a user it should add as a friend. The name is Steamworks' own spelling of the
	///     dialog, and it is the only way a game can put a request in front of the player who has to confirm it.
	/// </summary>
	private const string FriendDialogAddFriend = "friendadd";

	private static readonly TimeSpan TicketTimeout = TimeSpan.FromSeconds(15);

	private static readonly TimeSpan InviteLobbyTimeout = TimeSpan.FromSeconds(10);

	private bool _initialized;

	private bool _stopped;

	private Callback<GetTicketForWebApiResponse_t>? _ticketResponseCallback;

	private CallResult<LobbyCreated_t>? _lobbyCreatedCallResult;

	private Callback<LobbyEnter_t>? _lobbyEnterCallback;

	private CallResult<LobbyEnter_t>? _lobbyEnterCallResult;

	private Callback<GameLobbyJoinRequested_t>? _gameLobbyJoinRequestedCallback;

	private HAuthTicket _pendingTicket = HAuthTicket.Invalid;

	private TaskCompletionSource<string>? _pendingTicketResult;

	private TaskCompletionSource<bool>? _inviteLobbyCreation;

	private ulong _inviteLobbySteamId;

	private string? _inviteLobbyGameLobbyId;

	private ulong _pendingJoinedLobbySteamId;

	private string _pendingInviterName = string.Empty;

	/// <summary>
	///     Raised on the frame Steam reports that a friend accepted an invite and handed this screen the lobby it describes.
	///     The screen joins the match lobby through the ordinary registry join, so the invite is only a shortcut to a lobby id
	///     and never a second, Steam-owned match.
	/// </summary>
	public event Action<SteamLobbyInvite>? LobbyInviteAccepted;

	public SteamUnavailableReason UnavailableReason { get; private set; } = SteamUnavailableReason.NotStarted;

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
			ESteamAPIInitResult result = SteamAPI.InitEx(out string message);
			if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
			{
				UnavailableReason = DescribeUnavailableReason(result);
				GameLog.LogToConsole($"[Steam] Steamworks stays off: {UnavailableReason} ({result}) {message}");
				return;
			}

			_initialized = true;
			UnavailableReason = SteamUnavailableReason.Available;
			RegisterLobbyCallbacks();
			GameLog.LogToConsole($"[Steam] running as application {SteamUtils.GetAppID().m_AppId}");
		}
		catch (Exception exception) when (IsMissingNativeLibrary(exception))
		{
			UnavailableReason = SteamUnavailableReason.NativeLibraryMissing;
			GameLog.LogToConsole($"[Steam] {NativeLibraryName} is not shipped with this build, so Steamworks stays off and Steam is not offered as a sign-in provider");
		}
		catch (Exception exception) when (IsMismatchedNativeLibrary(exception))
		{
			UnavailableReason = SteamUnavailableReason.NativeLibraryMismatch;
			GameLog.LogError($"[Steam] the {NativeLibraryName} shipped with this build does not match Steamworks.NET: {exception.Message}");
		}
		catch (Exception exception)
		{
			UnavailableReason = SteamUnavailableReason.InitializationFailed;
			GameLog.LogError($"[Steam] Steamworks is not usable in this build: {exception.Message}");
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

	private static bool IsMismatchedNativeLibrary(Exception exception)
	{
		return exception is EntryPointNotFoundException or BadImageFormatException
		       || (exception is TypeInitializationException && exception.InnerException is EntryPointNotFoundException or BadImageFormatException);
	}

	private static SteamUnavailableReason DescribeUnavailableReason(ESteamAPIInitResult result)
	{
		return result switch
		{
			ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient => SteamUnavailableReason.SteamClientNotRunning,
			ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch => SteamUnavailableReason.NativeLibraryMismatch,
			ESteamAPIInitResult.k_ESteamAPIInitResult_OK => SteamUnavailableReason.Available,
			_ => SteamUnavailableReason.SteamRejectedThisProcess
		};
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
			GameLog.LogError($"[Steam] callbacks stopped: {exception.Message}");
			_initialized = false;
			UnavailableReason = SteamUnavailableReason.InitializationFailed;
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
			GameLog.LogError($"[Steam] a sign-in ticket could not be requested: {exception.Message}");
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
				GameLog.LogError($"[Steam] a sign-in ticket could not be cancelled: {exception.Message}");
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
			GameLog.LogError($"[Steam] shutdown failed: {exception.Message}");
		}

		_initialized = false;
	}

	private void RegisterLobbyCallbacks()
	{
		_lobbyEnterCallback ??= new Callback<LobbyEnter_t>(entered => HandleLobbyEnter(entered));
		_gameLobbyJoinRequestedCallback ??= new Callback<GameLobbyJoinRequested_t>(OnGameLobbyJoinRequested);
	}

	/// <summary>
	///     The Steam friends this player can invite, with the ones already in Realm first, then the rest of the online
	///     friends, then the offline ones. Each entry carries the Steam id an invite is addressed to, so the invite screen
	///     never has to name a friend by anything but the name Steam shows.
	/// </summary>
	public IReadOnlyList<SteamFriend> GetFriends()
	{
		if (!_initialized) return Array.Empty<SteamFriend>();

		var friends = new List<SteamFriend>();

		try
		{
			int friendCount = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
			for (int index = 0; index < friendCount; index++)
			{
				CSteamID friendId = SteamFriends.GetFriendByIndex(index, EFriendFlags.k_EFriendFlagImmediate);
				if (friendId.m_SteamID == 0) continue;

				string personaName = SteamFriends.GetFriendPersonaName(friendId);
				if (string.IsNullOrWhiteSpace(personaName)) continue;

				bool isOnline = SteamFriends.GetFriendPersonaState(friendId) != EPersonaState.k_EPersonaStateOffline;
				friends.Add(new SteamFriend(friendId.m_SteamID, personaName, isOnline, IsFriendInRealm(friendId)));
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the friends list could not be read: {exception.Message}");
			return Array.Empty<SteamFriend>();
		}

		friends.Sort(static (first, second) =>
		{
			int firstRank = Rank(first);
			int secondRank = Rank(second);
			return firstRank != secondRank ? firstRank.CompareTo(secondRank) : string.Compare(first.PersonaName, second.PersonaName, StringComparison.OrdinalIgnoreCase);
		});

		return friends;

		static int Rank(SteamFriend friend)
		{
			if (friend.IsPlayingRealm) return 0;
			return friend.IsOnline ? 1 : 2;
		}
	}

	private bool IsFriendInRealm(CSteamID friendId)
	{
		try
		{
			if (!SteamFriends.GetFriendGamePlayed(friendId, out FriendGameInfo_t gameInfo)) return false;
			return gameInfo.m_gameID.AppID() == SteamUtils.GetAppID();
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	///     Prepares the Steam lobby an invite is sent through and stamps it with the match lobby it stands for. The Steam
	///     lobby carries no match state: it exists only so Steam can deliver an invitation whose acceptance leads back to a
	///     lobby id this game can join, which is why every member of the match lobby can hold one of its own.
	/// </summary>
	public async Task<bool> OpenInviteLobbyAsync(string gameLobbyId, int maximumMembers)
	{
		if (!_initialized) return false;
		if (string.IsNullOrEmpty(gameLobbyId)) return false;
		if (_inviteLobbySteamId != 0 && string.Equals(_inviteLobbyGameLobbyId, gameLobbyId, StringComparison.Ordinal)) return true;

		TaskCompletionSource<bool>? creationInFlight = _inviteLobbyCreation;
		if (creationInFlight != null && string.Equals(_inviteLobbyGameLobbyId, gameLobbyId, StringComparison.Ordinal))
		{
			Task ready = await Task.WhenAny(creationInFlight.Task, Task.Delay(InviteLobbyTimeout)).ConfigureAwait(true);
			if (ready == creationInFlight.Task) return await creationInFlight.Task.ConfigureAwait(true);
		}

		CloseInviteLobby();

		_inviteLobbyGameLobbyId = gameLobbyId;
		var creation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_inviteLobbyCreation = creation;

		try
		{
			SteamAPICall_t createCall = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, Math.Clamp(maximumMembers, 2, 250));
			_lobbyCreatedCallResult?.Dispose();
			_lobbyCreatedCallResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
			_lobbyCreatedCallResult.Set(createCall);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] an invite lobby could not be requested: {exception.Message}");
			_inviteLobbyCreation = null;
			_inviteLobbyGameLobbyId = null;
			return false;
		}

		Task finished = await Task.WhenAny(creation.Task, Task.Delay(InviteLobbyTimeout)).ConfigureAwait(true);
		if (finished != creation.Task)
		{
			_inviteLobbyCreation = null;
			_inviteLobbyGameLobbyId = null;
			return false;
		}

		return await creation.Task.ConfigureAwait(true);
	}

	/// <summary>
	///     Asks Steam to invite each of these friends to the prepared invite lobby, and answers how many invitations Steam
	///     accepted. Steam shows each friend the invitation itself, so their accept leads them straight into this match.
	/// </summary>
	public int InviteFriends(IReadOnlyList<ulong> friendSteamIds)
	{
		if (!_initialized || _inviteLobbySteamId == 0 || friendSteamIds == null) return 0;

		var inviteLobbyId = new CSteamID(_inviteLobbySteamId);
		int sent = 0;

		foreach (ulong friendSteamId in friendSteamIds)
		{
			if (friendSteamId == 0) continue;

			try
			{
				if (SteamMatchmaking.InviteUserToLobby(inviteLobbyId, new CSteamID(friendSteamId)))
				{
					sent++;
				}
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] an invitation could not be sent: {exception.Message}");
			}
		}

		GameLog.LogToConsole($"[Steam] invited {sent} of {friendSteamIds.Count} selected friend(s)");
		return sent;
	}

	/// <summary>
	///     Gives up the invite lobby this screen was holding, which makes every invitation sent through it stop working.
	///     A match lobby that other players are still in is unaffected, because their own invites stand on their own lobbies.
	/// </summary>
	public void CloseInviteLobby()
	{
		_inviteLobbyCreation = null;
		_inviteLobbyGameLobbyId = null;

		LeaveInviteLobby(_inviteLobbySteamId);
		_inviteLobbySteamId = 0;
	}

	private void OnLobbyCreated(LobbyCreated_t created, bool ioFailure)
	{
		TaskCompletionSource<bool>? creation = _inviteLobbyCreation;
		_inviteLobbyCreation = null;

		string? gameLobbyId = _inviteLobbyGameLobbyId;
		bool isWanted = !ioFailure && created.m_eResult == EResult.k_EResultOK && !string.IsNullOrEmpty(gameLobbyId);

		if (!isWanted)
		{
			_inviteLobbyGameLobbyId = null;

			if (!ioFailure && created.m_eResult == EResult.k_EResultOK && created.m_ulSteamIDLobby != 0)
			{
				LeaveInviteLobby(created.m_ulSteamIDLobby);
			}

			creation?.TrySetResult(false);
			return;
		}

		_inviteLobbySteamId = created.m_ulSteamIDLobby;

		try
		{
			SteamMatchmaking.SetLobbyData(new CSteamID(_inviteLobbySteamId), GameLobbyIdMetadataKey, gameLobbyId);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the invite lobby could not be described: {exception.Message}");
		}

		GameLog.LogToConsole($"[Steam] invite lobby {_inviteLobbySteamId} stands for match lobby {gameLobbyId}");
		creation?.TrySetResult(_inviteLobbySteamId != 0);
	}

	private void LeaveInviteLobby(ulong inviteLobbySteamId)
	{
		if (inviteLobbySteamId == 0) return;

		try
		{
			SteamMatchmaking.LeaveLobby(new CSteamID(inviteLobbySteamId));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the invite lobby could not be left: {exception.Message}");
		}
	}

	private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t requested)
	{
		if (requested.m_steamIDLobby.m_SteamID == 0) return;

		_pendingJoinedLobbySteamId = requested.m_steamIDLobby.m_SteamID;

		try
		{
			_pendingInviterName = SteamFriends.GetFriendPersonaName(requested.m_steamIDFriend);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the inviter's name could not be read: {exception.Message}");
			_pendingInviterName = string.Empty;
		}

		try
		{
			SteamAPICall_t joinCall = SteamMatchmaking.JoinLobby(requested.m_steamIDLobby);
			_lobbyEnterCallResult?.Dispose();
			_lobbyEnterCallResult = CallResult<LobbyEnter_t>.Create((entered, _) => HandleLobbyEnter(entered));
			_lobbyEnterCallResult.Set(joinCall);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the invited lobby could not be joined: {exception.Message}");
			_pendingJoinedLobbySteamId = 0;
		}
	}

	private void HandleLobbyEnter(LobbyEnter_t entered)
	{
		if (_pendingJoinedLobbySteamId == 0 || entered.m_ulSteamIDLobby != _pendingJoinedLobbySteamId) return;

		ulong joinedLobbySteamId = _pendingJoinedLobbySteamId;
		_pendingJoinedLobbySteamId = 0;

		string inviterName = _pendingInviterName;
		_pendingInviterName = string.Empty;

		if ((int)entered.m_EChatRoomEnterResponse != (int)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
		{
			GameLog.LogError("[Steam] the invited lobby refused the entry, so the invitation names no lobby this screen can join");
			return;
		}

		string gameLobbyId = string.Empty;

		try
		{
			gameLobbyId = SteamMatchmaking.GetLobbyData(new CSteamID(joinedLobbySteamId), GameLobbyIdMetadataKey);
			SteamMatchmaking.LeaveLobby(new CSteamID(joinedLobbySteamId));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the invited lobby could not be read: {exception.Message}");
		}

		if (string.IsNullOrEmpty(gameLobbyId))
		{
			GameLog.LogError("[Steam] the invitation describes no match lobby, so there is nothing to join");
			return;
		}

		GameLog.LogToConsole($"[Steam] an invitation was accepted for match lobby {gameLobbyId}");
		LobbyInviteAccepted?.Invoke(new SteamLobbyInvite(gameLobbyId, inviterName));
	}

	/// <summary>
	///     Why Steam is not usable on this machine, written for the player, or an empty string while it is usable. Every
	///     screen that offers anything Steam-only states the same reason, so the player never has to guess which part of
	///     their setup is at fault.
	/// </summary>
	public string UnavailableExplanation
	{
		get
		{
			if (_initialized) return string.Empty;

			return UnavailableReason switch
			{
				SteamUnavailableReason.SteamClientNotRunning => TranslationServer.Translate("Steam is not running, so Realm cannot use your Steam account or invite friends. Start the Steam client and reopen the game."),
				SteamUnavailableReason.NativeLibraryMissing => TranslationServer.Translate("This build does not include the Steamworks library, so Steam features are unavailable. Start a Steam build."),
				SteamUnavailableReason.NativeLibraryMismatch => TranslationServer.Translate("The Steamworks library in this build does not match the game, so Steam features are unavailable. Start a current Steam build."),
				SteamUnavailableReason.SteamRejectedThisProcess => TranslationServer.Translate("Steam did not accept this copy of the game, so Steam features are unavailable. Start the game from your Steam library."),
				_ => TranslationServer.Translate("Steamworks is not available in this build, so Steam features are unavailable. Start the game through Steam.")
			};
		}
	}

	/// <summary>
	///     The name Steam knows this player by, or an empty string when Steamworks is not running. It is the name a sign-in
	///     claims, because the ticket proves the Steam account and not a name typed on this machine.
	/// </summary>
	public string UserName
	{
		get
		{
			if (!_initialized) return string.Empty;

			try
			{
				return SteamFriends.GetPersonaName();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] the persona name could not be read: {exception.Message}");
				return string.Empty;
			}
		}
	}

	/// <summary>
	///     The SteamID64 this process plays as, or <c>0</c> while Steamworks is not running. It is the identity a seat states
	///     to the lobby so that the table at the end of a match can point Steam at the person behind it.
	/// </summary>
	public ulong SteamId
	{
		get
		{
			if (!_initialized) return 0;

			try
			{
				return SteamUser.GetSteamID().m_SteamID;
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] this player's Steam id could not be read: {exception.Message}");
				return 0;
			}
		}
	}

	/// <summary>
	///     Asks Steam to add a player as a friend, by opening Steam's own add-friend dialog for that SteamID64. Steam has no
	///     interface that sends a request silently - the request is the player's to confirm in Steam's own window - so this is
	///     the whole of what a game can do, and it carries the same meaning as any other game's "add friend" button.
	/// </summary>
	/// <param name="steamId">The SteamID64 of the player to add.</param>
	/// <returns>Whether Steam was asked, which is false when Steamworks is not running or the id names nobody.</returns>
	public bool OpenAddFriendDialog(ulong steamId)
	{
		if (!_initialized || steamId == 0) return false;

		ulong ownSteamId = SteamId;
		if (ownSteamId != 0 && ownSteamId == steamId) return false;

		try
		{
			if (!SteamUtils.IsOverlayEnabled())
			{
				GameLog.LogError("[Steam] the Steam overlay is disabled for this game, so the add-friend dialog will not be shown");
			}

			SteamFriends.ActivateGameOverlayToUser(FriendDialogAddFriend, new CSteamID(steamId));
			GameLog.LogToConsole($"[Steam] asked Steam to add {steamId} as a friend");
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] Steam's add-friend dialog for {steamId} could not be opened: {exception.Message}");
			return false;
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
			GameLog.LogError($"[Steam] the store overlay could not be opened: {exception.Message}");
			return false;
		}
	}
}
