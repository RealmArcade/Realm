using Realm.Ecs.Common;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Realm.Client.ReplaySystem
{
	public static class ReplayObjectPool
	{
		private static readonly ConcurrentBag<ReplayFrame> _framePool = new();
		private static readonly ConcurrentBag<List<ReplayUnitSnapshot>> _listPool = new();
		private static readonly ConcurrentBag<List<ReplayProjectileSnapshot>> _projectileListPool = new();
		private static readonly ConcurrentBag<List<int>> _intListPool = new();

		public static ReplayFrame RentFrame()
		{
			int unitsCap = GameplayConstants.MaxUnitsLimit;
			int projCap = GameplayConstants.MaxProjectilesLimit;
			if (_framePool.TryTake(out var frame))
			{
				if (frame.Units == null)
				{
					frame.Units = new List<ReplayUnitSnapshot>(unitsCap);
				}
				else
				{
					frame.Units.Clear();
				}
				if (frame.Projectiles == null)
				{
					frame.Projectiles = new List<ReplayProjectileSnapshot>(projCap);
				}
				else
				{
					frame.Projectiles.Clear();
				}
				return frame;
			}
			return new ReplayFrame
			{
				Units = new List<ReplayUnitSnapshot>(unitsCap),
				Projectiles = new List<ReplayProjectileSnapshot>(projCap)
			};
		}

		public static void ReturnFrame(ReplayFrame frame)
		{
			if (frame.Units != null)
			{
				frame.Units.Clear();
			}
			if (frame.Projectiles != null)
			{
				frame.Projectiles.Clear();
			}
			_framePool.Add(frame);
		}

		public static List<ReplayUnitSnapshot> RentList()
		{
			if (_listPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<ReplayUnitSnapshot>(GameplayConstants.MaxUnitsLimit);
		}

		public static void ReturnList(List<ReplayUnitSnapshot> list)
		{
			list.Clear();
			_listPool.Add(list);
		}

		public static List<ReplayProjectileSnapshot> RentProjectileList()
		{
			if (_projectileListPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<ReplayProjectileSnapshot>(GameplayConstants.MaxProjectilesLimit);
		}

		public static void ReturnProjectileList(List<ReplayProjectileSnapshot> list)
		{
			list.Clear();
			_projectileListPool.Add(list);
		}

		public static List<int> RentIntList()
		{
			if (_intListPool.TryTake(out var list))
			{
				list.Clear();
				return list;
			}
			return new List<int>(GameplayConstants.MaxUnitsLimit);
		}

		public static void ReturnIntList(List<int> list)
		{
			list.Clear();
			_intListPool.Add(list);
		}
	}
}
