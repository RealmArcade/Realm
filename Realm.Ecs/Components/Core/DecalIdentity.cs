using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DecalIdentity
{
	public int StringId;

	public DecalIdentity(string decalId)
	{
		StringId = StringRegistry.GetOrAdd(decalId);
	}

	public string DecalId
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
