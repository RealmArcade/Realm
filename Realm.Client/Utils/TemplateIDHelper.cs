using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Realm.Client.Utils;

public static partial class TemplateIDHelper
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

	public static string GenerateTemplateID(string objectType, string assetFileNameOrName, HashSet<string> existingTemplateIDs)
	{
		string baseSlug = ToSnakeCase(assetFileNameOrName);
		string candidate = $"{objectType}/{baseSlug}";
		if (!existingTemplateIDs.Contains(candidate))
		{
			return candidate;
		}

		int index = 2;
		while (existingTemplateIDs.Contains($"{objectType}/{baseSlug}_{index}"))
		{
			index++;
		}
		return $"{objectType}/{baseSlug}_{index}";
	}

	public static string GenerateTemplateID(string objectType, string assetFileNameOrName, IEnumerable<StringName> existingTemplateIDs)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var sn in existingTemplateIDs)
		{
			set.Add(sn.ToString());
		}
		return GenerateTemplateID(objectType, assetFileNameOrName, set);
	}

	public static string NormalizeTemplateID(string objectType, string rawId)
	{
		if (string.IsNullOrWhiteSpace(rawId)) return string.Empty;
		string trimmed = rawId.Trim();
		if (trimmed.Contains('/')) return trimmed;
		return $"{objectType}/{ToSnakeCase(trimmed)}";
	}

	public static (string ObjectType, string Slug) ParseTemplateID(string objectId)
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
