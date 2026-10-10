using Godot;

namespace Realm.Client.UI.MapEditor;

public class MapEditorInspector
{
	private Label _lblInspectorTitle;
	private Label _lblInspectorPos;
	private Button _btnInspectorRotLeft;
	private Button _btnInspectorRotRight;
	private Button _btnInspectorScaleDown;
	private Button _btnInspectorScaleUp;
	private Button _btnInspectorScaleReset;
	private Button _btnInspectorDelete;

	public MapEditorInspector(Label lblInspectorTitle, Label lblInspectorPos,
		Button btnInspectorRotLeft, Button btnInspectorRotRight,
		Button btnInspectorScaleDown, Button btnInspectorScaleUp,
		Button btnInspectorScaleReset, Button btnInspectorDelete)
	{
		_lblInspectorTitle = lblInspectorTitle;
		_lblInspectorPos = lblInspectorPos;
		_btnInspectorRotLeft = btnInspectorRotLeft;
		_btnInspectorRotRight = btnInspectorRotRight;
		_btnInspectorScaleDown = btnInspectorScaleDown;
		_btnInspectorScaleUp = btnInspectorScaleUp;
		_btnInspectorScaleReset = btnInspectorScaleReset;
		_btnInspectorDelete = btnInspectorDelete;
	}

	private string _lastInspectorTitle = null;
	private string _lastInspectorPos = null;

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;
		if (_lblInspectorTitle != null && viewModel.InspectorTitle != _lastInspectorTitle)
		{
			_lastInspectorTitle = viewModel.InspectorTitle;
			_lblInspectorTitle.Text = TranslationServer.Translate(viewModel.InspectorTitle);
		}
		if (_lblInspectorPos != null && viewModel.InspectorPos != _lastInspectorPos)
		{
			_lastInspectorPos = viewModel.InspectorPos;
			_lblInspectorPos.Text = TranslationServer.Translate(viewModel.InspectorPos);
		}
	}
}