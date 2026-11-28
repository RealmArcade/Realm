namespace Realm.Client;

public class ObjectTemplateSwapAction : IEditorAction
{
	private readonly MapStateSnapshot _before;
	private readonly MapStateSnapshot _after;

	public ObjectTemplateSwapAction(MapStateSnapshot before, MapStateSnapshot after)
	{
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		MapStateSnapshot.Restore(_before, updateMapSize: false);
	}

	public void Redo()
	{
		MapStateSnapshot.Restore(_after, updateMapSize: false);
	}
}
