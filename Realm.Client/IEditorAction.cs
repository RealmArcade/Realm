namespace Realm.Client;

public interface IEditorAction
{
	void Undo();
	void Redo();
}