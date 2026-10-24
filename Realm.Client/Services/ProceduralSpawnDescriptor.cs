using Godot;

namespace Realm.Client.Services;

public enum ProceduralSpawnKind
{
	Decal,
	Vfx
}

public enum ProceduralSpawnLayer
{
	Water,
	Terrain
}

public readonly record struct ProceduralSpawnDescriptor(ProceduralSpawnKind Kind, string Id, Vector3 Position, float RotationYDegrees, float Scale);
