using Godot;

namespace Realm.Client.UI.MapEditor;

public class MapEditorTopBar
{
	private Button _btnBackToHub;
	private Button _btnPublish;
	private Button _btnSave;
	private Button _btnLoad;
	private Button _btnUndo;
	private Button _btnRedo;
	private Button _btnVSCode;
	private Label _statusLabel;
	private Label _feedbackLabel;

	public MapEditorTopBar(Button btnBackToHub, Button btnPublish, Button btnSave, Button btnLoad,
		Button btnUndo, Button btnRedo, Button btnVSCode, Label statusLabel, Label feedbackLabel)
	{
		_btnBackToHub = btnBackToHub;
		_btnPublish = btnPublish;
		_btnSave = btnSave;
		_btnLoad = btnLoad;
		_btnUndo = btnUndo;
		_btnRedo = btnRedo;
		_btnVSCode = btnVSCode;
		_statusLabel = statusLabel;
		_feedbackLabel = feedbackLabel;

		_btnBackToHub.TooltipText = "Return to Main Menu / Hub";
		_btnPublish.TooltipText = "Publish/export map to custom map registry";
		_btnSave.TooltipText = "Save current heightmap, textures, and entities (Ctrl+S)";
		_btnLoad.TooltipText = "Load heightmap, textures, and entities from a saved json file (Ctrl+O)";
		_btnUndo.TooltipText = "Undo the last action (Ctrl+Z)";
		_btnRedo.TooltipText = "Redo the last undone action (Ctrl+Y)";

		if (_btnVSCode != null)
		{
			_btnVSCode.TooltipText = "Toggle Embedded VSCode JSON / Script editor";
		}
	}

	private string _lastStatusText = null;

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (_statusLabel != null && viewModel != null && viewModel.StatusText != _lastStatusText)
		{
			_lastStatusText = viewModel.StatusText;
			_statusLabel.Text = viewModel.StatusText;
		}
	}

	public void ShowFeedback(string text)
	{
		if (_feedbackLabel == null) return;
		_feedbackLabel.Text = TranslationServer.Translate(text);
		_feedbackLabel.Modulate = new Color(1, 1, 1, 1);
		
		var tween = _feedbackLabel.CreateTween();
		tween.TweenProperty(_feedbackLabel, "modulate:a", 0.0f, 2.0f).SetDelay(1.5f);
	}
}