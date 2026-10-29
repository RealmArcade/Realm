using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Prepares what a match reads before the match scene exists, while the lobby is open and the machine is idle. It
///     walks the map's own template catalog and warms every model path the runtime will ask <see cref="ModelCache" /> for,
///     through that same cache, in small slices so the lobby keeps answering. Nothing is decoded into a list the runtime
///     never reads: the only thing prepared is what a unit, building, prop or resource asks for when it is spawned.
/// </summary>
public class LobbyMatchPreparation
{
	/// <summary>How long one frame may spend warming models before the rest is left for the next frame.</summary>
	private const double FrameBudgetMilliseconds = 6.0;

	private readonly Queue<string> _pendingModels = new();
	private readonly List<string> _warmedModels = new();

	private string _mapName = string.Empty;
	private string _mapVersion = "1.0.0";
	private int _totalModels;

	/// <summary>Whether every model the map names is already in the cache the match will read them from.</summary>
	public bool IsPrepared { get; private set; }

	/// <summary>Whether anything is being prepared for a map right now.</summary>
	public bool HasWork => _pendingModels.Count > 0 || (_totalModels > 0 && !IsPrepared);

	/// <summary>The map this preparation is for, or nothing before a map was named.</summary>
	public string MapName => _mapName;

	/// <summary>How many models the map names, and how many of them are still waiting to be warmed.</summary>
	public int TotalModels => _totalModels;

	/// <summary>How much time the warm actually spent parsing and decoding models, across every slice it took.</summary>
	public double WarmMilliseconds { get; private set; }

	/// <summary>Raised when preparation starts, finishes or is cancelled, so the screen can say which it is.</summary>
	public event Action? StateChanged;

	/// <summary>
	///     Starts preparing a map, or does nothing when this map is already prepared or already being prepared. The map
	///     must be on disk: a client that does not hold it yet is still downloading, and its preparation begins when the
	///     download reports the map ready.
	/// </summary>
	public void Prepare(string? mapName, string? mapVersion)
	{
		string name = mapName ?? string.Empty;
		string version = string.IsNullOrWhiteSpace(mapVersion) ? "1.0.0" : mapVersion;
		if (name.Length == 0) return;
		if (IsPrepared && string.Equals(_mapName, name, StringComparison.Ordinal) && string.Equals(_mapVersion, version, StringComparison.Ordinal)) return;
		if (HasWork && string.Equals(_mapName, name, StringComparison.Ordinal) && string.Equals(_mapVersion, version, StringComparison.Ordinal)) return;
		if (!MapAssetManager.IsMapDownloaded(name, version)) return;

		Cancel();

		_mapName = name;
		_mapVersion = version;
		MatchLoadTrace.Mark("preparation_begin");

		string? mapDirectory = GameHost.ResolveMapDirectory(name, version);
		if (string.IsNullOrEmpty(mapDirectory))
		{
			GD.PushWarning($"[LobbyMatchPreparation] The map '{name}' could not be resolved, so nothing was prepared for it.");
			return;
		}

		MapMetadata metadata;
		try
		{
			metadata = MapFileService.LoadMetadata(mapDirectory);
		}
		catch (Exception exception)
		{
			GD.PushWarning($"[LobbyMatchPreparation] The map '{name}' has no metadata to prepare from: {exception.Message}");
			return;
		}

		EnqueueTemplateModels(metadata.Templates);
		_totalModels = _pendingModels.Count + _warmedModels.Count;
		MatchLoadTrace.Mark("preparation_catalog");

		if (_pendingModels.Count == 0)
		{
			IsPrepared = true;
			StateChanged?.Invoke();
			return;
		}

		StateChanged?.Invoke();
	}

	/// <summary>
	///     Warms as many models as fit in one frame's budget. Called from the lobby's own frame, so a map with hundreds
	///     of models is spread over many frames rather than stalling the screen that is showing the lobby.
	/// </summary>
	/// <returns>True once every model the map names has been warmed.</returns>
	public bool Tick()
	{
		if (!HasWork) return IsPrepared;

		var budget = Stopwatch.StartNew();
		while (_pendingModels.Count > 0 && budget.Elapsed.TotalMilliseconds < FrameBudgetMilliseconds)
		{
			string modelPath = _pendingModels.Dequeue();
			long sliceStart = Stopwatch.GetTimestamp();
			ModelCache.PreloadModel(modelPath);
			WarmMilliseconds += (Stopwatch.GetTimestamp() - sliceStart) * 1000.0 / Stopwatch.Frequency;
			_warmedModels.Add(modelPath);
		}

		if (_pendingModels.Count > 0) return false;

		IsPrepared = true;
		StateChanged?.Invoke();
		return true;
	}

	/// <summary>
	///     Drops the preparation, and the models it put in the cache, so a player who leaves, is kicked or drops out of a
	///     lobby leaves nothing behind that the next lobby would inherit.
	/// </summary>
	public void Cancel()
	{
		bool hadWork = _totalModels > 0 || _warmedModels.Count > 0;

		for (int index = 0; index < _warmedModels.Count; index++) ModelCache.ReleaseModel(_warmedModels[index]);

		_warmedModels.Clear();
		_pendingModels.Clear();
		_totalModels = 0;
		WarmMilliseconds = 0;
		IsPrepared = false;
		_mapName = string.Empty;
		_mapVersion = "1.0.0";

		if (hadWork) StateChanged?.Invoke();
	}

	/// <summary>Whether the map this preparation is for is the one the lobby named.</summary>
	public bool IsPreparing(string? mapName) => HasWork && string.Equals(_mapName, mapName ?? string.Empty, StringComparison.Ordinal);

	private void EnqueueTemplateModels(TemplateContainer? templates)
	{
		if (templates == null) return;

		for (int index = 0; index < templates.Units.Count; index++) Enqueue(templates.Units[index].ModelPath);
		for (int index = 0; index < templates.Buildings.Count; index++) Enqueue(templates.Buildings[index].ModelPath);
		for (int index = 0; index < templates.Resources.Count; index++) Enqueue(templates.Resources[index].ModelPath);
		for (int index = 0; index < templates.Props.Count; index++) Enqueue(templates.Props[index].ModelPath);
	}

	private void Enqueue(string? modelPath)
	{
		if (string.IsNullOrWhiteSpace(modelPath)) return;
		if (_pendingModels.Contains(modelPath)) return;
		if (_warmedModels.Contains(modelPath)) return;

		_pendingModels.Enqueue(modelPath);
	}
}
