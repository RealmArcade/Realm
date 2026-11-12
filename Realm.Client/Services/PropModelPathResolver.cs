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
public static class PropModelPathResolver
{
	private static readonly Dictionary<string, string> ResolvedPathCache = new(StringComparer.OrdinalIgnoreCase);

	public static string Resolve(string propId)
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
	///     The same lookup for a caller that only wants a path that names a file: the registries first, then the key taken
	///     as the model path it already is, and no memo, because these keys are rewritten by the editor while it runs. A
	///     key the registries do not name and that resolves to no file reads as no path at all.
	/// </summary>
	public static string ResolveRegistered(string assetKey)
	{
		if (string.IsNullOrEmpty(assetKey))
			return string.Empty;

		string? targetModel = FindRegistryModelPath(assetKey, Path.GetFileNameWithoutExtension(assetKey));
		if (!string.IsNullOrEmpty(targetModel))
		{
			string? resolvedPath = ModelCache.ResolveModelPath(targetModel);
			if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
			{
				return resolvedPath;
			}

			return targetModel;
		}

		return ResolveKeyAsModelPath(assetKey);
	}

	/// <summary>
	///     A template's model path is often the only thing its asset key says, and the file it names need not share the
	///     name of any template: the melee ruleset names <c>gold_mine.rmesh</c> while its template is
	///     <c>resource/melee_gold_mine</c>. Such a key is a real model, so it is allowed to name itself as long as the path
	///     resolves to a file, which is what keeps a key that names nothing at all from becoming an empty prop group.
	/// </summary>
	/// <param name="assetKey">The key a prop registry entry or a model path gives.</param>
	private static string ResolveKeyAsModelPath(string assetKey)
	{
		string? resolvedPath = ModelCache.ResolveModelPath(assetKey);
		if (!string.IsNullOrEmpty(resolvedPath) && (resolvedPath.StartsWith("res://") || File.Exists(resolvedPath)))
		{
			return resolvedPath;
		}

		return string.Empty;
	}

	public static void Invalidate(string propId)
	{
		if (string.IsNullOrEmpty(propId)) return;
		ResolvedPathCache.Remove(propId);
	}

	public static void Clear()
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
