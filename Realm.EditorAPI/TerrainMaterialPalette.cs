namespace Realm.EditorAPI;

/// <summary>
/// Identifies the terrain texture swatch indices used for each generated terrain band.
/// </summary>
/// <param name="Water">Swatch index applied to cells below the water line.</param>
/// <param name="Ground">Swatch index applied to flat and lowland ground.</param>
/// <param name="Rock">Swatch index applied to raised rock bands.</param>
/// <param name="Cliff">Swatch index applied to high cliff bands and to the cliff splat map.</param>
public readonly record struct TerrainMaterialPalette(int Water, int Ground, int Rock, int Cliff);
