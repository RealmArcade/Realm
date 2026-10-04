using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Godot;

namespace Realm.Godot.Utils;

public static partial class ObjectIDHelper
{
	[GeneratedRegex(@"([a-z0-9])([A-Z])")]
	private static partial Regex CamelCaseSplitRegex();

	[GeneratedRegex(@"[\s\-\.]+")]
	private static partial Regex SeparatorRegex();

	[GeneratedRegex(@"[^a-z0-9_]")]
	private static partial Regex InvalidCharRegex();

	[GeneratedRegex(@"_+")]
	private static partial Regex MultipleUnderscoreRegex();

	public static string ToSnakeCase(string input)
	{
		if (string.IsNullOrWhiteSpace(input)) return "object";

		string name = Path.GetFileNameWithoutExtension(input.Trim());

		name = CamelCaseSplitRegex().Replace(name, "$1_$2");
		name = SeparatorRegex().Replace(name, "_");
		name = name.ToLowerInvariant();
		name = InvalidCharRegex().Replace(name, "");
		name = MultipleUnderscoreRegex().Replace(name, "_");
		name = name.Trim('_');

		return string.IsNullOrEmpty(name) ? "object" : name;
	}

	public static string GenerateSlug(string assetFileNameOrName, HashSet<string>? existingSlugs = null)
	{
		string baseSlug = ToSnakeCase(assetFileNameOrName);
		if (existingSlugs == null || !existingSlugs.Contains(baseSlug))
		{
			return baseSlug;
		}

		int index = 2;
		while (existingSlugs.Contains($"{baseSlug}_{index}"))
		{
			index++;
		}
		return $"{baseSlug}_{index}";
	}

	public static string GenerateObjectID(string objectType, string assetFileNameOrName, HashSet<string> existingObjectIDs)
	{
		string baseSlug = ToSnakeCase(assetFileNameOrName);
		string candidate = $"{objectType}/{baseSlug}";
		if (!existingObjectIDs.Contains(candidate))
		{
			return candidate;
		}

		int index = 2;
		while (existingObjectIDs.Contains($"{objectType}/{baseSlug}_{index}"))
		{
			index++;
		}
		return $"{objectType}/{baseSlug}_{index}";
	}

	public static string GenerateObjectID(string objectType, string assetFileNameOrName, IEnumerable<StringName> existingObjectIDs)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var sn in existingObjectIDs)
		{
			set.Add(sn.ToString());
		}
		return GenerateObjectID(objectType, assetFileNameOrName, set);
	}

	public static string NormalizeObjectID(string objectType, string rawId)
	{
		if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;
		string trimmed = rawId.Trim();
		if (trimmed.Contains('/')) return trimmed;
		return $"{objectType}/{ToSnakeCase(trimmed)}";
	}

	public static (string ObjectType, string Slug) ParseObjectID(string objectId)
	{
		if (string.IsNullOrWhiteSpace(objectId)) return (string.Empty, string.Empty);
		int slashIdx = objectId.IndexOf('/');
		if (slashIdx >= 0)
		{
			return (objectId.Substring(0, slashIdx), objectId.Substring(slashIdx + 1));
		}
		return (string.Empty, objectId);
	}
}
