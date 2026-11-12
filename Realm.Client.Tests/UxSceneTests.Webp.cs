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

/// <summary>saving the rendered image as webp through cwebp.</summary>
public partial class UxSceneTests
	{
	private static Error SaveAsWebp(Image image, string outputPath, string cwebpPath)
	{
		if (!string.IsNullOrEmpty(cwebpPath))
		{
			string tempPngPath = outputPath + ".source.png";
			Error pngError = image.SavePng(tempPngPath);
			if (pngError != Error.Ok)
			{
				return pngError;
			}

			var startInfo = new ProcessStartInfo(cwebpPath);
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.RedirectStandardOutput = true;
			startInfo.RedirectStandardError = true;
			startInfo.ArgumentList.Add("-quiet");
			startInfo.ArgumentList.Add("-q");
			startInfo.ArgumentList.Add("90");
			startInfo.ArgumentList.Add(tempPngPath);
			startInfo.ArgumentList.Add("-o");
			startInfo.ArgumentList.Add(outputPath);

			try
			{
				using Process? process = Process.Start(startInfo);
				if (process == null)
				{
					return Error.CantCreate;
				}

				process.WaitForExit();
				return process.ExitCode == 0 ? Error.Ok : Error.Failed;
			}
			finally
			{
				if (File.Exists(tempPngPath))
				{
					File.Delete(tempPngPath);
				}
			}
		}

		return image.SaveWebp(outputPath, true, 0.9f);
	}

	private static string FindCwebpExecutable()
	{
		string? configuredPath = Environment.GetEnvironmentVariable("REALM_CWEBP_PATH");
		if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
		{
			return configuredPath;
		}

		string? pathVariable = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(pathVariable))
		{
			return string.Empty;
		}

		foreach (string directory in pathVariable.Split(Path.PathSeparator))
		{
			string trimmedDirectory = directory.Trim().Trim('"');
			if (trimmedDirectory.Length == 0)
			{
				continue;
			}

			foreach (string executableName in new[] { "cwebp.exe", "cwebp" })
			{
				string candidatePath = Path.Combine(trimmedDirectory, executableName);
				if (File.Exists(candidatePath))
				{
					return candidatePath;
				}
			}
		}

		return string.Empty;
	}
}
