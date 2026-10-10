using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.Services;

public class DownloadedMapInfo
{
	public string Title { get; set; } = string.Empty;
	public string Author { get; set; } = "Unknown";
	public string Description { get; set; } = string.Empty;
	public List<string> Tags { get; set; } = new();
	public string Genre { get; set; } = "Custom Map";
	public string ThumbnailPath { get; set; } = string.Empty;
	public List<DownloadedMapVersionInfo> Versions { get; set; } = new();
	public string LatestVersion => Versions.Count > 0 ? Versions[0].Version : "1.0.0";
	public long TotalSizeBytes => Versions.Sum(v => v.TotalSizeBytes);
	public string FormattedTotalSize => MapStorageService.FormatBytes(TotalSizeBytes);
	public bool HasServerUpdate { get; set; }
	public string? ServerNewerVersion { get; set; }
	public string? ServerMapId { get; set; }
}