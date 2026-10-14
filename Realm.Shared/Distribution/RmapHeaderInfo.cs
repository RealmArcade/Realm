namespace Realm.Shared.Distribution;

public class RmapHeaderInfo
{
	public string MapName { get; set; } = string.Empty;
	public string Version { get; set; } = "1.0.0";
	public string GameBuildNumber { get; set; } = string.Empty;
	public string Author { get; set; } = "Unknown";
	public string Description { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = new();
}