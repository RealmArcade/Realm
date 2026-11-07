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

internal static class TextureConvertCommand
{
	internal static int Execute(TextureConvertOptions options)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rtex", options.AssetType, out string canonical, out var validTypes))
				{
					Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for textures. Valid asset_type values for textures (.rtex/.png) are: {string.Join(", ", validTypes)}.");
					return 1;
				}

				options.AssetType = canonical;
			}

			return CliFileSet.ProcessTraversedFiles(
				options.Input,
				options.Output,
				options.Recursive,
				options.InPlace,
				ImageFormatConverter.IsImageFile,
				file => Path.GetExtension(file).Equals(".rtex", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".rtex",
				(inputFile, targetFile) => ProcessSingleTextureConvert(inputFile, targetFile, options),
				summaryActionName: "texture conversion");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
	}

	internal static int ProcessSingleTextureConvert(string inputFile, string targetFile, TextureConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".rtex" && !Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			CliAssetAgreement.EnsureAccepted();
		}

		var res = TextureConverter.ConvertTextureFile(
			inputFile,
			targetFile,
			options.AssetType,
			options.Columns,
			options.Rows);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			}

			Console.WriteLine($"Successfully converted: {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}
}
