using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Services.ModelOptimization;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Services;

namespace Realm.Client;


public partial class PropMultiMeshManager : Node3D
{
	private static Transform3D GetRelativeTransform(Node node, Node root)
	{
		Transform3D xform = Transform3D.Identity;
		Node curr = node;
		while (curr != null && curr != root)
		{
			if (curr is Node3D n3d)
			{
				xform = n3d.Transform * xform;
			}

			curr = curr.GetParent();
		}

		return xform;
	}

	private static void FindMeshInstancesRecursive(Node parent, List<MeshInstance3D> result)
	{
		if (parent == null) return;
		if (parent is MeshInstance3D mi)
		{
			result.Add(mi);
		}

		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			FindMeshInstancesRecursive(parent.GetChild(i), result);
		}
	}

	private static string ResolveModelPathForAssetKey(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey))
			return string.Empty;

		string cleanId = Path.GetFileNameWithoutExtension(normAssetKey);
		string targetModel = GetModelPathFromRegistries(normAssetKey, cleanId);

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string? resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}

	private static string GetModelPathFromRegistries(string normAssetKey, string cleanId)
	{
		if (GameHost.PropRegistry != null && TryGetModelPath(GameHost.PropRegistry, normAssetKey, cleanId, out string? propPath)) return propPath;
		if (GameHost.ResourceRegistry != null && TryGetModelPath(GameHost.ResourceRegistry, normAssetKey, cleanId, out string? resPath)) return resPath;
		if (GameHost.UnitRegistry != null && TryGetModelPath(GameHost.UnitRegistry, normAssetKey, cleanId, out string? unitPath)) return unitPath;
		if (GameHost.BuildingRegistry != null && TryGetModelPath(GameHost.BuildingRegistry, normAssetKey, cleanId, out string? bldPath)) return bldPath;

		return normAssetKey;
	}

	private static bool TryGetModelPath<T>(Dictionary<StringName, T> registry, string normAssetKey, string cleanId, [NotNullWhen(true)] out string? modelPath)
	{
		modelPath = null;

		if (registry.TryGetValue(normAssetKey, out var meta) && TryGetModelPathFromMeta(meta, out modelPath))
		{
			return true;
		}

		if (!string.IsNullOrEmpty(cleanId) && registry.TryGetValue(cleanId, out meta) && TryGetModelPathFromMeta(meta, out modelPath))
		{
			return true;
		}

		return false;
	}

	private static bool TryGetModelPathFromMeta(object? meta, [NotNullWhen(true)] out string? modelPath)
	{
		modelPath = null;
		if (meta is PropMetadata propMeta && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			modelPath = propMeta.ModelPath;
			return true;
		}

		if (meta is ResourceMetadata resMeta && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			modelPath = resMeta.ModelPath;
			return true;
		}

		if (meta is UnitMetadata unitMeta && !string.IsNullOrEmpty(unitMeta.ModelPath))
		{
			modelPath = unitMeta.ModelPath;
			return true;
		}

		return false;
	}

}
