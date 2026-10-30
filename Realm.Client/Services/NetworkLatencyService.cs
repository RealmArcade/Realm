using System;
using Godot;

namespace Realm.Client.Services;

public class NetworkLatencyService
{
	public const float SampleIntervalSeconds = 1f;

	/// <summary>
	///     How often a link is probed before it has been measured at all. The buffer is not sized from a measurement until
	///     three samples agree, so the match is drawn at the depth a match was fixed at for as long as that takes; at this
	///     interval the link is known about a second in rather than three, which is a second less of a remote link being
	///     discovered by stalling.
	/// </summary>
	public const float BootstrapSampleIntervalSeconds = 0.5f;

	public const float SmoothingFactor = 0.35f;

	public const float RecoverySmoothingFactor = 0.7f;

	public const int BootstrapSampleCount = 3;
	public const int RedThresholdMilliseconds = 180;
	public const int GreenThresholdMilliseconds = 60;
	public const int YellowThresholdMilliseconds = 120;

	private const int MaximumPendingSamples = 8;

	private const int FloorWindowSamples = 8;

	public const int MaximumPlausibleRoundTripMilliseconds = 2000;

	public const float StallFrameSeconds = 0.25f;

	private readonly ulong[] _sentTimestamps = new ulong[MaximumPendingSamples];
	private readonly int[] _sentSequences = new int[MaximumPendingSamples];
	private readonly int[] _bootstrapSamples = new int[BootstrapSampleCount];
	private readonly float[] _floorWindowMilliseconds = new float[FloorWindowSamples];
	private int _floorWindowCount;
	private int _floorWindowIndex;
	private int _bootstrapCount;
	private int _pendingCount;
	private int _nextSequence = 1;

	/// <summary>
	///     A probe is owed from the first tick rather than a sample interval in, because the interval before the first probe
	///     is a whole second of the match drawn at a depth nothing has measured.
	/// </summary>
	private float _sampleTimer;

	public bool HasSample { get; private set; }
	public float SmoothedRoundTripMilliseconds { get; private set; }
	public float LastRoundTripMilliseconds { get; private set; }

	/// <summary>
	///     How far each round trip lands from the smoothed one, smoothed in turn. A buffer has to cover this besides the
	///     trip itself: a link that varies by three sequences stalls a buffer that holds exactly its round trip.
	/// </summary>
	public float SmoothedJitterMilliseconds { get; private set; }

	/// <summary>
	///     The shortest round trip this link has shown recently, which is the figure the presentation buffer is sized from.
	///     A smoothed average is the wrong figure for a floor: it rises with a spike the buffer never had to cover, and the
	///     delay that spike bought is then held until the average has fallen back.
	/// </summary>
	public float RoundTripFloorMilliseconds { get; private set; }

	public int SamplesReceived { get; private set; }

	public void Update(float delta)
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null || !session.IsRunning) return;

		if (delta >= StallFrameSeconds) DiscardPendingSamples();

		_sampleTimer -= delta;
		if (_sampleTimer > 0f) return;

		_sampleTimer = HasSample ? SampleIntervalSeconds : BootstrapSampleIntervalSeconds;
		session.SendLatencyProbe(BeginSample());
	}

	public int BeginSample()
	{
		int sequence = _nextSequence++;
		if (_pendingCount == MaximumPendingSamples) RemoveOldestPending();

		_sentSequences[_pendingCount] = sequence;
		_sentTimestamps[_pendingCount] = Time.GetTicksMsec();
		_pendingCount++;
		return sequence;
	}

	public void OnResponse(int sequence, ulong arrivalMilliseconds)
	{
		for (int index = 0; index < _pendingCount; index++)
		{
			if (_sentSequences[index] != sequence) continue;

			ulong sentMilliseconds = _sentTimestamps[index];
			RemoveAt(index);

			if (arrivalMilliseconds < sentMilliseconds) return;

			ulong elapsed = arrivalMilliseconds - sentMilliseconds;
			if (elapsed > MaximumPlausibleRoundTripMilliseconds) return;

			LastRoundTripMilliseconds = elapsed;
			ApplySample(elapsed);
			SamplesReceived++;
			return;
		}
	}

	private void ApplySample(ulong elapsed)
	{
		RecordFloorSample(elapsed);

		if (!HasSample)
		{
			_bootstrapSamples[_bootstrapCount++] = (int)elapsed;
			int smallest = _bootstrapSamples[0];
			for (int index = 1; index < _bootstrapCount; index++)
			{
				if (_bootstrapSamples[index] < smallest) smallest = _bootstrapSamples[index];
			}

			SmoothedRoundTripMilliseconds = smallest;
			HasSample = _bootstrapCount >= BootstrapSampleCount;
			if (HasSample) ReportLinkToPresentationBuffer();
			return;
		}

		float jitter = MathF.Abs(elapsed - SmoothedRoundTripMilliseconds);
		SmoothedJitterMilliseconds += (jitter - SmoothedJitterMilliseconds) * SmoothingFactor;

		float factor = elapsed < SmoothedRoundTripMilliseconds ? RecoverySmoothingFactor : SmoothingFactor;
		SmoothedRoundTripMilliseconds += ((float)elapsed - SmoothedRoundTripMilliseconds) * factor;

		ReportLinkToPresentationBuffer();
	}

	/// <summary>
	///     Keeps the shortest of the last few samples. The window is a handful of probes wide so one spike cannot raise the
	///     buffer and one lucky sample cannot lower it, and the minimum is what a floor is: the least this link has needed
	///     lately.
	/// </summary>
	private void RecordFloorSample(float roundTripMilliseconds)
	{
		_floorWindowMilliseconds[_floorWindowIndex] = roundTripMilliseconds;
		_floorWindowIndex = (_floorWindowIndex + 1) % FloorWindowSamples;
		if (_floorWindowCount < FloorWindowSamples) _floorWindowCount++;

		float smallest = _floorWindowMilliseconds[0];
		for (int index = 1; index < _floorWindowCount; index++)
		{
			if (_floorWindowMilliseconds[index] < smallest) smallest = _floorWindowMilliseconds[index];
		}

		RoundTripFloorMilliseconds = smallest;
	}

	private void ReportLinkToPresentationBuffer()
	{
		// The authority this screen launched is a process on this machine: the frames still cross ENet and two message
		// loops, but there is no transit to absorb, so a measured floor here reserves delay for a link that is not there.
		if (GameContext.ServerSession is { IsLocalAuthority: true })
		{
			GameContext.SnapshotTimeline.ObserveLocalLink();
			return;
		}

		GameContext.SnapshotTimeline.ObserveLink(RoundTripFloorMilliseconds, SmoothedJitterMilliseconds);
	}

	private void DiscardPendingSamples()
	{
		_pendingCount = 0;
	}

	private void RemoveOldestPending()
	{
		RemoveAt(0);
	}

	private void RemoveAt(int index)
	{
		for (int move = index; move < _pendingCount - 1; move++)
		{
			_sentSequences[move] = _sentSequences[move + 1];
			_sentTimestamps[move] = _sentTimestamps[move + 1];
		}

		_pendingCount--;
	}

	public void Clear()
	{
		_pendingCount = 0;
		_bootstrapCount = 0;
		_nextSequence = 1;
		_sampleTimer = 0f;
		HasSample = false;
		SmoothedRoundTripMilliseconds = 0f;
		LastRoundTripMilliseconds = 0f;
		SmoothedJitterMilliseconds = 0f;
		RoundTripFloorMilliseconds = 0f;
		SamplesReceived = 0;
		_floorWindowCount = 0;
		_floorWindowIndex = 0;
		Array.Clear(_sentSequences);
		Array.Clear(_sentTimestamps);
		Array.Clear(_bootstrapSamples);
		Array.Clear(_floorWindowMilliseconds);
	}
}
