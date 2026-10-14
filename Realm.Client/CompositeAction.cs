using System.Collections.Generic;

namespace Realm.Client;

public class CompositeAction : IEditorAction
{
	private readonly List<IEditorAction> _actions = new List<IEditorAction>();

	public CompositeAction(IEnumerable<IEditorAction> actions)
	{
		_actions.AddRange(actions);
	}

	public void Undo()
	{
		for (int i = _actions.Count - 1; i >= 0; i--)
		{
			_actions[i].Undo();
		}
	}

	public void Redo()
	{
		for (int i = 0; i < _actions.Count; i++)
		{
			_actions[i].Redo();
		}
	}
}