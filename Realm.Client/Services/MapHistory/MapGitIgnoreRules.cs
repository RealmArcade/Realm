using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Realm.Client.Services.MapHistory;

public class MapGitIgnoreRules
{
		private readonly List<MapGitIgnorePattern> _patterns = new();

	public static MapGitIgnoreRules LoadFromDirectory(string workTreePath)
	{
		var rules = new MapGitIgnoreRules();
		string ignoreFilePath = Path.Combine(workTreePath, ".gitignore");
		if (!File.Exists(ignoreFilePath))
		{
			return rules;
		}

		try
		{
			foreach (string line in File.ReadAllLines(ignoreFilePath))
			{
				rules.AddPattern(line);
			}
		}
		catch (Exception)
		{
			return rules;
		}

		return rules;
	}

	public void AddPattern(string line)
	{
		if (string.IsNullOrWhiteSpace(line)) return;

		string pattern = line.Trim();
		if (pattern.StartsWith('#')) return;

		bool negated = false;
		if (pattern.StartsWith('!'))
		{
			negated = true;
			pattern = pattern[1..].Trim();
		}

		if (pattern.Length == 0) return;

		bool directoryOnly = pattern.EndsWith('/');
		if (directoryOnly)
		{
			pattern = pattern.TrimEnd('/');
		}

		bool anchored = pattern.Contains('/');
		if (pattern.StartsWith('/'))
		{
			pattern = pattern[1..];
			anchored = true;
		}

		if (pattern.Length == 0) return;

		_patterns.Add(new MapGitIgnorePattern(CompilePattern(pattern, anchored), negated, directoryOnly));
	}

	public bool IsIgnored(string relativePath, bool isDirectory)
	{
		string normalized = relativePath.Replace('\\', '/').TrimStart('/');
		bool ignored = false;

		foreach (MapGitIgnorePattern pattern in _patterns)
		{
			if (pattern.DirectoryOnly && !isDirectory)
			{
				if (!MatchesDirectoryOrAncestor(pattern, normalized)) continue;
			}
			else if (!pattern.Regex.IsMatch(normalized))
			{
				continue;
			}

			ignored = !pattern.Negated;
		}

		return ignored;
	}

	private static bool MatchesDirectoryOrAncestor(MapGitIgnorePattern pattern, string normalized)
	{
		int separatorIndex = normalized.IndexOf('/');
		while (separatorIndex >= 0)
		{
			if (pattern.Regex.IsMatch(normalized[..separatorIndex]))
			{
				return true;
			}

			separatorIndex = normalized.IndexOf('/', separatorIndex + 1);
		}

		return pattern.Regex.IsMatch(normalized);
	}

	private static Regex CompilePattern(string pattern, bool anchored)
	{
		string expression = "^";
		if (!anchored)
		{
			expression += "(?:.*/)?";
		}

		int index = 0;
		while (index < pattern.Length)
		{
			char current = pattern[index];
			if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
			{
				index += 2;
				if (index < pattern.Length && pattern[index] == '/')
				{
					expression += "(?:.*/)?";
					index++;
				}
				else
				{
					expression += ".*";
				}
				continue;
			}

			if (current == '*')
			{
				expression += "[^/]*";
				index++;
				continue;
			}

			if (current == '?')
			{
				expression += "[^/]";
				index++;
				continue;
			}

			expression += Regex.Escape(current.ToString());
			index++;
		}

		expression += "(?:/.*)?$";
		return new Regex(expression, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
	}

	private readonly record struct MapGitIgnorePattern(Regex Regex, bool Negated, bool DirectoryOnly)
	{
	}
}
