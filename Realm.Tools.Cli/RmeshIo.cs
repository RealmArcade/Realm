using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;

namespace Realm.Tools.Cli;

internal static class RmeshIo
{
	internal static void EnsureDirectoryForFile(string filePath)
	{
		string? outDir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
	}

	internal static void TryDeleteInPlaceOriginal(bool inPlace, string inputPath, string outputPath)
	{
		if (inPlace && !string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
		{
			try
			{
				File.Delete(inputPath);
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"  Warning: converted in place but could not delete the source {inputPath}: {exception.Message}");
			}
		}
	}

	internal static JsonNode? TryParseMeta(string? existingMeta)
	{
		if (string.IsNullOrEmpty(existingMeta)) return null;

		try
		{
			return JsonNode.Parse(existingMeta);
		}
		catch
		{
			return null;
		}
	}

	internal static void UpdateAssetTypeAndAuthor(JsonNode node, ref string? targetAssetType, ref string? targetAuthor)
	{
		if (node is not JsonObject) return;
		targetAssetType ??= node["asset_type"]?.ToString();
		targetAuthor ??= node["author"]?.ToString();
	}

	internal static (string? assetType, string? author) ExtractAssetTypeAndAuthor(string? existingMeta, string? explicitAssetType)
	{
		string? targetAssetType = string.IsNullOrWhiteSpace(explicitAssetType) ? null : explicitAssetType;
		string? targetAuthor = null;

		JsonNode? node = TryParseMeta(existingMeta);
		if (node != null)
		{
			UpdateAssetTypeAndAuthor(node, ref targetAssetType, ref targetAuthor);
		}

		return (targetAssetType, targetAuthor);
	}
}
