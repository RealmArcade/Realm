// Extracted from EcsWorld.cs (task 6 decomposition).
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs.Services;

namespace Realm.Ecs;

public partial class EcsWorld
{
	public Entity Create<T1>(in T1 c1) where T1 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		return entity;
	}

	public Entity Create<T1, T2>(in T1 c1, in T2 c2) where T1 : unmanaged where T2 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		return entity;
	}

	public Entity Create<T1, T2, T3>(in T1 c1, in T2 c2, in T3 c3) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4>(in T1 c1, in T2 c2, in T3 c3, in T4 c4) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10, in T11 c11) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		Add(entity, c11);
		return entity;
	}

	public Entity Create<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(in T1 c1, in T2 c2, in T3 c3, in T4 c4, in T5 c5, in T6 c6, in T7 c7, in T8 c8, in T9 c9, in T10 c10, in T11 c11, in T12 c12) where T1 : unmanaged where T2 : unmanaged where T3 : unmanaged where T4 : unmanaged where T5 : unmanaged where T6 : unmanaged where T7 : unmanaged where T8 : unmanaged where T9 : unmanaged where T10 : unmanaged where T11 : unmanaged where T12 : unmanaged
	{
		var entity = Create();
		Add(entity, c1);
		Add(entity, c2);
		Add(entity, c3);
		Add(entity, c4);
		Add(entity, c5);
		Add(entity, c6);
		Add(entity, c7);
		Add(entity, c8);
		Add(entity, c9);
		Add(entity, c10);
		Add(entity, c11);
		Add(entity, c12);
		return entity;
	}
}
