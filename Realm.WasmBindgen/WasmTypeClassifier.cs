using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmTypeClassifier
{
	public static PrmKind ClassifyParam(ITypeSymbol type)
	{
		if (type.SpecialType == SpecialType.System_Int32) return PrmKind.DirectInt;
		if (type.TypeKind == TypeKind.Enum) return PrmKind.DirectInt;
		if (type.SpecialType == SpecialType.System_Single) return PrmKind.DirectFloat;
		if (type.SpecialType == SpecialType.System_Boolean) return PrmKind.BoolParam;
		if (type.SpecialType == SpecialType.System_String) return PrmKind.StringParam;

		if (WasmEntityDiscovery.IsCollection(type, out var elemType) && elemType.SpecialType == SpecialType.System_String)
			return PrmKind.StringListParam;

		if (WasmEntityDiscovery.IsCollection(type, out var floatElementType) && floatElementType.SpecialType == SpecialType.System_Single)
			return PrmKind.FloatListParam;

		string displayName = type.ToDisplayString();

		if (displayName == "System.Numerics.Vector2") return PrmKind.Vector2Param;
		if (displayName == "System.Numerics.Vector3") return PrmKind.Vector3Param;
		if (IsNullableVector3(type)) return PrmKind.Vector3NullableParam;
		if (WasmEntityDiscovery.IsEntityIdStruct(type, out _)) return PrmKind.EntityParam;
		if (WasmEntityDiscovery.IsEntityInterface(type, out _)) return PrmKind.EntityParam;

		return PrmKind.Unsupported;
	}

	public static RetKind ClassifyReturn(ITypeSymbol type)
	{
		var specialRet = ClassifySpecialReturn(type.SpecialType);
		if (specialRet != RetKind.Unsupported) return specialRet;

		if (type.TypeKind == TypeKind.Enum) return RetKind.DirectInt;

		if (WasmEntityDiscovery.IsEntityIdStruct(type, out _))
		{
			bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated || type.ToDisplayString().EndsWith("?");
			return isNullable ? RetKind.EntityNullableReturn : RetKind.EntityReturn;
		}

		if (WasmEntityDiscovery.IsEntityInterface(type, out _))
		{
			bool isNullable = type.NullableAnnotation == NullableAnnotation.Annotated || type.ToDisplayString().EndsWith("?");
			return isNullable ? RetKind.EntityNullableReturn : RetKind.EntityReturn;
		}

		return ClassifyCollectionReturn(type);
	}

	private static RetKind ClassifySpecialReturn(SpecialType specialType)
	{
		return specialType switch
		{
			SpecialType.System_Void => RetKind.Void,
			SpecialType.System_Int32 => RetKind.DirectInt,
			SpecialType.System_Single => RetKind.DirectFloat,
			SpecialType.System_Boolean => RetKind.BoolReturn,
			SpecialType.System_String => RetKind.StringReturn,
			_ => RetKind.Unsupported
		};
	}

	private static RetKind ClassifyCollectionReturn(ITypeSymbol type)
	{
		if (!WasmEntityDiscovery.IsCollection(type, out var elemType)) return RetKind.Unsupported;

		if (elemType.SpecialType == SpecialType.System_String) return RetKind.StringListReturn;
		if (WasmEntityDiscovery.IsEntityIdStruct(elemType, out _)) return RetKind.EntityListReturn;
		if (WasmEntityDiscovery.IsEntityInterface(elemType, out _)) return RetKind.EntityListReturn;

		return RetKind.Unsupported;
	}

	public static bool IsNullableVector3(ITypeSymbol type)
	{
		if (type is INamedTypeSymbol named && named.IsGenericType && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
		{
			return named.TypeArguments[0].ToDisplayString() == "System.Numerics.Vector3";
		}

		return false;
	}
}
