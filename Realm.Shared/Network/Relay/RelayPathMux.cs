using System;
using System.Diagnostics;
using System.Text;

namespace Realm.Shared.Network.Relay;

/// <summary>
///     What the match above the path layer receives: one inner datagram, exactly once, whichever path carried it and
///     however many copies were made of it.
/// </summary>
public interface IRelayPathSink
{
	/// <summary>Hands one inner datagram on to the transport that owns this connection.</summary>
	/// <param name="inner">The datagram the peer sent, as it was before the path layer sealed it.</param>
	/// <param name="delivery">The guarantee the peer stated for it.</param>
	void OnRelayDatagram(ReadOnlySpan<byte> inner, FrameDelivery delivery);
}

/// <summary>
///     The path layer of one relayed connection: everything that decides how a datagram reaches the other peer without the
///     match knowing how it got there. It seals each datagram end to end, puts it on the path measured best, keeps a second
///     copy on another path only while that copy is worth its bandwidth, drops every duplicate by sequence, and measures
///     each path with its own probes. A path appearing, failing or leaving changes which carrier is used and nothing above
///     this class: the transport's identity, the admission state and the snapshot baseline all survive it.
/// </summary>
public sealed class RelayPathMux : IDisposable
{
	/// <summary>The most paths one connection may have at once.</summary>
	public const int MaximumPaths = 8;

	private const int ProbeHistoryLength = 16;

	/// <summary>
	///     How long a path may go without answering a probe before this connection stops believing it carries anything. A
	///     UDP tunnel has no close to observe: the socket stays bound after the volunteer behind it has gone, so silence is
	///     the only evidence of a path that is gone, and a path that has not been answered in this long is never reported as
	///     carrying a peer.
	/// </summary>
	public const int ProbeSilenceTimeoutMilliseconds = 4000;

	/// <summary>How many probes a path must have sent before its silence is taken as evidence of anything.</summary>
	public const int MinimumProbesBeforeSilenceCounts = 3;

	private readonly RelayConnectionToken _token;
	private readonly RelayDatagramCipher _cipher;
	private readonly RelayDuplicateWindow _duplicates = new();
	private readonly RelayPath[] _paths = new RelayPath[MaximumPaths];
	private readonly byte[] _sendBuffer = new byte[RelayEnvelope.MaximumEncapsulatedDatagramBytes];
	private readonly byte[] _receiveBuffer = new byte[RelayEnvelope.MaximumEncapsulatedDatagramBytes];
	private readonly byte[] _innerBuffer = new byte[RelayEnvelope.MaximumInnerDatagramBytes];
	private readonly long _startedMilliseconds = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

	private readonly bool _duplicationEnabled;
	private readonly int _duplicationBudgetBytesPerSecond;

	private int _pathCount;
	private int _messageSequence;
	private int _probeSequence;
	private int _droppedWithoutPath;
	private double _duplicationAllowance;
	private long _lastBudgetMilliseconds;
	private bool _disposed;

	/// <summary>Creates the path layer of one relayed connection.</summary>
	/// <param name="token">The connection token both peers derived during the join.</param>
	/// <param name="cipher">The sealer and opener of this connection's directions.</param>
	/// <param name="duplicationEnabled">Whether a second path may carry a copy of a datagram the best path is struggling with.</param>
	/// <param name="duplicationBudgetBytesPerSecond">The most duplication this connection may spend, in bytes per second.</param>
	public RelayPathMux(RelayConnectionToken token, RelayDatagramCipher cipher, bool duplicationEnabled, int duplicationBudgetBytesPerSecond)
	{
		_token = token;
		_cipher = cipher;
		_duplicationEnabled = duplicationEnabled;
		_duplicationBudgetBytesPerSecond = Math.Max(0, duplicationBudgetBytesPerSecond);
		_duplicationAllowance = _duplicationBudgetBytesPerSecond;
		_lastBudgetMilliseconds = Now;
	}

	/// <summary>The connection this path layer belongs to.</summary>
	public RelayConnectionToken Token => _token;

	/// <summary>How many paths this connection can use right now.</summary>
	public int PathCount => _pathCount;

	/// <summary>The datagrams this connection has refused as duplicates, whichever paths carried them.</summary>
	public int DuplicateCount => _duplicates.DuplicateCount;

	/// <summary>
	///     The datagrams this connection could not move onto any path: none was left that a volunteer had answered on, the
	///     path it chose refused the datagram, or the datagram was wider than the layer carries. It is the one counter that
	///     tells a relayed connection that is merely slow from one that is going nowhere.
	/// </summary>
	public int DroppedWithoutPath => _droppedWithoutPath;

	/// <summary>Whether any path is open and able to carry a datagram.</summary>
	public bool HasOpenPath
	{
		get
		{
			for (int index = 0; index < _pathCount; index++)
			{
				if (_paths[index].IsLive) return true;
			}

			return false;
		}
	}

	private static long Now => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

	/// <summary>
	///     Adds a carrier as a path of this connection. The carrier's path identifier names it in every datagram it moves, so
	///     two paths through the same carrier are told apart by the identifier rather than by the endpoint behind it.
	/// </summary>
	/// <param name="carrier">The carrier to use as a path.</param>
	/// <returns>Whether the carrier was added, which it is not when the connection already holds its path ceiling.</returns>
	public bool AddPath(IRelayCarrier carrier)
	{
		if (_disposed || _pathCount >= MaximumPaths) return false;
		for (int index = 0; index < _pathCount; index++)
		{
			if (_paths[index].Carrier.PathId == carrier.PathId) return false;
		}

		_paths[_pathCount] = new RelayPath(carrier);
		_pathCount++;
		return true;
	}

	/// <summary>
	///     Removes a path and states whether it was there. A removed path is closed at once and its carrier disposed, so a
	///     volunteer that has gone costs this connection nothing after the removal.
	/// </summary>
	/// <param name="pathId">The path to remove.</param>
	/// <returns>Whether a path was removed.</returns>
	public bool RemovePath(byte pathId)
	{
		for (int index = 0; index < _pathCount; index++)
		{
			if (_paths[index].Carrier.PathId != pathId) continue;

			_paths[index].Carrier.Dispose();
			_paths[index] = _paths[_pathCount - 1];
			_paths[_pathCount - 1] = null!;
			_pathCount--;
			return true;
		}

		return false;
	}

	/// <summary>
	///     Removes every path, which is what a connection that has lost its volunteer does before it carries on over whatever
	///     path is left.
	/// </summary>
	public void RemoveAllPaths()
	{
		while (_pathCount > 0)
		{
			_pathCount--;
			_paths[_pathCount].Carrier.Dispose();
			_paths[_pathCount] = null!;
		}
	}

	/// <summary>
	///     Moves one inner datagram toward the other peer over the best measured path, and over a second path as well while
	///     that path's copy is worth its bandwidth. The sequence the duplicate window reads is assigned once here, so every
	///     copy of one datagram is the same datagram however many paths carried it.
	/// </summary>
	/// <param name="inner">The datagram for the other side, which is opaque to every carrier.</param>
	/// <param name="delivery">The guarantee the datagram needs.</param>
	/// <returns>How many copies were sent, or zero when no path could take the datagram.</returns>
	public int Send(ReadOnlySpan<byte> inner, FrameDelivery delivery)
	{
		if (_disposed) return 0;
		if (inner.Length > RelayEnvelope.MaximumInnerDatagramBytes)
		{
			_droppedWithoutPath++;
			return 0;
		}

		RelayPath? best = ChooseBestPath();
		if (best == null)
		{
			_droppedWithoutPath++;
			return 0;
		}

		_messageSequence = (_messageSequence + 1) & RelayEnvelope.MessageSequenceMask;
		byte deliveryFlag = RelayEnvelope.DeliveryFlag(delivery, probe: false);

		int copies = SendOnPath(best, deliveryFlag, inner) ? 1 : 0;
		if (copies == 0)
		{
			_droppedWithoutPath++;
			return 0;
		}

		if (!_duplicationEnabled || _pathCount < 2) return copies;

		RefillDuplicationBudget();
		if (!RelayPathScoring.ShouldDuplicate(best.Statistics, _duplicationAllowance >= RelayEnvelope.MaximumEncapsulatedDatagramBytes)) return copies;

		RelayPath? second = ChooseSecondPath(best);
		if (second == null) return copies;

		if (!SendOnPath(second, deliveryFlag, inner)) return copies;

		_duplicationAllowance = Math.Max(0, _duplicationAllowance - inner.Length - RelayEnvelope.HeaderBytes);
		return copies + 1;
	}

	/// <summary>
	///     Takes every datagram that has arrived on every path, proves it, drops the copies the connection already has, and
	///     hands the rest to the sink in the order this connection first saw them. Probes are answered and measured here and
	///     are never handed on.
	/// </summary>
	/// <param name="sink">What receives the inner datagrams.</param>
	public void Poll(IRelayPathSink sink)
	{
		if (_disposed) return;

		long now = Now;
		for (int index = 0; index < _pathCount; index++)
		{
			RelayPath path = _paths[index];
			while (path.Carrier.TryReceive(_receiveBuffer, out int length))
			{
				if (!_cipher.TryOpen(_receiveBuffer.AsSpan(0, length), _innerBuffer, out RelayPacketHeader header, out int innerLength))
				{
					path.Statistics.Rejected++;
					continue;
				}

				if (header.PathId != path.Carrier.PathId)
				{
					path.Statistics.Rejected++;
					continue;
				}

				if (header.IsProbe)
				{
					HandleProbe(path, header, now);
					continue;
				}

				path.Statistics.Received++;
				if (!_duplicates.TryAccept(header.MessageSequence))
				{
					path.Statistics.Duplicates++;
					continue;
				}

				sink.OnRelayDatagram(_innerBuffer.AsSpan(0, innerLength), header.Delivery);
			}

			SendProbeIfDue(path, now);
			RetireIfSilent(path, now);
			DecayIfStale(path, now);
		}
	}

	/// <summary>
	///     States each path, what it measured and what it carried, in a shape two hosts' outputs can be compared line by
	///     line. It is the log's and the diagnostics page's whole view of the path layer.
	/// </summary>
	/// <returns>One line per path, and a summary line for the connection.</returns>
	public string Describe()
	{
		var builder = new StringBuilder();
		builder.Append("relay token=").Append(_token.ToHex())
			.Append(" paths=").Append(_pathCount)
			.Append(" duplicates=").Append(_duplicates.DuplicateCount)
			.Append(" unmoved=").Append(_droppedWithoutPath)
			.Append(" best=");

		RelayPath? best = ChooseBestPath();
		builder.Append(best == null ? "none" : best.Carrier.PathId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');

		for (int index = 0; index < _pathCount; index++)
		{
			RelayPath path = _paths[index];
			RelayPathStatistics statistics = path.Statistics;
			RelayCarrierTraffic traffic = path.Carrier.Traffic;

			builder.Append("relay path=").Append(path.Carrier.PathId)
				.Append(" carrier=").Append(path.Carrier.Capabilities.Kind)
				.Append(" open=").Append(path.Carrier.IsOpen)
				.Append(" live=").Append(path.IsLive)
				.Append(" rtt=").Append(statistics.RoundTripMilliseconds).Append("ms")
				.Append(" jitter=").Append(statistics.JitterMilliseconds).Append("ms")
				.Append(" loss=").Append(statistics.LossPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('%')
				.Append(" sent=").Append(statistics.Sent)
				.Append(" received=").Append(statistics.Received)
				.Append(" duplicates=").Append(statistics.Duplicates)
				.Append(" dropped=").Append(statistics.Dropped)
				.Append(" rejected=").Append(statistics.Rejected)
				.Append(" bytes=").Append(traffic.Bytes)
				.Append('\n');
		}

		return builder.ToString();
	}

	/// <summary>States what one path measured, or null when this connection holds no path under that identifier.</summary>
	/// <param name="pathId">The path to describe.</param>
	/// <returns>The path's measurements, or null.</returns>
	public RelayPathStatistics? StatisticsOf(byte pathId)
	{
		for (int index = 0; index < _pathCount; index++)
		{
			if (_paths[index].Carrier.PathId == pathId) return _paths[index].Statistics;
		}

		return null;
	}

	/// <summary>
	///     What the path currently carrying this connection measured, or null when no path is left that a volunteer has
	///     answered on. A screen reads this to say what a relayed connection costs instead of promising it is as good as a
	///     direct one.
	/// </summary>
	public RelayPathSample? BestPath
	{
		get
		{
			RelayPath? best = ChooseBestPath();
			return best == null ? null : best.Statistics.Sample(best.Carrier.PathId);
		}
	}

	/// <summary>States the identifier of every path this connection holds, which is what the diagnostics page lists.</summary>
	/// <returns>The path identifiers in the order they were added.</returns>
	public byte[] PathIds()
	{
		var identifiers = new byte[_pathCount];
		for (int index = 0; index < _pathCount; index++) identifiers[index] = _paths[index].Carrier.PathId;
		return identifiers;
	}

	private RelayPath? ChooseBestPath()
	{
		RelayPath? best = null;
		for (int index = 0; index < _pathCount; index++)
		{
			RelayPath path = _paths[index];
			if (!path.IsLive) continue;
			if (!RelayPathScoring.IsBetterThan(path.Statistics, best?.Statistics)) continue;

			best = path;
		}

		return best;
	}

	private RelayPath? ChooseSecondPath(RelayPath best)
	{
		RelayPath? second = null;
		for (int index = 0; index < _pathCount; index++)
		{
			RelayPath path = _paths[index];
			if (ReferenceEquals(path, best) || !path.IsLive) continue;
			if (!RelayPathScoring.IsBetterThan(path.Statistics, second?.Statistics)) continue;

			second = path;
		}

		return second;
	}

	private bool SendOnPath(RelayPath path, byte deliveryFlag, ReadOnlySpan<byte> inner)
	{
		path.PathSequence = (path.PathSequence + 1) & RelayEnvelope.PathSequenceMask;
		int length = _cipher.Seal(_sendBuffer, _token, path.Carrier.PathId, deliveryFlag, _messageSequence, path.PathSequence, inner);
		if (!path.Carrier.TrySend(_sendBuffer.AsSpan(0, length)))
		{
			path.Statistics.Dropped++;
			return false;
		}

		path.Statistics.Sent++;
		path.Statistics.Bytes += length;
		path.LastSendMilliseconds = Now;
		return true;
	}

	private void HandleProbe(RelayPath path, RelayPacketHeader header, long now)
	{
		if (!header.IsProbeResponse)
		{
			SendProbeAnswer(path, header.MessageSequence);
			return;
		}

		if (!path.TryTakeProbe(header.MessageSequence, out long sentMilliseconds)) return;

		int roundTrip = (int)Math.Clamp(now - sentMilliseconds, 0, int.MaxValue);
		int jitter = path.Statistics.RoundTripMilliseconds == 0 ? 0 : Math.Abs(roundTrip - path.Statistics.RoundTripMilliseconds);
		path.Statistics.JitterMilliseconds = path.Statistics.ProbesAnswered == 0 ? jitter : (path.Statistics.JitterMilliseconds * 3 + jitter) / 4;
		path.Statistics.RoundTripMilliseconds = path.Statistics.ProbesAnswered == 0 ? roundTrip : (path.Statistics.RoundTripMilliseconds * 3 + roundTrip) / 4;
		path.Statistics.ProbesAnswered++;
		path.NoteProbeAnswered(now);
	}

	private void SendProbeIfDue(RelayPath path, long now)
	{
		if (!path.Carrier.IsOpen) return;
		if (now - path.LastProbeMilliseconds < UdpTunnelCarrier.ProbeInterval) return;

		path.LastProbeMilliseconds = now;
		_probeSequence = (_probeSequence + 1) & RelayEnvelope.MessageSequenceMask;
		path.PathSequence = (path.PathSequence + 1) & RelayEnvelope.PathSequenceMask;

		byte deliveryFlag = RelayEnvelope.DeliveryFlag(FrameDelivery.UnreliableOrdered, probe: true);
		int length = _cipher.Seal(_sendBuffer, _token, path.Carrier.PathId, deliveryFlag, _probeSequence, path.PathSequence, default);
		if (!path.Carrier.TrySend(_sendBuffer.AsSpan(0, length))) return;

		path.RememberProbe(_probeSequence, now);
		path.Statistics.ProbesSent++;
	}

	/// <summary>
	///     Stops this connection believing a path that has stopped answering. It is not a removal: the path keeps its place,
	///     its measurements and its carrier, and simply stops being chosen or reported as carrying anything. A connection
	///     whose last path retired reports no open path, which is what lets its owner say the peer is unreachable instead of
	///     pretending the volunteer is still there.
	/// </summary>
	/// <param name="path">The path to judge.</param>
	/// <param name="now">The clock the silence is measured against.</param>
	private static void RetireIfSilent(RelayPath path, long now)
	{
		if (path.HasFailed || path.Statistics.ProbesSent < MinimumProbesBeforeSilenceCounts) return;
		if (now - path.LastProbeAnswerMilliseconds < ProbeSilenceTimeoutMilliseconds) return;

		path.HasFailed = true;
	}

	private void SendProbeAnswer(RelayPath path, int probeSequence)
	{
		path.PathSequence = (path.PathSequence + 1) & RelayEnvelope.PathSequenceMask;

		byte deliveryFlag = RelayEnvelope.DeliveryFlag(FrameDelivery.UnreliableOrdered, probe: true, probeResponse: true);
		int length = _cipher.Seal(_sendBuffer, _token, path.Carrier.PathId, deliveryFlag, probeSequence, path.PathSequence, default);
		path.Carrier.TrySend(_sendBuffer.AsSpan(0, length));
	}

	private void RefillDuplicationBudget()
	{
		long now = Now;
		long elapsed = now - _lastBudgetMilliseconds;
		if (elapsed <= 0) return;

		_lastBudgetMilliseconds = now;
		_duplicationAllowance = Math.Min(_duplicationBudgetBytesPerSecond, _duplicationAllowance + _duplicationBudgetBytesPerSecond * elapsed / 1000.0);
	}

	private static void DecayIfStale(RelayPath path, long now)
	{
		if (now - path.LastDecayMilliseconds < 30_000) return;

		path.LastDecayMilliseconds = now;
		path.Statistics.ProbesSent /= 2;
		path.Statistics.ProbesAnswered /= 2;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		RemoveAllPaths();
		_cipher.Dispose();
	}

	private sealed class RelayPath
	{
		private readonly int[] _probeSequences = new int[ProbeHistoryLength];
		private readonly long[] _probeSentMilliseconds = new long[ProbeHistoryLength];
		private int _probeCursor;

		public RelayPath(IRelayCarrier carrier)
		{
			Carrier = carrier;
			Statistics = new RelayPathStatistics();
			LastProbeMilliseconds = Now;
			LastDecayMilliseconds = Now;
			LastProbeAnswerMilliseconds = Now;
		}

		public IRelayCarrier Carrier { get; }

		public RelayPathStatistics Statistics { get; }

		/// <summary>Whether this path has been silent long enough that this connection no longer believes it carries anything.</summary>
		public bool HasFailed { get; set; }

		/// <summary>Whether this path is both open at its carrier and answered by the volunteer behind it.</summary>
		public bool IsLive => !HasFailed && Carrier.IsOpen;

		/// <summary>When this path was last answered, or when it was opened while it has never been answered.</summary>
		public long LastProbeAnswerMilliseconds { get; private set; }

		/// <summary>Records that the volunteer behind this path answered, which is the evidence that it is still there.</summary>
		/// <param name="now">The clock the answer arrived on.</param>
		public void NoteProbeAnswered(long now)
		{
			LastProbeAnswerMilliseconds = now;
			HasFailed = false;
		}

		public int PathSequence { get; set; }

		public long LastProbeMilliseconds { get; set; }

		public long LastDecayMilliseconds { get; set; }

		public long LastSendMilliseconds { get; set; }

		public void RememberProbe(int sequence, long now)
		{
			_probeSequences[_probeCursor] = sequence;
			_probeSentMilliseconds[_probeCursor] = now;
			_probeCursor = (_probeCursor + 1) % ProbeHistoryLength;
		}

		public bool TryTakeProbe(int sequence, out long sentMilliseconds)
		{
			for (int index = 0; index < ProbeHistoryLength; index++)
			{
				if (_probeSequences[index] != sequence) continue;

				sentMilliseconds = _probeSentMilliseconds[index];
				_probeSequences[index] = -1;
				return true;
			}

			sentMilliseconds = 0;
			return false;
		}
	}
}
