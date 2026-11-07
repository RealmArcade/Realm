using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

internal static class WitIdentifierFormat
{
	internal static string CleanInterfaceName(ITypeSymbol type)
	{
		string name = type.Name;
		if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
			return name.Substring(1);
		return name;
	}

	internal static string EscapeWitIdentifier(string name)
	{
		string kebab = ToKebabCase(name);
		return kebab switch
		{
			"flags" or "type" or "resource" or "enum" or "record" or "variant" or "interface" or "world" or "package" or "import" or "export" or "use" or "default"
				=> $"%{kebab}",
			_ => kebab
		};
	}

	internal static string ToKebabCase(string value)
	{
		if (string.IsNullOrEmpty(value)) return "";
		var sb = new System.Text.StringBuilder();
		sb.Append(char.ToLowerInvariant(value[0]));
		for (int i = 1; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsUpper(c))
			{
				sb.Append('-');
				sb.Append(char.ToLowerInvariant(c));
			}
			else
			{
				sb.Append(c);
			}
		}

		return sb.ToString();
	}
}
