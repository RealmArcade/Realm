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
