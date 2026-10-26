using System;
using Godot;

namespace Realm.Client.Services;

/// <summary>
/// Measures the round trip to the authority so the on-screen ping readout reflects the live transport rather than a
/// lobby-time estimate. The authority answers immediately, so the measurement needs no clock synchronisation.
/// </summary>
public class NetworkLatencyService
{
	public const float SampleIntervalSeconds = 1f;
	public const float SmoothingFactor = 0.35f;

	/// <summary>
	/// The share of the gap to a faster sample that is applied at once. A probe answered faster than the estimate is
	/// evidence the link is at least that good, and the estimate that sets the interpolation buffer should follow it
	/// quickly: with one smoothing factor for both directions a single stall-sized sample kept the client's rendered world
	/// crawling for ten seconds after the link itself had been answering in sixteen milliseconds.
	/// </summary>
	public const float RecoverySmoothingFactor = 0.7f;

	/// <summary>
	/// How many probes the estimate waits for before it is trusted. The first probes of a match can still span the map
	/// load on either end - one measured 1.6 s on a link that answers in 16 ms - so the estimate starts from the smallest
	/// of them, and the client keeps the default interpolation buffer until it has that many.
	/// </summary>
	public const int BootstrapSampleCount = 3;
	public const int RedThresholdMilliseconds = 180;
	public const int GreenThresholdMilliseconds = 60;
	public const int YellowThresholdMilliseconds = 120;

	private const int MaximumPendingSamples = 8;

	/// <summary>
	/// A round trip longer than this cannot describe a session anyone would play: a probe that took that long was held by
	/// a stall on one of the two ends, so its duration is discarded instead of being smoothed into the estimate that sets
	/// the client's interpolation buffer.
	/// </summary>
	public const int MaximumPlausibleRoundTripMilliseconds = 2000;

	/// <summary>
	/// A single frame this long means this client stopped processing, and every probe in flight across it measured the
	/// stall rather than the link. Headless clients spend seconds loading a map, and one of those frames used to be
	/// accepted as a 2.3 s round trip, which set the interpolation buffer to 3.4 s and left the client's rendered world
	/// crawling several metres behind the authority for the first ten seconds of the match.
	/// </summary>
	public const float StallFrameSeconds = 0.25f;

	private readonly ulong[] _sentTimestamps = new ulong[MaximumPendingSamples];
	private readonly int[] _sentSequences = new int[MaximumPendingSamples];
	private readonly int[] _bootstrapSamples = new int[BootstrapSampleCount];
	private int _bootstrapCount;
	private int _pendingCount;
	private int _nextSequence = 1;
	private float _sampleTimer = SampleIntervalSeconds;

	public bool HasSample { get; private set; }
	public float SmoothedRoundTripMilliseconds { get; private set; }
	public float LastRoundTripMilliseconds { get; private set; }
	public int SamplesReceived { get; private set; }

	public void Update(float delta)
	{
		if (GameContext.Host == null || !GameContext.Host.HasRemoteAuthority) return;

		if (delta >= StallFrameSeconds) DiscardPendingSamples();

		_sampleTimer -= delta;
		if (_sampleTimer > 0f) return;

		_sampleTimer = SampleIntervalSeconds;
		GameContext.Host.SendLatencyProbe(BeginSample());
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

	public void OnResponse(int sequence)
	{
		for (int index = 0; index < _pendingCount; index++)
		{
			if (_sentSequences[index] != sequence) continue;

			ulong elapsed = Time.GetTicksMsec() - _sentTimestamps[index];
			RemoveAt(index);

			if (elapsed > MaximumPlausibleRoundTripMilliseconds) return;

			LastRoundTripMilliseconds = elapsed;
			ApplySample(elapsed);
			SamplesReceived++;
			return;
		}
	}

	private void ApplySample(ulong elapsed)
	{
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
			return;
		}

		float factor = elapsed < SmoothedRoundTripMilliseconds ? RecoverySmoothingFactor : SmoothingFactor;
		SmoothedRoundTripMilliseconds += ((float)elapsed - SmoothedRoundTripMilliseconds) * factor;
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
		_sampleTimer = SampleIntervalSeconds;
		HasSample = false;
		SmoothedRoundTripMilliseconds = 0f;
		LastRoundTripMilliseconds = 0f;
		SamplesReceived = 0;
		Array.Clear(_sentSequences);
		Array.Clear(_sentTimestamps);
		Array.Clear(_bootstrapSamples);
	}
}
