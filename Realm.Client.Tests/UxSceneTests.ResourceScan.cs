using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using Realm.Client.UI;
using Realm.Client.Utils;
using Environment = System.Environment;
using Realm.Client.Services;
namespace Realm.Client.Tests;

/// <summary>resolving and collecting scene and image resource paths.</summary>
public partial class UxSceneTests
	{
	private static string ResolveInPlacePath(string globalPath)
	{
		if (Path.GetExtension(globalPath).Equals(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return globalPath;
		}

		return Path.ChangeExtension(globalPath, ".webp");
	}

	private static string ResolveGlobalPath(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath))
		{
			return string.Empty;
		}

		if (resourcePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ProjectSettings.GlobalizePath(resourcePath);
		}

		return resourcePath;
	}

	private static bool IsUxImagePath(string resourcePath)
	{
		if (string.IsNullOrEmpty(resourcePath))
		{
			return false;
		}

		string extension = Path.GetExtension(resourcePath).ToLowerInvariant();
		foreach (string candidateExtension in UxImageExtensions)
		{
			if (extension == candidateExtension)
			{
				return true;
			}
		}

		return false;
	}

	private static List<string> FindAllSceneResourcePaths()
	{
		var sceneResourcePaths = new List<string>();
		CollectSceneResourcePaths("res://", sceneResourcePaths);
		sceneResourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
		return sceneResourcePaths;
	}

	private static List<string> FindAllUxImageResourcePaths()
	{
		var imageResourcePaths = new List<string>();
		CollectImageResourcePaths("res://Assets/UI", imageResourcePaths);
		imageResourcePaths.Sort(StringComparer.OrdinalIgnoreCase);
		return imageResourcePaths;
	}

	private static void CollectImageResourcePaths(string directoryResourcePath, List<string> imageResourcePaths)
	{
		using DirAccess directory = DirAccess.Open(directoryResourcePath);
		if (directory == null)
		{
			return;
		}

		string directoryPrefix = directoryResourcePath.EndsWith("/", StringComparison.Ordinal) ? directoryResourcePath : directoryResourcePath + "/";

		foreach (string fileName in directory.GetFiles())
		{
			if (IsUxImagePath(fileName))
			{
				imageResourcePaths.Add(directoryPrefix + fileName);
			}
		}

		foreach (string childDirectory in directory.GetDirectories())
		{
			if (childDirectory.StartsWith(".", StringComparison.Ordinal))
			{
				continue;
			}

			CollectImageResourcePaths(directoryPrefix + childDirectory, imageResourcePaths);
		}
	}

	private static void CollectSceneResourcePaths(string directoryResourcePath, List<string> sceneResourcePaths)
	{
		using DirAccess directory = DirAccess.Open(directoryResourcePath);
		if (directory == null)
		{
			return;
		}

		string directoryPrefix = directoryResourcePath.EndsWith("/", StringComparison.Ordinal) ? directoryResourcePath : directoryResourcePath + "/";

		foreach (string fileName in directory.GetFiles())
		{
			if (fileName.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
			{
				sceneResourcePaths.Add(directoryPrefix + fileName);
			}
		}

		foreach (string childDirectory in directory.GetDirectories())
		{
			if (childDirectory.StartsWith(".", StringComparison.Ordinal))
			{
				continue;
			}

			CollectSceneResourcePaths(directoryPrefix + childDirectory, sceneResourcePaths);
		}
	}
}
