using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Realm.Shared;

public static class NameNormalizationHelper
{
    private static readonly Dictionary<char, string> HomoglyphAndLigatureMap = new()
    {
        { 'ß', "ss" },
        { 'æ', "ae" },
        { 'Æ', "AE" },
        { 'œ', "oe" },
        { 'Œ', "OE" },
        { 'ø', "o" },
        { 'Ø', "O" },
        { 'ł', "l" },
        { 'Ł', "L" },
        { 'đ', "d" },
        { 'Đ', "D" },
        { 'þ', "th" },
        { 'Þ', "TH" },
        { 'ð', "d" },
        { 'Ð', "D" },
        { 'ı', "i" },
        { 'İ', "I" },

        { 'а', "a" }, { 'А', "A" },
        { 'б', "b" }, { 'Б', "B" },
        { 'в', "v" }, { 'В', "V" },
        { 'г', "g" }, { 'Г', "G" },
        { 'д', "d" }, { 'Д', "D" },
        { 'е', "e" }, { 'Е', "E" },
        { 'ё', "e" }, { 'Ё', "E" },
        { 'ж', "z" }, { 'Ж', "Z" },
        { 'з', "z" }, { 'З', "Z" },
        { 'и', "i" }, { 'И', "I" },
        { 'й', "j" }, { 'Й', "J" },
        { 'к', "k" }, { 'К', "K" },
        { 'л', "l" }, { 'Л', "L" },
        { 'м', "m" }, { 'М', "M" },
        { 'н', "n" }, { 'Н', "N" },
        { 'о', "o" }, { 'О', "O" },
        { 'п', "p" }, { 'П', "P" },
        { 'р', "r" }, { 'Р', "R" },
        { 'с', "c" }, { 'С', "C" },
        { 'т', "t" }, { 'Т', "T" },
        { 'у', "u" }, { 'У', "U" },
        { 'ф', "f" }, { 'Ф', "F" },
        { 'х', "h" }, { 'Х', "H" },
        { 'ц', "c" }, { 'Ц', "C" },
        { 'ч', "c" }, { 'Ч', "C" },
        { 'ш', "s" }, { 'Ш', "S" },
        { 'щ', "s" }, { 'Щ', "S" },
        { 'ъ', "" },  { 'Ъ', "" },
        { 'ы', "y" }, { 'Ы', "Y" },
        { 'ь', "" },  { 'Ь', "" },
        { 'э', "e" }, { 'Э', "E" },
        { 'ю', "yu" }, { 'Ю', "YU" },
        { 'я', "ya" }, { 'Я', "YA" },
        { 'і', "i" }, { 'І', "I" },
        { 'ј', "j" }, { 'Ј', "J" },
        { 'є', "e" }, { 'Є', "E" },
        { 'ґ', "g" }, { 'Ґ', "G" },

        { 'α', "a" }, { 'Α', "A" },
        { 'β', "b" }, { 'Β', "B" },
        { 'γ', "g" }, { 'Γ', "G" },
        { 'δ', "d" }, { 'Δ', "D" },
        { 'ε', "e" }, { 'Ε', "E" },
        { 'ζ', "z" }, { 'Ζ', "Z" },
        { 'η', "h" }, { 'Η', "H" },
        { 'θ', "th" }, { 'Θ', "TH" },
        { 'ι', "i" }, { 'Ι', "I" },
        { 'κ', "k" }, { 'Κ', "K" },
        { 'λ', "l" }, { 'Λ', "L" },
        { 'μ', "m" }, { 'Μ', "M" },
        { 'ν', "n" }, { 'Ν', "N" },
        { 'ξ', "x" }, { 'Ξ', "X" },
        { 'ο', "o" }, { 'Ο', "O" },
        { 'π', "p" }, { 'Π', "P" },
        { 'ρ', "r" }, { 'Ρ', "P" },
        { 'σ', "s" }, { 'Σ', "S" },
        { 'ς', "s" },
        { 'τ', "t" }, { 'Τ', "T" },
        { 'υ', "u" }, { 'Υ', "Y" },
        { 'φ', "f" }, { 'Φ', "F" },
        { 'χ', "x" }, { 'Χ', "X" },
        { 'ψ', "ps" }, { 'Ψ', "PS" },
        { 'ω', "o" }, { 'Ω', "O" }
    };

    public static string ToAscii(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalizedString = input.Normalize(NormalizationForm.FormKD);
        var stringBuilder = new StringBuilder(normalizedString.Length);

        foreach (var character in normalizedString)
        {
            if (HomoglyphAndLigatureMap.TryGetValue(character, out var mappedValue))
            {
                stringBuilder.Append(mappedValue);
                continue;
            }

            if (character >= 0xFF01 && character <= 0xFF5E)
            {
                stringBuilder.Append((char)(character - 0xFEE0));
                continue;
            }

            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark ||
                unicodeCategory == UnicodeCategory.SpacingCombiningMark ||
                unicodeCategory == UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (character <= 127)
            {
                stringBuilder.Append(character);
            }
        }

        return stringBuilder.ToString();
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

    public static int ComputeLevenshteinDistance(ReadOnlySpan<char> source, ReadOnlySpan<char> target)
    {
        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        Span<int> previousRow = stackalloc int[target.Length + 1];
        Span<int> currentRow = stackalloc int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
        {
            previousRow[j] = j;
        }

        for (var i = 0; i < source.Length; i++)
        {
            currentRow[0] = i + 1;

            for (var j = 0; j < target.Length; j++)
            {
                var substitutionCost = source[i] == target[j] ? 0 : 1;

                var deletion = previousRow[j + 1] + 1;
                var insertion = currentRow[j] + 1;
                var substitution = previousRow[j] + substitutionCost;

                currentRow[j + 1] = Math.Min(Math.Min(deletion, insertion), substitution);
            }

            currentRow.CopyTo(previousRow);
        }

        return previousRow[target.Length];
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
                var distance = ComputeLevenshteinDistance(normalizedCandidate.AsSpan(), normalizedExisting.AsSpan());
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
