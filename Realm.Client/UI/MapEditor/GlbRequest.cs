using System;
using Godot;

namespace Realm.Client.UI.MapEditor;

public class GlbRequest
{
	public string FilePath { get; set; } = string.Empty;
	public DateTime LastModifiedUtc { get; set; }
	public string Blake3 { get; set; } = string.Empty;
	public Action<string, Texture2D>? Callback { get; set; }
	public bool IsHighPriority { get; set; }
}