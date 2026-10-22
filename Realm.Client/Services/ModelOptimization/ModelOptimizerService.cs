using System;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Services;
using Realm.Shared;
using Realm.Shared.ModelOptimization;

namespace Realm.Client.Services.ModelOptimization;

public class ModelOptimizerService
{


	public struct OptimizationOptions
	{
		// Required by the language: a struct with property initializers needs an explicit parameterless constructor.
		public OptimizationOptions()
		{
		}

		public float AllowedPixelError { get; set; } = 1.5f;
		public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
		public int MaxTextureResolution { get; set; } = 1024;
		public bool ForceReDecimate { get; set; } = false;
		public MeshSurfaceCategory? SurfaceCategory { get; set; } = null;
		public LodTierConfig[] LodTiers { get; set; } = null;
		public bool ReduceUvIslands { get; set; } = true;
		public float UvChartMergeTolerance { get; set; } = UvChartReducer.DefaultTexCoordTolerance;

	}

	public struct OptimizationResult
	{
		public bool Success;
		public byte[] OptimizedGlbBytes;
		public int OriginalTriangleCount;
		public int OptimizedTriangleCount;
		public int[] LodTriangleCounts;
		public float ReductionRatio;
		public bool DecimationSkipped;
		public int TexturesProcessedCount;
		public int ChosenTextureResolution;
		public int UvIslandCountBefore;
		public int UvIslandCountAfter;
		public float UvChartMergeTolerance;
		public string ErrorMessage;
	}

	public OptimizationResult OptimizeGlb(byte[] glbBytes, OptimizationOptions options = default)
	{
		if (options.MaxTextureResolution <= 0)
		{
			options = new OptimizationOptions();
		}

		if (options.AllowedPixelError <= 0f)
		{
			options.AllowedPixelError = 1.5f;
		}

		var sharedOptions = new Shared.OptimizationOptions
		{
			AllowedPixelError = options.AllowedPixelError,
			MaxTextureResolution = options.MaxTextureResolution,
			ForceReDecimate = options.ForceReDecimate,
			CreaseAngleDegrees = options.CreaseAngleDegrees,
			CompressTextures = true,
			DecimationProfile = options.SurfaceCategory.HasValue ? LodDecimationProfile.ForCategory(options.SurfaceCategory.Value) : null,
			ReduceUvIslands = options.ReduceUvIslands,
			UvChartMergeTolerance = options.UvChartMergeTolerance
		};

		var sharedOptimizer = new GlbOptimizer();
		var sharedResult = sharedOptimizer.Optimize(glbBytes, sharedOptions);

		return new OptimizationResult
		{
			Success = sharedResult.Success,
			OptimizedGlbBytes = sharedResult.OutputGlbBytes,
			ErrorMessage = sharedResult.ErrorMessage,
			DecimationSkipped = sharedResult.DecimationSkipped,
			OriginalTriangleCount = 0,
			OptimizedTriangleCount = 0,
			LodTriangleCounts = sharedResult.LodTriangleCounts ?? Array.Empty<int>(),
			UvIslandCountBefore = sharedResult.UvIslandCountBefore,
			UvIslandCountAfter = sharedResult.UvIslandCountAfter,
			UvChartMergeTolerance = sharedResult.UvChartMergeTolerance,
			ReductionRatio = sharedResult.OriginalSize > 0 ? ((float)sharedResult.OptimizedSize / sharedResult.OriginalSize) : 1.0f
		};
	}

	public static bool HasOptimizationCompletedFlag(string filePath)
	{
		if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;
		byte[] bytes = File.ReadAllBytes(filePath);
		return GlbManifestUtils.HasOptimizationFlag(bytes);
	}

	public static bool HasOptimizationCompletedFlag(byte[] glbBytes)
	{
		return GlbManifestUtils.HasOptimizationFlag(glbBytes);
	}

	public static bool HasDecimationCompletedFlag(string filePath) => HasOptimizationCompletedFlag(filePath);

	public static bool HasDecimationCompletedFlag(byte[] glbBytes) => HasOptimizationCompletedFlag(glbBytes);


	public OptimizationResult OptimizeAndImportGlbDirect(byte[] glbBytes, int maxTextureResolution = 1024, float creaseAngleDegrees = GlbMeshSmoother.DefaultCreaseAngleDegrees, float allowedPixelError = 1.5f, bool forceReDecimate = false)
	{
		var options = new OptimizationOptions
		{
			MaxTextureResolution = maxTextureResolution,
			CreaseAngleDegrees = creaseAngleDegrees,
			AllowedPixelError = allowedPixelError,
			ForceReDecimate = forceReDecimate
		};
		return OptimizeGlb(glbBytes, options);
	}

	public static float CalculateTargetErrorFromScreenHeight(int viewportHeightPixels, float allowedPixelError = 1.5f)
	{
		if (viewportHeightPixels <= 0)
		{
			return 0.05f;
		}

		return Math.Clamp(allowedPixelError / viewportHeightPixels, 0.0001f, 0.5f);
	}

}