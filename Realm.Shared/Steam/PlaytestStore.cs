using System;
using System.Security.Cryptography;
using System.Text;

namespace Realm.Shared.Steam;

/// <summary>
///     Where a map's playtest ledger lives on the author's machine and how a map is keyed in it. The key is the map's own
///     name, exactly the way <c>MapHistoryService</c> keys its store, so two folders of the same map share one ledger and
///     moving, renaming or re-cloning a workspace does not reset a count that takes 25 testers to reach.
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
	///     A stable folder-safe key for one map, readable enough to recognise and hashed so two maps whose names differ only
	///     in characters a filesystem refuses do not share a ledger.
	/// </summary>
	/// <param name="mapName">The map's name as its metadata states it.</param>
	/// <returns>The key the map's records are stored under.</returns>
	public static string BuildMapKey(string? mapName)
	{
		string exactName = string.IsNullOrWhiteSpace(mapName) ? "Untitled Map" : mapName.Trim();
		string readable = Sanitize(exactName);
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(exactName)))[..KeyHashLength];
		return readable + "-" + hash;
	}

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
///     How many different people have to have played a map before its author may publish it publicly. The default is stated
///     here rather than in the editor, so a map that says nothing about it gets the same bar wherever it is opened, and a
///     map that cannot seat a crowd - a single-player campaign, a duel board - states its own lower number or none.
/// </summary>
public static class PlaytestGreenlightRules
{
	/// <summary>The number of distinct testers a map needs when it states no threshold of its own.</summary>
	public const int DefaultTesterThreshold = 25;

	/// <summary>
	///     Reads the threshold a map stated for itself.
	/// </summary>
	/// <param name="statedThreshold">The number the map's metadata carries, or null when it carries none.</param>
	/// <returns>How many distinct testers this map needs; zero means its author may publish it publicly at once.</returns>
	public static int ResolveTesterThreshold(int? statedThreshold)
	{
		if (statedThreshold == null) return DefaultTesterThreshold;
		return Math.Clamp(statedThreshold.Value, 0, 1000);
	}
}
