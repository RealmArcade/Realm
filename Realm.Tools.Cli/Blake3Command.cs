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

internal static class Blake3Command
{
	internal static int Execute(Blake3Options options)
	{
		if (!CliFileSet.EnsurePathExists(options.Input)) return 1;

		var files = CliFileSet.TraverseFiles(options.Input, options.Recursive).ToArray();
		if (files.Length == 0)
		{
			Console.WriteLine($"No files found in: {options.Input}");
			return 0;
		}

		foreach (var file in files)
		{
			byte[] bytes = File.ReadAllBytes(file);
			string hash = options.Raw
				? Hasher.Hash(bytes).ToString()
				: RealmMetadataHelper.ComputeBlake3(bytes, file);
			Console.WriteLine($"{hash}  {file}");
		}

		return 0;
	}
}
