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

/// <summary>reading and writing guest memory for the marshaling helpers.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	private int AllocateInGuest(int size)
	{
		if (_cabiRealloc == null) throw new InvalidOperationException("cabi_realloc is not exported by guest.");
		return (int)_cabiRealloc.Invoke(0, 0, 4, size)!;
	}

	private string ReadGuestString(Caller caller, int address, int length)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null || length == 0) return "";
		return memory.ReadString(address, length, Encoding.UTF8);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteInt32(Memory memory, int address, int value)
	{
		Span<byte> span = memory.GetSpan(address, 4);
		BinaryPrimitives.WriteInt32LittleEndian(span, value);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private int ReadInt32(Memory memory, int address)
	{
		return BinaryPrimitives.ReadInt32LittleEndian(memory.GetSpan(address, 4));
	}

	private void WriteGuestString(Caller caller, int retAreaAddress, string value)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		byte[] bytes = Encoding.UTF8.GetBytes(value);
		int ptr = AllocateInGuest(bytes.Length);
		bytes.CopyTo(memory.GetSpan(ptr, bytes.Length));

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, bytes.Length);
	}

	private void WriteGuestIntList(Caller caller, int retAreaAddress, List<int> list)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		int size = list.Count * 4;
		int ptr = AllocateInGuest(size);
		Span<byte> destSpan = memory.GetSpan(ptr, size);
		for (int i = 0; i < list.Count; i++)
		{
			BinaryPrimitives.WriteInt32LittleEndian(destSpan.Slice(i * 4, 4), list[i]);
		}

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, list.Count);
	}

	private void WriteGuestIntSpan(Caller caller, int retAreaAddress, ReadOnlySpan<int> span)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null) return;

		int size = span.Length * 4;
		int ptr = size > 0 ? AllocateInGuest(size) : 0;
		if (size > 0)
		{
			Span<byte> destSpan = memory.GetSpan(ptr, size);
			System.Runtime.InteropServices.MemoryMarshal.AsBytes(span).CopyTo(destSpan);
		}

		WriteInt32(memory, retAreaAddress, ptr);
		WriteInt32(memory, retAreaAddress + 4, span.Length);
	}

	private string[] ReadGuestStringList(Caller caller, int address, int count)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null || count <= 0) return Array.Empty<string>();

		var result = new string[count];
		for (int i = 0; i < count; i++)
		{
			int strPtr = ReadInt32(memory, address + i * 8);
			int strLen = ReadInt32(memory, address + i * 8 + 4);
			result[i] = memory.ReadString(strPtr, strLen, Encoding.UTF8);
		}

		return result;
	}

	private float[] ReadGuestFloatList(Caller caller, int address, int count)
	{
		var memory = caller.GetMemory("memory");
		if (memory == null || count <= 0) return Array.Empty<float>();

		ReadOnlySpan<byte> source = memory.GetSpan(address, count * 4);
		var result = new float[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = BinaryPrimitives.ReadSingleLittleEndian(source.Slice(i * 4, 4));
		}

		return result;
	}
}
