using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared;
using Steamworks;

namespace Realm.Client.Network;

/// <summary>
///     The lobby directory, which is Steam's. It creates, lists, joins and leaves Steam lobbies, keeps the short
///     advertisement in Steam metadata, and carries pre-match chat through Steam's lobby chat so it works before the match
///     connection exists. Nothing here calls a Realm server: there is no registry, no heartbeat, no GeoIP and no address to
///     share, and a lobby is found because Steam knows about it.
/// </summary>
public static class SteamLobbyDirectory
{
	/// <summary>The map's path name, which is what the browser shows and the join matches on.</summary>
	public const string MapKey = "map";

	/// <summary>The map's version string.</summary>
	public const string MapVersionKey = "mapVersion";

	/// <summary>The hash of the map content the host will run.</summary>
	public const string MapHashKey = "mapHash";

	/// <summary>The engine generation the map declares, which decides whether an older game can play it.</summary>
	public const string EngineGenerationKey = "generation";

	/// <summary>The game build the host runs.</summary>
	public const string GameBuildKey = "build";

	/// <summary>The match mode the lobby plays.</summary>
	public const string ModeKey = "mode";

	/// <summary>How many seats the lobby's map offers.</summary>
	public const string SeatsKey = "seats";

	/// <summary>The host's SteamID64, which is what a joiner connects to.</summary>
	public const string HostSteamIdKey = "hostSteamId";

	/// <summary>Whether the lobby has a join code. The code itself is never written here, because metadata is public.</summary>
	public const string RequiresCodeKey = "requiresCode";

	/// <summary>The largest lobby Steam will hold.</summary>
	public const int MaximumSteamLobbyMembers = 250;

	/// <summary>
	///     The headroom a code-gated lobby is given over its seats. A member may stand in a code-gated room before entering
	///     the code, so a room sized exactly to its seats would fill with people who cannot play and shut out the ones holding
	///     the code.
	/// </summary>
	public const int CodeGatedHeadroom = 2;

	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

	private static CallResult<LobbyCreated_t>? _createResult;

	private static CallResult<LobbyEnter_t>? _enterResult;

	private static CallResult<LobbyMatchList_t>? _listResult;

	private static TaskCompletionSource<ulong>? _createPending;

	private static TaskCompletionSource<bool>? _enterPending;

	private static TaskCompletionSource<bool>? _listPending;

	private static int _listedLobbyCount;

	private static Callback<LobbyChatUpdate_t>? _chatUpdateCallback;

	private static Callback<LobbyChatMsg_t>? _chatMessageCallback;

	private static Callback<LobbyDataUpdate_t>? _dataUpdateCallback;

	/// <summary>
	///     Raised when Steam reports a member entering or leaving the lobby this screen is in. The roster is reconciled from
	///     this rather than from polling, so a player who leaves is removed as soon as Steam says so.
	/// </summary>
	public static event Action<SteamLobbyMembershipChange>? MembershipChanged;

	/// <summary>Raised when another member says something in the lobby's chat.</summary>
	public static event Action<SteamLobbyChatEntry>? ChatMessageReceived;

	/// <summary>Raised when the lobby this screen is in is no longer joinable or no longer exists.</summary>
	public static event Action? LobbyClosed;

	/// <summary>Whether Steamworks is running, which is the only state in which a lobby can exist.</summary>
	public static bool IsAvailable => GameContext.Steam.IsAvailable;

	/// <summary>
	///     Creates a Steam lobby for this host. The visibility is Steam's own: public lobbies appear in the browser,
	///     friends-only ones in a friend's list, private ones only through an invitation, and invisible ones nowhere.
	/// </summary>
	/// <param name="visibility">The Steam visibility the host chose.</param>
	/// <param name="seatCount">The playable slots the lobby's map offers.</param>
	/// <param name="requiresCode">Whether a join code gates seating, which needs headroom above the seats.</param>
	/// <returns>The Steam lobby's id, or zero when Steam refused to create one.</returns>
	public static async Task<ulong> CreateAsync(ELobbyType visibility, int seatCount, bool requiresCode)
	{
		if (!IsAvailable) return 0;

		EnsureCallbacks();

		int memberLimit = Math.Clamp(seatCount + (requiresCode ? CodeGatedHeadroom : 0), 2, MaximumSteamLobbyMembers);

		var pending = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
		_createPending = pending;

		_createResult?.Dispose();
		_createResult = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);

		try
		{
			SteamAPICall_t call = SteamMatchmaking.CreateLobby(visibility, memberLimit);
			_createResult.Set(call);
		}
		catch (Exception exception)
		{
			_createPending = null;
			GameLog.LogError($"[SteamLobby] a lobby could not be created: {exception.Message}");
			return 0;
		}

		Task finished = await Task.WhenAny(pending.Task, Task.Delay(RequestTimeout)).ConfigureAwait(true);
		if (finished != pending.Task)
		{
			_createPending = null;
			GameLog.LogError("[SteamLobby] Steam did not answer the lobby creation in time");
			return 0;
		}

		return await pending.Task.ConfigureAwait(true);
	}

	private static void OnLobbyCreated(LobbyCreated_t created, bool ioFailure)
	{
		TaskCompletionSource<ulong>? pending = _createPending;
		_createPending = null;

		if (ioFailure || created.m_eResult != EResult.k_EResultOK || created.m_ulSteamIDLobby == 0)
		{
			pending?.TrySetResult(0);
			return;
		}

		pending?.TrySetResult(created.m_ulSteamIDLobby);
	}

	/// <summary>
	///     Enters a Steam lobby by id. Entry is Steam's decision: a lobby that closed, is full, or is friends-only answers a
	///     refusal this method states in the player's words.
	/// </summary>
	/// <param name="lobbyId">The Steam lobby id.</param>
	/// <returns>An empty string when the screen is in the lobby, or the reason it is not.</returns>
	public static async Task<string> JoinAsync(ulong lobbyId)
	{
		if (!IsAvailable) return GameContext.Steam.UnavailableExplanation;
		if (lobbyId == 0) return TranslationServer.Translate("This lobby no longer exists.");

		EnsureCallbacks();

		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_enterPending = pending;

		_enterResult?.Dispose();
		_enterResult = CallResult<LobbyEnter_t>.Create((entered, ioFailure) =>
		{
			TaskCompletionSource<bool>? waiting = _enterPending;
			_enterPending = null;

			if (ioFailure || (int)entered.m_EChatRoomEnterResponse != (int)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
			{
				waiting?.TrySetResult(false);
				return;
			}

			waiting?.TrySetResult(true);
		});

		try
		{
			SteamAPICall_t call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
			_enterResult.Set(call);
		}
		catch (Exception exception)
		{
			_enterPending = null;
			return string.Format(TranslationServer.Translate("Steam could not enter this lobby: {0}"), exception.Message);
		}

		Task finished = await Task.WhenAny(pending.Task, Task.Delay(RequestTimeout)).ConfigureAwait(true);
		if (finished != pending.Task)
		{
			_enterPending = null;
			return TranslationServer.Translate("Steam did not answer in time. The lobby may have closed.");
		}

		bool enteredLobby = await pending.Task.ConfigureAwait(true);
		return enteredLobby ? string.Empty : DescribeEnterFailure(lobbyId);
	}

	private static string DescribeEnterFailure(ulong lobbyId)
	{
		try
		{
			if (SteamMatchmaking.GetLobbyData(new CSteamID(lobbyId), GameBuildKey) is { Length: > 0 } build
			    && build != RealmVersion.GameBuildNumber)
			{
				return TranslationServer.Translate("This lobby was made with a different game version.");
			}
		}
		catch (Exception)
		{
		}

		return TranslationServer.Translate("This lobby could not be entered. It may be full, friends-only or closed.");
	}

	/// <summary>
	///     Asks Steam for the lobbies this screen could join, filtered to the game build and protocol the running game speaks
	///     so a screen that has not restarted after an update is never offered a lobby it cannot play.
	/// </summary>
	/// <returns>The lobbies Steam listed, newest answer replacing the last one.</returns>
	public static async Task<IReadOnlyList<ulong>> BrowseAsync()
	{
		if (!IsAvailable) return Array.Empty<ulong>();

		EnsureCallbacks();

		var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		_listPending = pending;

		_listResult?.Dispose();
		_listResult = CallResult<LobbyMatchList_t>.Create((list, ioFailure) =>
		{
			_listedLobbyCount = ioFailure ? 0 : (int)list.m_nLobbiesMatching;
			TaskCompletionSource<bool>? waiting = _listPending;
			_listPending = null;
			waiting?.TrySetResult(!ioFailure);
		});

		try
		{
			// The build number is the lock: Steam keeps players in step, and a screen that has not restarted after an update is
			// filtered out here rather than offered a lobby it would fail to join.
			SteamMatchmaking.AddRequestLobbyListStringFilter(GameBuildKey, RealmVersion.GameBuildNumber, ELobbyComparison.k_ELobbyComparisonEqual);
			SteamAPICall_t call = SteamMatchmaking.RequestLobbyList();
			_listResult.Set(call);
		}
		catch (Exception exception)
		{
			_listPending = null;
			GameLog.LogError($"[SteamLobby] the lobby list could not be requested: {exception.Message}");
			return Array.Empty<ulong>();
		}

		Task finished = await Task.WhenAny(pending.Task, Task.Delay(RequestTimeout)).ConfigureAwait(true);
		if (finished != pending.Task)
		{
			_listPending = null;
			return Array.Empty<ulong>();
		}

		if (!await pending.Task.ConfigureAwait(true)) return Array.Empty<ulong>();

		var lobbyIds = new List<ulong>();
		try
		{
			for (int index = 0; index < _listedLobbyCount; index++)
			{
				ulong lobbyId = SteamMatchmaking.GetLobbyByIndex(index).m_SteamID;
				if (lobbyId != 0) lobbyIds.Add(lobbyId);
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] the lobby list could not be read: {exception.Message}");
		}

		return lobbyIds;
	}

	/// <summary>Leaves the Steam lobby this screen is in, if it is in one.</summary>
	/// <param name="lobbyId">The lobby to leave, or zero to leave whatever lobby Steam says this screen is in.</param>
	public static void Leave(ulong lobbyId)
	{
		if (!IsAvailable) return;

		ulong target = lobbyId;
		if (target == 0)
		{
			try
			{
				target = SteamMatchmaking.GetLobbyByIndex(0).m_SteamID;
			}
			catch (Exception)
			{
				return;
			}
		}

		if (target == 0) return;

		try
		{
			SteamMatchmaking.LeaveLobby(new CSteamID(target));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] the lobby could not be left: {exception.Message}");
		}
	}

	/// <summary>Writes one short advertisement value into a lobby's metadata.</summary>
	public static void SetData(ulong lobbyId, string key, string value)
	{
		if (!IsAvailable || lobbyId == 0) return;

		try
		{
			SteamMatchmaking.SetLobbyData(new CSteamID(lobbyId), key, value ?? string.Empty);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] '{key}' could not be written to the lobby: {exception.Message}");
		}
	}

	/// <summary>Reads one advertisement value out of a lobby's metadata.</summary>
	public static string GetData(ulong lobbyId, string key)
	{
		if (!IsAvailable || lobbyId == 0) return string.Empty;

		try
		{
			return SteamMatchmaking.GetLobbyData(new CSteamID(lobbyId), key);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] '{key}' could not be read from the lobby: {exception.Message}");
			return string.Empty;
		}
	}

	/// <summary>Reads a lobby's member count as Steam states it.</summary>
	public static int GetMemberCount(ulong lobbyId)
	{
		if (!IsAvailable || lobbyId == 0) return 0;

		try
		{
			return SteamMatchmaking.GetNumLobbyMembers(new CSteamID(lobbyId));
		}
		catch (Exception)
		{
			return 0;
		}
	}

	/// <summary>The SteamID64 of a lobby's owner, who is the host.</summary>
	public static ulong GetOwnerSteamId(ulong lobbyId)
	{
		if (!IsAvailable || lobbyId == 0) return 0;

		try
		{
			return SteamMatchmaking.GetLobbyOwner(new CSteamID(lobbyId)).m_SteamID;
		}
		catch (Exception)
		{
			return 0;
		}
	}

	/// <summary>The SteamID64s of everybody standing in a lobby, in Steam's order.</summary>
	public static IReadOnlyList<ulong> GetMembers(ulong lobbyId)
	{
		if (!IsAvailable || lobbyId == 0) return Array.Empty<ulong>();

		var members = new List<ulong>();
		try
		{
			var lobby = new CSteamID(lobbyId);
			int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
			for (int index = 0; index < count; index++)
			{
				ulong member = SteamMatchmaking.GetLobbyMemberByIndex(lobby, index).m_SteamID;
				if (member != 0) members.Add(member);
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] the lobby's members could not be read: {exception.Message}");
		}

		return members;
	}

	/// <summary>
	///     Opens or closes the lobby to new members. A code-gated lobby closes once its seats are taken, so a stranger
	///     without the code can never occupy the last one.
	/// </summary>
	public static void SetJoinable(ulong lobbyId, bool joinable)
	{
		if (!IsAvailable || lobbyId == 0) return;

		try
		{
			SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobbyId), joinable);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] the lobby's joinability could not be set: {exception.Message}");
		}
	}

	/// <summary>Says something in the lobby's chat, which every member receives before the match connection exists.</summary>
	public static void SendChat(string message)
	{
		if (!IsAvailable || string.IsNullOrEmpty(message)) return;

		try
		{
			byte[] body = Encoding.UTF8.GetBytes(message);
			SteamMatchmaking.SendLobbyChatMsg(GetCurrentLobby(), body, body.Length);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] a chat line could not be sent: {exception.Message}");
		}
	}

	/// <summary>Opens Steam's own invitation dialog for the lobby this screen is in.</summary>
	public static bool OpenInviteDialog(ulong lobbyId)
	{
		if (!IsAvailable || lobbyId == 0) return false;

		try
		{
			SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobbyId));
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] Steam's invite dialog could not be opened: {exception.Message}");
			return false;
		}
	}

	private static CSteamID GetCurrentLobby()
	{
		return SteamMatchmaking.GetLobbyByIndex(0);
	}

	private static void EnsureCallbacks()
	{
		_chatUpdateCallback ??= Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);
		_chatMessageCallback ??= Callback<LobbyChatMsg_t>.Create(OnLobbyChatMessage);
		_dataUpdateCallback ??= Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
	}

	private static void OnLobbyChatUpdate(LobbyChatUpdate_t update)
	{
		ulong changed = update.m_ulSteamIDUserChanged;
		if (changed == 0) return;

		var change = new SteamLobbyMembershipChange(
			update.m_ulSteamIDLobby,
			changed,
			update.m_rgfChatMemberStateChange);

		MembershipChanged?.Invoke(change);

		if (SteamMatchmaking.GetNumLobbyMembers(new CSteamID(update.m_ulSteamIDLobby)) == 0) LobbyClosed?.Invoke();
	}

	private static void OnLobbyChatMessage(LobbyChatMsg_t message)
	{
		try
		{
			var buffer = new byte[4096];
			if (SteamMatchmaking.GetLobbyChatEntry(new CSteamID(message.m_ulSteamIDLobby), (int)message.m_iChatID, out CSteamID sender, buffer, buffer.Length, out _) <= 0) return;

			string text = Encoding.UTF8.GetString(buffer).TrimEnd('\0');
			if (text.Length == 0) return;

			string persona = TryReadPersona(sender.m_SteamID);
			ChatMessageReceived?.Invoke(new SteamLobbyChatEntry(message.m_ulSteamIDLobby, sender.m_SteamID, persona, text));
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[SteamLobby] a chat line could not be read: {exception.Message}");
		}
	}

	private static void OnLobbyDataUpdate(LobbyDataUpdate_t update)
	{
		if (update.m_bSuccess == 0) return;

		try
		{
			var lobby = new CSteamID(update.m_ulSteamIDLobby);
			if (SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID != 0 && GetData(update.m_ulSteamIDLobby, RequiresCodeKey) == "1" && GetMemberCount(update.m_ulSteamIDLobby) == 0)
			{
				LobbyClosed?.Invoke();
			}
		}
		catch (Exception)
		{
		}
	}

	private static string TryReadPersona(ulong steamId)
	{
		try
		{
			return SteamFriends.GetFriendPersonaName(new CSteamID(steamId));
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}
}

/// <summary>One member entering or leaving a Steam lobby, as Steam reported it.</summary>
public readonly record struct SteamLobbyMembershipChange(ulong LobbyId, ulong SteamId, uint StateChangeFlags)
{
	/// <summary>Whether this change is somebody entering the lobby.</summary>
	public bool Entered => (StateChangeFlags & (uint)EChatMemberStateChange.k_EChatMemberStateChangeEntered) != 0;

	/// <summary>Whether this change is somebody leaving the lobby for any reason.</summary>
	public bool Left => !Entered;
}

/// <summary>One line of pre-match chat, with the Steam identity that said it.</summary>
public readonly record struct SteamLobbyChatEntry(ulong LobbyId, ulong SteamId, string PersonaName, string Text);
