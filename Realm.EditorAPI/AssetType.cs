namespace Realm.EditorAPI;

/// <summary>
/// Specifies the classification type of an asset.
/// </summary>
public enum AssetType
{
	/// <summary>
	/// 3D character or unit model asset.
	/// </summary>
	Character,

	/// <summary>
	/// 3D building or structure model asset.
	/// </summary>
	Building,

	/// <summary>
	/// 3D prop or environmental model asset.
	/// </summary>
	Prop,

	/// <summary>
	/// 3D item, weapon, or projectile model asset.
	/// </summary>
	Item,

	/// <summary>
	/// 2D decal texture asset.
	/// </summary>
	Decal,

	/// <summary>
	/// 2D user interface icon asset.
	/// </summary>
	Icon,

	/// <summary>
	/// 2D noise texture asset.
	/// </summary>
	Noise,

	/// <summary>
	/// 2D ribbon trail texture asset.
	/// </summary>
	Ribbon,

	/// <summary>
	/// Panoramic skybox texture asset.
	/// </summary>
	Skybox,

	/// <summary>
	/// Visual effects animated sprite sheet texture asset.
	/// </summary>
	Spritesheet,

	/// <summary>
	/// Ground terrain splat texture asset.
	/// </summary>
	Terrain,

	/// <summary>
	/// Radial visual effects texture asset.
	/// </summary>
	VfxRadial,

	/// <summary>
	/// Vertical visual effects texture asset.
	/// </summary>
	VfxVertical,

	/// <summary>
	/// Skeletal or node animation asset.
	/// </summary>
	Animation,

	/// <summary>
	/// Background music audio track asset.
	/// </summary>
	Music,

	/// <summary>
	/// Sound effect audio clip asset.
	/// </summary>
	SoundEffect,

	/// <summary>
	/// Visual shader script asset.
	/// </summary>
	Shader
}

/// <summary>
/// Provides extension and utility methods for parsing and converting <see cref="AssetType"/> values.
/// </summary>
public static class AssetTypeExtensions
{
	/// <summary>
	/// Converts the asset type to its canonical string representation used in metadata JSON.
	/// </summary>
	/// <param name="assetType">The asset classification type.</param>
	/// <returns>The canonical string matching asset metadata specifications.</returns>
	public static string ToCanonicalString(this AssetType assetType)
	{
		return assetType switch
		{
			AssetType.Character => "Character",
			AssetType.Building => "Building",
			AssetType.Prop => "Prop",
			AssetType.Item => "Item",
			AssetType.Decal => "Decal",
			AssetType.Icon => "Icon",
			AssetType.Noise => "Noise",
			AssetType.Ribbon => "Ribbon",
			AssetType.Skybox => "Skybox",
			AssetType.Spritesheet => "Spritesheet",
			AssetType.Terrain => "Terrain",
			AssetType.VfxRadial => "vfx_radial",
			AssetType.VfxVertical => "vfx_vertical",
			AssetType.Animation => "Animation",
			AssetType.Music => "Music",
			AssetType.SoundEffect => "SoundEffect",
			AssetType.Shader => "Shader",
			_ => assetType.ToString()
		};
	}

	/// <summary>
	/// Attempts to parse a raw string representation into an <see cref="AssetType"/> enum value.
	/// Handles standard names, lowercase, underscores, and normalized variants.
	/// </summary>
	/// <param name="raw">The raw string to parse.</param>
	/// <param name="assetType">When this method returns, contains the parsed asset type if successful; otherwise default.</param>
	/// <returns><see langword="true"/> if successfully parsed; otherwise <see langword="false"/>.</returns>
	public static bool TryParse(string? raw, out AssetType assetType)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			assetType = default;
			return false;
		}

		string normalized = raw.Trim().ToLowerInvariant().Replace("_", "");
		switch (normalized)
		{
			case "character":
			case "unit":
			case "units":
				assetType = AssetType.Character;
				return true;
			case "building":
			case "buildings":
				assetType = AssetType.Building;
				return true;
			case "prop":
			case "props":
				assetType = AssetType.Prop;
				return true;
			case "item":
			case "items":
			case "attachment":
			case "attachments":
			case "projectile":
			case "projectiles":
				assetType = AssetType.Item;
				return true;
			case "decal":
			case "decals":
				assetType = AssetType.Decal;
				return true;
			case "icon":
			case "icons":
				assetType = AssetType.Icon;
				return true;
			case "noise":
			case "noisetextures":
				assetType = AssetType.Noise;
				return true;
			case "ribbon":
			case "ribbons":
			case "ribbontexture":
				assetType = AssetType.Ribbon;
				return true;
			case "skybox":
			case "skyboxes":
				assetType = AssetType.Skybox;
				return true;
			case "spritesheet":
			case "spritesheets":
			case "vfxspritesheet":
			case "vfxspritesheets":
			case "vfx":
				assetType = AssetType.Spritesheet;
				return true;
			case "terrain":
			case "texture":
			case "textures":
			case "terraintexture":
				assetType = AssetType.Terrain;
				return true;
			case "vfxradial":
				assetType = AssetType.VfxRadial;
				return true;
			case "vfxvertical":
				assetType = AssetType.VfxVertical;
				return true;
			case "animation":
			case "animations":
				assetType = AssetType.Animation;
				return true;
			case "music":
				assetType = AssetType.Music;
				return true;
			case "soundeffect":
			case "sfx":
				assetType = AssetType.SoundEffect;
				return true;
			case "shader":
			case "shaders":
				assetType = AssetType.Shader;
				return true;
			default:
				assetType = default;
				return false;
		}
	}

	/// <summary>
	/// Parses a raw string representation into an <see cref="AssetType"/> enum value, returning a fallback if parsing fails.
	/// </summary>
	/// <param name="raw">The raw string to parse.</param>
	/// <param name="fallback">The fallback value to return if parsing fails.</param>
	/// <returns>The parsed <see cref="AssetType"/> or the specified fallback value.</returns>
	public static AssetType Parse(string? raw, AssetType fallback = AssetType.Terrain)
	{
		return TryParse(raw, out var parsed) ? parsed : fallback;
	}
}
