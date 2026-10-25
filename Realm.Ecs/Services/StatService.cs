using Realm.Ecs.Common;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Services;

/// <summary>
///     Reads the stat snapshot that the derived stat pipeline writes. Modifiers are already folded
///     into these values, so a read never re-applies them.
/// </summary>
public class StatService
{
	/// <summary>
	///     Gets the current value of a stat. O(1), allocation free, and consistent with the value
	///     gameplay uses because both come from the same snapshot.
	/// </summary>
	public float GetStatValue(Entity entity, StatId statId)
	{
		if (ECSContext.World == null || !ECSContext.World.Has<Stats>(entity)) return 0f;

		ref var stats = ref ECSContext.World.Get<Stats>(entity);
		return stats.TryGetValue(statId, out var value) ? value : 0f;
	}
}
