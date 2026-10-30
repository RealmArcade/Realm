using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace Realm.Client.Services;

public class BaseMapFolderWatchService : IDisposable
{
	private const int DebounceMilliseconds = 500;
	private const double InternalWriteGraceMilliseconds = 2500.0;
	private const int CompareBufferSize = 65536;

	private readonly object _gate = new();
	private readonly HashSet<string> _pendingRelativePaths = new(StringComparer.OrdinalIgnoreCase);
	private FileSystemWatcher? _watcher;
	private System.Threading.Timer? _debounceTimer;
	private string? _baseFolder;
	private string? _workspaceFolder;
	private DateTime _lastInternalWriteUtc = DateTime.MinValue;
	private bool _disposed;

	public event Action<IReadOnlyList<string>>? BaseFolderFilesChanged;

	public bool IsWatching
	{
		get
		{
			lock (_gate)
			{
				return _watcher != null;
			}
		}
	}

	public string? WatchedBaseFolder
	{
		get
		{
			lock (_gate)
			{
				return _baseFolder;
			}
		}
	}

	public void Start(string baseFolder, string workspaceFolder)
	{
		Stop();

		if (string.IsNullOrWhiteSpace(baseFolder) || !Directory.Exists(baseFolder))
		{
			return;
		}

		try
		{
			string resolvedBaseFolder = Path.GetFullPath(baseFolder);
			string? resolvedWorkspaceFolder = string.IsNullOrWhiteSpace(workspaceFolder) ? null : Path.GetFullPath(workspaceFolder);

			var watcher = new FileSystemWatcher(resolvedBaseFolder)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
				InternalBufferSize = 65536
			};

			watcher.Changed += OnFileSystemEntryChanged;
			watcher.Created += OnFileSystemEntryChanged;
			watcher.Deleted += OnFileSystemEntryChanged;
			watcher.Renamed += OnFileSystemEntryRenamed;
			watcher.Error += OnWatcherError;

			lock (_gate)
			{
				_baseFolder = resolvedBaseFolder;
				_workspaceFolder = resolvedWorkspaceFolder;
				_pendingRelativePaths.Clear();
				_lastInternalWriteUtc = DateTime.MinValue;
				_watcher = watcher;
			}

			watcher.EnableRaisingEvents = true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[BaseMapFolderWatchService] Failed to watch '{baseFolder}': {ex.Message}");
			Stop();
		}
	}

	public void Stop()
	{
		FileSystemWatcher? watcher;
		System.Threading.Timer? timer;

		lock (_gate)
		{
			watcher = _watcher;
			timer = _debounceTimer;
			_watcher = null;
			_debounceTimer = null;
			_baseFolder = null;
			_workspaceFolder = null;
			_pendingRelativePaths.Clear();
		}

		if (watcher != null)
		{
			watcher.EnableRaisingEvents = false;
			watcher.Changed -= OnFileSystemEntryChanged;
			watcher.Created -= OnFileSystemEntryChanged;
			watcher.Deleted -= OnFileSystemEntryChanged;
			watcher.Renamed -= OnFileSystemEntryRenamed;
			watcher.Error -= OnWatcherError;
			watcher.Dispose();
		}

		timer?.Dispose();
	}

	public void MarkInternalWrite()
	{
		lock (_gate)
		{
			_lastInternalWriteUtc = DateTime.UtcNow;
		}
	}

	public int ReloadChangedFiles(IReadOnlyList<string> relativePaths)
	{
		string? baseFolder;
		string? workspaceFolder;

		lock (_gate)
		{
			baseFolder = _baseFolder;
			workspaceFolder = _workspaceFolder;
			_lastInternalWriteUtc = DateTime.UtcNow;
		}

		if (baseFolder == null || workspaceFolder == null || relativePaths == null) return 0;

		int reloadedCount = 0;
		foreach (string relativePath in relativePaths)
		{
			if (string.IsNullOrEmpty(relativePath)) continue;
			try
			{
				string sourcePath = Path.Combine(baseFolder, relativePath);
				string targetPath = Path.Combine(workspaceFolder, relativePath);
				if (File.Exists(sourcePath))
				{
					string? targetDirectory = Path.GetDirectoryName(targetPath);
					if (!string.IsNullOrEmpty(targetDirectory))
					{
						Directory.CreateDirectory(targetDirectory);
					}

					PathUtils.CopyFileClearingReadOnly(sourcePath, targetPath);
					reloadedCount++;
				}
				else if (File.Exists(targetPath))
				{
					File.Delete(targetPath);
					reloadedCount++;
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[BaseMapFolderWatchService] Failed to reload '{relativePath}': {ex.Message}");
			}
		}

		return reloadedCount;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Stop();
	}

	private void OnFileSystemEntryChanged(object sender, FileSystemEventArgs args)
	{
		EnqueueRelativePath(args.FullPath);
	}

	private void OnFileSystemEntryRenamed(object sender, RenamedEventArgs args)
	{
		EnqueueRelativePath(args.OldFullPath);
		EnqueueRelativePath(args.FullPath);
	}

	private void OnWatcherError(object sender, ErrorEventArgs args)
	{
		GD.PrintErr($"[BaseMapFolderWatchService] Watch error: {args.GetException().Message}");
	}

	private void EnqueueRelativePath(string fullPath)
	{
		string? relativePath = ToRelativePath(fullPath);
		if (relativePath == null) return;
		if (MapWorkspaceService.IsIgnoredPathDomain(relativePath)) return;

		lock (_gate)
		{
			if (_watcher == null) return;
			_pendingRelativePaths.Add(relativePath);
			_debounceTimer?.Dispose();
			_debounceTimer = new System.Threading.Timer(_ => FlushPendingChanges(), null, DebounceMilliseconds, System.Threading.Timeout.Infinite);
		}
	}

	private string? ToRelativePath(string fullPath)
	{
		string? baseFolder;
		lock (_gate)
		{
			baseFolder = _baseFolder;
		}

		if (string.IsNullOrEmpty(fullPath) || baseFolder == null) return null;

		try
		{
			string fullBaseFolder = Path.GetFullPath(baseFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string fullChangedPath = Path.GetFullPath(fullPath);
			if (!fullChangedPath.StartsWith(fullBaseFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			return fullChangedPath.Substring(fullBaseFolder.Length + 1).Replace('\\', '/');
		}
		catch
		{
			return null;
		}
	}

	private void FlushPendingChanges()
	{
		List<string> changedRelativePaths = new();
		string? baseFolder;
		string? workspaceFolder;

		lock (_gate)
		{
			_debounceTimer?.Dispose();
			_debounceTimer = null;

			if (_watcher == null || _pendingRelativePaths.Count == 0) return;
			if ((DateTime.UtcNow - _lastInternalWriteUtc).TotalMilliseconds < InternalWriteGraceMilliseconds)
			{
				_pendingRelativePaths.Clear();
				return;
			}

			changedRelativePaths.AddRange(_pendingRelativePaths);
			_pendingRelativePaths.Clear();
			baseFolder = _baseFolder;
			workspaceFolder = _workspaceFolder;
		}

		if (baseFolder == null || workspaceFolder == null) return;

		changedRelativePaths.Sort(StringComparer.OrdinalIgnoreCase);
		List<string> driftedRelativePaths = changedRelativePaths
			.Where(relativePath => HasWorkspaceCopyDrifted(baseFolder, workspaceFolder, relativePath))
			.ToList();

		if (driftedRelativePaths.Count == 0) return;

		Action<IReadOnlyList<string>>? handler = BaseFolderFilesChanged;
		if (handler == null) return;

		Callable.From(() => handler(driftedRelativePaths)).CallDeferred();
	}

	private static bool HasWorkspaceCopyDrifted(string baseFolder, string workspaceFolder, string relativePath)
	{
		try
		{
			string basePath = Path.Combine(baseFolder, relativePath);
			string workspacePath = Path.Combine(workspaceFolder, relativePath);

			bool baseExists = File.Exists(basePath);
			bool workspaceExists = File.Exists(workspacePath);
			if (baseExists != workspaceExists) return true;
			if (!baseExists) return false;

			return !FilesAreIdentical(basePath, workspacePath);
		}
		catch
		{
			return true;
		}
	}

	private static bool FilesAreIdentical(string firstPath, string secondPath)
	{
		using var firstStream = File.OpenRead(firstPath);
		using var secondStream = File.OpenRead(secondPath);
		if (firstStream.Length != secondStream.Length) return false;

		byte[] firstBuffer = new byte[CompareBufferSize];
		byte[] secondBuffer = new byte[CompareBufferSize];

		while (true)
		{
			int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
			int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
			if (firstRead != secondRead) return false;
			if (firstRead == 0) return true;
			if (!new ReadOnlySpan<byte>(firstBuffer, 0, firstRead).SequenceEqual(new ReadOnlySpan<byte>(secondBuffer, 0, secondRead))) return false;
		}
	}
}
