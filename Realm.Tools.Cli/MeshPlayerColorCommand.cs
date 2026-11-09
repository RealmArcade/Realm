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

public static class MeshPlayerColorCommand
{
	public static int Execute(MeshPlayerColorCliOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for 3D model. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}

			options.AssetType = canonical;
		}

		var processorOptions = new GlbPlayerColorOptions
		{
			ChromaKey = options.ChromaKey,
			AutoCorrectChromaKey = options.AutoCorrectChromaKey,
			CoreThreshold = options.CoreThreshold,
			FringeThreshold = options.FringeThreshold,
			MinClusterFaces = options.MinClusterFaces,
			DilationRadius = options.DilationRadius,
			CreaseAngleDegrees = options.CreaseAngleDegrees
		};

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			_ => ".rmesh",
			(inputFile, targetFile) => ProcessSingleMeshPlayerColor(inputFile, targetFile, processorOptions, options.AssetType, options.InPlace),
			customPathResolver: (inputRoot, currentFile) =>
			{
				if (Directory.Exists(inputRoot))
				{
					if (options.InPlace || string.IsNullOrEmpty(options.Output))
					{
						return ResolveMeshPlayerColorOutputPath(currentFile, null, options.InPlace);
					}

					string relativePath = Path.GetRelativePath(inputRoot, currentFile);
					string relativeTarget = Path.ChangeExtension(relativePath, ".rmesh");
					return Path.Combine(options.Output, relativeTarget);
				}

				return ResolveMeshPlayerColorOutputPath(currentFile, options.Output, options.InPlace);
			},
			summaryActionName: "mesh player color processing");
	}

	private static int ProcessSingleMeshPlayerColor(
		string inputPath,
		string outputPath,
		GlbPlayerColorOptions processorOptions,
		string? assetType = null,
		bool inPlace = false)
	{
		if (!outputPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			outputPath = Path.ChangeExtension(outputPath, ".rmesh");
		}

		Console.WriteLine($"Processing: {inputPath} -> {outputPath}");

		try
		{
			var (sourceGlbBytes, existingMeta) = ExtractInputBytesAndMetadata(inputPath);

			var (success, processedGlbBytes, errorMessage, maskedFaces, totalFaces, detectedKey) =
				GlbPlayerColorProcessor.ProcessBytes(sourceGlbBytes, processorOptions);

			if (!success || processedGlbBytes == null)
			{
				Console.Error.WriteLine($"  Failed player-color processing: {errorMessage}");
				return 1;
			}

			string resolvedChromaKey = detectedKey ?? processorOptions.ChromaKey;
			Console.WriteLine($"  Player-color mask applied (masked faces: {maskedFaces}/{totalFaces}, chroma key: {resolvedChromaKey})");

			RmeshIo.EnsureDirectoryForFile(outputPath);

			var (targetAssetType, targetAuthor) = RmeshIo.ExtractAssetTypeAndAuthor(existingMeta, assetType);

			return RepackAndSaveRmesh(inputPath, outputPath, processedGlbBytes, targetAssetType, targetAuthor, resolvedChromaKey, existingMeta, inPlace);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"  Failed to process {inputPath}: {ex.Message}");
			return 1;
		}
	}

	private static int RepackAndSaveRmesh(string inputPath, string outputPath, byte[] processedGlbBytes, string? targetAssetType, string? targetAuthor, string resolvedChromaKey, string? existingMeta, bool inPlace)
	{
		var convResult = ModelConverter.ConvertToRmesh(
			processedGlbBytes,
			inputPath,
			targetAssetType,
			force: false,
			author: targetAuthor,
			chromaKey: resolvedChromaKey,
			existingMetadataJson: existingMeta);

		if (!convResult.Success || convResult.OutputBytes == null)
		{
			Console.Error.WriteLine($"  Failed to repack into RMESH: {convResult.ErrorMessage}");
			return 1;
		}

		File.WriteAllBytes(outputPath, convResult.OutputBytes);
		Console.WriteLine($"  Successfully saved RMESH: {outputPath} ({convResult.OptimizedSize} bytes)");

		RmeshIo.TryDeleteInPlaceOriginal(inPlace, inputPath, outputPath);

		return 0;
	}

	public static (byte[] glbBytes, string? metadata) ExtractInputBytesAndMetadata(string inputPath)
	{
		string inputExt = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isRmeshInput = inputExt == ".rmesh";

		if (isRmeshInput)
		{
			byte[] rmeshBytes = File.ReadAllBytes(inputPath);
			var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
			return (glbBytes, meta);
		}

		return (File.ReadAllBytes(inputPath), null);
	}

	private static string ResolveMeshPlayerColorOutputPath(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace) return Path.ChangeExtension(inputPath, ".rmesh");
		if (!string.IsNullOrEmpty(explicitOutput))
		{
			if (Directory.Exists(explicitOutput) ||
			    explicitOutput.EndsWith(Path.DirectorySeparatorChar) ||
			    explicitOutput.EndsWith(Path.AltDirectorySeparatorChar))
			{
				string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
				return Path.Combine(explicitOutput, $"{nameWithoutExt}_masked.rmesh");
			}

			return Path.ChangeExtension(explicitOutput, ".rmesh");
		}

		string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
		string nameWithoutExtDefault = Path.GetFileNameWithoutExtension(inputPath);
		return Path.Combine(dir, $"{nameWithoutExtDefault}_masked.rmesh");
	}
}
