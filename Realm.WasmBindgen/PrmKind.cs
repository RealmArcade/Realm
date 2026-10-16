namespace Realm.WasmBindgen;

public enum PrmKind
{
	DirectInt,
	DirectFloat,
	BoolParam,
	StringParam,
	StringListParam,
	Vector2Param,
	Vector3Param,
	Vector3NullableParam,
	EntityParam,
	Unsupported
}