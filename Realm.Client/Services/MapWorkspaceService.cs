using Godot;
using Realm.Client.Animation;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Realm.Client.Services;

public static partial class MapWorkspaceService
{
	public const string DefaultWorkspaceFolder = "temp_map_workspace";
	public const string DefaultWorkspaceGodotPath = "user://temp_map_workspace";

	public static string GetDefaultWorkspaceGlobalPath() =>
		global::Godot.ProjectSettings.GlobalizePath(DefaultWorkspaceGodotPath);

	public static string GetActiveWorkspacePath()
	{
		if (!string.IsNullOrEmpty(UI.MapEditorHUD.Instance?.TempWorkspacePath))
			return UI.MapEditorHUD.Instance.TempWorkspacePath;
		if (Realm.Client.Core.GameHost.Instance != null && !string.IsNullOrEmpty(Realm.Client.Core.GameHost.Instance.CurrentMapDirectory))
			return Realm.Client.Core.GameHost.Instance.CurrentMapDirectory;
		return GetDefaultWorkspaceGlobalPath();
	}

	private static string _cachedRepoRoot;
	private static bool _repoRootResolved;

	private static string GetRepoRoot()
	{
		if (_repoRootResolved) return _cachedRepoRoot;
		_repoRootResolved = true;
		string baseDir = PathUtils.GetProjectRoot();
		var current = new DirectoryInfo(baseDir);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "Realm.sln")) || Directory.Exists(Path.Combine(current.FullName, "Realm.MapAPI")))
			{
				_cachedRepoRoot = current.FullName.Replace("\\", "/");
				return _cachedRepoRoot;
			}
			current = current.Parent;
		}
		GD.PushWarning("[MapWorkspaceService] Could not locate the Realm repository (Realm.sln or Realm.MapAPI not found above res://). Template files and the MapAPI DLL will not be available.");
		return null;
	}

	private static string FindRootFile(string relativePath)
	{
		string found = PathUtils.FindPath(relativePath);
		if (File.Exists(found) || Directory.Exists(found))
		{
			return found;
		}

		string repoRoot = GetRepoRoot();
		if (repoRoot == null) return null;
		return Path.Combine(repoRoot, relativePath).Replace("\\", "/");
	}

	private static string GetSchemaSourcePath()
	{
		return FindRootFile("Realm.MapEditorExtension/metadata.schema.json");
	}

	public static string GetTemplatePath(string fileName)
	{
		return FindRootFile("MapTemplate/" + fileName);
	}

	private const string MapApiDllRelativePath = "lib/Realm.MapAPI.dll";

	private static string FindBuiltApiDll()
	{
		string repoRoot = GetRepoRoot();
		if (repoRoot == null) return null;
		string binDir = Path.Combine(repoRoot, "Realm.MapAPI", "bin");
		if (!Directory.Exists(binDir)) return null;
		return Directory.GetFiles(binDir, "Realm.MapAPI.dll", SearchOption.AllDirectories)
			.OrderByDescending(f => File.GetLastWriteTimeUtc(f))
			.FirstOrDefault();
	}

	public static void SetupWorkspace(string directory, string mapName)
	{
		if (string.IsNullOrEmpty(directory)) return;
		Directory.CreateDirectory(directory);

		GenerateVSCodeConfig(directory);
		EnsureWitFile(directory);
		EnsureCsproj(directory, mapName);
		EnsureMapScript(directory, mapName);
		EnsureWasmEntryPoint(directory);
		EnsureMetadataJson(directory);
		EnsureLicenseFile(directory);
		Realm.Client.Utils.MapAssetHelper.EnsureManifestJson(directory);
		EnsureSolutionFile(directory, mapName);
		NormalizeMetadataTextureEntries(directory);
		EnsureGlbAssetsOptimized(directory);
		EnsurePngAssetsConverted(directory);
		EnsureNoiseTexturesGenerated(directory);
	}

	public static void EnsureNoiseTexturesGenerated(string directory)
	{
		Realm.Client.Services.NoiseTextureGenerator.EnsureAllNoiseTexturesGenerated(directory);
	}

	public static void CleanWorkspaceDirectory(string targetDir)
	{
		if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir)) return;
		try
		{
			foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
			{
				var fileAttributes = File.GetAttributes(file);
				if ((fileAttributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					File.SetAttributes(file, fileAttributes & ~FileAttributes.ReadOnly);
				}
				File.Delete(file);
			}

			foreach (var directory in Directory.GetDirectories(targetDir))
			{
				Directory.Delete(directory, true);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] CleanWorkspaceDirectory error: {ex.Message}");
		}
	}

	public static void CopyFolderToWorkspace(string sourceFolder, string targetWorkspacePath, string? mapName = null)
	{
		if (string.IsNullOrEmpty(sourceFolder) || !Directory.Exists(sourceFolder) || string.IsNullOrEmpty(targetWorkspacePath)) return;

		CleanWorkspaceDirectory(targetWorkspacePath);
		Directory.CreateDirectory(targetWorkspacePath);

		string resolvedMapName = !string.IsNullOrWhiteSpace(mapName) ? mapName : Path.GetFileName(sourceFolder) ?? "MapScript";

		var allFiles = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
		var filesToProcess = new List<(string Source, string Target, bool IsMutable)>(allFiles.Length);
		var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in allFiles)
		{
			string relativePath = file.Substring(sourceFolder.Length + 1);
			if (UI.MapEditorHUD.IsIgnoredPath(relativePath)) continue;

			string targetFile = Path.Combine(targetWorkspacePath, relativePath);
			string? targetDir = Path.GetDirectoryName(targetFile);
			
			if (!string.IsNullOrEmpty(targetDir) && createdDirs.Add(targetDir))
				Directory.CreateDirectory(targetDir);
				
			filesToProcess.Add((file, targetFile, PathUtils.IsMutableMapFileType(relativePath)));
		}

		Parallel.ForEach(filesToProcess, ProcessFileItem);

		SetupWorkspace(targetWorkspacePath, resolvedMapName);
	}

	private static void ProcessFileItem((string Source, string Target, bool IsMutable) item)
	{
		if (item.IsMutable)
			PathUtils.CopyFileClearingReadOnly(item.Source, item.Target);
		else
			PathUtils.LinkOrCopyFile(item.Source, item.Target, preferHardLink: true);
	}

	public static void CleanWorkspaceBinaries(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		foreach (var folder in new[] { "bin", "obj" })
		{
			string targetDir = Path.Combine(directory, folder);
			if (Directory.Exists(targetDir))
			{
				try
				{
					foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
					{
						var attrs = File.GetAttributes(file);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
						}
					}
					Directory.Delete(targetDir, true);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to clean build folder {folder}: {ex.Message}");
				}
			}
		}
	}

	public static void GenerateVSCodeConfig(string directory)
	{
		string vscodeDir = Path.Combine(directory, ".vscode");
		Directory.CreateDirectory(vscodeDir);

		string[] schemas = { "metadata.schema.json", "terrain.schema.json", "manifest.schema.json" };
		foreach (string schema in schemas)
		{
			string schemaSrc = FindRootFile("Realm.MapEditorExtension/" + schema);
			if (!string.IsNullOrEmpty(schemaSrc) && File.Exists(schemaSrc))
			{
				File.Copy(schemaSrc, Path.Combine(vscodeDir, schema), true);
			}
		}

		string templateVsCodeDir = GetTemplatePath(".vscode");
		if (Directory.Exists(templateVsCodeDir))
		{
			foreach (var file in Directory.GetFiles(templateVsCodeDir))
			{
				string dest = Path.Combine(vscodeDir, Path.GetFileName(file));
				if (!File.Exists(dest))
				{
					File.Copy(file, dest, true);
				}
			}
		}

		string agentsTemplate = GetTemplatePath("AGENTS.md");
		string agentsTarget = Path.Combine(directory, "AGENTS.md");
		if (File.Exists(agentsTemplate) && !File.Exists(agentsTarget))
		{
			File.Copy(agentsTemplate, agentsTarget, true);
		}
	}

	private static string GetWitPath()
	{
		return FindRootFile("Realm.MapAPI/wit/game.g.wit");
	}

	public static void EnsureWitFile(string directory)
	{
		string witDir = Path.Combine(directory, "wit");
		Directory.CreateDirectory(witDir);
		string witPath = Path.Combine(witDir, "game.g.wit");
		string sourceWit = GetWitPath();
		if (File.Exists(sourceWit))
		{
			File.Copy(sourceWit, witPath, true);
		}
	}

	public static void EnsureCsproj(string directory, string mapName)
	{
		string csprojPath = Path.Combine(directory, $"{mapName}.csproj");
		var existingCsprojs = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (existingCsprojs.Length > 0)
		{
			csprojPath = existingCsprojs[0];
		}

		string templatePath = GetTemplatePath("MapScript.csproj");

		if (!File.Exists(csprojPath))
		{
			if (!File.Exists(templatePath))
			{
				GD.PushWarning($"[MapWorkspaceService] Could not generate {Path.GetFileName(csprojPath)}: map script template not found at {templatePath ?? "n/a"}");
				return;
			}
			string csprojContent = File.ReadAllText(templatePath);
			csprojContent = NormalizeMapApiReference(csprojContent);
			File.WriteAllText(csprojPath, csprojContent);
		}
		else
		{
			string csprojContent = File.ReadAllText(csprojPath);
			string normalized = NormalizeMapApiReference(csprojContent);
			if (normalized != csprojContent)
			{
				File.WriteAllText(csprojPath, normalized);
				GD.Print($"[MapWorkspaceService] Repaired MapAPI reference in {Path.GetFileName(csprojPath)} to use the portable relative DLL path.");
			}
		}

		EnsureTrimmerRoot(csprojPath);

		EnsureApiLib(directory);

		EnsureDirectoryBuildTargets(directory);

		EnsureNugetConfig(directory);
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
		string templatePath = GetTemplatePath("NuGet.config");

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
			GD.PrintErr($"[MapWorkspaceService] Failed to ensure NuGet.config in {directory}: {ex.Message}");
		}
	}

	public static void EnsureDirectoryBuildTargets(string directory)
	{
		string targetsPath = Path.Combine(directory, "Directory.Build.targets");
		string targetsTemplate = GetTemplatePath("Directory.Build.targets");
		if (!string.IsNullOrEmpty(targetsTemplate) && File.Exists(targetsTemplate))
		{
			try
			{
				File.Copy(targetsTemplate, targetsPath, true);
				return;
			}
			catch
			{
			}
		}

		if (!File.Exists(targetsPath) || !File.ReadAllText(targetsPath).Contains("_InitializeWasiSdk"))
		{
			try
			{
				string fallbackContent = "<Project>\n  <Target Name=\"PrepareInputsForWasmBuild\" />\n  <Target Name=\"_InitializeWasiSdk\" Condition=\"'$(_targetOS)' == 'wasi'\">\n    <Error Text=\"Wasi SDK not found, not compiling to WebAssembly. To enable WebAssembly compilation, install Wasi SDK and ensure the WASI_SDK_PATH environment variable points to the directory containing share/wasi-sysroot\" Condition=\"'$(WASI_SDK_PATH)' == ''\" />\n    <PropertyGroup>\n      <_NativeWasmSdkBinPath>$([MSBuild]::NormalizeDirectory('$(WASI_SDK_PATH)', 'bin'))</_NativeWasmSdkBinPath>\n      <CppCompiler Condition=\"'$(CppCompiler)' == ''\">&quot;$(_NativeWasmSdkBinPath)clang&quot;</CppCompiler>\n      <CppLinker Condition=\"'$(CppLinker)' == ''\">$(CppCompiler)</CppLinker>\n    </PropertyGroup>\n  </Target>\n</Project>\n";
				File.WriteAllText(targetsPath, fallbackContent);
			}
			catch
			{
			}
		}
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
		GD.Print($"[MapWorkspaceService] Added TrimmerRootAssembly to {Path.GetFileName(csprojPath)}");
	}

	[GeneratedRegex(@"<ProjectReference\s+Include=""[^""]*Realm\.MapAPI\.csproj""[^>]*/>", RegexOptions.Singleline)]
	private static partial Regex ProjectReferenceMapApiRegex();

	[GeneratedRegex(@"<Reference\s+Include=""Realm\.MapAPI""\s*>.*?</Reference>", RegexOptions.Singleline)]
	private static partial Regex ReferenceMapApiRegex();

	[GeneratedRegex(@"<HintPath>[^<]*Realm\.MapAPI\.dll</HintPath>", RegexOptions.Singleline)]
	private static partial Regex HintPathMapApiRegex();

	[GeneratedRegex(@"<IlcLlvmTarget>\s*([^<\s]+)\s*</IlcLlvmTarget>", RegexOptions.IgnoreCase)]
	private static partial Regex IlcLlvmTargetRegex();

	[GeneratedRegex(@"<Target\s+Name=""ClearComponentWit""[^>]*>.*?</Target>", RegexOptions.Singleline)]
	private static partial Regex ClearComponentWitTargetRegex();

	public static string GetDefaultIlcLlvmTarget()
	{
		string templatePath = GetTemplatePath("MapScript.csproj");
		if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
		{
			string templateContent = File.ReadAllText(templatePath);
			var match = IlcLlvmTargetRegex().Match(templateContent);
			if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
			{
				return match.Groups[1].Value.Trim();
			}
		}

		return WasiSdkResolver.GetDefaultIlcLlvmTarget();
	}

	private static string NormalizeMapApiReference(string csprojContent)
	{
		csprojContent = ProjectReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = ReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = HintPathMapApiRegex().Replace(csprojContent,
			$"<HintPath>{MapApiDllRelativePath}</HintPath>");

		if (!csprojContent.Contains("TrimmerRootAssembly"))
		{
			csprojContent = csprojContent.Replace("</Project>",
				"  <ItemGroup>\n    <TrimmerRootAssembly Include=\"$(MSBuildProjectName)\" />\n  </ItemGroup>\n</Project>");
		}

		string defaultTarget = GetDefaultIlcLlvmTarget();

		if (IlcLlvmTargetRegex().IsMatch(csprojContent))
		{
			csprojContent = IlcLlvmTargetRegex().Replace(csprojContent,
				$"<IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>");
		}
		else if (csprojContent.Contains("EnableAotLate", StringComparison.OrdinalIgnoreCase))
		{
			csprojContent = Regex.Replace(csprojContent, @"(<Target\s+Name=""EnableAotLate""[^>]*>\s*<PropertyGroup>)",
				$"$1\n      <IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>", RegexOptions.IgnoreCase);
		}
		else
		{
			string aotTarget = $"  <Target Name=\"EnableAotLate\" BeforeTargets=\"ImportRuntimeIlcPackageTarget;IlcCompile;_ComputeAssembliesToCompileToNative\">\n    <PropertyGroup>\n      <PublishAot>true</PublishAot>\n      <IlcLlvmTarget>{defaultTarget}</IlcLlvmTarget>\n    </PropertyGroup>\n  </Target>\n";
			int projectEnd = csprojContent.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
			csprojContent = projectEnd >= 0 ? csprojContent.Insert(projectEnd, aotTarget) : csprojContent + aotTarget;
		}

		string fixWasiLinkArgsTarget = "  <Target Name=\"FixWasiSdkLinkArgs\" BeforeTargets=\"LinkNative;LinkNativeLlvm\">\n    <PropertyGroup>\n      <IlcWasmGlobalBase>1048576</IlcWasmGlobalBase>\n    </PropertyGroup>\n    <ItemGroup>\n      <WasmComponentTypeWit Remove=\"@(WasmComponentTypeWit)\" />\n    </ItemGroup>\n  </Target>\n";

		if (ClearComponentWitTargetRegex().IsMatch(csprojContent))
		{
			csprojContent = ClearComponentWitTargetRegex().Replace(csprojContent, fixWasiLinkArgsTarget.TrimEnd());
		}
		else if (csprojContent.Contains("FixWasiSdkLinkArgs", StringComparison.OrdinalIgnoreCase))
		{
			if (!csprojContent.Contains("IlcWasmGlobalBase", StringComparison.OrdinalIgnoreCase))
			{
				csprojContent = Regex.Replace(csprojContent, @"(<Target\s+Name=""FixWasiSdkLinkArgs""[^>]*>)",
					"$1\n    <PropertyGroup>\n      <IlcWasmGlobalBase>1048576</IlcWasmGlobalBase>\n    </PropertyGroup>", RegexOptions.IgnoreCase);
			}
		}
		else
		{
			int projectEnd = csprojContent.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
			csprojContent = projectEnd >= 0 ? csprojContent.Insert(projectEnd, fixWasiLinkArgsTarget) : csprojContent + fixWasiLinkArgsTarget;
		}

		return csprojContent;
	}

	public static void EnsureApiLib(string directory)
	{
		string libDir = Path.Combine(directory, "lib");
		Directory.CreateDirectory(libDir);

		string sourceDll = FindBestApiDllSource();
		if (sourceDll == null)
		{
			GD.PushWarning("[MapWorkspaceService] Realm.MapAPI.dll not found (neither the Realm.MapAPI build output nor MapTemplate/lib is available). Map scripts will not compile until a valid DLL is provided.");
			return;
		}

		string dllName = Path.GetFileName(sourceDll);
		string[] fileNames = { dllName, Path.ChangeExtension(dllName, ".pdb"), Path.ChangeExtension(dllName, ".xml"), "Realm.EditorAPI.xml" };

		foreach (var fileName in fileNames)
		{
			ProcessApiLibFile(sourceDll, fileName, libDir);
		}
	}

	private static string? FindBestApiDllSource()
	{
		var candidates = new System.Collections.Generic.List<string>();
		string builtDll = FindBuiltApiDll();
		if (builtDll != null) candidates.Add(builtDll);
		
		string templateDll = GetTemplatePath("lib/Realm.MapAPI.dll");
		if (templateDll != null && File.Exists(templateDll)) candidates.Add(templateDll);

		if (candidates.Count == 0) return null;

		return candidates.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
	}

	private static void ProcessApiLibFile(string sourceDll, string fileName, string libDir)
	{
		string source = Path.Combine(Path.GetDirectoryName(sourceDll), fileName);
		
		if (!File.Exists(source))
		{
			string templateAlt = GetTemplatePath("lib/" + fileName);
			if (templateAlt != null && File.Exists(templateAlt))
				source = templateAlt;
		}

		if (!File.Exists(source)) return;

		string dest = Path.Combine(libDir, fileName);
		
		try
		{
			if (ShouldCopyApiLibFile(source, dest))
				File.Copy(source, dest, true);
		}
		catch (IOException) { }
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
			string template = GetTemplatePath("MapScript.cs");
			if (File.Exists(template))
			{
				File.WriteAllText(scriptPath, File.ReadAllText(template).Replace("class MapScript", $"class {mapName}"));
			}
		}
	}

	public static void EnsureWasmEntryPoint(string directory)
	{
		string entryPointPath = Path.Combine(directory, "WasmEntryPoint.cs");
		string template = GetTemplatePath("WasmEntryPoint.cs");
		if (File.Exists(template))
		{
			if (!File.Exists(entryPointPath) || new FileInfo(entryPointPath).Length == 0)
			{
				File.Copy(template, entryPointPath);
			}
			else
			{
				string existing = File.ReadAllText(entryPointPath);
				if (existing.Contains("private static IWasmModule? _mapScript;"))
				{
					File.Copy(template, entryPointPath, true);
				}
			}
		}
	}

	public static void EnsureMetadataJson(string directory)
	{
		string metadataPath = MetadataService.ResolveMetadataPath(directory);
		if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length == 0)
		{
			string templateMeta = GetTemplatePath("metadata.json");
			if (File.Exists(templateMeta))
			{
				File.Copy(templateMeta, metadataPath, true);
			}
			else
			{
				MetadataService.Instance.SaveMetadata(metadataPath, new MapMetadata());
			}
		}

		if (File.Exists(metadataPath))
		{
			try
			{
				MetadataService.Instance.UpdateMetadata(directory, meta => MetadataService.Instance.CleanMetadata(meta));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[MapWorkspaceService] Failed to ensure license in metadata.json: {ex.Message}");
			}
		}

		string templateMetaPath = GetTemplatePath("metadata.json");
		if (File.Exists(templateMetaPath))
		{
			string templateAssetsDir = Path.Combine(Path.GetDirectoryName(templateMetaPath), "Assets");
			if (Directory.Exists(templateAssetsDir))
			{
				Realm.Client.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(templateAssetsDir);
				string destAssetsDir = Path.Combine(directory, "Assets");
				foreach (var assetFile in Directory.GetFiles(templateAssetsDir, "*", SearchOption.AllDirectories))
				{
					string relPath = Path.GetRelativePath(templateAssetsDir, assetFile);
					string destFile = Path.Combine(destAssetsDir, relPath);
					Directory.CreateDirectory(Path.GetDirectoryName(destFile));
					if (!File.Exists(destFile))
					{
						File.Copy(assetFile, destFile, true);
					}
				}
			}
		}

		Realm.Client.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(Path.Combine(directory, "Assets"));
		EnsureLicenseFile(directory);
	}

	public const string UgcLicenseSummaryText = @"REALM PLATFORM USER-GENERATED CONTENT (UGC)

This map and its custom assets are User-Generated Content created for the 
Realm Platform ecosystem.

Use, remixing, and redistribution of this content are governed by the 
Realm Platform UGC License:
https://www.realm-game.com/RealmPlatform_UGC_License_v1.txt

For details on permissions, streaming, and standalone export restrictions, 
please visit the URL above.
";

	public static void EnsureLicenseFile(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		try
		{
			Directory.CreateDirectory(directory);
			string targetLicensePath = Path.Combine(directory, "LICENSE.md");
			string[] candidateSourcePaths = new[]
			{
				PathUtils.FindPath("MapTemplate/LICENSE.md"),
				Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MapTemplate", "LICENSE.md")
			};

			bool copied = false;
			foreach (var candidate in candidateSourcePaths)
			{
				if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
				{
					File.Copy(candidate, targetLicensePath, overwrite: true);
					copied = true;
					break;
				}
			}

			if (!copied)
			{
				File.WriteAllText(targetLicensePath, UgcLicenseSummaryText);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to copy LICENSE.md to {directory}: {ex.Message}");
		}
	}

	public static void EnsureSolutionFile(string directory, string mapName)
	{
		string slnPath = Path.Combine(directory, $"{DefaultWorkspaceFolder}.slnx");
		if (!File.Exists(slnPath))
		{
			try
			{
				Realm.Shared.NativeToolRunner.RunTool("dotnet", $"new sln -n {DefaultWorkspaceFolder}", timeoutMs: 15000, workingDir: directory);
				Realm.Shared.NativeToolRunner.RunTool("dotnet", $"sln add {mapName}.csproj", timeoutMs: 15000, workingDir: directory);
			}
			catch
			{
			}
		}
	}

	private static readonly ConcurrentDictionary<string, (DateTime LastWriteTime, bool HasFlag)> _optimizedFlagCache = new(StringComparer.OrdinalIgnoreCase);

	public static void EnsureGlbAssetsOptimized(string workspacePath)
	{
		NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			bool anyReimported = ProcessGlbFiles(glbFiles, workspacePath);

			if (anyReimported)
				ModelCache.Clear();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsureGlbAssetsOptimized error: {ex.Message}");
		}
	}

	private static bool ProcessGlbFiles(string[] glbFiles, string workspacePath)
	{
		bool anyReimported = false;
		var optimizer = new Realm.Shared.GlbOptimizer();
		var options = new Realm.Shared.OptimizationOptions
		{
			SimplificationRatio = 0.5f, AllowedPixelError = 1.5f, MaxTextureResolution = 1024,
			ForceReDecimate = false, CompressTextures = true, GenerateLods = true
		};

		foreach (string glbPath in glbFiles)
		{
			if (IsPathIgnored(glbPath)) continue;

			if (!TryGetFileWriteTime(glbPath, out DateTime lastWrite)) continue;

			if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag)
				continue;

			anyReimported |= TryOptimizeGlb(glbPath, workspacePath, optimizer, options, lastWrite);
		}
		return anyReimported;
	}

	private static bool IsPathIgnored(string path)
	{
		string normalized = path.Replace("\\", "/");
		return normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || 
		       normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/");
	}

	private static bool TryGetFileWriteTime(string path, out DateTime lastWrite)
	{
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(path);
			return true;
		}
		catch
		{
			lastWrite = default;
			return false;
		}
	}

	private static bool TryOptimizeGlb(string glbPath, string workspacePath, Realm.Shared.GlbOptimizer optimizer, Realm.Shared.OptimizationOptions options, DateTime lastWrite)
	{
		byte[] glbBytes = null;
		try
		{
			glbBytes = File.ReadAllBytes(glbPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to read GLB {glbPath}: {ex.Message}");
			return false;
		}

		if (glbBytes == null || glbBytes.Length == 0) return false;

		if (optimizer.IsOptimized(glbBytes))
		{
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		string fileName = Path.GetFileName(glbPath);
		GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' is missing realm_optimize_completed extras. Optimizing into workspace...");

		glbBytes = ExtractAndStripAnimations(glbPath, workspacePath, fileName) ?? glbBytes;

		return PerformGlbOptimization(glbPath, workspacePath, fileName, glbBytes, optimizer, options, lastWrite);
	}

	private static byte[] ExtractAndStripAnimations(string glbPath, string workspacePath, string fileName)
	{
		try
		{
			string animsDir = Path.Combine(workspacePath, "Assets", "animations");
			Directory.CreateDirectory(animsDir);
			var extractedAnims = MixamoAnimationImporter.ExtractAnimationsFromGlb(glbPath);
			
			foreach (var (animName, animData) in extractedAnims)
			{
				string animFileName = $"{animName.ToLowerInvariant()}.ranim";
				string animFilePath = Path.Combine(animsDir, animFileName);
				if (!File.Exists(animFilePath))
				{
					RealmAnimationSerializer.SaveToFile(animFilePath, animData);
					UpdateMetadataAnimationHash(workspacePath, animFileName, RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(animFilePath), ".ranim"));
				}
			}

			if (extractedAnims.Count > 0)
			{
				MixamoAnimationImporter.StripAnimationsFromGlb(glbPath, glbPath);
				return File.ReadAllBytes(glbPath);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Animation extraction error for {fileName}: {ex.Message}");
		}
		return null;
	}

	private static bool PerformGlbOptimization(string glbPath, string workspacePath, string fileName, byte[] glbBytes, Realm.Shared.GlbOptimizer optimizer, Realm.Shared.OptimizationOptions options, DateTime lastWrite)
	{
		Realm.Shared.OptimizationResult optResult = default;
		try
		{
			optResult = optimizer.Optimize(glbBytes, options);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Exception ({ex.GetType().Name}) optimizing {fileName}: {ex.Message}");
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
		{
			return SaveOptimizedGlb(workspacePath, glbPath, fileName, optResult);
		}
		
		_optimizedFlagCache[glbPath] = (lastWrite, true);
		GD.PrintErr($"[MapWorkspaceService] Optimization failed for {fileName}: {optResult.ErrorMessage}");
		return false;
	}


	private static bool IsIgnoredGlbPath(string normalized)
	{
		return normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || 
		       normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/");
	}

	private static List<string> GetUnoptimizedGlbFiles(string[] glbFiles, Realm.Shared.GlbOptimizer optimizer)
	{
		var unoptimizedList = new List<string>();
		foreach (string glbPath in glbFiles)
		{
			string normalized = glbPath.Replace("\\", "/");
			if (IsIgnoredGlbPath(normalized)) continue;

			if (!TryReadGlbBytes(glbPath, out var lastWrite, out var bytes)) continue;

			if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag) continue;

			if (optimizer.IsOptimized(bytes))
			{
				_optimizedFlagCache[glbPath] = (lastWrite, true);
				continue;
			}

			unoptimizedList.Add(glbPath);
		}
		return unoptimizedList;
	}

	private static bool TryReadGlbBytes(string glbPath, out DateTime lastWrite, out byte[] bytes)
	{
		bytes = null;
		lastWrite = default;
		try
		{
			lastWrite = File.GetLastWriteTimeUtc(glbPath);
			bytes = File.ReadAllBytes(glbPath);
			return bytes != null && bytes.Length > 0;
		}
		catch
		{
			return false;
		}
	}
	private static async Task NotifyModelProgressAsync(Func<int, int, string, Task>? onModelProgress, int index, int total, string fileName)
	{
		if (onModelProgress == null) return;
		try
		{
			await onModelProgress(index + 1, total, fileName);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Progress callback error: {ex.Message}");
		}
	}

	private static byte[] ExtractAndStripAnimations(string workspacePath, string glbPath, string fileName, byte[] glbBytes)
	{
		try
		{
			string animsDir = Path.Combine(workspacePath, "Assets", "animations");
			Directory.CreateDirectory(animsDir);
			var extractedAnims = MixamoAnimationImporter.ExtractAnimationsFromGlb(glbPath);
			foreach (var (animName, animData) in extractedAnims)
			{
				string animFileName = $"{animName.ToLowerInvariant()}.ranim";
				string animFilePath = Path.Combine(animsDir, animFileName);
				if (!File.Exists(animFilePath))
				{
					RealmAnimationSerializer.SaveToFile(animFilePath, animData);
					UpdateMetadataAnimationHash(workspacePath, animFileName, RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(animFilePath), ".ranim"));
				}
			}

			if (extractedAnims.Count > 0)
			{
				MixamoAnimationImporter.StripAnimationsFromGlb(glbPath, glbPath);
				return File.ReadAllBytes(glbPath);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Animation extraction error for {fileName}: {ex.Message}");
		}
		return glbBytes;
	}

	private static bool TryOptimizeAndSaveGlb(string workspacePath, string glbPath, string fileName, byte[] glbBytes, DateTime lastWrite, Realm.Shared.GlbOptimizer optimizer, Realm.Shared.OptimizationOptions options)
	{
		Realm.Shared.OptimizationResult optResult = default;
		try
		{
			optResult = optimizer.Optimize(glbBytes, options);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Exception optimizing {fileName}: {ex.Message}");
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			return false;
		}

		if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
		{
			return SaveOptimizedGlb(workspacePath, glbPath, fileName, optResult);
		}
		else
		{
			_optimizedFlagCache[glbPath] = (lastWrite, true);
			GD.PrintErr($"[MapWorkspaceService] Optimization failed for {fileName}: {optResult.ErrorMessage}");
			return false;
		}
	}

		private static bool SaveOptimizedGlb(string workspacePath, string glbPath, string fileName, Realm.Shared.OptimizationResult optResult)
	{
		try
		{
			var attrs = File.GetAttributes(glbPath);
			if ((attrs & FileAttributes.ReadOnly) != 0)
			{
				File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
			}
			File.Delete(glbPath);
			File.WriteAllBytes(glbPath, optResult.OutputGlbBytes);

			DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
			_optimizedFlagCache[glbPath] = (newLastWrite, true);

			string newHash = RealmMetadataHelper.ComputeBlake3(optResult.OutputGlbBytes, ".glb");
			UpdateMetadataGlbHash(workspacePath, fileName, newHash);

			GD.Print($"[MapWorkspaceService] Successfully optimized {fileName} ({optResult.OriginalSize} -> {optResult.OptimizedSize} bytes).");
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to write optimized GLB {glbPath}: {ex.Message}");
			return false;
		}
	}

	private static async Task<bool> TryOptimizeSingleGlbAsync(string workspacePath, string glbPath, string fileName, int index, int count, Realm.Shared.GlbOptimizer optimizer, Realm.Shared.OptimizationOptions options, Func<int, int, string, Task>? onModelProgress)
	{
		await NotifyModelProgressAsync(onModelProgress, index, count, fileName);

		if (!TryReadGlbBytes(glbPath, out var lastWrite, out var glbBytes))
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to read GLB {glbPath}");
			return false;
		}

		GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' ({index + 1}/{count}) is missing realm_optimize_completed extras. Optimizing into workspace...");
		
		glbBytes = ExtractAndStripAnimations(workspacePath, glbPath, fileName, glbBytes);

		return TryOptimizeAndSaveGlb(workspacePath, glbPath, fileName, glbBytes, lastWrite, optimizer, options);
	}

	public static async Task EnsureGlbAssetsOptimizedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onModelProgress = null)
	{
		NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath)) return;
		if (!Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			await ProcessGlbAssetsAsync(workspacePath, assetsDir, onModelProgress);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsureGlbAssetsOptimizedCooperativeAsync error: {ex.Message}");
		}
	}

	private static async Task ProcessGlbAssetsAsync(string workspacePath, string assetsDir, Func<int, int, string, Task>? onModelProgress)
	{
		string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
		if (glbFiles.Length == 0) return;

		var optimizer = new Realm.Shared.GlbOptimizer();
		var options = new Realm.Shared.OptimizationOptions
		{
			SimplificationRatio = 0.5f,
			AllowedPixelError = 1.5f,
			MaxTextureResolution = 1024,
			ForceReDecimate = false,
			CompressTextures = true,
			GenerateLods = true
		};

		var unoptimizedList = GetUnoptimizedGlbFiles(glbFiles, optimizer);
		if (unoptimizedList.Count == 0) return;

		bool anyReimported = false;
		for (int i = 0; i < unoptimizedList.Count; i++)
		{
			string glbPath = unoptimizedList[i];
			string fileName = Path.GetFileName(glbPath);

			if (await TryOptimizeSingleGlbAsync(workspacePath, glbPath, fileName, i, unoptimizedList.Count, optimizer, options, onModelProgress))
			{
				anyReimported = true;
			}
		}

		if (anyReimported)
		{
			ModelCache.Clear();
		}
	}

	private static (string AssetType, int Columns, int Rows) DetectPngAssetInfo(string workspacePath, string pngPath, MapMetadata? metadata = null)
	{
		if (TryDetectFromEmbeddedMetadata(pngPath, out var embeddedInfo))
			return embeddedInfo;

		string fileName = Path.GetFileName(pngPath);
		string cleanName = Path.GetFileNameWithoutExtension(pngPath);
		
		if (TryDetectFromMapAssets(workspacePath, fileName, cleanName, metadata, out var mapAssetInfo))
			return mapAssetInfo;

		return DetectFromPathHeuristics(pngPath);
	}

	private static int GetJsonIntOrDefault(JsonObject node, string key, int defaultValue)
	{
		if (node.TryGetPropertyValue(key, out var valNode) && valNode != null)
		{
			return valNode.GetValue<int>();
		}
		return defaultValue;
	}

	private static bool TryDetectFromEmbeddedMetadata(string pngPath, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		string? metaJson = RealmMetadataHelper.ExtractMetadata(pngPath);
		if (string.IsNullOrEmpty(metaJson)) return false;

		try
		{
			if (JsonNode.Parse(metaJson) is not JsonObject node) return false;
			
			if (!node.TryGetPropertyValue("type", out var typeNode)) return false;
			if (typeNode == null) return false;
			
			string? type = typeNode.GetValue<string>();
			if (string.IsNullOrEmpty(type)) return false;

			bool isDecal = type.Contains("decal", StringComparison.OrdinalIgnoreCase);
			int defaultGrid = 4;
			if (isDecal)
			{
				defaultGrid = 1;
			}
			
			int cols = GetJsonIntOrDefault(node, "columns", defaultGrid);
			int rows = GetJsonIntOrDefault(node, "rows", defaultGrid);
			
			info = (type, cols, rows);
			return true;
		}
		catch { return false; }
	}

	private static bool ContainsAssetKey(Dictionary<string, string>? dict, string fileName, string cleanName)
	{
		if (dict == null) return false;
		
		if (dict.ContainsKey(fileName) || dict.ContainsKey($"{cleanName}.rtex")) return true;
		
		return dict.Any(kvp => 
			kvp.Key.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) || 
			kvp.Key.EndsWith($"/{cleanName}.rtex", StringComparison.OrdinalIgnoreCase));
	}

	private static readonly (string Category, string Type)[] _assetCategoryTypes = new[]
	{
		("Icon", "icon"),
		("Skybox", "skybox"),
		("Terrain", "terrain_texture"),
		("Ribbon", "ribbon_texture"),
		("Noise", "noise_texture")
	};

	private static bool TryDetectFromMapAssets(string workspacePath, string fileName, string cleanName, MapMetadata? metadata, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		var assetsObj = MapAssetHelper.LoadAssets(workspacePath);
		if (assetsObj == null) return false;

		var loadedMetadata = metadata ?? MapFileService.LoadMetadata(workspacePath);
		if (loadedMetadata?.Decals != null && (loadedMetadata.Decals.ContainsKey(fileName) || loadedMetadata.Decals.ContainsKey($"{cleanName}.rtex")))
		{
			info = ("decal", 1, 1);
			return true;
		}

		foreach (var (category, type) in _assetCategoryTypes)
		{
			if (ContainsAssetKey(assetsObj.GetCategory(category), fileName, cleanName))
			{
				info = (type, 4, 4);
				return true;
			}
		}

		if (TryDetectFromVfxMetadata(loadedMetadata, fileName, cleanName, out info)) return true;

		return false;
	}

	private static bool TryDetectFromVfxMetadata(MapMetadata? metadata, string fileName, string cleanName, out (string AssetType, int Columns, int Rows) info)
	{
		info = default;
		if (metadata?.VfxSpritesheets == null) return false;

		VfxMetadata? vmeta = null;
		if (metadata.VfxSpritesheets.TryGetValue(fileName, out var v1)) vmeta = v1;
		else if (metadata.VfxSpritesheets.TryGetValue($"{cleanName}.rtex", out var v2)) vmeta = v2;

		if (vmeta == null) return false;

		int cols = vmeta.Columns > 0 ? vmeta.Columns : 4;
		int rows = vmeta.Rows > 0 ? vmeta.Rows : 4;
		info = ("vfx_spritesheet", cols, rows);
		return true;
	}

	private static (string AssetType, int Columns, int Rows) DetectFromPathHeuristics(string pngPath)
	{
		string normalized = pngPath.Replace("\\", "/").ToLowerInvariant();
		
		if (normalized.Contains("/assets/decals/")) return ("decal", 1, 1);
		if (normalized.Contains("/assets/icons/")) return ("icon", 4, 4);
		if (normalized.Contains("/assets/skyboxes/")) return ("skybox", 4, 4);
		if (normalized.Contains("/assets/vfx/")) return ("vfx_spritesheet", 4, 4);
		if (normalized.Contains("/assets/ribbons/")) return ("ribbon_texture", 4, 4);
		if (normalized.Contains("/assets/noise/")) return ("noise_texture", 4, 4);
		
		return ("terrain_texture", 4, 4);
	}

	public static void UpdateMetadataConvertedTexture(
		string workspacePath,
		string pngFileName,
		string rtexFileName,
		string assetType,
		string newHash,
		int columns = 4,
		int rows = 4)
	{
		try
		{
			string categoryKey = MapAssetHelper.NormalizeCategoryKey(assetType);
			MapAssetHelper.UpdateManifestAsset(workspacePath, categoryKey, rtexFileName, newHash);
			if (!string.Equals(pngFileName, rtexFileName, StringComparison.OrdinalIgnoreCase))
			{
				MapAssetHelper.RemoveManifestAsset(workspacePath, categoryKey, pngFileName);
			}

			if (string.Equals(categoryKey, "Spritesheet", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
				MetadataService.Instance.UpdateMetadata(workspacePath, m =>
				{
					m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
					m.VfxSpritesheets[spritesheetTemplateId] = new VfxMetadata
					{
						TexturePath = rtexFileName,
						Columns = columns,
						Rows = rows
					};
				});
			}
			else if (string.Equals(categoryKey, "Terrain", StringComparison.OrdinalIgnoreCase))
			{
				string slug = TemplateIDHelper.GenerateSlug(rtexFileName);
				string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
				MetadataService.Instance.UpdateMetadata(workspacePath, m =>
				{
					m.Textures ??= new(StringComparer.OrdinalIgnoreCase);
					if (!m.Textures.TryGetValue(terrainTemplateId, out var tex))
					{
						tex = new TextureMetadata();
						m.Textures[terrainTemplateId] = tex;
					}
					tex.TexturePath = rtexFileName;
					if (tex.ScaleFactor <= 0.0001f)
					{
						string fullRtexPath = Path.Combine(workspacePath, "Assets", "textures", rtexFileName);
						float sf = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(fullRtexPath);
						tex.ScaleFactor = sf <= 0.0001f ? 1.0f : sf;
					}
				});
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update metadata for converted texture {pngFileName}: {ex.Message}");
		}
	}

	public static void EnsurePngAssetsConverted(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			MetadataService.Instance.TryLoadMetadata(workspacePath, out var metadataRoot);

			foreach (string pngPath in pngFiles)
			{
				if (IsPathIgnored(pngPath)) continue;
				ProcessPngAsset(pngPath, workspacePath, metadataRoot);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsurePngAssetsConverted error: {ex.Message}");
		}
	}

	private static void ProcessPngAsset(string pngPath, string workspacePath, MapMetadata? metadataRoot, int? index = null, int? totalFiles = null)
	{
		string pngFileName = Path.GetFileName(pngPath);
		string rtexFileName = Path.ChangeExtension(pngFileName, ".rtex");
		string rtexPath = Path.ChangeExtension(pngPath, ".rtex");

		var (assetType, columns, rows) = DetectPngAssetInfo(workspacePath, pngPath, metadataRoot);
		
		string progressStr = index.HasValue && totalFiles.HasValue ? $" ({index.Value + 1}/{totalFiles.Value})" : "";
		GD.Print($"[MapWorkspaceService] Converting PNG asset '{pngFileName}'{progressStr} (type: {assetType}) to .rtex...");

		var convResult = Realm.Shared.Textures.TextureConverter.ConvertTextureFile(pngPath, rtexPath, assetType, columns, rows);
		
		if (convResult.Success && File.Exists(rtexPath))
		{
			FinalizePngConversion(pngPath, rtexPath, workspacePath, pngFileName, rtexFileName, assetType, columns, rows);
		}
		else
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to convert PNG '{pngFileName}': {convResult.ErrorMessage}");
		}
	}

	private static void FinalizePngConversion(string pngPath, string rtexPath, string workspacePath, string pngFileName, string rtexFileName, string assetType, int columns, int rows)
	{
		byte[] rtexBytes = File.ReadAllBytes(rtexPath);
		string newHash = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

		UpdateMetadataConvertedTexture(workspacePath, pngFileName, rtexFileName, assetType, newHash, columns, rows);

		try
		{
			var attrs = File.GetAttributes(pngPath);
			if ((attrs & FileAttributes.ReadOnly) != 0)
				File.SetAttributes(pngPath, attrs & ~FileAttributes.ReadOnly);
				
			File.Delete(pngPath);
			GD.Print($"[MapWorkspaceService] Successfully converted '{pngFileName}' to '{rtexFileName}'.");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to delete PNG {pngPath}: {ex.Message}");
		}
	}

	public static async Task EnsurePngAssetsConvertedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onTextureProgress = null)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			var validPngs = pngFiles.Where(f => !IsPathIgnored(f)).ToList();
			if (validPngs.Count == 0) return;

			MetadataService.Instance.TryLoadMetadata(workspacePath, out var metadataRoot);

			for (int i = 0; i < validPngs.Count; i++)
			{
				string pngPath = validPngs[i];
				
				if (onTextureProgress != null)
				{
					try
					{
						await onTextureProgress(i + 1, validPngs.Count, Path.GetFileName(pngPath));
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Progress callback error: {ex.Message}");
					}
				}

				ProcessPngAsset(pngPath, workspacePath, metadataRoot, i, validPngs.Count);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsurePngAssetsConvertedCooperativeAsync error: {ex.Message}");
		}
	}

	public static float ExtractRtexScaleFactor(string rtexPath)
	{
		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;
		
		try
		{
			string? rtexMeta = RealmMetadataHelper.ExtractMetadata(rtexPath);
			if (string.IsNullOrEmpty(rtexMeta)) return 1.0f;
			
			var rNode = JsonNode.Parse(rtexMeta);
			if (rNode is not JsonObject rObj) return 1.0f;

			var keys = new[] { "Scale_Factor", "scale_factor", "scaleFactor", "ScaleFactor" };
			foreach (var key in keys)
			{
				if (rObj.TryGetPropertyValue(key, out var sfVal) && 
				    float.TryParse(sfVal?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedScale))
				{
					return Math.Clamp(parsedScale, 0.10f, 4.0f);
				}
			}
		}
		catch { }
		return 1.0f;
	}

	public static bool NormalizeTextureEntries(JsonObject root, string? wsPath = null)
	{
		if (root == null || !(root["textures"] is JsonObject texturesObj)) return false;
		
		bool modified = false;
		modified |= ConvertSimpleHashesToObjects(texturesObj);
		modified |= EnsureUniqueSwatchIndices(texturesObj);
		modified |= EnsureScaleFactors(texturesObj, wsPath);
		
		return modified;
	}

	private static bool ConvertSimpleHashesToObjects(JsonObject texturesObj)
	{
		bool modified = false;
		var entriesToConvert = new List<(string Key, string Hash)>();
		
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string hashStr))
				entriesToConvert.Add((kvp.Key, hashStr));
		}

		foreach (var (k, h) in entriesToConvert)
		{
			texturesObj[k] = new JsonObject { ["hash"] = h };
			modified = true;
		}
		return modified;
	}

	private static void GatherUsedSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is JsonObject texObj && 
			    texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) && 
			    sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0)
			{
				usedIndices.Add(parsedIdx);
			}
		}
	}

	private static bool AssignMissingSwatchIndices(JsonObject texturesObj, HashSet<int> usedIndices)
	{
		bool modified = false;
		int nextAvailable = 0;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidIdx = texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) && 
			                   sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0;
			
			if (hasValidIdx) continue;

			while (usedIndices.Contains(nextAvailable))
				nextAvailable++;
				
			texObj["swatchIndex"] = nextAvailable;
			usedIndices.Add(nextAvailable);
			modified = true;
		}
		return modified;
	}

	private static bool EnsureUniqueSwatchIndices(JsonObject texturesObj)
	{
		var usedIndices = new HashSet<int>();
		GatherUsedSwatchIndices(texturesObj, usedIndices);
		return AssignMissingSwatchIndices(texturesObj, usedIndices);
	}

	private static bool EnsureScaleFactors(JsonObject texturesObj, string? wsPath)
	{
		bool modified = false;
		foreach (var kvp in texturesObj)
		{
			if (kvp.Value is not JsonObject texObj) continue;

			bool hasValidSf = texObj.TryGetPropertyValue("Scale_Factor", out var sfNode) && 
			                  sfNode != null && float.TryParse(sfNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedSf) && parsedSf > 0.0001f;
			
			if (!hasValidSf)
			{
				float sf = CalculateTextureScaleFactor(kvp.Key, wsPath);
				texObj["Scale_Factor"] = sf;
				modified = true;
			}
		}
		return modified;
	}

	private static float CalculateTextureScaleFactor(string fileName, string? wsPath)
	{
		string rtexFileName = fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".rtex";
		string? rtexPath = FindRtexPath(rtexFileName, wsPath);

		if (string.IsNullOrEmpty(rtexPath) || !File.Exists(rtexPath)) return 1.0f;

		float rtexSf = ExtractRtexScaleFactor(rtexPath);
		if (rtexSf > 0.0001f && MathF.Abs(rtexSf - 1.0f) > 0.001f)
			return rtexSf;
			
		float calcSf = Realm.Shared.Textures.TextureConverter.CalculateLuminanceScaleFactor(rtexPath);
		return calcSf > 0.0001f ? calcSf : 1.0f;
	}

	private static string? FindRtexPath(string rtexFileName, string? wsPath)
	{
		if (!string.IsNullOrEmpty(wsPath))
		{
			string p1 = Path.Combine(wsPath, "Assets", "textures", rtexFileName);
			if (File.Exists(p1)) return p1;
			
			string p2 = Path.Combine(wsPath, rtexFileName);
			if (File.Exists(p2)) return p2;
		}

		string? found = PathUtils.FindPath($"Assets/textures/{rtexFileName}");
		if (!string.IsNullOrEmpty(found)) return found;

		return PathUtils.FindPath($"MapTemplate/Assets/textures/{rtexFileName}");
	}

	public static void NormalizeTextureEntries(MapManifestAssets assets, string workspacePath)
	{
		if (assets == null || string.IsNullOrEmpty(workspacePath)) return;
		MapAssetHelper.SaveAssetsToManifest(workspacePath, assets, removeFromMetadata: true);
	}

	public static void NormalizeMetadataTextureEntries(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		try
		{
			var assets = MapAssetHelper.LoadAssets(workspacePath);
			NormalizeTextureEntries(assets, workspacePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] NormalizeMetadataTextureEntries error: {ex.Message}");
		}
	}

	public static void UpdateMetadataGlbHash(string workspacePath, string fileName, string newHash)
	{
		try
		{
			string category = MapAssetHelper.NormalizeCategoryKey("Prop");
			MapAssetHelper.UpdateManifestAsset(workspacePath, category, fileName, newHash);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update manifest.json for {fileName}: {ex.Message}");
		}
	}

	public static void UpdateMetadataAnimationHash(string workspacePath, string animFileName, string newHash)
	{
		try
		{
			Realm.Client.Utils.MapAssetHelper.UpdateManifestAsset(workspacePath, "animations", animFileName, newHash);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update manifest.json for {animFileName}: {ex.Message}");
		}
	}
}