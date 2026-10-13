using System.Text.Json.Serialization;

namespace Realm.TranslationTool;

public class StorePageData
{
	[JsonPropertyName("itemid")] public string ItemId { get; set; } = "";

	[JsonPropertyName("languages")] public Dictionary<string, Dictionary<string, string>> Languages { get; set; } = new();
}