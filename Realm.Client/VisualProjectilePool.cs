using Godot;
using System.Collections.Generic;

namespace Realm.Client;

public class VisualProjectilePool
{
	private static VisualProjectilePool _instance;
	public static VisualProjectilePool Instance => _instance ??= new VisualProjectilePool();

	private readonly Stack<VisualProjectile3D> _available = new();
	private readonly List<VisualProjectile3D> _all = new();
	private const int InitialCapacity = 32;

	public int TotalAllocated => _all.Count;
	public int AvailableCount => _available.Count;
	public int ActiveCount => _all.Count - _available.Count;
	public IReadOnlyList<VisualProjectile3D> AllProjectiles => _all;

	public static VisualProjectile3D Rent(Node3D parent) => Instance.RentInternal(parent);
	public static void Return(VisualProjectile3D projectile) => Instance.ReturnInternal(projectile);

	public VisualProjectile3D RentInternal(Node3D parent)
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

		return projectile;
	}

	public void ReturnInternal(VisualProjectile3D projectile)
	{
		if (projectile == null || !GodotObject.IsInstanceValid(projectile)) return;

		projectile.SetProcess(false);
		projectile.Visible = false;
		if (!_available.Contains(projectile))
		{
			_available.Push(projectile);
		}
	}

	public void Clear()
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
	}
}