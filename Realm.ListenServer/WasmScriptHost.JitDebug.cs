using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Realm.MapAPI;
using Realm.Ecs;
using Realm.Ecs.Services;
using Wasmtime;
using Engine = Wasmtime.Engine;
using FileAccess = System.IO.FileAccess;

namespace Realm.ListenServer;

/// <summary>exporting the jit debug elf and resolving the workspace beside the wasm.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	public void ExportJitDebugElf(string wasmPath)
	{
		try
		{
			IntPtr wasmtimeModuleAddress = IntPtr.Zero;
			foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
			{
				if (module.ModuleName.Equals("wasmtime.dll", StringComparison.OrdinalIgnoreCase))
				{
					wasmtimeModuleAddress = module.BaseAddress;
					break;
				}
			}

			if (wasmtimeModuleAddress == IntPtr.Zero)
			{
				LogToConsole("[WASM DEBUG] wasmtime is not loaded in this process, so there is no JIT ELF to export.", true);
				return;
			}

			if (!NativeLibrary.TryGetExport(wasmtimeModuleAddress, "__jit_debug_descriptor", out IntPtr descriptorPointer))
			{
				LogToConsole("[WASM DEBUG] wasmtime exports no JIT debug descriptor, so there is no JIT ELF to export.", true);
				return;
			}

			IntPtr currentEntryPointer = Marshal.ReadIntPtr(descriptorPointer, 16);
			if (currentEntryPointer == IntPtr.Zero)
			{
				LogToConsole("[WASM DEBUG] No JITed module is registered in this process yet, so there is no JIT ELF to export.", true);
				return;
			}

			string? workspaceDirectory = ResolveWorkspaceDirectory(wasmPath);
			string? directoryName = Path.GetDirectoryName(wasmPath);
			string baseFileName = Path.GetFileNameWithoutExtension(wasmPath);
			int exportedCount = 0;

			while (currentEntryPointer != IntPtr.Zero)
			{
				IntPtr symbolFileAddress = Marshal.ReadIntPtr(currentEntryPointer, 16);
				ulong symbolFileSize = (ulong)Marshal.ReadInt64(currentEntryPointer, 24);

				if (symbolFileAddress != IntPtr.Zero && symbolFileSize > 0 && symbolFileSize <= 200_000_000)
				{
					byte[] elfBytes = new byte[symbolFileSize];
					Marshal.Copy(symbolFileAddress, elfBytes, 0, (int)symbolFileSize);

					if (elfBytes.Length >= 4 && elfBytes[0] == 0x7F && elfBytes[1] == 0x45 && elfBytes[2] == 0x4C && elfBytes[3] == 0x46)
					{
						int repairedRanges = JitDebugElfRangeRepair.Repair(elfBytes);
						if (repairedRanges > 0)
						{
							LogToConsole($"[WASM DEBUG] Repaired {repairedRanges} DWARF gap(s) in the JIT ELF of pid {System.Environment.ProcessId}.");
						}

						string suffix = exportedCount == 0 ? string.Empty : $"_{exportedCount}";
						string publishElfPath = Path.Combine(directoryName ?? string.Empty, $"{baseFileName}{suffix}.jit.elf");
						File.WriteAllBytes(publishElfPath, elfBytes);

						if (!string.IsNullOrEmpty(workspaceDirectory) && Directory.Exists(workspaceDirectory))
						{
							string workspaceElfPath = Path.Combine(workspaceDirectory, $"MapScript{suffix}.jit.elf");
							File.WriteAllBytes(workspaceElfPath, elfBytes);
							LogToConsole($"[WASM DEBUG] Exported the JIT ELF of pid {System.Environment.ProcessId} to '{workspaceElfPath}'.");
						}
						else
						{
							LogToConsole($"[WASM DEBUG] No map workspace was found, so the JIT ELF of pid {System.Environment.ProcessId} was only written beside the script at '{publishElfPath}'.", true);
						}

						exportedCount++;
					}
				}

				currentEntryPointer = Marshal.ReadIntPtr(currentEntryPointer, 0);
			}

			WriteDebugMetadata(wasmPath);
		}
		catch (Exception exception)
		{
			LogToConsole($"[WASM DEBUG] Failed to export JIT debug symbols: {exception.Message}", true);
		}
	}

	private void WriteDebugMetadata(string wasmPath)
	{
		try
		{
			string? workspaceDirectory = ResolveWorkspaceDirectory(wasmPath);
			string? directoryName = Path.GetDirectoryName(wasmPath);
			string pidStr = System.Environment.ProcessId.ToString();
			long wasmMemoryBase = _sharedMemoryPointer == IntPtr.Zero ? 0 : _sharedMemoryPointer.ToInt64() - _sharedMemoryOffset;
			string debugJson = $"{{\"pid\":{System.Environment.ProcessId},\"sharedMemoryPointer\":{_sharedMemoryPointer.ToInt64()},\"guestBaseOffset\":{_sharedMemoryOffset},\"wasmMemoryBase\":{wasmMemoryBase}}}";

			if (!string.IsNullOrEmpty(workspaceDirectory) && Directory.Exists(workspaceDirectory))
			{
				File.WriteAllText(Path.Combine(workspaceDirectory, "authority.pid"), pidStr);
				if (_sharedMemoryPointer != IntPtr.Zero)
				{
					File.WriteAllText(Path.Combine(workspaceDirectory, "MapScript.debug.json"), debugJson);
				}
			}

			if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
			{
				File.WriteAllText(Path.Combine(directoryName, "authority.pid"), pidStr);
				if (_sharedMemoryPointer != IntPtr.Zero)
				{
					File.WriteAllText(Path.Combine(directoryName, "MapScript.debug.json"), debugJson);
				}
			}
		}
		catch (Exception exception)
		{
			LogToConsole($"[WASM DEBUG] Failed to write debug metadata: {exception.Message}", true);
		}
	}

	private string? ResolveWorkspaceDirectory(string wasmPath)
	{
		if (!string.IsNullOrEmpty(_mapWorkspacePath) && Directory.Exists(_mapWorkspacePath))
		{
			return _mapWorkspacePath;
		}

		string? currentDirectory = Path.GetDirectoryName(Path.GetFullPath(wasmPath));
		while (!string.IsNullOrEmpty(currentDirectory))
		{
			if (File.Exists(Path.Combine(currentDirectory, "MapScript.csproj")) || Directory.Exists(Path.Combine(currentDirectory, ".vscode")))
			{
				return currentDirectory;
			}
			DirectoryInfo? parent = Directory.GetParent(currentDirectory);
			currentDirectory = parent?.FullName;
		}

		return null;
	}
}
