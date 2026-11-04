using System;

namespace Realm.Shared.Accounts;

/// <summary>
///     The one character that marks a name no signed-in account owns. Only a screen draws it, never a player: the mark is a
///     statement the interface makes about a name, so it is removed from every name a lobby carries and a registered
///     DisplayName can never contain it either.
/// </summary>
public static class UnverifiedNameMark
{
	public const string Glyph = "\u29B8";

	public static string Apply(string? name)
	{
		string trimmed = name?.Trim() ?? string.Empty;
		return trimmed.Length == 0 ? Glyph : $"{Glyph}{trimmed}";
	}

	public static string Remove(string? name)
	{
		if (string.IsNullOrEmpty(name)) return string.Empty;
		if (!Carries(name)) return name;

		return name.Replace(Glyph, string.Empty, StringComparison.Ordinal);
	}

	public static bool Carries(string? name)
	{
		return !string.IsNullOrEmpty(name) && name.Contains(Glyph, StringComparison.Ordinal);
	}
}
