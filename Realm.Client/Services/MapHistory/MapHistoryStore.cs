using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using LibGit2Sharp;

namespace Realm.Client.Services.MapHistory;

public class MapHistoryStoreLayout
{
	public MapHistoryStoreLayout(string rootDirectory, string storeKey)
	{
		RootDirectory = rootDirectory;
		StoreKey = storeKey;
		StoreDirectory = Path.Combine(rootDirectory, storeKey);
		RepositoryDirectory = Path.Combine(StoreDirectory, "repository");
		StateFilePath = Path.Combine(StoreDirectory, "state.json");
		LockFilePath = Path.Combine(StoreDirectory, "lock");
	}

	public string RootDirectory { get; }
	public string StoreKey { get; }
	public string StoreDirectory { get; }
	public string RepositoryDirectory { get; }
	public string StateFilePath { get; }
	public string LockFilePath { get; }
}

public class MapHistoryStoreSession : IDisposable
{
	private readonly FileStream? _lockStream;

	public MapHistoryStoreSession(MapHistoryStoreLayout layout, FileStream? lockStream, bool wasReset)
	{
		Layout = layout;
		_lockStream = lockStream;
		WasReset = wasReset;
	}

	public MapHistoryStoreLayout Layout { get; }
	public bool WasReset { get; }

	public void Dispose()
	{
		_lockStream?.Dispose();
	}
}

public static class MapHistoryStore
{
	private const string ApplicationDirectoryName = "Realm";
	private const string StoreRootDirectoryName = "map_history";
	private const string DamagedDirectoryName = "_damaged";
	private const int MaximumReadableKeyLength = 48;
	private const int KeyHashLength = 8;

	private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(10);
	private static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(25);

	public static string GetRootDirectory()
	{
		string localDataDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
		if (string.IsNullOrEmpty(localDataDirectory))
		{
			localDataDirectory = ProjectSettings.GlobalizePath("user://");
		}

		return Path.Combine(localDataDirectory, ApplicationDirectoryName, StoreRootDirectoryName);
	}

	public static MapHistoryStoreLayout Resolve(string mapDirectory)
	{
		return Resolve(mapDirectory, ResolveMapName(mapDirectory));
	}

	public static MapHistoryStoreLayout Resolve(string mapDirectory, string mapName)
	{
		return new MapHistoryStoreLayout(GetRootDirectory(), BuildStoreKey(mapName));
	}

	public static string BuildStoreKey(string mapName)
	{
		string exactName = mapName ?? string.Empty;
		string readable = SanitizeForDirectoryName(exactName);
		string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(exactName)))[..KeyHashLength];
		return readable + "-" + hash;
	}

	public static string ResolveMapName(string mapDirectory)
	{
		string? declaredName = TryReadDeclaredMapName(mapDirectory);
		if (!string.IsNullOrWhiteSpace(declaredName))
		{
			return declaredName.Trim();
		}

		string folderName = Path.GetFileName(mapDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return string.IsNullOrWhiteSpace(folderName) ? "Untitled Map" : folderName;
	}

	public static MapHistoryStoreSession Acquire(string mapDirectory)
	{
		MapHistoryStoreLayout layout = Resolve(mapDirectory);
		Directory.CreateDirectory(layout.StoreDirectory);
		FileStream lockStream = AcquireLock(layout.LockFilePath);
		try
		{
			bool wasReset = EnsureRepository(layout);
			return new MapHistoryStoreSession(layout, lockStream, wasReset);
		}
		catch (Exception exception) when (exception is LibGit2SharpException or UnauthorizedAccessException or IOException)
		{
			lockStream.Dispose();
			throw new InvalidOperationException($"The version history of this map could not be opened: {exception.Message}", exception);
		}
		catch
		{
			lockStream.Dispose();
			throw;
		}
	}

	public static MapHistoryStoreSession? TryAcquireExisting(MapHistoryStoreLayout layout, TimeSpan timeout)
	{
		if (!RepositoryExists(layout)) return null;

		DateTime deadline = DateTime.UtcNow + timeout;
		while (true)
		{
			try
			{
				var lockStream = new FileStream(layout.LockFilePath, FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
				return new MapHistoryStoreSession(layout, lockStream, false);
			}
			catch (IOException) when (DateTime.UtcNow < deadline)
			{
				System.Threading.Thread.Sleep(LockRetryInterval);
			}
			catch (IOException)
			{
				return null;
			}
		}
	}

	public static bool RepositoryExists(MapHistoryStoreLayout layout)
	{
		return Directory.Exists(layout.RepositoryDirectory) && TryIsValidRepository(layout.RepositoryDirectory);
	}

	private static bool EnsureRepository(MapHistoryStoreLayout layout)
	{
		bool wasReset = false;
		if (Directory.Exists(layout.RepositoryDirectory))
		{
			if (TryIsValidRepository(layout.RepositoryDirectory))
			{
				return false;
			}

			MoveDamagedStoreAside(layout);
			wasReset = true;
		}

		Directory.CreateDirectory(layout.RepositoryDirectory);
		Repository.Init(layout.RepositoryDirectory, true);
		using Repository repository = new(layout.RepositoryDirectory);
		repository.Refs.UpdateTarget("HEAD", "refs/heads/" + MapGitRepository.DefaultBranchName);
		return wasReset;
	}

	private static void MoveDamagedStoreAside(MapHistoryStoreLayout layout)
	{
		string damagedRoot = Path.Combine(layout.RootDirectory, DamagedDirectoryName);
		Directory.CreateDirectory(damagedRoot);

		string destination = Path.Combine(damagedRoot, $"{layout.StoreKey}-{DateTime.Now:yyyyMMdd-HHmmss}");
		for (int attempt = 1; Directory.Exists(destination) && attempt < 100; attempt++)
		{
			destination = Path.Combine(damagedRoot, $"{layout.StoreKey}-{DateTime.Now:yyyyMMdd-HHmmss}-{attempt}");
		}

		Directory.CreateDirectory(destination);
		Directory.Move(layout.RepositoryDirectory, Path.Combine(destination, "repository"));
		GD.PrintErr($"[MapHistoryStore] The version history of this map could not be read and was moved to {destination}. A new history starts now.");
	}

	private static bool TryIsValidRepository(string repositoryDirectory)
	{
		if (!Directory.Exists(Path.Combine(repositoryDirectory, "objects")) ||
		    !File.Exists(Path.Combine(repositoryDirectory, "HEAD")))
		{
			return false;
		}

		try
		{
			using Repository repository = new(repositoryDirectory);
			return true;
		}
		catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException)
		{
			return false;
		}
	}

	private static FileStream AcquireLock(string lockFilePath)
	{
		DateTime deadline = DateTime.UtcNow + LockTimeout;
		while (true)
		{
			try
			{
				return new FileStream(lockFilePath, FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
			}
			catch (IOException) when (DateTime.UtcNow < deadline)
			{
				System.Threading.Thread.Sleep(LockRetryInterval);
			}
			catch (IOException exception)
			{
				throw new InvalidOperationException("Another editor window is using this map's version history right now.", exception);
			}
		}
	}

	private static string SanitizeForDirectoryName(string mapName)
	{
		var builder = new StringBuilder(mapName.Length);
		char[] invalidCharacters = Path.GetInvalidFileNameChars();
		bool previousWasSeparator = false;

		foreach (char character in mapName)
		{
			bool isInvalid = char.IsControl(character) || Array.IndexOf(invalidCharacters, character) >= 0;
			if (isInvalid)
			{
				if (previousWasSeparator) continue;
				previousWasSeparator = true;
				builder.Append('-');
			}
			else
			{
				previousWasSeparator = false;
				builder.Append(character);
			}

			if (builder.Length >= MaximumReadableKeyLength) break;
		}

		string readable = builder.ToString().Trim('-', '.', ' ');
		return string.IsNullOrEmpty(readable) ? "map" : readable;
	}

	private static string? TryReadDeclaredMapName(string mapDirectory)
	{
		string? metadataName = TryReadMapNameFromFile(Path.Combine(mapDirectory, "metadata.json"), "MapProperties", "MapName");
		if (!string.IsNullOrWhiteSpace(metadataName))
		{
			return metadataName;
		}

		return TryReadMapNameFromFile(Path.Combine(mapDirectory, "manifest.json"), null, "MapName");
	}

	private static string? TryReadMapNameFromFile(string path, string? parentPropertyName, string propertyName)
	{
		try
		{
			if (!File.Exists(path)) return null;

			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement scope = document.RootElement;
			if (!string.IsNullOrEmpty(parentPropertyName))
			{
				if (!scope.TryGetProperty(parentPropertyName, out JsonElement parent) || parent.ValueKind != JsonValueKind.Object)
				{
					return null;
				}

				scope = parent;
			}

			if (!scope.TryGetProperty(propertyName, out JsonElement nameElement) || nameElement.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			return nameElement.GetString();
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
