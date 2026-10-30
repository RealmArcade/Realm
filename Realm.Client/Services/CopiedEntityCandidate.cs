using Godot;

namespace Realm.Client.Services;

public readonly record struct CopiedEntityCandidate(Vector3 Position, string Type, string Id, float RotationY, float Scale, int Player);
