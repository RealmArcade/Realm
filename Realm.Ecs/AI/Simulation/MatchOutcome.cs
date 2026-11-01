using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Realm.Ecs.AI.Simulation;

public class MatchSeatOutcome
{
	public int Seat { get; set; }
	public float GoldSpent { get; set; }
	public float DamageDealt { get; set; }
	public int UnitsDrafted { get; set; }
	public int HeroesPicked { get; set; }
	public int UpgradesBought { get; set; }
	public List<string> HeroPickIds { get; set; } = new();
}

public class MatchOutcome
{
	public string MapName { get; set; } = string.Empty;
	public int Seed { get; set; }
	public float MaxSeconds { get; set; }
	public bool Completed { get; set; }
	public string EndReason { get; set; } = string.Empty;
	public int WinnerSeat { get; set; } = -1;
	public float DurationSeconds { get; set; }
	public MatchSeatOutcome[] PerSeat { get; set; } = Array.Empty<MatchSeatOutcome>();

	public string ToJsonLine()
	{
		ArrayBufferWriter<byte> buffer = new(512);
		using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = false }))
		{
			writer.WriteStartObject();
			writer.WriteString("mapName", MapName);
			writer.WriteNumber("seed", Seed);
			writer.WriteNumber("maxSeconds", Sanitize(MaxSeconds));
			writer.WriteBoolean("completed", Completed);
			writer.WriteString("endReason", EndReason);
			writer.WriteNumber("winnerSeat", WinnerSeat);
			writer.WriteNumber("durationSeconds", Sanitize(DurationSeconds));
			writer.WriteStartArray("perSeat");
			for (int index = 0; index < PerSeat.Length; index++)
			{
				MatchSeatOutcome seat = PerSeat[index];
				writer.WriteStartObject();
				writer.WriteNumber("seat", seat.Seat);
				writer.WriteNumber("goldSpent", Sanitize(seat.GoldSpent));
				writer.WriteNumber("damageDealt", Sanitize(seat.DamageDealt));
				writer.WriteNumber("unitsDrafted", seat.UnitsDrafted);
				writer.WriteNumber("heroesPicked", seat.HeroesPicked);
				writer.WriteNumber("upgradesBought", seat.UpgradesBought);
				writer.WriteStartArray("heroPickIds");
				for (int pickIndex = 0; pickIndex < seat.HeroPickIds.Count; pickIndex++)
					writer.WriteStringValue(seat.HeroPickIds[pickIndex]);
				writer.WriteEndArray();
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	private static float Sanitize(float value) => float.IsFinite(value) ? value : 0f;
}
