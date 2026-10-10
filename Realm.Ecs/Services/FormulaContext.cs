using Arch.Core;

namespace Realm.Ecs.Services;

/// <summary>
/// Evaluation context for dynamic formula resolution against units, spell metadata, and dynamic properties.
/// </summary>
public struct FormulaContext
{
	public World? World;
	public Entity Caster;
	public Entity Target;
	public Dictionary<string, float>? SpellData;
	public Dictionary<string, float>? DynamicData;

	public FormulaContext(World? world, Entity caster = default, Entity target = default, Dictionary<string, float>? spellData = null, Dictionary<string, float>? dynamicData = null)
	{
		World = world;
		Caster = caster;
		Target = target;
		SpellData = spellData;
		DynamicData = dynamicData;
	}
}