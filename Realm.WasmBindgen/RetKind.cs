namespace Realm.WasmBindgen;

public enum RetKind
{
	Void,
	DirectInt,
	DirectFloat,
	BoolReturn,
	StringReturn,
	EntityReturn,
	EntityNullableReturn,
	EntityListReturn,
	StringListReturn,
	Unsupported
}