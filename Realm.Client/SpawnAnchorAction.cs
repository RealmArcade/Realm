using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client;

/// <summary>
///     Undoes and redoes the seat anchors the coordinate tool authored, the way <see cref="CoordinateAction" /> does for
///     the coordinates themselves.
/// </summary>
public class SpawnAnchorAction : IEditorAction
{
	private readonly List<EditorSpawnAnchor> _oldAnchors;
	private readonly List<EditorSpawnAnchor> _newAnchors;

	public SpawnAnchorAction(IEnumerable<EditorSpawnAnchor> oldAnchors, IEnumerable<EditorSpawnAnchor> newAnchors)
	{
		_oldAnchors = oldAnchors.ToList();
		_newAnchors = newAnchors.ToList();
	}

	public void Undo() => Apply(_oldAnchors);

	public void Redo() => Apply(_newAnchors);

	private static void Apply(List<EditorSpawnAnchor> anchors)
	{
		if (GameContext.Host == null) return;

		GameContext.EditorService.EditorSpawnAnchors.Clear();
		GameContext.EditorService.EditorSpawnAnchors.AddRange(anchors);
		GameContext.Host.RebuildAllSpawnAnchorMeshes();
		GameContext.MapEditorHud?.RefreshCoordinateListExternal();
	}
}
