﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     The two code models the import service compares: the source map's files, namespaces and types
///     (including which of them are exported and what they depend on), and the destination workspace's
///     namespaces, types, file paths and source-map attributions.
/// </summary>
	internal class SourceCodeIndex
	{
		public Dictionary<string, SourceType> TypesByFullName { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, List<SourceType>> TypesBySimpleName { get; } = new(StringComparer.Ordinal);
		public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);

		public static SourceCodeIndex Build(IEnumerable<SourceFile> sourceFiles)
		{
			var index = new SourceCodeIndex();
			foreach (SourceFile sourceFile in sourceFiles)
			{
				index.IndexFile(sourceFile);
			}

			if (index.TypesByFullName.Values.Any(sourceType => sourceType.NamespaceName.Length == 0))
			{
				index.Namespaces.Add(string.Empty);
			}

			return index;
		}

		public IReadOnlyList<SourceType> GetTypesInNamespaceTree(string namespaceName)
		{
			return TypesByFullName.Values
				.Where(sourceType => sourceType.NamespaceName.Equals(namespaceName, StringComparison.Ordinal)
				                     || sourceType.NamespaceName.StartsWith(namespaceName + ".", StringComparison.Ordinal))
				.OrderBy(sourceType => sourceType.FullName, StringComparer.Ordinal)
				.ToList();
		}

		public IReadOnlyList<SourceType> GetTypesInSourceFiles(IReadOnlyCollection<string> relativePath)
		{
			return TypesByFullName.Values
				.Where(sourceType => sourceType.SourceFilePaths.Overlaps(relativePath))
				.OrderBy(sourceType => sourceType.FullName, StringComparer.Ordinal)
				.ToList();
		}

		private void IndexFile(SourceFile sourceFile)
		{
			CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(sourceFile.Text, new CSharpParseOptions(LanguageVersion.Latest)).GetCompilationUnitRoot();
			var fileUsings = new List<UsingDirectiveSyntax>();

			foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
			{
				if (usingDirective.Parent is CompilationUnitSyntax)
				{
					fileUsings.Add(usingDirective);
				}
			}

			foreach (MemberDeclarationSyntax member in root.Members.OfType<MemberDeclarationSyntax>())
			{
				IndexMember(member, string.Empty, fileUsings, sourceFile);
			}
		}

		private void IndexMember(MemberDeclarationSyntax member, string containingNamespace, IReadOnlyList<UsingDirectiveSyntax> inheritedUsings, SourceFile sourceFile)
		{
			switch (member)
			{
				case BaseNamespaceDeclarationSyntax namespaceDeclaration:
					string childNamespace = CombineNamespace(containingNamespace, namespaceDeclaration.Name.ToString());
					var namespaceUsings = new List<UsingDirectiveSyntax>(inheritedUsings);
					namespaceUsings.AddRange(namespaceDeclaration.Usings);

					if (childNamespace.Length > 0)
					{
						Namespaces.Add(childNamespace);
					}

					foreach (MemberDeclarationSyntax childMember in namespaceDeclaration.Members)
					{
						IndexMember(childMember, childNamespace, namespaceUsings, sourceFile);
					}

					break;

				case BaseTypeDeclarationSyntax typeDeclaration:
					RegisterType(typeDeclaration, containingNamespace, inheritedUsings, sourceFile, GetTypeKind(typeDeclaration));
					break;

				case DelegateDeclarationSyntax delegateDeclaration:
					RegisterType(delegateDeclaration, containingNamespace, inheritedUsings, sourceFile, "delegate");
					break;
			}
		}

		private void RegisterType(SyntaxNode declaration, string containingNamespace, IReadOnlyList<UsingDirectiveSyntax> usings, SourceFile sourceFile, string kind)
		{
			string name = declaration switch
			{
				BaseTypeDeclarationSyntax typeDeclaration => typeDeclaration.Identifier.ValueText,
				DelegateDeclarationSyntax delegateDeclaration => delegateDeclaration.Identifier.ValueText,
				_ => string.Empty
			};

			if (string.IsNullOrEmpty(name)) return;

			string fullName = CombineNamespace(containingNamespace, name);

			if (!TypesByFullName.TryGetValue(fullName, out SourceType? sourceType))
			{
				sourceType = new SourceType(name, containingNamespace, fullName, kind);
				TypesByFullName[fullName] = sourceType;

				if (!TypesBySimpleName.TryGetValue(name, out List<SourceType>? simpleNameTypes))
				{
					simpleNameTypes = new List<SourceType>();
					TypesBySimpleName[name] = simpleNameTypes;
				}

				simpleNameTypes.Add(sourceType);
			}

			sourceType.IsTemplateFile |= sourceFile.IsTemplateFile;
			sourceType.SourceFilePaths.Add(sourceFile.RelativePath);
			sourceType.ApplyExportAttribute(FindExportAttribute(declaration));
			sourceType.AddPart(declaration, usings);
		}

		private static AttributeSyntax? FindExportAttribute(SyntaxNode declaration)
		{
			SyntaxList<AttributeListSyntax> attributeLists = MapCodeImportService.GetAttributeLists(declaration);
			foreach (AttributeListSyntax attributeList in attributeLists)
			{
				foreach (AttributeSyntax attribute in attributeList.Attributes)
				{
					string attributeName = MapCodeImportService.GetAttributeTypeName(attribute);
					if (attributeName.Equals(MapCodeImportService.ExportAttributeUsageName, StringComparison.Ordinal)
					    || attributeName.Equals(MapCodeImportService.ExportAttributeTypeName, StringComparison.Ordinal))
					{
						return attribute;
					}
				}
			}

			return null;
		}

		private static string CombineNamespace(string containingNamespace, string name)
		{
			return string.IsNullOrEmpty(containingNamespace) ? name : $"{containingNamespace}.{name}";
		}

		private static string GetTypeKind(BaseTypeDeclarationSyntax typeDeclaration)
		{
			return typeDeclaration switch
			{
				TypeDeclarationSyntax type => type.Keyword.ValueText,
				EnumDeclarationSyntax => "enum",
				_ => "type"
			};
		}
	}

	internal class SourceType
	{
		private readonly HashSet<string> _declaredIdentifiers = new(StringComparer.Ordinal);

		public SourceType(string name, string namespaceName, string fullName, string kind)
		{
			Name = name;
			NamespaceName = namespaceName;
			FullName = fullName;
			Kind = kind;
		}

		public string Name { get; }
		public string NamespaceName { get; }
		public string FullName { get; }
		public string Kind { get; }
		public List<TypePart> Parts { get; } = new();
		public HashSet<string> ReferencedIdentifiers { get; } = new(StringComparer.Ordinal);
		public HashSet<string> UsingNamespaces { get; } = new(StringComparer.Ordinal);
		public bool IsTemplateFile { get; set; }
		public HashSet<string> SourceFilePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
		public bool IsExported { get; private set; }
		public string ExportDisplayName { get; private set; } = string.Empty;
		public string ExportCategory { get; private set; } = string.Empty;

		public void ApplyExportAttribute(AttributeSyntax? exportAttribute)
		{
			if (exportAttribute == null || IsExported) return;

			IsExported = true;
			AttributeArgumentListSyntax? argumentList = exportAttribute.ArgumentList;
			if (argumentList == null) return;

			if (argumentList.Arguments.Count > 0)
			{
				ExportDisplayName = ReadStringAttributeArgument(argumentList.Arguments[0].Expression);
			}

			if (argumentList.Arguments.Count > 1)
			{
				ExportCategory = ReadStringAttributeArgument(argumentList.Arguments[1].Expression);
			}
		}

		private static string ReadStringAttributeArgument(ExpressionSyntax expression)
		{
			if (expression is LiteralExpressionSyntax literalExpression && literalExpression.IsKind(SyntaxKind.StringLiteralExpression))
			{
				return literalExpression.Token.ValueText.Trim();
			}

			return string.Empty;
		}

		public void AddPart(SyntaxNode declaration, IReadOnlyList<UsingDirectiveSyntax> usings)
		{
			Parts.Add(new TypePart(declaration, usings));
			CollectIdentifiers(declaration);

			foreach (UsingDirectiveSyntax usingDirective in usings)
			{
				string? target = usingDirective.NamespaceOrType?.ToString();

				if (usingDirective.Alias != null)
				{
					_declaredIdentifiers.Add(usingDirective.Alias.Name.Identifier.ValueText);
					AddUsingTargetTypeReference(target);
					continue;
				}

				if (usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword))
				{
					AddUsingTargetTypeReference(target);
					continue;
				}

				if (!string.IsNullOrEmpty(target))
				{
					UsingNamespaces.Add(target);
				}
			}

			ReferencedIdentifiers.ExceptWith(_declaredIdentifiers);
		}

		private void AddUsingTargetTypeReference(string? usingTarget)
		{
			if (string.IsNullOrEmpty(usingTarget)) return;

			int lastDotIndex = usingTarget.LastIndexOf('.');
			string typeName = lastDotIndex >= 0 ? usingTarget.Substring(lastDotIndex + 1) : usingTarget;
			if (typeName.Length == 0) return;

			if (lastDotIndex > 0)
			{
				UsingNamespaces.Add(usingTarget.Substring(0, lastDotIndex));
			}

			ReferencedIdentifiers.Add(typeName);
		}

		private void CollectIdentifiers(SyntaxNode declaration)
		{
			foreach (SimpleNameSyntax simpleName in declaration.DescendantNodes().OfType<SimpleNameSyntax>())
			{
				ReferencedIdentifiers.Add(simpleName.Identifier.ValueText);
			}

			foreach (BaseTypeDeclarationSyntax typeDeclaration in declaration.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(typeDeclaration.Identifier.ValueText);
			}

			foreach (DelegateDeclarationSyntax delegateDeclaration in declaration.DescendantNodes().OfType<DelegateDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(delegateDeclaration.Identifier.ValueText);
			}

			foreach (MethodDeclarationSyntax methodDeclaration in declaration.DescendantNodes().OfType<MethodDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(methodDeclaration.Identifier.ValueText);
			}

			foreach (ConstructorDeclarationSyntax constructorDeclaration in declaration.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(constructorDeclaration.Identifier.ValueText);
			}

			foreach (PropertyDeclarationSyntax propertyDeclaration in declaration.DescendantNodes().OfType<PropertyDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(propertyDeclaration.Identifier.ValueText);
			}

			foreach (EventDeclarationSyntax eventDeclaration in declaration.DescendantNodes().OfType<EventDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(eventDeclaration.Identifier.ValueText);
			}

			foreach (VariableDeclaratorSyntax variableDeclarator in declaration.DescendantNodes().OfType<VariableDeclaratorSyntax>())
			{
				_declaredIdentifiers.Add(variableDeclarator.Identifier.ValueText);
			}

			foreach (ParameterSyntax parameter in declaration.DescendantNodes().OfType<ParameterSyntax>())
			{
				_declaredIdentifiers.Add(parameter.Identifier.ValueText);
			}

			foreach (TypeParameterSyntax typeParameter in declaration.DescendantNodes().OfType<TypeParameterSyntax>())
			{
				_declaredIdentifiers.Add(typeParameter.Identifier.ValueText);
			}

			foreach (EnumMemberDeclarationSyntax enumMember in declaration.DescendantNodes().OfType<EnumMemberDeclarationSyntax>())
			{
				_declaredIdentifiers.Add(enumMember.Identifier.ValueText);
			}

			foreach (SingleVariableDesignationSyntax designation in declaration.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
			{
				_declaredIdentifiers.Add(designation.Identifier.ValueText);
			}

			foreach (ForEachStatementSyntax forEachStatement in declaration.DescendantNodes().OfType<ForEachStatementSyntax>())
			{
				_declaredIdentifiers.Add(forEachStatement.Identifier.ValueText);
			}

			foreach (CatchDeclarationSyntax catchDeclaration in declaration.DescendantNodes().OfType<CatchDeclarationSyntax>())
			{
				if (!catchDeclaration.Identifier.IsKind(SyntaxKind.None))
				{
					_declaredIdentifiers.Add(catchDeclaration.Identifier.ValueText);
				}
			}

			foreach (LocalFunctionStatementSyntax localFunction in declaration.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
			{
				_declaredIdentifiers.Add(localFunction.Identifier.ValueText);
			}
		}
	}

	internal class TypePart
	{
		public TypePart(SyntaxNode declaration, IReadOnlyList<UsingDirectiveSyntax> usings)
		{
			Declaration = declaration;
			Usings = usings;
		}

		public SyntaxNode Declaration { get; }
		public IReadOnlyList<UsingDirectiveSyntax> Usings { get; }
	}

	internal class DestinationCodeIndex
	{
		public HashSet<string> TypeFullNames { get; } = new(StringComparer.Ordinal);
		public HashSet<string> Namespaces { get; } = new(StringComparer.Ordinal);
		private Dictionary<string, List<string>> FilePathsByType { get; } = new(StringComparer.Ordinal);
		private Dictionary<string, HashSet<string>> SourceMapsByType { get; } = new(StringComparer.Ordinal);

		public void AddTypeFile(string typeFullName, string relativePath)
		{
			if (!FilePathsByType.TryGetValue(typeFullName, out List<string>? relativePaths))
			{
				relativePaths = new List<string>();
				FilePathsByType[typeFullName] = relativePaths;
			}

			if (!relativePaths.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
			{
				relativePaths.Add(relativePath);
			}
		}

		public IReadOnlyList<string> GetTypeFilePaths(string typeFullName)
		{
			return FilePathsByType.TryGetValue(typeFullName, out List<string>? relativePaths)
				? relativePaths
				: Array.Empty<string>();
		}

		public void AddSourceMapAttribution(string typeFullName, string sourceMapName)
		{
			if (!SourceMapsByType.TryGetValue(typeFullName, out HashSet<string>? sourceMapNames))
			{
				sourceMapNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				SourceMapsByType[typeFullName] = sourceMapNames;
			}

			sourceMapNames.Add(sourceMapName);
		}

		public bool IsImportedFromSourceMap(string typeFullName, string sourceMapName)
		{
			return SourceMapsByType.TryGetValue(typeFullName, out HashSet<string>? sourceMapNames)
			       && sourceMapNames.Contains(sourceMapName);
		}
	}

	internal class SourceFile
	{
		public SourceFile(string relativePath, string text, bool isTemplateFile)
		{
			RelativePath = relativePath;
			Text = text;
			IsTemplateFile = isTemplateFile;
		}

		public string RelativePath { get; }
		public string Text { get; }
		public bool IsTemplateFile { get; }
	}
