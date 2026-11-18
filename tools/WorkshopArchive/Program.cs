using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Tools.WorkshopArchive;

/// <summary>
///     Archives every Workshop item the game publishes under, keeps a verified copy of its content, and stages a reviewed
///     mirror. It is a developer tool: it is not part of the game solution, never runs inside a match, and never touches a
///     player's Steam account. Archiving is automatic and republishing is not, because a mirror republishes another
///     creator's work and that decision belongs to a person reading the attribution.
/// </summary>
public static class Program
{
	public static async Task<int> Main(string[] arguments)
	{
		if (arguments.Length == 0 || arguments[0] is "-h" or "--help" or "help")
		{
			PrintUsage();
			return 0;
		}

		string? apiKey = Environment.GetEnvironmentVariable("STEAM_WEB_API_KEY");
		string? archiveAccount = Environment.GetEnvironmentVariable("STEAM_ARCHIVE_ACCOUNT");
		string? archiveRoot = ResolveOption(arguments, "--archive-root");

		if (string.IsNullOrWhiteSpace(apiKey))
		{
			Console.Error.WriteLine("STEAM_WEB_API_KEY is not set, so the Workshop cannot be listed. Nothing was archived.");
			return 2;
		}

		if (string.IsNullOrWhiteSpace(archiveRoot))
		{
			Console.Error.WriteLine("--archive-root is required. It belongs outside the repository: it holds other creators' content.");
			return 2;
		}

		string root = Path.GetFullPath(archiveRoot);
		Directory.CreateDirectory(root);

		string command = arguments[0];
		var archive = new WorkshopArchive(root, apiKey, archiveAccount);

		try
		{
			return command switch
			{
				"list" => await archive.ListAsync(ResolveAppIds(arguments), dryRun: true),
				"archive" => await archive.ArchiveAsync(ResolveAppIds(arguments), ResolveIds(arguments)),
				"verify" => archive.VerifyAsync(ResolveAppIds(arguments)),
				"stage-mirror" => archive.StageMirror(ResolveId(arguments), arguments),
				"takedown" => archive.RecordTakedown(ResolveId(arguments), arguments),
				_ => Unknown(command)
			};
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"The archive run failed: {exception.Message}");
			return 1;
		}
	}

	private static int Unknown(string command)
	{
		Console.Error.WriteLine($"Unknown command '{command}'.");
		PrintUsage();
		return 2;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("""
Realm Workshop archive

  list          --archive-root <dir> --appid <id>[,<id>]
  archive       --archive-root <dir> --appid <id>[,<id>] [--item <id>] [--full] [--dry-run]
  verify        --archive-root <dir> --appid <id>[,<id>]
  stage-mirror  --archive-root <dir> --appid <id> --item <id> --reviewer <steamid> --note "<why>" --confirm
  takedown      --archive-root <dir> --appid <id> --item <id> --reason "<request>" --confirm

Environment:
  STEAM_WEB_API_KEY       the Steam Web API key the listing uses
  STEAM_ARCHIVE_ACCOUNT   the dedicated archive account SteamCMD logs in as
""");
	}

	private static IReadOnlyList<uint> ResolveAppIds(string[] arguments)
	{
		string? stated = ResolveOption(arguments, "--appid");
		if (string.IsNullOrWhiteSpace(stated))
		{
			return new[] { 5372260u, 5537420u };
		}

		return stated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(value => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint appId) ? appId : 0u)
			.Where(appId => appId != 0)
			.ToList();
	}

	private static ulong ResolveId(string[] arguments)
	{
		string? stated = ResolveOption(arguments, "--item");
		return ulong.TryParse(stated, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong id) ? id : 0;
	}

	private static IReadOnlyList<ulong> ResolveIds(string[] arguments)
	{
		ulong single = ResolveId(arguments);
		return single == 0 ? Array.Empty<ulong>() : new[] { single };
	}

	private static string? ResolveOption(string[] arguments, string name)
	{
		for (int index = 0; index < arguments.Length - 1; index++)
		{
			if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
		}

		return null;
	}
}

/// <summary>
///     The archive itself: listing, downloading, verifying and staging, with the archive root as its only state.
/// </summary>
public class WorkshopArchive
{
	private const string IndexFileName = "archive.db.json";
	private const string ReportJsonFileName = "archive_report.json";
	private const string ReportMarkdownFileName = "archive_report.md";
	private const string SteamWebApiClient = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/";

	private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

	private readonly string _root;
	private readonly string _apiKey;
	private readonly string? _archiveAccount;

	public WorkshopArchive(string root, string apiKey, string? archiveAccount)
	{
		_root = root;
		_apiKey = apiKey;
		_archiveAccount = archiveAccount;
	}

	/// <summary>Lists what the Workshop holds for each AppID, which is what a dry run reports.</summary>
	public async Task<int> ListAsync(IReadOnlyList<uint> appIds, bool dryRun)
	{
		using var http = new HttpClient();
		int total = 0;

		foreach (uint appId in appIds)
		{
			IReadOnlyList<JsonObject> items = await QueryFilesAsync(http, appId);
			total += items.Count;
			Console.WriteLine($"{appId}: {items.Count} item(s)");
			foreach (JsonObject item in items)
			{
				Console.WriteLine($"  {item["publishedfileid"]}  {item["title"]}");
			}
		}

		Console.WriteLine(dryRun ? $"{total} item(s) would be considered. Nothing was downloaded." : $"{total} item(s).");
		return 0;
	}

	/// <summary>
	///     Downloads and archives what changed. An item is fetched when its content handle or its update time differs from
	///     the archived record, which is what makes a second run cheap.
	/// </summary>
	public async Task<int> ArchiveAsync(IReadOnlyList<uint> appIds, IReadOnlyList<ulong> onlyItems)
	{
		if (string.IsNullOrWhiteSpace(_archiveAccount))
		{
			Console.Error.WriteLine("STEAM_ARCHIVE_ACCOUNT is not set, so SteamCMD has no account to log in as. Nothing was archived.");
			return 2;
		}

		using var http = new HttpClient();
		JsonObject index = LoadIndex();
		var report = new ArchiveReport();

		foreach (uint appId in appIds)
		{
			IReadOnlyList<JsonObject> items = await QueryFilesAsync(http, appId);
			report.ConsideredByApp[appId.ToString(CultureInfo.InvariantCulture)] = items.Count;

			foreach (JsonObject item in items)
			{
				ulong publishedFileId = ReadUlong(item, "publishedfileid");
				if (publishedFileId == 0) continue;
				if (onlyItems.Count > 0 && !onlyItems.Contains(publishedFileId)) continue;

				string itemFolder = Path.Combine(_root, appId.ToString(CultureInfo.InvariantCulture), publishedFileId.ToString(CultureInfo.InvariantCulture));
				Directory.CreateDirectory(itemFolder);

				string contentHandle = ReadString(item, "hcontent_file");
				long timeUpdated = (long)ReadDouble(item, "time_updated");
				string recordPath = Path.Combine(itemFolder, "metadata.json");
				JsonObject? existing = File.Exists(recordPath) ? JsonNode.Parse(File.ReadAllText(recordPath)) as JsonObject : null;

				bool unchanged = existing != null
					&& string.Equals(existing["content_handle"]?.GetValue<string>(), contentHandle, StringComparison.Ordinal)
					&& (long)(existing["time_updated"]?.GetValue<double>() ?? 0) == timeUpdated;

				if (unchanged)
				{
					report.Unchanged++;
					continue;
				}

				File.WriteAllText(recordPath, item.ToJsonString(WriteOptions));
				report.Updated++;

				bool downloaded = await DownloadItemAsync(appId, publishedFileId, itemFolder);
				if (!downloaded)
				{
					report.Failed++;
					continue;
				}

				WriteAttribution(itemFolder, item);
				WriteManifest(itemFolder);
				report.Downloaded++;
			}
		}

		MarkMissingItems(index, report);
		index["items"] = BuildIndexEntries(index);
		SaveIndex(index);
		WriteReports(report);
		report.Print();

		return report.Failed > 0 ? 1 : 0;
	}

	/// <summary>Recomputes each archived item's manifest and reports a file whose bytes no longer match it.</summary>
	public int VerifyAsync(IReadOnlyList<uint> appIds)
	{
		int corrupt = 0;
		foreach (uint appId in appIds)
		{
			string appFolder = Path.Combine(_root, appId.ToString(CultureInfo.InvariantCulture));
			if (!Directory.Exists(appFolder)) continue;

			foreach (string itemFolder in Directory.EnumerateDirectories(appFolder))
			{
				string manifestPath = Path.Combine(itemFolder, "manifest.json");
				if (!File.Exists(manifestPath)) continue;

				JsonObject? manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
				JsonArray? files = manifest?["files"] as JsonArray;
				if (files == null) continue;

				foreach (JsonNode? fileNode in files)
				{
					string relativePath = fileNode?["path"]?.GetValue<string>() ?? string.Empty;
					string expected = fileNode?["sha256"]?.GetValue<string>() ?? string.Empty;
					string contentPath = Path.Combine(itemFolder, "content", relativePath);
					if (relativePath.Length == 0 || !File.Exists(contentPath) || ComputeSha256(contentPath) != expected)
					{
						Console.WriteLine($"corrupt: {itemFolder} {relativePath}");
						corrupt++;
					}
				}
			}
		}

		Console.WriteLine(corrupt == 0 ? "Every archived item matches its manifest." : $"{corrupt} file(s) need a re-download.");
		return corrupt == 0 ? 0 : 1;
	}

	/// <summary>
	///     Copies a reviewed item into the mirror folder with the licence and attribution a mirror has to carry. Nothing is
	///     uploaded: the upload is a person using the dedicated mirror account, and this only prepares what they send.
	/// </summary>
	public int StageMirror(ulong publishedFileId, string[] arguments)
	{
		if (publishedFileId == 0)
		{
			Console.Error.WriteLine("--item <publishedfileid> is required.");
			return 2;
		}

		bool confirmed = arguments.Any(argument => string.Equals(argument, "--confirm", StringComparison.Ordinal));
		string reviewer = ReadOption(arguments, "--reviewer") ?? string.Empty;
		string note = ReadOption(arguments, "--note") ?? string.Empty;

		if (!confirmed || reviewer.Length == 0 || note.Length == 0)
		{
			Console.Error.WriteLine("Staging a mirror republishes another creator's work, so it needs --reviewer, --note and --confirm.");
			return 2;
		}

		string? itemFolder = FindItemFolder(publishedFileId);
		if (itemFolder == null)
		{
			Console.Error.WriteLine($"Item {publishedFileId} is not in the archive. Archive it before staging a mirror.");
			return 1;
		}

		string mirrorFolder = Path.Combine(_root, "mirror", publishedFileId.ToString(CultureInfo.InvariantCulture));
		if (Directory.Exists(mirrorFolder)) Directory.Delete(mirrorFolder, recursive: true);
		CopyDirectory(Path.Combine(itemFolder, "content"), Path.Combine(mirrorFolder, "content"));

		var mirror = new JsonObject
		{
			["original_item_id"] = publishedFileId.ToString(CultureInfo.InvariantCulture),
			["author_steam_id"] = ReadArchivedAuthor(itemFolder),
			["original_title"] = ReadArchivedTitle(itemFolder),
			["license_version"] = "Steam Workshop UGC licence as accepted at publish (mirror clause)",
			["reviewer_steam_id"] = reviewer,
			["date"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
			["upload_account"] = "the dedicated mirror account, never the partner account",
			["review_note"] = note
		};

		File.WriteAllText(Path.Combine(mirrorFolder, "mirror.json"), mirror.ToJsonString(WriteOptions));
		File.WriteAllText(Path.Combine(mirrorFolder, "ATTRIBUTION.txt"), ReadAttribution(itemFolder));

		Console.WriteLine($"Staged item {publishedFileId} in {mirrorFolder}. Upload it from the mirror account, never automatically.");
		return 0;
	}

	/// <summary>Records a takedown and removes the mirror, never the archive copy.</summary>
	public int RecordTakedown(ulong publishedFileId, string[] arguments)
	{
		if (publishedFileId == 0 || !arguments.Any(argument => string.Equals(argument, "--confirm", StringComparison.Ordinal)))
		{
			Console.Error.WriteLine("A takedown needs --item and --confirm, and it removes only the mirror.");
			return 2;
		}

		string reason = ReadOption(arguments, "--reason") ?? string.Empty;
		string mirrorFolder = Path.Combine(_root, "mirror", publishedFileId.ToString(CultureInfo.InvariantCulture));
		if (Directory.Exists(mirrorFolder)) Directory.Delete(mirrorFolder, recursive: true);

		string recordPath = Path.Combine(_root, "takedowns");
		Directory.CreateDirectory(recordPath);
		var record = new JsonObject
		{
			["item_id"] = publishedFileId.ToString(CultureInfo.InvariantCulture),
			["reason"] = reason,
			["recorded_at"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
			["mirror_removed"] = true,
			["archive_kept"] = true
		};
		File.WriteAllText(Path.Combine(recordPath, $"{publishedFileId}.json"), record.ToJsonString(WriteOptions));

		Console.WriteLine($"The mirror of {publishedFileId} is gone and the request is recorded. The archive copy is kept.");
		return 0;
	}

	private async Task<IReadOnlyList<JsonObject>> QueryFilesAsync(HttpClient http, uint appId)
	{
		var items = new List<JsonObject>();
		for (int page = 1; page <= 200; page++)
		{
			string url = $"{SteamWebApiClient}?key={Uri.EscapeDataString(_apiKey)}&appid={appId}&page={page}&numperpage=100" +
			             "&return_short_description=true&return_vote_data=true&return_tags=true&return_children=true" +
			             "&return_metadata=true&return_details=true&query_type=1";

			try
			{
				using HttpResponseMessage response = await http.GetAsync(url);
				if (!response.IsSuccessStatusCode)
				{
					Console.Error.WriteLine($"The Workshop listing answered {(int)response.StatusCode} for app {appId}.");
					break;
				}

				JsonNode? root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
				if (root?["response"]?["publishedfiledetails"] is not JsonArray details) break;

				int added = 0;
				foreach (JsonNode? entry in details)
				{
					if (entry is not JsonObject item) continue;
					items.Add(item);
					added++;
				}

				if (added < 100) break;
			}
			catch (Exception exception)
			{
				Console.Error.WriteLine($"The Workshop listing failed for app {appId}: {exception.Message}");
				break;
			}
		}

		return items;
	}

	private async Task<bool> DownloadItemAsync(uint appId, ulong publishedFileId, string itemFolder)
	{
		string staging = Path.Combine(Path.GetTempPath(), $"realm-archive-{publishedFileId}");
		if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
		Directory.CreateDirectory(staging);

		string commandLine = $"+force_install_dir \"{staging}\" +login {_archiveAccount} +workshop_download_item {appId} {publishedFileId} +quit";
		var startInfo = new System.Diagnostics.ProcessStartInfo("steamcmd", commandLine)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};

		try
		{
			using var process = System.Diagnostics.Process.Start(startInfo);
			if (process == null) return false;
			await process.WaitForExitAsync();
			if (process.ExitCode != 0) return false;
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine($"SteamCMD could not download item {publishedFileId}: {exception.Message}");
			return false;
		}

		string downloaded = Path.Combine(staging, "steamapps", "workshop", "content", appId.ToString(CultureInfo.InvariantCulture), publishedFileId.ToString(CultureInfo.InvariantCulture));
		if (!Directory.Exists(downloaded))
		{
			Console.Error.WriteLine($"SteamCMD reported success but item {publishedFileId} is not where it downloads to.");
			return false;
		}

		CopyDirectory(downloaded, Path.Combine(itemFolder, "content"));
		Directory.Delete(staging, recursive: true);
		return true;
	}

	private void WriteAttribution(string itemFolder, JsonObject item)
	{
		var builder = new StringBuilder();
		builder.AppendLine($"Workshop item {ReadUlong(item, "publishedfileid")}");
		builder.AppendLine($"Title: {ReadString(item, "title")}");
		builder.AppendLine($"Author SteamID64: {ReadUlong(item, "creator")}");
		builder.AppendLine($"Published: {ReadString(item, "time_created")}");
		builder.AppendLine($"App: {ReadUlong(item, "consumer_app_id")}");
		builder.AppendLine();
		builder.AppendLine("Composed from:");

		string referencesPath = Path.Combine(itemFolder, "content", "references.json");
		if (File.Exists(referencesPath))
		{
			try
			{
				JsonNode? root = JsonNode.Parse(File.ReadAllText(referencesPath));
				if (root?["References"] is JsonArray references && references.Count > 0)
				{
					foreach (JsonNode? reference in references)
					{
						builder.AppendLine($"  - {reference?["MapName"]?.GetValue<string>() ?? "an unnamed map"}");
					}
				}
				else
				{
					builder.AppendLine("  (none)");
				}
			}
			catch (JsonException)
			{
				builder.AppendLine("  (references.json could not be read)");
			}
		}
		else
		{
			builder.AppendLine("  (none)");
		}

		File.WriteAllText(Path.Combine(itemFolder, "ATTRIBUTION.txt"), builder.ToString());
	}

	private static void WriteManifest(string itemFolder)
	{
		string contentFolder = Path.Combine(itemFolder, "content");
		var files = new JsonArray();
		string folderHash = ComputeFolderHash(contentFolder, files);

		var manifest = new JsonObject
		{
			["folder_hash"] = folderHash,
			["files"] = files
		};

		File.WriteAllText(Path.Combine(itemFolder, "manifest.json"), manifest.ToJsonString(WriteOptions));
	}

	private static string ComputeFolderHash(string folder, JsonArray files)
	{
		using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (string path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
		{
			string relative = Path.GetRelativePath(folder, path).Replace('\\', '/');
			string sha = ComputeSha256(path);
			files.Add(new JsonObject { ["path"] = relative, ["sha256"] = sha, ["size"] = new FileInfo(path).Length });
			digest.AppendData(Encoding.UTF8.GetBytes($"{relative}\n{sha}\n"));
		}

		return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
	}

	private static string ComputeSha256(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
	}

	private void MarkMissingItems(JsonObject index, ArchiveReport report)
	{
		if (index["items"] is not JsonObject itemsObject) return;
		foreach (var pair in itemsObject)
		{
			if (pair.Value is not JsonObject entry) continue;
			string folder = entry["folder"]?.GetValue<string>() ?? string.Empty;
			if (folder.Length == 0 || Directory.Exists(folder)) continue;

			entry["state"] = "missing";
			report.Missing++;
		}
	}

	private static JsonObject BuildIndexEntries(JsonObject index)
	{
		return index["items"] as JsonObject ?? new JsonObject();
	}

	private JsonObject LoadIndex()
	{
		string path = Path.Combine(_root, IndexFileName);
		if (!File.Exists(path)) return new JsonObject { ["items"] = new JsonObject() };

		try
		{
			return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject { ["items"] = new JsonObject() };
		}
		catch (JsonException)
		{
			return new JsonObject { ["items"] = new JsonObject() };
		}
	}

	private void SaveIndex(JsonObject index)
	{
		File.WriteAllText(Path.Combine(_root, IndexFileName), index.ToJsonString(WriteOptions));
	}

	private void WriteReports(ArchiveReport report)
	{
		File.WriteAllText(Path.Combine(_root, ReportJsonFileName), JsonSerializer.Serialize(report, WriteOptions));
		File.WriteAllText(Path.Combine(_root, ReportMarkdownFileName), report.ToMarkdown());
	}

	private string? FindItemFolder(ulong publishedFileId)
	{
		string id = publishedFileId.ToString(CultureInfo.InvariantCulture);
		foreach (string appFolder in Directory.EnumerateDirectories(_root))
		{
			if (!uint.TryParse(Path.GetFileName(appFolder), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue;

			string candidate = Path.Combine(appFolder, id);
			if (Directory.Exists(candidate)) return candidate;
		}

		return null;
	}

	private static string ReadArchivedAuthor(string itemFolder)
	{
		string path = Path.Combine(itemFolder, "metadata.json");
		if (!File.Exists(path)) return string.Empty;

		JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
		return root?["creator"]?.ToString() ?? string.Empty;
	}

	private static string ReadArchivedTitle(string itemFolder)
	{
		string path = Path.Combine(itemFolder, "metadata.json");
		if (!File.Exists(path)) return string.Empty;

		JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
		return root?["title"]?.ToString() ?? string.Empty;
	}

	private static string ReadAttribution(string itemFolder)
	{
		string path = Path.Combine(itemFolder, "ATTRIBUTION.txt");
		return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
	}

	private static void CopyDirectory(string source, string destination)
	{
		if (!Directory.Exists(source)) return;

		Directory.CreateDirectory(destination);
		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			string target = Path.Combine(destination, Path.GetRelativePath(source, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, overwrite: true);
		}
	}

	private static string? ReadOption(string[] arguments, string name)
	{
		for (int index = 0; index < arguments.Length - 1; index++)
		{
			if (string.Equals(arguments[index], name, StringComparison.Ordinal)) return arguments[index + 1];
		}

		return null;
	}

	private static ulong ReadUlong(JsonObject item, string property)
	{
		JsonNode? node = item[property];
		if (node == null) return 0;
		return ulong.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong value) ? value : 0;
	}

	private static double ReadDouble(JsonObject item, string property)
	{
		JsonNode? node = item[property];
		if (node == null) return 0;
		return double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
	}

	private static string ReadString(JsonObject item, string property) => item[property]?.ToString() ?? string.Empty;
}

/// <summary>What one archive run did, which is the tool's only user-facing output.</summary>
public class ArchiveReport
{
	public int Considered { get; set; }
	public int Updated { get; set; }
	public int Downloaded { get; set; }
	public int Unchanged { get; set; }
	public int Failed { get; set; }
	public int Missing { get; set; }
	public long BytesDownloaded { get; set; }
	public Dictionary<string, int> ConsideredByApp { get; } = new();

	public void Print()
	{
		Console.WriteLine($"considered {ConsideredByApp.Values.Sum()}, updated {Updated}, downloaded {Downloaded}, unchanged {Unchanged}, missing {Missing}, failed {Failed}");
		foreach (var pair in ConsideredByApp) Console.WriteLine($"  app {pair.Key}: {pair.Value} item(s)");
	}

	public string ToMarkdown()
	{
		var builder = new StringBuilder();
		builder.AppendLine("# Workshop archive report");
		builder.AppendLine();
		builder.AppendLine($"- considered: {ConsideredByApp.Values.Sum()}");
		builder.AppendLine($"- updated: {Updated}");
		builder.AppendLine($"- downloaded: {Downloaded}");
		builder.AppendLine($"- unchanged: {Unchanged}");
		builder.AppendLine($"- missing since the last run: {Missing}");
		builder.AppendLine($"- failed: {Failed}");
		foreach (var pair in ConsideredByApp) builder.AppendLine($"- app {pair.Key}: {pair.Value} item(s)");
		return builder.ToString();
	}
}
