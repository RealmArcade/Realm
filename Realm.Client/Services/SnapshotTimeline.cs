using System;
using System.Numerics;
using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.Client.Services;

/// <summary>
///     The positions the authority has described, held per unit on the authority's own tick timeline and sampled at a
///     cursor that follows it from behind.
///     A description names the sequence of the tick it describes, and a sequence is a tick - the authority advances it
///     once per simulated tick - so a sequence is an exact place on the match clock. That is what makes buffering
///     possible at all: no wall clock has to be synchronised between the two processes, because the unit of time on this
///     side of the wire is already the sequence. The cursor advances at the match's real rate and is nudged towards
///     "the newest sequence minus the delay it currently holds", so a late description slows the cursor for a moment
///     instead of stalling it.
///     Sampling between two described positions is what removes the follower: a unit is drawn where the authority held
///     it at the moment being rendered, so it neither trails behind a smoothed target nor has to be caught up when the
///     screen falls behind. The delay is discovered rather than calculated - the loop deepens it when the cursor runs
///     off the end of what has arrived and shallows it when the buffer has been deeper than it needed for a while -
///     because a delay computed from a formula is only right on the link the formula was written for.
/// </summary>
public class SnapshotTimeline
{
	/// <summary>
	///     How many descriptions of one unit are kept. It has to cover the deepest delay plus one sample on either side
	///     of the cursor, which is why it is well above the delay bound rather than equal to it.
	/// </summary>
	public const int SamplesPerUnit = 10;

	/// <summary>
	///     The shallowest delay, in sequences, the buffer is ever rendered at. Two sequences is what a link that answers
	///     immediately needs to have one sample in hand while it renders between the previous one and the current one.
	/// </summary>
	public const int MinimumDelaySequences = 2;

	/// <summary>
	///     The deepest delay the buffer will hold. Beyond this the world is being shown late enough that the screen is
	///     answering a link nobody can play over, and a cursor that keeps falling back is worse than one that holds.
	/// </summary>
	public const int MaximumDelaySequences = 8;

	/// <summary>How often the delay is reconsidered, in seconds.</summary>
	private const float DelayEvaluationSeconds = 1f;

	/// <summary>
	///     How far the cursor may run past the newest description before it counts as having run out of world. A fraction
	///     of a sequence is normal - the cursor is a continuous time and descriptions arrive on tick boundaries.
	/// </summary>
	private const float StarvationToleranceSequences = 0.25f;

	/// <summary>
	///     How far behind its target the cursor may fall before it is placed rather than eased. A burst of descriptions
	///     applied in one frame, a stalled process, or a baseline that restarted the stream all move the target by more
	///     than easing can cover, and easing across that would play the world in slow motion for a second.
	/// </summary>
	private const float ResynchronizeSequences = 3f;

	/// <summary>
	///     The most the cursor's rate is bent to hold its place. A few percent is the difference between a world that
	///     keeps time and one that does not, and is far below what a player can see in a unit's speed.
	/// </summary>
	private const float MaximumRateCorrection = 0.05f;

	/// <summary>How strongly the gap to the target bends the cursor's rate.</summary>
	private const float RateCorrectionGain = 0.2f;

	private readonly Vector3[] _positions = new Vector3[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly float[] _rotations = new float[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _sequences = new int[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _counts = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _heads = new int[EcsWorld.MAX_ENTITIES];

	private float _cursor = -1f;
	private float _evaluationSeconds;
	private int _starvationsInWindow;
	private float _shallowestSurplusInWindow = float.MaxValue;
	private int _delaySequences = MinimumDelaySequences;

	public SnapshotTimeline()
	{
		Array.Fill(_sequences, -1);
	}

	/// <summary>The sequence being rendered, which is continuous rather than a whole tick.</summary>
	public float CursorSequence => _cursor;

	/// <summary>The newest sequence any description has been applied for, or nothing before the first one.</summary>
	public int NewestSequence { get; private set; } = -1;

	/// <summary>How far behind the newest description this screen renders, in sequences.</summary>
	public int DelaySequences => _delaySequences;

	/// <summary>
	///     How many times the cursor reached the end of what had arrived. Deepening the delay is the answer to this
	///     happening, so it is reported rather than hidden.
	/// </summary>
	public int StarvationCount { get; private set; }

	/// <summary>The shallowest the buffer has been since the last time the delay was reconsidered, in sequences.</summary>
	public float ShallowestSurplusSequences { get; private set; }

	public void Clear()
	{
		Array.Clear(_counts);
		Array.Clear(_heads);
		Array.Fill(_sequences, -1);
		_cursor = -1f;
		NewestSequence = -1;
		_delaySequences = MinimumDelaySequences;
		_evaluationSeconds = 0f;
		_starvationsInWindow = 0;
		_shallowestSurplusInWindow = float.MaxValue;
		StarvationCount = 0;
		ShallowestSurplusSequences = 0f;
	}

	/// <summary>
	///     Forgets what was described for one unit, which is what a discontinuity calls for: a unit that respawned, was
	///     placed by a script, or left this peer's sight and came back has no path between the two positions, and gliding
	///     across it would show the unit walking a distance it never walked.
	/// </summary>
	public void Reset(int entityId)
	{
		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return;

		_counts[entityId] = 0;
		_heads[entityId] = 0;
	}

	/// <summary>
	///     Adds the position the authority described for one unit, and returns whether it was taken. A description older
	///     than the newest one held for that unit is ignored, because a delta that arrives behind a baseline describes a
	///     world this screen has already moved past.
	/// </summary>
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

		if (sequence > NewestSequence)
		{
			NewestSequence = sequence;
			if (_cursor < 0f) _cursor = sequence - _delaySequences;
		}

		return true;
	}

	/// <summary>
	///     Advances the render cursor by one client tick. The cursor is what every sample is taken at, so this is the only
	///     thing that moves the world forward on this side of the wire.
	/// </summary>
	public void Advance(float delta)
	{
		if (!(delta > 0f) || NewestSequence < 0) return;

		if (_cursor < 0f) _cursor = NewestSequence - _delaySequences;

		float target = NewestSequence - _delaySequences;
		float error = target - _cursor;

		if (error > ResynchronizeSequences)
		{
			// The descriptions arrived faster than this screen consumed them - a burst after a loss, or a process that
			// stopped for a moment. Easing across that would play the world slowly until it caught up.
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
			// The cursor reached the end of what has arrived. Placing it back at the newest description is the only
			// option - there is no world ahead of it to show - and running out is what tells the loop to hold more.
			StarvationCount++;
			_starvationsInWindow++;
			_cursor = NewestSequence;
			surplus = 0f;
		}

		if (surplus < _shallowestSurplusInWindow) _shallowestSurplusInWindow = surplus;

		_evaluationSeconds += delta;
		if (_evaluationSeconds < DelayEvaluationSeconds) return;

		ShallowestSurplusSequences = _shallowestSurplusInWindow;
		if (_starvationsInWindow > 0 && _delaySequences < MaximumDelaySequences) _delaySequences++;
		else if (_starvationsInWindow == 0 && _shallowestSurplusInWindow >= _delaySequences && _delaySequences > MinimumDelaySequences) _delaySequences--;

		_evaluationSeconds = 0f;
		_starvationsInWindow = 0;
		_shallowestSurplusInWindow = float.MaxValue;
	}

	/// <summary>
	///     The position, facing and velocity to draw one unit at this tick, taken from the descriptions either side of
	///     the cursor. Answers false for a unit this screen holds no description of, which is a unit that has not been
	///     heard about yet rather than one that is standing still.
	/// </summary>
	public bool TrySample(int entityId, out Vector3 position, out float rotationY, out Vector3 velocity)
	{
		position = Vector3.Zero;
		rotationY = 0f;
		velocity = Vector3.Zero;

		if ((uint)entityId >= EcsWorld.MAX_ENTITIES) return false;

		int count = _counts[entityId];
		if (count == 0) return false;

		int head = _heads[entityId];
		int baseIndex = entityId * SamplesPerUnit;
		float cursor = _cursor;

		int newestIndex = baseIndex + head;
		int newestSequence = _sequences[newestIndex];

		velocity = ResolveVelocity(entityId, count, head);

		if (cursor >= newestSequence || count == 1)
		{
			position = _positions[newestIndex];
			rotationY = _rotations[newestIndex];
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
			float span = newerSequence - olderSequence;
			float fraction = span > 0 ? Math.Clamp((cursor - olderSequence) / span, 0f, 1f) : 1f;

			position = Vector3.Lerp(_positions[olderIndex], _positions[newerIndex], fraction);
			rotationY = InterpolateAngle(_rotations[olderIndex], _rotations[newerIndex], fraction);
			return true;
		}

		int oldestOffset = (head - count + 1 + SamplesPerUnit) % SamplesPerUnit;
		position = _positions[baseIndex + oldestOffset];
		rotationY = _rotations[baseIndex + oldestOffset];
		return true;
	}

	/// <summary>
	///     The speed the last two descriptions of a unit cover. It is not on the wire - two positions state it - and it is
	///     what the renderer animates a walk from.
	/// </summary>
	private Vector3 ResolveVelocity(int entityId, int count, int head)
	{
		if (count < 2) return Vector3.Zero;

		int baseIndex = entityId * SamplesPerUnit;
		int newestIndex = baseIndex + head;
		int previousIndex = baseIndex + (head - 1 + SamplesPerUnit) % SamplesPerUnit;

		int sequences = _sequences[newestIndex] - _sequences[previousIndex];
		if (sequences <= 0) return Vector3.Zero;

		float seconds = sequences * MatchTiming.SnapshotIntervalSeconds;
		if (!(seconds > 0f)) return Vector3.Zero;

		Vector3 displacement = _positions[newestIndex] - _positions[previousIndex];
		return displacement / seconds;
	}

	/// <summary>
	///     Turns from one facing to another the short way round, so a unit that crosses the seam between the last and the
	///     first radian turns through it rather than spinning the long way.
	/// </summary>
	private static float InterpolateAngle(float from, float to, float fraction)
	{
		float difference = to - from;
		while (difference > MathF.PI) difference -= MathF.Tau;
		while (difference < -MathF.PI) difference += MathF.Tau;

		return from + difference * fraction;
	}
}
