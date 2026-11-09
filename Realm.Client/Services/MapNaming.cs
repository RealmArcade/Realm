using System;

namespace Realm.Client.Services;

/// <summary>
///     The names a map carries before the author has given it one. Kept in one place because the editor, the export path
///     and the workspace bootstrap all have to agree on the same prefix.
/// </summary>
public static class MapNaming
{
	public const string UntitledMapNamePrefix = "Untitled Map";

	public static string CreateUntitledMapName()
	{
		return UntitledMapNamePrefix;
	}

	public static bool IsUntitledMapName(string? mapName)
	{
		return !string.IsNullOrWhiteSpace(mapName) && mapName.StartsWith(UntitledMapNamePrefix, StringComparison.OrdinalIgnoreCase);
	}
}
