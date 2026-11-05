using CommandLine;

namespace Realm.Tools.Cli;

[Verb("keygen", HelpText = "Generate a cryptographic Ed25519 author key pair for map attribution, asset signing, and admin verification.")]
public class KeygenOptions
{
	[Option('u', "username", Required = false, HelpText = "Display name / username to associate with this key pair.")]
	public string? Username { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output path to write .rkey file. If omitted, saves to default %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE_<username>.rkey")]
	public string? Output { get; set; }
}