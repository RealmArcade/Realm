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

public static class AudioConvertCommand
{
	public static int Execute(AudioConvertOptions options)
	{
		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			AudioConverter.IsAudioFile,
			file => Path.GetExtension(file).Equals(".raud", StringComparison.OrdinalIgnoreCase) ? ".ogg" : ".raud",
			(inputFile, targetFile) => ProcessSingleAudioConvert(inputFile, targetFile, options),
			summaryActionName: "audio conversion");
	}

	private static int ProcessSingleAudioConvert(string inputFile, string targetFile, AudioConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".raud" && !Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
		{
			CliAssetAgreement.EnsureAccepted();
		}

		var res = AudioConverter.ConvertAudioFile(
			inputFile,
			targetFile,
			options.AssetType);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
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
