using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Client.Services;

public class SnapshotTimeline
{
	public const int SamplesPerUnit = MaximumDelaySequences + 3;

	public const int MinimumDelaySequences = 2;

	public const int MaximumDelaySequences = 20;

	/// <summary>
	///     How deep the buffer may grow before a link has told this screen how long its round trip is. Inbound-only, this is
	///     the depth a match used to be fixed at; a measured link widens it as far as it needs.
	/// </summary>
	private const int InitialCeilingSequences = 8;

	private const float DelayEvaluationSeconds = 1f;

	private const float ResynchronizeSequences = MaximumDelaySequences;

	private const int MaximumDeepenPerWindow = 8;

	private const float MaximumRateCorrection = 0.15f;

	private const float RateCorrectionGain = 0.25f;

	private const int ShallowConfirmWindows = 2;

	private const float ShallowSurplusFraction = 0.5f;

	public const float MaximumExtrapolationSequences = 1f;

	public const float ExtrapolationLeadSequences = 1f;

	/// <summary>
	///     How long the lead takes to reach its full width, in seconds. It is a duration rather than a count of samples so a
	///     screen drawing at a different rate eases it in over the same moment instead of in an eighth of the time.
	/// </summary>
	private const float LeadRampSeconds = 0.2f;

	private const float DefaultFrameDeltaSeconds = 1f / 60f;

	/// <summary>
	///     The fraction of the time a run of descriptions was published over that it may arrive in and still count as a
	///     burst. A run the world arrived through at more than twice the rate it was written at was produced by the process
	///     publishing it, or by this screen not reading for a moment, rather than by the link: a deeper buffer brings a
	///     stalled publisher no closer and a screen that stopped reading its own pause no closer either.
	/// </summary>
	private const float ArrivalBurstFraction = 0.5f;

	public const float MaximumExtrapolationMeters = 0.75f;

	private readonly Vector3[] _positions = new Vector3[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly float[] _rotations = new float[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _sequences = new int[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _counts = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _heads = new int[EcsWorld.MAX_ENTITIES];
	private readonly float[] _leads = new float[EcsWorld.MAX_ENTITIES];
	private readonly int[] _orderConfirmedFrom = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _orderClearedAt = new int[EcsWorld.MAX_ENTITIES];

	private float _frameDeltaSeconds = DefaultFrameDeltaSeconds;

	/// <summary>
	///     How long this screen has been drawing, which is the clock a run of descriptions is measured against. It is a
	///     render clock rather than a wall clock because a frame that took a while is the stall this is trying to tell
	///     apart from a link that is late.
	/// </summary>
	private double _timelineSeconds;
	private double _lastArrivalTimelineSeconds;

	private float _cursor = -1f;
	private float _evaluationSeconds;
	private int _cleanWindowsInARow;
	private float _shallowestSurplusInWindow = float.MaxValue;
	private int _starvationsSinceArrival;
	private int _lateStarvationsInWindow;
	private int _largestStallGapInWindow;
	private int _delaySequences = MinimumDelaySequences;

	/// <summary>
	///     The depth this link was measured to need, which is its trip in one direction plus the variation around it. Depth
	///     is only ever given back down to this and no further: a link that varies keeps the room its variation needs, and
	///     one measured on a quiet minute does not have to discover its jitter again by stalling.
	/// </summary>
	private int _delayTargetSequences = MinimumDelaySequences;

	private int _delayFloorSequences = MinimumDelaySequences;
	private int _delayCeilingSequences = InitialCeilingSequences;
	private bool _isStarved;

	public SnapshotTimeline()
	{
		Array.Fill(_sequences, -1);
		Array.Fill(_orderConfirmedFrom, -1);
		Array.Fill(_orderClearedAt, int.MaxValue);
	}

	public float CursorSequence => _cursor;

	public int NewestSequence { get; private set; } = -1;

	public int DelaySequences => _delaySequences;

	public float DelayMilliseconds => _delaySequences * MatchTiming.SnapshotIntervalSeconds * 1000f;

	public int DelayFloorSequences => _delayFloorSequences;

	public int DelayCeilingSequences => _delayCeilingSequences;

	public int StarvationCount { get; private set; }

	public int ExtrapolatedSampleCount { get; private set; }

	public void Clear()
	{
		Array.Clear(_counts);
		Array.Clear(_heads);
		Array.Clear(_leads);
		Array.Fill(_sequences, -1);
		Array.Fill(_orderConfirmedFrom, -1);
		Array.Fill(_orderClearedAt, int.MaxValue);
		_cursor = -1f;
		NewestSequence = -1;
		_timelineSeconds = 0d;
		_lastArrivalTimelineSeconds = 0d;
		_delaySequences = MinimumDelaySequences;
		_delayTargetSequences = MinimumDelaySequences;
		_delayFloorSequences = MinimumDelaySequences;
		_delayCeilingSequences = InitialCeilingSequences;
		_isStarved = false;
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
		_orderConfirmedFrom[entityId] = -1;
		_orderClearedAt[entityId] = int.MaxValue;
	}

	/// <summary>
	///     Records whether the description just applied gave this unit a destination the match confirmed, and the sequence it
	///     was applied at. The lead a confirmed order draws is released by the moment the order is <em>drawn</em> to have
	///     ended rather than by the moment that end was described, so a unit stops being drawn ahead of itself exactly when
	///     the completion reaches the cursor.
	/// </summary>
	public void NoteConfirmedOrder(int entityId, bool confirmed)
	{
		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return;
		if (_counts[entityId] == 0) return;

		int sequence = _sequences[entityId * SamplesPerUnit + _heads[entityId]];
		if (sequence < 0) return;

		if (confirmed)
		{
			if (_orderConfirmedFrom[entityId] < 0) _orderConfirmedFrom[entityId] = sequence;
			_orderClearedAt[entityId] = int.MaxValue;
			return;
		}

		if (_orderConfirmedFrom[entityId] < 0) return;

		_orderClearedAt[entityId] = Math.Min(_orderClearedAt[entityId], sequence);
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

		int arrivalGap = NewestSequence >= 0 ? sequence - NewestSequence : 1;
		double publishedSpan = arrivalGap * MatchTiming.SnapshotIntervalSeconds;
		double elapsedSpan = NewestSequence >= 0 ? _timelineSeconds - _lastArrivalTimelineSeconds : publishedSpan;
		_lastArrivalTimelineSeconds = _timelineSeconds;

		// The gap a window records decides how it deepens the buffer, and a lost description is not lateness: a datagram
		// that did not arrive cannot be buffered back, and counting it as lateness ratcheted the delay to its ceiling on a
		// link that was only dropping the occasional frame.
		//
		// A run that arrived faster than it was written is the same story with a different cause: a process that stepped
		// several ticks in one call, or a screen that stopped reading for a moment and then drained what it had been sent.
		// Both look exactly like a link that fell behind, and neither is fixed by holding the cursor further back.
		if (NewestSequence >= 0 && _starvationsSinceArrival > 0)
		{
			bool arrivedAsABurst = elapsedSpan < publishedSpan * ArrivalBurstFraction;
			if (!arrivedAsABurst)
			{
				if (arrivalGap <= 1) _lateStarvationsInWindow += _starvationsSinceArrival;
				else if (arrivalGap >= 3 && arrivalGap - 1 > _largestStallGapInWindow) _largestStallGapInWindow = arrivalGap - 1;
			}

			_starvationsSinceArrival = 0;
		}

		NewestSequence = sequence;
		if (_cursor < 0f) _cursor = sequence - _delaySequences;
	}

	public void Advance(float delta)
	{
		// The lead a confirmed order draws is eased per second, so the frame's own step is what the sample below is told.
		if (delta > 0f && delta < 0.5f) _frameDeltaSeconds = delta;

		_timelineSeconds += delta;

		if (!(delta > 0f) || NewestSequence < 0) return;

		if (_cursor < 0f) _cursor = NewestSequence - _delaySequences;

		float target = NewestSequence - _delaySequences;
		float error = target - _cursor;

		// The descriptions arrived faster than this screen consumed them - a burst after a loss, or a process that
		// stopped for a moment. Easing across that would play the world slowly until it caught up.
		if (error > ResynchronizeSequences)
		{
			_cursor = target;
			_isStarved = false;
		}
		else
		{
			float rateCorrection = Math.Clamp(error * RateCorrectionGain, -MaximumRateCorrection, MaximumRateCorrection);
			float advance = delta / MatchTiming.SnapshotIntervalSeconds * (1f + rateCorrection);

			// Running out of descriptions is a stall to hold against, not a moment to move the clock backwards: the
			// clock stops where the world it can draw stops, so a unit holds its ground for the frames it takes to
			// catch up instead of jumping back to where it was a moment ago.
			if (_cursor + advance > NewestSequence)
			{
				if (!_isStarved)
				{
					StarvationCount++;
					_starvationsSinceArrival++;
					_isStarved = true;
				}

				_cursor = NewestSequence;
			}
			else
			{
				_cursor += advance;
				_isStarved = false;
			}
		}

		float surplus = NewestSequence - _cursor;

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

			// A starving window is short by one sequence, not by its whole depth: the cursor ran out at the newest
			// description this screen held, so the depth the window needed is the depth it had plus one. Measuring the
			// shortfall against the current delay instead doubled the buffer on every single starvation, which is what
			// pinned it to the ceiling on links that were never that late. A window that arrived after a genuinely wide
			// gap still deepens by that gap, because that is a stall no single extra sequence absorbs.
			int deepenBy = Math.Clamp(Math.Max(1, _largestStallGapInWindow), 1, MaximumDeepenPerWindow);
			_delaySequences = Math.Min(_delayCeilingSequences, _delaySequences + deepenBy);
		}
		else
		{
			// Depth is only taken back once the buffer held twice what the link needed for several windows running, so a
			// link that needs it keeps it while one deepened by a single bad second gets its headroom returned.
			if (_shallowestSurplusInWindow >= _delaySequences * ShallowSurplusFraction) _cleanWindowsInARow++;
			else _cleanWindowsInARow = 0;

			if (_cleanWindowsInARow >= ShallowConfirmWindows && _delaySequences > _delayTargetSequences)
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

	/// <summary>
	///     States how long this link's round trip is and how much it varies, which is what the buffer has to hold besides
	///     the trip itself. It is told rather than inferred because a buffer that only learns from running dry learns it
	///     by stalling: a link whose trip is three sequences long is declared too shallow before a frame has been drawn.
	/// </summary>
	/// <remarks>
	///     The floor is the trip in <em>one</em> direction: a description written now reaches this screen half a round trip
	///     from now, and that is all the buffer has to be behind by to have it in hand when the cursor reaches it. Sizing
	///     the floor from the whole round trip held every screen an extra half trip behind the match for nothing.
	/// </remarks>
	public void ObserveLink(float roundTripMilliseconds, float jitterMilliseconds)
	{
		if (!(roundTripMilliseconds > 0f)) return;

		float intervalMilliseconds = MatchTiming.SnapshotIntervalSeconds * 1000f;
		float oneWayMilliseconds = roundTripMilliseconds * 0.5f;
		int floor = (int)MathF.Ceiling(oneWayMilliseconds / intervalMilliseconds) + MinimumDelaySequences;
		floor = Math.Clamp(floor, MinimumDelaySequences, MaximumDelaySequences);

		// The ceiling is a limit on how deep this link may be held, not a depth it is held at: it never falls below the
		// depth a match was fixed at before links were measured, so a link that turns out to need more than its trip
		// still has the room to be given it.
		int jitterSequences = jitterMilliseconds > 0f ? (int)MathF.Ceiling(jitterMilliseconds / intervalMilliseconds) : 0;
		int deepestThisLinkNeeds = Math.Max(InitialCeilingSequences, floor) + jitterSequences + MinimumDelaySequences;
		int ceiling = Math.Clamp(deepestThisLinkNeeds, floor, MaximumDelaySequences);

		if (floor == _delayFloorSequences && ceiling == _delayCeilingSequences) return;

		// A floor that rises is met at once: drawing below what the link needs is the stall the floor exists to avoid. A
		// floor or a ceiling that falls is not applied here, because depth is only given back by the shallowing rule,
		// one sequence per two clean windows. Taking it back in one step leaves the cursor chasing a target several
		// sequences ahead, and the screen plays the match fast for as long as that takes to close.
		_delayFloorSequences = floor;
		_delayCeilingSequences = ceiling;

		// The depth this link needs is its trip plus the variation around it, not its trip alone. Holding only the trip
		// meant a link that varies by two sequences stalled first and learned about it by stalling, one starvation at a
		// time, however clearly the variation had already been measured.
		int neededSequences = Math.Min(_delayCeilingSequences, floor + jitterSequences);
		_delayTargetSequences = neededSequences;
		if (_delaySequences < neededSequences) _delaySequences = neededSequences;
	}

	/// <summary>
	///     States that this link has no transit to absorb, so nothing is reserved for one. It still keeps the room a stall
	///     needs: a screen that stopped drawing for a moment is the same problem on a loopback match as on a long link, and
	///     the deepening rule answers both.
	/// </summary>
	public void ObserveLocalLink()
	{
		if (_delayFloorSequences == MinimumDelaySequences && _delayCeilingSequences == InitialCeilingSequences) return;

		_delayFloorSequences = MinimumDelaySequences;
		_delayCeilingSequences = InitialCeilingSequences;
		_delayTargetSequences = MinimumDelaySequences;
		if (_delaySequences < MinimumDelaySequences) _delaySequences = MinimumDelaySequences;
	}

	public bool TrySample(int entityId, bool localOrderConfirmed, out Vector3 position, out float rotationY, out Vector3 velocity)
	{
		position = Vector3.Zero;
		rotationY = 0f;
		velocity = Vector3.Zero;

		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return false;

		int count = _counts[entityId];
		if (count == 0) return false;

		int head = _heads[entityId];
		int baseIndex = entityId * SamplesPerUnit;

		// An order this screen issued and the match confirmed is drawn at a render time of its own, one interval ahead of
		// the world around it, which is the interval of delay it masks for that unit. The lead is eased rather than switched
		// so neither the order nor its completion moves the unit. What decides whether it is drawn that way is the
		// description the cursor is showing, not the newest one held: a unit whose order is already described as finished
		// keeps its lead until the moment that finish is drawn.
		int drawnSequence = (int)MathF.Floor(_cursor);
		bool orderStillHeld = localOrderConfirmed
		                      || (_orderConfirmedFrom[entityId] >= 0 && drawnSequence <= _orderClearedAt[entityId]);

		float targetLead = orderStillHeld ? ExtrapolationLeadSequences : 0f;
		float leadStep = ExtrapolationLeadSequences * Math.Clamp(_frameDeltaSeconds / LeadRampSeconds, 0f, 1f);
		float lead = _leads[entityId];
		lead += Math.Clamp(targetLead - lead, -leadStep, leadStep);
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

			if (orderStillHeld && count >= 2)
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
