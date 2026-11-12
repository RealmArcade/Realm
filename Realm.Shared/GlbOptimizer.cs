using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;

namespace Realm.Shared;

public class GlbOptimizer
{
	public bool IsOptimized(byte[] glbBytes)
	{
		return GlbManifestUtils.HasOptimizationFlag(glbBytes);
	}

	public bool IsOptimized(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		byte[] bytes = File.ReadAllBytes(filePath);
		return IsOptimized(bytes);
	}

	private static int? GetArrayCount(JsonObject root, string propertyName)
	{
		if (!root.TryGetPropertyValue(propertyName, out var val)) return null;
		if (val is not JsonArray array) return null;
		return array.Count;
	}

	private static string? GetRealmVersion(JsonObject root)
	{
		if (!root.TryGetPropertyValue("extras", out var extrasVal)) return null;
		if (extrasVal is not JsonObject extras) return null;
		if (!extras.TryGetPropertyValue("realm_version", out var verVal)) return null;
		return verVal?.GetValue<string>();
	}

	private static float ResolveCreaseAngleDegrees(OptimizationOptions options)
	{
		if (options.CreaseAngleDegrees > 0f && options.CreaseAngleDegrees <= 180f) return options.CreaseAngleDegrees;
		return GlbMeshSmoother.DefaultCreaseAngleDegrees;
	}

	public GlbMetadata GetMetadata(byte[] glbBytes)
	{
		var meta = new GlbMetadata();
		var (json, _, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (json is not JsonObject root) return meta;

		meta.IsOptimized = GlbManifestUtils.HasOptimizationFlag(glbBytes);
		meta.RealmVersion = GetRealmVersion(root);
		meta.MeshCount = GetArrayCount(root, "meshes") ?? 0;
		meta.NodeCount = GetArrayCount(root, "nodes") ?? 0;
		meta.MaterialCount = GetArrayCount(root, "materials") ?? 0;
		meta.ImageCount = GetArrayCount(root, "images") ?? 0;

		return meta;
	}

	private static byte[] GetPackedBaseMesh(bool toolSuccess, byte[]? toolBytes, byte[] sanitized)
	{
		return (toolSuccess && toolBytes != null && toolBytes.Length > 0) ? toolBytes : sanitized;
	}

	private static float ResolveUvChartMergeTolerance(OptimizationOptions options)
	{
		if (!options.ReduceUvIslands) return 0f;
		if (options.UvChartMergeTolerance > 0f) return options.UvChartMergeTolerance;
		return UvChartReducer.DefaultTexCoordTolerance;
	}

	private static byte[] PackBaseMesh(byte[] glbBytes, OptimizationOptions options, ref OptimizationResult result)
	{
		byte[] smoothed = GlbMeshSmoother.SmoothMesh(glbBytes, ResolveCreaseAngleDegrees(options));
		byte[] sanitized = GlbManifestUtils.SanitizeMaterials(smoothed);

		var (toolSuccess, toolBytes, toolError) = NativeToolRunner.RunGltfPack(
			sanitized,
			1.0f,
			options.MaxTextureResolution,
			false
		);

		byte[] packedBaseMesh = GetPackedBaseMesh(toolSuccess, toolBytes, sanitized);
		RecordUvChartStatistics(packedBaseMesh, options, ref result);
		return packedBaseMesh;
	}

	private static void RecordUvChartStatistics(byte[] packedBaseMesh, OptimizationOptions options, ref OptimizationResult result)
	{
		float uvChartMergeTolerance = ResolveUvChartMergeTolerance(options);
		if (uvChartMergeTolerance <= 0f) return;

		var chartStatistics = UvChartReducer.Analyze(packedBaseMesh, uvChartMergeTolerance);
		result.UvIslandCountBefore = chartStatistics.IslandCountBefore;
		result.UvIslandCountAfter = chartStatistics.IslandCountAfter;
		result.UvChartMergeTolerance = chartStatistics.TexCoordTolerance;
	}

	private static byte[] CompressTextures(byte[] glbBytes, OptimizationOptions options)
	{
		return options.CompressTextures
			? GlbManifestUtils.EncodeGlbTexturesWebp(glbBytes, options.MaxTextureResolution)
			: glbBytes;
	}

	private static BaseTierDecimationOptions ResolveBaseTierDecimationOptions(OptimizationOptions options)
	{
		float allowedPixelError = options.AllowedPixelError > 0f ? options.AllowedPixelError : 1.5f;

		return new BaseTierDecimationOptions
		{
			TargetError = Math.Clamp(allowedPixelError / GlbLodGenerator.BaseTierReferenceViewportHeight, 0.0001f, 0.5f),
			MaximumTriangleCount = options.MaxBaseTierTriangleCount
		};
	}

	private static bool TryDecimateBaseTier(byte[] packedBaseMesh, OptimizationOptions options, ref OptimizationResult result, out byte[] decimatedBaseMesh)
	{
		decimatedBaseMesh = packedBaseMesh;

		var decimation = GlbLodGenerator.DecimateBaseMesh(
			packedBaseMesh,
			ResolveBaseTierDecimationOptions(options),
			options.DecimationProfile,
			ResolveUvChartMergeTolerance(options));

		if (!decimation.Success)
		{
			result.Success = false;
			result.ErrorMessage = $"Base tier decimation failed: {decimation.ErrorMessage}";
			return false;
		}

		decimatedBaseMesh = decimation.OutputGlbBytes;
		return true;
	}

	private void ProcessOptimization(byte[] glbBytes, OptimizationOptions options, ref OptimizationResult result)
	{
		byte[] packedBaseMesh = PackBaseMesh(glbBytes, options, ref result);
		if (!TryDecimateBaseTier(packedBaseMesh, options, ref result, out byte[] masterTierBytes)) return;

		var (lodSuccess, lodGlbBytes, lodError, lodTriangleCounts) = GlbLodGenerator.GenerateLods(masterTierBytes, decimationProfile: options.DecimationProfile, uvSeamMergeTolerance: ResolveUvChartMergeTolerance(options));
		if (!lodSuccess)
		{
			result.Success = false;
			result.ErrorMessage = $"LOD generation failed: {lodError}";
			return;
		}

		result.LodTriangleCounts = lodTriangleCounts;

		byte[] workingBytes = lodGlbBytes.Length > 0 ? lodGlbBytes : masterTierBytes;
		byte[] finalBytes = GlbManifestUtils.InjectOptimizationMetadata(
			CompressTextures(workingBytes, options),
			new Dictionary<string, object>
			{
				{ "original_size", result.OriginalSize },
				{ "simplification_ratio", options.SimplificationRatio }
			}
		);

		result.Success = true;
		result.OutputGlbBytes = finalBytes;
		result.OptimizedSize = finalBytes.Length;
	}

	private void ProcessBaseMeshOptimization(byte[] glbBytes, OptimizationOptions options, ref OptimizationResult result)
	{
		byte[] packedBaseMesh = PackBaseMesh(glbBytes, options, ref result);
		if (!TryDecimateBaseTier(packedBaseMesh, options, ref result, out byte[] masterTierBytes)) return;

		byte[] finalBytes = CompressTextures(masterTierBytes, options);

		result.Success = true;
		result.OutputGlbBytes = finalBytes;
		result.OptimizedSize = finalBytes.Length;
	}

	public OptimizationResult Optimize(byte[] glbBytes, OptimizationOptions options = default)
	{
		var result = new OptimizationResult
		{
			Success = false,
			OriginalSize = glbBytes?.Length ?? 0,
			OptimizedSize = glbBytes?.Length ?? 0,
			OutputGlbBytes = glbBytes
		};

		if (options.MaxTextureResolution <= 0)
		{
			options = new OptimizationOptions();
		}

		if (glbBytes == null || glbBytes.Length == 0)
		{
			result.ErrorMessage = "Empty or null GLB buffer.";
			return result;
		}

		if (!options.ForceReDecimate && IsOptimized(glbBytes))
		{
			result.Success = true;
			result.DecimationSkipped = true;
			result.OptimizedSize = glbBytes.Length;
			return result;
		}

		ProcessOptimization(glbBytes, options, ref result);
		return result;
	}

	public OptimizationResult PrepareForAutoRigging(byte[] glbBytes, int maxTextureResolution)
	{
		return OptimizeBaseMesh(glbBytes, new OptimizationOptions
		{
			MaxTextureResolution = maxTextureResolution,
			ForceReDecimate = true
		});
	}

	public OptimizationResult PrepareForAutoRiggingFile(string inputPath, string? outputPath, int maxTextureResolution)
	{
		return OptimizeBaseMeshFile(inputPath, outputPath, new OptimizationOptions
		{
			MaxTextureResolution = maxTextureResolution,
			ForceReDecimate = true
		});
	}

	public OptimizationResult OptimizeBaseMesh(byte[] glbBytes, OptimizationOptions options = default)
	{
		var result = new OptimizationResult
		{
			Success = false,
			OriginalSize = glbBytes?.Length ?? 0,
			OptimizedSize = glbBytes?.Length ?? 0,
			OutputGlbBytes = glbBytes
		};

		if (options.MaxTextureResolution <= 0)
		{
			options = new OptimizationOptions();
		}

		if (glbBytes == null || glbBytes.Length == 0)
		{
			result.ErrorMessage = "Empty or null GLB buffer.";
			return result;
		}

		if (!options.ForceReDecimate && IsOptimized(glbBytes))
		{
			result.Success = true;
			result.DecimationSkipped = true;
			result.OptimizedSize = glbBytes.Length;
			return result;
		}

		ProcessBaseMeshOptimization(glbBytes, options, ref result);
		return result;
	}

	public OptimizationResult OptimizeBaseMeshFile(string inputPath, string? outputPath = null, OptimizationOptions options = default)
	{
		if (!File.Exists(inputPath))
		{
			return new OptimizationResult
			{
				Success = false,
				ErrorMessage = $"Input file does not exist: {inputPath}"
			};
		}

		byte[] inputBytes = File.ReadAllBytes(inputPath);
		var result = OptimizeBaseMesh(inputBytes, options);

		if (result.Success && result.OutputGlbBytes != null)
		{
			string target = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
			string? dir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			File.WriteAllBytes(target, result.OutputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(target);
			result.OutputFilePath = target;
		}

		return result;
	}

	public OptimizationResult OptimizeFile(string inputPath, string? outputPath = null, OptimizationOptions options = default)
	{
		if (!File.Exists(inputPath))
		{
			return new OptimizationResult
			{
				Success = false,
				ErrorMessage = $"Input file does not exist: {inputPath}"
			};
		}

		byte[] inputBytes = File.ReadAllBytes(inputPath);
		var result = Optimize(inputBytes, options);

		if (result.Success && result.OutputGlbBytes != null)
		{
			string target = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
			string? dir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			File.WriteAllBytes(target, result.OutputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(target);
			result.OutputFilePath = target;
		}

		return result;
	}

	public UnoptimizationResult Unoptimize(byte[] glbBytes)
	{
		var result = new UnoptimizationResult
		{
			Success = false,
			OutputGlbBytes = glbBytes
		};

		if (glbBytes == null || glbBytes.Length == 0)
		{
			result.ErrorMessage = "Empty or null GLB buffer.";
			return result;
		}

		bool wasOpt = IsOptimized(glbBytes);
		var (strippedBytes, wasModified) = GlbManifestUtils.StripOptimizationMetadata(glbBytes);

		result.Success = true;
		result.WasOptimized = wasOpt;
		result.OutputGlbBytes = strippedBytes;
		return result;
	}

	public UnoptimizationResult UnoptimizeFile(string inputPath, string? outputPath = null)
	{
		if (!File.Exists(inputPath))
		{
			return new UnoptimizationResult
			{
				Success = false,
				ErrorMessage = $"Input file does not exist: {inputPath}"
			};
		}

		byte[] inputBytes = File.ReadAllBytes(inputPath);
		var result = Unoptimize(inputBytes);

		if (result.Success && result.OutputGlbBytes != null)
		{
			string target = string.IsNullOrEmpty(outputPath) ? inputPath : outputPath;
			string? dir = Path.GetDirectoryName(target);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			File.WriteAllBytes(target, result.OutputGlbBytes);
			RealmMetadataHelper.SyncBlake3Metadata(target);
			result.OutputFilePath = target;
		}

		return result;
	}
}