using System;
using System.Collections.Generic;

namespace Realm.Shared.Network;

/// <summary>
///     Works out which of a map's files a screen already holds, from the manifest the lobby served and the archive lookups
///     the screen made. It states the answer for the whole map - including the map it holds entirely, which is the case a
///     lobby has nothing at all to send for - so that nothing decides by omission that a file has to be fetched again.
/// </summary>
public static class MapTransferSelection
{
	/// <summary>
	///     The asset keys of a map's files that are not among the missing ones.
	/// </summary>
	/// <param name="assetKeys">Every asset key the map's manifest states.</param>
	/// <param name="missingAssetKeys">The keys the archive lookup answered as absent.</param>
	/// <returns>The keys the screen holds, each once.</returns>
	public static List<string> Held(IEnumerable<string> assetKeys, IEnumerable<string> missingAssetKeys)
	{
		var missing = new HashSet<string>(missingAssetKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		var held = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (assetKeys == null) return held;

		foreach (string assetKey in assetKeys)
		{
			if (string.IsNullOrEmpty(assetKey)) continue;
			if (missing.Contains(assetKey)) continue;
			if (!seen.Add(assetKey)) continue;

			held.Add(assetKey);
		}

		return held;
	}
}
