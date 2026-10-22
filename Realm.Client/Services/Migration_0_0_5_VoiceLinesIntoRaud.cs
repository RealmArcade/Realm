using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Audio;

namespace Realm.Client.Services;

public class Migration_0_0_5_VoiceLinesIntoRaud : IMapMigration
{
	public string FromVersion => "v0.0.4";
	public string ToVersion => "v0.0.5";
	public string Description => "Move voices.zip voice lines into each unit's .raud container with named entries and tags";

	private static readonly Dictionary<string, string> EventPropertyByToken = new(StringComparer.OrdinalIgnoreCase)
	{
		["select"] = "OnSelect",
		["selected"] = "OnSelect",
		["idle"] = "OnSelect",
		["hello"] = "OnSelect",
		["what"] = "OnSelect",
		["yes"] = "OnSelect",
		["move"] = "OnMoveOrder",
		["moveorder"] = "OnMoveOrder",
		["acknowledge"] = "OnMoveOrder",
		["orders"] = "OnMoveOrder",
		["attack"] = "OnAttackOrder",
		["attackorder"] = "OnAttackOrder",
		["wounded"] = "OnWounded",
		["hurt"] = "OnWounded",
		["pain"] = "OnWounded",
		["death"] = "OnDeath",
		["die"] = "OnDeath",
		["ready"] = "OnReady",
		["trained"] = "OnReady",
		["spell"] = "OnSpellCast",
		["cast"] = "OnSpellCast",
		["spellcast"] = "OnSpellCast"
	};

	private static readonly string[] TemplateCategories = ["Units", "Buildings", "Resources", "Props"];

	private static readonly string[] IgnoredDirectories =
	[
		".godot", ".git", ".vs", ".vscode", ".idea", "bin", "obj", "map_backups", "map_upgrades", ".backups", ".sidecarcache", ".cache"
	];

	public MigrationResult Up(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		try
		{
			string metadataPath = Path.Combine(mapDirectory, "metadata.json");
			JsonObject metadataRoot = LoadMetadata(metadataPath);

			List<string> voicesZipFiles = FindVoicesZipFiles(mapDirectory);
			var createdRaudFiles = new List<string>();
			const int totalSteps = 2;

			if (voicesZipFiles.Count > 0)
			{
				progress?.Report(new MigrationProgressUpdate(Description, 1, totalSteps, "Folding voices.zip voice lines into RAUD containers..."));

				for (int index = 0; index < voicesZipFiles.Count; index++)
				{
					string zipPath = voicesZipFiles[index];
					ProcessVoicesZip(mapDirectory, zipPath, metadataRoot, createdRaudFiles); 
				}

				UpdateManifestAssets(mapDirectory, createdRaudFiles);
			}

			progress?.Report(new MigrationProgressUpdate(Description, 2, totalSteps, "Updating map build number and saving metadata.json..."));

			metadataRoot["GameBuildNumber"] = ToVersion;
			MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);

			return new MigrationResult { Success = true, FromVersion = FromVersion, ToVersion = ToVersion };
		}
		catch (Exception ex)
		{
			return new MigrationResult { Success = false, ErrorMessage = $"Migration 0.0.5 failed: {ex.Message}", FromVersion = FromVersion, ToVersion = ToVersion };
		}
	}

	public Task<MigrationResult> UpAsync(string mapDirectory, IProgress<MigrationProgressUpdate>? progress = null)
	{
		return Task.Run(() => Up(mapDirectory, progress));
	}

	private static JsonObject LoadMetadata(string metadataPath)
	{
		if (!File.Exists(metadataPath)) return new JsonObject();
		return JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();
	}

	private static List<string> FindVoicesZipFiles(string mapDirectory)
	{
		var zipFiles = new List<string>();
		foreach (string filePath in Directory.GetFiles(mapDirectory, "voices.zip", SearchOption.AllDirectories))
		{
			if (IsInIgnoredDirectory(mapDirectory, filePath)) continue;
			zipFiles.Add(filePath);
		}

		zipFiles.Sort(StringComparer.OrdinalIgnoreCase);
		return zipFiles;
	}

	private static bool IsInIgnoredDirectory(string mapDirectory, string filePath)
	{
		string relativeDirectory = Path.GetRelativePath(mapDirectory, Path.GetDirectoryName(filePath) ?? mapDirectory);
		foreach (string segment in relativeDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
		{
			if (Array.Exists(IgnoredDirectories, ignored => ignored.Equals(segment, StringComparison.OrdinalIgnoreCase))) return true;
		}

		return false;
	}

	private static void ProcessVoicesZip(string mapDirectory, string zipPath, JsonObject metadataRoot, List<string> createdRaudFiles)
	{
		try
		{
			var clipsByUnit = ReadVoiceClips(zipPath);
			if (clipsByUnit.Count == 0) return;

			foreach (var unitClips in clipsByUnit)
			{
				string unitSlug = unitClips.Key;
				string raudPath = ResolveUnitRaudPath(mapDirectory, unitSlug, out bool created);
				WriteVoiceClipsToRaud(raudPath, unitSlug, unitClips.Value);

				if (created && !createdRaudFiles.Contains(raudPath, StringComparer.OrdinalIgnoreCase))
				{
					createdRaudFiles.Add(raudPath);
				}

				string voiceAssetPath = ComputeVoiceAssetPath(mapDirectory, raudPath);
				UpdateTemplateSounds(metadataRoot, unitSlug, voiceAssetPath, unitClips.Value);
			}

			File.Delete(zipPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Migration_0_0_5] Failed to migrate '{zipPath}': {ex.Message}");
		}
	}

	private static Dictionary<string, List<VoiceClip>> ReadVoiceClips(string zipPath)
	{
		var clipsByUnit = new Dictionary<string, List<VoiceClip>>(StringComparer.OrdinalIgnoreCase);

		using var archive = ZipFile.OpenRead(zipPath);
		foreach (var zipEntry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
		{
			if (zipEntry.FullName.EndsWith('/')) continue;
			if (!TryReadVoiceClip(zipEntry, out VoiceClip clip)) continue;

			if (!clipsByUnit.TryGetValue(clip.UnitSlug, out var unitClips))
			{
				unitClips = new List<VoiceClip>();
				clipsByUnit[clip.UnitSlug] = unitClips;
			}

			unitClips.Add(clip);
		}

		return clipsByUnit;
	}

	private static bool TryReadVoiceClip(ZipArchiveEntry zipEntry, out VoiceClip clip)
	{
		clip = default;

		string extension = Path.GetExtension(zipEntry.Name).ToLowerInvariant();
		if (string.IsNullOrEmpty(extension) || extension == ".raud") return false;
		if (!AudioConverter.IsAudioFile(zipEntry.Name)) return false;

		string baseName = Path.GetFileNameWithoutExtension(zipEntry.Name);
		int separatorIndex = baseName.IndexOf('_');
		if (separatorIndex <= 0 || separatorIndex >= baseName.Length - 1) return false;

		string unitSlug = baseName[..separatorIndex].ToLowerInvariant();
		string clipToken = baseName[(separatorIndex + 1)..];
		string eventProperty = ResolveEventProperty(clipToken);
		if (string.IsNullOrEmpty(eventProperty)) return false;

		byte[]? oggBytes = ReadClipOggBytes(zipEntry, extension);
		if (oggBytes == null || oggBytes.Length == 0) return false;

		string entryName = RaudFile.NormalizeEntryName(clipToken);
		int sortOrder = ExtractSortOrder(clipToken);
		clip = new VoiceClip(unitSlug, entryName, eventProperty, sortOrder, oggBytes);
		return true;
	}

	private static byte[]? ReadClipOggBytes(ZipArchiveEntry zipEntry, string extension)
	{
		try
		{
			if (extension == ".ogg")
			{
				using var entryStream = zipEntry.Open();
				using var memoryStream = new MemoryStream();
				entryStream.CopyTo(memoryStream);
				return memoryStream.ToArray();
			}

			string tempSourcePath = Path.Combine(Path.GetTempPath(), $"realm_voice_{Guid.NewGuid():N}{extension}");
			string tempOggPath = Path.ChangeExtension(tempSourcePath, ".ogg");
			try
			{
				zipEntry.ExtractToFile(tempSourcePath, true);
				var conversion = AudioConverter.ConvertToOgg(tempSourcePath, tempOggPath);
				if (!conversion.Success || !File.Exists(tempOggPath)) return null;
				return File.ReadAllBytes(tempOggPath);
			}
			finally
			{
				DeleteFileSilently(tempSourcePath);
				DeleteFileSilently(tempOggPath);
			}
		}
		catch
		{
			return null;
		}
	}

	private static void DeleteFileSilently(string filePath)
	{
		if (!File.Exists(filePath)) return;
		try
		{
			File.Delete(filePath);
		}
		catch
		{
		}
	}

	private static string ResolveEventProperty(string clipToken)
	{
		string token = clipToken.Split('_')[0];
		return EventPropertyByToken.TryGetValue(token, out string? eventProperty) ? eventProperty : string.Empty;
	}

	private static int ExtractSortOrder(string clipToken)
	{
		int lastSeparator = clipToken.LastIndexOf('_');
		if (lastSeparator < 0 || lastSeparator == clipToken.Length - 1) return 0;
		return int.TryParse(clipToken[(lastSeparator + 1)..], out int order) ? order : 0;
	}

	private static string ResolveUnitRaudPath(string mapDirectory, string unitSlug, out bool created)
	{
		string? existingPath = FindExistingRaudFile(mapDirectory, unitSlug);
		if (existingPath != null)
		{
			created = false;
			return existingPath;
		}

		string targetDirectory = Path.Combine(mapDirectory, "Assets", "audio", "sfx");
		Directory.CreateDirectory(targetDirectory);
		created = true;
		return Path.Combine(targetDirectory, $"{unitSlug}.raud");
	}

	private static string? FindExistingRaudFile(string mapDirectory, string unitSlug)
	{
		var candidates = new List<string>();
		foreach (string filePath in Directory.GetFiles(mapDirectory, "*.raud", SearchOption.AllDirectories))
		{
			if (IsInIgnoredDirectory(mapDirectory, filePath)) continue;
			if (string.Equals(Path.GetFileNameWithoutExtension(filePath), unitSlug, StringComparison.OrdinalIgnoreCase))
			{
				candidates.Add(filePath);
			}
		}

		if (candidates.Count == 0) return null;

		string preferredDirectory = Path.Combine(mapDirectory, "Assets", "audio", "sfx");
		string? preferred = candidates.FirstOrDefault(candidate => IsInDirectory(candidate, preferredDirectory));
		return preferred ?? candidates[0];
	}

	private static bool IsInDirectory(string filePath, string directory)
	{
		string normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return Path.GetFullPath(filePath).StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
	}

	private static void WriteVoiceClipsToRaud(string raudPath, string unitSlug, List<VoiceClip> clips)
	{
		JsonObject metaObj = new();
		var entries = new List<RaudEntry>();

		if (File.Exists(raudPath))
		{
			byte[] existingBytes = File.ReadAllBytes(raudPath);
			var (existingMeta, existingEntries, _) = RaudFile.Parse(existingBytes);
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
			}

			entries.AddRange(existingEntries);
		}

		foreach (VoiceClip clip in clips.OrderBy(c => c.EventProperty, StringComparer.Ordinal).ThenBy(c => c.SortOrder).ThenBy(c => c.EntryName, StringComparer.Ordinal))
		{
			int existingIndex = entries.FindIndex(entry => string.Equals(RaudFile.NormalizeEntryName(entry.Name), clip.EntryName, StringComparison.Ordinal));
			var newEntry = new RaudEntry(clip.EntryName, clip.OggBytes);
			if (existingIndex >= 0) entries[existingIndex] = newEntry;
			else entries.Add(newEntry);
		}

		metaObj["format"] = RaudFile.Format;
		metaObj["asset_type"] = "SoundEffect";
		metaObj["tags"] = BuildTagArray(metaObj["tags"], unitSlug, clips);

		byte[] raudBytes = RaudFile.Build(metaObj.ToJsonString(), entries);
		string? targetDirectory = Path.GetDirectoryName(raudPath);
		if (!string.IsNullOrEmpty(targetDirectory)) Directory.CreateDirectory(targetDirectory);
		File.WriteAllBytes(raudPath, raudBytes);
	}

	private static JsonArray BuildTagArray(JsonNode? existingTagsNode, string unitSlug, List<VoiceClip> clips)
	{
		var tags = new List<string>();
		if (existingTagsNode is JsonArray existingTags)
		{
			foreach (var tagNode in existingTags)
			{
				string? tag = tagNode?.ToString();
				if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
			}
		}

		tags.Add("voice");
		tags.Add(unitSlug);

		string[] eventTags = clips
			.Select(clip => clip.EventProperty.ToLowerInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		tags.AddRange(eventTags);

		var cleanTags = tags
			.Where(tag => !string.IsNullOrWhiteSpace(tag))
			.Select(tag => tag.Trim().ToLowerInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(tag => (JsonNode)JsonValue.Create(tag)!)
			.ToArray();

		return new JsonArray(cleanTags);
	}

	private static string ComputeVoiceAssetPath(string mapDirectory, string raudPath)
	{
		string audioRoot = Path.Combine(mapDirectory, "Assets", "audio");
		string fullRaudPath = Path.GetFullPath(raudPath);

		if (IsInDirectory(fullRaudPath, audioRoot))
		{
			return Path.GetRelativePath(audioRoot, fullRaudPath).Replace('\\', '/');
		}

		return Path.GetRelativePath(mapDirectory, fullRaudPath).Replace('\\', '/');
	}

	private static void UpdateTemplateSounds(JsonObject metadataRoot, string unitSlug, string voiceAssetPath, List<VoiceClip> clips)
	{
		if (metadataRoot["Templates"] is not JsonObject templatesObj) return;

		foreach (string category in TemplateCategories)
		{
			if (templatesObj[category] is not JsonArray categoryArray) continue;

			foreach (var templateNode in categoryArray)
			{
				if (templateNode is not JsonObject templateObj) continue;
				if (!TemplateMatchesUnit(templateObj, unitSlug)) continue;

				ApplySoundsToTemplate(templateObj, voiceAssetPath, clips);
			}
		}
	}

	private static bool TemplateMatchesUnit(JsonObject templateObj, string unitSlug)
	{
		string? templateId = templateObj["TemplateID"]?.ToString();
		if (!string.IsNullOrWhiteSpace(templateId) && string.Equals(TemplateIDHelper.GenerateSlug(templateId), unitSlug, StringComparison.OrdinalIgnoreCase)) return true;

		string? name = templateObj["Name"]?.ToString();
		if (!string.IsNullOrWhiteSpace(name) && string.Equals(TemplateIDHelper.GenerateSlug(name), unitSlug, StringComparison.OrdinalIgnoreCase)) return true;

		string? modelPath = templateObj["ModelPath"]?.ToString();
		if (!string.IsNullOrWhiteSpace(modelPath))
		{
			string modelSlug = Path.GetFileNameWithoutExtension(modelPath);
			if (string.Equals(TemplateIDHelper.GenerateSlug(modelSlug), unitSlug, StringComparison.OrdinalIgnoreCase)) return true;
			if (string.Equals(modelSlug, unitSlug, StringComparison.OrdinalIgnoreCase)) return true;
		}

		return false;
	}

	private static void ApplySoundsToTemplate(JsonObject templateObj, string voiceAssetPath, List<VoiceClip> clips)
	{
		if (templateObj["Sounds"] is not JsonObject soundsObj)
		{
			soundsObj = new JsonObject();
			templateObj["Sounds"] = soundsObj;
		}

		soundsObj["VoiceAsset"] = voiceAssetPath;

		foreach (var eventGroup in clips.GroupBy(clip => clip.EventProperty))
		{
			var entryNames = eventGroup
				.OrderBy(clip => clip.SortOrder)
				.ThenBy(clip => clip.EntryName, StringComparer.Ordinal)
				.Select(clip => (JsonNode)JsonValue.Create(clip.EntryName)!)
				.ToArray();

			soundsObj[eventGroup.Key] = new JsonArray(entryNames);
		}
	}

	private static void UpdateManifestAssets(string mapDirectory, List<string> createdRaudFiles)
	{
		if (createdRaudFiles.Count == 0) return;

		string manifestPath = Path.Combine(mapDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		JsonObject? manifestRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
		if (manifestRoot == null) return;

		if (manifestRoot["Assets"] is not JsonObject assetsObj)
		{
			assetsObj = new JsonObject();
			manifestRoot["Assets"] = assetsObj;
		}

		if (assetsObj["SoundEffect"] is not JsonObject soundEffectAssets)
		{
			soundEffectAssets = new JsonObject();
			assetsObj["SoundEffect"] = soundEffectAssets;
		}

		foreach (string raudPath in createdRaudFiles)
		{
			string fileName = Path.GetFileName(raudPath);
			string blake3 = RealmMetadataHelper.ComputeBlake3(raudPath);
			if (string.IsNullOrEmpty(blake3)) continue;

			soundEffectAssets[fileName] = blake3;
		}

		MapJsonFormatter.SaveFormattedJson(manifestPath, manifestRoot);
	}

	private readonly record struct VoiceClip(
		string UnitSlug,
		string EntryName,
		string EventProperty,
		int SortOrder,
		byte[] OggBytes);
}
