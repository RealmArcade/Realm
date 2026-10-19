using System;

namespace Realm.Ecs.Components.Core;

[Flags]
public enum PlayerRelationship : byte
{
	Neutral = 0,
	Ally = 1 << 0,
	Enemy = 1 << 1
}
