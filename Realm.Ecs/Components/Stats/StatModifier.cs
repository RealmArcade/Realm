using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     A component that modifies a stat on an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct StatModifier
{
	public StatId StatTypeId;
	public ModifierType Type;
	public float Value;
	public float Duration;
	public int WasmFuncStringId;
	public int SourceLevel;
	public int SourceUpgradeStringId;

	public StatModifier(
		StatId statTypeId,
		ModifierType type,
		float value,
		float duration = -1f,
		string wasmFunctionName = "",
		int sourceLevel = 1,
		string sourceUpgradeId = "")
	{
		StatTypeId = statTypeId;
		Type = type;
		Value = value;
		Duration = duration;
		WasmFuncStringId = StringRegistry.GetOrAdd(wasmFunctionName);
		SourceLevel = sourceLevel;
		SourceUpgradeStringId = StringRegistry.GetOrAdd(sourceUpgradeId);
	}

	public StatModifier(StatId statTypeId, ModifierType type, float value, float duration)
		: this(statTypeId, type, value, duration, string.Empty, 1, string.Empty)
	{
	}

	public string WASMFunctionName
	{
		get => StringRegistry.Get(WasmFuncStringId);
		set => WasmFuncStringId = StringRegistry.GetOrAdd(value);
	}

	public string SourceUpgradeId
	{
		get => StringRegistry.Get(SourceUpgradeStringId);
		set => SourceUpgradeStringId = StringRegistry.GetOrAdd(value);
	}
}
