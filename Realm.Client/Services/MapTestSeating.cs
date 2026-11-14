using System;
using System.Collections.Generic;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     The seats a test run of the open map hands out: which slot the local player takes and which slots the computer
///     players fill, exactly as the map's own test settings state them. The editor that warns about what a run will show
///     and the lobby that starts the run both read this one plan, so a randomized seat is the same seat in both places and
///     the warning is about the seat the player will actually be sitting in.
/// </summary>
public readonly struct MapTestSeatingPlan
{
	internal MapTestSeatingPlan(int humanSlot, int[] computerSlots, bool humanSlotIsCertain)
	{
		HumanSlot = humanSlot;
		ComputerSlots = computerSlots;
		HumanSlotIsCertain = humanSlotIsCertain;
	}

	/// <summary>The slot the local player takes.</summary>
	public int HumanSlot { get; }

	/// <summary>The slots the computer players take, in the order they are seated.</summary>
	public int[] ComputerSlots { get; }

	/// <summary>
	///     Whether the local player's slot is the one this plan states. A run that randomizes its slots without a seed
	///     rolls the seat when it starts, so the plan states the seat it would have rolled and answers false here.
	/// </summary>
	public bool HumanSlotIsCertain { get; }
}

/// <summary>
///     Reads the seats out of a map's test settings. Randomization is a shuffle of the playable slots, seeded by the map's
///     test seed when it states one, which is what makes a run repeatable; a map that randomizes without a seed gets a
///     fresh shuffle on every run.
/// </summary>
public static class MapTestSeating
{
	public static MapTestSeatingPlan Plan(MapInfoMetadata? mapProperties, int playablePlayerSlots)
	{
		int slotCount = Math.Clamp(playablePlayerSlots, MapInfoMetadata.MinPlayablePlayerSlots, MapInfoMetadata.MaxPlayablePlayerSlots);
		int requestedComputerCount = Math.Max(0, mapProperties?.TestAiPlayerCount ?? 1);
		int computerCount = Math.Min(requestedComputerCount, Math.Max(0, slotCount - 1));

		int? testSeed = mapProperties?.TestRandomSeed;
		bool rollSlots = mapProperties?.TestRandomizeSlots ?? false;

		if (!rollSlots)
		{
			var orderedSlots = new int[computerCount];
			for (int index = 0; index < computerCount; index++) orderedSlots[index] = 2 + index;
			return new MapTestSeatingPlan(1, orderedSlots, humanSlotIsCertain: true);
		}

		bool hasAuthoredSeed = testSeed.HasValue && testSeed.Value != 0;

		// 'Random' in this assembly is the project's own deterministic stream (Realm.Client/GlobalUsings.cs maps the name
		// to RandomProxy), which is the generator the lobby seated test runs with before this plan was extracted. Keeping
		// it is what makes a seeded run sit the player in exactly the seat it sat in before.
		var random = hasAuthoredSeed ? new Random(testSeed!.Value) : new Random();
		List<int> availableSlots = ShuffledSlots(slotCount, random);

		var computerSlots = new int[computerCount];
		for (int index = 0; index < computerCount; index++) computerSlots[index] = availableSlots[1 + index];

		return new MapTestSeatingPlan(availableSlots[0], computerSlots, humanSlotIsCertain: hasAuthoredSeed);
	}

	/// <summary>
	///     Reports whether a slot is one a test run could seat the local player in, which is the only slot whose units a run
	///     has to show through the shroud.
	/// </summary>
	/// <param name="playerIndex">The slot a unit belongs to.</param>
	/// <param name="plan">The seating plan the run will use.</param>
	/// <param name="playablePlayerSlots">The slots a match on the map may seat.</param>
	/// <returns>True when the local player could be sitting in that slot.</returns>
	public static bool CouldSeatLocalPlayer(int playerIndex, in MapTestSeatingPlan plan, int playablePlayerSlots)
	{
		if (plan.HumanSlotIsCertain) return playerIndex == plan.HumanSlot;

		int slotCount = Math.Clamp(playablePlayerSlots, MapInfoMetadata.MinPlayablePlayerSlots, MapInfoMetadata.MaxPlayablePlayerSlots);
		return playerIndex >= 1 && playerIndex <= slotCount;
	}

	private static List<int> ShuffledSlots(int slotCount, Random random)
	{
		var slots = new List<int>(slotCount);
		for (int slot = 1; slot <= slotCount; slot++) slots.Add(slot);

		int remaining = slots.Count;
		while (remaining > 1)
		{
			remaining--;
			int swapIndex = random.Next(remaining + 1);
			(slots[swapIndex], slots[remaining]) = (slots[remaining], slots[swapIndex]);
		}

		return slots;
	}
}
