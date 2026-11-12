using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Realm.WasmBindgen;

public static class WasmNatvisWriter
{
	public static void GenerateNatvisFile(string repoRoot, Compilation compilation)
	{
		string targetPath = Path.Combine(repoRoot, "realm.natvis");
		string templatePath = Path.Combine(repoRoot, "Realm.WasmBindgen", "realm.natvis.template.xml");

		string templateContent = File.Exists(templatePath)
			? File.ReadAllText(templatePath)
			: "";

		if (string.IsNullOrEmpty(templateContent)) return;

		var handleTypes = DiscoverHandleTypes(compilation);
		var sb = new StringBuilder();
		foreach (var handleType in handleTypes)
		{
			string typeName = handleType.Name;
			string ns = handleType.ContainingNamespace.ToDisplayString();
			string cleanName = typeName.EndsWith("Id", StringComparison.Ordinal)
				? typeName.Substring(0, typeName.Length - 2)
				: typeName;
			string cppNs = ns.Replace(".", "::");

			sb.AppendLine($"  <Type Name=\"{ns}.{typeName}\">");
			sb.AppendLine($"    <DisplayString>{cleanName} {{{{Value}}}}</DisplayString>");
			sb.AppendLine("    <Expand>");
			sb.AppendLine("      <Item Name=\"Value\">Value</Item>");
			sb.AppendLine("    </Expand>");
			sb.AppendLine("  </Type>");
			sb.AppendLine($"  <Type Name=\"{cppNs}::{typeName}\">");
			sb.AppendLine($"    <DisplayString>{cleanName} {{{{Value}}}}</DisplayString>");
			sb.AppendLine("    <Expand>");
			sb.AppendLine("      <Item Name=\"Value\">Value</Item>");
			sb.AppendLine("    </Expand>");
			sb.AppendLine("  </Type>");
		}

		string natvisContent = templateContent.Replace("{{TEMPLATE}}", sb.ToString().TrimEnd());
		try
		{
			if (!File.Exists(targetPath) || File.ReadAllText(targetPath, Encoding.UTF8) != natvisContent)
			{
				File.WriteAllText(targetPath, natvisContent, Encoding.UTF8);
			}
		}
		catch
		{
		}
	}

	private static List<INamedTypeSymbol> DiscoverHandleTypes(Compilation compilation)
	{
		var gameApiSymbol = compilation.GetTypeByMetadataName("Realm.MapAPI.IGameAPI");
		if (gameApiSymbol == null) return new List<INamedTypeSymbol>();

		return GetAllTypes(gameApiSymbol.ContainingAssembly)
			.Where(t => t.TypeKind == TypeKind.Struct && !t.IsStatic &&
						t.GetMembers().Any(m => (m is IPropertySymbol p && p.Name == "Value" && p.Type.SpecialType == SpecialType.System_Int32)
											 || (m is IFieldSymbol f && f.Name == "Value" && f.Type.SpecialType == SpecialType.System_Int32)))
			.OrderBy(t => t.Name)
			.ToList();
	}

	private static IEnumerable<INamedTypeSymbol> GetAllTypes(IAssemblySymbol assembly)
	{
		return GetAllTypes(assembly.GlobalNamespace);
	}

	private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol namespaceSymbol)
	{
		foreach (var member in namespaceSymbol.GetMembers())
		{
			if (member is INamespaceSymbol nestedNamespace)
			{
				foreach (var type in GetAllTypes(nestedNamespace))
				{
					yield return type;
				}
			}
			else if (member is INamedTypeSymbol type)
			{
				yield return type;
				foreach (var nestedType in type.GetTypeMembers())
				{
					yield return nestedType;
				}
			}
		}
	}
}
