using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     A component that modifies a stat on an entity.
/// </summary>
public record struct StatModifier(
	StatId StatTypeId,
	ModifierType Type,
	float Value,
	float Duration = -1f,
	string Formula = "",
	int SourceLevel = 1,
	string SourceUpgradeId = "")
{
	public StatModifier(StatId statTypeId, ModifierType type, float value, float duration)
		: this(statTypeId, type, value, duration, string.Empty, 1, string.Empty)
	{
	}
}