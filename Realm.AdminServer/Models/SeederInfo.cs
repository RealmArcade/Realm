namespace Realm.AdminServer.Models;

public class SeederInfo
{
	public required string SeederId { get; set; }
	public required string IP { get; set; }
	public required int Port { get; set; }
	public required List<string> MapIds { get; set; }
	public int CapacityPercentage { get; set; } = 100;
	public bool AcceptingUploads { get; set; } = true;
	public DateTime LastSeen { get; set; } = DateTime.UtcNow;

	/// <summary>
	///     Whether this seeder is only reachable from inside its own network. A seeder that states an endpoint nothing outside
	///     has confirmed is kept - the players on that network can still use it - but it is never offered to a player
	///     elsewhere, because offering an endpoint that does not answer is worse than offering none.
	/// </summary>
	public bool LanOnly { get; set; }

	/// <summary>When this node last reached the seeder's endpoint from outside, or null when it never has.</summary>
	public DateTime? ConfirmedAt { get; set; }
}