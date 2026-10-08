using System;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.ModelOptimization;

public class ModelConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public bool SupportsTeamColor { get; set; }
	public string? AssetType { get; set; }
	public string? Author { get; set; }
	public string? PreferredFileName { get; set; }
	public int OriginalSize { get; set; }
	public int OptimizedSize { get; set; }
	public byte[]? OutputBytes { get; set; }
}

public static class ModelConverter
{
	public static readonly string[] SupportedModelExtensions =
	[
		".glb", ".gltf", ".rmesh", ".fbx", ".obj"
	];

	public static bool IsModelFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return Array.Exists(SupportedModelExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
	}

	public static OptimizationOptions GetAutomaticOptimizationOptions(string? assetType, bool forceReDecimate)
	{
		int maxRes = 1024;
		if (string.Equals(assetType, "Item", StringComparison.OrdinalIgnoreCase))
		{
			maxRes = 512;
		}

		return new OptimizationOptions
		{
			SimplificationRatio = 0.5f,
			MaxTextureResolution = maxRes,
			ForceReDecimate = forceReDecimate
		};
	}



	private static JsonObject ParseExistingMetadata(string? existingMetaJson)
	{
		if (string.IsNullOrWhiteSpace(existingMetaJson)) return new JsonObject();
		try
		{
			return JsonNode.Parse(existingMetaJson)?.AsObject() ?? new JsonObject();
		}
		catch
		{
			return new JsonObject();
		}
	}

	private static string? DetermineEffectiveAssetType(string? assetType, JsonObject metaObj)
	{
		if (!string.IsNullOrEmpty(assetType))
		{
			if (RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", assetType, out string canonicalInput, out _))
			{
				return canonicalInput;
			}
			return assetType;
		}

		string? existingType = metaObj["asset_type"]?.ToString() ?? metaObj["default_asset_type"]?.ToString() ?? metaObj["type"]?.ToString();
		if (!string.IsNullOrEmpty(existingType))
		{
			if (RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", existingType, out string canonicalMeta, out _))
			{
				return canonicalMeta;
			}
			return existingType;
		}

		return null;
	}

	private static (bool Success, byte[]? OutputGlb, string? ErrorMessage) ProcessGlbOptimization(byte[] rawGlbBytes, string? effectiveAssetType, bool force, OptimizationOptions? options)
	{
		bool shouldForce = force || (options.HasValue && options.Value.ForceReDecimate);
		bool isAlreadyOptimized = GlbManifestUtils.HasOptimizationFlag(rawGlbBytes);

		if (!shouldForce && isAlreadyOptimized)
		{
			return (true, rawGlbBytes, null);
		}

		byte[] unoptimized = GlbManifestUtils.StripOptimizationMetadata(rawGlbBytes).UnoptimizedBytes;
		var opt = options ?? GetAutomaticOptimizationOptions(effectiveAssetType, shouldForce);
		opt.ForceReDecimate = shouldForce;
		var optimizer = new GlbOptimizer();
		var optResult = optimizer.Optimize(unoptimized, opt);
		if (!optResult.Success || optResult.OutputGlbBytes == null)
		{
			return (false, null, optResult.ErrorMessage ?? "Optimization failed.");
		}
		
		return (true, optResult.OutputGlbBytes, null);
	}

	private static string DetermineChromaKey(string? chromaKey, JsonObject metaObj, byte[] finalGlbBytes)
	{
		if (string.Equals(chromaKey, "auto", StringComparison.OrdinalIgnoreCase))
		{
			return GlbPlayerColorProcessor.AutoDetectChromaKey(finalGlbBytes) ?? "#FF00FF";
		}
		
		if (string.IsNullOrEmpty(chromaKey))
		{
			string? existingKey = metaObj["chroma_key"]?.ToString() ?? metaObj["chromaKey"]?.ToString();
			if (string.Equals(existingKey, "auto", StringComparison.OrdinalIgnoreCase))
			{
				return GlbPlayerColorProcessor.AutoDetectChromaKey(finalGlbBytes) ?? "#FF00FF";
			}
			return existingKey ?? string.Empty;
		}
		
		return chromaKey;
	}

	private static byte[] ReadInputBytes(string fullInput)
	{
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();
		if (ext is not ".obj" and not ".fbx" and not ".dae")
		{
			return File.ReadAllBytes(fullInput);
		}

		using var importer = new Assimp.AssimpContext();
		var scene = importer.ImportFile(fullInput, Assimp.PostProcessSteps.Triangulate | Assimp.PostProcessSteps.GenerateNormals | Assimp.PostProcessSteps.MakeLeftHanded | Assimp.PostProcessSteps.FlipUVs);
		string tempGlb = Path.Combine(Path.GetTempPath(), $"realm_import_{Guid.NewGuid():N}.glb");
		try
		{
			importer.ExportFile(scene, tempGlb, "glb2");
			return File.ReadAllBytes(tempGlb);
		}
		finally
		{
			if (File.Exists(tempGlb)) try { File.Delete(tempGlb); } catch { }
		}
	}

	public static ModelConversionResult ConvertToRmesh(
		string inputPath,
		string? outputPath = null,
		string? assetType = null,
		bool force = false,
		OptimizationOptions? options = null,
		string? author = null,
		string? chromaKey = null,
		string? existingMetadataJson = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		var result = new ModelConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input model file not found: {inputPath}";
			return result;
		}

		string targetRmesh = !string.IsNullOrEmpty(outputPath)
			? Path.GetFullPath(outputPath)
			: Path.ChangeExtension(fullInput, ".rmesh");
		result.OutputPath = targetRmesh;

		try
		{
			byte[] inputBytes = ReadInputBytes(fullInput);

			result.OriginalSize = inputBytes.Length;

			string fileName = Path.GetFileName(fullInput);
			var convRes = ConvertToRmesh(inputBytes, fullInput, assetType, force, options, author, chromaKey, existingMetadataJson);
			if (!convRes.Success || convRes.OutputBytes == null)
			{
				result.Success = false;
				result.ErrorMessage = convRes.ErrorMessage;
				return result;
			}

			string? targetDir = Path.GetDirectoryName(targetRmesh);
			if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

			File.WriteAllBytes(targetRmesh, convRes.OutputBytes);

			result.Success = true;
			result.OutputBytes = convRes.OutputBytes;
			result.OptimizedSize = convRes.OptimizedSize;
			result.SupportsTeamColor = convRes.SupportsTeamColor;
			result.AssetType = convRes.AssetType;
			result.Author = convRes.Author;
			result.PreferredFileName = convRes.PreferredFileName;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	private static (byte[] GlbBytes, string? MetaJson) ParseInputBytes(ReadOnlySpan<byte> inputBytes, string? existingMetadataJson)
	{
		if (!RmeshFile.IsRmeshBytes(inputBytes))
		{
			return (inputBytes.ToArray(), existingMetadataJson);
		}

		var (parsedMeta, parsedGlb, _) = RmeshFile.Parse(inputBytes);
		return (parsedGlb, existingMetadataJson ?? parsedMeta);
	}

	private static ModelConversionResult BuildFinalRmeshResult(
		ModelConversionResult result,
		JsonObject metaObj,
		byte[] finalGlbBytes,
		bool supportsTeamColor,
		string effectiveAssetType)
	{
		byte[] rmeshBytes = RmeshFile.Build(metaObj.ToJsonString(), finalGlbBytes, compressed: true);
		result.Success = true;
		result.OutputBytes = rmeshBytes;
		result.OptimizedSize = rmeshBytes.Length;
		result.SupportsTeamColor = supportsTeamColor;
		result.AssetType = effectiveAssetType;
		result.Author = metaObj["author"]?.ToString();
		result.PreferredFileName = metaObj["preferred_file_name"]?.ToString();
		return result;
	}

	private static void UpdateMetadataObject(
		JsonObject metaObj,
		string? inputFileName,
		string effectiveAssetType,
		string? author,
		string? chromaKey,
		byte[] finalGlbBytes,
		bool supportsTeamColor)
	{
		UpdateCreatedUtc(metaObj);

		metaObj["format"] = "rmesh";
		metaObj["asset_type"] = effectiveAssetType;
		metaObj["team_color"] = supportsTeamColor;
		metaObj["is_compressed"] = true;

		UpdatePreferredFileName(metaObj, inputFileName);
		UpdateAuthor(metaObj, author);
		UpdateChromaKey(metaObj, chromaKey, finalGlbBytes);
	}

	private static void UpdateCreatedUtc(JsonObject metaObj)
	{
		if (metaObj.ContainsKey("created_utc") && metaObj["created_utc"] != null)
		{
			return;
		}
			
		metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
	}

	private static void UpdatePreferredFileName(JsonObject metaObj, string? inputFileName)
	{
		if (string.IsNullOrEmpty(inputFileName))
		{
			return;
		}

		string currentPreferred = metaObj["preferred_file_name"]?.ToString() ?? string.Empty;
		if (!string.IsNullOrEmpty(currentPreferred) && inputFileName.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		metaObj["preferred_file_name"] = Path.GetFileName(inputFileName);
	}

	private static void UpdateAuthor(JsonObject metaObj, string? author)
	{
		if (string.IsNullOrEmpty(author))
		{
			return;
		}

		if (metaObj.ContainsKey("author") && !string.IsNullOrWhiteSpace(metaObj["author"]?.ToString()))
		{
			return;
		}

		metaObj["author"] = author;
	}

	private static void UpdateChromaKey(JsonObject metaObj, string? chromaKey, byte[] finalGlbBytes)
	{
		string finalChromaKey = DetermineChromaKey(chromaKey, metaObj, finalGlbBytes);
		if (string.IsNullOrEmpty(finalChromaKey))
		{
			return;
		}

		metaObj["chroma_key"] = finalChromaKey;
	}

	public static ModelConversionResult ConvertToRmesh(
		ReadOnlySpan<byte> inputBytes,
		string? inputFileName = null,
		string? assetType = null,
		bool force = false,
		OptimizationOptions? options = null,
		string? author = null,
		string? chromaKey = null,
		string? existingMetadataJson = null)
	{
		var result = new ModelConversionResult
		{
			OriginalSize = inputBytes.Length
		};

		if (inputBytes.Length == 0)
		{
			result.Success = false;
			result.ErrorMessage = "Empty input bytes.";
			return result;
		}

		try
		{
			var (rawGlbBytes, metaJson) = ParseInputBytes(inputBytes, existingMetadataJson);
			
			JsonObject metaObj = ParseExistingMetadata(metaJson);
			string? effectiveAssetType = DetermineEffectiveAssetType(assetType, metaObj);

			if (string.IsNullOrWhiteSpace(effectiveAssetType))
			{
				result.Success = false;
				result.ErrorMessage = "Asset type must be specified (Character, Building, Prop, Item) or present in model metadata.";
				return result;
			}

			var optRes = ProcessGlbOptimization(rawGlbBytes, effectiveAssetType, force, options);
			if (!optRes.Success || optRes.OutputGlb == null)
			{
				result.Success = false;
				result.ErrorMessage = optRes.ErrorMessage ?? "Optimization failed.";
				return result;
			}

			byte[] finalGlbBytes = optRes.OutputGlb;
			bool supportsTeamColor = GlbPlayerColorProcessor.DetectSupportsTeamColor(finalGlbBytes);

			UpdateMetadataObject(metaObj, inputFileName, effectiveAssetType, author, chromaKey, finalGlbBytes, supportsTeamColor);

			return BuildFinalRmeshResult(result, metaObj, finalGlbBytes, supportsTeamColor, effectiveAssetType);
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static byte[]? ExtractGlbFromRmesh(ReadOnlySpan<byte> rmeshBytes)
	{
		return RmeshFile.GetGlbBytes(rmeshBytes);
	}

	public static ModelConversionResult ExtractGlbFromRmesh(string inputRmeshPath, string? outputGlbPath = null)
	{
		string fullInput = Path.GetFullPath(inputRmeshPath);
		var result = new ModelConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input RMESH file not found: {inputRmeshPath}";
			return result;
		}

		string targetGlb = !string.IsNullOrEmpty(outputGlbPath)
			? Path.GetFullPath(outputGlbPath)
			: Path.ChangeExtension(fullInput, ".glb");
		result.OutputPath = targetGlb;

		try
		{
			byte[] rmeshBytes = File.ReadAllBytes(fullInput);
			result.OriginalSize = rmeshBytes.Length;

			byte[]? glbBytes = ExtractGlbFromRmesh(rmeshBytes);
			if (glbBytes == null || glbBytes.Length == 0)
			{
				result.Success = false;
				result.ErrorMessage = "Failed to extract GLB payload from RMESH file.";
				return result;
			}

			string? targetDir = Path.GetDirectoryName(targetGlb);
			if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetGlb, glbBytes);

			result.Success = true;
			result.OutputBytes = glbBytes;
			result.OptimizedSize = glbBytes.Length;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}


	private static ModelConversionResult ProcessGlbToGlb(string fullInput, string target, string? assetType, bool force, OptimizationOptions? options, string? author)
	{
		var result = new ModelConversionResult { InputPath = fullInput, OutputPath = target };
		try
		{
			byte[] inputGlbBytes = File.ReadAllBytes(fullInput);
			result.OriginalSize = inputGlbBytes.Length;

			bool shouldForce = force || (options.HasValue && options.Value.ForceReDecimate);
			bool isAlreadyOptimized = GlbManifestUtils.HasOptimizationFlag(inputGlbBytes);

			byte[] outputGlbBytes;
			if (!shouldForce && isAlreadyOptimized)
			{
				outputGlbBytes = inputGlbBytes;
			}
			else
			{
				byte[] unoptimized = GlbManifestUtils.StripOptimizationMetadata(inputGlbBytes).UnoptimizedBytes;
				var optRes = ProcessGlbOptimization(inputGlbBytes, assetType, force, options);
				outputGlbBytes = optRes.Success && optRes.OutputGlb != null ? optRes.OutputGlb : unoptimized;
			}

			string? targetDir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

			File.WriteAllBytes(target, outputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(target);

			result.Success = true;
			result.OutputBytes = outputGlbBytes;
			result.OptimizedSize = outputGlbBytes.Length;
			result.SupportsTeamColor = GlbPlayerColorProcessor.DetectSupportsTeamColor(outputGlbBytes);
			result.AssetType = assetType;
			result.Author = author;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static ModelConversionResult ConvertModelFile(
		string inputPath,
		string? outputPath = null,
		string? assetType = null,
		bool force = false,
		OptimizationOptions? options = null,
		string? author = null,
		string? chromaKey = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		string ext = Path.GetExtension(fullInput).ToLowerInvariant();
		string defaultExt = ext == ".rmesh" ? ".glb" : ".rmesh";

		string target = !string.IsNullOrEmpty(outputPath)
			? Path.GetFullPath(outputPath)
			: Path.ChangeExtension(fullInput, defaultExt);

		if (ext == ".rmesh" && Path.GetExtension(target).Equals(".glb", StringComparison.OrdinalIgnoreCase))
		{
			return ExtractGlbFromRmesh(fullInput, target);
		}

		if (ext == ".glb" && Path.GetExtension(target).Equals(".glb", StringComparison.OrdinalIgnoreCase))
		{
			return ProcessGlbToGlb(fullInput, target, assetType, force, options, author);
		}

		return ConvertToRmesh(fullInput, target, assetType, force, options, author, chromaKey);
	}

	public static int ConvertModelDirectory(
		string inputDir,
		string? outputDir = null,
		string? assetType = null,
		bool recursive = false,
		bool force = false)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			if (!IsModelFile(file)) continue;

			string fileExt = Path.GetExtension(file).ToLowerInvariant();
			string defaultExt = fileExt == ".rmesh" ? ".glb" : ".rmesh";

			string target;
			if (string.IsNullOrEmpty(fullOutputDir))
			{
				target = Path.ChangeExtension(file, defaultExt);
			}
			else
			{
				string rel = Path.GetRelativePath(fullInputDir, file);
				target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, defaultExt));
			}

			var res = ConvertModelFile(file, target, assetType, force);
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

		Console.WriteLine($"Finished model conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
