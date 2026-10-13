using Godot;

namespace Realm.Client.UI.MapEditor;

public class MapEditorPathingPanel
{
	private CheckBox _chkShallowWater;
	private CheckBox _chkDeepWater;
	private CheckBox _chkFlying;
	private CheckBox _chkGround;
	private CheckBox _chkBuildable;
	private OptionButton _optPathingMode;

	public MapEditorPathingPanel(CheckBox chkShallowWater, CheckBox chkDeepWater, CheckBox chkFlying,
		CheckBox chkGround, CheckBox chkBuildable, OptionButton optPathingMode)
	{
		_chkShallowWater = chkShallowWater;
		_chkDeepWater = chkDeepWater;
		_chkFlying = chkFlying;
		_chkGround = chkGround;
		_chkBuildable = chkBuildable;
		_optPathingMode = optPathingMode;

		if (_chkShallowWater != null)
			_chkShallowWater.Toggled += (val) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.ShallowWater = val;
			};
		if (_chkDeepWater != null)
			_chkDeepWater.Toggled += (val) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.DeepWater = val;
			};
		if (_chkFlying != null)
			_chkFlying.Toggled += (val) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.Flying = val;
			};
		if (_chkGround != null)
			_chkGround.Toggled += (val) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.Ground = val;
			};
		if (_chkBuildable != null)
			_chkBuildable.Toggled += (val) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.Buildable = val;
			};
		if (_optPathingMode != null)
			_optPathingMode.ItemSelected += (idx) =>
			{
				MapEditorHUD tempQualifier = MapEditorHUD.Instance;
				if ((tempQualifier != null ? tempQualifier._viewModel : null) != null) MapEditorHUD.Instance._viewModel.PathingModeIndex = (int)idx;
			};
	}

	public void Update(MapEditorHUDViewModel viewModel)
	{
		if (viewModel == null) return;
		UpdateCheckBox(_chkShallowWater, viewModel.ShallowWater);
		UpdateCheckBox(_chkDeepWater, viewModel.DeepWater);
		UpdateCheckBox(_chkFlying, viewModel.Flying);
		UpdateCheckBox(_chkGround, viewModel.Ground);
		UpdateCheckBox(_chkBuildable, viewModel.Buildable);
		UpdatePathingMode(viewModel.PathingModeIndex);
	}

	private void UpdateCheckBox(CheckBox checkBox, bool value)
	{
		if (checkBox == null || checkBox.ButtonPressed == value) return;
		checkBox.ButtonPressed = value;
	}

	private void UpdatePathingMode(int modeIndex)
	{
		if (_optPathingMode == null || _optPathingMode.Selected == modeIndex || modeIndex >= _optPathingMode.ItemCount) return;
		_optPathingMode.Selected = modeIndex;
	}
}