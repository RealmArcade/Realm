using CommandLine;
using Realm.Shared;

namespace Realm.Tools.Cli;

public static class Program
{
	private static readonly Type[] VerbOptions =
	{
		typeof(MeshConvertOptions),
		typeof(MeshRenderOptions),
		typeof(ImageSheetOptions),
		typeof(RmeshExtractSourceImageOptions),
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
		typeof(VerifySchemasOptions)
	};

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
		string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(originalArgs, VerbOptions);

		return Parser.Default.ParseArguments(sanitizedArgs, VerbOptions)
			.WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
			.MapResult(
				Dispatch,
				errors => 1);
	}

	private static int Dispatch(object options)
	{
		return options switch
		{
			MeshConvertOptions value => MeshConvertCommands.Execute(value),
			MeshRenderOptions value => MeshRenderCommand.Execute(value),
			ImageSheetOptions value => ImageSheetCommand.Execute(value),
			RmeshExtractSourceImageOptions value => RmeshExtractSourceImageCommand.Execute(value),
			TextureConvertOptions value => TextureConvertCommand.Execute(value),
			AudioConvertOptions value => AudioConvertCommand.Execute(value),
			FbxToRanimOptions value => RanimCommands.ExecuteFromFbx(value),
			RanimRenderOptions value => RanimCommands.ExecuteRender(value),
			MetadataOptions value => MetadataCommands.Execute(value),
			Blake3Options value => Blake3Command.Execute(value),
			MeshPlayerColorCliOptions value => MeshPlayerColorCommand.Execute(value),
			RigHumanoidOptions value => RigCommand.Execute(value),
			KeygenOptions value => KeygenCommand.Execute(value),
			GenerateManifestSchemaOptions value => SchemaCommands.ExecuteManifest(value),
			GenerateMetadataSchemaOptions value => SchemaCommands.ExecuteMetadata(value),
			GenerateTerrainSchemaOptions value => SchemaCommands.ExecuteTerrain(value),
			GenerateSchemasOptions value => SchemaCommands.ExecuteAll(value),
			VerifySchemasOptions value => SchemaCommands.ExecuteVerify(value),
			_ => 1
		};
	}

}
