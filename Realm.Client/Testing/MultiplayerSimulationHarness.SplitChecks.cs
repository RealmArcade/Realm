using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness
{
	private const float SplitScenarioTimeoutSeconds = 120f;
	private const float SummaryPositionToleranceMeters = 15f;

	private async Task RunSplitScenarioAsync(LobbyManager lobby, GameHost gameHost)
	{
		var failures = new List<string>();
		var notes = new List<string>();

		bool unitsAppeared = await WaitUntilAsync(() => gameHost.AllUnits.Count > 0, SplitScenarioTimeoutSeconds);
		if (!unitsAppeared) failures.Add("no unit was ever described to this screen");
		notes.Add($"units on this screen after the match started: {gameHost.AllUnits.Count}");

		var seatUnitCounts = new Dictionary<int, int>();
		await WaitUntilAsync(() => CountSeatUnits(gameHost, _peerCount + _botCount, seatUnitCounts), SplitScenarioTimeoutSeconds);
		notes.Add($"units per seat: {DescribeSeatUnits(seatUnitCounts)}");

		int commandSlot = _peerCount + _botCount + 1;
		if (commandSlot > PlayableSlotCount)
		{
			failures.Add($"the run leaves no free seat to add a computer player to");
		}
		else
		{
			_splitUnitsBeforeCommand = gameHost.AllUnits.Count;
			notes.Add($"chat command '-addai {commandSlot}' sent with {_splitUnitsBeforeCommand} unit(s) on this screen");
			lobby.SendChatMessage(HostPlayerName, $"-addai {commandSlot}", false);

			bool unitsAppearedAfterCommand = await WaitUntilAsync(() => gameHost.AllUnits.Count > _splitUnitsBeforeCommand, SplitScenarioTimeoutSeconds);

			seatUnitCounts.Clear();
			CountSeatUnits(gameHost, PlayableSlotCount, seatUnitCounts);
			notes.Add($"units after the chat command: {gameHost.AllUnits.Count} (seats: {DescribeSeatUnits(seatUnitCounts)})");
			if (!unitsAppearedAfterCommand) failures.Add($"'-addai {commandSlot}' typed in chat never reached the map: no unit appeared on this screen afterwards");
		}

		WriteSplitReport(failures, notes);
	}

	private static bool CountSeatUnits(GameHost gameHost, int highestSeat, Dictionary<int, int> counts)
	{
		counts.Clear();
		for (int index = 0; index < gameHost.AllUnits.Count; index++)
		{
			Unit3D unit = gameHost.AllUnits[index];
			if (!GodotObject.IsInstanceValid(unit)) continue;

			counts.TryGetValue(unit.Player, out int existing);
			counts[unit.Player] = existing + 1;
		}

		for (int slot = 1; slot <= highestSeat; slot++)
		{
			if (!counts.TryGetValue(slot, out int units) || units == 0) return false;
		}

		return true;
	}

	private static string DescribeSeatUnits(Dictionary<int, int> counts)
	{
		var parts = new List<string>();
		foreach (var pair in counts)
		{
			parts.Add($"{pair.Key}={pair.Value}");
		}

		parts.Sort(StringComparer.Ordinal);
		return string.Join(", ", parts);
	}

	private void WriteSplitReport(List<string> failures, List<string> notes)
	{
		try
		{
			var payload = new
			{
				failures = failures.Count,
				verdict = failures.Count == 0 ? "PASS" : "FAIL",
				messages = failures,
				notes
			};

			File.WriteAllText(Path.Combine(_outputDirectory, "split_report.json"), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

			var text = new System.Text.StringBuilder();
			text.AppendLine("=== Match split checks ===");
			for (int index = 0; index < notes.Count; index++) text.AppendLine($"  note: {notes[index]}");
			text.AppendLine($"Failures: {failures.Count}");
			for (int index = 0; index < failures.Count; index++) text.AppendLine($"  - {failures[index]}");
			text.AppendLine($"Verdict: {(failures.Count == 0 ? "PASS" : "FAIL")}");
			File.WriteAllText(Path.Combine(_outputDirectory, "split_report.txt"), text.ToString());
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Could not write split_report.json: {exception.Message}");
		}
	}

	private void WriteWorldSummary(string fileName, string role)
	{
		try
		{
			GameHost? gameHost = GameContext.Host;
			EcsWorld? world = GameContext.World;
			if (gameHost == null || world == null) return;

			var units = new List<object>(gameHost.AllUnits.Count);
			for (int index = 0; index < gameHost.AllUnits.Count; index++)
			{
				Unit3D unit = gameHost.AllUnits[index];
				if (!GodotObject.IsInstanceValid(unit)) continue;
				if (!world.IsAlive(unit.Entity)) continue;

				float hp = world.TryGet<Health>(unit.Entity, out Health health) ? health.Current : 0f;
				float maxHp = world.TryGet<Health>(unit.Entity, out Health maxHealth) ? maxHealth.Max : 0f;
				units.Add(new
				{
					serverId = GameContext.NetworkService.GetServerEntityId(unit.Entity.Id),
					unitId = unit.UnitId,
					player = unit.Player,
					building = unit.IsBuilding,
					x = MathF.Round(unit.GlobalPosition.X, 3),
					y = MathF.Round(unit.GlobalPosition.Y, 3),
					z = MathF.Round(unit.GlobalPosition.Z, 3),
					hp = MathF.Round(hp, 3),
					maxHp = MathF.Round(maxHp, 3),
					nodeScale = MathF.Round(unit.Scale.Y, 3),
					modelScale = MathF.Round(unit.ModelNode is { } model ? model.Scale.Y : 0f, 3),
					bodyScale = world.Has<CollisionScale>(unit.Entity) ? MathF.Round(world.Get<CollisionScale>(unit.Entity).Value, 3) : -1f,
					bodyRadius = MathF.Round(unit.GetBaseObstacleRadius(), 3)
				});
			}

			units.Sort((left, right) => ReadSummaryServerId(left).CompareTo(ReadSummaryServerId(right)));

			var players = new List<object>();
			Entity worldEntity = GameContext.Simulation.WorldEntity;
			if (worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<ScriptPlayersState>(worldEntity))
			{
				ScriptPlayersState playersState = world.Get<ScriptPlayersState>(worldEntity);
				for (int slot = 1; slot < ScriptPlayersState.SlotCount; slot++)
				{
					ScriptPlayer player = playersState.Get(slot);
					if (!player.Active && !player.IsComputer && slot > _peerCount + _botCount + 1) continue;

					Entity playerEntity = GameContext.Simulation.GetPlayerEntity(slot);

					players.Add(new
				{
					slot,
					active = player.Active,
					computer = player.IsComputer,
					team = player.Team,
					gold = MathF.Round(player.Gold, 3),
					wood = MathF.Round(player.Wood, 3),
					stone = MathF.Round(player.Stone, 3),
					foodUsed = world.Has<PlayerPopulation>(playerEntity) ? world.Get<PlayerPopulation>(playerEntity).Current : 0,
					foodCap = world.Has<PlayerPopulation>(playerEntity) ? world.Get<PlayerPopulation>(playerEntity).Max : 0
				});
				}
			}

			var payload = new
			{
				role,
				peerIndex = _peerIndex,
				playerSlot = GameContext.ServerSession?.LocalPlayerSlot ?? -1,
				staticTexts = CountStaticTextLabels(gameHost),
				staticTextLabels = DescribeStaticTextLabels(gameHost),
				units,
				players
			};

			File.WriteAllText(Path.Combine(_outputDirectory, fileName), JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MP_HARNESS] Could not write {fileName}: {exception.Message}");
		}
	}

	private static int CountStaticTextLabels(GameHost gameHost)
	{
		return ReadStaticTextLabels(gameHost) is { } labels ? labels.Count : -1;
	}

	private static List<object> DescribeStaticTextLabels(GameHost gameHost)
	{
		var described = new List<object>();
		if (ReadStaticTextLabels(gameHost) is not { } labels) return described;

		foreach (System.Collections.DictionaryEntry entry in labels)
		{
			if (entry.Value is not Label3D label || !GodotObject.IsInstanceValid(label)) continue;

			described.Add(new
			{
				handle = (int)entry.Key,
				text = label.Text,
				x = MathF.Round(label.GlobalPosition.X, 2),
				y = MathF.Round(label.GlobalPosition.Y, 2),
				z = MathF.Round(label.GlobalPosition.Z, 2),
				fontSize = label.FontSize,
				visible = label.Visible
			});
		}

		return described;
	}

	private static System.Collections.IDictionary? ReadStaticTextLabels(GameHost gameHost)
	{
		System.Reflection.PropertyInfo? labels = typeof(GameHost).GetProperty("_staticTextLabels", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		return labels?.GetValue(gameHost) as System.Collections.IDictionary;
	}

	private static int ReadSummaryServerId(object unit)
	{
		var property = unit.GetType().GetProperty("serverId");
		return property?.GetValue(unit) as int? ?? 0;
	}
}
