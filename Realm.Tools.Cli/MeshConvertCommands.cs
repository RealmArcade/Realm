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

public static class MeshConvertCommands
{
	public static int Execute(MeshConvertOptions options)
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

		if (!string.IsNullOrEmpty(options.ExtractSourceImage) && Directory.Exists(options.Input))
		{
			Console.Error.WriteLine("Error: --extract_source_image requires a single .rmesh input file.");
			return 1;
		}

		if (!string.IsNullOrEmpty(options.SourceImage))
		{
			try
			{
				options.SourceProfileImageWebp = ImageFormatConverter.EncodeWebpBytes(options.SourceImage, RmeshFile.SourceProfileImageQuality);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Error: Failed to encode source image '{options.SourceImage}': {ex.Message}");
				return 1;
			}
		}

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			ModelConverter.IsModelFile,
			file => options.InPlace ? Path.GetExtension(file) : (Path.GetExtension(file).Equals(".rmesh", StringComparison.OrdinalIgnoreCase) ? ".glb" : ".rmesh"),
			(inputFile, targetFile) => ProcessSingleMeshConvert(inputFile, targetFile, options),
			summaryActionName: "model conversion");
	}

	private static int ProcessSingleMeshConvert(string inputFile, string targetFile, MeshConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();
		string targetExt = Path.GetExtension(targetFile).ToLowerInvariant();

		if (!string.IsNullOrEmpty(options.ExtractSourceImage))
		{
			int extractExitCode = ExtractSourceProfileImage(inputFile, options.ExtractSourceImage);
			if (extractExitCode != 0) return extractExitCode;
		}

		if (fileExt == ".rmesh" && !targetExt.Equals(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			CliAssetAgreement.EnsureAccepted();
		}

		if (targetExt.Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".rmesh")
		{
			return ExtractGlbFromRmesh(inputFile, targetFile);
		}

		if (targetExt.Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".glb")
		{
			return OptimizeGlb(inputFile, targetFile, options);
		}

		return ExecuteModelToRmeshConversion(inputFile, targetFile, options);
	}

	private static int ExtractSourceProfileImage(string inputFile, string targetPath)
	{
		if (!string.Equals(Path.GetExtension(inputFile), ".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			Console.Error.WriteLine($"Error: --extract_source_image requires an .rmesh input, got '{inputFile}'.");
			return 1;
		}

		byte[]? imageBytes = RmeshFile.GetSourceProfileImageWebpFromFile(inputFile);
		if (imageBytes == null || imageBytes.Length == 0)
		{
			Console.Error.WriteLine($"Error: '{inputFile}' has no embedded source reference image.");
			return 1;
		}

		try
		{
			string fullTargetPath = Path.GetFullPath(targetPath);
			string? targetDirectory = Path.GetDirectoryName(fullTargetPath);
			if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);

			File.WriteAllBytes(fullTargetPath, imageBytes);
			Console.WriteLine($"Successfully extracted source reference image: {inputFile} -> {fullTargetPath} ({imageBytes.Length} bytes)");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to write source reference image '{targetPath}': {ex.Message}");
			return 1;
		}
	}

	public static int ExtractGlbFromRmesh(string inputFile, string targetFile)
	{
		var res = ModelConverter.ExtractGlbFromRmesh(inputFile, targetFile);
		if (res.Success)
		{
			Console.WriteLine($"Successfully extracted GLB: {inputFile} -> {targetFile}");
			return 0;
		}

		Console.Error.WriteLine($"Failed to extract GLB from {inputFile}: {res.ErrorMessage}");
		return 1;
	}

	public static int OptimizeGlb(string inputFile, string targetFile, MeshConvertOptions options)
	{
		try
		{
			byte[] inputGlbBytes = File.ReadAllBytes(inputFile);
			byte[] outputGlbBytes;
			if (!options.Force && GlbManifestUtils.HasOptimizationFlag(inputGlbBytes))
			{
				outputGlbBytes = inputGlbBytes;
			}
			else
			{
				byte[] unoptimized = GlbManifestUtils.StripOptimizationMetadata(inputGlbBytes).UnoptimizedBytes;
				var opt = BuildMeshConvertOptimizationOptions(options);
				var optimizer = new GlbOptimizer();
				var optResult = optimizer.Optimize(unoptimized, opt);
				outputGlbBytes = optResult.Success && optResult.OutputGlbBytes != null ? optResult.OutputGlbBytes : unoptimized;
				WriteLodTriangleCountsReport(optResult, inputFile);
				WriteUvChartReport(optResult, inputFile);
			}

			string? outDir = Path.GetDirectoryName(targetFile);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			File.WriteAllBytes(targetFile, outputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			Console.WriteLine($"Successfully converted model: {inputFile} -> {targetFile} (Size: {outputGlbBytes.Length} bytes)");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {ex.Message}");
			return 1;
		}
	}

	private static void WriteLodTriangleCountsReport(Realm.Shared.OptimizationResult result, string inputFile)
	{
		WriteLodTriangleCountsReport(result.LodTriangleCounts, inputFile);
	}

	private static void WriteUvChartReport(Realm.Shared.OptimizationResult result, string inputFile)
	{
		WriteUvChartReport(result.UvIslandCountBefore, result.UvIslandCountAfter, result.UvChartMergeTolerance, inputFile);
	}

	private static void WriteUvChartReport(int islandCountBefore, int islandCountAfter, float tolerance, string inputFile)
	{
		if (islandCountBefore <= 0) return;

		Console.WriteLine($"UV charts for {inputFile}: {islandCountBefore} -> {islandCountAfter} islands (seam tolerance {tolerance:0.00000})");
	}

	private static void WriteLodTriangleCountsReport(int[]? counts, string inputFile)
	{
		if (counts == null || counts.Length == 0)
		{
			Console.WriteLine($"LOD triangles for {inputFile}: no LOD tiers were generated.");
			return;
		}

		var report = new System.Text.StringBuilder();
		for (int tier = 0; tier < counts.Length; tier++)
		{
			if (tier > 0) report.Append(", ");
			report.Append($"LOD{tier}={counts[tier]}");
		}

		Console.WriteLine($"LOD triangles for {inputFile}: {report}");
	}

	private static OptimizationOptions BuildMeshConvertOptimizationOptions(MeshConvertOptions options)
	{
		var optimizationOptions = ModelConverter.GetAutomaticOptimizationOptions(options.AssetType, options.Force);
		optimizationOptions.ReduceUvIslands = !options.NoUvChartReduction;
		if (options.UvChartTolerance > 0f)
		{
			optimizationOptions.UvChartMergeTolerance = options.UvChartTolerance;
		}

		return optimizationOptions;
	}

	private static int ExecuteModelToRmeshConversion(string inputFile, string targetFile, MeshConvertOptions options)
	{
		OptimizationOptions optimizationOptions = BuildMeshConvertOptimizationOptions(options);

		var res = ModelConverter.ConvertToRmesh(
			inputFile,
			targetFile,
			options.AssetType,
			options.Force,
			optimizationOptions,
			chromaKey: options.ChromaKey,
			sourceProfileImageWebp: options.SourceProfileImageWebp,
			stripSourceProfileImage: options.StripSourceImage);

		if (res.Success)
		{
			Console.WriteLine($"Successfully converted model: {inputFile} -> {targetFile} (Size: {res.OptimizedSize} bytes, TeamColor: {res.SupportsTeamColor})");
			WriteLodTriangleCountsReport(res.LodTriangleCounts, inputFile);
			WriteUvChartReport(res.UvIslandCountBefore, res.UvIslandCountAfter, res.UvChartMergeTolerance, inputFile);
			return 0;
		}

		Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
		return 1;
	}

	public static string ResolveMeshTargetExtension(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace)
		{
			return Path.GetExtension(inputPath);
		}

		if (!string.IsNullOrEmpty(explicitOutput))
		{
			string outExt = Path.GetExtension(explicitOutput);
			if (!string.IsNullOrEmpty(outExt))
			{
				return outExt;
			}
		}

		string inExt = Path.GetExtension(inputPath);
		return string.Equals(inExt, ".glb", StringComparison.OrdinalIgnoreCase) ? ".glb" : ".rmesh";
	}
}
