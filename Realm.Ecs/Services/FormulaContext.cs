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
	public string WASMFunctionName;

	public FormulaContext(World? world, Entity caster = default, Entity target = default, string wasmFunctionName = "")
	{
		World = world;
		Caster = caster;
		Target = target;
		WASMFunctionName = wasmFunctionName;
	}
}