using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Realm.Shared.BlenderSetup;

public enum MeshRenderMode
{
	Sheet,
	Views
}

public enum MeshRenderEngine
{
	Auto,
	Eevee,
	Workbench
}

public class MeshTurnaroundOptions
{
	public static readonly string[] DefaultViews = { "front", "three_quarter", "side", "back" };

	public MeshRenderMode Mode { get; init; } = MeshRenderMode.Sheet;

	public int Size { get; init; } = 512;

	public string[] Views { get; init; } = DefaultViews;

	public string Background { get; init; } = "#14171F";

	public MeshRenderEngine Engine { get; init; } = MeshRenderEngine.Auto;

	public int Samples { get; init; } = 32;

	public float ElevationDegrees { get; init; } = 18f;

	public bool DrawLabels { get; init; } = true;
}

public class MeshRenderResult
{
	public bool Success { get; set; }

	public string ErrorMessage { get; set; } = string.Empty;

	public IReadOnlyList<string> OutputPaths { get; set; } = Array.Empty<string>();
}

public static class BlenderMeshRenderer
{
	public static MeshRenderResult Render(
		string glbPath,
		string outputPath,
		string? fileNamePrefix,
		MeshTurnaroundOptions options,
		Action<string>? logCallback = null)
	{
		var result = new MeshRenderResult();

		try
		{
			BlenderSetup.EnsureSetup(logCallback);

			string resolvedPrefix = string.IsNullOrEmpty(fileNamePrefix)
				? Path.GetFileNameWithoutExtension(glbPath)
				: fileNamePrefix;

			var processStartInfo = BuildProcessStartInfo(glbPath, outputPath, resolvedPrefix, options);
			return RunRenderProcess(processStartInfo, outputPath, resolvedPrefix, options, result, logCallback);
		}
		catch (Exception exception)
		{
			result.Success = false;
			result.ErrorMessage = exception.Message;
			return result;
		}
	}

	public static IReadOnlyList<string> ResolveExpectedOutputPaths(string outputPath, string fileNamePrefix, MeshTurnaroundOptions options)
	{
		if (options.Mode == MeshRenderMode.Sheet)
		{
			return new[] { outputPath };
		}

		return options.Views
			.Select(view => Path.Combine(outputPath, $"{fileNamePrefix}_{view}.png"))
			.ToArray();
	}

	private static ProcessStartInfo BuildProcessStartInfo(string glbPath, string outputPath, string fileNamePrefix, MeshTurnaroundOptions options)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = BlenderSetup.PythonExePath,
			WorkingDirectory = BlenderSetup.NodeDir,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		startInfo.ArgumentList.Add(BlenderSetup.MeshRenderScriptPath);
		startInfo.ArgumentList.Add("--model");
		startInfo.ArgumentList.Add(Path.GetFullPath(glbPath));
		startInfo.ArgumentList.Add("--output");
		startInfo.ArgumentList.Add(Path.GetFullPath(outputPath));
		startInfo.ArgumentList.Add("--mode");
		startInfo.ArgumentList.Add(options.Mode == MeshRenderMode.Sheet ? "sheet" : "views");
		startInfo.ArgumentList.Add("--size");
		startInfo.ArgumentList.Add(options.Size.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("--views");
		startInfo.ArgumentList.Add(string.Join(",", options.Views));
		startInfo.ArgumentList.Add("--background");
		startInfo.ArgumentList.Add(options.Background);
		startInfo.ArgumentList.Add("--engine");
		startInfo.ArgumentList.Add(FormatEngine(options.Engine));
		startInfo.ArgumentList.Add("--samples");
		startInfo.ArgumentList.Add(options.Samples.ToString(CultureInfo.InvariantCulture));
		startInfo.ArgumentList.Add("--elevation");
		startInfo.ArgumentList.Add(options.ElevationDegrees.ToString(CultureInfo.InvariantCulture));

		startInfo.ArgumentList.Add("--name");
		startInfo.ArgumentList.Add(fileNamePrefix);

		if (!options.DrawLabels)
		{
			startInfo.ArgumentList.Add("--no-labels");
		}

		return startInfo;
	}

	private static string FormatEngine(MeshRenderEngine engine)
	{
		return engine switch
		{
			MeshRenderEngine.Eevee => "eevee",
			MeshRenderEngine.Workbench => "workbench",
			_ => "auto"
		};
	}

	private static MeshRenderResult RunRenderProcess(
		ProcessStartInfo startInfo,
		string outputPath,
		string fileNamePrefix,
		MeshTurnaroundOptions options,
		MeshRenderResult result,
		Action<string>? logCallback)
	{
		var standardOutput = new StringBuilder();
		var standardError = new StringBuilder();

		using var process = new Process { StartInfo = startInfo };
		process.OutputDataReceived += (_, eventArgs) => HandleProcessData(eventArgs, standardOutput, logCallback);
		process.ErrorDataReceived += (_, eventArgs) => HandleProcessData(eventArgs, standardError, logCallback);

		if (!process.Start())
		{
			return FailResult(result, "Failed to launch the Blender Python rendering process.");
		}

		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		process.WaitForExit();

		var expectedOutputs = ResolveExpectedOutputPaths(outputPath, fileNamePrefix, options);
		bool everyOutputExists = expectedOutputs.All(File.Exists);

		if (process.ExitCode != 0 || !everyOutputExists)
		{
			string errorMessage = standardError.Length > 0
				? standardError.ToString()
				: $"Process exited with code {process.ExitCode}";

			if (process.ExitCode == 0 && !everyOutputExists)
			{
				errorMessage = $"Blender reported success but did not write every expected image. Missing: {string.Join(", ", expectedOutputs.Where(path => !File.Exists(path)))}";
			}

			return FailResult(result, errorMessage);
		}

		result.Success = true;
		result.OutputPaths = expectedOutputs;
		return result;
	}

	private static MeshRenderResult FailResult(MeshRenderResult result, string message)
	{
		result.Success = false;
		result.ErrorMessage = message;
		return result;
	}

	private static void HandleProcessData(DataReceivedEventArgs eventArgs, StringBuilder buffer, Action<string>? logCallback)
	{
		if (eventArgs.Data == null) return;
		buffer.AppendLine(eventArgs.Data);
		logCallback?.Invoke(eventArgs.Data);
	}
}
