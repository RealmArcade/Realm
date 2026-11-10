using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Shared.Diagnostics;

namespace Realm.Client.Testing;

public partial class MultiplayerSimulationHarness : Node
{
	private readonly StringBuilder _abilityDumpLine = new();
	private readonly StringBuilder _buildOptionDumpLine = new();

	private float _nextDraftAtSeconds = 9f;
	private int _draftsIssued;

	/// <summary>
	///     Drafts a hero onto one of this seat's circles, which is the one thing a smoke run never does: the circle is given
	///     up, the tile becomes a hero, and the freed identifier is handed to the replacement the map spawns. Everything the
	///     map had declared about the tile it replaced has to be gone by the time the next unit holds that identifier.
	/// </summary>
	private void TryDraftOnCircle()
	{
		if (_draftsIssued >= 2) return;

		GameHost? host = GameContext.Host;
		if (host == null || host.GameElapsedTime < _nextDraftAtSeconds) return;

		_nextDraftAtSeconds = host.GameElapsedTime + 6f;
		int controlledPlayer = GameContext.PerspectiveFilter.ControlledPlayerIndex;
		List<Unit3D> units = host.AllUnits;

		Unit3D? builder = null;
		Unit3D? circle = null;
		for (int index = 0; index < units.Count; index++)
		{
			Unit3D unit = units[index];
			if (!GodotObject.IsInstanceValid(unit) || unit.Player != controlledPlayer) continue;

			if (unit.UnitId is "unit/builder") builder = unit;
			else if (unit.UnitId.EndsWith("_element", StringComparison.Ordinal)) circle = unit;
		}

		if (builder == null || circle == null) return;

		string? offer = null;
		if (GameHost.TryGetUnitOrBuildingMetadata(builder.UnitId, out var builderMetadata) && builderMetadata.BuildOptions is { Length: > 0 } buildOptions)
		{
			for (int optionIndex = 0; optionIndex < buildOptions.Length; optionIndex++)
			{
				if (GameContext.RegistryService.IsAbilityHiddenForUnit(new UnitId(builder.Entity.Id), buildOptions[optionIndex])) continue;

				offer = buildOptions[optionIndex];
				break;
			}
		}

		if (offer == null) return;

		_draftsIssued++;
		GameLog.LogToConsole($"[DRAFT] placing '{offer}' on circle {circle.Entity.Id} '{circle.UnitId}' at {circle.GlobalPosition}.");
		host.PlaceBuildingAt(offer, circle.GlobalPosition);
	}

	/// <summary>
	///     Prints the command grid every replicated unit would offer right now, computed exactly as the panel computes it:
	///     the abilities the unit lists, and the build options its template declares, each dropped when the map withheld it
	///     from this unit. A grid offering more than the map let through is then a number in the log, not a screenshot.
	/// </summary>
	private void DumpAbilityStates(string stage)
	{
		if (GameContext.World is not { } world) return;

		GameHost? host = GameContext.Host;
		if (host == null) return;

		List<Unit3D> units = host.AllUnits;
		for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
		{
			Unit3D unit = units[unitIndex];
			if (!GodotObject.IsInstanceValid(unit)) continue;

			Entity entity = unit.Entity;
			if (!world.IsAlive(entity) || !world.Has<AbilityState>(entity)) continue;

			ref AbilityState abilityState = ref world.Get<AbilityState>(entity);
			if (abilityState.Count == 0) continue;

			_abilityDumpLine.Clear();
			int hiddenCount = 0;
			int visibleCount = 0;
			for (int slot = 0; slot < abilityState.Count; slot++)
			{
				string? abilityId = abilityState.Get(slot);
				if (string.IsNullOrEmpty(abilityId)) continue;

				if (GameContext.RegistryService.IsAbilityHiddenForUnit(new UnitId(entity.Id), abilityId))
				{
					hiddenCount++;
					continue;
				}

				if (visibleCount > 0) _abilityDumpLine.Append('|');
				_abilityDumpLine.Append(abilityId);
				visibleCount++;
			}

			_buildOptionDumpLine.Clear();
			int buildOptionTotal = 0;
			int buildOptionVisible = 0;
			if (GameHost.TryGetUnitOrBuildingMetadata(unit.UnitId, out var metadata) && metadata.BuildOptions is { Length: > 0 } buildOptions)
			{
				buildOptionTotal = buildOptions.Length;
				for (int optionIndex = 0; optionIndex < buildOptions.Length; optionIndex++)
				{
					string optionId = buildOptions[optionIndex];
					if (GameContext.RegistryService.IsAbilityHiddenForUnit(new UnitId(entity.Id), optionId)) continue;

					if (buildOptionVisible > 0) _buildOptionDumpLine.Append('|');
					_buildOptionDumpLine.Append(optionId);
					buildOptionVisible++;
				}
			}

			int serverEntityId = 0;
			if (world.Has<Realm.Ecs.Components.Core.NetworkMappingState>(GameContext.Simulation.WorldEntity))
			{
				NetworkMappingState localMapping = world.Get<NetworkMappingState>(GameContext.Simulation.WorldEntity);
				if (localMapping.ClientToServerEntityMap.TryGetValue(entity.Id, out int mappedServerId)) serverEntityId = mappedServerId;
			}

			GameContext.NetworkService.DescribeAbilityMirrorFor(serverEntityId, out int mirroredSlots, out int mirroredNames);

			GameLog.LogToConsole($"[ABILITY_DUMP] stage={stage} unit={entity.Id} server={serverEntityId} type={unit.UnitId} list={abilityState.Count} mirroredSlots={mirroredSlots} mirroredNames={mirroredNames} hidden={hiddenCount} visibleCount={visibleCount} visible={_abilityDumpLine} buildOptions={buildOptionTotal} buildVisible={buildOptionVisible} buildVisibleIds={_buildOptionDumpLine}");
		}
	}
}
