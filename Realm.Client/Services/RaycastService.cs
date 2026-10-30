using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.VFX;
using Realm.Ecs.Common;

namespace Realm.Client.Services;

public class RaycastResult
{
	public bool HasHit;
	public Vector3 Position;
	public Vector3 Normal;
	public Node Collider;
	public float Distance;

	public Godot.Collections.Dictionary ToGodotDictionary()
	{
		if (!HasHit) return null;
		var dict = new Godot.Collections.Dictionary();
		dict["position"] = Position;
		dict["normal"] = Normal;
		if (Collider != null && GodotObject.IsInstanceValid(Collider))
		{
			dict["collider"] = Collider;
		}

		return dict;
	}
}

public class RaycastService
{
	public const uint TerrainLayer = RuntimeTerrain.TerrainCollisionLayer;
	public const uint ObjectLayer = 1u;

	private readonly List<Decal3D> _registeredDecals = new();
	private readonly List<ProceduralVfxInstance3D> _registeredVfx = new();

	public void RegisterDecal(Decal3D decal)
	{
		if (decal != null && !_registeredDecals.Contains(decal))
		{
			_registeredDecals.Add(decal);
		}
	}

	public void UnregisterDecal(Decal3D decal)
	{
		if (decal != null)
		{
			_registeredDecals.Remove(decal);
		}
	}

	public void RegisterVfx(ProceduralVfxInstance3D vfx)
	{
		if (vfx != null && !_registeredVfx.Contains(vfx))
		{
			_registeredVfx.Add(vfx);
		}
	}

	public void UnregisterVfx(ProceduralVfxInstance3D vfx)
	{
		if (vfx != null)
		{
			_registeredVfx.Remove(vfx);
		}
	}

	public bool TryRaycast(Vector3 from, Vector3 to, uint collisionMask, out Vector3 position, out Vector3 normal, out Node collider)
	{
		var result = Raycast(from, to, collisionMask);
		if (result != null && result.HasHit)
		{
			position = result.Position;
			normal = result.Normal;
			collider = result.Collider;
			return true;
		}

		position = Vector3.Zero;
		normal = Vector3.Up;
		collider = null;
		return false;
	}

	public RaycastResult Raycast(Vector3 from, Vector3 to, uint collisionMask = uint.MaxValue)
	{
		Vector3 dir = to - from;
		float maxDist = dir.Length();
		if (maxDist <= 0.0001f) return null;
		dir /= maxDist;

		RaycastResult closestResult = null;
		float closestDist = maxDist;

		if ((collisionMask & TerrainLayer) != 0)
		{
			var terrainHit = RaycastTerrain(from, dir, maxDist);
			if (terrainHit != null && terrainHit.HasHit && terrainHit.Distance < closestDist)
			{
				closestDist = terrainHit.Distance;
				closestResult = terrainHit;
			}
		}

		if ((collisionMask & ObjectLayer) != 0)
		{
			var unitHit = RaycastUnits(from, dir, closestDist);
			if (unitHit != null && unitHit.HasHit && unitHit.Distance < closestDist)
			{
				closestDist = unitHit.Distance;
				closestResult = unitHit;
			}

			var propHit = RaycastProps(from, dir, closestDist);
			if (propHit != null && propHit.HasHit && propHit.Distance < closestDist)
			{
				closestDist = propHit.Distance;
				closestResult = propHit;
			}

			var decalHit = RaycastDecals(from, dir, closestDist);
			if (decalHit != null && decalHit.HasHit && decalHit.Distance < closestDist)
			{
				closestDist = decalHit.Distance;
				closestResult = decalHit;
			}

			var vfxHit = RaycastVfx(from, dir, closestDist);
			if (vfxHit != null && vfxHit.HasHit && vfxHit.Distance < closestDist)
			{
				closestDist = vfxHit.Distance;
				closestResult = vfxHit;
			}
		}

		return closestResult;
	}

	public RaycastResult RaycastTerrain(Vector3 origin, Vector3 dir, float maxDist)
	{
		var terrain = GameContext.Terrain;
		if (terrain == null || terrain.Cells == null) return null;

		float quadSize = terrain.QuadSize;
		int width = terrain.Width;
		int depth = terrain.Depth;
		if (width <= 0 || depth <= 0 || quadSize <= 0.0001f) return null;

		float halfW = (width / 2.0f) * quadSize;
		float halfD = (depth / 2.0f) * quadSize;

		float rangeMinT = maxDist;
		float rangeMaxT = 0f;
		bool anyChunkHit = false;

		// Broad-phase chunk AABB intersection
		if (terrain.Chunks != null)
		{
			foreach (var chunk in terrain.Chunks)
			{
				if (chunk != null && chunk.WorldAabb.Size != Vector3.Zero && RayIntersectAabb(origin, dir, chunk.WorldAabb, out float tEntry, out float tExit))
				{
					if (tExit >= 0f && tEntry <= maxDist)
					{
						anyChunkHit = true;
						if (tEntry < rangeMinT) rangeMinT = MathF.Max(0f, tEntry);
						if (tExit > rangeMaxT) rangeMaxT = MathF.Min(maxDist, tExit);
					}
				}
			}
		}

		if (!anyChunkHit)
		{
			Aabb fullTerrainAabb = new Aabb(new Vector3(-halfW, -500f, -halfD), new Vector3(halfW * 2f, 1000f, halfD * 2f));
			if (RayIntersectAabb(origin, dir, fullTerrainAabb, out float tEntry, out float tExit))
			{
				if (tExit >= 0f && tEntry <= maxDist)
				{
					rangeMinT = MathF.Max(0f, tEntry);
					rangeMaxT = MathF.Min(maxDist, tExit);
					anyChunkHit = true;
				}
			}
		}

		if (!anyChunkHit) return null;

		float stepSize = quadSize * 0.5f;
		float startT = rangeMinT;
		float endT = rangeMaxT;
		int totalSteps = Mathf.CeilToInt((endT - startT) / stepSize);
		if (totalSteps <= 0) totalSteps = 1;

		float prevT = startT;
		Vector3 prevPos = origin + dir * startT;
		terrain.GetHeightAndNormal(prevPos.X, prevPos.Z, out float prevH, out _);
		bool prevAbove = prevPos.Y >= prevH;

		for (int i = 1; i <= totalSteps; i++)
		{
			float t = startT + i * stepSize;
			if (t > endT) t = endT;

			Vector3 currPos = origin + dir * t;
			if (currPos.X < -halfW || currPos.X > halfW || currPos.Z < -halfD || currPos.Z > halfD)
			{
				prevT = t;
				prevPos = currPos;
				terrain.GetHeightAndNormal(currPos.X, currPos.Z, out prevH, out _);
				prevAbove = currPos.Y >= prevH;
				continue;
			}

			terrain.GetHeightAndNormal(currPos.X, currPos.Z, out float currH, out _);
			bool currAbove = currPos.Y >= currH;

			if (prevAbove != currAbove)
			{
				float lowT = prevT;
				float highT = t;
				for (int step = 0; step < 10; step++)
				{
					float midT = (lowT + highT) * 0.5f;
					Vector3 midPos = origin + dir * midT;
					terrain.GetHeightAndNormal(midPos.X, midPos.Z, out float midH, out _);
					if ((midPos.Y >= midH) == prevAbove)
					{
						lowT = midT;
					}
					else
					{
						highT = midT;
					}
				}

				float hitT = (lowT + highT) * 0.5f;
				Vector3 hitPos = origin + dir * hitT;
				terrain.GetHeightAndNormal(hitPos.X, hitPos.Z, out _, out Vector3 hitNorm);

				return new RaycastResult
				{
					HasHit = true,
					Position = hitPos,
					Normal = hitNorm,
					Collider = terrain,
					Distance = hitT
				};
			}

			prevT = t;
			prevPos = currPos;
			prevH = currH;
			prevAbove = currAbove;
		}

		return null;
	}

	public RaycastResult RaycastUnits(Vector3 origin, Vector3 dir, float maxDist)
	{
		var gameHost = GameContext.Host;
		if (gameHost == null) return null;

		RaycastResult bestHit = null;
		float bestDist = maxDist;

		foreach (var unit in gameHost.AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit) || !unit.Visible) continue;

			float radius = MathF.Max(GameplayConstants.ResolveBodyRadius(GameContext.World, unit.Entity, GameplayConstants.DefaultCollisionRadius), 0.5f);
			float height = unit.IsBuilding ? MathF.Max(2.0f, radius * 2.5f) : 2.5f;
			Vector3 center = unit.GlobalPosition;

			if (RayIntersectVerticalCylinder(origin, dir, center, radius, height, out float t, out Vector3 hitPos, out Vector3 hitNorm))
			{
				if (t > 0.001f && t < bestDist)
				{
					bestDist = t;
					bestHit = new RaycastResult
					{
						HasHit = true,
						Position = hitPos,
						Normal = hitNorm,
						Collider = unit,
						Distance = t
					};
				}
			}
		}

		return bestHit;
	}

	public RaycastResult RaycastProps(Vector3 origin, Vector3 dir, float maxDist)
	{
		var gameHost = GameContext.Host;
		if (gameHost == null) return null;

		RaycastResult bestHit = null;
		float bestDist = maxDist;

		foreach (var prop in gameHost.AllProps)
		{
			if (prop == null || !GodotObject.IsInstanceValid(prop) || !prop.Visible) continue;

			float radius = MathF.Max(GameplayConstants.ResolveBodyRadius(GameContext.World, prop.Entity, GameplayConstants.DefaultPropCollisionRadius), 0.5f);
			float height = MathF.Max(1.0f, radius * 2.5f);
			Vector3 center = prop.GlobalPosition;

			if (RayIntersectVerticalCylinder(origin, dir, center, radius, height, out float t, out Vector3 hitPos, out Vector3 hitNorm))
			{
				if (t > 0.001f && t < bestDist)
				{
					bestDist = t;
					bestHit = new RaycastResult
					{
						HasHit = true,
						Position = hitPos,
						Normal = hitNorm,
						Collider = prop,
						Distance = t
					};
				}
			}
		}

		return bestHit;
	}

	public RaycastResult RaycastDecals(Vector3 origin, Vector3 dir, float maxDist)
	{
		var gameHost = GameContext.Host;
		if (gameHost == null) return null;

		RaycastResult bestHit = null;
		float bestDist = maxDist;

		_registeredDecals.RemoveAll(d => d == null || !GodotObject.IsInstanceValid(d));

		foreach (var decal in _registeredDecals)
		{
			if (decal == null || !GodotObject.IsInstanceValid(decal) || !decal.Visible) continue;

			Vector3 boxSize = decal.Size;
			Aabb localAabb = new Aabb(-boxSize * 0.5f, boxSize);

			Transform3D invXform = decal.GlobalTransform.AffineInverse();
			Vector3 localOrigin = invXform * origin;
			Vector3 localDir = invXform.Basis * dir;
			float localDirLen = localDir.Length();

			if (localDirLen > 0.0001f)
			{
				Vector3 localDirNorm = localDir / localDirLen;
				if (RayIntersectAabb(localOrigin, localDirNorm, localAabb, out float tLocalEntry, out _))
				{
					float tWorld = tLocalEntry / localDirLen;
					if (tWorld > 0.001f && tWorld < bestDist)
					{
						bestDist = tWorld;
						Vector3 hitPos = origin + dir * tWorld;
						Vector3 localHitPos = localOrigin + localDir * tWorld;
						Vector3 localNorm = GetAabbNormal(localHitPos, localAabb);
						Vector3 worldNorm = decal.GlobalTransform.Basis * localNorm;

						bestHit = new RaycastResult
						{
							HasHit = true,
							Position = hitPos,
							Normal = worldNorm.Normalized(),
							Collider = decal,
							Distance = tWorld
						};
					}
				}
			}
		}

		return bestHit;
	}

	public RaycastResult RaycastVfx(Vector3 origin, Vector3 dir, float maxDist)
	{
		var gameHost = GameContext.Host;
		if (gameHost == null) return null;

		RaycastResult bestHit = null;
		float bestDist = maxDist;

		_registeredVfx.RemoveAll(v => v == null || !GodotObject.IsInstanceValid(v));

		foreach (var vfx in _registeredVfx)
		{
			if (vfx == null || !GodotObject.IsInstanceValid(vfx) || !vfx.Visible) continue;

			Vector3 center = vfx.GlobalPosition;
			float radius = 1.5f;

			if (RayIntersectSphere(origin, dir, center, radius, out float t))
			{
				if (t > 0.001f && t < bestDist)
				{
					bestDist = t;
					Vector3 hitPos = origin + dir * t;
					Vector3 norm = (hitPos - center).Normalized();

					bestHit = new RaycastResult
					{
						HasHit = true,
						Position = hitPos,
						Normal = norm,
						Collider = vfx,
						Distance = t
					};
				}
			}
		}

		return bestHit;
	}

	private static bool RayIntersectVerticalCylinder(Vector3 origin, Vector3 dir, Vector3 center, float radius, float height, out float tHit, out Vector3 hitPos, out Vector3 hitNorm)
	{
		tHit = 0f;
		hitPos = Vector3.Zero;
		hitNorm = Vector3.Up;

		float minY = center.Y;
		float maxY = center.Y + height;

		Vector2 oxz = new Vector2(origin.X - center.X, origin.Z - center.Z);
		Vector2 dxz = new Vector2(dir.X, dir.Z);

		float a = dxz.LengthSquared();

		if (a < 0.000001f)
		{
			if (oxz.LengthSquared() <= radius * radius)
			{
				if (MathF.Abs(dir.Y) > 0.00001f)
				{
					float tTop = (maxY - origin.Y) / dir.Y;
					float tBottom = (minY - origin.Y) / dir.Y;

					float tMinCap = MathF.Min(tTop, tBottom);
					float tMaxCap = MathF.Max(tTop, tBottom);

					if (tMinCap > 0f)
					{
						tHit = tMinCap;
						hitPos = origin + dir * tHit;
						hitNorm = (tTop < tBottom) ? Vector3.Up : Vector3.Down;
						return true;
					}
				}
			}

			return false;
		}

		float b = 2.0f * oxz.Dot(dxz);
		float c = oxz.LengthSquared() - radius * radius;

		float disc = b * b - 4.0f * a * c;
		if (disc < 0f) return false;

		float sqrtDisc = MathF.Sqrt(disc);
		float t1 = (-b - sqrtDisc) / (2.0f * a);
		float t2 = (-b + sqrtDisc) / (2.0f * a);

		if (t1 > 0f)
		{
			float y = origin.Y + dir.Y * t1;
			if (y >= minY && y <= maxY)
			{
				tHit = t1;
				hitPos = origin + dir * tHit;
				Vector3 sideNorm = new Vector3(hitPos.X - center.X, 0f, hitPos.Z - center.Z);
				hitNorm = sideNorm.LengthSquared() > 0.0001f ? sideNorm.Normalized() : Vector3.Up;
				return true;
			}
		}

		if (MathF.Abs(dir.Y) > 0.0001f)
		{
			float tTop = (maxY - origin.Y) / dir.Y;
			if (tTop > 0f && (tHit <= 0f || tTop < tHit))
			{
				Vector3 capPos = origin + dir * tTop;
				Vector2 capXZ = new Vector2(capPos.X - center.X, capPos.Z - center.Z);
				if (capXZ.LengthSquared() <= radius * radius)
				{
					tHit = tTop;
					hitPos = capPos;
					hitNorm = Vector3.Up;
					return true;
				}
			}

			float tBot = (minY - origin.Y) / dir.Y;
			if (tBot > 0f && (tHit <= 0f || tBot < tHit))
			{
				Vector3 capPos = origin + dir * tBot;
				Vector2 capXZ = new Vector2(capPos.X - center.X, capPos.Z - center.Z);
				if (capXZ.LengthSquared() <= radius * radius)
				{
					tHit = tBot;
					hitPos = capPos;
					hitNorm = Vector3.Down;
					return true;
				}
			}
		}

		if (t2 > 0f)
		{
			float y = origin.Y + dir.Y * t2;
			if (y >= minY && y <= maxY)
			{
				tHit = t2;
				hitPos = origin + dir * tHit;
				Vector3 sideNorm = new Vector3(hitPos.X - center.X, 0f, hitPos.Z - center.Z);
				hitNorm = sideNorm.LengthSquared() > 0.0001f ? sideNorm.Normalized() : Vector3.Up;
				return true;
			}
		}

		return false;
	}

	private static bool RayIntersectSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius, out float tHit)
	{
		tHit = 0f;
		Vector3 oc = origin - center;
		float b = oc.Dot(dir);
		float c = oc.LengthSquared() - radius * radius;
		float disc = b * b - c;
		if (disc < 0f) return false;

		float sqrtDisc = MathF.Sqrt(disc);
		float t = -b - sqrtDisc;
		if (t <= 0f) t = -b + sqrtDisc;

		if (t > 0f)
		{
			tHit = t;
			return true;
		}

		return false;
	}

	public static bool RayIntersectAabb(Vector3 origin, Vector3 dir, Aabb aabb, out float tEntry, out float tExit)
	{
		tEntry = 0f;
		tExit = 0f;

		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		float invDirX = MathF.Abs(dir.X) < 0.000001f ? (dir.X >= 0 ? 10000000f : -10000000f) : (1.0f / dir.X);
		float invDirY = MathF.Abs(dir.Y) < 0.000001f ? (dir.Y >= 0 ? 10000000f : -10000000f) : (1.0f / dir.Y);
		float invDirZ = MathF.Abs(dir.Z) < 0.000001f ? (dir.Z >= 0 ? 10000000f : -10000000f) : (1.0f / dir.Z);

		float tx1 = (min.X - origin.X) * invDirX;
		float tx2 = (max.X - origin.X) * invDirX;
		float tmin = MathF.Min(tx1, tx2);
		float tmax = MathF.Max(tx1, tx2);

		float ty1 = (min.Y - origin.Y) * invDirY;
		float ty2 = (max.Y - origin.Y) * invDirY;
		tmin = MathF.Max(tmin, MathF.Min(ty1, ty2));
		tmax = MathF.Min(tmax, MathF.Max(ty1, ty2));

		float tz1 = (min.Z - origin.Z) * invDirZ;
		float tz2 = (max.Z - origin.Z) * invDirZ;
		tmin = MathF.Max(tmin, MathF.Min(tz1, tz2));
		tmax = MathF.Min(tmax, MathF.Max(tz1, tz2));

		if (tmax >= tmin && tmax >= 0f)
		{
			tEntry = tmin;
			tExit = tmax;
			return true;
		}

		return false;
	}

	private static Vector3 GetAabbNormal(Vector3 localHitPos, Aabb aabb)
	{
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;
		float eps = 0.01f;

		if (MathF.Abs(localHitPos.X - min.X) < eps) return Vector3.Left;
		if (MathF.Abs(localHitPos.X - max.X) < eps) return Vector3.Right;
		if (MathF.Abs(localHitPos.Y - min.Y) < eps) return Vector3.Down;
		if (MathF.Abs(localHitPos.Y - max.Y) < eps) return Vector3.Up;
		if (MathF.Abs(localHitPos.Z - min.Z) < eps) return Vector3.Back;
		if (MathF.Abs(localHitPos.Z - max.Z) < eps) return Vector3.Forward;

		return Vector3.Up;
	}
}
