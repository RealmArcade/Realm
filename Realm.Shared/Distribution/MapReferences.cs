using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Realm.Shared.Distribution;

/// <summary>
///     One map whose content a parent map takes into itself on every save.
/// </summary>
public class MapReference
{
	public string MapName { get; set; } = string.Empty;
	public string Version { get; set; } = "1.0.0";

	/// <summary>
	///     The blake3 of the referenced map's own <c>manifest.json</c>, which is what identifies the exact package the
	///     reference was written against inside the content-addressable store.
	/// </summary>
	public string ManifestBlake3 { get; set; } = string.Empty;

	/// <summary>
	///     A folder on this machine holding the referenced map while it is still being authored. It is searched before the
	///     store, so a reference follows the map an author is editing rather than the last copy published from it.
	/// </summary>
	public string FolderPath { get; set; } = string.Empty;
}

/// <summary>
///     The maps a map folder takes content from, kept beside that map as <c>references.json</c>.
/// </summary>
/// <remarks>
///     A reference is not a package format: the referencing map stays a normal, editable map that happens to gain the
///     referenced maps' files every time it is saved. Because the save writes those files into the map folder itself,
///     everything downstream - testing, exporting, publishing, downloading, playing - reads one ordinary map and never
///     has to resolve anything.
///     <para />
///     <c>metadata.json</c> is always merged deeply: objects merge key by key, a scalar takes the last contributor's value,
///     and arrays are combined. Every other file is taken whole from exactly one map, decided by
///     <see cref="ConflictPriorities" />, because merging two binaries or two <c>.cs</c> files has no defined meaning.
/// </remarks>
public class MapReferences
{
	public const string FileName = "references.json";

	public const int CurrentFormatVersion = 1;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public int FormatVersion { get; set; } = CurrentFormatVersion;

	public List<MapReference> References { get; set; } = new();

	/// <summary>
	///     States, for one file both the parent and a referenced map carry, which of them the saved map keeps. A path the
	///     file is missing from is decided by the default rule: the parent keeps it, and when the parent does not carry the
	///     file either, the single referenced map that does. A <c>null</c> value states the parent explicitly and is the same
	///     as removing the entry.
	/// </summary>
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	public Dictionary<string, string?> ConflictPriorities { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	[JsonIgnore]
	public bool IsEmpty => References == null || References.Count == 0;

	/// <summary>
	///     Reads the references a map folder keeps.
	/// </summary>
	/// <param name="mapFolder">The map folder, which may keep no references at all.</param>
	/// <returns>The references, or null when the folder keeps none or keeps one that cannot be read.</returns>
	public static MapReferences? Load(string mapFolder)
	{
		if (string.IsNullOrWhiteSpace(mapFolder))
		{
			return null;
		}

		string path = Path.Combine(mapFolder, FileName);
		if (!File.Exists(path))
		{
			return null;
		}

		return LoadFromJson(File.ReadAllText(path));
	}

	public static MapReferences? LoadFromJson(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}

		try
		{
			MapReferences? references = JsonSerializer.Deserialize<MapReferences>(json, JsonOptions);
			if (references == null)
			{
				return null;
			}

			references.References ??= new List<MapReference>();
			references.ConflictPriorities ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
			return references;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, JsonOptions);
	}

	/// <summary>
	///     Serializes the references the way a package ships them: every other field is kept, but the helper folder a
	///     reference names is cleared, because that folder only exists on the machine the map is being authored on. A map
	///     downloaded from a package resolves its references from the content-addressable store by the pinned manifest hash
	///     instead, and the author who imports it points a reference at a folder of their own.
	/// </summary>
	/// <returns>The references as they travel inside a package.</returns>
	public string ToDistributionJson()
	{
		var portable = new MapReferences
		{
			FormatVersion = FormatVersion,
			ConflictPriorities = new Dictionary<string, string?>(ConflictPriorities ?? new(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
		};

		foreach (MapReference reference in References ?? new List<MapReference>())
		{
			portable.References.Add(new MapReference
			{
				MapName = reference.MapName,
				Version = reference.Version,
				ManifestBlake3 = reference.ManifestBlake3,
				FolderPath = string.Empty
			});
		}

		return portable.ToJson();
	}

	public void Save(string mapFolder)
	{
		if (string.IsNullOrWhiteSpace(mapFolder))
		{
			return;
		}

		Directory.CreateDirectory(mapFolder);
		string path = Path.Combine(mapFolder, FileName);
		byte[] bytes = Encoding.UTF8.GetBytes(ToJson());
		if (File.Exists(path))
		{
			File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
			if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
			{
				return;
			}
		}

		File.WriteAllBytes(path, bytes);
	}

	public (bool IsValid, string ErrorMessage) Validate()
	{
		if (FormatVersion != CurrentFormatVersion)
		{
			return (false, $"The references file uses format version {FormatVersion}; this build reads version {CurrentFormatVersion}.");
		}

		if (References == null || References.Count == 0)
		{
			return (false, "The references file names no map.");
		}

		var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (MapReference reference in References)
		{
			if (reference == null)
			{
				return (false, "The references file holds an empty entry.");
			}

			if (string.IsNullOrWhiteSpace(reference.MapName))
			{
				return (false, "A reference has no map name.");
			}

			if (!IsSafePathSegment(reference.MapName))
			{
				return (false, $"The referenced map name '{reference.MapName}' cannot be used as a folder name.");
			}

			if (string.IsNullOrWhiteSpace(reference.Version) || !IsSafePathSegment(reference.Version))
			{
				return (false, $"The referenced map '{reference.MapName}' has no usable version.");
			}

			if (string.IsNullOrWhiteSpace(reference.ManifestBlake3))
			{
				return (false, $"The referenced map '{reference.MapName}' has no pinned manifest hash.");
			}

			if (!string.IsNullOrWhiteSpace(reference.FolderPath) && !IsSafeFolderPath(reference.FolderPath))
			{
				return (false, $"The referenced map '{reference.MapName}' declares an unusable helper folder '{reference.FolderPath}'.");
			}

			string identity = $"{reference.MapName}\n{reference.Version}\n{ContentAddressableStorage.NormalizeBlake3Hash(reference.ManifestBlake3)}";
			if (!identities.Add(identity))
			{
				return (false, $"The referenced map '{reference.MapName}' v{reference.Version} is listed more than once.");
			}
		}

		foreach (KeyValuePair<string, string?> priority in ConflictPriorities)
		{
			if (string.IsNullOrWhiteSpace(priority.Key))
			{
				return (false, "The references file states a conflict priority for an empty path.");
			}

			if (string.IsNullOrWhiteSpace(priority.Value))
			{
				continue;
			}

			bool namedMapExists = false;
			foreach (MapReference reference in References)
			{
				if (string.Equals(reference.MapName, priority.Value, StringComparison.OrdinalIgnoreCase))
				{
					namedMapExists = true;
					break;
				}
			}

			if (!namedMapExists)
			{
				return (false, $"The conflict priority for '{priority.Key}' names '{priority.Value}', which is not a referenced map.");
			}
		}

		return (true, string.Empty);
	}

	/// <summary>
	///     Answers which map keeps one file. <paramref name="mapName" /> is the referenced map the file is taken from, or
	///     null when the parent keeps it.
	/// </summary>
	/// <param name="relativePath">The file's path inside the map folder.</param>
	/// <param name="mapName">The referenced map that keeps the file, or null for the parent.</param>
	/// <returns>True when the file has a priority of its own.</returns>
	public bool TryGetConflictPriority(string relativePath, out string? mapName)
	{
		mapName = null;
		if (ConflictPriorities == null || ConflictPriorities.Count == 0)
		{
			return false;
		}

		string normalized = NormalizeRelativePath(relativePath);
		foreach (KeyValuePair<string, string?> priority in ConflictPriorities)
		{
			if (!string.Equals(NormalizeRelativePath(priority.Key), normalized, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			mapName = string.IsNullOrWhiteSpace(priority.Value) ? null : priority.Value.Trim();
			return true;
		}

		return false;
	}

	public void SetConflictPriority(string relativePath, string? mapName)
	{
		ConflictPriorities ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		string normalized = NormalizeRelativePath(relativePath);

		List<string> existingKeys = ConflictPriorities.Keys
			.Where(existingKey => string.Equals(NormalizeRelativePath(existingKey), normalized, StringComparison.OrdinalIgnoreCase))
			.ToList();

		foreach (string existingKey in existingKeys)
		{
			ConflictPriorities.Remove(existingKey);
		}

		if (!string.IsNullOrWhiteSpace(mapName))
		{
			ConflictPriorities[normalized] = mapName.Trim();
		}
	}

	public MapReference FindReference(string mapName)
	{
		foreach (MapReference reference in References)
		{
			if (string.Equals(reference.MapName, mapName, StringComparison.OrdinalIgnoreCase))
			{
				return reference;
			}
		}

		return null;
	}

	public static string NormalizeRelativePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return string.Empty;
		}

		string normalized = path.Replace('\\', '/').Trim();
		if (normalized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(6);
		}

		normalized = normalized.TrimStart('/');
		var segments = new List<string>();
		foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if (segment == ".")
			{
				continue;
			}

			if (segment == "..")
			{
				continue;
			}

			segments.Add(segment);
		}

		return string.Join('/', segments);
	}

	public static bool IsSafePathSegment(string value)
	{
		return !string.IsNullOrWhiteSpace(value) &&
		       value != "." &&
		       value != ".." &&
		       value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
		       !value.Contains('/') &&
		       !value.Contains('\\');
	}

	public static bool IsSafeFolderPath(string path)
	{
		return !string.IsNullOrWhiteSpace(path) && path.IndexOfAny(Path.GetInvalidPathChars()) < 0;
	}
}
