using System.Reflection;

namespace Realm.Shared;

public class BooleanOptionMetadata
{
	public string CanonicalLongName { get; set; } = string.Empty;
	public bool IsInverted { get; set; }
	public PropertyInfo Property { get; set; } = null!;
}