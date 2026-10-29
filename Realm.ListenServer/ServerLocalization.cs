using System.Text.Json;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public class ServerLocalization
{
	public const string DefaultLanguage = "en";

	private readonly Dictionary<string, Dictionary<string, string>> _textByLanguage = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, string> _languageByPlayerSlot = new();

	public ServerLocalization(ServerMatchPaths paths, ServerLog log)
	{
		LoadDirectory(paths.BaseLocaleDirectory, log, "game text");
		LoadDirectory(paths.MapLocaleDirectory, log, "map text");
	}

	public void SetPlayerLanguage(int playerSlot, string? language)
	{
		if (playerSlot < 0) return;

		string normalized = Normalize(language);
		_languageByPlayerSlot[playerSlot] = normalized;
	}

	public void ForgetPlayer(int playerSlot) => _languageByPlayerSlot.Remove(playerSlot);

	public string GetPlayerLanguage(int playerSlot)
	{
		return _languageByPlayerSlot.TryGetValue(playerSlot, out string? language) ? language : DefaultLanguage;
	}

	public string Translate(string key, int playerSlot)
	{
		if (string.IsNullOrEmpty(key)) return string.Empty;

		string language = GetPlayerLanguage(playerSlot);
		if (TryLookup(language, key, out string text)) return text;
		if (language != DefaultLanguage && TryLookup(DefaultLanguage, key, out text)) return text;

		return key;
	}

	private bool TryLookup(string language, string key, out string text)
	{
		text = string.Empty;
		if (!_textByLanguage.TryGetValue(language, out Dictionary<string, string>? entries)) return false;

		return entries.TryGetValue(key, out text!);
	}

	private void LoadDirectory(string directory, ServerLog log, string what)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
		{
			string language = Normalize(Path.GetFileNameWithoutExtension(file));
			if (language.Length == 0) continue;

			try
			{
				Dictionary<string, string>? entries = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
				if (entries == null || entries.Count == 0) continue;

				if (!_textByLanguage.TryGetValue(language, out Dictionary<string, string>? existing))
				{
					existing = new Dictionary<string, string>(StringComparer.Ordinal);
					_textByLanguage[language] = existing;
				}

				foreach (var entry in entries)
				{
					if (entry.Value == null) continue;

					existing[entry.Key] = entry.Value;
				}

				log.Write($"loaded {entries.Count} {what} entries for '{language}' from '{file}'");
			}
			catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
			{
				log.Write($"could not read {what} '{file}': {exception.Message}");
			}
		}
	}

	private static string Normalize(string? language)
	{
		if (string.IsNullOrWhiteSpace(language)) return DefaultLanguage;

		ReadOnlySpan<char> trimmed = language.AsSpan().Trim();
		int separator = trimmed.IndexOfAny('-', '_');
		if (separator > 0) trimmed = trimmed[..separator];

		return trimmed.ToString().ToLowerInvariant();
	}
}
