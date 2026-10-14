using System;

namespace Realm.Client.Services;

public class DownloadedMapVersionInfo
{
	public string Version { get; set; } = "1.0.0";
	public string ManifestHash { get; set; } = string.Empty;
	public string DirectoryPath { get; set; } = string.Empty;
	public string ManifestFilePath { get; set; } = string.Empty;
	public long TotalSizeBytes { get; set; }
	public string FormattedSize { get; set; } = "0 B";
	public DateTime LastModified { get; set; }
}