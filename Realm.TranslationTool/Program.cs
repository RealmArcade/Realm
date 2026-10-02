using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Realm.TranslationTool
{
	class Program
	{
		private static readonly string BaseModel = "gemma4:12b-it-q4_K_M";
		private static readonly Dictionary<string, string> LanguageLocaleMap = new()
		{
			{ "Español", "es" },
			{ "Français", "fr" },
			{ "Deutsch", "de" },
			{ "Português", "pt" },
			{ "Русский", "ru" },
			{ "中文", "zh" },
			{ "日本語", "ja" },
			{ "العربية", "ar" },
			{ "हिन्दी", "hi" }
		};

		private static readonly Dictionary<string, string> StorePageLanguageKeyToNameMap = new()
		{
			{ "french", "French" },
			{ "italian", "Italian" },
			{ "german", "German" },
			{ "spanish", "Spanish (Spain)" },
			{ "bulgarian", "Bulgarian" },
			{ "czech", "Czech" },
			{ "danish", "Danish" },
			{ "dutch", "Dutch" },
			{ "finnish", "Finnish" },
			{ "greek", "Greek" },
			{ "hungarian", "Hungarian" },
			{ "indonesian", "Indonesian" },
			{ "japanese", "Japanese" },
			{ "koreana", "Korean" },
			{ "malay", "Malay" },
			{ "norwegian", "Norwegian" },
			{ "polish", "Polish" },
			{ "brazilian", "Portuguese (Brazil)" },
			{ "portuguese", "Portuguese (Portugal)" },
			{ "romanian", "Romanian" },
			{ "russian", "Russian" },
			{ "schinese", "Simplified Chinese" },
			{ "latam", "Spanish (Latin America)" },
			{ "swedish", "Swedish" },
			{ "thai", "Thai" },
			{ "tchinese", "Traditional Chinese" },
			{ "turkish", "Turkish" },
			{ "ukrainian", "Ukrainian" },
			{ "vietnamese", "Vietnamese" }
		};

		private static readonly OllamaClient _ollama = new OllamaClient();

		static async Task Main(string[] args)
		{
			string workingDir = Directory.GetCurrentDirectory();
			string cacheDir = Path.Combine(workingDir, ".translation_cache");
			if (!Directory.Exists(cacheDir))
			{
				Directory.CreateDirectory(cacheDir);
			}

			await _ollama.EnsureOllama();

			if (args.Length > 0 && args[0].Equals("--storepage", StringComparison.OrdinalIgnoreCase))
			{
				string storePageFile = args.Length > 1 ? args[1] : FindStorePageFile(workingDir);
				await ProcessStorePage(storePageFile, cacheDir);
				return;
			}

			if (args.Length > 0 && args[0].Equals("--all", StringComparison.OrdinalIgnoreCase))
			{
				ProcessGameLocalization(workingDir, cacheDir);
				string storePageFile = FindStorePageFile(workingDir);
				if (File.Exists(storePageFile))
				{
					await ProcessStorePage(storePageFile, cacheDir);
				}
				return;
			}

			if (args.Length > 0 && File.Exists(args[0]) && Path.GetExtension(args[0]).Equals(".json", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(args[0]).Contains("storepage", StringComparison.OrdinalIgnoreCase))
			{
				await ProcessStorePage(args[0], cacheDir);
				return;
			}

			ProcessGameLocalization(workingDir, cacheDir);

			string autoStorePageFile = FindStorePageFile(workingDir);
			if (File.Exists(autoStorePageFile))
			{
				await ProcessStorePage(autoStorePageFile, cacheDir);
			}
		}

		private static string FindStorePageFile(string workingDir)
		{
			string localPath = Path.GetFullPath(Path.Combine(workingDir, "storepage_1357037_all.json"));
			if (File.Exists(localPath))
			{
				return localPath;
			}

			string toolDirPath = Path.GetFullPath(Path.Combine(workingDir, "Realm.TranslationTool", "storepage_1357037_all.json"));
			if (File.Exists(toolDirPath))
			{
				return toolDirPath;
			}

			string parentDirPath = Path.GetFullPath(Path.Combine(workingDir, "..", "Realm.TranslationTool", "storepage_1357037_all.json"));
			if (File.Exists(parentDirPath))
			{
				return parentDirPath;
			}

			return localPath;
		}

		private static async Task ProcessStorePage(string storePageFilePath, string cacheDir)
		{
			if (!File.Exists(storePageFilePath))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: Store page file not found at: {storePageFilePath}");
				Console.ResetColor();
				return;
			}

			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.WriteLine($"=== Processing Store Page Translations: {storePageFilePath} ===");
			Console.ResetColor();

			string jsonContent = File.ReadAllText(storePageFilePath, Encoding.UTF8);
			using var doc = JsonDocument.Parse(jsonContent);
			var root = doc.RootElement;

			string itemId = root.TryGetProperty("itemid", out var itemIdProp) ? itemIdProp.GetString() ?? "" : "";
			if (!root.TryGetProperty("languages", out var languagesProp) || languagesProp.ValueKind != JsonValueKind.Object)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: Store page json missing 'languages' object.");
				Console.ResetColor();
				return;
			}

			if (!languagesProp.TryGetProperty("english", out var englishProp) || englishProp.ValueKind != JsonValueKind.Object)
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Error: Store page json missing 'languages.english' source object.");
				Console.ResetColor();
				return;
			}

			var englishDict = new Dictionary<string, string>();
			foreach (var prop in englishProp.EnumerateObject())
			{
				englishDict[prop.Name] = prop.Value.GetString() ?? "";
			}

			var languagesResult = new Dictionary<string, Dictionary<string, string>>();
			languagesResult["english"] = englishDict;

			bool anyModified = false;

			foreach (var langProp in languagesProp.EnumerateObject())
			{
				string langKey = langProp.Name;
				if (langKey.Equals("english", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string targetLanguageName = StorePageLanguageKeyToNameMap.TryGetValue(langKey, out var mappedName) ? mappedName : langKey;

				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"--- Processing Store Page Language: {langKey} ({targetLanguageName}) ---");
				Console.ResetColor();

				var currentTargetDict = new Dictionary<string, string>();
				if (langProp.Value.ValueKind == JsonValueKind.Object)
				{
					foreach (var p in langProp.Value.EnumerateObject())
					{
						currentTargetDict[p.Name] = p.Value.GetString() ?? "";
					}
				}

				foreach (var englishKvp in englishDict)
				{
					string key = englishKvp.Key;
					string englishText = englishKvp.Value;

					if (currentTargetDict.TryGetValue(key, out var existingValue) && !string.IsNullOrWhiteSpace(existingValue))
					{
						continue;
					}

					string context = GetStorePageContextForKey(key);
					string translatedText = await TranslateStorePageLine(englishText, targetLanguageName, langKey, context, cacheDir);
					currentTargetDict[key] = translatedText;
					anyModified = true;
				}

				languagesResult[langKey] = currentTargetDict;
			}

			if (anyModified)
			{
				var outputStorePage = new StorePageData
				{
					ItemId = itemId,
					Languages = languagesResult
				};

				var serializeOptions = new JsonSerializerOptions
				{
					WriteIndented = false,
					Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				};

				string outputJson = JsonSerializer.Serialize(outputStorePage, serializeOptions);
				File.WriteAllText(storePageFilePath, outputJson, Encoding.UTF8);

				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"Saved updated store page file: {storePageFilePath}");
				Console.ResetColor();
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.WriteLine($"Store page file is already up to date: {storePageFilePath}");
				Console.ResetColor();
			}
		}

		private static string GetStorePageContextForKey(string key)
		{
			if (key.Contains("earlyaccess", StringComparison.OrdinalIgnoreCase))
			{
				return "Steam store page Early Access description section for an open-source tactical RTS arcade game";
			}

			if (key.Contains("about", StringComparison.OrdinalIgnoreCase))
			{
				return "Steam store page 'About This Game' description section with BBCode tags (such as [p], [/p], [b], [/b], [h2], [/h2], [list], [*]) for an open-source tactical RTS arcade game";
			}

			if (key.Contains("short_description", StringComparison.OrdinalIgnoreCase))
			{
				return "Steam store page short description for an open-source tactical RTS arcade game";
			}

			if (key.Contains("sysreqs", StringComparison.OrdinalIgnoreCase))
			{
				return "Steam store page PC system requirements specification";
			}

			return "Steam store page content for an open-source tactical RTS arcade game";
		}

		private static async void ProcessGameLocalization(string workingDir, string cacheDir)
		{
			string godotLocaleDir = Path.GetFullPath(Path.Combine(workingDir, "Realm.Godot", "locale"));
			if (!Directory.Exists(godotLocaleDir))
			{
				godotLocaleDir = Path.GetFullPath(Path.Combine(workingDir, "..", "Realm.Godot", "locale"));
			}

			if (!Directory.Exists(godotLocaleDir))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: Locale directory not found at: {godotLocaleDir}");
				Console.ResetColor();
				return;
			}

			string enFilePath = Path.Combine(godotLocaleDir, "en.json");
			if (!File.Exists(enFilePath))
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Error: English source file en.json not found at: {enFilePath}");
				Console.ResetColor();
				return;
			}

			string enJson = File.ReadAllText(enFilePath, Encoding.UTF8);
			var sourceStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(enJson);
			if (sourceStrings == null)
			{
				Console.WriteLine("Error: Failed to deserialize en.json");
				return;
			}

			foreach (var kvp in LanguageLocaleMap)
			{
				string languageName = kvp.Key;
				string locale = kvp.Value;

				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine($"--- Processing Language: {languageName} ({locale}) ---");
				Console.ResetColor();

				string outFilePath = Path.Combine(godotLocaleDir, $"{locale}.json");
				Dictionary<string, string> targetStrings = new();
				if (File.Exists(outFilePath))
				{
					try
					{
						string existingContent = File.ReadAllText(outFilePath, Encoding.UTF8);
						var existingDict = JsonSerializer.Deserialize<Dictionary<string, string>>(existingContent);
						if (existingDict != null)
						{
							targetStrings = existingDict;
						}
					}
					catch
					{
					}
				}

				bool modified = false;
				foreach (var sourceKvp in sourceStrings)
				{
					string key = sourceKvp.Key;
					string englishText = sourceKvp.Value;

					if (targetStrings.ContainsKey(key) && !string.IsNullOrWhiteSpace(targetStrings[key]))
					{
						continue;
					}

					string translatedText = await TranslateLine(englishText, languageName, locale, "RTS game user interface string", cacheDir);
					targetStrings[key] = translatedText;
					modified = true;
				}

				if (modified || !File.Exists(outFilePath))
				{
					var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
					string outputJson = JsonSerializer.Serialize(targetStrings, options);
					File.WriteAllText(outFilePath, outputJson, Encoding.UTF8);
					Console.ForegroundColor = ConsoleColor.Green;
					Console.WriteLine($"Saved: {locale}.json");
					Console.ResetColor();
				}
			}

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine("\nAll game translations completed successfully.");
			Console.ResetColor();
		}

		private static async Task<string> TranslateStorePageLine(string text, string targetLanguage, string locale, string context, string cacheDirectory)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return text;
			}

			string stringHash = GetStringHash($"{text}-{context}");
			string cacheFile = Path.Combine(cacheDirectory, $"storepage_{locale}_{stringHash}.json");

			if (!File.Exists(cacheFile))
			{
				string preview = text.Length > 60 ? text.Substring(0, 60) + "..." : text;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"Translating store page line ({locale}): '{preview}' to {targetLanguage}...");
				Console.ResetColor();

				string prompt = $"Translate the following text from English to {targetLanguage}.\n" +
								$"Maintain the correct style and formatting for a Steam store page for a real-time strategy (RTS) video game.\n" +
								$"Preserve all BBCode tags intact exactly as formatted (for example: [p], [/p], [b], [/b], [h2], [/h2], [list], [*], [/*]). Do not remove or alter tags.\n" +
								$"Output ONLY the translated text, do not include outer quotes, markdown code blocks, explanations, or any preamble.\n" +
								$"Context: {context}\n" +
								$"Text:\n{text}";

				string response = await _ollama.GenerateText(prompt);
				string translatedText = response.Trim();

				if (translatedText.StartsWith("```") && translatedText.EndsWith("```"))
				{
					int firstNewline = translatedText.IndexOf('\n');
					int lastBackticks = translatedText.LastIndexOf("```", StringComparison.Ordinal);
					if (firstNewline != -1 && lastBackticks > firstNewline)
					{
						translatedText = translatedText.Substring(firstNewline + 1, lastBackticks - firstNewline - 1).Trim();
					}
				}

				if (translatedText.StartsWith("\"") && translatedText.EndsWith("\"") && translatedText.Length >= 2)
				{
					translatedText = translatedText.Substring(1, translatedText.Length - 2);
				}

				var cacheObj = new CacheEntry
				{
					Original = text,
					TranslatedText = translatedText,
					Locale = locale,
					Context = context,
					Timestamp = DateTime.Now.ToString()
				};

				string cacheJson = JsonSerializer.Serialize(cacheObj, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(cacheFile, cacheJson, Encoding.UTF8);
			}

			try
			{
				string cacheContent = File.ReadAllText(cacheFile, Encoding.UTF8);
				var cacheEntry = JsonSerializer.Deserialize<CacheEntry>(cacheContent);
				if (cacheEntry != null)
				{
					if (cacheEntry.Original.Trim().Replace(" ", "") != text.Trim().Replace(" ", ""))
					{
						throw new Exception("Source mismatch");
					}

					return cacheEntry.TranslatedText;
				}
			}
			catch
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Warning: Cache file {cacheFile} is corrupted. Deleting.");
				Console.ResetColor();
				try
				{
					File.Delete(cacheFile);
				}
				catch { }
			}

			return text;
		}

		private static async Task<string> TranslateLine(string text, string targetLanguage, string locale, string context, string cacheDirectory)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return text;
			}

			string stringHash = GetStringHash($"{text}-{context}");
			string cacheFile = Path.Combine(cacheDirectory, $"{locale}_{stringHash}.json");

			if (!File.Exists(cacheFile))
			{
				string preview = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"Translating: '{preview}' to {targetLanguage}...");
				Console.ResetColor();

				string prompt = $"Translate the following text from English to {targetLanguage}.\n" +
								$"Maintain the correct context for a real-time strategy (RTS) video game.\n" +
								$"Output ONLY the translated text, do not include quotes, explanations, or any preamble.\n" +
								$"Context: {context}\n" +
								$"Text: \"{text}\"";

				string response = await _ollama.GenerateText(prompt);
				string translatedText = response.Trim().Replace("\"", "");

				var cacheObj = new CacheEntry
				{
					Original = text,
					TranslatedText = translatedText,
					Locale = locale,
					Context = context,
					Timestamp = DateTime.Now.ToString()
				};

				string cacheJson = JsonSerializer.Serialize(cacheObj, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(cacheFile, cacheJson, Encoding.UTF8);
			}

			try
			{
				string cacheContent = File.ReadAllText(cacheFile, Encoding.UTF8);
				var cacheEntry = JsonSerializer.Deserialize<CacheEntry>(cacheContent);
				if (cacheEntry != null)
				{
					if (cacheEntry.Original.Trim().Replace(" ", "") != text.Trim().Replace(" ", ""))
					{
						throw new Exception("Source mismatch");
					}

					return cacheEntry.TranslatedText;
				}
			}
			catch
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"Warning: Cache file {cacheFile} is corrupted. Deleting.");
				Console.ResetColor();
				try
				{
					File.Delete(cacheFile);
				}
				catch { }
			}

			return text;
		}

		private static string GetStringHash(string input)
		{
			using (var sha256 = SHA256.Create())
			{
				byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
				var builder = new StringBuilder();
				foreach (var b in bytes)
				{
					builder.Append(b.ToString("X2"));
				}
				return builder.ToString();
			}
		}

		private class StorePageData
		{
			[System.Text.Json.Serialization.JsonPropertyName("itemid")]
			public string ItemId { get; set; } = "";

			[System.Text.Json.Serialization.JsonPropertyName("languages")]
			public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new();
		}

		private class CacheEntry
		{
			public string Original { get; set; } = "";
			public string TranslatedText { get; set; } = "";
			public string Locale { get; set; } = "";
			public string Context { get; set; } = "";
			public string Timestamp { get; set; } = "";
		}
	}
}

