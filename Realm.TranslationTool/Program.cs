using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Realm.TranslationTool
{
	partial class Program
	{
		private const int MaximumReportedEntries = 20;
		private const int MaximumReportedSources = 25;
		private const int MinimumProseWordCount = 2;
		private const int MinimumAttributionRunLength = 12;

		private static readonly string[] UntranslatableDirectoryNames = { "obj", "bin", ".godot", ".git", "node_modules", "Maps", "MapTemplate", "vscode_embedded", "wasi_sdk_embedded" };

		[GeneratedRegex(@"[A-Za-z]{3,}")]
		private static partial Regex ProseWordRegex();

		[GeneratedRegex(@"[A-Za-z][A-Za-z0-9 ,.:'()\-/&%!?]{11,}")]
		private static partial Regex AttributionRunRegex();

		[GeneratedRegex(@"\{[^{}]*\}")]
		private static partial Regex PlaceholderTokenRegex();

		[GeneratedRegex(@"\[[^\[\]]*\]")]
		private static partial Regex BracketTokenRegex();

		private static readonly JsonSerializerOptions SerializerOptions = new()
		{
			WriteIndented = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			NewLine = "\n",
			IndentCharacter = ' ',
			IndentSize = 2
		};

		private static readonly UTF8Encoding CatalogEncoding = new(encoderShouldEmitUTF8Identifier: false);

		private static readonly HashSet<string> BbCodeTagNames = new(StringComparer.Ordinal)
		{
			"b", "i", "u", "s", "strike", "url", "img", "list", "olist", "hr", "p",
			"h1", "h2", "h3", "noparse", "code", "quote", "color", "table", "spoiler",
			"center", "right", "left", "size", "font", "sub", "sup", "*"
		};

		static int Main(string[] args)
		{
			string command = args.Length > 0 ? args[0].TrimStart('-').ToLowerInvariant() : "verify";
			string localeDirectory = ResolveLocaleDirectory(Directory.GetCurrentDirectory());

			if (!Directory.Exists(localeDirectory))
			{
				WriteError($"Error: Locale directory not found at: {localeDirectory}");
				return 2;
			}

			string englishPath = Path.Combine(localeDirectory, "en.json");
			Dictionary<string, string>? englishCatalog = LoadCatalog(englishPath);

			if (englishCatalog == null)
			{
				return 2;
			}

			string[] localePaths = Directory.GetFiles(localeDirectory, "*.json")
				.Where(path => !Path.GetFileName(path).Equals("en.json", StringComparison.OrdinalIgnoreCase))
				.OrderBy(path => path, StringComparer.Ordinal)
				.ToArray();

			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine($"Locale directory: {localeDirectory}");
			Console.WriteLine($"en.json: {englishCatalog.Count} keys across {localePaths.Length} locale catalogs");
			Console.ResetColor();

			switch (command)
			{
				case "verify":
					return Verify(englishCatalog, localePaths);
				case "prune":
					return Prune(englishCatalog, localePaths);
				case "coverage":
					return Coverage(englishCatalog, localePaths, localeDirectory);
				default:
					WriteError($"Error: Unknown command '{args[0]}'.");
					Console.WriteLine("Usage: Realm.TranslationTool [verify|prune|coverage]");
					return 2;
			}
		}

		private static int Verify(Dictionary<string, string> englishCatalog, string[] localePaths)
		{
			int issueCount = 0;

			foreach (string localePath in localePaths)
			{
				string locale = Path.GetFileNameWithoutExtension(localePath);
				Dictionary<string, string>? localeCatalog = LoadCatalog(localePath);

				Console.WriteLine();
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"--- {locale} ({Path.GetFileName(localePath)}) ---");
				Console.ResetColor();

				if (localeCatalog == null)
				{
					issueCount++;
					continue;
				}

				var missingKeys = new List<string>();
				var emptyValues = new List<string>();
				var placeholderMismatches = new List<string>();
				var bbCodeMismatches = new List<string>();
				var bracketHintDifferences = new List<string>();
				var untranslatedValues = new List<string>();

				foreach (var englishEntry in englishCatalog)
				{
					string key = englishEntry.Key;
					string englishText = englishEntry.Value;

					if (!localeCatalog.TryGetValue(key, out string? localizedText))
					{
						missingKeys.Add(key);
						continue;
					}

					if (string.IsNullOrWhiteSpace(localizedText))
					{
						emptyValues.Add(key);
						continue;
					}

					string expectedPlaceholders = DescribeTokens(PlaceholderTokenRegex(), englishText);
					string actualPlaceholders = DescribeTokens(PlaceholderTokenRegex(), localizedText);
					if (!expectedPlaceholders.Equals(actualPlaceholders, StringComparison.Ordinal))
					{
						placeholderMismatches.Add($"\"{key}\" expected [{expectedPlaceholders}] but found [{actualPlaceholders}]");
					}

					DescribeBrackets(englishText, out string expectedBbCode, out string expectedHints);
					DescribeBrackets(localizedText, out string actualBbCode, out string actualHints);
					if (!expectedBbCode.Equals(actualBbCode, StringComparison.Ordinal))
					{
						bbCodeMismatches.Add($"\"{key}\" expected [{expectedBbCode}] but found [{actualBbCode}]");
					}

					if (!expectedHints.Equals(actualHints, StringComparison.Ordinal))
					{
						bracketHintDifferences.Add($"\"{key}\" expected [{expectedHints}] but found [{actualHints}]");
					}

					if (localizedText.Equals(englishText, StringComparison.Ordinal))
					{
						untranslatedValues.Add(key);
					}
				}

				var orphanKeys = localeCatalog.Keys
					.Where(key => !englishCatalog.ContainsKey(key))
					.ToList();

				issueCount += ReportIssues("Missing keys", missingKeys, true);
				issueCount += ReportIssues("Empty values", emptyValues, true);
				issueCount += ReportIssues("Placeholder mismatches", placeholderMismatches, true);
				issueCount += ReportIssues("BBCode tag mismatches", bbCodeMismatches, true);
				ReportIssues("Bracket hint differences (hotkeys and flags)", bracketHintDifferences, false);
				issueCount += ReportIssues("Orphan keys (absent from en.json)", orphanKeys, true);
				ReportIssues("Values identical to English", untranslatedValues, false);
			}

			Console.WriteLine();
			if (issueCount == 0)
			{
				WriteSuccess("All locale catalogs are valid.");
				return 0;
			}

			WriteError($"Verification failed with {issueCount} issue(s).");
			return 1;
		}

		private static int Prune(Dictionary<string, string> englishCatalog, string[] localePaths)
		{
			if (englishCatalog.Count == 0)
			{
				WriteError("Error: en.json contains no keys. Refusing to prune every catalog.");
				return 2;
			}

			int totalRemoved = 0;

			foreach (string localePath in localePaths)
			{
				string locale = Path.GetFileNameWithoutExtension(localePath);
				Dictionary<string, string>? localeCatalog = LoadCatalog(localePath);

				if (localeCatalog == null)
				{
					return 2;
				}

				var orphanKeys = localeCatalog.Keys
					.Where(key => !englishCatalog.ContainsKey(key))
					.ToList();

				if (orphanKeys.Count == 0)
				{
					Console.WriteLine($"{locale}: nothing to prune");
					continue;
				}

				if (orphanKeys.Count * 2 > localeCatalog.Count)
				{
					WriteError($"Error: pruning {locale} would remove {orphanKeys.Count} of {localeCatalog.Count} keys. Verify en.json is complete before pruning again.");
					return 2;
				}

				foreach (string orphanKey in orphanKeys)
				{
					Console.WriteLine($"  {locale}: removed \"{orphanKey}\"");
					localeCatalog.Remove(orphanKey);
				}

				if (!SaveCatalog(localePath, localeCatalog))
				{
					return 2;
				}

				WriteSuccess($"{locale}: pruned {orphanKeys.Count} key(s)");
				totalRemoved += orphanKeys.Count;
			}

			Console.WriteLine();
			WriteSuccess($"Pruned {totalRemoved} orphan key(s) across {localePaths.Length} locale catalogs.");
			return 0;
		}

		private static int Coverage(Dictionary<string, string> englishCatalog, string[] localePaths, string localeDirectory)
		{
			var stillEnglish = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			var everyEnglishKey = new SortedSet<string>(StringComparer.Ordinal);

			foreach (string localePath in localePaths)
			{
				string locale = Path.GetFileNameWithoutExtension(localePath);
				Dictionary<string, string>? localeCatalog = LoadCatalog(localePath);
				if (localeCatalog == null)
				{
					return 2;
				}

				var proseKeys = new List<string>();
				foreach (var englishEntry in englishCatalog)
				{
					if (!LooksLikeProse(englishEntry.Value)) continue;
					if (!localeCatalog.TryGetValue(englishEntry.Key, out string? localizedText)) continue;
					if (!localizedText.Equals(englishEntry.Value, StringComparison.Ordinal)) continue;
					proseKeys.Add(englishEntry.Key);
					everyEnglishKey.Add(englishEntry.Key);
				}

				stillEnglish[locale] = proseKeys;
			}

			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine("Keys whose value is still the English prose, per catalog:");
			Console.ResetColor();

			int proseKeyCount = englishCatalog.Count(entry => LooksLikeProse(entry.Value));
			foreach (var entry in stillEnglish.OrderBy(entry => entry.Value.Count))
			{
				int translated = proseKeyCount - entry.Value.Count;
				string percentage = proseKeyCount == 0 ? "-" : (translated * 100 / proseKeyCount).ToString();
				Console.WriteLine($"  {entry.Key}: {entry.Value.Count,4} left  ({translated} of {proseKeyCount} translated, {percentage}%)");
			}

			if (everyEnglishKey.Count == 0)
			{
				Console.WriteLine();
				WriteSuccess("Every catalog translates every prose key.");
				return 0;
			}

			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine($"Where the {everyEnglishKey.Count} untranslated key(s) are authored:");
			Console.ResetColor();

			var sources = LoadAuthoringSources(localeDirectory);
			var countsBySource = new Dictionary<string, int>(StringComparer.Ordinal);
			var unattributed = new List<string>();

			foreach (string key in everyEnglishKey)
			{
				string source = Attribute(key, sources) ?? "(no source found)";
				countsBySource.TryGetValue(source, out int count);
				countsBySource[source] = count + 1;
				if (source.Equals("(no source found)", StringComparison.Ordinal)) unattributed.Add(key);
			}

			foreach (var entry in countsBySource.OrderByDescending(entry => entry.Value).Take(MaximumReportedSources))
			{
				Console.WriteLine($"  {entry.Value,4}  {DescribeSource(entry.Key)}");
			}

			if (countsBySource.Count > MaximumReportedSources)
			{
				Console.WriteLine($"  ... and {countsBySource.Count - MaximumReportedSources} more source(s)");
			}

			Console.WriteLine();
			Console.WriteLine($"No source found for {unattributed.Count} key(s): slugs, example values and node names collected from scenes.");
			return 0;
		}

		private static bool LooksLikeProse(string text)
		{
			return ProseWordRegex().Matches(text).Count >= MinimumProseWordCount;
		}

		private static Dictionary<string, string> LoadAuthoringSources(string localeDirectory)
		{
			var sources = new Dictionary<string, string>(StringComparer.Ordinal);
			string repositoryRoot = Path.GetFullPath(Path.Combine(localeDirectory, "..", ".."));
			if (!Directory.Exists(repositoryRoot)) return sources;

			foreach (string sourceFile in EnumerateSourceFiles(repositoryRoot))
			{
				sources[sourceFile] = File.ReadAllText(sourceFile);
			}

			return sources;
		}

		private static IEnumerable<string> EnumerateSourceFiles(string directory)
		{
			foreach (string pattern in new[] { "*.cs", "*.tscn" })
			{
				foreach (string file in EnumerateFilesSkippingGeneratedDirectories(directory, pattern)) yield return file;
			}
		}

		private static IEnumerable<string> EnumerateFilesSkippingGeneratedDirectories(string directory, string pattern)
		{
			string[] files;
			string[] directories;

			try
			{
				files = Directory.GetFiles(directory, pattern);
				directories = Directory.GetDirectories(directory);
			}
			catch (UnauthorizedAccessException)
			{
				yield break;
			}

			foreach (string file in files) yield return file;

			foreach (string subDirectory in directories)
			{
				string name = Path.GetFileName(subDirectory);
				if (UntranslatableDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
				foreach (string file in EnumerateFilesSkippingGeneratedDirectories(subDirectory, pattern)) yield return file;
			}
		}

		private static string? Attribute(string key, Dictionary<string, string> sources)
		{
			var runs = AttributionRunRegex().Matches(key)
				.Select(match => match.Value.Trim())
				.Where(run => run.Length >= MinimumAttributionRunLength)
				.OrderByDescending(run => run.Length);

			foreach (string run in runs)
			{
				string needle = run.Length > 40 ? run.Substring(0, 40) : run;
				foreach (var source in sources)
				{
					if (source.Value.Contains(needle, StringComparison.Ordinal)) return source.Key;
				}
			}

			return null;
		}

		private static string DescribeSource(string source)
		{
			if (source.Equals("(no source found)", StringComparison.Ordinal)) return source;

			string relative = source.Replace('\\', '/');
			int markerIndex = relative.LastIndexOf("/Realm.", StringComparison.Ordinal);
			if (markerIndex >= 0) relative = relative.Substring(markerIndex + 1);

			string area = relative.Contains("/UI/MapEditor", StringComparison.Ordinal) ? "map editor"
				: relative.Contains("/UI/InGame", StringComparison.Ordinal) ? "in-game"
				: relative.Contains("/Services", StringComparison.Ordinal) ? "services"
				: relative.Contains("/Core", StringComparison.Ordinal) ? "core input"
				: relative.Contains("/UI/", StringComparison.Ordinal) ? "menus and lobby"
				: "other";

			return $"{area,-16} {relative}";
		}

		private static int ReportIssues(string title, List<string> issues, bool isError)
		{
			if (issues.Count == 0)
			{
				Console.ForegroundColor = ConsoleColor.DarkGreen;
				Console.WriteLine($"  {title}: 0");
				Console.ResetColor();
				return 0;
			}

			Console.ForegroundColor = isError ? ConsoleColor.Red : ConsoleColor.Yellow;
			Console.WriteLine($"  {title}: {issues.Count}");
			Console.ResetColor();

			foreach (string issue in issues.Take(MaximumReportedEntries))
			{
				Console.WriteLine($"    {issue}");
			}

			if (issues.Count > MaximumReportedEntries)
			{
				Console.WriteLine($"    ... and {issues.Count - MaximumReportedEntries} more");
			}

			return isError ? issues.Count : 0;
		}

		private static void DescribeBrackets(string text, out string bbCodeSignature, out string hintSignature)
		{
			var bbCodeTags = new List<string>();
			var bracketHints = new List<string>();

			foreach (Match match in BracketTokenRegex().Matches(text))
			{
				string? bbCodeTag = NormalizeBbCodeTag(match.Value);
				if (bbCodeTag != null)
				{
					bbCodeTags.Add(bbCodeTag);
				}
				else
				{
					bracketHints.Add(match.Value);
				}
			}

			bbCodeTags.Sort(StringComparer.Ordinal);
			bracketHints.Sort(StringComparer.Ordinal);
			bbCodeSignature = string.Join(" ", bbCodeTags);
			hintSignature = string.Join(" ", bracketHints);
		}

		private static string? NormalizeBbCodeTag(string token)
		{
			string inner = token.Substring(1, token.Length - 2).Trim();
			bool isClosingTag = inner.StartsWith('/');
			if (isClosingTag)
			{
				inner = inner.Substring(1).Trim();
			}

			int attributeIndex = inner.IndexOf('=');
			if (attributeIndex >= 0)
			{
				inner = inner.Substring(0, attributeIndex).Trim();
			}

			string tagName = inner.ToLowerInvariant();
			if (!BbCodeTagNames.Contains(tagName))
			{
				return null;
			}

			return isClosingTag ? "/" + tagName : tagName;
		}

		private static string DescribeTokens(Regex regex, string text)
		{
			var tokens = regex.Matches(text)
				.Select(match => match.Value)
				.OrderBy(token => token, StringComparer.Ordinal);

			return string.Join(" ", tokens);
		}

		private static Dictionary<string, string>? LoadCatalog(string path)
		{
			if (!File.Exists(path))
			{
				WriteError($"Error: Catalog not found at: {path}");
				return null;
			}

			try
			{
				Dictionary<string, string>? catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
				if (catalog == null)
				{
					WriteError($"Error: Catalog deserialized to null: {path}");
				}

				return catalog;
			}
			catch (Exception exception)
			{
				WriteError($"Error: Failed to parse {path}: {exception.Message}");
				return null;
			}
		}

		private static bool SaveCatalog(string path, Dictionary<string, string> catalog)
		{
			try
			{
				string json = JsonSerializer.Serialize(catalog, SerializerOptions) + "\n";
				File.WriteAllText(path, json, CatalogEncoding);
				return true;
			}
			catch (Exception exception)
			{
				WriteError($"Error: Failed to write {path}: {exception.Message}");
				return false;
			}
		}

		private static string ResolveLocaleDirectory(string workingDirectory)
		{
			string localeDirectory = Path.GetFullPath(Path.Combine(workingDirectory, "Realm.Client", "locale"));
			if (!Directory.Exists(localeDirectory))
			{
				localeDirectory = Path.GetFullPath(Path.Combine(workingDirectory, "..", "Realm.Client", "locale"));
			}

			return localeDirectory;
		}

		private static void WriteError(string message)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(message);
			Console.ResetColor();
		}

		private static void WriteSuccess(string message)
		{
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine(message);
			Console.ResetColor();
		}
	}
}
