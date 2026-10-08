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

        var selectedVerbTypes = GetSelectedVerbTypes(args, verbTypes);
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
                ProcessArgWithSeparatorForSanitize(currentArg, separatorIndex, lookup, sanitized);
                continue;
            }

            ProcessArgWithoutSeparatorForSanitize(args, ref index, currentArg, lookup, sanitized);
        }

        return sanitized.ToArray();
    }

    private static IEnumerable<Type> GetSelectedVerbTypes(string[] args, IEnumerable<Type> verbTypes)
    {
        var verbList = verbTypes.ToList();

        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            return verbList;
        }

        string verbName = args[0];
        var matchingVerb = verbList.FirstOrDefault(type =>
        {
            var verbAttribute = type.GetCustomAttributes().FirstOrDefault(attribute => attribute.GetType().Name is "VerbAttribute" or "Verb");
            if (verbAttribute == null)
            {
                return false;
            }

            string? name = verbAttribute.GetType().GetProperty("Name")?.GetValue(verbAttribute)?.ToString();
            return string.Equals(name, verbName, StringComparison.OrdinalIgnoreCase);
        });

        return matchingVerb != null ? [matchingVerb] : verbList;
    }

    private static void ProcessArgWithSeparatorForSanitize(string currentArg, int separatorIndex, Dictionary<string, BooleanOptionMetadata> lookup, List<string> sanitized)
    {
        string flagPart = currentArg[..separatorIndex];
        string valuePart = currentArg[(separatorIndex + 1)..];
        string cleanFlag = flagPart.TrimStart('-');

        if (!TryMatchOption(lookup, cleanFlag, out var matchedMetadata, out bool isNegatedPrefix) ||
            !TryParseBoolean(valuePart, out bool parsedBooleanValue))
        {
            sanitized.Add(currentArg);
            return;
        }

        bool finalValue = isNegatedPrefix
            ? !parsedBooleanValue
            : (matchedMetadata.IsInverted ? !parsedBooleanValue : parsedBooleanValue);

        if (finalValue)
        {
            sanitized.Add($"--{matchedMetadata.CanonicalLongName}");
        }
    }

    private static void ProcessArgWithoutSeparatorForSanitize(string[] args, ref int index, string currentArg, Dictionary<string, BooleanOptionMetadata> lookup, List<string> sanitized)
    {
        string flagName = currentArg.TrimStart('-');
        if (!TryMatchOption(lookup, flagName, out var metadata, out bool isNegated))
        {
            sanitized.Add(currentArg);
            return;
        }

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
            return;
        }

        bool switchValue = !isNegated && !metadata.IsInverted;

        if (switchValue)
        {
            sanitized.Add($"--{metadata.CanonicalLongName}");
        }
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
                ProcessArgWithSeparatorForOverride(currentArg, separatorIndex, lookup, options);
                continue;
            }

            ProcessArgWithoutSeparatorForOverride(args, ref index, currentArg, lookup, options);
        }
    }

    private static void ProcessArgWithSeparatorForOverride(string currentArg, int separatorIndex, Dictionary<string, BooleanOptionMetadata> lookup, object options)
    {
        string flagPart = currentArg[..separatorIndex];
        string valuePart = currentArg[(separatorIndex + 1)..];
        string cleanFlag = flagPart.TrimStart('-');

        if (!TryMatchOption(lookup, cleanFlag, out var matchedMetadata, out bool isNegatedPrefix) ||
            !TryParseBoolean(valuePart, out bool parsedBooleanValue))
        {
            return;
        }

        bool finalValue = isNegatedPrefix
            ? !parsedBooleanValue
            : (matchedMetadata.IsInverted ? !parsedBooleanValue : parsedBooleanValue);

        matchedMetadata.Property.SetValue(options, finalValue);
    }

    private static void ProcessArgWithoutSeparatorForOverride(string[] args, ref int index, string currentArg, Dictionary<string, BooleanOptionMetadata> lookup, object options)
    {
        string flagName = currentArg.TrimStart('-');
        if (!TryMatchOption(lookup, flagName, out var metadata, out bool isNegated))
        {
            return;
        }

        if (index + 1 < args.Length && TryParseBoolean(args[index + 1], out bool nextBooleanValue))
        {
            index++;
            bool finalValue = isNegated
                ? !nextBooleanValue
                : (metadata.IsInverted ? !nextBooleanValue : nextBooleanValue);

            metadata.Property.SetValue(options, finalValue);
            return;
        }

        bool switchValue = !isNegated && !metadata.IsInverted;

        metadata.Property.SetValue(options, switchValue);
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
                ProcessProperty(property, lookup);
            }
        }

        return lookup;
    }

    private static void ProcessProperty(PropertyInfo property, Dictionary<string, BooleanOptionMetadata> lookup)
    {
        if (!IsBooleanProperty(property))
        {
            return;
        }

        var optionAttribute = GetOptionAttribute(property);
        if (optionAttribute == null)
        {
            return;
        }

        string? longName = GetAttributePropertyValue(optionAttribute, "LongName");
        string? shortName = GetAttributePropertyValue(optionAttribute, "ShortName");

        string canonical = GetCanonicalName(longName, shortName, property.Name);

        var metadata = new BooleanOptionMetadata
        {
            CanonicalLongName = canonical,
            IsInverted = false,
            Property = property
        };

        RegisterPropertyNames(lookup, property.Name, longName, shortName, metadata);
    }

    private static bool IsBooleanProperty(PropertyInfo property)
    {
        return property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?);
    }

    private static object? GetOptionAttribute(PropertyInfo property)
    {
        return property.GetCustomAttributes()
            .FirstOrDefault(attribute => attribute.GetType().Name is "OptionAttribute" or "Option");
    }

    private static string? GetAttributePropertyValue(object attribute, string propertyName)
    {
        return attribute.GetType().GetProperty(propertyName)?.GetValue(attribute)?.ToString();
    }

    private static string GetCanonicalName(string? longName, string? shortName, string propertyName)
    {
        if (!string.IsNullOrWhiteSpace(longName))
        {
            return longName;
        }

        if (!string.IsNullOrWhiteSpace(shortName))
        {
            return shortName;
        }
        
        return propertyName.ToLowerInvariant();
    }

    private static void RegisterPropertyNames(Dictionary<string, BooleanOptionMetadata> lookup, string propertyName, string? longName, string? shortName, BooleanOptionMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(longName))
        {
            RegisterNames(lookup, longName, metadata);
        }

        if (!string.IsNullOrWhiteSpace(shortName))
        {
            RegisterNames(lookup, shortName, metadata);
        }

        RegisterNames(lookup, propertyName, metadata);
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
