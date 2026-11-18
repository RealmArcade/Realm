using System;
using Godot;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>One player's lifetime record, as Steam holds it.</summary>
/// <param name="MatchesPlayed">Every counted multiplayer match this player has finished or left.</param>
/// <param name="MatchesWon">The matches of those this player won.</param>
/// <param name="MatchesLost">The matches of those this player did not win.</param>
public readonly record struct SteamLifetimeRecord(int MatchesPlayed, int MatchesWon, int MatchesLost);

/// <summary>One row of a Steam leaderboard.</summary>
/// <param name="SteamId">The Steam identity the row belongs to.</param>
/// <param name="Rank">The row's position on the board, from one.</param>
/// <param name="Score">The score the row holds.</param>
/// <param name="IsLocalPlayer">Whether this row is the player reading the board.</param>
public readonly record struct SteamLeaderboardRow(ulong SteamId, int Rank, int Score, bool IsLocalPlayer);

/// <summary>What Steam answered for one leaderboard query.</summary>
/// <param name="Rows">The rows, best first.</param>
/// <param name="LocalPlayerRank">The reader's own rank, or zero when Steam did not place them on this view.</param>
/// <param name="FriendsOnly">Whether the rows are the reader's friends rather than everybody.</param>
public readonly record struct SteamLeaderboardPage(SteamLeaderboardRow[] Rows, int LocalPlayerRank, bool FriendsOnly);

/// <summary>
///     The game's own Steam stats and leaderboards: lifetime totals every player keeps, written by their own client
///     after a counted match. This is deliberately small and fixed - Steam stats are player-facing counters, not a
///     database - and it is namespaced apart from the stats a map script writes, so a map can never overwrite a lifetime
///     total and a base-game counter can never collide with one a map defines. Steam reports these numbers from the
///     client, so they are a record rather than proof; nothing here is treated as anti-cheat data or as a ranking.
/// </summary>
public partial class SteamService
{
	/// <summary>
	///     The prefix every base-game stat and leaderboard is named with. It is stated here once and is distinct from
	///     <see cref="SteamScriptNames.ForMap"/>, which is what a map script's own stats and achievements live under.
	/// </summary>
	public const string GameStatPrefix = "realm_";

	/// <summary>Every counted multiplayer match this player finished or left early.</summary>
	public const string MatchesPlayedStat = GameStatPrefix + "matches_played";

	/// <summary>The counted matches this player won.</summary>
	public const string MatchesWonStat = GameStatPrefix + "matches_won";

	/// <summary>The counted matches this player did not win.</summary>
	public const string MatchesLostStat = GameStatPrefix + "matches_lost";

	/// <summary>
	///     The one global board this game keeps: lifetime wins. It is fixed rather than one board per map, because the
	///     Workshop's map count is unbounded and Steam states no cap a per-map board could be measured against.
	/// </summary>
	public const string WinsLeaderboardName = GameStatPrefix + "lifetime_wins";

	/// <summary>How many rows a leaderboard view asks Steam for.</summary>
	private const int MaximumLeaderboardRows = 25;

	private SteamAPICall_t _winsLeaderboardCall;

	private CallResult<LeaderboardFindResult_t>? _winsLeaderboardFindResult;

	private Action<SteamLeaderboardPage?>? _pendingLeaderboardReply;

	private bool _pendingLeaderboardFriendsOnly;

	private SteamLeaderboard_t _winsLeaderboard;

	private bool _winsLeaderboardFound;

	private bool _leaderboardUploadRequested;

	private CallResult<LeaderboardScoreUploaded_t>? _leaderboardUploadResult;

	private CallResult<LeaderboardScoresDownloaded_t>? _leaderboardEntriesResult;

	private SteamLeaderboardEntries_t _downloadedEntries;

	private int _downloadedEntryCount;

	private int _downloadedLocalRank;

	private readonly SteamLeaderboardRow[] _downloadedRows = new SteamLeaderboardRow[MaximumLeaderboardRows];

	/// <summary>
	///     Whether this player's own record can be read right now. It is false without Steam, and false until Steam has
	///     answered the current-stats request this process makes when it starts, so a screen can say the numbers are not
	///     available instead of drawing zeroes that look like a fresh record.
	/// </summary>
	public bool IsLifetimeRecordAvailable => _initialized && _lifetimeRecordLoaded;

	private bool _lifetimeRecordLoaded;

	/// <summary>Whether the player asked Steam for their current stats and Steam answered.</summary>
	private bool _lifetimeRecordAnswered;

	/// <summary>
	///     Asks Steam for this player's stored stats, which a session has to do before it may read or add to them. It is
	///     called once when Steamworks starts and is harmless to call again.
	/// </summary>
	private void RequestLifetimeStats()
	{
		if (!_initialized || _lifetimeRecordAnswered) return;

		_lifetimeRecordAnswered = true;

		try
		{
			if (!SteamUserStats.RequestCurrentStats())
			{
				GameLog.LogToConsole("[Steam] this player's stats could not be requested yet; they are read again on the next session");
				return;
			}

			_lifetimeRecordLoaded = true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] this player's stats could not be requested: {exception.Message}");
		}
	}

	/// <summary>
	///     Adds one finished match to this player's lifetime record and stores it. Only a counted multiplayer match is
	///     recorded, and only by the client that played it, because Steam stats belong to the user who is signed in.
	/// </summary>
	/// <param name="victory">Whether this player won the match.</param>
	/// <returns>Whether Steam accepted the change, so the caller can say it was not stored.</returns>
	public bool RecordMatchResult(bool victory)
	{
		if (!_initialized) return false;

		RequestLifetimeStats();

		try
		{
			if (!SteamUserStats.GetStat(MatchesPlayedStat, out int played))
			{
				GameLog.LogToConsole($"[Steam] this player's match record could not be read, so the result was not stored");
				return false;
			}

			SteamUserStats.GetStat(MatchesWonStat, out int won);
			SteamUserStats.GetStat(MatchesLostStat, out int lost);

			if (!SteamUserStats.SetStat(MatchesPlayedStat, played + 1)) return false;
			if (!SteamUserStats.SetStat(victory ? MatchesWonStat : MatchesLostStat, (victory ? won : lost) + 1)) return false;

			if (!SteamUserStats.StoreStats())
			{
				GameLog.LogToConsole("[Steam] this match's result could not be stored; Steam keeps it and the next store retries");
				return false;
			}

			RequestWinsLeaderboardUpload(victory ? won + 1 : won);
			GameLog.LogToConsole($"[Steam] match record stored: {(victory ? "won" : "lost")}, {played + 1} played");
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] this match's result could not be recorded: {exception.Message}");
			return false;
		}
	}

	/// <summary>Reads this player's lifetime record as Steam holds it.</summary>
	/// <param name="record">The totals, or zeroes when they cannot be read.</param>
	/// <returns>Whether the totals are known, rather than being zeroes standing in for an unknown.</returns>
	public bool TryReadLifetimeRecord(out SteamLifetimeRecord record)
	{
		record = default;
		if (!_initialized) return false;

		RequestLifetimeStats();
		if (!_lifetimeRecordLoaded) return false;

		try
		{
			if (!SteamUserStats.GetStat(MatchesPlayedStat, out int played)) return false;
			SteamUserStats.GetStat(MatchesWonStat, out int won);
			SteamUserStats.GetStat(MatchesLostStat, out int lost);

			record = new SteamLifetimeRecord(played, won, lost);
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] this player's match record could not be read: {exception.Message}");
			return false;
		}
	}

	/// <summary>
	///     Opens this player's own Steam profile in the overlay, which is where their Steam-level record lives. Without
	///     Steam, or with the overlay disabled, it does nothing: there is no second profile page to fall back to.
	/// </summary>
	public void OpenOwnSteamProfile()
	{
		if (!_initialized) return;

		try
		{
			SteamFriends.ActivateGameOverlayToUser("steamid", SteamUser.GetSteamID());
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the profile overlay could not be opened: {exception.Message}");
		}
	}

	/// <summary>
	///     Reads the lifetime-wins board, best first. Steam answers asynchronously, so the reply arrives on a later frame
	///     through the callback pump; a second request made before the first answers replaces it, because a player who
	///     changed the filter does not want the older view drawn.
	/// </summary>
	/// <param name="friendsOnly">Whether to read the reader's friends instead of everybody.</param>
	/// <param name="onLoaded">Receives the page, or <c>null</c> when Steam could not answer.</param>
	public void RequestWinsLeaderboard(bool friendsOnly, Action<SteamLeaderboardPage?> onLoaded)
	{
		if (!_initialized)
		{
			onLoaded(null);
			return;
		}

		_pendingLeaderboardReply = onLoaded;
		_pendingLeaderboardFriendsOnly = friendsOnly;

		if (_winsLeaderboardFound)
		{
			DownloadWinsLeaderboard();
			return;
		}

		try
		{
			_winsLeaderboardFindResult?.Dispose();
			_winsLeaderboardFindResult = CallResult<LeaderboardFindResult_t>.Create(OnWinsLeaderboardFound);
			_winsLeaderboardCall = SteamUserStats.FindOrCreateLeaderboard(WinsLeaderboardName,
				ELeaderboardSortMethod.k_ELeaderboardSortMethodDescending,
				ELeaderboardDisplayType.k_ELeaderboardDisplayTypeNumeric);
			_winsLeaderboardFindResult.Set(_winsLeaderboardCall);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the wins leaderboard could not be found: {exception.Message}");
			CompleteLeaderboardRequest(null);
		}
	}

	private void OnWinsLeaderboardFound(LeaderboardFindResult_t result, bool ioFailure)
	{
		if (ioFailure || result.m_hSteamLeaderboard.m_SteamLeaderboard == 0)
		{
			GameLog.LogToConsole("[Steam] the wins leaderboard is not available, so no board is shown");
			CompleteLeaderboardRequest(null);
			return;
		}

		_winsLeaderboard = result.m_hSteamLeaderboard;
		_winsLeaderboardFound = true;
		DownloadWinsLeaderboard();
	}

	private void DownloadWinsLeaderboard()
	{
		try
		{
			_leaderboardEntriesResult?.Dispose();
			_leaderboardEntriesResult = CallResult<LeaderboardScoresDownloaded_t>.Create(OnWinsLeaderboardDownloaded);

			ELeaderboardDataRequest request = _pendingLeaderboardFriendsOnly
				? ELeaderboardDataRequest.k_ELeaderboardDataRequestFriends
				: ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal;

			SteamAPICall_t call = SteamUserStats.DownloadLeaderboardEntries(_winsLeaderboard, request, 1, MaximumLeaderboardRows);
			_leaderboardEntriesResult.Set(call);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the wins leaderboard could not be read: {exception.Message}");
			CompleteLeaderboardRequest(null);
		}
	}

	private void OnWinsLeaderboardDownloaded(LeaderboardScoresDownloaded_t result, bool ioFailure)
	{
		if (ioFailure)
		{
			GameLog.LogToConsole("[Steam] the wins leaderboard did not answer, so no board is shown");
			CompleteLeaderboardRequest(null);
			return;
		}

		_downloadedEntries = result.m_hSteamLeaderboardEntries;
		_downloadedEntryCount = Math.Min((int)result.m_cEntryCount, MaximumLeaderboardRows);
		_downloadedLocalRank = 0;

		ulong localSteamId = SteamId;
		var details = new int[0];

		for (int index = 0; index < _downloadedEntryCount; index++)
		{
			if (!SteamUserStats.GetDownloadedLeaderboardEntry(_downloadedEntries, index, out LeaderboardEntry_t entry, details, 0))
			{
				_downloadedEntryCount = index;
				break;
			}

			ulong steamId = entry.m_steamIDUser.m_SteamID;
			bool isLocalPlayer = steamId == localSteamId && localSteamId != 0;
			if (isLocalPlayer) _downloadedLocalRank = entry.m_nGlobalRank;

			_downloadedRows[index] = new SteamLeaderboardRow(steamId, entry.m_nGlobalRank, entry.m_nScore, isLocalPlayer);
		}

		var rows = new SteamLeaderboardRow[_downloadedEntryCount];
		Array.Copy(_downloadedRows, rows, _downloadedEntryCount);

		CompleteLeaderboardRequest(new SteamLeaderboardPage(rows, _downloadedLocalRank, _pendingLeaderboardFriendsOnly));
	}

	private void CompleteLeaderboardRequest(SteamLeaderboardPage? page)
	{
		Action<SteamLeaderboardPage?>? reply = _pendingLeaderboardReply;
		_pendingLeaderboardReply = null;
		reply?.Invoke(page);
	}

	/// <summary>
	///     Sends this player's lifetime wins to the board, once per ending match. A failure is logged and dropped: the
	///     board is a view of the totals, so it catches up the next time a match is recorded rather than being queued.
	/// </summary>
	/// <param name="wins">The lifetime wins the player now holds.</param>
	private void RequestWinsLeaderboardUpload(int wins)
	{
		if (_leaderboardUploadRequested || !_winsLeaderboardFound) return;

		_leaderboardUploadRequested = true;

		try
		{
			_leaderboardUploadResult?.Dispose();
			_leaderboardUploadResult = CallResult<LeaderboardScoreUploaded_t>.Create(OnWinsLeaderboardUploaded);

			SteamAPICall_t call = SteamUserStats.UploadLeaderboardScore(_winsLeaderboard,
				ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest, wins, null, 0);
			_leaderboardUploadResult.Set(call);
		}
		catch (Exception exception)
		{
			_leaderboardUploadRequested = false;
			GameLog.LogError($"[Steam] this player's wins could not be sent to the leaderboard: {exception.Message}");
		}
	}

	private void OnWinsLeaderboardUploaded(LeaderboardScoreUploaded_t result, bool ioFailure)
	{
		_leaderboardUploadRequested = false;

		if (ioFailure)
		{
			GameLog.LogToConsole("[Steam] this player's wins did not reach the leaderboard; it catches up when the next match is recorded");
			return;
		}

		GameLog.LogToConsole($"[Steam] lifetime wins {result.m_nScore} are on the leaderboard at rank {result.m_nGlobalRankNew}");
	}
}
