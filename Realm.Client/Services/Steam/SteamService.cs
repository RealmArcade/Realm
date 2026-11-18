using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Accounts;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>
///     Owns the one Steamworks context this process ever has, and is the single identity a player has: the SteamID64, the
///     persona name, the avatar and the AppID all come from here and nowhere else. Steam is the platform whenever the game
///     runs, so no account feature is written to survive without it. A machine with no usable Steam client is a local-only
///     installation: single-player, the editor, local maps and <c>.rmap</c> import still work, and every Steam-backed feature
///     is absent rather than present and failing. There is no guest identity and no display name typed on this machine,
///     because a name nothing verified would be the first step back to a second identity model. The process never relaunches
///     itself into one fixed application: a single build is shipped as both the demo and the base game, so asking Steam to
///     restart it under either id would send the other product's install to the wrong store page. It initialises against
///     whichever application Steam launched it as instead.
/// </summary>
public partial class SteamService
{
	private const string NativeLibraryName = "steam_api64";

	/// <summary>
	///     The overlay dialog Steam opens for a user it should add as a friend. The name is Steamworks' own spelling of the
	///     dialog, and it is the only way a game can put a request in front of the player who has to confirm it.
	/// </summary>
	private const string FriendDialogAddFriend = "friendadd";


	private bool _initialized;

	private bool _stopped;

	private string _initializationMessage = string.Empty;

	private Texture2D? _avatarTexture;

	private ulong _avatarTextureSteamId;

	/// <summary>Avatars read for other people this session, which is a session cache and never a stored profile.</summary>
	private readonly Dictionary<ulong, Texture2D?> _personaAvatars = new();



	private Callback<LobbyEnter_t>? _lobbyEnterCallback;

	private CallResult<LobbyEnter_t>? _lobbyEnterCallResult;

	private Callback<GameLobbyJoinRequested_t>? _gameLobbyJoinRequestedCallback;






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

	/// <summary>
	///     Whether the player chose to continue without Steam at startup. It is the only thing that tells a local-only launch
	///     apart from one that has not been asked yet, so a screen that offers Steam-backed actions can say why they are gone
	///     instead of looking like a build that is still starting.
	/// </summary>
	public bool StartedWithoutSteam { get; private set; }

	/// <summary>
	///     What Steamworks said when it refused to start, verbatim and in English. The translated explanation is what the player
	///     reads; this is what a support report needs, because a bare boolean makes every failure look the same.
	/// </summary>
	public string InitializationMessage => _initializationMessage;

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
				_initializationMessage = string.IsNullOrWhiteSpace(message) ? result.ToString() : message;
				GameLog.LogToConsole($"[Steam] Steamworks stays off: {UnavailableReason} ({result}) {_initializationMessage}");
				return;
			}

			_initialized = true;
			UnavailableReason = SteamUnavailableReason.Available;
			_initializationMessage = string.Empty;
			StartedWithoutSteam = false;
			RegisterLobbyCallbacks();
			RequestLifetimeStats();
			GameLog.LogToConsole($"[Steam] running as application {SteamUtils.GetAppID().m_AppId}, player {SteamUser.GetSteamID().m_SteamID}");
		}
		catch (Exception exception) when (IsMissingNativeLibrary(exception))
		{
			UnavailableReason = SteamUnavailableReason.NativeLibraryMissing;
			_initializationMessage = exception.Message;
			GameLog.LogToConsole($"[Steam] {NativeLibraryName} is not shipped with this build, so Steamworks stays off and this installation can only play locally");
		}
		catch (Exception exception) when (IsMismatchedNativeLibrary(exception))
		{
			UnavailableReason = SteamUnavailableReason.NativeLibraryMismatch;
			_initializationMessage = exception.Message;
			GameLog.LogError($"[Steam] the {NativeLibraryName} shipped with this build does not match Steamworks.NET: {exception.Message}");
		}
		catch (Exception exception)
		{
			UnavailableReason = SteamUnavailableReason.InitializationFailed;
			_initializationMessage = exception.Message;
			GameLog.LogError($"[Steam] Steamworks is not usable in this build: {exception.Message}");
		}
	}

	/// <summary>
	///     Tries Steamworks again after a start that could not reach it, which is what the startup choice and the persistent
	///     no-Steam marker offer. It is a no-op once Steamworks is running or was deliberately given up, so a screen cannot use
	///     it to bring a shut-down context back.
	/// </summary>
	/// <returns>Whether Steamworks is running after the attempt.</returns>
	public bool Retry()
	{
		if (_stopped || _initialized) return _initialized;

		Start();
		return _initialized;
	}

	/// <summary>
	///     Records that the player chose to continue without Steam. Nothing about the process changes: it is a state the screens
	///     read so they can hide what cannot work instead of offering it.
	/// </summary>
	public void ContinueWithoutSteam()
	{
		if (_initialized) return;
		StartedWithoutSteam = true;
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
		}
	}

	public void Stop()
	{
		if (!_initialized || _stopped) return;

		_stopped = true;
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

		// The Steam lobby is the match lobby, so the invitation already names the one thing a join needs. This screen is
		// only a member of it until the lobby manager takes the room over: leaving here would make Steam tell every other
		// member that this player arrived and left again.
		string lobbyId = joinedLobbySteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);

		GameLog.LogToConsole($"[Steam] an invitation was accepted for Steam lobby {lobbyId}");
		LobbyInviteAccepted?.Invoke(new SteamLobbyInvite(lobbyId, inviterName));
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
	///     The Steam avatar of the player this process is signed in as, as a texture ready to draw, or <c>null</c> while
	///     Steamworks is not running or Steam has not produced one yet. The image is held on this service because Steam hands
	///     out a handle rather than bytes: the texture is built once from that handle and given back to every screen that shows
	///     who the player is.
	/// </summary>
	/// <returns>The player's Steam avatar, or <c>null</c> when there is no Steam identity to draw.</returns>
	public Texture2D? PlayerAvatar
	{
		get
		{
			if (!_initialized) return null;

			ulong steamId = SteamId;
			if (steamId == 0) return null;
			if (_avatarTexture != null && _avatarTextureSteamId == steamId) return _avatarTexture;

			try
			{
				int avatarHandle = SteamFriends.GetLargeFriendAvatar(new CSteamID(steamId));
				if (avatarHandle <= 0) return _avatarTexture;
				if (!SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height) || width == 0 || height == 0) return _avatarTexture;

				var pixels = new byte[width * height * 4];
				if (!SteamUtils.GetImageRGBA(avatarHandle, pixels, pixels.Length)) return _avatarTexture;

				var image = Image.CreateFromData((int)width, (int)height, false, Image.Format.Rgba8, pixels);
				_avatarTexture = ImageTexture.CreateFromImage(image);
				_avatarTextureSteamId = steamId;
				return _avatarTexture;
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] this player's avatar could not be read: {exception.Message}");
				return _avatarTexture;
			}
		}
	}

	/// <summary>
	///     The Steam avatar of any player Steam knows, which is what a creator row draws beside the author's name. Steam
	///     hands out a handle rather than bytes, and a texture built from a handle Steam later releases goes stale, so the
	///     images are cached per identity and never written to disk: a name or an avatar is a profile Steam owns and the game
	///     only reads.
	/// </summary>
	/// <param name="steamId">The SteamID64 whose avatar to read.</param>
	/// <returns>The avatar as a texture, or null when Steam has not produced one.</returns>
	public Texture2D? GetAvatar(ulong steamId)
	{
		if (!_initialized || steamId == 0) return null;
		if (_personaAvatars.TryGetValue(steamId, out Texture2D? cachedAvatar)) return cachedAvatar;

		try
		{
			int avatarHandle = SteamFriends.GetLargeFriendAvatar(new CSteamID(steamId));
			if (avatarHandle <= 0) return null;
			if (!SteamUtils.GetImageSize(avatarHandle, out uint width, out uint height) || width == 0 || height == 0) return null;

			var pixels = new byte[width * height * 4];
			if (!SteamUtils.GetImageRGBA(avatarHandle, pixels, pixels.Length)) return null;

			var image = Image.CreateFromData((int)width, (int)height, false, Image.Format.Rgba8, pixels);
			var texture = ImageTexture.CreateFromImage(image);
			_personaAvatars[steamId] = texture;
			return texture;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the avatar for {steamId} could not be read: {exception.Message}");
			return null;
		}
	}

	/// <summary>
	///     Whether Steam already lists a person among this player's friends, which is what decides if a friend request is
	///     still worth a place in the game-over table: an add-friend dialog for somebody Steam has already accepted asks the
	///     player to do what is already done. A session without Steamworks has no friend list to read and answers nothing.
	/// </summary>
	/// <param name="steamId">The SteamID64 of the person to look up.</param>
	/// <returns>Whether Steam lists that person as this player's friend.</returns>
	public bool IsFriend(ulong steamId)
	{
		if (!_initialized || steamId == 0) return false;

		try
		{
			return SteamFriends.HasFriend(new CSteamID(steamId), EFriendFlags.k_EFriendFlagImmediate);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] whether {steamId} is a friend could not be read: {exception.Message}");
			return false;
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
