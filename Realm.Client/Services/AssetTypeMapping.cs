using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.EditorAPI;

namespace Realm.Client.Services;

/// <summary>
///     How an indexed asset's reported type and extension become an AssetType, and how that type names the
///     metadata category and the folder an imported asset lands in.
/// </summary>
public static class AssetTypeMapping
{
	private static readonly Dictionary<string, AssetType> PathSegmentToAssetType = new(StringComparer.OrdinalIgnoreCase)
	{
		["decals"] = AssetType.Decal,
		["textures"] = AssetType.Terrain,
		["terrain"] = AssetType.Terrain,
		["icons"] = AssetType.Icon,
		["noise"] = AssetType.Noise,
		["ribbons"] = AssetType.Ribbon,
		["skyboxes"] = AssetType.Skybox,
		["vfx_spritesheets"] = AssetType.Spritesheet,
		["vfx_radial"] = AssetType.VfxRadial,
		["vfx_vertical"] = AssetType.VfxVertical,
		["units"] = AssetType.Character,
		["characters"] = AssetType.Character,
		["buildings"] = AssetType.Building,
		["props"] = AssetType.Prop,
		["items"] = AssetType.Item,
		["attachments"] = AssetType.Item,
		["projectiles"] = AssetType.Item,
		["animations"] = AssetType.Animation,
		["music"] = AssetType.Music,
		["sfx"] = AssetType.SoundEffect,
		["shaders"] = AssetType.Shader
	};

	public static AssetType MapStringToAssetType(AssetType? rawType, string extension, string? pathHint = null)
	{
		if (rawType.HasValue)
		{
			return rawType.Value;
		}

		return MapStringToAssetType((string?)null, extension, pathHint);
	}

	public static AssetType MapStringToAssetType(string? rawType, string extension, string? pathHint = null)
	{
		if (AssetTypeExtensions.TryParse(rawType, out var parsed))
		{
			return parsed;
		}

		if (!string.IsNullOrEmpty(pathHint))
		{
			string normalizedHint = pathHint.Replace('\\', '/');
			string[] segments = normalizedHint.Split('/', StringSplitOptions.RemoveEmptyEntries);
			foreach (var segment in segments.Reverse())
			{
				if (PathSegmentToAssetType.TryGetValue(segment, out var matchedType))
				{
					return matchedType;
				}
				if (AssetTypeExtensions.TryParse(segment, out var segmentParsed))
				{
					return segmentParsed;
				}
			}
		}

		string ext = (extension ?? string.Empty).ToLowerInvariant();
		return ext switch
		{
			".rmesh" => AssetType.Character,
			".ranim" => AssetType.Animation,
			".raud" => AssetType.SoundEffect,
			".gdshader" => AssetType.Shader,
			_ => AssetType.Terrain
		};
	}

	public static string MapAssetTypeToCategoryString(AssetType assetType)
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
			_ => "Other"
		};
	}

	public static string MapCategoryToDestinationSubFolder(string category)
	{
		return category switch
		{
			"Character" => "models/units",
			"Building" => "models/buildings",
			"Prop" => "models/props",
			"Item" => "models/items",
			"Spritesheet" or "vfx_spritesheets" => "vfx_spritesheets",
			"vfx_radial" => "vfx_radial",
			"vfx_vertical" => "vfx_vertical",
			"Animation" => "animations",
			"SoundEffect" => "audio/sfx",
			"Music" => "audio/music",
			"Icon" => "icons",
			"Decal" => "decals",
			"Ribbon" => "ribbons",
			"Noise" => "noise",
			"Skybox" => "skyboxes",
			"Terrain" => "textures",
			"Shader" => "shaders",
			_ => category.ToLowerInvariant()
		};
	}
}
