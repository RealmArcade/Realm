using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Realm.Ecs;

/// <summary>
/// Provides zero-allocation enumeration over entities matching an ECS query.
/// </summary>
public readonly ref struct QueryEnumerable
{
	private readonly EcsWorld _world;
	private readonly QueryDescription _query;

	public QueryEnumerable(EcsWorld world, QueryDescription query)
	{
		_world = world;
		_query = query;
	}

	public QueryEnumerator GetEnumerator() => new QueryEnumerator(_world, _query);
}

/// <summary>
/// Enumerator yielding entities that match an ECS query chunk by chunk using bit manipulation.
/// </summary>
public ref struct QueryEnumerator
{
	private readonly EcsWorld _world;
	private readonly QueryDescription _query;
	private int _chunkIndex;
	private ulong _currentMatch;
	private Entity _current;

	public QueryEnumerator(EcsWorld world, QueryDescription query)
	{
		_world = world;
		_query = query;
		_chunkIndex = 0;
		_currentMatch = 0;
		_current = Entity.Null;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool MoveNext()
	{
		while (_currentMatch == 0)
		{
			if (_chunkIndex >= EcsWorld.BITSET_CHUNKS) return false;
			_currentMatch = _world.GetMatchingChunk(_query, _chunkIndex);
			_chunkIndex++;
			if (_currentMatch == 0) continue;
		}

		int bit = BitOperations.TrailingZeroCount(_currentMatch);
		int entityId = ((_chunkIndex - 1) << 6) | bit;
		_currentMatch &= ~(1UL << bit);
		_current = new Entity(entityId);
		return true;
	}

	public readonly Entity Current => _current;
}
