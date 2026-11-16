using CommandLine;

namespace Realm.Tools.Cli;

/// <summary>
///     One diagnostic join, aimed at a lobby a registry node knows or at an address somebody was given.
/// </summary>
public class JoinLobbyOptions
{
	[Option("lobby", HelpText = "The identifier of a lobby a registry node knows. Requires --registry.")]
	public string LobbyId { get; set; } = string.Empty;

	[Option("address", HelpText = "An address to join directly, as host:port or [ipv6]:port. Skips the registry.")]
	public string Address { get; set; } = string.Empty;

	[Option("registry", HelpText = "The lobby server to ask where the lobby is. Its join answer also states where this machine reports itself.")]
	public string Registry { get; set; } = string.Empty;

	[Option("timeout", Default = 15, HelpText = "How long the authority is given to answer a connection attempt, in seconds.")]
	public int TimeoutSeconds { get; set; } = 15;

	[Option("json", HelpText = "Write one machine-readable object instead of a human report.")]
	public bool Json { get; set; }

	[Option("expect", HelpText = "Assert a failure, as phase:outcome (for example answer:silent). Exits zero when the run fails exactly that way.")]
	public string Expect { get; set; } = string.Empty;
}
