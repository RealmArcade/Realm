using System.IO;

namespace Realm.Client.Services;

/// <summary>
///     The single spelling of an index path: absolute, forward-slashed and without a trailing separator, so a directory
///     recorded in the index and a path read from disk compare equal.
/// </summary>
public static class AssetIndexPaths
{
	public static string Normalize(string path)
	{
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	/// <summary>Template folders are never part of the index, however they are spelled.</summary>
	public static bool IsForbidden(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;
		string norm = path.Replace('\\', '/').ToLowerInvariant();
		return norm.Contains("maptemplate");
	}
}
