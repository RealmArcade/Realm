using System;
using System.Collections.Generic;
using MemoryPack;

namespace Realm.Shared.Network;

/// <summary>
///     The cloud files one player's screen holds for the map being played, sent to the authority so the map script can
///     read them. The connection states the seat, so nothing here names a player; the handle is the request the map asked
///     with, which lets the authority drop an answer that arrived after a newer request.
/// </summary>
[MemoryPackable]
public partial struct SteamCloudUpload
{
	public int RequestId;

	/// <summary>Whether Steam's remote storage was readable for this map on that screen.</summary>
	public bool CloudAvailable;

	public SteamCloudUploadFile[] Files;
}

/// <summary>One cloud file a screen holds, with the name the map gave it and its content.</summary>
[MemoryPackable]
public partial struct SteamCloudUploadFile
{
	public string Name;
	public string Content;
}

/// <summary>
///     The limits one map's cloud files are held to. They are stated once and read by the screen that writes Steam and by
///     the authority that caches what it uploaded, so a map cannot grow a save the rest of the pipeline refuses.
/// </summary>
public static class SteamCloudLimits
{
	/// <summary>The longest file name a map may state, and the longest name that survives a Steam cloud path.</summary>
	public const int MaximumFileNameCharacters = 64;

	/// <summary>The largest content one file may carry, in characters.</summary>
	public const int MaximumFileContentCharacters = 32768;

	/// <summary>How many files one player may hold for one map.</summary>
	public const int MaximumFilesPerPlayer = 64;

	/// <summary>How much content one player's whole upload may carry, in characters.</summary>
	public const int MaximumUploadCharacters = 131072;

	private static readonly char[] InvalidNameCharacters = ['/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0'];

	/// <summary>
	///     Whether a file name is one this pipeline can carry: a plain name with no folder in it, short enough to stay
	///     inside the limits above, and made only of characters that mean the same thing on every machine.
	/// </summary>
	/// <param name="fileName">The name the map stated.</param>
	/// <returns>True when the name may be used for a cloud file.</returns>
	public static bool IsAcceptableFileName(string? fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName)) return false;
		if (fileName.Length > MaximumFileNameCharacters) return false;
		if (fileName.IndexOfAny(InvalidNameCharacters) >= 0) return false;
		if (fileName.Contains("..", StringComparison.Ordinal)) return false;

		return fileName.Trim().Length == fileName.Length;
	}

	/// <summary>
	///     Whether the content of one file fits the size this pipeline carries.
	/// </summary>
	/// <param name="content">The content the map stated.</param>
	/// <returns>True when the content may be written.</returns>
	public static bool IsAcceptableContent(string? content)
	{
		return content != null && content.Length <= MaximumFileContentCharacters;
	}

	/// <summary>
	///     Measures how much one upload carries, which is the sum of its file contents.
	/// </summary>
	/// <param name="files">The files an upload carries.</param>
	/// <returns>The number of characters the upload states.</returns>
	public static int MeasureUpload(IReadOnlyList<SteamCloudUploadFile>? files)
	{
		if (files == null) return 0;

		int total = 0;
		for (int index = 0; index < files.Count; index++)
		{
			string? content = files[index].Content;
			if (content != null) total += content.Length;
		}

		return total;
	}

	/// <summary>
	///     Whether one upload is small enough and holds few enough files to be carried.
	/// </summary>
	/// <param name="files">The files an upload carries.</param>
	/// <returns>True when the upload may be delivered.</returns>
	public static bool IsAcceptableUpload(IReadOnlyList<SteamCloudUploadFile>? files)
	{
		if (files == null) return true;
		if (files.Count > MaximumFilesPerPlayer) return false;

		return MeasureUpload(files) <= MaximumUploadCharacters;
	}
}
