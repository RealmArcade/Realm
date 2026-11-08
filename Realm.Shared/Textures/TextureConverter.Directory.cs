using System.Globalization;
using System.Text.Json.Nodes;
using Imazen.WebP;
using Realm.Shared.Metadata;
using SkiaSharp;

namespace Realm.Shared.Textures;

/// <summary>converting every texture in a directory.</summary>
public static partial class TextureConverter
	{
	public static int ConvertTextureDirectory(
		string inputDir,
		string? outputDir,
		string? assetType,
		bool recursive,
		int? columns = null,
		int? rows = null,
		float? fps = null)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			ProcessSingleDirectoryFile(file, fullInputDir, fullOutputDir, assetType, columns, rows, fps, ref successCount, ref failCount);
		}

		Console.WriteLine($"Finished texture conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
