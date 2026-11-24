using System;
using System.Security.Cryptography;
using System.Text;

namespace Realm.Shared.Steam;

/// <summary>
///     Where a map's playtest ledger lives on the author's machine and how a map is keyed in it. There are two keys, and
///     they answer two different questions. The ledger key is the map's name plus the author's SteamID64, so a count that
///     takes 25 testers to reach follows the map across renames of the folder, new versions, a move from one AppID to
///     another and a re-published item, while two different authors who each wrote a map called the same thing keep their
///     own counts. The code key is the name alone, because the authority that mints a vote code knows the map it is running
///     but not which of its players is the author, and a playtest hosted by somebody else still has to produce a code the
///     author can paste into their ledger.
/// </summary>
public static class PlaytestStore
{
	/// <summary>The directory under the user's local application data that every playtest record is kept in.</summary>
	public const string RootDirectoryName = "playtest";

	private const int KeyHashLength = 8;

	private const int MaximumReadableKeyLength = 48;

	/// <summary>
	///     The directory the ledger and the greenlight record live in. It is never inside a map folder, so nothing written
	///     here can be uploaded, exported or shipped with the map.
	/// </summary>
	/// <returns>The store's root directory.</returns>
	public static string GetRootDirectory()
	{
		string localDataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrEmpty(localDataDirectory)) localDataDirectory = AppContext.BaseDirectory;

		return System.IO.Path.Combine(localDataDirectory, "Realm", RootDirectoryName);
	}

	/// <summary>
	///     The stable folder-safe key one author's ledger and greenlight record are filed under: the map's name and the
	///     author's SteamID64, readable enough to recognise and hashed so two maps whose names differ only in characters a
	///     filesystem refuses do not share a ledger.
	/// </summary>
	/// <param name="mapName">The map's name as its metadata states it.</param>
	/// <param name="authorSteamId">The SteamID64 of the account that authored the map; zero when it is not known.</param>
	/// <returns>The key the author's records for this map are stored under.</returns>
	public static string BuildMapKey(string? mapName, ulong authorSteamId)
	{
		string exactName = ExactName(mapName);
		string readable = Sanitize(exactName);
		string identity = exactName + "\n" + authorSteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..KeyHashLength];
		return readable + "-" + authorSteamId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + hash;
	}

	/// <summary>
	///     The key a playtest vote code is minted and read with: the map's name alone, because the authority running the
	///     match knows which map it is running and not who wrote it.
	/// </summary>
	/// <param name="mapName">The map's name as its metadata states it.</param>
	/// <returns>The key a vote code names.</returns>
	public static string BuildNameKey(string? mapName)
	{
		string exactName = ExactName(mapName);
		string readable = Sanitize(exactName);
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(exactName)))[..KeyHashLength];
		return readable + "-" + hash;
	}

	private static string ExactName(string? mapName) => string.IsNullOrWhiteSpace(mapName) ? "Untitled Map" : mapName.Trim();

	private static string Sanitize(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (char character in name)
		{
			builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_');
		}

		string cleaned = builder.ToString().Trim('_');
		if (cleaned.Length == 0) cleaned = "map";
		return cleaned.Length <= MaximumReadableKeyLength ? cleaned : cleaned[..MaximumReadableKeyLength];
	}
}

/// <summary>
///     How many different people have to have played a map before its author may publish it. It is one number for every
///     map, with no per-map override: the bar exists to keep an unfinished map out of the catalogue, and a map that seats
///     two people or a single-player campaign is exactly the kind of map whose author most needs somebody else to have
///     played it before it goes out. A creator whose map cannot gather that many testers can still share it as an
///     `.rmap` or through a host's lobby, which needs no publication.
/// </summary>
public static class PlaytestGreenlightRules
{
	/// <summary>The number of distinct testers every map needs before it may be published.</summary>
	public const int TesterThreshold = 5;
}
