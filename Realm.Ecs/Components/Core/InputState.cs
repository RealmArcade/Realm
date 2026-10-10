namespace Realm.Ecs.Components.Core
{
	/// <summary>
	/// Holds the current input selection index and targeting/placement mode states.
	/// </summary>
	public struct InputState
	{
		public int CycleSelectionIndex;
		public string? ActiveSpellTargeting;
		public string? ActiveCommandTargeting;
		public string? ActiveBuildingPlacementType;
		public bool ActivePingMode;

		public InputState(
			int cycleSelectionIndex,
			string? activeSpellTargeting,
			string? activeCommandTargeting,
			string? activeBuildingPlacementType,
			bool activePingMode)
		{
			CycleSelectionIndex = cycleSelectionIndex;
			ActiveSpellTargeting = activeSpellTargeting;
			ActiveCommandTargeting = activeCommandTargeting;
			ActiveBuildingPlacementType = activeBuildingPlacementType;
			ActivePingMode = activePingMode;
		}
	}
}
