using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Icon
{
	public int StringId;

	public Icon(string path)
	{
		StringId = StringRegistry.GetOrAdd(path);
	}

	public string Path
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
