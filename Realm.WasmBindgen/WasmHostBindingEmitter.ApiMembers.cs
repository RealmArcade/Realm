using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;


public static partial class WasmHostBindingEmitter
{
	private static void EmitPropertyBindings(StringBuilder sb, IPropertySymbol property, ref bool lastWasMultiLine, string targetInstance, HashSet<string> definedFunctions, List<string> methodNames)
	{
		var retKind = WasmTypeClassifier.ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported || RetKindToWit(retKind) == null)
			return;

		string witBase = WitIdentifierFormat.ToKebabCase(property.Name);

		EmitPropertyGetter(sb, property, witBase, retKind, targetInstance, ref lastWasMultiLine, definedFunctions, methodNames);
		EmitPropertySetter(sb, property, witBase, targetInstance, definedFunctions, methodNames);
	}

	private static void EmitPropertyGetter(StringBuilder sb, IPropertySymbol property, string witBase, RetKind retKind, string targetInstance, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.GetMethod == null) return;

		string getWitBase = $"get-{witBase}";
		if (!definedFunctions.Add(getWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(getWitBase);

		if (lastWasMultiLine) sb.AppendLine();
		string expr = BuildPropertyGetterExpression(property.Name, retKind, targetInstance);

		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", (Caller caller, int retArea) => {{ using var _scope = BeginProfileApiCall({methodIndex}); string s = {expr}; WriteGuestString(caller, retArea, s); }});");
		}
		else
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"get-{witBase}\", () => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {expr}; }});");
		}

		lastWasMultiLine = false;
	}

	private static void EmitPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string targetInstance, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.SetMethod == null) return;

		string setWitBase = $"set-{witBase}";
		if (!definedFunctions.Add(setWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(setWitBase);

		if (property.Type.SpecialType == SpecialType.System_Boolean)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val != 0; }});");
		else if (property.Type.SpecialType == SpecialType.System_Single)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (float val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_Int32)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_String)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int valPtr, int valLen) => {{ using var _scope = BeginProfileApiCall({methodIndex}); if ({targetInstance} != null) {targetInstance}.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
	}

	private static string BuildPropertyGetterExpression(string propertyName, RetKind retKind, string targetInstance)
	{
		return retKind switch
		{
			RetKind.DirectFloat => $"{targetInstance}?.{propertyName} ?? 0f",
			RetKind.DirectInt => $"(int)({targetInstance}?.{propertyName} ?? 0)",
			RetKind.BoolReturn => $"({targetInstance}?.{propertyName} ?? false) ? 1 : 0",
			RetKind.StringReturn => $"{targetInstance}?.{propertyName} ?? \"\"",
			_ => "0"
		};
	}

	private static void EmitMethodBinding(StringBuilder sb, IMethodSymbol method, ref bool lastWasMultiLine, string targetInstance, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (method.Parameters.Any(p => p.RefKind != RefKind.None)) return;

		var paramInfos = new List<ParamInfo>();
		foreach (var param in method.Parameters)
		{
			var kind = WasmTypeClassifier.ClassifyParam(param.Type);
			if (kind == PrmKind.Unsupported) return;
			paramInfos.Add(new ParamInfo(param.Name, kind, param.Type));
		}

		var retKind = WasmTypeClassifier.ClassifyReturn(method.ReturnType);
		bool isVector3Ret = method.ReturnType.ToDisplayString() == "System.Numerics.Vector3";
		if (retKind == RetKind.Unsupported && !isVector3Ret) return;

		var witName = WitIdentifierFormat.ToKebabCase(method.Name);
		if (!definedFunctions.Add(witName)) return;

		if (retKind == RetKind.StringListReturn)
		{
			EmitStringListMethodBinding(sb, method, witName, paramInfos, targetInstance, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
			return;
		}

		EmitStandardMethodBinding(sb, method, witName, paramInfos, retKind, isVector3Ret, targetInstance, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
	}

	private static void EmitStringListMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string countName = $"{witName}-count";
		int countIdx = methodNames.Count;
		methodNames.Add(countName);

		var lambdaParamsCount = BuildLambdaParams(paramInfos, paramInfos.Exists(p => p.Kind == PrmKind.StringParam), false);
		sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", ({lambdaParamsCount}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({countIdx});");
		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		string callExprCount = BuildCallExpression(method, targetInstance);
		sb.AppendLine($"            return {callExprCount}?.Count() ?? 0;");
		sb.AppendLine("        });");

		string getName = $"{witName}-get";
		int getIdx = methodNames.Count;
		methodNames.Add(getName);

		var paramInfosGet = new List<ParamInfo>(paramInfos);
		paramInfosGet.Add(new ParamInfo("index", PrmKind.DirectInt, gameApiSymbol));
		var lambdaParamsGet = BuildLambdaParams(paramInfosGet, needsCaller: true, needsRetArea: true);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{getName}\", ({lambdaParamsGet}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({getIdx});");
		EmitEntityMethodParameters(sb, paramInfos, gameApiSymbol);
		string callExprGet = BuildCallExpression(method, targetInstance);
		sb.AppendLine($"            string s = {callExprGet}?.ElementAtOrDefault(index) ?? \"\";");
		sb.AppendLine("            WriteGuestString(caller, retArea, s);");
		sb.AppendLine("        });");

		lastWasMultiLine = true;
	}

	private static void EmitStandardMethodBinding(StringBuilder sb, IMethodSymbol method, string witName, List<ParamInfo> paramInfos, RetKind retKind, bool isVector3Ret, string targetInstance, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		bool hasStringParam = method.Parameters.Any(p => p.Type.SpecialType == SpecialType.System_String);
		bool hasListParam = method.Parameters.Any(p => WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.StringListParam || WasmTypeClassifier.ClassifyParam(p.Type) == PrmKind.FloatListParam);
		bool needsRetArea = !isVector3Ret && (retKind == RetKind.StringReturn || retKind == RetKind.EntityListReturn);
		bool needsCaller = hasStringParam || hasListParam || needsRetArea;

		var lambdaParams = BuildLambdaParams(paramInfos, needsCaller, needsRetArea);

		if (CanUseExpressionBody(method, isVector3Ret, hasStringParam, hasListParam, needsRetArea, retKind))
		{
			int methodIndex = methodNames.Count;
			methodNames.Add(witName);
			string expr = BuildSimpleExpression(method, paramInfos, retKind, targetInstance, gameApiSymbol);
			if (lastWasMultiLine) sb.AppendLine();
			if (method.ReturnType.SpecialType == SpecialType.System_Void)
			{
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) => {{ using var _scope = BeginProfileApiCall({methodIndex}); {expr}; }});");
			}
			else
			{
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {expr}; }});");
			}
			lastWasWasMultiLine(ref lastWasMultiLine, false);
			return;
		}

		if (lastWasMultiLine) sb.AppendLine();

		if (isVector3Ret)
		{
			EmitVector3AxisBinding(sb, witName, "x", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			EmitVector3AxisBinding(sb, witName, "y", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			EmitVector3AxisBinding(sb, witName, "z", lambdaParams, paramInfos, method, targetInstance, gameApiSymbol, methodNames);
			lastWasWasMultiLine(ref lastWasMultiLine, true);
			return;
		}

		int blockIndex = methodNames.Count;
		methodNames.Add(witName);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{witName}\", ({lambdaParams}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({blockIndex});");
		EmitBlockBody(sb, method, paramInfos, retKind, targetInstance, gameApiSymbol);
		sb.AppendLine("        });");
		sb.AppendLine();
		lastWasWasMultiLine(ref lastWasMultiLine, true);
	}

	private static bool CanUseExpressionBody(IMethodSymbol method, bool isVector3Ret, bool hasStringParam, bool hasListParam, bool needsRetArea, RetKind retKind)
	{
		bool hasVector2Param = method.Parameters.Any(p => p.Type.ToDisplayString() == "System.Numerics.Vector2");
		bool hasVector3Param = method.Parameters.Any(p => p.Type.ToDisplayString() == "System.Numerics.Vector3");
		bool hasVector3NullableParam = method.Parameters.Any(p => WasmTypeClassifier.IsNullableVector3(p.Type));
		bool hasUnitParam = method.Parameters.Any(p => WasmEntityDiscovery.IsEntityInterface(p.Type, out _) || WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _));

		return !isVector3Ret && !hasStringParam && !hasListParam && !hasVector2Param && !hasVector3Param && !hasVector3NullableParam && !hasUnitParam && !needsRetArea && retKind != RetKind.EntityReturn && retKind != RetKind.EntityNullableReturn;
	}

	private static void EmitVector3AxisBinding(StringBuilder sb, string witName, string axis, string lambdaParams, List<ParamInfo> paramInfos, IMethodSymbol method, string targetInstance, INamedTypeSymbol gameApiSymbol, List<string> methodNames)
	{
		string axisName = $"{witName}-{axis}";
		int methodIndex = methodNames.Count;
		methodNames.Add(axisName);

		sb.AppendLine($"        _linker.DefineFunction(mod, \"{axisName}\", ({lambdaParams}) =>");
		sb.AppendLine("        {");
		sb.AppendLine($"            using var _scope = BeginProfileApiCall({methodIndex});");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.StringParam)
				sb.AppendLine($"            string {p.Name} = ReadGuestString(caller, {p.Name}Ptr, {p.Name}Len);");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.Vector2Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector2({p.Name}X, {p.Name}Y);");
		foreach (var p in paramInfos)
			if (p.Kind == PrmKind.Vector3Param)
				sb.AppendLine($"            var {p.Name} = new System.Numerics.Vector3({p.Name}X, {p.Name}Y, {p.Name}Z);");
		foreach (var p in paramInfos)
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

		var entityParams = paramInfos.FindAll(p => p.Kind == PrmKind.EntityParam && !WasmEntityDiscovery.IsEntityIdStruct(p.Type, out _));
		string callExpr = BuildCallExpression(method, targetInstance);

		if (entityParams.Count == 0)
		{
			sb.AppendLine($"            return ({callExpr}).GetValueOrDefault().{axis.ToUpper()};");
		}
		else
		{
			string condition = string.Join(" && ", entityParams.Select(u => $"{u.Name} != null"));
			sb.AppendLine($"            return ({condition}) ? ({callExpr}).GetValueOrDefault().{axis.ToUpper()} : 0f;");
		}

		sb.AppendLine("        });");
	}

	private static string BuildLambdaParams(List<ParamInfo> paramInfos, bool needsCaller, bool needsRetArea)
	{
		var parts = new List<string>();

		if (needsCaller) parts.Add("Caller caller");
		parts.AddRange(paramInfos.Select(GetLambdaParamDefinition).Where(s => !string.IsNullOrEmpty(s)));
		if (needsRetArea) parts.Add("int retArea");

		return string.Join(", ", parts);
	}

	private static List<string> GetMethodCallArgs(IMethodSymbol method)
	{
		var args = new List<string>();
		foreach (var p in method.Parameters)
		{
			if (p.Type.ToDisplayString() == "System.Numerics.Vector3")
				args.Add(p.Name);
			else if (WasmTypeClassifier.IsNullableVector3(p.Type))
				args.Add(p.Name);
			else if (p.Type.SpecialType == SpecialType.System_Boolean)
				args.Add($"{p.Name} != 0");
			else if (p.Type.TypeKind == TypeKind.Enum)
				args.Add($"({p.Type.ToDisplayString()}){p.Name}");
			else
				args.Add(p.Name);
		}
		return args;
	}

	private static string BuildCallExpression(IMethodSymbol method, string targetInstance)
	{
		var args = GetMethodCallArgs(method);
		return $"{targetInstance}?.{method.Name}({string.Join(", ", args)})";
	}
}
