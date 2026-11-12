using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmWitDocumentWriter
{
	public static string GenerateWitContent(INamedTypeSymbol gameApiSymbol, HashSet<INamedTypeSymbol> entityInterfaces, string manualFunctions)
	{
		var functions = new SortedDictionary<string, string>(StringComparer.Ordinal);

		AppendWitGameApiMembers(functions, gameApiSymbol);
		AppendWitEntityInterfaces(functions, entityInterfaces);

		var witBody = new StringBuilder();
		foreach (var kvp in functions)
		{
			witBody.AppendLine($"    {kvp.Key}: {kvp.Value};");
		}

		if (!string.IsNullOrEmpty(manualFunctions))
		{
			witBody.AppendLine();
			witBody.AppendLine("    // Manual WIT entries");
			witBody.AppendLine("    " + manualFunctions.Replace("\r\n", "\n").Replace("\n", "\n    "));
		}

		var wit = new StringBuilder();
		wit.AppendLine("package custom:game;");
		wit.AppendLine();
		wit.AppendLine("interface game-api {");
		wit.Append(witBody.ToString());
		wit.AppendLine("}");
		wit.AppendLine();
		wit.AppendLine("world game-client {");
		wit.AppendLine("    import game-api;");
		wit.AppendLine("}");

		return wit.ToString();
	}

	private static void AppendWitGameApiMembers(SortedDictionary<string, string> functions, INamedTypeSymbol gameApiSymbol)
	{
		var propertyAccessorMethods = WasmHostBindingEmitter.CollectPropertyAccessorNames(gameApiSymbol);

		foreach (var member in gameApiSymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
		{
			if (member is IEventSymbol || !member.IsAbstract) continue;

			if (member is IPropertySymbol property)
			{
				AppendWitProperty(functions, property);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessorMethods.Contains(method.Name))
				{
					AppendWitMethod(functions, method);
				}
			}
		}
	}

	private static void AppendWitEntityInterfaces(SortedDictionary<string, string> functions, HashSet<INamedTypeSymbol> entityInterfaces)
	{
		foreach (var entityIface in entityInterfaces.OrderBy(e => e.Name, StringComparer.Ordinal))
		{
			AppendWitEntityInterface(functions, entityIface);
		}
	}

	private static void AppendWitEntityInterface(SortedDictionary<string, string> functions, INamedTypeSymbol entitySymbol)
	{
		string tKebab = WitIdentifierFormat.ToKebabCase(WitIdentifierFormat.CleanInterfaceName(entitySymbol));
		bool hasUniqueId = entitySymbol.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		if (!hasUniqueId)
		{
			AppendFunction(functions, $"{tKebab}-count", "func() -> s32");
		}

		var propertyAccessors = WasmHostBindingEmitter.CollectPropertyAccessorNames(entitySymbol);
		foreach (var member in entitySymbol.GetMembers().OrderBy(m => m.Name, StringComparer.Ordinal))
		{
			if (!member.IsAbstract) continue;

			if (member is IPropertySymbol property && property.Name != "UniqueId")
			{
				AppendWitEntityProperties(functions, property, tKebab);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessors.Contains(method.Name))
				{
					AppendWitEntityMethod(functions, method, tKebab);
				}
			}
		}
	}

	private static void AppendWitEntityProperties(SortedDictionary<string, string> functions, IPropertySymbol property, string tKebab)
	{
		string witBase = $"{tKebab}-{WitIdentifierFormat.ToKebabCase(property.Name)}";

		if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
		{
			AppendFunction(functions, $"{witBase}-x", "func(id: s32) -> f32");
			AppendFunction(functions, $"{witBase}-y", "func(id: s32) -> f32");
			AppendFunction(functions, $"{witBase}-z", "func(id: s32) -> f32");

			if (property.SetMethod != null)
			{
				AppendFunction(functions, $"set-{witBase}", "func(id: s32, val-x: f32, val-y: f32, val-z: f32)");
			}

			return;
		}

		var retKind = WasmTypeClassifier.ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported) return;

		string? witType = WasmHostBindingEmitter.RetKindToWit(retKind);
		if (witType != null)
		{
			AppendFunction(functions, witBase, $"func(id: s32) -> {witType}");
			if (property.SetMethod != null)
			{
				string? paramType = RetKindToWitParam(retKind);
				if (paramType != null)
					AppendFunction(functions, $"set-{witBase}", $"func(id: s32, val: {paramType})");
			}
		}
	}

	private static void AppendWitEntityMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string tKebab)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var retKind = WasmTypeClassifier.ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";

		if (retKind == RetKind.Unsupported && !isVector3Ret && method.ReturnType.SpecialType != SpecialType.System_Void) return;
		if (retKind == RetKind.EntityListReturn) return;

		string? paramsStr = BuildWitEntityMethodParams(method);
		if (paramsStr == null) return;

		string witName = tKebab + "-" + WitIdentifierFormat.ToKebabCase(method.Name);

		if (isVector3Ret)
		{
			AppendWitVector3Method(functions, witName, paramsStr);
			return;
		}

		if (retKind == RetKind.StringListReturn)
		{
			AppendWitStringListMethod(functions, witName, paramsStr);
			return;
		}

		AppendWitStandardMethod(functions, method, witName, paramsStr, retKind);
	}

	private static void AppendWitVector3Method(SortedDictionary<string, string> functions, string witName, string paramsStr)
	{
		AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
		AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
		AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
	}

	private static void AppendWitStringListMethod(SortedDictionary<string, string> functions, string witName, string paramsStr)
	{
		AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
		AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
	}

	private static void AppendWitStandardMethod(SortedDictionary<string, string> functions, IMethodSymbol method, string witName, string paramsStr, RetKind retKind)
	{
		string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {WasmHostBindingEmitter.RetKindToWit(retKind)}";
		AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
	}

	public static string? BuildWitEntityMethodParams(IMethodSymbol method)
	{
		var witParams = new List<string> { "id: s32" };
		foreach (var param in method.Parameters)
		{
			string? paramWit = ParamToWit(param.Type, param.Name);
			if (paramWit == null) return null;
			witParams.Add(paramWit);
		}

		return string.Join(", ", witParams);
	}

	private static void AppendWitProperty(SortedDictionary<string, string> functions, IPropertySymbol property)
	{
		var retKind = WasmTypeClassifier.ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported)
			return;

		string witBase = WitIdentifierFormat.ToKebabCase(property.Name);
		string? witType = WasmHostBindingEmitter.RetKindToWit(retKind);
		if (witType == null)
			return;

		if (property.GetMethod != null)
			AppendFunction(functions, $"get-{witBase}", $"func() -> {witType}");

		if (property.SetMethod != null)
		{
			string? paramType = RetKindToWitParam(retKind);
			if (paramType != null)
				AppendFunction(functions, $"set-{witBase}", $"func(val: {paramType})");
		}
	}

	private static void AppendWitMethod(SortedDictionary<string, string> functions, IMethodSymbol method)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var retKind = WasmTypeClassifier.ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
		if (retKind == RetKind.Unsupported && !isVector3Ret) return;

		string? paramsStr = BuildWitMethodParams(method);
		if (paramsStr == null && method.Parameters.Length > 0) return;
		paramsStr ??= "";

		string witName = WitIdentifierFormat.ToKebabCase(method.Name);

		if (retKind == RetKind.StringListReturn)
		{
			AppendFunction(functions, $"{witName}-count", $"func({paramsStr}) -> s32");
			AppendFunction(functions, $"{witName}-get", $"func({paramsStr}, index: s32) -> string");
		}
		else if (isVector3Ret)
		{
			AppendFunction(functions, $"{witName}-x", $"func({paramsStr}) -> f32");
			AppendFunction(functions, $"{witName}-y", $"func({paramsStr}) -> f32");
			AppendFunction(functions, $"{witName}-z", $"func({paramsStr}) -> f32");
		}
		else
		{
			string retStr = (retKind == RetKind.Void || method.ReturnType.SpecialType == SpecialType.System_Void) ? "" : $" -> {WasmHostBindingEmitter.RetKindToWit(retKind)}";
			AppendFunction(functions, witName, $"func({paramsStr}){retStr}");
		}
	}

	public static string? BuildWitMethodParams(IMethodSymbol method)
	{
		if (method.Parameters.Length == 0) return "";
		var witParams = new List<string>();
		foreach (var param in method.Parameters)
		{
			string? paramWit = ParamToWit(param.Type, param.Name);
			if (paramWit == null) return null;
			witParams.Add(paramWit);
		}

		return string.Join(", ", witParams);
	}

	private static string? ParamToWit(ITypeSymbol type, string name)
	{
		var kind = WasmTypeClassifier.ClassifyParam(type);
		string paramName = WitIdentifierFormat.EscapeWitIdentifier(name);
		return kind switch
		{
			PrmKind.DirectInt => $"{paramName}: s32",
			PrmKind.DirectFloat => $"{paramName}: f32",
			PrmKind.BoolParam => $"{paramName}: bool",
			PrmKind.StringParam => $"{paramName}: string",
			PrmKind.StringListParam => $"{paramName}: list<string>",
			PrmKind.FloatListParam => $"{paramName}: list<f32>",
			PrmKind.Vector2Param => $"{paramName}-x: f32, {paramName}-y: f32",
			PrmKind.Vector3Param => $"{paramName}-x: f32, {paramName}-y: f32, {paramName}-z: f32",
			PrmKind.EntityParam => paramName.EndsWith("-id", StringComparison.OrdinalIgnoreCase) ? $"{paramName}: s32" : $"{paramName}-id: s32",
			PrmKind.Vector3NullableParam => $"{paramName}-r: f32, {paramName}-g: f32, {paramName}-b: f32, {paramName}-has-color: bool",
			_ => null
		};
	}

	private static string? RetKindToWitParam(RetKind retKind)
	{
		return retKind switch
		{
			RetKind.Void => null,
			RetKind.DirectInt => "s32",
			RetKind.DirectFloat => "f32",
			RetKind.BoolReturn => "bool",
			RetKind.StringReturn => "string",
			RetKind.EntityReturn => "s32",
			RetKind.EntityNullableReturn => "s32",
			_ => null
		};
	}

	private static void AppendFunction(SortedDictionary<string, string> functions, string name, string body)
	{
		if (!functions.ContainsKey(name))
		{
			functions.Add(name, body);
		}
	}
}
