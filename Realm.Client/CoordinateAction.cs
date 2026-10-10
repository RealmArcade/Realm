using System.Collections.Generic;
using System.Linq;
using Realm.Client.Core;

namespace Realm.Client;

public class CoordinateAction : IEditorAction
{
	private readonly List<Realm.Client.Core.GameHost.EditorCoordinate> _oldCoordinates;
	private readonly List<Realm.Client.Core.GameHost.EditorCoordinate> _newCoordinates;

	public CoordinateAction(IEnumerable<Realm.Client.Core.GameHost.EditorCoordinate> oldCoordinates, IEnumerable<Realm.Client.Core.GameHost.EditorCoordinate> newCoordinates)
	{
		_oldCoordinates = oldCoordinates.ToList();
		_newCoordinates = newCoordinates.ToList();
	}

	public void Undo()
	{
		if (Realm.Client.Core.GameHost.Instance != null)
		{
			Realm.Client.Core.GameHost.Instance.EditorCoordinates.Clear();
			Realm.Client.Core.GameHost.Instance.EditorCoordinates.AddRange(_oldCoordinates);
			Realm.Client.Core.GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			Realm.Client.Core.GameHost.Instance.HideCoordinateSelectionOutline();
			UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}

	public void Redo()
	{
		if (Realm.Client.Core.GameHost.Instance != null)
		{
			Realm.Client.Core.GameHost.Instance.EditorCoordinates.Clear();
			Realm.Client.Core.GameHost.Instance.EditorCoordinates.AddRange(_newCoordinates);
			Realm.Client.Core.GameHost.Instance.RebuildAllCoordinatePersistentMeshes();
			Realm.Client.Core.GameHost.Instance.HideCoordinateSelectionOutline();
			UI.MapEditorHUD.Instance?.RefreshCoordinateListExternal();
		}
	}
}