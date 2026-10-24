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
	public const int RedThresholdMilliseconds = 180;
	public const int GreenThresholdMilliseconds = 60;
	public const int YellowThresholdMilliseconds = 120;

	private const int MaximumPendingSamples = 8;

	private readonly ulong[] _sentTimestamps = new ulong[MaximumPendingSamples];
	private readonly int[] _sentSequences = new int[MaximumPendingSamples];
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

			LastRoundTripMilliseconds = elapsed;
			SmoothedRoundTripMilliseconds = HasSample
				? SmoothedRoundTripMilliseconds + (elapsed - SmoothedRoundTripMilliseconds) * SmoothingFactor
				: elapsed;
			HasSample = true;
			SamplesReceived++;
			return;
		}
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
		_nextSequence = 1;
		_sampleTimer = SampleIntervalSeconds;
		HasSample = false;
		SmoothedRoundTripMilliseconds = 0f;
		LastRoundTripMilliseconds = 0f;
		SamplesReceived = 0;
		Array.Clear(_sentSequences);
		Array.Clear(_sentTimestamps);
	}
}
