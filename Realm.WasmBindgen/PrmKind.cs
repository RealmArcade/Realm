namespace Realm.WasmBindgen;

public enum PrmKind
{
	DirectInt,
	DirectFloat,
	BoolParam,
	StringParam,
	StringListParam,
	Vector3Param,
	Vector3NullableParam,
	EntityParam,
	Unsupported
}