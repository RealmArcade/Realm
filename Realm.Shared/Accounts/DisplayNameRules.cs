using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Realm.Shared.Accounts;

/// <summary>
///     Keeps the name a screen states for its own seat readable and inside the length a lobby message can carry. The name is
///     Steam's persona, so nothing is registered and nothing is looked up: there is no uniqueness key and no lookalike check,
///     because a name is not claimed by anybody and cannot be taken from anybody. What remains is the part a lobby still
///     needs - collapsing the whitespace a seat typed, refusing the control and format characters a player cannot see, and
///     cutting the result at a grapheme boundary so a surrogate pair, or a base plus its combining marks, is never split.
/// </summary>
public static class DisplayNameRules
{
	private const int MinimumGraphemeClusters = 3;

	private const int MinimumCjkGraphemeClusters = 2;

	private const int MaximumGraphemeClusters = 20;

	/// <summary>How many UTF-8 bytes of a name a lobby message may carry, which is also the name's length ceiling.</summary>
	public const int MaximumUtf8Bytes = 128;

	private const char SpaceCharacter = ' ';

	private static readonly HashSet<int> AllowedPunctuationCodePoints = new() { '-', '_', '.', '\'' };

	private static bool TryCanonicalize(string? candidate, out string canonical, out DisplayNameError error)
	{
		canonical = string.Empty;
		error = DisplayNameError.None;

		if (string.IsNullOrWhiteSpace(candidate))
		{
			error = DisplayNameError.Empty;
			return false;
		}

		string collapsed = CollapseWhitespace(candidate);
		if (collapsed.Length == 0)
		{
			error = DisplayNameError.Empty;
			return false;
		}

		if (!RejectForbiddenCodePoints(collapsed, out error)) return false;

		string normalised;
		try
		{
			normalised = collapsed.Normalize(NormalizationForm.FormC);
		}
		catch (ArgumentException)
		{
			error = DisplayNameError.DisallowedCharacter;
			return false;
		}

		if (!HasCombiningMarksAttachedToBases(normalised))
		{
			error = DisplayNameError.CombiningMarkWithoutBase;
			return false;
		}

		int graphemeClusters = CountGraphemeClusters(normalised);
		if (graphemeClusters < MinimumGraphemeClusters && !IsShortCjkName(normalised, graphemeClusters))
		{
			error = DisplayNameError.TooShort;
			return false;
		}

		if (graphemeClusters > MaximumGraphemeClusters)
		{
			error = DisplayNameError.TooLong;
			return false;
		}

		if (Encoding.UTF8.GetByteCount(normalised) > MaximumUtf8Bytes)
		{
			error = DisplayNameError.TooManyBytes;
			return false;
		}

		if (!ContainsLetterOrDigit(normalised))
		{
			error = DisplayNameError.NoLettersOrDigits;
			return false;
		}

		canonical = normalised;
		return true;
	}

	/// <summary>
	///     The name a lobby may carry for a seat. A persona that already reads cleanly survives untouched, so a player sees the
	///     name Steam shows; anything else is stripped of the characters a lobby cannot carry and cut to the seat's ceiling.
	/// </summary>
	public static string AdmitForLobby(string? candidate, int maximumUtf16Units)
	{
		if (string.IsNullOrWhiteSpace(candidate)) return string.Empty;
		if (TryCanonicalize(candidate, out string canonical, out _) && canonical.Length <= maximumUtf16Units) return canonical;

		return TruncateAtGraphemeBoundary(StripDisallowedCodePoints(candidate.Trim()), maximumUtf16Units);
	}

	/// <summary>
	///     Cuts a string to a UTF-16 ceiling without ever splitting a grapheme cluster, so a surrogate pair, a base plus its
	///     combining marks, or a joined family stays whole or is dropped entirely.
	/// </summary>
	public static string TruncateAtGraphemeBoundary(string value, int maximumUtf16Units)
	{
		if (string.IsNullOrEmpty(value) || value.Length <= maximumUtf16Units) return value;

		var builder = new StringBuilder(value.Length);
		int usedUnits = 0;
		TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
		while (elements.MoveNext())
		{
			string element = (string)elements.Current;
			if (usedUnits + element.Length > maximumUtf16Units) break;
			builder.Append(element);
			usedUnits += element.Length;
		}

		return builder.ToString();
	}

	/// <summary>
	///     Trims the ends and collapses runs of the one space a name may contain. No other whitespace is turned into a space:
	///     a tab, a line break or a non-breaking space inside a name is a forbidden code point and is reported as one, rather
	///     than being silently rewritten into something the player never typed.
	/// </summary>
	private static string CollapseWhitespace(string value)
	{
		var builder = new StringBuilder(value.Length);
		bool previousWasSpace = false;
		foreach (Rune rune in value.Trim().EnumerateRunes())
		{
			if (rune.Value == SpaceCharacter)
			{
				if (previousWasSpace) continue;
				builder.Append(SpaceCharacter);
				previousWasSpace = true;
				continue;
			}

			previousWasSpace = false;
			builder.Append(rune.ToString());
		}

		return builder.ToString().TrimEnd();
	}

	private static bool RejectForbiddenCodePoints(string value, out DisplayNameError error)
	{
		error = DisplayNameError.None;
		foreach (Rune rune in value.EnumerateRunes())
		{
			if (rune.Value == SpaceCharacter) continue;
			if (AllowedPunctuationCodePoints.Contains(rune.Value)) continue;

			UnicodeCategory category = Rune.GetUnicodeCategory(rune);
			switch (category)
			{
				case UnicodeCategory.UppercaseLetter:
				case UnicodeCategory.LowercaseLetter:
				case UnicodeCategory.TitlecaseLetter:
				case UnicodeCategory.ModifierLetter:
				case UnicodeCategory.OtherLetter:
				case UnicodeCategory.DecimalDigitNumber:
				case UnicodeCategory.NonSpacingMark:
				case UnicodeCategory.SpacingCombiningMark:
				case UnicodeCategory.EnclosingMark:
					continue;
				default:
					error = DisplayNameError.DisallowedCharacter;
					return false;
			}
		}

		return true;
	}

	private static string StripDisallowedCodePoints(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (Rune rune in value.EnumerateRunes())
		{
			if (Rune.IsControl(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) continue;
			builder.Append(rune.ToString());
		}

		return builder.ToString();
	}

	private static bool HasCombiningMarksAttachedToBases(string value)
	{
		bool hasBase = false;
		foreach (Rune rune in value.EnumerateRunes())
		{
			UnicodeCategory category = Rune.GetUnicodeCategory(rune);
			bool isCombiningMark = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
			if (!isCombiningMark)
			{
				hasBase = true;
				continue;
			}

			if (!hasBase) return false;
		}

		return true;
	}

	private static int CountGraphemeClusters(string value)
	{
		if (value.Length == 0) return 0;
		return StringInfo.ParseCombiningCharacters(value).Length;
	}

	private static bool ContainsLetterOrDigit(string value)
	{
		foreach (Rune rune in value.EnumerateRunes())
		{
			if (Rune.IsLetterOrDigit(rune)) return true;
		}

		return false;
	}

	private static bool IsShortCjkName(string value, int graphemeClusters)
	{
		if (graphemeClusters != MinimumCjkGraphemeClusters) return false;

		foreach (Rune rune in value.EnumerateRunes())
		{
			if (IsCjkCodePoint(rune.Value)) continue;

			UnicodeCategory category = Rune.GetUnicodeCategory(rune);
			if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;

			return false;
		}

		return true;
	}

	private static bool IsCjkCodePoint(int codePoint)
	{
		return codePoint is >= 0x1100 and <= 0x11FF
			or >= 0x3005 and <= 0x3011
			or >= 0x3040 and <= 0x31FF
			or >= 0x3400 and <= 0x4DBF
			or >= 0x4E00 and <= 0x9FFF
			or >= 0xA960 and <= 0xA97F
			or >= 0xAC00 and <= 0xD7FF
			or >= 0xF900 and <= 0xFAFF
			or >= 0xFF66 and <= 0xFF9F
			or >= 0x20000 and <= 0x2FA1F;
	}
}
