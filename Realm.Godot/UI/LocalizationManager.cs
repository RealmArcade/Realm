using System;
using Godot;
using System.Collections.Generic;
using System.Text.Json;

public static class LocalizationManager
{
	private static readonly string[] Locales = { "en", "es", "fr", "de", "pt", "ru", "zh", "ja", "ar", "hi" };

	public static string CurrentMapName { get; set; } = "";

	public static void SetupTranslations()
	{
		foreach (var locale in Locales)
		{
			var mergedDict = new Dictionary<string, string>();

			LoadBaseTranslations(locale, mergedDict);
			LoadMapTranslations(locale, mergedDict);

			if (mergedDict.Count == 0) continue;

			var translation = new Translation();
			translation.Locale = locale;
			foreach (var kvp in mergedDict)
			{
				translation.AddMessage(kvp.Key, kvp.Value);
			}
			TranslationServer.AddTranslation(translation);
		}

		UpdateLocale(GameSettings.Language);
	}

	private static void LoadMapTranslations(string locale, Dictionary<string, string> mergedDict)
	{
		if (string.IsNullOrEmpty(CurrentMapName)) return;

		string? resolvedDir = GameHost.ResolveMapDirectory(CurrentMapName);
		if (!string.IsNullOrEmpty(resolvedDir))
		{
			string localePath = System.IO.Path.Combine(resolvedDir, "locale", $"{locale}.json");
			if (!System.IO.File.Exists(localePath)) return;

			string content = System.IO.File.ReadAllText(localePath);
			LoadJsonIntoDictionary(content, mergedDict, $"Failed to load map translation for {locale} in {CurrentMapName}");
			return;
		}

		string mapPath = (CurrentMapName.StartsWith("user://") || CurrentMapName.StartsWith("res://"))
			? $"{CurrentMapName.TrimEnd('/')}/locale/{locale}.json"
			: $"res://Maps/{CurrentMapName}/locale/{locale}.json";
		
		if (!FileAccess.FileExists(mapPath)) return;

		using var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
		if (file == null) return;

		LoadJsonIntoDictionary(file.GetAsText(), mergedDict, $"Failed to load map translation for {locale} in {CurrentMapName}");
	}

	private static void LoadBaseTranslations(string locale, Dictionary<string, string> mergedDict)
	{
		string basePath = $"res://locale/{locale}.json";
		if (!FileAccess.FileExists(basePath)) return;

		using var file = FileAccess.Open(basePath, FileAccess.ModeFlags.Read);
		if (file == null) return;

		LoadJsonIntoDictionary(file.GetAsText(), mergedDict, $"Failed to load base translation for {locale}");
	}

	private static void LoadJsonIntoDictionary(string content, Dictionary<string, string> mergedDict, string errorContext)
	{
		try
		{
			var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
			if (dict == null) return;

			foreach (var kvp in dict)
			{
				mergedDict[kvp.Key] = kvp.Value;
			}
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"{errorContext}: {e.Message}");
		}
	}

	public static event Action<GameLanguage> LanguageChanged;

	public static void UpdateLocale(GameLanguage language)
	{
		UpdateLocale(language.ToLocaleCode());
		LanguageChanged?.Invoke(language);
	}

	public static void UpdateLocale(string locale)
	{
		TranslationServer.SetLocale(locale);
		if (UIManager.Instance != null)
		{
			UIManager.Instance.LayoutDirection = IsLocaleRtl(locale)
				? Control.LayoutDirectionEnum.Rtl
				: Control.LayoutDirectionEnum.Ltr;
		}
	}

	public static Dictionary<string, string> GetDictionary(string locale)
	{
		var dict = new Dictionary<string, string>();
		string basePath = $"res://locale/{locale}.json";
		if (FileAccess.FileExists(basePath))
		{
			using var file = FileAccess.Open(basePath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				try
				{
					var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
					if (parsed != null)
					{
						foreach (var kvp in parsed) dict[kvp.Key] = kvp.Value;
					}
				}
				catch { }
			}
		}
		if (dict.Count == 0 && locale != "en")
		{
			return GetDictionary("en");
		}
		return dict;
	}

	private static readonly object SyncLock = new object();
	private static Dictionary<string, string>? _enCatalog;
	private static bool _isDirty;
	private static System.Threading.Timer? _debounceTimer;
	private static bool _initializedProcessExit;

	public static bool IsLocaleRtl(string locale)
	{
		return locale == "ar";
	}

	public static string GetCurrentLanguageCode()
	{
		return TranslationServer.GetLocale();
	}

	public static string Translate(string key, string fallback = "")
	{
		return TranslateKey(key, fallback);
	}

	public static string TranslateKey(string key, string fallback = "")
	{
		if (string.IsNullOrEmpty(key)) return "";
		string translated = TranslationServer.Translate(key);
		if (!string.IsNullOrEmpty(translated) && translated != key)
		{
			return translated;
		}
		if (!OS.IsDebugBuild())
		{
			var enDict = GetDictionary("en");
			if (enDict != null && enDict.TryGetValue(key, out var enVal))
				return enVal;
			return !string.IsNullOrEmpty(fallback) ? fallback : key;
		}
		return TranslateKeyDebug(key, fallback);
	}

	private static string TranslateKeyDebug(string key, string fallback)
	{
		EnsureEnCatalogLoaded();
		lock (SyncLock)
		{
			if (_enCatalog!.TryGetValue(key, out var enVal))
			{
				return enVal;
			}

			string recordVal = !string.IsNullOrEmpty(fallback) ? fallback : key;
			_enCatalog[key] = recordVal;
			_isDirty = true;
			ScheduleDebouncedFlush();
			return recordVal;
		}
	}

	private static void EnsureEnCatalogLoaded()
	{
		if (_enCatalog != null) return;
		lock (SyncLock)
		{
			if (_enCatalog != null) return;
			_enCatalog = GetDictionary("en") ?? new Dictionary<string, string>();
			if (!_initializedProcessExit)
			{
				_initializedProcessExit = true;
				AppDomain.CurrentDomain.ProcessExit += (s, e) => FlushPendingWrites();
			}
		}
	}

	private static void ScheduleDebouncedFlush()
	{
		lock (SyncLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = new System.Threading.Timer(_ => FlushPendingWrites(), null, 1000, System.Threading.Timeout.Infinite);
		}
	}

	public static void FlushPendingWrites()
	{
		if (!OS.IsDebugBuild() || !_isDirty || _enCatalog == null) return;

		lock (SyncLock)
		{
			if (!_isDirty || _enCatalog == null) return;
			_isDirty = false;
			_debounceTimer?.Dispose();
			_debounceTimer = null;

			try
			{
				string filePath = GetEnJsonDiskPath();
				if (string.IsNullOrEmpty(filePath)) return;

				string? dir = System.IO.Path.GetDirectoryName(filePath);
				if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
				{
					System.IO.Directory.CreateDirectory(dir);
				}

				var options = new JsonSerializerOptions
				{
					WriteIndented = true,
					Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
				};
				string json = JsonSerializer.Serialize(_enCatalog, options);
				System.IO.File.WriteAllText(filePath, json);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to flush en.json: {ex.Message}");
			}
		}
	}

	private static string GetEnJsonDiskPath()
	{
		try
		{
			string resPath = ProjectSettings.GlobalizePath("res://locale/en.json");
			if (!string.IsNullOrEmpty(resPath) && resPath != "res://locale/en.json")
			{
				return resPath;
			}
		}
		catch
		{
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string localPath = System.IO.Path.Combine(baseDir, "locale", "en.json");
		if (System.IO.File.Exists(localPath)) return localPath;

		string repoPath = System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "Realm.Godot", "locale", "en.json");
		if (System.IO.File.Exists(repoPath)) return System.IO.Path.GetFullPath(repoPath);

		return localPath;
	}
}
