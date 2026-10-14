using System;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class IndexedMapPackage
{
	public int Id { get; set; }
	public string MapName { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public string ManifestPath { get; set; } = string.Empty;
	public DateTime DownloadedUtc { get; set; }
	public List<string> AssetHashes { get; set; } = new();
	public bool IsP2P { get; set; }
}