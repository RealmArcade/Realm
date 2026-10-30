using System.Collections.Generic;
using Realm.Client.Core;
using Realm.Client.UI;
using Realm.Client.Services;

namespace Realm.Client;

public static class EditorHistoryManager
{
	public static readonly Stack<IEditorAction> _undoStack = new Stack<IEditorAction>();
	public static readonly Stack<IEditorAction> _redoStack = new Stack<IEditorAction>();

	public static void RecordAction(IEditorAction action)
	{
		_undoStack.Push(action);
		_redoStack.Clear();
		GameContext.MapEditorHud?.RegenerateMinimap();
	}

	public static void Undo()
	{
		if (_undoStack.Count > 0)
		{
			var action = _undoStack.Pop();
			action.Undo();
			_redoStack.Push(action);
			GameContext.MapEditorHud?.RegenerateMinimap();
			GameContext.Host?.RefreshSelectionHighlight();
		}
	}

	public static void Redo()
	{
		if (_redoStack.Count > 0)
		{
			var action = _redoStack.Pop();
			action.Redo();
			_undoStack.Push(action);
			GameContext.MapEditorHud?.RegenerateMinimap();
			GameContext.Host?.RefreshSelectionHighlight();
		}
	}

	public static void Clear()
	{
		_undoStack.Clear();
		_redoStack.Clear();
	}
}