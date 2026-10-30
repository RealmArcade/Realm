using System;
using System.Collections.Generic;
using Godot;

namespace Realm.Client.Services;

public class ServiceScope : IDisposable
{
	private readonly Dictionary<Type, object> _services = new();
	private readonly Dictionary<Type, Action<object>> _cleanups = new();
	private readonly List<Type> _creationOrder = new();
	private readonly List<Action> _disposeActions = new();

	public ServiceScope(string name)
	{
		Name = name;
	}

	public string Name { get; }
	public bool IsDisposed { get; private set; }

	public T Get<T>() where T : class, new()
	{
		ThrowIfDisposed();

		if (_services.TryGetValue(typeof(T), out object? existingService))
		{
			return (T)existingService;
		}

		var service = new T();
		_services[typeof(T)] = service;
		_creationOrder.Add(typeof(T));
		return service;
	}

	public void RegisterCleanup<T>(Action<T> cleanup) where T : class
	{
		_cleanups[typeof(T)] = service => cleanup((T)service);
	}

	public void RegisterDisposeAction(Action disposeAction)
	{
		_disposeActions.Add(disposeAction);
	}

	private void ThrowIfDisposed()
	{
		if (IsDisposed)
		{
			throw new ObjectDisposedException(Name, $"The service scope '{Name}' has already been disposed. Resolve the live scope again to obtain fresh services.");
		}
	}

	public void Dispose()
	{
		if (IsDisposed) return;
		IsDisposed = true;

		for (int index = _creationOrder.Count - 1; index >= 0; index--)
		{
			Type serviceType = _creationOrder[index];
			if (_services.TryGetValue(serviceType, out object? service))
			{
				RunCleanup(serviceType, service);
			}
		}

		_services.Clear();
		_creationOrder.Clear();
		_cleanups.Clear();

		RunDisposeActions();
		_disposeActions.Clear();
	}

	private void RunCleanup(Type serviceType, object service)
	{
		try
		{
			if (service is IDisposable disposable)
			{
				disposable.Dispose();
				return;
			}

			if (_cleanups.TryGetValue(serviceType, out Action<object>? cleanup))
			{
				cleanup(service);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[ServiceScope:{Name}] Failed to clean up '{serviceType.Name}': {exception.Message}");
		}
	}

	private void RunDisposeActions()
	{
		foreach (Action disposeAction in _disposeActions)
		{
			try
			{
				disposeAction();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[ServiceScope:{Name}] Dispose action failed: {exception.Message}");
			}
		}
	}
}
