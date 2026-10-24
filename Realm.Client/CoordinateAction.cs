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
		if (GameContext.Host != null)
		{
			GameContext.EditorService.EditorCoordinates.Clear();
			GameContext.EditorService.EditorCoordinates.AddRange(_oldCoordinates);
			GameContext.Host.RebuildAllCoordinatePersistentMeshes();
			GameContext.Host.HideCoordinateSelectionOutline();
			GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		}
	}

	public void Redo()
	{
		if (GameContext.Host != null)
		{
			GameContext.EditorService.EditorCoordinates.Clear();
			GameContext.EditorService.EditorCoordinates.AddRange(_newCoordinates);
			GameContext.Host.RebuildAllCoordinatePersistentMeshes();
			GameContext.Host.HideCoordinateSelectionOutline();
			GameContext.MapEditorHud?.RefreshCoordinateListExternal();
		}
	}
}