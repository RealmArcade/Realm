namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Stores procedural height-blending calibration parameters for a terrain texture swatch.
	/// </summary>
	public struct TerrainSwatchConfig
	{
		public float HeightScale;
		public float HeightOffset;
		public float CrevicePower;
		public float EdgeNoiseInfluence;

		public TerrainSwatchConfig(float heightScale = 1.0f, float heightOffset = 0.0f, float crevicePower = 1.0f, float edgeNoiseInfluence = 1.0f)
		{
			HeightScale = heightScale;
			HeightOffset = heightOffset;
			CrevicePower = crevicePower;
			EdgeNoiseInfluence = edgeNoiseInfluence;
		}
	}
}
