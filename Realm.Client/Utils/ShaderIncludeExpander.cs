using System;
using System.IO;
using System.Text;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Realm.Client.Utils;

public static class ShaderIncludeExpander
{
	private const string IncludeDirective = "#include";
	private const string ResourcePrefix = "res://";
	private const int MaxIncludeDepth = 8;

	public static string LoadExpandedShader(string resourcePath)
	{
		string shaderCode = ReadShaderSource(resourcePath);
		if (string.IsNullOrEmpty(shaderCode)) return shaderCode;
		return ExpandIncludes(shaderCode, 0);
	}

	public static string ExpandIncludes(string shaderCode, int depth)
	{
		if (string.IsNullOrEmpty(shaderCode)) return shaderCode;
		if (depth > MaxIncludeDepth) return shaderCode;

		string[] lines = shaderCode.Split('\n');
		StringBuilder builder = new(shaderCode.Length + 1024);

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string line = lines[lineIndex];
			string includePath = TryReadIncludePath(line);
			if (includePath == null)
			{
				builder.Append(line);
				if (lineIndex < lines.Length - 1) builder.Append('\n');
				continue;
			}

			string includedCode = ReadShaderSource(includePath);
			if (string.IsNullOrEmpty(includedCode)) continue;
			builder.Append(ExpandIncludes(includedCode, depth + 1));
			builder.Append('\n');
		}

		return builder.ToString();
	}

	private static string TryReadIncludePath(string line)
	{
		string trimmed = line.TrimStart();
		if (!trimmed.StartsWith(IncludeDirective, StringComparison.Ordinal)) return null;

		int quoteStart = trimmed.IndexOf('"');
		if (quoteStart < 0) return null;
		int quoteEnd = trimmed.IndexOf('"', quoteStart + 1);
		if (quoteEnd <= quoteStart) return null;

		string includePath = trimmed.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
		return string.IsNullOrWhiteSpace(includePath) ? null : includePath;
	}

	private static string ReadShaderSource(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return string.Empty;

		if (FileAccess.FileExists(path))
		{
			using var resourceFile = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			string resourceCode = resourceFile?.GetAsText();
			if (!string.IsNullOrEmpty(resourceCode)) return resourceCode;
		}

		string[] candidatePaths = BuildCandidatePaths(path);
		for (int index = 0; index < candidatePaths.Length; index++)
		{
			string candidatePath = candidatePaths[index];
			if (!File.Exists(candidatePath)) continue;
			return File.ReadAllText(candidatePath);
		}

		return string.Empty;
	}

	private static string[] BuildCandidatePaths(string path)
	{
		if (!path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
		{
			return new[] { path };
		}

		string relativePath = path.Substring(ResourcePrefix.Length);
		return new[]
		{
			ProjectSettings.GlobalizePath(path),
			relativePath,
			Path.Combine("Realm.Client", relativePath),
			Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath)
		};
	}
}
