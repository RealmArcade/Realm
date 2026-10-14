using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public struct ParamInfo
{
	public string Name { get; }
	public PrmKind Kind { get; }
	public ITypeSymbol Type { get; }

	public ParamInfo(string name, PrmKind kind, ITypeSymbol type)
	{
		Name = name;
		Kind = kind;
		Type = type;
	}
}