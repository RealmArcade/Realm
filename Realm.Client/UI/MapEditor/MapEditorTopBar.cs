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

		_btnBackToHub.TooltipText = TranslationServer.Translate("Return to Main Menu / Hub");
		_btnPublish.TooltipText = TranslationServer.Translate("Publish/export map to custom map registry");
		_btnSave.TooltipText = TranslationServer.Translate("Save current heightmap, textures, and entities (Ctrl+S)");
		_btnLoad.TooltipText = TranslationServer.Translate("Load heightmap, textures, and entities from a saved json file (Ctrl+O)");
		_btnUndo.TooltipText = TranslationServer.Translate("Undo the last action (Ctrl+Z)");
		_btnRedo.TooltipText = TranslationServer.Translate("Redo the last undone action (Ctrl+Y)");

		if (_btnVSCode != null)
		{
			_btnVSCode.TooltipText = TranslationServer.Translate("Toggle Embedded VSCode JSON / Script editor");
		}
	}

	private string _lastStatusText = null;
	private readonly ThrottledLabelWidth _statusLabelWidth = new();

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (_statusLabel != null && viewModel != null && viewModel.StatusText != _lastStatusText)
		{
			_lastStatusText = viewModel.StatusText;
			_statusLabelWidth.Apply(_statusLabel, viewModel.StatusText);
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