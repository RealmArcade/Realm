using CommandLine;
using Realm.Shared;

namespace Realm.Tools.Cli;

public static class Program
{
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
		string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(
			originalArgs,
			typeof(MeshConvertOptions),
			typeof(TextureConvertOptions),
			typeof(AudioConvertOptions),
			typeof(FbxToRanimOptions),
			typeof(RanimRenderOptions),
			typeof(MetadataOptions),
			typeof(Blake3Options),
			typeof(MeshPlayerColorCliOptions),
			typeof(RigHumanoidOptions),
			typeof(KeygenOptions),
			typeof(GenerateManifestSchemaOptions),
			typeof(GenerateMetadataSchemaOptions),
			typeof(GenerateTerrainSchemaOptions),
			typeof(GenerateSchemasOptions),
			typeof(VerifySchemasOptions));

		return Parser.Default.ParseArguments<MeshConvertOptions, TextureConvertOptions, AudioConvertOptions, FbxToRanimOptions, RanimRenderOptions, MetadataOptions, Blake3Options, MeshPlayerColorCliOptions, RigHumanoidOptions, KeygenOptions, GenerateManifestSchemaOptions, GenerateMetadataSchemaOptions, GenerateTerrainSchemaOptions, GenerateSchemasOptions, VerifySchemasOptions>(sanitizedArgs)
			.WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
			.MapResult(
				(MeshConvertOptions options) => MeshConvertCommands.Execute(options),
				(TextureConvertOptions options) => TextureConvertCommand.Execute(options),
				(AudioConvertOptions options) => AudioConvertCommand.Execute(options),
				(FbxToRanimOptions options) => RanimCommands.ExecuteFromFbx(options),
				(RanimRenderOptions options) => RanimCommands.ExecuteRender(options),
				(MetadataOptions options) => MetadataCommands.Execute(options),
				(Blake3Options options) => Blake3Command.Execute(options),
				(MeshPlayerColorCliOptions options) => MeshPlayerColorCommand.Execute(options),
				(RigHumanoidOptions options) => RigCommand.Execute(options),
				(KeygenOptions options) => KeygenCommand.Execute(options),
				(GenerateManifestSchemaOptions options) => SchemaCommands.ExecuteManifest(options),
				(GenerateMetadataSchemaOptions options) => SchemaCommands.ExecuteMetadata(options),
				(GenerateTerrainSchemaOptions options) => SchemaCommands.ExecuteTerrain(options),
				(GenerateSchemasOptions options) => SchemaCommands.ExecuteAll(options),
				(VerifySchemasOptions options) => SchemaCommands.ExecuteVerify(options),
				errors => 1);
	}

}