using System.Buffers;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.Diagnostics;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.ListenServer;

/// <summary>pause requests, allowances and the broadcast state.</summary>
public partial class ListenServerHost
	{
	private void HandlePauseRequest(int peerId, ReadOnlySpan<byte> payload)
	{
		if (!_seatedPeers.TryGetValue(peerId, out int playerSlot))
		{
			_log.Write($"peer {peerId} asked to change the pause state before it was seated");
			return;
		}

		MatchPauseState request;
		try
		{
			request = MemoryPackSerializer.Deserialize<MatchPauseState>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a pause request that could not be read: {exception.Message}");
			return;
		}

		if (request.Paused) PauseMatch(peerId, playerSlot);
		else RequestResume(peerId, playerSlot);
	}

	/// <summary>
	///     Holds the match for one player. In a match shared with others every player may pause, but only within what it has
	///     left: a pause costs one of its three for the match and starts a minute of recovery, so a match cannot be held by
	///     one screen indefinitely. A match of one holds only the screen that asked, so it pays nothing and waits for
	///     nothing.
	/// </summary>
	/// <param name="peerId">The peer that asked.</param>
	/// <param name="playerSlot">The slot it is seated in.</param>
	private void PauseMatch(int peerId, int playerSlot)
	{
		if (_paused)
		{
			SendPauseState(peerId);
			SendPauseAllowance(peerId, string.Empty);
			return;
		}

		if (!IsSinglePlayerMatch)
		{
			int pausesRemaining = PausesRemainingFor(playerSlot);
			if (pausesRemaining <= 0)
			{
				_log.Write($"player {playerSlot} asked to pause the match but has used every pause it is allowed");
				SendPauseAllowance(peerId, MatchPauseAllowance.RefusalExhausted);
				return;
			}

			int cooldownSeconds = CooldownSecondsRemaining(playerSlot);
			if (cooldownSeconds > 0)
			{
				_log.Write($"player {playerSlot} asked to pause the match {cooldownSeconds} second(s) before its pause recovers");
				SendPauseAllowance(peerId, MatchPauseAllowance.RefusalCooldown);
				return;
			}

			_pausesRemainingPerPlayerSlot[playerSlot] = pausesRemaining - 1;
			_pauseCooldownEndsAtSeconds[playerSlot] = _lastTickSeconds + PauseCooldownSeconds;
		}

		_paused = true;
		_pausedByPlayerSlot = playerSlot;
		_resumeCountdownRemaining = 0;
		_log.Write(IsSinglePlayerMatch
			? $"player {playerSlot} paused a single-player match"
			: $"player {playerSlot} paused the match; it has {PausesRemainingFor(playerSlot)} pause(s) left and may pause again in {PauseCooldownSeconds} second(s)");

		BroadcastPauseState();
		SendPauseAllowance(peerId, string.Empty);
	}

	/// <summary>
	///     Starts the ready countdown that resumes a paused match. Any seated player may ask for it, and it then runs to the
	///     end for everyone rather than being cancelled, so no screen resumes a match the others have not been warned about.
	/// </summary>
	/// <param name="peerId">The peer that asked.</param>
	/// <param name="playerSlot">The slot it is seated in.</param>
	private void RequestResume(int peerId, int playerSlot)
	{
		if (!_paused)
		{
			SendPauseState(peerId);
			SendPauseAllowance(peerId, string.Empty);
			return;
		}

		if (_resumeCountdownRemaining > 0)
		{
			SendPauseState(peerId);
			return;
		}

		_resumeCountdownRemaining = ResumeCountdownSeconds;
		_log.Write($"player {playerSlot} asked for the match to resume; it resumes in {ResumeCountdownSeconds} second(s)");

		BroadcastPauseState();
		SendPauseAllowance(peerId, string.Empty);
	}

	private void ResumeMatch(string reason)
	{
		if (!_paused) return;

		_paused = false;
		_pausedByPlayerSlot = -1;
		_resumeCountdownRemaining = 0;
		_log.Write(reason);

		BroadcastPauseState();
	}

	private int PausesRemainingFor(int playerSlot)
	{
		if (playerSlot < 0 || playerSlot >= _pausesRemainingPerPlayerSlot.Length) return 0;
		return _pausesRemainingPerPlayerSlot[playerSlot];
	}

	private int CooldownSecondsRemaining(int playerSlot)
	{
		if (playerSlot < 0 || playerSlot >= _pauseCooldownEndsAtSeconds.Length) return 0;

		double remaining = _pauseCooldownEndsAtSeconds[playerSlot] - _lastTickSeconds;
		return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
	}

	/// <summary>
	///     Puts pausing back to how a match starts out: every player holds its full allowance, nothing is cooling down and
	///     the match is running. A hold belongs to the match that was stopped and not to the one starting in its place, so
	///     it is lifted here rather than inherited.
	/// </summary>
	private void ResetPauseAllowances()
	{
		for (int slot = 0; slot < _pausesRemainingPerPlayerSlot.Length; slot++)
		{
			_pausesRemainingPerPlayerSlot[slot] = PausesPerPlayerPerMatch;
			_pauseCooldownEndsAtSeconds[slot] = 0;
		}

		_paused = false;
		_pausedByPlayerSlot = -1;
		_resumeCountdownRemaining = 0;
	}

	private MatchPauseState BuildPauseState()
	{
		return new MatchPauseState
		{
			Paused = _paused,
			RequestedByPlayer = _paused ? _pausedByPlayerSlot : -1,
			ResumeCountdownSeconds = _paused && _resumeCountdownRemaining > 0 ? (int)Math.Ceiling(_resumeCountdownRemaining) : 0
		};
	}

	private void SendPauseState(int peerId) =>
		_transport.Send(peerId, NetworkFrameKind.MatchPaused, MemoryPackSerializer.Serialize(BuildPauseState()));

	/// <summary>
	///     Tells one player what it may still do about pausing, and why the pause it just asked for was refused when it was.
	/// </summary>
	/// <param name="peerId">The peer being answered.</param>
	/// <param name="rejection">Why a pause was refused, or empty when it was not.</param>
	private void SendPauseAllowance(int peerId, string rejection)
	{
		if (!_seatedPeers.TryGetValue(peerId, out int playerSlot)) return;

		var allowance = new MatchPauseAllowance
		{
			PausesRemaining = PausesRemainingFor(playerSlot),
			CooldownSecondsRemaining = CooldownSecondsRemaining(playerSlot),
			Rejection = rejection ?? string.Empty
		};

		_transport.Send(peerId, NetworkFrameKind.PauseAllowance, MemoryPackSerializer.Serialize(allowance));
	}

	private void BroadcastPauseState()
	{
		if (_seatedPeers.Count == 0) return;

		byte[] payload = MemoryPackSerializer.Serialize(BuildPauseState());

		int[] peerIds = new int[_seatedPeers.Count];
		_seatedPeers.Keys.CopyTo(peerIds, 0);
		for (int index = 0; index < peerIds.Length; index++) _transport.Send(peerIds[index], NetworkFrameKind.MatchPaused, payload);
	}
}
