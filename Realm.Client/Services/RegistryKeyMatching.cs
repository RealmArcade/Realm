﻿using System;
using System.Collections.Generic;
using Realm.Ecs.Common;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Whether an id names a prop, resource, unit or building: the registry lookups first, then a scan of
///     the registry metadata, then the asset path conventions the editor uses for imported files.
/// </summary>
internal static class RegistryKeyMatching
{
	internal static bool CheckPropRegistryForMatch(RegistryService registries, string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (registries.PropRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && registries.PropRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}

	internal static bool MatchesUnitKey(RegistryService registries, string unitId, string? modelPath, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (isMatch(unitId, normTarget)) return true;
		if (isMatch(modelPath, normTarget)) return true;
		if (registries.UnitRegistry.TryGetValue(unitId, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (registries.BuildingRegistry.TryGetValue(unitId, out var bldMeta) && isMatch(bldMeta.ModelPath, normTarget)) return true;
		return false;
	}

	internal static bool IsPropOrResourceKey(RegistryService registries, string key, string norm, string? resolved, Func<string, string> normalizeFunc)
	{
		if (registries.PropRegistry.ContainsKey(key) || registries.PropRegistry.ContainsKey(norm)) return true;
		if (registries.ResourceRegistry.ContainsKey(key) || registries.ResourceRegistry.ContainsKey(norm)) return true;

		if (HasMatchingPropOrResourceMetadata(registries, norm, normalizeFunc)) return true;
		if (MatchesPropOrResourcePathPattern(registries, key) || MatchesPropOrResourcePathPattern(registries, norm)) return true;

		return !string.IsNullOrEmpty(resolved) && MatchesPropOrResourcePathPattern(registries, resolved);
	}

	internal static bool HasMatchingPropOrResourceMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingPropMetadata(registries, norm, normalizeFunc) || HasMatchingResourceMetadata(registries, norm, normalizeFunc);
	}

	internal static bool HasMatchingPropMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		foreach (var propMeta in registries.PropRegistry.Values)
		{
			if (!string.IsNullOrEmpty(propMeta.ModelPath) && normalizeFunc(propMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(propMeta.TemplateID) && normalizeFunc(propMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	internal static bool HasMatchingResourceMetadata(RegistryService registries, string norm, Func<string, string> normalizeFunc)
	{
		foreach (var resMeta in registries.ResourceRegistry.Values)
		{
			if (!string.IsNullOrEmpty(resMeta.ModelPath) && normalizeFunc(resMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(resMeta.TemplateID) && normalizeFunc(resMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	internal static bool MatchesPropOrResourcePathPattern(RegistryService registries, string path)
	{
		return path.Contains("/props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("/resources/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool CheckBuildingRegistryForMatch(RegistryService registries, string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (registries.BuildingRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && registries.BuildingRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}
}
