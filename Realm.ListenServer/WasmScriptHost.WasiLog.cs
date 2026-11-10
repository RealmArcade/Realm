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

/// <summary>buffering the guest log and flushing it to the console and file.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	/// </summary>
	private void RequestWasiLogFlush()
	{
		if (string.IsNullOrEmpty(_wasiLogPath)) return;
		if (Interlocked.Exchange(ref _logFlushQueued, 1) != 0) return;

		ThreadPool.QueueUserWorkItem(static host => ((WasmScriptHost)host!).FlushQueuedWasiLog(), this);
	}

	private void FlushQueuedWasiLog()
	{
		try
		{
			FlushWasiLogFile();
		}
		finally
		{
			Volatile.Write(ref _logFlushQueued, 0);
		}
	}

	private void FlushWasiLogFile()
	{
		if (string.IsNullOrEmpty(_wasiLogPath) || !File.Exists(_wasiLogPath)) return;
		if (Interlocked.CompareExchange(ref _logFlushInProgress, 1, 0) != 0) return;
		try
		{
			using var fs = new FileStream(_wasiLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fs.Length <= _wasiLogBytesFlushed) return;

			fs.Seek(_wasiLogBytesFlushed, SeekOrigin.Begin);
			using var reader = new StreamReader(fs, Encoding.UTF8);
			string newText = reader.ReadToEnd();
			_wasiLogBytesFlushed = fs.Position;

			if (!string.IsNullOrWhiteSpace(newText))
			{
				foreach (var rawLine in newText.Split('\n'))
				{
					string trimmed = rawLine.TrimEnd('\r');
					if (!string.IsNullOrWhiteSpace(trimmed))
					{
						LogToConsole($"[WASM STDOUT] {trimmed}");
					}
				}
			}
		}
		catch
		{

		}
		finally
		{
			Volatile.Write(ref _logFlushInProgress, 0);
		}
	}
}
