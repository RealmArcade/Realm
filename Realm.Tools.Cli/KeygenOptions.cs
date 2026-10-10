using CommandLine;

namespace Realm.Tools.Cli;

[Verb("keygen", HelpText = "Generate a cryptographic Ed25519 author key pair for map attribution, asset signing, and admin verification.")]
public class KeygenOptions
{
	[Option('u', "username", Required = false, HelpText = "Display name / username to associate with this key pair.")]
	public string? Username { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output path to write .rkey file. If omitted, saves to default %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Output { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL to register unique username.")]
	public string? Server { get; set; }

	[Option("register", Required = false, Default = false, HelpText = "Register the generated key pair and username with the official registry server.")]
	public bool Register { get; set; }
}