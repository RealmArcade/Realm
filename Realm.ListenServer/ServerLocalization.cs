using System.Text.Json;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The text a map's script asks the authority for. A script builds the strings it shows a screen, so the words have
///     to come from the authority in the language of the player who will read them: the base text of the game is read
///     from the folder the authority is installed with, the map's own text from the map folder, and each player's
///     language from the hello that player connected with.
/// </summary>
public class ServerLocalization
{
	/// <summary>The language a player who said nothing about one is answered in.</summary>
	public const string DefaultLanguage = "en";

	private readonly Dictionary<string, Dictionary<string, string>> _textByLanguage = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, string> _languageByPlayerSlot = new();

	public ServerLocalization(ServerMatchPaths paths, ServerLog log)
	{
		LoadDirectory(paths.BaseLocaleDirectory, log, "game text");
		LoadDirectory(paths.MapLocaleDirectory, log, "map text");
	}

	/// <summary>Records the language a player connected in, which every later answer for that player is written in.</summary>
	public void SetPlayerLanguage(int playerSlot, string? language)
	{
		if (playerSlot < 0) return;

		string normalized = Normalize(language);
		_languageByPlayerSlot[playerSlot] = normalized;
	}

	/// <summary>Forgets a player that left, so a slot that is seated again starts from the default language.</summary>
	public void ForgetPlayer(int playerSlot) => _languageByPlayerSlot.Remove(playerSlot);

	/// <summary>The language a player is answered in.</summary>
	public string GetPlayerLanguage(int playerSlot)
	{
		return _languageByPlayerSlot.TryGetValue(playerSlot, out string? language) ? language : DefaultLanguage;
	}

	/// <summary>
	///     What a key reads as in a player's language. A key the map never wrote a translation for reads as the key
	///     itself, which is how a screen is shown that the text is missing rather than being shown the wrong words.
	/// </summary>
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

	/// <summary>
	///     Reads the flat key-to-text files one folder holds, one per language, by the name of the file. Text loaded later
	///     wins, which is what lets a map's own words replace the game's words for the same key.
	/// </summary>
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
