using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds active user input targeting, command modes, and selection states.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct InputState
{
	public int SpellStringId;
	public int CommandStringId;
	public int BuildingStringId;
	public int CycleSelectionIndex;
	public byte ActivePingModeByte;

	public bool ActivePingMode
	{
		get => ActivePingModeByte != 0;
		set => ActivePingModeByte = value ? (byte)1 : (byte)0;
	}

	public InputState(
		string? activeSpellTargeting,
		string? activeCommandTargeting,
		string? activeBuildingPlacementType,
		bool activePingMode = false,
		int cycleSelectionIndex = 0)
	{
		SpellStringId = StringRegistry.GetOrAdd(activeSpellTargeting);
		CommandStringId = StringRegistry.GetOrAdd(activeCommandTargeting);
		BuildingStringId = StringRegistry.GetOrAdd(activeBuildingPlacementType);
		ActivePingModeByte = activePingMode ? (byte)1 : (byte)0;
		CycleSelectionIndex = cycleSelectionIndex;
	}

	public InputState(
		int spellStringId,
		string? activeSpellTargeting,
		string? activeCommandTargeting,
		string? activeBuildingPlacementType,
		bool activePingMode)
	{
		SpellStringId = spellStringId != 0 ? spellStringId : StringRegistry.GetOrAdd(activeSpellTargeting);
		CommandStringId = StringRegistry.GetOrAdd(activeCommandTargeting);
		BuildingStringId = StringRegistry.GetOrAdd(activeBuildingPlacementType);
		ActivePingModeByte = activePingMode ? (byte)1 : (byte)0;
		CycleSelectionIndex = 0;
	}

	public string? ActiveSpellTargeting
	{
		get => StringRegistry.Get(SpellStringId);
		set => SpellStringId = StringRegistry.GetOrAdd(value);
	}

	public string? ActiveCommandTargeting
	{
		get => StringRegistry.Get(CommandStringId);
		set => CommandStringId = StringRegistry.GetOrAdd(value);
	}

	public string? ActiveBuildingPlacementType
	{
		get => StringRegistry.Get(BuildingStringId);
		set => BuildingStringId = StringRegistry.GetOrAdd(value);
	}
}
