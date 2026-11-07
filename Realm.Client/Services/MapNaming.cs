using System;

namespace Realm.Client.Services;

/// <summary>
///     The names a map carries before the author has given it one. Kept in one place because the editor, the export path
///     and the workspace bootstrap all have to agree on the same prefix.
/// </summary>
internal static class MapNaming
{
	public const string UntitledMapNamePrefix = "Untitled Map";

	internal static string CreateUntitledMapName()
	{
		return UntitledMapNamePrefix;
	}

	internal static bool IsUntitledMapName(string? mapName)
	{
		return !string.IsNullOrWhiteSpace(mapName) && mapName.StartsWith(UntitledMapNamePrefix, StringComparison.OrdinalIgnoreCase);
	}
}
