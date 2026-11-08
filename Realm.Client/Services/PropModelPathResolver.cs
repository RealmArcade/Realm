using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
using Realm.Client.Utils;

namespace Realm.Client.Services;

/// <summary>
///     Where the model file for a prop id lives: the registries first (prop, resource, unit, then building), then the
///     id taken as a path, then the asset conventions <see cref="ModelCache" /> knows. Owns the resolved-path cache so a
///     node only ever asks, and keeps the lookup usable without a scene tree.
/// </summary>
internal static class PropModelPathResolver
{
	private static readonly Dictionary<string, string> ResolvedPathCache = new(StringComparer.OrdinalIgnoreCase);

	internal static string Resolve(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		if (ResolvedPathCache.TryGetValue(propId, out var cachedPath))
			return cachedPath;

		string resolved = ResolveUncached(propId);
		ResolvedPathCache[propId] = resolved;
		return resolved;
	}

	/// <summary>
	///     The same lookup for a caller that only wants a path the registries actually name: no fallback to the key
	///     itself, and no memo, because these keys are rewritten by the editor while it runs.
	/// </summary>
	internal static string ResolveRegistered(string assetKey)
	{
		if (string.IsNullOrEmpty(assetKey))
			return string.Empty;

		string targetModel = FindRegistryModelPath(assetKey, Path.GetFileNameWithoutExtension(assetKey));
		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string? resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}

	internal static void Invalidate(string propId)
	{
		if (string.IsNullOrEmpty(propId)) return;
		ResolvedPathCache.Remove(propId);
	}

	internal static void Clear()
	{
		ResolvedPathCache.Clear();
	}

	private static string ResolveUncached(string propId)
	{
		string cleanId = Path.GetFileNameWithoutExtension(propId);
		string targetModel = FindRegistryModelPath(propId, cleanId) ?? propId;

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		string? resolvedPath = ModelCache.ResolveModelPath(targetModel);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return targetModel;
	}

	private static string? FindRegistryModelPath(string propId, string cleanId)
	{
		var registries = GameContext.RegistryService;
		if (registries == null) return null;

		if (registries.PropRegistry.TryGetValue(propId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath)) return propMeta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registries.PropRegistry.TryGetValue(cleanId, out propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath)) return propMeta.ModelPath;

		if (registries.ResourceRegistry.TryGetValue(propId, out var resourceMeta) && !string.IsNullOrEmpty(resourceMeta.ModelPath)) return resourceMeta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registries.ResourceRegistry.TryGetValue(cleanId, out resourceMeta) && !string.IsNullOrEmpty(resourceMeta.ModelPath)) return resourceMeta.ModelPath;

		if (registries.UnitRegistry.TryGetValue(propId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath)) return unitMeta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registries.UnitRegistry.TryGetValue(cleanId, out unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath)) return unitMeta.ModelPath;

		if (registries.BuildingRegistry.TryGetValue(propId, out var buildingMeta) && !string.IsNullOrEmpty(buildingMeta.ModelPath)) return buildingMeta.ModelPath;
		if (!string.IsNullOrEmpty(cleanId) && registries.BuildingRegistry.TryGetValue(cleanId, out buildingMeta) && !string.IsNullOrEmpty(buildingMeta.ModelPath)) return buildingMeta.ModelPath;

		return null;
	}
}
