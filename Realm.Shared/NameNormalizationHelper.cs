using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AnyAscii;
using Fastenshtein;

namespace Realm.Shared;

public static class NameNormalizationHelper
{
    public static string ToAscii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return input.Transliterate();
    }

    public static string NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return string.Empty;
        }

        var asciiRepresentation = ToAscii(username).ToLowerInvariant();
        var stringBuilder = new StringBuilder(asciiRepresentation.Length);

        foreach (var character in asciiRepresentation)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                stringBuilder.Append(character);
            }
        }

        return stringBuilder.ToString();
    }

    public static string NormalizeMapName(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return string.Empty;
        }

        var asciiRepresentation = ToAscii(mapName).ToLowerInvariant();
        var stringBuilder = new StringBuilder(asciiRepresentation.Length);

        foreach (var character in asciiRepresentation)
        {
            if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
            {
                stringBuilder.Append(character);
            }
        }

        return stringBuilder.ToString();
    }

    public static string NormalizeMapNameAlphaOnly(string? mapName)
    {
        if (string.IsNullOrWhiteSpace(mapName))
        {
            return string.Empty;
        }

        var asciiRepresentation = ToAscii(mapName).ToLowerInvariant();
        var stringBuilder = new StringBuilder(asciiRepresentation.Length);

        foreach (var character in asciiRepresentation)
        {
            if (character >= 'a' && character <= 'z')
            {
                stringBuilder.Append(character);
            }
        }

        return stringBuilder.ToString();
    }

    public static int ComputeLevenshteinDistance(string? source, string? target)
    {
        if (string.IsNullOrEmpty(source))
        {
            return target?.Length ?? 0;
        }

        if (string.IsNullOrEmpty(target))
        {
            return source.Length;
        }

        return Levenshtein.Distance(source, target);
    }

    public static bool IsMapNameTooSimilar(string candidateMapName, IEnumerable<string> existingMapNames, int minDistanceThreshold, out string? conflictingMapName)
    {
        conflictingMapName = null;

        var normalizedCandidate = NormalizeMapName(candidateMapName);
        if (string.IsNullOrEmpty(normalizedCandidate))
        {
            return false;
        }

        foreach (var existingName in existingMapNames)
        {
            if (string.IsNullOrWhiteSpace(existingName))
            {
                continue;
            }

            var normalizedExisting = NormalizeMapName(existingName);
            if (string.IsNullOrEmpty(normalizedExisting))
            {
                continue;
            }

            if (string.Equals(normalizedCandidate, normalizedExisting, StringComparison.OrdinalIgnoreCase))
            {
                conflictingMapName = existingName;
                return true;
            }

            if (normalizedCandidate.Length >= 3 && normalizedExisting.Length >= 3)
            {
                var distance = ComputeLevenshteinDistance(normalizedCandidate, normalizedExisting);
                var effectiveThreshold = normalizedCandidate.Length <= 4 || normalizedExisting.Length <= 4
                    ? 1
                    : Math.Min(minDistanceThreshold, 2);

                if (distance <= effectiveThreshold)
                {
                    conflictingMapName = existingName;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool AreUsernamesConflicting(string? usernameA, string? usernameB)
    {
        var normalizedA = NormalizeUsername(usernameA);
        var normalizedB = NormalizeUsername(usernameB);

        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB))
        {
            return false;
        }

        return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
    }

    public static bool AreMapNamesConflicting(string? mapNameA, string? mapNameB)
    {
        var normalizedA = NormalizeMapName(mapNameA);
        var normalizedB = NormalizeMapName(mapNameB);

        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB))
        {
            return false;
        }

        return string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ValidateUsername(string? username, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(username))
        {
            errorMessage = "Username cannot be empty.";
            return false;
        }

        var trimmed = username.Trim();
        if (trimmed.Length < 3 || trimmed.Length > 32)
        {
            errorMessage = "Username must be between 3 and 32 characters.";
            return false;
        }

        var normalized = NormalizeUsername(trimmed);
        if (string.IsNullOrEmpty(normalized))
        {
            errorMessage = "Username must contain at least one alphanumeric character.";
            return false;
        }

        return true;
    }

    public static bool ValidateMapName(string? mapName, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(mapName))
        {
            errorMessage = "Map name cannot be empty.";
            return false;
        }

        var trimmed = mapName.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 64)
        {
            errorMessage = "Map name must be between 2 and 64 characters.";
            return false;
        }

        var normalized = NormalizeMapName(trimmed);
        if (string.IsNullOrEmpty(normalized))
        {
            errorMessage = "Map name must contain at least one valid alphanumeric character.";
            return false;
        }

        return true;
    }
}
