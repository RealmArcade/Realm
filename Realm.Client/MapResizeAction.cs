namespace Realm.Client;

public class MapResizeAction : IEditorAction
{
	private readonly MapStateSnapshot _before;
	private readonly MapStateSnapshot _after;

	public MapResizeAction(MapStateSnapshot before, MapStateSnapshot after)
	{
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		MapStateSnapshot.Restore(_before, updateMapSize: true);
	}

	public void Redo()
	{
		MapStateSnapshot.Restore(_after, updateMapSize: true);
	}
}
