using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Realm.Shared.Distribution;

/// <summary>
///     What rewriting one source file of a referenced map produced.
/// </summary>
public class MapSourceRewriteResult
{
	/// <summary>
	///     The rewritten source, ready to be written into the composed folder.
	/// </summary>
	public string Source { get; init; } = string.Empty;

	/// <summary>
	///     Whether this file declares the top-level <c>MapScript</c> class a composed map is started through.
	/// </summary>
	public bool DeclaresEntryScript { get; init; }
}

/// <summary>
///     Rewrites the C# a referenced map contributes so it can compile beside the map that composed it: its namespace
///     moves under the composition, the types it declares stop being sealed, and the members it declared private are
///     widened so the composing map can override them.
/// </summary>
/// <remarks>
///     The rewrite is syntactic. It never has to compile the file, and it works on the syntax tree rather than on text,
///     so a keyword that appears inside a string, a comment or an identifier is not touched.
/// </remarks>
public static class MapSourceRewriter
{
	private const string RootNamespace = "Realm.Maps";

	private const string ComposedNamespacePrefix = "Realm.Maps.Composed";

	/// <summary>
	///     Rewrites one source file for composition.
	/// </summary>
	/// <param name="sourceText">The file's text.</param>
	/// <param name="composedNamespace">The namespace the map's own namespace is moved under.</param>
	/// <returns>The rewritten text and whether this file declares the map's entry script.</returns>
	public static MapSourceRewriteResult Rewrite(string sourceText, string composedNamespace)
	{
		SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceText, new CSharpParseOptions(LanguageVersion.Latest));
		SyntaxNode root = tree.GetRoot();
		var rewriter = new ComposedSourceRewriter(composedNamespace);
		SyntaxNode rewrittenRoot = rewriter.Visit(root) ?? root;

		return new MapSourceRewriteResult
		{
			Source = rewrittenRoot.ToFullString(),
			DeclaresEntryScript = DeclaresMapScript(root)
		};
	}

	/// <summary>
	///     Answers whether a syntax tree declares the top-level <c>MapScript</c> class a match starts a map through.
	/// </summary>
	/// <param name="root">The tree's root.</param>
	/// <returns>True when the file declares the entry script.</returns>
	public static bool DeclaresMapScript(SyntaxNode root)
	{
		foreach (ClassDeclarationSyntax declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
		{
			if (!string.Equals(declaration.Identifier.ValueText, "MapScript", StringComparison.Ordinal)) continue;
			if (declaration.Parent is BaseNamespaceDeclarationSyntax) return true;
		}

		return false;
	}

	private class ComposedSourceRewriter : CSharpSyntaxRewriter
	{
		private readonly string _composedNamespace;

		public ComposedSourceRewriter(string composedNamespace)
		{
			_composedNamespace = composedNamespace;
		}

		public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
		{
			var visited = (NamespaceDeclarationSyntax)base.VisitNamespaceDeclaration(node)!;
			return visited.WithName(ComposeName(visited.Name));
		}

		public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
		{
			var visited = (FileScopedNamespaceDeclarationSyntax)base.VisitFileScopedNamespaceDeclaration(node)!;
			return visited.WithName(ComposeName(visited.Name));
		}

		public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
		{
			var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
			return ComposeName(visited);
		}

		public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
		{
			var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
		{
			var visited = (StructDeclarationSyntax)base.VisitStructDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
		{
			var visited = (InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
		{
			var visited = (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
		{
			var visited = (EnumDeclarationSyntax)base.VisitEnumDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
		{
			var visited = (DelegateDeclarationSyntax)base.VisitDelegateDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
		{
			var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
		{
			var visited = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
		{
			var visited = (EventFieldDeclarationSyntax)base.VisitEventFieldDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
		{
			var visited = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
		{
			var visited = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitEventDeclaration(EventDeclarationSyntax node)
		{
			var visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		public override SyntaxNode? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
		{
			var visited = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node)!;
			return visited.WithModifiers(RewriteModifiers(node, visited.Modifiers));
		}

		/// <summary>
		///     Drops <c>sealed</c> from a declaration and, when it was declared <c>private</c>, replaces that with the most
		///     capable accessibility the declaration's context accepts: <c>protected</c> where a derived type could reach it,
		///     otherwise <c>internal</c>, otherwise <c>public</c>.
		/// </summary>
		/// <param name="declaration">The declaration whose modifiers are being rewritten.</param>
		/// <param name="modifiers">Its modifiers.</param>
		/// <returns>The modifiers it keeps, with any private accessibility replaced.</returns>
		private SyntaxTokenList RewriteModifiers(SyntaxNode declaration, SyntaxTokenList modifiers)
		{
			bool removesSealed = modifiers.Any(SyntaxKind.SealedKeyword);
			bool widensPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);
			if (!removesSealed && !widensPrivate) return modifiers;

			SyntaxKind accessibility = ChooseAccessibility(declaration);
			var rewritten = new List<SyntaxToken>(modifiers.Count + 1);
			for (int index = 0; index < modifiers.Count; index++)
			{
				SyntaxToken token = modifiers[index];
				if (token.IsKind(SyntaxKind.SealedKeyword)) continue;

				if (!token.IsKind(SyntaxKind.PrivateKeyword))
				{
					rewritten.Add(token);
					continue;
				}

				if (index + 1 < modifiers.Count && modifiers[index + 1].IsKind(SyntaxKind.ProtectedKeyword)) index++;
				rewritten.Add(SyntaxFactory.Token(accessibility).WithTriviaFrom(token));
			}

			return SyntaxFactory.TokenList(rewritten);
		}

		private static SyntaxKind ChooseAccessibility(SyntaxNode declaration)
		{
			if (ProtectedReachesDeclaration(declaration)) return SyntaxKind.ProtectedKeyword;
			if (InternalReachesDeclaration(declaration)) return SyntaxKind.InternalKeyword;
			return SyntaxKind.PublicKeyword;
		}

		/// <summary>
		///     Answers whether a member declared <c>protected</c> would be legal where this declaration sits and would reach
		///     further than <c>internal</c> does.
		/// </summary>
		/// <param name="declaration">The declaration being widened.</param>
		/// <returns>True when <c>protected</c> is legal in this context.</returns>
		private static bool ProtectedReachesDeclaration(SyntaxNode declaration)
		{
			if (declaration is AccessorDeclarationSyntax)
			{
				BasePropertyDeclarationSyntax? container = declaration.FirstAncestorOrSelf<BasePropertyDeclarationSyntax>();
				if (container == null) return false;
				return container.Modifiers.Any(SyntaxKind.PublicKeyword) && ContainingTypeAcceptsProtected(container);
			}

			if (!ContainingTypeAcceptsProtected(declaration)) return false;
			if (declaration is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
			{
				return declaration.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>() is ClassDeclarationSyntax or RecordDeclarationSyntax;
			}

			return true;
		}

		private static bool ContainingTypeAcceptsProtected(SyntaxNode declaration)
		{
			TypeDeclarationSyntax? containingType = declaration.Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
			if (containingType == null) return false;
			if (containingType is StructDeclarationSyntax) return false;
			if (containingType is RecordDeclarationSyntax record && record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)) return false;
			return !containingType.Modifiers.Any(SyntaxKind.StaticKeyword);
		}

		/// <summary>
		///     Answers whether <c>internal</c> is legal for this declaration. Every member a composed map can declare sits
		///     in one assembly, so <c>internal</c> is legal wherever it matters.
		/// </summary>
		/// <param name="declaration">The declaration being widened.</param>
		/// <returns>True when <c>internal</c> can be written on the declaration.</returns>
		private static bool InternalReachesDeclaration(SyntaxNode declaration)
		{
			return true;
		}

		private NameSyntax ComposeName(NameSyntax name)
		{
			string text = name.ToString();
			string composed = ComposeNameText(text);
			if (string.Equals(composed, text, StringComparison.Ordinal)) return name;
			return SyntaxFactory.ParseName(composed).WithTriviaFrom(name);
		}

		private string ComposeNameText(string nameText)
		{
			if (string.Equals(nameText, RootNamespace, StringComparison.Ordinal)) return _composedNamespace;
			if (!nameText.StartsWith(RootNamespace + ".", StringComparison.Ordinal)) return nameText;
			if (nameText.StartsWith(ComposedNamespacePrefix + ".", StringComparison.Ordinal)) return nameText;
			return _composedNamespace + nameText.Substring(RootNamespace.Length);
		}
	}
}
