using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public class SymbolComparer : IEqualityComparer<INamedTypeSymbol>
{
	public static readonly SymbolComparer Instance = new SymbolComparer();
	public bool Equals(INamedTypeSymbol x, INamedTypeSymbol y) => SymbolEqualityComparer.Default.Equals(x, y);
	public int GetHashCode(INamedTypeSymbol obj) => obj.ToDisplayString().GetHashCode();
}