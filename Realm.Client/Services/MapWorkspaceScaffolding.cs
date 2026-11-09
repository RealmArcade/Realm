using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Building a map workspace out of the template: the editor's schema files and agent skills, the WIT
///     file and lldb script, the map's csproj with its trimmer root and global using, NuGet.config,
///     Directory.Build.targets and the MapAPI library, the map script and its source-map attribution, the
///     wasm entry point and the solution. Each step is idempotent and keeps what the map already has; the
///     file operations retry briefly, because a workspace is also held open by dotnet watch and the ld
///     toolchain.
/// </summary>
public static class MapWorkspaceScaffolding
{
	private static string? FindBuiltApiDll()
	{
		string? repoRoot = MapWorkspacePaths.RepoRoot();
		if (repoRoot == null) return null;
		string binDir = Path.Combine(repoRoot, "Realm.MapAPI", "bin");
		if (!Directory.Exists(binDir)) return null;
		return Directory.GetFiles(binDir, "Realm.MapAPI.dll", SearchOption.AllDirectories)
			.OrderByDescending(f => File.GetLastWriteTimeUtc(f))
			.FirstOrDefault();
	}

	public static void EnsureNoiseTexturesGenerated(string directory)
	{
		NoiseTextureGenerator.EnsureAllNoiseTexturesGenerated(directory);
	}

	public static void GenerateVSCodeConfig(string directory)
	{
		string vscodeDir = Path.Combine(directory, ".vscode");
		Directory.CreateDirectory(vscodeDir);

		string[] schemas = { "metadata.schema.json", "terrain.schema.json", "manifest.schema.json" };
		foreach (string schema in schemas)
		{
			string? schemaSrc = MapWorkspacePaths.FindRootFile("Realm.MapEditorExtension/" + schema);
			if (!string.IsNullOrEmpty(schemaSrc) && File.Exists(schemaSrc))
			{
				File.Copy(schemaSrc, Path.Combine(vscodeDir, schema), true);
			}
		}

		string templateVsCodeDir = MapWorkspacePaths.TemplatePath(".vscode");
		if (Directory.Exists(templateVsCodeDir))
		{
			foreach (var file in Directory.GetFiles(templateVsCodeDir))
			{
				string fileName = Path.GetFileName(file);
				string dest = Path.Combine(vscodeDir, fileName);
				if (!File.Exists(dest) || fileName.Equals("launch.json", StringComparison.OrdinalIgnoreCase))
				{
					File.Copy(file, dest, true);
				}
			}
		}

		string agentsTemplate = MapWorkspacePaths.TemplatePath("AGENTS.md");
		string agentsTarget = Path.Combine(directory, "AGENTS.md");
		if (File.Exists(agentsTemplate) && !File.Exists(agentsTarget))
		{
			File.Copy(agentsTemplate, agentsTarget, true);
		}
	}

	/// <summary>
	///     Copies the template's agent skills into a map workspace, so a new map arrives with the skills its author's agent
	///     needs (asset assignment, translation) instead of instructions to fetch them. A file that already exists is kept:
	///     a skill a map has adapted to its own pipeline is never overwritten by the next workspace setup.
	/// </summary>
	public static void EnsureAgentSkills(string directory)
	{
		string templateSkills = MapWorkspacePaths.TemplatePath(".agent/skills");
		if (string.IsNullOrEmpty(templateSkills) || !Directory.Exists(templateSkills)) return;

		string skillsRoot = Path.Combine(directory, ".agent", "skills");
		foreach (string source in Directory.GetFiles(templateSkills, "*", SearchOption.AllDirectories))
		{
			string target = Path.Combine(skillsRoot, Path.GetRelativePath(templateSkills, source));
			if (File.Exists(target)) continue;

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(target) ?? skillsRoot);
				PathUtils.CopyFileClearingReadOnly(source, target);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				GD.PushWarning($"[MapWorkspaceScaffolding] Could not copy agent skill '{source}': {exception.Message}");
			}
		}
	}

	public static void EnsureWitFile(string directory)
	{
		string witDir = Path.Combine(directory, "wit");
		Directory.CreateDirectory(witDir);
		string witPath = Path.Combine(witDir, "game.g.wit");
		string? sourceWit = MapWorkspacePaths.WitPath();
		if (File.Exists(sourceWit))
		{
			File.Copy(sourceWit, witPath, true);
		}
	}

	public static void EnsureLldbScript(string directory)
	{
		string sourcePath = MapWorkspacePaths.TemplatePath("realm_lldb.py");
		if (!File.Exists(sourcePath)) return;

		PathUtils.CopyFileClearingReadOnly(sourcePath, Path.Combine(directory, "realm_lldb.py"));
	}

	public static void EnsureCsproj(string directory, string mapName)
	{
		string csprojPath = Path.Combine(directory, $"{mapName}.csproj");
		var existingCsprojs = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (existingCsprojs.Length > 0)
		{
			csprojPath = existingCsprojs[0];
		}

		string templatePath = MapWorkspacePaths.TemplatePath("MapScript.csproj");

		if (!File.Exists(csprojPath) && !File.Exists(templatePath))
		{
			GD.PushWarning($"[MapWorkspaceScaffolding] Could not generate {Path.GetFileName(csprojPath)}: map script template not found at {templatePath ?? "n/a"}");
			return;
		}

		RunScaffoldingStep(() => WriteOrRepairCsproj(csprojPath, templatePath), "map project file");

		RunScaffoldingStep(() => EnsureTrimmerRoot(csprojPath), "map project file");
		RunScaffoldingStep(() => EnsureSourceMapGlobalUsing(csprojPath), "map project file");
		RunScaffoldingStep(() => EnsureApiLib(directory), "map API library");
		RunScaffoldingStep(() => EnsureDirectoryBuildTargets(directory), "build targets");
		RunScaffoldingStep(() => EnsureNugetConfig(directory), "NuGet configuration");
	}

	private static void WriteOrRepairCsproj(string csprojPath, string templatePath)
	{
		if (!File.Exists(csprojPath))
		{
			File.WriteAllText(csprojPath, MapProjectFileNormalizer.Normalize(File.ReadAllText(templatePath), MapProjectFileNormalizer.ResolveDefaultIlcLlvmTarget(MapWorkspacePaths.TemplatePath("MapScript.csproj"))));
			return;
		}

		string csprojContent = File.ReadAllText(csprojPath);
		string normalized = MapProjectFileNormalizer.Normalize(csprojContent, MapProjectFileNormalizer.ResolveDefaultIlcLlvmTarget(MapWorkspacePaths.TemplatePath("MapScript.csproj")));
		if (normalized == csprojContent) return;

		File.WriteAllText(csprojPath, normalized);
		GameLog.LogToConsole($"[MapWorkspaceScaffolding] Repaired MapAPI reference in {Path.GetFileName(csprojPath)} to use the portable relative DLL path.");
	}

	private static void RunScaffoldingStep(Action step, string description)
	{
		if (TryRunFileOperation(step)) return;

		GameLog.LogError($"[MapWorkspaceScaffolding] Could not update the {description} because another program is holding the file open. The copy already in the map folder is kept.");
	}

	private const string CanonicalNugetConfigContent =
		"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
		"<configuration>\n" +
		"  <packageSources>\n" +
		"    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" />\n" +
		"    <add key=\"dotnet-experimental\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json\" />\n" +
		"  </packageSources>\n" +
		"</configuration>\n";

	private static void WriteMissingNugetConfig(string configPath, string templatePath)
	{
		if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
		{
			PathUtils.CopyFileClearingReadOnly(templatePath, configPath);
			return;
		}

		File.WriteAllText(configPath, CanonicalNugetConfigContent);
	}

	private static bool TryUpdateNugetContent(ref string content)
	{
		const string experimentalKey = "dotnet-experimental";
		const string experimentalUrl = "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json";
		const string entryToAdd = "    <add key=\"dotnet-experimental\" value=\"https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-experimental/nuget/v3/index.json\" />\n";

		if (!content.Contains(experimentalKey, StringComparison.OrdinalIgnoreCase))
		{
			int closingSourcesIndex = content.IndexOf("</packageSources>", StringComparison.OrdinalIgnoreCase);
			if (closingSourcesIndex >= 0)
			{
				content = content.Insert(closingSourcesIndex, entryToAdd);
				return true;
			}

			int closingConfigIndex = content.IndexOf("</configuration>", StringComparison.OrdinalIgnoreCase);
			if (closingConfigIndex >= 0)
			{
				string sectionToAdd = "  <packageSources>\n" + entryToAdd + "  </packageSources>\n";
				content = content.Insert(closingConfigIndex, sectionToAdd);
				return true;
			}

			content = CanonicalNugetConfigContent;
			return true;
		}

		if (!content.Contains(experimentalUrl, StringComparison.OrdinalIgnoreCase))
		{
			content = Regex.Replace(content, @"<add\s+key\s*=\s*""dotnet-experimental""\s+value\s*=\s*""[^""]*""\s*/>",
				$"<add key=\"{experimentalKey}\" value=\"{experimentalUrl}\" />", RegexOptions.IgnoreCase);
			return true;
		}

		return false;
	}

	public static void EnsureNugetConfig(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		string configPath = Path.Combine(directory, "NuGet.config");
		string templatePath = MapWorkspacePaths.TemplatePath("NuGet.config");

		try
		{
			if (!File.Exists(configPath))
			{
				WriteMissingNugetConfig(configPath, templatePath);
				return;
			}

			string content = File.ReadAllText(configPath);

			if (TryUpdateNugetContent(ref content))
			{
				var fileAttributes = File.GetAttributes(configPath);
				if ((fileAttributes & FileAttributes.ReadOnly) != 0)
				{
					File.SetAttributes(configPath, fileAttributes & ~FileAttributes.ReadOnly);
				}

				File.WriteAllText(configPath, content);
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[MapWorkspaceScaffolding] Failed to ensure NuGet.config in {directory}: {ex.Message}");
		}
	}

	public static void EnsureDirectoryBuildTargets(string directory)
	{
		string targetsPath = Path.Combine(directory, "Directory.Build.targets");
		string targetsTemplate = MapWorkspacePaths.TemplatePath("Directory.Build.targets");
		if (string.IsNullOrEmpty(targetsTemplate) || !File.Exists(targetsTemplate))
		{
			throw new FileNotFoundException($"Directory.Build.targets template was not found: {targetsTemplate}");
		}

		if (TryRunFileOperation(() => File.Copy(targetsTemplate, targetsPath, true))) return;
		if (File.Exists(targetsPath)) return;

		throw new IOException($"Directory.Build.targets could not be written into {directory}.");
	}

	private static void EnsureTrimmerRoot(string csprojPath)
	{
		if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath)) return;

		string content = File.ReadAllText(csprojPath);
		if (content.Contains("<TrimmerRootAssembly", StringComparison.OrdinalIgnoreCase)) return;

		string root = "  <ItemGroup>\n    <TrimmerRootAssembly Include=\"$(AssemblyName)\" />\n  </ItemGroup>\n";
		int projectEnd = content.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
		content = projectEnd >= 0 ? content.Insert(projectEnd, root) : content + root;

		File.WriteAllText(csprojPath, content);
		GameLog.LogToConsole($"[MapWorkspaceScaffolding] Added TrimmerRootAssembly to {Path.GetFileName(csprojPath)}");
	}

	public static void EnsureApiLib(string directory)
	{
		string libDir = Path.Combine(directory, "lib");
		Directory.CreateDirectory(libDir);

		string? sourceDll = FindBestApiDllSource();
		if (sourceDll == null)
		{
			GD.PushWarning("[MapWorkspaceScaffolding] Realm.MapAPI.dll not found (neither the Realm.MapAPI build output nor MapTemplate/lib is available). Map scripts will not compile until a valid DLL is provided.");
			return;
		}

		string dllName = Path.GetFileName(sourceDll);
		string[] fileNames = { dllName, Path.ChangeExtension(dllName, ".pdb"), Path.ChangeExtension(dllName, ".xml"), "Realm.Ecs.dll", "Realm.Ecs.pdb", "Realm.Ecs.xml", "Realm.EditorAPI.xml" };

		foreach (var fileName in fileNames)
		{
			ProcessApiLibFile(sourceDll, fileName, libDir);
		}
	}

	private static string? FindBestApiDllSource()
	{
		var candidates = new List<string>();
		string? builtDll = FindBuiltApiDll();
		if (builtDll != null) candidates.Add(builtDll);

		string templateDll = MapWorkspacePaths.TemplatePath("lib/Realm.MapAPI.dll");
		if (File.Exists(templateDll)) candidates.Add(templateDll);

		if (candidates.Count == 0) return null;

		return candidates.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
	}

	private static void ProcessApiLibFile(string sourceDll, string fileName, string libDir)
	{
		string source = Path.GetDirectoryName(sourceDll) is { } sourceDir
			? Path.Combine(sourceDir, fileName)
			: MapWorkspacePaths.TemplatePath("lib/" + fileName);

		if (!File.Exists(source))
		{
			string templateAlt = MapWorkspacePaths.TemplatePath("lib/" + fileName);
			if (File.Exists(templateAlt))
				source = templateAlt;
		}

		if (!File.Exists(source)) return;

		string dest = Path.Combine(libDir, fileName);

		try
		{
			if (ShouldCopyApiLibFile(source, dest))
				File.Copy(source, dest, true);
		}
		catch (IOException ex)
		{
			GameLog.LogError($"could not copy the map API library {source} into the workspace, so the map will not compile: {ex.GetType().Name}: {ex.Message}");
		}
	}

	private static bool ShouldCopyApiLibFile(string source, string dest)
	{
		if (!File.Exists(dest)) return true;

		var srcInfo = new FileInfo(source);
		var dstInfo = new FileInfo(dest);

		if (srcInfo.Length != dstInfo.Length) return true;

		byte[] srcBytes = File.ReadAllBytes(source);
		byte[] dstBytes = File.ReadAllBytes(dest);

		return !srcBytes.AsSpan().SequenceEqual(dstBytes);
	}

	public static void EnsureMapScript(string directory, string mapName)
	{
		string scriptPath = Path.Combine(directory, "MapScript.cs");
		if (!File.Exists(scriptPath) || new FileInfo(scriptPath).Length == 0)
		{
			string template = MapWorkspacePaths.TemplatePath("MapScript.cs");
			if (File.Exists(template))
			{
				File.WriteAllText(scriptPath, File.ReadAllText(template).Replace("class MapScript", $"class {mapName}"));
			}
		}
	}

	public static void EnsureSourceMapAttributionAttribute(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		string sourcePath = MapWorkspacePaths.TemplatePath("Attribution_SourceMapAttribute.cs");
		if (!File.Exists(sourcePath)) return;

		PathUtils.CopyFileClearingReadOnly(sourcePath, Path.Combine(directory, "Attribution_SourceMapAttribute.cs"));
	}

	private static void EnsureSourceMapGlobalUsing(string csprojPath)
	{
		if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath)) return;

		string content = File.ReadAllText(csprojPath);
		if (content.Contains("<Using Include=\"Realm.Maps\"", StringComparison.OrdinalIgnoreCase)) return;

		const string globalUsingItemGroup = "  <ItemGroup>\n    <Using Include=\"Realm.Maps\" />\n  </ItemGroup>\n";
		int projectEnd = content.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
		content = projectEnd >= 0 ? content.Insert(projectEnd, globalUsingItemGroup) : content + globalUsingItemGroup;

		File.WriteAllText(csprojPath, content);
	}

	public static void EnsureWasmEntryPoint(string directory)
	{
		MapTemplateGeneratorService.EnsureWasmEntryPoint(directory);
	}

	private const int FileLockRetryAttempts = 4;

	private const int FileLockRetryDelayMilliseconds = 100;

	public static bool TryRunFileOperation(Action operation)
	{
		for (int attempt = 0; attempt < FileLockRetryAttempts; attempt++)
		{
			try
			{
				operation();
				return true;
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				System.Threading.Thread.Sleep(FileLockRetryDelayMilliseconds);
			}
		}

		return false;
	}

	public static void ClearFolderBestEffort(string folderPath)
	{
		foreach (string filePath in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
		{
			string fileToClear = filePath;
			if (TryRunFileOperation(() => DeleteFileClearingReadOnly(fileToClear))) continue;

			GameLog.LogError($"[MapWorkspaceScaffolding] '{fileToClear}' is held open by another program, so the untitled map keeps the version the template is about to replace it with.");
		}

		foreach (string directoryPath in Directory.GetDirectories(folderPath))
		{
			string directoryToClear = directoryPath;
			TryRunFileOperation(() => Directory.Delete(directoryToClear, true));
		}
	}

	private static void DeleteFileClearingReadOnly(string filePath)
	{
		if (!File.Exists(filePath)) return;

		FileAttributes attributes = File.GetAttributes(filePath);
		if ((attributes & FileAttributes.ReadOnly) != 0)
		{
			File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
		}

		File.Delete(filePath);
	}

	public static void CopyFolderBestEffort(string sourceFolder, string targetFolder)
	{
		foreach (string sourcePath in Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories))
		{
			string relativePath = sourcePath.Substring(sourceFolder.Length + 1);
			if (MapWorkspaceService.IsIgnoredPathDomain(relativePath)) continue;

			string targetPath = Path.Combine(targetFolder, relativePath);
			string? targetDirectory = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(targetDirectory))
			{
				Directory.CreateDirectory(targetDirectory);
			}

			bool copyIsMutable = PathUtils.IsMutableMapFileType(relativePath);
			if (TryRunFileOperation(() => CopyTemplateFile(sourcePath, targetPath, copyIsMutable))) continue;

			GameLog.LogError($"[MapWorkspaceScaffolding] Could not write '{relativePath}' into the untitled map because another program is holding it open. The file already in the folder is kept.");
		}
	}

	private static void CopyTemplateFile(string sourcePath, string targetPath, bool copyIsMutable)
	{
		if (copyIsMutable)
		{
			PathUtils.CopyFileClearingReadOnly(sourcePath, targetPath);
			return;
		}

		PathUtils.LinkOrCopyFile(sourcePath, targetPath, preferHardLink: true);
	}

	public static void EnsureSolutionFile(string directory, string mapName)
	{
		string solutionName = QuoteToolArgument(mapName);
		string projectName = QuoteToolArgument($"{mapName}.csproj");
		string slnPath = Path.Combine(directory, $"{mapName}.slnx");
		string dotnetExecutablePath = DotnetSdkResolver.ResolveSdkExecutablePath();
		if (!File.Exists(slnPath) && !string.IsNullOrEmpty(dotnetExecutablePath))
		{
			try
			{
				NativeToolRunner.RunTool(dotnetExecutablePath, $"new sln -n {solutionName}", timeoutMs: 15000, workingDir: directory);
				NativeToolRunner.RunTool(dotnetExecutablePath, $"sln add {projectName}", timeoutMs: 15000, workingDir: directory);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"could not create the solution for map project {projectName}, so the workspace has no buildable solution: {ex.GetType().Name}: {ex.Message}");
			}
		}
	}

	private static string QuoteToolArgument(string argument)
	{
		return argument.Contains(' ') ? $"\"{argument}\"" : argument;
	}
}
