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

/// <summary>the debug chat command, formula evaluation, shared memory, seeds and snapshot import.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	private void HandleDebugChatCommand(string message, UnitId senderUnitId, int senderPlayerIndex)
	{
		if (string.Equals(message, "/wasmperf", StringComparison.OrdinalIgnoreCase) ||
		    string.Equals(message, "/wasmprofile", StringComparison.OrdinalIgnoreCase))
		{
			LogApiCallProfilingBreakdown();
			return;
		}

		if (string.Equals(message, "/strings", StringComparison.OrdinalIgnoreCase))
		{
			LogToConsole(Realm.Ecs.StringRegistry.DescribeTable());
		}
	}

	public float Evaluate(in FormulaContext context, float baseValue, float modifierValue, int level)
	{
		return ExecuteCustomFormula(context.WASMFunctionName, baseValue, modifierValue, level);
	}

	public IntPtr GetSharedMemoryPointer()
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0) return IntPtr.Zero;
		Span<byte> byteSpan = memory.GetSpan(_sharedMemoryOffset, 1);
		if (byteSpan.IsEmpty) return IntPtr.Zero;
		unsafe
		{
			return (IntPtr)Unsafe.AsPointer(ref byteSpan[0]);
		}
	}

	/// <summary>
	///     States the seed this script draws from. A module the lobby warmed is instantiated before its match has a seed,
	///     so the match restates it here before the script initializes.
	/// </summary>
	/// <param name="seed">The seed the match's script draws from.</param>
	public void PublishRandomSeed(int seed)
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0) return;

		WriteInt32(memory, _sharedMemoryOffset + Realm.Ecs.EcsSharedMemory.RANDOM_SEED_OFFSET, seed);
		LogToConsole($"[WASM RUNTIME] Published deterministic random seed {seed} to the map script.");
	}

	private void PublishSeededRandomSeed()
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0) return;

		int seed = _randomSeed;
		WriteInt32(memory, _sharedMemoryOffset + Realm.Ecs.EcsSharedMemory.RANDOM_SEED_OFFSET, seed);
		LogToConsole($"[WASM RUNTIME] Published deterministic random seed {seed} to the map script.");
	}

	public unsafe void ImportBinarySnapshot(ReadOnlySpan<byte> snapshotPayload)
	{
		var memory = _instance.GetMemory("memory");
		if (memory == null || _sharedMemoryOffset <= 0 || snapshotPayload.IsEmpty) return;

		int totalSharedMemorySize = EcsSharedMemory.TOTAL_SIZE_BYTES;

		if (snapshotPayload.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(snapshotPayload.Slice(0, 4)) == 0x53434552)
		{
			int freeListCount = BinaryPrimitives.ReadInt32LittleEndian(snapshotPayload.Slice(16, 4));
			int offset = 32 + (freeListCount * sizeof(int));
			int payloadSharedBytes = Math.Min(snapshotPayload.Length - offset, totalSharedMemorySize);

			if (payloadSharedBytes > 0)
			{
				ReadOnlySpan<byte> sharedSrc = snapshotPayload.Slice(offset, payloadSharedBytes);
				Span<byte> memorySpan = memory.GetSpan(_sharedMemoryOffset, payloadSharedBytes);
				sharedSrc.CopyTo(memorySpan);

				if (payloadSharedBytes < totalSharedMemorySize)
				{
					int remaining = totalSharedMemorySize - payloadSharedBytes;
					Span<byte> remainingSpan = memory.GetSpan(_sharedMemoryOffset + payloadSharedBytes, remaining);
					remainingSpan.Clear();
				}
				return;
			}
		}

		int bytesToCopy = Math.Min(snapshotPayload.Length, totalSharedMemorySize);
		Span<byte> destSpan = memory.GetSpan(_sharedMemoryOffset, bytesToCopy);
		snapshotPayload.Slice(0, bytesToCopy).CopyTo(destSpan);

		if (bytesToCopy < totalSharedMemorySize)
		{
			Span<byte> tailSpan = memory.GetSpan(_sharedMemoryOffset + bytesToCopy, totalSharedMemorySize - bytesToCopy);
			tailSpan.Clear();
		}
	}
}
