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

public static class KeygenCommand
{
	public static int Execute(KeygenOptions options)
	{
		var (privateKeyBase64, publicKeyBase64) = AuthorSignatureHelper.GenerateKeyPair();

		PrintKeygenHeader(publicKeyBase64, privateKeyBase64);

		string keyUsername = !string.IsNullOrWhiteSpace(options.Username) ? options.Username.Trim() : string.Empty;

		if (string.IsNullOrWhiteSpace(options.Output) && keyUsername.Length == 0)
		{
			Console.Error.WriteLine("Error: An authorship key is named after its user, so keygen needs --username (or an explicit --output path).");
			return 1;
		}

		string outputPath = !string.IsNullOrWhiteSpace(options.Output)
			? options.Output.Trim()
			: AuthorshipKeyHelper.GetDefaultAuthorshipKeyPath(null, keyUsername);

		string? outDir = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		byte[] rkeyBytes = RkeyFile.Build(keyUsername, publicKeyBase64, privateKeyBase64);
		File.WriteAllBytes(outputPath, rkeyBytes);
		Console.WriteLine($"Saved key container to: {outputPath}");
		Console.WriteLine("Open the map editor and register the key from the publish dialog to lock this name to your Steam account.");

		Console.WriteLine();
		Console.WriteLine("Back up the .rkey file: it signs every map you publish, and an update signed with a key you lost can no longer be shown as yours.");
		Console.WriteLine($"The public key ({publicKeyBase64}) is carried inside each map you sign, so anyone can check the map came from you. Share it freely; it cannot sign anything.");
		Console.WriteLine("=================================================");
		return 0;
	}

	private static void PrintKeygenHeader(string publicKeyBase64, string privateKeyBase64)
	{
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Cryptographic Ed25519 Key Pair Generated");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Public Key (Base64):  {publicKeyBase64}");
		Console.WriteLine($"Private Key (Base64): {privateKeyBase64}");
		Console.WriteLine();
	}
}
