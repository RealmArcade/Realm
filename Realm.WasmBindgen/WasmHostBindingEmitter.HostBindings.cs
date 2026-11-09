using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;


public static partial class WasmHostBindingEmitter
{
	private static void GenerateGameApiBindings(StringBuilder sb, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		var propertyAccessorMethods = CollectPropertyAccessorNames(gameApiSymbol);

		foreach (var member in gameApiSymbol.GetMembers())
		{
			if (member is IEventSymbol || !member.IsAbstract)
				continue;

			if (member is IPropertySymbol property)
			{
				EmitPropertyBindings(sb, property, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)", definedFunctions, methodNames);
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				if (!propertyAccessorMethods.Contains(method.Name))
				{
					EmitMethodBinding(sb, method, ref lastWasMultiLine, "(_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)", gameApiSymbol, definedFunctions, methodNames);
				}
			}
		}
	}

	private static void GenerateEntityInterfaceBindings(StringBuilder sb, HashSet<INamedTypeSymbol> entityInterfaces, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		foreach (var entityIface in entityInterfaces)
		{
			GenerateSingleEntityInterfaceBinding(sb, entityIface, gameApiSymbol, definedFunctions, methodNames, ref lastWasMultiLine);
		}
	}

	private static void GenerateSingleEntityInterfaceBinding(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		string tKebab = WitIdentifierFormat.ToKebabCase(WitIdentifierFormat.CleanInterfaceName(entityIface));
		bool hasUniqueId = entityIface.GetMembers().Any(m => m is IPropertySymbol p && p.Name == "UniqueId");

		EmitEntityCountFunctionIfNeeded(sb, entityIface, gameApiSymbol, tKebab, hasUniqueId, definedFunctions, methodNames);

		var accessorMethods = CollectPropertyAccessorNames(entityIface);
		foreach (var member in entityIface.GetMembers())
		{
			ProcessEntityInterfaceMember(sb, member, entityIface, gameApiSymbol, tKebab, accessorMethods, definedFunctions, methodNames, ref lastWasMultiLine);
		}
	}

	private static void EmitEntityCountFunctionIfNeeded(StringBuilder sb, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, bool hasUniqueId, HashSet<string> definedFunctions, List<string> methodNames)
	{
		if (hasUniqueId) return;

		string countName = $"{tKebab}-count";
		if (definedFunctions.Add(countName))
		{
			int methodIndex = methodNames.Count;
			methodNames.Add(countName);
			sb.AppendLine($"        _linker.DefineFunction(mod, \"{countName}\", () => {{ using var _scope = BeginProfileApiCall({methodIndex}); return (_cachedApi ?? (IGameAPI?)Realm.MapAPI.GameAPI.Current)?.{WasmEntityDiscovery.FindCollectionMember(gameApiSymbol, entityIface)}?.Count() ?? 0; }});");
		}
	}

	private static void ProcessEntityInterfaceMember(StringBuilder sb, ISymbol member, INamedTypeSymbol entityIface, INamedTypeSymbol gameApiSymbol, string tKebab, HashSet<string> accessorMethods, HashSet<string> definedFunctions, List<string> methodNames, ref bool lastWasMultiLine)
	{
		if (!member.IsAbstract) return;

		if (member is IPropertySymbol property && property.Name != "UniqueId")
		{
			EmitEntityPropertyBindings(sb, property, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
		}
		else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
		{
			if (!accessorMethods.Contains(method.Name))
			{
				EmitEntityMethodBinding(sb, method, tKebab, entityIface, gameApiSymbol, ref lastWasMultiLine, definedFunctions, methodNames);
			}
		}
	}
}
