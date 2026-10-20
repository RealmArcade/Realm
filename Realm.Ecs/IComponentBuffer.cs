using System;

namespace Realm.Ecs;

public interface IComponentBuffer
{
	int ComponentId { get; }
	void Clear(int entityId);
	void BindExternalMemory(IntPtr pointer, int lengthInBytes);
	void BindExternalMemory(IntPtr pointer, int lengthInBytes, int capacity);
}
