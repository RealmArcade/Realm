using System;

namespace Realm.Client.UI.MapEditor;

public class ImageThumbnailRequest
{
	public string FilePath { get; set; } = string.Empty;
	public DateTime LastModifiedUtc { get; set; }
	public string? Blake3 { get; set; }
	public bool IsHighPriority { get; set; }
}