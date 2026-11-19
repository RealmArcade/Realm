using System;
using MemoryPack;
using Realm.Shared.Network;
using Realm.Shared.Steam;

namespace Realm.ListenServer;

/// <summary>
///     The playtest vote a player casts while a hosted match runs.
///     <para>
///         The vote is recorded here rather than in the lobby because it belongs to a match, and the identity it is
///         recorded against is the one this process seated the connection as - asserted by Steam on the socket - never a
///         id the screen states. That is why the code handed back is minted here: a client that could name an identity
///         could vote for a player who never played the map.
///     </para>
///     <para>
///         Nothing is verified, signed or sent anywhere. The authority decides this vote was cast by a seated Steam
///         identity and by somebody who sat through five minutes of the match; the count itself is kept on the map
///         author's own machine, where it can be edited by anyone who tries.
///     </para>
/// </summary>
public partial class ListenServerHost
{
	/// <summary>
	///     How long a player has to have been in the match before their answer counts, in match seconds. It is the same
	///     gate the in-game control shows, enforced here as well so a screen that skipped it cannot vote from the lobby.
	/// </summary>
	public const int MinimumMatchSecondsBeforeVote = 300;

	/// <summary>
	///     The votes this match has recorded, by SteamID, so a tester who changes their mind replaces their own answer
	///     instead of adding a second one. A dropped player's vote stands.
	/// </summary>
	private readonly System.Collections.Generic.Dictionary<ulong, PlaytestVote> _playtestVotes = new();

	/// <summary>The map's title, read once for this match because every vote names its map with it.</summary>
	private string? _playtestMapTitle;

	/// <summary>Reads one vote out of a frame and answers the screen that cast it.</summary>
	/// <param name="peerId">The connection the vote arrived on.</param>
	/// <param name="payload">The serialized <see cref="PlaytestVoteMessage" />.</param>
	private void HandlePlaytestVote(int peerId, ReadOnlySpan<byte> payload)
	{
		PlaytestVoteMessage message;
		try
		{
			message = MemoryPackSerializer.Deserialize<PlaytestVoteMessage>(payload);
		}
		catch (Exception exception) when (exception is System.IO.IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a playtest vote that could not be read: {exception.Message}");
			return;
		}

		if (message.Vote != (int)PlaytestVote.Playtested && message.Vote != (int)PlaytestVote.NotReady) return;

		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		if (!_seatedPeers.ContainsKey(peerId) || !_transport.TryGetPeerSteamId(peerId, out ulong steamId) || steamId == 0)
		{
			SendPlaytestAnswer(peerId, false, string.Empty, "Only a seat with a Steam account behind it can answer for a map.");
			return;
		}

		int playedSeconds = (int)(simulation.TickCount / MatchTiming.SimulationTickRateHz);
		if (playedSeconds < MinimumMatchSecondsBeforeVote)
		{
			int remaining = MinimumMatchSecondsBeforeVote - playedSeconds;
			SendPlaytestAnswer(peerId, false, string.Empty, $"Play a little longer before answering: {remaining} second(s) to go.");
			return;
		}

		string mapTitle = ResolvePlaytestMapTitle();
		string mapKey = PlaytestStore.BuildNameKey(mapTitle);
		var vote = (PlaytestVote)message.Vote;
		string code = PlaytestVoteCode.Create(steamId, mapKey, vote, DateTimeOffset.UtcNow);

		bool replaced = _playtestVotes.ContainsKey(steamId);
		_playtestVotes[steamId] = vote;

		_log.Write(replaced
			? $"seat {steamId} changed its playtest answer for '{mapKey}' to {vote}"
			: $"seat {steamId} answered {vote} for '{mapKey}', which is vote {_playtestVotes.Count} of this match");

		SendPlaytestAnswer(peerId, true, code, string.Empty);
		BroadcastPlaytestRecord(mapKey, mapTitle, vote, code);
	}

	/// <summary>
	///     The map's own name, as its metadata states it, which is the name its author publishes it under. A vote code names
	///     the map and a screen files the vote under the name and the author, so both have to be the title rather than the
	///     folder the map happens to sit in: the folder changes when a map is renamed, moved or reseated under a different
	///     Workshop item, and a count that takes 25 testers must not be reset by that.
	/// </summary>
	/// <returns>The map's title, or the name the lobby was started with when its metadata states none.</returns>
	private string ResolvePlaytestMapTitle()
	{
		if (_playtestMapTitle != null) return _playtestMapTitle;

		string fallback = _lobby?.MapName ?? _arguments.MapName;
		try
		{
			string title = Realm.Shared.Services.MapFileService.LoadMetadata(_arguments.MapPath).MapProperties?.MapName ?? string.Empty;
			_playtestMapTitle = string.IsNullOrWhiteSpace(title) ? fallback : title;
		}
		catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_log.Write($"the map's title could not be read, so playtest votes name '{fallback}': {exception.Message}");
			_playtestMapTitle = fallback;
		}

		return _playtestMapTitle;
	}

	/// <summary>
	///     Hands an accepted vote to every screen in the match, because the map's author may be any one of them. Each
	///     screen keeps what it receives in its own ledger; the authority keeps no copy beyond the match.
	/// </summary>
	/// <param name="mapKey">The map the vote is about.</param>
	/// <param name="mapTitle">The map's own name, which a screen files the vote under.</param>
	/// <param name="vote">The answer the tester gave.</param>
	/// <param name="code">The opaque code that carries the vote.</param>
	private void BroadcastPlaytestRecord(string mapKey, string mapTitle, PlaytestVote vote, string code)
	{
		var record = new PlaytestVoteRecordMessage
		{
			MapKey = mapKey,
			MapTitle = mapTitle,
			Vote = (int)vote,
			Code = code
		};

		byte[] bytes = MemoryPackSerializer.Serialize(record);
		foreach (int seatedPeerId in _seatedPeers.Keys)
		{
			_transport.Send(seatedPeerId, NetworkFrameKind.PlaytestVoteRecord, bytes, FrameDelivery.Reliable);
		}
	}

	/// <summary>Answers a vote with whether it counted and, when it did, the code the tester copies.</summary>
	/// <param name="peerId">The connection that voted.</param>
	/// <param name="accepted">Whether the vote counted.</param>
	/// <param name="code">The code that carries the vote, or empty when it was refused.</param>
	/// <param name="failure">Why the vote was refused, empty when it counted.</param>
	private void SendPlaytestAnswer(int peerId, bool accepted, string code, string failure)
	{
		var answer = new PlaytestVoteAnswerMessage
		{
			Accepted = accepted,
			Code = code,
			Failure = failure
		};

		_transport.Send(peerId, NetworkFrameKind.PlaytestVoteAnswer, MemoryPackSerializer.Serialize(answer), FrameDelivery.Reliable);
	}
}
