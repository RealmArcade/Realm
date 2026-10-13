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
	static ModelOptimizerService()
	{
		GltfDocumentExtensionMsftLod.RegisterExtension();
	}

	public struct OptimizationOptions
	{
		public float AllowedPixelError { get; set; } = 1.5f;
		public float CreaseAngleDegrees { get; set; } = GlbMeshSmoother.DefaultCreaseAngleDegrees;
		public int MaxTextureResolution { get; set; } = 1024;
		public bool ForceReDecimate { get; set; } = false;
		public LodTierConfig[] LodTiers { get; set; } = null;

		public OptimizationOptions()
		{
		}
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
		public string ErrorMessage;
	}

	public ModelOptimizerService()
	{
		GltfDocumentExtensionMsftLod.RegisterExtension();
	}

	public OptimizationResult OptimizeGlb(byte[] glbBytes, OptimizationOptions options = default)
	{
		if (options.MaxTextureResolution <= 0)
		{
			options.MaxTextureResolution = 1024;
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
			CompressTextures = true
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
			LodTriangleCounts = Array.Empty<int>(),
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

	public static (Shape3D Shape, Vector3 Offset) GenerateAnalyticalCollisionShape(Aabb aabb, bool isBuilding = false, float marginRatio = 1.0f)
	{
		Vector3 center = aabb.GetCenter();
		Vector3 size = aabb.Size * marginRatio;

		if (isBuilding)
		{
			var box = new BoxShape3D
			{
				Size = new Vector3(Math.Max(1.0f, size.X), Math.Max(1.0f, size.Y), Math.Max(1.0f, size.Z))
			};
			return (box, center);
		}

		float radius = Math.Max(size.X, size.Z) * 0.5f;
		float height = Math.Max(1.0f, size.Y);

		if (height >= radius * 2.0f)
		{
			var capsule = new CapsuleShape3D
			{
				Radius = Math.Max(0.2f, radius),
				Height = height
			};
			return (capsule, center);
		}
		else
		{
			var cylShape = new CylinderShape3D
			{
				Radius = Math.Max(0.2f, radius),
				Height = height
			};
			return (cylShape, center);
		}
	}
}