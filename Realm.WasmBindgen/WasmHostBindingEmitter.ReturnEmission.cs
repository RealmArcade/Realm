using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;


public static partial class WasmHostBindingEmitter
{
	private static void EmitBlockBody(StringBuilder sb, IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
	{
		ReadStringParameters(sb, paramInfos);
		ReadVector3Parameters(sb, paramInfos);
		ReadEntityParameters(sb, paramInfos, gameApiSymbol);

		var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam);
		string callExpr = BuildCallExpression(method, targetInstance);

		EmitMethodCallAndReturn(sb, method, retKind, gameApiSymbol, entityParams, callExpr);
	}

	private static void ReadStringParameters(StringBuilder sb, List<ParamInfo> paramInfos)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.StringListParam)
				sb.AppendLine($"            string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.FloatListParam)
				sb.AppendLine($"            float[] {p.Name} = ReadGuestFloatList(caller, {p.Name}Ptr, {p.Name}Len);");
		}
	}

	private static void ReadVector3Parameters(StringBuilder sb, List<ParamInfo> paramInfos)
	{
		var processedVec = new HashSet<string>();
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.Vector2Param && p.Name.EndsWith("X"))
			{
				string baseName = p.Name.Substring(0, p.Name.Length - 1);
				if (processedVec.Add(baseName))
					sb.AppendLine($"            var {baseName} = new System.Numerics.Vector2({baseName}X, {baseName}Y);");
			}
			else if (p.Kind == PrmKind.Vector2Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector2({p.Name}X, {p.Name}Y);");
			else if (p.Kind == PrmKind.Vector3Param && p.Name.EndsWith("X"))
			{
				string baseName = p.Name.Substring(0, p.Name.Length - 1);
				if (processedVec.Add(baseName))
					sb.AppendLine($"            var {baseName} = new System.Numerics.Vector3({baseName}X, {baseName}Y, {baseName}Z);");
			}
			else if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
			else if (p.Kind == PrmKind.Vector3NullableParam)
				sb.AppendLine($"            System.Numerics.Vector3? {p.Name} = {p.Name}HasColor != 0 ? new System.Numerics.Vector3({p.Name}R, {p.Name}G, {p.Name}B) : null;");
		}
	}

	private static void ReadEntityParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.EntityParam)
			{
				if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _))
				{
					sb.AppendLine($"            var {p.Name} = ({p.Type.ToDisplayString()}){p.Name}RawId;");
				}
				else if (WasmEntityDiscovery.IsEntityInterface(p.Type, out var unwrapped))
				{
					string res = WasmEntityDiscovery.FindResolverMember(gameApiSymbol, unwrapped);
					sb.AppendLine($"            var {p.Name} = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{res}({p.Name}RawId);");
				}
			}
		}
	}

	private static void EmitMethodCallAndReturn(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, List<ParamInfo> entityParams, string callExpr)
	{
		var nonStructEntityParams = entityParams.FindAll(p => !WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _));
		string condition = nonStructEntityParams.Count > 0 ? string.Join(" && ", nonStructEntityParams.Select(u => $"{u.Name} != null")) : "";

		switch (retKind)
		{
			case RetKind.Void:
				EmitVoidReturn(sb, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			case RetKind.StringReturn:
				EmitStringReturn(sb, callExpr);
				break;
			case RetKind.EntityListReturn:
				EmitEntityListReturn(sb, method, gameApiSymbol, callExpr);
				break;
			case RetKind.EntityReturn:
			case RetKind.EntityNullableReturn:
				EmitEntitySingleReturn(sb, method, gameApiSymbol, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			case RetKind.BoolReturn:
				EmitBoolReturn(sb, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
			default:
				EmitDefaultReturn(sb, retKind, nonStructEntityParams.Count == 0, condition, callExpr);
				break;
		}
	}

	private static void EmitVoidReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
	{
		if (noEntityParams)
			sb.AppendLine($"            {callExpr};");
		else
			sb.AppendLine($"            if ({condition}) {callExpr};");
	}

	private static void EmitStringReturn(StringBuilder sb, string callExpr)
	{
		sb.AppendLine($"            string result = {callExpr} ?? \"\";");
		sb.AppendLine("            WriteGuestString(caller, retArea, result);");
	}

	private static void EmitEntityListReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, string callExpr)
	{
		WasmEntityDiscovery.IsCollection(method.ReturnType, out var elemType);
		if (WasmEntityDiscovery.IsEntityIdStruct(elemType, out _))
		{
			var args = GetMethodCallArgs(method);
			string argsCall = string.Join(", ", args);
			sb.AppendLine($"            var api = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current);");
			sb.AppendLine($"            var items = api != null ? api.{method.Name}({argsCall}) : ReadOnlySpan<{elemType.ToDisplayString()}>.Empty;");
			sb.AppendLine($"            WriteGuestIntSpan(caller, retArea, MemoryMarshal.Cast<{elemType.ToDisplayString()}, int>(items));");
			return;
		}

		bool hasUniqueId = elemType.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
		sb.AppendLine($"            var items = {callExpr} ?? Array.Empty<{elemType.ToDisplayString()}>();");

		if (hasUniqueId)
		{
			sb.AppendLine("            WriteGuestIntList(caller, retArea, items.Select(e => e.UniqueId).ToList());");
		}
		else
		{
			string colName = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, elemType);
			sb.AppendLine($"            WriteGuestIntList(caller, retArea, items.Select(e => ((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{colName}.ToList().IndexOf(e) ?? -1).ToList());");
		}
	}

	private static void EmitEntitySingleReturn(StringBuilder sb, IMethodSymbol method, INamedTypeSymbol gameApiSymbol, bool noEntityParams, string condition, string callExpr)
	{
		if (WasmEntityDiscovery.IsEntityIdStruct(method.ReturnType, out _))
		{
			if (noEntityParams)
			{
				sb.AppendLine($"            return (int)({callExpr} ?? default);");
			}
			else
			{
				sb.AppendLine($"            if ({condition})");
				sb.AppendLine("            {");
				sb.AppendLine($"                return (int)({callExpr} ?? default);");
				sb.AppendLine("            }");
				sb.AppendLine("            return 0;");
			}
			return;
		}

		WasmEntityDiscovery.IsEntityInterface(method.ReturnType, out var unwrapped);
		bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		if (noEntityParams)
		{
			sb.AppendLine($"            var result = {callExpr};");
			EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
		}
		else
		{
			sb.AppendLine($"            if ({condition})");
			sb.AppendLine("            {");
			sb.AppendLine($"                var result = {callExpr};");
			EmitEntityReturnMapping(sb, gameApiSymbol, unwrapped, hasUniqueId, "result");
			sb.AppendLine("            }");
			sb.AppendLine($"            return {(hasUniqueId ? "0" : "-1")};");
		}
	}

	private static void EmitBoolReturn(StringBuilder sb, bool noEntityParams, string condition, string callExpr)
	{
		if (noEntityParams)
			sb.AppendLine($"            return ({callExpr} ?? false) ? 1 : 0;");
		else
			sb.AppendLine($"            return ({condition}) ? (({callExpr} ?? false) ? 1 : 0) : 0;");
	}

	private static void EmitDefaultReturn(StringBuilder sb, RetKind retKind, bool noEntityParams, string condition, string callExpr)
	{
		string defaultVal = retKind == RetKind.DirectFloat ? "0f" : "0";
		if (noEntityParams)
			sb.AppendLine($"            return {callExpr} ?? {defaultVal};");
		else
			sb.AppendLine($"            return ({condition}) ? ({callExpr} ?? {defaultVal}) : {defaultVal};");
	}

	private static void EmitEntityReturnMapping(StringBuilder sb, INamedTypeSymbol gameApiSymbol, ITypeSymbol unwrapped, bool hasUniqueId, string resultVar)
	{
		if (hasUniqueId)
		{
			sb.AppendLine($"                return {resultVar}?.UniqueId ?? 0;");
		}
		else
		{
			string colName = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, unwrapped);
			sb.AppendLine($"                return ({resultVar} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{colName}.ToList().IndexOf({resultVar}) ?? -1) : -1;");
		}
	}

	private static string BuildSimpleExpression(IMethodSymbol method, List<ParamInfo> paramInfos, RetKind retKind, string targetInstance, INamedTypeSymbol gameApiSymbol)
	{
		string callExpr = BuildCallExpression(method, targetInstance);
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			WasmEntityDiscovery.IsEntityInterface(method.ReturnType, out var unwrapped);
			bool hasUniqueId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (hasUniqueId)
			{
				return $"({callExpr})?.UniqueId ?? 0";
			}
			else
			{
				string colName = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, unwrapped);
				return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
			}
		}

		return retKind switch
		{
			RetKind.Void => callExpr,
			RetKind.DirectInt => $"(int)({callExpr} ?? 0)",
			RetKind.DirectFloat => $"{callExpr} ?? 0f",
			RetKind.BoolReturn => $"({callExpr} ?? false) ? 1 : 0",
			RetKind.StringReturn => $"{callExpr} ?? \"\"",
			_ => callExpr
		};
	}

	private static void lastWasWasMultiLine(ref bool last, bool current)
	{
		last = current;
	}

	public static string? RetKindToWit(RetKind retKind)
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
			RetKind.EntityListReturn => "list<s32>",
			_ => null
		};
	}

	public static HashSet<string> CollectPropertyAccessorNames(INamedTypeSymbol symbol)
	{
		var set = new HashSet<string>(StringComparer.Ordinal);
		foreach (var member in symbol.GetMembers())
		{
			if (member is IPropertySymbol prop)
			{
				if (prop.GetMethod != null) set.Add(prop.GetMethod.Name);
				if (prop.SetMethod != null) set.Add(prop.SetMethod.Name);
			}
		}

		return set;
	}
}
