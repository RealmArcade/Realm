using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PropIdentity
{
	public int StringId;

	public PropIdentity(string propId)
	{
		StringId = StringRegistry.GetOrAdd(propId);
	}

	public string PropId
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
