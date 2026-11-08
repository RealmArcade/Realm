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

/// <summary>executing a map supplied custom formula.</summary>
public partial class WasmScriptHost : IWasmRuntime, IWasmEvaluationEngine, IDisposable
	{
	public float ExecuteCustomFormula(string functionName, float baseValue, float modifierValue, int level)
	{
		if (IsFaulted || string.IsNullOrWhiteSpace(functionName))
		{
			return 0f;
		}

		if (!_formulaFuncCache.TryGetValue(functionName, out var funcDelegate))
		{
			var func = _instance.GetFunction(functionName);
			if (func == null)
			{
				_formulaFuncCache[functionName] = null;
				return 0f;
			}

			try
			{
				funcDelegate = func.WrapFunc<float, float, int, float>();
			}
			catch
			{
				funcDelegate = (b, m, l) =>
				{
					var result = func.Invoke(b, m, l);
					if (result is float f) return f;
					if (result is int i) return (float)i;
					if (result is double d) return (float)d;
					return Convert.ToSingle(result);
				};
			}

			_formulaFuncCache[functionName] = funcDelegate;
		}

		if (funcDelegate == null)
		{
			return 0f;
		}

		return ExecuteGuardedFunc(() => funcDelegate(baseValue, modifierValue, level), 0f, $"custom formula '{functionName}'");
	}
}
