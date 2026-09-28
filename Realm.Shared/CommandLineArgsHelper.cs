using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Realm.Shared;

public static class CommandLineArgsHelper
{
	private class BooleanOptionMetadata
	{
		public string CanonicalLongName { get; set; } = string.Empty;
		public bool IsInverted { get; set; }
		public PropertyInfo Property { get; set; } = null!;
	}

	public static string[] SanitizeArgs(string[] args, params Type[] verbTypes)
	{
		return SanitizeArgs(args, (IEnumerable<Type>)verbTypes);
	}

	public static string[] SanitizeArgs(string[] args, IEnumerable<Type> verbTypes)
	{
		if (args == null || args.Length == 0)
		{
			return Array.Empty<string>();
		}

		var verbList = verbTypes.ToList();
		var selectedVerbTypes = verbList;

		if (args.Length > 0 && !args[0].StartsWith('-'))
		{
			string verbName = args[0];
			var matchingVerb = verbList.FirstOrDefault(type =>
			{
				var verbAttribute = type.GetCustomAttributes().FirstOrDefault(attribute => attribute.GetType().Name is "VerbAttribute" or "Verb");
				if (verbAttribute != null)
				{
					string? name = verbAttribute.GetType().GetProperty("Name")?.GetValue(verbAttribute)?.ToString();
					return string.Equals(name, verbName, StringComparison.OrdinalIgnoreCase);
				}
				return false;
			});

			if (matchingVerb != null)
			{
				selectedVerbTypes = [matchingVerb];
			}
		}

		var lookup = BuildOptionLookup(selectedVerbTypes);
		var sanitized = new List<string>(args.Length);

		for (int index = 0; index < args.Length; index++)
		{
			string currentArg = args[index];

			if (!currentArg.StartsWith('-') || currentArg == "-" || currentArg == "--")
			{
				sanitized.Add(currentArg);
				continue;
			}

			int separatorIndex = currentArg.IndexOfAny(['=', ':']);
			if (separatorIndex >= 0)
			{
				string flagPart = currentArg[..separatorIndex];
				string valuePart = currentArg[(separatorIndex + 1)..];
				string cleanFlag = flagPart.TrimStart('-');

				if (TryMatchOption(lookup, cleanFlag, out var matchedMetadata, out bool isNegatedPrefix))
				{
					if (TryParseBoolean(valuePart, out bool parsedBooleanValue))
					{
						bool finalValue = isNegatedPrefix
							? !parsedBooleanValue
							: (matchedMetadata.IsInverted ? !parsedBooleanValue : parsedBooleanValue);

						if (finalValue)
						{
							sanitized.Add($"--{matchedMetadata.CanonicalLongName}");
						}
						continue;
					}
				}

				sanitized.Add(currentArg);
				continue;
			}

			string flagName = currentArg.TrimStart('-');
			if (TryMatchOption(lookup, flagName, out var metadata, out bool isNegated))
			{
				if (index + 1 < args.Length && TryParseBoolean(args[index + 1], out bool nextBooleanValue))
				{
					index++;
					bool finalValue = isNegated
						? !nextBooleanValue
						: (metadata.IsInverted ? !nextBooleanValue : nextBooleanValue);

					if (finalValue)
					{
						sanitized.Add($"--{metadata.CanonicalLongName}");
					}
					continue;
				}

				bool switchValue = isNegated
					? false
					: (metadata.IsInverted ? false : true);

				if (switchValue)
				{
					sanitized.Add($"--{metadata.CanonicalLongName}");
				}
				continue;
			}

			sanitized.Add(currentArg);
		}

		return sanitized.ToArray();
	}

	public static void ApplyBooleanOverrides(object? options, string[]? args)
	{
		if (options == null || args == null || args.Length == 0)
		{
			return;
		}

		var lookup = BuildOptionLookup([options.GetType()]);

		for (int index = 0; index < args.Length; index++)
		{
			string currentArg = args[index];

			if (!currentArg.StartsWith('-') || currentArg == "-" || currentArg == "--")
			{
				continue;
			}

			int separatorIndex = currentArg.IndexOfAny(['=', ':']);
			if (separatorIndex >= 0)
			{
				string flagPart = currentArg[..separatorIndex];
				string valuePart = currentArg[(separatorIndex + 1)..];
				string cleanFlag = flagPart.TrimStart('-');

				if (TryMatchOption(lookup, cleanFlag, out var matchedMetadata, out bool isNegatedPrefix))
				{
					if (TryParseBoolean(valuePart, out bool parsedBooleanValue))
					{
						bool finalValue = isNegatedPrefix
							? !parsedBooleanValue
							: (matchedMetadata.IsInverted ? !parsedBooleanValue : parsedBooleanValue);

						matchedMetadata.Property.SetValue(options, finalValue);
					}
				}
				continue;
			}

			string flagName = currentArg.TrimStart('-');
			if (TryMatchOption(lookup, flagName, out var metadata, out bool isNegated))
			{
				if (index + 1 < args.Length && TryParseBoolean(args[index + 1], out bool nextBooleanValue))
				{
					index++;
					bool finalValue = isNegated
						? !nextBooleanValue
						: (metadata.IsInverted ? !nextBooleanValue : nextBooleanValue);

					metadata.Property.SetValue(options, finalValue);
					continue;
				}

				bool switchValue = isNegated
					? false
					: (metadata.IsInverted ? false : true);

				metadata.Property.SetValue(options, switchValue);
			}
		}
	}

	public static bool TryParseBoolean(string? text, out bool result)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			result = false;
			return false;
		}

		string trimmed = text.Trim();

		if (bool.TryParse(trimmed, out result))
		{
			return true;
		}

		string lower = trimmed.ToLowerInvariant();
		if (lower is "1" or "yes" or "y" or "on" or "enable" or "enabled" or "t")
		{
			result = true;
			return true;
		}

		if (lower is "0" or "no" or "n" or "off" or "disable" or "disabled" or "f")
		{
			result = false;
			return true;
		}

		result = false;
		return false;
	}

	private static Dictionary<string, BooleanOptionMetadata> BuildOptionLookup(IEnumerable<Type> verbTypes)
	{
		var lookup = new Dictionary<string, BooleanOptionMetadata>(StringComparer.OrdinalIgnoreCase);

		foreach (var type in verbTypes)
		{
			var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
			foreach (var property in properties)
			{
				if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?))
				{
					continue;
				}

				var optionAttribute = property.GetCustomAttributes()
					.FirstOrDefault(attribute => attribute.GetType().Name is "OptionAttribute" or "Option");

				if (optionAttribute == null)
				{
					continue;
				}

				var attributeType = optionAttribute.GetType();
				string? longName = attributeType.GetProperty("LongName")?.GetValue(optionAttribute)?.ToString();
				string? shortName = attributeType.GetProperty("ShortName")?.GetValue(optionAttribute)?.ToString();

				string canonical = !string.IsNullOrWhiteSpace(longName)
					? longName
					: (!string.IsNullOrWhiteSpace(shortName) ? shortName : property.Name.ToLowerInvariant());

				var metadata = new BooleanOptionMetadata
				{
					CanonicalLongName = canonical,
					IsInverted = false,
					Property = property
				};

				if (!string.IsNullOrWhiteSpace(longName))
				{
					RegisterNames(lookup, longName, metadata);
				}

				if (!string.IsNullOrWhiteSpace(shortName))
				{
					RegisterNames(lookup, shortName, metadata);
				}

				RegisterNames(lookup, property.Name, metadata);
			}
		}

		return lookup;
	}

	private static void RegisterNames(Dictionary<string, BooleanOptionMetadata> lookup, string rawName, BooleanOptionMetadata metadata)
	{
		lookup[rawName] = metadata;

		string normalized = NormalizeName(rawName);
		lookup[normalized] = metadata;

		string withHyphens = rawName.Replace('_', '-');
		lookup[withHyphens] = metadata;

		string withUnderscores = rawName.Replace('-', '_');
		lookup[withUnderscores] = metadata;

		if (rawName.StartsWith("no-", StringComparison.OrdinalIgnoreCase) || rawName.StartsWith("no_", StringComparison.OrdinalIgnoreCase))
		{
			string positiveForm = rawName[3..];
			var invertedMetadata = new BooleanOptionMetadata
			{
				CanonicalLongName = metadata.CanonicalLongName,
				IsInverted = true,
				Property = metadata.Property
			};

			lookup[positiveForm] = invertedMetadata;
			lookup[NormalizeName(positiveForm)] = invertedMetadata;
			lookup[positiveForm.Replace('_', '-')] = invertedMetadata;
			lookup[positiveForm.Replace('-', '_')] = invertedMetadata;
		}
	}

	private static string NormalizeName(string name)
	{
		return name.Replace("-", "").Replace("_", "").ToLowerInvariant();
	}

	private static bool TryMatchOption(
		Dictionary<string, BooleanOptionMetadata> lookup,
		string flagName,
		out BooleanOptionMetadata metadata,
		out bool isNegatedPrefix)
	{
		isNegatedPrefix = false;

		if (lookup.TryGetValue(flagName, out metadata!))
		{
			return true;
		}

		string normalized = NormalizeName(flagName);
		if (lookup.TryGetValue(normalized, out metadata!))
		{
			return true;
		}

		if (flagName.StartsWith("no-", StringComparison.OrdinalIgnoreCase) ||
			flagName.StartsWith("no_", StringComparison.OrdinalIgnoreCase))
		{
			string stripped = flagName[3..];
			if (lookup.TryGetValue(stripped, out metadata!) ||
				lookup.TryGetValue(NormalizeName(stripped), out metadata!))
			{
				isNegatedPrefix = true;
				return true;
			}
		}

		metadata = null!;
		return false;
	}
}
