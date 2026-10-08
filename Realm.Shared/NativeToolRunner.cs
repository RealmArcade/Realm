using System;
using System.Diagnostics;
using System.IO;

namespace Realm.Shared;

public static class NativeToolRunner
{
	private static string? _cachedGltfPackPath;

	private static string? SearchCandidatePaths(string exeName)
	{
		string baseDir = AppContext.BaseDirectory;
		string cwd = Directory.GetCurrentDirectory();

		string[] candidatePaths = new string[]
		{
			Path.Combine(baseDir, exeName),
			Path.Combine(baseDir, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, exeName),
			Path.Combine(cwd, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "..", "ThirdPartyBinaries", exeName)
		};

		foreach (var path in candidatePaths)
		{
			if (File.Exists(path))
			{
				return Path.GetFullPath(path);
			}
		}

		return null;
	}

	private static string? SearchPathEnvironment(string exeName, bool isWindows)
	{
		var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
		var pathSeparator = isWindows ? ';' : ':';
		
		foreach (var dir in pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(dir.Trim(), exeName);
			if (File.Exists(candidate))
			{
				return Path.GetFullPath(candidate);
			}
		}

		return null;
	}

	private static string? ExtractGltfPackToTemp(string exeName)
	{
		try
		{
			string tempDir = Path.Combine(Path.GetTempPath(), "realm_tools_bin");
			string tempExe = Path.Combine(tempDir, exeName);
			
			if (File.Exists(tempExe) && new FileInfo(tempExe).Length > 0)
			{
				return tempExe;
			}

			Stream? stream = GetGltfPackResourceStream();
			if (stream == null) return null;

			Directory.CreateDirectory(tempDir);
			using var fileStream = File.Create(tempExe);
			stream.CopyTo(fileStream);
			
			return tempExe;
		}
		catch
		{
			return null;
		}
	}

	public static string? FindGltfPackPath()
	{
		if (!string.IsNullOrEmpty(_cachedGltfPackPath) && File.Exists(_cachedGltfPackPath))
		{
			return _cachedGltfPackPath;
		}

		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
		string exeName = isWindows ? "gltfpack.exe" : "gltfpack";

		_cachedGltfPackPath = SearchCandidatePaths(exeName) 
			?? SearchPathEnvironment(exeName, isWindows) 
			?? ExtractGltfPackToTemp(exeName);

		return _cachedGltfPackPath;
	}

	private static Stream? GetGltfPackResourceStream()
	{
		var asm = typeof(NativeToolRunner).Assembly;
		Stream? stream = asm.GetManifestResourceStream("Realm.Shared.ThirdPartyBinaries.gltfpack.exe");
		if (stream != null) return stream;

		foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
		{
			stream = GetGltfPackStreamFromAssembly(a);
			if (stream != null) return stream;
		}
		return null;
	}

	private static Stream? GetGltfPackStreamFromAssembly(System.Reflection.Assembly assembly)
	{
		Stream? stream = assembly.GetManifestResourceStream("Realm.Tools.Cli.ThirdPartyBinaries.gltfpack.exe");
		if (stream != null) return stream;

		var names = assembly.GetManifestResourceNames();
		foreach (var name in names)
		{
			if (name.EndsWith("gltfpack.exe", StringComparison.OrdinalIgnoreCase))
			{
				return assembly.GetManifestResourceStream(name);
			}
		}
		return null;
	}

	private static string? _cachedFfmpegPath;

	public static string? FindFfmpegPath()
	{
		if (!string.IsNullOrEmpty(_cachedFfmpegPath) && File.Exists(_cachedFfmpegPath))
		{
			return _cachedFfmpegPath;
		}

		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
		string exeName = isWindows ? "ffmpeg.exe" : "ffmpeg";

		string baseDir = AppContext.BaseDirectory;
		string cwd = Directory.GetCurrentDirectory();

		string[] candidatePaths = new string[]
		{
			Path.Combine(baseDir, exeName),
			Path.Combine(baseDir, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, exeName),
			Path.Combine(cwd, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "..", "ThirdPartyBinaries", exeName)
		};

		foreach (var path in candidatePaths)
		{
			if (File.Exists(path))
			{
				_cachedFfmpegPath = Path.GetFullPath(path);
				return _cachedFfmpegPath;
			}
		}

		var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
		var pathSeparator = isWindows ? ';' : ':';
		foreach (var dir in pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(dir.Trim(), exeName);
			if (File.Exists(candidate))
			{
				_cachedFfmpegPath = Path.GetFullPath(candidate);
				return _cachedFfmpegPath;
			}
		}

		return null;
	}

	private static bool ExtractEmbeddedResource(string resourceSuffix, string destinationPath)
	{
		if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
		{
			return true;
		}

		Stream? stream = null;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			var names = asm.GetManifestResourceNames();
			foreach (var name in names)
			{
				if (name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
				{
					stream = asm.GetManifestResourceStream(name);
					break;
				}
			}
			if (stream != null) break;
		}

		if (stream != null)
		{
			string? dir = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			using var fileStream = File.Create(destinationPath);
			stream.CopyTo(fileStream);
			return true;
		}

		return false;
	}

	public static (int ExitCode, string Stdout, string Stderr) RunTool(string toolFileName, string arguments, int timeoutMs = 60000, string? workingDir = null)
	{
		string effectiveWorkingDir = !string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)
			? workingDir
			: Directory.GetCurrentDirectory();
		var psi = new ProcessStartInfo
		{
			FileName = toolFileName,
			Arguments = arguments,
			WorkingDirectory = effectiveWorkingDir,
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using var proc = Process.Start(psi);
		if (proc == null)
		{
			return (-1, string.Empty, $"Failed to start process: {toolFileName}");
		}

		string stdout = proc.StandardOutput.ReadToEnd();
		string stderr = proc.StandardError.ReadToEnd();
		proc.WaitForExit(timeoutMs);

		return (proc.ExitCode, stdout, stderr);
	}

	private static bool HasBasisuExtension(System.Text.Json.Nodes.JsonObject root, string propertyName)
	{
		if (!root.TryGetPropertyValue(propertyName, out var extProp) || extProp is not System.Text.Json.Nodes.JsonArray array)
		{
			return false;
		}

		foreach (var node in array)
		{
			if (node?.GetValue<string>() == "KHR_texture_basisu")
			{
				return true;
			}
		}

		return false;
	}

	private static void DeleteDirectorySafely(string dir)
	{
		try
		{
			if (Directory.Exists(dir))
			{
				Directory.Delete(dir, true);
			}
		}
		catch
		{
		}
	}

	private static (bool Success, byte[] OutputBytes, string ErrorMessage) ExecuteKtxDecompress(string tempDir, string tempIn, string tempOut, byte[] glbBytes)
	{
		try
		{
			File.WriteAllBytes(tempIn, glbBytes);
			bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
			string exeName = isWindows ? "cmd.exe" : "npx";
			string args = isWindows
				? $"/c npx --yes @gltf-transform/cli ktxdecompress \"{tempIn}\" \"{tempOut}\""
				: $"--yes @gltf-transform/cli ktxdecompress \"{tempIn}\" \"{tempOut}\"";

			var runResult = RunTool(exeName, args, timeoutMs: 60000, workingDir: tempDir);
			if (runResult.ExitCode == 0 && File.Exists(tempOut))
			{
				byte[] decompressed = File.ReadAllBytes(tempOut);
				return (true, decompressed, string.Empty);
			}

			return (false, glbBytes, $"ktxdecompress failed (exit code {runResult.ExitCode}): {runResult.Stderr}\n{runResult.Stdout}");
		}
		catch (Exception ex)
		{
			return (false, glbBytes, $"Exception during ktxdecompress: {ex.Message}");
		}
		finally
		{
			DeleteDirectorySafely(tempDir);
		}
	}

	public static (bool Success, byte[] OutputBytes, string ErrorMessage) DecompressKhrTextureBasisuIfNeeded(byte[] glbBytes)
	{
		if (glbBytes == null || glbBytes.Length == 0) return (true, glbBytes ?? Array.Empty<byte>(), string.Empty);

		var (json, _, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (json is not System.Text.Json.Nodes.JsonObject root) return (true, glbBytes, string.Empty);

		bool hasBasisu = HasBasisuExtension(root, "extensionsUsed") || HasBasisuExtension(root, "extensionsRequired");

		if (!hasBasisu) return (true, glbBytes, string.Empty);

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_decomp_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempIn = Path.Combine(tempDir, "input.glb");
		string tempOut = Path.Combine(tempDir, "output.glb");

		return ExecuteKtxDecompress(tempDir, tempIn, tempOut, glbBytes);
	}

	private static string BuildGltfPackArgs(string tempInput, string tempOutput, float simplificationRatio, int maxTextureResolution, bool compressTextures)
	{
		string textureArgs = compressTextures
			? $"-tw -tl {Math.Max(128, maxTextureResolution)}"
			: string.Empty;

		string ratioArg = simplificationRatio is > 0f and < 1.0f
			? $"-si {simplificationRatio:F2}"
			: string.Empty;

		return $"-i \"{tempInput}\" -o \"{tempOutput}\" {textureArgs} {ratioArg} -kn -km -ke -noq";
	}

	private static (bool Success, byte[]? OutputBytes, string ErrorMessage) ExecuteGltfPackProcess(string tool, string args, string tempOutput)
	{
		var psi = new ProcessStartInfo
		{
			FileName = tool,
			Arguments = args,
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using var proc = Process.Start(psi);
		if (proc == null)
		{
			return (false, null, "Failed to start gltfpack process.");
		}

		string stdout = proc.StandardOutput.ReadToEnd();
		string stderr = proc.StandardError.ReadToEnd();
		proc.WaitForExit(60000);

		if (proc.ExitCode == 0 && File.Exists(tempOutput))
		{
			byte[] output = File.ReadAllBytes(tempOutput);
			return (true, output, string.Empty);
		}

		return (false, null, $"gltfpack exited with code {proc.ExitCode}: {stderr}\n{stdout}");
	}

	public static (bool Success, byte[]? OutputBytes, string ErrorMessage) RunGltfPack(
		byte[] inputBytes,
		float simplificationRatio = 0.5f,
		int maxTextureResolution = 1024,
		bool compressTextures = true)
	{
		string? tool = FindGltfPackPath();
		if (string.IsNullOrEmpty(tool))
		{
			return (false, null, "gltfpack binary not found on system or candidate paths.");
		}

		byte[] effectiveInput = inputBytes;
		var (decompOk, decompBytes, _) = DecompressKhrTextureBasisuIfNeeded(inputBytes);
		if (decompOk && decompBytes != null && decompBytes.Length > 0)
		{
			effectiveInput = decompBytes;
		}

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_pipeline_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempInput = Path.Combine(tempDir, "input.glb");
		string tempOutput = Path.Combine(tempDir, "output.glb");

		try
		{
			File.WriteAllBytes(tempInput, effectiveInput);
			string args = BuildGltfPackArgs(tempInput, tempOutput, simplificationRatio, maxTextureResolution, compressTextures);
			return ExecuteGltfPackProcess(tool, args, tempOutput);
		}
		catch (Exception ex)
		{
			return (false, null, $"Exception during gltfpack execution: {ex.Message}");
		}
		finally
		{
			DeleteDirectorySafely(tempDir);
		}
	}
}
