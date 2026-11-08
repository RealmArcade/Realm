using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;


internal static partial class WasmHostBindingEmitter
{
	internal static void EmitEntityMethodBinding(StringBuilder sb, IMethodSymbol method, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		var retKind = WasmTypeClassifier.ClassifyReturn(method.ReturnType);
		if (!IsValidReturnType(retKind, method.ReturnType)) return;

		string witName = tKebab + "-" + WitIdentifierFormat.ToKebabCase(method.Name);
		if (!definedFunctions.Add(witName)) return;

		string resolver = WasmEntityDiscovery.FindResolverMember(gameApiSymbol, entitySymbol);

		if (retKind == RetKind.StringListReturn)
		{
			EmitEntityStringListMethod(sb, witName, resolver, method, ref lastWasMultiLine, definedFunctions, methodNames);
			return;
		}

		var paramInfos = ExtractParamInfos(method);
		if (paramInfos == null) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(witName);

		bool needsRetArea = retKind == RetKind.StringReturn;
		string lambdaParamsStr = BuildEntityLambdaParams(method, paramInfos, needsRetArea);

		if (lastWasMultiLine) sb.AppendLine();
		sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParamsStr}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({methodIndex});");
		sb.AppendLine($"            var hostTarget = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id);");

		string defaultReturn = GetDefaultReturnValue(method, retKind, needsRetArea);
		if (!string.IsNullOrEmpty(defaultReturn))
			sb.AppendLine($"            if (hostTarget == null) return {defaultReturn};");
		else
			sb.AppendLine($"            if (hostTarget == null) return;");

		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		EmitEntityMethodCall(sb, method, retKind, gameApiSymbol, needsRetArea);

		if (!string.IsNullOrEmpty(defaultReturn))
			sb.AppendLine($"            return {defaultReturn};");

		sb.AppendLine("        });");
		lastWasMultiLine = true;
	}

	internal static bool IsValidReturnType(RetKind retKind, ITypeSymbol returnType)
	{
		if (retKind == RetKind.EntityListReturn) return false;
		if (retKind != RetKind.Unsupported) return true;
		return returnType.ToDisplayString() == "System.Numerics.Vector3" || returnType.SpecialType == SpecialType.System_Void;
	}

	internal static string GetDefaultReturnValue(IMethodSymbol method, RetKind retKind, bool needsRetArea)
	{
		if (method.ReturnType.SpecialType == SpecialType.System_Void || needsRetArea) return string.Empty;
		return retKind == RetKind.DirectFloat ? "0f" : "0";
	}

	internal static List<ParamInfo>? ExtractParamInfos(IMethodSymbol method)
	{
		var paramInfos = new List<ParamInfo>();
		foreach (var param in method.Parameters)
		{
			var kind = WasmTypeClassifier.ClassifyParam(param.Type);
			if (kind == PrmKind.Unsupported) return null;
			paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
		}

		return paramInfos;
	}

	internal static string BuildEntityLambdaParams(IMethodSymbol method, List<ParamInfo> paramInfos, bool needsRetArea)
	{
		bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
		bool hasListParam = method.Parameters.Any(p => WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.StringListParam || WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.FloatListParam);
		bool needsCaller = hasStringParam || hasListParam || needsRetArea;

		var lambdaParams = new List<string>();
		if (needsCaller) lambdaParams.Add("Caller caller");
		lambdaParams.Add("int id");
		lambdaParams.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
		if (needsRetArea) lambdaParams.Add("int retArea");

		return string.Join(", ", lambdaParams);
	}

	internal static void EmitEntityStringListMethod(StringBuilder sb, string witName, string resolver, IMethodSymbol method, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string countName = $"{witName}-count";
		if (definedFunctions.Add(countName))
		{
			int countIdx = methodNames.Count;
			methodNames.Add(countName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", (int id) =>");
			sb.AppendLine("        {");
			sb.AppendLine($"            using var _scope = BeginProfileApiCall({countIdx});");
			sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id);");
			sb.AppendLine($"            return target?.{method.Name}().Count() ?? 0;");
			sb.AppendLine("        });");
		}

		string getName = $"{witName}-get";
		if (definedFunctions.Add(getName))
		{
			int getIdx = methodNames.Count;
			methodNames.Add(getName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{getName}\", (Caller caller, int id, int index, int retArea) =>");
			sb.AppendLine("        {");
			sb.AppendLine($"            using var _scope = BeginProfileApiCall({getIdx});");
			sb.AppendLine($"            var target = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id);");
			sb.AppendLine($"            string s = target?.{method.Name}().ElementAtOrDefault(index) ?? \"\";");
			sb.AppendLine("            WriteGuestString(caller, retArea, s);");
			sb.AppendLine("        });");
		}
		lastWasMultiLine = true;
	}

	internal static string GetLambdaParamDefinition(ParamInfo p)
	{
		return p.Kind switch
		{
			PrmKind.StringParam => $"int {p.Name}Ptr, int {p.Name}Len",
			PrmKind.StringListParam => $"int {p.Name}Ptr, int {p.Name}Len",
			PrmKind.FloatListParam => $"int {p.Name}Ptr, int {p.Name}Len",
			PrmKind.DirectFloat => $"float {p.Name}",
			PrmKind.DirectInt or PrmKind.BoolParam => $"int {p.Name}",
			PrmKind.EntityParam => $"int {p.Name}RawId",
			PrmKind.Vector2Param => $"float {p.Name}X, float {p.Name}Y",
			PrmKind.Vector3Param => $"float {p.Name}X, float {p.Name}Y, float {p.Name}Z",
			PrmKind.Vector3NullableParam => $"float {p.Name}R, float {p.Name}G, float {p.Name}B, int {p.Name}HasColor",
			_ => ""
		};
	}

	internal static void EmitEntityMethodParameters(StringBuilder sb, List<ParamInfo> paramInfos, INamedTypeSymbol gameApiSymbol)
	{
		foreach (var p in paramInfos)
		{
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"                string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.StringListParam)
				sb.AppendLine($"                string[] {p.Name} = ReadGuestStringList(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.FloatListParam)
				sb.AppendLine($"                float[] {p.Name} = ReadGuestFloatList(caller, {p.Name}Ptr, {p.Name}Len);");
			else if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"                var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
			else if (p.Kind == PrmKind.EntityParam)
			{
				if (WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _))
				{
					sb.AppendLine($"                var {p.Name} = ({p.Type.ToDisplayString()}){p.Name}RawId;");
				}
				else if (WasmEntityDiscovery.IsEntityInterface(p.Type, out var unwrapped))
				{
					string res = WasmEntityDiscovery.FindResolverMember(gameApiSymbol, unwrapped);
					sb.AppendLine($"                var {p.Name} = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{res}({p.Name}RawId);");
				}
			}
		}
	}

	internal static void EmitEntityMethodCall(StringBuilder sb, IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, bool needsRetArea)
	{
		string callExpr = BuildEntityMethodCallExpression(method);

		if (method.ReturnType.SpecialType == SpecialType.System_Void)
		{
			sb.AppendLine($"                {callExpr};");
			return;
		}

		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"                string s = {callExpr} ?? \"\";");
			sb.AppendLine("                WriteGuestString(caller, retArea, s);");
			return;
		}

		string exprMapped = MapEntityMethodReturnExpression(method, retKind, gameApiSymbol, callExpr);
		sb.AppendLine($"                return {exprMapped};");
	}

	internal static string BuildEntityMethodCallExpression(IMethodSymbol method)
	{
		var callArgs = new List<string>();
		foreach (var p in method.Parameters)
		{
			if (p.Type.SpecialType == SpecialType.System_Boolean)
				callArgs.Add($"{p.Name} != 0");
			else if (p.Type.TypeKind == TypeKind.Enum)
				callArgs.Add($"({p.Type.ToDisplayString()}){p.Name}");
			else
				callArgs.Add(p.Name);
		}

		return $"hostTarget.{method.Name}({string.Join(", ", callArgs)})";
	}

	internal static string MapEntityMethodReturnExpression(IMethodSymbol method, RetKind retKind, INamedTypeSymbol gameApiSymbol, string callExpr)
	{
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			WasmEntityDiscovery.IsEntityInterface(method.ReturnType, out var unwrapped);
			bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (targetHasId)
			{
				return $"({callExpr})?.UniqueId ?? 0";
			}

			string colName = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, unwrapped);
			return $"({callExpr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{colName}.ToList().IndexOf({callExpr}) ?? -1) : -1";
		}

		return retKind switch
		{
			RetKind.BoolReturn => $"({callExpr}) ? 1 : 0",
			_ => callExpr
		};
	}
}
