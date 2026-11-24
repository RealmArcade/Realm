using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>The optional join code that gates a lobby's seats.</summary>
public partial class ServerLobby
{
	/// <summary>
	///     The code this lobby gates its seats with, or empty when anybody who can see the lobby may sit in it. It is held
	///     here for the lobby's lifetime, compared here, and never published to a screen or to Steam metadata.
	/// </summary>
	private string _joinCode = string.Empty;

	/// <summary>
	///     The members standing in this room without a seat, because the lobby gates its seats with a code they have not
	///     presented. Steam membership admits them to the room; only the code admits them to a seat.
	/// </summary>
	private readonly Dictionary<int, WaitingForJoinCode> _waitingJoins = new();

	/// <summary>
	///     The Steam identities that have presented the code, which is what lets a dropped seat reconnect without being
	///     asked again. It holds identities rather than peer ids because a reconnect always arrives on a new connection.
	/// </summary>
	private readonly HashSet<ulong> _joinCodeGrantedSteamIds = new();

	/// <summary>
	///     The Steam identities this lobby removed, which its authority refuses by name. A kicked player's Steam lobby
	///     membership survives the kick, so the refusal cannot be Steam's: it is the one place that both the connection and
	///     the reconnect pass through.
	/// </summary>
	private readonly HashSet<ulong> _refusedSteamIds = new();

	/// <summary>Sets the code this lobby gates its seats with, from the match it was started with.</summary>
	/// <param name="joinCode">The code the host set, or empty for a lobby anyone who can see it may sit in.</param>
	private void ApplyJoinCode(string? joinCode)
	{
		_joinCode = LobbyLimits.Sanitize(joinCode, LobbyLimits.MaximumJoinCodeLength);
		if (_joinCode.Length > 0) _log.Write("this lobby gates its seats with a join code");
	}

	/// <summary>
	///     Whether a peer that just arrived may take a seat yet. A lobby with no code seats everybody; a lobby with a code
	///     seats nobody until that screen presents it, except its own administrator, who wrote it.
	/// </summary>
	/// <param name="steamId">The Steam identity Steam asserted for the arriving peer, which a reconnect presents again.</param>
	/// <param name="proveAdmin">Whether the arriving peer proved the ticket that administers this lobby.</param>
	/// <returns>Whether the peer waits in the room instead of taking a seat.</returns>
	private bool WaitsForJoinCode(ulong steamId, bool proveAdmin)
	{
		if (_joinCode.Length == 0) return false;
		if (proveAdmin) return false;
		if (steamId == 0) return true;

		return !_joinCodeGrantedSteamIds.Contains(steamId);
	}

	/// <summary>Remembers a member standing in the room until the code is presented.</summary>
	/// <param name="peerId">The connection the member arrived on.</param>
	/// <param name="name">The name the member plays under.</param>
	/// <param name="steamId">The Steam identity Steam asserted for the member.</param>
	private void WaitForJoinCode(int peerId, string name, ulong steamId)
	{
		_waitingJoins[peerId] = new WaitingForJoinCode(name, steamId);
		_log.Write($"peer {peerId} stands in this lobby without a seat: it gates its seats with a join code");
	}

	/// <summary>
	///     Compares a code a screen presented with the one this lobby holds, and seats the member when they match. A wrong
	///     code is answered as a refused seat rather than a disconnect, because the player is in the room and can try again.
	/// </summary>
	/// <param name="peerId">The connection that presented the code.</param>
	/// <param name="presented">The code the player typed.</param>
	public void PresentJoinCode(int peerId, string presented)
	{
		if (_matchStarted) return;
		if (!_waitingJoins.TryGetValue(peerId, out WaitingForJoinCode waiting)) return;

		if (_joinCode.Length == 0)
		{
			// The host cleared the code while this screen was typing: the seats opened without it.
			SeatWaitingMember(peerId, waiting);
			return;
		}

		if (!MatchesJoinCode(presented))
		{
			_log.Write($"peer {peerId} presented a join code that does not match, so its seat stays refused");
			SendJoinCodeAnswer(peerId, accepted: false);
			return;
		}

		if (waiting.SteamId != 0) _joinCodeGrantedSteamIds.Add(waiting.SteamId);
		SeatWaitingMember(peerId, waiting);
	}

	/// <summary>Gives a member that was waiting for the code the seat it was refused.</summary>
	/// <param name="peerId">The connection the member is waiting on.</param>
	/// <param name="waiting">What the member stated when it arrived.</param>
	private void SeatWaitingMember(int peerId, in WaitingForJoinCode waiting)
	{
		_waitingJoins.Remove(peerId);

		string rejection = SeatPeer(peerId, waiting.Name, waiting.SteamId, proveAdmin: false, out bool isAdmin);
		if (rejection.Length > 0)
		{
			_log.Write($"peer {peerId} presented the join code but could not be seated: {rejection}");
			SendJoinCodeAnswer(peerId, accepted: false);
			return;
		}

		_log.Write($"peer {peerId} presented the join code and was seated as '{(isAdmin ? "administrator" : "member")}'");
		SendJoinCodeAnswer(peerId, accepted: true);
		BroadcastState();
	}

	/// <summary>
	///     Sets or clears the code this lobby gates its seats with. Only the administrator may, and clearing it releases every
	///     member who was waiting for it, which is why the change is published as ordinary lobby state.
	/// </summary>
	/// <param name="joinCode">The new code, or empty to open the seats to everybody in the room.</param>
	private void SetJoinCode(string? joinCode)
	{
		string sanitized = LobbyLimits.Sanitize(joinCode, LobbyLimits.MaximumJoinCodeLength);
		if (string.Equals(sanitized, _joinCode, StringComparison.Ordinal)) return;

		_joinCode = sanitized;

		if (_joinCode.Length == 0)
		{
			_log.Write("this lobby no longer gates its seats with a join code; the members waiting for one are being seated");
			foreach (var pair in new Dictionary<int, WaitingForJoinCode>(_waitingJoins)) SeatWaitingMember(pair.Key, pair.Value);
		}
		else
		{
			_log.Write("this lobby now gates its seats with a join code");
			_joinCodeGrantedSteamIds.Clear();
		}

		AddSystemChat(_joinCode.Length == 0
			? "This lobby no longer asks for a join code; its seats are open."
			: "This lobby now asks for a join code before it seats a player.");
		BroadcastState();
	}

	/// <summary>
	///     Compares a presented code with the one this lobby holds in constant time, so the answer never times the guess.
	/// </summary>
	/// <param name="presented">The code the player typed.</param>
	/// <returns>Whether the two match.</returns>
	private bool MatchesJoinCode(string? presented)
	{
		byte[] expected = Encoding.UTF8.GetBytes(_joinCode);
		byte[] actual = Encoding.UTF8.GetBytes(LobbyLimits.Sanitize(presented, LobbyLimits.MaximumJoinCodeLength));
		return CryptographicOperations.FixedTimeEquals(expected, actual);
	}

	/// <summary>Answers the screen that presented a code with whether its seat is now held.</summary>
	/// <param name="peerId">The connection that presented the code.</param>
	/// <param name="accepted">Whether the code matched.</param>
	private void SendJoinCodeAnswer(int peerId, bool accepted)
	{
		var answer = new LobbyJoinCodeAnswerMessage { Accepted = accepted };
		_send(peerId, NetworkFrameKind.LobbyJoinCodeAnswer, MemoryPackSerializer.Serialize(answer), FrameDelivery.Reliable);
	}

	/// <summary>Whether a member is standing in this room without a seat.</summary>
	/// <param name="peerId">The connection to ask about.</param>
	/// <returns>Whether the connection is waiting for a join code.</returns>
	public bool IsWaitingForJoinCode(int peerId) => _waitingJoins.ContainsKey(peerId);

	/// <summary>Every connection standing in this room without a seat.</summary>
	/// <returns>The connections waiting for a join code.</returns>
	private IEnumerable<int> WaitingJoinPeers => _waitingJoins.Keys;

	/// <summary>What a member stated when it arrived and was made to wait for a join code.</summary>
	private readonly record struct WaitingForJoinCode(string Name, ulong SteamId);
}
