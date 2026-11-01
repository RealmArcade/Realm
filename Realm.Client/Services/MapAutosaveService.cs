using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Shared.Terrain;

namespace Realm.Client.Services;

public class MapAutosaveService
{
	private readonly object _queueLock = new();
	private readonly object _writeLock = new();

	private MapTerrainSaveRequest? _pendingWrite;
	private bool _isWriting;
	private Task? _worker;

	public bool HasPendingWrite
	{
		get
		{
			lock (_queueLock)
			{
				return _pendingWrite != null || _isWriting;
			}
		}
	}

	public void EnqueueTerrainWrite(MapTerrainSaveRequest request)
	{
		if (request == null || string.IsNullOrEmpty(request.AbsolutePath)) return;

		lock (_queueLock)
		{
			_pendingWrite = request;
			if (_isWriting) return;

			_isWriting = true;
			_worker = Task.Run(DrainPendingWrites);
		}
	}

	public void FlushPendingWrites(TimeSpan timeout)
	{
		Task? worker;
		lock (_queueLock)
		{
			worker = _worker;
		}

		if (worker == null || worker.IsCompleted) return;

		try
		{
			worker.Wait(timeout);
		}
		catch (AggregateException exception)
		{
			GD.PrintErr($"[MapAutosaveService] The background terrain write did not finish cleanly: {exception.GetBaseException().Message}");
		}
	}

	private void DrainPendingWrites()
	{
		while (true)
		{
			MapTerrainSaveRequest? write;
			lock (_queueLock)
			{
				write = _pendingWrite;
				_pendingWrite = null;
				if (write == null)
				{
					_isWriting = false;
					return;
				}
			}

			WriteTerrain(write);
		}
	}

	private void WriteTerrain(MapTerrainSaveRequest write)
	{
		lock (_writeLock)
		{
			try
			{
				write.WriteToDisk();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapAutosaveService] Could not write '{write.AbsolutePath}': {exception.Message}");
			}
		}
	}
}
