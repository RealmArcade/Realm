using System;

namespace Realm.MapAPI;

/// <summary>
/// Represents a WebAssembly host runtime executing a guest Wasm module.
/// </summary>
public interface IWasmRuntime : IMapScript
{
	/// <summary>
	/// Gets the linear memory offset of the shared memory region in guest WebAssembly linear memory.
	/// </summary>
	int SharedMemoryOffset { get; }

	/// <summary>
	/// Gets a span slice pointing to the shared player slots table in WebAssembly linear memory.
	/// </summary>
	/// <returns>A span of <see cref="SharedPlayerSlot"/> structs.</returns>
	Span<SharedPlayerSlot> GetSharedPlayerSlotsSpan();

	/// <summary>
	/// Gets a span slice pointing to the shared entity slots table in WebAssembly linear memory.
	/// </summary>
	/// <returns>A span of <see cref="SharedEntitySlot"/> structs.</returns>
	Span<SharedEntitySlot> GetSharedEntitySlotsSpan();

	/// <summary>
	/// Gets the host pointer address to the shared linear memory region in WebAssembly linear memory.
	/// </summary>
	/// <returns>The native host memory pointer address.</returns>
	IntPtr GetSharedMemoryPointer();

	/// <summary>
	/// Executes an exported custom formula function from the WebAssembly guest module.
	/// </summary>
	/// <param name="functionName">The name of the exported function in the WebAssembly module.</param>
	/// <param name="baseValue">The current base value of the stat.</param>
	/// <param name="modifierValue">The configured value of the modifier.</param>
	/// <param name="level">The source level of the modifier.</param>
	/// <returns>The calculated bonus stat value, or zero if execution fails.</returns>
	float ExecuteCustomFormula(string functionName, float baseValue, float modifierValue, int level);
}
