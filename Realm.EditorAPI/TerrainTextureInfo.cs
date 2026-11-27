namespace Realm.EditorAPI;

/// <summary>
/// Describes one terrain texture swatch slot of the open workspace, so a generator can compare an
/// image colour against the textures the map can actually be painted with.
/// </summary>
/// <param name="SwatchIndex">Index the splat maps and painting tools use for this texture.</param>
/// <param name="Name">Display name of the swatch, which still follows the source file name.</param>
/// <param name="TexturePath">Absolute path of the swatch texture on disk, or an empty string for an empty slot.</param>
public readonly record struct TerrainTextureInfo(int SwatchIndex, string Name, string TexturePath);
