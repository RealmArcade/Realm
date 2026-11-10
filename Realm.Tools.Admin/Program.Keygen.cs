using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Tools.Admin;


public static partial class Program
{
	private static int ExecuteKeygen(AdminKeygenOptions options)
	{
		if (!PrivateKeyEncryptionHelper.TryValidatePassword(options.Password, out string passwordError))
		{
			Console.Error.WriteLine($"Error: {passwordError}");
			return 1;
		}

		string outputDirectory = string.IsNullOrWhiteSpace(options.Output)
			? Path.GetDirectoryName(AuthorshipKeyHelper.GetDefaultAdminKeyPath()) ?? AppContext.BaseDirectory
			: Path.GetFullPath(options.Output.Trim());
		Directory.CreateDirectory(outputDirectory);
		string outputPath = Path.Combine(outputDirectory, AuthorshipKeyHelper.DefaultAdminKeyFileName);

		if (File.Exists(outputPath))
		{
			Console.Error.WriteLine($"Error: '{outputPath}' already exists. Move or remove the existing admin key before generating a new one.");
			return 1;
		}

		var (privateKeyBase64, publicKeyBase64) = AuthorSignatureHelper.GenerateKeyPair();
		AuthorshipKeyEncryption encryptedPrivateKey = PrivateKeyEncryptionHelper.EncryptPrivateKey(privateKeyBase64, options.Password);

		var keyData = new AuthorshipKeyData
		{
			UserName = options.Username?.Trim() ?? string.Empty,
			PublicKey = publicKeyBase64,
			PrivateKey = string.Empty,
			EncryptedPrivateKey = encryptedPrivateKey
		};

		File.WriteAllBytes(outputPath, RkeyFile.Build(keyData));

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Encrypted Admin Key Generated");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Public Key (Base64): {publicKeyBase64}");
		Console.WriteLine($"Encrypted Key File:  {outputPath}");
		Console.WriteLine();
		Console.WriteLine("Add the Public Key to Realm.AdminServer/appsettings.json under 'AdminPublicKeys':");
		Console.WriteLine($"   \"AdminPublicKeys\": [\n     \"{publicKeyBase64}\"\n   ]");
		Console.WriteLine("Keep the password and the .rkey file separately; the password is required for every admin action.");
		Console.WriteLine("=================================================");
		return 0;
	}
}
