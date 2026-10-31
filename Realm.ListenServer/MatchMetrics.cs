using System;
using System.Collections.Generic;
using Realm.Ecs.AI.Simulation;

namespace Realm.ListenServer;

public class MatchMetrics
{
	public const int MaxSeats = 25;

	private readonly SeatCounters[] _seats = new SeatCounters[MaxSeats];

	public int WinnerSeat { get; private set; } = -1;
	public bool VictoryObserved { get; private set; }
	public bool MatchEnded { get; private set; }

	public void Reset()
	{
		Array.Clear(_seats, 0, _seats.Length);
		WinnerSeat = -1;
		VictoryObserved = false;
		MatchEnded = false;
	}

	public void RecordGoldSpent(int seat, float amount)
	{
		if (!IsTrackedSeat(seat) || amount <= 0f) return;
		_seats[seat].GoldSpent += amount;
	}

	public void RecordDamageDealt(int seat, float amount)
	{
		if (!IsTrackedSeat(seat) || amount <= 0f) return;
		_seats[seat].DamageDealt += amount;
	}

	public void RecordUnitSpawned(int seat)
	{
		if (!IsTrackedSeat(seat)) return;
		_seats[seat].UnitsDrafted++;
	}

	public void RecordHeroPick(int seat, string heroId)
	{
		if (!IsTrackedSeat(seat)) return;
		ref SeatCounters counters = ref _seats[seat];
		counters.HeroesPicked++;
		counters.HeroPickIds ??= new List<string>();
		counters.HeroPickIds.Add(heroId);
	}

	public void RecordUpgradeBought(int seat)
	{
		if (!IsTrackedSeat(seat)) return;
		_seats[seat].UpgradesBought++;
	}

	public void RecordVictory(int seat)
	{
		VictoryObserved = true;
		MatchEnded = true;
		if (!IsTrackedSeat(seat) || WinnerSeat >= 0) return;
		WinnerSeat = seat;
	}

	public void RecordMatchEnded()
	{
		MatchEnded = true;
	}

	public MatchOutcome BuildOutcome(
		string mapName,
		int seed,
		float maxSeconds,
		bool completed,
		string endReason,
		float durationSeconds,
		int seatCount)
	{
		int resolvedSeatCount = Math.Clamp(seatCount, 0, MaxSeats);
		MatchSeatOutcome[] perSeat = new MatchSeatOutcome[resolvedSeatCount];
		for (int seat = 0; seat < resolvedSeatCount; seat++)
		{
			ref SeatCounters counters = ref _seats[seat];
			perSeat[seat] = new MatchSeatOutcome
			{
				Seat = seat,
				GoldSpent = counters.GoldSpent,
				DamageDealt = counters.DamageDealt,
				UnitsDrafted = counters.UnitsDrafted,
				HeroesPicked = counters.HeroesPicked,
				UpgradesBought = counters.UpgradesBought,
				HeroPickIds = counters.HeroPickIds != null ? new List<string>(counters.HeroPickIds) : new List<string>()
			};
		}

		return new MatchOutcome
		{
			MapName = mapName,
			Seed = seed,
			MaxSeconds = maxSeconds,
			Completed = completed,
			EndReason = endReason,
			WinnerSeat = WinnerSeat,
			DurationSeconds = durationSeconds,
			PerSeat = perSeat
		};
	}

	private static bool IsTrackedSeat(int seat) => seat >= 0 && seat < MaxSeats;

	private struct SeatCounters
	{
		public float GoldSpent;
		public float DamageDealt;
		public int UnitsDrafted;
		public int HeroesPicked;
		public int UpgradesBought;
		public List<string>? HeroPickIds;
	}
}
