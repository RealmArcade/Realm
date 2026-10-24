using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Core;
using Realm.Shared.Distribution;

namespace Realm.Client.UI;

public static class MapAttributionLookup
{
	public static IReadOnlyList<string> Resolve(IEnumerable<string>? sourceMapNames, string? mapTitle, string? version)
	{
		List<string> values = Clean(sourceMapNames);
		if (values.Count > 0) return values;

		return Clean(ReadFromLocalManifest(mapTitle, version));
	}

	public static List<string> Clean(IEnumerable<string>? sourceMapNames)
	{
		if (sourceMapNames == null) return new List<string>();

		return sourceMapNames
			.Where(sourceMapName => !string.IsNullOrWhiteSpace(sourceMapName))
			.Select(sourceMapName => sourceMapName.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(sourceMapName => sourceMapName, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<string> ReadFromLocalManifest(string? mapTitle, string? version)
	{
		if (string.IsNullOrWhiteSpace(mapTitle)) return new List<string>();

		try
		{
			string? mapDirectory = GameHost.ResolveMapDirectory(mapTitle, version);
			if (string.IsNullOrEmpty(mapDirectory)) return new List<string>();

			string manifestPath = Path.Combine(mapDirectory, "manifest.json");
			if (!File.Exists(manifestPath)) return new List<string>();

			MapManifest manifest = MapManifest.LoadFromFile(manifestPath);
			return manifest.Attribution_SourceMap ?? new List<string>();
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapAttributionLookup] Failed to read attribution for '{mapTitle}': {exception.Message}");
			return new List<string>();
		}
	}
}
