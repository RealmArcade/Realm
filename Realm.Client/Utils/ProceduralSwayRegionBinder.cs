using System;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Metadata;
using Realm.Shared.Terrain;

namespace Realm.Client.Utils;

public readonly struct ProceduralSwayMeshMask
{
	public bool Enabled { get; init; }

	public SpatialMaskMode MaskMode { get; init; }

	public float MaskMin { get; init; }

	public float MaskMax { get; init; }

	public float SignedMaskPower { get; init; }

	public Vector3 RegionMin { get; init; }

	public Vector3 RegionMax { get; init; }
}

public class ProceduralSwayRegionBinder
{
	private const float MinimumBoundsSize = 0.001f;

	private readonly Node3D _modelRoot;
	private readonly ProceduralAnimationConfig _config;
	private readonly HashSet<ulong> _patternMatchedMeshIds = new();
	private readonly bool _patternSelectionActive;

	public ProceduralSwayRegionBinder(Node3D modelRoot, ProceduralAnimationConfig config, string? modelKey)
	{
		_modelRoot = modelRoot;
		_config = config;
		Resolution = ProceduralSwayRegionResolver.Resolve(config, modelKey);
		ModelBounds = modelRoot.CalculateLocalBounds();
		TotalMeshCount = CollectPatternMatches(modelRoot);
		_patternSelectionActive = Resolution.UsesPartPatterns && _patternMatchedMeshIds.Count > 0;
	}

	public ProceduralSwayRegionResolution Resolution { get; }

	public Aabb ModelBounds { get; }

	public int TotalMeshCount { get; }

	public int MatchedMeshCount => _patternMatchedMeshIds.Count;

	public bool PatternSelectionActive => _patternSelectionActive;

	public bool MatchesMesh(GeometryInstance3D geometryInstance)
	{
		if (geometryInstance == null) return false;
		if (!_patternSelectionActive) return true;
		return _patternMatchedMeshIds.Contains(geometryInstance.GetInstanceId());
	}

	public ProceduralSwayMeshMask BuildMeshMask(GeometryInstance3D geometryInstance)
	{
		if (!Resolution.HasRegionBox)
		{
			return new ProceduralSwayMeshMask
			{
				Enabled = true,
				MaskMode = _config.MaskMode,
				MaskMin = _config.MaskMin,
				MaskMax = _config.MaskMax,
				SignedMaskPower = _config.MaskInvert ? -MathF.Abs(_config.MaskPower) : MathF.Abs(_config.MaskPower),
				RegionMin = new Vector3(_config.MaskRegionMin.X, _config.MaskRegionMin.Y, _config.MaskRegionMin.Z),
				RegionMax = new Vector3(_config.MaskRegionMax.X, _config.MaskRegionMax.Y, _config.MaskRegionMax.Z)
			};
		}

		Vector3 modelSpaceMin = ToModelSpacePoint(Resolution.NormalizedMin);
		Vector3 modelSpaceMax = ToModelSpacePoint(Resolution.NormalizedMax);
		Vector3 orderedMin = new(MathF.Min(modelSpaceMin.X, modelSpaceMax.X), MathF.Min(modelSpaceMin.Y, modelSpaceMax.Y), MathF.Min(modelSpaceMin.Z, modelSpaceMax.Z));
		Vector3 orderedMax = new(MathF.Max(modelSpaceMin.X, modelSpaceMax.X), MathF.Max(modelSpaceMin.Y, modelSpaceMax.Y), MathF.Max(modelSpaceMin.Z, modelSpaceMax.Z));

		Transform3D relativeTransform = ComputeRelativeTransform(geometryInstance);
		Transform3D inverseTransform = relativeTransform.AffineInverse();

		TransformCornersToLocal(inverseTransform, orderedMin, orderedMax, out Vector3 localMin, out Vector3 localMax);

		float featherModelUnits = MathF.Max(Resolution.Feather, 0.0f) * MathF.Max(ModelBounds.Size.Y, MinimumBoundsSize);
		float localFeather = (inverseTransform.Basis * new Vector3(0.0f, featherModelUnits, 0.0f)).Length();
		if (localFeather < MinimumBoundsSize)
		{
			localFeather = MathF.Max(featherModelUnits, MinimumBoundsSize);
		}

		return new ProceduralSwayMeshMask
		{
			Enabled = true,
			MaskMode = SpatialMaskMode.RegionBox,
			MaskMin = localFeather,
			MaskMax = 0.0f,
			SignedMaskPower = Resolution.MaskInvert ? -MathF.Abs(Resolution.MaskPower) : MathF.Abs(Resolution.MaskPower),
			RegionMin = localMin,
			RegionMax = localMax
		};
	}

	private Vector3 ToModelSpacePoint(Vector3Data normalized)
	{
		return new Vector3(
			ModelBounds.Position.X + Mathf.Clamp(normalized.X, 0.0f, 1.0f) * ModelBounds.Size.X,
			ModelBounds.Position.Y + Mathf.Clamp(normalized.Y, 0.0f, 1.0f) * ModelBounds.Size.Y,
			ModelBounds.Position.Z + Mathf.Clamp(normalized.Z, 0.0f, 1.0f) * ModelBounds.Size.Z
		);
	}

	private Transform3D ComputeRelativeTransform(Node node)
	{
		Transform3D relativeTransform = Transform3D.Identity;
		Node? current = node;
		while (current != null && current != _modelRoot)
		{
			if (current is Node3D node3D)
			{
				relativeTransform = node3D.Transform * relativeTransform;
			}

			current = current.GetParent();
		}

		return relativeTransform;
	}

	private static void TransformCornersToLocal(Transform3D inverseTransform, Vector3 boxMin, Vector3 boxMax, out Vector3 localMin, out Vector3 localMax)
	{
		localMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
		localMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

		for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
		{
			Vector3 corner = new(
				(cornerIndex & 1) == 0 ? boxMin.X : boxMax.X,
				(cornerIndex & 2) == 0 ? boxMin.Y : boxMax.Y,
				(cornerIndex & 4) == 0 ? boxMin.Z : boxMax.Z);

			Vector3 localCorner = inverseTransform * corner;
			localMin = new Vector3(MathF.Min(localMin.X, localCorner.X), MathF.Min(localMin.Y, localCorner.Y), MathF.Min(localMin.Z, localCorner.Z));
			localMax = new Vector3(MathF.Max(localMax.X, localCorner.X), MathF.Max(localMax.Y, localCorner.Y), MathF.Max(localMax.Z, localCorner.Z));
		}
	}

	private int CollectPatternMatches(Node node)
	{
		int meshCount = 0;
		if (node is GeometryInstance3D geometryInstance)
		{
			meshCount++;
			if (Resolution.UsesPartPatterns && MeshMatchesPatterns(geometryInstance))
			{
				_patternMatchedMeshIds.Add(geometryInstance.GetInstanceId());
			}
		}

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			meshCount += CollectPatternMatches(node.GetChild(i));
		}

		return meshCount;
	}

	private bool MeshMatchesPatterns(GeometryInstance3D geometryInstance)
	{
		if (ProceduralSwayRegionResolver.MatchesAnyPattern(Resolution.MeshNamePatterns, geometryInstance.Name.ToString()))
		{
			return true;
		}

		return MatchesMaterialPatterns(geometryInstance);
	}

	private bool MatchesMaterialPatterns(GeometryInstance3D geometryInstance)
	{
		if (Resolution.MaterialNamePatterns.Count == 0) return false;

		if (geometryInstance is MeshInstance3D meshInstance)
		{
			int surfaceCount = meshInstance.Mesh != null ? meshInstance.Mesh.GetSurfaceCount() : 0;
			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
			{
				if (ProceduralSwayRegionResolver.MatchesAnyPattern(Resolution.MaterialNamePatterns, ReadMaterialName(meshInstance, surfaceIndex)))
				{
					return true;
				}
			}
		}

		if (geometryInstance.MaterialOverride != null
		    && ProceduralSwayRegionResolver.MatchesAnyPattern(Resolution.MaterialNamePatterns, ReadMaterialResourceName(geometryInstance.MaterialOverride)))
		{
			return true;
		}

		return false;
	}

	private static string ReadMaterialName(MeshInstance3D meshInstance, int surfaceIndex)
	{
		Material? surfaceMaterial = meshInstance.Mesh?.SurfaceGetMaterial(surfaceIndex);
		if (surfaceMaterial != null)
		{
			string surfaceName = ReadMaterialResourceName(surfaceMaterial);
			if (!string.IsNullOrEmpty(surfaceName)) return surfaceName;
		}

		Material? overrideMaterial = meshInstance.GetSurfaceOverrideMaterial(surfaceIndex);
		return overrideMaterial != null ? ReadMaterialResourceName(overrideMaterial) : string.Empty;
	}

	private static string ReadMaterialResourceName(Material material)
	{
		string resourceName = material.ResourceName;
		if (!string.IsNullOrEmpty(resourceName)) return resourceName;

		string resourcePath = material.ResourcePath;
		if (!string.IsNullOrEmpty(resourcePath)) return System.IO.Path.GetFileName(resourcePath);

		return string.Empty;
	}
}
