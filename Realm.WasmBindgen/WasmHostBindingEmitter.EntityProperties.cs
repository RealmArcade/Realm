using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;


internal static partial class WasmHostBindingEmitter
{
	internal static void EmitEntityPropertyBindings(StringBuilder sb, IPropertySymbol property, string tKebab, INamedTypeSymbol entitySymbol, INamedTypeSymbol gameApiSymbol, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string witBase = tKebab + "-" + WitIdentifierFormat.ToKebabCase(property.Name);
		string resolver = WasmEntityDiscovery.FindResolverMember(gameApiSymbol, entitySymbol);

		if (property.Type.ToDisplayString() == "System.Numerics.Vector3")
		{
			EmitEntityVector3Property(sb, property, witBase, resolver, definedFunctions, methodNames);
			return;
		}

		var retKind = WasmTypeClassifier.ClassifyReturn(property.Type);
		if (retKind == RetKind.Unsupported) return;

		EmitEntityStandardProperty(sb, property, witBase, resolver, gameApiSymbol, retKind, ref lastWasMultiLine, definedFunctions, methodNames);
		EmitEntityPropertySetter(sb, property, witBase, resolver, gameApiSymbol, definedFunctions, methodNames);
	}

	internal static void EmitEntityVector3Property(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string nameX = $"{witBase}-x";
		if (definedFunctions.Add(nameX))
		{
			int idxX = methodNames.Count;
			methodNames.Add(nameX);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameX}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxX}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); return u != null ? u.{property.Name}.X : 0f; }});");
		}

		string nameY = $"{witBase}-y";
		if (definedFunctions.Add(nameY))
		{
			int idxY = methodNames.Count;
			methodNames.Add(nameY);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameY}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxY}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); return u != null ? u.{property.Name}.Y : 0f; }});");
		}

		string nameZ = $"{witBase}-z";
		if (definedFunctions.Add(nameZ))
		{
			int idxZ = methodNames.Count;
			methodNames.Add(nameZ);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{nameZ}\", (int id) => {{ using var _scope = BeginProfileApiCall({idxZ}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); return u != null ? u.{property.Name}.Z : 0f; }});");
		}

		if (property.SetMethod != null)
		{
			string setWitBase = "set-" + witBase;
			if (definedFunctions.Add(setWitBase))
			{
				int idxSet = methodNames.Count;
				methodNames.Add(setWitBase);
				sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float valX, float valY, float valZ) => {{ using var _scope = BeginProfileApiCall({idxSet}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = new System.Numerics.Vector3(valX, valY, valZ); }});");
			}
		}
	}

	internal static void EmitEntityStandardProperty(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, RetKind retKind, ref bool lastWasMultiLine, HashSet<string> definedFunctions, List<string> methodNames)
	{
		string expr = $"(_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id)?.{property.Name}";
		string mapped = MapEntityPropertyExpression(property, gameApiSymbol, retKind, expr);

		if (!definedFunctions.Add(witBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(witBase);

		if (lastWasMultiLine) sb.AppendLine();
		if (retKind == RetKind.StringReturn)
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (Caller caller, int id, int retArea) => {{ using var _scope = BeginProfileApiCall({methodIndex}); string s = {mapped}; WriteGuestString(caller, retArea, s); }});");
		}
		else
		{
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{witBase}\", (int id) => {{ using var _scope = BeginProfileApiCall({methodIndex}); return {mapped}; }});");
		}

		lastWasMultiLine = false;
	}

	internal static string MapEntityPropertyExpression(IPropertySymbol property, INamedTypeSymbol gameApiSymbol, RetKind retKind, string expr)
	{
		if (retKind == RetKind.EntityReturn || retKind == RetKind.EntityNullableReturn)
		{
			WasmEntityDiscovery.IsEntityInterface(property.Type, out var unwrapped);
			bool targetHasId = unwrapped.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");
			if (targetHasId)
			{
				return $"{expr}?.UniqueId ?? 0";
			}
			else
			{
				string colName = WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, unwrapped);
				return $"({expr} != null) ? ((_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{colName}.ToList().IndexOf({expr}) ?? -1) : -1";
			}
		}

		return retKind switch
		{
			RetKind.BoolReturn => $"({expr} ?? false) ? 1 : 0",
			RetKind.DirectFloat => $"{expr} ?? 0f",
			RetKind.DirectInt => $"(int)({expr} ?? 0)",
			RetKind.StringReturn => $"{expr} ?? \"\"",
			_ => "0"
		};
	}

	internal static void EmitEntityPropertySetter(StringBuilder sb, IPropertySymbol property, string witBase, string resolver, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (property.SetMethod == null) return;

		string setWitBase = "set-" + witBase;
		if (!definedFunctions.Add(setWitBase)) return;

		int methodIndex = methodNames.Count;
		methodNames.Add(setWitBase);

		if (property.Type.SpecialType == SpecialType.System_Boolean)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = val != 0; }});");
		else if (property.Type.SpecialType == SpecialType.System_Single)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, float val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_Int32)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = val; }});");
		else if (property.Type.SpecialType == SpecialType.System_String)
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (Caller caller, int id, int valPtr, int valLen) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = ReadGuestString(caller, valPtr, valLen); }});");
		else if (WasmEntityDiscovery.IsEntityInterface(property.Type, out var unwrapped))
		{
			string resolverR = WasmEntityDiscovery.FindResolverMember(gameApiSymbol, unwrapped);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{setWitBase}\", (int id, int val) => {{ using var _scope = BeginProfileApiCall({methodIndex}); var u = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolver}(id); if (u != null) u.{property.Name} = (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{resolverR}(val); }});");
		}
	}
}
