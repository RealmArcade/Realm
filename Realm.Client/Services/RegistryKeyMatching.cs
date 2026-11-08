﻿using System;
using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Ecs.Common;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Whether an id names a prop, resource, unit or building: the registry lookups first, then a scan of
///     the registry metadata, then the asset path conventions the editor uses for imported files.
/// </summary>
internal static class RegistryKeyMatching
{
	internal static bool CheckPropRegistryForMatch(string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (GameHost.PropRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && GameHost.PropRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}

	internal static bool MatchesUnitKey(string unitId, string? modelPath, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (isMatch(unitId, normTarget)) return true;
		if (isMatch(modelPath, normTarget)) return true;
		if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (GameHost.BuildingRegistry.TryGetValue(unitId, out var bldMeta) && isMatch(bldMeta.ModelPath, normTarget)) return true;
		return false;
	}

	internal static bool IsPropOrResourceKey(string key, string norm, string? resolved, Func<string, string> normalizeFunc)
	{
		if (GameHost.PropRegistry.ContainsKey(key) || GameHost.PropRegistry.ContainsKey(norm)) return true;
		if (GameHost.ResourceRegistry.ContainsKey(key) || GameHost.ResourceRegistry.ContainsKey(norm)) return true;

		if (HasMatchingPropOrResourceMetadata(norm, normalizeFunc)) return true;
		if (MatchesPropOrResourcePathPattern(key) || MatchesPropOrResourcePathPattern(norm)) return true;

		return !string.IsNullOrEmpty(resolved) && MatchesPropOrResourcePathPattern(resolved);
	}

	internal static bool HasMatchingPropOrResourceMetadata(string norm, Func<string, string> normalizeFunc)
	{
		return HasMatchingPropMetadata(norm, normalizeFunc) || HasMatchingResourceMetadata(norm, normalizeFunc);
	}

	internal static bool HasMatchingPropMetadata(string norm, Func<string, string> normalizeFunc)
	{
		foreach (var propMeta in GameHost.PropRegistry.Values)
		{
			if (!string.IsNullOrEmpty(propMeta.ModelPath) && normalizeFunc(propMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(propMeta.TemplateID) && normalizeFunc(propMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	internal static bool HasMatchingResourceMetadata(string norm, Func<string, string> normalizeFunc)
	{
		foreach (var resMeta in GameHost.ResourceRegistry.Values)
		{
			if (!string.IsNullOrEmpty(resMeta.ModelPath) && normalizeFunc(resMeta.ModelPath) == norm) return true;
			if (!string.IsNullOrEmpty(resMeta.TemplateID) && normalizeFunc(resMeta.TemplateID) == norm) return true;
		}

		return false;
	}

	internal static bool MatchesPropOrResourcePathPattern(string path)
	{
		return path.Contains("/props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\props\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("props/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("/resources/", StringComparison.OrdinalIgnoreCase) ||
		       path.Contains("\\resources\\", StringComparison.OrdinalIgnoreCase) ||
		       path.StartsWith("resources/", StringComparison.OrdinalIgnoreCase);
	}

	internal static bool CheckBuildingRegistryForMatch(string id, string cleanId, string normTarget, Func<string?, string, bool> isMatch)
	{
		if (GameHost.BuildingRegistry.TryGetValue(id, out var meta) && isMatch(meta.ModelPath, normTarget)) return true;
		if (!string.IsNullOrEmpty(cleanId) && GameHost.BuildingRegistry.TryGetValue(cleanId, out meta) && isMatch(meta.ModelPath, normTarget)) return true;
		return false;
	}
}
