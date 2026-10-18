using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Identifies the 3D visual mesh model path for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Model
{
	public int StringId;

	public Model(string path)
	{
		StringId = StringRegistry.GetOrAdd(path);
	}

	public string Path
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
