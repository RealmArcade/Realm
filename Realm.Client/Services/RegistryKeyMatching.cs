using System;
using System.Collections.Generic;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     Whether an id names a prop, resource, unit or building: the one placeable store first, then a scan of
///     the stored templates, then the asset path conventions the editor uses for imported files.
/// </summary>
public static class RegistryKeyMatching
{
	public static bool CheckPropRegistryForMatch(RegistryService registries, string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (registries.Placeables.TryGet(id, out PlaceableTemplate template) && template.Kind == PlaceableKind.Prop && isMatch(template.Metadata.ModelPath, normTarget)) return true;
		if (string.IsNullOrEmpty(cleanId)) return false;

		return registries.Placeables.TryGet(cleanId, out template) && template.Kind == PlaceableKind.Prop && isMatch(template.Metadata.ModelPath, normTarget);
	}

	public static bool MatchesUnitKey(RegistryService registries, string unitId, string? modelPath, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (isMatch(unitId, normTarget)) return true;
		if (isMatch(modelPath, normTarget)) return true;
		if (!registries.Placeables.TryGet(unitId, out PlaceableTemplate template)) return false;
		if (template.Kind is not (PlaceableKind.Unit or PlaceableKind.Building)) return false;

		return isMatch(template.Metadata.ModelPath, normTarget);
	}

	public static bool IsPropOrResourceKey(RegistryService registries, string key, string norm, string? resolved, Func<string, string> normalizeFunc)
	{
		if (IsKind(registries, key, PlaceableKind.Prop) || IsKind(registries, norm, PlaceableKind.Prop)) return true;
		if (IsKind(registries, key, PlaceableKind.Resource) || IsKind(registries, norm, PlaceableKind.Resource)) return true;

		if (HasMatchingPropOrResourceMetadata(registries, norm, normalizeFunc)) return true;
		if (MatchesPropOrResourcePathPattern(registries, key) || MatchesPropOrResourcePathPattern(registries, norm)) return true;

		return !string.IsNullOrEmpty(resolved) && MatchesPropOrResourcePathPattern(registries, resolved);
	}

	public static bool HasMatchingPropOrResourceMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingPropMetadata(registries, norm, normalizeFunc) || HasMatchingResourceMetadata(registries, norm, normalizeFunc);
	}

	public static bool HasMatchingPropMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingMetadata(registries.Placeables.ValuesOfKind(PlaceableKind.Prop), norm, normalizeFunc);
	}

	public static bool HasMatchingResourceMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingMetadata(registries.Placeables.ValuesOfKind(PlaceableKind.Resource), norm, normalizeFunc);
	}

	public static bool MatchesPropOrResourcePathPattern(RegistryService registries, string path)
	{
		return path.Contains("/props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("/resources/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase);
	}

	public static bool CheckBuildingRegistryForMatch(RegistryService registries, string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (registries.Placeables.TryGet(id, out PlaceableTemplate template) && template.Kind == PlaceableKind.Building && isMatch(template.Metadata.ModelPath, normTarget)) return true;
		if (string.IsNullOrEmpty(cleanId)) return false;

		return registries.Placeables.TryGet(cleanId, out template) && template.Kind == PlaceableKind.Building && isMatch(template.Metadata.ModelPath, normTarget);
	}

	private static bool IsKind(RegistryService registries, string key, PlaceableKind kind)
	{
		return !string.IsNullOrEmpty(key) && registries.Placeables.ContainsKey(key, kind);
	}

	private static bool HasMatchingMetadata(IEnumerable<PlaceableMetadata> templates, string norm, Func<string, string> normalizeFunc)
	{
		foreach (PlaceableMetadata metadata in templates)
		{
			if (!string.IsNullOrEmpty(metadata.ModelPath) && normalizeFunc(metadata.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(metadata.TemplateID) && normalizeFunc(metadata.TemplateID) == norm) return true;
		}

		return false;
	}
}
