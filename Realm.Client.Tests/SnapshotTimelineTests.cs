using System;
using System.Numerics;
using GdUnit4;
using Realm.Client.Services;
using Realm.Shared.Network;

namespace Realm.Client.Tests;

[TestSuite]
public partial class SnapshotTimelineTests
{
	private const int Unit = 1;
	private const int Other = 2;

	private static float Step() => MatchTiming.SnapshotIntervalSeconds;

	private static bool Near(float actual, float expected, float tolerance = 0.001f) => MathF.Abs(actual - expected) <= tolerance;

	private static void AssertPosition(SnapshotTimeline timeline, int entityId, bool mayExtrapolate, float expectedX)
	{
		bool sampled = timeline.TrySample(entityId, mayExtrapolate, out Vector3 position, out _, out _);
		Assertions.AssertThat(sampled).OverrideFailureMessage($"no sample was taken for entity {entityId} at cursor {timeline.CursorSequence:F3}").IsTrue();
		Assertions.AssertThat(Near(position.X, expectedX)).OverrideFailureMessage($"entity {entityId} was drawn at x={position.X:F4}, expected {expectedX:F4}, cursor {timeline.CursorSequence:F3}").IsTrue();
	}

	[TestCase]
	public void ADescriptionOlderThanTheNewestHeldIsIgnored()
	{
		var timeline = new SnapshotTimeline();
		timeline.AdvanceTo(100);
		Assertions.AssertThat(timeline.Record(Unit, new Vector3(1f, 0f, 0f), 0f, 100)).IsTrue();
		Assertions.AssertThat(timeline.Record(Unit, new Vector3(2f, 0f, 0f), 0f, 100)).OverrideFailureMessage("a repeat of the sequence held was taken as a newer description").IsFalse();
		Assertions.AssertThat(timeline.Record(Unit, new Vector3(3f, 0f, 0f), 0f, 99)).OverrideFailureMessage("a description from behind the newest one was taken").IsFalse();

		AssertPosition(timeline, Unit, false, 1f);
	}

	[TestCase]
	public void AUnitNothingHasBeenDescribedForAnswersNoSample()
	{
		var timeline = new SnapshotTimeline();
		Assertions.AssertThat(timeline.TrySample(Unit, false, out _, out _, out _)).IsFalse();
	}

	[TestCase]
	public void SampleIsTakenBetweenTheDescriptionsEitherSideOfTheCursor()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 102);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.Record(Unit, new Vector3(10f, 0f, 0f), 0f, 102);
		timeline.AdvanceTo(102);

		timeline.Advance(Step() * 0.5f);

		float expected = (timeline.CursorSequence - 100f) / 2f * 10f;
		AssertPosition(timeline, Unit, false, expected);
	}

	[TestCase]
	public void AUnitWithOneDescriptionIsHeldAtItEvenWhenAnOrderIsConfirmed()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 105);
		timeline.Record(Unit, new Vector3(4f, 0f, 0f), 0f, 100);
		timeline.AdvanceTo(105);
		timeline.Advance(Step());

		AssertPosition(timeline, Unit, true, 4f);
	}

	[TestCase]
	public void GoingPastTheNewestDescriptionIsRefusedWithoutAConfirmedOrder()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 102);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.Record(Unit, new Vector3(10f, 0f, 0f), 0f, 101);
		timeline.AdvanceTo(102);

		timeline.Advance(Step() * 1.5f);

		Assertions.AssertThat(timeline.CursorSequence > 101f).OverrideFailureMessage($"the cursor stayed behind the newest description of the unit at {timeline.CursorSequence:F3}").IsTrue();
		AssertPosition(timeline, Unit, false, 10f);
	}

	[TestCase]
	public void AConfirmedOrderMayDrawTheStepPastTheNewestDescription()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 102);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.Record(Unit, new Vector3(10f, 0f, 0f), 0f, 101);
		timeline.AdvanceTo(102);

		timeline.Advance(Step());

		bool sampled = timeline.TrySample(Unit, true, out Vector3 position, out _, out _);
		Assertions.AssertThat(sampled).IsTrue();
		Assertions.AssertThat(position.X > 10f).OverrideFailureMessage($"a confirmed order was not drawn past the newest description: x={position.X:F4}").IsTrue();
		Assertions.AssertThat(timeline.ExtrapolatedSampleCount > 0).OverrideFailureMessage("the run did not count the sample it extrapolated").IsTrue();
	}

	[TestCase]
	public void TheStepPastTheNewestDescriptionIsBoundedByDistance()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 102);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.Record(Unit, new Vector3(10f, 0f, 0f), 0f, 101);
		timeline.AdvanceTo(102);

		timeline.Advance(Step());

		Assertions.AssertThat(timeline.TrySample(Unit, true, out Vector3 position, out _, out _)).IsTrue();
		float travelled = position.X - 10f;
		Assertions.AssertThat(travelled <= SnapshotTimeline.MaximumExtrapolationMeters + 0.001f)
			.OverrideFailureMessage($"a unit was drawn {travelled:F4} m past its newest description, over the {SnapshotTimeline.MaximumExtrapolationMeters:F2} m ceiling").IsTrue();
		Assertions.AssertThat(travelled > 0f).OverrideFailureMessage("nothing was extrapolated, so the ceiling was not what held it").IsTrue();
	}

	[TestCase]
	public void TheStepPastTheNewestDescriptionIsRefusedBeyondTheHorizon()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 105);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.Record(Unit, new Vector3(10f, 0f, 0f), 0f, 101);
		timeline.AdvanceTo(105);

		timeline.Advance(Step());

		Assertions.AssertThat(timeline.CursorSequence - 101f > SnapshotTimeline.MaximumExtrapolationSequences)
			.OverrideFailureMessage($"the cursor is only {timeline.CursorSequence - 101f:F3} sequences past the unit, inside the horizon").IsTrue();
		Assertions.AssertThat(timeline.TrySample(Unit, true, out Vector3 position, out _, out _)).IsTrue();
		Assertions.AssertThat(Near(position.X, 10f)).OverrideFailureMessage($"a unit was drawn {position.X - 10f:F4} m past a description the horizon forbids going beyond").IsTrue();
	}

	[TestCase]
	public void ResetForgetsWhatWasDescribedForAUnit()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Unit, new Vector3(3f, 0f, 0f), 0f, 100);
		timeline.Record(Other, new Vector3(0f, 0f, 0f), 0f, 101);

		timeline.Reset(Unit);

		Assertions.AssertThat(timeline.TrySample(Unit, false, out _, out _, out _)).IsFalse();
		Assertions.AssertThat(timeline.Record(Unit, new Vector3(7f, 0f, 0f), 0f, 99)).OverrideFailureMessage("a description older than the forgotten one was refused").IsTrue();
	}

	[TestCase]
	public void TheRingKeepsOnlyTheNewestDescriptions()
	{
		var timeline = new SnapshotTimeline();
		for (int sequence = 0; sequence < SnapshotTimeline.SamplesPerUnit * 2; sequence++)
		{
			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(sequence, 0f, 0f), 0f, sequence);
		}

		int oldestHeld = SnapshotTimeline.SamplesPerUnit * 2 - SnapshotTimeline.SamplesPerUnit;
		float expected = Math.Max(timeline.CursorSequence, oldestHeld);
		Assertions.AssertThat(timeline.TrySample(Unit, false, out Vector3 position, out _, out _)).IsTrue();
		Assertions.AssertThat(Near(position.X, expected)).OverrideFailureMessage($"the ring answered x={position.X:F3} where the oldest description it still holds is {oldestHeld} and the cursor is {timeline.CursorSequence:F3}").IsTrue();
	}

	[TestCase]
	public void AFeedThatKeepsUpSettlesAndStopsStarving()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.AdvanceTo(100);

		int sequence = 100;
		for (int tick = 0; tick < 60; tick++)
		{
			timeline.Advance(Step());
			sequence++;
			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		int settledStarvations = timeline.StarvationCount;
		for (int tick = 0; tick < 90; tick++)
		{
			timeline.Advance(Step());
			sequence++;
			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		Assertions.AssertThat(timeline.StarvationCount).OverrideFailureMessage($"a feed that never fell behind starved {timeline.StarvationCount} times").IsEqual(settledStarvations);
		Assertions.AssertThat(timeline.DelaySequences >= SnapshotTimeline.MinimumDelaySequences && timeline.DelaySequences <= SnapshotTimeline.MaximumDelaySequences)
			.OverrideFailureMessage($"the delay settled outside its bounds at {timeline.DelaySequences}").IsTrue();
	}

	[TestCase]
	public void RunningOutOfWorldDeepensTheDelayUpToTheMaximum()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.AdvanceTo(100);

		int sequence = 100;
		for (int round = 0; round < 12; round++)
		{
			for (int tick = 0; tick < 30; tick++) timeline.Advance(Step());

			sequence += 30;
			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		for (int tick = 0; tick < 30; tick++) timeline.Advance(Step());

		Assertions.AssertThat(timeline.StarvationCount > 0).OverrideFailureMessage("advancing through a world nothing describes never ran out").IsTrue();

		// A link that has not measured itself yet is held to the depth a match was fixed at, so that is the ceiling such a
		// window can reach: a screen cannot know how deep a link needs to be held before it has been told how long it is.
		Assertions.AssertThat(timeline.DelaySequences > SnapshotTimeline.MinimumDelaySequences)
			.OverrideFailureMessage($"a link that starves every window never deepened past {timeline.DelaySequences}").IsTrue();
		Assertions.AssertThat(timeline.DelaySequences).OverrideFailureMessage($"a link that starves every window held a delay of only {timeline.DelaySequences} where its ceiling is {timeline.DelayCeilingSequences}")
			.IsEqual(timeline.DelayCeilingSequences);
	}

	[TestCase]
	public void AWorldThatOvertakesTheBufferShallowsItAgain()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);
		timeline.AdvanceTo(100);

		int sequence = 100;
		for (int round = 0; round < 6; round++)
		{
			for (int tick = 0; tick < 30; tick++) timeline.Advance(Step());

			sequence += 30;
			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		int deepened = timeline.DelaySequences;
		Assertions.AssertThat(deepened > SnapshotTimeline.MinimumDelaySequences).OverrideFailureMessage("the delay never deepened, so there was nothing to shallow").IsTrue();

		for (int tick = 0; tick < 240; tick++)
		{
			timeline.Advance(Step());
			for (int burst = 0; burst < 3; burst++)
			{
				sequence++;
				timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
			}

			timeline.AdvanceTo(sequence);
		}

		Assertions.AssertThat(timeline.DelaySequences < deepened).OverrideFailureMessage($"a world arriving three times faster than it is rendered left the delay at {timeline.DelaySequences}").IsTrue();
	}

	[TestCase]
	public void ClearForgetsTheWholeMatch()
	{
		var timeline = new SnapshotTimeline();
		timeline.Record(Unit, new Vector3(5f, 0f, 0f), 0f, 4000);
		timeline.AdvanceTo(4000);
		timeline.Advance(Step());

		timeline.Clear();

		Assertions.AssertThat(timeline.NewestSequence).IsEqual(-1);
		Assertions.AssertThat(timeline.StarvationCount).IsEqual(0);
		Assertions.AssertThat(timeline.DelaySequences).IsEqual(SnapshotTimeline.MinimumDelaySequences);
		Assertions.AssertThat(timeline.TrySample(Unit, false, out _, out _, out _)).IsFalse();
		Assertions.AssertThat(timeline.Record(Unit, new Vector3(6f, 0f, 0f), 0f, 1)).OverrideFailureMessage("a new match's first sequence was refused against the previous match's").IsTrue();
	}

	[TestCase]
	public void AQuietTickStillAdvancesTheWorld()
	{
		var timeline = new SnapshotTimeline();
		timeline.AdvanceTo(100);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);

		int sequence = 100;
		for (int tick = 0; tick < 180; tick++)
		{
			timeline.Advance(Step());
			sequence++;
			timeline.AdvanceTo(sequence);
		}

		Assertions.AssertThat(timeline.StarvationCount)
			.OverrideFailureMessage($"a match whose world never changed starved {timeline.StarvationCount} times at cursor {timeline.CursorSequence:F3}")
			.IsEqual(0);
		Assertions.AssertThat(timeline.DelaySequences)
			.OverrideFailureMessage($"a quiet match deepened the buffer to {timeline.DelaySequences} for a link that never fell behind")
			.IsEqual(SnapshotTimeline.MinimumDelaySequences);
	}

	[TestCase]
	public void ALostDescriptionIsAbsorbedWithoutDeepeningTheBuffer()
	{
		var timeline = new SnapshotTimeline();
		timeline.AdvanceTo(100);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);

		int sequence = 100;
		for (int tick = 0; tick < 400; tick++)
		{
			timeline.Advance(Step());
			sequence++;
			if (tick % 5 == 4) continue;

			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		Assertions.AssertThat(timeline.StarvationCount)
			.OverrideFailureMessage($"a link dropping one description in five still ran out of world {timeline.StarvationCount} times at a delay of {timeline.DelaySequences}")
			.IsEqual(0);
		Assertions.AssertThat(timeline.DelaySequences)
			.OverrideFailureMessage($"a lost description deepened the buffer to {timeline.DelaySequences}, and no deeper buffer brings one back")
			.IsEqual(SnapshotTimeline.MinimumDelaySequences);
	}

	[TestCase]
	public void AWorldThatStopsArrivingForAFewTicksDeepensTheDelay()
	{
		var timeline = new SnapshotTimeline();
		timeline.AdvanceTo(100);
		timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, 100);

		int sequence = 100;
		for (int tick = 0; tick < 600; tick++)
		{
			timeline.Advance(Step());
			sequence++;
			if (tick % 20 >= 16) continue;

			timeline.AdvanceTo(sequence);
			timeline.Record(Unit, new Vector3(0f, 0f, 0f), 0f, sequence);
		}

		Assertions.AssertThat(timeline.DelaySequences > SnapshotTimeline.MinimumDelaySequences)
			.OverrideFailureMessage($"a world that stopped for four sequences at a time left the delay at {timeline.DelaySequences}")
			.IsTrue();
	}
}
