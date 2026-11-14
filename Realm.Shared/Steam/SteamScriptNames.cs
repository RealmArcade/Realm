using System;
using System.Text;

namespace Realm.Shared.Steam;

/// <summary>
///     Turns an identifier a map script states into the name Steam is given for it. Every map shares one Steam
///     application's achievement and stat tables, so the map the identifier came from is part of the name: two maps may both
///     call an achievement <c>chapter_one</c> without meaning the same achievement, and a name a map states can never reach
///     an achievement that belongs to the game itself.
/// </summary>
public static class SteamScriptNames
{
	private const string Prefix = "realm_";

	private const int MaximumIdentifierCharacters = 96;

	/// <summary>
	///     The name Steam is given for one identifier of one map, which is <c>realm_&lt;map&gt;_&lt;identifier&gt;</c> with
	///     everything Steam does not accept - anything but a lower case letter, a digit and an underscore - dropped.
	/// </summary>
	/// <param name="mapSlug">The map the identifier belongs to, as its folder name states it.</param>
	/// <param name="identifier">The identifier the map script stated.</param>
	/// <returns>The name Steam is asked about, or an empty string when the identifier states nothing usable.</returns>
	public static string ForMap(string? mapSlug, string? identifier)
	{
		string mapPart = Sanitize(mapSlug, MaximumIdentifierCharacters);
		string identifierPart = Sanitize(identifier, MaximumIdentifierCharacters);
		if (identifierPart.Length == 0) return string.Empty;

		return mapPart.Length == 0 ? Prefix + identifierPart : Prefix + mapPart + "_" + identifierPart;
	}

	/// <summary>
	///     The part of Steam's cloud a map's files live in. Each player's Steam account keeps one cloud per application, so the
	///     folder is what separates one map's saves from another's and from the game's own.
	/// </summary>
	/// <param name="mapSlug">The map the files belong to, as its folder name states it.</param>
	/// <returns>The folder prefix a map's cloud files are stored under, ending in a slash.</returns>
	public static string CloudFolder(string? mapSlug)
	{
		string mapPart = Sanitize(mapSlug, MaximumIdentifierCharacters);
		return mapPart.Length == 0 ? "maps/unstated/" : "maps/" + mapPart + "/";
	}

	private static string Sanitize(string? value, int maximumCharacters)
	{
		if (string.IsNullOrWhiteSpace(value)) return string.Empty;

		var builder = new StringBuilder(Math.Min(value.Length, maximumCharacters));
		for (int index = 0; index < value.Length && builder.Length < maximumCharacters; index++)
		{
			char character = char.ToLowerInvariant(value[index]);
			bool kept = character is >= 'a' and <= 'z' or >= '0' and <= '9';
			if (kept)
			{
				builder.Append(character);
				continue;
			}

			// Anything else becomes the separator the names read with, which keeps two different identifiers from
			// collapsing into one name: "chapter one" and "chapter-one" are the same name on purpose.
			if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
		}

		return builder.ToString().Trim('_');
	}
}
