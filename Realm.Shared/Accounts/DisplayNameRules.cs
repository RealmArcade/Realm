using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Realm.Shared.Accounts;

/// <summary>
///     The one validation routine every DisplayName passes through: registration, sign-in lookup, rename, lobby admission and
///     the player-facing preview all call it. A DisplayName is the only human-facing identity an account has, so the same
///     canonical bytes and the same uniqueness key must be produced everywhere.
///     <para>
///         The steps always run in this fixed order: trim and collapse whitespace, reject forbidden code points, normalize to
///         NFC, measure grapheme clusters and UTF-8 bytes, fold for the uniqueness key.
///     </para>
///     <para>
///         Two separate concepts live here on purpose. The <b>uniqueness key</b> is the folded canonical form and decides
///         whether two accounts claim the same name. The <b>confusable skeleton</b> is a best-effort lookalike reduction used
///         only to refuse a name that renders as another account's name through homoglyphs; it never decides uniqueness and is
///         reported under its own error.
///     </para>
/// </summary>
public static class DisplayNameRules
{
	public const int MinimumGraphemeClusters = 3;

	public const int MaximumGraphemeClusters = 20;

	public const int MaximumUtf8Bytes = 128;

	public const int MinimumPasswordLength = 10;

	private const char SpaceCharacter = ' ';

	private static readonly HashSet<int> AllowedPunctuationCodePoints = new() { '-', '_', '.', '\'' };

	private static readonly Dictionary<int, string> FoldExpansions = new()
	{
		{ 0x00DF, "ss" },
		{ 0x1E9E, "ss" },
		{ 0x0130, "i\u0307" },
		{ 0xFB00, "ff" },
		{ 0xFB01, "fi" },
		{ 0xFB02, "fl" },
		{ 0xFB03, "ffi" },
		{ 0xFB04, "ffl" },
		{ 0xFB05, "st" },
		{ 0xFB06, "st" }
	};

	private static readonly Dictionary<int, char> ConfusableLatinSkeletonMap = BuildConfusableLatinSkeletonMap();

	public static bool TryCanonicalize(string? candidate, out string canonical, out DisplayNameError error)
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
		if (graphemeClusters < MinimumGraphemeClusters)
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

	public static bool IsValid(string? candidate)
	{
		return TryCanonicalize(candidate, out _, out _);
	}

	/// <summary>
	///     The case fold the uniqueness index is keyed on. .NET exposes no full Unicode case-folding API, so this folds by
	///     upper-casing each rune invariantly (which collapses the Greek final sigma and the micro sign exactly as case folding
	///     does) plus a table for the expansions case folding performs and upper-casing does not (sharp s, ligatures, dotted
	///     capital I). The result is re-normalised to NFC so a decomposed variant of the same folded text keys identically.
	/// </summary>
	public static string Fold(string canonical)
	{
		if (string.IsNullOrEmpty(canonical)) return string.Empty;

		var builder = new StringBuilder(canonical.Length);
		foreach (Rune rune in canonical.EnumerateRunes())
		{
			if (FoldExpansions.TryGetValue(rune.Value, out string? expansion))
			{
				builder.Append(expansion);
				continue;
			}

			builder.Append(Rune.ToUpperInvariant(rune).ToString());
		}

		return builder.ToString().Normalize(NormalizationForm.FormC);
	}

	public static string ComputeUniquenessKey(string canonical)
	{
		return Fold(canonical);
	}

	/// <summary>
	///     Reduces a name to the shapes a reader cannot tell apart: the common Greek and Cyrillic homoglyphs of Latin letters
	///     are mapped onto their Latin lookalike, and spacing and punctuation are dropped so they cannot be used to slip a name
	///     past it. Combining marks are kept, because a mark genuinely changes what a reader sees, so a name with an accent does
	///     not collapse onto the same word without it. This is deliberately a bounded, documented approximation; it is a separate
	///     check from the folded uniqueness key and never replaces it.
	/// </summary>
	public static string ComputeConfusableSkeleton(string canonical)
	{
		if (string.IsNullOrEmpty(canonical)) return string.Empty;

		var builder = new StringBuilder(canonical.Length);
		foreach (Rune rune in Fold(canonical).EnumerateRunes())
		{
			UnicodeCategory category = Rune.GetUnicodeCategory(rune);
			bool isCombiningMark = category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
			if (!isCombiningMark && !Rune.IsLetterOrDigit(rune)) continue;

			if (ConfusableLatinSkeletonMap.TryGetValue(rune.Value, out char latinLookalike))
			{
				builder.Append(char.ToLowerInvariant(latinLookalike));
				continue;
			}

			builder.Append(Rune.ToLowerInvariant(rune).ToString());
		}

		return builder.ToString();
	}

	public static bool AreVisuallyConfusable(string canonicalA, string canonicalB)
	{
		string skeletonA = ComputeConfusableSkeleton(canonicalA);
		string skeletonB = ComputeConfusableSkeleton(canonicalB);
		return skeletonA.Length > 0 && string.Equals(skeletonA, skeletonB, StringComparison.Ordinal);
	}

	/// <summary>
	///     The leaked name a lobby may carry. A legally registered DisplayName must survive untouched, so this validates it as a
	///     DisplayName first and only falls back to a grapheme-safe cut for the seat's own length ceiling.
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

	public static string DescribeError(DisplayNameError error)
	{
		return error switch
		{
			DisplayNameError.Empty => "A display name is required.",
			DisplayNameError.TooShort => $"A display name must be at least {MinimumGraphemeClusters} characters long.",
			DisplayNameError.TooLong => $"A display name must be at most {MaximumGraphemeClusters} characters long.",
			DisplayNameError.TooManyBytes => $"A display name must be at most {MaximumUtf8Bytes} bytes when encoded as UTF-8.",
			DisplayNameError.DisallowedCharacter => "A display name may only contain letters, digits, single spaces, hyphens, underscores, periods and apostrophes.",
			DisplayNameError.CombiningMarkWithoutBase => "A combining mark must follow the character it belongs to.",
			DisplayNameError.NoLettersOrDigits => "A display name must contain at least one letter or digit.",
			DisplayNameError.AlreadyTaken => "That display name is already taken.",
			DisplayNameError.VisuallySimilar => "That display name looks too similar to a name another account already uses.",
			_ => string.Empty
		};
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

	private static Dictionary<int, char> BuildConfusableLatinSkeletonMap()
	{
		var map = new Dictionary<int, char>();

		AddRange(map, 0x0430, "a");
		AddRange(map, 0x0410, "A");
		AddRange(map, 0x0435, "e");
		AddRange(map, 0x0415, "E");
		AddRange(map, 0x043E, "o");
		AddRange(map, 0x041E, "O");
		AddRange(map, 0x0440, "p");
		AddRange(map, 0x0420, "P");
		AddRange(map, 0x0441, "c");
		AddRange(map, 0x0421, "C");
		AddRange(map, 0x0443, "y");
		AddRange(map, 0x0423, "Y");
		AddRange(map, 0x0445, "x");
		AddRange(map, 0x0425, "X");
		AddRange(map, 0x0456, "i");
		AddRange(map, 0x0406, "I");
		AddRange(map, 0x0458, "j");
		AddRange(map, 0x0408, "J");
		AddRange(map, 0x0455, "s");
		AddRange(map, 0x0405, "S");
		AddRange(map, 0x04CF, "l");
		AddRange(map, 0x0501, "d");
		AddRange(map, 0x04BB, "h");
		AddRange(map, 0x041D, "H");
		AddRange(map, 0x043A, "k");
		AddRange(map, 0x041A, "K");
		AddRange(map, 0x043C, "m");
		AddRange(map, 0x041C, "M");
		AddRange(map, 0x043D, "n");
		AddRange(map, 0x041F, "N");
		AddRange(map, 0x0442, "t");
		AddRange(map, 0x0422, "T");
		AddRange(map, 0x0432, "b");
		AddRange(map, 0x0412, "B");
		AddRange(map, 0x0433, "r");
		AddRange(map, 0x0413, "R");

		AddRange(map, 0x03B1, "a");
		AddRange(map, 0x0391, "A");
		AddRange(map, 0x03BF, "o");
		AddRange(map, 0x039F, "O");
		AddRange(map, 0x03C1, "p");
		AddRange(map, 0x03A1, "P");
		AddRange(map, 0x03B5, "e");
		AddRange(map, 0x0395, "E");
		AddRange(map, 0x03B9, "i");
		AddRange(map, 0x0399, "I");
		AddRange(map, 0x03BA, "k");
		AddRange(map, 0x039A, "K");
		AddRange(map, 0x03BD, "v");
		AddRange(map, 0x039D, "N");
		AddRange(map, 0x03C4, "t");
		AddRange(map, 0x03A4, "T");
		AddRange(map, 0x03C5, "u");
		AddRange(map, 0x03A5, "Y");
		AddRange(map, 0x03C7, "x");
		AddRange(map, 0x03A7, "X");
		AddRange(map, 0x03B6, "z");
		AddRange(map, 0x0396, "Z");

		return map;
	}

	private static void AddRange(Dictionary<int, char> map, int codePoint, string latinLookalike)
	{
		map[codePoint] = latinLookalike[0];
	}
}
