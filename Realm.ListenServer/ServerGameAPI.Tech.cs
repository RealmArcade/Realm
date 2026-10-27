using System.Numerics;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     How far each player is up each of a map's tech tracks. A level is granted on the authority and changes the units
///     its owner already has, so what a script asks for here is what the match plays at; the levels also travel, because
///     the screens that offer the next level have to show the one a player is on.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>How far a player is up one track.</summary>
	public int GetPlayerTechLevel(int playerIndex, string techId) => TechLevels.Get(playerIndex, techId);

	/// <summary>Sets how far a player is up one track, granting what that level gives their units.</summary>
	public void SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;

		TechLevels.Set(playerIndex, techId, level);
		PublishTechLevels();
	}

	/// <summary>Raises or lowers how far a player is up one track.</summary>
	public void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1)
	{
		if (string.IsNullOrEmpty(techId)) return;

		TechLevels.Add(playerIndex, techId, delta);
		PublishTechLevels();
	}

	/// <summary>
	///     Tells every screen what each player is now up to on each track the match has raised. A level is not state a
	///     client can work out for itself, and it decides which upgrade a player is offered next, so the level a script
	///     set is said out loud rather than implied.
	/// </summary>
	private void PublishTechLevels()
	{
		IReadOnlyList<(int PlayerIndex, string TechId, int Level)> changes = TechLevels.ConsumeChanges();
		for (int index = 0; index < changes.Count; index++)
		{
			(int playerIndex, string techId, int level) = changes[index];
			_presentation.Write(PresentationKind.TechLevelSet, playerIndex, 0, techId, string.Empty, string.Empty, string.Empty, null,
				Vector3.Zero, Vector3.Zero, Vector3.Zero, playerIndex, 0, level, 0f, 0f, false);
		}
	}
}
