namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the domain data for the strategy shroud grid (ExplorationShroud and VisionShroud).
	/// </summary>
	public struct ShroudState
	{
		public const byte ExplorationShroud = 0;
		public const byte VisionShroud = 1;
		public const byte Visible = 2;

		public byte[,] ShroudGrid;
		public string ShroudType;

		public ShroudState(byte[,] shroudGrid, string shroudType)
		{
			ShroudGrid = shroudGrid;
			ShroudType = shroudType;
		}
	}
}
