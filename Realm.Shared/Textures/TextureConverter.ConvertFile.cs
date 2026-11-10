using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>converting one texture file and choosing its output path.</summary>
public static partial class TextureConverter
	{
	public static TextureConversionResult ConvertTextureFile(
		string inputPath,
		string? outputPath,
		string? assetType = null,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();

		if (ext == ".rtex")
		{
			return HandleRtexConversion(fullInput, outputPath);
		}

		string normType = ResolveAssetType(fullInput, assetType, ref columns, ref rows, ref fps);
		string targetRtex = GetTargetRtexPath(fullInput, outputPath);

		return ProcessByAssetType(normType, fullInput, targetRtex, columns, rows, fps);
	}

	private static TextureConversionResult HandleRtexConversion(string fullInput, string? outputPath)
	{
		string targetWebp = string.IsNullOrEmpty(outputPath)
			? Path.ChangeExtension(fullInput, ".webp")
			: Path.GetFullPath(outputPath);

		if (targetWebp.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			return ExtractPngFromRtex(fullInput, targetWebp);
		}

		return ExtractWebpFromRtex(fullInput, targetWebp);
	}

	private static string ResolveAssetType(string fullInput, string? assetType, ref int? columns, ref int? rows, ref float? fps)
	{
		string normType = (assetType ?? string.Empty).Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(normType))
		{
			ExtractMetadataInfo(fullInput, ref normType, ref columns, ref rows, ref fps);
		}

		if (string.IsNullOrEmpty(normType))
		{
			throw new InvalidOperationException($"Asset type was not specified and could not be detected from image metadata in '{fullInput}'. Please specify -t / --type (Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical).");
		}

		return normType;
	}

	private static string GetTargetRtexPath(string fullInput, string? outputPath)
	{
		if (string.IsNullOrEmpty(outputPath))
		{
			return Path.ChangeExtension(fullInput, ".rtex");
		}

		return Path.GetFullPath(outputPath);
	}
}
