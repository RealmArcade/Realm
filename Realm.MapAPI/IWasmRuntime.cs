namespace Realm.MapAPI;

/// <summary>
/// Represents a WebAssembly host runtime executing a guest Wasm module.
/// </summary>
public interface IWasmRuntime : IMapScript
{
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