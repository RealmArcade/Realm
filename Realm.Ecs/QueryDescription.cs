using System.Runtime.InteropServices;

namespace Realm.Ecs;

/// <summary>
/// Description of a query filter containing required and excluded component masks.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct QueryDescription
{
	public readonly ComponentMask All;
	public readonly ComponentMask None;

	public QueryDescription(ComponentMask all, ComponentMask none = default)
	{
		All = all;
		None = none;
	}

	public QueryDescription WithAll<T>() where T : unmanaged => new QueryDescription(All | ComponentMasks.Of<T>(), None);
	public QueryDescription WithAll<T1, T2>() where T1 : unmanaged where T2 : unmanaged => new QueryDescription(All | ComponentMasks.Of<T1, T2>(), None);
	public QueryDescription WithAll<T1, T2, T3>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => new QueryDescription(All | ComponentMasks.Of<T1, T2, T3>(), None);
	public QueryDescription WithAll<T1, T2, T3, T4>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged => new QueryDescription(All | ComponentMasks.Of<T1, T2, T3, T4>(), None);

	public QueryDescription WithNone<T>() where T : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T>());
	public QueryDescription WithNone<T1, T2>() where T1 : unmanaged where T2 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2>());
	public QueryDescription WithNone<T1, T2, T3>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3>());
	public QueryDescription WithNone<T1, T2, T3, T4>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3, T4>());
	public QueryDescription WithNone<T1, T2, T3, T4, T5>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3, T4>() | ComponentMasks.Of<T5>());
	public QueryDescription WithNone<T1, T2, T3, T4, T5, T6>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3, T4>() | ComponentMasks.Of<T5, T6>());
	public QueryDescription WithNone<T1, T2, T3, T4, T5, T6, T7>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3, T4>() | ComponentMasks.Of<T5, T6, T7>());
	public QueryDescription WithNone<T1, T2, T3, T4, T5, T6, T7, T8>() where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged => new QueryDescription(All, None | ComponentMasks.Of<T1, T2, T3, T4>() | ComponentMasks.Of<T5, T6, T7>() | ComponentMasks.Of<T8>());

	public static implicit operator QueryDescription(ComponentMask mask) => new QueryDescription(mask);
}
