using System;
using System.Collections.Generic;

namespace Realm.Client.Services;

public class IndexedAsset
{
	public int Id { get; set; }
	public string FilePath { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public string Extension { get; set; } = string.Empty;
	public string DirectoryPath { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
	public DateTime LastModifiedUtc { get; set; }
	public List<string> Tags { get; set; } = new();
	public string MetadataJson { get; set; } = string.Empty;
	public bool HasRealmMetadata { get; set; }
	public string? AssetType { get; set; }
	public string? MapName { get; set; }
	public string? MapVersion { get; set; }
	public string? Blake3 { get; set; }
	public bool HasPlayerColorMask { get; set; }
	public string? ChromaKey { get; set; }
}