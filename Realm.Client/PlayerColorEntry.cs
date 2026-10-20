using Godot;

namespace Realm.Client;

public readonly struct PlayerColorEntry
{
	public int Index { get; }
	public string Name { get; }
	public Color Color { get; }

	public PlayerColorEntry(int index, string name, Color color)
	{
		Index = index;
		Name = name;
		Color = color;
	}
}