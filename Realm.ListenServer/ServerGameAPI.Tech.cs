using System.Numerics;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public int GetPlayerTechLevel(int playerIndex, string techId) => TechLevels.Get(playerIndex, techId);

	public void SetPlayerTechLevel(int playerIndex, string techId, int level)
	{
		if (string.IsNullOrEmpty(techId)) return;

		TechLevels.Set(playerIndex, techId, level);
		PublishTechLevels();
	}

	public void AddPlayerTechLevel(int playerIndex, string techId, int delta = 1)
	{
		if (string.IsNullOrEmpty(techId)) return;

		TechLevels.Add(playerIndex, techId, delta);
		PublishTechLevels();
	}

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
