using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;

namespace Realm.Client;

public class CoordinateAction : IEditorAction
{
	private readonly List<EditorCoordinate> _oldCoordinates;
	private readonly List<EditorCoordinate> _newCoordinates;

	public CoordinateAction(IEnumerable<EditorCoordinate> oldCoordinates, IEnumerable<EditorCoordinate> newCoordinates)
	{
		_oldCoordinates = oldCoordinates.ToList();
		_newCoordinates = newCoordinates.ToList();
	}

	public void Undo()
	{
		if (GameHost.Instance != null)
		{
			GameContext.EditorService.EditorCoordinates.Clear();
			GameContext.EditorService.EditorCoordinates.AddRange(_oldCoordinates);
			GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			GameHost.Instance.HideCoordinateSelectionOutline();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}

	public void Redo()
	{
		if (GameHost.Instance != null)
		{
			GameContext.EditorService.EditorCoordinates.Clear();
			GameContext.EditorService.EditorCoordinates.AddRange(_newCoordinates);
			GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			GameHost.Instance.HideCoordinateSelectionOutline();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}
}