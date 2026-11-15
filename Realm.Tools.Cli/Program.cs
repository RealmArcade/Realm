using CommandLine;
using Realm.Shared;

namespace Realm.Tools.Cli;

public static class Program
{
	private static readonly (Type OptionsType, Func<object, int> Execute)[] Verbs =
	{
		(typeof(MeshConvertOptions), options => MeshConvertCommands.Execute((MeshConvertOptions)options)),
		(typeof(MeshRenderOptions), options => MeshRenderCommand.Execute((MeshRenderOptions)options)),
		(typeof(ImageSheetOptions), options => ImageSheetCommand.Execute((ImageSheetOptions)options)),
		(typeof(RmeshExtractSourceImageOptions), options => RmeshExtractSourceImageCommand.Execute((RmeshExtractSourceImageOptions)options)),
		(typeof(TextureConvertOptions), options => TextureConvertCommand.Execute((TextureConvertOptions)options)),
		(typeof(AudioConvertOptions), options => AudioConvertCommand.Execute((AudioConvertOptions)options)),
		(typeof(FbxToRanimOptions), options => RanimCommands.ExecuteFromFbx((FbxToRanimOptions)options)),
		(typeof(RanimRenderOptions), options => RanimCommands.ExecuteRender((RanimRenderOptions)options)),
		(typeof(MetadataOptions), options => MetadataCommands.Execute((MetadataOptions)options)),
		(typeof(Blake3Options), options => Blake3Command.Execute((Blake3Options)options)),
		(typeof(MeshPlayerColorCliOptions), options => MeshPlayerColorCommand.Execute((MeshPlayerColorCliOptions)options)),
		(typeof(RigHumanoidOptions), options => RigCommand.Execute((RigHumanoidOptions)options)),
		(typeof(KeygenOptions), options => KeygenCommand.Execute((KeygenOptions)options)),
		(typeof(GenerateManifestSchemaOptions), options => SchemaCommands.ExecuteManifest((GenerateManifestSchemaOptions)options)),
		(typeof(GenerateMetadataSchemaOptions), options => SchemaCommands.ExecuteMetadata((GenerateMetadataSchemaOptions)options)),
		(typeof(GenerateTerrainSchemaOptions), options => SchemaCommands.ExecuteTerrain((GenerateTerrainSchemaOptions)options)),
		(typeof(GenerateSchemasOptions), options => SchemaCommands.ExecuteAll((GenerateSchemasOptions)options)),
		(typeof(VerifySchemasOptions), options => SchemaCommands.ExecuteVerify((VerifySchemasOptions)options))
	};

	private static readonly Type[] VerbOptionTypes = Verbs.Select(verb => verb.OptionsType).ToArray();

	private static readonly Dictionary<Type, Func<object, int>> VerbExecutionByOptionsType =
		Verbs.ToDictionary(verb => verb.OptionsType, verb => verb.Execute);

	public static int Main(string[] args)
	{
		var unhandledArgs = new List<string>(args.Length);
		for (int index = 0; index < args.Length; index++)
		{
			string argument = args[index];
			if (string.Equals(argument, "--eula-accept", StringComparison.OrdinalIgnoreCase))
			{
				CliAssetAgreement.Accepted = true;
				continue;
			}

			unhandledArgs.Add(argument);
		}

		string[] originalArgs = unhandledArgs.ToArray();
		string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(originalArgs, VerbOptionTypes);

		return Parser.Default.ParseArguments(sanitizedArgs, VerbOptionTypes)
			.WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
			.MapResult(
				Dispatch,
				errors => 1);
	}

	private static int Dispatch(object options)
	{
		if (VerbExecutionByOptionsType.TryGetValue(options.GetType(), out var execute))
		{
			return execute(options);
		}

		Console.Error.WriteLine($"Error: no handler is registered for the '{options.GetType().Name}' verb.");
		return 1;
	}

}
