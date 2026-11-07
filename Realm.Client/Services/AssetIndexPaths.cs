using System.IO;

namespace Realm.Client.Services;

/// <summary>
///     The single spelling of an index path: absolute, forward-slashed and without a trailing separator, so a directory
///     recorded in the index and a path read from disk compare equal.
/// </summary>
internal static class AssetIndexPaths
{
	internal static string Normalize(string path)
	{
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}
}
