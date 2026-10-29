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

	/// <summary>
	///     How many clean windows in a row the delay must hold before it is shallowed by one sequence. A link that only
	///     just keeps up is clean for a window now and then, and shallowing on the first such window would take back the
	///     headroom it is about to need.
	/// </summary>
	private const int ShallowConfirmWindows = 2;

	/// <summary>
	///     How deep the buffer must have been throughout a window, as a fraction of the delay held, before the delay is
	///     considered deeper than the link needs. The cursor is driven towards "newest minus the delay", so on a healthy
	///     link the surplus settles at exactly the delay held: a rule that asked for a surplus above the delay could never
	///     be met, which is a delay that only ever grows. Half is the margin that a link which genuinely needs the depth
	///     cannot hold through a whole window.
	/// </summary>
	private const float ShallowSurplusFraction = 0.5f;

	/// <summary>
	///     How far past its newest description a unit with a confirmed order may be drawn, in sequences. This is a third
	///     of one snapshot interval: enough to cover the moment the cursor crosses a description that has not arrived yet,
	///     and short enough that the next description always lands before the guess could become visible as a wrong path.
	/// </summary>
	public const float MaximumExtrapolationSequences = 1f;

	/// <summary>
	///     The furthest a unit may be drawn past its newest description, in metres. A unit at this game's movement ceiling
	///     covers under half of it in the horizon above, so this only ever binds after a long gap, which is exactly when the
	///     guess should stop rather than run on.
	/// </summary>
	public const float MaximumExtrapolationMeters = 0.75f;

	private readonly Vector3[] _positions = new Vector3[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly float[] _rotations = new float[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _sequences = new int[EcsWorld.MAX_ENTITIES * SamplesPerUnit];
	private readonly int[] _counts = new int[EcsWorld.MAX_ENTITIES];
	private readonly int[] _heads = new int[EcsWorld.MAX_ENTITIES];

	private float _cursor = -1f;
	private float _evaluationSeconds;
	private int _starvationsInWindow;
	private int _cleanWindowsInARow;
	private float _shallowestSurplusInWindow = float.MaxValue;
	private int _largestArrivalGapInWindow = 1;
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
	///     How far behind the newest description this screen renders, in milliseconds. It is the figure a player asks
	///     about when the match feels late, and it is not the round trip: a low ping with a deep buffer is a screen that
	///     is holding descriptions it does not need to, which is a different problem from a slow link.
	/// </summary>
	public float DelayMilliseconds => _delaySequences * MatchTiming.SnapshotIntervalSeconds * 1000f;

	/// <summary>
	///     How many times the cursor reached the end of what had arrived. Deepening the delay is the answer to this
	///     happening, so it is reported rather than hidden.
	/// </summary>
	public int StarvationCount { get; private set; }

	/// <summary>The shallowest the buffer has been since the last time the delay was reconsidered, in sequences.</summary>
	public float ShallowestSurplusSequences { get; private set; }

	/// <summary>
	///     How many times a unit with a confirmed order was drawn past its newest description. A run that never reports
	///     this is running with a buffer deep enough that it never needed to guess, which is the state to aim for.
	/// </summary>
	public int ExtrapolatedSampleCount { get; private set; }

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
		_cleanWindowsInARow = 0;
		_shallowestSurplusInWindow = float.MaxValue;
		_largestArrivalGapInWindow = 1;
		StarvationCount = 0;
		ShallowestSurplusSequences = 0f;
		ExtrapolatedSampleCount = 0;
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
			// How many sequences passed between the description that was newest and this one. A gap above one is a tick
			// the authority did not get to describe or this screen did not get to read, which is the deficit the buffer is
			// there to cover and therefore what it is deepened by.
			if (NewestSequence >= 0)
			{
				int arrivalGap = sequence - NewestSequence;
				if (arrivalGap > _largestArrivalGapInWindow) _largestArrivalGapInWindow = arrivalGap;
			}

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
		if (_starvationsInWindow > 0)
		{
			// Deepened by the deficit this window actually showed rather than by one sequence: a burst the buffer could
			// not cover costs a second per missing sequence to answer, and the link is already running late for all of
			// them. A window that starved without a gap - descriptions arriving a little late rather than missing - has no
			// measured deficit, so it still deepens by one.
			_cleanWindowsInARow = 0;
			int deepenBy = Math.Max(1, _largestArrivalGapInWindow - 1);
			_delaySequences = Math.Min(MaximumDelaySequences, _delaySequences + deepenBy);
		}
		else
		{
			// The depth is only taken back once the buffer has held twice what the link ever needed for several windows
			// running: a link that needs the depth cannot hold that margin through a whole window, so it keeps it, while a
			// link that was deepened by one bad second gets its headroom returned instead of paying for it for the match.
			if (_shallowestSurplusInWindow >= _delaySequences * ShallowSurplusFraction) _cleanWindowsInARow++;
			else _cleanWindowsInARow = 0;

			if (_cleanWindowsInARow >= ShallowConfirmWindows && _delaySequences > MinimumDelaySequences)
			{
				_delaySequences--;
				_cleanWindowsInARow = 0;
			}
		}

		_evaluationSeconds = 0f;
		_starvationsInWindow = 0;
		_shallowestSurplusInWindow = float.MaxValue;
		_largestArrivalGapInWindow = 1;
	}

	/// <summary>
	///     The position, facing and velocity to draw one unit at this tick, where <paramref name="mayExtrapolate" /> says the
	///     match has confirmed an order for it and the short step past its newest description may be drawn.
	///     Extrapolating before the authority has confirmed anything would be the client inventing movement, which is what
	///     this whole presentation model exists not to do. After it has - the unit carries a destination the match itself
	///     published - the only unknown is the last third of a tick, and the direction it is filled in from is the one the
	///     authority was already moving the unit along rather than a route this client planned.
	///     Answers false for a unit this screen holds no description of, which is a unit that has not been heard about yet
	///     rather than one that is standing still.
	/// </summary>
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
		float cursor = _cursor;

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

		// The sample and the speed are taken from the same pair of descriptions, so a unit that is drawn between two of
		// them is animated at the speed it covered that span at rather than at the speed of the last pair held. Reading
		// the newest pair here made a unit that had just started moving animate a walk while it was still drawn standing
		// where it stopped, and made one that had just stopped report the speed of its last step.
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

	/// <summary>
	///     The short step past a description that a confirmed order allows. It is bounded twice - by the horizon in
	///     sequences and by the distance that horizon may cover - so a unit whose two samples are far apart, or whose last
	///     sample is already stale, is held rather than flung forward.
	/// </summary>
	private static Vector3 Extrapolate(Vector3 newestPosition, Vector3 velocity, float sequencesAhead)
	{
		if (!(sequencesAhead > 0f) || sequencesAhead > MaximumExtrapolationSequences) return newestPosition;

		float speed = velocity.Length();
		if (speed < 0.01f) return newestPosition;

		float meters = MathF.Min(speed * sequencesAhead * MatchTiming.SnapshotIntervalSeconds, MaximumExtrapolationMeters);
		return newestPosition + velocity * (meters / speed);
	}

	/// <summary>
	///     The speed two descriptions cover. It is not on the wire - two positions state it - and it is what the renderer
	///     animates a walk from and turns a unit along, so it is resolved from the pair the unit is drawn between.
	/// </summary>
	private static Vector3 ResolveVelocity(Vector3 from, Vector3 to, int sequences)
	{
		if (sequences <= 0) return Vector3.Zero;

		float seconds = sequences * MatchTiming.SnapshotIntervalSeconds;
		if (!(seconds > 0f)) return Vector3.Zero;

		return (to - from) / seconds;
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
