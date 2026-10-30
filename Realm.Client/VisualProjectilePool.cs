using System.Collections.Generic;
using Godot;

namespace Realm.Client;

public static class VisualProjectilePool
{
	private static readonly Stack<VisualProjectile3D> _available = new();
	private static readonly List<VisualProjectile3D> _all = new();
	private static readonly List<VisualProjectile3D> _activeProjectiles = new();
	private const int InitialCapacity = 32;

	public static int TotalAllocated => _all.Count;
	public static int AvailableCount => _available.Count;
	public static int ActiveCount => _activeProjectiles.Count;
	public static IReadOnlyList<VisualProjectile3D> AllProjectiles => _all;
	public static IReadOnlyList<VisualProjectile3D> ActiveProjectiles => _activeProjectiles;

	public static VisualProjectile3D Rent(Node3D parent)
	{
		VisualProjectile3D projectile = null;
		while (_available.Count > 0)
		{
			var cand = _available.Pop();
			if (cand != null && GodotObject.IsInstanceValid(cand))
			{
				projectile = cand;
				break;
			}
		}

		if (projectile == null)
		{
			projectile = new VisualProjectile3D();
			projectile.Name = $"VisualProjectile_{_all.Count}";
			projectile.OnRecycleRequested = Return;
			_all.Add(projectile);
		}

		if (projectile.GetParent() != parent)
		{
			if (projectile.GetParent() != null)
			{
				projectile.GetParent().RemoveChild(projectile);
			}

			parent.AddChild(projectile);
		}

		if (!_activeProjectiles.Contains(projectile))
		{
			_activeProjectiles.Add(projectile);
		}

		return projectile;
	}

	public static void Return(VisualProjectile3D projectile)
	{
		if (projectile == null || !GodotObject.IsInstanceValid(projectile)) return;

		projectile.SetProcess(false);
		projectile.Visible = false;
		_activeProjectiles.Remove(projectile);

		if (!_available.Contains(projectile))
		{
			_available.Push(projectile);
		}
	}

	public static void DeactivateAll()
	{
		for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
		{
			var projectile = _activeProjectiles[i];
			if (projectile == null || !GodotObject.IsInstanceValid(projectile))
			{
				_activeProjectiles.RemoveAt(i);
				continue;
			}

			Return(projectile);
		}
	}

	public static void AdvanceActiveProjectiles(float deltaSeconds)
	{
		for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
		{
			var proj = _activeProjectiles[i];
			if (proj != null && GodotObject.IsInstanceValid(proj))
			{
				if (!proj.IsPaused && proj.IsActive)
				{
					proj.AdvanceSimulation(deltaSeconds * proj.TimeScale);
				}
			}
			else
			{
				_activeProjectiles.RemoveAt(i);
			}
		}
	}

	public static void Clear()
	{
		foreach (var proj in _all)
		{
			if (proj != null && GodotObject.IsInstanceValid(proj))
			{
				proj.QueueFree();
			}
		}

		_all.Clear();
		_available.Clear();
		_activeProjectiles.Clear();
	}
}
