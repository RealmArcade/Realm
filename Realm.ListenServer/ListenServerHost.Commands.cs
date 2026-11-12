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

/// <summary>player commands and their outcomes.</summary>
public partial class ListenServerHost
	{
	private void HandleCommand(int peerId, ReadOnlySpan<byte> payload)
	{
		ServerSimulation? simulation = _simulation;
		if (simulation == null) return;

		NetworkCommand command;
		try
		{
			command = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
		{
			_log.Write($"peer {peerId} sent a command that could not be read: {exception.Message}");
			return;
		}

		// Orders handed over since the last tick are this tick's budget. The window is cleared by the tick that consumes
		// it rather than by the frame that happened to arrive before it: this process runs physics faster than the match
		// ticks, and clearing on the frame let a peer hand over three times the work a tick allows.
		int commandId = command.CommandId;
		_commandsThisTick.TryGetValue(peerId, out int commandsThisTick);
		if (commandsThisTick >= MaximumCommandsPerPeerPerTick)
		{
			if (commandsThisTick == MaximumCommandsPerPeerPerTick)
			{
				_log.Write($"peer {peerId} is above the {MaximumCommandsPerPeerPerTick} command(s) a tick allows; its further orders are refused");
			}

			_commandsThisTick[peerId] = commandsThisTick + 1;
			// A peer that floods keeps being refused, and a reader watching the match live has to see the budget doing its
			// job. The running total is restated at most once a second rather than once per refused order, so the report
			// cannot become the flood.
			_rateLimitedCommandCount++;
			long nowTicks = Environment.TickCount64;
			if (nowTicks >= _nextRateLimitReportTicks)
			{
				_nextRateLimitReportTicks = nowTicks + RateLimitReportIntervalMilliseconds;
				_log.Write($"the command rate limit has refused {_rateLimitedCommandCount} command(s) so far");
			}

			SendOutcome(peerId, commandId, false, "the command rate limit for one tick was exceeded");
			return;
		}

		_commandsThisTick[peerId] = commandsThisTick + 1;

		if (_paused)
		{
			_log.Write($"peer {peerId} command {commandId} '{command.CommandType}' refused: the match is paused");
			SendOutcome(peerId, commandId, false, "the match is paused");
			return;
		}

		if (!NetworkCommandLimits.TrySanitize(ref command, out string rejection))
		{
			_log.Write($"peer {peerId} command {commandId} refused: {rejection}");
			SendOutcome(peerId, commandId, false, rejection);
			return;
		}

		ServerCommandResult result = simulation.ApplyCommand(peerId, command);
		bool accepted = !result.HasRejection;

		_log.Write(accepted
			? $"peer {peerId} command {commandId} '{command.CommandType}' accepted"
			: $"peer {peerId} command {commandId} '{command.CommandType}' refused: {result.RejectionReason}");

		// A repeat of an order that was already answered is not answered again. The transport is reliable and ordered, so
		// the first answer arrived unless the connection is gone, and a reconnecting peer is described from scratch under
		// a new connection with no record to repeat. A second, negative answer took back a route preview this match had
		// already accepted.
		if (result.WasAlreadyAnswered) return;

		SendOutcome(peerId, commandId, accepted, result.RejectionReason);
	}

	private void SendOutcome(int peerId, int commandId, bool accepted, string rejectionReason)
	{
		var outcome = new CommandOutcome
		{
			CommandId = commandId,
			Accepted = accepted,
			RejectionReason = rejectionReason ?? string.Empty
		};

		_transport.Send(peerId, NetworkFrameKind.CommandOutcome, MemoryPackSerializer.Serialize(outcome));
	}
}
