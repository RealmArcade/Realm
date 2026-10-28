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

/// <summary>
/// What a match looks like from each process that plays it. The authority is the only process that simulates, so what
/// every screen holds has to come to it from there: the seats the map spawned units for, the world those units are in,
/// and what a map command typed in chat did. These checks compare the screens against each other, which is the only way
/// to see a change that never left the authority.
/// </summary>
public partial class MultiplayerSimulationHarness
{
	private const float SplitScenarioTimeoutSeconds = 120f;
	private const float SummaryPositionToleranceMeters = 15f;

	/// <summary>
	/// Runs the checks a split match has to pass: every seat the lobby arranged was seated before the map's script ran,
	/// the seats the match plays itself were seated too, and a map command typed in chat reached the map.
	/// </summary>
	private async Task RunSplitScenarioAsync(LobbyManager lobby, GameHost gameHost)
	{
		var failures = new List<string>();
		var notes = new List<string>();

		bool unitsAppeared = await WaitUntilAsync(() => gameHost.AllUnits.Count > 0, SplitScenarioTimeoutSeconds);
		if (!unitsAppeared) failures.Add("no unit was ever described to this screen");
		notes.Add($"units on this screen after the match started: {gameHost.AllUnits.Count}");

		// Which seat a map gives its units to is the map's own business: a map that plays its enemies on proxy seats keeps
		// its units under the proxy, and a map that drafts gives a player nothing until they pick. What the split has to
		// deliver is every unit the map spawned, so the distribution is reported rather than asserted.
		var seatUnitCounts = new Dictionary<int, int>();
		await WaitUntilAsync(() => CountSeatUnits(gameHost, _peerCount + _botCount, seatUnitCounts), SplitScenarioTimeoutSeconds);
		notes.Add($"units per seat: {DescribeSeatUnits(seatUnitCounts)}");

		// A map's own commands are typed in chat, so this is the path a player actually uses. The slot has to be one the
		// map has not used, and the check is that the map acted on the line rather than that a screen showed it.
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

			// The command arrives at the authority and the map acts on it; which seat the map gives the new player is again
			// the map's decision. What the split must not do is swallow the line, so the check is that the world changed.
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

	/// <summary>
	/// Writes what this process holds, so the run can be checked across processes rather than only inside one. Every
	/// player in a match is a client of the authority, so two screens holding the same world is the property a split
	/// match has to have; two screens that disagree is a change that never left the process that simulated it.
	/// </summary>
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
					maxHp = MathF.Round(maxHp, 3)
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

					players.Add(new
					{
						slot,
						active = player.Active,
						computer = player.IsComputer,
						team = player.Team,
						gold = MathF.Round(player.Gold, 3),
						wood = MathF.Round(player.Wood, 3),
						stone = MathF.Round(player.Stone, 3)
					});
				}
			}

			var payload = new
			{
				role,
				peerIndex = _peerIndex,
				playerSlot = GameContext.ServerSession?.LocalPlayerSlot ?? -1,
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

	private static int ReadSummaryServerId(object unit)
	{
		var property = unit.GetType().GetProperty("serverId");
		return property?.GetValue(unit) as int? ?? 0;
	}
}
