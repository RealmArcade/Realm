using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Realm.Shared.Diagnostics;
using Realm.Shared.Network;

namespace Realm.Client.Network;

/// <summary>
///     A link between this screen and the authority that behaves like a network instead of like loopback: every frame
///     waits the configured one-way delay before it is handed on, and a seeded share of the frames on a stream that never
///     retransmits is dropped. It sits above ENet rather than below it because a datagram relay is not a link ENet will
///     use: the connect handshake names the address the peer can be reached at, so the authority would answer the screen
///     directly and nothing would ever cross the relay.
///     <para>
///         Only the unreliable stream loses frames. A reliable frame is never lost on a real link, only delayed by the
///         retransmission a lost datagram costs, and dropping one here would invent a fault ENet does not have.
///     </para>
///     It is configured from the environment so only a harness that sets those variables pays for it; a normal screen
///     runs with no link at all and hands every frame straight on.
/// </summary>
public class SimulatedLink
{
	public const string RoundTripMillisecondsVariable = "REALM_SIMULATED_LINK_RTT_MS";

	public const string JitterMillisecondsVariable = "REALM_SIMULATED_LINK_JITTER_MS";

	public const string PacketLossPercentVariable = "REALM_SIMULATED_LINK_LOSS_PERCENT";

	public const string SeedVariable = "REALM_SIMULATED_LINK_SEED";

	private readonly int _oneWayDelayMilliseconds;
	private readonly int _jitterMilliseconds;
	private readonly int _unreliableLossPercent;
	private readonly Random _random;
	private readonly List<PendingFrame> _pendingFrames = new();
	private readonly long _delayTicks;
	private readonly long _jitterTicks;
	private long _droppedFrameCount;
	private long _deliveredFrameCount;

	public SimulatedLink(int roundTripMilliseconds, int jitterMilliseconds, int unreliableLossPercent, int seed)
	{
		_oneWayDelayMilliseconds = Math.Max(0, roundTripMilliseconds) / 2;
		_jitterMilliseconds = Math.Max(0, jitterMilliseconds);
		_unreliableLossPercent = Math.Clamp(unreliableLossPercent, 0, 100);
		_random = new Random(seed);
		_delayTicks = _oneWayDelayMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000L;
		_jitterTicks = _jitterMilliseconds * System.Diagnostics.Stopwatch.Frequency / 1000L;
	}

	public static SimulatedLink None { get; } = new(0, 0, 0, 0);

	public bool IsEnabled => _oneWayDelayMilliseconds > 0 || _unreliableLossPercent > 0;

	/// <summary>
	///     Whether frames on this link spend real time in transit, as opposed to being dropped. A link that only loses
	///     frames has no trip to absorb; one that delays them does, and a screen on it has to hold its buffer behind the
	///     match the way a screen talking to another machine does.
	/// </summary>
	public bool AddsTransit => _oneWayDelayMilliseconds > 0;

	public string Describe()
	{
		if (!IsEnabled) return "direct loopback, no added latency or loss";

		string jitter = _jitterMilliseconds > 0 ? $" +/- {_jitterMilliseconds} ms jitter" : string.Empty;
		return $"{_oneWayDelayMilliseconds * 2} ms round trip{jitter}, {_unreliableLossPercent}% seeded loss on the unreliable stream";
	}

	public long DroppedFrameCount => _droppedFrameCount;

	public long DeliveredFrameCount => _deliveredFrameCount;

	public static SimulatedLink FromEnvironment()
	{
		int roundTripMilliseconds = ReadInteger(RoundTripMillisecondsVariable);
		int jitterMilliseconds = ReadInteger(JitterMillisecondsVariable);
		int lossPercent = ReadInteger(PacketLossPercentVariable);
		int seed = ReadInteger(SeedVariable);
		if (roundTripMilliseconds <= 0 && jitterMilliseconds <= 0 && lossPercent <= 0) return None;

		return new SimulatedLink(roundTripMilliseconds, jitterMilliseconds, lossPercent, seed);
	}

	private static int ReadInteger(string name)
	{
		string? text = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(text)) return 0;
		return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
	}

	/// <summary>
	///     Takes one frame the screen is sending and either holds it for the link's delay or drops it. A reliable frame is
	///     never the one dropped.
	/// </summary>
	public bool TryQueueOutbound(NetworkFrameKind kind, byte[] payload, bool unreliable)
	{
		if (!IsEnabled) return false;
		if (unreliable && _unreliableLossPercent > 0 && _random.Next(100) < _unreliableLossPercent)
		{
			_droppedFrameCount++;
			return true;
		}

		Enqueue(true, kind, payload, unreliable);
		return true;
	}

	/// <summary>
	///     Takes one frame that arrived for this screen and holds it for the link's delay. Frames are held and dropped on
	///     this side too, so a round trip the screen measures is the round trip the link actually costs.
	/// </summary>
	public bool TryQueueInbound(NetworkFrameKind kind, byte[] payload, bool unreliable)
	{
		if (!IsEnabled) return false;
		// Descriptions arrive here, so this is the direction the loss has to be applied in: a link that only dropped what
		// the screen sent would lose acknowledgements and probes and never a description, which is not what a lossy link
		// costs a match.
		if (unreliable && _unreliableLossPercent > 0 && _random.Next(100) < _unreliableLossPercent)
		{
			_droppedFrameCount++;
			return true;
		}

		Enqueue(false, kind, payload, unreliable);
		return true;
	}

	private void Enqueue(bool outbound, NetworkFrameKind kind, byte[] payload, bool unreliable)
	{
		long jitterTicks = _jitterTicks > 0 ? _random.Next(_jitterMilliseconds + 1) * System.Diagnostics.Stopwatch.Frequency / 1000L : 0L;
		long dueTimestamp = System.Diagnostics.Stopwatch.GetTimestamp() + _delayTicks + jitterTicks;
		_pendingFrames.Add(new PendingFrame(outbound, kind, payload, unreliable, dueTimestamp));
	}

	/// <summary>
	///     Answers the oldest frame that has waited its delay, in the order the frames were taken. Constant delay keeps
	///     those due times in order, so the head is the only frame worth looking at.
	/// </summary>
	public bool TryDequeue(out bool outbound, out NetworkFrameKind kind, out byte[] payload, out bool unreliable)
	{
		outbound = false;
		kind = default;
		payload = Array.Empty<byte>();
		unreliable = false;

		if (_pendingFrames.Count == 0) return false;

		long now = System.Diagnostics.Stopwatch.GetTimestamp();
		int earliest = -1;
		for (int index = 0; index < _pendingFrames.Count; index++)
		{
			if (earliest >= 0 && _pendingFrames[index].DueTimestamp >= _pendingFrames[earliest].DueTimestamp) continue;
			earliest = index;
		}

		PendingFrame frame = _pendingFrames[earliest];
		if (frame.DueTimestamp > now) return false;

		_pendingFrames.RemoveAt(earliest);
		_deliveredFrameCount++;
		outbound = frame.Outbound;
		kind = frame.Kind;
		payload = frame.Payload;
		unreliable = frame.Unreliable;
		return true;
	}

	public void ObserveCounters()
	{
		if (!IsEnabled) return;
		LagProbe.SetCount(LagEvent.LinkFramesDelivered, _deliveredFrameCount);
		LagProbe.SetCount(LagEvent.LinkFramesDropped, _droppedFrameCount);
	}

	/// <summary>
	///     States what the link carried and what it threw away, so a run proves the conditions it was asked for were the
	///     ones it actually ran under rather than one it merely named.
	/// </summary>
	public void WriteStatistics(string path)
	{
		if (!IsEnabled || string.IsNullOrEmpty(path)) return;

		try
		{
			File.WriteAllText(
				path,
				$"delivered={_deliveredFrameCount}\ndropped={_droppedFrameCount}\none_way_ms={_oneWayDelayMilliseconds}\njitter_ms={_jitterMilliseconds}\nloss_percent={_unreliableLossPercent}");
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private readonly struct PendingFrame
	{
		public readonly bool Outbound;
		public readonly NetworkFrameKind Kind;
		public readonly byte[] Payload;
		public readonly bool Unreliable;
		public readonly long DueTimestamp;

		public PendingFrame(bool outbound, NetworkFrameKind kind, byte[] payload, bool unreliable, long dueTimestamp)
		{
			Outbound = outbound;
			Kind = kind;
			Payload = payload;
			Unreliable = unreliable;
			DueTimestamp = dueTimestamp;
		}
	}
}
