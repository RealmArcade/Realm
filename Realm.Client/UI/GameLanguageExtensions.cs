using System;
using System.Collections.Generic;

namespace Realm.Client.UI;

public static class GameLanguageExtensions
{
	private static readonly string[] _localeCodes = { "en", "es", "fr", "de", "pt", "ru", "zh", "ja", "ar", "hi" };
	private static readonly Dictionary<string, GameLanguage> _languageMap = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "en", GameLanguage.English }, { "english", GameLanguage.English },
		{ "es", GameLanguage.Spanish }, { "spanish", GameLanguage.Spanish },
		{ "fr", GameLanguage.French }, { "french", GameLanguage.French },
		{ "de", GameLanguage.German }, { "german", GameLanguage.German },
		{ "pt", GameLanguage.Portuguese }, { "portuguese", GameLanguage.Portuguese },
		{ "ru", GameLanguage.Russian }, { "russian", GameLanguage.Russian },
		{ "zh", GameLanguage.Chinese }, { "chinese", GameLanguage.Chinese },
		{ "ja", GameLanguage.Japanese }, { "japanese", GameLanguage.Japanese },
		{ "ar", GameLanguage.Arabic }, { "arabic", GameLanguage.Arabic },
		{ "hi", GameLanguage.Hindi }, { "hindi", GameLanguage.Hindi }
	};

	public static string ToLocaleCode(this GameLanguage language)
	{
		int index = (int)language;
		if (index >= 0 && index < _localeCodes.Length)
			return _localeCodes[index];
		return "en";
	}

	public static GameLanguage ParseGameLanguage(string code)
	{
		if (string.IsNullOrEmpty(code)) return GameLanguage.English;
		return _languageMap.TryGetValue(code, out var lang) ? lang : GameLanguage.English;
	}
}