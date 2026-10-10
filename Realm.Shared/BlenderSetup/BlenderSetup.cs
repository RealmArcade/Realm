using System.Diagnostics;
using System.Reflection;

namespace Realm.Shared.BlenderSetup;

public static class BlenderSetup
{
	private static readonly string AppDataRoot = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"Realm.Tools.Cli");

	public static readonly string NodeDir = Path.Combine(AppDataRoot, "BlenderEnv");
	public static readonly string VenvDir = Path.Combine(NodeDir, "venv311");

	public static string RenderScriptPath => Path.Combine(NodeDir, "render_ranim.py");

	public static string PythonExePath
	{
		get
		{
			bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
			return Path.Combine(VenvDir,
				isWindows
					? Path.Combine("Scripts", "python.exe")
					: Path.Combine("bin", "python"));
		}
	}

	private static string MarkerPath => Path.Combine(VenvDir, "install_completed.marker");

	public static bool IsSetupComplete() =>
		File.Exists(MarkerPath) &&
		File.Exists(PythonExePath) &&
		File.Exists(RenderScriptPath);

	public static void EnsureSetup(Action<string>? logCallback = null)
	{
		void Log(string message)
		{
			logCallback?.Invoke(message);
			Console.WriteLine(message);
		}

		Directory.CreateDirectory(NodeDir);
		ExtractRenderScript(Log);

		if (IsSetupComplete())
		{
			return;
		}

		Log("[Blender] Initializing Blender Python environment...");
		CheckToolAvailable("uv", "--version", "uv is required to create the Python environment. Install it from https://github.com/astral-sh/uv");

		var createProcessStartInfo = new ProcessStartInfo
		{
			FileName = "uv",
			WorkingDirectory = NodeDir,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		createProcessStartInfo.ArgumentList.Add("venv");
		createProcessStartInfo.ArgumentList.Add("--clear");
		createProcessStartInfo.ArgumentList.Add("--python");
		createProcessStartInfo.ArgumentList.Add("3.11");
		createProcessStartInfo.ArgumentList.Add(VenvDir);

		RunProcess(createProcessStartInfo, "[Blender]", throwOnNonZero: true, Log);

		Log("[Blender] Installing bpy and Pillow...");

		var installProcessStartInfo = new ProcessStartInfo
		{
			FileName = "uv",
			WorkingDirectory = NodeDir,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		installProcessStartInfo.ArgumentList.Add("pip");
		installProcessStartInfo.ArgumentList.Add("install");
		installProcessStartInfo.ArgumentList.Add("--python");
		installProcessStartInfo.ArgumentList.Add(PythonExePath);
		installProcessStartInfo.ArgumentList.Add("bpy");
		installProcessStartInfo.ArgumentList.Add("Pillow");

		RunProcess(installProcessStartInfo, "[Blender]", throwOnNonZero: true, Log);

		File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"));
		Log("[Blender] Python environment ready.");
	}

	private static void ExtractRenderScript(Action<string>? log = null)
	{
		var assembly = Assembly.GetExecutingAssembly();
		var stream = assembly.GetManifestResourceNames()
			.Where(name => name.EndsWith("render_ranim.py", StringComparison.OrdinalIgnoreCase))
			.Select(name => assembly.GetManifestResourceStream(name))
			.FirstOrDefault(s => s != null);

		if (stream == null)
		{
			if (File.Exists(RenderScriptPath))
			{
				return;
			}
			throw new FileNotFoundException(
				$"Embedded resource render_ranim.py not found. Available resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
		}

		try
		{
			using (stream)
			using (var fileStream = File.Create(RenderScriptPath))
			{
				stream.CopyTo(fileStream);
			}
		}
		catch (IOException) when (File.Exists(RenderScriptPath))
		{
		}
	}

	private static void CheckToolAvailable(string tool, string testArgument, string errorMessage)
	{
		try
		{
			var processStartInfo = new ProcessStartInfo
			{
				FileName = tool,
				Arguments = testArgument,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};
			using var process = Process.Start(processStartInfo);
			process?.WaitForExit(5000);
		}
		catch
		{
			throw new InvalidOperationException(errorMessage);
		}
	}

	private static void RunProcess(ProcessStartInfo processStartInfo, string logPrefix, bool throwOnNonZero, Action<string>? log = null)
	{
		using var process = new Process { StartInfo = processStartInfo };
		process.OutputDataReceived += (_, eventArgs) =>
		{
			if (eventArgs.Data != null)
			{
				log?.Invoke($"{logPrefix} {eventArgs.Data}");
				Console.WriteLine($"{logPrefix} {eventArgs.Data}");
			}
		};
		process.ErrorDataReceived += (_, eventArgs) =>
		{
			if (eventArgs.Data != null)
			{
				log?.Invoke($"{logPrefix} {eventArgs.Data}");
				Console.Error.WriteLine($"{logPrefix} {eventArgs.Data}");
			}
		};

		if (!process.Start())
		{
			throw new InvalidOperationException($"Failed to start process: {processStartInfo.FileName}");
		}

		if (processStartInfo.RedirectStandardInput)
		{
			process.StandardInput.Close();
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		if (throwOnNonZero && process.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"Process exited with code {process.ExitCode}: {processStartInfo.FileName}");
		}
	}
}
