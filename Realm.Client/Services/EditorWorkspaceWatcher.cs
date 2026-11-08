using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Realm.Shared.Maps;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
/// Watches the open map's workspace for metadata and terrain files changing underneath the editor. It
/// records what each monitored file looked like, notices when the editor itself writes one, and raises a
/// pending-change flag the UI can consume, so an external rewrite never gets mistaken for the editor's own
/// save.
/// </summary>
internal static class EditorWorkspaceWatcher
{
	private static readonly string[] MonitoredFileNames = BuildMonitoredFileNames();
	private static readonly Dictionary<string, MapFileState> KnownFileStates = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object _watcherLock = new();

	private static FileSystemWatcher? _workspaceWatcher;
	private static string? _watchedDirectory;
	private static Timer? _debounceTimer;
	private static bool _metadataChangePending;
	private static bool _terrainChangePending;
	private static volatile bool _hasPendingExternalChanges;
	private static long _lastEditorChangeUtcTicks = DateTime.MinValue.Ticks;

	/// <summary>True while the editor should ignore file change events it caused itself.</summary>
	public static Func<bool>? IsSuppressed { get; set; }

	public static DateTime LastEditorChangeUtc => new(Volatile.Read(ref _lastEditorChangeUtcTicks), DateTimeKind.Utc);

	public static void NoteEditorChange()
	{
		Volatile.Write(ref _lastEditorChangeUtcTicks, DateTime.UtcNow.Ticks);
	}

	public static void NoteInternalFileWrite(string filePath)
	{
		if (string.IsNullOrEmpty(filePath)) return;
		string fileName = Path.GetFileName(filePath);
		if (!IsMetadataFile(fileName) && !IsTerrainFile(fileName)) return;

		lock (_watcherLock)
		{
			if (!TryReadFileState(filePath, out MapFileState state))
			{
				KnownFileStates.Remove(filePath);
				return;
			}

			KnownFileStates[filePath] = state;

			if (IsMetadataFile(fileName))
			{
				_metadataChangePending = false;
			}
			else
			{
				_terrainChangePending = false;
			}

			_hasPendingExternalChanges = _metadataChangePending || _terrainChangePending;
		}
	}

	public static bool HasPendingExternalChanges => _hasPendingExternalChanges;

	public static bool TryTakePendingExternalChanges(out bool metadataChanged, out bool terrainChanged)
	{
		lock (_watcherLock)
		{
			metadataChanged = _metadataChangePending;
			terrainChanged = _terrainChangePending;
			_metadataChangePending = false;
			_terrainChangePending = false;
			_hasPendingExternalChanges = false;
			return metadataChanged || terrainChanged;
		}
	}

	public static void UpdateWatchedFileTimestamps()
	{
		ReconcileKnownFileStatesFromDisk();
	}

	public static void ReconcileKnownFileStatesFromDisk(bool clearPendingChanges = true)
	{
		lock (_watcherLock)
		{
			if (string.IsNullOrEmpty(_watchedDirectory) || !Directory.Exists(_watchedDirectory)) return;

			foreach (string fileName in MonitoredFileNames)
			{
				string filePath = Path.Combine(_watchedDirectory, fileName);
				if (!TryReadFileState(filePath, out MapFileState state))
				{
					KnownFileStates.Remove(filePath);
					continue;
				}

				bool wasRewritten = KnownFileStates.TryGetValue(filePath, out MapFileState previous) && !previous.Equals(state);
				KnownFileStates[filePath] = state;

				if (!wasRewritten || !clearPendingChanges) continue;

				if (IsMetadataFile(fileName))
				{
					_metadataChangePending = false;
				}
				else if (IsTerrainFile(fileName))
				{
					_terrainChangePending = false;
				}

				_hasPendingExternalChanges = _metadataChangePending || _terrainChangePending;
			}
		}
	}

	public static void Start(string directory)
	{
		TrackFileWritesFromWriters();

		lock (_watcherLock)
		{
			Stop();

			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
			{
				return;
			}

			_watchedDirectory = directory;
			KnownFileStates.Clear();
			_metadataChangePending = false;
			_terrainChangePending = false;
			_hasPendingExternalChanges = false;

			try
			{
				foreach (string fileName in MonitoredFileNames)
				{
					string filePath = Path.Combine(directory, fileName);
					if (TryReadFileState(filePath, out MapFileState state))
					{
						KnownFileStates[filePath] = state;
					}
				}

				_workspaceWatcher = new FileSystemWatcher(directory)
				{
					NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
					IncludeSubdirectories = false,
					EnableRaisingEvents = true
				};

				_workspaceWatcher.Changed += (s, e) => OnWorkspaceFileChanged(e.FullPath);
				_workspaceWatcher.Created += (s, e) => OnWorkspaceFileChanged(e.FullPath);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[EditorService] Failed to start FileSystemWatcher on {directory}: {ex.Message}");
			}
		}
	}

	public static void Stop()
	{
		lock (_watcherLock)
		{
			if (_workspaceWatcher != null)
			{
				_workspaceWatcher.EnableRaisingEvents = false;
				_workspaceWatcher.Dispose();
				_workspaceWatcher = null;
			}

			_debounceTimer?.Dispose();
			_debounceTimer = null;
		}
	}

	/// <summary>Drops any queued evaluation, for when the editor must stop reacting to the filesystem.</summary>
	public static void CancelPendingEvaluation()
	{
		lock (_watcherLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = null;
		}
	}

	private static void TrackFileWritesFromWriters()
	{
		MapFileService.FileWritten = NoteInternalFileWrite;
	}

	private static void OnWorkspaceFileChanged(string fullPath)
	{
		string fileName = Path.GetFileName(fullPath);
		if (!IsMetadataFile(fileName) && !IsTerrainFile(fileName)) return;

		lock (_watcherLock)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = new Timer(_ => EvaluateFileChange(fullPath, fileName), null, 200, Timeout.Infinite);
		}
	}

	private static void EvaluateFileChange(string fullPath, string fileName)
	{
		try
		{
			if (IsSuppressed?.Invoke() == true) return;
			if (!TryReadFileState(fullPath, out MapFileState current)) return;

			lock (_watcherLock)
			{
				if (KnownFileStates.TryGetValue(fullPath, out MapFileState known) && known.Equals(current)) return;

				KnownFileStates[fullPath] = current;
				if (IsMetadataFile(fileName))
				{
					_metadataChangePending = true;
				}
				else
				{
					_terrainChangePending = true;
				}

				_hasPendingExternalChanges = true;
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[EditorService] EvaluateFileChange error: {ex.Message}");
		}
	}

	private static bool IsMetadataFile(string fileName)
	{
		return fileName.Equals("metadata.json", StringComparison.OrdinalIgnoreCase) ||
		       fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase);
	}

	private static string[] BuildMonitoredFileNames()
	{
		var fileNames = new List<string> { "metadata.json", "manifest.json", "terrain.json" };
		fileNames.AddRange(MapTerrainFiles.TerrainDataFileNames);
		return fileNames.ToArray();
	}

	private static bool IsTerrainFile(string fileName)
	{
		return fileName.Equals("terrain.json", StringComparison.OrdinalIgnoreCase) ||
		       MapTerrainFiles.IsTerrainDataFileName(fileName);
	}

	private static bool TryReadFileState(string filePath, out MapFileState state)
	{
		state = default;
		try
		{
			var fileInfo = new FileInfo(filePath);
			if (!fileInfo.Exists) return false;

			state = new MapFileState(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);
			return true;
		}
		catch (IOException)
		{
			return false;
		}
	}

	private readonly record struct MapFileState(long Size, long LastWriteTicks);
}
