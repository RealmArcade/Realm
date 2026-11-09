using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmEntityDiscovery
{
	public static HashSet<INamedTypeSymbol> DiscoverEntityInterfaces(INamedTypeSymbol gameApiSymbol)
	{
		var discovered = new HashSet<INamedTypeSymbol>(SymbolComparer.Instance);
		var queue = new Queue<INamedTypeSymbol>();
		queue.Enqueue(gameApiSymbol);

		while (queue.Count > 0)
		{
			var current = queue.Dequeue();
			foreach (var member in current.GetMembers())
			{
				ITypeSymbol? type = null;
				if (member is IPropertySymbol prop)
				{
					type = prop.Type;
				}
				else if (member is IMethodSymbol method)
				{
					foreach (var p in method.Parameters)
					{
						ProcessType(p.Type, discovered, queue);
					}

					type = method.ReturnType;
				}

				if (type != null)
				{
					ProcessType(type, discovered, queue);
				}
			}
		}

		discovered.Remove(gameApiSymbol);
		return discovered;
	}

	private static void ProcessType(ITypeSymbol type, HashSet<INamedTypeSymbol> discovered, Queue<INamedTypeSymbol> queue)
	{
		if (IsCollection(type, out var elemType))
		{
			type = elemType;
		}

		if (IsEntityInterface(type, out var unwrapped))
		{
			if (unwrapped is INamedTypeSymbol named && !discovered.Contains(named))
			{
				discovered.Add(named);
				queue.Enqueue(named);
			}
		}
	}

	public static bool IsEntityInterface(ITypeSymbol type, out ITypeSymbol unwrappedType)
	{
		unwrappedType = type;
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			unwrappedType = named.TypeArguments[0];
		}
		else if (type.NullableAnnotation == NullableAnnotation.Annotated)
		{
			unwrappedType = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
		}

		if (unwrappedType.TypeKind != TypeKind.Interface)
			return false;

		string display = unwrappedType.ToDisplayString();
		if (!display.StartsWith("Realm.MapAPI."))
			return false;

		if (display == "Realm.MapAPI.IGameAPI" || display == "Realm.MapAPI.IMapScript" || display == "Realm.MapAPI.IPlayer")
			return false;

		return true;
	}

	public static bool IsEntityIdStruct(ITypeSymbol type, out string typeName)
	{
		typeName = string.Empty;
		if (type == null) return false;

		ITypeSymbol unwrapped = type;
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			unwrapped = named.TypeArguments[0];
		}

		string fullName = unwrapped.ToDisplayString();
		if (fullName == "Realm.Ecs.UnitId" || unwrapped.Name == "UnitId")
		{
			typeName = "UnitId";
			return true;
		}
		if (fullName == "Realm.Ecs.PlayerId" || unwrapped.Name == "PlayerId")
		{
			typeName = "PlayerId";
			return true;
		}
		if (fullName == "Realm.Ecs.LootItemId" || unwrapped.Name == "LootItemId")
		{
			typeName = "LootItemId";
			return true;
		}
		if (fullName == "Realm.Ecs.ResourceNodeId" || unwrapped.Name == "ResourceNodeId")
		{
			typeName = "ResourceNodeId";
			return true;
		}
		return false;
	}

	public static bool IsCollection(ITypeSymbol type, out ITypeSymbol elementType)
	{
		elementType = null!;
		if (type == null || type.SpecialType == SpecialType.System_String)
			return false;

		if (type is IArrayTypeSymbol arrayType)
		{
			elementType = arrayType.ElementType;
			return true;
		}

		if (type is INamedTypeSymbol namedType)
		{
			if (namedType.IsGenericType)
			{
				string def = namedType.ConstructedFrom.ToDisplayString();
				if (def == "System.Collections.Generic.IEnumerable<T>" ||
				    def == "System.ReadOnlySpan<T>" ||
				    def == "System.Span<T>")
				{
					elementType = namedType.TypeArguments[0];
					return true;
				}
			}

			foreach (var iface in namedType.AllInterfaces)
			{
				if (iface.IsGenericType && iface.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
				{
					elementType = iface.TypeArguments[0];
					return true;
				}
			}
		}

		return false;
	}

	public static string FindCollectionMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
	{
		foreach (var member in gameApiSymbol.GetMembers())
		{
			ITypeSymbol? returnType = null;
			if (member is IPropertySymbol prop)
			{
				returnType = prop.Type;
			}
			else if (member is IMethodSymbol method && method.Parameters.Length == 0 && method.MethodKind == MethodKind.Ordinary)
			{
				returnType = method.ReturnType;
			}

			if (returnType != null && IsCollection(returnType, out var elemType) && SymbolEqualityComparer.Default.Equals(elemType, entityType))
			{
				return member is IMethodSymbol ? member.Name + "()" : member.Name;
			}
		}

		throw new InvalidOperationException($"Could not find collection member on IGameAPI returning a collection of {entityType.ToDisplayString()}");
	}

	public static string FindResolverMember(INamedTypeSymbol gameApiSymbol, ITypeSymbol entityType)
	{
		foreach (var member in gameApiSymbol.GetMembers())
		{
			if (member is IMethodSymbol method && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Int32 && method.MethodKind == MethodKind.Ordinary)
			{
				ITypeSymbol ret = method.ReturnType;
				if (IsEntityInterface(ret, out var unwrapped) && SymbolEqualityComparer.Default.Equals(unwrapped, entityType))
				{
					return member.Name;
				}
			}
		}

		throw new InvalidOperationException($"Could not find resolver method on IGameAPI returning {entityType.ToDisplayString()} taking a single int parameter");
	}
}
