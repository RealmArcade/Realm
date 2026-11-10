using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>dispatching a conversion by asset type.</summary>
public static partial class TextureConverter
	{
	private static TextureConversionResult ProcessByAssetType(string normType, string fullInput, string targetRtex, int? columns, int? rows, float? fps)
	{
		return normType switch
		{
			"terrain" => ProcessAndSaveTerrainTexture(fullInput, targetRtex),
			"decal" => ProcessDecalAsset(fullInput, targetRtex, columns, rows),
			"spritesheet" => ProcessSpritesheetAsset(fullInput, targetRtex, columns, rows, fps),
			"skybox" => ProcessSkybox(fullInput, targetRtex),
			"ribbon" => ProcessAndSaveRibbonTexture(fullInput, targetRtex),
			"noise" => ProcessAndSaveSingleLayerTexture(fullInput, targetRtex, "noise_texture"),
			"icon" => ProcessAndSaveIconTexture(fullInput, targetRtex),
			_ => ProcessVfxOrThrow(normType, fullInput, targetRtex)
		};
	}

	private static TextureConversionResult ProcessDecalAsset(string fullInput, string targetRtex, int? columns, int? rows)
	{
		return ProcessAndSaveDecalTexture(fullInput, targetRtex, columns: columns ?? 1, rows: rows ?? 1);
	}

	private static TextureConversionResult ProcessSpritesheetAsset(string fullInput, string targetRtex, int? columns, int? rows, float? fps)
	{
		return ProcessAndSaveSpritesheet(fullInput, targetRtex, columns ?? 4, rows ?? 4, fps: fps ?? 20.0f);
	}

	private static TextureConversionResult ProcessVfxOrThrow(string normType, string fullInput, string targetRtex)
	{
		return normType switch
		{
			"vfx_radial" => ProcessAndSaveVfxRadialTexture(fullInput, targetRtex),
			"vfx_vertical" => ProcessAndSaveVfxVerticalTexture(fullInput, targetRtex),
			_ => throw new InvalidOperationException($"Unsupported asset type '{normType}'. Supported types: Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical.")
		};
	}

	private static TextureConversionResult ProcessSkybox(string fullInput, string targetRtex)
	{
		if (Path.GetExtension(targetRtex).ToLowerInvariant() is not ".rtex")
		{
			return SkyboxProcessor.ProcessSkyboxFile(fullInput, targetRtex);
		}

		return ProcessAndSaveSkybox(fullInput, targetRtex);
	}

	private static void ProcessSingleDirectoryFile(
		string file,
		string fullInputDir,
		string? fullOutputDir,
		string? assetType,
		int? columns,
		int? rows,
		float? fps,
		ref int successCount,
		ref int failCount)
	{
		if (!ImageFormatConverter.IsImageFile(file)) return;

		string fileExt = Path.GetExtension(file).ToLowerInvariant();
		string targetExt = fileExt == ".rtex" ? ".webp" : ".rtex";

		string target;
		if (string.IsNullOrEmpty(fullOutputDir))
		{
			target = Path.ChangeExtension(file, targetExt);
		}
		else
		{
			string rel = Path.GetRelativePath(fullInputDir, file);
			target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, targetExt));
		}

		try
		{
			var res = ConvertTextureFile(file, target, assetType, columns, rows, fps);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {target}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to convert {file}: {ex.Message}");
			failCount++;
		}
	}
}
