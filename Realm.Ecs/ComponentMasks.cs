using System.Runtime.CompilerServices;
using Realm.MapAPI;

namespace Realm.Ecs;

public static class ComponentMasks
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T>() where T : unmanaged => ComponentMask.FromId(ComponentType<T>.Id);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2>() where T1 : unmanaged where T2 : unmanaged => Of<T1>() | Of<T2>();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2, T3>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => Of<T1>() | Of<T2>() | Of<T3>();

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ComponentMask Of<T1, T2, T3, T4>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged => Of<T1>() | Of<T2>() | Of<T3>() | Of<T4>();
}
