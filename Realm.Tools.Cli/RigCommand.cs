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

public static class RigCommand
{
	public static int Execute(RigHumanoidOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for humanoid rig. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}

			options.AssetType = canonical;
		}

		if (!string.IsNullOrWhiteSpace(options.MiaDir))
		{
			Environment.SetEnvironmentVariable("MIA_DIR", options.MiaDir);
		}

		return CliFileSet.ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			file => MeshConvertCommands.ResolveMeshTargetExtension(file, options.Output, options.InPlace),
			(inputFile, targetFile) => ProcessSingleRigHumanoid(inputFile, targetFile, options),
			summaryActionName: "humanoid rigging");
	}

	private static int ProcessSingleRigHumanoid(string inputPath, string targetOutput, RigHumanoidOptions options)
	{
		Console.WriteLine($"Processing humanoid rig: {inputPath} -> {targetOutput}");

		string tempGlbOutput = Path.Combine(Path.GetTempPath(), $"realm_rig_out_{Guid.NewGuid():N}.glb");
		string? tempExtractedGlb = null;
		string tempGlbInput = inputPath;
		string preparedGlbInput = inputPath;

		try
		{
			bool isRmeshInput = Path.GetExtension(inputPath).Equals(".rmesh", StringComparison.OrdinalIgnoreCase);
			string? existingMeta = null;

			if (isRmeshInput)
			{
				(tempExtractedGlb, existingMeta) = ExtractRigInputBytes(inputPath);
				tempGlbInput = tempExtractedGlb;
			}

			preparedGlbInput = PrepareInputForAutoRigging(tempGlbInput, options.AssetType);

			var result = GlbAutoRigger.RigHumanoid(preparedGlbInput, tempGlbOutput, new GlbAutoRiggerOptions
			{
				NoFingers = options.NoFingers,
				UseNormals = options.UseNormals,
				WeightPostprocess = options.WeightPostprocess
			});

			if (!result.Success || !File.Exists(tempGlbOutput))
			{
				Console.Error.WriteLine($"Error rigging {inputPath}: {result.ErrorMessage}");
				return 1;
			}

			return ProcessRiggedOutput(inputPath, targetOutput, tempGlbOutput, existingMeta, options);
		}
		finally
		{
			TryDeleteFile(tempExtractedGlb);
			TryDeleteFile(tempGlbOutput);
			if (!string.Equals(preparedGlbInput, tempGlbInput, StringComparison.Ordinal)) TryDeleteFile(preparedGlbInput);
		}
	}

	private static string PrepareInputForAutoRigging(string inputPath, string? assetType)
	{
		int maxTextureResolution = ModelConverter.GetAutomaticOptimizationOptions(assetType, true).MaxTextureResolution;
		string preparedPath = Path.Combine(Path.GetTempPath(), $"realm_rig_prepared_{Guid.NewGuid():N}.glb");

		var preparation = new GlbOptimizer().PrepareForAutoRiggingFile(inputPath, preparedPath, maxTextureResolution);
		if (!preparation.Success)
		{
			Console.WriteLine($"  Warning: pre-rig optimization skipped: {preparation.ErrorMessage}");
			return inputPath;
		}

		Console.WriteLine($"  Pre-rig optimized {preparation.OriginalSize} -> {preparation.OptimizedSize} bytes before rigging.");
		return preparedPath;
	}

	private static int ProcessRiggedOutput(string inputPath, string targetOutput, string tempGlbOutput, string? existingMeta, RigHumanoidOptions options)
	{
		byte[] riggedGlb = File.ReadAllBytes(tempGlbOutput);
		RmeshIo.EnsureDirectoryForFile(targetOutput);

		bool isRmeshOutput = Path.GetExtension(targetOutput).Equals(".rmesh", StringComparison.OrdinalIgnoreCase);

		if (isRmeshOutput)
		{
			int rmeshResult = SaveRiggedRmesh(riggedGlb, inputPath, targetOutput, options, existingMeta);
			if (rmeshResult != 0) return rmeshResult;
		}
		else
		{
			File.WriteAllBytes(targetOutput, riggedGlb);
			var optimizeResult = new GlbOptimizer().OptimizeFile(targetOutput, targetOutput, new OptimizationOptions { ForceReDecimate = true });
			if (!optimizeResult.Success)
			{
				Console.Error.WriteLine($"  Warning: LOD generation failed for {targetOutput}: {optimizeResult.ErrorMessage}");
			}

			RealmMetadataHelper.SyncBlake3Metadata(targetOutput);
			Console.WriteLine($"Successfully rigged and saved GLB: {targetOutput}");
		}

		RmeshIo.TryDeleteInPlaceOriginal(options.InPlace, inputPath, targetOutput);
		return 0;
	}

	public static void TryDeleteFile(string? filePath)
	{
		if (filePath != null && File.Exists(filePath))
		{
			try
			{
				File.Delete(filePath);
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"  Warning: could not delete temporary file {filePath}: {exception.Message}");
			}
		}
	}

	public static (string tempExtractedGlb, string? existingMeta) ExtractRigInputBytes(string inputPath)
	{
		byte[] rmeshBytes = File.ReadAllBytes(inputPath);
		var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
		string tempExtractedGlb = Path.Combine(Path.GetTempPath(), $"realm_rig_in_{Guid.NewGuid():N}.glb");
		File.WriteAllBytes(tempExtractedGlb, glbBytes);

		return (tempExtractedGlb, meta);
	}

	private static int SaveRiggedRmesh(byte[] riggedGlb, string inputPath, string targetOutput, RigHumanoidOptions options, string? existingMeta)
	{
		var (extractedType, targetAuthor) = RmeshIo.ExtractAssetTypeAndAuthor(existingMeta, options.AssetType);
		string targetAssetType = extractedType ?? "Character";

		var convRes = ModelConverter.ConvertToRmesh(
			riggedGlb,
			inputPath,
			targetAssetType,
			force: true,
			author: targetAuthor,
			existingMetadataJson: existingMeta);

		if (!convRes.Success || convRes.OutputBytes == null)
		{
			Console.Error.WriteLine($"Failed to pack rigged model to RMESH: {convRes.ErrorMessage}");
			return 1;
		}

		File.WriteAllBytes(targetOutput, convRes.OutputBytes);
		Console.WriteLine($"Successfully rigged and saved RMESH: {targetOutput}");
		return 0;
	}
}
