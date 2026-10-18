using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the unit production and training queue state for a production building.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ProductionQueue
{
	public int Handle;
	public float CurrentProgress;
	public float BuildTime;

	public ProductionQueue()
	{
		Handle = ColdPayloadRegistry<List<string>>.Register(new List<string>());
		CurrentProgress = 0f;
		BuildTime = 10f;
	}

	public List<string> UnitIds
	{
		get => ColdPayloadRegistry<List<string>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<List<string>>.Register(value);
	}
}
