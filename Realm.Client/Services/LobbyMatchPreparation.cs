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

public class LobbyMatchPreparation
{
	private const double FrameBudgetMilliseconds = 6.0;

	private readonly Queue<string> _pendingModels = new();
	private readonly List<string> _warmedModels = new();

	private string _mapName = string.Empty;
	private string _mapVersion = "1.0.0";
	private int _totalModels;

	public bool IsPrepared { get; private set; }

	public bool HasWork => _pendingModels.Count > 0 || (_totalModels > 0 && !IsPrepared);

	public string MapName => _mapName;

	public int TotalModels => _totalModels;

	public double WarmMilliseconds { get; private set; }

	public event Action? StateChanged;

	public void Prepare(string? mapName, string? mapVersion)
	{
		string name = mapName ?? string.Empty;
		string version = string.IsNullOrWhiteSpace(mapVersion) ? "1.0.0" : mapVersion;
		if (name.Length == 0) return;
		if (IsPrepared && string.Equals(_mapName, name, StringComparison.Ordinal) && string.Equals(_mapVersion, version, StringComparison.Ordinal)) return;
		if (HasWork && string.Equals(_mapName, name, StringComparison.Ordinal) && string.Equals(_mapVersion, version, StringComparison.Ordinal)) return;

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
