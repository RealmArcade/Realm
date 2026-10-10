using Godot;
using System.Collections.Generic;

namespace Realm.Client.UI.MapEditor;

public class AnimatedThumbnail
{
	public List<Texture2D> Frames { get; set; } = new();
	public float Fps { get; set; } = 6.0f;
	public Texture2D? PrimaryFrame => Frames.Count > 0 ? Frames[0] : null;
}