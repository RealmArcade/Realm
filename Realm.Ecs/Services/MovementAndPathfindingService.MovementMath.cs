// Extracted from MovementAndPathfindingService.cs (task 6 decomposition).
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Realm.Ecs;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using PathingFlags = Realm.Ecs.Components.Movement.PathingFlags;

namespace Realm.Ecs.Services
{
	public partial class MovementAndPathfindingService
	{
		private static Vector3 MoveTowards(Vector3 from, Vector3 to, float maxDelta)
		{
			Vector3 diff = to - from;
			float len = diff.Length();
			if (len <= maxDelta || len <= 0.0001f)
			{
				return to;
			}

			return from + diff * (maxDelta / len);
		}

		private long GetCellKey(float x, float z)
		{
			int cx = (int)Math.Floor(x / _collisionCellSize);
			int cz = (int)Math.Floor(z / _collisionCellSize);
			return ((long)cx << 32) | (uint)cz;
		}

		private List<SpatialGridEntry> GetEntityListFromPool()
		{
			if (_listPool.Count > 0)
			{
				int lastIdx = _listPool.Count - 1;
				var list = _listPool[lastIdx];
				_listPool.RemoveAt(lastIdx);
				return list;
			}

			return new List<SpatialGridEntry>(16);
		}
	}
}
