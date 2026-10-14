namespace Realm.TranslationTool;

public class CacheEntry
{
	public string Original { get; set; } = "";
	public string TranslatedText { get; set; } = "";
	public string Locale { get; set; } = "";
	public string Context { get; set; } = "";
	public string Timestamp { get; set; } = "";
}