using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;

namespace Realm.Tools.Cli;

internal static class CliFileSet
{
	internal static bool EnsurePathExists(string inputPath)
	{
		if (File.Exists(inputPath) || Directory.Exists(inputPath))
		{
			return true;
		}

		Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
		return false;
	}

	internal static IEnumerable<string> TraverseFiles(string inputPath, bool recursive, Func<string, bool>? filter = null)
	{
		if (File.Exists(inputPath))
		{
			if (filter == null || filter(inputPath))
			{
				yield return inputPath;
			}
		}
		else if (Directory.Exists(inputPath))
		{
			var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", searchOption))
			{
				if (filter == null || filter(file))
				{
					yield return file;
				}
			}
		}
	}

	internal static string ResolveTargetOutputPath(
		string inputRoot,
		string currentFile,
		string? outputDestination,
		bool inPlace,
		Func<string, string>? extensionResolver = null)
	{
		string targetExtension = extensionResolver != null
			? extensionResolver(currentFile)
			: Path.GetExtension(currentFile);

		if (inPlace)
		{
			return extensionResolver != null
				? Path.ChangeExtension(currentFile, targetExtension)
				: currentFile;
		}

		if (Directory.Exists(inputRoot))
		{
			return ResolveDirectoryTarget(inputRoot, currentFile, outputDestination, targetExtension);
		}

		if (!string.IsNullOrEmpty(outputDestination))
		{
			return ResolveOutputDestination(currentFile, outputDestination, targetExtension, extensionResolver != null);
		}

		return Path.ChangeExtension(currentFile, targetExtension);
	}

	internal static string ResolveDirectoryTarget(
		string inputRoot,
		string currentFile,
		string? outputDestination,
		string targetExtension)
	{
		string relativePath = Path.GetRelativePath(inputRoot, currentFile);
		if (!string.IsNullOrEmpty(outputDestination))
		{
			string relativeTarget = Path.ChangeExtension(relativePath, targetExtension);
			return Path.Combine(outputDestination, relativeTarget);
		}

		return Path.ChangeExtension(currentFile, targetExtension);
	}

	internal static string ResolveOutputDestination(
		string currentFile,
		string outputDestination,
		string targetExtension,
		bool hasExtensionResolver)
	{
		if (Directory.Exists(outputDestination) ||
		    outputDestination.EndsWith(Path.DirectorySeparatorChar) ||
		    outputDestination.EndsWith(Path.AltDirectorySeparatorChar))
		{
			string fileName = Path.ChangeExtension(Path.GetFileName(currentFile), targetExtension);
			return Path.Combine(outputDestination, fileName);
		}

		string explicitExt = Path.GetExtension(outputDestination);
		if (!string.IsNullOrEmpty(explicitExt))
		{
			return outputDestination;
		}

		return hasExtensionResolver
			? Path.ChangeExtension(outputDestination, targetExtension)
			: outputDestination;
	}

	internal static int ProcessTraversedFiles(
		string inputPath,
		string? outputPath,
		bool recursive,
		bool inPlace,
		Func<string, bool> fileFilter,
		Func<string, string>? extensionResolver,
		Func<string, string, int> processFile,
		Func<string, string, string>? customPathResolver = null,
		string? summaryActionName = null)
	{
		if (!EnsurePathExists(inputPath))
		{
			return 1;
		}

		if (File.Exists(inputPath))
		{
			if (!fileFilter(inputPath))
			{
				string ext = Path.GetExtension(inputPath);
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for input '{inputPath}'.");
				return 1;
			}

			string targetPath = customPathResolver != null
				? customPathResolver(inputPath, inputPath)
				: ResolveTargetOutputPath(inputPath, inputPath, outputPath, inPlace, extensionResolver);

			EnsureTargetDirectoryExists(targetPath);

			return processFile(inputPath, targetPath);
		}

		string fullInputDir = Path.GetFullPath(inputPath);
		var matchingFiles = TraverseFiles(fullInputDir, recursive, fileFilter).ToArray();

		if (matchingFiles.Length == 0)
		{
			Console.WriteLine($"No matching files found in: {inputPath}");
			return 0;
		}

		var (successCount, failCount) = ProcessMultipleFiles(matchingFiles, fullInputDir, outputPath, inPlace, extensionResolver, processFile, customPathResolver);

		if (!string.IsNullOrEmpty(summaryActionName))
		{
			Console.WriteLine($"Finished {summaryActionName}. {successCount} succeeded, {failCount} failed.");
		}

		return failCount > 0 ? 1 : 0;
	}

	internal static void EnsureTargetDirectoryExists(string targetPath)
	{
		string? outDir = Path.GetDirectoryName(targetPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
	}

	internal static (int successCount, int failCount) ProcessMultipleFiles(
		string[] matchingFiles,
		string fullInputDir,
		string? outputPath,
		bool inPlace,
		Func<string, string>? extensionResolver,
		Func<string, string, int> processFile,
		Func<string, string, string>? customPathResolver)
	{
		int successCount = 0;
		int failCount = 0;

		foreach (string file in matchingFiles)
		{
			string targetPath = customPathResolver != null
				? customPathResolver(fullInputDir, file)
				: ResolveTargetOutputPath(fullInputDir, file, outputPath, inPlace, extensionResolver);

			EnsureTargetDirectoryExists(targetPath);

			int exitCode = processFile(file, targetPath);
			if (exitCode == 0)
			{
				successCount++;
			}
			else
			{
				failCount++;
			}
		}

		return (successCount, failCount);
	}
}
