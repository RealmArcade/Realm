using Realm.Shared.ModelOptimization;

namespace Realm.Tools.Cli;

public static class RmeshExtractSourceImageCommand
{
	private const string SourceImageExtension = ".source.webp";

	public static int Execute(RmeshExtractSourceImageOptions options)
	{
		try
		{
			return CliFileSet.ProcessTraversedFiles(
				options.Input,
				options.Output,
				options.Recursive,
				false,
				IsRmeshFile,
				_ => SourceImageExtension,
				ProcessSingleExtractSourceImage,
				summaryActionName: "source reference image extraction");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
	}

	private static bool IsRmeshFile(string file)
	{
		return Path.GetExtension(file).Equals(".rmesh", StringComparison.OrdinalIgnoreCase);
	}

	private static int ProcessSingleExtractSourceImage(string inputFile, string targetFile)
	{
		CliAssetAgreement.EnsureAccepted();

		var result = ModelConverter.ExtractSourceProfileImageFromRmesh(inputFile, targetFile);
		if (!result.Success)
		{
			Console.Error.WriteLine($"Failed to extract source reference image from {inputFile}: {result.ErrorMessage}");
			return 1;
		}

		Console.WriteLine($"Successfully extracted source reference image: {result.InputPath} -> {result.OutputPath} ({result.OptimizedSize} bytes)");
		return 0;
	}
}
