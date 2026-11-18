using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using Realm.Shared;

namespace Realm.Client.Services;

/// <summary>
///     Makes the copy of a map a lobby actually plays. Steam owns the installed Workshop folder and replaces it on the next
///     update, and a local map folder belongs to its author, so neither is written to: the package is copied into a session
///     folder under the user data directory, the map's data is migrated forward inside that copy, and the lobby, the
///     authority and the client all read the copy. Exactly one copy is kept - the most recently opened - and it is reused
///     only while the key recomputed from the installed map still matches it, so a retained copy can never make a stale
///     version of a map playable.
/// </summary>
public class MapSessionCopyService
{
	/// <summary>The folder under the user data directory that holds the retained copy.</summary>
	public const string SessionRootFolderName = "map_sessions";

	private const string CopyFolderName = "map";
	private const string SessionFileName = "session.json";
	private const string BuildingFolderInfix = ".building";

	private static readonly string[] IgnoredFolderNames = { ".git", ".godot", "obj", ".vscode" };

	/// <summary>Why the last <see cref="TryPrepare" /> call refused a map, written for the player, or empty.</summary>
	public string LastRefusal { get; private set; } = string.Empty;

	/// <summary>
	///     The map folder this service last handed out, which is the copy a running match reads. Empty before the first
	///     successful call.
	/// </summary>
	public string LastSessionDirectory { get; private set; } = string.Empty;

	/// <summary>The hash of the package the retained copy was made from, as the map itself stated it.</summary>
	public string LastOriginalHash { get; private set; } = string.Empty;

	/// <summary>The map's engine generation as its own metadata states it, or the current generation when it states none.</summary>
	public int LastEngineGeneration { get; private set; } = RealmVersion.EngineGeneration;

	/// <summary>
	///     Answers the folder a lobby should hand to the authority for a map, which is its own private copy of the
	///     installed package with the map's data migrated to this build.
	/// </summary>
	/// <param name="installedMapDirectory">The absolute folder of the installed or authored map.</param>
	/// <returns>The session copy to play from, or null when the map cannot be played, which <see cref="LastRefusal" /> explains.</returns>
	public string? TryPrepare(string installedMapDirectory)
	{
		LastRefusal = string.Empty;

		if (string.IsNullOrWhiteSpace(installedMapDirectory) || !Directory.Exists(installedMapDirectory))
		{
			LastRefusal = TranslationServer.Translate("This map is not on this machine.");
			return null;
		}

		string fullSource = Path.GetFullPath(installedMapDirectory);
		string sessionRoot = ProjectSettings.GlobalizePath($"user://{SessionRootFolderName}");

		if (IsInside(sessionRoot, fullSource))
		{
			LastSessionDirectory = fullSource;
			return fullSource;
		}

		MapSessionIdentity identity = ReadIdentity(fullSource);
		if (identity.EngineGeneration > RealmVersion.EngineGeneration)
		{
			LastRefusal = TranslationServer.Translate("This map needs a newer version of Realm. Update the game and try again.");
			return null;
		}

		string packageSignature = ComputePackageSignature(fullSource);
		if (packageSignature.Length == 0)
		{
			LastRefusal = TranslationServer.Translate("This map's files could not be read.");
			return null;
		}

		LastEngineGeneration = identity.EngineGeneration;
		LastOriginalHash = ReadStatedPackageHash(fullSource) is { Length: > 0 } statedHash ? statedHash : packageSignature;

		string retainedDirectory = Path.Combine(sessionRoot, packageSignature);
		string retainedCopy = Path.Combine(retainedDirectory, CopyFolderName);
		if (IsComplete(retainedDirectory, packageSignature) && Directory.Exists(retainedCopy))
		{
			LastSessionDirectory = retainedCopy;
			return retainedCopy;
		}

		string buildingDirectory = retainedDirectory + BuildingFolderInfix;
		string buildingCopy = Path.Combine(buildingDirectory, CopyFolderName);
		TryDeleteDirectory(buildingDirectory);

		try
		{
			Directory.CreateDirectory(buildingDirectory);
			CopyDirectory(fullSource, buildingCopy);
			WriteSessionRecord(buildingDirectory, packageSignature, identity, LastOriginalHash, complete: false);

			UpgradeResult upgrade = GameContext.MapUpgradeService.UpgradeMap(buildingCopy);
			if (!upgrade.Success)
			{
				LastRefusal = TranslationServer.Translate("This map was built for an older version of Realm and needs to be upgraded by its author.");
				GameLog.LogError($"[MapSessionCopy] '{fullSource}' could not be upgraded: {upgrade.ErrorMessage}");
				TryDeleteDirectory(buildingDirectory);
				return null;
			}

			WriteSessionRecord(buildingDirectory, packageSignature, identity, LastOriginalHash, complete: true);

			// The new copy is whole before the old one goes, so a failed copy never leaves the player with nothing.
			SweepSessionFolders(sessionRoot, retainedDirectory);
			Directory.Move(buildingDirectory, retainedDirectory);
		}
		catch (Exception exception)
		{
			LastRefusal = TranslationServer.Translate("This map could not be prepared for play.");
			GameLog.LogError($"[MapSessionCopy] '{fullSource}' could not be prepared: {exception}");
			TryDeleteDirectory(buildingDirectory);
			return null;
		}

		LastSessionDirectory = retainedCopy;
		return retainedCopy;
	}

	/// <summary>
	///     Deletes the copies a session cannot have made, which is what a run interrupted while building one would leave
	///     behind. Called once at startup, before any map is opened.
	/// </summary>
	public void SweepOrphans()
	{
		string sessionRoot = ProjectSettings.GlobalizePath($"user://{SessionRootFolderName}");
		if (!Directory.Exists(sessionRoot)) return;

		SweepSessionFolders(sessionRoot, keepDirectory: string.Empty, keepMostRecentCompleteFolder: true);
	}

	/// <summary>Reads the identity a map's own metadata states, which is what decides whether this build may play it.</summary>
	/// <param name="mapDirectory">The map's folder.</param>
	/// <returns>The stated generation, package hash and build number.</returns>
	public static MapSessionIdentity ReadIdentity(string mapDirectory)
	{
		string metadataPath = Path.Combine(mapDirectory, "metadata.json");
		if (!File.Exists(metadataPath)) return new MapSessionIdentity();

		try
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
			JsonElement root = document.RootElement;

			int generation = root.TryGetProperty("EngineGeneration", out JsonElement generationElement) &&
			                 generationElement.ValueKind == JsonValueKind.Number &&
			                 generationElement.TryGetInt32(out int statedGeneration)
				? statedGeneration
				: 1;

			string buildNumber = root.TryGetProperty("GameBuildNumber", out JsonElement buildElement) && buildElement.ValueKind == JsonValueKind.String
				? buildElement.GetString() ?? string.Empty
				: string.Empty;

			return new MapSessionIdentity
			{
				EngineGeneration = generation,
				GameBuildNumber = buildNumber
			};
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] the metadata at '{metadataPath}' could not be read: {exception.Message}");
			return new MapSessionIdentity();
		}
	}

	/// <summary>
	///     Reads the package hash a map folder states in its own manifest, which is the hash every machine holding the
	///     same package computes and therefore the one a lobby may advertise. A folder with no manifest - a map being
	///     authored, or one copied by hand - states nothing and answers empty.
	/// </summary>
	/// <param name="mapDirectory">The map's folder.</param>
	/// <returns>The package hash as lowercase hex, or an empty string.</returns>
	public static string ReadStatedPackageHash(string mapDirectory)
	{
		string manifestPath = Path.Combine(mapDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return string.Empty;

		try
		{
			byte[] manifest = File.ReadAllBytes(manifestPath);
			if (manifest.Length == 0) return string.Empty;

			return Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] '{manifestPath}' could not be read: {exception.Message}");
			return string.Empty;
		}
	}

	/// <summary>
	///     Hashes the files a map package carries, which is the key a retained copy is reused on. It is recomputed from the
	///     installed map every time, so a copy left from an older package can only be matched by the package it was made
	///     from. Build scaffolding that is not part of the package is skipped, because a map's own compile rewrites it.
	/// </summary>
	/// <param name="mapDirectory">The map's folder.</param>
	/// <returns>The signature as lowercase hex, or an empty string when the folder holds nothing readable.</returns>
	public static string ComputePackageSignature(string mapDirectory)
	{
		string[] files;
		try
		{
			var collected = new List<string>(256);
			CollectPackageFiles(mapDirectory, mapDirectory, collected);
			if (collected.Count == 0) return string.Empty;

			collected.Sort(StringComparer.Ordinal);
			files = collected.ToArray();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] '{mapDirectory}' could not be walked: {exception.Message}");
			return string.Empty;
		}

		using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var header = new StringBuilder(256);
		foreach (string file in files)
		{
			string relative = Path.GetRelativePath(mapDirectory, file).Replace('\\', '/');
			byte[] content;
			try
			{
				content = File.ReadAllBytes(file);
			}
			catch (IOException exception)
			{
				GameLog.LogError($"[MapSessionCopy] '{file}' could not be read while keying the map: {exception.Message}");
				return string.Empty;
			}

			header.Clear();
			header.Append(relative).Append('\n').Append(content.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
			digest.AppendData(Encoding.UTF8.GetBytes(header.ToString()));
			digest.AppendData(content);
		}

		return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
	}

	private static void CollectPackageFiles(string root, string directory, List<string> collected)
	{
		foreach (string file in Directory.EnumerateFiles(directory))
		{
			string name = Path.GetFileName(file);
			if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
			collected.Add(file);
		}

		foreach (string child in Directory.EnumerateDirectories(directory))
		{
			string name = Path.GetFileName(child);
			if (IsIgnoredFolder(name)) continue;
			if (name.StartsWith(".build-", StringComparison.Ordinal)) continue;

			CollectPackageFiles(root, child, collected);
		}
	}

	private static bool IsIgnoredFolder(string name)
	{
		foreach (string ignored in IgnoredFolderNames)
		{
			if (string.Equals(name, ignored, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static void CopyDirectory(string source, string destination)
	{
		Directory.CreateDirectory(destination);

		foreach (string file in Directory.EnumerateFiles(source))
		{
			string target = Path.Combine(destination, Path.GetFileName(file));
			File.Copy(file, target, overwrite: true);
			MakeWritable(target);
		}

		foreach (string child in Directory.EnumerateDirectories(source))
		{
			string name = Path.GetFileName(child);
			if (IsIgnoredFolder(name)) continue;
			if (name.StartsWith(".build-", StringComparison.Ordinal)) continue;

			CopyDirectory(child, Path.Combine(destination, name));
		}
	}

	private static void MakeWritable(string path)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(path);
			if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] '{path}' could not be made writable: {exception.Message}");
		}
	}

	private static void WriteSessionRecord(string directory, string packageSignature, MapSessionIdentity identity, string originalHash, bool complete)
	{
		var record = new SessionCopyRecord
		{
			PackageSignature = packageSignature,
			EngineGeneration = identity.EngineGeneration,
			StatedBuildNumber = identity.GameBuildNumber,
			OriginalHash = originalHash,
			PreparedEngineGeneration = RealmVersion.EngineGeneration,
			Complete = complete,
			PreparedAtUtc = DateTimeOffset.UtcNow
		};

		string target = Path.Combine(directory, SessionFileName);
		File.WriteAllText(target, JsonSerializer.Serialize(record, SessionCopyRecord.SerializerOptions));
	}

	private static bool IsComplete(string directory, string packageSignature)
	{
		string recordPath = Path.Combine(directory, SessionFileName);
		if (!File.Exists(recordPath)) return false;

		try
		{
			SessionCopyRecord? record = JsonSerializer.Deserialize<SessionCopyRecord>(File.ReadAllText(recordPath), SessionCopyRecord.SerializerOptions);
			return record is { Complete: true } && string.Equals(record.PackageSignature, packageSignature, StringComparison.Ordinal);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] '{recordPath}' could not be read: {exception.Message}");
			return false;
		}
	}

	private static void SweepSessionFolders(string sessionRoot, string keepDirectory, bool keepMostRecentCompleteFolder = false)
	{
		if (!Directory.Exists(sessionRoot)) return;

		string retained = keepDirectory;
		if (keepMostRecentCompleteFolder && retained.Length == 0)
		{
			retained = FindMostRecentCompleteFolder(sessionRoot);
		}

		foreach (string directory in Directory.EnumerateDirectories(sessionRoot))
		{
			string full = Path.GetFullPath(directory);
			if (retained.Length > 0 && string.Equals(full, Path.GetFullPath(retained), StringComparison.OrdinalIgnoreCase)) continue;

			TryDeleteDirectory(full);
		}
	}

	private static string FindMostRecentCompleteFolder(string sessionRoot)
	{
		string newest = string.Empty;
		DateTime newestWrite = DateTime.MinValue;

		foreach (string directory in Directory.EnumerateDirectories(sessionRoot))
		{
			if (Path.GetFileName(directory).EndsWith(BuildingFolderInfix, StringComparison.Ordinal)) continue;

			string recordPath = Path.Combine(directory, SessionFileName);
			if (!File.Exists(recordPath)) continue;

			DateTime written = File.GetLastWriteTimeUtc(recordPath);
			if (written <= newestWrite) continue;

			newestWrite = written;
			newest = directory;
		}

		return newest;
	}

	private static void TryDeleteDirectory(string directory)
	{
		if (!Directory.Exists(directory)) return;

		try
		{
			foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
			{
				MakeWritable(file);
			}

			Directory.Delete(directory, recursive: true);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapSessionCopy] '{directory}' could not be removed: {exception.Message}");
		}
	}

	private static bool IsInside(string root, string candidate)
	{
		string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return candidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>What a map's own metadata states about the package and the engine it was authored for.</summary>
	public readonly struct MapSessionIdentity
	{
		/// <summary>The engine generation the map was authored against.</summary>
		public int EngineGeneration { get; init; }

		/// <summary>The build number the map states, which only chooses its migrations.</summary>
		public string GameBuildNumber { get; init; }
	}

	/// <summary>What a retained session folder records about the package it was made from.</summary>
	private class SessionCopyRecord
	{
		public static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

		public string PackageSignature { get; set; } = string.Empty;
		public int EngineGeneration { get; set; }
		public string StatedBuildNumber { get; set; } = string.Empty;
		public string OriginalHash { get; set; } = string.Empty;
		public int PreparedEngineGeneration { get; set; } = RealmVersion.EngineGeneration;
		public bool Complete { get; set; }
		public DateTimeOffset PreparedAtUtc { get; set; }
	}
}
