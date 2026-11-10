using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Fastenshtein;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = Godot.Environment;

namespace Realm.Client.UI.MapEditor;

public partial class FloatingDialogBase
{
	public static string FindClosestMatch(string query, List<string> allItems, string fallbackValue = "")
	{
		if (allItems == null || allItems.Count == 0)
		{
			return fallbackValue ?? string.Empty;
		}

		if (string.IsNullOrWhiteSpace(query))
		{
			if (!string.IsNullOrEmpty(fallbackValue) && allItems.Exists(s => s.Equals(fallbackValue, StringComparison.OrdinalIgnoreCase)))
			{
				return fallbackValue;
			}

			return allItems[0];
		}

		string cleanQuery = query.Trim();
		string bestItem = allItems[0];
		int bestScore = int.MaxValue;
		var lev = new Levenshtein(cleanQuery.ToLowerInvariant());

		for (int i = 0; i < allItems.Count; i++)
		{
			string item = allItems[i];
			int score = ComputeMatchScore(item, cleanQuery, lev);
			if (score < bestScore)
			{
				bestScore = score;
				bestItem = item;
				if (score == 0) break;
			}
		}

		return bestItem;
	}

	public static int ComputeMatchScore(string item, string query, Levenshtein? lev = null)
	{
		if (string.IsNullOrEmpty(item)) return int.MaxValue;
		if (string.IsNullOrEmpty(query)) return 0;

		string itemFull = item.Trim();
		string itemLower = itemFull.ToLowerInvariant();
		string q = query.Trim().ToLowerInvariant();

		if (itemLower == q) return 0;

		string fileName = Path.GetFileName(itemFull).ToLowerInvariant();
		if (fileName == q) return 1;

		string nameNoExt = Path.GetFileNameWithoutExtension(itemFull).ToLowerInvariant();
		if (nameNoExt == q) return 2;

		int score = CheckPrefixMatches(q, itemLower, fileName, nameNoExt);
		if (score != -1) return score;

		score = CheckWordMatches(q, itemLower, nameNoExt);
		if (score != -1) return score;

		string[] tokens = nameNoExt.Split(new[] { '_', '-', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
		score = CheckTokenMatches(q, nameNoExt, tokens);
		if (score != -1) return score;

		score = CheckContainsMatches(q, itemLower, fileName, nameNoExt);
		if (score != -1) return score;

		return ComputeDistanceMatches(q, nameNoExt, tokens, lev);
	}

	private static int CheckPrefixMatches(string q, string itemLower, string fileName, string nameNoExt)
	{
		if (nameNoExt.StartsWith(q)) return 10 + (nameNoExt.Length - q.Length);
		if (fileName.StartsWith(q)) return 20 + (fileName.Length - q.Length);
		if (itemLower.StartsWith(q)) return 30 + (itemLower.Length - q.Length);
		return -1;
	}

	private static int CheckWordMatches(string q, string itemLower, string nameNoExt)
	{
		string[] queryWords = q.Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
		if (queryWords.Length <= 1) return -1;

		for (int i = 0; i < queryWords.Length; i++)
		{
			if (!itemLower.Contains(queryWords[i]))
			{
				return -1;
			}
		}

		return 25 + Math.Abs(nameNoExt.Length - q.Length);
	}

	private static int CheckTokenMatches(string q, string nameNoExt, string[] tokens)
	{
		for (int i = 0; i < tokens.Length; i++)
		{
			string token = tokens[i];
			if (token == q) return 40 + (nameNoExt.Length - q.Length);
			if (token.StartsWith(q)) return 50 + (nameNoExt.Length - q.Length);
		}

		return -1;
	}

	private static int CheckContainsMatches(string q, string itemLower, string fileName, string nameNoExt)
	{
		if (nameNoExt.Contains(q)) return 100 + nameNoExt.IndexOf(q) * 5 + (nameNoExt.Length - q.Length);
		if (fileName.Contains(q)) return 200 + fileName.IndexOf(q) * 5 + (fileName.Length - q.Length);
		if (itemLower.Contains(q)) return 300 + itemLower.IndexOf(q) * 2 + (itemLower.Length - q.Length);
		return -1;
	}

	private static int ComputeDistanceMatches(string q, string nameNoExt, string[] tokens, Levenshtein? lev)
	{
		int bestTokenDist = int.MaxValue;
		for (int i = 0; i < tokens.Length; i++)
		{
			int dist = lev != null ? lev.DistanceFrom(tokens[i]) : Levenshtein.Distance(q, tokens[i]);
			if (dist < bestTokenDist) bestTokenDist = dist;
		}

		if (bestTokenDist <= 2)
		{
			return 500 + bestTokenDist * 50 + Math.Abs(nameNoExt.Length - q.Length);
		}

		int distName = lev != null ? lev.DistanceFrom(nameNoExt) : Levenshtein.Distance(q, nameNoExt);
		return 1000 + distName * 20 + Math.Abs(nameNoExt.Length - q.Length);
	}
}
