using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public class SnapshotTimeline
{
	public const int SamplesPerUnit = MaximumDelaySequences + 3;

	public const int MinimumDelaySequences = 2;

	public const int MaximumDelaySequences = 8;

	private const float DelayEvaluationSeconds = 1f;

	private const float StarvationToleranceSequences = 0.25f;

	private const float ResynchronizeSequences = MaximumDelaySequences;

	private const float MaximumRateCorrection = 0.15f;

	private const float RateCorrectionGain = 0.25f;

	private const int ShallowConfirmWindows = 2;

	private const float ShallowSurplusFraction = 0.5f;

	public const float MaximumExtrapolationSequences = 1f;

	public const float ExtrapolationLeadSequences = 1f;

	private const float LeadRampPerSample = ExtrapolationLeadSequences / 6f;

	public const float MaximumExtrapolationMeters = 0.75f;

	private readonly Vector3[] _positions = new Vector3[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly float[] _rotations = new float[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _sequences = new int[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _counts = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _heads = new int[EcsWorld.MAX_ENTITIES];
	private readonly float[] _leads = new float[EcsWorld.MAX_ENTITIES];

	private float _cursor = -1f;
	private float _evaluationSeconds;
	private int _cleanWindowsInARow;
	private float _shallowestSurplusInWindow = float.MaxValue;
	private int _starvationsSinceArrival;
	private int _lateStarvationsInWindow;
	private int _largestStallGapInWindow;
	private int _delaySequences = MinimumDelaySequences;

	public SnapshotTimeline()
	{
		Array.Fill(_sequences, -1);
	}

	public float CursorSequence => _cursor;

	public int NewestSequence { get; private set; } = -1;

	public int DelaySequences => _delaySequences;

	public float DelayMilliseconds => _delaySequences * MatchTiming.SnapshotIntervalSeconds * 1000f;

	public int StarvationCount { get; private set; }

	public int ExtrapolatedSampleCount { get; private set; }

	public void Clear()
	{
		Array.Clear(_counts);
		Array.Clear(_heads);
		Array.Clear(_leads);
		Array.Fill(_sequences, -1);
		_cursor = -1f;
		NewestSequence = -1;
		_delaySequences = MinimumDelaySequences;
		_evaluationSeconds = 0f;
		_cleanWindowsInARow = 0;
		_shallowestSurplusInWindow = float.MaxValue;
		_starvationsSinceArrival = 0;
		_lateStarvationsInWindow = 0;
		_largestStallGapInWindow = 0;
		StarvationCount = 0;
		ExtrapolatedSampleCount = 0;
	}

	public void Reset(int entityId)
	{
		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return;

		_counts[entityId] = 0;
		_heads[entityId] = 0;
		_leads[entityId] = 0f;
	}

	public bool Record(int entityId, Vector3 position, float rotationY, int sequence)
	{
		if ((uint)entityId >= EcsWorld.MAX_ENTITIES || sequence < 0) return false;

		int count = _counts[entityId];
		int head = _heads[entityId];
		if (count > 0 && sequence <= _sequences[entityId * SamplesPerUnit + head]) return false;

		int slot;
		if (count == 0)
		{
			head = 0;
			slot = 0;
			count = 1;
		}
		else
		{
			head = (head + 1) % SamplesPerUnit;
			slot = head;
			if (count < SamplesPerUnit) count++;
		}

		_positions[entityId * SamplesPerUnit + slot] = position;
		_rotations[entityId * SamplesPerUnit + slot] = rotationY;
		_sequences[entityId * SamplesPerUnit + slot] = sequence;

		_heads[entityId] = head;
		_counts[entityId] = count;

		return true;
	}

	public void AdvanceTo(int sequence)
	{
		if (sequence <= NewestSequence) return;

		// The gap a window records decides how it deepens the buffer, and a lost description is not lateness: a datagram
		// that did not arrive cannot be buffered back, and counting it as lateness ratcheted the delay to its ceiling on a
		// link that was only dropping the occasional frame.
		if (NewestSequence >= 0 && _starvationsSinceArrival > 0)
		{
			int arrivalGap = sequence - NewestSequence;
			if (arrivalGap <= 1) _lateStarvationsInWindow += _starvationsSinceArrival;
			else if (arrivalGap >= 3 && arrivalGap - 1 > _largestStallGapInWindow) _largestStallGapInWindow = arrivalGap - 1;
			_starvationsSinceArrival = 0;
		}

		NewestSequence = sequence;
		if (_cursor < 0f) _cursor = sequence - _delaySequences;
	}

	public void Advance(float delta)
	{
		if (!(delta > 0f) || NewestSequence < 0) return;

		if (_cursor < 0f) _cursor = NewestSequence - _delaySequences;

		float target = NewestSequence - _delaySequences;
		float error = target - _cursor;

		// The descriptions arrived faster than this screen consumed them - a burst after a loss, or a process that
		// stopped for a moment. Easing across that would play the world slowly until it caught up.
		if (error > ResynchronizeSequences)
		{
			_cursor = target;
		}
		else
		{
			float rateCorrection = Math.Clamp(error * RateCorrectionGain, -MaximumRateCorrection, MaximumRateCorrection);
			_cursor += delta / MatchTiming.SnapshotIntervalSeconds * (1f + rateCorrection);
		}

		float surplus = NewestSequence - _cursor;
		if (surplus < -StarvationToleranceSequences)
		{
			// The cursor reached the end of what has arrived. Placing it at the newest description is the only option -
			// there is no world ahead of it to show - and running out is what tells the loop to hold more.
			StarvationCount++;
			_starvationsSinceArrival++;
			_cursor = NewestSequence;
			surplus = 0f;
		}

		if (surplus < _shallowestSurplusInWindow) _shallowestSurplusInWindow = surplus;

		_evaluationSeconds += delta;
		if (_evaluationSeconds < DelayEvaluationSeconds) return;

		bool linkFellBehind = _lateStarvationsInWindow > 0 || _largestStallGapInWindow > 0;
		if (linkFellBehind)
		{
			// A gap wider than this buffer can ever hold is not one its depth answers: a screen that spent a second building
			// what it had already been sent leaves exactly that gap, and deepening to the ceiling for it buys nothing while
			// the shallowing rule then takes as many clean windows as the ceiling has sequences to give it back.
			_cleanWindowsInARow = 0;
			int deepenBy = _largestStallGapInWindow > MaximumDelaySequences ? 1 : Math.Max(1, _largestStallGapInWindow);
			_delaySequences = Math.Min(MaximumDelaySequences, _delaySequences + deepenBy);
		}
		else
		{
			// Depth is only taken back once the buffer held twice what the link needed for several windows running, so a
			// link that needs it keeps it while one deepened by a single bad second gets its headroom returned.
			if (_shallowestSurplusInWindow >= _delaySequences * ShallowSurplusFraction) _cleanWindowsInARow++;
			else _cleanWindowsInARow = 0;

			if (_cleanWindowsInARow >= ShallowConfirmWindows && _delaySequences > MinimumDelaySequences)
			{
				_delaySequences--;
				_cleanWindowsInARow = 0;
			}
		}

		_evaluationSeconds = 0f;
		_shallowestSurplusInWindow = float.MaxValue;
		_lateStarvationsInWindow = 0;
		_largestStallGapInWindow = 0;
	}

	public bool TrySample(int entityId, bool mayExtrapolate, out Vector3 position, out float rotationY, out Vector3 velocity)
	{
		position = Vector3.Zero;
		rotationY = 0f;
		velocity = Vector3.Zero;

		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return false;

		int count = _counts[entityId];
		if (count == 0) return false;

		int head = _heads[entityId];
		int baseIndex = entityId * SamplesPerUnit;

		// A confirmed order is drawn at a render time of its own, one interval ahead of the world around it, and the lead is
		// eased rather than switched so neither the order nor its completion moves the unit.
		float targetLead = mayExtrapolate ? ExtrapolationLeadSequences : 0f;
		float lead = _leads[entityId];
		lead += Math.Clamp(targetLead - lead, -LeadRampPerSample, LeadRampPerSample);
		_leads[entityId] = lead;

		float cursor = _cursor + lead;

		int newestIndex = baseIndex + head;
		int newestSequence = _sequences[newestIndex];

		if (cursor >= newestSequence || count == 1)
		{
			position = _positions[newestIndex];
			rotationY = _rotations[newestIndex];

			if (count >= 2)
			{
				int previousIndex = baseIndex + (head - 1 + SamplesPerUnit) % SamplesPerUnit;
				velocity = ResolveVelocity(_positions[previousIndex], _positions[newestIndex], newestSequence - _sequences[previousIndex]);
			}

			if (mayExtrapolate && count >= 2)
			{
				Vector3 held = position;
				position = Extrapolate(held, velocity, cursor - newestSequence);
				if (position != held) ExtrapolatedSampleCount++;
			}

			return true;
		}

		for (int steps = 1; steps < count; steps++)
		{
			int olderOffset = (head - steps + SamplesPerUnit) % SamplesPerUnit;
			int olderIndex = baseIndex + olderOffset;
			int olderSequence = _sequences[olderIndex];
			if (olderSequence > cursor) continue;

			int newerIndex = baseIndex + (olderOffset + 1) % SamplesPerUnit;
			int newerSequence = _sequences[newerIndex];
			int span = newerSequence - olderSequence;
			float fraction = span > 0 ? Math.Clamp((cursor - olderSequence) / span, 0f, 1f) : 1f;

			// The sample and the speed come from the same pair of descriptions: a unit drawn between two of them is animated
			// at the speed it covered that span at, not at the speed of the newest pair held.
			position = Vector3.Lerp(_positions[olderIndex], _positions[newerIndex], fraction);
			rotationY = InterpolateAngle(_rotations[olderIndex], _rotations[newerIndex], fraction);
			velocity = ResolveVelocity(_positions[olderIndex], _positions[newerIndex], span);
			return true;
		}

		int oldestOffset = (head - count + 1 + SamplesPerUnit) % SamplesPerUnit;
		int oldestIndex = baseIndex + oldestOffset;
		position = _positions[oldestIndex];
		rotationY = _rotations[oldestIndex];
		velocity = ResolveVelocity(_positions[oldestIndex], _positions[baseIndex + (oldestOffset + 1) % SamplesPerUnit], _sequences[baseIndex + (oldestOffset + 1) % SamplesPerUnit] - _sequences[oldestIndex]);
		return true;
	}

	private static Vector3 Extrapolate(Vector3 newestPosition, Vector3 velocity, float sequencesAhead)
	{
		if (!(sequencesAhead > 0f) || sequencesAhead > MaximumExtrapolationSequences) return newestPosition;

		float speed = velocity.Length();
		if (speed < 0.01f) return newestPosition;

		float meters = MathF.Min(speed * sequencesAhead * MatchTiming.SnapshotIntervalSeconds, MaximumExtrapolationMeters);
		return newestPosition + velocity * (meters / speed);
	}

	private static Vector3 ResolveVelocity(Vector3 from, Vector3 to, int sequences)
	{
		if (sequences <= 0) return Vector3.Zero;

		float seconds = sequences * MatchTiming.SnapshotIntervalSeconds;
		if (!(seconds > 0f)) return Vector3.Zero;

		return (to - from) / seconds;
	}

	private static float InterpolateAngle(float from, float to, float fraction)
	{
		float difference = to - from;
		while (difference > MathF.PI) difference -= MathF.Tau;
		while (difference < -MathF.PI) difference += MathF.Tau;

		return from + difference * fraction;
	}
}
